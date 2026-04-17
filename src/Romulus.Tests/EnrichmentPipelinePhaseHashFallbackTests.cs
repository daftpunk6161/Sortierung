using System.IO.Compression;
using System.Security.Cryptography;
using Romulus.Infrastructure.Hashing;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

public sealed class EnrichmentPipelinePhaseHashFallbackTests : IDisposable
{
    private readonly string _tempDir;

    public EnrichmentPipelinePhaseHashFallbackTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "EnrichmentHashFallback_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void GetLookupHashTypeOrder_DefaultChain_IsSha1ThenCrc32ThenMd5()
    {
        var order = EnrichmentPipelinePhase.GetLookupHashTypeOrder("SHA1");

        Assert.Equal(["SHA1", "CRC32", "MD5"], order);
    }

    [Fact]
    public void GetLookupHashTypeOrder_CrcAlias_IsNormalizedAndUnique()
    {
        var order = EnrichmentPipelinePhase.GetLookupHashTypeOrder("crc");

        Assert.Equal(["CRC32", "SHA1", "MD5"], order);
    }

    [Fact]
    public void GetFileLookupHashes_ComputesFallbackHashes_InDeterministicOrder()
    {
        var filePath = Path.Combine(_tempDir, "game.bin");
        File.WriteAllBytes(filePath, [0x01, 0x02, 0x03, 0x04, 0x05]);

        var hashService = new FileHashService();
        var hashes = EnrichmentPipelinePhase.GetFileLookupHashes(filePath, hashService, "SHA1");

        var expectedSha1 = Convert.ToHexString(SHA1.HashData(File.ReadAllBytes(filePath))).ToLowerInvariant();

        Assert.Equal(3, hashes.Count);
        Assert.Equal(expectedSha1, hashes[0]);
        Assert.All(hashes, h => Assert.False(string.IsNullOrWhiteSpace(h)));
    }

    [Fact]
    public void GetArchiveLookupHashes_ComputesFallbackHashes_ForZip()
    {
        var zipPath = Path.Combine(_tempDir, "single-entry.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("game.bin");
            using var stream = entry.Open();
            stream.Write([0xAA, 0xBB, 0xCC, 0xDD]);
        }

        var archiveHashService = new ArchiveHashService();
        var hashes = EnrichmentPipelinePhase.GetArchiveLookupHashes(zipPath, archiveHashService, "SHA1");

        Assert.Equal(3, hashes.Count);
        Assert.All(hashes, h => Assert.False(string.IsNullOrWhiteSpace(h)));
    }
}
