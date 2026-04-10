using System.Buffers.Binary;
using System.Text;
using Romulus.Core.Classification;
using Xunit;

namespace Romulus.Tests;

public class DiscHeaderDetectorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DiscHeaderDetector _detector;

    public DiscHeaderDetectorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DiscHeaderTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _detector = new DiscHeaderDetector(isoCacheSize: 64, chdCacheSize: 32);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // ── Helper: create a disc image with specific bytes ──

    private string CreateImage(string name, byte[] data)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, data);
        return path;
    }

    private static byte[] MakeBuffer(int size) => new byte[size];

    // ── Null / missing / too small ──

    [Fact]
    public void DetectFromDiscImage_Null_ReturnsNull()
    {
        Assert.Null(_detector.DetectFromDiscImage(null!));
    }

    [Fact]
    public void DetectFromDiscImage_Missing_ReturnsNull()
    {
        Assert.Null(_detector.DetectFromDiscImage(Path.Combine(_tempDir, "nope.iso")));
    }

    [Fact]
    public void DetectFromDiscImage_TooSmall_ReturnsNull()
    {
        var path = CreateImage("tiny.iso", new byte[16]);
        Assert.Null(_detector.DetectFromDiscImage(path));
    }

    [Fact]
    public void DetectFromChd_Null_ReturnsNull()
    {
        Assert.Null(_detector.DetectFromChd(null!));
    }

    [Fact]
    public void DetectFromChd_Missing_ReturnsNull()
    {
        Assert.Null(_detector.DetectFromChd(Path.Combine(_tempDir, "nope.chd")));
    }

    // ── GameCube magic (0xC2339F3D at offset 0x1C) ──

    [Fact]
    public void DetectFromDiscImage_GC_ByMagic()
    {
        var data = MakeBuffer(4096);
        data[0x1C] = 0xC2;
        data[0x1D] = 0x33;
        data[0x1E] = 0x9F;
        data[0x1F] = 0x3D;
        var path = CreateImage("game.gcm", data);
        Assert.Equal("GC", _detector.DetectFromDiscImage(path));
    }

    // ── Wii magic (0x5D1C9EA3 at offset 0x18) ──

    [Fact]
    public void DetectFromDiscImage_Wii_ByMagic()
    {
        var data = MakeBuffer(4096);
        data[0x18] = 0x5D;
        data[0x19] = 0x1C;
        data[0x1A] = 0x9E;
        data[0x1B] = 0xA3;
        var path = CreateImage("game.iso", data);
        Assert.Equal("WII", _detector.DetectFromDiscImage(path));
    }

    // ── 3DO Opera (0x01 + 5×0x5A) ──

    [Fact]
    public void DetectFromDiscImage_3DO_ByMagic()
    {
        var data = MakeBuffer(4096);
        data[0] = 0x01;
        data[1] = 0x5A;
        data[2] = 0x5A;
        data[3] = 0x5A;
        data[4] = 0x5A;
        data[5] = 0x5A;
        data[6] = 0x01; // Opera FS volume record version
        var path = CreateImage("game.iso", data);
        Assert.Equal("3DO", _detector.DetectFromDiscImage(path));
    }

    // ── Xbox "MICROSOFT*XBOX*MEDIA" at offset 0x10000 ──

    [Fact]
    public void DetectFromDiscImage_Xbox_ByXdvdfs()
    {
        var data = MakeBuffer(0x10000 + 32);
        var sig = Encoding.ASCII.GetBytes("MICROSOFT*XBOX*MEDIA");
        Array.Copy(sig, 0, data, 0x10000, sig.Length);
        var path = CreateImage("game.iso", data);
        Assert.Equal("XBOX", _detector.DetectFromDiscImage(path));
    }

    // ── Dreamcast IP.BIN at offset 0x0000 ──

    [Fact]
    public void DetectFromDiscImage_Dreamcast_IpBin()
    {
        var data = MakeBuffer(4096);
        var seg = Encoding.ASCII.GetBytes("SEGA SEGAKATANA SEGA ENTERPRISES");
        Array.Copy(seg, 0, data, 0, seg.Length);
        var path = CreateImage("game.bin", data);
        Assert.Equal("DC", _detector.DetectFromDiscImage(path));
    }

    // ── Saturn IP.BIN at offset 0x0000 ──

    [Fact]
    public void DetectFromDiscImage_Saturn_IpBin()
    {
        var data = MakeBuffer(4096);
        var seg = Encoding.ASCII.GetBytes("SEGA SATURN  SEGA ENTERPRISES");
        Array.Copy(seg, 0, data, 0, seg.Length);
        var path = CreateImage("game.bin", data);
        Assert.Equal("SAT", _detector.DetectFromDiscImage(path));
    }

    // ── Sega CD at offset 0x0010 (raw sector) ──

    [Fact]
    public void DetectFromDiscImage_SegaCD_IpBinRaw()
    {
        var data = MakeBuffer(4096);
        var seg = Encoding.ASCII.GetBytes("SEGADISCSYSTEM");
        Array.Copy(seg, 0, data, 0x0010, seg.Length);
        var path = CreateImage("game.bin", data);
        Assert.Equal("SCD", _detector.DetectFromDiscImage(path));
    }

    // ── PS1 via ISO9660 PVD at 0x8000 ──

    [Fact]
    public void DetectFromDiscImage_PS1_PVD()
    {
        var data = MakeBuffer(0x8000 + 64);
        // PVD magic: 0x01 + "CD001"
        data[0x8000] = 0x01;
        Encoding.ASCII.GetBytes("CD001").CopyTo(data, 0x8001);
        // System Identifier at PVD+8: "PLAYSTATION"
        Encoding.ASCII.GetBytes("PLAYSTATION").CopyTo(data, 0x8008);
        var path = CreateImage("game.bin", data);
        Assert.Equal("PS1", _detector.DetectFromDiscImage(path));
    }

    // ── PS2 via PVD + "BOOT2 =" marker ──

    [Fact]
    public void DetectFromDiscImage_PS2_PVD()
    {
        var data = MakeBuffer(0x8000 + 256);
        data[0x8000] = 0x01;
        Encoding.ASCII.GetBytes("CD001").CopyTo(data, 0x8001);
        Encoding.ASCII.GetBytes("PLAYSTATION").CopyTo(data, 0x8008);
        Encoding.ASCII.GetBytes("BOOT2 =").CopyTo(data, 0x8000 + 64);
        var path = CreateImage("game.iso", data);
        Assert.Equal("PS2", _detector.DetectFromDiscImage(path));
    }

    // ── PSP via PVD + "PSP GAME" marker ──

    [Fact]
    public void DetectFromDiscImage_PSP_PVD()
    {
        var data = MakeBuffer(0x8000 + 256);
        data[0x8000] = 0x01;
        Encoding.ASCII.GetBytes("CD001").CopyTo(data, 0x8001);
        Encoding.ASCII.GetBytes("PLAYSTATION").CopyTo(data, 0x8008);
        Encoding.ASCII.GetBytes("PSP GAME").CopyTo(data, 0x8000 + 64);
        var path = CreateImage("game.iso", data);
        Assert.Equal("PSP", _detector.DetectFromDiscImage(path));
    }

    // ── FM Towns via PVD System ID ──

    [Fact]
    public void DetectFromDiscImage_FMTowns_PVD()
    {
        var data = MakeBuffer(0x8000 + 64);
        data[0x8000] = 0x01;
        Encoding.ASCII.GetBytes("CD001").CopyTo(data, 0x8001);
        Encoding.ASCII.GetBytes("FM TOWNS").CopyTo(data, 0x8008);
        var path = CreateImage("game.iso", data);
        Assert.Equal("FMTOWNS", _detector.DetectFromDiscImage(path));
    }

    // ── Boot sector keyword ──

    [Theory]
    [InlineData("NEOGEO CD", "NEOCD")]
    [InlineData("PC-FX:Hu_CD", "PCFX")]
    [InlineData("PC Engine", "PCECD")]
    [InlineData("ATARI JAGUAR", "JAGCD")]
    [InlineData("AMIGA BOOT CD32", "CD32")]
    public void DetectFromDiscImage_BootSectorKeyword(string keyword, string expected)
    {
        var data = MakeBuffer(8192);
        Encoding.ASCII.GetBytes(keyword).CopyTo(data, 100);
        var path = CreateImage("game.bin", data);
        Assert.Equal(expected, _detector.DetectFromDiscImage(path));
    }

    // ── CHD metadata detection ──

    [Fact]
    public void DetectFromChd_Dreamcast()
    {
        var data = MakeBuffer(4096);
        // CHD magic "MComprHD"
        Encoding.ASCII.GetBytes("MComprHD").CopyTo(data, 0);
        // Embedded platform string
        Encoding.ASCII.GetBytes("SEGA DREAMCAST Game Data").CopyTo(data, 128);
        var path = CreateImage("game.chd", data);
        Assert.Equal("DC", _detector.DetectFromChd(path));
    }

    [Fact]
    public void DetectFromChd_Saturn()
    {
        var data = MakeBuffer(4096);
        Encoding.ASCII.GetBytes("MComprHD").CopyTo(data, 0);
        Encoding.ASCII.GetBytes("SEGA SATURN").CopyTo(data, 200);
        var path = CreateImage("game.chd", data);
        Assert.Equal("SAT", _detector.DetectFromChd(path));
    }

    [Fact]
    public void DetectFromChd_V5MetaOffset_PS2()
    {
        var data = MakeBuffer(220_000);
        Encoding.ASCII.GetBytes("MComprHD").CopyTo(data, 0);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(12, 4), 5);

        const ulong metaOffset = 180_000;
        BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan(0x30, 8), metaOffset);

        // Marker only in metadata region; this verifies offset-based parsing.
        Encoding.ASCII.GetBytes("Sony Computer Entertainment BOOT2 = cdrom0:").CopyTo(data, (int)metaOffset + 64);

        var path = CreateImage("game-v5.chd", data);
        Assert.Equal("PS2", _detector.DetectFromChd(path));
    }

    [Fact]
    public void DetectFromChd_InvalidMagic_ReturnsNull()
    {
        var data = MakeBuffer(4096);
        Encoding.ASCII.GetBytes("NOTACHD!").CopyTo(data, 0);
        var path = CreateImage("game.chd", data);
        Assert.Null(_detector.DetectFromChd(path));
    }

    [Fact]
    public void DetectFromChd_TooSmall_ReturnsNull()
    {
        var path = CreateImage("tiny.chd", new byte[4]);
        Assert.Null(_detector.DetectFromChd(path));
    }

    // ── Caching ──

    [Fact]
    public void DetectFromDiscImage_ResultIsCached()
    {
        var data = MakeBuffer(4096);
        data[0x1C] = 0xC2; data[0x1D] = 0x33; data[0x1E] = 0x9F; data[0x1F] = 0x3D;
        var path = CreateImage("cached.gcm", data);
        var first = _detector.DetectFromDiscImage(path);
        var second = _detector.DetectFromDiscImage(path);
        Assert.Equal("GC", first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void DetectFromChd_ResultIsCached()
    {
        var data = MakeBuffer(4096);
        Encoding.ASCII.GetBytes("MComprHD").CopyTo(data, 0);
        Encoding.ASCII.GetBytes("SEGA SATURN").CopyTo(data, 200);
        var path = CreateImage("cached.chd", data);
        var first = _detector.DetectFromChd(path);
        var second = _detector.DetectFromChd(path);
        Assert.Equal("SAT", first);
        Assert.Equal(first, second);
    }

    // ── Batch detection ──

    [Fact]
    public void DetectBatch_MixedExtensions()
    {
        // GC .gcm
        var gcData = MakeBuffer(4096);
        gcData[0x1C] = 0xC2; gcData[0x1D] = 0x33; gcData[0x1E] = 0x9F; gcData[0x1F] = 0x3D;
        var gcPath = CreateImage("game1.gcm", gcData);

        // CHD with DC metadata
        var chdData = MakeBuffer(4096);
        Encoding.ASCII.GetBytes("MComprHD").CopyTo(chdData, 0);
        Encoding.ASCII.GetBytes("SEGA DREAMCAST").CopyTo(chdData, 128);
        var chdPath = CreateImage("game2.chd", chdData);

        // Unsupported extension
        var txtPath = CreateImage("notes.txt", Encoding.UTF8.GetBytes("hello"));

        var results = _detector.DetectBatch(new[] { gcPath, chdPath, txtPath });

        Assert.Equal(3, results.Count);
        Assert.Equal("GC", results[gcPath]);
        Assert.Equal("DC", results[chdPath]);
        Assert.Null(results[txtPath]);
    }

    [Fact]
    public void DetectBatch_SkipsNullAndEmpty()
    {
        var results = _detector.DetectBatch(new[] { null!, "", "   " });
        Assert.Empty(results);
    }

    // ── ResolveConsoleFromText static tests ──

    [Theory]
    [InlineData("SEGA SEGAKATANA blah", "DC")]
    [InlineData("sega dreamcast game", "DC")]
    [InlineData("SEGA SATURN v1.0", "SAT")]
    [InlineData("SEGADISCSYSTEM", "SCD")]
    [InlineData("SEGA MEGA CD something", "SCD")]
    [InlineData("NEOGEO CD player", "NEOCD")]
    [InlineData("PC-FX:Hu_CD-ROM game", "PCFX")]
    [InlineData("NEC PC-FX something", "PCFX")]
    [InlineData("PC Engine CD data", "PCECD")]
    [InlineData("turbografx game", "PCECD")]
    [InlineData("ATARI JAGUAR CD game", "JAGCD")]
    [InlineData("CDTV disc image", "CDTV")]
    [InlineData("AMIGA BOOT stuff", "CD32")]
    [InlineData("CD32 game disc", "CD32")]
    [InlineData("CD-RTOS disc data", "CDI")]
    [InlineData("PHILIPS CD-I application", "CDI")]
    [InlineData("FM TOWNS application", "FMTOWNS")]
    [InlineData("Sony Computer Entertainment Inc.", "PS1")]
    [InlineData("PLAYSTATION PSP GAME disc", "PSP")]
    [InlineData("PLAYSTATION BOOT2 = cdrom0", "PS2")]
    [InlineData("playstation 2 game BOOT2 =", "PS2")]
    [InlineData("PLAYSTATION game only", "PS1")]
    [InlineData("MICROSOFT*XBOX*MEDIA", "XBOX")]
    public void ResolveConsoleFromText_Patterns(string text, string expected)
    {
        Assert.Equal(expected, DiscHeaderDetector.ResolveConsoleFromText(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("random text with no console markers")]
    public void ResolveConsoleFromText_NoMatch(string? text)
    {
        Assert.Null(DiscHeaderDetector.ResolveConsoleFromText(text!));
    }

    // ── PS2 PVD with 2352 sector offset (0x9310 Mode 1) ──

    [Fact]
    public void DetectFromDiscImage_PS2_Mode1RawSector()
    {
        var data = MakeBuffer(0x9310 + 256);
        data[0x9310] = 0x01;
        Encoding.ASCII.GetBytes("CD001").CopyTo(data, 0x9311);
        Encoding.ASCII.GetBytes("PLAYSTATION").CopyTo(data, 0x9318);
        Encoding.ASCII.GetBytes("cdrom0:MAIN").CopyTo(data, 0x9310 + 64);
        var path = CreateImage("game.bin", data);
        Assert.Equal("PS2", _detector.DetectFromDiscImage(path));
    }

    // ── PS2 PVD with 2352 sector offset (0x9318 Mode 2/XA) ──

    [Fact]
    public void DetectFromDiscImage_PS1_Mode2XaSector()
    {
        var data = MakeBuffer(0x9318 + 64);
        data[0x9318] = 0x01;
        Encoding.ASCII.GetBytes("CD001").CopyTo(data, 0x9319);
        Encoding.ASCII.GetBytes("PLAYSTATION").CopyTo(data, 0x9320);
        var path = CreateImage("game.bin", data);
        Assert.Equal("PS1", _detector.DetectFromDiscImage(path));
    }

    // ── Wii U binary magic: "WUP-" at offset 0x00 ──

    [Fact]
    public void DetectFromDiscImage_WiiU_WupMagic()
    {
        var data = MakeBuffer(0x20);
        Encoding.ASCII.GetBytes("WUP-").CopyTo(data, 0);
        var path = CreateImage("game.iso", data);
        Assert.Equal("WIIU", _detector.DetectFromDiscImage(path));
    }

    // ── CDI via PVD System Identifier "CD-RTOS" ──

    [Fact]
    public void DetectFromDiscImage_CDI_PvdCdRtos()
    {
        var data = MakeBuffer(0x8000 + 64);
        data[0x8000] = 0x01; // PVD type
        Encoding.ASCII.GetBytes("CD001").CopyTo(data, 0x8001);
        Encoding.ASCII.GetBytes("CD-RTOS").CopyTo(data, 0x8008); // System Identifier
        var path = CreateImage("game.iso", data);
        Assert.Equal("CDI", _detector.DetectFromDiscImage(path));
    }

    // ── CDTV vs CD32 must not collide ──

    [Fact]
    public void ResolveConsoleFromText_Cdtv_NotCd32()
    {
        Assert.Equal("CDTV", DiscHeaderDetector.ResolveConsoleFromText("CDTV disc"));
        Assert.Equal("CD32", DiscHeaderDetector.ResolveConsoleFromText("AMIGA BOOT disc"));
    }

    // ── Neo Geo CD regex must not match bare "NEOGEO" without CD/separator ──

    [Theory]
    [InlineData("NEO-GEO game disc", "NEOCD")]     // Hyphenated form → match
    [InlineData("NEO GEO disc data", "NEOCD")]      // Space separator → match
    [InlineData("NEOGEO CD player", "NEOCD")]        // Explicit CD → match
    [InlineData("NEOGEOCD boot", "NEOCD")]           // Concatenated CD → match
    public void ResolveConsoleFromText_NeoGeoCd_ValidPatterns(string text, string expected)
    {
        Assert.Equal(expected, DiscHeaderDetector.ResolveConsoleFromText(text));
    }

    [Fact]
    public void ResolveConsoleFromText_BareNeoGeo_NoFalsePositive()
    {
        // "NEOGEO" without separator or "CD" should NOT match NEOCD
        // (prevents false positives for cartridge-based Neo Geo AES/MVS references)
        Assert.Null(DiscHeaderDetector.ResolveConsoleFromText("NEOGEO game rom"));
    }
}
