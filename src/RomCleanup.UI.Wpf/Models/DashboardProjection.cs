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
    string DatHaveDisplay,
    string DatWrongNameDisplay,
    string DatMissDisplay,
    string DatUnknownDisplay,
    string DatAmbiguousDisplay,
    string DedupeRate,
    string MoveConsequenceText,
    string ConvertedDisplay,
    string ConvertBlockedDisplay,
    string ConvertReviewDisplay,
    string ConvertSavedBytesDisplay,
    IReadOnlyList<ConsoleDistributionItem> ConsoleDistribution,
    IReadOnlyList<DedupeGroupItem> DedupeGroups)
{
    public static DashboardProjection From(RunProjection projection, RunResult result, bool isConvertOnlyRun, bool isDryRun = false)
    {
        var isPartialCancelledOrFailed = IsPartialCancelledOrFailed(result);

        var winners = isConvertOnlyRun ? "–" : MarkPlan(MarkProvisional(projection.Keep.ToString(), isPartialCancelledOrFailed), isDryRun);
        var dupes = isConvertOnlyRun ? "–" : MarkPlan(MarkProvisional(projection.Dupes.ToString(), isPartialCancelledOrFailed), isDryRun);
        var junk = isConvertOnlyRun ? "–" : MarkPlan(MarkProvisional(projection.Junk.ToString(), isPartialCancelledOrFailed), isDryRun);
        var duration = $"{projection.DurationMs / 1000.0:F1}s";
        var healthScore = isConvertOnlyRun || projection.TotalFiles <= 0
            ? "–"
            : MarkPlan(MarkProvisional($"{projection.HealthScore}%", isPartialCancelledOrFailed), isDryRun);
        var games = isConvertOnlyRun ? "–" : MarkPlan(MarkProvisional(projection.Games.ToString(), isPartialCancelledOrFailed), isDryRun);
        var datHits = isConvertOnlyRun ? "–" : MarkPlan(MarkProvisional(projection.DatMatches.ToString(), isPartialCancelledOrFailed), isDryRun);
        var hasDatAudit = projection.DatHaveCount > 0
                          || projection.DatHaveWrongNameCount > 0
                          || projection.DatMissCount > 0
                          || projection.DatUnknownCount > 0
                          || projection.DatAmbiguousCount > 0;
        var datHaveDisplay = hasDatAudit ? projection.DatHaveCount.ToString() : "–";
        var datWrongNameDisplay = hasDatAudit ? projection.DatHaveWrongNameCount.ToString() : "–";
        var datMissDisplay = hasDatAudit ? projection.DatMissCount.ToString() : "–";
        var datUnknownDisplay = hasDatAudit ? projection.DatUnknownCount.ToString() : "–";
        var datAmbiguousDisplay = hasDatAudit ? projection.DatAmbiguousCount.ToString() : "–";
        var dedupeDenominator = projection.Keep + projection.Dupes;
        var dedupeRate = isConvertOnlyRun || dedupeDenominator <= 0
            ? "–"
            : MarkPlan(MarkProvisional($"{100.0 * projection.Dupes / dedupeDenominator:F0}%", isPartialCancelledOrFailed), isDryRun);

        var consoleDistribution = BuildConsoleDistribution(result.AllCandidates);
        var dedupeGroups = BuildDedupeGroupItems(result.DedupeGroups);

        var totalMove = projection.MoveCount + projection.JunkRemovedCount;
        var moveConsequenceText = isConvertOnlyRun
            ? "Nur Konvertierung aktiv. Keine Dateien werden verschoben."
            : isPartialCancelledOrFailed
                ? "Lauf abgebrochen. Kennzahlen sind vorläufig und basieren auf bereits gescannten Dateien."
            : isDryRun
                ? "Vorschau (Plan): Kennzahlen zeigen geplante Aktionen, es wurden keine Dateien verschoben."
            : totalMove > 0
                ? $"Es werden {totalMove} Dateien verschoben ({projection.Dupes} Duplikate, {projection.Junk} Junk)."
                : "Keine Dateien zum Verschieben erkannt.";

        var hasConversion = projection.ConvertedCount > 0 || projection.ConvertErrorCount > 0
                            || projection.ConvertBlockedCount > 0 || projection.ConvertSkippedCount > 0;
        var convertedDisplay = hasConversion ? projection.ConvertedCount.ToString() : "–";
        var convertBlockedDisplay = hasConversion ? projection.ConvertBlockedCount.ToString() : "–";
        var convertReviewDisplay = hasConversion ? projection.ConvertReviewCount.ToString() : "–";
        var convertSavedBytesDisplay = hasConversion && projection.ConvertSavedBytes != 0
            ? FormatBytes(projection.ConvertSavedBytes)
            : "–";

        return new DashboardProjection(
            Winners: winners,
            Dupes: dupes,
            Junk: junk,
            Duration: duration,
            HealthScore: healthScore,
            Games: games,
            DatHits: datHits,
            DatHaveDisplay: datHaveDisplay,
            DatWrongNameDisplay: datWrongNameDisplay,
            DatMissDisplay: datMissDisplay,
            DatUnknownDisplay: datUnknownDisplay,
            DatAmbiguousDisplay: datAmbiguousDisplay,
            DedupeRate: dedupeRate,
            MoveConsequenceText: moveConsequenceText,
            ConvertedDisplay: convertedDisplay,
            ConvertBlockedDisplay: convertBlockedDisplay,
            ConvertReviewDisplay: convertReviewDisplay,
            ConvertSavedBytesDisplay: convertSavedBytesDisplay,
            ConsoleDistribution: consoleDistribution,
            DedupeGroups: dedupeGroups);
    }

    private static bool IsPartialCancelledOrFailed(RunResult result)
    {
        var isCancelledOrFailed =
            string.Equals(result.Status, "cancelled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(result.Status, "failed", StringComparison.OrdinalIgnoreCase);

        var hasCandidates = (result.AllCandidates?.Count ?? 0) > 0;
        var hasDedupeGroups = (result.DedupeGroups?.Count ?? 0) > 0;

        return isCancelledOrFailed && hasCandidates && !hasDedupeGroups;
    }

    private static string MarkProvisional(string value, bool isProvisional)
        => isProvisional && value != "–" ? $"{value} (vorläufig)" : value;

    private static string MarkPlan(string value, bool isDryRun)
        => isDryRun && value != "–" ? $"{value} (Plan)" : value;

    private static string FormatBytes(long bytes)
    {
        var abs = Math.Abs(bytes);
        string formatted = abs switch
        {
            >= 1_073_741_824 => $"{abs / 1_073_741_824.0:F1} GB",
            >= 1_048_576 => $"{abs / 1_048_576.0:F1} MB",
            >= 1024 => $"{abs / 1024.0:F1} KB",
            _ => $"{abs} B"
        };
        return bytes < 0 ? $"+{formatted}" : $"-{formatted}";
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

    private static IReadOnlyList<DedupeGroupItem> BuildDedupeGroupItems(IReadOnlyList<RomCleanup.Contracts.Models.DedupeGroup> groups)
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
                    DecisionClass = grp.Winner.DecisionClass.ToString(),
                    EvidenceTier = grp.Winner.EvidenceTier.ToString(),
                    PrimaryMatchKind = grp.Winner.PrimaryMatchKind.ToString(),
                    PlatformFamily = grp.Winner.PlatformFamily.ToString(),
                    IsWinner = true
                },
                Losers = grp.Losers.Select(l => new DedupeEntryItem
                {
                    FileName = Path.GetFileName(l.MainPath),
                    Region = l.Region,
                    RegionScore = l.RegionScore,
                    FormatScore = l.FormatScore,
                    VersionScore = l.VersionScore,
                    DecisionClass = l.DecisionClass.ToString(),
                    EvidenceTier = l.EvidenceTier.ToString(),
                    PrimaryMatchKind = l.PrimaryMatchKind.ToString(),
                    PlatformFamily = l.PlatformFamily.ToString(),
                    IsWinner = false
                }).ToList()
            })
            .ToList();
    }
}