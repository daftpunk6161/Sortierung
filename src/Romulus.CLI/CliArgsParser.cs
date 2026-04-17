using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Index;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Paths;
using Romulus.Infrastructure.Workflow;
using Romulus.Infrastructure.Safety;
using Romulus.Infrastructure.Version;

namespace Romulus.CLI;

/// <summary>
/// Pure argument parser: string[] → CliParseResult.
/// No side effects, no Console.Write, no File.Exists.
/// ADR-008 §C-01, §C-05.
/// </summary>
internal static partial class CliArgsParser
{
    private static readonly HashSet<string> AllowedHashTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SHA1", "SHA256", "MD5"
    };

    private static readonly HashSet<string> AllowedLogLevels = new(StringComparer.OrdinalIgnoreCase)
    {
        "Debug", "Info", "Warning", "Error"
    };

    private static readonly IReadOnlySet<string> AllowedConflictPolicies = Romulus.Contracts.RunConstants.ValidConflictPolicies;
    private static readonly IReadOnlySet<string> AllowedConvertFormats = Romulus.Contracts.RunConstants.ValidConvertFormats;

    internal static CliParseResult Parse(string[] args)
    {
        if (args.Length == 0)
            return CliParseResult.Help();

        // Subcommand detection: first non-flag argument may be a subcommand
        var subcommandResult = TryParseSubcommand(args);
        if (subcommandResult is not null)
            return subcommandResult;

        var opts = new CliRunOptions();
        var errors = new List<string>();
        var rootsSpecified = false;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg.ToLowerInvariant())
            {
                case "-roots" or "--roots":
                    if (rootsSpecified)
                    {
                        if (TryConsumeValue(args, ref i, "--roots", errors, out _))
                        {
                            errors.Add("[Error] Duplicate --roots is not allowed. Provide all roots in a single --roots value separated by ';'.");
                        }
                        break;
                    }

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
                    opts.ModeExplicit = true;
                    break;

                case "-prefer" or "--prefer" or "-preferregions":
                    if (!TryConsumeValue(args, ref i, "--prefer", errors, out var regionsRaw))
                        break;
                    opts.PreferRegions = regionsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    opts.PreferRegionsExplicit = true;
                    break;

                case "--profile":
                    if (!TryConsumeValue(args, ref i, "--profile", errors, out var profileVal))
                        break;
                    opts.ProfileId = profileVal;
                    opts.ProfileIdExplicit = true;
                    break;

                case "--profile-file":
                    if (!TryConsumeValue(args, ref i, "--profile-file", errors, out var profileFileVal))
                        break;
                    opts.ProfileFilePath = profileFileVal;
                    break;

                case "--workflow":
                    if (!TryConsumeValue(args, ref i, "--workflow", errors, out var workflowVal))
                        break;
                    opts.WorkflowScenarioId = workflowVal;
                    opts.WorkflowScenarioExplicit = true;
                    break;

                case "-extensions" or "--extensions":
                    if (!TryConsumeValue(args, ref i, "--extensions", errors, out var extsRaw))
                        break;
                    var exts = VersionHelper.NormalizeExtensionList(extsRaw);
                    opts.Extensions = new HashSet<string>(
                        exts,
                        StringComparer.OrdinalIgnoreCase);
                    opts.ExtensionsExplicit = true;
                    break;

                case "-trashroot" or "--trashroot":
                    if (!TryConsumeValue(args, ref i, "--trashroot", errors, out var trashVal))
                        break;
                    opts.TrashRoot = trashVal;
                    opts.TrashRootExplicit = true;
                    break;

                case "-removejunk" or "--removejunk":
                    opts.RemoveJunk = true;
                    opts.RemoveJunkExplicit = true;
                    break;

                case "-no-removejunk" or "--no-removejunk":
                    opts.RemoveJunk = false;
                    opts.RemoveJunkExplicit = true;
                    break;

                case "-convertonly" or "--convertonly":
                    opts.ConvertOnly = true;
                    opts.ConvertFormat ??= RunConstants.ConvertFormatAuto;
                    opts.ConvertOnlyExplicit = true;
                    opts.ConvertFormatExplicit = true;
                    break;

                case "-approve-reviews" or "--approve-reviews":
                    opts.ApproveReviews = true;
                    opts.ApproveReviewsExplicit = true;
                    break;

                case "--approve-conversion-review":
                    opts.ApproveConversionReview = true;
                    opts.ApproveConversionReviewExplicit = true;
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
                    opts.ConflictPolicyExplicit = true;
                    break;

                case "-gamesonly" or "--gamesonly":
                    opts.OnlyGames = true;
                    opts.OnlyGamesExplicit = true;
                    break;

                case "-keepunknown" or "--keepunknown":
                    opts.KeepUnknownWhenOnlyGames = true;
                    opts.KeepUnknownExplicit = true;
                    break;

                case "-dropunknown" or "--dropunknown":
                    opts.KeepUnknownWhenOnlyGames = false;
                    opts.KeepUnknownExplicit = true;
                    break;

                case "-aggressivejunk" or "--aggressivejunk":
                    opts.AggressiveJunk = true;
                    opts.AggressiveJunkExplicit = true;
                    break;

                case "-sortconsole" or "--sortconsole":
                    opts.SortConsole = true;
                    opts.SortConsoleExplicit = true;
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
                    opts.EnableDatExplicit = true;
                    break;

                case "-dat-audit" or "--dat-audit" or "-dataudit" or "--dataudit":
                    opts.EnableDatAudit = true;
                    opts.EnableDatAuditExplicit = true;
                    break;

                case "-datrename" or "--datrename":
                    opts.EnableDatRename = true;
                    opts.EnableDatRenameExplicit = true;
                    break;

                case "-datroot" or "--datroot":
                    if (!TryConsumeValue(args, ref i, "--datroot", errors, out var datRootVal))
                        break;
                    opts.DatRoot = datRootVal;
                    opts.DatRootExplicit = true;
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
                    opts.HashTypeExplicit = true;
                    break;

                case "-update-dats" or "--update-dats":
                    opts.UpdateDats = true;
                    break;

                case "-import-packs-from" or "--import-packs-from":
                    if (!TryConsumeValue(args, ref i, "--import-packs-from", errors, out var importPacksFromVal))
                        break;
                    opts.ImportPacksFrom = importPacksFromVal;
                    break;

                case "-force-dat-update" or "--force-dat-update":
                    opts.ForceDatUpdate = true;
                    break;

                case "-smart-dat-update" or "--smart-dat-update":
                    opts.SmartDatUpdate = true;
                    break;

                case "-dat-stale-days" or "--dat-stale-days":
                    if (!TryConsumeValue(args, ref i, "--dat-stale-days", errors, out var staleDaysVal))
                        break;

                    if (!int.TryParse(staleDaysVal, out var staleDays) || staleDays <= 0 || staleDays > 3650)
                    {
                        errors.Add($"[Error] Invalid DAT stale threshold '{staleDaysVal}'. Must be an integer between 1 and 3650.");
                        break;
                    }

                    opts.DatStaleDays = staleDays;
                    break;

                case "-convertformat" or "--convertformat":
                {
                    var convertFormatValue = RunConstants.ConvertFormatAuto;
                    if (TryConsumeOptionalValue(args, ref i, out var explicitConvertFormat))
                        convertFormatValue = explicitConvertFormat;

                    var normalizedConvertFormat = NormalizeConvertFormatValue(convertFormatValue);
                    if (normalizedConvertFormat is null || !AllowedConvertFormats.Contains(normalizedConvertFormat))
                    {
                        errors.Add($"[Error] Invalid convert format '{convertFormatValue}'. Must be auto, chd, rvz, zip, or 7z.");
                        break;
                    }

                    opts.ConvertFormat = normalizedConvertFormat;
                    opts.ConvertFormatExplicit = true;
                    break;
                }

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

        if (opts.UpdateDats)
        {
            var datRootError = ValidateOptionalPath(opts.DatRoot, "DAT root", allowUnc: false);
            if (datRootError is not null)
                return CliParseResult.ValidationError([$"[Error] {datRootError}"]);

            if (!string.IsNullOrWhiteSpace(opts.ImportPacksFrom))
            {
                var importRootError = ValidateOptionalPath(opts.ImportPacksFrom, "Import-Packs root", allowUnc: false);
                if (importRootError is not null)
                    return CliParseResult.ValidationError([$"[Error] {importRootError}"]);

                if (!Directory.Exists(opts.ImportPacksFrom))
                    return CliParseResult.ValidationError([$"[Error] Import-Packs directory not found: {opts.ImportPacksFrom}"]);
            }

            if (opts.DatStaleDays is <= 0)
                return CliParseResult.ValidationError(["[Error] DAT stale threshold must be greater than zero."]);

            return CliParseResult.UpdateDats(opts);
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

            // SEC-CLI-03: Block drive roots (C:\, D:\, etc.) — parity with API
            if (SafetyValidator.IsDriveRoot(fullRoot))
                return CliParseResult.ValidationError([$"[Error] Drive root not allowed as root directory: {fullRoot}"]);

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
            ?? ValidateOptionalPath(opts.AuditPath, "audit path", allowUnc: false)
            ?? ValidateOptionalPath(opts.ProfileFilePath, "profile file path", allowUnc: false);
        if (protectedPathError is not null)
            return CliParseResult.ValidationError([$"[Error] {protectedPathError}"]);

        if (!opts.OnlyGames && !opts.KeepUnknownWhenOnlyGames)
            return CliParseResult.ValidationError(["[Error] --dropunknown requires --gamesonly."]);

        return CliParseResult.Run(opts);
    }

    /// <summary>
    /// Detect and parse subcommands: analyze, export, dat, integrity, convert, header, junk-report.
    /// Returns null if args[0] is not a known subcommand.
    /// </summary>

    private static string? NormalizeConvertFormatValue(string? convertFormat)
        => string.IsNullOrWhiteSpace(convertFormat)
            ? null
            : convertFormat.Trim().ToLowerInvariant();

    private static bool TryConsumeOptionalValue(string[] args, ref int index, out string value)
    {
        value = "";
        var candidateIndex = index + 1;
        if (candidateIndex >= args.Length)
            return false;

        var candidate = args[candidateIndex];
        if (IsFlagLikeArgument(candidate))
            return false;

        index = candidateIndex;
        value = candidate;
        return true;
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
        if (IsFlagLikeArgument(candidate))
        {
            // Value looks like a flag — put back so it's parsed next iteration.
            // No error: the flag was just used without its optional value.
            index--;
            return false;
        }

        value = candidate;
        return true;
    }

    private static bool IsFlagLikeArgument(string candidate)
        => candidate.StartsWith('-') && !candidate.StartsWith("-.") && !candidate.StartsWith("-/");

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
        => Romulus.Infrastructure.Safety.SafetyValidator.IsProtectedSystemPath(fullPath);
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

    public static CliParseResult UpdateDats(CliRunOptions options) =>
        new() { Command = CliCommand.UpdateDats, ExitCode = 0, Options = options };

    public static CliParseResult Subcommand(CliCommand command, CliRunOptions options) =>
        new() { Command = command, ExitCode = 0, Options = options };

    public static CliParseResult ValidationError(IReadOnlyList<string> errors) =>
        new() { Command = CliCommand.Run, ExitCode = 3, Errors = errors };

    public static CliParseResult Run(CliRunOptions options) =>
        new() { Command = CliCommand.Run, ExitCode = 0, Options = options };
}

