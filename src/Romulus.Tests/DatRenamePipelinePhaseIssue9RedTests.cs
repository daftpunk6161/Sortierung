using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Contracts;
using Romulus.Infrastructure.Metrics;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// TDD RED (Issue9/A-15): specification tests for DatRenamePipelinePhase.
/// These tests intentionally fail until DatRenamePipelinePhase and related models are implemented.
/// </summary>
public sealed class DatRenamePipelinePhaseIssue9RedTests
{
    [Fact]
    public void Execute_ShouldOnlyCreateProposals_WhenModeIsDryRun_Issue9()
    {
        // Arrange
        var options = new RunOptions
        {
            Mode = "DryRun",
            Roots = new[] { @"C:\roms" },
            AuditPath = @"C:\audit\run.csv"
        };

        var entries = new[]
        {
            new DatAuditEntry(
                FilePath: @"C:\roms\NES\wrong.nes",
                Hash: "abc123",
                Status: DatAuditStatus.HaveWrongName,
                DatGameName: "Super Mario Bros",
                DatRomFileName: "Super Mario Bros.nes",
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

        // Assert
        Assert.Equal(1, result.ProposedCount);
        Assert.Equal(0, result.ExecutedCount);
        Assert.Equal(0, fs.RenameCalls);
        Assert.Equal(0, audit.AppendCalls);
    }

    [Fact]
    public void Execute_ShouldRenameAndAudit_WhenModeIsMove_Issue9()
    {
        // Arrange
        var options = new RunOptions
        {
            Mode = "Move",
            Roots = new[] { @"C:\roms" },
            AuditPath = @"C:\audit\run.csv"
        };

        var entries = new[]
        {
            new DatAuditEntry(
                FilePath: @"C:\roms\NES\wrong.nes",
                Hash: "abc123",
                Status: DatAuditStatus.HaveWrongName,
                DatGameName: "Super Mario Bros",
                DatRomFileName: "Super Mario Bros.nes",
                ConsoleKey: "NES",
                Confidence: 100)
        };

        var fs = new TrackingFileSystem
        {
            RenameResult = @"C:\roms\NES\Super Mario Bros.nes"
        };
        var audit = new TrackingAuditStore();
        var context = CreateContext(options, fs, audit);

        // Act
        var result = new DatRenamePipelinePhase().Execute(
            new DatRenameInput(entries, options),
            context,
            CancellationToken.None);

        // Assert
        Assert.Equal(1, result.ProposedCount);
        Assert.Equal(1, result.ExecutedCount);
        Assert.Equal(1, fs.RenameCalls);
        Assert.Equal(2, audit.AppendCalls);
        Assert.Contains(RunConstants.AuditActions.DatRenamePending, audit.Actions);
        Assert.Equal("DAT_RENAME", audit.LastAction);
    }

    [Fact]
    public void Execute_ShouldSkipEntries_ThatAreNotHaveWrongName_Issue9()
    {
        // Arrange
        var options = new RunOptions
        {
            Mode = "Move",
            Roots = new[] { @"C:\roms" },
            AuditPath = @"C:\audit\run.csv"
        };

        var entries = new[]
        {
            new DatAuditEntry(
                FilePath: @"C:\roms\NES\ok.nes",
                Hash: "def456",
                Status: DatAuditStatus.Have,
                DatGameName: "Already Ok",
                DatRomFileName: "ok.nes",
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

        // Assert
        Assert.Equal(0, result.ExecutedCount);
        Assert.True(result.SkippedCount >= 1);
        Assert.Equal(0, fs.RenameCalls);
        Assert.Equal(0, audit.AppendCalls);
    }

    [Fact]
    public void Execute_TargetAlreadyExists_StillAttemptsAtomicRename_Issue9()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DatRenameRace_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var sourcePath = Path.Combine(tempDir, "wrong.nes");
            var existingTargetPath = Path.Combine(tempDir, "Super Mario Bros.nes");
            File.WriteAllText(sourcePath, "source");
            File.WriteAllText(existingTargetPath, "target");

            var options = new RunOptions
            {
                Mode = "Move",
                Roots = [tempDir],
                AuditPath = Path.Combine(tempDir, "audit.csv")
            };

            var entries = new[]
            {
                new DatAuditEntry(
                    FilePath: sourcePath,
                    Hash: "abc123",
                    Status: DatAuditStatus.HaveWrongName,
                    DatGameName: "Super Mario Bros",
                    DatRomFileName: "Super Mario Bros.nes",
                    ConsoleKey: "NES",
                    Confidence: 100)
            };

            var fs = new TrackingFileSystem
            {
                RenameResult = Path.Combine(tempDir, "Super Mario Bros__DUP1.nes")
            };
            var audit = new TrackingAuditStore();
            var context = CreateContext(options, fs, audit);

            var result = new DatRenamePipelinePhase().Execute(
                new DatRenameInput(entries, options),
                context,
                CancellationToken.None);

            Assert.Equal(1, fs.RenameCalls);
            Assert.Equal(1, result.ExecutedCount);
            Assert.Equal(0, result.SkippedCount);
            Assert.Equal(2, audit.AppendCalls);
            Assert.Contains(RunConstants.AuditActions.DatRenamePending, audit.Actions);
            Assert.Contains(RunConstants.AuditActions.DatRename, audit.Actions);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Execute_TargetCollisionWithinBatch_KeepsHigherConfidenceWinner_Issue9()
    {
        var options = new RunOptions
        {
            Mode = "Move",
            Roots = [@"C:\roms"],
            AuditPath = @"C:\audit\run.csv"
        };

        var highConfidence = new DatAuditEntry(
            FilePath: @"C:\roms\NES\winner-a.nes",
            Hash: "hash-a",
            Status: DatAuditStatus.HaveWrongName,
            DatGameName: "Shared Game",
            DatRomFileName: "shared-target.nes",
            ConsoleKey: "NES",
            Confidence: 99);

        var lowConfidence = new DatAuditEntry(
            FilePath: @"C:\roms\NES\winner-b.nes",
            Hash: "hash-b",
            Status: DatAuditStatus.HaveWrongName,
            DatGameName: "Shared Game",
            DatRomFileName: "shared-target.nes",
            ConsoleKey: "NES",
            Confidence: 10);

        var fs = new TrackingFileSystem();
        var audit = new TrackingAuditStore();
        var context = CreateContext(options, fs, audit);

        var result = new DatRenamePipelinePhase().Execute(
            new DatRenameInput([highConfidence, lowConfidence], options),
            context,
            CancellationToken.None);

        Assert.Equal(2, result.ProposedCount);
        Assert.Equal(1, result.ExecutedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Single(fs.RenameRequests);
        Assert.Equal(highConfidence.FilePath, fs.RenameRequests[0].SourcePath);
    }

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
        public List<(string SourcePath, string NewFileName)> RenameRequests { get; } = [];
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
            RenameRequests.Add((sourcePath, newFileName));
            if (!string.IsNullOrWhiteSpace(RenameResult))
                return RenameResult;

            var directory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
            return Path.Combine(directory, newFileName);
        }
    }

    private sealed class TrackingAuditStore : IAuditStore
    {
        public int AppendCalls { get; private set; }
        public List<string> Actions { get; } = [];
        public string LastAction { get; private set; } = string.Empty;

        public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata) { }
        public bool TestMetadataSidecar(string auditCsvPath) => true;
        public void Flush(string auditCsvPath) { }
        public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots, string[] allowedCurrentRoots, bool dryRun = false)
            => Array.Empty<string>();

        public void AppendAuditRow(string auditCsvPath, string rootPath, string oldPath, string newPath, string action, string category = "", string hash = "", string reason = "")
        {
            AppendCalls++;
            Actions.Add(action);
            LastAction = action;
        }
    }
}
