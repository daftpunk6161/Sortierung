using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.Orchestration;
using RomCleanup.UI.Wpf.Models;
using Xunit;

namespace RomCleanup.Tests;

public sealed class UiProjectionTests
{
    [Fact]
    public void ErrorSummaryProjection_IncludesConvertError_WhenPresent()
    {
        var result = new RunResult
        {
            ConvertErrorCount = 2,
            AllCandidates = new[]
            {
                new RomCandidate { MainPath = "a.zip", Category = FileCategory.Game, DatMatch = false }
            }
        };

        var errors = ErrorSummaryProjection.Build(
            result,
            result.AllCandidates,
            Array.Empty<LogEntry>());

        Assert.Contains(errors, e => e.Code == "CONVERT-ERR");
    }

    [Fact]
    public void ErrorSummaryProjection_ReturnsRunOk_WhenNoIssues()
    {
        var result = new RunResult
        {
            WinnerCount = 1,
            LoserCount = 0,
            AllCandidates = new[]
            {
                new RomCandidate { MainPath = "ok.zip", Category = FileCategory.Game, DatMatch = true }
            }
        };

        var errors = ErrorSummaryProjection.Build(
            result,
            result.AllCandidates,
            Array.Empty<LogEntry>());

        Assert.Contains(errors, e => e.Code == "RUN-OK");
    }

    [Fact]
    public void DashboardProjection_ConvertOnly_ShowsConvertOnlyConsequence()
    {
        var result = new RunResult
        {
            WinnerCount = 3,
            LoserCount = 2,
            TotalFilesScanned = 5,
            DurationMs = 1000,
            DedupeGroups = new[]
            {
                new DedupeResult
                {
                    GameKey = "g1",
                    Winner = new RomCandidate { MainPath = "winner.chd", Region = "EU", RegionScore = 10, FormatScore = 10, VersionScore = 10 },
                    Losers = new[]
                    {
                        new RomCandidate { MainPath = "loser.chd", Region = "US", RegionScore = 9, FormatScore = 9, VersionScore = 9 }
                    }
                }
            },
            AllCandidates = new[]
            {
                new RomCandidate { MainPath = "winner.chd", Category = FileCategory.Game, DatMatch = true, ConsoleKey = "psx" },
                new RomCandidate { MainPath = "loser.chd", Category = FileCategory.Game, DatMatch = false, ConsoleKey = "psx" }
            }
        };

        var runProjection = RunProjectionFactory.Create(result);
        var dashboard = DashboardProjection.From(runProjection, result, isConvertOnlyRun: true);

        Assert.Equal("Nur Konvertierung aktiv. Keine Dateien werden verschoben.", dashboard.MoveConsequenceText);
        Assert.Equal("–", dashboard.Winners);
        Assert.Single(dashboard.ConsoleDistribution);
        Assert.Single(dashboard.DedupeGroups);
    }

    [Fact]
    public void DashboardProjection_ShouldExposeDatAuditDisplays_WhenDatAuditCountersPresent_Issue9()
    {
        var result = new RunResult
        {
            DatHaveCount = 4,
            DatHaveWrongNameCount = 3,
            DatMissCount = 2,
            DatUnknownCount = 1,
            DatAmbiguousCount = 5,
            AllCandidates = new[]
            {
                new RomCandidate { MainPath = "a.zip", Category = FileCategory.Game, DatMatch = true }
            }
        };

        var projection = RunProjectionFactory.Create(result);
        var dashboard = DashboardProjection.From(projection, result, isConvertOnlyRun: false);

        Assert.Equal("4", dashboard.DatHaveDisplay);
        Assert.Equal("3", dashboard.DatWrongNameDisplay);
        Assert.Equal("2", dashboard.DatMissDisplay);
        Assert.Equal("1", dashboard.DatUnknownDisplay);
        Assert.Equal("5", dashboard.DatAmbiguousDisplay);
    }

    [Fact]
    public void DashboardProjection_ShouldShowDashForDatAuditDisplays_WhenAllCountersZero_Issue9()
    {
        var result = new RunResult
        {
            AllCandidates = new[]
            {
                new RomCandidate { MainPath = "a.zip", Category = FileCategory.Game, DatMatch = false }
            }
        };

        var projection = RunProjectionFactory.Create(result);
        var dashboard = DashboardProjection.From(projection, result, isConvertOnlyRun: false);

        Assert.Equal("–", dashboard.DatHaveDisplay);
        Assert.Equal("–", dashboard.DatWrongNameDisplay);
        Assert.Equal("–", dashboard.DatMissDisplay);
        Assert.Equal("–", dashboard.DatUnknownDisplay);
        Assert.Equal("–", dashboard.DatAmbiguousDisplay);
    }
}
