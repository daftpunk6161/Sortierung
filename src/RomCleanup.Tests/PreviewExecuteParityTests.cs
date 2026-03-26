using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.FileSystem;
using RomCleanup.Infrastructure.Orchestration;
using Xunit;

namespace RomCleanup.Tests;

public sealed class PreviewExecuteParityTests : IDisposable
{
    private readonly string _tempDir;

    public PreviewExecuteParityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Parity_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void DryRunAndMoveMode_KeepCoreDedupeCountsInParity()
    {
        var dryRoot = Path.Combine(_tempDir, "dry");
        var moveRoot = Path.Combine(_tempDir, "move");
        Directory.CreateDirectory(dryRoot);
        Directory.CreateDirectory(moveRoot);

        SeedDataset(dryRoot);
        SeedDataset(moveRoot);

        var fs = new FileSystemAdapter();

        var dry = new RunOrchestrator(fs, new NullAuditStore()).Execute(new RunOptions
        {
            Roots = new[] { dryRoot },
            Mode = "DryRun",
            Extensions = new[] { ".zip" },
            PreferRegions = new[] { "US", "EU", "JP", "WORLD" }
        });

        var execute = new RunOrchestrator(fs, new NullAuditStore()).Execute(new RunOptions
        {
            Roots = new[] { moveRoot },
            Mode = "Move",
            Extensions = new[] { ".zip" },
            PreferRegions = new[] { "US", "EU", "JP", "WORLD" }
        });

        Assert.Equal(dry.TotalFilesScanned, execute.TotalFilesScanned);
        Assert.Equal(dry.GroupCount, execute.GroupCount);
        Assert.Equal(dry.WinnerCount, execute.WinnerCount);
        Assert.Equal(dry.LoserCount, execute.LoserCount);

        var dryWinners = dry.DedupeGroups
            .Select(g => Path.GetFileName(g.Winner.MainPath))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var executeWinners = execute.DedupeGroups
            .Select(g => Path.GetFileName(g.Winner.MainPath))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.Equal(dryWinners, executeWinners);

        var dryLosers = dry.DedupeGroups
            .SelectMany(g => g.Losers)
            .Select(c => Path.GetFileName(c.MainPath))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var executeLosers = execute.DedupeGroups
            .SelectMany(g => g.Losers)
            .Select(c => Path.GetFileName(c.MainPath))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.Equal(dryLosers, executeLosers);

        Assert.NotNull(execute.MoveResult);
        Assert.Equal(dry.LoserCount, execute.MoveResult!.MoveCount + execute.MoveResult.SkipCount + execute.MoveResult.FailCount);
    }

    [Fact]
    public void DryRunAndMoveMode_KeepUnknownClassificationParity()
    {
        var dryRoot = Path.Combine(_tempDir, "dry-unknown");
        var moveRoot = Path.Combine(_tempDir, "move-unknown");
        Directory.CreateDirectory(dryRoot);
        Directory.CreateDirectory(moveRoot);

        File.WriteAllText(Path.Combine(dryRoot, "Mystery [h].zip"), "x");
        File.WriteAllText(Path.Combine(moveRoot, "Mystery [h].zip"), "x");

        var fs = new FileSystemAdapter();
        var dry = new RunOrchestrator(fs, new NullAuditStore()).Execute(new RunOptions
        {
            Roots = new[] { dryRoot },
            Mode = "DryRun",
            Extensions = new[] { ".zip" },
            RemoveJunk = false
        });

        var execute = new RunOrchestrator(fs, new NullAuditStore()).Execute(new RunOptions
        {
            Roots = new[] { moveRoot },
            Mode = "Move",
            Extensions = new[] { ".zip" },
            RemoveJunk = false
        });

        Assert.Equal(dry.UnknownCount, execute.UnknownCount);
        Assert.Equal(dry.UnknownReasonCounts.Count, execute.UnknownReasonCounts.Count);

        foreach (var reason in dry.UnknownReasonCounts.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            Assert.True(execute.UnknownReasonCounts.ContainsKey(reason.Key));
            Assert.Equal(reason.Value, execute.UnknownReasonCounts[reason.Key]);
        }
    }

    private static void SeedDataset(string root)
    {
        File.WriteAllText(Path.Combine(root, "Mega Game (US).zip"), "us");
        File.WriteAllText(Path.Combine(root, "Mega Game (EU).zip"), "eu");
        File.WriteAllText(Path.Combine(root, "Another Game (JP).zip"), "jp");
    }

    private sealed class NullAuditStore : IAuditStore
    {
        public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata)
        {
        }

        public bool TestMetadataSidecar(string auditCsvPath) => true;

        public void Flush(string auditCsvPath)
        {
        }

        public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots, string[] allowedCurrentRoots, bool dryRun = false)
            => Array.Empty<string>();

        public void AppendAuditRow(string auditCsvPath, string rootPath, string oldPath, string newPath, string action, string category = "", string hash = "", string reason = "")
        {
        }
    }
}
