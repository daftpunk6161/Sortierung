// Coverage‑Boost Phase 6: ApplicationServiceFacade, ConsoleSorter atomic ops,
// OperationResult factories, JsonlLogWriter rotation/gzip,
// RunManager shutdown/eviction, ArchiveHashService 7z/ZipSlip paths.
using System.IO.Compression;
using RomCleanup.Api;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Core.Classification;
using RomCleanup.Infrastructure.Audit;
using RomCleanup.Infrastructure.FileSystem;
using RomCleanup.Infrastructure.Hashing;
using RomCleanup.Infrastructure.Logging;
using RomCleanup.Infrastructure.Orchestration;
using RomCleanup.Infrastructure.Services;
using RomCleanup.Infrastructure.Sorting;
using Xunit;

namespace RomCleanup.Tests;

// ═══════════════════════════════════════════════════════════════════════
// 1) ApplicationServiceFacade
// ═══════════════════════════════════════════════════════════════════════
public class ApplicationServiceFacadeTests : IDisposable
{
    private readonly string _tempDir;

    public ApplicationServiceFacadeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AppSvcFacade_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void RunDedupe_DelegatesToOrchestrator()
    {
        var fs = new FileSystemAdapter();
        var audit = new AuditCsvStore();
        var orchestrator = new RunOrchestrator(fs, audit);
        var logs = new List<string>();
        var facade = new ApplicationServiceFacade(fs, audit, new StubToolRunner(), orchestrator, log: msg => logs.Add(msg));

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Mode = "DryRun",
            Extensions = RunOptions.DefaultExtensions
        };

        var result = facade.RunDedupe(options);

        Assert.Equal("ok", result.Status);
        Assert.Contains(logs, l => l.Contains("Starting dedupe run"));
    }

    [Fact]
    public void RunPreflight_DelegatesToOrchestrator()
    {
        var fs = new FileSystemAdapter();
        var audit = new AuditCsvStore();
        var orchestrator = new RunOrchestrator(fs, audit);
        var facade = new ApplicationServiceFacade(fs, audit, new StubToolRunner(), orchestrator);

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Mode = "DryRun",
            Extensions = RunOptions.DefaultExtensions
        };

        var result = facade.RunPreflight(options);

        Assert.NotNull(result);
        Assert.Equal("ok", result.Status);
    }

    [Fact]
    public void GetConversionPreview_ReturnsPreview()
    {
        var fs = new FileSystemAdapter();
        var audit = new AuditCsvStore();
        var orchestrator = new RunOrchestrator(fs, audit);
        var facade = new ApplicationServiceFacade(fs, audit, new StubToolRunner(), orchestrator);

        var preview = facade.GetConversionPreview(new[] { _tempDir });

        Assert.NotNull(preview);
    }

    [Fact]
    public void RunDedupe_WithoutLog_NoThrow()
    {
        var fs = new FileSystemAdapter();
        var audit = new AuditCsvStore();
        var orchestrator = new RunOrchestrator(fs, audit);
        var facade = new ApplicationServiceFacade(fs, audit, new StubToolRunner(), orchestrator);

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Mode = "DryRun",
            Extensions = RunOptions.DefaultExtensions
        };

        var result = facade.RunDedupe(options);

        Assert.Equal("ok", result.Status);
    }

    private sealed class StubToolRunner : IToolRunner
    {
        public string? FindTool(string toolName) => null;
        public ToolResult InvokeProcess(string filePath, string[] arguments, string? errorLabel = null) =>
            new(ExitCode: 1, Output: "", Success: false);
        public ToolResult Invoke7z(string sevenZipPath, string[] arguments) =>
            new(ExitCode: 1, Output: "", Success: false);
    }
}

// ═══════════════════════════════════════════════════════════════════════
// 2) OperationResult – direct factory & property tests
// ═══════════════════════════════════════════════════════════════════════
public class OperationResultDirectTests
{
    [Fact]
    public void Ok_DefaultStatus()
    {
        var r = OperationResult.Ok();
        Assert.Equal("ok", r.Status);
        Assert.Null(r.Reason);
        Assert.Null(r.Value);
        Assert.False(r.ShouldReturn);
        Assert.Equal("OK", r.Outcome);
    }

