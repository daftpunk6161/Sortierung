using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.Orchestration;
using System.IO;

namespace RomCleanup.UI.Wpf.Models;

/// <summary>
/// Immutable UI projection for dashboard and analysis panels.
/// Converts a run result into display-ready values and collections.
/// </summary>
public sealed record DashboardProjection(
    string Winners,
    string Dupes,
    string Junk,
    string Duration,
    string HealthScore,
    string Games,
    string DatHits,
    string DedupeRate,
    string MoveConsequenceText,
    IReadOnlyList<ConsoleDistributionItem> ConsoleDistribution,
    IReadOnlyList<DedupeGroupItem> DedupeGroups)
{
    public static DashboardProjection From(RunProjection projection, RunResult result, bool isConvertOnlyRun)
    {
        var winners = isConvertOnlyRun ? "–" : projection.Keep.ToString();
        var dupes = isConvertOnlyRun ? "–" : projection.Dupes.ToString();
        var junk = isConvertOnlyRun ? "–" : projection.Junk.ToString();
        var duration = $"{projection.DurationMs / 1000.0:F1}s";
        var healthScore = isConvertOnlyRun || projection.TotalFiles <= 0
            ? "–"
            : $"{projection.HealthScore}%";
        var games = isConvertOnlyRun ? "–" : projection.Games.ToString();
        var datHits = isConvertOnlyRun ? "–" : projection.DatMatches.ToString();
        var dedupeDenominator = projection.Keep + projection.Dupes;
        var dedupeRate = isConvertOnlyRun || dedupeDenominator <= 0
            ? "–"
            : $"{100.0 * projection.Dupes / dedupeDenominator:F0}%";

        var consoleDistribution = BuildConsoleDistribution(result.AllCandidates);
        var dedupeGroups = BuildDedupeGroupItems(result.DedupeGroups);

        var totalMove = projection.MoveCount + projection.JunkRemovedCount;
        var moveConsequenceText = isConvertOnlyRun
            ? "Nur Konvertierung aktiv. Keine Dateien werden verschoben."
            : totalMove > 0
                ? $"Es werden {totalMove} Dateien verschoben ({projection.Dupes} Duplikate, {projection.Junk} Junk)."
                : "Keine Dateien zum Verschieben erkannt.";

        return new DashboardProjection(
            Winners: winners,
            Dupes: dupes,
            Junk: junk,
            Duration: duration,
            HealthScore: healthScore,
            Games: games,
            DatHits: datHits,
            DedupeRate: dedupeRate,
            MoveConsequenceText: moveConsequenceText,
            ConsoleDistribution: consoleDistribution,
            DedupeGroups: dedupeGroups);
    }

    private static IReadOnlyList<ConsoleDistributionItem> BuildConsoleDistribution(IReadOnlyList<RomCleanup.Contracts.Models.RomCandidate> candidates)
    {
        var consoleCounts = candidates
            .Where(c => !string.IsNullOrEmpty(c.ConsoleKey))
            .GroupBy(c => c.ConsoleKey)
            .Select(g => (Key: g.Key, Count: g.Count()))
            .OrderByDescending(x => x.Count)
            .Take(20)
            .ToList();

        int maxCount = consoleCounts.Count > 0 ? consoleCounts[0].Count : 1;
        return consoleCounts.Select(x => new ConsoleDistributionItem
        {
            ConsoleKey = x.Key,
            DisplayName = x.Key,
            FileCount = x.Count,
            Fraction = (double)x.Count / maxCount
        }).ToList();
    }

    private static IReadOnlyList<DedupeGroupItem> BuildDedupeGroupItems(IReadOnlyList<RomCleanup.Contracts.Models.DedupeResult> groups)
    {
        return groups
            .Take(200)
            .Select(grp => new DedupeGroupItem
            {
                GameKey = grp.GameKey,
                Winner = new DedupeEntryItem
                {
                    FileName = Path.GetFileName(grp.Winner.MainPath),
                    Region = grp.Winner.Region,
                    RegionScore = grp.Winner.RegionScore,
                    FormatScore = grp.Winner.FormatScore,
                    VersionScore = grp.Winner.VersionScore,
                    IsWinner = true
                },
                Losers = grp.Losers.Select(l => new DedupeEntryItem
                {
                    FileName = Path.GetFileName(l.MainPath),
                    Region = l.Region,
                    RegionScore = l.RegionScore,
                    FormatScore = l.FormatScore,
                    VersionScore = l.VersionScore,
                    IsWinner = false
                }).ToList()
            })
            .ToList();
    }
}