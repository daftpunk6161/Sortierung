using System.IO.Compression;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Core.Conversion;

namespace RomCleanup.Infrastructure.Conversion;

/// <summary>
/// Format conversion orchestrator. Port of Convert.ps1.
/// Maps console types to target formats and drives chdman/dolphintool/7z conversions.
/// </summary>
public sealed class FormatConverterAdapter : IFormatConverter
{
    private readonly IToolRunner _tools;
    private readonly IReadOnlyDictionary<string, ConversionTarget> _bestFormats;
    private readonly IConversionRegistry? _registry;
    private readonly IConversionPlanner? _planner;
    private readonly IConversionExecutor? _executor;

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
        : this(tools, null, null, null) { }

    /// <summary>
    /// Creates a FormatConverterAdapter with optional custom format mappings.
    /// Falls back to <see cref="DefaultBestFormats"/> when <paramref name="bestFormats"/> is null.
    /// </summary>
    public FormatConverterAdapter(IToolRunner tools, IReadOnlyDictionary<string, ConversionTarget>? bestFormats)
        : this(tools, bestFormats, null, null)
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
        IConversionExecutor? executor)
    {
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _bestFormats = bestFormats ?? DefaultBestFormats;
        _registry = registry;
        _planner = planner;
        _executor = executor;
    }

    public FormatConverterAdapter(
        IToolRunner tools,
        IReadOnlyDictionary<string, ConversionTarget>? bestFormats,
        IConversionRegistry? registry,
        IConversionExecutor? executor)
        : this(tools, bestFormats, registry, null, executor)
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

        var sourceExt = Path.GetExtension(sourcePath).ToLowerInvariant();
        var dir = Path.GetDirectoryName(sourcePath)!;
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        var targetPath = Path.Combine(dir, baseName + target.Extension);

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
            "chdman" => ConvertWithChdman(sourcePath, targetPath, toolPath, target.Command),
            "dolphintool" => ConvertWithDolphinTool(sourcePath, targetPath, toolPath, sourceExt),
            "7z" => ConvertWithSevenZip(sourcePath, targetPath, toolPath),
            "psxtract" => ConvertWithPsxtract(sourcePath, targetPath, toolPath, target.Command),
            _ => new ConversionResult(sourcePath, null, ConversionOutcome.Error, $"unknown-tool:{target.ToolName}")
        };
    }

    /// <summary>
    /// Planner-backed conversion path that uses source path + console key to compute and execute a full plan.
    /// Falls back to legacy conversion flow if planner/executor are not available.
    /// </summary>
    public ConversionResult ConvertForConsole(string sourcePath, string consoleKey, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath))
            return new ConversionResult(sourcePath, null, ConversionOutcome.Error, "source-not-found");

        var sourceExt = Path.GetExtension(sourcePath).ToLowerInvariant();

        if (_planner is null || _executor is null)
        {
            var target = GetTargetFormat(consoleKey, sourceExt);
            if (target is null)
                return new ConversionResult(sourcePath, null, ConversionOutcome.Skipped, "no-target-defined");
            return Convert(sourcePath, target, cancellationToken);
        }

        var plan = _planner.Plan(sourcePath, consoleKey, sourceExt);
        if (!plan.IsExecutable)
        {
            // Preserve legacy archive extraction behavior for disc systems when graph data has no archive edge.
            if (string.Equals(plan.SkipReason, "no-conversion-path", StringComparison.OrdinalIgnoreCase)
                && IsArchiveContainerSource(sourceExt))
            {
                var fallbackTarget = GetTargetFormat(consoleKey, sourceExt);
                if (fallbackTarget is not null)
                    return ConvertLegacy(sourcePath, fallbackTarget, cancellationToken);
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

        return _executor.Execute(plan, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Returns the planner-generated conversion plan for preview/review UI flows.
    /// Returns null when no planner is configured or source file does not exist.
    /// </summary>
    public ConversionPlan? PlanForConsole(string sourcePath, string consoleKey)
    {
        if (_planner is null || !File.Exists(sourcePath))
            return null;

        var sourceExt = Path.GetExtension(sourcePath).ToLowerInvariant();
        return _planner.Plan(sourcePath, consoleKey, sourceExt);
    }

    private static bool IsArchiveContainerSource(string extension)
    {
        return string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".7z", StringComparison.OrdinalIgnoreCase);
    }

    private ConversionResult ConvertLegacy(string sourcePath, ConversionTarget target, CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath))
            return new ConversionResult(sourcePath, null, ConversionOutcome.Error, "source-not-found");

        cancellationToken.ThrowIfCancellationRequested();

        var toolPath = _tools.FindTool(target.ToolName);
        if (toolPath is null)
            return new ConversionResult(sourcePath, null, ConversionOutcome.Skipped, $"tool-not-found:{target.ToolName}");

        var sourceExt = Path.GetExtension(sourcePath).ToLowerInvariant();
        var dir = Path.GetDirectoryName(sourcePath)!;
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        var targetPath = Path.Combine(dir, baseName + target.Extension);

        if (string.Equals(sourceExt, target.Extension, StringComparison.OrdinalIgnoreCase))
            return new ConversionResult(sourcePath, null, ConversionOutcome.Skipped, "already-target-format");

        if (File.Exists(targetPath))
            return new ConversionResult(sourcePath, null, ConversionOutcome.Skipped, "target-exists");

        return target.ToolName.ToLowerInvariant() switch
        {
            "chdman" => ConvertWithChdman(sourcePath, targetPath, toolPath, target.Command),
            "dolphintool" => ConvertWithDolphinTool(sourcePath, targetPath, toolPath, sourceExt),
            "7z" => ConvertWithSevenZip(sourcePath, targetPath, toolPath),
            "psxtract" => ConvertWithPsxtract(sourcePath, targetPath, toolPath, target.Command),
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

        var ext = target.Extension.ToLowerInvariant();

        if (ext == ".chd")
        {
            var chdmanPath = _tools.FindTool("chdman");
            if (chdmanPath is null) return false;
            var result = _tools.InvokeProcess(chdmanPath, new[] { "verify", "-i", targetPath }, "chdman verify");
            return result.Success;
        }

        if (ext == ".rvz")
        {
            // DolphinTool does not have a verify command.
            // Verify by checking file existence, non-zero size, and RVZ magic bytes.
            var info = new FileInfo(targetPath);
            if (!info.Exists || info.Length < 4) return false;
            try
            {
                using var fs = File.OpenRead(targetPath);
                Span<byte> magic = stackalloc byte[4];
                if (fs.ReadAtLeast(magic, 4, throwOnEndOfStream: false) < 4) return false;
                // RVZ magic: "RVZ\x01"
                return magic[0] == 'R' && magic[1] == 'V' && magic[2] == 'Z' && magic[3] == 0x01;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        if (ext == ".zip")
        {
            var zipPath = _tools.FindTool("7z");
            if (zipPath is null) return false;
            var result = _tools.InvokeProcess(zipPath, new[] { "t", "-y", targetPath }, "7z verify");
            return result.Success;
        }

        // PBP→CHD conversion produces a .chd output; verify via chdman.
        // This path handles the case where Verify is called with the PbpTarget
        // but the actual output file is .chd (already handled above via ext==".chd").
        // If the target extension is something else for PBP, fall through.

        return false;
    }

    private ConversionResult ConvertWithChdman(string sourcePath, string targetPath, string toolPath, string command)
    {
        var sourceExt = Path.GetExtension(sourcePath).ToLowerInvariant();

        // ZIP/7Z containing .cue/.bin → extract first, then convert the .cue
        if (sourceExt is ".zip" or ".7z")
            return ConvertArchiveToChdman(sourcePath, targetPath, toolPath, command, sourceExt);

        // chdman only accepts .cue, .gdi, .iso, .bin as direct input
        if (sourceExt is not (".cue" or ".gdi" or ".iso" or ".bin" or ".img"))
            return new ConversionResult(sourcePath, null, ConversionOutcome.Skipped,
                $"chdman-unsupported-source:{sourceExt}");

        // PS2 CD/DVD safety heuristic: images under 700MB should be treated as CD images.
        // This avoids createDVD on CD-based PS2 titles which can produce invalid outputs.
        var effectiveCommand = command;
        if (string.Equals(command, "createdvd", StringComparison.OrdinalIgnoreCase) && sourceExt is ".iso" or ".bin" or ".img")
        {
            try
            {
                var size = new FileInfo(sourcePath).Length;
                if (size > 0 && size < ToolInvokers.ToolInvokerSupport.CdImageThresholdBytes)
                    effectiveCommand = "createcd";
            }
            catch (IOException)
            {
                // Best effort only; keep caller-selected command if size cannot be read.
            }
        }

        var args = new[] { effectiveCommand, "-i", sourcePath, "-o", targetPath };
        var result = _tools.InvokeProcess(toolPath, args, "chdman");

        if (!result.Success)
        {
            CleanupPartialOutput(targetPath);
            var detail = string.IsNullOrWhiteSpace(result.Output) ? "" : $" ({result.Output.Trim().Split('\n')[0]})";
            return new ConversionResult(sourcePath, null, ConversionOutcome.Error,
                $"chdman-failed{detail}", result.ExitCode);
        }

        if (!File.Exists(targetPath))
            return new ConversionResult(sourcePath, null, ConversionOutcome.Error, "output-not-created");

        return new ConversionResult(sourcePath, targetPath, ConversionOutcome.Success);
    }

    /// <summary>Maximum number of entries allowed in a ZIP archive during conversion extraction.</summary>
    private const int MaxZipEntryCount = 10_000;

    /// <summary>Maximum total uncompressed size for archive extraction (10 GB, generous for DVD images).</summary>
    private const long MaxExtractedTotalBytes = 10L * 1024 * 1024 * 1024;

    /// <summary>SEC-CONV-04: Maximum allowed compression ratio per entry (zip bomb protection).</summary>
    internal static readonly double MaxCompressionRatio = 50.0;

    /// <summary>
    /// Extract a ZIP/7Z archive, find the .cue file inside, convert to CHD, then clean up.
    /// Handles the common case of disc-based ROMs distributed as ZIP containing .bin/.cue.
    /// SEC-CONV-01: Per-entry Zip-Slip-safe extraction (no ZipFile.ExtractToDirectory).
    /// SEC-CONV-02/03: Entry count + total size limits to block zip bombs.
    /// </summary>
    private ConversionResult ConvertArchiveToChdman(
        string sourcePath, string targetPath, string toolPath, string command, string sourceExt)
    {
        var dir = Path.GetDirectoryName(sourcePath)!;
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        var extractDir = Path.Combine(dir, $"_extract_{baseName}_{Guid.NewGuid():N}");

        try
        {
            // Step 1: Extract archive with Zip-Slip protection
            if (sourceExt == ".zip")
            {
                var extractError = ExtractZipSafe(sourcePath, extractDir);
                if (extractError is not null)
                    return new ConversionResult(sourcePath, null, ConversionOutcome.Error, extractError);
            }
            else
            {
                // .7z — use 7z tool to extract
                var sevenZipPath = _tools.FindTool("7z");
                if (sevenZipPath is null)
                    return new ConversionResult(sourcePath, null, ConversionOutcome.Skipped, "tool-not-found:7z");

                Directory.CreateDirectory(extractDir);
                var extractResult = _tools.InvokeProcess(sevenZipPath,
                    new[] { "x", "-y", $"-o{extractDir}", sourcePath }, "7z extract");
                if (!extractResult.Success)
                    return new ConversionResult(sourcePath, null, ConversionOutcome.Error, "7z-extract-failed");

                // SEC-CONV-07: Post-extraction validation for 7z (parity with zip bomb protection)
                if (!ValidateExtractedContents(extractDir))
                    return new ConversionResult(sourcePath, null, ConversionOutcome.Error, "archive-path-traversal-detected");

                var extractedFiles = Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories);
                if (extractedFiles.Length > MaxZipEntryCount)
                    return new ConversionResult(sourcePath, null, ConversionOutcome.Error, "archive-too-many-entries");

                long totalExtractedSize = 0;
                foreach (var f in extractedFiles)
                {
                    totalExtractedSize += new FileInfo(f).Length;
                    if (totalExtractedSize > MaxExtractedTotalBytes)
                        return new ConversionResult(sourcePath, null, ConversionOutcome.Error, "archive-too-large");
                }
            }

            // Step 2: Find the .cue file (preferred) or .gdi, or fall back to .iso/.bin
            var cueFiles = Directory.GetFiles(extractDir, "*.cue", SearchOption.AllDirectories);
            var gdiFiles = Directory.GetFiles(extractDir, "*.gdi", SearchOption.AllDirectories);
            var isoFiles = Directory.GetFiles(extractDir, "*.iso", SearchOption.AllDirectories);

            // TASK-012/TASK-149: Deterministic CUE selection — sort alphabetically before selecting.
            // This ensures identical results regardless of filesystem enumeration order.
            Array.Sort(cueFiles, StringComparer.OrdinalIgnoreCase);
            Array.Sort(gdiFiles, StringComparer.OrdinalIgnoreCase);
            Array.Sort(isoFiles, StringComparer.OrdinalIgnoreCase);

            // Path traversal guard: Ensure selected files are within extractDir
            static bool IsWithinDir(string filePath, string baseDir)
            {
                var fullBase = Path.GetFullPath(baseDir) + Path.DirectorySeparatorChar;
                return Path.GetFullPath(filePath).StartsWith(fullBase, StringComparison.OrdinalIgnoreCase);
            }

            // Filter to safe files within extractDir
            var safeCueFiles = cueFiles.Where(f => IsWithinDir(f, extractDir)).ToArray();
            var safeGdiFiles = gdiFiles.Where(f => IsWithinDir(f, extractDir)).ToArray();
            var safeIsoFiles = isoFiles.Where(f => IsWithinDir(f, extractDir)).ToArray();

            // TASK-012: Multi-CUE atomicity — if multiple .cue files exist, each needs conversion.
            // For multi-disc archives, convert all CUE files as atomic set.
            if (safeCueFiles.Length > 1)
            {
                return ConvertMultiCueArchive(sourcePath, safeCueFiles, dir, toolPath, command);
            }

            string? inputFile = null;
            if (safeCueFiles.Length == 1)
                inputFile = safeCueFiles[0];
            else if (safeGdiFiles.Length > 0)
                inputFile = safeGdiFiles[0];
            else if (safeIsoFiles.Length > 0)
                inputFile = safeIsoFiles[0];

            if (inputFile is null)
                return new ConversionResult(sourcePath, null, ConversionOutcome.Skipped,
                    "archive-no-disc-image");

            // Step 3: Convert via chdman
            var args = new[] { command, "-i", inputFile, "-o", targetPath };
            var result = _tools.InvokeProcess(toolPath, args, "chdman");

            if (!result.Success)
            {
                CleanupPartialOutput(targetPath);
                var detail = string.IsNullOrWhiteSpace(result.Output) ? "" : $" ({result.Output.Trim().Split('\n')[0]})";
                return new ConversionResult(sourcePath, null, ConversionOutcome.Error,
                    $"chdman-failed{detail}", result.ExitCode);
            }

            if (!File.Exists(targetPath))
                return new ConversionResult(sourcePath, null, ConversionOutcome.Error, "output-not-created");

            return new ConversionResult(sourcePath, targetPath, ConversionOutcome.Success);
        }
        catch (InvalidDataException)
        {
            return new ConversionResult(sourcePath, null, ConversionOutcome.Error, "archive-corrupt");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ConversionResult(sourcePath, null, ConversionOutcome.Error,
                $"extract-failed:{ex.Message}");
        }
        finally
        {
            // Clean up extracted files
            try
            {
                if (Directory.Exists(extractDir))
                    Directory.Delete(extractDir, recursive: true);
            }
            catch (IOException) { /* best-effort cleanup — dir may be locked */ }
        }
    }

    /// <summary>
    /// TASK-012: Convert all CUE files from a multi-disc archive atomically.
    /// All must succeed or the entire conversion is rolled back.
    /// </summary>
    private ConversionResult ConvertMultiCueArchive(
        string sourcePath, string[] cueFiles, string outputDir, string toolPath, string command)
    {
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        var outputs = new List<string>();

        for (int i = 0; i < cueFiles.Length; i++)
        {
            var cueBaseName = Path.GetFileNameWithoutExtension(cueFiles[i]);
            var targetPath = Path.Combine(outputDir, cueBaseName + ".chd");

            var args = new[] { command, "-i", cueFiles[i], "-o", targetPath };
            var result = _tools.InvokeProcess(toolPath, args, "chdman");

            if (!result.Success || !File.Exists(targetPath))
            {
                // Rollback: delete all already-created CHDs
                foreach (var output in outputs)
                    CleanupPartialOutput(output);
                CleanupPartialOutput(targetPath);

                return new ConversionResult(sourcePath, null, ConversionOutcome.Error,
                    $"multi-cue-failed:disc{i + 1}of{cueFiles.Length}");
            }

            outputs.Add(targetPath);
        }

        // Return first output as primary, note multi-disc in detail
        return new ConversionResult(sourcePath, outputs[0], ConversionOutcome.Success,
            $"multi-disc:{cueFiles.Length}");
    }

    private ConversionResult ConvertWithDolphinTool(string sourcePath, string targetPath, string toolPath, string sourceExt)
    {
        var allowedExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".iso", ".gcm", ".wbfs", ".rvz", ".gcz", ".wia" };

        if (!allowedExts.Contains(sourceExt))
            return new ConversionResult(sourcePath, null, ConversionOutcome.Skipped, "dolphintool-unsupported-source");

        var args = new[] { "convert", "-i", sourcePath, "-o", targetPath, "-f", "rvz", "-c", "zstd", "-l", "5", "-b", "131072" };
        var result = _tools.InvokeProcess(toolPath, args, "dolphintool");

        if (!result.Success)
        {
            CleanupPartialOutput(targetPath);
            return new ConversionResult(sourcePath, null, ConversionOutcome.Error,
                "dolphintool-failed", result.ExitCode);
        }

        if (!File.Exists(targetPath))
            return new ConversionResult(sourcePath, null, ConversionOutcome.Error, "output-not-created");

        return new ConversionResult(sourcePath, targetPath, ConversionOutcome.Success);
    }

    private ConversionResult ConvertWithSevenZip(string sourcePath, string targetPath, string toolPath)
    {
        var zipTool = toolPath;

        var args = new[] { "a", "-tzip", "-y", targetPath, sourcePath };
        var result = _tools.InvokeProcess(zipTool, args, "7z");

        if (!result.Success)
        {
            CleanupPartialOutput(targetPath);
            return new ConversionResult(sourcePath, null, ConversionOutcome.Error,
                "7z-failed", result.ExitCode);
        }

        if (!File.Exists(targetPath))
            return new ConversionResult(sourcePath, null, ConversionOutcome.Error, "output-not-created");

        return new ConversionResult(sourcePath, targetPath, ConversionOutcome.Success);
    }

    private ConversionResult ConvertWithPsxtract(string sourcePath, string targetPath, string toolPath, string command)
    {
        var args = new[] { command, "-i", sourcePath, "-o", targetPath };
        var result = _tools.InvokeProcess(toolPath, args, "psxtract");

        if (!result.Success)
        {
            CleanupPartialOutput(targetPath);
            return new ConversionResult(sourcePath, null, ConversionOutcome.Error,
                "psxtract-failed", result.ExitCode);
        }

        if (!File.Exists(targetPath))
            return new ConversionResult(sourcePath, null, ConversionOutcome.Error, "output-not-created");

        return new ConversionResult(sourcePath, targetPath, ConversionOutcome.Success);
    }

    /// <summary>
    /// SEC-CONV-01: Safe per-entry ZIP extraction with Zip-Slip protection.
    /// Validates each entry path before extraction, enforces entry count and total size limits.
    /// </summary>
    private static string? ExtractZipSafe(string zipPath, string extractDir)
    {
        Directory.CreateDirectory(extractDir);
        var normalizedBase = Path.GetFullPath(extractDir) + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(zipPath);

        // SEC-CONV-02: Entry count limit (zip bomb protection)
        if (archive.Entries.Count > MaxZipEntryCount)
            return $"archive-too-many-entries:{archive.Entries.Count}";

        // SEC-CONV-03: Total uncompressed size limit
        long totalUncompressed = 0;
        foreach (var entry in archive.Entries)
        {
            totalUncompressed += entry.Length;
            if (totalUncompressed > MaxExtractedTotalBytes)
                return "archive-extraction-size-exceeded";

            // SEC-CONV-04: Per-entry compression ratio check (zip bomb detection)
            // Only check entries >1 MB uncompressed to avoid false positives on small legitimate files
            if (entry.CompressedLength > 0 && entry.Length > 1_048_576 &&
                entry.Length / (double)entry.CompressedLength > MaxCompressionRatio)
                return "archive-compression-ratio-exceeded";
        }

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue; // Skip directory entries

            var destPath = Path.GetFullPath(Path.Combine(extractDir, entry.FullName));

            // Zip-Slip protection: reject entries that escape extractDir
            if (!destPath.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
                return "archive-zip-slip-detected";

            // Ensure parent directory exists
            var entryDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(entryDir))
                Directory.CreateDirectory(entryDir);

            entry.ExtractToFile(destPath, overwrite: false);
        }

        return null; // success
    }

    /// <summary>
    /// Post-extraction validation: ensure all files and directories are within the expected root
    /// and no reparse points were created during extraction.
    /// </summary>
    private static bool ValidateExtractedContents(string extractDir)
    {
        var normalizedBase = Path.GetFullPath(extractDir) + Path.DirectorySeparatorChar;

        foreach (var dir in Directory.GetDirectories(extractDir, "*", SearchOption.AllDirectories))
        {
            if (!Path.GetFullPath(dir).StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
                return false;
            var dirInfo = new DirectoryInfo(dir);
            if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                return false;
        }

        foreach (var file in Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories))
        {
            if (!Path.GetFullPath(file).StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
                return false;
            var attrs = File.GetAttributes(file);
            if ((attrs & FileAttributes.ReparsePoint) != 0)
                return false;
        }

        return true;
    }

    private static void CleanupPartialOutput(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort cleanup
        }
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
