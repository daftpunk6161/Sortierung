using Romulus.Core.Classification;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Unit tests for CartridgeHeaderDetector covering binary header parsing
/// for NES, N64, GBA, Genesis/MD, GB/GBC, Lynx, Atari 7800, and SNES.
/// Uses constructor-injected TestClassificationIo for deterministic I/O.
/// </summary>
public sealed class CartridgeHeaderDetectorCoverageTests
{
    private readonly TestClassificationIo _io;
    private readonly CartridgeHeaderDetector _detector;

    public CartridgeHeaderDetectorCoverageTests()
    {
        _io = new TestClassificationIo
        {
            FileExistsFunc = _ => true,
            OpenReadFunc = path => throw new InvalidOperationException("No stream configured for " + path),
            FileLengthFunc = _ => 0
        };
        _detector = new CartridgeHeaderDetector(classificationIo: _io);
    }

    private void ConfigureStream(byte[] data)
    {
        _io.OpenReadFunc = _ => new MemoryStream(data);
        _io.FileLengthFunc = _ => data.Length;
    }

    // ═══ NES (iNES header) ═══════════════════════════════════════════

    [Fact]
    public void Detect_NesHeader_ReturnsNES()
    {
        var data = new byte[512];
        // iNES magic: NES\x1A
        data[0] = 0x4E; // N
        data[1] = 0x45; // E
        data[2] = 0x53; // S
        data[3] = 0x1A;
        ConfigureStream(data);

        Assert.Equal("NES", _detector.Detect("test.nes"));
    }

    // ═══ N64 ═════════════════════════════════════════════════════════

    [Fact]
    public void Detect_N64BigEndian_ReturnsN64()
    {
        var data = new byte[512];
        data[0] = 0x80; data[1] = 0x37; data[2] = 0x12; data[3] = 0x40;
        ConfigureStream(data);

        Assert.Equal("N64", _detector.Detect("test.z64"));
    }

    [Fact]
    public void Detect_N64ByteSwapped_ReturnsN64()
    {
        var data = new byte[512];
        data[0] = 0x37; data[1] = 0x80; data[2] = 0x40; data[3] = 0x12;
        ConfigureStream(data);

        Assert.Equal("N64", _detector.Detect("test.v64"));
    }

    [Fact]
    public void Detect_N64LittleEndian_ReturnsN64()
    {
        var data = new byte[512];
        data[0] = 0x40; data[1] = 0x12; data[2] = 0x37; data[3] = 0x80;
        ConfigureStream(data);

        Assert.Equal("N64", _detector.Detect("test.n64"));
    }

    // ═══ Atari Lynx ══════════════════════════════════════════════════

    [Fact]
    public void Detect_LynxHeader_ReturnsLYNX()
    {
        var data = new byte[512];
        // LYNX magic at offset 0
        data[0] = 0x4C; data[1] = 0x59; data[2] = 0x4E; data[3] = 0x58;
        ConfigureStream(data);

        Assert.Equal("LYNX", _detector.Detect("test.lnx"));
    }

    // ═══ GBA ═════════════════════════════════════════════════════════

    [Fact]
    public void Detect_GbaLogo_ReturnsGBA()
    {
        var data = new byte[512];
        // Nintendo logo at offset 0x04
        data[0x04] = 0x24; data[0x05] = 0xFF; data[0x06] = 0xAE; data[0x07] = 0x51;
        ConfigureStream(data);

        Assert.Equal("GBA", _detector.Detect("test.gba"));
    }

    // ═══ Atari 7800 ══════════════════════════════════════════════════

    [Fact]
    public void Detect_Atari7800Header_Returns7800()
    {
        var data = new byte[512];
        // "ATARI7800" at offset 1
        var magic = "ATARI7800"u8;
        magic.CopyTo(data.AsSpan(1));
        ConfigureStream(data);

        Assert.Equal("7800", _detector.Detect("test.a78"));
    }

    // ═══ Genesis / Mega Drive ════════════════════════════════════════

    [Fact]
    public void Detect_GenesisMegaDrive_ReturnsMD()
    {
        var data = new byte[0x120];
        // "SEGA MEGA DRIVE" at offset 0x100
        var magic = System.Text.Encoding.ASCII.GetBytes("SEGA MEGA DRIVE ");
        Array.Copy(magic, 0, data, 0x100, magic.Length);
        ConfigureStream(data);

        Assert.Equal("MD", _detector.Detect("test.md"));
    }

    [Fact]
    public void Detect_GenesisLabel_ReturnsMD()
    {
        var data = new byte[0x120];
        var magic = System.Text.Encoding.ASCII.GetBytes("SEGA GENESIS    ");
        Array.Copy(magic, 0, data, 0x100, magic.Length);
        ConfigureStream(data);

        Assert.Equal("MD", _detector.Detect("test.bin"));
    }

