using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.Safety;

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
            ProtectedPaths = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            }.Where(p => !string.IsNullOrEmpty(p)).ToArray(),
            ProtectedPathsText = "Windows, ProgramFiles, ProgramFiles(x86), Desktop, Dokumente"
        },
        ["Balanced"] = new SafetyProfile
        {
            Name = "Balanced",
            Strict = false,
            ProtectedPaths = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            }.Where(p => !string.IsNullOrEmpty(p)).ToArray(),
            ProtectedPathsText = "Windows, ProgramFiles, ProgramFiles(x86)"
        },
        ["Expert"] = new SafetyProfile
        {
            Name = "Expert",
            Strict = false,
            ProtectedPaths = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Windows)
            }.Where(p => !string.IsNullOrEmpty(p)).ToArray(),
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

        // SEC-PATH-03: Reject extended-length/device path prefixes (bypass for path normalization)
        var trimmed = path.Trim();
        if (trimmed.StartsWith(@"\\?\") || trimmed.StartsWith(@"\\.\"))
            return null;

        // SEC-PATH-04: Reject NTFS Alternate Data Streams (colon after drive letter)
        var adsCheckPortion = trimmed.Length >= 2 && trimmed[1] == ':'
            ? trimmed[2..]
            : trimmed;
        if (adsCheckPortion.Contains(':'))
            return null;

        // SEC-PATH-05: Reject trailing dots/spaces in path segments (Windows silently strips them → path bypass)
        var segments = trimmed.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var seg in segments)
        {
            if (seg.Length > 0 && (seg[^1] == '.' || seg[^1] == ' '))
                return null;
        }

        try { return Path.GetFullPath(trimmed); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or System.Security.SecurityException or PathTooLongException)
        {
            return null;
        }
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

            // Protected path check (use trailing separator to avoid C:\WindowsApps matching C:\Windows)
            var normalizedWithSep = normalized.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var protectedMatch = protectedPaths.FirstOrDefault(p =>
            {
                var protectedWithSep = p.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                return normalizedWithSep.StartsWith(protectedWithSep, StringComparison.OrdinalIgnoreCase);
            });
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
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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

                var probeResult = _tools.InvokeProcess(
                    toolPath,
                    versionArgs,
                    $"{toolName} probe",
                    TimeSpan.FromSeconds(timeoutSeconds),
                    CancellationToken.None);

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

    /// <summary>
    /// Single source of truth: checks whether a path is inside a protected system directory.
    /// Used by CLI, API and SafetyValidator to avoid divergent implementations.
    /// </summary>
    public static bool IsProtectedSystemPath(string fullPath)
    {
        var protectedRoots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }
        .Where(p => !string.IsNullOrWhiteSpace(p))
        .ToArray();

        var normalized = NormalizePath(fullPath);
        if (normalized is null) return true; // invalid path → treat as protected

        var normalizedWithSep = normalized.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var root in protectedRoots)
        {
            var rootWithSep = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (normalizedWithSep.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Single source of truth: checks whether a path is a drive root (e.g. C:\).
    /// Handles both trimmed ("C:") and untrimmed ("C:\") forms.
    /// </summary>
    public static bool IsDriveRoot(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return false;
        var trimmed = fullPath.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed.Length == 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':';
    }

    /// <summary>
    /// Single source of truth for writable export/report output paths.
    /// Blocks invalid paths, protected system locations, drive roots, and reparse-point targets.
    /// Returns the normalized absolute path when the destination is safe to create or overwrite.
    /// </summary>
    public static string EnsureSafeOutputPath(string path, bool allowUnc = true)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Output path must not be empty.");

        var trimmed = path.Trim();
        if (!allowUnc && trimmed.StartsWith(@"\\", StringComparison.Ordinal))
            throw new InvalidOperationException("Output path must not be a UNC path.");

        var normalized = NormalizePath(trimmed)
            ?? throw new InvalidOperationException("Output path is invalid.");

        if (IsProtectedSystemPath(normalized))
            throw new InvalidOperationException("Output path points to a protected system path.");

        if (IsDriveRoot(normalized))
            throw new InvalidOperationException("Output path must not be a drive root.");

        EnsureNoReparsePointInExistingAncestry(normalized);
        return normalized;
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

    private static void EnsureNoReparsePointInExistingAncestry(string normalizedPath)
    {
        try
        {
            if (File.Exists(normalizedPath))
            {
                var attrs = File.GetAttributes(normalizedPath);
                if ((attrs & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Output path must not target a reparse-point file.");
            }

            var current = Directory.Exists(normalizedPath)
                ? Path.GetFullPath(normalizedPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : Path.GetDirectoryName(Path.GetFullPath(normalizedPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            while (!string.IsNullOrWhiteSpace(current))
            {
                if (Directory.Exists(current))
                {
                    var info = new DirectoryInfo(current);
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidOperationException("Output path must not target a reparse-point directory.");
                }

                var root = Path.GetPathRoot(current);
                if (!string.IsNullOrWhiteSpace(root)
                    && string.Equals(
                        current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                        root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                        StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                var parent = Path.GetDirectoryName(current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                    break;

                current = parent;
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new InvalidOperationException("Output path attributes could not be verified.", ex);
        }
    }
}
