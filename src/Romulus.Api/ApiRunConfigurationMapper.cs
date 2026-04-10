using System.Text.Json;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Profiles;

namespace Romulus.Api;

internal static class ApiRunConfigurationMapper
{
    public static async ValueTask<ApiResolvedRunConfiguration> ResolveAsync(
        RunRequest request,
        JsonElement rootElement,
        RomulusSettings settings,
        RunConfigurationMaterializer materializer,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(materializer);

        var draft = new RunConfigurationDraft
        {
            Roots = request.Roots ?? Array.Empty<string>(),
            Mode = HasProperty(rootElement, "mode") ? request.Mode : null,
            WorkflowScenarioId = HasProperty(rootElement, "workflowScenarioId") ? request.WorkflowScenarioId : null,
            ProfileId = HasProperty(rootElement, "profileId") ? request.ProfileId : null,
            PreferRegions = HasProperty(rootElement, "preferRegions") ? request.PreferRegions : null,
            Extensions = HasProperty(rootElement, "extensions") ? request.Extensions : null,
            RemoveJunk = HasProperty(rootElement, "removeJunk") ? request.RemoveJunk : null,
            OnlyGames = HasProperty(rootElement, "onlyGames") ? request.OnlyGames : null,
            KeepUnknownWhenOnlyGames = HasProperty(rootElement, "keepUnknownWhenOnlyGames") ? request.KeepUnknownWhenOnlyGames : null,
            AggressiveJunk = HasProperty(rootElement, "aggressiveJunk") ? request.AggressiveJunk : null,
            SortConsole = HasProperty(rootElement, "sortConsole") ? request.SortConsole : null,
            EnableDat = HasProperty(rootElement, "enableDat") ? request.EnableDat : null,
            EnableDatAudit = HasProperty(rootElement, "enableDatAudit") ? request.EnableDatAudit : null,
            EnableDatRename = HasProperty(rootElement, "enableDatRename") ? request.EnableDatRename : null,
            DatRoot = HasProperty(rootElement, "datRoot") ? request.DatRoot : null,
            HashType = HasProperty(rootElement, "hashType") ? request.HashType : null,
            ConvertFormat = HasProperty(rootElement, "convertFormat") ? request.ConvertFormat : null,
            ConvertOnly = HasProperty(rootElement, "convertOnly") ? request.ConvertOnly : null,
            ApproveReviews = HasProperty(rootElement, "approveReviews") ? request.ApproveReviews : null,
            ApproveConversionReview = HasProperty(rootElement, "approveConversionReview") ? request.ApproveConversionReview : null,
            ConflictPolicy = HasProperty(rootElement, "conflictPolicy") ? request.ConflictPolicy : null,
            TrashRoot = HasProperty(rootElement, "trashRoot") ? request.TrashRoot : null
        };

        var explicitness = new RunConfigurationExplicitness
        {
            Mode = HasProperty(rootElement, "mode"),
            PreferRegions = HasProperty(rootElement, "preferRegions"),
            Extensions = HasProperty(rootElement, "extensions"),
            RemoveJunk = HasProperty(rootElement, "removeJunk"),
            OnlyGames = HasProperty(rootElement, "onlyGames"),
            KeepUnknownWhenOnlyGames = HasProperty(rootElement, "keepUnknownWhenOnlyGames"),
            AggressiveJunk = HasProperty(rootElement, "aggressiveJunk"),
            SortConsole = HasProperty(rootElement, "sortConsole"),
            EnableDat = HasProperty(rootElement, "enableDat"),
            EnableDatAudit = HasProperty(rootElement, "enableDatAudit"),
            EnableDatRename = HasProperty(rootElement, "enableDatRename"),
            DatRoot = HasProperty(rootElement, "datRoot"),
            HashType = HasProperty(rootElement, "hashType"),
            ConvertFormat = HasProperty(rootElement, "convertFormat"),
            ConvertOnly = HasProperty(rootElement, "convertOnly"),
            ApproveReviews = HasProperty(rootElement, "approveReviews"),
            ApproveConversionReview = HasProperty(rootElement, "approveConversionReview"),
            ConflictPolicy = HasProperty(rootElement, "conflictPolicy"),
            TrashRoot = HasProperty(rootElement, "trashRoot")
        };

        var materialized = await materializer.MaterializeAsync(draft, explicitness, settings, ct: ct).ConfigureAwait(false);
        return new ApiResolvedRunConfiguration(ToCanonicalRequest(materialized), materialized);
    }

    private static RunRequest ToCanonicalRequest(MaterializedRunConfiguration materialized)
    {
        var options = materialized.Options;
        return new RunRequest
        {
            Roots = options.Roots.ToArray(),
            Mode = options.Mode,
            WorkflowScenarioId = materialized.Workflow?.Id,
            ProfileId = materialized.EffectiveProfileId,
            PreferRegions = options.PreferRegions,
            RemoveJunk = options.RemoveJunk,
            AggressiveJunk = options.AggressiveJunk,
            SortConsole = options.SortConsole,
            EnableDat = options.EnableDat,
            EnableDatAudit = options.EnableDatAudit,
            EnableDatRename = options.EnableDatRename,
            DatRoot = options.DatRoot,
            OnlyGames = options.OnlyGames,
            KeepUnknownWhenOnlyGames = options.KeepUnknownWhenOnlyGames,
            HashType = options.HashType,
            ConvertFormat = options.ConvertFormat,
            ConvertOnly = options.ConvertOnly,
            ApproveReviews = options.ApproveReviews,
            ApproveConversionReview = options.ApproveConversionReview,
            ConflictPolicy = options.ConflictPolicy,
            TrashRoot = options.TrashRoot,
            Extensions = options.Extensions.ToArray()
        };
    }

    private static bool HasProperty(JsonElement rootElement, string propertyName)
        => rootElement.ValueKind == JsonValueKind.Object
           && rootElement.TryGetProperty(propertyName, out _);
}

internal sealed record ApiResolvedRunConfiguration(
    RunRequest Request,
    MaterializedRunConfiguration Materialized);
