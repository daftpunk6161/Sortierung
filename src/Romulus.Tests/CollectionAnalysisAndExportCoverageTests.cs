using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Analysis;
using Xunit;

namespace Romulus.Tests;

public sealed class CollectionAnalysisAndExportCoverageTests : IDisposable
{
    private readonly string _tempDir;

    public CollectionAnalysisAndExportCoverageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CollectionAnalysisExport_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void ExportCollectionCsv_SanitizesSpreadsheetFields_AndUsesCustomLabels()
    {
        var candidate = Candidate(
            Path.Combine(_tempDir, "SNES", "=cmd.zip"),
            consoleKey: "-console",
            region: "+EU",
            extension: ".zip",
            sizeBytes: 1_572_864,
            datMatch: true);

        var csv = CollectionExportService.ExportCollectionCsv(
            [candidate],
            delimiter: ';',
            labels: CollectionTabularExportLabels.German);

        Assert.StartsWith("Dateiname;Konsole;Region;Format;Groesse_MB;Kategorie;DAT_Status;Pfad", csv, StringComparison.Ordinal);
        Assert.Contains("'=cmd.zip", csv, StringComparison.Ordinal);
        Assert.Contains("'-console", csv, StringComparison.Ordinal);
        Assert.Contains("'+EU", csv, StringComparison.Ordinal);
        Assert.Contains(";1.50;GAME;Verified;", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportExcelXml_EscapesXmlValues_AndKeepsSizeNumeric()
    {
        var candidate = Candidate(
            Path.Combine(_tempDir, "NES", "A&B.zip"),
            consoleKey: "NES&SNES",
            region: "EU <West>",
            extension: ".zip",
            sizeBytes: 2 * 1024 * 1024,
            category: FileCategory.Bios);

        var xml = CollectionExportService.ExportExcelXml([candidate]);

        Assert.Contains("A&amp;B.zip", xml, StringComparison.Ordinal);
        Assert.Contains("NES&amp;SNES", xml, StringComparison.Ordinal);
        Assert.Contains("EU &lt;West&gt;", xml, StringComparison.Ordinal);
        Assert.Contains("<Data ss:Type=\"Number\">2.00</Data>", xml, StringComparison.Ordinal);
        Assert.Contains("<Data ss:Type=\"String\">BIOS</Data>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildJunkReport_GroupsReasons_LimitsListing_AndIgnoresNonJunkCandidates()
    {
        var junk = Enumerable.Range(0, 12)
            .Select(i => Candidate(
                Path.Combine(_tempDir, "SNES", $"Game {i:00} (Demo).zip"),
                gameKey: $"game-{i:00}",
                category: FileCategory.Junk))
            .ToArray();
        var clean = Candidate(Path.Combine(_tempDir, "SNES", "Clean Game.zip"), gameKey: "clean");

        var report = CollectionExportService.BuildJunkReport([.. junk, clean], aggressive: false);

        Assert.Contains("-- junk-tag (12 files) --", report, StringComparison.Ordinal);
        Assert.Contains("Reason: junk-tag [standard]", report, StringComparison.Ordinal);
        Assert.Contains("... and 2 more", report, StringComparison.Ordinal);
        Assert.Contains("Total: 12 junk files", report, StringComparison.Ordinal);
        Assert.DoesNotContain("Clean Game.zip", report, StringComparison.Ordinal);
    }

    [Fact]
    public void GetJunkReason_ReturnsNullForGame_AndAggressiveLevelForAggressiveMatch()
    {
        Assert.Null(CollectionExportService.GetJunkReason("Super Mario World (Europe)", aggressive: false));
        Assert.Null(CollectionExportService.GetJunkReason("wip game build", aggressive: false));

        var reason = CollectionExportService.GetJunkReason("wip game build", aggressive: true);

        Assert.NotNull(reason);
        Assert.Equal("junk-aggressive-word", reason.Tag);
        Assert.Equal("aggressive", reason.Level);
    }

    [Fact]
    public void BuildReportData_UsesCanonicalRunReportProjection_AndPreservesRunCounters()
    {
        var winner = Candidate(Path.Combine(_tempDir, "SNES", "Game (USA).sfc"), gameKey: "game", datMatch: true);
        var loser = Candidate(Path.Combine(_tempDir, "SNES", "Game (Europe).sfc"), gameKey: "game", region: "EU");
        var groups = new[]
        {
            new DedupeGroup
            {
                GameKey = "game",
                Winner = winner,
                Losers = [loser]
            }
        };
        var runResult = new RunResult
        {
            Status = RunConstants.StatusOk,
            ConvertedCount = 2,
            ConvertSkippedCount = 1,
            DurationMs = 1234,
            CompletedUtc = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc)
        };

        var (summary, entries) = CollectionExportService.BuildReportData([winner, loser], groups, runResult, dryRun: true);

        Assert.Equal(RunConstants.ModeDryRun, summary.Mode);
        Assert.Equal(2, summary.TotalFiles);
        Assert.Equal(1, summary.GroupCount);
        Assert.Equal(2, summary.ConvertedCount);
        Assert.Equal(1, summary.ConvertSkippedCount);
        Assert.Equal(TimeSpan.FromMilliseconds(1234), summary.Duration);
        Assert.Contains(entries, e => e.FilePath == winner.MainPath && e.Action == "KEEP");
        Assert.Contains(entries, e => e.FilePath == loser.MainPath && e.Action == "DUPE");
    }

    [Fact]
    public void BuildPreviewProjectionSource_RejectsNullInputs()
    {
        var groups = Array.Empty<DedupeGroup>();

        Assert.Throws<ArgumentNullException>(() => CollectionExportService.BuildPreviewProjectionSource(null!, groups));
        Assert.Throws<ArgumentNullException>(() => CollectionExportService.BuildPreviewProjectionSource([], null!));
    }

    [Fact]
    public void BuildPreviewProjectionSource_CreatesMinimalCanonicalRunResult()
    {
        var candidate = Candidate(Path.Combine(_tempDir, "SNES", "Preview Game.sfc"), gameKey: "preview");
        var group = new DedupeGroup { GameKey = "preview", Winner = candidate };

        var result = CollectionExportService.BuildPreviewProjectionSource([candidate], [group]);

        Assert.Equal(RunConstants.StatusOk, result.Status);
        Assert.Equal(1, result.TotalFilesScanned);
        Assert.Equal(1, result.GroupCount);
        Assert.Same(candidate, Assert.Single(result.AllCandidates));
        Assert.Same(group, Assert.Single(result.DedupeGroups));
    }

    [Fact]
    public void GetDuplicateHeatmap_ResolvesConsoleLabels_AndSortsByDuplicateCount()
    {
        var snesWinner = Candidate(Path.Combine(_tempDir, "SNES", "Game A (USA).sfc"), gameKey: "a", consoleKey: "UNKNOWN");
        var nesWinner = Candidate(Path.Combine(_tempDir, "NES", "Game B (USA).nes"), gameKey: "b", consoleKey: "NES", extension: ".nes");
        var groups = new[]
        {
            new DedupeGroup
            {
                GameKey = "a",
                Winner = snesWinner,
                Losers =
                [
                    Candidate(Path.Combine(_tempDir, "SNES", "Game A (Europe).sfc"), gameKey: "a"),
                    Candidate(Path.Combine(_tempDir, "SNES", "Game A (Japan).sfc"), gameKey: "a")
                ]
            },
            new DedupeGroup
            {
                GameKey = "b",
                Winner = nesWinner,
                Losers = [Candidate(Path.Combine(_tempDir, "NES", "Game B (Europe).nes"), gameKey: "b", consoleKey: "NES", extension: ".nes")]
            }
        };

        var heatmap = CollectionAnalysisService.GetDuplicateHeatmap(groups);

        Assert.Equal("SNES", heatmap[0].Console);
        Assert.Equal(3, heatmap[0].Total);
        Assert.Equal(2, heatmap[0].Duplicates);
        Assert.Equal(100.0 * 2 / 3, heatmap[0].DuplicatePercent, precision: 8);
        Assert.Equal("NES", heatmap[1].Console);
    }

    [Fact]
    public void GetDuplicateInspector_ReadsMoveAndDryRunRows_SortsAndLimits()
    {
        var dirA = Path.Combine(_tempDir, "source-a");
        var dirB = Path.Combine(_tempDir, "source-b");
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirB);
        var auditPath = Path.Combine(_tempDir, "audit.csv");
        File.WriteAllLines(auditPath,
        [
            "Root,OldPath,NewPath,Action,Category",
            $"{_tempDir},{Path.Combine(dirA, "one.zip")},,MOVE,GAME",
            $"{_tempDir},{Path.Combine(dirA, "two.zip")},,move,GAME",
            $"{_tempDir},{Path.Combine(dirB, "three.zip")},,SKIP_DRYRUN,GAME",
            $"{_tempDir},{Path.Combine(dirB, "ignored.zip")},,KEEP,GAME",
            "too,short,row"
        ]);

        var entries = CollectionAnalysisService.GetDuplicateInspector(auditPath);

        Assert.Equal(2, entries.Count);
        Assert.Equal(new DuplicateSourceEntry(dirA, 2), entries[0]);
        Assert.Equal(new DuplicateSourceEntry(dirB, 1), entries[1]);
        Assert.Empty(CollectionAnalysisService.GetDuplicateInspector(Path.Combine(_tempDir, "missing.csv")));
    }

    [Fact]
    public void SearchRomCollection_MatchesPathGameKeyRegionCategoryAndExtension()
    {
        var candidates = new[]
        {
            Candidate(Path.Combine(_tempDir, "SNES", "Alpha.sfc"), gameKey: "alpha", region: "USA", extension: ".sfc"),
            Candidate(Path.Combine(_tempDir, "SNES", "Beta.bin"), gameKey: "beta", region: "EU", extension: ".bin", category: FileCategory.Bios),
            Candidate(Path.Combine(_tempDir, "NES", "Gamma.nes"), gameKey: "gamma", region: "JP", extension: ".nes")
        };

        Assert.Equal(3, CollectionAnalysisService.SearchRomCollection(candidates, " ").Count);
        Assert.Equal("beta", Assert.Single(CollectionAnalysisService.SearchRomCollection(candidates, "BIOS")).GameKey);
        Assert.Equal("alpha", Assert.Single(CollectionAnalysisService.SearchRomCollection(candidates, ".sfc")).GameKey);
        Assert.Equal("gamma", Assert.Single(CollectionAnalysisService.SearchRomCollection(candidates, "JP")).GameKey);
    }

    [Fact]
    public void AnalyzeStorageTiers_UsesExistingFilesOnly_AndFixedTimeProvider()
    {
        var now = new DateTimeOffset(2026, 5, 16, 12, 0, 0, TimeSpan.Zero);
        var hotPath = Path.Combine(_tempDir, "hot.sfc");
        var coldPath = Path.Combine(_tempDir, "cold.sfc");
        File.WriteAllText(hotPath, "hot");
        File.WriteAllText(coldPath, "cold");
        File.SetLastAccessTime(hotPath, now.AddDays(-5).DateTime);
        File.SetLastAccessTime(coldPath, now.AddDays(-40).DateTime);

        var report = CollectionAnalysisService.AnalyzeStorageTiers(
            [
                Candidate(hotPath, sizeBytes: 1 * 1024 * 1024),
                Candidate(coldPath, sizeBytes: 2 * 1024 * 1024),
                Candidate(Path.Combine(_tempDir, "missing.sfc"), sizeBytes: 4 * 1024 * 1024)
            ],
            hotThresholdDays: 30,
            timeProvider: new FixedUtcTimeProvider(now));

        Assert.Contains("Hot (<=30d): 1 files, 1.00 MB", report, StringComparison.Ordinal);
        Assert.Contains("Cold (>30d): 1 files, 2.00 MB", report, StringComparison.Ordinal);
        Assert.Contains("2.00 MB SSD space freed", report, StringComparison.Ordinal);
    }

    [Fact]
    public void TextPreviews_AreDeterministicAndBounded()
    {
        var groups = Enumerable.Range(0, 51)
            .Select(i => new DedupeGroup
            {
                GameKey = $"game-{i:00}",
                Winner = Candidate(Path.Combine(_tempDir, "SNES", $"Game {i:00} (USA).sfc"), gameKey: $"game-{i:00}", sizeBytes: 1024 * 1024),
                Losers = [Candidate(Path.Combine(_tempDir, "SNES", $"Game {i:00} (Europe).sfc"), gameKey: $"game-{i:00}", sizeBytes: 2 * 1024 * 1024)]
            })
            .ToArray();
        var candidates = new[]
        {
            Candidate(Path.Combine(_tempDir, "NES", "Alpha.nes"), consoleKey: "UNKNOWN", region: "USA", extension: ".nes", sizeBytes: 1024),
            Candidate(Path.Combine(_tempDir, "SNES", "Beta.sfc"), consoleKey: "SNES", region: "EU", sizeBytes: 2048),
            Candidate(Path.Combine(_tempDir, "SNES", "Gamma.sfc"), consoleKey: "SNES", region: "EU", sizeBytes: 4096)
        };

        var cloneTree = CollectionAnalysisService.BuildCloneTree(groups);
        var hardlinkEstimate = CollectionAnalysisService.GetHardlinkEstimate(groups[..1]);
        var virtualFolders = CollectionAnalysisService.BuildVirtualFolderPreview(candidates);
        var nasInfo = CollectionAnalysisService.GetNasInfo([_tempDir]);
        var uncNasInfo = CollectionAnalysisService.GetNasInfo([@"\\server\share"]);

        Assert.Contains("... und 1 weitere Gruppen", cloneTree, StringComparison.Ordinal);
        Assert.Contains("Hardlink-Modus: 1 Links", hardlinkEstimate, StringComparison.Ordinal);
        Assert.Contains("2.00 MB Speicher sparbar", hardlinkEstimate, StringComparison.Ordinal);
        Assert.True(virtualFolders.IndexOf("[NES]", StringComparison.Ordinal) < virtualFolders.IndexOf("[SNES]", StringComparison.Ordinal));
        Assert.Contains("[EU] 2 files", virtualFolders, StringComparison.Ordinal);
        Assert.Contains("Type: Local drive", nasInfo, StringComparison.Ordinal);
        Assert.Contains("Network path: No", nasInfo, StringComparison.Ordinal);
        Assert.Contains("Type: UNC network path", uncNasInfo, StringComparison.Ordinal);
        Assert.Contains("Network path: Yes", uncNasInfo, StringComparison.Ordinal);
        Assert.Contains("Reduce batch size", uncNasInfo, StringComparison.Ordinal);
        Assert.Contains("WARNING: Path not reachable!", uncNasInfo, StringComparison.Ordinal);
    }

    [Fact]
    public void CategoryAndConsoleHelpers_CoverUnknownAndNullBranches()
    {
        Assert.Equal("JUNK", CollectionAnalysisService.ToCategoryLabel(FileCategory.Junk));
        Assert.Equal("UNKNOWN", CollectionAnalysisService.ToCategoryLabel(FileCategory.Unknown));
        Assert.Equal("Unknown", CollectionAnalysisService.DetectConsoleFromPath("loosefile.sfc"));
        Assert.Throws<ArgumentNullException>(() => CollectionAnalysisService.ResolveConsoleLabel(null!));
    }

    [Theory]
    [InlineData(null, "C:/roms/NES/game.nes", "NES")]
    [InlineData("", "C:/roms/NES/game.nes", "NES")]
    [InlineData("UNKNOWN", "C:/roms/NES/game.nes", "NES")]
    [InlineData("AMBIGUOUS", "C:/roms/NES/game.nes", "NES")]
    [InlineData("snes", "C:/roms/SNES/game.sfc", "SNES")]
    [InlineData("MegaDrive", "C:/roms/MD/game.md", "MegaDrive")]
    public void ResolveConsoleLabel_UsesPathFallbackOnlyForUnresolvedKeys(string? consoleKey, string path, string expected)
    {
        Assert.Equal(expected, CollectionAnalysisService.ResolveConsoleLabel(consoleKey, path));
    }

    private static RomCandidate Candidate(
        string path,
        string gameKey = "game",
        string consoleKey = "SNES",
        string region = "USA",
        string extension = ".sfc",
        long sizeBytes = 1_048_576,
        FileCategory category = FileCategory.Game,
        bool datMatch = false)
        => new()
        {
            MainPath = path,
            GameKey = gameKey,
            ConsoleKey = consoleKey,
            Region = region,
            Extension = extension,
            SizeBytes = sizeBytes,
            Category = category,
            DatMatch = datMatch,
            SortDecision = SortDecision.Sort,
            DecisionClass = DecisionClass.DatVerified
        };

    private sealed class FixedUtcTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedUtcTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
