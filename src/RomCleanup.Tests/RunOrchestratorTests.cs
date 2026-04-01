using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Core.Classification;
using RomCleanup.Infrastructure.Index;
using RomCleanup.Infrastructure.Orchestration;
using System.Text.RegularExpressions;
using Xunit;

namespace RomCleanup.Tests;

public class RunOrchestratorTests : IDisposable
{
    private readonly string _tempDir;

    public RunOrchestratorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RunOrch_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // ── Preflight Tests ───────────────────────────────────────────

    [Fact]
    public void Preflight_NoRoots_ReturnsBlocked()
    {
        var orch = BuildOrchestrator();
        var options = new RunOptions { Roots = Array.Empty<string>() };

        var result = orch.Preflight(options);

        Assert.Equal("blocked", result.Status);
        Assert.Contains("No roots", result.Reason);
    }

    [Fact]
    public void Preflight_RootDoesNotExist_ReturnsBlocked()
    {
        var fs = new FakeFileSystem();
        fs.ExistingPaths.Clear(); // no paths exist
        var orch = BuildOrchestrator(fs: fs);
        var options = new RunOptions { Roots = new[] { @"C:\NonExistent" } };

        var result = orch.Preflight(options);

        Assert.Equal("blocked", result.Status);
        Assert.Contains("does not exist", result.Reason);
    }

    [Fact]
    public void Preflight_ValidRoot_ReturnsOk()
    {
        var orch = BuildOrchestrator();
        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" }
        };

        var result = orch.Preflight(options);

        Assert.Equal("ok", result.Status);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Preflight_DatEnabledNoDatIndex_AddsWarning()
    {
        var orch = BuildOrchestrator(); // no datIndex
        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            EnableDat = true
        };

        var result = orch.Preflight(options);

