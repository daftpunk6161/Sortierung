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
        var presence = BuildPresence(propertyNames);

        var draft = new RunConfigurationDraft
        {
            Roots = request.Roots ?? Array.Empty<string>(),
            Mode = presence.Mode ? request.Mode : null,
            WorkflowScenarioId = presence.WorkflowScenarioId ? request.WorkflowScenarioId : null,
            ProfileId = presence.ProfileId ? request.ProfileId : null,
            PreferRegions = presence.PreferRegions ? request.PreferRegions : null,
            Extensions = presence.Extensions ? request.Extensions : null,
            RemoveJunk = presence.RemoveJunk ? request.RemoveJunk : null,
            OnlyGames = presence.OnlyGames ? request.OnlyGames : null,
            KeepUnknownWhenOnlyGames = presence.KeepUnknownWhenOnlyGames ? request.KeepUnknownWhenOnlyGames : null,
            AggressiveJunk = presence.AggressiveJunk ? request.AggressiveJunk : null,
            SortConsole = presence.SortConsole ? request.SortConsole : null,
            EnableDat = presence.EnableDat ? request.EnableDat : null,
            EnableDatAudit = presence.EnableDatAudit ? request.EnableDatAudit : null,
            EnableDatRename = presence.EnableDatRename ? request.EnableDatRename : null,
            DatRoot = presence.DatRoot ? request.DatRoot : null,
            HashType = presence.HashType ? request.HashType : null,
            ConvertFormat = presence.ConvertFormat ? request.ConvertFormat : null,
            ConvertOnly = presence.ConvertOnly ? request.ConvertOnly : null,
            ApproveReviews = presence.ApproveReviews ? request.ApproveReviews : null,
            ApproveConversionReview = presence.ApproveConversionReview ? request.ApproveConversionReview : null,
            ConflictPolicy = presence.ConflictPolicy ? request.ConflictPolicy : null,
            TrashRoot = presence.TrashRoot ? request.TrashRoot : null
        };

        var explicitness = new RunConfigurationExplicitness
        {
            Mode = presence.Mode,
            WorkflowScenarioId = presence.WorkflowScenarioId,
            ProfileId = presence.ProfileId,
            PreferRegions = presence.PreferRegions,
            Extensions = presence.Extensions,
            RemoveJunk = presence.RemoveJunk,
            OnlyGames = presence.OnlyGames,
            KeepUnknownWhenOnlyGames = presence.KeepUnknownWhenOnlyGames,
            AggressiveJunk = presence.AggressiveJunk,
            SortConsole = presence.SortConsole,
            EnableDat = presence.EnableDat,
            EnableDatAudit = presence.EnableDatAudit,
            EnableDatRename = presence.EnableDatRename,
            DatRoot = presence.DatRoot,
            HashType = presence.HashType,
            ConvertFormat = presence.ConvertFormat,
            ConvertOnly = presence.ConvertOnly,
            ApproveReviews = presence.ApproveReviews,
            ApproveConversionReview = presence.ApproveConversionReview,
            ConflictPolicy = presence.ConflictPolicy,
            TrashRoot = presence.TrashRoot
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

    private static RunRequestPropertyPresence BuildPresence(IReadOnlySet<string> propertyNames)
        => new(
            Mode: propertyNames.Contains("mode"),
            WorkflowScenarioId: propertyNames.Contains("workflowScenarioId"),
            ProfileId: propertyNames.Contains("profileId"),
            PreferRegions: propertyNames.Contains("preferRegions"),
            Extensions: propertyNames.Contains("extensions"),
            RemoveJunk: propertyNames.Contains("removeJunk"),
            OnlyGames: propertyNames.Contains("onlyGames"),
            KeepUnknownWhenOnlyGames: propertyNames.Contains("keepUnknownWhenOnlyGames"),
            AggressiveJunk: propertyNames.Contains("aggressiveJunk"),
            SortConsole: propertyNames.Contains("sortConsole"),
            EnableDat: propertyNames.Contains("enableDat"),
            EnableDatAudit: propertyNames.Contains("enableDatAudit"),
            EnableDatRename: propertyNames.Contains("enableDatRename"),
            DatRoot: propertyNames.Contains("datRoot"),
            HashType: propertyNames.Contains("hashType"),
            ConvertFormat: propertyNames.Contains("convertFormat"),
            ConvertOnly: propertyNames.Contains("convertOnly"),
            ApproveReviews: propertyNames.Contains("approveReviews"),
            ApproveConversionReview: propertyNames.Contains("approveConversionReview"),
            ConflictPolicy: propertyNames.Contains("conflictPolicy"),
            TrashRoot: propertyNames.Contains("trashRoot"));

    private readonly record struct RunRequestPropertyPresence(
        bool Mode,
        bool WorkflowScenarioId,
        bool ProfileId,
        bool PreferRegions,
        bool Extensions,
        bool RemoveJunk,
        bool OnlyGames,
        bool KeepUnknownWhenOnlyGames,
        bool AggressiveJunk,
        bool SortConsole,
        bool EnableDat,
        bool EnableDatAudit,
        bool EnableDatRename,
        bool DatRoot,
        bool HashType,
        bool ConvertFormat,
        bool ConvertOnly,
        bool ApproveReviews,
        bool ApproveConversionReview,
        bool ConflictPolicy,
        bool TrashRoot);
}

internal sealed record ApiResolvedRunConfiguration(
    RunRequest Request,
    MaterializedRunConfiguration Materialized);
