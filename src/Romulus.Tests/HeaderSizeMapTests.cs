using Romulus.Core.Classification;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Phase 2 TASK-010/014: HeaderSizeMap tests for header skip bytes.
/// Covers: NES (16), SNES (512 conditional), Atari 7800 (128), Atari Lynx (64).
/// </summary>
public sealed class HeaderSizeMapTests
{
    // ── NES (iNES header = 16 bytes) ─────────────────────────────────────
    private static readonly byte[] InesHeader = [0x4E, 0x45, 0x53, 0x1A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

    [Fact]
    public void GetSkipBytes_NES_WithInesHeader_Returns16_Issue9()
    {
        var skip = HeaderSizeMap.GetSkipBytes("NES", InesHeader, 256 * 1024);
        Assert.Equal(16, skip);
    }

    [Fact]
    public void GetSkipBytes_NES_WithoutInesHeader_Returns0_Issue9()
    {
        var noHeader = new byte[] { 0x00, 0x00, 0x00, 0x00 };
        var skip = HeaderSizeMap.GetSkipBytes("NES", noHeader, 256 * 1024);
        Assert.Equal(0, skip);
    }

    [Fact]
    public void GetSkipBytes_NES_CaseInsensitive_Issue9()
    {
        var skip = HeaderSizeMap.GetSkipBytes("nes", InesHeader, 256 * 1024);
        Assert.Equal(16, skip);
    }

    [Fact]
    public void GetSkipBytes_NES_FileSmallerThanHeader_Returns0_Issue9()
    {
        var skip = HeaderSizeMap.GetSkipBytes("NES", InesHeader, 10);
        Assert.Equal(0, skip);
    }

    // ── SNES (copier header = 512 bytes, conditional on fileSize % 1024 == 512) ──

    [Fact]
    public void GetSkipBytes_SNES_WithCopierHeader_Returns512_Issue9()
    {
        // File size with SMC copier header: 256KB + 512 = 262656
        var skip = HeaderSizeMap.GetSkipBytes("SNES", [], 262656);
        Assert.Equal(512, skip);
    }

    [Fact]
    public void GetSkipBytes_SNES_WithoutCopierHeader_Returns0_Issue9()
    {
        // Clean SNES ROM size: exact multiple of 1024
        var skip = HeaderSizeMap.GetSkipBytes("SNES", [], 262144);
        Assert.Equal(0, skip);
    }

    [Fact]
    public void GetSkipBytes_SNES_CaseInsensitive_Issue9()
    {
        var skip = HeaderSizeMap.GetSkipBytes("snes", [], 262656);
        Assert.Equal(512, skip);
    }

    // ── Atari 7800 (A7800 header = 128 bytes) ──────────────────────────

    private static byte[] MakeAtari7800Header()
    {
        var header = new byte[128];
        // "ATARI7800" at byte 1
        var magic = "ATARI7800"u8;
        magic.CopyTo(header.AsSpan(1));
        return header;
    }

    [Fact]
    public void GetSkipBytes_Atari7800_WithMagic_Returns128_Issue9()
    {
        var skip = HeaderSizeMap.GetSkipBytes("ATARI7800", MakeAtari7800Header(), 128 * 1024);
        Assert.Equal(128, skip);
    }

    [Fact]
    public void GetSkipBytes_Atari7800_WithoutMagic_Returns0_Issue9()
    {
        var noMagic = new byte[128];
        var skip = HeaderSizeMap.GetSkipBytes("ATARI7800", noMagic, 128 * 1024);
        Assert.Equal(0, skip);
    }

    // ── Atari Lynx (Lynx header = 64 bytes) ────────────────────────────

    private static readonly byte[] LynxHeader = [0x4C, 0x59, 0x4E, 0x58, 0x00, 0x00, 0x00, 0x00]; // "LYNX"

    [Fact]
    public void GetSkipBytes_AtariLynx_WithMagic_Returns64_Issue9()
    {
        var skip = HeaderSizeMap.GetSkipBytes("ATARILYNX", LynxHeader, 64 * 1024);
        Assert.Equal(64, skip);
    }

    [Fact]
    public void GetSkipBytes_AtariLynx_WithoutMagic_Returns0_Issue9()
    {
        var noMagic = new byte[] { 0x00, 0x00, 0x00, 0x00 };
        var skip = HeaderSizeMap.GetSkipBytes("ATARILYNX", noMagic, 64 * 1024);
        Assert.Equal(0, skip);
    }

    // ── Consoles without mapping ────────────────────────────────────────

    [Theory]
    [InlineData("MD")]
    [InlineData("N64")]
    [InlineData("GBA")]
    [InlineData("GB")]
    [InlineData("PSX")]
    [InlineData("")]
    public void GetSkipBytes_ConsoleWithoutMapping_Returns0_Issue9(string consoleKey)
    {
        var skip = HeaderSizeMap.GetSkipBytes(consoleKey, new byte[16], 1024 * 1024);
        Assert.Equal(0, skip);
    }

    [Fact]
    public void GetSkipBytes_NullConsoleKey_Returns0_Issue9()
    {
        var skip = HeaderSizeMap.GetSkipBytes(null!, [], 1024);
        Assert.Equal(0, skip);
    }

    // ── HasMapping ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("NES", true)]
    [InlineData("SNES", true)]
    [InlineData("ATARI7800", true)]
    [InlineData("ATARILYNX", true)]
    [InlineData("nes", true)]
    [InlineData("snes", true)]
    [InlineData("MD", false)]
    [InlineData("N64", false)]
    [InlineData("GBA", false)]
    [InlineData("", false)]
    public void HasMapping_ReturnsExpected_Issue9(string consoleKey, bool expected)
    {
        Assert.Equal(expected, HeaderSizeMap.HasMapping(consoleKey));
    }

    // ── Determinism ─────────────────────────────────────────────────────

    [Fact]
    public void GetSkipBytes_Deterministic_SameInputsSameOutput_Issue9()
    {
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(16, HeaderSizeMap.GetSkipBytes("NES", InesHeader, 256 * 1024));
            Assert.Equal(512, HeaderSizeMap.GetSkipBytes("SNES", [], 262656));
            Assert.Equal(0, HeaderSizeMap.GetSkipBytes("SNES", [], 262144));
            Assert.Equal(128, HeaderSizeMap.GetSkipBytes("ATARI7800", MakeAtari7800Header(), 128 * 1024));
            Assert.Equal(64, HeaderSizeMap.GetSkipBytes("ATARILYNX", LynxHeader, 64 * 1024));
        }
    }
}
