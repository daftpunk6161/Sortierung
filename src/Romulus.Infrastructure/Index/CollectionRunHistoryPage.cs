using Romulus.Contracts.Models;

namespace Romulus.Infrastructure.Index;

/// <summary>
/// Shared read model for persisted run history pages.
/// Keeps paging semantics aligned across entry points without duplicating snapshot math.
/// </summary>
public sealed class CollectionRunHistoryPage
{
    public int Total { get; init; }
    public int Offset { get; init; }
    public int Limit { get; init; }
    public int Returned { get; init; }
    public bool HasMore { get; init; }
    public CollectionRunHistoryItem[] Runs { get; init; } = Array.Empty<CollectionRunHistoryItem>();
}

public sealed class CollectionRunHistoryItem
{
    public string RunId { get; init; } = "";
    public DateTime StartedUtc { get; init; }
    public DateTime CompletedUtc { get; init; }
    public string Mode { get; init; } = "";
    public string Status { get; init; } = "";
    public int RootCount { get; init; }
    public string RootFingerprint { get; init; } = "";
    public long DurationMs { get; init; }
    public int TotalFiles { get; init; }
    public long CollectionSizeBytes { get; init; }
    public int Games { get; init; }
    public int Dupes { get; init; }
    public int Junk { get; init; }
    public int DatMatches { get; init; }
    public int ConvertedCount { get; init; }
    public int FailCount { get; init; }
    public long SavedBytes { get; init; }
    public long ConvertSavedBytes { get; init; }
    public int HealthScore { get; init; }
}

public static class CollectionRunHistoryPageBuilder
{
    public const int DefaultLimit = 200;
    public const int MaxLimit = 1000;

    public static CollectionRunHistoryPage Build(
        IReadOnlyList<CollectionRunSnapshot> snapshots,
        int totalCount,
        int offset = 0,
        int? limit = null)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        var normalizedOffset = Math.Max(0, offset);
        var normalizedLimit = NormalizeLimit(limit);
        var page = snapshots
            .Skip(normalizedOffset)
            .Take(normalizedLimit)
            .Select(snapshot => new CollectionRunHistoryItem
            {
                RunId = snapshot.RunId,
                StartedUtc = snapshot.StartedUtc,
                CompletedUtc = snapshot.CompletedUtc,
                Mode = snapshot.Mode,
                Status = snapshot.Status,
                RootCount = snapshot.Roots.Count,
                RootFingerprint = snapshot.RootFingerprint,
                DurationMs = snapshot.DurationMs,
                TotalFiles = snapshot.TotalFiles,
                CollectionSizeBytes = snapshot.CollectionSizeBytes,
                Games = snapshot.Games,
                Dupes = snapshot.Dupes,
                Junk = snapshot.Junk,
                DatMatches = snapshot.DatMatches,
                ConvertedCount = snapshot.ConvertedCount,
                FailCount = snapshot.FailCount,
                SavedBytes = snapshot.SavedBytes,
                ConvertSavedBytes = snapshot.ConvertSavedBytes,
                HealthScore = snapshot.HealthScore
            })
            .ToArray();

        var normalizedTotal = Math.Max(0, totalCount);
        return new CollectionRunHistoryPage
        {
            Total = normalizedTotal,
            Offset = normalizedOffset,
            Limit = normalizedLimit,
            Returned = page.Length,
            HasMore = normalizedOffset + page.Length < normalizedTotal,
            Runs = page
        };
    }

    public static int NormalizeLimit(int? limit)
    {
        if (limit is null)
            return DefaultLimit;

        return Math.Clamp(limit.Value, 1, MaxLimit);
    }
}
