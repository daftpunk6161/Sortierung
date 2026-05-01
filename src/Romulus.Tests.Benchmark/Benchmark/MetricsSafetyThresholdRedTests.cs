using Xunit;

namespace Romulus.Tests.Benchmark;

public sealed class MetricsSafetyThresholdRedTests
{
    [Fact]
    [Trait("Category", "RedPhase")]
    public void Should_NotCount_WrongWithConfidence82_AsUnsafeSort_Issue9()
    {
        // Arrange
        var results = new List<BenchmarkSampleResult>
        {
            new("s1", BenchmarkVerdict.Wrong, "NES", "SNES", 82, false, null),
            new("s2", BenchmarkVerdict.Correct, "NES", "NES", 92, false, null),
        };

        // Act
        var aggregate = MetricsAggregator.CalculateAggregate(results);

        // Assert
        Assert.Equal(0d, aggregate["unsafeSortRate"]);
    }
}
