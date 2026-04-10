using Xunit;
using Xunit.Abstractions;

namespace Romulus.Tests.Benchmark;

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
        var enforce = string.Equals(
            Environment.GetEnvironmentVariable("ROMULUS_ENFORCE_QUALITY_GATES"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        var results = EvaluateAllSets();
        var aggregate = MetricsAggregator.CalculateExtendedAggregate(results);

        var m4  = G(aggregate, "wrongMatchRate");
        var m6  = G(aggregate, "falseConfidenceRate");
        var m7  = G(aggregate, "unsafeSortRate");
        var m8  = G(aggregate, "safeSortCoverage");
        var m9  = G(aggregate, "categoryConfusionRate");
        var m9a = G(aggregate, "gameAsJunkRate");
        var m9b = G(aggregate, "biosAsGameRate");
        var m10 = G(aggregate, "maxConsoleConfusionRate");
        var m11 = G(aggregate, "datExactMatchRate");
        var m13 = G(aggregate, "ambiguousMatchRate");
        var m14 = G(aggregate, "repairSafeRate");

        _output.WriteLine($"M4  wrongMatchRate        = {m4:P4}  (target <= 0.5%)");
        _output.WriteLine($"M6  falseConfidenceRate   = {m6:P4}  (target <= 5.0%)");
        _output.WriteLine($"M7  unsafeSortRate        = {m7:P4}  (target <= 0.3%)");
        _output.WriteLine($"M8  safeSortCoverage      = {m8:P4}  (target >= 80.0%)");
        _output.WriteLine($"M9  categoryConfusionRate = {m9:P4}  (target <= 5.0%)");
        _output.WriteLine($"M9a gameAsJunkRate        = {m9a:P4} (target <= 0.1%)");
        _output.WriteLine($"M9b biosAsGameRate        = {m9b:P4} (target <= 0.5%)");
        _output.WriteLine($"M10 maxConsoleConfusion   = {m10:P4} (target <= 2.0%)");
        _output.WriteLine($"M11 datExactMatchRate     = {m11:P4} (info only)");
        _output.WriteLine($"M13 ambiguousMatchRate    = {m13:P4} (target <= 8.0%)");
        _output.WriteLine($"M14 repairSafeRate        = {m14:P4} (info only)");

        if (!enforce)
        {
            _output.WriteLine("Quality gates are informational only. Set ROMULUS_ENFORCE_QUALITY_GATES=true to enforce hard-fail.");
            return;
        }

        Assert.True(m4 <= 0.005, $"M4 failed: {m4:P4} > 0.5%");
        Assert.True(m6 <= 0.05, $"M6 failed: {m6:P4} > 5.0%");
        Assert.True(m7 <= 0.003, $"M7 failed: {m7:P4} > 0.3%");
        Assert.True(m9a <= 0.001, $"M9a failed: {m9a:P4} > 0.1%");
        Assert.True(m9b <= 0.005, $"M9b failed: {m9b:P4} > 0.5%");
        Assert.True(m13 <= 0.08, $"M13 failed: {m13:P4} > 8.0%");

        static double G(IReadOnlyDictionary<string, double> metrics, string key)
            => metrics.TryGetValue(key, out var value) ? value : 0;
    }

    [Fact]
    [Trait("Category", "QualityGate")]
    public void M14_RepairGate_TracksRepairReadiness()
    {
        var results = EvaluateAllSets();
        double m14 = MetricsAggregator.CalculateRepairSafeRate(results);

        _output.WriteLine($"M14 repairSafeRate = {m14:P4}");
        _output.WriteLine($"Repair feature readiness: {(m14 >= 0.70 ? "READY" : "NOT READY")} (threshold >= 70%)");

        // This is informational — the repair feature flag should only be enabled
        // when M14 consistently exceeds 70% across multiple consecutive builds.
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

    /// <summary>
    /// TASK-091: Hard-fail quality gates that always assert regardless of environment variable.
    /// These thresholds represent the minimum quality bar for any release.
    /// </summary>
    [Fact]
    [Trait("Category", "QualityGate")]
    public void QualityGates_HardFail_AlwaysEnforced()
    {
        var results = EvaluateAllSets();
        var dict = MetricsAggregator.CalculateExtendedAggregate(results);
        var metrics = ExtendedMetrics.FromDictionary(dict);

        _output.WriteLine($"M4  wrongMatchRate        = {metrics.WrongMatchRate:P4}");
        _output.WriteLine($"M6  falseConfidenceRate   = {metrics.FalseConfidenceRate:P4}");
        _output.WriteLine($"M7  unsafeSortRate        = {metrics.UnsafeSortRate:P4}");
        _output.WriteLine($"M9a gameAsJunkRate        = {metrics.GameAsJunkRate:P4}");
        _output.WriteLine($"M13 ambiguousMatchRate    = {metrics.AmbiguousMatchRate:P4}");

        Assert.True(metrics.WrongMatchRate <= 0.005, $"M4 hard-fail: {metrics.WrongMatchRate:P4} > 0.5%");
        Assert.True(metrics.FalseConfidenceRate <= 0.05, $"M6 hard-fail: {metrics.FalseConfidenceRate:P4} > 5.0%");
        Assert.True(metrics.UnsafeSortRate <= 0.003, $"M7 hard-fail: {metrics.UnsafeSortRate:P4} > 0.3%");
        Assert.True(metrics.GameAsJunkRate <= 0.001, $"M9a hard-fail: {metrics.GameAsJunkRate:P4} > 0.1%");
        Assert.True(metrics.AmbiguousMatchRate <= 0.08, $"M13 hard-fail: {metrics.AmbiguousMatchRate:P4} > 8.0%");
    }

    /// <summary>
    /// TASK-090: Validates that ExtendedMetrics record correctly maps all keys from the aggregate dictionary.
    /// </summary>
    [Fact]
    [Trait("Category", "QualityGate")]
    public void ExtendedMetrics_FromDictionary_MapsAllFields()
    {
        var results = EvaluateAllSets();
        var dict = MetricsAggregator.CalculateExtendedAggregate(results);
        var metrics = ExtendedMetrics.FromDictionary(dict);

        Assert.Equal(dict["wrongMatchRate"], metrics.WrongMatchRate);
        Assert.Equal(dict["unknownRate"], metrics.UnknownRate);
        Assert.Equal(dict["falseConfidenceRate"], metrics.FalseConfidenceRate);
        Assert.Equal(dict["unsafeSortRate"], metrics.UnsafeSortRate);
        Assert.Equal(dict["safeSortCoverage"], metrics.SafeSortCoverage);
        Assert.Equal(dict["categoryConfusionRate"], metrics.CategoryConfusionRate);
        Assert.Equal(dict["gameAsJunkRate"], metrics.GameAsJunkRate);
        Assert.Equal(dict["biosAsGameRate"], metrics.BiosAsGameRate);
        Assert.Equal(dict["maxConsoleConfusionRate"], metrics.MaxConsoleConfusionRate);
        Assert.Equal(dict["datExactMatchRate"], metrics.DatExactMatchRate);
        Assert.Equal(dict["ambiguousMatchRate"], metrics.AmbiguousMatchRate);
        Assert.Equal(dict["repairSafeRate"], metrics.RepairSafeRate);
    }
}
