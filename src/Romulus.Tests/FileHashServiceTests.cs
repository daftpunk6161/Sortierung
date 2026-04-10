using System.Buffers.Binary;
using System.Text;
using Romulus.Infrastructure.Hashing;
using Romulus.Infrastructure.Index;
using Xunit;

namespace Romulus.Tests;

public class FileHashServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileHashService _svc;

    public FileHashServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_HashTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _svc = new FileHashService(1000);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Theory]
    [InlineData("SHA1")]
    [InlineData("SHA256")]
    [InlineData("MD5")]
    [InlineData("CRC32")]
    public void GetHash_AllAlgorithms_ReturnNonEmpty(string algo)
    {
        var file = CreateTestFile("test.bin", new byte[] { 1, 2, 3, 4, 5 });
        var hash = _svc.GetHash(file, algo);
        Assert.NotNull(hash);
        Assert.NotEmpty(hash);
    }

    [Fact]
    public void GetHash_SHA1_IsDeterministic()
    {
        var file = CreateTestFile("det.bin", new byte[] { 0x48, 0x65, 0x6c, 0x6c, 0x6f });
        var h1 = _svc.GetHash(file, "SHA1");
        var h2 = _svc.GetHash(file, "SHA1");
        Assert.Equal(h1, h2);
    }

    [Fact]
    public void GetHash_CRC32_KnownValue()
    {
        // CRC32 of "Hello" = f7d18982
        var file = CreateTestFile("crc.bin", new byte[] { 0x48, 0x65, 0x6c, 0x6c, 0x6f });
        Assert.Equal("f7d18982", _svc.GetHash(file, "CRC32"));
    }

    [Fact]
    public void GetHash_ChdV5Sha1_UsesEmbeddedRawSha1()
    {
        var chd = new byte[512];
        "MComprHD"u8.CopyTo(chd);
        BinaryPrimitives.WriteUInt32BigEndian(chd.AsSpan(12, 4), 5);

        var expectedBytes = Enumerable.Range(1, 20).Select(i => (byte)i).ToArray();
        expectedBytes.CopyTo(chd, 0x40);

        var file = CreateTestFile("game.chd", chd);
        var hash = _svc.GetHash(file, "SHA1");

        Assert.Equal(Convert.ToHexString(expectedBytes).ToLowerInvariant(), hash);
    }

    [Fact]
    public void GetHash_ChdV4Sha1_UsesLegacyEmbeddedSha1()
    {
        var chd = new byte[512];
        "MComprHD"u8.CopyTo(chd);
        BinaryPrimitives.WriteUInt32BigEndian(chd.AsSpan(12, 4), 4);

        var expectedBytes = Enumerable.Range(20, 20).Select(i => (byte)i).ToArray();
        expectedBytes.CopyTo(chd, 0x50);

        var file = CreateTestFile("game-v4.chd", chd);
        var hash = _svc.GetHash(file, "SHA1");

        Assert.Equal(Convert.ToHexString(expectedBytes).ToLowerInvariant(), hash);
    }

    [Fact]
    public void GetHash_NonExistent_ReturnsNull()
    {
        Assert.Null(_svc.GetHash(Path.Combine(_tempDir, "nope.bin"), "SHA1"));
    }

    [Fact]
    public void GetHash_UsesCache_SecondCallFaster()
    {
        var file = CreateTestFile("cached.bin", new byte[] { 1, 2, 3 });

        var h1 = _svc.GetHash(file, "SHA1");
        Assert.Equal(1, _svc.CacheCount);

        var h2 = _svc.GetHash(file, "SHA1");
        Assert.Equal(h1, h2);
        Assert.Equal(1, _svc.CacheCount); // still just one entry
    }

    [Fact]
    public void GetHash_DifferentAlgos_DifferentCacheEntries()
    {
        var file = CreateTestFile("multi.bin", new byte[] { 1, 2, 3 });

        _svc.GetHash(file, "SHA1");
        _svc.GetHash(file, "MD5");

        Assert.Equal(2, _svc.CacheCount); // two separate cache keys
    }

    [Fact]
    public void ClearCache_ResetsCount()
    {
        var file = CreateTestFile("clear.bin", new byte[] { 1 });
        _svc.GetHash(file, "SHA1");
        Assert.Equal(1, _svc.CacheCount);

        _svc.ClearCache();
        Assert.Equal(0, _svc.CacheCount);
    }

    [Fact]
    public void MaxEntries_CanBeSet()
    {
        var svc = new FileHashService(1000);
        Assert.Equal(1000, svc.MaxEntries);

        svc.MaxEntries = 2000;
        Assert.Equal(2000, svc.MaxEntries);
    }

    [Fact]
    public void MaxEntries_MinimumIs500()
    {
        var svc = new FileHashService(1000);
        svc.MaxEntries = 10; // below minimum, should clamp to 500
        Assert.Equal(500, svc.MaxEntries);
    }

    [Fact]
    public void PersistentCache_ReloadsHashAcrossInstances_WhenFileUnchanged()
    {
        var cachePath = Path.Combine(_tempDir, "cache", "file-hashes.json");
        var file = CreateTestFile("persist.bin", new byte[] { 9, 8, 7, 6 });

        var first = new FileHashService(1000, cachePath);
        var originalHash = first.GetHash(file, "SHA1");
        first.FlushPersistentCache();

        var second = new FileHashService(1000, cachePath);
        var reloadedHash = second.GetHash(file, "SHA1");

        Assert.Equal(originalHash, reloadedHash);
        Assert.True(File.Exists(cachePath));
    }

    [Fact]
    public void PersistentCache_InvalidatesEntry_WhenFileChanges()
    {
        var cachePath = Path.Combine(_tempDir, "cache", "file-hashes.json");
        var file = CreateTestFile("changed.bin", new byte[] { 1, 2, 3, 4 });

        var first = new FileHashService(1000, cachePath);
        var originalHash = first.GetHash(file, "SHA1");
        first.FlushPersistentCache();

        File.WriteAllBytes(file, new byte[] { 4, 3, 2, 1, 0 });
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(1));

        var second = new FileHashService(1000, cachePath);
        var newHash = second.GetHash(file, "SHA1");

        Assert.NotEqual(originalHash, newHash);
    }

    [Fact]
    public void PersistentCache_ClearCache_RemovesPersistedEntriesOnNextFlush()
    {
        var cachePath = Path.Combine(_tempDir, "cache", "file-hashes.json");
        var file = CreateTestFile("clear-persist.bin", new byte[] { 1, 1, 2, 3 });

        var first = new FileHashService(1000, cachePath);
        Assert.NotNull(first.GetHash(file, "SHA1"));
        first.FlushPersistentCache();

        var second = new FileHashService(1000, cachePath);
        second.ClearCache();
        second.FlushPersistentCache();

        var json = File.ReadAllText(cachePath);
        Assert.DoesNotContain("clear-persist.bin", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CollectionIndexPersistence_ReloadsHashAcrossInstances_WhenFileUnchanged()
    {
        var dbPath = Path.Combine(_tempDir, "collection.db");
        var file = CreateTestFile("persist-index.bin", new byte[] { 7, 8, 9, 10 });

        string? originalHash;
        using (var first = new FileHashService(
                   1000,
                   collectionIndex: new LiteDbCollectionIndex(dbPath),
                   ownsCollectionIndex: true))
        {
            originalHash = first.GetHash(file, "SHA1");
        }

        using var second = new FileHashService(
            1000,
            collectionIndex: new LiteDbCollectionIndex(dbPath),
            ownsCollectionIndex: true);
        var reloadedHash = second.GetHash(file, "SHA1");

        Assert.Equal(originalHash, reloadedHash);
        Assert.True(File.Exists(dbPath));
    }

    [Fact]
    public void CollectionIndexPersistence_InvalidatesEntry_WhenFileChanges()
    {
        var dbPath = Path.Combine(_tempDir, "collection-invalidated.db");
        var file = CreateTestFile("changed-index.bin", new byte[] { 1, 2, 3, 4 });

        string? originalHash;
        using (var first = new FileHashService(
                   1000,
                   collectionIndex: new LiteDbCollectionIndex(dbPath),
                   ownsCollectionIndex: true))
        {
            originalHash = first.GetHash(file, "SHA1");
        }

        File.WriteAllBytes(file, new byte[] { 4, 3, 2, 1, 0 });
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(1));

        using var second = new FileHashService(
            1000,
            collectionIndex: new LiteDbCollectionIndex(dbPath),
            ownsCollectionIndex: true);
        var newHash = second.GetHash(file, "SHA1");

        Assert.NotEqual(originalHash, newHash);
    }

    [Fact]
    public void NormalizePathForCacheKey_NfdAndNfcProduceSameKeyPath()
    {
        var nfdPath = Path.Combine(_tempDir, "U\u0308bung.zip");
        var nfcPath = Path.Combine(_tempDir, "\u00DCbung.zip");

        var normalizedNfd = FileHashService.NormalizePathForCacheKey(nfdPath);
        var normalizedNfc = FileHashService.NormalizePathForCacheKey(nfcPath);

        Assert.Equal(normalizedNfc, normalizedNfd);
        Assert.True(normalizedNfd.IsNormalized(NormalizationForm.FormC));
    }

    private string CreateTestFile(string name, byte[] data)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, data);
        return path;
    }
}
