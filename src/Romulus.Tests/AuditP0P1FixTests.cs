using System.Windows.Input;
using Romulus.CLI;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Metrics;
using Romulus.Infrastructure.Orchestration;
using Romulus.UI.Wpf.Models;
using Romulus.UI.Wpf.Services;
using Romulus.UI.Wpf.ViewModels;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Tests for P0/P1 audit fixes (2026-03-26).
/// Covers: P0-1 Fingerprint, P0-2 CLI Reparse, P1-1 CLI Rollback Parser,
///         P1-3 DatRename Conflict, P1-5 Stub Commands.
/// </summary>
public sealed class AuditP0P1FixTests : IDisposable
{
    private readonly string _tempDir;

    public AuditP0P1FixTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"audit_p0p1_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  P1-1: CLI --rollback flag parsing
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void CliParser_Rollback_ShouldParseCommand()
    {
        // Arrange: create a fake audit file so the parser doesn't fail with "not found"
        var auditFile = Path.Combine(_tempDir, "audit.csv");
        File.WriteAllText(auditFile, "old,new,action\n");

        // Act
        var result = CliArgsParser.Parse(["--rollback", auditFile]);

        // Assert
        Assert.Equal(CliCommand.Rollback, result.Command);
        Assert.Equal(0, result.ExitCode);
        Assert.NotNull(result.Options);
        Assert.Equal(auditFile, result.Options!.RollbackAuditPath);
    }

    [Fact]
    public void CliParser_Rollback_WithDryRun_SetsFlag()
    {
        var auditFile = Path.Combine(_tempDir, "audit.csv");
        File.WriteAllText(auditFile, "old,new,action\n");

        var result = CliArgsParser.Parse(["--rollback", auditFile, "--rollback-dry-run"]);

        Assert.Equal(CliCommand.Rollback, result.Command);
        Assert.True(result.Options!.RollbackDryRun);
    }

    [Fact]
    public void CliParser_Rollback_MissingFile_ShouldReturnValidationError()
    {
        var result = CliArgsParser.Parse(["--rollback", @"C:\nonexistent\audit.csv"]);

        Assert.NotEqual(0, result.ExitCode);
        Assert.True(result.Errors.Count > 0);
    }

