using Romulus.Contracts.Models;

namespace Romulus.Core.Deduplication;

/// <summary>
/// Deterministic winner selection for ROM deduplication.
/// Port of Select-Winner from Core.ps1 lines 567-590.
/// Invariant: identical inputs always produce the identical winner.
/// </summary>
public static class DeduplicationEngine
{
    private static readonly IReadOnlyDictionary<string, int> FallbackCategoryRanks =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(FileCategory.Game)] = 5,
            [nameof(FileCategory.Bios)] = 4,
            [nameof(FileCategory.NonGame)] = 3,
            [nameof(FileCategory.Junk)] = 2,
            [nameof(FileCategory.Unknown)] = 1
        };

    public sealed record DeduplicationResult(
        IReadOnlyList<DedupeGroup> Groups,
        int SkippedEmptyGameKeyCount);

    /// <summary>
    /// Selects the best ROM candidate from a group sharing the same GameKey.
    /// Multi-criteria sort (all descending except MainPath alphabetical):
    /// CompletenessScore → DatMatch → RegionScore → HeaderScore →
    /// VersionScore → FormatScore → SizeTieBreakScore → MainPath (asc).
    /// BUG-011 FIX: Alphabetical MainPath tiebreaker ensures determinism.
    /// </summary>
    public static RomCandidate? SelectWinner(IReadOnlyList<RomCandidate> items)
        => SelectWinner(items, FallbackCategoryRanks);

    public static RomCandidate? SelectWinner(
        IReadOnlyList<RomCandidate> items,
        IReadOnlyDictionary<string, int> categoryRanks)
    {
        ArgumentNullException.ThrowIfNull(categoryRanks);

        if (items is null || items.Count == 0) return null;
        if (items.Count == 1) return items[0];

        var highestCategoryRank = items.Max(x => GetCategoryRank(x, categoryRanks));
        var prioritized = items.Where(x => GetCategoryRank(x, categoryRanks) == highestCategoryRank);

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

    private static int GetCategoryRank(
        RomCandidate candidate,
        IReadOnlyDictionary<string, int> categoryRanks)
    {
        var categoryName = candidate.Category.ToString();
        return categoryRanks.TryGetValue(categoryName, out var rank) ? rank : 0;
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
        => DeduplicateWithDiagnostics(candidates, FallbackCategoryRanks).Groups;

    public static IReadOnlyList<DedupeGroup> Deduplicate(
        IReadOnlyList<RomCandidate> candidates,
        IReadOnlyDictionary<string, int> categoryRanks)
        => DeduplicateWithDiagnostics(candidates, categoryRanks).Groups;

    public static DeduplicationResult DeduplicateWithDiagnostics(
        IReadOnlyList<RomCandidate> candidates,
        IReadOnlyDictionary<string, int> categoryRanks)
    {
        ArgumentNullException.ThrowIfNull(categoryRanks);

        // Build groups with a single pass over candidates
        var groupDict = new Dictionary<string, List<RomCandidate>>(StringComparer.OrdinalIgnoreCase);
        var skippedEmptyGameKeys = 0;
        foreach (var c in candidates)
        {
            if (string.IsNullOrWhiteSpace(c.GameKey))
            {
                skippedEmptyGameKeys++;
                continue;
            }

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
            var winner = SelectWinner(items, categoryRanks)!;
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

        return new DeduplicationResult(results, skippedEmptyGameKeys);
    }

    private static string BuildGroupKey(RomCandidate candidate)
    {
        var consoleKey = NormalizeConsoleKey(candidate.ConsoleKey);

        // F11/F12: For UNKNOWN console (or BIOS with UNKNOWN region — which CandidateFactory
        // encodes as "__BIOS__UNKNOWN__..." in the GameKey) the GameKey alone cannot identify
        // the same content: two unrelated files (different real consoles, or a name collision
        // like "tetris") would otherwise be collapsed into one dedupe group and one would be
        // flagged as a duplicate loser → silent data loss.
        // Fix: when a content hash is available, append it to the group key. This keeps
        // identical-content files (same hash from multiple roots) grouped, while distinct
        // content stays separated. When no hash is available we cannot prove distinctness,
        // so we keep the legacy grouping behaviour to avoid mass-segregation regressions.
        var needsContentDiscriminator =
            (string.Equals(consoleKey, "UNKNOWN", StringComparison.Ordinal) ||
             (candidate.GameKey is { } gk && gk.StartsWith("__BIOS__UNKNOWN__", StringComparison.Ordinal)))
            && !string.IsNullOrWhiteSpace(candidate.Hash);

        if (!needsContentDiscriminator)
            return $"{consoleKey}\0{candidate.GameKey}";

        return $"{consoleKey}\0{candidate.GameKey}\0{candidate.Hash}";
    }

    private static string NormalizeConsoleKey(string? consoleKey)
    {
        if (string.IsNullOrWhiteSpace(consoleKey))
            return "UNKNOWN";

        var normalized = consoleKey.Trim();
        foreach (var ch in normalized)
        {
            if (!(char.IsLetterOrDigit(ch) || ch is '-' or '_' or ' '))
                return "UNKNOWN";
        }

        return normalized.ToUpperInvariant();
    }

    /// <summary>
    /// T-W2-SCORING-REASON-TRACE: produces one
    /// <see cref="WinnerReasonTrace"/> per <see cref="DedupeGroup"/>. Pure
    /// and deterministic — depends only on the data already inside the
    /// groups (no I/O, no time, no environment). Trace order matches the
    /// input order of <paramref name="groups"/> (which itself is sorted by
    /// <see cref="DeduplicateWithDiagnostics"/>) so GUI/CLI/API/Reports
    /// share one fachliche Wahrheit and never re-derive the order.
    ///
    /// <para>
    /// Vorbereitung fuer T-W4-DECISION-EXPLAINER. The trace deliberately
    /// uses the file name only, never the full path, so reports cannot leak
    /// root information beyond what <see cref="DedupeGroup"/> already
    /// exposes.
    /// </para>
    /// </summary>
    public static IReadOnlyList<WinnerReasonTrace> BuildWinnerReasons(
        IReadOnlyList<DedupeGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        if (groups.Count == 0)
            return Array.Empty<WinnerReasonTrace>();

        var traces = new List<WinnerReasonTrace>(groups.Count);
        foreach (var group in groups)
        {
            traces.Add(BuildWinnerReason(group));
        }
        return traces;
    }

    /// <summary>
    /// Builds a single <see cref="WinnerReasonTrace"/> for the given group.
    /// Public for surgical callers (e.g. tests, the upcoming Decision
    /// Explainer view-model) that already have the group at hand.
    /// </summary>
    public static WinnerReasonTrace BuildWinnerReason(DedupeGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        var winner = group.Winner;

        var fileName = string.IsNullOrEmpty(winner.MainPath)
            ? string.Empty
            : System.IO.Path.GetFileName(winner.MainPath);

        var summary = string.Join(
            " > ",
            $"Cat={winner.Category}",
            $"Compl={winner.CompletenessScore}",
            $"Dat={(winner.DatMatch ? 1 : 0)}",
            $"Reg={winner.RegionScore}",
            $"Hdr={winner.HeaderScore}",
            $"Ver={winner.VersionScore}",
            $"Fmt={winner.FormatScore}",
            $"SizeTb={winner.SizeTieBreakScore}");

        return new WinnerReasonTrace(
            ConsoleKey: NormalizeConsoleKey(winner.ConsoleKey),
            GameKey: group.GameKey ?? winner.GameKey ?? string.Empty,
            WinnerFileName: fileName ?? string.Empty,
            WinnerExtension: winner.Extension ?? string.Empty,
            WinnerRegion: winner.Region ?? string.Empty,
            RegionScore: winner.RegionScore,
            FormatScore: winner.FormatScore,
            VersionScore: winner.VersionScore,
            HeaderScore: winner.HeaderScore,
            CompletenessScore: winner.CompletenessScore,
            DatMatch: winner.DatMatch,
            MultiDatResolution: winner.MultiDatResolution,
            SizeTieBreakScore: winner.SizeTieBreakScore,
            WinnerCategory: winner.Category.ToString(),
            LoserCount: group.Losers.Count,
            TiebreakerSummary: summary);
    }
}
