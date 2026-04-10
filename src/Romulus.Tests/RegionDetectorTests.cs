using Romulus.Core.Regions;
using Xunit;

namespace Romulus.Tests;

public class RegionDetectorTests
{
    // --- Standard Regions ---

    [Theory]
    [InlineData("Game (Europe)", "EU")]
    [InlineData("Game (EUR)", "EU")]
    [InlineData("Game (PAL)", "EU")]
    [InlineData("Game (USA)", "US")]
    [InlineData("Game (Japan)", "JP")]
    [InlineData("Game (World)", "WORLD")]
    [InlineData("Game (Australia)", "AU")]
    public void StandardRegions_DetectedCorrectly(string input, string expected)
    {
        Assert.Equal(expected, RegionDetector.GetRegionTag(input));
    }

    // --- EU Country Tags ---

    [Theory]
    [InlineData("Game (UK)", "EU")]
    [InlineData("Game (United Kingdom)", "EU")]
    [InlineData("Game (Belgium)", "EU")]
    [InlineData("Game (Portugal)", "EU")]
    public void EuCountryTags_MapToEU(string input, string expected)
    {
        Assert.Equal(expected, RegionDetector.GetRegionTag(input));
    }

    // --- ASIA Country Tags ---

    [Theory]
    [InlineData("Game (Taiwan)", "ASIA")]
    [InlineData("Game (Hong Kong)", "ASIA")]
    [InlineData("Game (India)", "ASIA")]
    [InlineData("Game (CN)", "CN")]
    [InlineData("Game (China)", "CN")]
    public void AsiaAndCnCountryTags_DetectedCorrectly(string input, string expected)
    {
        Assert.Equal(expected, RegionDetector.GetRegionTag(input));
    }

    // --- Mixed Token Regions ---

    [Theory]
    [InlineData("Game (USA,En)", "US")]
    [InlineData("Game (UK) (Fr,De)", "EU")]
    public void MixedTokens_DetectedCorrectly(string input, string expected)
    {
        Assert.Equal(expected, RegionDetector.GetRegionTag(input));
    }

    // --- Edge Cases ---

    [Theory]
    [InlineData("", "UNKNOWN")]
    [InlineData("Random Game Name", "UNKNOWN")]
    public void NoRegion_ReturnsUnknown(string input, string expected)
    {
        Assert.Equal(expected, RegionDetector.GetRegionTag(input));
    }

    // --- Case Insensitivity ---

    [Fact]
    public void CaseInsensitive()
    {
        var lower = RegionDetector.GetRegionTag("game (usa)");
        var upper = RegionDetector.GetRegionTag("GAME (USA)");
        var mixed = RegionDetector.GetRegionTag("Game (USA)");
        Assert.Equal("US", lower);
        Assert.Equal(lower, upper);
        Assert.Equal(upper, mixed);
    }

    // --- Never throws ---

    [Theory]
    [InlineData("!!!@@##$$%%")]
    [InlineData("(((()" + ")")]
    [InlineData("Game (????????)")]
    public void AlienFormats_NeverThrow(string input)
    {
        var ex = Record.Exception(() => RegionDetector.GetRegionTag(input));
        Assert.Null(ex);
    }

    // --- Valid output ---

    [Theory]
    [InlineData("Game (Europe)")]
    [InlineData("Game (USA)")]
    [InlineData("")]
    [InlineData("Some random text")]
    public void Result_IsValidToken(string input)
    {
        var result = RegionDetector.GetRegionTag(input);
        Assert.False(string.IsNullOrWhiteSpace(result));
        // Should be all uppercase or mixed as defined
        Assert.Equal(result, result.ToUpperInvariant());
    }

    // --- PS3 Country Tags ---

    [Theory]
    [InlineData("Game (Spain)", "EU")]
    [InlineData("Game (Italy)", "EU")]
    [InlineData("Game (France)", "EU")]
    [InlineData("Game (Germany)", "EU")]
    [InlineData("Game (Poland)", "PL")]
    [InlineData("Game (Scandinavia)", "EU")]
    [InlineData("Game (Turkey)", "TR")]
    [InlineData("Game (Netherlands)", "EU")]
    [InlineData("Game (Greece)", "EU")]
    [InlineData("Game (Switzerland)", "EU")]
    [InlineData("Game (Denmark)", "EU")]
    [InlineData("Game (Finland)", "EU")]
    [InlineData("Game (Sweden)", "EU")]
    [InlineData("Game (Norway)", "EU")]
    [InlineData("Game (Russia)", "RU")]
    [InlineData("Game (Korea)", "KR")]
    [InlineData("Game (Brazil)", "BR")]
    [InlineData("Game (Canada)", "CA")]
    [InlineData("Game (Latin America)", "LATAM")]
    [InlineData("Game (United Arab Emirates)", "AE")]
    [InlineData("Game (New Zealand)", "AU")]
    [InlineData("Game (South Africa)", "EU")]
    [InlineData("Game (India)", "ASIA")]
    [InlineData("Game (Asia)", "ASIA")]
    public void PS3CountryTags_DetectedCorrectly(string input, string expected)
    {
        Assert.Equal(expected, RegionDetector.GetRegionTag(input));
    }