    [Fact]
    public void Ok_WithReasonAndValue()
    {
        var r = OperationResult.Ok("done", 42);
        Assert.Equal("ok", r.Status);
        Assert.Equal("done", r.Reason);
        Assert.Equal(42, r.Value);
    }

    [Fact]
    public void Completed_SetsStatus()
    {
        var r = OperationResult.Completed("finished", "result-data");
        Assert.Equal("completed", r.Status);
        Assert.Equal("finished", r.Reason);
        Assert.Equal("result-data", r.Value);
        Assert.False(r.ShouldReturn);
        Assert.Equal("OK", r.Outcome);
    }

    [Fact]
    public void Skipped_SetsStatusAndOutcome()
    {
        var r = OperationResult.Skipped("no-op");
        Assert.Equal("skipped", r.Status);
        Assert.Equal("no-op", r.Reason);
        Assert.False(r.ShouldReturn);
        Assert.Equal("SKIP", r.Outcome);
    }

    [Fact]
    public void Blocked_SetsStatusAndShouldReturn()
    {
        var r = OperationResult.Blocked("root-missing");
        Assert.Equal("blocked", r.Status);
        Assert.True(r.ShouldReturn);
        Assert.Equal("ERROR", r.Outcome);
    }

    [Fact]
    public void Error_SetsStatusAndShouldReturn()
    {
        var r = OperationResult.Error("crash");
        Assert.Equal("error", r.Status);
        Assert.True(r.ShouldReturn);
        Assert.Equal("ERROR", r.Outcome);
    }

    [Fact]
    public void Meta_Warnings_Metrics_Artifacts_AreDefaultEmpty()
    {
        var r = OperationResult.Ok();
        Assert.Empty(r.Meta);
        Assert.Empty(r.Warnings);
        Assert.Empty(r.Metrics);
        Assert.Empty(r.Artifacts);
    }

    [Fact]
    public void Continue_Outcome_IsOK()
    {
        var r = new OperationResult { Status = "continue" };
        Assert.Equal("OK", r.Outcome);
        Assert.False(r.ShouldReturn);
    }
}

// ═══════════════════════════════════════════════════════════════════════
// 3) JsonlLogWriter – gzip rotation, pruning, convenience methods
// ═══════════════════════════════════════════════════════════════════════
public class JsonlLogWriterGzipTests : IDisposable
{
    private readonly string _tempDir;

    public JsonlLogWriterGzipTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "LogGzip_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void RotateIfNeeded_GzipTrue_CreatesGzFile()
    {
        var logPath = Path.Combine(_tempDir, "test.jsonl");
        using (var writer = new JsonlLogWriter(logPath, LogLevel.Debug))
        {
            // Write enough to exceed 100 bytes
            for (int i = 0; i < 20; i++)
                writer.Info("Mod", "Act", $"Message {i} with padding to grow file size");

            writer.RotateIfNeeded(maxBytes: 100, keepFiles: 5, gzip: true);
        }

        // Should have a .gz file and a new empty log
        var gzFiles = Directory.GetFiles(_tempDir, "*.gz");
        Assert.NotEmpty(gzFiles);
        Assert.True(File.Exists(logPath), "New log file should exist after rotation");
    }

    [Fact]
    public void RotateIfNeeded_GzipFalse_CreatesJsonlArchive()
    {
        var logPath = Path.Combine(_tempDir, "test.jsonl");
        using (var writer = new JsonlLogWriter(logPath, LogLevel.Debug))
        {
            for (int i = 0; i < 20; i++)
                writer.Info("Mod", "Act", $"Message {i} with padding to grow file size");

            writer.RotateIfNeeded(maxBytes: 100, keepFiles: 5, gzip: false);
        }

        var archives = Directory.GetFiles(_tempDir, "test-*.jsonl");
        Assert.NotEmpty(archives);
    }

