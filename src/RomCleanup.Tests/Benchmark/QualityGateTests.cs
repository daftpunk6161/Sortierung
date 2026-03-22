using Xunit;
using Xunit.Abstractions;

namespace RomCleanup.Tests.Benchmark;

[Collection("BenchmarkEvaluation")]
public sealed class QualityGateTests : IClassFixture<BenchmarkFixture>
{
    private readonly BenchmarkFixture _fixture;
    private readonly ITestOutputHelper _output;

    public QualityGateTests(BenchmarkFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    [Trait("Category", "QualityGate")]
    public void QualityGates_M4_M6_M7_M9a_MustStayWithinThresholds()
    {
        // Default behavior is non-blocking for local/dev runs.
        // In CI gate mode set ROMCLEANUP_ENFORCE_QUALITY_GATES=true.
        var enforce = string.Equals(
            Environment.GetEnvironmentVariable("ROMCLEANUP_ENFORCE_QUALITY_GATES"),
            "true",
            StringComparison.OrdinalIgnoreCase);

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

        var aggregate = MetricsAggregator.CalculateAggregate(results);

        var m4WrongMatchRate = GetMetric(aggregate, "wrongMatchRate");
        var m6FalseConfidenceRate = GetMetric(aggregate, "falseConfidenceRate");
        var m7UnsafeSortRate = GetMetric(aggregate, "unsafeSortRate");
        var m9aGameAsJunkRate = GetMetric(aggregate, "gameAsJunkRate");

        _output.WriteLine($"M4 wrongMatchRate={m4WrongMatchRate:P4} (target <= 0.5%)");
        _output.WriteLine($"M6 falseConfidenceRate={m6FalseConfidenceRate:P4} (target <= 5.0%)");
        _output.WriteLine($"M7 unsafeSortRate={m7UnsafeSortRate:P4} (target <= 0.3%)");
        _output.WriteLine($"M9a gameAsJunkRate={m9aGameAsJunkRate:P4} (target <= 0.1%)");

        if (!enforce)
        {
            _output.WriteLine("Quality gates are informational only. Set ROMCLEANUP_ENFORCE_QUALITY_GATES=true to enforce hard-fail.");
            return;
        }

        Assert.True(m4WrongMatchRate <= 0.005, $"M4 failed: {m4WrongMatchRate:P4} > 0.5%");
        Assert.True(m6FalseConfidenceRate <= 0.05, $"M6 failed: {m6FalseConfidenceRate:P4} > 5.0%");
        Assert.True(m7UnsafeSortRate <= 0.003, $"M7 failed: {m7UnsafeSortRate:P4} > 0.3%");
        Assert.True(m9aGameAsJunkRate <= 0.001, $"M9a failed: {m9aGameAsJunkRate:P4} > 0.1%");

        static double GetMetric(IReadOnlyDictionary<string, double> metrics, string key)
            => metrics.TryGetValue(key, out var value) ? value : 0;
    }
}
