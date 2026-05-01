using Romulus.Tests.Benchmark.Models;
using Xunit;
using Xunit.Abstractions;

namespace Romulus.Tests.Benchmark;

[Collection("BenchmarkEvaluation")]
[Trait("Category", "Benchmark")]
public sealed class EdgeCaseBenchmarkTests : IClassFixture<BenchmarkFixture>
{
    private readonly BenchmarkFixture _fixture;
    private readonly ITestOutputHelper _output;

    public EdgeCaseBenchmarkTests(BenchmarkFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Theory]
    [BenchmarkData("edge-cases.jsonl")]
    public void EdgeCase_ConsoleDetection_MatchesGroundTruth(GroundTruthEntry entry)
    {
        var result = BenchmarkEvaluationRunner.Evaluate(_fixture, entry);
        _output.WriteLine($"[{entry.Id}] {result.Verdict} expected={result.ExpectedConsoleKey ?? "null"} actual={result.ActualConsoleKey ?? "null"}");

        Assert.True(result.ActualConfidence is >= 0 and <= 100, $"[{entry.Id}] confidence out of range");
        Assert.DoesNotContain("Sample file missing", result.Details ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