    [Fact]
    public void CliParser_Rollback_MissingValue_ShouldReturnError()
    {
        var result = CliArgsParser.Parse(["--rollback"]);

        Assert.True(result.Errors.Count > 0);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  P1-3: DatRename Konflikterkennung — skip when target exists
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void DatRename_ShouldSkip_WhenTargetFileAlreadyExists()
    {
        // Arrange: create source and target files in the temp dir
        var sourceFile = Path.Combine(_tempDir, "wrong_name.nes");
        var targetFile = Path.Combine(_tempDir, "Correct Name.nes");
        File.WriteAllText(sourceFile, "source");
        File.WriteAllText(targetFile, "existing target");

        var options = new RunOptions
        {
            Mode = "Move",
            Roots = [_tempDir],
            ConflictPolicy = "Skip",
            AuditPath = Path.Combine(_tempDir, "audit.csv")
        };

        var entries = new[]
        {
            new DatAuditEntry(
                FilePath: sourceFile,
                Hash: "abc123",
                Status: DatAuditStatus.HaveWrongName,
                DatGameName: "Correct Name",
                DatRomFileName: "Correct Name.nes",
                ConsoleKey: "NES",
                Confidence: 100)
        };

        var fs = new TrackingFileSystem();
        var audit = new TrackingAuditStore();
        var context = CreateContext(options, fs, audit);

        // Act
        var result = new DatRenamePipelinePhase().Execute(
            new DatRenameInput(entries, options),
            context,
            CancellationToken.None);

        // Assert: should be skipped, NOT renamed
        Assert.Equal(0, result.ExecutedCount);
        Assert.True(result.SkippedCount >= 1);
        Assert.Equal(0, fs.RenameCalls);
    }

    [Fact]
    public void DatRename_ShouldProceed_WhenTargetDoesNotExist()
    {
        // Arrange: only source exists
        var sourceFile = Path.Combine(_tempDir, "wrong_name.nes");
        File.WriteAllText(sourceFile, "source");

        var options = new RunOptions
        {
            Mode = "Move",
            Roots = [_tempDir],
            AuditPath = Path.Combine(_tempDir, "audit.csv")
        };

        var entries = new[]
        {
            new DatAuditEntry(
                FilePath: sourceFile,
                Hash: "abc123",
                Status: DatAuditStatus.HaveWrongName,
                DatGameName: "Correct Name",
                DatRomFileName: "Correct Name.nes",
                ConsoleKey: "NES",
                Confidence: 100)
        };

        var fs = new TrackingFileSystem
        {
            RenameResult = Path.Combine(_tempDir, "Correct Name.nes")
        };
        var audit = new TrackingAuditStore();
        var context = CreateContext(options, fs, audit);

        // Act
        var result = new DatRenamePipelinePhase().Execute(
            new DatRenameInput(entries, options),
            context,
            CancellationToken.None);

        // Assert: should rename
        Assert.Equal(1, result.ExecutedCount);
        Assert.Equal(1, fs.RenameCalls);
        Assert.Equal(2, audit.AppendCalls);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  P1-5: Stub-Commands were removed during feature consolidation (Phases 1-6).
    //  All referenced stub keys (FtpSource, CloudSync, PluginMarketplaceFeature,
    //  SchedulerAdvanced, GpuHashing, ParallelHashing) are no longer registered.

    private sealed class MinimalDialog : IDialogService
    {
        public string? BrowseFolder(string title = "") => null;
        public string? BrowseFile(string title = "", string filter = "") => null;
        public string? SaveFile(string title = "", string filter = "", string? defaultFileName = null) => null;
        public bool Confirm(string message, string title = "") => true;
        public void Info(string message, string title = "") { }
        public void Error(string message, string title = "") { }
        public ConfirmResult YesNoCancel(string message, string title = "") => Contracts.Ports.ConfirmResult.Yes;
        public string ShowInputBox(string prompt, string title = "", string defaultValue = "") => "";
        public void ShowText(string title, string content) { }
        public bool DangerConfirm(string title, string message, string confirmText, string buttonLabel = "") => true;
        public bool ConfirmConversionReview(string title, string summary, IReadOnlyList<Romulus.Contracts.Models.ConversionReviewEntry> entries) => true;
        public bool ConfirmDatRenamePreview(IReadOnlyList<DatAuditEntry> renameProposals) => true;
    }

    private sealed class MinimalSettings : ISettingsService
    {
        public string? LastAuditPath => null;
        public string LastTheme => "Dark";
        public SettingsDto? Load() => new();
        public void LoadInto(MainViewModel vm) { }
        public bool SaveFrom(MainViewModel vm, string? lastAuditPath = null) => true;
    }

    private sealed class MinimalTheme : IThemeService
    {
        public AppTheme Current => AppTheme.Dark;
        public bool IsDark => true;
        public IReadOnlyList<AppTheme> AvailableThemes => [AppTheme.Dark];
        public void ApplyTheme(AppTheme theme) { }
        public void ApplyTheme(bool dark) { }
        public void Toggle() { }
    }

    private sealed class MinimalWindowHost : IWindowHost
    {
        public double FontSize { get; set; } = 14;
        public void SelectTab(int index) { }
        public void ShowTextDialog(string title, string content) { }
        public void ToggleSystemTray() { }
        public void StartApiProcess(string projectPath) { }
        public void StopApiProcess() { }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════════

    private static PipelineContext CreateContext(RunOptions options, IFileSystem fs, IAuditStore audit)
    {
        var metrics = new PhaseMetricsCollector();
        metrics.Initialize();

        return new PipelineContext
        {
            Options = options,
            FileSystem = fs,
            AuditStore = audit,
            Metrics = metrics,
            OnProgress = _ => { }
        };
    }

    private sealed class TrackingFileSystem : IFileSystem
    {
        public int RenameCalls { get; private set; }
        public string? RenameResult { get; init; }

        public bool TestPath(string literalPath, string pathType = "Any") => true;
        public string EnsureDirectory(string path) => path;
        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? allowedExtensions = null) => Array.Empty<string>();
        public string? MoveItemSafely(string sourcePath, string destinationPath) => destinationPath;
        public bool MoveDirectorySafely(string sourcePath, string destinationPath) => true;
        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath) => Path.Combine(rootPath, relativePath);
        public bool IsReparsePoint(string path) => false;
        public void DeleteFile(string path) { }
        public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false) { }

        public string? RenameItemSafely(string sourcePath, string newFileName)
        {
            RenameCalls++;
            return RenameResult;
        }
    }

    private sealed class TrackingAuditStore : IAuditStore
    {
        public int AppendCalls { get; private set; }
        public string LastAction { get; private set; } = string.Empty;

        public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata) { }
        public bool TestMetadataSidecar(string auditCsvPath) => true;
        public void Flush(string auditCsvPath) { }
        public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots, string[] allowedCurrentRoots, bool dryRun = false)
            => Array.Empty<string>();

        public void AppendAuditRow(string auditCsvPath, string rootPath, string oldPath, string newPath, string action, string category = "", string hash = "", string reason = "")
        {
            AppendCalls++;
            LastAction = action;
        }
    }
}
