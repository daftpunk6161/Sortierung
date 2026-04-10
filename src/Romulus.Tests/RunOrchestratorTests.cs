using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Globalization;
using System.Text.RegularExpressions;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Classification;
using Romulus.Infrastructure.Index;
using Romulus.Infrastructure.Metrics;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

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
        var auditDir = Path.Combine(Path.GetTempPath(), "RunOrchAudit", Guid.NewGuid().ToString("N"));
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

    [Fact]
    public void Preflight_ProtectedTrashRoot_ReturnsBlocked()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var protectedPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(protectedPath))
            return;

        var orch = BuildOrchestrator();
        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            TrashRoot = protectedPath
        };

        var result = orch.Preflight(options);

        Assert.Equal("blocked", result.Status);
        Assert.Contains("trashRoot", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Preflight_ConvertFormatWithMissingTools_ReturnsBlocked()
    {
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new FakeAuditStore();
        var converter = new MissingToolsConverter(["chdman"]);
        var orch = new RunOrchestrator(fs, audit, converter: converter);

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "DryRun",
            ConvertFormat = "chd"
        };

        var result = orch.Preflight(options);

        Assert.Equal("blocked", result.Status);
        Assert.Contains("chdman", result.Reason, StringComparison.OrdinalIgnoreCase);
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

        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
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

        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
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
    public void Execute_AlreadyCancelledToken_ReturnsCancelledResult()
    {
        var file1 = CreateFile("Game (USA).zip", 100);
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
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
    public void Execute_CancellationDuringRun_ReturnsCancelledResult()
    {
        CreateFile("Game (USA).zip", 100);
        CreateFile("Game (Europe).zip", 100);

        var cts = new CancellationTokenSource();
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new FakeAuditStore();
        var orch = new RunOrchestrator(fs, audit, onProgress: message =>
        {
            if (message.Contains("[Scan]", StringComparison.OrdinalIgnoreCase))
                cts.Cancel();
        });

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "DryRun"
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

        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
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
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
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
    public void Execute_ProgressCallback_UsesEnglishPreflightMessage_WhenCurrentUiCultureIsEnglish_FindingF33()
    {
        CreateFile("Game (USA).zip", 50);
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new FakeAuditStore();
        var messages = new List<string>();

        var previousUiCulture = CultureInfo.CurrentUICulture;
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en-US");
            CultureInfo.CurrentCulture = new CultureInfo("en-US");

            var orch = new RunOrchestrator(fs, audit, onProgress: msg => messages.Add(msg));
            var options = new RunOptions
            {
                Roots = new[] { _tempDir },
                Extensions = new[] { ".zip" }
            };

            orch.Execute(options);

            Assert.Contains(messages,
                message => message.StartsWith("[Preflight] Checking prerequisites", StringComparison.Ordinal));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousUiCulture;
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void Execute_RecordsDuration()
    {
        CreateFile("Test (USA).zip", 10);
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
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

        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new FakeAuditStore();
        var orch = new RunOrchestrator(fs, audit);
        var reportPath = Path.Combine(Path.GetTempPath(), "RunOrchReports", Guid.NewGuid().ToString("N"), "result.html");

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
    public void Execute_WithInvalidReportPath_BlocksInPreflight()
    {
        CreateFile("Game (USA).zip", 100);

        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
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
        Assert.Equal("blocked", result.Status);
        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public void Execute_WithUnwritableReportPath_UsesAppDataFallback()
    {
        CreateFile("Game (USA).zip", 100);

        var blocker = Path.Combine(Path.GetTempPath(), "RunOrchReportsBlocker", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(blocker)!);
        File.WriteAllText(blocker, "not a directory");
        var primaryReportPath = Path.Combine(blocker, "result.html");

        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
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
            "Romulus",
            "reports");
        Assert.StartsWith(Path.GetFullPath(appDataReports), Path.GetFullPath(result.ReportPath!), StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(result.ReportPath!));
    }

    [Fact]
    public void Execute_ConvertOnly_OnlyGames_WithReportPath_WritesReport()
    {
        CreateFile("Game (USA).zip", 100);
        CreateFile("Disk Utility (Tool).zip", 30);

        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new FakeAuditStore();
        var orch = new RunOrchestrator(fs, audit, converter: new FakeFormatConverter());
        var reportPath = Path.Combine(Path.GetTempPath(), "RunOrchReports", Guid.NewGuid().ToString("N"), "convert-only.html");

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = RunConstants.ModeMove,
            ConvertOnly = true,
            ConvertFormat = "chd",
            OnlyGames = true,
            KeepUnknownWhenOnlyGames = false,
            ReportPath = reportPath
        };

        var result = orch.Execute(options);

        Assert.Equal("ok", result.Status);
        Assert.True(result.FilteredNonGameCount > 0);
        Assert.Equal(Path.GetFullPath(reportPath), result.ReportPath);
        Assert.True(File.Exists(reportPath));
    }

    // ── Move Phase Tests ──────────────────────────────────────────

    [Fact]
    public void Execute_MoveMode_TrashRoot_UsesCustomTrash()
    {
        var trashDir = Path.Combine(Path.GetTempPath(), "RunOrchTrash", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(trashDir);

        CreateFile("Game (USA).zip", 100);
        CreateFile("Game (Europe).zip", 100);

        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
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
    public void Execute_MoveMode_WritesRollbackRootMetadata_ToAuditSidecar()
    {
        var trashDir = Path.Combine(Path.GetTempPath(), "RunOrchTrash", Guid.NewGuid().ToString("N"));
        var auditDir = Path.Combine(Path.GetTempPath(), "RunOrchAudit", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(trashDir);
        Directory.CreateDirectory(auditDir);

        CreateFile("Game (USA).zip", 100);
        CreateFile("Game (Europe).zip", 100);

        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new FakeAuditStore();
        var orch = new RunOrchestrator(fs, audit);

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "Move",
            PreferRegions = new[] { "USA" },
            TrashRoot = trashDir,
            AuditPath = Path.Combine(auditDir, "audit.csv")
        };

        File.WriteAllText(options.AuditPath, "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp\n");

        var result = orch.Execute(options);

        var latestSidecar = Assert.Single(
            audit.SidecarLog,
            entry => entry.path == options.AuditPath && entry.meta.ContainsKey("FinalCheckpoint")).meta;
        var restoreRoots = Assert.IsType<string[]>(latestSidecar["AllowedRestoreRoots"]);
        var currentRoots = Assert.IsType<string[]>(latestSidecar["AllowedCurrentRoots"]);

        Assert.Equal("ok", result.Status);
        Assert.Equal(new[] { Path.GetFullPath(_tempDir) }, restoreRoots);
        Assert.Equal(
            new[] { Path.GetFullPath(_tempDir), Path.GetFullPath(trashDir) }.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
            currentRoots.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    [Fact]
    public void Execute_JunkFiles_ClassifiedCorrectly()
    {
        CreateFile("Game (USA) (Beta).zip", 50);
        CreateFile("Game (USA).zip", 50);

        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
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

        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
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

        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
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
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
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
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
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
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
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
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
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

    [Fact]
    public void Execute_ConvertOnly_Success_WritesVerifiedAuditSidecar()
    {
        CreateFile("Convert Me.zip", 128);
        var auditPath = Path.Combine(Path.GetTempPath(), "RunOrchAudit", Guid.NewGuid().ToString("N"), "convert-only-audit.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(auditPath)!);
        var audit = new Romulus.Infrastructure.Audit.AuditCsvStore();
        var orchestrator = new RunOrchestrator(
            new Romulus.Infrastructure.FileSystem.FileSystemAdapter(),
            audit,
            converter: new FakeFormatConverter());

        var result = orchestrator.Execute(new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = RunConstants.ModeMove,
            ConvertOnly = true,
            ConvertFormat = "chd",
            AuditPath = auditPath
        });

        Assert.Equal("ok", result.Status);
        Assert.True(File.Exists(auditPath));
        Assert.True(audit.TestMetadataSidecar(auditPath));
        Assert.True(File.Exists(auditPath + ".meta.json"));
    }

    [Fact]
    public void RunDatRenameStep_RebasesLoserPathsBeforeMove()
    {
        var winnerPath = CreateFile("winner.zip", 64);
        var loserPath = CreateFile("loser.zip", 48);
        var renamedLoserPath = Path.Combine(_tempDir, "renamed-loser.zip");
        var winner = CreateCandidate(winnerPath, "game", "PS1");
        var loser = CreateCandidate(loserPath, "game", "PS1");
        var state = new PipelineState();
        state.SetScanOutput([winner, loser], [winner, loser]);
        state.SetDedupeOutput(
        [
            new DedupeGroup
            {
                GameKey = "game",
                Winner = winner,
                Losers = [loser]
            }
        ],
        [
            new DedupeGroup
            {
                GameKey = "game",
                Winner = winner,
                Losers = [loser]
            }
        ]);
        state.SetDatAuditOutput(new DatAuditResult(
            Entries:
            [
                new DatAuditEntry(
                    loserPath,
                    "hash-loser",
                    DatAuditStatus.HaveWrongName,
                    "Game",
                    "renamed-loser.zip",
                    "PS1",
                    100)
            ],
            HaveCount: 0,
            HaveWrongNameCount: 1,
            MissCount: 0,
            UnknownCount: 0,
            AmbiguousCount: 0));

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = RunConstants.ModeMove,
            EnableDatRename = true
        };
        var orchestrator = new RunOrchestrator(
            new Romulus.Infrastructure.FileSystem.FileSystemAdapter(),
            new FakeAuditStore());
        var builder = new RunResultBuilder();
        var metrics = CreateMetricsCollector();

        InvokePhaseStep(orchestrator, "RunDatRenameStep", state, options, builder, metrics);

        Assert.Equal(renamedLoserPath, state.GameGroups!.Single().Losers.Single().MainPath);
        Assert.True(File.Exists(renamedLoserPath));
        Assert.False(File.Exists(loserPath));

        InvokePhaseStep(orchestrator, "RunMoveStep", state, options, builder, metrics);

        Assert.NotNull(builder.MoveResult);
        Assert.Equal(1, builder.MoveResult!.MoveCount);
        Assert.Equal(0, builder.MoveResult.FailCount);
        Assert.Contains(renamedLoserPath, builder.MoveResult.MovedSourcePaths!);
        Assert.True(File.Exists(Path.Combine(_tempDir, RunConstants.WellKnownFolders.TrashRegionDedupe, "renamed-loser.zip")));
        Assert.False(File.Exists(renamedLoserPath));
    }

    [Fact]
    public void RunDatRenameStep_RebasesCandidatePathsBeforeConsoleSort()
    {
        var originalPath = CreateFile("game.zip", 96);
        var renamedPath = Path.Combine(_tempDir, "renamed.zip");
        var sortedPath = Path.Combine(_tempDir, "PS1", "renamed.zip");
        var candidate = CreateCandidate(originalPath, "game", "PS1");
        var state = new PipelineState();
        state.SetScanOutput([candidate], [candidate]);
        state.SetDatAuditOutput(new DatAuditResult(
            Entries:
            [
                new DatAuditEntry(
                    originalPath,
                    "hash-a",
                    DatAuditStatus.HaveWrongName,
                    "Game",
                    "renamed.zip",
                    "PS1",
                    100)
            ],
            HaveCount: 0,
            HaveWrongNameCount: 1,
            MissCount: 0,
            UnknownCount: 0,
            AmbiguousCount: 0));

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = RunConstants.ModeMove,
            EnableDatRename = true,
            SortConsole = true
        };
        var orchestrator = new RunOrchestrator(
            new Romulus.Infrastructure.FileSystem.FileSystemAdapter(),
            new FakeAuditStore(),
            consoleDetector: BuildConsoleDetector("PS1", ".zip"));
        var builder = new RunResultBuilder();
        var metrics = CreateMetricsCollector();

        InvokePhaseStep(orchestrator, "RunDatRenameStep", state, options, builder, metrics);
        InvokePhaseStep(orchestrator, "RunConsoleSortStep", state, options, builder, metrics);

        Assert.Equal(sortedPath, state.AllCandidates!.Single().MainPath);
        Assert.NotNull(builder.ConsoleSortResult);
        Assert.Contains(builder.ConsoleSortResult!.PathMutations!, m =>
            string.Equals(m.SourcePath, renamedPath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(m.TargetPath, sortedPath, StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(sortedPath));
        Assert.False(File.Exists(originalPath));
        Assert.False(File.Exists(renamedPath));
    }

    [Fact]
    public void RunDatRenameStep_RebasesWinnerPathsBeforeWinnerConversion()
    {
        var winnerPath = CreateFile("winner.zip", 112);
        var renamedWinnerPath = Path.Combine(_tempDir, "renamed-winner.zip");
        var winner = CreateCandidate(winnerPath, "game", "PS1");
        var state = new PipelineState();
        state.SetScanOutput([winner], [winner]);
        state.SetDedupeOutput(
        [
            new DedupeGroup
            {
                GameKey = "game",
                Winner = winner,
                Losers = Array.Empty<RomCandidate>()
            }
        ],
        [
            new DedupeGroup
            {
                GameKey = "game",
                Winner = winner,
                Losers = Array.Empty<RomCandidate>()
            }
        ]);
        state.SetDatAuditOutput(new DatAuditResult(
            Entries:
            [
                new DatAuditEntry(
                    winnerPath,
                    "hash-w",
                    DatAuditStatus.HaveWrongName,
                    "Game",
                    "renamed-winner.zip",
                    "PS1",
                    100)
            ],
            HaveCount: 0,
            HaveWrongNameCount: 1,
            MissCount: 0,
            UnknownCount: 0,
            AmbiguousCount: 0));

        var converter = new FakeFormatConverter();
        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = RunConstants.ModeMove,
            EnableDatRename = true,
            ConvertFormat = "chd"
        };
        var orchestrator = new RunOrchestrator(
            new Romulus.Infrastructure.FileSystem.FileSystemAdapter(),
            new FakeAuditStore(),
            converter: converter);
        var builder = new RunResultBuilder();
        var metrics = CreateMetricsCollector();

        InvokePhaseStep(orchestrator, "RunDatRenameStep", state, options, builder, metrics);
        InvokePhaseStep(orchestrator, "RunWinnerConversionStep", state, options, builder, metrics);

        Assert.Single(converter.ConvertedPaths);
        Assert.Equal(renamedWinnerPath, converter.ConvertedPaths.Single());
        Assert.True(File.Exists(renamedWinnerPath + ".chd"));
    }

    [Fact]
    public void RunConsoleSortStep_RebasesWinnerPathsBeforeWinnerConversion()
    {
        var winnerPath = CreateFile("winner.zip", 112);
        var sortedWinnerPath = Path.Combine(_tempDir, "PS1", "winner.zip");
        var winner = CreateCandidate(winnerPath, "game", "PS1");
        var state = new PipelineState();
        state.SetScanOutput([winner], [winner]);
        state.SetDedupeOutput(
        [
            new DedupeGroup
            {
                GameKey = "game",
                Winner = winner,
                Losers = Array.Empty<RomCandidate>()
            }
        ],
        [
            new DedupeGroup
            {
                GameKey = "game",
                Winner = winner,
                Losers = Array.Empty<RomCandidate>()
            }
        ]);

        var converter = new FakeFormatConverter();
        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = RunConstants.ModeMove,
            SortConsole = true,
            ConvertFormat = "chd"
        };
        var orchestrator = new RunOrchestrator(
            new Romulus.Infrastructure.FileSystem.FileSystemAdapter(),
            new FakeAuditStore(),
            consoleDetector: BuildConsoleDetector("PS1", ".zip"),
            converter: converter);
        var builder = new RunResultBuilder();
        var metrics = CreateMetricsCollector();

        InvokePhaseStep(orchestrator, "RunConsoleSortStep", state, options, builder, metrics);
        InvokePhaseStep(orchestrator, "RunWinnerConversionStep", state, options, builder, metrics);

        Assert.Single(converter.ConvertedPaths);
        Assert.Equal(sortedWinnerPath, converter.ConvertedPaths.Single());
        Assert.True(File.Exists(sortedWinnerPath + ".chd"));
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

    private static PhaseStepResult InvokePhaseStep(
        RunOrchestrator orchestrator,
        string methodName,
        PipelineState state,
        RunOptions options,
        RunResultBuilder builder,
        PhaseMetricsCollector metrics)
    {
        var method = typeof(RunOrchestrator).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Phase step method not found: {methodName}");

        try
        {
            return (PhaseStepResult)(method.Invoke(orchestrator, [state, options, builder, metrics, CancellationToken.None])
                ?? throw new InvalidOperationException($"Phase step returned null: {methodName}"));
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static PhaseMetricsCollector CreateMetricsCollector()
    {
        var metrics = new PhaseMetricsCollector();
        metrics.Initialize();
        return metrics;
    }

    private static RomCandidate CreateCandidate(string mainPath, string gameKey, string consoleKey)
        => new()
        {
            MainPath = mainPath,
            GameKey = gameKey,
            Extension = Path.GetExtension(mainPath),
            ConsoleKey = consoleKey,
            Category = FileCategory.Game,
            SortDecision = SortDecision.Sort,
            SizeBytes = new FileInfo(mainPath).Length
        };

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

    private sealed class MissingToolsConverter(IReadOnlyList<string> missingTools) : IFormatConverter
    {
        public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension) => null;

        public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken cancellationToken = default)
            => new(sourcePath, sourcePath, ConversionOutcome.Skipped);

        public bool Verify(string targetPath, ConversionTarget target) => true;

        public IReadOnlyList<string> GetMissingToolsForFormat(string? convertFormat)
            => missingTools;
    }

    [Fact]
    public void Execute_Phase6_SkipsJunkRemovedGroups()
    {
        // "Junkonly (Beta).zip" is JUNK and the sole member of its GameKey → removed in Phase 3b
        // "Normal (USA).zip" is GAME → should be converted in Phase 6
        CreateFile("Junkonly (Beta).zip", 50);
        CreateFile("Normal (USA).zip", 60);

        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
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
    public void Execute_DryRun_RemoveJunk_WritesJunkPreviewAuditRow_FindingF08()
    {
        CreateFile("Junkonly (Beta).zip", 50);

        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new FakeAuditStore();
        var orch = new RunOrchestrator(fs, audit);

        var auditPath = Path.Combine(Path.GetTempPath(), "RunOrchAudit", Guid.NewGuid().ToString("N"), "audit.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(auditPath)!);

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "DryRun",
            RemoveJunk = true,
            AuditPath = auditPath,
            PreferRegions = new[] { "US" }
        };

        var result = orch.Execute(options);

        Assert.Equal("ok", result.Status);
        Assert.Contains(audit.AuditRows, row => string.Equals(row.action, "JUNK_PREVIEW", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void VerifyFailed_TriggersCompletedWithErrorsAndHealthPenalty()
    {
        CreateFile("Verify (USA).zip", 80);

        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
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
        Assert.Equal(4, failResult.ExitCode);
        Assert.True(failResult.ConvertVerifyFailedCount > 0);
        Assert.True(failProjection.FailCount > okProjection.FailCount);
        Assert.True(failProjection.HealthScore < okProjection.HealthScore);
    }

    [Fact]
    public void AuditSidecarWriteFailure_MarksRunAsError()
    {
        CreateFile("Game (USA).zip", 100);
        CreateFile("Game (Europe).zip", 100);

        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
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
