using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.Orchestration;

namespace RomCleanup.CLI;

/// <summary>
/// Pure argument parser: string[] → CliParseResult.
/// No side effects, no Console.Write, no File.Exists.
/// ADR-008 §C-01, §C-05.
/// </summary>
internal static class CliArgsParser
{
    private static readonly HashSet<string> AllowedHashTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SHA1", "SHA256", "MD5"
    };

    private static readonly HashSet<string> AllowedLogLevels = new(StringComparer.OrdinalIgnoreCase)
    {
        "Debug", "Info", "Warning", "Error"
    };

    private static readonly IReadOnlySet<string> AllowedConflictPolicies = RomCleanup.Contracts.RunConstants.ValidConflictPolicies;

    internal static CliParseResult Parse(string[] args)
    {
        if (args.Length == 0)
            return CliParseResult.Help();

        var opts = new CliRunOptions();
        var errors = new List<string>();
        var rootsSpecified = false;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg.ToLowerInvariant())
            {
                case "-roots" or "--roots":
                    rootsSpecified = true;
                    if (!TryConsumeValue(args, ref i, "--roots", errors, out var rootsRaw))
                        break;

                    if (!TryParseRootsArgument(rootsRaw, out var parsedRoots, out var rootsError))
                    {
                        errors.Add($"[Error] {rootsError}");
                        break;
                    }

                    opts.Roots = parsedRoots;
                    break;

                case "-mode" or "--mode":
                    if (!TryConsumeValue(args, ref i, "--mode", errors, out var modeVal))
                        break;

                    if (string.Equals(modeVal, "DryRun", StringComparison.OrdinalIgnoreCase))
                        opts.Mode = "DryRun";
                    else if (string.Equals(modeVal, "Move", StringComparison.OrdinalIgnoreCase))
                        opts.Mode = "Move";
                    else
                        errors.Add($"[Error] Invalid mode '{modeVal}'. Must be DryRun or Move.");
                    break;

                case "-prefer" or "--prefer" or "-preferregions":
                    if (!TryConsumeValue(args, ref i, "--prefer", errors, out var regionsRaw))
                        break;
                    opts.PreferRegions = regionsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    break;

                case "-extensions" or "--extensions":
                    if (!TryConsumeValue(args, ref i, "--extensions", errors, out var extsRaw))
                        break;
                    var exts = extsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    opts.Extensions = new HashSet<string>(
                        exts.Select(e => e.StartsWith(".") ? e : "." + e),
                        StringComparer.OrdinalIgnoreCase);
                    opts.ExtensionsExplicit = true;
                    break;

                case "-trashroot" or "--trashroot":
                    if (!TryConsumeValue(args, ref i, "--trashroot", errors, out var trashVal))
                        break;
                    opts.TrashRoot = trashVal;
                    break;

                case "-removejunk" or "--removejunk":
                    opts.RemoveJunk = true;
                    break;

                case "-no-removejunk" or "--no-removejunk":
                    opts.RemoveJunk = false;
                    break;

                case "-convertonly" or "--convertonly":
                    opts.ConvertOnly = true;
                    opts.ConvertFormat = true;
                    break;

                case "-conflictpolicy" or "--conflictpolicy":
                    if (!TryConsumeValue(args, ref i, "--conflictpolicy", errors, out var conflictPolicyVal))
                        break;
                    if (!AllowedConflictPolicies.Contains(conflictPolicyVal))
                    {
                        errors.Add($"[Error] Invalid conflict policy '{conflictPolicyVal}'. Must be Rename, Skip, or Overwrite.");
                        break;
                    }
                    opts.ConflictPolicy = conflictPolicyVal;
                    break;

                case "-gamesonly" or "--gamesonly":
                    opts.OnlyGames = true;
                    break;

                case "-keepunknown" or "--keepunknown":
                    opts.KeepUnknownWhenOnlyGames = true;
                    break;

                case "-dropunknown" or "--dropunknown":
                    opts.KeepUnknownWhenOnlyGames = false;
                    break;

                case "-aggressivejunk" or "--aggressivejunk":
                    opts.AggressiveJunk = true;
                    break;

                case "-sortconsole" or "--sortconsole":
                    opts.SortConsole = true;
                    break;

                case "-report" or "--report":
                    if (!TryConsumeValue(args, ref i, "--report", errors, out var reportVal))
                        break;
                    opts.ReportPath = reportVal;
                    break;

                case "-audit" or "--audit":
                    if (!TryConsumeValue(args, ref i, "--audit", errors, out var auditVal))
                        break;
                    opts.AuditPath = auditVal;
                    break;

                case "-log" or "--log":
                    if (!TryConsumeValue(args, ref i, "--log", errors, out var logVal))
                        break;
                    opts.LogPath = logVal;
                    break;

                case "-loglevel" or "--loglevel":
                    if (!TryConsumeValue(args, ref i, "--loglevel", errors, out var logLevelVal))
                        break;
                    if (!AllowedLogLevels.Contains(logLevelVal))
                    {
                        errors.Add($"[Error] Invalid log level '{logLevelVal}'. Must be Debug, Info, Warning, or Error.");
                        break;
                    }
                    opts.LogLevel = logLevelVal;
                    break;

                case "-enabledat" or "--enabledat":
                    opts.EnableDat = true;
                    break;

                case "-dat-audit" or "--dat-audit" or "-dataudit" or "--dataudit":
                    opts.EnableDatAudit = true;
                    break;

                case "-datrename" or "--datrename":
                    opts.EnableDatRename = true;
                    break;

                case "-datroot" or "--datroot":
                    if (!TryConsumeValue(args, ref i, "--datroot", errors, out var datRootVal))
                        break;
                    opts.DatRoot = datRootVal;
                    break;

                case "-hashtype" or "--hashtype":
                    if (!TryConsumeValue(args, ref i, "--hashtype", errors, out var hashTypeVal))
                        break;
                    if (!AllowedHashTypes.Contains(hashTypeVal))
                    {
                        errors.Add($"[Error] Invalid hash type '{hashTypeVal}'. Must be SHA1, SHA256, or MD5.");
                        break;
                    }
                    opts.HashType = hashTypeVal;
                    break;

                case "-convertformat" or "--convertformat":
                    opts.ConvertFormat = true;
                    break;

                case "-yes" or "--yes" or "-y":
                    opts.Yes = true;
                    break;

                case "-rollback" or "--rollback":
                    if (!TryConsumeValue(args, ref i, "--rollback", errors, out var rollbackPath))
                        break;
                    opts.RollbackAuditPath = rollbackPath;
                    break;

                case "-rollback-dry-run" or "--rollback-dry-run":
                    opts.RollbackDryRun = true;
                    break;

                case "-help" or "--help" or "-h" or "-?":
                    return CliParseResult.Help();

                case "--version" or "-v":
                    return CliParseResult.Version();

                default:
                    if (!arg.StartsWith("-"))
                    {
                        var roots = new List<string>(opts.Roots) { arg };
                        opts.Roots = roots.ToArray();
                    }
                    else
                    {
                        errors.Add($"[Error] Unknown flag '{arg}'. Use --help for usage.");
                    }
                    break;
            }
        }

        if (errors.Count > 0)
            return CliParseResult.ValidationError(errors);

        // --rollback mode: requires audit path, no roots needed
        if (!string.IsNullOrWhiteSpace(opts.RollbackAuditPath))
        {
            if (!File.Exists(opts.RollbackAuditPath))
                return CliParseResult.ValidationError([$"[Error] Audit file not found: {opts.RollbackAuditPath}"]);
            return CliParseResult.Rollback(opts);
        }

        if (opts.Roots.Length == 0)
        {
            if (rootsSpecified)
                return CliParseResult.ValidationError(["[Error] No valid root paths were provided."]);

            return CliParseResult.Help();
        }

        // Validate root directories exist
        foreach (var root in opts.Roots)
        {
            if (string.IsNullOrWhiteSpace(root))
                return CliParseResult.ValidationError(["[Error] Empty root path provided."]);

            if (IsUncPath(root))
                return CliParseResult.ValidationError([$"[Error] UNC root paths are not allowed: {root}"]);

            var fullRoot = Path.GetFullPath(root);
            if (IsProtectedSystemPath(fullRoot))
                return CliParseResult.ValidationError([$"[Error] Root directory is in a protected system path: {fullRoot}"]);

            if (!Directory.Exists(fullRoot))
                return CliParseResult.ValidationError([$"[Error] Root directory not found: {fullRoot}"]);

            // SEC-CLI-02: Block reparse points (symlinks/junctions) as root paths — parity with API
            try
            {
                var dirInfo = new DirectoryInfo(fullRoot);
                if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                    return CliParseResult.ValidationError([$"[Error] Root directory is a reparse point (symlink/junction): {fullRoot}"]);
            }
            catch (IOException ex)
            {
                return CliParseResult.ValidationError([$"[Error] Cannot verify root directory attributes: {fullRoot} ({ex.Message})"]);
            }
            catch (UnauthorizedAccessException ex)
            {
                return CliParseResult.ValidationError([$"[Error] Cannot verify root directory attributes: {fullRoot} ({ex.Message})"]);
            }
        }

        var protectedPathError = ValidateOptionalPath(opts.TrashRoot, "trash root", allowUnc: false)
            ?? ValidateOptionalPath(opts.DatRoot, "DAT root", allowUnc: false)
            ?? ValidateOptionalPath(opts.LogPath, "log path", allowUnc: false)
            ?? ValidateOptionalPath(opts.ReportPath, "report path", allowUnc: false)
            ?? ValidateOptionalPath(opts.AuditPath, "audit path", allowUnc: false);
        if (protectedPathError is not null)
            return CliParseResult.ValidationError([$"[Error] {protectedPathError}"]);

        // Validate extensions have dot prefix
        var invalidExts = opts.Extensions.Where(e => !e.StartsWith('.')).ToList();
        if (invalidExts.Count > 0)
            return CliParseResult.ValidationError([$"[Error] Extensions must start with '.': {string.Join(", ", invalidExts)}"]);

        if (!opts.OnlyGames && !opts.KeepUnknownWhenOnlyGames)
            return CliParseResult.ValidationError(["[Error] --dropunknown requires --gamesonly."]);

        return CliParseResult.Run(opts);
    }

    /// <summary>
    /// Consumes the next argument as a value, with strict validation.
    /// Returns false if: value missing (adds error) OR value looks like a flag (puts back, no error).
    /// ADR-008 §C-05.
    /// </summary>
    private static bool TryConsumeValue(string[] args, ref int index, string flagName,
        List<string> errors, out string value)
    {
        value = "";
        if (++index >= args.Length)
        {
            // Truly missing: no more arguments
            errors.Add($"[Error] Missing value for {flagName}.");
            return false;
        }

        var candidate = args[index];
        if (candidate.StartsWith('-') && !candidate.StartsWith("-.") && !candidate.StartsWith("-/"))
        {
            // Value looks like a flag — put back so it's parsed next iteration.
            // No error: the flag was just used without its optional value.
            index--;
            return false;
        }

        value = candidate;
        return true;
    }

    private static bool TryParseRootsArgument(string rawValue, out string[] roots, out string? error)
    {
        roots = Array.Empty<string>();
        error = null;

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            error = "No valid root paths were provided.";
            return false;
        }

        var parsedRoots = rawValue
            .Split(';', StringSplitOptions.None)
            .Select(part => part.Trim())
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        if (parsedRoots.Length == 0)
        {
            error = "No valid root paths were provided.";
            return false;
        }

        roots = parsedRoots;
        return true;
    }

    private static string? ValidateOptionalPath(string? path, string label, bool allowUnc)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        if (!allowUnc && IsUncPath(path))
            return $"{label} must not be a UNC path: {path}";

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (IsProtectedSystemPath(fullPath))
                return $"{label} points to a protected system path: {fullPath}";
        }
        catch (Exception ex)
        {
            return $"{label} is invalid: {ex.Message}";
        }

        return null;
    }

    private static bool IsUncPath(string path)
        => path.StartsWith("\\\\", StringComparison.Ordinal);

    private static bool IsProtectedSystemPath(string fullPath)
    {
        var protectedRoots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }
        .Where(p => !string.IsNullOrWhiteSpace(p))
        .Select(p => Path.GetFullPath(p!).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        .ToArray();

        var normalized = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return protectedRoots.Any(root =>
            normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Result of CLI argument parsing. Immutable value object.
/// </summary>
internal sealed class CliParseResult
{
    public CliCommand Command { get; private init; }
    public int ExitCode { get; private init; }
    public IReadOnlyList<string> Errors { get; private init; } = Array.Empty<string>();
    public CliRunOptions? Options { get; private init; }

    public static CliParseResult Help() => new() { Command = CliCommand.Help, ExitCode = 0 };
    public static CliParseResult Version() => new() { Command = CliCommand.Version, ExitCode = 0 };
    public static CliParseResult Rollback(CliRunOptions options) =>
        new() { Command = CliCommand.Rollback, ExitCode = 0, Options = options };

    public static CliParseResult ValidationError(IReadOnlyList<string> errors) =>
        new() { Command = CliCommand.Run, ExitCode = 3, Errors = errors };

    public static CliParseResult Run(CliRunOptions options) =>
        new() { Command = CliCommand.Run, ExitCode = 0, Options = options };
}

internal enum CliCommand { Run, Help, Version, Rollback }

/// <summary>
/// Raw parsed CLI options — before settings merge.
/// </summary>
internal sealed class CliRunOptions
{
    public string[] Roots { get; set; } = Array.Empty<string>();
    public string Mode { get; set; } = "DryRun";
    public string[] PreferRegions { get; set; } = Array.Empty<string>();
    public HashSet<string> Extensions { get; set; } = new(RunOptions.DefaultExtensions, StringComparer.OrdinalIgnoreCase);
    public bool ExtensionsExplicit { get; set; }
    public string? TrashRoot { get; set; }
    public bool RemoveJunk { get; set; } = true;
    public bool OnlyGames { get; set; }
    public bool KeepUnknownWhenOnlyGames { get; set; } = true;
    public bool AggressiveJunk { get; set; }
    public bool SortConsole { get; set; }
    public bool EnableDat { get; set; }
    public bool EnableDatAudit { get; set; }
    public bool EnableDatRename { get; set; }
    public string? DatRoot { get; set; }
    public string? HashType { get; set; }
    public bool ConvertFormat { get; set; }
    public bool ConvertOnly { get; set; }
    public string ConflictPolicy { get; set; } = RomCleanup.Contracts.RunConstants.DefaultConflictPolicy;
    public bool Yes { get; set; }
    public string? ReportPath { get; set; }
    public string? AuditPath { get; set; }
    public string? LogPath { get; set; }
    public string LogLevel { get; set; } = "Info";
    public string? RollbackAuditPath { get; set; }
    public bool RollbackDryRun { get; set; } = true;
}