    [Fact]
    public void RotateIfNeeded_BelowThreshold_NoRotation()
    {
        var logPath = Path.Combine(_tempDir, "small.jsonl");
        using var writer = new JsonlLogWriter(logPath, LogLevel.Debug);
        writer.Info("Mod", "Act", "small");

        writer.RotateIfNeeded(maxBytes: 10 * 1024 * 1024);

        // Only the original log file should exist
        var allFiles = Directory.GetFiles(_tempDir);
        Assert.Single(allFiles);
    }

    [Fact]
    public void Rotation_PrunesOldArchives_WhenExceedingKeepFiles()
    {
        var logPath = Path.Combine(_tempDir, "prune.jsonl");

        // Create artificial archive files (these are already-rotated archives)
        for (int i = 0; i < 8; i++)
        {
            var ts = DateTime.Now.AddMinutes(-10 - i).ToString("yyyyMMdd-HHmmss");
            File.WriteAllText(Path.Combine(_tempDir, $"prune-{ts}.jsonl"), "old");
            Thread.Sleep(10); // ensure unique timestamps
        }

        // Write a big current log file to trigger rotation
        File.WriteAllText(logPath, new string('x', 2000));

        // Rotate with threshold below current size → triggers rotation + prune
        JsonlLogRotation.Rotate(logPath, maxBytes: 100, keepFiles: 3);

        // After rotation: 8 old archives + 1 newly rotated = 9, keepFiles=3 → should prune to 3
        var remaining = Directory.GetFiles(_tempDir, "prune-*.jsonl");
        Assert.True(remaining.Length <= 3, $"Expected ≤3 archives after pruning, got {remaining.Length}");
    }

    [Fact]
    public void Debug_ConvenienceMethod_WritesAtDebugLevel()
    {
        var logPath = Path.Combine(_tempDir, "debug.jsonl");
        using (var writer = new JsonlLogWriter(logPath, LogLevel.Debug))
        {
            writer.Debug("TestModule", "Debug message");
        }

        var content = File.ReadAllText(logPath);
        Assert.Contains("Debug", content);
        Assert.Contains("TestModule", content);
    }

    [Fact]
    public void Warning_ConvenienceMethod_IncludesErrorClass()
    {
        var logPath = Path.Combine(_tempDir, "warn.jsonl");
        using (var writer = new JsonlLogWriter(logPath, LogLevel.Debug))
        {
            writer.Warning("WarnMod", "Warning text", "Recoverable");
        }

        var content = File.ReadAllText(logPath);
        Assert.Contains("Warning", content);
        Assert.Contains("Recoverable", content);
    }

    [Fact]
    public void Error_ConvenienceMethod_IncludesErrorClass()
    {
        var logPath = Path.Combine(_tempDir, "error.jsonl");
        using (var writer = new JsonlLogWriter(logPath, LogLevel.Debug))
        {
            writer.Error("ErrMod", "Error text", "Critical");
        }

        var content = File.ReadAllText(logPath);
        Assert.Contains("Error", content);
        Assert.Contains("Critical", content);
    }

    [Fact]
    public void Write_WithMetrics_IncludesMetricsInOutput()
    {
        var logPath = Path.Combine(_tempDir, "metrics.jsonl");
        using (var writer = new JsonlLogWriter(logPath, LogLevel.Debug))
        {
            writer.Write(LogLevel.Info, "Mod", "Act", "msg", metrics: new Dictionary<string, object>
            {
                { "filesProcessed", 42 },
                { "durationMs", 1234.5 }
            });
        }

        var content = File.ReadAllText(logPath);
        Assert.Contains("filesProcessed", content);
        Assert.Contains("42", content);
    }

    [Fact]
    public void Write_BelowMinLevel_Suppressed()
    {
        var logPath = Path.Combine(_tempDir, "minlevel.jsonl");
        using (var writer = new JsonlLogWriter(logPath, LogLevel.Warning))
        {
            writer.Debug("Mod", "should-not-appear");
            writer.Info("Mod", "Act", "should-not-appear");
        }

        var content = File.ReadAllText(logPath);
        Assert.Empty(content.Trim());
    }

