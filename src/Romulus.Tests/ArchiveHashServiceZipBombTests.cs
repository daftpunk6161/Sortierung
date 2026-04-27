using System.IO.Compression;
using Romulus.Infrastructure.Hashing;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// F-DAT-15 + F-DAT-16: ArchiveHashService must reject ZIPs whose declared
/// uncompressed payload exceeds the configured cumulative cap (zipbomb-Schutz)
/// and surface the skip reason through the optional logger.
/// </summary>
public sealed class ArchiveHashServiceZipBombTests
{
    [Fact]
    public void HashZipEntries_RespectsCumulativeUncompressedCap_AndLogsReason()
    {
        var tempZip = Path.Combine(Path.GetTempPath(), $"romulus_zipbomb_{Guid.NewGuid():N}.zip");
        try
        {
            // Two real entries (1 MB each) inside the ZIP.
            using (var fs = File.Create(tempZip))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                for (var i = 0; i < 2; i++)
                {
                    var entry = archive.CreateEntry($"file_{i}.bin", CompressionLevel.NoCompression);
                    using var es = entry.Open();
                    es.Write(new byte[1024 * 1024], 0, 1024 * 1024);
                }
            }

            var skipMessages = new List<string>();
            // Cap below the cumulative size of two entries (2 MB) so the second entry must trip
            // the zipbomb guard. Pick 1.5 MB.
            var svc = new ArchiveHashService(
                toolRunner: null,
                maxArchiveSizeBytes: 50 * 1024 * 1024,
                maxCumulativeUncompressedBytes: 1_500_000,
                log: msg => skipMessages.Add(msg));

            var hashes = svc.GetArchiveHashes(tempZip, "SHA1");

            // Hard fail-closed: cap exceeded => no hashes at all (matches 7z path semantics).
            Assert.Empty(hashes);
            Assert.Contains(skipMessages, m =>
                m.Contains("CumulativeBytesExceeded", StringComparison.Ordinal)
                && m.Contains("zipbomb-Schutz", StringComparison.Ordinal));
        }
        finally
        {
            try { File.Delete(tempZip); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void HashZipEntries_StaysBelowCap_HashesAllEntries()
    {
        var tempZip = Path.Combine(Path.GetTempPath(), $"romulus_zipok_{Guid.NewGuid():N}.zip");
        try
        {
            using (var fs = File.Create(tempZip))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                for (var i = 0; i < 2; i++)
                {
                    var entry = archive.CreateEntry($"file_{i}.bin", CompressionLevel.NoCompression);
                    using var es = entry.Open();
                    es.Write(new byte[1024], 0, 1024);
                }
            }

            var svc = new ArchiveHashService(
                toolRunner: null,
                maxArchiveSizeBytes: 50 * 1024 * 1024,
                maxCumulativeUncompressedBytes: 10 * 1024 * 1024);

            var hashes = svc.GetArchiveHashes(tempZip, "SHA1");
            Assert.Equal(2, hashes.Length);
        }
        finally
        {
            try { File.Delete(tempZip); } catch { /* best effort */ }
        }
    }
}
