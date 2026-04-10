using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Classification;
using Romulus.Infrastructure.Sorting;
using Romulus.Tests.Benchmark;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Phase 2 — Recognition Quality: Category &amp; Sorting
/// RED-phase tests for TASK-017 through TASK-035.
/// </summary>
public class Phase2RecognitionQualityTests : IDisposable
{
    private readonly string _tempDir;

    public Phase2RecognitionQualityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Phase2Tests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private string CreateFile(string relativePath, string content = "dummy")
    {
        var full = Path.Combine(_tempDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    private ConsoleDetector BuildDetector()
    {
        var consoles = new List<ConsoleInfo>
        {
            new("NES", "Nintendo", false, new[] { ".nes" }, Array.Empty<string>(), new[] { "NES", "Nintendo Entertainment System" }),
            new("SNES", "Super Nintendo", false, new[] { ".sfc", ".smc" }, Array.Empty<string>(), new[] { "SNES", "Super Nintendo" }),
            new("GBA", "Game Boy Advance", false, new[] { ".gba" }, Array.Empty<string>(), new[] { "GBA", "Game Boy Advance" }),
        };
        return new ConsoleDetector(consoles);
    }

    // ─── TASK-020: StreamingScanPipelinePhase non-ROM extension blocklist ───

    [Theory]
    [InlineData(".txt")]
    [InlineData(".jpg")]
    [InlineData(".exe")]
    [InlineData(".html")]
    [InlineData(".url")]
    [InlineData(".nfo")]
    public void FileClassifier_NonRomExtension_Returns_NonGame(string extension)
    {
        var result = FileClassifier.Analyze("readme", extension, 1024);
        Assert.Equal(FileCategory.NonGame, result.Category);
    }

    [Fact]
    public void FileClassifier_ZeroByte_Returns_NonGame()
    {
        var result = FileClassifier.Analyze("game", ".nes", 0);
        Assert.Equal(FileCategory.NonGame, result.Category);
    }

    // ─── TASK-021: Console-Aware Junk Sorting ───

    [Fact]
    public void ConsoleSorter_Junk_WithKnownConsole_MovesToTrashJunkConsoleSubdir()
    {
        CreateFile("junkgame.nes", "junk content");
        var detector = BuildDetector();
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, detector);

        // Pass enriched sort decisions indicating Junk category
        var enrichedConsoleKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine(_tempDir, "junkgame.nes")] = "NES"
        };
        var enrichedSortDecisions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine(_tempDir, "junkgame.nes")] = "Blocked"
        };
        var enrichedCategories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine(_tempDir, "junkgame.nes")] = "Junk"
        };

        var result = sorter.Sort(
            new[] { _tempDir }, new[] { ".nes" }, dryRun: false,
            enrichedConsoleKeys: enrichedConsoleKeys,
            enrichedSortDecisions: enrichedSortDecisions,
            enrichedCategories: enrichedCategories);

        // File should be moved to _TRASH_JUNK/NES/ instead of flat _TRASH_JUNK/
        var expectedPath = Path.Combine(_tempDir, "_TRASH_JUNK", "NES", "junkgame.nes");
        Assert.True(File.Exists(expectedPath),
            $"Expected junk file at {expectedPath}");
    }

    // ─── TASK-029: ConsoleSorter with SortDecision + Review bucket ───

    [Fact]
    public void ConsoleSorter_Review_MovesToReviewBucket()
    {
        CreateFile("uncertain.nes", "review content");
        var detector = BuildDetector();
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, detector);

        var enrichedConsoleKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine(_tempDir, "uncertain.nes")] = "NES"
        };
        var enrichedSortDecisions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine(_tempDir, "uncertain.nes")] = "Review"
        };

        var result = sorter.Sort(
            new[] { _tempDir }, new[] { ".nes" }, dryRun: false,
            enrichedConsoleKeys: enrichedConsoleKeys,
            enrichedSortDecisions: enrichedSortDecisions);

        var expectedPath = Path.Combine(_tempDir, "_REVIEW", "NES", "uncertain.nes");
        Assert.True(File.Exists(expectedPath),
            $"Expected review file at {expectedPath}");
        Assert.Equal(1, result.Reviewed);
    }

    [Fact]
    public void ConsoleSorter_Blocked_MovesToBlockedReasonFolder_AndCountsBlocked()
    {
        var filePath = CreateFile("blocked.nes", "blocked content");
        var detector = BuildDetector();
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, detector);

        var enrichedConsoleKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [filePath] = "NES"
        };
        var enrichedSortDecisions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [filePath] = "Blocked"
        };
        var enrichedSortReasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [filePath] = "cross-family conflict"
        };

        var result = sorter.Sort(
            new[] { _tempDir }, new[] { ".nes" }, dryRun: false,
            enrichedConsoleKeys: enrichedConsoleKeys,
            enrichedSortDecisions: enrichedSortDecisions,
            enrichedSortReasons: enrichedSortReasons);

        var expectedPath = Path.Combine(_tempDir, "_BLOCKED", "cross-family-conflict", "blocked.nes");
        Assert.True(File.Exists(expectedPath), "Blocked file should be moved to blocked staging folder");
        Assert.Equal(1, result.Blocked);
        Assert.Equal(0, result.Moved);
    }

    [Fact]
    public void ConsoleSorter_Unknown_MovesToUnknownFolder()
    {
        var filePath = CreateFile("unknown.nes", "unknown content");
        var detector = BuildDetector();
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, detector);

        var enrichedConsoleKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [filePath] = "UNKNOWN"
        };
        var enrichedSortDecisions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [filePath] = "Unknown"
        };

        var result = sorter.Sort(
            new[] { _tempDir }, new[] { ".nes" }, dryRun: false,
            enrichedConsoleKeys: enrichedConsoleKeys,
            enrichedSortDecisions: enrichedSortDecisions);

        var expectedPath = Path.Combine(_tempDir, "_UNKNOWN", "unknown.nes");
        Assert.True(File.Exists(expectedPath), "Unknown file should be moved to unknown staging folder");
        Assert.Equal(1, result.Unknown);
    }

    [Fact]
    public void ConsoleSorter_Sort_Decision_MovesToConsoleSubdir()
    {
        CreateFile("game.nes", "sort content");
        var detector = BuildDetector();
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, detector);

        var enrichedConsoleKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine(_tempDir, "game.nes")] = "NES"
        };
        var enrichedSortDecisions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine(_tempDir, "game.nes")] = "Sort"
        };

        var result = sorter.Sort(
            new[] { _tempDir }, new[] { ".nes" }, dryRun: false,
            enrichedConsoleKeys: enrichedConsoleKeys,
            enrichedSortDecisions: enrichedSortDecisions);

        var expectedPath = Path.Combine(_tempDir, "NES", "game.nes");
        Assert.True(File.Exists(expectedPath));
        Assert.Equal(1, result.Moved);
    }

    [Fact]
    public void ConsoleSorter_DatVerified_MovesToConsoleSubdir()
    {
        CreateFile("verified.nes", "dat content");
        var detector = BuildDetector();
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, detector);

        var enrichedConsoleKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine(_tempDir, "verified.nes")] = "NES"
        };
        var enrichedSortDecisions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine(_tempDir, "verified.nes")] = "DatVerified"
        };

        var result = sorter.Sort(
            new[] { _tempDir }, new[] { ".nes" }, dryRun: false,
            enrichedConsoleKeys: enrichedConsoleKeys,
            enrichedSortDecisions: enrichedSortDecisions);

        var expectedPath = Path.Combine(_tempDir, "NES", "verified.nes");
        Assert.True(File.Exists(expectedPath));
        Assert.Equal(1, result.Moved);
    }

    [Fact]
    public void ConsoleSortResult_Has_Reviewed_And_Blocked_Counters()
    {
        var result = new ConsoleSortResult(
            Total: 10, Moved: 5, SetMembersMoved: 0, Skipped: 1,
            Unknown: 0, UnknownReasons: new Dictionary<string, int>(),
            Failed: 0, Reviewed: 3, Blocked: 1);

        Assert.Equal(3, result.Reviewed);
        Assert.Equal(1, result.Blocked);
    }

    [Fact]
    public void ConsoleSorter_ExcludedFolders_Includes_Review()
    {
        CreateFile("_REVIEW" + Path.DirectorySeparatorChar + "NES" + Path.DirectorySeparatorChar + "game.nes", "reviewed");
        var detector = BuildDetector();
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, detector);

        var result = sorter.Sort(new[] { _tempDir }, new[] { ".nes" }, dryRun: true);

        Assert.Equal(0, result.Total); // _REVIEW should be excluded
    }

    [Fact]
    public void ConsoleSorter_ExcludedFolders_Include_Blocked_And_Unknown()
    {
        CreateFile("_BLOCKED" + Path.DirectorySeparatorChar + "cross-family-conflict" + Path.DirectorySeparatorChar + "game.nes", "blocked");
        CreateFile("_UNKNOWN" + Path.DirectorySeparatorChar + "mystery.nes", "unknown");
        var detector = BuildDetector();
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, detector);

        var result = sorter.Sort(new[] { _tempDir }, new[] { ".nes" }, dryRun: true);

        Assert.Equal(0, result.Total); // _BLOCKED/_UNKNOWN should be excluded
    }

    // ─── TASK-022/030: MetricsAggregator categoryRecognitionRate + SortDecision counts ───

    [Fact]
    public void MetricsAggregator_CalculateAggregate_Includes_CategoryRecognitionRate()
    {
        var results = new List<BenchmarkSampleResult>
        {
            new("s1", BenchmarkVerdict.Correct, "NES", "NES", 95, false, null, "Game", "Game", SortDecision.Sort),
            new("s2", BenchmarkVerdict.Correct, "SNES", "SNES", 90, false, null, "Junk", "Junk", SortDecision.Blocked),
            new("s3", BenchmarkVerdict.Wrong, "GBA", "NES", 80, false, null, "Game", "Junk", SortDecision.Sort),
        };

        var aggregate = MetricsAggregator.CalculateAggregate(results);

        Assert.True(aggregate.ContainsKey("categoryRecognitionRate"),
            "Aggregate should include categoryRecognitionRate");
        // 2 out of 3 have matching expected/actual category
        Assert.Equal(2.0 / 3.0, aggregate["categoryRecognitionRate"], precision: 4);
    }

    [Fact]
    public void MetricsAggregator_CalculateAggregate_Includes_SortDecisionCounts()
    {
        var results = new List<BenchmarkSampleResult>
        {
            new("s1", BenchmarkVerdict.Correct, "NES", "NES", 95, false, null, "Game", "Game", SortDecision.Sort),
            new("s2", BenchmarkVerdict.Correct, "SNES", "SNES", 90, false, null, "Game", "Game", SortDecision.DatVerified),
            new("s3", BenchmarkVerdict.Correct, "GBA", "GBA", 70, false, null, "Game", "Game", SortDecision.Review),
            new("s4", BenchmarkVerdict.Wrong, "NES", "SNES", 40, false, null, "Game", "Game", SortDecision.Blocked),
        };

        var aggregate = MetricsAggregator.CalculateAggregate(results);

        Assert.True(aggregate.ContainsKey("sortCount"));
        Assert.True(aggregate.ContainsKey("reviewCount"));
        Assert.True(aggregate.ContainsKey("blockedCount"));
        Assert.Equal(1, aggregate["sortCount"]);
        Assert.Equal(1, aggregate["reviewCount"]);
        Assert.Equal(1, aggregate["blockedCount"]);
    }

    [Fact]
    public void MetricsAggregator_CalculateAggregate_Includes_JunkClassifiedRate()
    {
        var results = new List<BenchmarkSampleResult>
        {
            new("s1", BenchmarkVerdict.Correct, "NES", "NES", 95, false, null),
            new("s2", BenchmarkVerdict.JunkClassified, "NES", "NES", 80, false, null, "Junk", "Junk"),
            new("s3", BenchmarkVerdict.TrueNegative, null, "UNKNOWN", 0, false, null, "Junk", "Junk"),
        };

        var aggregate = MetricsAggregator.CalculateAggregate(results);

        Assert.True(aggregate.ContainsKey("junkClassifiedRate"));
        // 1 JunkClassified out of 2 Junk expected entries
        Assert.Equal(0.5, aggregate["junkClassifiedRate"], precision: 4);
    }

    // ─── TASK-031: RunProjection Review/Blocked parity ───

    [Fact]
    public void RunProjection_Includes_ConsoleSortReviewed_And_Blocked()
    {
        var runResult = new RunResult
        {
            Status = "ok",
            ExitCode = 0,
            TotalFilesScanned = 10,
            ConsoleSortResult = new ConsoleSortResult(
                Total: 10, Moved: 5, SetMembersMoved: 0, Skipped: 1,
                Unknown: 0, UnknownReasons: new Dictionary<string, int>(),
                Failed: 0, Reviewed: 3, Blocked: 1)
        };

        var projection = Romulus.Infrastructure.Orchestration.RunProjectionFactory.Create(runResult);

        Assert.Equal(3, projection.ConsoleSortReviewed);
        Assert.Equal(1, projection.ConsoleSortBlocked);
    }
}
