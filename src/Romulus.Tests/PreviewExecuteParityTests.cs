using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Classification;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

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

    [Fact]
    public void DryRunAndMoveMode_KeepConsoleSortPlanInParity_WhenMovesSucceed()
    {
        var dryRoot = Path.Combine(_tempDir, "dry-sort");
        var moveRoot = Path.Combine(_tempDir, "move-sort");
        Directory.CreateDirectory(dryRoot);
        Directory.CreateDirectory(moveRoot);

        File.WriteAllText(Path.Combine(dryRoot, "Alpha.nes"), "a");
        File.WriteAllText(Path.Combine(dryRoot, "Beta.nes"), "b");
        File.WriteAllText(Path.Combine(moveRoot, "Alpha.nes"), "a");
        File.WriteAllText(Path.Combine(moveRoot, "Beta.nes"), "b");

        var detector = BuildConsoleDetector();
        var fs = new FileSystemAdapter();

        var dry = new RunOrchestrator(fs, new NullAuditStore(), consoleDetector: detector).Execute(new RunOptions
        {
            Roots = new[] { dryRoot },
            Mode = "DryRun",
            Extensions = new[] { ".nes" },
            SortConsole = true,
            RemoveJunk = false
        });

        var execute = new RunOrchestrator(fs, new NullAuditStore(), consoleDetector: detector).Execute(new RunOptions
        {
            Roots = new[] { moveRoot },
            Mode = "Move",
            Extensions = new[] { ".nes" },
            SortConsole = true,
            RemoveJunk = false
        });

        Assert.NotNull(dry.ConsoleSortResult);
        Assert.NotNull(execute.ConsoleSortResult);
        Assert.Equal(dry.ConsoleSortResult!.Total, execute.ConsoleSortResult!.Total);
        Assert.Equal(dry.ConsoleSortResult!.Moved, execute.ConsoleSortResult!.Moved);
        Assert.Equal(dry.ConsoleSortResult.Reviewed, execute.ConsoleSortResult.Reviewed);
        Assert.Equal(dry.ConsoleSortResult.Blocked, execute.ConsoleSortResult.Blocked);
    }

    private static void SeedDataset(string root)
    {
        File.WriteAllText(Path.Combine(root, "Mega Game (US).zip"), "us");
        File.WriteAllText(Path.Combine(root, "Mega Game (EU).zip"), "eu");
        File.WriteAllText(Path.Combine(root, "Another Game (JP).zip"), "jp");
    }

    private static ConsoleDetector BuildConsoleDetector()
    {
        return new ConsoleDetector(
        [
            new ConsoleInfo("NES", "Nintendo", false, [".nes"], Array.Empty<string>(), ["NES"])
        ]);
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