internal enum CliCommand
{
    Run,
    Help,
    Version,
    Rollback,
    UpdateDats,
    Analyze,
    Export,
    ProfilesList,
    ProfilesShow,
    ProfilesImport,
    ProfilesExport,
    ProfilesDelete,
    Workflows,
    Diff,
    Merge,
    Compare,
    Trends,
    DatDiff,
    DatFix,
    IntegrityCheck,
    IntegrityBaseline,
    History,
    Watch,
    Convert,
    Header,
    JunkReport,
    Completeness,
    Enrich,
    Health
}

/// <summary>
/// Raw parsed CLI options — before settings merge.
/// </summary>
internal sealed class CliRunOptions
{
    public string[] Roots { get; set; } = Array.Empty<string>();
    public string Mode { get; set; } = "DryRun";
    public bool ModeExplicit { get; set; }
    public string? WorkflowScenarioId { get; set; }
    public bool WorkflowScenarioExplicit { get; set; }
    public string? ProfileId { get; set; }
    public bool ProfileIdExplicit { get; set; }
    public string? ProfileFilePath { get; set; }
    public string[] PreferRegions { get; set; } = Array.Empty<string>();
    public bool PreferRegionsExplicit { get; set; }
    public HashSet<string> Extensions { get; set; } = new(RunOptions.DefaultExtensions, StringComparer.OrdinalIgnoreCase);
    public bool ExtensionsExplicit { get; set; }
    public string? TrashRoot { get; set; }
    public bool TrashRootExplicit { get; set; }
    public bool RemoveJunk { get; set; } = true;
    public bool RemoveJunkExplicit { get; set; }
    public bool OnlyGames { get; set; }
    public bool OnlyGamesExplicit { get; set; }
    public bool KeepUnknownWhenOnlyGames { get; set; } = true;
    public bool KeepUnknownExplicit { get; set; }
    public bool AggressiveJunk { get; set; }
    public bool AggressiveJunkExplicit { get; set; }
    public bool SortConsole { get; set; }
    public bool SortConsoleExplicit { get; set; }
    public bool EnableDat { get; set; }
    public bool EnableDatExplicit { get; set; }
    public bool EnableDatAudit { get; set; }
    public bool EnableDatAuditExplicit { get; set; }
    public bool EnableDatRename { get; set; }
    public bool EnableDatRenameExplicit { get; set; }
    public string? DatRoot { get; set; }
    public bool DatRootExplicit { get; set; }
    public string? HashType { get; set; }
    public bool HashTypeExplicit { get; set; }
    public string? ConvertFormat { get; set; }
    public bool ConvertFormatExplicit { get; set; }
    public bool ConvertOnly { get; set; }
    public bool ConvertOnlyExplicit { get; set; }
    public bool ApproveReviews { get; set; }
    public bool ApproveReviewsExplicit { get; set; }
    public bool ApproveConversionReview { get; set; }
    public bool ApproveConversionReviewExplicit { get; set; }
    public string ConflictPolicy { get; set; } = Romulus.Contracts.RunConstants.DefaultConflictPolicy;
    public bool ConflictPolicyExplicit { get; set; }
    public bool Yes { get; set; }
    public string? ReportPath { get; set; }
    public string? AuditPath { get; set; }
    public string? LogPath { get; set; }
    public string LogLevel { get; set; } = "Info";
    public bool LogLevelExplicit { get; set; }
    public string? RollbackAuditPath { get; set; }
    public bool RollbackDryRun { get; set; } = true;
    public bool UpdateDats { get; set; }
    public string? ImportPacksFrom { get; set; }
    public bool ForceDatUpdate { get; set; }
    public bool SmartDatUpdate { get; set; }
    public int? DatStaleDays { get; set; }

    // Subcommand-specific options
    public string? InputPath { get; set; }
    public string? OutputPath { get; set; }
    public string? ExportFormat { get; set; }
    public string? CollectionName { get; set; }
    public string? ConsoleKey { get; set; }
    public string? TargetFormat { get; set; }
    public string? DatFileA { get; set; }
    public string? DatFileB { get; set; }
    public string? DatName { get; set; }
    public string[] LeftRoots { get; set; } = Array.Empty<string>();
    public string[] RightRoots { get; set; } = Array.Empty<string>();
    public string? LeftLabel { get; set; }
    public string? RightLabel { get; set; }
    public string? TargetRoot { get; set; }
    public bool AllowMoves { get; set; }
    public bool MergeApply { get; set; }
    public int CollectionOffset { get; set; }
    public int? CollectionLimit { get; set; }
    public string? RunId { get; set; }
    public string? CompareToRunId { get; set; }
    public int? HistoryLimit { get; set; }
    public int HistoryOffset { get; set; }
    public int WatchDebounceSeconds { get; set; } = 5;
    public int? WatchIntervalMinutes { get; set; }
    public string? WatchCronExpression { get; set; }
}
