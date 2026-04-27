using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Romulus.Contracts.Models;

namespace Romulus.UI.Wpf.Services;

/// <summary>
/// Deterministic content-fingerprint over the channel-neutral <see cref="RunConfigurationDraft"/>.
///
/// Designed as the single gate between Preview (DryRun) and Execute (Move/Convert):
/// if the fingerprint of the draft that produced the last Preview differs from the
/// fingerprint of the current draft, the move/convert apply path must lock.
///
/// Drift-safe by construction: all properties of <see cref="RunConfigurationDraft"/>
/// participate via reflection-driven serialization. Adding a new property to the draft
/// automatically expands the fingerprint surface — there is no hand-maintained
/// "relevant properties" list to forget.
/// </summary>
internal static class RunConfigurationDraftFingerprint
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = null,
        IncludeFields = false,
        // Stable ordering: properties are emitted in declaration order, which is stable
        // because RunConfigurationDraft is a sealed record with init-only properties.
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    /// <summary>
    /// Returns a SHA-256 hash (hex, lowercase, 64 chars) over a canonical
    /// representation of the draft. Pure function. Same input -> same output.
    /// </summary>
    public static string Compute(RunConfigurationDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        // Canonical projection: collections normalized to ordered, lower-cased lists
        // so semantically equivalent drafts (e.g. region order tie-breaker) produce
        // the same fingerprint without losing genuine differences.
        var canonical = new
        {
            draft.Roots, // Roots are order-significant in normalization (RunOptionsBuilder); keep as-is.
            draft.Mode,
            draft.WorkflowScenarioId,
            draft.ProfileId,
            PreferRegions = NormalizeStringList(draft.PreferRegions),
            Extensions = NormalizeStringList(draft.Extensions),
            draft.RemoveJunk,
            draft.OnlyGames,
            draft.KeepUnknownWhenOnlyGames,
            draft.AggressiveJunk,
            draft.SortConsole,
            draft.EnableDat,
            draft.EnableDatAudit,
            draft.EnableDatRename,
            draft.DatRoot,
            draft.HashType,
            draft.ConvertFormat,
            draft.ConvertOnly,
            draft.ApproveReviews,
            draft.ApproveConversionReview,
            draft.ConflictPolicy,
            draft.TrashRoot
        };

        var json = JsonSerializer.Serialize(canonical, SerializerOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        var hash = SHA256.HashData(bytes);

        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
            sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    private static string[]? NormalizeStringList(string[]? values)
    {
        if (values is null)
            return null;
        return values
            .Where(static s => !string.IsNullOrWhiteSpace(s))
            .Select(static s => s.Trim())
            .OrderBy(static s => s, StringComparer.Ordinal)
            .ToArray();
    }
}
