using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
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
                new RomCandidate { MainPath = "a.zip", Type = "NES", Extension = ".zip" },
                new RomCandidate { MainPath = "b.zip", Type = "NES", Extension = ".zip" },
                new RomCandidate { MainPath = "c.iso", Type = "PSX", Extension = ".iso" }
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
                new RomCandidate { MainPath = "a.zip", Type = "NES", Extension = ".zip" },
                new RomCandidate { MainPath = "c.iso", Type = "PSX", Extension = ".iso" }
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
                new RomCandidate { MainPath = "a.zip", Type = "NES", DatMatch = true },
                new RomCandidate { MainPath = "b.zip", Type = "NES", DatMatch = false },
                new RomCandidate { MainPath = "c.zip", Type = "NES", DatMatch = true }
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
                Type = $"Console{i}",
                DatMatch = i % 2 == 0
            });
        }

        var result = new RunResult { AllCandidates = candidates, DedupeGroups = [] };
        var rows = InsightsEngine.GetDatCoverageHeatmap(result, top: 5);
        Assert.True(rows.Count <= 5);
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
            Assert.True(lines.Length >= 2); // header + 1 row
            Assert.Contains("GameKey", lines[0]);
            Assert.Contains("Mario", lines[1]);
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
        public bool MoveItemSafely(string src, string dest) => true;
        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath)
            => Path.Combine(rootPath, relativePath);
    }
}
