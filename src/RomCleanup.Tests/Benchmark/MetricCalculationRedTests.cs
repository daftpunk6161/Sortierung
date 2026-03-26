using Xunit;

namespace RomCleanup.Tests.Benchmark;

public sealed class MetricCalculationRedTests
{
    [Fact]
    [Trait("Category", "RedPhase")]
    public void Should_Expose_FalseConfidenceRate_InAggregateMetrics_Issue9()
    {
        // Arrange
        var results = new List<BenchmarkSampleResult>
        {
            new("w1", BenchmarkVerdict.Wrong, "NES", "SNES", 90, false, null),
            new("c1", BenchmarkVerdict.Correct, "NES", "NES", 90, false, null),
        };

        // Act
        var aggregate = MetricsAggregator.CalculateAggregate(results);

        // Assert
        Assert.True(aggregate.ContainsKey("falseConfidenceRate"),
            "Aggregate metrics should include falseConfidenceRate for high-confidence wrong detections.");
    }
}
