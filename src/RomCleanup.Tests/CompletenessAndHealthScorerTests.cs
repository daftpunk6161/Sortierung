using RomCleanup.Core.Scoring;
using Xunit;

namespace RomCleanup.Tests;

public sealed class CompletenessAndHealthScorerTests : IDisposable
{
    private readonly string _tempDir;

    public CompletenessAndHealthScorerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Scorer_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Completeness_Calculate_StandaloneWithoutDatMatch_Returns25()
    {
        var score = CompletenessScorer.Calculate("game.zip", ".zip", Array.Empty<string>(), datMatch: false);

        Assert.Equal(25, score);
    }

    [Fact]
    public void Completeness_Calculate_StandaloneWithDatMatch_Returns75()
    {
        var score = CompletenessScorer.Calculate("game.zip", ".zip", Array.Empty<string>(), datMatch: true);

        Assert.Equal(75, score);
    }

    [Fact]
    public void Completeness_Calculate_NonDescriptorWithSetMembers_DoesNotAddStandaloneBonus()
    {
        var score = CompletenessScorer.Calculate("game.bin", ".bin", new[] { "track01.bin" }, datMatch: false);

        Assert.Equal(0, score);
    }

    [Fact]
    public void Completeness_Calculate_CueCompleteSet_Returns50()
    {
        var cuePath = Path.Combine(_tempDir, "disc.cue");
        var trackPath = Path.Combine(_tempDir, "track01.bin");
        File.WriteAllText(trackPath, "dummy");
        File.WriteAllText(cuePath, "FILE \"track01.bin\" BINARY");

        var score = CompletenessScorer.Calculate(cuePath, ".cue", Array.Empty<string>(), datMatch: false);

        Assert.Equal(50, score);
    }

    [Fact]
    public void Completeness_Calculate_CueMissingSet_ReturnsMinus50()
    {
        var cuePath = Path.Combine(_tempDir, "disc-missing.cue");
        File.WriteAllText(cuePath, "FILE \"missing.bin\" BINARY");

        var score = CompletenessScorer.Calculate(cuePath, ".cue", Array.Empty<string>(), datMatch: false);

        Assert.Equal(-50, score);
    }

    [Fact]
    public void Completeness_Calculate_GdiMissingSetWithDatMatch_ReturnsZero()
    {
        var gdiPath = Path.Combine(_tempDir, "disc.gdi");
        File.WriteAllText(gdiPath, "1\n1 0 4 2352 \"track01.bin\" 0\n");

        var score = CompletenessScorer.Calculate(gdiPath, ".gdi", Array.Empty<string>(), datMatch: true);

        Assert.Equal(0, score);
    }

    [Fact]
    public void Health_GetHealthScore_ZeroTotal_ReturnsZero()
    {
        var score = HealthScorer.GetHealthScore(0, dupes: 10, junk: 10, verified: 10);

        Assert.Equal(0, score);
    }

    [Fact]
    public void Health_GetHealthScore_ClampsAtLowerBound()
    {
        var score = HealthScorer.GetHealthScore(100, dupes: 100, junk: 100, verified: 0);

        Assert.Equal(0, score);
    }

    [Fact]
    public void Health_GetHealthScore_ClampsAtUpperBound()
    {
        var score = HealthScorer.GetHealthScore(100, dupes: 0, junk: 0, verified: 100);

        Assert.Equal(100, score);
    }

    [Fact]
    public void Health_GetHealthScore_JunkPenaltyIsCappedAt30()
    {
        var score = HealthScorer.GetHealthScore(100, dupes: 0, junk: 100, verified: 0);

        Assert.Equal(70, score);
    }

    [Fact]
    public void Health_GetHealthScore_VerifiedBonusIsCappedAt10()
    {
        var score = HealthScorer.GetHealthScore(100, dupes: 50, junk: 0, verified: 100);

        Assert.Equal(60, score);
    }

    [Fact]
    public void Health_GetHealthScore_KnownFormulaCase_IsDeterministic()
    {
        var score = HealthScorer.GetHealthScore(100, dupes: 10, junk: 20, verified: 40);

        Assert.Equal(90, score);
    }
}
