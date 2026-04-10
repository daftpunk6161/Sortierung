using Xunit;
using Xunit.Abstractions;

namespace Romulus.Tests.Benchmark;

/// <summary>
/// Dedicated anti-gaming gate tests per EVALUATION_STRATEGY §10 / Phase 4.
/// Ensures that improvements on known test data don't masquerade as general improvement
/// by monitoring UNKNOWN→WRONG migration and confidence calibration.
/// </summary>
[Collection("BenchmarkEvaluation")]
public sealed class AntiGamingGateTests : IClassFixture<BenchmarkFixture>
{
    private readonly BenchmarkFixture _fixture;
    private readonly ITestOutputHelper _output;

    public AntiGamingGateTests(BenchmarkFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    [Trait("Category", "AntiGaming")]
    public void M15_UnknownToWrongMigration_BelowThreshold()
    {
        var results = EvaluateAllSets();
        var aggregate = MetricsAggregator.CalculateExtendedAggregate(results);

        double unknownRate = aggregate.TryGetValue("unknownRate", out var u) ? u : 0;
        double wrongRate = aggregate.TryGetValue("wrongMatchRate", out var w) ? w : 0;

        _output.WriteLine($"UNKNOWN Rate: {unknownRate:P4}");
        _output.WriteLine($"Wrong Rate:   {wrongRate:P4}");
        _output.WriteLine("Anti-gaming check: A decrease in UNKNOWN must not cause an increase in WRONG.");
        _output.WriteLine($"If UNKNOWN drops significantly while WRONG rises, that's gaming the metrics.");

        // M15 is monitored in BaselineRegressionGateTests with historical baseline.
        // Here we check the absolute wrong rate stays within safe bounds.
        var enforce = IsEnforceMode();
        if (!enforce)
        {
            _output.WriteLine("Anti-gaming gates are informational only. Set ROMULUS_ENFORCE_QUALITY_GATES=true to enforce.");
            return;
        }

        Assert.True(wrongRate <= 0.005, $"M4 wrongMatchRate {wrongRate:P4} exceeds 0.5% hard limit");
    }

    [Fact]
    [Trait("Category", "AntiGaming")]
    public void M16_ConfidenceCalibration_ECE_BelowThreshold()
    {
        var results = EvaluateAllSets();
        var calibration = MetricsAggregator.CalculateConfidenceCalibration(results);

        _output.WriteLine($"ECE (Expected Calibration Error): {calibration.ExpectedCalibrationError:F4}");
        _output.WriteLine($"Buckets: {calibration.Buckets.Count}");

        foreach (var bucket in calibration.Buckets)
        {
            _output.WriteLine($"  [{bucket.LowerBound}-{bucket.UpperBound}]: " +
                $"n={bucket.SampleCount}, correct={bucket.CorrectCount}, " +
                $"acc={bucket.Accuracy:P2}, err={bucket.Error:F4}");
        }

        // ECE < 0.15 means confidence is reasonably calibrated.
        // Higher values indicate the system is either over-confident or under-confident.
        if (!IsEnforceMode())
        {
            _output.WriteLine("Calibration gate is informational only.");
            return;
        }

        Assert.True(calibration.ExpectedCalibrationError < 0.15,
            $"ECE {calibration.ExpectedCalibrationError:F4} exceeds 0.15 threshold — confidence is poorly calibrated");
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

    private static bool IsEnforceMode() =>
        string.Equals(
            Environment.GetEnvironmentVariable("ROMULUS_ENFORCE_QUALITY_GATES"),
            "true",
            StringComparison.OrdinalIgnoreCase);
}
