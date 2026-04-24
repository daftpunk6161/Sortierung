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

    private static ReadOnlySpan<byte> GbaNintendoLogo =>
    [
        0x24, 0xFF, 0xAE, 0x51, 0x69, 0x9A, 0xA2, 0x21, 0x3D, 0x84, 0x82, 0x0A,
        0x84, 0xE4, 0x09, 0xAD, 0x11, 0x24, 0x8B, 0x98, 0xC0, 0x81, 0x7F, 0x21,
        0xA3, 0x52, 0xBE, 0x19, 0x93, 0x09, 0xCE, 0x20, 0x10, 0x46, 0x4A, 0x4A,
        0xF8, 0x27, 0x31, 0xEC, 0x58, 0xC7, 0xE8, 0x33, 0x82, 0xE3, 0xCE, 0xBF,
        0x85, 0xF4, 0xDF, 0x94, 0xCE, 0x4B, 0x09, 0xC1, 0x94, 0x56, 0x8A, 0xC0,
        0x13, 0x72, 0xA7, 0xFC, 0x9F, 0x84, 0x4D, 0x73, 0xA3, 0xCA, 0x9A, 0x61,
        0x58, 0x97, 0xA3, 0x27, 0xFC, 0x03, 0x98, 0x76, 0x23, 0x1D, 0xC7, 0x61,
        0x03, 0x04, 0xAE, 0x56, 0xBF, 0x38, 0x84, 0x00, 0x40, 0xA7, 0x0E, 0xFD,
        0xFF, 0x52, 0xFE, 0x03, 0x6F, 0x95, 0x30, 0xF1, 0x97, 0xFB, 0xC0, 0x85,
        0x60, 0xD6, 0x80, 0x25, 0xA9, 0x63, 0xBE, 0x03, 0x01, 0x4E, 0x38, 0xE2,
        0xF9, 0xA2, 0x34, 0xFF, 0xBB, 0x3E, 0x03, 0x44, 0x78, 0x00, 0x90, 0xCB,
        0x88, 0x11, 0x3A, 0x94, 0x65, 0xC0, 0x7C, 0x63, 0x87, 0xF0, 0x3C, 0xAF,
        0xD6, 0x25, 0xE4, 0x8B, 0x38, 0x0A, 0xAC, 0x72, 0x21, 0xD4, 0xF8, 0x07
    ];

    private static ReadOnlySpan<byte> GbNintendoLogo =>
    [
        0xCE, 0xED, 0x66, 0x66, 0xCC, 0x0D, 0x00, 0x0B, 0x03, 0x73, 0x00, 0x83,
        0x00, 0x0C, 0x00, 0x0D, 0x00, 0x08, 0x11, 0x1F, 0x88, 0x89, 0x00, 0x0E,
        0xDC, 0xCC, 0x6E, 0xE6, 0xDD, 0xDD, 0xD9, 0x99, 0xBB, 0xBB, 0x67, 0x63,
        0x6E, 0x0E, 0xEC, 0xCC, 0xDD, 0xDC, 0x99, 0x9F, 0xBB, 0xB9, 0x33, 0x3E
    ];

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
        GbaNintendoLogo.CopyTo(data.AsSpan(0x04));
        ConfigureStream(data);

        Assert.Equal("GBA", _detector.Detect("test.gba"));
    }

    // ═══ Atari 7800 ══════════════════════════════════════════════════

    [Fact]
    public void Detect_Atari7800Header_ReturnsA78()
    {
        var data = new byte[512];
        // "ATARI7800" at offset 1
        var magic = "ATARI7800"u8;
        magic.CopyTo(data.AsSpan(1));
        ConfigureStream(data);

        Assert.Equal("A78", _detector.Detect("test.a78"));
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
        GbNintendoLogo.CopyTo(data.AsSpan(0x104));
        // GBC flag at 0x143: 0x00 = GB only
        data[0x143] = 0x00;
        ConfigureStream(data);

        Assert.Equal("GB", _detector.Detect("test.gb"));
    }

    [Fact]
    public void Detect_GameBoyColor_ReturnsGBC()
    {
        var data = new byte[0x150];
        GbNintendoLogo.CopyTo(data.AsSpan(0x104));
        // GBC flag: 0x80 = dual mode
        data[0x143] = 0x80;
        ConfigureStream(data);

        Assert.Equal("GBC", _detector.Detect("test.gbc"));
    }

    [Fact]
    public void Detect_GameBoyColorOnly_ReturnsGBC()
    {
        var data = new byte[0x150];
        GbNintendoLogo.CopyTo(data.AsSpan(0x104));
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
