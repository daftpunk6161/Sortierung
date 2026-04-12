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

        var propertyNames = CollectPropertyNames(rootElement);

        var draft = new RunConfigurationDraft
        {
            Roots = request.Roots ?? Array.Empty<string>(),
            Mode = HasProperty(propertyNames, "mode") ? request.Mode : null,
            WorkflowScenarioId = HasProperty(propertyNames, "workflowScenarioId") ? request.WorkflowScenarioId : null,
            ProfileId = HasProperty(propertyNames, "profileId") ? request.ProfileId : null,
            PreferRegions = HasProperty(propertyNames, "preferRegions") ? request.PreferRegions : null,
            Extensions = HasProperty(propertyNames, "extensions") ? request.Extensions : null,
            RemoveJunk = HasProperty(propertyNames, "removeJunk") ? request.RemoveJunk : null,
            OnlyGames = HasProperty(propertyNames, "onlyGames") ? request.OnlyGames : null,
            KeepUnknownWhenOnlyGames = HasProperty(propertyNames, "keepUnknownWhenOnlyGames") ? request.KeepUnknownWhenOnlyGames : null,
            AggressiveJunk = HasProperty(propertyNames, "aggressiveJunk") ? request.AggressiveJunk : null,
            SortConsole = HasProperty(propertyNames, "sortConsole") ? request.SortConsole : null,
            EnableDat = HasProperty(propertyNames, "enableDat") ? request.EnableDat : null,
            EnableDatAudit = HasProperty(propertyNames, "enableDatAudit") ? request.EnableDatAudit : null,
            EnableDatRename = HasProperty(propertyNames, "enableDatRename") ? request.EnableDatRename : null,
            DatRoot = HasProperty(propertyNames, "datRoot") ? request.DatRoot : null,
            HashType = HasProperty(propertyNames, "hashType") ? request.HashType : null,
            ConvertFormat = HasProperty(propertyNames, "convertFormat") ? request.ConvertFormat : null,
            ConvertOnly = HasProperty(propertyNames, "convertOnly") ? request.ConvertOnly : null,
            ApproveReviews = HasProperty(propertyNames, "approveReviews") ? request.ApproveReviews : null,
            ApproveConversionReview = HasProperty(propertyNames, "approveConversionReview") ? request.ApproveConversionReview : null,
            ConflictPolicy = HasProperty(propertyNames, "conflictPolicy") ? request.ConflictPolicy : null,
            TrashRoot = HasProperty(propertyNames, "trashRoot") ? request.TrashRoot : null
        };

        var explicitness = new RunConfigurationExplicitness
        {
            Mode = HasProperty(propertyNames, "mode"),
            PreferRegions = HasProperty(propertyNames, "preferRegions"),
            Extensions = HasProperty(propertyNames, "extensions"),
            RemoveJunk = HasProperty(propertyNames, "removeJunk"),
            OnlyGames = HasProperty(propertyNames, "onlyGames"),
            KeepUnknownWhenOnlyGames = HasProperty(propertyNames, "keepUnknownWhenOnlyGames"),
            AggressiveJunk = HasProperty(propertyNames, "aggressiveJunk"),
            SortConsole = HasProperty(propertyNames, "sortConsole"),
            EnableDat = HasProperty(propertyNames, "enableDat"),
            EnableDatAudit = HasProperty(propertyNames, "enableDatAudit"),
            EnableDatRename = HasProperty(propertyNames, "enableDatRename"),
            DatRoot = HasProperty(propertyNames, "datRoot"),
            HashType = HasProperty(propertyNames, "hashType"),
            ConvertFormat = HasProperty(propertyNames, "convertFormat"),
            ConvertOnly = HasProperty(propertyNames, "convertOnly"),
            ApproveReviews = HasProperty(propertyNames, "approveReviews"),
            ApproveConversionReview = HasProperty(propertyNames, "approveConversionReview"),
            ConflictPolicy = HasProperty(propertyNames, "conflictPolicy"),
            TrashRoot = HasProperty(propertyNames, "trashRoot")
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

    private static HashSet<string> CollectPropertyNames(JsonElement rootElement)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (rootElement.ValueKind != JsonValueKind.Object)
            return names;

        foreach (var property in rootElement.EnumerateObject())
            names.Add(property.Name);

        return names;
    }

    private static bool HasProperty(IReadOnlySet<string> propertyNames, string propertyName)
        => propertyNames.Contains(propertyName);
}

internal sealed record ApiResolvedRunConfiguration(
    RunRequest Request,
    MaterializedRunConfiguration Materialized);
