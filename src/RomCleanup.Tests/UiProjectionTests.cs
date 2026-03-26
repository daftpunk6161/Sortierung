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

    // ═══ TASK-121: ProgressProjection ════════════════════════════════════

    [Fact]
    public void ProgressProjection_Idle_HasDefaults()
    {
        var p = ProgressProjection.Idle;
        Assert.Equal(0, p.Progress);
        Assert.Equal(RunState.Idle, p.CurrentRunState);
        Assert.False(p.IsBusy);
        Assert.NotNull(p.ProgressText);
        Assert.NotNull(p.CurrentPhase);
        Assert.NotNull(p.CurrentFile);
    }

    [Fact]
    public void ProgressProjection_CanCreateWithValues()
    {
        var p = new ProgressProjection(42.5, "42%", "Phase: Scan", "rom.zip", RunState.Scanning, true, "Scanning...");
        Assert.Equal(42.5, p.Progress);
        Assert.Equal("42%", p.ProgressText);
        Assert.Equal(RunState.Scanning, p.CurrentRunState);
        Assert.True(p.IsBusy);
    }

    [Fact]
    public void ProgressProjection_IsImmutable()
    {
        var p1 = new ProgressProjection(10, "10%", "Scan", "a.zip", RunState.Scanning, true, "");
        var p2 = p1 with { Progress = 50 };
        Assert.Equal(10, p1.Progress);
        Assert.Equal(50, p2.Progress);
    }

    // ═══ TASK-121: StatusProjection ══════════════════════════════════════

    [Fact]
    public void StatusProjection_Empty_AllMissing()
    {
        var s = StatusProjection.Empty;
        Assert.Equal(StatusLevel.Missing, s.RootsStatusLevel);
        Assert.Equal(StatusLevel.Missing, s.ToolsStatusLevel);
        Assert.Equal(StatusLevel.Missing, s.DatStatusLevel);
        Assert.Equal(StatusLevel.Missing, s.ReadyStatusLevel);
        Assert.Equal("–", s.ChdmanStatusText);
        Assert.Equal("–", s.DolphinStatusText);
    }

    [Fact]
    public void StatusProjection_CanCreateWithValues()
    {
        var s = new StatusProjection(
            "3 Ordner", StatusLevel.Ok,
            "4 Tools", StatusLevel.Ok,
            "2 DATs", StatusLevel.Warning,
            "Bereit", StatusLevel.Ok,
            ".NET 10",
            "✓", "✓", "✓", "–", "–");
        Assert.Equal(StatusLevel.Ok, s.RootsStatusLevel);
        Assert.Equal(StatusLevel.Warning, s.DatStatusLevel);
        Assert.Equal("✓", s.ChdmanStatusText);
    }

    // ═══ TASK-121: BannerProjection ══════════════════════════════════════

    [Fact]
    public void BannerProjection_None_AllFalse()
    {
        var b = BannerProjection.None;
        Assert.False(b.ShowDryRunBanner);
        Assert.False(b.ShowMoveCompleteBanner);
        Assert.False(b.ShowConfigChangedBanner);
    }

    [Fact]
    public void BannerProjection_CanCreateWithValues()
    {
        var b = new BannerProjection(true, false, true);
        Assert.True(b.ShowDryRunBanner);
        Assert.False(b.ShowMoveCompleteBanner);
        Assert.True(b.ShowConfigChangedBanner);
    }

    // ═══ TASK-121: MoveGateProjection ════════════════════════════════════

    [Fact]
    public void MoveGateProjection_Idle_HasDefaults()
    {
        var m = MoveGateProjection.Idle;
        Assert.False(m.ShowStartMoveButton);
        Assert.False(m.HasRunResult);
        Assert.False(m.CanRollback);
        Assert.Equal("", m.LastReportPath);
    }

    [Fact]
    public void MoveGateProjection_CanCreateWithValues()
    {
        var m = new MoveGateProjection(true, true, true, "/report.html");
        Assert.True(m.ShowStartMoveButton);
        Assert.True(m.HasRunResult);
        Assert.Equal("/report.html", m.LastReportPath);
    }

    [Fact]
    public void MoveGateProjection_IsImmutable()
    {
        var m1 = new MoveGateProjection(false, false, false, "");
        var m2 = m1 with { HasRunResult = true };
        Assert.False(m1.HasRunResult);
        Assert.True(m2.HasRunResult);
    }
}
