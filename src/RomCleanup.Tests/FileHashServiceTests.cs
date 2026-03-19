using System.Buffers.Binary;
using RomCleanup.Infrastructure.Hashing;
using Xunit;

namespace RomCleanup.Tests;

public class FileHashServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileHashService _svc;

    public FileHashServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RomCleanup_HashTest_" + Guid.NewGuid().ToString("N")[..8]);
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

    private string CreateTestFile(string name, byte[] data)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, data);
        return path;
    }
}
