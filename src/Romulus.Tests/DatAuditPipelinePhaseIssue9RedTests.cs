using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Metrics;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// TDD RED (Issue9/A-07): specification tests for DatAuditPipelinePhase.
/// These tests are expected to fail until DatAuditPipelinePhase exists.
/// </summary>
public sealed class DatAuditPipelinePhaseIssue9RedTests
{
    [Fact]
    public void Execute_ShouldClassifyCandidates_AndAggregateCounts_Issue9()
    {
        // Arrange
        var datIndex = new DatIndex();
        datIndex.Add("NES", "hash-have", "Super Mario Bros.", "mario.nes");
        datIndex.Add("NES", "hash-wrong", "Contra", "Contra (World).nes");

        var candidates = new[]
        {
            new RomCandidate
            {
                MainPath = @"C:\\roms\\NES\\mario.nes",
                ConsoleKey = "NES",
                Hash = "hash-have"
            },
            new RomCandidate
            {
                MainPath = @"C:\\roms\\NES\\contra-wrong.nes",
                ConsoleKey = "NES",
                Hash = "hash-wrong"
            },
            new RomCandidate
            {
                MainPath = @"C:\\roms\\NES\\unknown.nes",
                ConsoleKey = "NES",
                Hash = "hash-miss"
            }
        };

        var options = new RunOptions { Mode = "DryRun" };
        var context = CreateContext(options);

        // Act
        var result = new DatAuditPipelinePhase().Execute(
            new DatAuditInput(candidates, datIndex, options),
            context,
            CancellationToken.None);

        // Assert
        Assert.Equal(1, result.HaveCount);
        Assert.Equal(1, result.HaveWrongNameCount);
        Assert.Equal(1, result.MissCount);
        Assert.Equal(0, result.UnknownCount);
        Assert.Equal(0, result.AmbiguousCount);

        Assert.Equal(3, result.Entries.Count);
        Assert.Equal(DatAuditStatus.Have, result.Entries[0].Status);
        Assert.Equal(DatAuditStatus.HaveWrongName, result.Entries[1].Status);
        Assert.Equal(DatAuditStatus.Miss, result.Entries[2].Status);
    }

    private static PipelineContext CreateContext(RunOptions options)
    {
        var metrics = new PhaseMetricsCollector();
        metrics.Initialize();

        return new PipelineContext
        {
            Options = options,
            FileSystem = new NoOpFileSystem(),
            AuditStore = new NoOpAuditStore(),
            Metrics = metrics,
            OnProgress = _ => { }
        };
    }

    private sealed class NoOpFileSystem : IFileSystem
    {
        public bool TestPath(string literalPath, string pathType = "Any") => true;
        public string EnsureDirectory(string path) => path;
        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? allowedExtensions = null) => Array.Empty<string>();
        public string? MoveItemSafely(string sourcePath, string destinationPath) => destinationPath;
        public bool MoveDirectorySafely(string sourcePath, string destinationPath) => true;
        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath) => Path.Combine(rootPath, relativePath);
        public bool IsReparsePoint(string path) => false;
        public void DeleteFile(string path) { }
        public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false) { }
        public string? RenameItemSafely(string sourcePath, string newFileName) => Path.Combine(Path.GetDirectoryName(sourcePath) ?? string.Empty, newFileName);
    }

    private sealed class NoOpAuditStore : IAuditStore
    {
        public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata) { }
        public bool TestMetadataSidecar(string auditCsvPath) => true;
        public void Flush(string auditCsvPath) { }
        public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots, string[] allowedCurrentRoots, bool dryRun = false)
            => Array.Empty<string>();
        public void AppendAuditRow(string auditCsvPath, string rootPath, string oldPath, string newPath, string action, string category = "", string hash = "", string reason = "") { }
    }
}