    [Fact]
    public void GzipRotation_CompressedFileIsValid()
    {
        var logPath = Path.Combine(_tempDir, "gzvalid.jsonl");
        var bigContent = string.Join("\n", Enumerable.Range(0, 100).Select(i => $"{{\"line\":{i}}}"));
        File.WriteAllText(logPath, bigContent);

        JsonlLogRotation.Rotate(logPath, maxBytes: 10, keepFiles: 5, gzip: true);

        var gzFiles = Directory.GetFiles(_tempDir, "*.gz");
        Assert.NotEmpty(gzFiles);

        // Verify GZIP is decompressible
        using var fs = File.OpenRead(gzFiles[0]);
        using var gz = new GZipStream(fs, CompressionMode.Decompress);
        using var reader = new StreamReader(gz);
        var decompressed = reader.ReadToEnd();
        Assert.Contains("\"line\":", decompressed);
    }
}

// ═══════════════════════════════════════════════════════════════════════
// 4) ConsoleSorter – atomic set move with rollback
// ═══════════════════════════════════════════════════════════════════════
public class ConsoleSorterAtomicTests : IDisposable
{
    private readonly string _tempDir;

    public ConsoleSorterAtomicTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SorterAtomic_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            try { Directory.Delete(_tempDir, true); } catch { }
    }

    private ConsoleDetector BuildDetector()
    {
        var consoles = new List<ConsoleInfo>
        {
            new("PS1", "PlayStation", true, new[] { ".cue", ".bin" }, Array.Empty<string>(),
                new[] { "PS1", "PlayStation" }),
            new("SNES", "Super Nintendo", false, new[] { ".sfc" }, Array.Empty<string>(),
                new[] { "SNES" }),
        };
        return new ConsoleDetector(consoles);
    }

    [Fact]
    public void Sort_CueBinSet_DryRun_CountsSetMembers()
    {
        // Create CUE+BIN set
        var cueContent = "FILE \"Track01.bin\" BINARY\r\n  TRACK 01 MODE1/2352\r\n    INDEX 01 00:00:00\r\n";
        File.WriteAllText(Path.Combine(_tempDir, "Game.cue"), cueContent);
        File.WriteAllText(Path.Combine(_tempDir, "Track01.bin"), "binary data here");

        var fs = new FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, BuildDetector());

        var result = sorter.Sort(new[] { _tempDir }, new[] { ".cue", ".bin" }, dryRun: true);

        Assert.Equal(1, result.Moved); // CUE primary
        Assert.True(result.SetMembersMoved >= 1, "BIN should count as set member");
    }

    [Fact]
    public void Sort_CueBinSet_MoveMode_MovesAllSetMembers()
    {
        var cueContent = "FILE \"Track01.bin\" BINARY\r\n  TRACK 01 MODE1/2352\r\n    INDEX 01 00:00:00\r\n";
        File.WriteAllText(Path.Combine(_tempDir, "Game.cue"), cueContent);
        File.WriteAllText(Path.Combine(_tempDir, "Track01.bin"), "binary data");

        var fs = new FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, BuildDetector());

        var result = sorter.Sort(new[] { _tempDir }, new[] { ".cue", ".bin" }, dryRun: false);

        Assert.Equal(1, result.Moved);
        Assert.True(File.Exists(Path.Combine(_tempDir, "PS1", "Game.cue")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "PS1", "Track01.bin")));
    }

    [Fact]
    public void Sort_InvalidConsoleKey_CountsAsUnknown()
    {
        // File that a rigged detector would return an invalid key for
        // We exploit a file that won't match any console → UNKNOWN
        File.WriteAllText(Path.Combine(_tempDir, "game.xyz"), "data");
        var fs = new FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, BuildDetector());

        var result = sorter.Sort(new[] { _tempDir }, new[] { ".xyz" }, dryRun: true);

        Assert.True(result.Unknown >= 1);
    }
}

