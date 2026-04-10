using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Hashing;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Phase 2 TASK-011/012/014: HeaderlessHasher tests with real temp files.
/// Validates correct header skipping + hashing for NES, SNES, Atari 7800, Atari Lynx.
/// </summary>
public sealed class HeaderlessHasherTests : IDisposable
{
    private readonly string _tempDir;
    private readonly HeaderlessHasher _hasher;

    public HeaderlessHasherTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus", "HeaderlessHasher", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _hasher = new HeaderlessHasher();
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
        catch { /* best effort */ }
    }

    // ── NES (16-byte iNES header) ───────────────────────────────────────

    [Fact]
    public void ComputeHeaderlessHash_NES_SkipsInesHeader_Issue9()
    {
        // Build a fake NES ROM: 16 bytes iNES header + 256 bytes content
        var content = new byte[256];
        Array.Fill(content, (byte)0xAA);
        var rom = BuildNesRom(content);
        var path = WriteFile("test.nes", rom);

        var headerlessHash = _hasher.ComputeHeaderlessHash(path, "NES", "SHA1");
        var contentOnlyHash = ComputeSha1(content);

        Assert.NotNull(headerlessHash);
        Assert.Equal(contentOnlyHash, headerlessHash);
    }

    [Fact]
    public void ComputeHeaderlessHash_NES_WithoutInesHeader_ReturnsNull_Issue9()
    {
        // ROM without iNES magic → no header to skip
        var raw = new byte[1024];
        Array.Fill(raw, (byte)0xBB);
        var path = WriteFile("noineshdr.nes", raw);

        var result = _hasher.ComputeHeaderlessHash(path, "NES", "SHA1");
        Assert.Null(result);
    }

    // ── SNES (512-byte copier header, conditional) ──────────────────────

    [Fact]
    public void ComputeHeaderlessHash_SNES_WithCopierHeader_SkipsCopier_Issue9()
    {
        // Build SNES ROM with copier header: 512 bytes header + 32768 bytes content
        // Total = 33280, which % 1024 == 512
        var content = new byte[32768];
        Array.Fill(content, (byte)0xCC);
        var copierHeader = new byte[512]; // dummy copier header
        var rom = copierHeader.Concat(content).ToArray();
        var path = WriteFile("snes_copier.sfc", rom);

        var headerlessHash = _hasher.ComputeHeaderlessHash(path, "SNES", "SHA1");
        var contentOnlyHash = ComputeSha1(content);

        Assert.NotNull(headerlessHash);
        Assert.Equal(contentOnlyHash, headerlessHash);
    }

    [Fact]
    public void ComputeHeaderlessHash_SNES_WithoutCopierHeader_ReturnsNull_Issue9()
    {
        // Clean SNES ROM: 32768 bytes (exact multiple of 1024) → no copier header
        var rom = new byte[32768];
        Array.Fill(rom, (byte)0xDD);
        var path = WriteFile("snes_clean.sfc", rom);

        var result = _hasher.ComputeHeaderlessHash(path, "SNES", "SHA1");
        Assert.Null(result);
    }

    // ── Atari 7800 (128-byte header) ────────────────────────────────────

    [Fact]
    public void ComputeHeaderlessHash_Atari7800_SkipsHeader_Issue9()
    {
        var content = new byte[4096];
        Array.Fill(content, (byte)0xEE);
        var rom = BuildAtari7800Rom(content);
        var path = WriteFile("test.a78", rom);

        var headerlessHash = _hasher.ComputeHeaderlessHash(path, "ATARI7800", "SHA1");
        var contentOnlyHash = ComputeSha1(content);

        Assert.NotNull(headerlessHash);
        Assert.Equal(contentOnlyHash, headerlessHash);
    }

    // ── Atari Lynx (64-byte header) ─────────────────────────────────────

    [Fact]
    public void ComputeHeaderlessHash_AtariLynx_SkipsHeader_Issue9()
    {
        var content = new byte[2048];
        Array.Fill(content, (byte)0xFF);
        var rom = BuildLynxRom(content);
        var path = WriteFile("test.lnx", rom);

        var headerlessHash = _hasher.ComputeHeaderlessHash(path, "ATARILYNX", "SHA1");
        var contentOnlyHash = ComputeSha1(content);

        Assert.NotNull(headerlessHash);
        Assert.Equal(contentOnlyHash, headerlessHash);
    }

    // ── N64 (canonical byte-order normalization) ───────────────────────

    [Fact]
    public void ComputeHeaderlessHash_N64_BigEndian_ReturnsCanonicalSha1()
    {
        var canonical = BuildN64CanonicalRom();
        var path = WriteFile("test.z64", canonical);

        var normalizedHash = _hasher.ComputeHeaderlessHash(path, "N64", "SHA1");

        Assert.NotNull(normalizedHash);
        Assert.Equal(ComputeSha1(canonical), normalizedHash);
    }

    [Fact]
    public void ComputeHeaderlessHash_N64_ByteSwapped_NormalizesToCanonicalSha1()
    {
        var canonical = BuildN64CanonicalRom();
        var byteSwapped = SwapPairs(canonical);
        var path = WriteFile("test.v64", byteSwapped);

        var normalizedHash = _hasher.ComputeHeaderlessHash(path, "N64", "SHA1");

        Assert.NotNull(normalizedHash);
        Assert.Equal(ComputeSha1(canonical), normalizedHash);
    }

    [Fact]
    public void ComputeHeaderlessHash_N64_LittleEndian_NormalizesToCanonicalSha1()
    {
        var canonical = BuildN64CanonicalRom();
        var littleEndian = ReverseWords(canonical);
        var path = WriteFile("test.n64", littleEndian);

        var normalizedHash = _hasher.ComputeHeaderlessHash(path, "N64", "SHA1");

        Assert.NotNull(normalizedHash);
        Assert.Equal(ComputeSha1(canonical), normalizedHash);
    }

    // ── Console without mapping → null ──────────────────────────────────

    [Theory]
    [InlineData("MD")]
    [InlineData("GBA")]
    public void ComputeHeaderlessHash_ConsoleWithoutMapping_ReturnsNull_Issue9(string console)
    {
        var rom = new byte[1024];
        var path = WriteFile($"test_{console}.bin", rom);

        var result = _hasher.ComputeHeaderlessHash(path, console, "SHA1");
        Assert.Null(result);
    }

    // ── Edge cases ──────────────────────────────────────────────────────

    [Fact]
    public void ComputeHeaderlessHash_NullPath_ReturnsNull_Issue9()
    {
        var result = _hasher.ComputeHeaderlessHash(null!, "NES");
        Assert.Null(result);
    }

    [Fact]
    public void ComputeHeaderlessHash_EmptyConsole_ReturnsNull_Issue9()
    {
        var result = _hasher.ComputeHeaderlessHash("any.nes", "");
        Assert.Null(result);
    }

    [Fact]
    public void ComputeHeaderlessHash_NonexistentFile_ReturnsNull_Issue9()
    {
        var result = _hasher.ComputeHeaderlessHash(Path.Combine(_tempDir, "nope.nes"), "NES");
        Assert.Null(result);
    }

    [Fact]
    public void ComputeHeaderlessHash_CachesResult_Issue9()
    {
        var content = new byte[256];
        Array.Fill(content, (byte)0xAA);
        var rom = BuildNesRom(content);
        var path = WriteFile("cached.nes", rom);

        var hash1 = _hasher.ComputeHeaderlessHash(path, "NES");
        var hash2 = _hasher.ComputeHeaderlessHash(path, "NES");

        Assert.Equal(hash1, hash2);
        Assert.True(_hasher.CacheCount > 0);
    }

    // ── Determinism ─────────────────────────────────────────────────────

    [Fact]
    public void ComputeHeaderlessHash_Deterministic_SameInputSameOutput_Issue9()
    {
        var content = new byte[512];
        Array.Fill(content, (byte)0x42);
        var rom = BuildNesRom(content);
        var path = WriteFile("det.nes", rom);

        var results = new HashSet<string?>();
        for (int i = 0; i < 3; i++)
        {
            results.Add(_hasher.ComputeHeaderlessHash(path, "NES"));
        }

        Assert.Single(results);
        Assert.NotNull(results.First());
    }

    // ── SHA256 support ──────────────────────────────────────────────────

    [Fact]
    public void ComputeHeaderlessHash_SHA256_ProducesValidHash_Issue9()
    {
        var content = new byte[256];
        Array.Fill(content, (byte)0xAA);
        var rom = BuildNesRom(content);
        var path = WriteFile("sha256.nes", rom);

        var hash = _hasher.ComputeHeaderlessHash(path, "NES", "SHA256");
        Assert.NotNull(hash);
        Assert.Equal(64, hash!.Length); // SHA256 = 32 bytes = 64 hex chars
    }

    // ── TASK-015 Regression: Non-headered consoles unaffected ───────────

    [Fact]
    public void Regression_NonHeaderedConsoles_NeverGetHeaderlessHash_Issue9()
    {
        var rom = new byte[1024];
        var path = WriteFile("regression.bin", rom);

        foreach (var console in new[] { "MD", "GBA", "GB", "GBC", "PSX", "DC", "UNKNOWN" })
        {
            Assert.Null(_hasher.ComputeHeaderlessHash(path, console));
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static byte[] BuildNesRom(byte[] content)
    {
        var header = new byte[16];
        header[0] = 0x4E; // N
        header[1] = 0x45; // E
        header[2] = 0x53; // S
        header[3] = 0x1A; // \x1A
        return header.Concat(content).ToArray();
    }

    private static byte[] BuildAtari7800Rom(byte[] content)
    {
        var header = new byte[128];
        "ATARI7800"u8.CopyTo(header.AsSpan(1));
        return header.Concat(content).ToArray();
    }

    private static byte[] BuildLynxRom(byte[] content)
    {
        var header = new byte[64];
        header[0] = 0x4C; // L
        header[1] = 0x59; // Y
        header[2] = 0x4E; // N
        header[3] = 0x58; // X
        return header.Concat(content).ToArray();
    }

    private static byte[] BuildN64CanonicalRom()
    {
        return
        [
            0x80, 0x37, 0x12, 0x40,
            0x11, 0x22, 0x33, 0x44,
            0x55, 0x66, 0x77, 0x88,
            0x99, 0xAA, 0xBB, 0xCC
        ];
    }

    private static byte[] SwapPairs(byte[] data)
    {
        var copy = data.ToArray();
        for (var i = 0; i + 1 < copy.Length; i += 2)
            (copy[i], copy[i + 1]) = (copy[i + 1], copy[i]);

        return copy;
    }

    private static byte[] ReverseWords(byte[] data)
    {
        var copy = data.ToArray();
        for (var i = 0; i + 3 < copy.Length; i += 4)
        {
            var b0 = copy[i];
            var b1 = copy[i + 1];
            copy[i] = copy[i + 3];
            copy[i + 1] = copy[i + 2];
            copy[i + 2] = b1;
            copy[i + 3] = b0;
        }

        return copy;
    }

    private string WriteFile(string name, byte[] data)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, data);
        return path;
    }

    private static string ComputeSha1(byte[] data)
    {
        var hash = System.Security.Cryptography.SHA1.HashData(data);
        return Convert.ToHexStringLower(hash);
    }
}
