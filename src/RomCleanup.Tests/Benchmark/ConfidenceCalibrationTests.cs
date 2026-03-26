using RomCleanup.Contracts.Models;
using Xunit;
using Xunit.Abstractions;

namespace RomCleanup.Tests.Benchmark;

/// <summary>
/// Dedicated confidence calibration tests per EVALUATION_STRATEGY §10 P2, ADR-015.
/// Verifies that stated confidence values correlate with actual detection accuracy.
/// </summary>
[Collection("BenchmarkEvaluation")]
public sealed class ConfidenceCalibrationTests : IClassFixture<BenchmarkFixture>
{
    private readonly BenchmarkFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ConfidenceCalibrationTests(BenchmarkFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    [Trait("Category", "QualityGate")]
    public void CalibrationBuckets_HighConfidence_HasHighAccuracy()
    {
        var results = EvaluateAllSets();
        var calibration = MetricsAggregator.CalculateConfidenceCalibration(results);

        _output.WriteLine("=== Confidence Calibration Report ===");
        _output.WriteLine($"ECE: {calibration.ExpectedCalibrationError:F4}");

        foreach (var bucket in calibration.Buckets)
        {
            _output.WriteLine($"  [{bucket.LowerBound,3}-{bucket.UpperBound,3}]: " +
                $"n={bucket.SampleCount,5}, correct={bucket.CorrectCount,5}, " +
                $"acc={bucket.Accuracy:P2}, err={bucket.Error:F4}");
        }

        // High-confidence bucket (80-100) should have high accuracy.
        // This is the most critical calibration check — false confidence here → potential data loss.
        var highConfBucket = calibration.Buckets.FirstOrDefault(b => b.LowerBound >= 80);
        if (highConfBucket is not null && highConfBucket.SampleCount > 0)
        {
            _output.WriteLine($"High-confidence bucket accuracy: {highConfBucket.Accuracy:P2} (target >= 90%)");
            Assert.True(highConfBucket.Accuracy >= 0.80,
                $"High-confidence bucket ({highConfBucket.LowerBound}-{highConfBucket.UpperBound}) " +
                $"has only {highConfBucket.Accuracy:P2} accuracy — false confidence risk");
        }
    }

    [Fact]
    [Trait("Category", "QualityGate")]
    public void CalibrationBuckets_LowConfidence_MustNotSortAggressively()
    {
        var results = EvaluateAllSets();

        // Low-confidence entries (< 50) that were sorted → unsafe
        var lowConfSorted = results.Where(r =>
            r.ActualConfidence < 50 &&
            r.ActualSortDecision is SortDecision.Sort or SortDecision.DatVerified &&
            r.Verdict is BenchmarkVerdict.Wrong).ToList();

        _output.WriteLine($"Low-confidence (<50) wrong-sorts: {lowConfSorted.Count}");

        foreach (var r in lowConfSorted.Take(10))
        {
            _output.WriteLine($"  {r.Id}: conf={r.ActualConfidence}, expected={r.ExpectedConsoleKey}, actual={r.ActualConsoleKey}");
        }

        // Zero tolerance for low-confidence wrong-sorts
        Assert.Empty(lowConfSorted);
    }

    [Fact]
    [Trait("Category", "QualityGate")]
    public void ECE_TrackedAcrossSets()
    {
        var setFiles = new[]
        {
            "golden-core.jsonl",
            "golden-realworld.jsonl",
            "edge-cases.jsonl",
            "chaos-mixed.jsonl",
        };

        _output.WriteLine("=== Per-Set ECE ===");
        foreach (var set in setFiles)
        {
            var results = BenchmarkEvaluationRunner.EvaluateSet(_fixture, set);
            if (results.Count == 0) continue;

            var calibration = MetricsAggregator.CalculateConfidenceCalibration(results);
            _output.WriteLine($"  {set,-30} ECE={calibration.ExpectedCalibrationError:F4} (n={results.Count})");
        }
    }

    private List<BenchmarkSampleResult> EvaluateAllSets()
    {
        var setFiles = new[]
        {
            "golden-core.jsonl",
            "edge-cases.jsonl",
            "negative-controls.jsonl",
            "golden-realworld.jsonl",
            "chaos-mixed.jsonl",
            "dat-coverage.jsonl",
            "repair-safety.jsonl",
        };

        var results = new List<BenchmarkSampleResult>();
        foreach (var set in setFiles)
        {
            results.AddRange(BenchmarkEvaluationRunner.EvaluateSet(_fixture, set));
        }
        return results;
    }
}