    // --- PS3 Multi-Region Combos ---

    [Theory]
    [InlineData("Game (USA, Asia)", "WORLD")]
    [InlineData("Game (Japan, Korea)", "WORLD")]
    [InlineData("Game (Europe, Australia)", "WORLD")]
    [InlineData("Game (USA, Europe)", "WORLD")]
    [InlineData("Game (USA, Brazil)", "WORLD")]
    [InlineData("Game (USA, Canada)", "WORLD")]
    [InlineData("Game (Japan, Asia)", "WORLD")]
    [InlineData("Game (USA, Korea)", "WORLD")]
    [InlineData("Game (Europe, Asia)", "WORLD")]
    [InlineData("Game (USA, Japan)", "WORLD")]
    [InlineData("Game (Austria, Switzerland)", "EU")]
    [InlineData("Game (Australia, New Zealand)", "AU")]
    public void PS3MultiRegionCombos_DetectedCorrectly(string input, string expected)
    {
        Assert.Equal(expected, RegionDetector.GetRegionTag(input));
    }

    // --- PS3 Multi-Language Tags ---

    [Theory]
    [InlineData("Game (En,Fr,De,Es,It)")]
    [InlineData("Game (En,Fr,De,Es,It,Nl,Pt,Sv,No,Da,Fi,Pl,Ru)")]
    [InlineData("Game (En,Fr,De,Es,It,Nl,Pt,Sv,No,Da,Fi,Eu,Ca,Gd)")]
    [InlineData("Game (En,Ja,Fr,De,Es,It,Zh,Pl,Ru,Cs,Hu)")]
    [InlineData("Game (Pl,Ru,Cs,Hu)")]
    public void PS3MultiLanguageTags_ReturnWorld(string input)
    {
        Assert.Equal("WORLD", RegionDetector.GetRegionTag(input));
    }

    // --- Edge cases: empty/nested parens ---

    [Theory]
    [InlineData("Game ()")]
    [InlineData("Game ((USA))")]
    public void EdgeCase_EmptyOrNestedParens_NoCrash(string input)
    {
        var result = RegionDetector.GetRegionTag(input);
        Assert.NotNull(result);
    }

    [Fact]
    public void EdgeCase_DuplicateRegion_StillDetected()
    {
        var result = RegionDetector.GetRegionTag("Game (USA) (USA)");
        Assert.Equal("US", result);
    }

    // --- Determinism ---

    [Theory]
    [InlineData("Game (Europe)", "EU")]
    [InlineData("Game (USA)", "US")]
    [InlineData("Game (Japan)", "JP")]
    [InlineData("Game (World)", "WORLD")]
    [InlineData("Game (USA, Europe)", "WORLD")]
    [InlineData("Game (Austria, Switzerland)", "EU")]
    [InlineData("Random Game Name", "UNKNOWN")]
    public void GetRegionTag_IsDeterministic(string input, string expected)
    {
        for (var run = 0; run < 100; run++)
        {
            Assert.Equal(expected, RegionDetector.GetRegionTag(input));
        }
    }

    // --- Token-based resolution returns stable result ---

    [Fact]
    public void ResolveRegionFromTokens_SingleRegion_IsDeterministic()
    {
        // Ensure single-element HashSet always returns same value
        for (var run = 0; run < 100; run++)
        {
            var result = RegionDetector.GetRegionTag("Game (USA) (En)");
            Assert.Equal("US", result);
        }
    }

    [Fact]
    public void GetRegionTagWithDiagnostics_KnownRule_ProvidesReason()
    {
        var result = RegionDetector.GetRegionTagWithDiagnostics("Game (USA)");

        Assert.Equal("US", result.Region);
        Assert.Equal("ordered-rule:US", result.DiagnosticReason);
    }

    [Fact]
    public void GetRegionTagWithDiagnostics_Unknown_ProvidesNoMatchReason()
    {
        var result = RegionDetector.GetRegionTagWithDiagnostics("Completely Unmapped Game Name");

        Assert.Equal("UNKNOWN", result.Region);
        Assert.Equal("no-match", result.DiagnosticReason);
    }
}
