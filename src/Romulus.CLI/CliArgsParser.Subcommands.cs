using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Index;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Paths;
using Romulus.Infrastructure.Workflow;
using Romulus.Infrastructure.Safety;
using Romulus.Infrastructure.Version;
using Romulus.Infrastructure.Watch;

namespace Romulus.CLI;

internal static partial class CliArgsParser
{
    private static CliParseResult? TryParseSubcommand(string[] args)
    {
        var first = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();

        return first switch
        {
            "analyze" => ParseSubcommandWithRoots(CliCommand.Analyze, rest),
            "simulate" => ParseSubcommandWithRoots(CliCommand.Simulate, rest),
            "explain" => ParseExplainSubcommand(rest),
            "provenance" => ParseProvenanceSubcommand(rest),
            "validate-policy" => ParseValidatePolicySubcommand(rest),
            "profiles" => ParseProfilesSubcommand(rest),
            "diff" => ParseDiffSubcommand(rest),
            "merge" => ParseMergeSubcommand(rest),
            "compare" => ParseCompareSubcommand(rest),
            "trends" => ParseTrendsSubcommand(rest),
            "workflows" => ParseWorkflowsSubcommand(rest),
            "dat" => ParseDatSubcommand(rest),
            "integrity" => ParseIntegritySubcommand(rest),
            "history" => ParseHistorySubcommand(rest),
            "watch" => ParseWatchSubcommand(rest),
            "convert" => ParseConvertSubcommand(rest),
            "header" => ParseSingleInputSubcommand(CliCommand.Header, rest),
            "junk-report" => ParseSubcommandWithRoots(CliCommand.JunkReport, rest),
            "completeness" => ParseSubcommandWithRoots(CliCommand.Completeness, rest),
            "health" => ParseHealthSubcommand(rest),
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

        var outputPathError = ValidateOptionalPath(
            opts.OutputPath,
            $"{command.ToString().ToLowerInvariant()} output path",
            allowUnc: false);
        if (outputPathError is not null)
            return CliParseResult.ValidationError([$"[Error] {outputPathError}"]);

        return CliParseResult.Subcommand(command, opts);
    }

    private static CliParseResult ParseDatSubcommand(string[] args)
    {
        if (args.Length == 0)
            return CliParseResult.ValidationError(["[Error] 'dat' requires a sub-action: diff, fixdat"]);

        var action = args[0].ToLowerInvariant();

        return action switch
        {
            "diff" => ParseDatDiffAction(args),
            "fix" or "fixdat" => ParseDatFixDatAction(args),
            _ => CliParseResult.ValidationError([$"[Error] Unknown dat action '{action}'. Available: diff, fixdat"])
        };
    }

    private static CliParseResult ParseDatDiffAction(string[] args)
    {
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

    private static CliParseResult ParseDatFixDatAction(string[] args)
    {
        var opts = new CliRunOptions();
        var errors = new List<string>();
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--roots" or "-roots":
                    if (!TryConsumeValue(args, ref i, "--roots", errors, out var rootsRaw))
                        break;
                    if (TryParseRootsArgument(rootsRaw, out var roots, out var rootsError))
                        opts.Roots = roots;
                    else
                        errors.Add($"[Error] {rootsError}");
                    break;
                case "-o" or "--output":
                    if (!TryConsumeValue(args, ref i, "--output", errors, out var outputVal))
                        break;
                    opts.OutputPath = outputVal;
                    break;
                case "--name":
                    if (!TryConsumeValue(args, ref i, "--name", errors, out var datNameVal))
                        break;
                    opts.DatName = datNameVal;
                    break;
                case "--dat-root" or "--datroot":
                    if (!TryConsumeValue(args, ref i, "--dat-root", errors, out var datRootVal))
                        break;
                    opts.DatRoot = datRootVal;
                    opts.DatRootExplicit = true;
                    break;
                default:
                    if (!args[i].StartsWith("-"))
                        opts.Roots = [.. opts.Roots, args[i]];
                    else
                        errors.Add($"[Error] Unknown flag '{args[i]}' for dat fixdat.");
                    break;
            }
        }

        if (errors.Count > 0) return CliParseResult.ValidationError(errors);

        if (opts.Roots.Length == 0)
            return CliParseResult.ValidationError(["[Error] dat fixdat requires --roots <path>."]);

        var pathError = ValidateOptionalPath(opts.OutputPath, "fixdat output path", allowUnc: false)
            ?? ValidateOptionalPath(opts.DatRoot, "DAT root", allowUnc: false);
        if (pathError is not null)
            return CliParseResult.ValidationError([$"[Error] {pathError}"]);

        return CliParseResult.Subcommand(CliCommand.DatFix, opts);
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
                case "--approve-conversion-review":
                    opts.ApproveConversionReview = true;
                    opts.ApproveConversionReviewExplicit = true;
                    break;
                case "--accept-data-loss":
                    if (!TryConsumeValue(args, ref i, "--accept-data-loss", errors, out var acceptDataLossConvertVal)) break;
                    opts.AcceptDataLossToken = acceptDataLossConvertVal;
                    opts.AcceptDataLossTokenExplicit = true;
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
                    opts.ModeExplicit = true;
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
                    var normalizedCron = cronRaw.Trim();
                    if (!CronScheduleEvaluator.TryValidateCronExpression(normalizedCron, out var cronValidationError))
                    {
                        errors.Add($"[Error] {cronValidationError ?? "Invalid cron expression."}");
                        break;
                    }

                    opts.WatchCronExpression = normalizedCron;
                    break;

                case "--approve-reviews":
                    opts.ApproveReviews = true;
                    opts.ApproveReviewsExplicit = true;
                    break;

                case "--approve-conversion-review":
                    opts.ApproveConversionReview = true;
                    opts.ApproveConversionReviewExplicit = true;
                    break;

                case "--accept-data-loss":
                    if (!TryConsumeValue(args, ref i, "--accept-data-loss", errors, out var acceptDataLossWatchVal)) break;
                    opts.AcceptDataLossToken = acceptDataLossWatchVal;
                    opts.AcceptDataLossTokenExplicit = true;
                    break;

                case "--sortconsole":
                    opts.SortConsole = true;
                    opts.SortConsoleExplicit = true;
                    break;

                case "--enabledat":
                    opts.EnableDat = true;
                    opts.EnableDatExplicit = true;
                    break;

                case "--dat-audit" or "-dataudit" or "--dataudit":
                    opts.EnableDatAudit = true;
                    opts.EnableDatAuditExplicit = true;
                    break;

                case "--datrename":
                    opts.EnableDatRename = true;
                    opts.EnableDatRenameExplicit = true;
                    break;

                case "--datroot":
                    if (!TryConsumeValue(args, ref i, "--datroot", errors, out var datRootRaw))
                        break;
                    opts.DatRoot = datRootRaw;
                    opts.DatRootExplicit = true;
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
                    opts.HashTypeExplicit = true;
                    break;

                case "--yes" or "-y":
                    opts.Yes = true;
                    break;

                case "--profile":
                    if (!TryConsumeValue(args, ref i, "--profile", errors, out var profileVal))
                        break;
                    opts.ProfileId = profileVal;
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

    private static CliParseResult ParseProfilesSubcommand(string[] args)
    {
        if (args.Length == 0)
            return CliParseResult.ValidationError(["[Error] 'profiles' requires an action: list, show, import, export, delete."]);

        var action = args[0].ToLowerInvariant();
        var opts = new CliRunOptions();
        var errors = new List<string>();

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--id":
                    if (!TryConsumeValue(args, ref i, "--id", errors, out var idVal)) break;
                    opts.ProfileId = idVal;
                    break;
                case "--input" or "-i":
                    if (!TryConsumeValue(args, ref i, "--input", errors, out var inputVal)) break;
                    opts.InputPath = inputVal;
                    break;
                case "--output" or "-o":
                    if (!TryConsumeValue(args, ref i, "--output", errors, out var outputVal)) break;
                    opts.OutputPath = outputVal;
                    break;
                default:
                    errors.Add($"[Error] Unknown flag '{args[i]}' for profiles {action}.");
                    break;
            }
        }

