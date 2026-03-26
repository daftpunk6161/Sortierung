using RomCleanup.Core.Scoring;
using Xunit;

namespace RomCleanup.Tests;

public class FormatScorerTests
{
    // --- Format Hierarchy ---

    [Theory]
    [InlineData(".chd", 850)]
    [InlineData(".iso", 700)]
    [InlineData(".zip", 500)]
    [InlineData(".7z", 480)]
    [InlineData(".rar", 400)]
    [InlineData(".xyz123", 300)]
    public void GetFormatScore_KnownExtensions(string ext, int expected)
    {
        Assert.Equal(expected, FormatScorer.GetFormatScore(ext));
    }

    // --- Set Types ---

    [Theory]
    [InlineData("M3USET", 900)]
    [InlineData("CUESET", 800)]
    [InlineData("GDISET", 800)]
    [InlineData("CCDSET", 750)]
    public void GetFormatScore_SetTypes(string type, int expected)
    {
        Assert.Equal(expected, FormatScorer.GetFormatScore(".bin", type));
    }

    // --- CHD > ISO > ZIP > RAR ---

    [Fact]
    public void FormatScore_Hierarchy()
    {
        Assert.True(FormatScorer.GetFormatScore(".chd") > FormatScorer.GetFormatScore(".iso"));
        Assert.True(FormatScorer.GetFormatScore(".iso") > FormatScorer.GetFormatScore(".zip"));
        Assert.True(FormatScorer.GetFormatScore(".zip") > FormatScorer.GetFormatScore(".rar"));
        Assert.True(FormatScorer.GetFormatScore(".rar") > FormatScorer.GetFormatScore(".xyz"));
    }

    // --- Region Score ---

    [Fact]
    public void GetRegionScore_PreferredFirst()
    {
        var prefer = new[] { "EU", "US", "JP" };
        Assert.Equal(1000, FormatScorer.GetRegionScore("EU", prefer));
        Assert.Equal(999, FormatScorer.GetRegionScore("US", prefer));
        Assert.Equal(998, FormatScorer.GetRegionScore("JP", prefer));
    }

    [Fact]
    public void GetRegionScore_World_500()
    {
        Assert.Equal(500, FormatScorer.GetRegionScore("WORLD", Array.Empty<string>()));
    }

    [Fact]
    public void GetRegionScore_Unknown_100()
    {
        Assert.Equal(100, FormatScorer.GetRegionScore("UNKNOWN", Array.Empty<string>()));
    }

    // --- Size Tiebreak ---

    [Fact]
    public void SizeTieBreak_DiscFormat_LargerBetter()
    {
        var large = FormatScorer.GetSizeTieBreakScore(null, ".iso", 500_000);
        var small = FormatScorer.GetSizeTieBreakScore(null, ".iso", 100_000);
        Assert.True(large > small);
    }

    [Fact]
    public void SizeTieBreak_M3uPlaylist_LargerBetter()
    {
        var large = FormatScorer.GetSizeTieBreakScore(null, ".m3u", 500_000);
        var small = FormatScorer.GetSizeTieBreakScore(null, ".m3u", 100_000);
        Assert.True(large > small);
    }

    [Fact]
    public void SizeTieBreak_CartridgeFormat_SmallerBetter()
    {
        var large = FormatScorer.GetSizeTieBreakScore(null, ".nes", 500_000);
        var small = FormatScorer.GetSizeTieBreakScore(null, ".nes", 100_000);
        Assert.True(small > large); // negative, so smaller absolute = higher
    }

    // --- Header Variant ---

    [Fact]
    public void HeaderVariant_Headered_Plus10()
    {
        Assert.Equal(10, FormatScorer.GetHeaderVariantScore("roms", "game headered.nes"));
    }

    [Fact]
    public void HeaderVariant_Headerless_Minus10()
    {
        Assert.Equal(-10, FormatScorer.GetHeaderVariantScore("roms", "game headerless.nes"));
    }

    [Fact]
    public void HeaderVariant_NeitherTag_Zero()
    {
        Assert.Equal(0, FormatScorer.GetHeaderVariantScore("roms", "game.nes"));
    }
}
