using System.IO.Compression;
using System.Reflection;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.Hashing;
using Romulus.Core.GameKeys;
using Xunit;

namespace Romulus.Tests;

public sealed class TrackerAllFindingsBatch2RedTests : IDisposable
{
    private readonly string _tempDir;

    public TrackerAllFindingsBatch2RedTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_Batch2_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Th13_Crc32_MustExposeCancellationAwareHashStreamOverload()
    {
        var method = typeof(Crc32).GetMethod(
            "HashStream",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(Stream), typeof(CancellationToken)],
            modifiers: null);

        Assert.NotNull(method);
    }

    [Fact]
    public void Sort05_ArchiveHashService_Sha1Mode_MustNotMixInCrc32Hashes()
    {
        var zipPath = Path.Combine(_tempDir, "single.zip");
        var payloadPath = Path.Combine(_tempDir, "payload.bin");
        File.WriteAllBytes(payloadPath, [1, 2, 3, 4, 5, 6, 7]);

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(payloadPath, "payload.bin");
        }

        var sut = new ArchiveHashService();
        var hashes = sut.GetArchiveHashes(zipPath, "SHA1");

        Assert.Single(hashes);
        Assert.All(hashes, hash => Assert.Equal(40, hash.Length));
    }

    [Fact]
    public void Sort04_AuditCsvStore_FileLockMap_MustNotGrowUnbounded()
    {
        var auditPath = Path.Combine(_tempDir, "audit.csv");
        IAuditStore store = new AuditCsvStore();

        for (var i = 0; i < 5; i++)
        {
            store.AppendAuditRow(
                auditPath,
                rootPath: _tempDir,
                oldPath: Path.Combine(_tempDir, $"old-{i}.rom"),
                newPath: Path.Combine(_tempDir, $"new-{i}.rom"),
                action: "MOVE",
                category: "GAME");
        }

        var field = typeof(AuditCsvStore).GetField("FileLocks", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var lockMap = field!.GetValue(null);
        Assert.NotNull(lockMap);

        var countProperty = lockMap!.GetType().GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(countProperty);
        var countValue = (int)countProperty!.GetValue(lockMap)!;

        Assert.Equal(0, countValue);
    }

    [Fact]
    public void Core08_DiscPadding_Disc001AndDisc1_MustNormalizeToSameKey()
    {
        var keyA = GameKeyNormalizer.Normalize("Final Fantasy VII (Disc 001) (Europe)");
        var keyB = GameKeyNormalizer.Normalize("Final Fantasy VII (Disc 1) (Europe)");

        Assert.Equal(keyB, keyA);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
