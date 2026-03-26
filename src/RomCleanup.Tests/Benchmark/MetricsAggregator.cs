using RomCleanup.Contracts.Models;

namespace RomCleanup.Tests.Benchmark;

internal sealed record SystemMetrics(double Precision, double Recall, double F1, int TruePositive, int FalsePositive, int FalseNegative);

internal sealed record ConfusionEntry(string ExpectedSystem, string ActualSystem, int Count);

internal sealed record CalibrationBucket(int LowerBound, int UpperBound, int SampleCount, int CorrectCount, double Accuracy, double Error);

internal sealed record ConfidenceCalibrationResult(double ExpectedCalibrationError, IReadOnlyList<CalibrationBucket> Buckets);

internal sealed record CategoryConfusionEntry(string ExpectedCategory, string ActualCategory, int Count);

internal sealed record ConsoleConfusionPair(string SystemA, string SystemB, double Rate, int Count);

/// <summary>
/// Typed record for all extended metrics M4-M16.
/// Provides compile-time safety over the dictionary-based approach.
/// </summary>
internal sealed record ExtendedMetrics(
    double WrongMatchRate,
    double UnknownRate,
    double FalseConfidenceRate,
    double UnsafeSortRate,
    double SafeSortCoverage,
    double CategoryConfusionRate,
    double GameAsJunkRate,
    double BiosAsGameRate,
    double MaxConsoleConfusionRate,
    double DatExactMatchRate,
    double AmbiguousMatchRate,
    double RepairSafeRate,
    double CategoryRecognitionRate,
    double JunkClassifiedRate)
{
    /// <summary>
    /// Creates an ExtendedMetrics from the dictionary-based aggregate.
    /// </summary>
    public static ExtendedMetrics FromDictionary(IReadOnlyDictionary<string, double> dict)
    {
        return new ExtendedMetrics(
            WrongMatchRate: G(dict, "wrongMatchRate"),
            UnknownRate: G(dict, "unknownRate"),
            FalseConfidenceRate: G(dict, "falseConfidenceRate"),
            UnsafeSortRate: G(dict, "unsafeSortRate"),
            SafeSortCoverage: G(dict, "safeSortCoverage"),
            CategoryConfusionRate: G(dict, "categoryConfusionRate"),
            GameAsJunkRate: G(dict, "gameAsJunkRate"),
            BiosAsGameRate: G(dict, "biosAsGameRate"),
            MaxConsoleConfusionRate: G(dict, "maxConsoleConfusionRate"),
            DatExactMatchRate: G(dict, "datExactMatchRate"),
            AmbiguousMatchRate: G(dict, "ambiguousMatchRate"),
            RepairSafeRate: G(dict, "repairSafeRate"),
            CategoryRecognitionRate: G(dict, "categoryRecognitionRate"),
            JunkClassifiedRate: G(dict, "junkClassifiedRate"));
    }

    private static double G(IReadOnlyDictionary<string, double> d, string key)
        => d.TryGetValue(key, out var v) ? v : 0;
}

internal static class MetricsAggregator
{
    private const int FalseConfidenceThreshold = 80;

    private static bool IsSortingDecision(SortDecision decision) =>
        decision is SortDecision.Sort or SortDecision.DatVerified;

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

        // TASK-022: categoryRecognitionRate — proportion of samples where expected and actual category match
        int categoryMatches = results.Count(r =>
            !string.IsNullOrWhiteSpace(r.ExpectedCategory) &&
            !string.IsNullOrWhiteSpace(r.ActualCategory) &&
            string.Equals(r.ExpectedCategory, r.ActualCategory, StringComparison.OrdinalIgnoreCase));

        // TASK-022: junkClassifiedRate — proportion of junk-expected samples correctly identified as junk
        int totalExpectedJunk = results.Count(r =>
            string.Equals(r.ExpectedCategory, "Junk", StringComparison.OrdinalIgnoreCase));
        int junkClassified = results.Count(r => r.Verdict == BenchmarkVerdict.JunkClassified);

        // TASK-030: SortDecision breakdown counts
        int sortCount = results.Count(r => r.ActualSortDecision == SortDecision.Sort);
        int reviewCount = results.Count(r => r.ActualSortDecision == SortDecision.Review);
        int blockedCount = results.Count(r => r.ActualSortDecision == SortDecision.Blocked);

        return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["wrongMatchRate"] = total == 0 ? 0 : (double)wrong / total,
            ["unknownRate"] = total == 0 ? 0 : (double)unknown / total,
            ["falseConfidenceRate"] = wrong == 0 ? 0 : (double)falseConfidence / wrong,
            ["unsafeSortRate"] = totalSortDecisions == 0 ? 0 : (double)wrongSortDecisions / totalSortDecisions,
            ["safeSortCoverage"] = total == 0 ? 0 : (double)correctSortDecisions / total,
            ["totalSortDecisions"] = totalSortDecisions,
            ["wrongSortDecisions"] = wrongSortDecisions,
            ["gameAsJunkRate"] = CalculateGameAsJunkRate(results),
            ["categoryRecognitionRate"] = total == 0 ? 0 : (double)categoryMatches / total,
            ["junkClassifiedRate"] = totalExpectedJunk == 0 ? 0 : (double)junkClassified / totalExpectedJunk,
            ["sortCount"] = sortCount,
            ["reviewCount"] = reviewCount,
            ["blockedCount"] = blockedCount,
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

