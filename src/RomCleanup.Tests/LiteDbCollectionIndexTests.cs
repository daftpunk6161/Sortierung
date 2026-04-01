using LiteDB;
using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.Index;
using Xunit;

namespace RomCleanup.Tests;

public sealed class LiteDbCollectionIndexTests : IDisposable
{
    private readonly string _tempDir;

    public LiteDbCollectionIndexTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RomCleanup_IndexTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task Metadata_IsInitialized_WithCurrentSchemaVersion()
    {
        using var index = CreateIndex();

        var metadata = await index.GetMetadataAsync();

        Assert.Equal(1, metadata.SchemaVersion);
        Assert.Equal(DateTimeKind.Utc, metadata.CreatedUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, metadata.UpdatedUtc.Kind);
        Assert.True(metadata.UpdatedUtc >= metadata.CreatedUtc);
    }

    [Fact]
    public async Task UpsertAndGetByPath_RoundTrip_PreservesNormalizedEntry()
    {
        using var index = CreateIndex();
        var path = Path.Combine(_tempDir, "roms", "SNES", "Mario.sfc");
        var root = Path.Combine(_tempDir, "roms", "SNES");

        await index.UpsertEntriesAsync(
        [
            new CollectionIndexEntry
            {
                Path = path,
                Root = root,
                FileName = "Mario.sfc",
                Extension = ".sfc",
                SizeBytes = 1234,
                LastWriteUtc = new DateTime(2026, 4, 1, 9, 0, 0, DateTimeKind.Utc),
                LastScannedUtc = new DateTime(2026, 4, 1, 9, 1, 0, DateTimeKind.Utc),
                PrimaryHashType = "sha1",
                PrimaryHash = "ABCDEF",
                ConsoleKey = "SNES",
                GameKey = "super-mario-world",
                Region = "EU",
                DatMatch = true,
                DatGameName = "Super Mario World (Europe)",
                DatAuditStatus = DatAuditStatus.Have,
                SortDecision = SortDecision.DatVerified,
                DecisionClass = DecisionClass.DatVerified,
                EvidenceTier = EvidenceTier.Tier0_ExactDat,
                PrimaryMatchKind = MatchKind.ExactDatHash,
                DetectionConfidence = 100,
                ClassificationReasonCode = "dat-hash",
                ClassificationConfidence = 100
            }
        ]);

        var entry = await index.TryGetByPathAsync(path);

        Assert.NotNull(entry);
        Assert.Equal(Path.GetFullPath(path), entry.Path);
        Assert.Equal(Path.GetFullPath(root), entry.Root);
        Assert.Equal("SHA1", entry.PrimaryHashType);
        Assert.Equal("abcdef", entry.PrimaryHash);
        Assert.Equal("SNES", entry.ConsoleKey);
        Assert.Equal("super-mario-world", entry.GameKey);
        Assert.Equal(SortDecision.DatVerified, entry.SortDecision);
    }

    [Fact]
    public async Task GetByPathsAsync_PreservesInputOrder_ForExistingEntries()
    {
        using var index = CreateIndex();
        var root = Path.Combine(_tempDir, "roms");
        var firstPath = Path.Combine(root, "A.sfc");
        var secondPath = Path.Combine(root, "B.sfc");

        await index.UpsertEntriesAsync(
        [
            new CollectionIndexEntry { Path = firstPath, Root = root, FileName = "A.sfc", Extension = ".sfc", ConsoleKey = "SNES" },
            new CollectionIndexEntry { Path = secondPath, Root = root, FileName = "B.sfc", Extension = ".sfc", ConsoleKey = "SNES" }
        ]);

        var entries = await index.GetByPathsAsync([secondPath, firstPath]);

        Assert.Equal(2, entries.Count);
        Assert.Equal(Path.GetFullPath(secondPath), entries[0].Path);
        Assert.Equal(Path.GetFullPath(firstPath), entries[1].Path);
    }

