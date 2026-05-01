using Xunit;

namespace Romulus.Tests.Benchmark;

public sealed class RegressionDetectionRedTests
{
    [Fact]
    [Trait("Category", "RedPhase")]
    public void Should_Flag_Regression_When_WrongRateRises_InUnknownBucket_Issue9()
    {
        // Arrange
        var baseline = new BenchmarkReport(
            Timestamp: DateTimeOffset.UtcNow.AddDays(-1),
            GroundTruthVersion: BenchmarkReportWriter.GroundTruthVersion,
            TotalSamples: 100,
            Correct: 99,
            Acceptable: 0,
            Wrong: 1,
            Missed: 0,
            TrueNegative: 0,
            JunkClassified: 0,
            FalsePositive: 0,
            WrongMatchRate: 0.01,
            UnsafeSortRate: 0.0,
            PerSystem: new Dictionary<string, BenchmarkSystemSummary>(StringComparer.OrdinalIgnoreCase)
            {
                ["NES"] = new BenchmarkSystemSummary(99, 0, 1, 0, 0, 0, 0),
            },
            AggregateMetrics: new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
            ConfusionMatrix: []);

        var current = new BenchmarkReport(
            Timestamp: DateTimeOffset.UtcNow,
            GroundTruthVersion: "1.0.1",
            TotalSamples: 100,
            Correct: 98,
            Acceptable: 0,
            Wrong: 1,
            Missed: 0,
            TrueNegative: 0,
            JunkClassified: 0,
            FalsePositive: 1,
            WrongMatchRate: 0.02,
            UnsafeSortRate: 0.01,
            PerSystem: new Dictionary<string, BenchmarkSystemSummary>(StringComparer.OrdinalIgnoreCase)
            {
                ["NES"] = new BenchmarkSystemSummary(98, 0, 1, 0, 0, 0, 0),
                ["UNKNOWN"] = new BenchmarkSystemSummary(0, 0, 0, 0, 0, 0, 1),
            },
            AggregateMetrics: new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
            ConfusionMatrix: []);

        var baselinePath = Path.GetTempFileName();

        try
        {
            BenchmarkReportWriter.Write(baseline, baselinePath);

            // Act
            var regression = BaselineComparator.Compare(current, baselinePath);

            // Assert
            Assert.True(regression.PerSystemRegressions.Count > 0,
                "Regression in UNKNOWN bucket should be flagged when wrong match rate rises.");
        }
        finally
        {
            File.Delete(baselinePath);
        }
    }
}