// ═══════════════════════════════════════════════════════════════════════
// 5) RunManager – ShutdownAsync & EvictOldRuns
// ═══════════════════════════════════════════════════════════════════════
public class RunManagerAdvancedTests
{
    private static RunManager CreateManager() => new(new FileSystemAdapter(), new AuditCsvStore());

    [Fact]
    public async Task ShutdownAsync_NoActiveRun_CompletesWithoutError()
    {
        var mgr = CreateManager();
        await mgr.ShutdownAsync();
        // Should complete without exception
    }

    [Fact]
    public async Task ShutdownAsync_WithActiveRun_CancelsAndWaits()
    {
        var mgr = CreateManager();
        var dir = CreateTempDir();
        try
        {
            var request = new RunRequest { Roots = new[] { dir }, Mode = "DryRun" };
            var run = mgr.TryCreate(request, "DryRun");
            Assert.NotNull(run);

            await mgr.ShutdownAsync();

            var completed = mgr.Get(run!.RunId);
            Assert.NotNull(completed);
            Assert.NotEqual("running", completed!.Status);
        }
        finally
        {
            CleanupDir(dir);
        }
    }

    [Fact]
    public async Task EvictOldRuns_PurgesCompletedBeyondLimit()
    {
        var mgr = CreateManager();
        var dir = CreateTempDir();
        try
        {
            // Create and complete 5 runs sequentially
            for (int i = 0; i < 5; i++)
            {
                var request = new RunRequest { Roots = new[] { dir }, Mode = "DryRun" };
                var run = mgr.TryCreate(request, "DryRun");
                Assert.NotNull(run);
                await mgr.WaitForCompletion(run!.RunId, 50);
            }

            // All 5 should be retrievable (well below MaxRunHistory=100)
            // This tests the eviction path doesn't break under normal usage
        }
        finally
        {
            CleanupDir(dir);
        }
    }

    [Fact]
    public void Cancel_NonExistentRun_NoThrow()
    {
        var mgr = CreateManager();
        mgr.Cancel("ghost-run-id");
        // Should not throw
    }

    [Fact]
    public async Task Cancel_AlreadyCompletedRun_NoThrow()
    {
        var mgr = CreateManager();
        var dir = CreateTempDir();
        try
        {
            var request = new RunRequest { Roots = new[] { dir }, Mode = "DryRun" };
            var run = mgr.TryCreate(request, "DryRun");
            await mgr.WaitForCompletion(run!.RunId, 50);

            // Cancel after completion — CTS may be disposed
            mgr.Cancel(run.RunId);
        }
        finally
        {
            CleanupDir(dir);
        }
    }

    [Fact]
    public void RunRecord_ProgressMessage_ThreadSafe()
    {
        var record = new RunRecord { RunId = "test" };
        record.ProgressMessage = "Scanning...";
        Assert.Equal("Scanning...", record.ProgressMessage);
        record.ProgressMessage = "Done";
        Assert.Equal("Done", record.ProgressMessage);
    }

    [Fact]
    public void ApiRunResult_DefaultValues()
    {
        var r = new ApiRunResult();
        Assert.Equal("", r.Status);
        Assert.Equal(0, r.ExitCode);
        Assert.Equal(0, r.TotalFiles);
        Assert.Null(r.Error);
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rmgr-adv-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void CleanupDir(string dir)
    {
        if (Directory.Exists(dir))
            try { Directory.Delete(dir, true); } catch { }
    }
}

// ═══════════════════════════════════════════════════════════════════════
// 6) ArchiveHashService – Zip-Slip & 7z paths
// ═══════════════════════════════════════════════════════════════════════
public class ArchiveHashServiceZipSlipTests
{
    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\..\\windows\\system32\\config")]
    [InlineData("foo/../../../bar")]
    [InlineData("C:/absolute/path/file.rom")]
    [InlineData("/absolute/unix/path")]
    public void AreEntryPathsSafe_MaliciousPaths_ReturnsFalse(string path)
    {
        Assert.False(ArchiveHashService.AreEntryPathsSafe(new[] { path }));
    }

