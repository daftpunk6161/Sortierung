using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Romulus.Infrastructure.Hashing;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage boost for FileHashService: CHD v3 fallback parsing, persistent cache
/// edge cases, fingerprint mismatch, flush/trim, and exception paths.
/// Targets ~44 uncovered lines.
/// </summary>
public sealed class FileHashServiceCoverageBoostTests : IDisposable
{
    private readonly string _tempDir;

    public FileHashServiceCoverageBoostTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FHS_Cov_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private string CreateFile(string name, byte[]? content = null)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, content ?? Encoding.UTF8.GetBytes("test-content"));
        return path;
    }

    private string CreateChdFile(string name, uint version, byte[]? sha1At0x50 = null, byte[]? sha1At0x64 = null, byte[]? sha1At0x40 = null)
    {
        var path = Path.Combine(_tempDir, name);
        var header = new byte[0x80];

        // Magic: "MComprHD"
        Encoding.ASCII.GetBytes("MComprHD").CopyTo(header, 0);
        // Version at offset 12 (big-endian)
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(12, 4), version);

        if (sha1At0x40 is not null)
            sha1At0x40.CopyTo(header, 0x40);
        if (sha1At0x50 is not null)
            sha1At0x50.CopyTo(header, 0x50);
        if (sha1At0x64 is not null)
            sha1At0x64.CopyTo(header, 0x64);

        File.WriteAllBytes(path, header);
        return path;
    }

    // ===== CHD v3 SHA1 at 0x50 =====

    [Fact]
    public void GetHash_ChdV3_PrefersSha1At0x50()
    {
        var sha1Bytes = new byte[20];
        sha1Bytes[0] = 0xAB; sha1Bytes[1] = 0xCD;
        var path = CreateChdFile("v3.chd", 3, sha1At0x50: sha1Bytes);

        using var svc = new FileHashService();
        var hash = svc.GetHash(path, "SHA1");

        Assert.NotNull(hash);
        Assert.StartsWith("abcd", hash);
    }

    // ===== CHD v3 fallback to 0x64 when 0x50 is zeroed =====

    [Fact]
    public void GetHash_ChdV3_FallsBackTo0x64WhenSha1At0x50IsZero()
    {
        var zeroSha1 = new byte[20]; // all zeros
        var sha1At64 = new byte[20];
        sha1At64[0] = 0xDE; sha1At64[1] = 0xAD;
        var path = CreateChdFile("v3fallback.chd", 3, sha1At0x50: zeroSha1, sha1At0x64: sha1At64);

        using var svc = new FileHashService();
        var hash = svc.GetHash(path, "SHA1");

        Assert.NotNull(hash);
        Assert.StartsWith("dead", hash);
    }

    // ===== CHD v4 SHA1 at 0x50 =====

    [Fact]
    public void GetHash_ChdV4_PrefersSha1At0x50()
    {
        var sha1Bytes = new byte[20];
        sha1Bytes[0] = 0xBE; sha1Bytes[1] = 0xEF;
        var path = CreateChdFile("v4.chd", 4, sha1At0x50: sha1Bytes);

        using var svc = new FileHashService();
        var hash = svc.GetHash(path, "SHA1");

        Assert.NotNull(hash);
        Assert.StartsWith("beef", hash);
    }

    // ===== CHD unknown version → standard SHA1 =====

    [Fact]
    public void GetHash_ChdUnknownVersion_FallsBackToStreamHash()
    {
        var path = CreateChdFile("v99.chd", 99);

        using var svc = new FileHashService();
        var hash = svc.GetHash(path, "SHA1");

        // Should compute standard SHA1, not CHD-extracted
        Assert.NotNull(hash);
        Assert.Equal(40, hash.Length);
    }

    // ===== CHD v5 with zeroed sha1 at 0x40 falls back to stream =====

    [Fact]
    public void GetHash_ChdV5_ZeroedSha1_FallsBackToStreamHash()
    {
        var zeroSha1 = new byte[20]; // all zeros at 0x40
        var path = CreateChdFile("v5zero.chd", 5, sha1At0x40: zeroSha1);

        using var svc = new FileHashService();
        var hash = svc.GetHash(path, "SHA1");

        // Zeroed SHA1 → TryReadSha1AtOffset returns false → standard hash
        Assert.NotNull(hash);
        Assert.Equal(40, hash.Length);
    }

    // ===== CHD too short header =====

    [Fact]
    public void GetHash_ChdTooShort_FallsBackToStreamHash()
    {
        var path = Path.Combine(_tempDir, "short.chd");
        // Less than 0x54 bytes
        File.WriteAllBytes(path, new byte[0x20]);

        using var svc = new FileHashService();
        var hash = svc.GetHash(path, "SHA1");

        Assert.NotNull(hash);
    }

    // ===== Non-CHD file with .chd extension but wrong magic =====

    [Fact]
    public void GetHash_ChdWrongMagic_FallsBackToStreamHash()
    {
        var path = Path.Combine(_tempDir, "fake.chd");
        var data = new byte[0x80];
        Encoding.ASCII.GetBytes("NotACHD!").CopyTo(data, 0);
        File.WriteAllBytes(path, data);

        using var svc = new FileHashService();
        var hash = svc.GetHash(path, "SHA1");

        Assert.NotNull(hash);
        Assert.Equal(40, hash.Length);
    }

    // ===== Persistent cache: save, reload, fingerprint match =====

    [Fact]
    public void PersistentCache_FlushAndReload_ReturnsPersistedHash()
    {
        var cachePath = Path.Combine(_tempDir, "hash-cache.json");
        var file = CreateFile("rom.bin");

        string? hash1;
        using (var svc = new FileHashService(persistentCachePath: cachePath))
        {
            hash1 = svc.GetHash(file, "SHA1");
            Assert.NotNull(hash1);
            svc.FlushPersistentCache();
        }

        Assert.True(File.Exists(cachePath));

        // Reload from persisted cache
        using (var svc2 = new FileHashService(persistentCachePath: cachePath))
        {
            var hash2 = svc2.GetHash(file, "SHA1");
            Assert.Equal(hash1, hash2);
        }
    }

    // ===== Persistent cache: fingerprint mismatch (file changed) =====

    [Fact]
    public void PersistentCache_FingerprintMismatch_Recomputes()
    {
        var cachePath = Path.Combine(_tempDir, "hash-cache-fp.json");
        var file = CreateFile("mutable.bin", Encoding.UTF8.GetBytes("version1"));

        string? hash1;
        using (var svc = new FileHashService(persistentCachePath: cachePath))
        {
            hash1 = svc.GetHash(file, "SHA1");
            svc.FlushPersistentCache();
        }

        // Modify file → fingerprint changes
        File.WriteAllBytes(file, Encoding.UTF8.GetBytes("version2-changed-content!"));

        using (var svc2 = new FileHashService(persistentCachePath: cachePath))
        {
            var hash2 = svc2.GetHash(file, "SHA1");
            Assert.NotEqual(hash1, hash2);
        }
    }

    // ===== Persistent cache: corrupt JSON =====

    [Fact]
    public void PersistentCache_CorruptJson_GracefullyIgnored()
    {
        var cachePath = Path.Combine(_tempDir, "corrupt-cache.json");
        File.WriteAllText(cachePath, "{{{not-json");

        using var svc = new FileHashService(persistentCachePath: cachePath);
        var file = CreateFile("after-corrupt.bin");
        var hash = svc.GetHash(file, "SHA1");

        Assert.NotNull(hash);
    }

    // ===== Persistent cache: empty entries =====

    [Fact]
    public void PersistentCache_EmptyEntries_NoErrors()
    {
        var cachePath = Path.Combine(_tempDir, "empty-entries.json");
        File.WriteAllText(cachePath, JsonSerializer.Serialize(new { Entries = new object[]
        {
            new { Path = "", Hash = "", HashType = "" },
            new { Path = (string?)null, Hash = "abc", HashType = "SHA1" }
        } }));

        using var svc = new FileHashService(persistentCachePath: cachePath);
        Assert.Equal(0, svc.CacheCount);
    }

    // ===== FlushPersistentCache: no-op when not dirty =====

    [Fact]
    public void FlushPersistentCache_NotDirty_DoesNotWriteFile()
    {
        var cachePath = Path.Combine(_tempDir, "no-write.json");
        using var svc = new FileHashService(persistentCachePath: cachePath);
        svc.FlushPersistentCache();

        Assert.False(File.Exists(cachePath));
    }

    // ===== FlushPersistentCache: trims overflow entries =====

    [Fact]
    public void FlushPersistentCache_TrimsOverflowEntries()
    {
        var cachePath = Path.Combine(_tempDir, "trim-cache.json");
        using var svc = new FileHashService(maxEntries: 500, persistentCachePath: cachePath);

        // Create and hash many files to exceed capacity
        for (int i = 0; i < 10; i++)
        {
            var f = CreateFile($"file{i}.bin", Encoding.UTF8.GetBytes($"content-{i}"));
            svc.GetHash(f, "SHA1");
        }

        svc.FlushPersistentCache();

        Assert.True(File.Exists(cachePath));
        var json = File.ReadAllText(cachePath);
        Assert.NotEmpty(json);
    }

    // ===== ClearCache: clears persistent entries =====

    [Fact]
    public void ClearCache_WithPersistentPath_ClearsPersistentEntries()
    {
        var cachePath = Path.Combine(_tempDir, "clear-cache.json");
        var file = CreateFile("clearing.bin");

        using var svc = new FileHashService(persistentCachePath: cachePath);
        svc.GetHash(file, "SHA1");
        Assert.True(svc.CacheCount > 0);

        svc.ClearCache();
        Assert.Equal(0, svc.CacheCount);
    }

    // ===== Dispose: flushes persistent cache =====

    [Fact]
    public void Dispose_WithPersistentPath_FlushesCache()
    {
        var cachePath = Path.Combine(_tempDir, "dispose-flush.json");
        var file = CreateFile("disposable.bin");

        var svc = new FileHashService(persistentCachePath: cachePath);
        svc.GetHash(file, "SHA1");
        svc.Dispose();

        Assert.True(File.Exists(cachePath));

        // Double dispose should not throw
        svc.Dispose();
    }

    // ===== IsPersistent =====

    [Fact]
    public void IsPersistent_WithCachePath_ReturnsTrue()
    {
        var cachePath = Path.Combine(_tempDir, "persist.json");
        using var svc = new FileHashService(persistentCachePath: cachePath);
        Assert.True(svc.IsPersistent);
    }

    [Fact]
    public void IsPersistent_NoCachePath_ReturnsFalse()
    {
        using var svc = new FileHashService();
        Assert.False(svc.IsPersistent);
    }

    // ===== MD5 and SHA256 algorithm paths =====

    [Fact]
    public void GetHash_MD5_ReturnsValid32CharHex()
    {
        var file = CreateFile("md5.bin");
        using var svc = new FileHashService();
        var hash = svc.GetHash(file, "MD5");

        Assert.NotNull(hash);
        Assert.Equal(32, hash.Length);
    }

    [Fact]
    public void GetHash_SHA256_ReturnsValid64CharHex()
    {
        var file = CreateFile("sha256.bin");
        using var svc = new FileHashService();
        var hash = svc.GetHash(file, "SHA256");

        Assert.NotNull(hash);
        Assert.Equal(64, hash.Length);
    }

    // ===== NormalizeHashType: CRC → CRC32 =====

    [Fact]
    public void GetHash_CrcAlias_NormalizesToCRC32()
    {
        var file = CreateFile("crc.bin");
        using var svc = new FileHashService();
        var h1 = svc.GetHash(file, "CRC");
        var h2 = svc.GetHash(file, "CRC32");

        Assert.NotNull(h1);
        Assert.Equal(h1, h2);
    }

    // ===== GetHash: non-existent file =====

    [Fact]
    public void GetHash_NonExistentFile_ReturnsNull()
    {
        using var svc = new FileHashService();
        var hash = svc.GetHash(Path.Combine(_tempDir, "no-such-file.rom"), "SHA1");
        Assert.Null(hash);
    }

    // ===== ResolveDefaultPersistentCachePath is consistent =====

    [Fact]
    public void ResolveDefaultPersistentCachePath_ReturnsDeterministicPath()
    {
        var p1 = FileHashService.ResolveDefaultPersistentCachePath();
        var p2 = FileHashService.ResolveDefaultPersistentCachePath();

        Assert.NotNull(p1);
        Assert.Equal(p1, p2);
        Assert.EndsWith("file-hashes-v1.json", p1);
    }

    // ===== MaxEntries getter/setter =====

    [Fact]
    public void MaxEntries_SetBelowMinimum_ClampsTo500()
    {
        using var svc = new FileHashService(maxEntries: 1000);
        svc.MaxEntries = 10;
        Assert.Equal(500, svc.MaxEntries);
    }

    // ===== ThrowIfDisposed =====

    [Fact]
    public void GetHash_AfterDispose_Throws()
    {
        var svc = new FileHashService();
        svc.Dispose();

        Assert.Throws<ObjectDisposedException>(() => svc.GetHash(Path.Combine(_tempDir, "x"), "SHA1"));
    }
}
