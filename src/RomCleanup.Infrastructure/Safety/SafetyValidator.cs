using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;

namespace RomCleanup.Infrastructure.Safety;

/// <summary>
/// Safety validation service with policy profiles and tool health checks.
/// Port of SafetyToolsService.ps1.
/// </summary>
public sealed class SafetyValidator
{
    private static readonly Dictionary<string, SafetyProfile> Profiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Conservative"] = new SafetyProfile
        {
            Name = "Conservative",
            Strict = true,
            ProtectedPaths = [
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            ],
            ProtectedPathsText = "Windows, ProgramFiles, ProgramFiles(x86), UserProfile"
        },
        ["Balanced"] = new SafetyProfile
        {
            Name = "Balanced",
            Strict = false,
            ProtectedPaths = [
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            ],
            ProtectedPathsText = "Windows, ProgramFiles, ProgramFiles(x86)"
        },
        ["Expert"] = new SafetyProfile
        {
            Name = "Expert",
            Strict = false,
            ProtectedPaths = [
                Environment.GetFolderPath(Environment.SpecialFolder.Windows)
            ],
            ProtectedPathsText = "Windows"
        }
    };

    private static readonly string[] ToolNames = ["chdman", "dolphintool", "7z", "psxtract", "ciso"];

    private readonly IToolRunner _tools;
    private readonly IFileSystem _fs;
    private readonly Action<string>? _log;

    public SafetyValidator(IToolRunner tools, IFileSystem fs, Action<string>? log = null)
    {
        _tools = tools;
        _fs = fs;
        _log = log;
    }

    /// <summary>
    /// Get all available safety profiles.
    /// </summary>
    public static IReadOnlyDictionary<string, SafetyProfile> GetProfiles() => Profiles;

    /// <summary>
    /// Get a specific safety profile by name.
    /// </summary>
    public static SafetyProfile GetProfile(string name)
    {
        if (Profiles.TryGetValue(name, out var profile))
            return profile;
        return Profiles["Balanced"]; // default
    }

    /// <summary>
    /// Normalize a path for safety checking.
    /// </summary>
    public static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { return Path.GetFullPath(path.Trim()); }
        catch { return null; }
    }

    /// <summary>
    /// Run sandbox validation: check roots, paths, tools, DAT config.
    /// Returns blockers (must fix) and warnings (should fix).
    /// </summary>
    public SandboxValidationResult ValidateSandbox(
        IReadOnlyList<string> roots,
        string? trashRoot = null,
        string? auditRoot = null,
        bool strictSafety = false,
        string? protectedPathsText = null,
        bool useDat = false,
        string? datRoot = null,
        bool convertEnabled = false,
        IDictionary<string, string>? toolOverrides = null,
        IReadOnlyList<string>? extensions = null)
    {
        var blockers = new List<string>();
        var warnings = new List<string>();
        var recommendations = new List<string>();
        var pathChecks = new List<PathCheckEntry>();

        // Parse protected paths
        var protectedPaths = ParseProtectedPaths(protectedPathsText, strictSafety);

        // Check roots
        foreach (var root in roots)
        {
            var normalized = NormalizePath(root);
            if (normalized is null)
            {
                blockers.Add($"Invalid root path: {root}");
                pathChecks.Add(new PathCheckEntry { Path = root, Status = "blocked", Reason = "Invalid path" });
                continue;
            }

            if (!Directory.Exists(normalized))
            {
                blockers.Add($"Root directory does not exist: {normalized}");
                pathChecks.Add(new PathCheckEntry { Path = normalized, Status = "blocked", Reason = "Does not exist" });
                continue;
            }

            // Drive root check (e.g., C:\)
            if (Path.GetPathRoot(normalized) == normalized)
            {
                blockers.Add($"Root is a drive root (dangerous): {normalized}");
                pathChecks.Add(new PathCheckEntry { Path = normalized, Status = "blocked", Reason = "Drive root" });
                continue;
            }

            // Protected path check
            var protectedMatch = protectedPaths.FirstOrDefault(p =>
                normalized.StartsWith(p, StringComparison.OrdinalIgnoreCase));
            if (protectedMatch is not null)
            {
                blockers.Add($"Root is inside protected path '{protectedMatch}': {normalized}");
                pathChecks.Add(new PathCheckEntry { Path = normalized, Status = "blocked", Reason = $"Inside protected: {protectedMatch}" });
                continue;
            }

            pathChecks.Add(new PathCheckEntry { Path = normalized, Status = "ok" });
        }

        // Check extensions
        if (extensions is null || extensions.Count == 0)
            warnings.Add("No file extensions specified — will scan all files");

        // DAT checks
        if (useDat)
        {
            if (string.IsNullOrWhiteSpace(datRoot))
            {
                warnings.Add("DAT enabled but no DAT root specified");
                recommendations.Add("Set a DAT root directory for ROM verification");
            }
            else if (!Directory.Exists(datRoot))
            {
                warnings.Add($"DAT root does not exist: {datRoot}");
            }
        }

        // Conversion tool checks
        if (convertEnabled)
        {
            string? chdmanPath = null;
            if (toolOverrides is not null && toolOverrides.TryGetValue("chdman", out var chdOverride))
                chdmanPath = chdOverride;
            chdmanPath ??= _tools.FindTool("chdman");
            if (chdmanPath is null)
                warnings.Add("Conversion enabled but chdman not found");
        }

        // Audit root check
        if (!string.IsNullOrWhiteSpace(auditRoot))
        {
            var normalizedAudit = NormalizePath(auditRoot);
            if (normalizedAudit is not null && !Directory.Exists(normalizedAudit))
            {
                try
                {
                    _fs.EnsureDirectory(normalizedAudit);
                }
                catch
                {
                    blockers.Add($"Cannot create audit directory: {auditRoot}");
                }
            }
        }

        var status = blockers.Count > 0 ? "blocked" : "ok";

        return new SandboxValidationResult
        {
            Status = status,
            BlockerCount = blockers.Count,
            WarningCount = warnings.Count,
            RootCount = roots.Count,
            StrictSafety = strictSafety,
            UseDat = useDat,
            ConvertEnabled = convertEnabled,
            Blockers = blockers,
            Warnings = warnings,
            Recommendations = recommendations,
            PathChecks = pathChecks,
            CheckedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Probe all known tools and report health status.
    /// </summary>
    public ToolSelfTestResult TestTools(
        IDictionary<string, string>? toolOverrides = null,
        int timeoutSeconds = 8)
    {
        var results = new List<ToolTestEntry>();
        int healthy = 0, missing = 0, warning = 0;

        foreach (var toolName in ToolNames)
        {
            string? overridePath = null;
            if (toolOverrides is not null && toolOverrides.TryGetValue(toolName, out var ovr))
                overridePath = ovr;
            var toolPath = overridePath ?? _tools.FindTool(toolName);

            if (toolPath is null)
            {
                results.Add(new ToolTestEntry { Tool = toolName, Status = "missing" });
                missing++;
                _log?.Invoke($"Tool '{toolName}': MISSING");
                continue;
            }

            try
            {
                // Probe the tool with a version/help flag
                var versionArgs = toolName.ToLowerInvariant() switch
                {
                    "7z" => new[] { "i" },
                    "chdman" => new[] { "help" },
                    "dolphintool" => new[] { "--help" },
                    _ => new[] { "--version" }
                };

                var probeResult = _tools.InvokeProcess(toolPath, versionArgs, $"{toolName} probe");

                if (probeResult.Success || probeResult.ExitCode == 0)
                {
                    // Extract version from output (first line typically)
                    var versionLine = probeResult.Output.Split('\n').FirstOrDefault()?.Trim();
                    results.Add(new ToolTestEntry
                    {
                        Tool = toolName,
                        Status = "healthy",
                        Path = toolPath,
                        Version = versionLine
                    });
                    healthy++;
                    _log?.Invoke($"Tool '{toolName}': HEALTHY at {toolPath}");
                }
                else
                {
                    results.Add(new ToolTestEntry
                    {
                        Tool = toolName,
                        Status = "warning",
                        Path = toolPath,
                        Error = $"Non-zero exit code: {probeResult.ExitCode}"
                    });
                    warning++;
                    _log?.Invoke($"Tool '{toolName}': WARNING (exit {probeResult.ExitCode})");
                }
            }
            catch (Exception ex)
            {
                results.Add(new ToolTestEntry
                {
                    Tool = toolName,
                    Status = "error",
                    Path = toolPath,
                    Error = ex.Message
                });
                warning++;
                _log?.Invoke($"Tool '{toolName}': ERROR - {ex.Message}");
            }
        }

        return new ToolSelfTestResult
        {
            Results = results,
            HealthyCount = healthy,
            MissingCount = missing,
            WarningCount = warning
        };
    }

    private static List<string> ParseProtectedPaths(string? text, bool strict)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(p => NormalizePath(p))
                .Where(p => p is not null)
                .Cast<string>()
                .ToList();
        }

        // Default: use profile-based protection
        var profile = strict ? GetProfile("Conservative") : GetProfile("Balanced");
        return profile.ProtectedPaths.ToList();
    }
}
