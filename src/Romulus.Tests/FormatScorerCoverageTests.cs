using Xunit;
using Romulus.Core.Scoring;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for FormatScorer: IsDiscExtension, IsKnownFormat,
/// GetFormatScore edge cases, GetSizeTieBreakScore set-type branches,
/// GetRegionScore edge cases.
/// </summary>
public sealed class FormatScorerCoverageTests
{
    #region IsDiscExtension

    [Theory]
    [InlineData(".iso", true)]
    [InlineData(".bin", true)]
    [InlineData(".chd", true)]
    [InlineData(".rvz", true)]
    [InlineData(".gcz", true)]
    [InlineData(".cso", true)]
    [InlineData(".pbp", true)]
    [InlineData(".wbfs", true)]
    [InlineData(".m3u", true)]
    [InlineData(".nsp", true)]
    [InlineData(".xci", true)]
    [InlineData(".nsz", true)]
    [InlineData(".wud", true)]
    [InlineData(".wux", true)]
    [InlineData(".cue", true)]
    [InlineData(".gdi", true)]
    [InlineData(".ccd", true)]
    [InlineData(".img", true)]
    public void IsDiscExtension_KnownDisc_ReturnsTrue(string ext, bool expected)
        => Assert.Equal(expected, FormatScorer.IsDiscExtension(ext));

    [Theory]
    [InlineData(".zip")]
    [InlineData(".7z")]
    [InlineData(".nes")]
    [InlineData(".sfc")]
    [InlineData(".gba")]
    [InlineData(".txt")]
    [InlineData("")]
    public void IsDiscExtension_NonDisc_ReturnsFalse(string ext)
        => Assert.False(FormatScorer.IsDiscExtension(ext));

    #endregion

    #region IsKnownFormat

    [Theory]
    [InlineData(".chd", true)]
    [InlineData(".iso", true)]
    [InlineData(".zip", true)]
    [InlineData(".7z", true)]
    [InlineData(".rar", true)]
    [InlineData(".nes", true)]
    [InlineData(".sfc", true)]
    [InlineData(".gba", true)]
    [InlineData(".nds", true)]
    [InlineData(".ecm", true)]
    [InlineData(".m3u", true)]
    [InlineData(".pkg", true)]
    [InlineData(".rpx", true)]
    [InlineData(".vpk", true)]
    [InlineData(".adf", true)]
    [InlineData(".d64", true)]
    [InlineData(".col", true)]
    [InlineData(".vec", true)]
    [InlineData(".min", true)]
    [InlineData(".tgc", true)]
    [InlineData(".dmg", true)]
    [InlineData(".gxb", true)]
    public void IsKnownFormat_KnownExtension_ReturnsTrue(string ext, bool expected)
        => Assert.Equal(expected, FormatScorer.IsKnownFormat(ext));

    [Theory]
    [InlineData(".unknown")]
    [InlineData(".doc")]
    [InlineData(".pdf")]
    [InlineData("")]
    public void IsKnownFormat_UnknownExtension_ReturnsFalse(string ext)
        => Assert.False(FormatScorer.IsKnownFormat(ext));

    #endregion

    #region GetFormatScore – set type edge cases

    [Theory]
    [InlineData("M3USET", 900)]
    [InlineData("GDISET", 800)]
    [InlineData("CUESET", 800)]
    [InlineData("CCDSET", 750)]
    public void GetFormatScore_SetType_ReturnsExpectedScore(string type, int expected)
        => Assert.Equal(expected, FormatScorer.GetFormatScore(".bin", type));

    [Fact]
    public void GetFormatScore_UnknownSetType_FallsBackToExtension()
    {
        // Type is not null but not a known set type → falls through to extension scoring
        Assert.Equal(850, FormatScorer.GetFormatScore(".chd", "UNKNOWNSET"));
    }

    [Fact]
    public void GetFormatScore_NullType_UsesExtension()
        => Assert.Equal(850, FormatScorer.GetFormatScore(".chd", null));

    [Theory]
    [InlineData(".cso", 680)]
    [InlineData(".pbp", 680)]
    [InlineData(".gcz", 680)]
    [InlineData(".rvz", 680)]
    [InlineData(".wia", 670)]
    [InlineData(".wbf1", 660)]
    [InlineData(".wbfs", 650)]
    [InlineData(".nsp", 650)]
    [InlineData(".xci", 650)]
    [InlineData(".3ds", 650)]
    [InlineData(".wud", 650)]
    [InlineData(".wux", 650)]
    [InlineData(".dax", 650)]
    [InlineData(".jso", 650)]
    [InlineData(".zso", 650)]
    [InlineData(".rpx", 645)]
    [InlineData(".pkg", 645)]
    [InlineData(".cia", 640)]
    [InlineData(".nsz", 640)]
    [InlineData(".xcz", 640)]
    [InlineData(".nrg", 620)]
    [InlineData(".mdf", 610)]
    [InlineData(".mds", 610)]
    [InlineData(".cdi", 610)]
    [InlineData(".ecm", 550)]
    [InlineData(".m3u", 800)]
    [InlineData(".gdi", 790)]
    [InlineData(".cue", 790)]
    [InlineData(".ccd", 780)]
    public void GetFormatScore_CompressedAndContainerFormats_ReturnExpectedScores(string ext, int expected)
        => Assert.Equal(expected, FormatScorer.GetFormatScore(ext));

