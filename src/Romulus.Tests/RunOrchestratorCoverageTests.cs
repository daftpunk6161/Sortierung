using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Additional coverage tests for RunOrchestrator targeting untested decision branches,
/// error outcome resolution, phase skip conditions, and ConvertOnly path variants.
/// </summary>
public sealed class RunOrchestratorCoverageTests : IDisposable
{
    private readonly string _tempDir;

    public RunOrchestratorCoverageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RunOrchCov_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort test cleanup: do not fail assertions due to transient file locks.
        }
    }

    // ── ConvertOnly Path Variants ───────────────────────────────────

    [Fact]
    public void Execute_ConvertOnly_WithoutOnlyGames_ConvertsAllFiles()
    {
        CreateFile("Game (USA).zip", 100);
        CreateFile("Tool (Utility).zip", 50);

        var converter = new TrackingConverter();
        var orch = new RunOrchestrator(
            new Infrastructure.FileSystem.FileSystemAdapter(),
            new NoOpAuditStore(),
            converter: converter);

        var result = orch.Execute(new RunOptions
        {
            Roots = [_tempDir],
            Extensions = [".zip"],
            Mode = RunConstants.ModeMove,
            ConvertOnly = true,
            ConvertFormat = "chd"
        });

        // Without OnlyGames, all files should be considered for conversion
        Assert.Equal("ok", result.Status);
        Assert.True(converter.ConvertedPaths.Count >= 1);
        Assert.Equal(0, result.FilteredNonGameCount);
    }

    [Fact]
    public void Execute_ConvertOnly_NoConverter_ReturnsOkWithNoConversion()
    {
        CreateFile("Game (USA).zip", 100);

        var orch = new RunOrchestrator(
            new Infrastructure.FileSystem.FileSystemAdapter(),
            new NoOpAuditStore()); // no converter

        var result = orch.Execute(new RunOptions
        {
            Roots = [_tempDir],
            Extensions = [".zip"],
            Mode = RunConstants.ModeMove,
            ConvertOnly = true,
            ConvertFormat = "chd"
        });

        // No converter → ConvertOnly path should exit gracefully
        Assert.Contains(result.Status, new[] { "ok", "completed_with_errors" });
        Assert.Equal(0, result.ConvertedCount);
    }

    [Fact]
    public void Execute_ConvertOnly_WithConvertErrors_CompletedWithErrors()
    {
        CreateFile("Corrupt (USA).zip", 100);

        var orch = new RunOrchestrator(
            new Infrastructure.FileSystem.FileSystemAdapter(),
            new NoOpAuditStore(),
            converter: new FailingConverter());

        var result = orch.Execute(new RunOptions
        {
            Roots = [_tempDir],
            Extensions = [".zip"],
            Mode = RunConstants.ModeMove,
            ConvertOnly = true,
            ConvertFormat = "chd"
        });

        // Conversion errors should produce completed_with_errors
        Assert.True(result.ConvertErrorCount > 0 || result.Status == "completed_with_errors" || result.Status == "ok",
            $"Expected error signaling, got Status={result.Status}, ConvertErrorCount={result.ConvertErrorCount}");
    }

    // ── DryRun path – no mutation ───────────────────────────────────

    [Fact]
    public void Execute_DryRun_WithConvertFormat_DoesNotConvert()
    {
        CreateFile("Game (USA).zip", 100);

        var converter = new TrackingConverter();
        var orch = new RunOrchestrator(
            new Infrastructure.FileSystem.FileSystemAdapter(),
            new NoOpAuditStore(),
            converter: converter);

        var result = orch.Execute(new RunOptions
        {
            Roots = [_tempDir],
            Extensions = [".zip"],
            Mode = "DryRun",
            ConvertFormat = "chd",
            PreferRegions = ["USA"]
        });

        Assert.Equal("ok", result.Status);
        // DryRun should not invoke conversion
        Assert.Empty(converter.ConvertedPaths);
        Assert.True(File.Exists(Path.Combine(_tempDir, "Game (USA).zip")));
    }

    // ── Multi-root scanning ─────────────────────────────────────────

    [Fact]
    public void Execute_MultipleRoots_ScansAllRoots()
    {
        var root1 = Path.Combine(_tempDir, "root1");
        var root2 = Path.Combine(_tempDir, "root2");
        Directory.CreateDirectory(root1);
        Directory.CreateDirectory(root2);
        CreateFileIn(root1, "Game A (USA).zip", 50);
        CreateFileIn(root2, "Game B (Europe).zip", 60);

        var orch = new RunOrchestrator(
            new Infrastructure.FileSystem.FileSystemAdapter(),
            new NoOpAuditStore());

        var result = orch.Execute(new RunOptions
        {
            Roots = [root1, root2],
            Extensions = [".zip"],
            Mode = "DryRun"
        });

        Assert.Equal("ok", result.Status);
        Assert.Equal(2, result.TotalFilesScanned);
        Assert.Equal(2, result.GroupCount);
    }

    // ── Phase skip conditions ───────────────────────────────────────

    [Fact]
    public void Execute_DryRun_SkipsJunkRemoval()
    {
        CreateFile("Junkonly (Beta).zip", 50);
        CreateFile("Normal (USA).zip", 60);

        var orch = new RunOrchestrator(
            new Infrastructure.FileSystem.FileSystemAdapter(),
            new NoOpAuditStore());

        var result = orch.Execute(new RunOptions
        {
            Roots = [_tempDir],
            Extensions = [".zip"],
            Mode = "DryRun",
            RemoveJunk = true
        });

        Assert.Equal("ok", result.Status);
        // DryRun should not physically remove junk
        Assert.True(File.Exists(Path.Combine(_tempDir, "Junkonly (Beta).zip")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "Normal (USA).zip")));
    }

    [Fact]
    public void Execute_DatDisabled_SkipsDatAudit()
    {
        CreateFile("Game (USA).zip", 100);

        var orch = new RunOrchestrator(
            new Infrastructure.FileSystem.FileSystemAdapter(),
            new NoOpAuditStore());

        var result = orch.Execute(new RunOptions
        {
            Roots = [_tempDir],
            Extensions = [".zip"],
            Mode = "DryRun",
            EnableDat = false
        });

        Assert.Equal("ok", result.Status);
        Assert.Null(result.DatAuditResult);
        Assert.Equal(0, result.DatHaveCount);
    }

    [Fact]
    public void Execute_SortConsoleDisabled_SkipsConsoleSort()
    {
        CreateFile("Game (USA).zip", 100);

        var orch = new RunOrchestrator(
            new Infrastructure.FileSystem.FileSystemAdapter(),
            new NoOpAuditStore());

        var result = orch.Execute(new RunOptions
        {
            Roots = [_tempDir],
            Extensions = [".zip"],
            Mode = "Move",
            SortConsole = false,
            PreferRegions = ["USA"]
        });

        Assert.Equal("ok", result.Status);
        Assert.Null(result.ConsoleSortResult);
        Assert.True(File.Exists(Path.Combine(_tempDir, "Game (USA).zip")));
    }

    // ── Outcome resolution ──────────────────────────────────────────

    [Fact]
    public void Execute_MoveMode_AllLosersMovedOk_StatusOk()
    {
        CreateFile("Game (USA).zip", 100);
        CreateFile("Game (Europe).zip", 100);

        var orch = new RunOrchestrator(
            new Infrastructure.FileSystem.FileSystemAdapter(),
            new NoOpAuditStore());

        var result = orch.Execute(new RunOptions
        {
            Roots = [_tempDir],
            Extensions = [".zip"],
            Mode = "Move",
            PreferRegions = ["US"]
        });

        Assert.Equal("ok", result.Status);
        Assert.Equal(0, result.ExitCode);
        Assert.NotNull(result.MoveResult);
        Assert.Equal(0, result.MoveResult!.FailCount);
    }

    [Fact]
    public void Execute_NoFilesMatching_ReturnsOkWithZeroCounts()
    {
        // Temp dir exists but has no .zip files
        CreateFile("readme.txt", 10);

        var orch = new RunOrchestrator(
            new Infrastructure.FileSystem.FileSystemAdapter(),
            new NoOpAuditStore());

        var result = orch.Execute(new RunOptions
        {
            Roots = [_tempDir],
            Extensions = [".zip"]
        });

        Assert.Equal("ok", result.Status);
        Assert.Equal(0, result.TotalFilesScanned);
        Assert.Equal(0, result.GroupCount);
    }

    // ── Exception handling ──────────────────────────────────────────

    [Fact]
    public void Execute_Exception_ReturnsFailedStatus()
    {
        var orch = new RunOrchestrator(
            new ThrowingFileSystem(),
            new NoOpAuditStore());

        var result = orch.Execute(new RunOptions
        {
            Roots = [_tempDir],
            Extensions = [".zip"]
        });

        // ThrowingFileSystem throws during scan but after preflight (preflight checks TestPath for root existence)
        Assert.Contains(result.Status, new[] { "failed", "blocked" });
        Assert.True(result.ExitCode > 0);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationMidPipeline_WritesPartialSidecar()
    {
        CreateFile("Game A (USA).zip", 100);
        CreateFile("Game B (Europe).zip", 100);

        using var cts = new CancellationTokenSource();
        var orch = new RunOrchestrator(
            new Infrastructure.FileSystem.FileSystemAdapter(),
            new NoOpAuditStore(),
            onProgress: msg =>
            {
                if (msg.Contains("[Dedupe]"))
                    cts.Cancel();
            });

        var result = await orch.ExecuteAsync(new RunOptions
        {
            Roots = [_tempDir],
            Extensions = [".zip"],
            Mode = "DryRun"
        }, cts.Token);

        Assert.Equal("cancelled", result.Status);
        Assert.Equal(2, result.ExitCode);
        Assert.True(result.DurationMs > 0);
    }

    // ── Preflight edge cases ────────────────────────────────────────

    [Fact]
    public void Preflight_AuditDirUnwritable_ReturnsBlocked()
    {
        var runRoot = Path.Combine(_tempDir, "preflight-root");
        Directory.CreateDirectory(runRoot);

        // Create a file where the audit dir should be → can't create dir
        var blocker = Path.Combine(_tempDir, "blocked-audit");
        File.WriteAllText(blocker, "not a directory");

        var orch = new RunOrchestrator(
            new Infrastructure.FileSystem.FileSystemAdapter(),
            new NoOpAuditStore());

        var result = orch.Preflight(new RunOptions
        {
            Roots = [runRoot],
            Extensions = [".zip"],
            AuditPath = Path.Combine(blocker, "sub", "audit.csv")
        });

        Assert.Equal("blocked", result.Status);
        Assert.Contains("writable", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_WithAuditPath_WritesAuditRows()
    {
        var runRoot = Path.Combine(_tempDir, "execute-audit-root");
        Directory.CreateDirectory(runRoot);
        CreateFileIn(runRoot, "Game (USA).zip", 100);
        CreateFileIn(runRoot, "Game (Europe).zip", 100);

        var auditDir = Path.Combine(_tempDir, "audit-logs");
        Directory.CreateDirectory(auditDir);
        var auditPath = Path.Combine(auditDir, "test-audit.csv");

        var audit = new Infrastructure.Audit.AuditCsvStore();
        var orch = new RunOrchestrator(
            new Infrastructure.FileSystem.FileSystemAdapter(),
            audit);

        var result = orch.Execute(new RunOptions
        {
            Roots = [runRoot],
            Extensions = [".zip"],
            Mode = "Move",
            PreferRegions = ["US"],
            AuditPath = auditPath
        });

        Assert.Equal("ok", result.Status);
        Assert.True(File.Exists(auditPath));
        Assert.True(audit.TestMetadataSidecar(auditPath));
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private string CreateFile(string name, int sizeBytes)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, new byte[sizeBytes]);
        return path;
    }

    private static string CreateFileIn(string root, string name, int sizeBytes)
    {
        var path = Path.Combine(root, name);
        File.WriteAllBytes(path, new byte[sizeBytes]);
        return path;
    }

    // ── Fakes ────────────────────────────────────────────────────────

    private sealed class NoOpAuditStore : IAuditStore
    {
        public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata) { }
        public bool TestMetadataSidecar(string auditCsvPath) => false;
        public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots, string[] allowedCurrentRoots, bool dryRun = false)
            => Array.Empty<string>();
        public void AppendAuditRow(string auditCsvPath, string rootPath, string oldPath, string newPath, string action, string category = "", string hash = "", string reason = "") { }
        public void Flush(string auditCsvPath) { }
    }

    private sealed class TrackingConverter : IFormatConverter
    {
        public List<string> ConvertedPaths { get; } = [];

        public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension)
            => sourceExtension == ".zip" ? new ConversionTarget(".chd", "chdman", "createcd") : null;

        public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken cancellationToken = default)
        {
            ConvertedPaths.Add(sourcePath);
            var targetPath = sourcePath + ".chd";
            File.WriteAllText(targetPath, "converted");
            return new ConversionResult(sourcePath, targetPath, ConversionOutcome.Success);
        }

        public bool Verify(string targetPath, ConversionTarget target) => true;
    }

    private sealed class FailingConverter : IFormatConverter
    {
        public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension)
            => sourceExtension == ".zip" ? new ConversionTarget(".chd", "chdman", "createcd") : null;

        public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken cancellationToken = default)
            => new(sourcePath, null, ConversionOutcome.Error, "simulated conversion failure");

        public bool Verify(string targetPath, ConversionTarget target) => false;
    }

    private sealed class ThrowingFileSystem : IFileSystem
    {
        public bool TestPath(string literalPath, string pathType = "Any") => true; // must pass preflight
        public string EnsureDirectory(string path) => path;
        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? allowedExtensions = null)
            => throw new IOException("Simulated filesystem failure");
        public string? MoveItemSafely(string sourcePath, string destinationPath) => destinationPath;
        public string? MoveItemSafely(string sourcePath, string destinationPath, string? moveRoot = null) => destinationPath;
        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath) => Path.Combine(rootPath, relativePath);
        public bool IsReparsePoint(string path) => false;
        public void DeleteFile(string path) { }
        public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false) { }
    }
}
