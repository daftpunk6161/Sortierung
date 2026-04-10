using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Paths;
using Romulus.Infrastructure.Profiles;

namespace Romulus.CLI;

/// <summary>
/// Maps CliRunOptions + RomulusSettings to normalized RunOptions.
/// Profile/workflow overlays are resolved before settings fallback so explicit CLI input keeps priority.
/// </summary>
internal static class CliOptionsMapper
{
    private static readonly HashSet<string> DefaultExtensions =
    [
        ..RunOptions.DefaultExtensions
    ];

    internal static (RunOptions? runOptions, IReadOnlyList<string>? errors) Map(
        CliRunOptions cli,
        RomulusSettings settings,
        string? dataDir = null)
    {
        try
        {
            EnsureLegacyExplicitness(cli);

            var resolvedDataDir = dataDir ?? RunEnvironmentBuilder.ResolveDataDir();
            var profileService = new RunProfileService(new JsonRunProfileStore(), resolvedDataDir);
            var materializer = new RunConfigurationMaterializer(
                new RunConfigurationResolver(profileService),
                new RunOptionsFactory());
            var materialized = materializer.MaterializeAsync(
                CreateDraft(cli),
                CreateExplicitness(cli),
                settings,
                profileFilePath: cli.ProfileFilePath).GetAwaiter().GetResult();

            cli.ProfileId = materialized.EffectiveProfileId;
            cli.WorkflowScenarioId = materialized.Workflow?.Id;

            var auditPath = cli.AuditPath;
            if (string.IsNullOrEmpty(auditPath) &&
                string.Equals(materialized.Options.Mode, RunConstants.ModeMove, StringComparison.OrdinalIgnoreCase))
            {
                var auditDir = ArtifactPathResolver.GetArtifactDirectory(cli.Roots, AppIdentity.ArtifactDirectories.AuditLogs);
                auditPath = Path.Combine(Path.GetFullPath(auditDir),
                    $"audit-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.csv");
            }

            var runOptions = (auditPath == materialized.Options.AuditPath && cli.ReportPath == materialized.Options.ReportPath)
                ? materialized.Options
                : materializer.MaterializeAsync(
                    CreateDraft(cli),
                    CreateExplicitness(cli),
                    settings,
                    profileFilePath: cli.ProfileFilePath,
                    auditPath: auditPath,
                    reportPath: cli.ReportPath).GetAwaiter().GetResult().Options;

            return (runOptions, null);
        }
        catch (InvalidOperationException ex)
        {
            return (null, [$"[Error] {ex.Message}"]);
        }
    }

    private static RunConfigurationDraft CreateDraft(CliRunOptions cli)
    {
        return new RunConfigurationDraft
        {
            Roots = cli.Roots,
            Mode = cli.ModeExplicit ? cli.Mode : null,
            WorkflowScenarioId = cli.WorkflowScenarioId,
            ProfileId = cli.ProfileId,
            PreferRegions = cli.PreferRegionsExplicit ? cli.PreferRegions : null,
            Extensions = cli.ExtensionsExplicit ? cli.Extensions.OrderBy(static ext => ext, StringComparer.OrdinalIgnoreCase).ToArray() : null,
            RemoveJunk = cli.RemoveJunkExplicit ? cli.RemoveJunk : null,
            OnlyGames = cli.OnlyGamesExplicit ? cli.OnlyGames : null,
            KeepUnknownWhenOnlyGames = cli.KeepUnknownExplicit ? cli.KeepUnknownWhenOnlyGames : null,
            AggressiveJunk = cli.AggressiveJunkExplicit ? cli.AggressiveJunk : null,
            SortConsole = cli.SortConsoleExplicit ? cli.SortConsole : null,
            EnableDat = cli.EnableDatExplicit ? cli.EnableDat : null,
            EnableDatAudit = cli.EnableDatAuditExplicit ? cli.EnableDatAudit : null,
            EnableDatRename = cli.EnableDatRenameExplicit ? cli.EnableDatRename : null,
            DatRoot = cli.DatRootExplicit ? cli.DatRoot : null,
            HashType = cli.HashTypeExplicit ? cli.HashType : null,
            ConvertFormat = cli.ConvertFormatExplicit ? NormalizeConvertFormat(cli.ConvertFormat) : null,
            ConvertOnly = cli.ConvertOnlyExplicit ? cli.ConvertOnly : null,
            ApproveReviews = cli.ApproveReviewsExplicit ? cli.ApproveReviews : null,
            ApproveConversionReview = cli.ApproveConversionReviewExplicit ? cli.ApproveConversionReview : null,
            ConflictPolicy = cli.ConflictPolicyExplicit ? cli.ConflictPolicy : null,
            TrashRoot = cli.TrashRootExplicit ? cli.TrashRoot : null
        };
    }

