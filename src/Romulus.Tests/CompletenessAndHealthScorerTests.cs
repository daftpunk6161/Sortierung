using Romulus.Core.Scoring;
using Romulus.Core.SetParsing;
using Xunit;

namespace Romulus.Tests;

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
        var score = CompletenessScorer.Calculate("game.zip", ".zip", Array.Empty<string>(), 0, datMatch: false);

        Assert.Equal(25, score);
    }

    [Fact]
    public void Completeness_Calculate_StandaloneWithDatMatch_Returns75()
    {
        var score = CompletenessScorer.Calculate("game.zip", ".zip", Array.Empty<string>(), 0, datMatch: true);

        Assert.Equal(75, score);
    }

    [Fact]
    public void Completeness_Calculate_NonDescriptorWithSetMembers_DoesNotAddStandaloneBonus()
    {
        var score = CompletenessScorer.Calculate("game.bin", ".bin", new[] { "track01.bin" }, 0, datMatch: false);

        Assert.Equal(0, score);
    }

    [Fact]
    public void Completeness_Calculate_CueCompleteSet_Returns50()
    {
        var score = CompletenessScorer.Calculate("disc.cue", ".cue", Array.Empty<string>(), 0, datMatch: false);

        Assert.Equal(50, score);
    }

    [Fact]
    public void Completeness_Calculate_CueMissingSet_ReturnsMinus50()
    {
        var score = CompletenessScorer.Calculate("disc-missing.cue", ".cue", Array.Empty<string>(), 1, datMatch: false);

        Assert.Equal(-50, score);
    }

    [Fact]
    public void Completeness_Calculate_GdiMissingSetWithDatMatch_ReturnsZero()
    {
        var score = CompletenessScorer.Calculate("disc.gdi", ".gdi", Array.Empty<string>(), 1, datMatch: true);

        Assert.Equal(0, score);
    }

    [Fact]
    public void Completeness_UsesCentralizedDescriptorExtensions_M3uIsDescriptor()
    {
        Assert.True(SetDescriptorSupport.IsDescriptorExtension(".m3u"));

        var score = CompletenessScorer.Calculate("playlist.m3u", ".m3u", Array.Empty<string>(), 1, datMatch: false);

        Assert.Equal(-50, score);
    }

    [Fact]
    public void Completeness_UsesCentralizedDescriptorExtensions_MdsIsDescriptor()
    {
        Assert.True(SetDescriptorSupport.IsDescriptorExtension(".mds"));

        var score = CompletenessScorer.Calculate("disc.mds", ".mds", Array.Empty<string>(), 1, datMatch: false);

        Assert.Equal(-50, score);
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
    public void Health_GetHealthScore_AllJunk_IsHeavilyPenalized()
    {
        var score = HealthScorer.GetHealthScore(100, dupes: 0, junk: 100, verified: 0);

        Assert.True(score <= 30);
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
