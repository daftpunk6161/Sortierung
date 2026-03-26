using RomCleanup.Tests.Benchmark.Models;
using Xunit;

namespace RomCleanup.Tests.Benchmark;

[Collection("BenchmarkEvaluation")]
[Trait("Category", "Benchmark")]
public sealed class GoldenRealworldBenchmarkTests : IClassFixture<BenchmarkFixture>
{
    private readonly BenchmarkFixture _fixture;

    public GoldenRealworldBenchmarkTests(BenchmarkFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [BenchmarkData("golden-realworld.jsonl")]
    public void GoldenRealworld_ConsoleDetection_MatchesGroundTruth(GroundTruthEntry entry)
    {
        var result = BenchmarkEvaluationRunner.Evaluate(_fixture, entry);

        Assert.True(result.ActualConfidence is >= 0 and <= 100, $"[{entry.Id}] confidence out of range");
        Assert.DoesNotContain("Sample file missing", result.Details ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
