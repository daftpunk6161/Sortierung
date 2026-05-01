using Xunit;

namespace Romulus.Tests.Benchmark;

public sealed class ConfusionMatrixRedTests
{
    [Fact]
    [Trait("Category", "RedPhase")]
    public void Should_Exclude_MissedUnknown_FromConfusionMatrix_Issue9()
    {
        // Arrange
        var results = new List<BenchmarkSampleResult>
        {
            new("m1", BenchmarkVerdict.Missed, "PS2", "UNKNOWN", 0, false, "missed"),
            new("w1", BenchmarkVerdict.Wrong, "NES", "SNES", 87, false, "wrong"),
        };

        // Act
        var matrix = MetricsAggregator.BuildConfusionMatrix(results);

        // Assert
        Assert.DoesNotContain(matrix, x =>
            x.ExpectedSystem == "PS2" &&
            x.ActualSystem == "UNKNOWN");
    }
}
