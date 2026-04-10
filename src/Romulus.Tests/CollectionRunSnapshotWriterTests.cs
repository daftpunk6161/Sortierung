using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Index;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

public sealed class CollectionRunSnapshotWriterTests : IDisposable
{
    private readonly string _tempDir;

    public CollectionRunSnapshotWriterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_SnapshotTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void CreateSnapshot_ExtractsApiRunId_FromAuditPath()
    {
        var options = new RunOptions
        {
            Roots = [Path.Combine(_tempDir, "roms")],
            Mode = RunConstants.ModeMove,
            AuditPath = Path.Combine(_tempDir, "audit", "audit-abcd1234.csv")
        };

        var result = new RunResult
        {
            Status = RunConstants.StatusOk,
            ExitCode = 0,
            TotalFilesScanned = 5,
            DurationMs = 250,
            AllCandidates =
            [
                new RomCandidate { MainPath = Path.Combine(_tempDir, "roms", "Game A.zip"), SizeBytes = 1024 },
                new RomCandidate { MainPath = Path.Combine(_tempDir, "roms", "Game B.zip"), SizeBytes = 2048 }
            ]
        };

        var projection = RunProjectionFactory.Create(result);

        var snapshot = CollectionRunSnapshotWriter.CreateSnapshot(
            options,
            result,
            projection,
            new DateTime(2026, 4, 1, 9, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 1, 9, 0, 1, DateTimeKind.Utc));

        Assert.Equal("abcd1234", snapshot.RunId);
        Assert.Equal(RunConstants.ModeMove, snapshot.Mode);
        Assert.Equal(RunConstants.StatusOk, snapshot.Status);
        Assert.Single(snapshot.Roots);
        Assert.Equal(3072, snapshot.CollectionSizeBytes);
    }

    [Fact]
    public async Task TryPersistAsync_AppendsSnapshot_ToCollectionIndex()
    {
        var indexPath = Path.Combine(_tempDir, "collection.db");
        using var index = new LiteDbCollectionIndex(indexPath);

        var options = new RunOptions
        {
            Roots = [Path.Combine(_tempDir, "roms")],
            Mode = RunConstants.ModeDryRun,
            AuditPath = Path.Combine(_tempDir, "audit", "audit-run-42.csv")
        };

        var result = new RunResult
        {
            Status = RunConstants.StatusCompletedWithErrors,
            ExitCode = 1,
            TotalFilesScanned = 12,
            WinnerCount = 7,
            LoserCount = 5,
            DurationMs = 987,
            AllCandidates =
            [
                new RomCandidate { MainPath = Path.Combine(_tempDir, "roms", "Game (USA).zip"), Category = FileCategory.Game, SizeBytes = 4096 },
                new RomCandidate { MainPath = Path.Combine(_tempDir, "roms", "Bad (Beta).zip"), Category = FileCategory.Junk, SizeBytes = 2048 }
            ]
        };

        await CollectionRunSnapshotWriter.TryPersistAsync(
            index,
            options,
            result,
            new DateTime(2026, 4, 1, 9, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 1, 9, 0, 1, DateTimeKind.Utc));

        var snapshots = await index.ListRunSnapshotsAsync();

        Assert.Single(snapshots);
        Assert.Equal("run-42", snapshots[0].RunId);
        Assert.Equal(RunConstants.StatusCompletedWithErrors, snapshots[0].Status);
        Assert.Equal(12, snapshots[0].TotalFiles);
        Assert.Equal(987, snapshots[0].DurationMs);
        Assert.Equal(6144, snapshots[0].CollectionSizeBytes);
    }
}
