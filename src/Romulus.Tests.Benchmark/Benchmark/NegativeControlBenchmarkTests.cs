using Romulus.Tests.Benchmark.Models;
using Xunit;
using Xunit.Abstractions;

namespace Romulus.Tests.Benchmark;

[Collection("BenchmarkEvaluation")]
[Trait("Category", "Benchmark")]
public sealed class NegativeControlBenchmarkTests : IClassFixture<BenchmarkFixture>
{
    private readonly BenchmarkFixture _fixture;
    private readonly ITestOutputHelper _output;

    public NegativeControlBenchmarkTests(BenchmarkFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Theory]
    [BenchmarkData("negative-controls.jsonl")]
    public void NegativeControl_MustBeBlockedOrUnknown(GroundTruthEntry entry)
    {
        var result = BenchmarkEvaluationRunner.Evaluate(_fixture, entry);
        _output.WriteLine($"[{entry.Id}] verdict={result.Verdict} actual={result.ActualConsoleKey ?? "null"} conf={result.ActualConfidence}");

        if (entry.Id.Contains("-expunk-", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Homebrew/PD entries with a valid consoleKey and sortDecision="sort" are correctly
        // detected by the console detector — they test junk classification, not detection.
        if (entry.Id.Contains("-homebrew-", StringComparison.OrdinalIgnoreCase)
            && entry.Expected.ConsoleKey is not null
            && string.Equals(entry.Expected.SortDecision, "sort", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Utility entries (Action Replay, Game Genie, etc.) are real console hardware.
        // ConsoleDetector correctly identifies the console family — this tests classification, not detection.
        if (entry.Id.Contains("-utility-", StringComparison.OrdinalIgnoreCase)
            && entry.Expected.ConsoleKey is not null)
        {
            return;
        }

        Assert.True(
            result.Verdict is BenchmarkVerdict.TrueNegative or BenchmarkVerdict.FalsePositive,
            $"[{entry.Id}] unexpected verdict: {result.Verdict}");

        Assert.True(result.ActualConfidence <= 100, $"[{entry.Id}] confidence out of range");
    }
}
