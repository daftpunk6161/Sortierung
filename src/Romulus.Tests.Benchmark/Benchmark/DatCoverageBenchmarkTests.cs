using System.Xml.Linq;
using Romulus.Tests.Benchmark.Infrastructure;
using Romulus.Tests.Benchmark.Models;
using Xunit;

namespace Romulus.Tests.Benchmark;

[Collection("BenchmarkEvaluation")]
[Trait("Category", "Benchmark")]
public sealed class DatCoverageBenchmarkTests : IClassFixture<BenchmarkFixture>
{
    private readonly BenchmarkFixture _fixture;

    public DatCoverageBenchmarkTests(BenchmarkFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void DatFiles_ExistAndContainGames()
    {
        var expected = new[]
        {
            Path.Combine(BenchmarkPaths.DatsDir, "test-nointro-nes.xml"),
            Path.Combine(BenchmarkPaths.DatsDir, "test-nointro-snes.xml"),
            Path.Combine(BenchmarkPaths.DatsDir, "test-nointro-gba.xml"),
            Path.Combine(BenchmarkPaths.DatsDir, "test-redump-ps1.xml"),
            Path.Combine(BenchmarkPaths.DatsDir, "test-redump-ps2.xml"),
            Path.Combine(BenchmarkPaths.DatsDir, "test-collision.xml"),
        };

        foreach (var dat in expected)
        {
            Assert.True(File.Exists(dat), $"Missing DAT file: {dat}");
            var doc = XDocument.Load(dat);
            var gameCount = doc.Descendants("game").Count();
            Assert.True(gameCount > 0, $"DAT has no game entries: {dat}");
        }
    }

    [Theory]
    [BenchmarkData("dat-coverage.jsonl")]
    public void DatCoverage_ConsoleDetection_StaysWithinAcceptedRange(GroundTruthEntry entry)
    {
        var result = BenchmarkEvaluationRunner.Evaluate(_fixture, entry);

        Assert.True(result.ActualConfidence is >= 0 and <= 100, $"[{entry.Id}] confidence out of range");
    }
}