    /// <summary>
    /// M9: Category Confusion Rate — rate of off-diagonal entries in the category confusion matrix.
    /// Includes specific sub-rates: gameAsJunk (M9a), biosAsGame, junkAsGame.
    /// </summary>
    public static IReadOnlyDictionary<string, double> CalculateCategoryConfusion(IReadOnlyList<BenchmarkSampleResult> results)
    {
        int total = results.Count;
        if (total == 0)
            return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        int offDiagonal = results.Count(r =>
            !string.IsNullOrWhiteSpace(r.ExpectedCategory) &&
            !string.IsNullOrWhiteSpace(r.ActualCategory) &&
            !string.Equals(r.ExpectedCategory, r.ActualCategory, StringComparison.OrdinalIgnoreCase));

        int totalExpectedGame = results.Count(r =>
            string.Equals(r.ExpectedCategory, "Game", StringComparison.OrdinalIgnoreCase));
        int gameAsJunk = results.Count(r =>
            string.Equals(r.ExpectedCategory, "Game", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.ActualCategory, "Junk", StringComparison.OrdinalIgnoreCase));
        int totalExpectedBios = results.Count(r =>
            string.Equals(r.ExpectedCategory, "BIOS", StringComparison.OrdinalIgnoreCase));
        int biosAsGame = results.Count(r =>
            string.Equals(r.ExpectedCategory, "BIOS", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.ActualCategory, "Game", StringComparison.OrdinalIgnoreCase));
        int totalExpectedJunk = results.Count(r =>
            string.Equals(r.ExpectedCategory, "Junk", StringComparison.OrdinalIgnoreCase));
        int junkAsGame = results.Count(r =>
            string.Equals(r.ExpectedCategory, "Junk", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.ActualCategory, "Game", StringComparison.OrdinalIgnoreCase));

        return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["categoryConfusionRate"] = (double)offDiagonal / total,
            ["gameAsJunkRate"] = totalExpectedGame == 0 ? 0 : (double)gameAsJunk / totalExpectedGame,
            ["biosAsGameRate"] = totalExpectedBios == 0 ? 0 : (double)biosAsGame / totalExpectedBios,
            ["junkAsGameRate"] = totalExpectedJunk == 0 ? 0 : (double)junkAsGame / totalExpectedJunk,
        };
    }

    /// <summary>
    /// M9 detail: Builds the full category confusion matrix.
    /// </summary>
    public static IReadOnlyList<CategoryConfusionEntry> BuildCategoryConfusionMatrix(IReadOnlyList<BenchmarkSampleResult> results)
    {
        return results
            .Where(r => !string.IsNullOrWhiteSpace(r.ExpectedCategory) && !string.IsNullOrWhiteSpace(r.ActualCategory))
            .GroupBy(r => (Expected: r.ExpectedCategory!, Actual: r.ActualCategory!), new CategoryKeyComparer())
            .Select(g => new CategoryConfusionEntry(g.Key.Expected, g.Key.Actual, g.Count()))
            .OrderByDescending(x => x.Count)
            .ToList();
    }

    /// <summary>
    /// M10: Console Confusion Rate — identifies systematic cross-system misidentification pairs.
    /// Returns pairs above the specified threshold rate.
    /// </summary>
    public static IReadOnlyList<ConsoleConfusionPair> CalculateConsoleConfusionPairs(
        IReadOnlyList<BenchmarkSampleResult> results, double thresholdRate = 0.02)
    {
        var byExpected = results
            .Where(r => !string.IsNullOrWhiteSpace(r.ExpectedConsoleKey))
            .GroupBy(r => r.ExpectedConsoleKey!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var pairs = new List<ConsoleConfusionPair>();

        foreach (var (expected, entries) in byExpected)
        {
            int totalForSystem = entries.Count;
            if (totalForSystem == 0) continue;

            var wrongByActual = entries
                .Where(r => r.Verdict is BenchmarkVerdict.Wrong or BenchmarkVerdict.FalsePositive)
                .Where(r => !string.IsNullOrWhiteSpace(r.ActualConsoleKey) &&
                            !string.Equals(r.ActualConsoleKey, expected, StringComparison.OrdinalIgnoreCase))
                .GroupBy(r => r.ActualConsoleKey!, StringComparer.OrdinalIgnoreCase);

            foreach (var group in wrongByActual)
            {
                double rate = (double)group.Count() / totalForSystem;
                if (rate >= thresholdRate)
                {
                    pairs.Add(new ConsoleConfusionPair(expected, group.Key, rate, group.Count()));
                }
            }
        }

        return pairs.OrderByDescending(p => p.Rate).ToList();
    }

    /// <summary>
    /// M10 aggregate: Maximum confusion rate across all system pairs.
    /// </summary>
    public static double CalculateMaxConsoleConfusionRate(IReadOnlyList<BenchmarkSampleResult> results)
    {
        var pairs = CalculateConsoleConfusionPairs(results, thresholdRate: 0);
        return pairs.Count == 0 ? 0 : pairs.Max(p => p.Rate);
    }

    /// <summary>
    /// M11: DAT Exact Match Rate — proportion of DatVerified sort decisions among all entries.
    /// </summary>
    public static double CalculateDatExactMatchRate(IReadOnlyList<BenchmarkSampleResult> results)
    {
        int total = results.Count;
        if (total == 0) return 0;
        int datVerified = results.Count(r =>
            r.ActualSortDecision == SortDecision.DatVerified);
        return (double)datVerified / total;
    }

    /// <summary>
    /// M13: Ambiguous Match Rate — proportion of results with HasConflict=true.
    /// </summary>
    public static double CalculateAmbiguousMatchRate(IReadOnlyList<BenchmarkSampleResult> results)
    {
        int total = results.Count;
        if (total == 0) return 0;
        int ambiguous = results.Count(r => r.ActualHasConflict);
        return (double)ambiguous / total;
    }

    /// <summary>
    /// M14: Repair-Safe Match Rate — proportion of results that are safe for destructive ops
    /// (correct match with Confidence ≥ 95 and no conflict).
    /// </summary>
    public static double CalculateRepairSafeRate(IReadOnlyList<BenchmarkSampleResult> results)
    {
        int total = results.Count;
        if (total == 0) return 0;
        int repairSafe = results.Count(r =>
            r.Verdict is BenchmarkVerdict.Correct or BenchmarkVerdict.Acceptable &&
            r.ActualConfidence >= 95 &&
            !r.ActualHasConflict);
        return (double)repairSafe / total;
    }

    /// <summary>
    /// Aggregated extended metrics M8-M14 keyed for JSON output.
    /// Merges into the existing aggregate dictionary.
    /// </summary>
    public static IReadOnlyDictionary<string, double> CalculateExtendedAggregate(IReadOnlyList<BenchmarkSampleResult> results)
    {
        var basic = CalculateAggregate(results);
        var catConfusion = CalculateCategoryConfusion(results);
        var dict = new Dictionary<string, double>(basic, StringComparer.OrdinalIgnoreCase);

        foreach (var kv in catConfusion)
            dict[kv.Key] = kv.Value;

        dict["maxConsoleConfusionRate"] = CalculateMaxConsoleConfusionRate(results);
        dict["datExactMatchRate"] = CalculateDatExactMatchRate(results);
        dict["ambiguousMatchRate"] = CalculateAmbiguousMatchRate(results);
        dict["repairSafeRate"] = CalculateRepairSafeRate(results);

        return dict;
    }

    /// <summary>
    /// M16 Confidence Calibration: Measures how well the confidence scores
    /// correlate with actual correctness within fixed buckets.
    /// Returns bucket-level stats and an overall calibration error (ECE).
    /// </summary>
    public static ConfidenceCalibrationResult CalculateConfidenceCalibration(
        IReadOnlyList<BenchmarkSampleResult> results, int bucketWidth = 10)
    {
        var buckets = new List<CalibrationBucket>();
        double weightedErrorSum = 0;
        int totalSamples = 0;

        for (int lower = 0; lower <= 100 - bucketWidth; lower += bucketWidth)
        {
            int upper = lower + bucketWidth;
            var inBucket = results
                .Where(r => r.ActualConfidence >= lower && r.ActualConfidence < upper)
                .ToList();

            if (inBucket.Count == 0)
            {
                buckets.Add(new CalibrationBucket(lower, upper, 0, 0, 0, 0));
                continue;
            }

            int correct = inBucket.Count(r =>
                r.Verdict is BenchmarkVerdict.Correct or BenchmarkVerdict.Acceptable);
            double accuracy = (double)correct / inBucket.Count;
            double midpoint = (lower + upper) / 2.0 / 100.0;
            double error = Math.Abs(accuracy - midpoint);

            buckets.Add(new CalibrationBucket(lower, upper, inBucket.Count, correct, accuracy, error));
            weightedErrorSum += error * inBucket.Count;
            totalSamples += inBucket.Count;
        }

        double ece = totalSamples == 0 ? 0 : weightedErrorSum / totalSamples;
        return new ConfidenceCalibrationResult(ece, buckets);
    }

    private sealed class CategoryKeyComparer : IEqualityComparer<(string Expected, string Actual)>
    {
        // Anonymous types use structural equality, but we need case-insensitive comparison
        // for the GroupBy in BuildCategoryConfusionMatrix.
        public bool Equals((string Expected, string Actual) x, (string Expected, string Actual) y) =>
            string.Equals(x.Expected, y.Expected, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Actual, y.Actual, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Expected, string Actual) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Expected),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Actual));
    }
}