    [Fact]
    public async Task SetHashAndTryGetHash_UseCompositeFileStateKey()
    {
        using var index = CreateIndex();
        var lastWriteUtc = new DateTime(2026, 4, 1, 9, 0, 0, DateTimeKind.Utc);
        var path = Path.Combine(_tempDir, "roms", "hash.bin");

        await index.SetHashAsync(new CollectionHashCacheEntry
        {
            Path = path,
            Algorithm = "crc",
            SizeBytes = 4096,
            LastWriteUtc = lastWriteUtc,
            Hash = "AABBCCDD",
            RecordedUtc = new DateTime(2026, 4, 1, 9, 5, 0, DateTimeKind.Utc)
        });

        var hit = await index.TryGetHashAsync(path, "CRC32", 4096, lastWriteUtc);
        var miss = await index.TryGetHashAsync(path, "CRC32", 4097, lastWriteUtc);

        Assert.NotNull(hit);
        Assert.Equal("CRC32", hit.Algorithm);
        Assert.Equal("aabbccdd", hit.Hash);
        Assert.Null(miss);
    }

    [Fact]
    public async Task AppendRunSnapshotAndListRunSnapshots_ReturnNewestFirst()
    {
        using var index = CreateIndex();

        await index.AppendRunSnapshotAsync(new CollectionRunSnapshot
        {
            RunId = "run-1",
            StartedUtc = new DateTime(2026, 4, 1, 9, 0, 0, DateTimeKind.Utc),
            CompletedUtc = new DateTime(2026, 4, 1, 9, 1, 0, DateTimeKind.Utc),
            Roots = [Path.Combine(_tempDir, "roms-a")],
            RootFingerprint = "A",
            TotalFiles = 10,
            Games = 8
        });

        await index.AppendRunSnapshotAsync(new CollectionRunSnapshot
        {
            RunId = "run-2",
            StartedUtc = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
            CompletedUtc = new DateTime(2026, 4, 1, 10, 2, 0, DateTimeKind.Utc),
            Roots = [Path.Combine(_tempDir, "roms-b")],
            RootFingerprint = "B",
            TotalFiles = 20,
            Games = 15
        });

        var snapshotCount = await index.CountRunSnapshotsAsync();
        var snapshots = await index.ListRunSnapshotsAsync();

        Assert.Equal(2, snapshotCount);
        Assert.Equal(2, snapshots.Count);
        Assert.Equal("run-2", snapshots[0].RunId);
        Assert.Equal("run-1", snapshots[1].RunId);
    }

    [Fact]
    public async Task Constructor_RecoversFromCorruptDatabaseFile()
    {
        var dbPath = Path.Combine(_tempDir, "collection.db");
        await File.WriteAllTextAsync(dbPath, "not-a-litedb-file");

        using var index = CreateIndex(dbPath);
        var metadata = await index.GetMetadataAsync();
        var backups = Directory.GetFiles(_tempDir, "collection.db.open-failure.*.bak");

        Assert.Equal(1, metadata.SchemaVersion);
        Assert.NotEmpty(backups);
        Assert.True(File.Exists(dbPath));
    }

    [Fact]
    public async Task Constructor_RecoversFromUnsupportedSchemaVersion()
    {
        var dbPath = Path.Combine(_tempDir, "collection.db");
        using (var database = new LiteDatabase(dbPath))
        {
            var metadata = database.GetCollection<SchemaSeedDocument>("metadata");
            metadata.Upsert(new SchemaSeedDocument
            {
                Id = "collection-index",
                SchemaVersion = 99,
                CreatedUtcTicks = DateTime.UtcNow.Ticks,
                UpdatedUtcTicks = DateTime.UtcNow.Ticks
            });
        }

        using var index = CreateIndex(dbPath);
        var metadataResult = await index.GetMetadataAsync();
        var backups = Directory.GetFiles(_tempDir, "collection.db.schema-mismatch.*.bak");

        Assert.Equal(1, metadataResult.SchemaVersion);
        Assert.NotEmpty(backups);
        Assert.True(File.Exists(dbPath));
    }

    private LiteDbCollectionIndex CreateIndex(string? dbPath = null)
        => new(dbPath ?? Path.Combine(_tempDir, "collection.db"));

    private sealed class SchemaSeedDocument
    {
        public string Id { get; set; } = "";
        public int SchemaVersion { get; set; }
        public long CreatedUtcTicks { get; set; }
        public long UpdatedUtcTicks { get; set; }
    }
}