        if (errors.Count > 0)
            return CliParseResult.ValidationError(errors);

        var pathError = ValidateOptionalPath(opts.InputPath, "profile input path", allowUnc: false)
            ?? ValidateOptionalPath(opts.OutputPath, "profile output path", allowUnc: false);
        if (pathError is not null)
            return CliParseResult.ValidationError([$"[Error] {pathError}"]);

        return action switch
        {
            "list" => CliParseResult.Subcommand(CliCommand.ProfilesList, opts),
            "show" when !string.IsNullOrWhiteSpace(opts.ProfileId) => CliParseResult.Subcommand(CliCommand.ProfilesShow, opts),
            "import" when !string.IsNullOrWhiteSpace(opts.InputPath) => CliParseResult.Subcommand(CliCommand.ProfilesImport, opts),
            "export" when !string.IsNullOrWhiteSpace(opts.ProfileId) && !string.IsNullOrWhiteSpace(opts.OutputPath) => CliParseResult.Subcommand(CliCommand.ProfilesExport, opts),
            "delete" when !string.IsNullOrWhiteSpace(opts.ProfileId) => CliParseResult.Subcommand(CliCommand.ProfilesDelete, opts),
            "show" => CliParseResult.ValidationError(["[Error] profiles show requires --id <profile-id>."]),
            "import" => CliParseResult.ValidationError(["[Error] profiles import requires --input <path>."]),
            "export" => CliParseResult.ValidationError(["[Error] profiles export requires --id <profile-id> --output <path>."]),
            "delete" => CliParseResult.ValidationError(["[Error] profiles delete requires --id <profile-id>."]),
            _ => CliParseResult.ValidationError([$"[Error] Unknown profiles action '{action}'. Available: list, show, import, export, delete."])
        };
    }

    private static CliParseResult ParseDiffSubcommand(string[] args)
    {
        var opts = new CliRunOptions();
        var errors = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--left-roots":
                    if (!TryConsumeValue(args, ref i, "--left-roots", errors, out var leftRootsRaw)) break;
                    if (TryParseRootsArgument(leftRootsRaw, out var leftRoots, out var leftErr))
                        opts.LeftRoots = leftRoots;
                    else
                        errors.Add($"[Error] {leftErr}");
                    break;
                case "--right-roots":
                    if (!TryConsumeValue(args, ref i, "--right-roots", errors, out var rightRootsRaw)) break;
                    if (TryParseRootsArgument(rightRootsRaw, out var rightRoots, out var rightErr))
                        opts.RightRoots = rightRoots;
                    else
                        errors.Add($"[Error] {rightErr}");
                    break;
                case "--left-label":
                    if (!TryConsumeValue(args, ref i, "--left-label", errors, out var leftLabel)) break;
                    opts.LeftLabel = leftLabel;
                    break;
                case "--right-label":
                    if (!TryConsumeValue(args, ref i, "--right-label", errors, out var rightLabel)) break;
                    opts.RightLabel = rightLabel;
                    break;
                case "--extensions":
                    if (!TryConsumeValue(args, ref i, "--extensions", errors, out var extsRaw)) break;
                    opts.Extensions = new HashSet<string>(
                        extsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(static ext => ext.StartsWith('.') ? ext : "." + ext),
                        StringComparer.OrdinalIgnoreCase);
                    break;
                case "--offset":
                    if (!TryConsumeValue(args, ref i, "--offset", errors, out var offsetRaw)) break;
                    if (!int.TryParse(offsetRaw, out var parsedOffset) || parsedOffset < 0)
                    {
                        errors.Add("[Error] diff offset must be a non-negative integer.");
                        break;
                    }
                    opts.CollectionOffset = parsedOffset;
                    break;
                case "--limit":
                    if (!TryConsumeValue(args, ref i, "--limit", errors, out var limitRaw)) break;
                    if (!int.TryParse(limitRaw, out var parsedLimit) || parsedLimit < 1 || parsedLimit > 5000)
                    {
                        errors.Add("[Error] diff limit must be an integer between 1 and 5000.");
                        break;
                    }
                    opts.CollectionLimit = parsedLimit;
                    break;
                case "--output" or "-o":
                    if (!TryConsumeValue(args, ref i, "--output", errors, out var outputVal)) break;
                    opts.OutputPath = outputVal;
                    break;
                default:
                    errors.Add($"[Error] Unknown flag '{args[i]}' for diff.");
                    break;
            }
        }

        if (errors.Count > 0)
            return CliParseResult.ValidationError(errors);

        if (opts.LeftRoots.Length == 0 || opts.RightRoots.Length == 0)
            return CliParseResult.ValidationError(["[Error] diff requires --left-roots <paths> and --right-roots <paths>."]);

        var rootValidationError = ValidateCollectionRoots(opts.LeftRoots, "left-roots")
            ?? ValidateCollectionRoots(opts.RightRoots, "right-roots");
        if (rootValidationError is not null)
            return CliParseResult.ValidationError([$"[Error] {rootValidationError}"]);

        var outputPathError = ValidateOptionalPath(opts.OutputPath, "diff output path", allowUnc: false);
        if (outputPathError is not null)
            return CliParseResult.ValidationError([$"[Error] {outputPathError}"]);

        return CliParseResult.Subcommand(CliCommand.Diff, opts);
    }

    private static CliParseResult ParseMergeSubcommand(string[] args)
    {
        var opts = new CliRunOptions();
        var errors = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--left-roots":
                    if (!TryConsumeValue(args, ref i, "--left-roots", errors, out var leftRootsRaw)) break;
                    if (TryParseRootsArgument(leftRootsRaw, out var leftRoots, out var leftErr))
                        opts.LeftRoots = leftRoots;
                    else
                        errors.Add($"[Error] {leftErr}");
                    break;
                case "--right-roots":
                    if (!TryConsumeValue(args, ref i, "--right-roots", errors, out var rightRootsRaw)) break;
                    if (TryParseRootsArgument(rightRootsRaw, out var rightRoots, out var rightErr))
                        opts.RightRoots = rightRoots;
                    else
                        errors.Add($"[Error] {rightErr}");
                    break;
                case "--target-root":
                    if (!TryConsumeValue(args, ref i, "--target-root", errors, out var targetRootVal)) break;
                    opts.TargetRoot = targetRootVal;
                    break;
                case "--left-label":
                    if (!TryConsumeValue(args, ref i, "--left-label", errors, out var leftLabel)) break;
                    opts.LeftLabel = leftLabel;
                    break;
                case "--right-label":
                    if (!TryConsumeValue(args, ref i, "--right-label", errors, out var rightLabel)) break;
                    opts.RightLabel = rightLabel;
                    break;
                case "--extensions":
                    if (!TryConsumeValue(args, ref i, "--extensions", errors, out var extsRaw)) break;
                    opts.Extensions = new HashSet<string>(
                        extsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(static ext => ext.StartsWith('.') ? ext : "." + ext),
                        StringComparer.OrdinalIgnoreCase);
                    break;
                case "--offset":
                    if (!TryConsumeValue(args, ref i, "--offset", errors, out var offsetRaw)) break;
                    if (!int.TryParse(offsetRaw, out var parsedOffset) || parsedOffset < 0)
                    {
                        errors.Add("[Error] merge offset must be a non-negative integer.");
                        break;
                    }
                    opts.CollectionOffset = parsedOffset;
                    break;
                case "--limit":
                    if (!TryConsumeValue(args, ref i, "--limit", errors, out var limitRaw)) break;
                    if (!int.TryParse(limitRaw, out var parsedLimit) || parsedLimit < 1 || parsedLimit > 5000)
                    {
                        errors.Add("[Error] merge limit must be an integer between 1 and 5000.");
                        break;
                    }
                    opts.CollectionLimit = parsedLimit;
                    break;
                case "--allow-moves":
                    opts.AllowMoves = true;
                    break;
                case "--apply":
                    opts.MergeApply = true;
                    break;
                case "--plan":
                    opts.MergeApply = false;
                    break;
                case "--audit":
                    if (!TryConsumeValue(args, ref i, "--audit", errors, out var auditVal)) break;
                    opts.AuditPath = auditVal;
                    break;
                case "--output" or "-o":
                    if (!TryConsumeValue(args, ref i, "--output", errors, out var outputVal)) break;
                    opts.OutputPath = outputVal;
                    break;
                case "--yes" or "-y":
                    opts.Yes = true;
                    break;
                default:
                    errors.Add($"[Error] Unknown flag '{args[i]}' for merge.");
                    break;
            }
        }

        if (errors.Count > 0)
            return CliParseResult.ValidationError(errors);

        if (opts.LeftRoots.Length == 0 || opts.RightRoots.Length == 0 || string.IsNullOrWhiteSpace(opts.TargetRoot))
        {
            return CliParseResult.ValidationError(["[Error] merge requires --left-roots <paths> --right-roots <paths> --target-root <path>."]);
        }

        var rootValidationError = ValidateCollectionRoots(opts.LeftRoots, "left-roots")
            ?? ValidateCollectionRoots(opts.RightRoots, "right-roots")
            ?? ValidateOptionalPath(opts.TargetRoot, "target root", allowUnc: false)
            ?? ValidateOptionalPath(opts.AuditPath, "merge audit path", allowUnc: false)
            ?? ValidateOptionalPath(opts.OutputPath, "merge output path", allowUnc: false);
        if (rootValidationError is not null)
            return CliParseResult.ValidationError([$"[Error] {rootValidationError}"]);

        if (File.Exists(opts.TargetRoot))
            return CliParseResult.ValidationError([$"[Error] target root must be a directory path: {opts.TargetRoot}"]);

        return CliParseResult.Subcommand(CliCommand.Merge, opts);
    }

    private static CliParseResult ParseCompareSubcommand(string[] args)
    {
        var opts = new CliRunOptions();
        var errors = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--run":
                    if (!TryConsumeValue(args, ref i, "--run", errors, out var runVal)) break;
                    opts.RunId = runVal;
                    break;
                case "--compare-to":
                    if (!TryConsumeValue(args, ref i, "--compare-to", errors, out var compareVal)) break;
                    opts.CompareToRunId = compareVal;
                    break;
                case "--output" or "-o":
                    if (!TryConsumeValue(args, ref i, "--output", errors, out var outputVal)) break;
                    opts.OutputPath = outputVal;
                    break;
                default:
                    errors.Add($"[Error] Unknown flag '{args[i]}' for compare.");
                    break;
            }
        }

        if (errors.Count > 0)
            return CliParseResult.ValidationError(errors);

        if (string.IsNullOrWhiteSpace(opts.RunId) || string.IsNullOrWhiteSpace(opts.CompareToRunId))
            return CliParseResult.ValidationError(["[Error] compare requires --run <run-id> --compare-to <run-id>."]);

        var outputPathError = ValidateOptionalPath(opts.OutputPath, "compare output path", allowUnc: false);
        if (outputPathError is not null)
            return CliParseResult.ValidationError([$"[Error] {outputPathError}"]);

        return CliParseResult.Subcommand(CliCommand.Compare, opts);
    }

    private static string? ValidateCollectionRoots(IReadOnlyList<string> roots, string label)
    {
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
                return $"{label} contains an empty root path.";

            var pathError = ValidateOptionalPath(root, label, allowUnc: false);
            if (pathError is not null)
                return pathError;

            var fullRoot = Path.GetFullPath(root);
            if (!Directory.Exists(fullRoot))
                return $"{label} root not found: {fullRoot}";

            try
            {
                var dirInfo = new DirectoryInfo(fullRoot);
                if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                    return $"{label} root is a reparse point (symlink/junction): {fullRoot}";
            }
            catch (IOException ex)
            {
                return $"Cannot verify {label} root attributes: {fullRoot} ({ex.Message})";
            }
            catch (UnauthorizedAccessException ex)
            {
                return $"Cannot verify {label} root attributes: {fullRoot} ({ex.Message})";
            }
        }

        return null;
    }

    private static CliParseResult ParseTrendsSubcommand(string[] args)
    {
        var opts = new CliRunOptions();
        var errors = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--limit":
                    if (!TryConsumeValue(args, ref i, "--limit", errors, out var limitVal)) break;
                    if (!int.TryParse(limitVal, out var parsedLimit) || parsedLimit < 1 || parsedLimit > 3650)
                    {
                        errors.Add("[Error] trends limit must be an integer between 1 and 3650.");
                        break;
                    }
                    opts.HistoryLimit = parsedLimit;
                    break;
                case "--output" or "-o":
                    if (!TryConsumeValue(args, ref i, "--output", errors, out var outputVal)) break;
                    opts.OutputPath = outputVal;
                    break;
                default:
                    errors.Add($"[Error] Unknown flag '{args[i]}' for trends.");
                    break;
            }
        }

        if (errors.Count > 0)
            return CliParseResult.ValidationError(errors);

        var outputPathError = ValidateOptionalPath(opts.OutputPath, "trends output path", allowUnc: false);
        if (outputPathError is not null)
            return CliParseResult.ValidationError([$"[Error] {outputPathError}"]);

        return CliParseResult.Subcommand(CliCommand.Trends, opts);
    }

    private static CliParseResult ParseWorkflowsSubcommand(string[] args)
    {
        var opts = new CliRunOptions();
        var errors = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--id":
                    if (!TryConsumeValue(args, ref i, "--id", errors, out var idVal)) break;
                    opts.WorkflowScenarioId = idVal;
                    break;
                default:
                    errors.Add($"[Error] Unknown flag '{args[i]}' for workflows.");
                    break;
            }
        }

        if (errors.Count > 0)
            return CliParseResult.ValidationError(errors);

        if (!string.IsNullOrWhiteSpace(opts.WorkflowScenarioId) &&
            WorkflowScenarioCatalog.TryGet(opts.WorkflowScenarioId) is null)
        {
            return CliParseResult.ValidationError([$"[Error] Unknown workflow '{opts.WorkflowScenarioId}'."]);
        }

        return CliParseResult.Subcommand(CliCommand.Workflows, opts);
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

    private static CliParseResult ParseHealthSubcommand(string[] args)
    {
        var opts = new CliRunOptions();
        var errors = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--roots" or "-roots":
                    if (!TryConsumeValue(args, ref i, "--roots", errors, out var rootsRaw)) break;
                    opts.Roots = rootsRaw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    break;
                case "--console" or "-c":
                    if (!TryConsumeValue(args, ref i, "--console", errors, out var consoleVal)) break;
                    opts.ConsoleKey = consoleVal;
                    break;
                case "--json":
                    opts.ExportFormat = "json";
                    break;
                case "--health-snapshot":
                    opts.HealthSnapshot = true;
                    break;
                default:
                    errors.Add($"[Error] Unknown flag '{args[i]}' for health.");
                    break;
            }
        }

        if (errors.Count > 0)
            return CliParseResult.ValidationError(errors);

        if (opts.Roots.Length == 0 && string.IsNullOrWhiteSpace(opts.ConsoleKey))
            return CliParseResult.ValidationError(["[Error] health requires --roots <paths> or --console <key>."]);

        return CliParseResult.Subcommand(CliCommand.Health, opts);
    }

    private static CliParseResult ParseProvenanceSubcommand(string[] args)
    {
        var opts = new CliRunOptions();
        var errors = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--fingerprint" or "--hash" or "-f":
                    if (!TryConsumeValue(args, ref i, "--fingerprint", errors, out var fingerprint)) break;
                    opts.Fingerprint = fingerprint;
                    break;
                case "-o" or "--output":
                    if (!TryConsumeValue(args, ref i, "--output", errors, out var outVal)) break;
                    opts.OutputPath = outVal;
                    break;
                default:
                    if (!args[i].StartsWith("-"))
                        opts.Fingerprint ??= args[i];
                    else
                        errors.Add($"[Error] Unknown flag '{args[i]}' for provenance.");
                    break;
            }
        }

        if (errors.Count > 0)
            return CliParseResult.ValidationError(errors);
        if (string.IsNullOrWhiteSpace(opts.Fingerprint))
            return CliParseResult.ValidationError(["[Error] provenance requires --fingerprint <hex>"]);

        var outputPathError = ValidateOptionalPath(opts.OutputPath, "provenance output path", allowUnc: false);
        if (outputPathError is not null)
            return CliParseResult.ValidationError([$"[Error] {outputPathError}"]);

        return CliParseResult.Subcommand(CliCommand.Provenance, opts);
    }

    private static CliParseResult ParseValidatePolicySubcommand(string[] args)
    {
        var opts = new CliRunOptions();
        var errors = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--policy" or "-p":
                    if (!TryConsumeValue(args, ref i, "--policy", errors, out var policyPath)) break;
                    opts.PolicyPath = policyPath;
                    break;
                case "--roots" or "-roots":
                    if (!TryConsumeValue(args, ref i, "--roots", errors, out var rootsRaw)) break;
                    if (TryParseRootsArgument(rootsRaw, out var roots, out var rootsErr))
                        opts.Roots = roots;
                    else
                        errors.Add($"[Error] {rootsErr}");
                    break;
                case "--extensions":
                    if (!TryConsumeValue(args, ref i, "--extensions", errors, out var extsRaw)) break;
                    opts.Extensions = new HashSet<string>(
                        VersionHelper.NormalizeExtensionList(extsRaw),
                        StringComparer.OrdinalIgnoreCase);
                    opts.ExtensionsExplicit = true;
                    break;
                case "-o" or "--output":
                    if (!TryConsumeValue(args, ref i, "--output", errors, out var outputPath)) break;
                    opts.OutputPath = outputPath;
                    break;
                case "--sign" or "--sign-policy":
                    opts.SignPolicy = true;
                    break;
                default:
                    if (!args[i].StartsWith("-") && string.IsNullOrWhiteSpace(opts.PolicyPath))
                        opts.PolicyPath = args[i];
                    else
                        errors.Add($"[Error] Unknown flag '{args[i]}' for validate-policy.");
                    break;
            }
        }

        if (errors.Count > 0)
            return CliParseResult.ValidationError(errors);
        if (string.IsNullOrWhiteSpace(opts.PolicyPath))
            return CliParseResult.ValidationError(["[Error] validate-policy requires --policy <file>."]);
        if (opts.Roots.Length == 0)
            return CliParseResult.ValidationError(["[Error] validate-policy requires --roots <paths>."]);

        var pathError = ValidateOptionalPath(opts.PolicyPath, "policy file path", allowUnc: false)
            ?? ValidateOptionalPath(opts.OutputPath, "policy validation output path", allowUnc: false);
        if (pathError is not null)
            return CliParseResult.ValidationError([$"[Error] {pathError}"]);

        return CliParseResult.Subcommand(CliCommand.ValidatePolicy, opts);
    }

    /// <summary>
    /// Wave 4 — T-W4-DECISION-EXPLAINER. Parses `romulus explain --roots ...
    /// [--console-key X] [--game-key Y] [-o out.json]`. The handler runs a
    /// DryRun analysis, projects WinnerReasons through the canonical
    /// DecisionExplainerProjection and emits a JSON envelope.
    /// </summary>
    private static CliParseResult ParseExplainSubcommand(string[] args)
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
                case "--console" or "--console-key":
                    if (!TryConsumeValue(args, ref i, "--console-key", errors, out var consoleVal)) break;
                    opts.ConsoleKey = consoleVal;
                    break;
                case "--game" or "--game-key":
                    if (!TryConsumeValue(args, ref i, "--game-key", errors, out var gameVal)) break;
                    opts.GameKey = gameVal;
                    break;
                case "-o" or "--output":
                    if (!TryConsumeValue(args, ref i, "--output", errors, out var outVal)) break;
                    opts.OutputPath = outVal;
                    break;
                default:
                    if (!args[i].StartsWith("-"))
                        opts.Roots = new List<string>(opts.Roots) { args[i] }.ToArray();
                    else
                        errors.Add($"[Error] Unknown flag '{args[i]}' for explain.");
                    break;
            }
        }

        if (errors.Count > 0)
            return CliParseResult.ValidationError(errors);

        if (opts.Roots.Length == 0)
            return CliParseResult.ValidationError(["[Error] --roots is required for 'explain'."]);

        var outputPathError = ValidateOptionalPath(opts.OutputPath, "explain output path", allowUnc: false);
        if (outputPathError is not null)
            return CliParseResult.ValidationError([$"[Error] {outputPathError}"]);

        return CliParseResult.Subcommand(CliCommand.Explain, opts);
    }
}
