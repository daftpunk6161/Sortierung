using Romulus.Contracts.Models;

namespace Romulus.Tests.Benchmark.Infrastructure;

/// <summary>
/// Evaluates benchmark results at seven quality levels (A-G) per EVALUATION_STRATEGY §2.
/// Each level measures a different aspect of recognition quality with independent accuracy.
/// </summary>
internal static class QualityLevelEvaluator
{
    /// <summary>
    /// Evaluates all seven quality levels and returns a summary per level.
    /// </summary>
    public static IReadOnlyList<QualityLevelResult> Evaluate(IReadOnlyList<BenchmarkSampleResult> results)
    {
        if (results.Count == 0)
            return [];

        return
        [
            EvaluateContainer(results),
            EvaluateSystem(results),
            EvaluateCategory(results),
            EvaluateIdentity(results),
            EvaluateDatMatch(results),
            EvaluateSorting(results),
            EvaluateRepair(results),
        ];
    }

    /// <summary>Level A: Container — did we identify the file type correctly?</summary>
    private static QualityLevelResult EvaluateContainer(IReadOnlyList<BenchmarkSampleResult> results)
    {
        // Container detection is implicit: if the file was evaluated at all, container was parsed.
        // A "Missed" verdict with "Sample file missing" means stub generation failed, not container.
        int evaluated = results.Count(r => r.Verdict != BenchmarkVerdict.Missed);
        double rate = results.Count == 0 ? 0 : 100.0 * evaluated / results.Count;
        return new QualityLevelResult("A", "Container", rate, evaluated, results.Count);
    }

    /// <summary>Level B: System — correct console key?</summary>
    private static QualityLevelResult EvaluateSystem(IReadOnlyList<BenchmarkSampleResult> results)
    {
        var systemRelevant = results.Where(r =>
            r.ExpectedConsoleKey is not null).ToList();

        int correct = systemRelevant.Count(r =>
            r.Verdict is BenchmarkVerdict.Correct or BenchmarkVerdict.Acceptable);
        double rate = systemRelevant.Count == 0 ? 0 : 100.0 * correct / systemRelevant.Count;
        return new QualityLevelResult("B", "System", rate, correct, systemRelevant.Count);
    }

    /// <summary>Level C: Category — GAME/BIOS/JUNK correct?</summary>
    private static QualityLevelResult EvaluateCategory(IReadOnlyList<BenchmarkSampleResult> results)
    {
        var categoryRelevant = results.Where(r =>
            !string.IsNullOrEmpty(r.ExpectedCategory) &&
            !string.Equals(r.ExpectedCategory, "Unknown", StringComparison.OrdinalIgnoreCase)).ToList();

        int correct = categoryRelevant.Count(r =>
            string.Equals(r.ExpectedCategory, r.ActualCategory, StringComparison.OrdinalIgnoreCase));
        double rate = categoryRelevant.Count == 0 ? 0 : 100.0 * correct / categoryRelevant.Count;
        return new QualityLevelResult("C", "Category", rate, correct, categoryRelevant.Count);
    }

    /// <summary>Level D: Identity — correct game identified? (proxy: correct system + category)</summary>
    private static QualityLevelResult EvaluateIdentity(IReadOnlyList<BenchmarkSampleResult> results)
    {
        // Identity = correct system AND correct category (full match proxy)
        var identityRelevant = results.Where(r =>
            r.ExpectedConsoleKey is not null &&
            !string.Equals(r.ExpectedCategory, "Unknown", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(r.ExpectedCategory, "NonRom", StringComparison.OrdinalIgnoreCase)).ToList();

        int correct = identityRelevant.Count(r =>
            (r.Verdict is BenchmarkVerdict.Correct or BenchmarkVerdict.Acceptable) &&
            string.Equals(r.ExpectedCategory, r.ActualCategory, StringComparison.OrdinalIgnoreCase));
        double rate = identityRelevant.Count == 0 ? 0 : 100.0 * correct / identityRelevant.Count;
        return new QualityLevelResult("D", "Identity", rate, correct, identityRelevant.Count);
    }

    /// <summary>Level E: DAT Match — correct DAT verification.</summary>
    private static QualityLevelResult EvaluateDatMatch(IReadOnlyList<BenchmarkSampleResult> results)
    {
        // DAT matching is not evaluated per-sample in current pipeline;
        // aggregate M11 datExactMatchRate from MetricsAggregator covers this.
        // Report as N/A if no DAT metrics available.
        var aggregate = MetricsAggregator.CalculateExtendedAggregate(results);
        double datRate = aggregate.TryGetValue("datExactMatchRate", out var v) ? v * 100 : 0;
        return new QualityLevelResult("E", "DAT-Match", datRate, 0, 0, true);
    }

    /// <summary>Level F: Sorting — correct sort or block decision.</summary>
    private static QualityLevelResult EvaluateSorting(IReadOnlyList<BenchmarkSampleResult> results)
    {
        // Sort is correct when: Correct/Acceptable/TrueNegative/JunkClassified
        // Sort is wrong when: Wrong + sorted, or FalsePositive
        int correctSort = results.Count(r =>
            r.Verdict is BenchmarkVerdict.Correct or BenchmarkVerdict.Acceptable
                or BenchmarkVerdict.TrueNegative or BenchmarkVerdict.JunkClassified);
        int correctBlock = results.Count(r =>
            r.Verdict == BenchmarkVerdict.Missed &&
            r.ActualSortDecision == SortDecision.Blocked);
        int total = results.Count;
        int correct = correctSort + correctBlock;
        double rate = total == 0 ? 0 : 100.0 * correct / total;
        return new QualityLevelResult("F", "Sorting", rate, correct, total);
    }

    /// <summary>Level G: Repair — entries safe for destructive repair operations.</summary>
    private static QualityLevelResult EvaluateRepair(IReadOnlyList<BenchmarkSampleResult> results)
    {
        double m14 = MetricsAggregator.CalculateRepairSafeRate(results);
        return new QualityLevelResult("G", "Repair", m14 * 100, 0, 0, true);
    }
}

/// <summary>
/// Result for a single quality evaluation level (A through G).
/// </summary>
internal sealed record QualityLevelResult(
    string Level,
    string Name,
    double AccuracyPercent,
    int CorrectCount,
    int TotalCount,
    bool IsAggregate = false)
{
    public override string ToString() =>
        IsAggregate
            ? $"Level {Level} ({Name}): {AccuracyPercent:F2}% (aggregate metric)"
            : $"Level {Level} ({Name}): {AccuracyPercent:F2}% ({CorrectCount}/{TotalCount})";
}
