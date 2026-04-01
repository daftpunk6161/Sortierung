using RomCleanup.Contracts;
using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.Index;
using RomCleanup.Infrastructure.Orchestration;
using RomCleanup.Infrastructure.Paths;
using RomCleanup.Infrastructure.Safety;

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

                case "-approve-reviews" or "--approve-reviews":
                    opts.ApproveReviews = true;
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
    /// Detect and parse subcommands: analyze, export, dat, integrity, convert, header, junk-report.
    /// Returns null if args[0] is not a known subcommand.
    /// </summary>
    private static CliParseResult? TryParseSubcommand(string[] args)
    {
        var first = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();

        return first switch
        {
            "analyze" => ParseSubcommandWithRoots(CliCommand.Analyze, rest),
            "export" => ParseExportSubcommand(rest),
            "dat" => ParseDatSubcommand(rest),
            "integrity" => ParseIntegritySubcommand(rest),
            "history" => ParseHistorySubcommand(rest),
            "watch" => ParseWatchSubcommand(rest),
            "convert" => ParseConvertSubcommand(rest),
            "header" => ParseSingleInputSubcommand(CliCommand.Header, rest),
            "junk-report" => ParseSubcommandWithRoots(CliCommand.JunkReport, rest),
            "completeness" => ParseSubcommandWithRoots(CliCommand.Completeness, rest),
            _ => null
        };
    }

    private static CliParseResult ParseSubcommandWithRoots(CliCommand command, string[] args)
    {
        var opts = new CliRunOptions();
        var errors = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--roots" or "-roots":
                    if (!TryConsumeValue(args, ref i, "--roots", errors, out var rootsRaw)) break;
                    if (TryParseRootsArgument(rootsRaw, out var roots, out var rootsErr))
                        opts.Roots = roots;
                    else
                        errors.Add($"[Error] {rootsErr}");
                    break;
                case "-o" or "--output":
                    if (!TryConsumeValue(args, ref i, "--output", errors, out var outVal)) break;
                    opts.OutputPath = outVal;
                    break;
                case "--aggressive":
                    opts.AggressiveJunk = true;
                    break;
                default:
                    if (!args[i].StartsWith("-"))
                    {
                        opts.Roots = new List<string>(opts.Roots) { args[i] }.ToArray();
                    }
                    else
                    {
                        errors.Add($"[Error] Unknown flag '{args[i]}' for {command}. Use --help for usage.");
                    }
                    break;
            }
        }
        if (errors.Count > 0) return CliParseResult.ValidationError(errors);
        if (opts.Roots.Length == 0) return CliParseResult.ValidationError([$"[Error] --roots is required for '{command}'."]);
        return CliParseResult.Subcommand(command, opts);
    }

    private static CliParseResult ParseExportSubcommand(string[] args)
    {
        var opts = new CliRunOptions();
        var errors = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--roots" or "-roots":
                    if (!TryConsumeValue(args, ref i, "--roots", errors, out var rootsRaw)) break;
                    if (TryParseRootsArgument(rootsRaw, out var roots, out var rootsErr))
                        opts.Roots = roots;
                    else
                        errors.Add($"[Error] {rootsErr}");
                    break;
                case "--format" or "-f":
                    if (!TryConsumeValue(args, ref i, "--format", errors, out var fmt)) break;
                    if (fmt is not ("csv" or "json" or "excel"))
                    {
                        errors.Add($"[Error] Invalid export format '{fmt}'. Must be csv, json, or excel.");
                        break;
                    }
                    opts.ExportFormat = fmt;
                    break;
                case "-o" or "--output":
                    if (!TryConsumeValue(args, ref i, "--output", errors, out var outVal)) break;
                    opts.OutputPath = outVal;
                    break;
                default:
                    if (!args[i].StartsWith("-"))
                        opts.Roots = new List<string>(opts.Roots) { args[i] }.ToArray();
                    else
                        errors.Add($"[Error] Unknown flag '{args[i]}' for export. Use --help for usage.");
                    break;
            }
        }
        if (errors.Count > 0) return CliParseResult.ValidationError(errors);
        if (opts.Roots.Length == 0) return CliParseResult.ValidationError(["[Error] --roots is required for 'export'."]);
        return CliParseResult.Subcommand(CliCommand.Export, opts);
    }

    private static CliParseResult ParseDatSubcommand(string[] args)
    {
        if (args.Length == 0)
            return CliParseResult.ValidationError(["[Error] 'dat' requires a sub-action: diff"]);

        var action = args[0].ToLowerInvariant();
        if (action != "diff")
            return CliParseResult.ValidationError([$"[Error] Unknown dat action '{action}'. Available: diff"]);

        var opts = new CliRunOptions();
        var errors = new List<string>();
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--old":
                    if (!TryConsumeValue(args, ref i, "--old", errors, out var oldVal)) break;
                    opts.DatFileA = oldVal;
                    break;
                case "--new":
                    if (!TryConsumeValue(args, ref i, "--new", errors, out var newVal)) break;
                    opts.DatFileB = newVal;
                    break;
                default:
                    errors.Add($"[Error] Unknown flag '{args[i]}' for dat diff.");
                    break;
            }
        }
        if (errors.Count > 0) return CliParseResult.ValidationError(errors);
        if (string.IsNullOrWhiteSpace(opts.DatFileA) || string.IsNullOrWhiteSpace(opts.DatFileB))
            return CliParseResult.ValidationError(["[Error] dat diff requires --old <path> --new <path>"]);
        return CliParseResult.Subcommand(CliCommand.DatDiff, opts);
    }

    private static CliParseResult ParseIntegritySubcommand(string[] args)
    {
        if (args.Length == 0)
            return CliParseResult.ValidationError(["[Error] 'integrity' requires a sub-action: check, baseline"]);

        var action = args[0].ToLowerInvariant();
        var opts = new CliRunOptions();
        var errors = new List<string>();

        switch (action)
        {
            case "check":
                return CliParseResult.Subcommand(CliCommand.IntegrityCheck, opts);

            case "baseline":
                for (int i = 1; i < args.Length; i++)
                {
                    switch (args[i].ToLowerInvariant())
                    {
                        case "--roots" or "-roots":
                            if (!TryConsumeValue(args, ref i, "--roots", errors, out var rootsRaw)) break;
                            if (TryParseRootsArgument(rootsRaw, out var roots, out var rootsErr))
                                opts.Roots = roots;
                            else
                                errors.Add($"[Error] {rootsErr}");
                            break;
                        default:
                            if (!args[i].StartsWith("-"))
                                opts.Roots = new List<string>(opts.Roots) { args[i] }.ToArray();
                            else
                                errors.Add($"[Error] Unknown flag '{args[i]}' for integrity baseline.");
                            break;
                    }
                }
                if (errors.Count > 0) return CliParseResult.ValidationError(errors);
                if (opts.Roots.Length == 0) return CliParseResult.ValidationError(["[Error] --roots is required for integrity baseline."]);
                return CliParseResult.Subcommand(CliCommand.IntegrityBaseline, opts);

            default:
                return CliParseResult.ValidationError([$"[Error] Unknown integrity action '{action}'. Available: check, baseline"]);
        }
    }

    private static CliParseResult ParseConvertSubcommand(string[] args)
    {
        var opts = new CliRunOptions();
        var errors = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--input" or "-i":
                    if (!TryConsumeValue(args, ref i, "--input", errors, out var inputVal)) break;
                    opts.InputPath = inputVal;
                    break;
                case "--target" or "-t":
                    if (!TryConsumeValue(args, ref i, "--target", errors, out var targetVal)) break;
                    opts.TargetFormat = targetVal;
                    break;
                case "--console" or "-c":
                    if (!TryConsumeValue(args, ref i, "--console", errors, out var consoleVal)) break;
                    opts.ConsoleKey = consoleVal;
                    break;
                default:
                    if (!args[i].StartsWith("-"))
                        opts.InputPath ??= args[i];
                    else
                        errors.Add($"[Error] Unknown flag '{args[i]}' for convert.");
                    break;
            }
        }
        if (errors.Count > 0) return CliParseResult.ValidationError(errors);
        if (string.IsNullOrWhiteSpace(opts.InputPath))
            return CliParseResult.ValidationError(["[Error] convert requires --input <file|dir>"]);
        return CliParseResult.Subcommand(CliCommand.Convert, opts);
    }

    private static CliParseResult ParseHistorySubcommand(string[] args)
    {
        var opts = new CliRunOptions();
        var errors = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--limit":
                    if (!TryConsumeValue(args, ref i, "--limit", errors, out var limitValue))
                        break;

                    if (!int.TryParse(limitValue, out var parsedLimit) || parsedLimit < 1 || parsedLimit > CollectionRunHistoryPageBuilder.MaxLimit)
                    {
                        errors.Add($"[Error] Invalid history limit '{limitValue}'. Must be an integer between 1 and {CollectionRunHistoryPageBuilder.MaxLimit}.");
                        break;
                    }

                    opts.HistoryLimit = parsedLimit;
                    break;

                case "--offset":
                    if (!TryConsumeValue(args, ref i, "--offset", errors, out var offsetValue))
                        break;

                    if (!int.TryParse(offsetValue, out var parsedOffset) || parsedOffset < 0)
                    {
                        errors.Add($"[Error] Invalid history offset '{offsetValue}'. Must be a non-negative integer.");
                        break;
                    }

                    opts.HistoryOffset = parsedOffset;
                    break;

                case "-o" or "--output":
                    if (!TryConsumeValue(args, ref i, "--output", errors, out var outputValue))
                        break;

                    opts.OutputPath = outputValue;
                    break;

                default:
                    errors.Add($"[Error] Unknown flag '{args[i]}' for history. Use --help for usage.");
                    break;
            }
        }

        if (errors.Count > 0)
            return CliParseResult.ValidationError(errors);

        var outputPathError = ValidateOptionalPath(opts.OutputPath, "history output path", allowUnc: false);
        if (outputPathError is not null)
            return CliParseResult.ValidationError([$"[Error] {outputPathError}"]);

        return CliParseResult.Subcommand(CliCommand.History, opts);
    }

    private static CliParseResult ParseWatchSubcommand(string[] args)
    {
        var opts = new CliRunOptions
        {
            Mode = RunConstants.ModeDryRun
        };
        var errors = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--roots" or "-roots":
                    if (!TryConsumeValue(args, ref i, "--roots", errors, out var rootsRaw))
                        break;
                    if (TryParseRootsArgument(rootsRaw, out var roots, out var rootsErr))
                        opts.Roots = roots;
                    else
                        errors.Add($"[Error] {rootsErr}");
                    break;

                case "--mode":
                    if (!TryConsumeValue(args, ref i, "--mode", errors, out var modeRaw))
                        break;
                    if (!string.Equals(modeRaw, RunConstants.ModeDryRun, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(modeRaw, RunConstants.ModeMove, StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add($"[Error] Invalid mode '{modeRaw}'. Must be DryRun or Move.");
                        break;
                    }

                    opts.Mode = string.Equals(modeRaw, RunConstants.ModeMove, StringComparison.OrdinalIgnoreCase)
                        ? RunConstants.ModeMove
                        : RunConstants.ModeDryRun;
                    break;

                case "--debounce":
                    if (!TryConsumeValue(args, ref i, "--debounce", errors, out var debounceRaw))
                        break;
                    if (!int.TryParse(debounceRaw, out var debounceSeconds) || debounceSeconds < 1 || debounceSeconds > 300)
                    {
                        errors.Add("[Error] debounce must be an integer between 1 and 300.");
                        break;
                    }

                    opts.WatchDebounceSeconds = debounceSeconds;
                    break;

                case "--interval":
                    if (!TryConsumeValue(args, ref i, "--interval", errors, out var intervalRaw))
                        break;
                    if (!int.TryParse(intervalRaw, out var intervalMinutes) || intervalMinutes < 1 || intervalMinutes > 10080)
                    {
                        errors.Add("[Error] interval must be an integer between 1 and 10080.");
                        break;
                    }

                    opts.WatchIntervalMinutes = intervalMinutes;
                    break;

                case "--cron":
                    if (!TryConsumeValue(args, ref i, "--cron", errors, out var cronRaw))
                        break;
                    var cronFields = cronRaw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (cronFields.Length != 5)
                    {
                        errors.Add("[Error] cron must contain exactly five fields.");
                        break;
                    }

                    opts.WatchCronExpression = cronRaw;
                    break;

                case "--approve-reviews":
                    opts.ApproveReviews = true;
                    break;

                case "--sortconsole":
                    opts.SortConsole = true;
                    break;

                case "--enabledat":
                    opts.EnableDat = true;
                    break;

                case "--dat-audit" or "-dataudit" or "--dataudit":
                    opts.EnableDatAudit = true;
                    break;

                case "--datrename":
                    opts.EnableDatRename = true;
                    break;

                case "--datroot":
                    if (!TryConsumeValue(args, ref i, "--datroot", errors, out var datRootRaw))
                        break;
                    opts.DatRoot = datRootRaw;
                    break;

                case "--hashtype":
                    if (!TryConsumeValue(args, ref i, "--hashtype", errors, out var hashTypeRaw))
                        break;
                    if (!AllowedHashTypes.Contains(hashTypeRaw))
                    {
                        errors.Add($"[Error] Invalid hash type '{hashTypeRaw}'. Must be SHA1, SHA256, or MD5.");
                        break;
                    }

                    opts.HashType = hashTypeRaw;
                    break;

                case "--yes" or "-y":
                    opts.Yes = true;
                    break;

                default:
                    if (!args[i].StartsWith("-"))
                    {
                        opts.Roots = new List<string>(opts.Roots) { args[i] }.ToArray();
                    }
                    else
                    {
                        errors.Add($"[Error] Unknown flag '{args[i]}' for watch. Use --help for usage.");
                    }
                    break;
            }
        }

        if (errors.Count > 0)
            return CliParseResult.ValidationError(errors);

        if (opts.Roots.Length == 0)
            return CliParseResult.ValidationError(["[Error] --roots is required for 'watch'."]);

        if (opts.WatchIntervalMinutes is null && string.IsNullOrWhiteSpace(opts.WatchCronExpression))
            return CliParseResult.ValidationError(["[Error] watch requires --interval <minutes> or --cron <expr>."]);

        return CliParseResult.Subcommand(CliCommand.Watch, opts);
    }

    private static CliParseResult ParseSingleInputSubcommand(CliCommand command, string[] args)
    {
        var opts = new CliRunOptions();
        var errors = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--input" or "-i":
                    if (!TryConsumeValue(args, ref i, "--input", errors, out var inputVal)) break;
                    opts.InputPath = inputVal;
                    break;
                default:
                    if (!args[i].StartsWith("-"))
                        opts.InputPath ??= args[i];
                    else
                        errors.Add($"[Error] Unknown flag '{args[i]}' for {command}.");
                    break;
            }
        }
        if (errors.Count > 0) return CliParseResult.ValidationError(errors);
        if (string.IsNullOrWhiteSpace(opts.InputPath))
            return CliParseResult.ValidationError([$"[Error] {command} requires --input <path>"]);
        return CliParseResult.Subcommand(command, opts);
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
        => RomCleanup.Infrastructure.Safety.SafetyValidator.IsProtectedSystemPath(fullPath);
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

internal enum CliCommand { Run, Help, Version, Rollback, UpdateDats, Analyze, Export, DatDiff, IntegrityCheck, IntegrityBaseline, History, Watch, Convert, Header, JunkReport, Completeness }

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
    public bool ApproveReviews { get; set; }
    public string ConflictPolicy { get; set; } = RomCleanup.Contracts.RunConstants.DefaultConflictPolicy;
    public bool Yes { get; set; }
    public string? ReportPath { get; set; }
    public string? AuditPath { get; set; }
    public string? LogPath { get; set; }
    public string LogLevel { get; set; } = "Info";
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
    public string? ConsoleKey { get; set; }
    public string? TargetFormat { get; set; }
    public string? DatFileA { get; set; }
    public string? DatFileB { get; set; }
    public int? HistoryLimit { get; set; }
    public int HistoryOffset { get; set; }
    public int WatchDebounceSeconds { get; set; } = 5;
    public int? WatchIntervalMinutes { get; set; }
    public string? WatchCronExpression { get; set; }
}
