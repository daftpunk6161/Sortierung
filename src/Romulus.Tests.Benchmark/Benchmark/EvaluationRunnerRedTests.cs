using Xunit;
using Romulus.Tests.Benchmark.Infrastructure;
using Romulus.Contracts.Models;
using Romulus.Core.Classification;
using Romulus.Tests.Benchmark.Models;

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
    public void MergeEnrichmentProjection_UsesCandidateConsoleCategoryAndSortDecision()
    {
        var entry = new GroundTruthEntry
        {
            Id = "red-benchmark-enrichment-projection",
            Source = new SourceInfo
            {
                FileName = "sample.bin",
                Extension = ".bin",
                SizeBytes = 1024
            },
            Tags = ["unit-test"],
            Difficulty = "easy",
            Expected = new ExpectedResult
            {
                ConsoleKey = "PS1",
                Category = "Game",
                Confidence = 0,
                DatMatchLevel = "none",
                DatEcosystem = "none",
                SortDecision = "review"
            }
        };

        var detection = new ConsoleDetectionResult(
            "NES",
            60,
            [],
            false,
            null,
            HasHardEvidence: false,
            IsSoftOnly: true,
            SortDecision: SortDecision.Review,
            DecisionClass: DecisionClass.Review,
            MatchEvidence: new MatchEvidence
            {
                Level = MatchLevel.Probable,
                Reasoning = "detector only",
                Tier = EvidenceTier.Tier3_WeakHeuristic,
                PrimaryMatchKind = MatchKind.FolderNameMatch
            });

        var candidate = new RomCandidate
        {
            ConsoleKey = "PS1",
            DetectionConfidence = 95,
            DetectionConflict = true,
            DetectionConflictType = ConflictType.CrossFamily,
            HasHardEvidence = true,
            IsSoftOnly = false,
            SortDecision = SortDecision.Blocked,
            DecisionClass = DecisionClass.Blocked,
            Category = FileCategory.Bios,
            MatchEvidence = new MatchEvidence
            {
                Level = MatchLevel.Ambiguous,
                Reasoning = "enrichment projection",
                Tier = EvidenceTier.Tier1_Structural,
                PrimaryMatchKind = MatchKind.SerialNumberMatch
            }
        };

        var result = BenchmarkEvaluationRunner.MergeEnrichmentProjection(
            entry,
            detection,
            actualCategory: "Game",
            enrichmentCandidate: candidate);

        Assert.Equal("PS1", result.ActualConsoleKey);
        Assert.Equal(SortDecision.Blocked, result.ActualSortDecision);
        Assert.Equal("Bios", result.ActualCategory);
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
