using RomCleanup.Contracts.Models;

namespace RomCleanup.Core.Deduplication;

/// <summary>
/// Deterministic winner selection for ROM deduplication.
/// Port of Select-Winner from Core.ps1 lines 567-590.
/// Invariant: identical inputs always produce the identical winner.
/// </summary>
public static class DeduplicationEngine
{
    /// <summary>
    /// Selects the best ROM candidate from a group sharing the same GameKey.
    /// Multi-criteria sort (all descending except MainPath alphabetical):
    /// CompletenessScore → DatMatch → RegionScore → HeaderScore →
    /// VersionScore → FormatScore → SizeTieBreakScore → MainPath (asc).
    /// BUG-011 FIX: Alphabetical MainPath tiebreaker ensures determinism.
    /// </summary>
    public static RomCandidate? SelectWinner(IReadOnlyList<RomCandidate> items)
    {
        if (items is null || items.Count == 0) return null;
        if (items.Count == 1) return items[0];

        return items
            .OrderByDescending(x => x.CompletenessScore)
            .ThenByDescending(x => x.DatMatch ? 1 : 0)
            .ThenByDescending(x => x.RegionScore)
            .ThenByDescending(x => x.HeaderScore)
            .ThenByDescending(x => x.VersionScore)
            .ThenByDescending(x => x.FormatScore)
            .ThenByDescending(x => x.SizeTieBreakScore)
            .ThenBy(x => x.MainPath, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    /// <summary>
    /// Runs deduplication across all groups.
    /// Each group is keyed by GameKey; returns the winner + losers for each group.
    /// V2-H12: Uses dictionary-based grouping instead of LINQ GroupBy+OrderBy+ToList
    /// to reduce intermediate allocations for large candidate sets.
    /// </summary>
    public static IReadOnlyList<DedupeResult> Deduplicate(
        IReadOnlyList<RomCandidate> candidates)
    {
        // Build groups with a single pass over candidates
        var groupDict = new Dictionary<string, List<RomCandidate>>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in candidates)
        {
            if (string.IsNullOrWhiteSpace(c.GameKey)) continue;
            if (!groupDict.TryGetValue(c.GameKey, out var list))
            {
                list = new List<RomCandidate>(2); // most groups have 1-3 items
                groupDict[c.GameKey] = list;
            }
            list.Add(c);
        }

        // Sort keys for deterministic output order
        var sortedKeys = groupDict.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

        var results = new List<DedupeResult>(sortedKeys.Count);
        foreach (var key in sortedKeys)
        {
            var items = groupDict[key];
            var winner = SelectWinner(items)!;
            var losers = items.Where(x => !string.Equals(x.MainPath, winner.MainPath, StringComparison.OrdinalIgnoreCase)).ToList();
            results.Add(new DedupeResult
            {
                Winner = winner,
                Losers = losers,
                GameKey = key
            });
        }

        return results;
    }
}
