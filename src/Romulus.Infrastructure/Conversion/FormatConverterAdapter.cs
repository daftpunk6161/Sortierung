using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Conversion;

namespace Romulus.Infrastructure.Conversion;

/// <summary>
/// Format conversion orchestrator. Port of Convert.ps1.
/// Maps console types to target formats and dispatches to tool-specific converters.
/// </summary>
public sealed class FormatConverterAdapter : IFormatConverter
{
    private readonly IToolRunner _tools;
    private readonly IReadOnlyDictionary<string, ConversionTarget> _bestFormats;
    private readonly IConversionRegistry? _registry;
    private readonly IConversionPlanner? _planner;
    private readonly IConversionExecutor? _executor;
    private readonly bool _allowReviewRequiredPlans;

    private readonly ChdmanToolConverter _chdman;
    private readonly DolphinToolConverter _dolphin;
    private readonly SevenZipToolConverter _sevenZip;
    private readonly PsxtractToolConverter _psxtract;

    // Systems that must never be auto-converted because format conversion would
    // violate set integrity, require proprietary keys, or has no safe target path.
    private static readonly IReadOnlySet<string> BlockedAutoSystems =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ARCADE", "NEOGEO", "SWITCH", "PS3", "3DS", "VITA", "DOS"
        };

    // Systems that may have technical conversion paths but require explicit user review.
    // The current IFormatConverter contract has no confirmation channel, therefore these
    // are blocked from auto-selection here.
    private static readonly IReadOnlySet<string> ManualOnlySystems =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "XBOX", "X360", "WIIU", "PC98", "X68K"
        };

    /// <summary>Forwarded ZIP entry-count safety limit used by archive-to-CHD conversion.</summary>
    internal const int MaxZipEntryCount = ChdmanToolConverter.MaxZipEntryCount;

    /// <summary>Forwarded ZIP extraction size limit used by archive-to-CHD conversion.</summary>
    internal const long MaxExtractedTotalBytes = ChdmanToolConverter.MaxExtractedTotalBytes;

    /// <summary>SEC-CONV-04: Maximum allowed compression ratio per entry (zip bomb protection). Forwarded from ChdmanToolConverter.</summary>
    internal static readonly double MaxCompressionRatio = ChdmanToolConverter.MaxCompressionRatio;

    /// <summary>
    /// Default best target format per console type.
    /// Port of $script:BEST_FORMAT from Convert.ps1.
    /// Injected via constructor to allow configuration override (ADR-0007 §3.3).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, ConversionTarget> DefaultBestFormats =
        new Dictionary<string, ConversionTarget>(StringComparer.OrdinalIgnoreCase)
    {
        // CD-based → CHD (chdman createcd)
        ["PS1"]   = new(".chd", "chdman", "createcd"),
        ["SAT"]   = new(".chd", "chdman", "createcd"),
        ["DC"]    = new(".chd", "chdman", "createcd"),
        ["SCD"]   = new(".chd", "chdman", "createcd"),
        ["PCECD"] = new(".chd", "chdman", "createcd"),
        ["NEOCD"] = new(".chd", "chdman", "createcd"),
        ["3DO"]   = new(".chd", "chdman", "createcd"),
        ["JAGCD"] = new(".chd", "chdman", "createcd"),
        // DVD-based → CHD (chdman createdvd)
        ["PS2"] = new(".chd", "chdman", "createdvd"),
        // PSP → CHD (UMD)
        ["PSP"] = new(".chd", "chdman", "createcd"),
        // GameCube/Wii → RVZ (DolphinTool)
        ["GC"]  = new(".rvz", "dolphintool", "convert"),
        ["WII"] = new(".rvz", "dolphintool", "convert"),
        // Cartridge → ZIP (7z)
        ["NES"]    = new(".zip", "7z", "zip"),
        ["SNES"]   = new(".zip", "7z", "zip"),
        ["N64"]    = new(".zip", "7z", "zip"),
        ["GB"]     = new(".zip", "7z", "zip"),
        ["GBC"]    = new(".zip", "7z", "zip"),
        ["GBA"]    = new(".zip", "7z", "zip"),
        ["NDS"]    = new(".zip", "7z", "zip"),
        ["MD"]     = new(".zip", "7z", "zip"),
        ["SMS"]    = new(".zip", "7z", "zip"),
        ["GG"]     = new(".zip", "7z", "zip"),
        ["PCE"]    = new(".zip", "7z", "zip"),
    };

    private static readonly ConversionTarget PbpTarget = new(".chd", "psxtract", "pbp2chd");

    /// <summary>
    /// Creates a FormatConverterAdapter with default format mappings.
    /// </summary>
    public FormatConverterAdapter(IToolRunner tools)
        : this(tools, null, null, null, null, false) { }

    /// <summary>
    /// Creates a FormatConverterAdapter with optional custom format mappings.
    /// Falls back to <see cref="DefaultBestFormats"/> when <paramref name="bestFormats"/> is null.
    /// </summary>
    public FormatConverterAdapter(IToolRunner tools, IReadOnlyDictionary<string, ConversionTarget>? bestFormats)
        : this(tools, bestFormats, null, null, null, false)
    {
    }

    /// <summary>
    /// Creates a FormatConverterAdapter with optional registry/executor-backed conversion flow.
    /// </summary>
    public FormatConverterAdapter(
        IToolRunner tools,
        IReadOnlyDictionary<string, ConversionTarget>? bestFormats,
        IConversionRegistry? registry,
        IConversionPlanner? planner,
        IConversionExecutor? executor,
        bool allowReviewRequiredPlans = false)
    {
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _bestFormats = bestFormats ?? DefaultBestFormats;
        _registry = registry;
        _planner = planner;
        _executor = executor;
        _allowReviewRequiredPlans = allowReviewRequiredPlans;
        _chdman = new ChdmanToolConverter(tools);
        _dolphin = new DolphinToolConverter(tools);
        _sevenZip = new SevenZipToolConverter(tools);
        _psxtract = new PsxtractToolConverter(tools);
    }

    public FormatConverterAdapter(
        IToolRunner tools,
        IReadOnlyDictionary<string, ConversionTarget>? bestFormats,
        IConversionRegistry? registry,
        IConversionExecutor? executor,
        bool allowReviewRequiredPlans = false)
        : this(tools, bestFormats, registry, null, executor, allowReviewRequiredPlans)
    {
    }

    public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension)
    {
        var ext = sourceExtension?.ToLowerInvariant() ?? "";

        if (string.IsNullOrWhiteSpace(consoleKey))
            return null;

        var normalizedConsole = consoleKey.Trim();

        var registryTarget = TryGetRegistryTarget(normalizedConsole, ext);
        if (registryTarget is not null)
            return registryTarget;

        if (BlockedAutoSystems.Contains(normalizedConsole) || ManualOnlySystems.Contains(normalizedConsole))
            return null;

        if (ext == ".pbp")
            return PbpTarget;

        if (!_bestFormats.TryGetValue(normalizedConsole, out var target))
            return null;

        return IsSupportedSourceForTarget(ext, target) ? target : null;
    }

    public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath))
            return new ConversionResult(sourcePath, null, ConversionOutcome.Error, "source-not-found");

        cancellationToken.ThrowIfCancellationRequested();

        var toolPath = _tools.FindTool(target.ToolName);
        if (toolPath is null)
            return new ConversionResult(sourcePath, null, ConversionOutcome.Skipped, $"tool-not-found:{target.ToolName}");

        var sourceExt = SourcePathFormatDetector.ResolveSourceExtension(sourcePath);
        var dir = Path.GetDirectoryName(sourcePath)!;
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        var targetPath = Path.Combine(dir, baseName + target.Extension);

        if (IsBlockedLegacyLossyPair(sourcePath, sourceExt, target.Extension))
            return new ConversionResult(sourcePath, null, ConversionOutcome.Blocked, "lossy-to-lossy-blocked-legacy");

        // Skip if already in target format
        if (string.Equals(sourceExt, target.Extension, StringComparison.OrdinalIgnoreCase))
            return new ConversionResult(sourcePath, null, ConversionOutcome.Skipped, "already-target-format");

        // Don't overwrite existing target
        if (File.Exists(targetPath))
            return new ConversionResult(sourcePath, null, ConversionOutcome.Skipped, "target-exists");

        if (_executor is not null)
        {
            var planned = TryExecuteSingleStepPlan(sourcePath, sourceExt, target, cancellationToken);
            if (planned is not null)
                return planned;
        }

        return target.ToolName.ToLowerInvariant() switch
        {
            "chdman" => _chdman.Convert(sourcePath, targetPath, toolPath, target.Command),
            "dolphintool" => _dolphin.Convert(sourcePath, targetPath, toolPath, sourceExt),
            "7z" => _sevenZip.Convert(sourcePath, targetPath, toolPath),
            "psxtract" => _psxtract.Convert(sourcePath, targetPath, toolPath, target.Command),
            _ => new ConversionResult(sourcePath, null, ConversionOutcome.Error, $"unknown-tool:{target.ToolName}")
        };
    }

    /// <summary>
    /// Planner-backed conversion path that uses source path + console key to compute and execute a full plan.
    /// Falls back to legacy conversion flow if planner/executor are not available.
    /// </summary>
    public ConversionResult ConvertForConsole(string sourcePath, string consoleKey, CancellationToken cancellationToken = default)
        => ConvertForConsole(sourcePath, consoleKey, onProgress: null, cancellationToken);

    public ConversionResult ConvertForConsole(
        string sourcePath,
        string consoleKey,
        Action<string>? onProgress,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath))
            return new ConversionResult(sourcePath, null, ConversionOutcome.Error, "source-not-found");

        var sourceExt = SourcePathFormatDetector.ResolveSourceExtension(sourcePath);

        if (_planner is null || _executor is null)
        {
            var target = GetTargetFormat(consoleKey, sourceExt);
            if (target is null)
                return new ConversionResult(sourcePath, null, ConversionOutcome.Skipped, "no-target-defined");

            EmitConvertStartProgress(onProgress, sourcePath, target.Extension);
            return Convert(sourcePath, target, cancellationToken);
        }

        var plan = _planner.Plan(sourcePath, consoleKey, sourceExt);
        if (plan.RequiresReview && !_allowReviewRequiredPlans)
        {
            return new ConversionResult(sourcePath, null, ConversionOutcome.Blocked, "review-required")
            {
                Plan = plan,
                SourceIntegrity = plan.SourceIntegrity,
                Safety = plan.Safety,
                VerificationResult = VerificationStatus.NotAttempted,
                DurationMs = 0
            };
        }

        if (!plan.IsExecutable)
        {
            // Preserve legacy archive extraction behavior for disc systems when graph data has no archive edge.
            if (string.Equals(plan.SkipReason, "no-conversion-path", StringComparison.OrdinalIgnoreCase)
                && IsArchiveContainerSource(sourceExt))
            {
                var fallbackTarget = GetTargetFormat(consoleKey, sourceExt);
                if (fallbackTarget is not null)
                {
                    EmitConvertStartProgress(onProgress, sourcePath, fallbackTarget.Extension);
                    return ConvertLegacy(sourcePath, fallbackTarget, cancellationToken);
                }
            }

            var outcome = plan.Safety == ConversionSafety.Blocked
                ? ConversionOutcome.Blocked
                : ConversionOutcome.Skipped;
            return new ConversionResult(sourcePath, null, outcome, plan.SkipReason)
            {
                Plan = plan,
                SourceIntegrity = plan.SourceIntegrity,
                Safety = plan.Safety,
                VerificationResult = VerificationStatus.NotAttempted,
                DurationMs = 0
            };
        }

        EmitConvertStartProgress(onProgress, sourcePath, plan.FinalTargetExtension);

        return _executor.Execute(
            plan,
            onStepComplete: CreateStepProgressEmitter(sourcePath, plan, onProgress),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Returns the planner-generated conversion plan for preview/review UI flows.
    /// Returns null when no planner is configured or source file does not exist.
    /// </summary>
    public ConversionPlan? PlanForConsole(string sourcePath, string consoleKey)
    {
        if (_planner is null || !File.Exists(sourcePath))
            return null;

        var sourceExt = SourcePathFormatDetector.ResolveSourceExtension(sourcePath);
        return _planner.Plan(sourcePath, consoleKey, sourceExt);
    }

    private static bool IsArchiveContainerSource(string extension)
    {
        return string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".7z", StringComparison.OrdinalIgnoreCase);
    }

    private static void EmitConvertStartProgress(Action<string>? onProgress, string sourcePath, string? targetExtension)
    {
        if (onProgress is null || string.IsNullOrWhiteSpace(targetExtension))
            return;

        onProgress($"[Convert] {Path.GetFileName(sourcePath)} -> {targetExtension}");
    }

    private static Action<ConversionStep, ConversionStepResult>? CreateStepProgressEmitter(
        string sourcePath,
        ConversionPlan plan,
        Action<string>? onProgress)
    {
        if (onProgress is null || plan.Steps.Count <= 1)
            return null;

        var fileName = Path.GetFileName(sourcePath);
        var totalSteps = plan.Steps.Count;

        return (step, result) =>
        {
            if (!result.Success)
                return;

            onProgress($"[Convert] {fileName} Schritt {step.Order + 1} von {totalSteps} abgeschlossen");
        };
    }

    private ConversionResult ConvertLegacy(string sourcePath, ConversionTarget target, CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath))
            return new ConversionResult(sourcePath, null, ConversionOutcome.Error, "source-not-found");

        cancellationToken.ThrowIfCancellationRequested();

        var toolPath = _tools.FindTool(target.ToolName);
        if (toolPath is null)
            return new ConversionResult(sourcePath, null, ConversionOutcome.Skipped, $"tool-not-found:{target.ToolName}");

        var sourceExt = SourcePathFormatDetector.ResolveSourceExtension(sourcePath);
        var dir = Path.GetDirectoryName(sourcePath)!;
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        var targetPath = Path.Combine(dir, baseName + target.Extension);

        if (IsBlockedLegacyLossyPair(sourcePath, sourceExt, target.Extension))
            return new ConversionResult(sourcePath, null, ConversionOutcome.Blocked, "lossy-to-lossy-blocked-legacy");

        if (string.Equals(sourceExt, target.Extension, StringComparison.OrdinalIgnoreCase))
            return new ConversionResult(sourcePath, null, ConversionOutcome.Skipped, "already-target-format");

        if (File.Exists(targetPath))
            return new ConversionResult(sourcePath, null, ConversionOutcome.Skipped, "target-exists");

        return target.ToolName.ToLowerInvariant() switch
        {
            "chdman" => _chdman.Convert(sourcePath, targetPath, toolPath, target.Command),
            "dolphintool" => _dolphin.Convert(sourcePath, targetPath, toolPath, sourceExt),
            "7z" => _sevenZip.Convert(sourcePath, targetPath, toolPath),
            "psxtract" => _psxtract.Convert(sourcePath, targetPath, toolPath, target.Command),
            _ => new ConversionResult(sourcePath, null, ConversionOutcome.Error, $"unknown-tool:{target.ToolName}")
        };
    }

    private ConversionTarget? TryGetRegistryTarget(string consoleKey, string sourceExtension)
    {
        if (_registry is null)
            return null;

        var policy = _registry.GetPolicy(consoleKey);
        if (policy is ConversionPolicy.None or ConversionPolicy.ManualOnly)
            return null;

        IEnumerable<string> targetCandidates = [];
        var preferred = _registry.GetPreferredTarget(consoleKey);
        if (!string.IsNullOrWhiteSpace(preferred))
            targetCandidates = new[] { preferred! }.Concat(_registry.GetAlternativeTargets(consoleKey));

        foreach (var targetExtension in targetCandidates)
        {
            var edge = _registry.GetCapabilities()
                .Where(c => string.Equals(c.TargetExtension, targetExtension, StringComparison.OrdinalIgnoreCase))
                .Where(c => c.Condition == ConversionCondition.None)
                .Where(c => string.Equals(c.SourceExtension, sourceExtension, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(c.SourceExtension, "*", StringComparison.OrdinalIgnoreCase))
                .Where(c => c.ApplicableConsoles is null || c.ApplicableConsoles.Count == 0 || c.ApplicableConsoles.Contains(consoleKey))
                .OrderBy(c => c.Cost)
                .FirstOrDefault();

            if (edge is null)
                continue;

            return new ConversionTarget(edge.TargetExtension, edge.Tool.ToolName, edge.Command);
        }

        return null;
    }

    private static bool IsBlockedLegacyLossyPair(string sourcePath, string sourceExtension, string targetExtension)
    {
        if (string.Equals(sourceExtension, ".cso", StringComparison.OrdinalIgnoreCase)
            && string.Equals(targetExtension, ".chd", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var fileName = Path.GetFileName(sourcePath);
        if (!string.IsNullOrWhiteSpace(fileName)
            && fileName.Contains(".nkit.", StringComparison.OrdinalIgnoreCase)
            && string.Equals(targetExtension, ".rvz", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private ConversionResult? TryExecuteSingleStepPlan(
        string sourcePath,
        string sourceExtension,
        ConversionTarget target,
        CancellationToken cancellationToken)
    {
        if (_executor is null)
            return null;

        var safety = ConversionSafety.Safe;
        var integrity = SourceIntegrityClassifier.Classify(sourceExtension, Path.GetFileName(sourcePath));
        if (integrity == SourceIntegrity.Lossy)
            safety = ConversionSafety.Acceptable;

        var capability = new ConversionCapability
        {
            SourceExtension = sourceExtension,
            TargetExtension = target.Extension,
            Tool = new ToolRequirement { ToolName = target.ToolName },
            Command = target.Command,
            ApplicableConsoles = null,
            RequiredSourceIntegrity = null,
            ResultIntegrity = integrity,
            Lossless = target.ToolName is "7z" or "chdman" or "dolphintool",
            Cost = 0,
            Verification = GetVerificationForExtension(target.Extension),
            Description = "adapter-single-step",
            Condition = ConversionCondition.None
        };

        var plan = new ConversionPlan
        {
            SourcePath = sourcePath,
            ConsoleKey = "N/A",
            Policy = ConversionPolicy.Auto,
            SourceIntegrity = integrity,
            Safety = safety,
            Steps = new[]
            {
                new ConversionStep
                {
                    Order = 0,
                    InputExtension = sourceExtension,
                    OutputExtension = target.Extension,
                    Capability = capability,
                    IsIntermediate = false
                }
            },
            SkipReason = null
        };

        if (plan.RequiresReview && !_allowReviewRequiredPlans)
        {
            return new ConversionResult(sourcePath, null, ConversionOutcome.Blocked, "review-required")
            {
                Plan = plan,
                SourceIntegrity = plan.SourceIntegrity,
                Safety = plan.Safety,
                VerificationResult = VerificationStatus.NotAttempted,
                DurationMs = 0
            };
        }

        return _executor.Execute(plan, cancellationToken: cancellationToken);
    }

    private static VerificationMethod GetVerificationForExtension(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".chd" => VerificationMethod.ChdmanVerify,
            ".rvz" => VerificationMethod.RvzMagicByte,
            ".zip" => VerificationMethod.SevenZipTest,
            _ => VerificationMethod.FileExistenceCheck
        };
    }

    public bool Verify(string targetPath, ConversionTarget target)
    {
        if (!File.Exists(targetPath))
            return false;

        return target.Extension.ToLowerInvariant() switch
        {
            ".chd" => _chdman.Verify(targetPath),
            ".rvz" => DolphinToolConverter.Verify(targetPath),
            ".zip" => _sevenZip.Verify(targetPath),
            ".iso" => new FileInfo(targetPath).Length > 0,
            ".bin" => new FileInfo(targetPath).Length > 0,
            _ => false
        };
    }

    public IReadOnlyList<string> GetMissingToolsForFormat(string? convertFormat)
    {
        var requiredTools = DetermineRequiredToolsForFormat(convertFormat);
        if (requiredTools.Count == 0)
            return Array.Empty<string>();

        var missing = new List<string>(requiredTools.Count);
        foreach (var tool in requiredTools)
        {
            if (string.IsNullOrWhiteSpace(_tools.FindTool(tool)))
                missing.Add(tool);
        }

        return missing;
    }

    private static IReadOnlyList<string> DetermineRequiredToolsForFormat(string? convertFormat)
    {
        if (string.IsNullOrWhiteSpace(convertFormat))
            return Array.Empty<string>();

        return convertFormat.Trim().ToLowerInvariant() switch
        {
            "chd" => ["chdman"],
            "rvz" => ["dolphintool"],
            "zip" => ["7z"],
            "7z" => ["7z"],
            "auto" => ["chdman", "dolphintool", "7z"],
            _ => Array.Empty<string>()
        };
    }

    private static bool IsSupportedSourceForTarget(string sourceExt, ConversionTarget target)
    {
        if (string.IsNullOrWhiteSpace(sourceExt))
            return false;

        var tool = target.ToolName.ToLowerInvariant();
        return tool switch
        {
            "chdman" => sourceExt is ".cue" or ".gdi" or ".iso" or ".bin" or ".img" or ".zip" or ".7z",
            "dolphintool" => sourceExt is ".iso" or ".gcm" or ".wbfs" or ".rvz" or ".gcz" or ".wia",
            "psxtract" => sourceExt == ".pbp",
            "7z" => true,
            _ => false
        };
    }
}
