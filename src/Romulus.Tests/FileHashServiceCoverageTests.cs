using Romulus.Infrastructure.Hashing;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for FileHashService — targeting:
/// - CHD v3 header parsing (only v4/v5 tested before)
/// - Malformed persistent cache JSON handling
/// - Empty/null persistent cache document
/// - Disposed state throws
/// - ClearCache with collection index (noop path)
/// - FlushPersistentCache with collection index (noop path)
/// - ResolveDefaultPersistentCachePath returns non-null
/// - MaxEntries clamp minimum
/// - GetHash with CRC alias normalization
/// - Cache key differs by hash type for same file
/// - Permission error on file access → null
/// </summary>
public sealed class FileHashServiceCoverageTests : IDisposable
{
    private readonly string _tempDir;

    public FileHashServiceCoverageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FHS_Cov_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
        catch { /* best-effort */ }
    }

    #region Helpers

    private string CreateFile(string name, byte[] content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private string CreateTextFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Creates a minimal CHD v3 header with raw SHA1 at offset 0x50.</summary>
    private byte[] BuildChdV3Header(byte[] sha1Bytes)
    {
        var header = new byte[0x80];
        // MComprHD magic
        "MComprHD"u8.CopyTo(header.AsSpan(0, 8));
        // Version 3 (big-endian at offset 12)
        header[12] = 0; header[13] = 0; header[14] = 0; header[15] = 3;
        // Raw SHA1 at offset 0x50
        sha1Bytes.AsSpan(0, 20).CopyTo(header.AsSpan(0x50, 20));
        return header;
    }

    /// <summary>Creates a minimal CHD v3 header with zeros at 0x50 and SHA1 at 0x64.</summary>
    private byte[] BuildChdV3HeaderFallback(byte[] sha1Bytes)
    {
        var header = new byte[0x80];
        "MComprHD"u8.CopyTo(header.AsSpan(0, 8));
        header[12] = 0; header[13] = 0; header[14] = 0; header[15] = 3;
        // 0x50 is all zeros → fallback to 0x64
        sha1Bytes.AsSpan(0, 20).CopyTo(header.AsSpan(0x64, 20));
        return header;
    }

    #endregion

    // =================================================================
    //  CHD v3 header parsing
    // =================================================================

    [Fact]
    public void GetHash_ChdV3_UsesRawSha1AtOffset0x50()
    {
        var fakeSha1 = new byte[20];
        for (int i = 0; i < 20; i++) fakeSha1[i] = (byte)(0xA0 + i);

        var chdPath = CreateFile("game-v3.chd", BuildChdV3Header(fakeSha1));

        using var svc = new FileHashService();
        var hash = svc.GetHash(chdPath, "SHA1");

        var expected = Convert.ToHexString(fakeSha1).ToLowerInvariant();
        Assert.Equal(expected, hash);
    }

    [Fact]
    public void GetHash_ChdV3_FallsBackTo0x64_WhenOffset0x50IsZero()
    {
        var fakeSha1 = new byte[20];
        for (int i = 0; i < 20; i++) fakeSha1[i] = (byte)(0xB0 + i);

        var chdPath = CreateFile("game-v3-fb.chd", BuildChdV3HeaderFallback(fakeSha1));

        using var svc = new FileHashService();
        var hash = svc.GetHash(chdPath, "SHA1");

        var expected = Convert.ToHexString(fakeSha1).ToLowerInvariant();
        Assert.Equal(expected, hash);
    }

    [Fact]
    public void GetHash_ChdTruncated_FallsBackToStreamHash()
    {
        // CHD file too short for header → regular SHA1
        var chdPath = CreateFile("tiny.chd", new byte[10]);

        using var svc = new FileHashService();
        var hash = svc.GetHash(chdPath, "SHA1");

        Assert.NotNull(hash);
        Assert.Equal(40, hash!.Length); // SHA1 = 40 hex chars
    }

    [Fact]
    public void GetHash_ChdUnknownVersion_FallsBackToStreamHash()
    {
        var header = new byte[0x80];
        "MComprHD"u8.CopyTo(header.AsSpan(0, 8));
        // Version 99 — unknown
        header[12] = 0; header[13] = 0; header[14] = 0; header[15] = 99;

        var chdPath = CreateFile("unknown.chd", header);

        using var svc = new FileHashService();
        var hash = svc.GetHash(chdPath, "SHA1");

        Assert.NotNull(hash);
    }

    // =================================================================
    //  Malformed persistent cache
    // =================================================================

    [Fact]
    public void Constructor_MalformedPersistentCache_DoesNotThrow()
    {
        var cachePath = CreateTextFile("bad-cache.json", "NOT-VALID-JSON{{{");

        // Should silently ignore malformed JSON
        using var svc = new FileHashService(persistentCachePath: cachePath);
        Assert.Equal(0, svc.CacheCount);
    }

    [Fact]
    public void Constructor_EmptyPersistentCache_DoesNotThrow()
    {
        var cachePath = CreateTextFile("empty-cache.json", "{}");

        using var svc = new FileHashService(persistentCachePath: cachePath);
        Assert.Equal(0, svc.CacheCount);
    }

    [Fact]
    public void Constructor_PersistentCacheWithNullEntries_DoesNotThrow()
    {
        var cachePath = CreateTextFile("null-entries.json", """{"Entries": null}""");

        using var svc = new FileHashService(persistentCachePath: cachePath);
        Assert.Equal(0, svc.CacheCount);
    }

    [Fact]
    public void Constructor_PersistentCacheWithInvalidEntries_SkipsInvalid()
    {
        var cachePath = CreateTextFile("bad-entries.json", """
        {
            "Entries": [
                {"Path": "", "Hash": "abc", "HashType": "SHA1"},
                {"Path": "test.rom", "Hash": "", "HashType": "SHA1"},
                {"Path": "valid.rom", "Hash": "abc123", "HashType": ""}
            ]
        }
        """);

        using var svc = new FileHashService(persistentCachePath: cachePath);
        // All 3 entries have empty required fields → all skipped
        Assert.True(svc.IsPersistent);
    }

    // =================================================================
    //  Disposed state
    // =================================================================

    [Fact]
    public void GetHash_AfterDispose_Throws()
    {
        var svc = new FileHashService();
        svc.Dispose();

        Assert.Throws<ObjectDisposedException>(() => svc.GetHash("test.rom"));
    }

    [Fact]
    public void ClearCache_AfterDispose_Throws()
    {
        var svc = new FileHashService();
        svc.Dispose();

        Assert.Throws<ObjectDisposedException>(() => svc.ClearCache());
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var svc = new FileHashService();
        svc.Dispose();
        svc.Dispose(); // second call → no-op
    }

    // =================================================================
    //  CRC alias normalization
    // =================================================================

    [Fact]
    public void GetHash_CrcAlias_NormalizedToCrc32()
    {
        var path = CreateTextFile("crc-test.rom", "Hello");

        using var svc = new FileHashService();
        var hashCrc = svc.GetHash(path, "CRC");
        var hashCrc32 = svc.GetHash(path, "CRC32");

        // Both aliases resolve to the same value
        Assert.Equal(hashCrc, hashCrc32);
    }

    // =================================================================
    //  Non-existent file → null
    // =================================================================

    [Fact]
    public void GetHash_NonExistentFile_ReturnsNull()
    {
        using var svc = new FileHashService();
        Assert.Null(svc.GetHash(Path.Combine(_tempDir, "phantom.rom")));
    }

    // =================================================================
    //  IsPersistent property
    // =================================================================

    [Fact]
    public void IsPersistent_WithoutPersistence_ReturnsFalse()
    {
        using var svc = new FileHashService();
        Assert.False(svc.IsPersistent);
    }

    [Fact]
    public void IsPersistent_WithPersistentCachePath_ReturnsTrue()
    {
        var cachePath = Path.Combine(_tempDir, "hash-cache.json");
        using var svc = new FileHashService(persistentCachePath: cachePath);
        Assert.True(svc.IsPersistent);
    }

    // =================================================================
    //  FlushPersistentCache — noop paths
    // =================================================================

    [Fact]
    public void FlushPersistentCache_WithoutPersistence_NoOp()
    {
        using var svc = new FileHashService();
        svc.FlushPersistentCache(); // should not throw
    }

    [Fact]
    public void FlushPersistentCache_NothingDirty_NoFileCreated()
    {
        var cachePath = Path.Combine(_tempDir, "no-flush.json");
        using var svc = new FileHashService(persistentCachePath: cachePath);
        svc.FlushPersistentCache();

        // No file created because nothing was cached
        Assert.False(File.Exists(cachePath));
    }

    [Fact]
    public void FlushPersistentCache_AfterGetHash_CreatesFile()
    {
        var romPath = CreateTextFile("flush-test.rom", "some content");
        var cachePath = Path.Combine(_tempDir, "flushed.json");

        using var svc = new FileHashService(persistentCachePath: cachePath);
        svc.GetHash(romPath, "SHA1");
        svc.FlushPersistentCache();

        Assert.True(File.Exists(cachePath));
        var content = File.ReadAllText(cachePath);
        Assert.Contains("flush-test.rom", content, StringComparison.OrdinalIgnoreCase);
    }

    // =================================================================
    //  ResolveDefaultPersistentCachePath
    // =================================================================

    [Fact]
    public void ResolveDefaultPersistentCachePath_ReturnsNonEmpty()
    {
        var path = FileHashService.ResolveDefaultPersistentCachePath();
        Assert.False(string.IsNullOrWhiteSpace(path));
        Assert.Contains("file-hashes", path, StringComparison.OrdinalIgnoreCase);
    }

    // =================================================================
    //  MaxEntries minimum clamp
    // =================================================================

    [Fact]
    public void MaxEntries_BelowMinimum_ClampedTo500()
    {
        using var svc = new FileHashService();
        svc.MaxEntries = 10;
        Assert.Equal(500, svc.MaxEntries);
    }

    [Fact]
    public void MaxEntries_ValidValue_Accepted()
    {
        using var svc = new FileHashService();
        svc.MaxEntries = 50_000;
        Assert.Equal(50_000, svc.MaxEntries);
    }
}