        Assert.Equal("ok", result.Status);
        Assert.Contains(result.Warnings, w => w.Contains("DatIndex"));
    }

    [Fact]
    public void Preflight_NoExtensions_AddsWarning()
    {
        var orch = BuildOrchestrator();
        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = Array.Empty<string>()
        };

        var result = orch.Preflight(options);

        Assert.Equal("ok", result.Status);
        Assert.Contains(result.Warnings, w => w.Contains("extension"));
    }

    [Fact]
    public void Preflight_AuditPath_WritableDir_Succeeds()
    {
        var auditDir = Path.Combine(_tempDir, "audit");
        Directory.CreateDirectory(auditDir);
        var orch = BuildOrchestrator();
        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            AuditPath = Path.Combine(auditDir, "audit.csv")
        };

        var result = orch.Preflight(options);

        Assert.Equal("ok", result.Status);
    }

    // ── Execute Tests ─────────────────────────────────────────────

    [Fact]
    public void Execute_NoFiles_ReturnsOkWithZeroCounts()
    {
        var fs = new FakeFileSystem();
        fs.ExistingPaths.Add(_tempDir);
        fs.FileLists[_tempDir] = new List<string>();

        var orch = BuildOrchestrator(fs: fs);
        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" }
        };

        var result = orch.Execute(options);

        Assert.Equal("ok", result.Status);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, result.TotalFilesScanned);
    }

    [Fact]
    public void Execute_PreflightFails_ReturnsBlocked()
    {
        var orch = BuildOrchestrator();
        var options = new RunOptions { Roots = Array.Empty<string>() };

        var result = orch.Execute(options);

        Assert.Equal("blocked", result.Status);
        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public void Execute_DryRun_DoesNotMoveFiles()
    {
        // Create real files
        var file1 = CreateFile("Game (USA).zip", 100);
        var file2 = CreateFile("Game (Europe).zip", 100);

        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new FakeAuditStore();
        var orch = new RunOrchestrator(fs, audit);

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "DryRun",
            PreferRegions = new[] { "USA" }
        };

        var result = orch.Execute(options);

        Assert.Equal("ok", result.Status);
        Assert.Equal(2, result.TotalFilesScanned);
        // DryRun should not execute MovePhase
        Assert.Null(result.MoveResult);
        // Files should still exist
        Assert.True(File.Exists(file1));
        Assert.True(File.Exists(file2));
    }

    [Fact]
    public void Execute_MoveMode_MovesLosers()
    {
        var file1 = CreateFile("Game (USA).zip", 100);
        var file2 = CreateFile("Game (Europe).zip", 100);

        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new FakeAuditStore();
        var orch = new RunOrchestrator(fs, audit);

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "Move",
            PreferRegions = new[] { "US" }
        };

        var result = orch.Execute(options);

        Assert.Equal("ok", result.Status);
        Assert.NotNull(result.MoveResult);
        // Two files with the same GameKey "game" → exactly 1 group
        Assert.Equal(1, result.GroupCount);
        Assert.Equal(1, result.WinnerCount);
        Assert.Equal(1, result.LoserCount);
        // The loser must have been moved
        Assert.Equal(1, result.MoveResult!.MoveCount);
        Assert.Equal(0, result.MoveResult.FailCount);
        // Winner should be US (preferred region), Europe is the loser
        Assert.True(File.Exists(file1), "US (winner) should still exist");
    }

    [Fact]
    public void Execute_Cancellation_ThrowsOperationCanceled()
    {
        var file1 = CreateFile("Game (USA).zip", 100);
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new FakeAuditStore();
        var orch = new RunOrchestrator(fs, audit);

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" }
        };

        var result = orch.Execute(options, cts.Token);
        Assert.Equal("cancelled", result.Status);
        Assert.Equal(2, result.ExitCode);
    }

    [Fact]
    public void Execute_ScanBuildsCorrectCandidates()
    {
        // Create files with distinct region tags
        CreateFile("Mario (USA).zip", 50);
        CreateFile("Mario (Japan).zip", 40);
        CreateFile("Zelda (Europe).zip", 60);

        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new FakeAuditStore();
        var orch = new RunOrchestrator(fs, audit);

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "DryRun",
            PreferRegions = new[] { "USA", "Europe" }
        };

        var result = orch.Execute(options);

        Assert.Equal(3, result.TotalFilesScanned);
        // 2 game keys: "mario" and "zelda"
        Assert.Equal(2, result.GroupCount);
    }

    [Fact]
    public void Execute_ProgressCallbackInvoked()
    {
        CreateFile("Game (USA).zip", 50);
        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new FakeAuditStore();
        var messages = new List<string>();

        var orch = new RunOrchestrator(fs, audit, onProgress: msg => messages.Add(msg));

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" }
        };

        orch.Execute(options);

        Assert.Contains(messages, m => m.Contains("Preflight"));
        Assert.Contains(messages, m => m.Contains("[Scan]"));
        Assert.Contains(messages, m => m.Contains("[Dedupe]"));
    }

    [Fact]
    public void Execute_RecordsDuration()
    {
        CreateFile("Test (USA).zip", 10);
        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new FakeAuditStore();
        var orch = new RunOrchestrator(fs, audit);

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" }
        };

        var result = orch.Execute(options);

        Assert.Equal("ok", result.Status);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, result.TotalFilesScanned);
        Assert.True(result.DurationMs > 0, "Duration should be positive for a real execution");
    }

    [Fact]
    public void Execute_WithReportPath_SetsVerifiedReportPath()
    {
        CreateFile("Game (USA).zip", 100);
        CreateFile("Game (Europe).zip", 100);

        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new FakeAuditStore();
        var orch = new RunOrchestrator(fs, audit);
        var reportPath = Path.Combine(_tempDir, "reports", "result.html");

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "DryRun",
            ReportPath = reportPath
        };

        var result = orch.Execute(options);

        Assert.Equal(Path.GetFullPath(reportPath), result.ReportPath);
        Assert.True(File.Exists(reportPath));
    }

    [Fact]
    public void Execute_WithInvalidReportPath_LeavesReportPathNull()
    {
        CreateFile("Game (USA).zip", 100);

        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new FakeAuditStore();
        var orch = new RunOrchestrator(fs, audit);

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "DryRun",
            ReportPath = Path.Combine(_tempDir, "bad\0report.html")
        };

        var result = orch.Execute(options);

        Assert.Null(result.ReportPath);
        Assert.Equal("ok", result.Status);
    }

    [Fact]
    public void Execute_WithUnwritableReportPath_UsesAppDataFallback()
    {
        CreateFile("Game (USA).zip", 100);

        var blocker = Path.Combine(_tempDir, "reports-blocker");
        File.WriteAllText(blocker, "not a directory");
        var primaryReportPath = Path.Combine(blocker, "result.html");

        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new FakeAuditStore();
        var orch = new RunOrchestrator(fs, audit);

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "DryRun",
            ReportPath = primaryReportPath
        };

        var result = orch.Execute(options);

        Assert.False(string.IsNullOrWhiteSpace(result.ReportPath));
        var appDataReports = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RomCleanupRegionDedupe",
            "reports");
        Assert.StartsWith(Path.GetFullPath(appDataReports), Path.GetFullPath(result.ReportPath!), StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(result.ReportPath!));
    }

    // ── Move Phase Tests ──────────────────────────────────────────

    [Fact]
    public void Execute_MoveMode_TrashRoot_UsesCustomTrash()
    {
        var trashDir = Path.Combine(_tempDir, "custom_trash");
        Directory.CreateDirectory(trashDir);

        CreateFile("Game (USA).zip", 100);
        CreateFile("Game (Europe).zip", 100);

        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new FakeAuditStore();
        var orch = new RunOrchestrator(fs, audit);

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "Move",
            PreferRegions = new[] { "USA" },
            TrashRoot = trashDir
        };

        var result = orch.Execute(options);

        if (result.LoserCount > 0)
        {
            // Trash should be under custom trash root
            var trashDedupeDir = Path.Combine(trashDir, "_TRASH_REGION_DEDUPE");
            Assert.True(Directory.Exists(trashDedupeDir),
                "Custom trash directory should be created");
        }
    }

    [Fact]
    public void Execute_JunkFiles_ClassifiedCorrectly()
    {
        CreateFile("Game (USA) (Beta).zip", 50);
        CreateFile("Game (USA).zip", 50);

        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new FakeAuditStore();
        var orch = new RunOrchestrator(fs, audit);

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "DryRun"
        };

        var result = orch.Execute(options);

        Assert.Equal("ok", result.Status);
        Assert.Equal(2, result.TotalFilesScanned);
        // Both files share GameKey "game" → 1 group, 1 winner, 1 loser
        Assert.Equal(1, result.GroupCount);
        Assert.Equal(1, result.WinnerCount);
        Assert.Equal(1, result.LoserCount);
        // The Beta file should be classified as JUNK
        var betaCandidate = result.AllCandidates.FirstOrDefault(c => c.MainPath.Contains("Beta"));
        Assert.NotNull(betaCandidate);
        Assert.Equal(FileCategory.Junk, betaCandidate!.Category);
        // The non-Beta file should be classified as GAME
        var normalCandidate = result.AllCandidates.FirstOrDefault(c => !c.MainPath.Contains("Beta"));
        Assert.NotNull(normalCandidate);
        Assert.Equal(FileCategory.Game, normalCandidate!.Category);
    }

    [Fact]
    public void Execute_OnlyGames_FiltersNonGameCandidates()
    {
        CreateFile("Super Mario (USA).zip", 80);
        CreateFile("Workbench (System Disk).zip", 40);

        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new FakeAuditStore();
        var orch = new RunOrchestrator(fs, audit);

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "DryRun",
            OnlyGames = true,
            KeepUnknownWhenOnlyGames = true
        };

        var result = orch.Execute(options);

        Assert.Equal("ok", result.Status);
        Assert.Equal(2, result.TotalFilesScanned);
        Assert.Equal(1, result.FilteredNonGameCount);
        Assert.Single(result.DedupeGroups);
        Assert.Equal("supermario", result.DedupeGroups[0].GameKey);
    }

    [Fact]
    public void Execute_OnlyGames_DropUnknown_RemovesUnknownFromProcessing()
    {
        CreateFile(".zip", 10);
        CreateFile("Valid Game (USA).zip", 50);

        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new FakeAuditStore();
        var orch = new RunOrchestrator(fs, audit);

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "DryRun",
            OnlyGames = true,
            KeepUnknownWhenOnlyGames = false
        };

        var result = orch.Execute(options);

        Assert.Equal("ok", result.Status);
        Assert.Equal(2, result.TotalFilesScanned);
        Assert.Equal(1, result.UnknownCount);
        Assert.Equal(1, result.FilteredNonGameCount);
        Assert.True(result.UnknownReasonCounts.ContainsKey("empty-basename"));
        Assert.Single(result.DedupeGroups);
        Assert.Equal("validgame", result.DedupeGroups[0].GameKey);
    }

    [Fact]
    public async Task Execute_ReusesPersistedCandidates_WhenFingerprintMatchesAndFileIsUnchanged()
    {
        var romPath = CreateFile("Game.nes", 64);
        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();
        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".nes" },
            Mode = "DryRun"
        };
        var dbPath = Path.Combine(_tempDir, "collection.db");

        using (var firstIndex = new LiteDbCollectionIndex(dbPath))
        using (var firstOrchestrator = new RunOrchestrator(
                   fs,
                   new FakeAuditStore(),
                   consoleDetector: BuildConsoleDetector("NES", ".nes"),
                   collectionIndex: firstIndex,
                   enrichmentFingerprint: "fp-a"))
        {
            var firstResult = firstOrchestrator.Execute(options);
            Assert.Equal("NES", firstResult.AllCandidates.Single().ConsoleKey);
        }

        using var secondIndex = new LiteDbCollectionIndex(dbPath);
        using var secondOrchestrator = new RunOrchestrator(
            fs,
            new FakeAuditStore(),
            collectionIndex: secondIndex,
            enrichmentFingerprint: "fp-a");

        var secondResult = secondOrchestrator.Execute(options);

        Assert.Equal("NES", secondResult.AllCandidates.Single().ConsoleKey);

        var persisted = await secondIndex.TryGetByPathAsync(romPath);
        Assert.NotNull(persisted);
        Assert.Equal("fp-a", persisted!.EnrichmentFingerprint);
        Assert.Equal("NES", persisted.ConsoleKey);
    }

    [Fact]
    public async Task Execute_DoesNotReusePersistedCandidates_WhenFingerprintOrFileStateChanged()
    {
        var romPath = CreateFile("Game.nes", 64);
        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();
        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".nes" },
            Mode = "DryRun"
        };
        var dbPath = Path.Combine(_tempDir, "collection.db");

        using (var firstIndex = new LiteDbCollectionIndex(dbPath))
        using (var firstOrchestrator = new RunOrchestrator(
                   fs,
                   new FakeAuditStore(),
                   consoleDetector: BuildConsoleDetector("NES", ".nes"),
                   collectionIndex: firstIndex,
                   enrichmentFingerprint: "fp-a"))
        {
            var firstResult = firstOrchestrator.Execute(options);
            Assert.Equal("NES", firstResult.AllCandidates.Single().ConsoleKey);
        }

        File.AppendAllText(romPath, "changed");
        File.SetLastWriteTimeUtc(romPath, DateTime.UtcNow.AddMinutes(1));

        using var secondIndex = new LiteDbCollectionIndex(dbPath);
        using var secondOrchestrator = new RunOrchestrator(
            fs,
            new FakeAuditStore(),
            collectionIndex: secondIndex,
            enrichmentFingerprint: "fp-b");

        var secondResult = secondOrchestrator.Execute(options);
        var candidate = secondResult.AllCandidates.Single();
        var persisted = await secondIndex.TryGetByPathAsync(romPath);

        Assert.Equal("UNKNOWN", candidate.ConsoleKey);
        Assert.NotNull(persisted);
        Assert.Equal("fp-b", persisted!.EnrichmentFingerprint);
        Assert.Equal("UNKNOWN", persisted.ConsoleKey);
        Assert.Equal(new FileInfo(romPath).Length, persisted.SizeBytes);
    }

    [Fact]
    public async Task Execute_RemovesStaleCollectionIndexEntries_OnlyWithinCurrentScanScope()
    {
        var currentPath = CreateFile("Game.nes", 64);
        var otherRoot = Path.Combine(Path.GetTempPath(), "RunOrchOther_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(otherRoot);
        var staleInScopePath = Path.Combine(_tempDir, "Old.nes");
        var staleOtherExtensionPath = Path.Combine(_tempDir, "Keep.zip");
        var staleOtherRootPath = Path.Combine(otherRoot, "Other.nes");
        var dbPath = Path.Combine(_tempDir, "collection.db");
        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();
        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".nes" },
            Mode = "DryRun"
        };

        try
        {
            using (var index = new LiteDbCollectionIndex(dbPath))
            {
                await index.UpsertEntriesAsync(
                [
                    new CollectionIndexEntry { Path = staleInScopePath, Root = _tempDir, FileName = "Old.nes", Extension = ".nes" },
                    new CollectionIndexEntry { Path = staleOtherExtensionPath, Root = _tempDir, FileName = "Keep.zip", Extension = ".zip" },
                    new CollectionIndexEntry { Path = staleOtherRootPath, Root = otherRoot, FileName = "Other.nes", Extension = ".nes" }
                ]);
            }

            using var cleanupIndex = new LiteDbCollectionIndex(dbPath);
            using var orchestrator = new RunOrchestrator(
                fs,
                new FakeAuditStore(),
                consoleDetector: BuildConsoleDetector("NES", ".nes"),
                collectionIndex: cleanupIndex,
                enrichmentFingerprint: "fp-a");

            var result = orchestrator.Execute(options);

            Assert.Equal("ok", result.Status);
            Assert.NotNull(await cleanupIndex.TryGetByPathAsync(currentPath));
            Assert.Null(await cleanupIndex.TryGetByPathAsync(staleInScopePath));
            Assert.NotNull(await cleanupIndex.TryGetByPathAsync(staleOtherExtensionPath));
            Assert.NotNull(await cleanupIndex.TryGetByPathAsync(staleOtherRootPath));
        }
        finally
        {
            if (Directory.Exists(otherRoot))
                Directory.Delete(otherRoot, true);
        }
    }

    [Fact]
    public async Task Execute_CancelledRun_DoesNotRemoveStaleCollectionIndexEntries()
    {
        var dbPath = Path.Combine(_tempDir, "collection.db");
        var stalePath = Path.Combine(_tempDir, "Old.nes");
        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();
        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".nes" },
            Mode = "DryRun"
        };

        using (var seedIndex = new LiteDbCollectionIndex(dbPath))
        {
            await seedIndex.UpsertEntriesAsync(
            [
                new CollectionIndexEntry { Path = stalePath, Root = _tempDir, FileName = "Old.nes", Extension = ".nes" }
            ]);
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        using var index = new LiteDbCollectionIndex(dbPath);
        using var orchestrator = new RunOrchestrator(
            fs,
            new FakeAuditStore(),
            consoleDetector: BuildConsoleDetector("NES", ".nes"),
            collectionIndex: index,
            enrichmentFingerprint: "fp-a");

        var result = orchestrator.Execute(options, cts.Token);

        Assert.Equal("cancelled", result.Status);
        Assert.NotNull(await index.TryGetByPathAsync(stalePath));
    }

    // ── Helpers ───────────────────────────────────────────────────

    private string CreateFile(string name, int sizeBytes)
    {
        var path = Path.Combine(_tempDir, name);
        var data = new byte[sizeBytes];
        new Random(42).NextBytes(data);
        File.WriteAllBytes(path, data);
        return path;
    }

    private RunOrchestrator BuildOrchestrator(FakeFileSystem? fs = null)
    {
        fs ??= new FakeFileSystem();
        fs.ExistingPaths.Add(_tempDir);
        return new RunOrchestrator(fs, new FakeAuditStore());
    }

    private static ConsoleDetector BuildConsoleDetector(string consoleKey, params string[] extensions)
        => new([
            new ConsoleInfo(
                consoleKey,
                consoleKey,
                false,
                extensions,
                Array.Empty<string>(),
                [consoleKey])
        ]);

    // ── Fakes ─────────────────────────────────────────────────────

    private sealed class FakeFileSystem : IFileSystem
    {
        public HashSet<string> ExistingPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<string>> FileLists { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<(string src, string dst)> MoveLog { get; } = new();

        public bool TestPath(string literalPath, string pathType = "Any")
            => ExistingPaths.Contains(literalPath);

        public string EnsureDirectory(string path)
        {
            ExistingPaths.Add(path);
            return path;
        }

        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? allowedExtensions = null)
        {
            if (FileLists.TryGetValue(root, out var list))
                return list;
            return Array.Empty<string>();
        }

        public string? MoveItemSafely(string sourcePath, string destinationPath)
        {
            MoveLog.Add((sourcePath, destinationPath));
            return destinationPath;
        }

        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath)
        {
            var full = Path.Combine(rootPath, relativePath);
            return full.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase) ? full : null;
        }
        public bool IsReparsePoint(string path) => false;
        public void DeleteFile(string path) { }
        public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false) { }
    }

    private sealed class FakeAuditStore : IAuditStore
    {
        public List<(string path, IDictionary<string, object> meta)> SidecarLog { get; } = new();
        public List<(string csvPath, string rootPath, string oldPath, string newPath, string action)> AuditRows { get; } = new();

        public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata)
            => SidecarLog.Add((auditCsvPath, metadata));

        public bool TestMetadataSidecar(string auditCsvPath)
            => SidecarLog.Exists(s => s.path == auditCsvPath);

        public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots,
            string[] allowedCurrentRoots, bool dryRun = false)
            => Array.Empty<string>();

        public void AppendAuditRow(string auditCsvPath, string rootPath, string oldPath,
            string newPath, string action, string category = "", string hash = "", string reason = "")
            => AuditRows.Add((auditCsvPath, rootPath, oldPath, newPath, action));
        public void Flush(string auditCsvPath) { }    }

    private sealed class FakeFormatConverter : IFormatConverter
    {
        public List<string> ConvertedPaths { get; } = new();

        public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension)
        {
            // Return a target for .zip files to simulate conversion being applicable
            if (sourceExtension == ".zip")
                return new ConversionTarget(".chd", "chdman", "createcd");
            return null;
        }

        public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken cancellationToken = default)
        {
            ConvertedPaths.Add(sourcePath);
            var targetPath = sourcePath + ".chd";
            File.WriteAllText(targetPath, "converted");
            return new ConversionResult(sourcePath, targetPath, ConversionOutcome.Success);
        }

        public bool Verify(string targetPath, ConversionTarget target) => true;
    }

    private sealed class VerifyOutcomeConverter : IFormatConverter
    {
        private readonly bool _verificationOk;

        public VerifyOutcomeConverter(bool verificationOk)
        {
            _verificationOk = verificationOk;
        }

        public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension)
            => sourceExtension == ".zip" ? new ConversionTarget(".chd", "chdman", "createcd") : null;

        public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken cancellationToken = default)
        {
            var targetPath = sourcePath + target.Extension;
            File.WriteAllText(targetPath, _verificationOk ? "verified" : "verify-failed");
            return new ConversionResult(sourcePath, targetPath, ConversionOutcome.Success)
            {
                VerificationResult = _verificationOk ? VerificationStatus.Verified : VerificationStatus.VerifyFailed,
                SourceIntegrity = SourceIntegrity.Lossless,
                Safety = ConversionSafety.Safe
            };
        }

        public bool Verify(string targetPath, ConversionTarget target)
            => _verificationOk;
    }

    [Fact]
    public void Execute_Phase6_SkipsJunkRemovedGroups()
    {
        // "Junkonly (Beta).zip" is JUNK and the sole member of its GameKey → removed in Phase 3b
        // "Normal (USA).zip" is GAME → should be converted in Phase 6
        CreateFile("Junkonly (Beta).zip", 50);
        CreateFile("Normal (USA).zip", 60);

        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new FakeAuditStore();
        var converter = new FakeFormatConverter();
        var orch = new RunOrchestrator(fs, audit, converter: converter);

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "Move",
            RemoveJunk = true,
            ConvertFormat = "chd",
            PreferRegions = new[] { "USA" }
        };

        var result = orch.Execute(options);

        Assert.Equal("ok", result.Status);
        // Junk file should have been removed
        Assert.True(result.JunkRemovedCount >= 1, "Junk file should be removed in Phase 3b");
        // Converter should have been called for the normal file only
        Assert.Single(converter.ConvertedPaths);
        Assert.Contains("Normal (USA).zip", converter.ConvertedPaths[0]);
        // Junk file must NOT appear in conversion list
        Assert.DoesNotContain(converter.ConvertedPaths, p => p.Contains("Junkonly"));
    }

    [Fact]
    public void VerifyFailed_TriggersCompletedWithErrorsAndHealthPenalty()
    {
        CreateFile("Verify (USA).zip", 80);

        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();
        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "Move",
            ConvertFormat = "chd",
            PreferRegions = new[] { "USA" }
        };

        var okOrchestrator = new RunOrchestrator(fs, new FakeAuditStore(), converter: new VerifyOutcomeConverter(verificationOk: true));
        var failOrchestrator = new RunOrchestrator(fs, new FakeAuditStore(), converter: new VerifyOutcomeConverter(verificationOk: false));

        var okResult = okOrchestrator.Execute(options);
        var failResult = failOrchestrator.Execute(options);

        var okProjection = RunProjectionFactory.Create(okResult);
        var failProjection = RunProjectionFactory.Create(failResult);

        Assert.Equal("ok", okResult.Status);
        Assert.Equal("completed_with_errors", failResult.Status);
        Assert.True(failResult.ConvertVerifyFailedCount > 0);
        Assert.True(failProjection.FailCount > okProjection.FailCount);
        Assert.True(failProjection.HealthScore < okProjection.HealthScore);
    }

    [Fact]
    public void AuditSidecarWriteFailure_MarksRunAsError()
    {
        CreateFile("Game (USA).zip", 100);
        CreateFile("Game (Europe).zip", 100);

        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new ThrowingSidecarAuditStore();
        var orch = new RunOrchestrator(fs, audit);

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "Move",
            PreferRegions = new[] { "US" },
            AuditPath = Path.Combine(_tempDir, "audit.csv")
        };

        var result = orch.Execute(options);

        Assert.NotEqual("ok", result.Status);
        Assert.NotEqual(0, result.ExitCode);
    }

    private sealed class ThrowingSidecarAuditStore : IAuditStore
    {
        public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata)
            => throw new IOException("sidecar write failed");

        public bool TestMetadataSidecar(string auditCsvPath) => false;

        public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots,
            string[] allowedCurrentRoots, bool dryRun = false)
            => Array.Empty<string>();

        public void AppendAuditRow(string auditCsvPath, string rootPath, string oldPath,
            string newPath, string action, string category = "", string hash = "", string reason = "")
        {
        }

        public void Flush(string auditCsvPath)
        {
        }
    }
}
