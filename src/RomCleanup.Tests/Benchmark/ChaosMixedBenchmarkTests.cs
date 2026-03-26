using RomCleanup.Tests.Benchmark.Models;
using Xunit;

namespace RomCleanup.Tests.Benchmark;

[Collection("BenchmarkEvaluation")]
[Trait("Category", "Benchmark")]
public sealed class ChaosMixedBenchmarkTests : IClassFixture<BenchmarkFixture>
{
    private readonly BenchmarkFixture _fixture;

    public ChaosMixedBenchmarkTests(BenchmarkFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [BenchmarkData("chaos-mixed.jsonl")]
    public void ChaosMixed_Robustness_RespectsGroundTruth(GroundTruthEntry entry)
    {
        var result = BenchmarkEvaluationRunner.Evaluate(_fixture, entry);

        Assert.True(result.ActualConfidence is >= 0 and <= 100, $"[{entry.Id}] confidence out of range");
        Assert.DoesNotContain("Sample file missing", result.Details ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