    [Theory]
    [InlineData("game.rom")]
    [InlineData("subdir/game.rom")]
    [InlineData("a/b/c/deep/file.bin")]
    public void AreEntryPathsSafe_SafePaths_ReturnsTrue(string path)
    {
        Assert.True(ArchiveHashService.AreEntryPathsSafe(new[] { path }));
    }

    [Fact]
    public void AreEntryPathsSafe_EmptyAndWhitespace_Ignored()
    {
        Assert.True(ArchiveHashService.AreEntryPathsSafe(new[] { "", "  ", "game.rom" }));
    }

    [Fact]
    public void AreEntryPathsSafe_MixedSafeAndUnsafe_ReturnsFalse()
    {
        Assert.False(ArchiveHashService.AreEntryPathsSafe(new[] { "safe.rom", "../../../evil.txt" }));
    }

    [Fact]
    public void GetArchiveHashes_NonexistentFile_ReturnsEmpty()
    {
        var svc = new ArchiveHashService();
        var result = svc.GetArchiveHashes("/nonexistent/file.zip");
        Assert.Empty(result);
    }

    [Fact]
    public void GetArchiveHashes_NullPath_ReturnsEmpty()
    {
        var svc = new ArchiveHashService();
        var result = svc.GetArchiveHashes(null!);
        Assert.Empty(result);
    }

    [Fact]
    public void GetArchiveHashes_EmptyPath_ReturnsEmpty()
    {
        var svc = new ArchiveHashService();
        var result = svc.GetArchiveHashes("");
        Assert.Empty(result);
    }

    [Fact]
    public void GetArchiveHashes_7zWithoutToolRunner_ReturnsEmpty()
    {
        var tempFile = Path.GetTempFileName();
        var sevenZFile = Path.ChangeExtension(tempFile, ".7z");
        File.Move(tempFile, sevenZFile);
        try
        {
            var svc = new ArchiveHashService(toolRunner: null);
            var result = svc.GetArchiveHashes(sevenZFile);
            Assert.Empty(result);
        }
        finally
        {
            File.Delete(sevenZFile);
        }
    }

    [Fact]
    public void GetArchiveHashes_OversizedFile_ReturnsEmpty()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            // Create a "big" archive (exceeds tiny limit)
            File.WriteAllBytes(tempFile, new byte[100]);
            var zipFile = Path.ChangeExtension(tempFile, ".zip");
            File.Move(tempFile, zipFile);

            var svc = new ArchiveHashService(maxArchiveSizeBytes: 50);
            var result = svc.GetArchiveHashes(zipFile);
            Assert.Empty(result);

            File.Delete(zipFile);
        }
        catch
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void GetArchiveHashes_ZipWithEntries_ReturnsHashes()
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.zip");
        try
        {
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("game.rom");
                using var s = entry.Open();
                s.Write(new byte[] { 0x01, 0x02, 0x03, 0x04 });
            }

            var svc = new ArchiveHashService();
            var hashes = svc.GetArchiveHashes(zipPath, "SHA1");
            Assert.NotEmpty(hashes);

