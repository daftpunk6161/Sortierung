using Romulus.Core.Scoring;
using Xunit;

namespace Romulus.Tests;

public class VersionScorerTests
{
    private readonly VersionScorer _sut = new();

    // --- Verified Dump ---

    [Fact]
    public void VerifiedDump_Gets500Bonus()
    {
        var withVerified = _sut.GetVersionScore("Game (USA) [!]");
        var without = _sut.GetVersionScore("Game (USA)");
        Assert.Equal(500, withVerified - without);
    }

    // --- Revision Letters ---

    [Fact]
    public void RevisionLetters_HigherRevisionHigherScore()
    {
        var revA = _sut.GetVersionScore("Game (Rev A)");
        var revB = _sut.GetVersionScore("Game (Rev B)");
        var revC = _sut.GetVersionScore("Game (Rev C)");
        Assert.True(revC > revB);
        Assert.True(revB > revA);
    }

    // --- Numeric Revision ---

    [Fact]
    public void NumericRevision_HigherNumberHigherScore()
    {
        var rev1 = _sut.GetVersionScore("Game (Rev 1)");
        var rev2 = _sut.GetVersionScore("Game (Rev 2)");
        Assert.True(rev2 > rev1);
    }

    // --- Version Numbers ---

    [Fact]
    public void VersionNumbers_HigherVersionHigherScore()
    {
        var v10 = _sut.GetVersionScore("Game (v1.0)");
        var v11 = _sut.GetVersionScore("Game (v1.1)");
        var v20 = _sut.GetVersionScore("Game (v2.0)");
        Assert.True(v20 > v11);
        Assert.True(v11 > v10);
    }

    [Fact]
    public void MultiSegmentVersionNumbers_AreScored()
    {
        var v120 = _sut.GetVersionScore("Game (v1.2.0)");
        var v123 = _sut.GetVersionScore("Game (v1.2.3)");

        Assert.True(v120 > 0);
        Assert.True(v123 > v120);
    }

    // --- Language Bonus ---

    [Fact]
    public void EnglishLanguage_GetsBonus()
    {
        var withEn = _sut.GetVersionScore("Game (en,fr)");
        var without = _sut.GetVersionScore("Game (fr,de)");
        Assert.True(withEn > without);
    }

    [Fact]
    public void MultiLanguage_HigherThanSingle()
    {
        var multi = _sut.GetVersionScore("Game (en,fr,de,es)");
        var single = _sut.GetVersionScore("Game (en)");
        Assert.True(multi > single);
    }

    // --- Empty ---

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyInput_ReturnsZero(string input)
    {
        Assert.Equal(0, _sut.GetVersionScore(input));
    }

    // --- PS3 Zero-Padded Versions ---

    [Fact]
    public void ZeroPaddedVersions_ScoreCorrectly()
    {
        var v0100 = _sut.GetVersionScore("Game (v01.00)");
        var v0101 = _sut.GetVersionScore("Game (v01.01)");
        var v0200 = _sut.GetVersionScore("Game (v02.00)");
        Assert.True(v0200 > v0101);
        Assert.True(v0101 > v0100);
    }

    // --- PS3 Extended Language Bonus ---

    [Fact]
    public void ExtendedLanguages_ScoredCorrectly()
    {
        var small = _sut.GetVersionScore("Game (en,fr)");
        var large = _sut.GetVersionScore("Game (en,fr,de,es,it,nl,pt,sv,no,da,fi,eu,ca,gd)");
        Assert.True(large > small);
    }

    [Fact]
    public void AfrikaansLanguage_Recognized()
    {
        var withAf = _sut.GetVersionScore("Game (en,af)");
        var enOnly = _sut.GetVersionScore("Game (en)");
        Assert.True(withAf > enOnly);
    }

    // --- Overflow Protection ---

    [Fact]
    public void DottedRevision_ExtremeNumbers_DoesNotThrow()
    {
        var first = _sut.GetVersionScore("Game (Rev 9999999999.1.1)");
        var second = _sut.GetVersionScore("Game (Rev 9999999999.1.1)");

        Assert.Equal(9_999_999_999_001_001L, first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void NumericRevision_ExtremeNumber_DoesNotThrow()
    {
        var first = _sut.GetVersionScore("Game (Rev 99999999999)");
        var second = _sut.GetVersionScore("Game (Rev 99999999999)");

        Assert.Equal(999_999_999_990L, first);
        Assert.Equal(first, second);
    }
}
