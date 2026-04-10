using Romulus.Contracts.Models;

namespace Romulus.Core.Deduplication;

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

        var highestCategoryRank = items.Max(GetCategoryRank);
        var prioritized = items.Where(x => GetCategoryRank(x) == highestCategoryRank);

        return prioritized
            .OrderByDescending(x => x.CompletenessScore)
            .ThenByDescending(x => x.DatMatch ? 1 : 0)
            .ThenByDescending(x => x.RegionScore)
            .ThenByDescending(x => x.HeaderScore)
            .ThenByDescending(x => x.VersionScore)
            .ThenByDescending(x => x.FormatScore)
            .ThenByDescending(x => x.SizeTieBreakScore)
            .ThenBy(x => x.MainPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.MainPath, StringComparer.Ordinal)
            .First();
    }

    private static int GetCategoryRank(RomCandidate candidate)
    {
        return candidate.Category switch
        {
            FileCategory.Game => 5,
            FileCategory.Bios => 4,
            FileCategory.NonGame => 3,
            FileCategory.Junk => 2,
            FileCategory.Unknown => 1,
            _ => 0
        };
    }

    /// <summary>
    /// Runs deduplication across all groups.
    /// Each group is keyed by ConsoleKey+GameKey to avoid cross-platform collisions;
    /// returns the winner + losers for each group.
    /// V2-H12: Uses dictionary-based grouping instead of LINQ GroupBy+OrderBy+ToList
    /// to reduce intermediate allocations for large candidate sets.
    /// </summary>
    public static IReadOnlyList<DedupeGroup> Deduplicate(
        IReadOnlyList<RomCandidate> candidates)
    {
        // Build groups with a single pass over candidates
        var groupDict = new Dictionary<string, List<RomCandidate>>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in candidates)
        {
            if (string.IsNullOrWhiteSpace(c.GameKey)) continue;
            var groupKey = BuildGroupKey(c);

            if (!groupDict.TryGetValue(groupKey, out var list))
            {
                list = new List<RomCandidate>(2); // most groups have 1-3 items
                groupDict[groupKey] = list;
            }
            list.Add(c);
        }

        // Sort keys for deterministic output order
        var sortedKeys = groupDict.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

        var results = new List<DedupeGroup>(sortedKeys.Count);
        foreach (var key in sortedKeys)
        {
            var items = groupDict[key];
            var winner = SelectWinner(items)!;
            // Safety invariant:
            // A physical file path must never be both winner and loser within the same group.
            // Also deduplicate loser paths to avoid duplicate move attempts for identical files.
            var losers = items
                .Where(x => !string.Equals(x.MainPath, winner.MainPath, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.MainPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.MainPath, StringComparer.Ordinal)
                .DistinctBy(x => x.MainPath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            results.Add(new DedupeGroup
            {
                Winner = winner,
                Losers = losers,
                GameKey = winner.GameKey
            });
        }

        // SEC-DEDUP: A file path that is a winner in one group must not appear as a loser
        // in another group (prevents conflicting move/keep decisions for the same physical file).
        var winnerPaths = new HashSet<string>(
            results.Select(r => r.Winner.MainPath), StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < results.Count; i++)
        {
            var group = results[i];
            var filtered = group.Losers
                .Where(l => !winnerPaths.Contains(l.MainPath))
                .ToList();
            if (filtered.Count != group.Losers.Count)
            {
                int removedCount = group.Losers.Count - filtered.Count;
                results[i] = group with { Losers = filtered, CrossGroupFilteredCount = removedCount };
            }
        }

        return results;
    }

    private static string BuildGroupKey(RomCandidate candidate)
    {
        var consoleKey = string.IsNullOrWhiteSpace(candidate.ConsoleKey)
            ? "UNKNOWN"
            : candidate.ConsoleKey.Trim();

        return $"{consoleKey}||{candidate.GameKey}";
    }
}
