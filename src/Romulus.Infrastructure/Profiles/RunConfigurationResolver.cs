using Romulus.Contracts.Models;
using Romulus.Infrastructure.Workflow;

namespace Romulus.Infrastructure.Profiles;

public sealed class RunConfigurationResolver
{
    private readonly RunProfileService _profileService;

    public RunConfigurationResolver(RunProfileService profileService)
    {
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
    }

    public async ValueTask<ResolvedRunConfiguration> ResolveAsync(
        RunConfigurationDraft draft,
        RunConfigurationExplicitness explicitness,
        string? profileFilePath = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(explicitness);

        var workflow = ResolveWorkflow(draft.WorkflowScenarioId);
        var profile = await ResolveProfileAsync(draft.ProfileId, workflow, profileFilePath, ct).ConfigureAwait(false);
        var profileSettings = profile?.Settings ?? new RunProfileSettings();
        var workflowSettings = workflow?.Settings ?? new RunProfileSettings();

        var resolvedDraft = draft with
        {
            Mode = ResolveValue(draft.Mode, explicitness.Mode, profileSettings.Mode, workflowSettings.Mode),
            ProfileId = profile?.Id,
            WorkflowScenarioId = workflow?.Id,
            PreferRegions = ResolveValue(draft.PreferRegions, explicitness.PreferRegions, profileSettings.PreferRegions, workflowSettings.PreferRegions),
            Extensions = ResolveValue(draft.Extensions, explicitness.Extensions, profileSettings.Extensions, workflowSettings.Extensions),
            RemoveJunk = ResolveValue(draft.RemoveJunk, explicitness.RemoveJunk, profileSettings.RemoveJunk, workflowSettings.RemoveJunk),
            OnlyGames = ResolveValue(draft.OnlyGames, explicitness.OnlyGames, profileSettings.OnlyGames, workflowSettings.OnlyGames),
            KeepUnknownWhenOnlyGames = ResolveValue(draft.KeepUnknownWhenOnlyGames, explicitness.KeepUnknownWhenOnlyGames, profileSettings.KeepUnknownWhenOnlyGames, workflowSettings.KeepUnknownWhenOnlyGames),
            AggressiveJunk = ResolveValue(draft.AggressiveJunk, explicitness.AggressiveJunk, profileSettings.AggressiveJunk, workflowSettings.AggressiveJunk),
            SortConsole = ResolveValue(draft.SortConsole, explicitness.SortConsole, profileSettings.SortConsole, workflowSettings.SortConsole),
            EnableDat = ResolveValue(draft.EnableDat, explicitness.EnableDat, profileSettings.EnableDat, workflowSettings.EnableDat),
            EnableDatAudit = ResolveValue(draft.EnableDatAudit, explicitness.EnableDatAudit, profileSettings.EnableDatAudit, workflowSettings.EnableDatAudit),
            EnableDatRename = ResolveValue(draft.EnableDatRename, explicitness.EnableDatRename, profileSettings.EnableDatRename, workflowSettings.EnableDatRename),
            DatRoot = ResolveValue(draft.DatRoot, explicitness.DatRoot, profileSettings.DatRoot, workflowSettings.DatRoot),
            HashType = ResolveValue(draft.HashType, explicitness.HashType, profileSettings.HashType, workflowSettings.HashType),
            ConvertFormat = ResolveValue(draft.ConvertFormat, explicitness.ConvertFormat, profileSettings.ConvertFormat, workflowSettings.ConvertFormat),
            ConvertOnly = ResolveValue(draft.ConvertOnly, explicitness.ConvertOnly, profileSettings.ConvertOnly, workflowSettings.ConvertOnly),
            ApproveReviews = ResolveValue(draft.ApproveReviews, explicitness.ApproveReviews, profileSettings.ApproveReviews, workflowSettings.ApproveReviews),
            ApproveConversionReview = ResolveValue(draft.ApproveConversionReview, explicitness.ApproveConversionReview, profileSettings.ApproveConversionReview, workflowSettings.ApproveConversionReview),
            ConflictPolicy = ResolveValue(draft.ConflictPolicy, explicitness.ConflictPolicy, profileSettings.ConflictPolicy, workflowSettings.ConflictPolicy),
            TrashRoot = ResolveValue(draft.TrashRoot, explicitness.TrashRoot, profileSettings.TrashRoot, workflowSettings.TrashRoot)
        };

        return new ResolvedRunConfiguration(resolvedDraft, workflow, profile, profile?.Id);
    }

    private static WorkflowScenarioDefinition? ResolveWorkflow(string? workflowScenarioId)
    {
        if (string.IsNullOrWhiteSpace(workflowScenarioId))
            return null;

        return WorkflowScenarioCatalog.TryGet(workflowScenarioId)
            ?? throw new InvalidOperationException($"Workflow '{workflowScenarioId}' was not found.");
    }

    private async ValueTask<RunProfileDocument?> ResolveProfileAsync(
        string? profileId,
        WorkflowScenarioDefinition? workflow,
        string? profileFilePath,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(profileFilePath))
            return await _profileService.LoadExternalAsync(profileFilePath, ct).ConfigureAwait(false);

        var effectiveProfileId = !string.IsNullOrWhiteSpace(profileId)
            ? profileId
            : workflow?.RecommendedProfileId;

        if (string.IsNullOrWhiteSpace(effectiveProfileId))
            return null;

        return await _profileService.TryGetAsync(effectiveProfileId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Profile '{effectiveProfileId}' was not found.");
    }

    private static T? ResolveValue<T>(T? explicitValue, bool isExplicit, T? profileValue, T? workflowValue)
    {
        if (isExplicit)
            return explicitValue;

        return workflowValue is not null
            ? workflowValue
            : profileValue;
    }
}