    private static RunConfigurationExplicitness CreateExplicitness(CliRunOptions cli)
    {
        return new RunConfigurationExplicitness
        {
            Mode = cli.ModeExplicit,
            PreferRegions = cli.PreferRegionsExplicit,
            Extensions = cli.ExtensionsExplicit,
            RemoveJunk = cli.RemoveJunkExplicit,
            OnlyGames = cli.OnlyGamesExplicit,
            KeepUnknownWhenOnlyGames = cli.KeepUnknownExplicit,
            AggressiveJunk = cli.AggressiveJunkExplicit,
            SortConsole = cli.SortConsoleExplicit,
            EnableDat = cli.EnableDatExplicit,
            EnableDatAudit = cli.EnableDatAuditExplicit,
            EnableDatRename = cli.EnableDatRenameExplicit,
            DatRoot = cli.DatRootExplicit,
            HashType = cli.HashTypeExplicit,
            ConvertFormat = cli.ConvertFormatExplicit,
            ConvertOnly = cli.ConvertOnlyExplicit,
            ApproveReviews = cli.ApproveReviewsExplicit,
            ApproveConversionReview = cli.ApproveConversionReviewExplicit,
            ConflictPolicy = cli.ConflictPolicyExplicit,
            TrashRoot = cli.TrashRootExplicit
        };
    }

    private static string? NormalizeConvertFormat(string? convertFormat)
    {
        if (string.IsNullOrWhiteSpace(convertFormat))
            return null;

        var normalized = convertFormat.Trim().ToLowerInvariant();
        if (!RunConstants.ValidConvertFormats.Contains(normalized))
        {
            throw new InvalidOperationException(
                $"Invalid convertFormat '{convertFormat}'. Must be one of: {string.Join(", ", RunConstants.ValidConvertFormats.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase))}.");
        }

        return normalized;
    }

    private static void EnsureLegacyExplicitness(CliRunOptions cli)
    {
        ArgumentNullException.ThrowIfNull(cli);

        // Backward-compatibility: many tests and internal callers construct CliRunOptions directly
        // without setting explicitness flags. Infer explicitness from non-default values.
        if (!cli.ModeExplicit &&
            !string.IsNullOrWhiteSpace(cli.Mode) &&
            string.Equals(cli.Mode, RunConstants.ModeMove, StringComparison.OrdinalIgnoreCase))
        {
            cli.ModeExplicit = true;
        }

        if (!cli.PreferRegionsExplicit && cli.PreferRegions.Length > 0)
            cli.PreferRegionsExplicit = true;

        if (!cli.ExtensionsExplicit && !cli.Extensions.SetEquals(DefaultExtensions))
            cli.ExtensionsExplicit = true;

        if (!cli.RemoveJunkExplicit && !cli.RemoveJunk)
            cli.RemoveJunkExplicit = true;

        if (!cli.OnlyGamesExplicit && cli.OnlyGames)
            cli.OnlyGamesExplicit = true;

        if (!cli.KeepUnknownExplicit && !cli.KeepUnknownWhenOnlyGames)
            cli.KeepUnknownExplicit = true;

        if (!cli.AggressiveJunkExplicit && cli.AggressiveJunk)
            cli.AggressiveJunkExplicit = true;

        if (!cli.SortConsoleExplicit && cli.SortConsole)
            cli.SortConsoleExplicit = true;

        if (!cli.EnableDatExplicit && cli.EnableDat)
            cli.EnableDatExplicit = true;

        if (!cli.EnableDatAuditExplicit && cli.EnableDatAudit)
            cli.EnableDatAuditExplicit = true;

        if (!cli.EnableDatRenameExplicit && cli.EnableDatRename)
            cli.EnableDatRenameExplicit = true;

        if (!cli.DatRootExplicit && !string.IsNullOrWhiteSpace(cli.DatRoot))
            cli.DatRootExplicit = true;

        if (!cli.HashTypeExplicit && !string.IsNullOrWhiteSpace(cli.HashType))
            cli.HashTypeExplicit = true;

        if (!cli.ConvertFormatExplicit && !string.IsNullOrWhiteSpace(cli.ConvertFormat))
            cli.ConvertFormatExplicit = true;

        if (cli.ConvertOnly && string.IsNullOrWhiteSpace(cli.ConvertFormat))
        {
            cli.ConvertFormat = RunConstants.ConvertFormatAuto;
            cli.ConvertFormatExplicit = true;
        }

        if (!cli.ConvertOnlyExplicit && cli.ConvertOnly)
            cli.ConvertOnlyExplicit = true;

        if (!cli.ApproveReviewsExplicit && cli.ApproveReviews)
            cli.ApproveReviewsExplicit = true;

        if (!cli.ApproveConversionReviewExplicit && cli.ApproveConversionReview)
            cli.ApproveConversionReviewExplicit = true;

        if (!cli.ConflictPolicyExplicit &&
            !string.Equals(cli.ConflictPolicy, RunConstants.DefaultConflictPolicy, StringComparison.OrdinalIgnoreCase))
        {
            cli.ConflictPolicyExplicit = true;
        }

        if (!cli.TrashRootExplicit && !string.IsNullOrWhiteSpace(cli.TrashRoot))
            cli.TrashRootExplicit = true;
    }

}