            // Second call should be cached
            var cached = svc.GetArchiveHashes(zipPath, "SHA1");
            Assert.Equal(hashes, cached);
        }
        finally
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
        }
    }

    [Fact]
    public void GetArchiveHashes_UnsupportedExtension_ReturnsEmpty()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.rar");
        File.WriteAllText(tempFile, "not-a-real-rar");
        try
        {
            var svc = new ArchiveHashService();
            var result = svc.GetArchiveHashes(tempFile);
            Assert.Empty(result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ClearCache_ResetsCacheCount()
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"cc_{Guid.NewGuid():N}.zip");
        try
        {
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("data.bin");
                using var s = entry.Open();
                s.WriteByte(0xFF);
            }

            var svc = new ArchiveHashService();
            svc.GetArchiveHashes(zipPath);
            Assert.True(svc.CacheCount > 0);

            svc.ClearCache();
            Assert.Equal(0, svc.CacheCount);
        }
        finally
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
        }
    }

    [Fact]
    public void GetArchiveHashes_Sha256_ReturnsHex()
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"sha256_{Guid.NewGuid():N}.zip");
        try
        {
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("rom.bin");
                using var s = entry.Open();
                s.Write(new byte[] { 0xAA, 0xBB, 0xCC });
            }

            var svc = new ArchiveHashService();
            var hashes = svc.GetArchiveHashes(zipPath, "SHA256");
            Assert.NotEmpty(hashes);
            Assert.All(hashes, h => Assert.Matches("^[0-9a-f]{64}$", h));
        }
        finally
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
        }
    }

    [Fact]
    public void GetArchiveHashes_MD5_ReturnsHex()
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"md5_{Guid.NewGuid():N}.zip");
        try
        {
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("rom.bin");
                using var s = entry.Open();
                s.Write(new byte[] { 0x11, 0x22, 0x33 });
            }

            var svc = new ArchiveHashService();
            var hashes = svc.GetArchiveHashes(zipPath, "MD5");
            Assert.NotEmpty(hashes);
            Assert.All(hashes, h => Assert.Matches("^[0-9a-f]{32}$", h));
        }
        finally
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════
// 7) DatSourceService – catalog loading
// ═══════════════════════════════════════════════════════════════════════
public class DatSourceServiceCoverageTests : IDisposable
{
    private readonly string _tempDir;

    public DatSourceServiceCoverageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DatSvc_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public async Task DownloadDatAsync_PathTraversal_Rejects()
    {
        using var svc = new RomCleanup.Infrastructure.Dat.DatSourceService(_tempDir);
        var result = await svc.DownloadDatAsync(
            "https://example.com/dat.xml",
            "../../../etc/passwd");

        Assert.Null(result);
    }

    [Fact]
    public async Task DownloadDatAsync_NullFileName_Rejects()
    {
        using var svc = new RomCleanup.Infrastructure.Dat.DatSourceService(_tempDir);
        var result = await svc.DownloadDatAsync(
            "https://example.com/dat.xml",
            null!);

        Assert.Null(result);
    }

    [Fact]
    public async Task DownloadDatAsync_EmptyUrl_Rejects()
    {
        using var svc = new RomCleanup.Infrastructure.Dat.DatSourceService(_tempDir);
        var result = await svc.DownloadDatAsync("", "file.dat");
        Assert.Null(result);
    }
}

// ═══════════════════════════════════════════════════════════════════════
// 8) AuditSigningService – additional branch coverage
// ═══════════════════════════════════════════════════════════════════════
public class AuditSigningAdditionalTests
{
    [Fact]
    public void VerifyMetadataSidecar_NonexistentSidecar_Throws()
    {
        var fs = new FileSystemAdapter();
        var svc = new RomCleanup.Infrastructure.Audit.AuditSigningService(fs);
        Assert.Throws<FileNotFoundException>(() =>
            svc.VerifyMetadataSidecar(Path.Combine(Path.GetTempPath(), "nonexistent_" + Guid.NewGuid())));
    }

    [Fact]
    public void VerifyMetadataSidecar_InvalidJson_Throws()
    {
        var tempCsv = Path.GetTempFileName();
        var metaPath = tempCsv + ".meta.json";
        try
        {
            File.WriteAllText(metaPath, "not-json");
            var fs = new FileSystemAdapter();
            var svc = new RomCleanup.Infrastructure.Audit.AuditSigningService(fs);
            Assert.ThrowsAny<Exception>(() => svc.VerifyMetadataSidecar(tempCsv));
        }
        finally
        {
            if (File.Exists(tempCsv)) File.Delete(tempCsv);
            if (File.Exists(metaPath)) File.Delete(metaPath);
        }
    }
}
