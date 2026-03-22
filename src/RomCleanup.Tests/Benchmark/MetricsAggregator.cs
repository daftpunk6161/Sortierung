namespace RomCleanup.Tests.Benchmark;

internal sealed record SystemMetrics(double Precision, double Recall, double F1, int TruePositive, int FalsePositive, int FalseNegative);

internal sealed record ConfusionEntry(string ExpectedSystem, string ActualSystem, int Count);

internal static class MetricsAggregator
{
    private const int FalseConfidenceThreshold = 80;

    private static bool IsSortingDecision(RomCleanup.Core.Classification.SortDecision decision) =>
        decision is RomCleanup.Core.Classification.SortDecision.Sort or RomCleanup.Core.Classification.SortDecision.DatVerified;

    public static IReadOnlyDictionary<string, SystemMetrics> CalculatePerSystem(IReadOnlyList<BenchmarkSampleResult> results)
    {
        var systems = results
            .SelectMany(r => new[] { r.ExpectedConsoleKey, r.ActualConsoleKey })
            .Where(s => !string.IsNullOrWhiteSpace(s) && !string.Equals(s, "UNKNOWN", StringComparison.OrdinalIgnoreCase))
            .Select(s => s!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var map = new Dictionary<string, SystemMetrics>(StringComparer.OrdinalIgnoreCase);

        foreach (var system in systems)
        {
            int tp = results.Count(r =>
                string.Equals(r.ExpectedConsoleKey, system, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.ActualConsoleKey, system, StringComparison.OrdinalIgnoreCase));

            int fp = results.Count(r =>
                !string.Equals(r.ExpectedConsoleKey, system, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.ActualConsoleKey, system, StringComparison.OrdinalIgnoreCase));

            int fn = results.Count(r =>
                string.Equals(r.ExpectedConsoleKey, system, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(r.ActualConsoleKey, system, StringComparison.OrdinalIgnoreCase));

            var precision = tp + fp == 0 ? 0 : (double)tp / (tp + fp);
            var recall = tp + fn == 0 ? 0 : (double)tp / (tp + fn);
            var f1 = precision + recall == 0 ? 0 : 2d * precision * recall / (precision + recall);

            map[system] = new SystemMetrics(precision, recall, f1, tp, fp, fn);
        }

        return map;
    }

    public static IReadOnlyDictionary<string, double> CalculateAggregate(IReadOnlyList<BenchmarkSampleResult> results)
    {
        int total = results.Count;
        int wrong = results.Count(r => r.Verdict is BenchmarkVerdict.Wrong or BenchmarkVerdict.FalsePositive);
        int unknown = results.Count(r => string.IsNullOrWhiteSpace(r.ActualConsoleKey) || string.Equals(r.ActualConsoleKey, "UNKNOWN", StringComparison.OrdinalIgnoreCase));
        int falseConfidence = results.Count(r =>
            r.ActualConfidence >= FalseConfidenceThreshold &&
            (r.Verdict is BenchmarkVerdict.Wrong or BenchmarkVerdict.FalsePositive));
        int totalSortDecisions = results.Count(r => IsSortingDecision(r.ActualSortDecision));
        int wrongSortDecisions = results.Count(r =>
            IsSortingDecision(r.ActualSortDecision) &&
            (r.Verdict is BenchmarkVerdict.Wrong or BenchmarkVerdict.FalsePositive));
        int correctSortDecisions = results.Count(r =>
            IsSortingDecision(r.ActualSortDecision) &&
            r.Verdict is BenchmarkVerdict.Correct or BenchmarkVerdict.Acceptable);

        return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["wrongMatchRate"] = total == 0 ? 0 : (double)wrong / total,
            ["unknownRate"] = total == 0 ? 0 : (double)unknown / total,
            ["falseConfidenceRate"] = wrong == 0 ? 0 : (double)falseConfidence / wrong,
            ["unsafeSortRate"] = totalSortDecisions == 0 ? 0 : (double)wrongSortDecisions / totalSortDecisions,
            ["safeSortCoverage"] = total == 0 ? 0 : (double)correctSortDecisions / total,
            ["totalSortDecisions"] = totalSortDecisions,
            ["wrongSortDecisions"] = wrongSortDecisions,
            ["gameAsJunkRate"] = CalculateGameAsJunkRate(results)
        };
    }

    private static double CalculateGameAsJunkRate(IReadOnlyList<BenchmarkSampleResult> results)
    {
        int totalExpectedGame = results.Count(r => string.Equals(r.ExpectedCategory, "Game", StringComparison.OrdinalIgnoreCase));
        if (totalExpectedGame == 0)
        {
            return 0;
        }

        int gameAsJunk = results.Count(r =>
            string.Equals(r.ExpectedCategory, "Game", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.ActualCategory, "Junk", StringComparison.OrdinalIgnoreCase));

        return (double)gameAsJunk / totalExpectedGame;
    }

    public static IReadOnlyList<ConfusionEntry> BuildConfusionMatrix(IReadOnlyList<BenchmarkSampleResult> results)
    {
        return results
            .Where(r => r.Verdict is BenchmarkVerdict.Wrong or BenchmarkVerdict.FalsePositive or BenchmarkVerdict.Ambiguous)
            .GroupBy(r => new
            {
                Expected = r.ExpectedConsoleKey ?? "UNKNOWN",
                Actual = r.ActualConsoleKey ?? "UNKNOWN"
            })
            .Select(g => new ConfusionEntry(g.Key.Expected, g.Key.Actual, g.Count()))
            .OrderByDescending(x => x.Count)
            .ToList();
    }
}
