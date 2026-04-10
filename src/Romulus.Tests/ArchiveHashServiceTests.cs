using System.IO.Compression;
using Romulus.Infrastructure.Hashing;
using Xunit;

namespace Romulus.Tests;

public class ArchiveHashServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ArchiveHashService _service;

    public ArchiveHashServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ArchiveHashTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _service = new ArchiveHashService(maxEntries: 64);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private string CreateZipWithEntries(string name, params (string entry, byte[] data)[] entries)
    {
        var zipPath = Path.Combine(_tempDir, name);
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var (entry, data) in entries)
        {
            var e = archive.CreateEntry(entry);
            using var s = e.Open();
            s.Write(data, 0, data.Length);
        }
        return zipPath;
    }

    // ── ZIP hashing ──

    [Fact]
    public void GetArchiveHashes_ZipSha1_ReturnsHashes()
    {
        var zip = CreateZipWithEntries("test.zip",
            ("file1.bin", new byte[] { 0x01, 0x02, 0x03 }),
            ("file2.bin", new byte[] { 0xFF, 0xFE }));

        var hashes = _service.GetArchiveHashes(zip, "SHA1");

        // 2 entries × (SHA1 + native CRC32 from ZIP header) = 4 hashes
        Assert.Equal(4, hashes.Length);
        var sha1Hashes = hashes.Where(h => h.Length == 40).ToArray();
        var crc32Hashes = hashes.Where(h => h.Length == 8).ToArray();
        Assert.Equal(2, sha1Hashes.Length);
        Assert.Equal(2, crc32Hashes.Length);
    }

    [Fact]
    public void GetArchiveHashes_ZipCrc32_ReturnsHashes()
    {
        var zip = CreateZipWithEntries("crc.zip",
            ("data.bin", new byte[] { 0xAA, 0xBB, 0xCC }));

        var hashes = _service.GetArchiveHashes(zip, "CRC32");

        Assert.Single(hashes);
        Assert.Equal(8, hashes[0].Length); // CRC32 = 8 hex chars
    }

    [Fact]
    public void GetArchiveHashes_ZipMd5_ReturnsHashes()
    {
        var zip = CreateZipWithEntries("md5.zip",
            ("data.bin", new byte[] { 0x10, 0x20, 0x30 }));

        var hashes = _service.GetArchiveHashes(zip, "MD5");

        // 1 entry × (MD5 + native CRC32) = 2 hashes
        Assert.Equal(2, hashes.Length);
        Assert.Equal(32, hashes[0].Length); // MD5 = 32 hex chars
        Assert.Equal(8, hashes[1].Length);  // CRC32 = 8 hex chars
    }

    [Fact]
    public void GetArchiveHashes_ZipSha256_ReturnsHashes()
    {
        var zip = CreateZipWithEntries("sha256.zip",
            ("data.bin", new byte[] { 0x42 }));

        var hashes = _service.GetArchiveHashes(zip, "SHA256");

        // 1 entry × (SHA256 + native CRC32) = 2 hashes
        Assert.Equal(2, hashes.Length);
        Assert.Equal(64, hashes[0].Length); // SHA256 = 64 hex chars
        Assert.Equal(8, hashes[1].Length);  // CRC32 = 8 hex chars
    }

    // ── Caching ──

    [Fact]
    public void GetArchiveHashes_ResultIsCached()
    {
        var zip = CreateZipWithEntries("cached.zip",
            ("file.bin", new byte[] { 0x01 }));

        var first = _service.GetArchiveHashes(zip, "SHA1");
        var second = _service.GetArchiveHashes(zip, "SHA1");

        Assert.Equal(first, second);
        Assert.True(_service.CacheCount > 0);
    }

    [Fact]
    public void ClearCache_ResetsCacheCount()
    {
        var zip = CreateZipWithEntries("x.zip", ("f.bin", new byte[] { 0x01 }));
        _service.GetArchiveHashes(zip);
        Assert.True(_service.CacheCount > 0);

        _service.ClearCache();
        Assert.Equal(0, _service.CacheCount);
    }

    // ── Edge cases ──

    [Fact]
    public void GetArchiveHashes_Null_ReturnsEmpty()
    {
        Assert.Empty(_service.GetArchiveHashes(null!));
    }

    [Fact]
    public void GetArchiveHashes_Missing_ReturnsEmpty()
    {
        Assert.Empty(_service.GetArchiveHashes(Path.Combine(_tempDir, "nope.zip")));
    }

    [Fact]
    public void GetArchiveHashes_EmptyZip_ReturnsEmpty()
    {
        var zipPath = Path.Combine(_tempDir, "empty.zip");
        using (var _ = ZipFile.Open(zipPath, ZipArchiveMode.Create)) { }

        Assert.Empty(_service.GetArchiveHashes(zipPath));
    }

    [Fact]
    public void GetArchiveHashes_CorruptFile_ReturnsEmpty()
    {
        var path = Path.Combine(_tempDir, "corrupt.zip");
        File.WriteAllBytes(path, new byte[] { 0xFF, 0xFE, 0x00 });

        Assert.Empty(_service.GetArchiveHashes(path));
    }

    [Fact]
    public void GetArchiveHashes_OversizedArchive_ReturnsEmpty()
    {
        var zip = CreateZipWithEntries("big.zip", ("f.bin", new byte[] { 0x01 }));
        // Use a service with 1 byte max size
        var service = new ArchiveHashService(maxArchiveSizeBytes: 1);

        Assert.Empty(service.GetArchiveHashes(zip));
    }

    [Fact]
    public void GetArchiveHashes_7z_WithoutToolRunner_ReturnsEmpty()
    {
        var path = Path.Combine(_tempDir, "test.7z");
        File.WriteAllBytes(path, new byte[] { 0x37, 0x7A }); // 7z magic stub

        Assert.Empty(_service.GetArchiveHashes(path));
    }

    [Fact]
    public void GetArchiveHashes_UnsupportedExt_ReturnsEmpty()
    {
        var path = Path.Combine(_tempDir, "test.rar");
        File.WriteAllBytes(path, new byte[] { 0x52, 0x61, 0x72 });

        Assert.Empty(_service.GetArchiveHashes(path));
    }

    // ── Zip-Slip detection ──

    [Theory]
    [InlineData("../etc/passwd")]
    [InlineData("..\\Windows\\System32\\cmd.exe")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\System32")]
    public void AreEntryPathsSafe_MaliciousPaths_ReturnsFalse(string path)
    {
        Assert.False(ArchiveHashService.AreEntryPathsSafe(new[] { path }));
    }

    [Theory]
    [InlineData("game.bin")]
    [InlineData("subfolder/game.iso")]
    [InlineData("a/b/c.txt")]
    public void AreEntryPathsSafe_SafePaths_ReturnsTrue(string path)
    {
        Assert.True(ArchiveHashService.AreEntryPathsSafe(new[] { path }));
    }

    [Fact]
    public void GetArchiveHashes_DifferentHashTypes_CachedSeparately()
    {
        var zip = CreateZipWithEntries("multi.zip",
            ("file.bin", new byte[] { 0x01, 0x02 }));

        var sha1 = _service.GetArchiveHashes(zip, "SHA1");
        var crc = _service.GetArchiveHashes(zip, "CRC32");

        Assert.NotEqual(sha1[0], crc[0]);
        Assert.Equal(2, _service.CacheCount); // two separate cache entries
    }
}
