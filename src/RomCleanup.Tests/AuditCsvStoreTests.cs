using RomCleanup.Infrastructure.Audit;
using Xunit;

namespace RomCleanup.Tests;

public class AuditCsvStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _keyPath;
    private readonly AuditCsvStore _audit;

    public AuditCsvStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RomCleanup_AuditTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _keyPath = Path.Combine(_tempDir, "audit-signing.key");
        _audit = new AuditCsvStore(keyFilePath: _keyPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void WriteMetadataSidecar_CreatesJsonFile()
    {
        var csvPath = Path.Combine(_tempDir, "audit.csv");
        File.WriteAllLines(csvPath, [
            "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp",
            $"{_tempDir},{Path.Combine(_tempDir, "old.rom")},{Path.Combine(_tempDir, "new.rom")},Move,GAME,,,2025-01-01T00:00:00Z"
        ]);

        var metadata = new Dictionary<string, object>
        {
            ["RunId"] = "abc-123",
            ["Mode"] = "DryRun",
            ["Timestamp"] = "2025-01-01T00:00:00Z"
        };

        _audit.WriteMetadataSidecar(csvPath, metadata);

        var sidecar = csvPath + ".meta.json";
        Assert.True(File.Exists(sidecar));
        var content = File.ReadAllText(sidecar);
        Assert.Contains("abc-123", content);
        Assert.Contains("DryRun", content);
        Assert.Contains("CsvSha256", content);
    }

    [Fact]
    public void TestMetadataSidecar_ReturnsTrueIfSidecarIsValid()
    {
        var csvPath = Path.Combine(_tempDir, "audit.csv");
        File.WriteAllLines(csvPath, [
            "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp",
            $"{_tempDir},{Path.Combine(_tempDir, "old.rom")},{Path.Combine(_tempDir, "new.rom")},Move,GAME,,,2025-01-01T00:00:00Z"
        ]);
        _audit.WriteMetadataSidecar(csvPath, new Dictionary<string, object>());

        Assert.True(_audit.TestMetadataSidecar(csvPath));
    }

    [Fact]
    public void TestMetadataSidecar_ReturnsFalseIfTampered()
    {
        var csvPath = Path.Combine(_tempDir, "audit.csv");
        File.WriteAllLines(csvPath, [
            "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp",
            $"{_tempDir},{Path.Combine(_tempDir, "old.rom")},{Path.Combine(_tempDir, "new.rom")},Move,GAME,,,2025-01-01T00:00:00Z"
        ]);
        _audit.WriteMetadataSidecar(csvPath, new Dictionary<string, object>());

        File.AppendAllText(csvPath, "tampered\n");

        Assert.False(_audit.TestMetadataSidecar(csvPath));
    }

    [Fact]
    public void TestMetadataSidecar_ReturnsFalseIfMissing()
    {
        var csvPath = Path.Combine(_tempDir, "nope.csv");
        Assert.False(_audit.TestMetadataSidecar(csvPath));
    }

    [Fact]
    public void Rollback_DryRun_DoesNotMoveFiles()
    {
        var csvPath = Path.Combine(_tempDir, "audit.csv");
        var oldPath = Path.Combine(_tempDir, "original", "game.rom");
        var newPath = Path.Combine(_tempDir, "moved", "game.rom");

        // Create the moved file
        Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
        File.WriteAllText(newPath, "data");

        // Write CSV with header + move entry
        File.WriteAllLines(csvPath, new[]
        {
            "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp",
            $"{_tempDir},{oldPath},{newPath},Move,GAME,abc,dedupe,2025-01-01"
        });

        var result = _audit.Rollback(csvPath,
            allowedRestoreRoots: new[] { _tempDir },
            allowedCurrentRoots: new[] { _tempDir },
            dryRun: true);

        Assert.Single(result);
        Assert.Equal(oldPath, result[0]);
        // File should NOT have been moved in dry run
        Assert.True(File.Exists(newPath));
        Assert.False(File.Exists(oldPath));
    }

    [Fact]
    public void Rollback_DryRun_MissingCurrentFile_DoesNotReportRestorablePath()
    {
        var csvPath = Path.Combine(_tempDir, "audit.csv");
        var oldPath = Path.Combine(_tempDir, "original", "game.rom");
        var newPath = Path.Combine(_tempDir, "moved", "game.rom");

        // Create then remove the current file to simulate stale audit entries.
        Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
        File.WriteAllText(newPath, "data");
        File.Delete(newPath);

        File.WriteAllLines(csvPath, new[]
        {
            "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp",
            $"{_tempDir},{oldPath},{newPath},Move,GAME,abc,dedupe,2025-01-01"
        });

        var result = _audit.Rollback(csvPath,
            allowedRestoreRoots: new[] { _tempDir },
            allowedCurrentRoots: new[] { _tempDir },
            dryRun: true);

        Assert.Empty(result);
    }

    [Fact]
    public void Rollback_ActualMove_RestoresFile()
    {
        var csvPath = Path.Combine(_tempDir, "audit.csv");
        var oldDir = Path.Combine(_tempDir, "original");
        var oldPath = Path.Combine(oldDir, "game.rom");
        var newPath = Path.Combine(_tempDir, "moved", "game.rom");

        Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
        File.WriteAllText(newPath, "data");

        File.WriteAllLines(csvPath, new[]
        {
            "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp",
            $"{_tempDir},{oldPath},{newPath},Move,GAME,abc,dedupe,2025-01-01"
        });
        _audit.WriteMetadataSidecar(csvPath, new Dictionary<string, object> { ["Mode"] = "Move" });

        var result = _audit.Rollback(csvPath,
            allowedRestoreRoots: new[] { _tempDir },
            allowedCurrentRoots: new[] { _tempDir },
            dryRun: false);

        Assert.Single(result);
        Assert.True(File.Exists(oldPath));
        Assert.False(File.Exists(newPath));
    }

    [Fact]
    public void Rollback_PathOutsideAllowedRoot_Skipped()
    {
        var csvPath = Path.Combine(_tempDir, "audit.csv");
        File.WriteAllLines(csvPath, new[]
        {
            "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp",
            @"C:\Other,C:\Other\game.rom,C:\Other\moved\game.rom,Move,GAME,abc,dedupe,2025-01-01"
        });

        var result = _audit.Rollback(csvPath,
            allowedRestoreRoots: new[] { _tempDir },
            allowedCurrentRoots: new[] { _tempDir },
            dryRun: true);

        Assert.Empty(result);
    }

    [Fact]
    public void Rollback_NonExistentCsv_ReturnsEmpty()
    {
        var result = _audit.Rollback(
            Path.Combine(_tempDir, "nope.csv"),
            new[] { _tempDir }, new[] { _tempDir });

        Assert.Empty(result);
    }
}
