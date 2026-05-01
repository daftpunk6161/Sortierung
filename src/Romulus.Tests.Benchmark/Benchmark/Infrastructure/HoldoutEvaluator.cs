using Romulus.Tests.Benchmark.Models;

namespace Romulus.Tests.Benchmark.Infrastructure;

/// <summary>
/// Evaluates a holdout dataset (separate from training/tuning data) to detect overfitting.
/// Holdout entries are loaded from a dedicated JSONL file and evaluated against the same
/// detection pipeline. Results are compared against main-set performance to detect drift.
/// </summary>
internal static class HoldoutEvaluator
{
    /// <summary>
    /// Loads holdout entries from the dedicated holdout JSONL file.
    /// Returns empty list if holdout file does not exist (graceful degradation).
    /// </summary>
    public static IReadOnlyList<GroundTruthEntry> LoadHoldoutEntries()
    {
        var holdoutPath = BenchmarkPaths.HoldoutJsonlPath;
        if (!File.Exists(holdoutPath))
            return [];

        return GroundTruthLoader.LoadFile(holdoutPath);
    }

    /// <summary>
    /// Evaluates holdout entries and returns per-entry results.
    /// </summary>
    public static List<BenchmarkSampleResult> EvaluateHoldout(BenchmarkFixture fixture)
    {
        var entries = LoadHoldoutEntries();
        if (entries.Count == 0)
            return [];

        return entries
            .OrderBy(e => e.Id, StringComparer.Ordinal)
            .Select(e => BenchmarkEvaluationRunner.Evaluate(fixture, e))
            .ToList();
    }

    /// <summary>
    /// Compares holdout accuracy against main-set accuracy.
    /// A significant drop (> maxDriftPercent) indicates overfitting.
    /// </summary>
    public static HoldoutDriftResult CalculateDrift(
        IReadOnlyList<BenchmarkSampleResult> mainResults,
        IReadOnlyList<BenchmarkSampleResult> holdoutResults,
        double maxDriftPercent = 5.0)
    {
        if (holdoutResults.Count == 0)
            return new HoldoutDriftResult(0, 0, 0, true, "No holdout entries available");

        double mainAccuracy = CalculateAccuracy(mainResults);
        double holdoutAccuracy = CalculateAccuracy(holdoutResults);
        double drift = mainAccuracy - holdoutAccuracy;
        bool passed = drift <= maxDriftPercent;

        string summary = passed
            ? $"Holdout drift {drift:F1}% within tolerance ({maxDriftPercent}%)"
            : $"OVERFITTING DETECTED: holdout drift {drift:F1}% exceeds tolerance ({maxDriftPercent}%)";

        return new HoldoutDriftResult(mainAccuracy, holdoutAccuracy, drift, passed, summary);
    }

    private static double CalculateAccuracy(IReadOnlyList<BenchmarkSampleResult> results)
    {
        if (results.Count == 0) return 0;
        int correct = results.Count(r =>
            r.Verdict is BenchmarkVerdict.Correct or BenchmarkVerdict.Acceptable);
        return 100.0 * correct / results.Count;
    }
}

internal sealed record HoldoutDriftResult(
    double MainAccuracy,
    double HoldoutAccuracy,
    double DriftPercent,
    bool Passed,
    string Summary);