    [Fact]
    public void Detect_Sega32X_Returns32X()
    {
        var data = new byte[0x120];
        var magic = System.Text.Encoding.ASCII.GetBytes("SEGA 32X        ");
        Array.Copy(magic, 0, data, 0x100, magic.Length);
        ConfigureStream(data);

        Assert.Equal("32X", _detector.Detect("test.32x"));
    }

    // ═══ Game Boy / Game Boy Color ═══════════════════════════════════

    [Fact]
    public void Detect_GameBoy_ReturnsGB()
    {
        var data = new byte[0x150];
        // Nintendo logo at 0x104
        data[0x104] = 0xCE; data[0x105] = 0xED; data[0x106] = 0x66; data[0x107] = 0x66;
        // GBC flag at 0x143: 0x00 = GB only
        data[0x143] = 0x00;
        ConfigureStream(data);

        Assert.Equal("GB", _detector.Detect("test.gb"));
    }

    [Fact]
    public void Detect_GameBoyColor_ReturnsGBC()
    {
        var data = new byte[0x150];
        data[0x104] = 0xCE; data[0x105] = 0xED; data[0x106] = 0x66; data[0x107] = 0x66;
        // GBC flag: 0x80 = dual mode
        data[0x143] = 0x80;
        ConfigureStream(data);

        Assert.Equal("GBC", _detector.Detect("test.gbc"));
    }

    [Fact]
    public void Detect_GameBoyColorOnly_ReturnsGBC()
    {
        var data = new byte[0x150];
        data[0x104] = 0xCE; data[0x105] = 0xED; data[0x106] = 0x66; data[0x107] = 0x66;
        // GBC flag: 0xC0 = GBC only
        data[0x143] = 0xC0;
        ConfigureStream(data);

        Assert.Equal("GBC", _detector.Detect("test.gbc"));
    }

    // ═══ SNES ════════════════════════════════════════════════════════

    [Fact]
    public void Detect_SnesLoRom_ReturnsSNES()
    {
        // SNES LoROM header at offset 0x7FC0
        var data = new byte[0x8000];
        // Fill title at 0x7FC0 with printable ASCII
        var title = System.Text.Encoding.ASCII.GetBytes("SUPER MARIOWORLD    \0");
        Array.Copy(title, 0, data, 0x7FC0, 21);
        // Map mode byte at 0x7FC0 + 21 = 0x7FD5
        data[0x7FD5] = 0x20; // LoROM
        // Checksum complement at 0x7FC0 + 28 = 0x7FDC
        // Complement and checksum XOR must equal 0xFFFF
        data[0x7FDC] = 0xFF; data[0x7FDD] = 0x00; // complement = 0x00FF
        data[0x7FDE] = 0x00; data[0x7FDF] = 0xFF; // checksum = 0xFF00
        // 0x00FF ^ 0xFF00 = 0xFFFF ✓

        _io.OpenReadFunc = _ => new MemoryStream(data);
        _io.FileLengthFunc = _ => data.Length;

        Assert.Equal("SNES", _detector.Detect("test.sfc"));
    }

    // ═══ Edge Cases ══════════════════════════════════════════════════

    [Fact]
    public void Detect_EmptyPath_ReturnsNull()
    {
        Assert.Null(_detector.Detect(""));
        Assert.Null(_detector.Detect(null!));
    }

    [Fact]
    public void Detect_FileDoesNotExist_ReturnsNull()
    {
        _io.FileExistsFunc = _ => false;
        Assert.Null(_detector.Detect("nonexistent.nes"));
    }

    [Fact]
    public void Detect_TooSmall_ReturnsNull()
    {
        ConfigureStream(new byte[2]);
        Assert.Null(_detector.Detect("tiny.bin"));
    }

    [Fact]
    public void Detect_UnknownFormat_ReturnsNull()
    {
        var data = new byte[512]; // all zeros, no known header
        ConfigureStream(data);
        // Also need to handle SNES path (ScanSnesHeader needs fileLength)
        _io.OpenReadFunc = _ => new MemoryStream(data);
        _io.FileLengthFunc = _ => data.Length;

        Assert.Null(_detector.Detect("unknown.bin"));
    }

    [Fact]
    public void Detect_IoException_ReturnsNull()
    {
        _io.OpenReadFunc = _ => throw new IOException("disk error");
        _io.FileLengthFunc = _ => 512;

        Assert.Null(_detector.Detect("error.nes"));
    }

    [Fact]
    public void Detect_CachesResult()
    {
        var data = new byte[512];
        data[0] = 0x4E; data[1] = 0x45; data[2] = 0x53; data[3] = 0x1A; // NES
        int callCount = 0;
        _io.OpenReadFunc = _ => { callCount++; return new MemoryStream(data); };
        _io.FileLengthFunc = _ => data.Length;

        var first = _detector.Detect("cached.nes");
        var second = _detector.Detect("cached.nes");

        Assert.Equal("NES", first);
        Assert.Equal("NES", second);
        Assert.Equal(1, callCount); // cached on second call
    }
}