    [Theory]
    [InlineData(".nds", 600)]
    [InlineData(".gba", 600)]
    [InlineData(".gbc", 600)]
    [InlineData(".gb", 600)]
    [InlineData(".sfc", 600)]
    [InlineData(".smc", 600)]
    [InlineData(".n64", 600)]
    [InlineData(".z64", 600)]
    [InlineData(".v64", 600)]
    [InlineData(".md", 600)]
    [InlineData(".gen", 600)]
    [InlineData(".sms", 600)]
    [InlineData(".gg", 600)]
    [InlineData(".pce", 600)]
    [InlineData(".fds", 600)]
    [InlineData(".32x", 600)]
    [InlineData(".a26", 600)]
    [InlineData(".lnx", 600)]
    [InlineData(".jag", 600)]
    [InlineData(".snes", 600)]
    [InlineData(".ngp", 600)]
    [InlineData(".ws", 600)]
    [InlineData(".wsc", 600)]
    [InlineData(".vb", 600)]
    [InlineData(".ndd", 600)]
    [InlineData(".col", 600)]
    [InlineData(".int", 600)]
    [InlineData(".o2", 600)]
    [InlineData(".vec", 600)]
    [InlineData(".min", 600)]
    [InlineData(".tgc", 600)]
    [InlineData(".vpk", 600)]
    [InlineData(".dmg", 600)]
    [InlineData(".gxb", 600)]
    public void GetFormatScore_NativeDumpFormats_Return600(string ext, int expected)
        => Assert.Equal(expected, FormatScorer.GetFormatScore(ext));

    [Fact]
    public void GetFormatScore_UnknownExtension_Returns300()
        => Assert.Equal(300, FormatScorer.GetFormatScore(".xyz"));

    #endregion

    #region GetSizeTieBreakScore – set type branches

    [Theory]
    [InlineData("M3USET")]
    [InlineData("GDISET")]
    [InlineData("CUESET")]
    [InlineData("CCDSET")]
    [InlineData("DOSDIR")]
    public void GetSizeTieBreakScore_SetType_ReturnsPositiveSize(string type)
    {
        var score = FormatScorer.GetSizeTieBreakScore(type, ".bin", 1024);
        Assert.Equal(1024, score);
    }

    [Fact]
    public void GetSizeTieBreakScore_DiscExtension_ReturnsPositiveSize()
        => Assert.Equal(2048L, FormatScorer.GetSizeTieBreakScore(null, ".iso", 2048));

    [Fact]
    public void GetSizeTieBreakScore_CartridgeExtension_ReturnsNegativeSize()
        => Assert.Equal(-512L, FormatScorer.GetSizeTieBreakScore(null, ".nes", 512));

    [Fact]
    public void GetSizeTieBreakScore_NullExtension_ReturnsNegativeSize()
        => Assert.Equal(-100L, FormatScorer.GetSizeTieBreakScore(null, null, 100));

    #endregion

    #region GetRegionScore – edge cases

    [Fact]
    public void GetRegionScore_NotInPreferOrder_FallbackByRegionName()
    {
        var order = new[] { "US", "EU" };
        Assert.Equal(200, FormatScorer.GetRegionScore("JP", order)); // not in list, not WORLD/UNKNOWN
    }

    [Fact]
    public void GetRegionScore_World_Returns500()
        => Assert.Equal(500, FormatScorer.GetRegionScore("WORLD", new[] { "US" }));

    [Fact]
    public void GetRegionScore_Unknown_Returns100()
        => Assert.Equal(100, FormatScorer.GetRegionScore("UNKNOWN", new[] { "US" }));

    [Fact]
    public void GetRegionScore_EmptyPreferOrder_StillFallsBack()
        => Assert.Equal(200, FormatScorer.GetRegionScore("JP", Array.Empty<string>()));

    [Fact]
    public void GetRegionScore_CaseInsensitiveMatch()
    {
        var order = new[] { "us", "eu" };
        Assert.Equal(1000, FormatScorer.GetRegionScore("US", order));
    }

    #endregion

    #region GetHeaderVariantScore – edge cases

    [Fact]
    public void GetHeaderVariantScore_RootContainsHeadered_Returns10()
        => Assert.Equal(10, FormatScorer.GetHeaderVariantScore(@"C:\roms\headered", @"C:\roms\headered\Game.nes"));

    [Fact]
    public void GetHeaderVariantScore_PathContainsHeaderless_ReturnsMinus10()
        => Assert.Equal(-10, FormatScorer.GetHeaderVariantScore(@"C:\roms", @"C:\roms\headerless\Game.nes"));

    [Fact]
    public void GetHeaderVariantScore_NoHeaderTag_ReturnsZero()
        => Assert.Equal(0, FormatScorer.GetHeaderVariantScore(@"C:\roms", @"C:\roms\Game.nes"));

    [Fact]
    public void GetHeaderVariantScore_CaseInsensitive()
        => Assert.Equal(10, FormatScorer.GetHeaderVariantScore(@"C:\ROMS\HEADERED", @"C:\ROMS\HEADERED\Game.nes"));

    #endregion
}
