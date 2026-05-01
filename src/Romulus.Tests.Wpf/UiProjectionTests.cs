using Romulus.Contracts.Models;
using Romulus.Infrastructure.Orchestration;
using Romulus.UI.Wpf.Models;
using Xunit;

namespace Romulus.Tests;

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
                new DedupeGroup
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
        Assert.Equal("Entfällt", dashboard.Winners);
        Assert.Equal("Entfällt", dashboard.Dupes);
        Assert.Equal("Entfällt", dashboard.Junk);
        Assert.Equal("Entfällt", dashboard.Games);
        Assert.Equal("Entfällt", dashboard.HealthScore);
        Assert.Equal("Entfällt", dashboard.DedupeRate);
        Assert.Single(dashboard.ConsoleDistribution);
        Assert.Single(dashboard.DedupeGroups);
    }

    [Fact]
    public void DashboardProjection_ConvertOnly_UsesConversionReportSourcesForConsoleDistribution()
    {
        var result = new RunResult
        {
            TotalFilesScanned = 3,
            DurationMs = 1000,
            AllCandidates = new[]
            {
                new RomCandidate { MainPath = "a.iso", Category = FileCategory.Game, ConsoleKey = "PS2" },
                new RomCandidate { MainPath = "b.iso", Category = FileCategory.Game, ConsoleKey = "UNKNOWN" },
                new RomCandidate { MainPath = "c.iso", Category = FileCategory.Game, ConsoleKey = "UNKNOWN" }
            },
            ConversionReport = new ConversionReport
            {
                TotalPlanned = 2,
                Converted = 1,
                Skipped = 0,
                Errors = 0,
                Blocked = 1,
                RequiresReview = 0,
                TotalSavedBytes = 0,
                Results = new[]
                {
                    new ConversionResult("a.iso", "a.chd", ConversionOutcome.Success),
                    new ConversionResult("b.iso", null, ConversionOutcome.Blocked, "unknown-console")
                }
            }
        };

        var projection = RunProjectionFactory.Create(result);
        var dashboard = DashboardProjection.From(projection, result, isConvertOnlyRun: true);

        Assert.Equal(2, dashboard.ConsoleDistribution.Sum(item => item.FileCount));
        var unknown = Assert.Single(dashboard.ConsoleDistribution, item => item.ConsoleKey == "UNKNOWN");
        Assert.Equal(1, unknown.FileCount);
    }

    [Fact]
    public void DashboardProjection_ConvertOnly_WithoutConversionReport_FallsBackToAllCandidates()
    {
        var result = new RunResult
        {
            TotalFilesScanned = 2,
            DurationMs = 1000,
            AllCandidates = new[]
            {
                new RomCandidate { MainPath = "a.iso", Category = FileCategory.Game, ConsoleKey = "PS2" },
                new RomCandidate { MainPath = "b.iso", Category = FileCategory.Game, ConsoleKey = "UNKNOWN" }
            }
        };

        var projection = RunProjectionFactory.Create(result);
        var dashboard = DashboardProjection.From(projection, result, isConvertOnlyRun: true);

        Assert.Equal(2, dashboard.ConsoleDistribution.Sum(item => item.FileCount));
        Assert.Contains(dashboard.ConsoleDistribution, item => item.ConsoleKey == "PS2");
        Assert.Contains(dashboard.ConsoleDistribution, item => item.ConsoleKey == "UNKNOWN");
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

    [Fact]
    public void DashboardProjection_PositiveConvertSavedBytes_UsesPositiveSign()
    {
        var result = new RunResult
        {
            TotalFilesScanned = 1,
            ConvertedCount = 1,
            ConvertSavedBytes = 1024,
            AllCandidates =
            [
                new RomCandidate { MainPath = "a.zip", Category = FileCategory.Game, DatMatch = false }
            ]
        };

        var projection = RunProjectionFactory.Create(result);
        var dashboard = DashboardProjection.From(projection, result, isConvertOnlyRun: false);

        Assert.Equal("+1.0 KB", dashboard.ConvertSavedBytesDisplay);
    }

    [Fact]
    public void DashboardProjection_CancelledPartialRun_MarksTopStatsAsProvisional()
    {
        var result = new RunResult
        {
            Status = "cancelled",
            TotalFilesScanned = 3,
            DurationMs = 2000,
            DedupeGroups = Array.Empty<DedupeGroup>(),
            AllCandidates = new[]
            {
                new RomCandidate { MainPath = "a.zip", Category = FileCategory.Game, DatMatch = true, ConsoleKey = "3ds" },
                new RomCandidate { MainPath = "b.zip", Category = FileCategory.Game, DatMatch = false, ConsoleKey = "3ds" },
                new RomCandidate { MainPath = "junk.zip", Category = FileCategory.Junk, DatMatch = false, ConsoleKey = "3ds" }
            }
        };

        var projection = RunProjectionFactory.Create(result);
        var dashboard = DashboardProjection.From(projection, result, isConvertOnlyRun: false);

        Assert.Equal("2 (vorläufig)", dashboard.Games);
        Assert.Equal("2 (vorläufig)", dashboard.Winners);
        Assert.Equal("0 (vorläufig)", dashboard.Dupes);
        Assert.Equal("1 (vorläufig)", dashboard.Junk);
        Assert.Contains("vorläufig", dashboard.HealthScore, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("vorläufig", dashboard.MoveConsequenceText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DashboardProjection_FailedRunAfterDedupe_MarksTopStatsAsProvisional()
    {
        var result = new RunResult
        {
            Status = "failed",
            TotalFilesScanned = 2,
            DurationMs = 1500,
            DedupeGroups =
            [
                new DedupeGroup
                {
                    GameKey = "g1",
                    Winner = new RomCandidate { MainPath = "winner.zip", Region = "US", RegionScore = 10, FormatScore = 10, VersionScore = 10 },
                    Losers = [new RomCandidate { MainPath = "loser.zip", Region = "EU", RegionScore = 9, FormatScore = 9, VersionScore = 9 }]
                }
            ],
            AllCandidates =
            [
                new RomCandidate { MainPath = "winner.zip", Category = FileCategory.Game, DatMatch = true, ConsoleKey = "psx" },
                new RomCandidate { MainPath = "loser.zip", Category = FileCategory.Game, DatMatch = false, ConsoleKey = "psx" }
            ]
        };

        var projection = RunProjectionFactory.Create(result);
        var dashboard = DashboardProjection.From(projection, result, isConvertOnlyRun: false);

        Assert.Contains("vorläufig", dashboard.Winners, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("vorläufig", dashboard.Dupes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("vorläufig", dashboard.MoveConsequenceText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DashboardProjection_ConsoleDistribution_TieBreaksByConsoleKey()
    {
        var result = new RunResult
        {
            AllCandidates =
            [
                new RomCandidate { MainPath = "a.zip", Category = FileCategory.Game, ConsoleKey = "SNES" },
                new RomCandidate { MainPath = "b.zip", Category = FileCategory.Game, ConsoleKey = "PS1" }
            ]
        };

        var projection = RunProjectionFactory.Create(result);
        var dashboard = DashboardProjection.From(projection, result, isConvertOnlyRun: false);

        Assert.Equal(["PS1", "SNES"], dashboard.ConsoleDistribution.Select(item => item.ConsoleKey).ToArray());
    }

    [Fact]
    public void DashboardProjection_DryRun_ShowsPlanMarker()
    {
        var result = new RunResult
        {
            TotalFilesScanned = 3,
            DurationMs = 1000,
            DedupeGroups = new[]
            {
                new DedupeGroup
                {
                    GameKey = "g1",
                    Winner = new RomCandidate { MainPath = "winner.zip", Region = "US", RegionScore = 10, FormatScore = 10, VersionScore = 10 },
                    Losers = new[]
                    {
                        new RomCandidate { MainPath = "loser.zip", Region = "EU", RegionScore = 9, FormatScore = 9, VersionScore = 9 }
                    }
                }
            },
            AllCandidates = new[]
            {
                new RomCandidate { MainPath = "winner.zip", Category = FileCategory.Game, DatMatch = true, ConsoleKey = "psx" },
                new RomCandidate { MainPath = "loser.zip", Category = FileCategory.Game, DatMatch = false, ConsoleKey = "psx" },
                new RomCandidate { MainPath = "junk.zip", Category = FileCategory.Junk, DatMatch = false, ConsoleKey = "psx" }
            }
        };

        var projection = RunProjectionFactory.Create(result);
        var dashboard = DashboardProjection.From(projection, result, isConvertOnlyRun: false, isDryRun: true);

        Assert.Contains("(Vorschau)", dashboard.Winners, StringComparison.Ordinal);
        Assert.Contains("(Vorschau)", dashboard.Dupes, StringComparison.Ordinal);
        Assert.Contains("(Vorschau)", dashboard.Junk, StringComparison.Ordinal);
        Assert.Contains("Vorschau", dashboard.MoveConsequenceText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ErrorSummaryProjection_Truncation_AddsRunTruncMarker()
    {
        var logs = Enumerable.Range(1, 55)
            .Select(i => new LogEntry($"Warnung {i}", "WARN"))
            .ToArray();

        var errors = ErrorSummaryProjection.Build(
            result: null,
            candidates: Array.Empty<RomCandidate>(),
            runLogs: logs);

        Assert.Equal(51, errors.Count);
        var trunc = errors.Last();
        Assert.Equal("RUN-TRUNC", trunc.Code);
        Assert.Contains("weitere", trunc.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ═══ Dead projection records removed (TASK-121 cleanup) ═══════════
    // StatusProjection, ProgressProjection, BannerProjection, MoveGateProjection
    // were never wired into production code and have been deleted.
}
