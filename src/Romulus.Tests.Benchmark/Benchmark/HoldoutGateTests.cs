using Romulus.Tests.Benchmark.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace Romulus.Tests.Benchmark;

[Collection("BenchmarkEvaluation")]
public sealed class HoldoutGateTests : IClassFixture<BenchmarkFixture>
{
    private readonly BenchmarkFixture _fixture;
    private readonly ITestOutputHelper _output;

    public HoldoutGateTests(BenchmarkFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    [Trait("Category", "QualityGate")]
    public void Holdout_DriftWithinTolerance()
    {
        var mainResults = EvaluateMainSets();
        var holdoutResults = HoldoutEvaluator.EvaluateHoldout(_fixture);

        var drift = HoldoutEvaluator.CalculateDrift(mainResults, holdoutResults, maxDriftPercent: 5.0);

        _output.WriteLine($"Main Accuracy:    {drift.MainAccuracy:F2}%");
        _output.WriteLine($"Holdout Accuracy: {drift.HoldoutAccuracy:F2}%");
        _output.WriteLine($"Drift:            {drift.DriftPercent:F2}%");
        _output.WriteLine(drift.Summary);

        if (holdoutResults.Count == 0)
        {
            _output.WriteLine("SKIP: No holdout entries found.");
            return;
        }

        var enforce = string.Equals(
            Environment.GetEnvironmentVariable("ROMULUS_ENFORCE_QUALITY_GATES"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        if (!enforce)
        {
            _output.WriteLine("Holdout drift gate is informational only. Set ROMULUS_ENFORCE_QUALITY_GATES=true to enforce.");
            return;
        }

        Assert.True(drift.Passed, drift.Summary);
    }

    [Fact]
    [Trait("Category", "QualityGate")]
    public void Holdout_DetectionRate_AboveMinimum()
    {
        var holdoutResults = HoldoutEvaluator.EvaluateHoldout(_fixture);

        if (holdoutResults.Count == 0)
        {
            _output.WriteLine("SKIP: No holdout entries found.");
            return;
        }

        int correct = holdoutResults.Count(r =>
            r.Verdict is BenchmarkVerdict.Correct or BenchmarkVerdict.Acceptable or BenchmarkVerdict.TrueNegative or BenchmarkVerdict.JunkClassified);
        double rate = 100.0 * correct / holdoutResults.Count;

        _output.WriteLine($"Holdout detection rate: {rate:F2}% ({correct}/{holdoutResults.Count})");

        var enforce = string.Equals(
            Environment.GetEnvironmentVariable("ROMULUS_ENFORCE_QUALITY_GATES"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        if (!enforce)
        {
            _output.WriteLine("Holdout detection-rate gate is informational only. Set ROMULUS_ENFORCE_QUALITY_GATES=true to enforce.");
            return;
        }

        Assert.True(rate >= 20.0, $"Holdout detection rate {rate:F2}% below minimum 20%");
    }

    private List<BenchmarkSampleResult> EvaluateMainSets()
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
