using Xunit;
using Romulus.Tests.Benchmark.Infrastructure;

namespace Romulus.Tests.Benchmark;

[Collection("BenchmarkEvaluation")]
public sealed class EvaluationRunnerRedTests : IClassFixture<BenchmarkFixture>
{
    private readonly BenchmarkFixture _fixture;

    public EvaluationRunnerRedTests(BenchmarkFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    [Trait("Category", "RedPhase")]
    public void Should_Return_ResultsSortedById_ForDeterministicEvaluation_Issue9()
    {
        // Arrange
        var setFileName = "tmp-red-eval-order.jsonl";
        var setPath = Path.Combine(BenchmarkPaths.GroundTruthDir, setFileName);
        Directory.CreateDirectory(BenchmarkPaths.GroundTruthDir);

        var lineB =
            "{\"id\":\"zz-NES-ref-002\",\"source\":{\"fileName\":\"MissingB.nes\",\"extension\":\".nes\",\"sizeBytes\":40960},\"tags\":[\"clean-reference\"],\"difficulty\":\"easy\",\"expected\":{\"consoleKey\":\"NES\",\"category\":\"Game\"}}";
        var lineA =
            "{\"id\":\"zz-NES-ref-001\",\"source\":{\"fileName\":\"MissingA.nes\",\"extension\":\".nes\",\"sizeBytes\":40960},\"tags\":[\"clean-reference\"],\"difficulty\":\"easy\",\"expected\":{\"consoleKey\":\"NES\",\"category\":\"Game\"}}";

        File.WriteAllLines(setPath, new[] { lineB, lineA });

        try
        {
            // Act
            var results = BenchmarkEvaluationRunner.EvaluateSet(_fixture, setFileName);

            // Assert
            Assert.Equal("zz-NES-ref-001", results[0].Id);
            Assert.Equal("zz-NES-ref-002", results[1].Id);
        }
        finally
        {
            if (File.Exists(setPath))
            {
                File.Delete(setPath);
            }
        }
    }
}
