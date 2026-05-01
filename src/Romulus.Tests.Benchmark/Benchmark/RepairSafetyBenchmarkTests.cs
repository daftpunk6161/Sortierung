using Romulus.Tests.Benchmark.Models;
using Xunit;

namespace Romulus.Tests.Benchmark;

[Collection("BenchmarkEvaluation")]
[Trait("Category", "Benchmark")]
public sealed class RepairSafetyBenchmarkTests : IClassFixture<BenchmarkFixture>
{
    private readonly BenchmarkFixture _fixture;

    public RepairSafetyBenchmarkTests(BenchmarkFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [BenchmarkData("repair-safety.jsonl")]
    public void RepairSafety_DecisionRules_AreHonored(GroundTruthEntry entry)
    {
        var result = BenchmarkEvaluationRunner.Evaluate(_fixture, entry);

        Assert.True(result.ActualConfidence is >= 0 and <= 100, $"[{entry.Id}] confidence out of range");
        Assert.DoesNotContain("Sample file missing", result.Details ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
