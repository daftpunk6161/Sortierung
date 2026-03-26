using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Core.Deduplication;
using RomCleanup.Infrastructure.Analytics;
using RomCleanup.Infrastructure.Orchestration;
using Xunit;

namespace RomCleanup.Tests;

public sealed class InsightsEngineTests
{
    // =========================================================================
    //  GetCollectionHealthRows Tests
    // =========================================================================

    [Fact]
    public void GetCollectionHealthRows_EmptyResult_ReturnsEmpty()
    {
        var result = new RunResult();
        var rows = InsightsEngine.GetCollectionHealthRows(result);
        Assert.Empty(rows);
    }

    [Fact]
    public void GetCollectionHealthRows_GroupsByType()
    {
        var result = new RunResult
        {
            AllCandidates =
            [
                new RomCandidate { MainPath = "a.zip", ConsoleKey = "NES", Extension = ".zip" },
                new RomCandidate { MainPath = "b.zip", ConsoleKey = "NES", Extension = ".zip" },
                new RomCandidate { MainPath = "c.iso", ConsoleKey = "PSX", Extension = ".iso" }
            ],
            DedupeGroups = []
        };

        var rows = InsightsEngine.GetCollectionHealthRows(result);
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.Console == "NES" && r.Roms == 2);
        Assert.Contains(rows, r => r.Console == "PSX" && r.Roms == 1);
    }

    [Fact]
    public void GetCollectionHealthRows_FilterText_Filters()
    {
        var result = new RunResult
        {
            AllCandidates =
            [
                new RomCandidate { MainPath = "a.zip", ConsoleKey = "NES", Extension = ".zip" },
                new RomCandidate { MainPath = "c.iso", ConsoleKey = "PSX", Extension = ".iso" }
            ],
            DedupeGroups = []
        };

        var rows = InsightsEngine.GetCollectionHealthRows(result, "NES");
        Assert.Single(rows);
        Assert.Equal("NES", rows[0].Console);
    }

    // =========================================================================
    //  GetDatCoverageHeatmap Tests
    // =========================================================================

    [Fact]
    public void GetDatCoverageHeatmap_EmptyResult_ReturnsEmpty()
    {
        var result = new RunResult();
        var rows = InsightsEngine.GetDatCoverageHeatmap(result);
        Assert.Empty(rows);
    }

    [Fact]
    public void GetDatCoverageHeatmap_CalculatesCoverage()
    {
        var result = new RunResult
        {
            AllCandidates =
            [
                new RomCandidate { MainPath = "a.zip", ConsoleKey = "NES", DatMatch = true },
                new RomCandidate { MainPath = "b.zip", ConsoleKey = "NES", DatMatch = false },
                new RomCandidate { MainPath = "c.zip", ConsoleKey = "NES", DatMatch = true }
            ],
            DedupeGroups = []
        };

        var rows = InsightsEngine.GetDatCoverageHeatmap(result);
        Assert.Single(rows);

        var nes = rows[0];
        Assert.Equal("NES", nes.Console);
        Assert.Equal(2, nes.Matched);
        Assert.Equal(3, nes.Expected);
        Assert.Equal(1, nes.Missing);
        // Coverage ~66.7%
        Assert.True(nes.Coverage > 60 && nes.Coverage < 70);
        // Heat bar present
        Assert.False(string.IsNullOrEmpty(nes.Heat));
    }

    [Fact]
    public void GetDatCoverageHeatmap_RespectsTopLimit()
    {
        var candidates = new List<RomCandidate>();
        for (int i = 0; i < 20; i++)
        {
            candidates.Add(new RomCandidate
            {
                MainPath = $"game{i}.zip",
                ConsoleKey = $"Console{i}",
                DatMatch = i % 2 == 0
            });
        }

        var result = new RunResult { AllCandidates = candidates, DedupeGroups = [] };
        var rows = InsightsEngine.GetDatCoverageHeatmap(result, top: 5);
        Assert.Equal(5, rows.Count);
        // Each console has exactly 1 candidate, so Expected == 1 for all
        Assert.All(rows, r => Assert.Equal(1, r.Expected));
        // Coverage should be either 100% (DatMatch=true) or 0% (DatMatch=false)
        Assert.All(rows, r => Assert.True(r.Coverage == 100.0 || r.Coverage == 0.0,
            $"Expected coverage 100 or 0, got {r.Coverage} for {r.Console}"));
    }

    // =========================================================================
    //  GetCrossCollectionHints Tests
    // =========================================================================

    [Fact]
    public void GetCrossCollectionHints_SingleRoot_NoHints()
    {
        var fs = new StubFs([@"D:\roms\Game (USA).zip", @"D:\roms\Game (Europe).zip"]);
        var engine = new InsightsEngine(fs);
        var hints = engine.GetCrossCollectionHints(
            roots: [@"D:\roms"],
            extensions: [".zip"]);

        // Same root → no cross-collection hints
        Assert.Empty(hints);
    }

    [Fact]
    public void GetCrossCollectionHints_EmptyRoots_ReturnsEmpty()
    {
        var fs = new StubFs();
        var engine = new InsightsEngine(fs);
        var hints = engine.GetCrossCollectionHints([], [".zip"]);
        Assert.Empty(hints);
    }

    [Fact]
    public void GetDuplicateInspectorRows_WinnerSelection_DoesNotDriftFromCoreEngine()
    {
        var dir = Path.Combine(Path.GetTempPath(), "insights_drift_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var files = new[]
            {
                Path.Combine(dir, "Mega Game (Europe) (Rev 1).zip"),
                Path.Combine(dir, "Mega Game (USA) (Rev 2).chd"),
                Path.Combine(dir, "Mega Game (Japan).iso"),
                Path.Combine(dir, "Puzzle Quest (USA).zip"),
                Path.Combine(dir, "Puzzle Quest (Europe).zip")
            };

            foreach (var file in files)
                File.WriteAllText(file, "test");

            var fs = new StubFs(files);
            var engine = new InsightsEngine(fs);

            var rows = engine.GetDuplicateInspectorRows(
                roots: [dir],
                extensions: [".zip", ".chd", ".iso"],
                preferRegions: ["EU", "US", "JP"]);

            Assert.NotEmpty(rows);

            var grouped = rows.GroupBy(r => r.GameKey, StringComparer.OrdinalIgnoreCase).ToList();
            Assert.True(grouped.Count >= 2);

            foreach (var group in grouped)
            {
                var insightWinner = group.Single(r => r.Winner).MainPath;

                var candidates = group.Select(r => new RomCandidate
                {
                    MainPath = r.MainPath,
                    GameKey = r.GameKey,
                    RegionScore = r.RegionScore,
                    FormatScore = r.FormatScore,
                    VersionScore = r.VersionScore,
                    SizeTieBreakScore = 0,
                    Category = FileCategory.Game
                }).ToList();

                var coreWinner = DeduplicationEngine.SelectWinner(candidates);
                Assert.NotNull(coreWinner);
                Assert.Equal(coreWinner!.MainPath, insightWinner);
            }
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    // =========================================================================
    //  ExportInspectorCsv Tests
    // =========================================================================

    [Fact]
    public void ExportInspectorCsv_WritesCsvWithHeader()
    {
        var dir = Path.Combine(Path.GetTempPath(), "insights_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var rows = new List<DuplicateInspectorRow>
            {
                new()
                {
                    GameKey = "Mario",
                    Winner = true,
                    WinnerSource = "AUTO",
                    Region = "EU",
                    Type = ".zip",
                    SizeMB = 1.5,
                    RegionScore = 1000,
                    FormatScore = 500,
                    VersionScore = 0,
                    TotalScore = 1500,
                    ScoreBreakdown = "R:1000 F:500 V:0",
                    MainPath = @"D:\roms\Mario (Europe).zip"
                }
            };

            var csvPath = Path.Combine(dir, "inspector.csv");
            InsightsEngine.ExportInspectorCsv(rows, csvPath);

            Assert.True(File.Exists(csvPath));
            var lines = File.ReadAllLines(csvPath);
            Assert.Equal(2, lines.Length); // exactly 1 header + 1 data row
            Assert.Contains("GameKey", lines[0]);
            Assert.Contains("Winner", lines[0]);
            Assert.Contains("Region", lines[0]);
            Assert.Contains("TotalScore", lines[0]);
            Assert.Contains("Mario", lines[1]);
            Assert.Contains("1500", lines[1]);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // Fakes
    private sealed class StubFs : IFileSystem
    {
        private readonly IReadOnlyList<string> _files;
        public StubFs(IReadOnlyList<string>? files = null) => _files = files ?? [];
        public bool TestPath(string literalPath, string pathType = "Any") => true;
        public string EnsureDirectory(string path) => path;
        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? extensions = null) => _files;
        public string? MoveItemSafely(string src, string dest) => dest;
        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath)
            => Path.Combine(rootPath, relativePath);
        public bool IsReparsePoint(string path) => false;
        public void DeleteFile(string path) { }
        public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false) { }
    }
}
