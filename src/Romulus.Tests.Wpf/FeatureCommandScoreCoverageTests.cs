using Romulus.Contracts.Models;
using Romulus.UI.Wpf.Services;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for FeatureCommandService internal static score helpers.
/// </summary>
public sealed class FeatureCommandScoreCoverageTests
{
    // ═══════════════════════════════════════════
    //  GetCandidateTotalScore
    // ═══════════════════════════════════════════

    [Fact]
    public void GetCandidateTotalScore_AllZero_ReturnsZero()
    {
        var candidate = new RomCandidate();
        Assert.Equal(0L, FeatureCommandService.GetCandidateTotalScore(candidate));
    }

    [Fact]
    public void GetCandidateTotalScore_SumsAllComponents()
    {
        var candidate = new RomCandidate
        {
            RegionScore = 100,
            FormatScore = 50,
            VersionScore = 25,
            HeaderScore = 10,
            CompletenessScore = 5,
            SizeTieBreakScore = 1
        };
        Assert.Equal(191L, FeatureCommandService.GetCandidateTotalScore(candidate));
    }

    [Fact]
    public void GetCandidateTotalScore_NegativeValues_HandledCorrectly()
    {
        var candidate = new RomCandidate
        {
            RegionScore = 100,
            FormatScore = -20,
            VersionScore = -5,
            HeaderScore = 0,
            CompletenessScore = 0,
            SizeTieBreakScore = 0
        };
        Assert.Equal(75L, FeatureCommandService.GetCandidateTotalScore(candidate));
    }

    [Fact]
    public void GetCandidateTotalScore_LargeValues_NoOverflow()
    {
        var candidate = new RomCandidate
        {
            RegionScore = int.MaxValue,
            FormatScore = int.MaxValue,
            VersionScore = long.MaxValue / 4,
            SizeTieBreakScore = 0
        };
        var score = FeatureCommandService.GetCandidateTotalScore(candidate);
        Assert.True(score > 0);
    }

    // ═══════════════════════════════════════════
    //  FormatScoreBreakdown
    // ═══════════════════════════════════════════

    [Fact]
    public void FormatScoreBreakdown_ContainsAllFields()
    {
        var candidate = new RomCandidate
        {
            RegionScore = 100,
            FormatScore = 50,
            VersionScore = 25,
            HeaderScore = 10,
            CompletenessScore = 5,
            SizeTieBreakScore = 1
        };
        var breakdown = FeatureCommandService.FormatScoreBreakdown(candidate);

        Assert.Contains("Region=100", breakdown);
        Assert.Contains("Format=50", breakdown);
        Assert.Contains("Version=25", breakdown);
        Assert.Contains("Header=10", breakdown);
        Assert.Contains("Completeness=5", breakdown);
        Assert.Contains("SizeTieBreak=1", breakdown);
    }

    [Fact]
    public void FormatScoreBreakdown_ZeroValues_ShowsZeros()
    {
        var candidate = new RomCandidate();
        var breakdown = FeatureCommandService.FormatScoreBreakdown(candidate);

        Assert.Contains("Region=0", breakdown);
        Assert.Contains("Format=0", breakdown);
        Assert.Contains("Version=0", breakdown);
    }

    [Fact]
    public void FormatScoreBreakdown_NegativeValues_ShowsNegatives()
    {
        var candidate = new RomCandidate
        {
            RegionScore = -10,
            FormatScore = -5
        };
        var breakdown = FeatureCommandService.FormatScoreBreakdown(candidate);

        Assert.Contains("Region=-10", breakdown);
        Assert.Contains("Format=-5", breakdown);
    }
}
