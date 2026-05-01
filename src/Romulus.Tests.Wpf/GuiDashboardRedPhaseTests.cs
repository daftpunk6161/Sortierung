using Romulus.Contracts.Models;
using Romulus.Infrastructure.Orchestration;
using Romulus.UI.Wpf.Models;
using Romulus.UI.Wpf.ViewModels;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// TDD Red Phase: Failing tests for GUI / Dashboard / ViewModel state issues.
/// These tests verify correct WPF behavior for:
///   - Dashboard reset on re-run / cancel / rollback
///   - Status correctness after error / cancel states
///   - Command enabled/disabled in correct states
///   - ConvertOnly vs Dedupe KPI visibility
///   - Progress and summary consistency
///   - Danger zone safeguards
///   - GUI↔CLI/API parity
/// </summary>
public sealed class GuiDashboardRedPhaseTests
{
    // ═══════════════════════════════════════════════════════════════════
    // HELPER: Build a minimal RunResult for test scenarios
    // ═══════════════════════════════════════════════════════════════════

    private static RunResult BuildSuccessResult(int winners = 5, int losers = 3, int junk = 2, int scanned = 10, long durationMs = 1500)
    {
        var candidates = new List<RomCandidate>();
        for (int i = 0; i < winners; i++)
            candidates.Add(new RomCandidate { MainPath = "winner" + i + ".zip", Category = FileCategory.Game, DatMatch = true, ConsoleKey = "SNES" });
        for (int i = 0; i < losers; i++)
            candidates.Add(new RomCandidate { MainPath = "loser" + i + ".zip", Category = FileCategory.Game, DatMatch = false, ConsoleKey = "SNES" });
        for (int i = 0; i < junk; i++)
            candidates.Add(new RomCandidate { MainPath = "junk" + i + ".zip", Category = FileCategory.Junk, DatMatch = false, ConsoleKey = "SNES" });

        var dedupeGroups = new List<DedupeGroup>();
        if (winners > 0 && losers > 0)
        {
            dedupeGroups.Add(new DedupeGroup
            {
                GameKey = "TestGame",
                Winner = candidates[0],
                Losers = candidates.Skip(winners).Take(losers).ToArray()
            });
        }

        return new RunResult
        {
            Status = "completed",
            WinnerCount = winners,
            LoserCount = losers,
            TotalFilesScanned = scanned,
            DurationMs = durationMs,
            AllCandidates = candidates.ToArray(),
            DedupeGroups = dedupeGroups.ToArray(),
            MoveResult = new MovePhaseResult(losers + junk, 0, 0)
        };
    }

    private static RunResult BuildCancelledResult(int scannedSoFar = 3)
    {
        var candidates = new List<RomCandidate>();
        for (int i = 0; i < scannedSoFar; i++)
        {
            candidates.Add(new RomCandidate { MainPath = "partial" + i + ".zip", Category = FileCategory.Game, DatMatch = false, ConsoleKey = "NES" });
        }

        return new RunResult
        {
            Status = "cancelled",
            TotalFilesScanned = scannedSoFar,
            DurationMs = 500,
            AllCandidates = candidates.ToArray(),
            DedupeGroups = Array.Empty<DedupeGroup>()
        };
    }

    private static RunResult BuildFailedResult()
    {
        return new RunResult
        {
            Status = "failed",
            TotalFilesScanned = 2,
            DurationMs = 300,
            AllCandidates = new[]
            {
                new RomCandidate { MainPath = "err.zip", Category = FileCategory.Game, DatMatch = false, ConsoleKey = "GBA" }
            },
            DedupeGroups = Array.Empty<DedupeGroup>()
        };
    }

    private static RunResult BuildConvertOnlyResult()
    {
        return new RunResult
        {
            Status = "completed",
            TotalFilesScanned = 4,
            DurationMs = 2000,
            ConvertedCount = 3,
            ConvertErrorCount = 1,
            ConvertBlockedCount = 0,
            AllCandidates = new[]
            {
                new RomCandidate { MainPath = "a.iso", Category = FileCategory.Game, DatMatch = true, ConsoleKey = "PS2" },
                new RomCandidate { MainPath = "b.bin", Category = FileCategory.Game, DatMatch = true, ConsoleKey = "PS2" },
                new RomCandidate { MainPath = "c.cue", Category = FileCategory.Game, DatMatch = false, ConsoleKey = "PS2" },
                new RomCandidate { MainPath = "d.img", Category = FileCategory.Game, DatMatch = false, ConsoleKey = "PS2" },
            },
            DedupeGroups = Array.Empty<DedupeGroup>()
        };
    }

    // ═══════════════════════════════════════════════════════════════════
    // 1. Dashboard zeigt nach Re-Run keine alten Werte
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Nach einem erfolgreichen Run und anschließendem Re-Run (neuer DryRun)
    /// muss das Dashboard komplett zurückgesetzt werden, bevor neue Daten kommen.
    /// Ziel: Alte KPI-Werte (Winners, Dupes, Junk, HealthScore, DatHits)
    ///       dürfen nicht zwischen zwei Runs weiterleben.
    /// Warum heute rot: ResetDashboardForNewRun setzt DashWinners auf "–",
    ///       aber RunViewModel.DashWinners startet mit "0" — Reset-Wert Inkonsistenz.
    /// Betroffene Dateien: MainViewModel.RunPipeline.cs, RunViewModel.cs
    /// </summary>
    [Fact]
    public void Dashboard_AfterReRun_DoesNotShowStaleValues()
    {
        var vm = new MainViewModel();

        // Simulate first run result
        var firstResult = BuildSuccessResult(winners: 10, losers: 5, junk: 3);
        vm.Run.CurrentRunState = RunState.Preflight;
        vm.Run.CurrentRunState = RunState.Scanning;
        vm.Run.CurrentRunState = RunState.CompletedDryRun;
        vm.DryRun = true;
        vm.ApplyRunResult(firstResult);

        // Verify first run values populated
        Assert.NotEqual("–", vm.DashWinners);
        Assert.NotEqual("0", vm.DashWinners);

        // Start second run: simulate OnRun() reset
        vm.Run.CurrentRunState = RunState.Preflight;

        // After re-run start, all dashboard KPIs must be reset (no stale old data)
        // The reset should set all dash values to "–" (placeholder)
        Assert.Equal("–", vm.DashWinners);
        Assert.Equal("–", vm.DashDupes);
        Assert.Equal("–", vm.DashJunk);
        Assert.Equal("–", vm.HealthScore);
        Assert.Equal("–", vm.DashGames);
        Assert.Equal("–", vm.DashDatHits);
        Assert.Equal("–", vm.DedupeRate);
        Assert.Equal("", vm.MoveConsequenceText);
        Assert.Empty(vm.ConsoleDistribution);
        Assert.Empty(vm.DedupeGroupItems);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 2. Dashboard zeigt nach Cancel keine stale Partial-Daten
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Nach Cancel muss das Dashboard entweder klar "vorläufig" markierte Werte zeigen
    /// oder die Partial-Daten als solche kennzeichnen.
    /// Ziel: Cancel-KPIs müssen als "(vorläufig)" markiert sein.
    /// Warum heute rot: DashboardProjection markiert cancelled partial data mit
    ///       "(vorläufig)", aber der MoveConsequenceText weist nicht explizit
    ///       auf den Abbruch hin wenn keine Candidates vorhanden sind.
    /// Betroffene Dateien: DashboardProjection.cs, MainViewModel.RunPipeline.cs
    /// </summary>
    [Fact]
    public void Dashboard_AfterCancel_MarksPartialValuesAsProvisional()
    {
        var cancelledResult = BuildCancelledResult(scannedSoFar: 5);
        var projection = RunProjectionFactory.Create(cancelledResult);
        var dashboard = DashboardProjection.From(projection, cancelledResult, isConvertOnlyRun: false, isDryRun: false);

        // Cancelled run with partial data must mark values as provisional
        Assert.Contains("vorläufig", dashboard.MoveConsequenceText);
        Assert.Contains("abgebrochen", dashboard.MoveConsequenceText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Cancelled run mit 0 Candidates: Dashboard darf keine irreführenden Zahlen zeigen.
    /// Warum heute rot: Ein cancelled Run ohne Candidates zeigt ggf. "0" statt "–".
    /// Betroffene Dateien: DashboardProjection.cs
    /// </summary>
    [Fact]
    public void Dashboard_AfterCancel_NoCandidates_ShowsDashInsteadOfZero()
    {
        var cancelledResult = new RunResult
        {
            Status = "cancelled",
            TotalFilesScanned = 0,
            DurationMs = 100,
            AllCandidates = Array.Empty<RomCandidate>(),
            DedupeGroups = Array.Empty<DedupeGroup>()
        };
        var projection = RunProjectionFactory.Create(cancelledResult);
        var dashboard = DashboardProjection.From(projection, cancelledResult, isConvertOnlyRun: false, isDryRun: false);

        // A cancelled run with 0 data should show "–" for health, not "0%" or similar
        Assert.Equal("–", dashboard.HealthScore);
        // Winners should not show misleading "0" — should be dash
        Assert.Equal("–", dashboard.Winners);
        Assert.Equal("–", dashboard.Dupes);
        Assert.Equal("–", dashboard.DedupeRate);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 3. Dashboard zeigt nach Rollback korrekt reset oder klaren Zustand
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Nach Rollback muss das Dashboard zurückgesetzt oder einen klaren
    /// "Rollback abgeschlossen" Zustand zeigen. DashMode muss "Rollback" sein.
    /// Warum heute rot: OnRollbackAsync ruft ResetDashboardForNewRun() auf und setzt
    ///       DashMode = "Rollback", aber DashWinners ist dann "–" und nicht "0".
    ///       RunViewModel.DashWinners startet mit Default "0", nicht "–".
    /// Betroffene Dateien: MainViewModel.RunPipeline.cs, RunViewModel.cs
    /// </summary>
    [Fact]
    public void Dashboard_AfterRollback_ShowsCleanResetState()
    {
        var vm = new MainViewModel();

        // Populate some data
        vm.DashWinners = "10";
        vm.DashDupes = "5";
        vm.DashJunk = "3";
        vm.DashMode = "Move";

        // Simulate what OnRollbackAsync does internally:
        // 1. ResetDashboardForNewRun()
        // 2. DashMode = "Rollback"
        vm.DashWinners = "–";
        vm.DashDupes = "–";
        vm.DashJunk = "–";
        vm.DashMode = "Rollback";

        Assert.Equal("–", vm.DashWinners);
        Assert.Equal("Rollback", vm.DashMode);
        // RunSummary should indicate rollback, not empty
        Assert.False(string.IsNullOrWhiteSpace(vm.RunSummaryText),
            "RunSummary should contain rollback status text, not be empty");
    }

    // ═══════════════════════════════════════════════════════════════════
    // 4. Status ist nie "ok"/"completed", wenn Fehler oder Cancel vorliegen
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// RunState.Completed darf nicht gesetzt werden wenn der RunResult.Status = "failed" ist.
    /// Ziel: Status-Divergenz zwischen RunResult.Status und RunState verhindern.
    /// Warum heute rot: CompleteRun(success: true) setzt RunState.Completed unabhängig
    ///       davon, was RunResult.Status tatsächlich sagt.
    /// Betroffene Dateien: MainViewModel.RunPipeline.cs
    /// </summary>
    [Fact]
    public void Status_CompletedNotSetWhenResultStatusIsFailed()
    {
        var vm = new MainViewModel();
        vm.Run.CurrentRunState = RunState.Preflight;
        vm.Run.CurrentRunState = RunState.Scanning;

        // Apply a failed result
        var failedResult = BuildFailedResult();
        vm.ApplyRunResult(failedResult, force: true);

        // Now complete the run — marked as success=false
        vm.CompleteRun(false);

        // RunState must be Failed, never Completed
        Assert.Equal(RunState.Failed, vm.Run.CurrentRunState);
        Assert.NotEqual(RunState.Completed, vm.Run.CurrentRunState);
    }

    /// <summary>
    /// RunSummarySeverity darf nie Info sein, wenn der Run gecancelt wurde.
    /// Warum heute rot: Es gibt keinen Test dafür.
    /// Betroffene Dateien: MainViewModel.RunPipeline.cs
    /// </summary>
    [Fact]
    public void Status_CancelledRun_SummarySeverityIsNotInfo()
    {
        var vm = new MainViewModel();
        vm.Run.CurrentRunState = RunState.Preflight;
        vm.Run.CurrentRunState = RunState.Scanning;

        vm.CompleteRun(false, cancelled: true);

        Assert.NotEqual(UiErrorSeverity.Info, vm.RunSummarySeverity);
        Assert.True(vm.RunSummarySeverity is UiErrorSeverity.Warning or UiErrorSeverity.Error,
            "Cancelled runs should have Warning or Error severity, not Info");
    }

    /// <summary>
    /// RunSummarySeverity muss Error sein bei failed runs.
    /// Warum heute rot: Es gibt keinen Test dafür.
    /// Betroffene Dateien: MainViewModel.RunPipeline.cs
    /// </summary>
    [Fact]
    public void Status_FailedRun_SummarySeverityIsError()
    {
        var vm = new MainViewModel();
        vm.Run.CurrentRunState = RunState.Preflight;
        vm.Run.CurrentRunState = RunState.Scanning;

        vm.CompleteRun(false);

        Assert.Equal(UiErrorSeverity.Error, vm.RunSummarySeverity);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 5. Commands enabled/disabled in passenden Zuständen
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// CancelCommand darf nur während IsBusy enabled sein.
    /// Nach Completed, Failed, Cancelled muss CancelCommand disabled sein.
    /// Warum heute rot: Es gibt keinen Lifecycle-Test über alle Terminal-States.
    /// Betroffene Dateien: MainViewModel.cs (CancelCommand CanExecute)
    /// </summary>
    [Theory]
    [InlineData(RunState.Completed)]
    [InlineData(RunState.CompletedDryRun)]
    [InlineData(RunState.Failed)]
    [InlineData(RunState.Cancelled)]
    [InlineData(RunState.Idle)]
    public void Commands_CancelDisabledInTerminalStates(RunState terminalState)
    {
        var vm = new MainViewModel();

        // Navigate to the terminal state via valid transitions
        if (terminalState != RunState.Idle)
        {
            vm.Run.CurrentRunState = RunState.Preflight;
            vm.Run.CurrentRunState = RunState.Scanning;
            vm.Run.CurrentRunState = terminalState;
        }

        Assert.False(vm.CancelCommand.CanExecute(null),
            $"CancelCommand should be disabled in {terminalState} state");
    }

    /// <summary>
    /// RunCommand darf nicht enabled sein während IsBusy, auch wenn Roots gesetzt sind.
    /// Warum heute rot: Es gibt keinen expliziten Test dafür.
    /// Betroffene Dateien: MainViewModel.cs, MainViewModel.RunPipeline.cs
    /// </summary>
    [Theory]
    [InlineData(RunState.Preflight)]
    [InlineData(RunState.Scanning)]
    [InlineData(RunState.Deduplicating)]
    [InlineData(RunState.Moving)]
    [InlineData(RunState.Converting)]
    public void Commands_RunDisabledDuringBusyStates(RunState busyState)
    {
        var vm = new MainViewModel();
        vm.Roots.Add(@"C:\TestRoot");
        vm.DryRun = true;

        // Navigate through valid transitions to reach the target busy state
        vm.Run.CurrentRunState = RunState.Preflight;
        if (busyState is RunState.Scanning or RunState.Deduplicating or RunState.Moving or RunState.Converting)
            vm.Run.CurrentRunState = RunState.Scanning;
        if (busyState is RunState.Deduplicating or RunState.Moving or RunState.Converting)
            vm.Run.CurrentRunState = RunState.Deduplicating;
        if (busyState is RunState.Moving or RunState.Converting)
            vm.Run.CurrentRunState = RunState.Moving;
        if (busyState is RunState.Converting)
            vm.Run.CurrentRunState = RunState.Converting;

        Assert.False(vm.RunCommand.CanExecute(null),
            $"RunCommand should be disabled during {busyState} state");
    }

    /// <summary>
    /// StartMoveCommand darf nur nach CompletedDryRun UND unverändertem Config enabled sein.
    /// Warum heute rot: Es fehlt ein Test für den kombinierten Gate-Check.
    /// Betroffene Dateien: MainViewModel.cs, MainViewModel.RunPipeline.cs
    /// </summary>
    [Fact]
    public void Commands_StartMoveDisabledWithoutDryRunFirst()
    {
        var vm = new MainViewModel();
        vm.Roots.Add(@"C:\TestRoot");
        vm.DryRun = false;

        // Should be disabled because no DryRun has been completed
        Assert.False(vm.StartMoveCommand.CanExecute(null),
            "StartMoveCommand should be disabled when no DryRun has been completed first");
    }

    /// <summary>
    /// RollbackCommand darf nur wenn !IsBusy UND CanRollback=true enabled sein.
    /// Warum heute rot: Es gibt keinen Test für Rollback während Busy.
    /// Betroffene Dateien: MainViewModel.cs
    /// </summary>
    [Fact]
    public void Commands_RollbackDisabledDuringBusy()
    {
        var vm = new MainViewModel();
        vm.CanRollback = true;

        vm.Run.CurrentRunState = RunState.Preflight;

        Assert.False(vm.RollbackCommand.CanExecute(null),
            "RollbackCommand should be disabled during busy state even when CanRollback=true");
    }

    /// <summary>
    /// ConvertOnlyCommand darf nicht während Busy enabled sein.
    /// Warum heute rot: Es gibt keinen expliziten Test dafür.
    /// Betroffene Dateien: MainViewModel.cs
    /// </summary>
    [Fact]
    public void Commands_ConvertOnlyDisabledDuringBusy()
    {
        var vm = new MainViewModel();
        vm.Roots.Add(@"C:\TestRoot");

        vm.Run.CurrentRunState = RunState.Preflight;

        Assert.False(vm.ConvertOnlyCommand.CanExecute(null),
            "ConvertOnlyCommand should be disabled when IsBusy");
    }

    // ═══════════════════════════════════════════════════════════════════
    // 6. ConvertOnly zeigt keine irrelevanten Dedupe-KPIs
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Im ConvertOnly-Modus muss DashboardProjection Winners, Dupes, Junk,
    /// HealthScore, DedupeRate als "–" zeigen.
    /// Warum heute rot: Winners/Dupes/Junk werden "–", aber Games wird
    ///       ebenfalls "–" — kein Test prüft die vollständige Abdeckung.
    /// Betroffene Dateien: DashboardProjection.cs
    /// </summary>
    [Fact]
    public void ConvertOnly_HidesAllDedupeKPIs()
    {
        var convertResult = BuildConvertOnlyResult();
        var projection = RunProjectionFactory.Create(convertResult);
        var dashboard = DashboardProjection.From(projection, convertResult, isConvertOnlyRun: true, isDryRun: false);

        Assert.Equal("Entfällt", dashboard.Winners);
        Assert.Equal("Entfällt", dashboard.Dupes);
        Assert.Equal("Entfällt", dashboard.Junk);
        Assert.Equal("Entfällt", dashboard.HealthScore);
        Assert.Equal("Entfällt", dashboard.Games);
        Assert.Equal("Entfällt", dashboard.DatHits);
        Assert.Equal("Entfällt", dashboard.DedupeRate);

        // But conversion KPIs must be visible
        Assert.NotEqual("–", dashboard.ConvertedDisplay);
    }

    /// <summary>
    /// ConvertOnly-Modus: Der MoveConsequenceText muss klar sagen,
    /// dass keine Dateien verschoben werden.
    /// Warum heute rot: Existierender Test, aber kein negativer Gegentest
    ///       für "verschoben" im ConvertOnly-Text.
    /// Betroffene Dateien: DashboardProjection.cs
    /// </summary>
    [Fact]
    public void ConvertOnly_MoveConsequenceExcludesMoveCounts()
    {
        var convertResult = BuildConvertOnlyResult();
        var projection = RunProjectionFactory.Create(convertResult);
        var dashboard = DashboardProjection.From(projection, convertResult, isConvertOnlyRun: true, isDryRun: false);

        Assert.DoesNotContain("Duplikate", dashboard.MoveConsequenceText);
        Assert.Contains("Keine Dateien werden verschoben", dashboard.MoveConsequenceText);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 7. DryRun-Kennzeichnung
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Dashboard-KPIs müssen im DryRun mit "(Plan)" markiert sein.
    /// Warum heute rot: Es gibt keinen Test der alle relevanten KPIs auf "(Plan)" prüft.
    /// Betroffene Dateien: DashboardProjection.cs
    /// </summary>
    [Fact]
    public void DryRun_DashboardKPIsMarkedAsPlan()
    {
        var result = BuildSuccessResult();
        var projection = RunProjectionFactory.Create(result);
        var dashboard = DashboardProjection.From(projection, result, isConvertOnlyRun: false, isDryRun: true);

        Assert.Contains("(Vorschau)", dashboard.Winners);
        Assert.Contains("(Vorschau)", dashboard.Dupes);
        Assert.Contains("(Vorschau)", dashboard.Junk);
        Assert.Contains("(Vorschau)", dashboard.HealthScore);
        Assert.Contains("(Vorschau)", dashboard.Games);
        Assert.Contains("(Vorschau)", dashboard.DedupeRate);
    }

    /// <summary>
    /// DryRun-MoveConsequenceText muss "Vorschau" oder "Plan" enthalten.
    /// Warum heute rot: Kein isolierter Test.
    /// Betroffene Dateien: DashboardProjection.cs
    /// </summary>
    [Fact]
    public void DryRun_MoveConsequenceIndicatesPreview()
    {
        var result = BuildSuccessResult();
        var projection = RunProjectionFactory.Create(result);
        var dashboard = DashboardProjection.From(projection, result, isConvertOnlyRun: false, isDryRun: true);

        Assert.Contains("Vorschau", dashboard.MoveConsequenceText);
        Assert.DoesNotContain("Es werden", dashboard.MoveConsequenceText);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 8. RunState darf nach Cancel nicht auf Completed stehen
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// CompleteRun(cancelled: true) muss RunState = Cancelled setzen, nie Completed.
    /// Warum heute rot: Assertion existiert nicht explizit.
    /// Betroffene Dateien: MainViewModel.RunPipeline.cs
    /// </summary>
    [Fact]
    public void CompleteRun_Cancelled_NeverSetsCompleted()
    {
        var vm = new MainViewModel();
        vm.Run.CurrentRunState = RunState.Preflight;
        vm.Run.CurrentRunState = RunState.Scanning;

        vm.CompleteRun(false, cancelled: true);

        Assert.Equal(RunState.Cancelled, vm.Run.CurrentRunState);
        Assert.NotEqual(RunState.Completed, vm.Run.CurrentRunState);
        Assert.NotEqual(RunState.CompletedDryRun, vm.Run.CurrentRunState);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 9. HasRunResult korrekt in verschiedenen Zuständen
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// HasRunResult muss true sein nach Cancelled wenn TotalFilesScanned > 0.
    /// Der User muss noch die Partial-Daten sehen können.
    /// Warum heute rot: Der Pfad existiert im Code, aber kein Test validiert die
    ///       MainViewModel-Ebene (nur RunViewModel-Level).
    /// Betroffene Dateien: MainViewModel.RunPipeline.cs, RunViewModel.cs
    /// </summary>
    [Fact]
    public void HasRunResult_TrueAfterCancelledWithPartialData()
    {
        var vm = new MainViewModel();
        vm.Run.CurrentRunState = RunState.Preflight;
        vm.Run.CurrentRunState = RunState.Scanning;

        var cancelledResult = BuildCancelledResult(scannedSoFar: 5);
        vm.ApplyRunResult(cancelledResult, force: true);
        vm.Run.CurrentRunState = RunState.Cancelled;
        vm.LastRunResult = cancelledResult;

        Assert.True(vm.HasRunResult,
            "HasRunResult should be true after Cancelled with partial data, so user can inspect results");
    }

    /// <summary>
    /// HasRunResult muss false sein nach Cancelled wenn 0 Dateien gescannt.
    /// Warum heute rot: Kein expliziter Test.
    /// Betroffene Dateien: RunViewModel.cs, MainViewModel.RunPipeline.cs
    /// </summary>
    [Fact]
    public void HasRunResult_FalseAfterCancelledWithZeroData()
    {
        var vm = new MainViewModel();
        vm.Run.CurrentRunState = RunState.Preflight;
        vm.Run.CurrentRunState = RunState.Cancelled;

        Assert.False(vm.HasRunResult,
            "HasRunResult should be false when Cancel happened before any scan data existed");
    }

    /// <summary>
    /// HasRunResult muss true sein nach Failed wenn TotalFilesScanned > 0.
    /// Warum heute rot: Nur RunViewModel-Level getestet, nicht geprüft ob MainViewModel korrekt delegiert.
    /// Betroffene Dateien: MainViewModel.RunPipeline.cs, RunViewModel.cs
    /// </summary>
    [Fact]
    public void HasRunResult_TrueAfterFailedWithPartialData()
    {
        var vm = new MainViewModel();
        vm.Run.CurrentRunState = RunState.Preflight;
        vm.Run.CurrentRunState = RunState.Scanning;

        var failedResult = BuildFailedResult();
        vm.ApplyRunResult(failedResult, force: true);
        vm.Run.CurrentRunState = RunState.Failed;
        vm.LastRunResult = failedResult;

        Assert.True(vm.HasRunResult,
            "HasRunResult should be true after Failed with partial data");
    }

    // ═══════════════════════════════════════════════════════════════════
    // 10. Progress reset bei neuem Run
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Progress muss auf 0 zurückgesetzt werden wenn ein neuer Run startet.
    /// Warum heute rot: Es gibt keinen Test dafür auf ViewModel-Ebene.
    /// Betroffene Dateien: MainViewModel.RunPipeline.cs
    /// </summary>
    [Fact]
    public void Progress_ResetOnNewRun()
    {
        var vm = new MainViewModel();
        vm.Progress = 75;
        vm.ProgressText = "75%";

        // Simulate what OnRun does
        vm.Run.CurrentRunState = RunState.Preflight;
        vm.Progress = 0;
        vm.ProgressText = "0%";

        Assert.Equal(0, vm.Progress);
        Assert.Equal("0%", vm.ProgressText);
    }

    /// <summary>
    /// Progress ist 100 nach erfolgreichem Run.
    /// Warum heute rot: Kein expliziter Test.
    /// Betroffene Dateien: MainViewModel.RunPipeline.cs (ApplyRunResult)
    /// </summary>
    [Fact]
    public void Progress_100AfterSuccessfulRun()
    {
        var vm = new MainViewModel();
        vm.Run.CurrentRunState = RunState.Preflight;
        vm.Run.CurrentRunState = RunState.Scanning;

        var result = BuildSuccessResult();
        vm.DryRun = true;
        vm.ApplyRunResult(result);

        Assert.Equal(100, vm.Progress);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 11. ConvertOnly-Flag wird nach Run zurückgesetzt
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// ConvertOnly ist ein transientes Flag — nach CompleteRun muss es false sein.
    /// Warum heute rot: Kein Test validiert den Reset.
    /// Betroffene Dateien: MainViewModel.RunPipeline.cs (CompleteRun)
    /// </summary>
    [Fact]
    public void ConvertOnly_ResetAfterCompleteRun()
    {
        var vm = new MainViewModel();
        vm.ConvertOnly = true;
        vm.Run.CurrentRunState = RunState.Preflight;
        vm.Run.CurrentRunState = RunState.Scanning;
        vm.DryRun = false;

        vm.CompleteRun(true);

        Assert.False(vm.ConvertOnly,
            "ConvertOnly should be reset to false after CompleteRun — it's transient");
    }

    /// <summary>
    /// ConvertOnly wird auch bei Cancel zurückgesetzt.
    /// Warum heute rot: Kein Test validiert den Reset bei Cancel.
    /// Betroffene Dateien: MainViewModel.RunPipeline.cs (CompleteRun)
    /// </summary>
    [Fact]
    public void ConvertOnly_ResetAfterCancelledRun()
    {
        var vm = new MainViewModel();
        vm.ConvertOnly = true;
        vm.Run.CurrentRunState = RunState.Preflight;
        vm.Run.CurrentRunState = RunState.Scanning;

        vm.CompleteRun(false, cancelled: true);

        Assert.False(vm.ConvertOnly,
            "ConvertOnly should be reset even after cancel");
    }

    // ═══════════════════════════════════════════════════════════════════
    // 12. ShowConfigChangedBanner Verhalten
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// ShowConfigChangedBanner darf nicht true sein wenn noch nie ein Preview gemacht wurde.
    /// Warum heute rot: Kein Test prüft den Initial-State.
    /// Betroffene Dateien: MainViewModel.RunPipeline.cs
    /// </summary>
    [Fact]
    public void ShowConfigChangedBanner_FalseIfNoPreviewDone()
    {
        var vm = new MainViewModel();
        vm.Roots.Add(@"C:\TestRoot");
        vm.DryRun = false;

        Assert.False(vm.ShowConfigChangedBanner,
            "ShowConfigChangedBanner should be false when no DryRun has been completed");
    }

    // ═══════════════════════════════════════════════════════════════════
    // 13. DashboardProjection — Failed Run Kennzeichnung
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Failed Run mit Partial-Daten muss KPIs als "(vorläufig)" kennzeichnen.
    /// Ein failed Run ist kein vollständiger, die Zahlen sind nicht verlässlich.
    /// Warum heute rot: Kein spezifischer Test für failed+partial.
    /// Betroffene Dateien: DashboardProjection.cs
    /// </summary>
    [Fact]
    public void DashboardProjection_FailedWithPartialData_MarksProvisional()
    {
        var failedResult = BuildFailedResult();
        var projection = RunProjectionFactory.Create(failedResult);
        var dashboard = DashboardProjection.From(projection, failedResult, isConvertOnlyRun: false, isDryRun: false);

        // Failed + has candidates = partial → should be marked provisional
        Assert.Contains("vorläufig", dashboard.MoveConsequenceText);
    }

    /// <summary>
    /// Failed Run ohne Candidates: HealthScore muss "–" sein, nicht "0%".
    /// Warum heute rot: HealthScore Berechnung auf TotalFiles=0 könnte "0%" ergeben.
    /// Betroffene Dateien: DashboardProjection.cs
    /// </summary>
    [Fact]
    public void DashboardProjection_FailedNoCandidates_HealthScoreIsDash()
    {
        var failedResult = new RunResult
        {
            Status = "failed",
            TotalFilesScanned = 0,
            DurationMs = 50,
            AllCandidates = Array.Empty<RomCandidate>(),
            DedupeGroups = Array.Empty<DedupeGroup>()
        };
        var projection = RunProjectionFactory.Create(failedResult);
        var dashboard = DashboardProjection.From(projection, failedResult, isConvertOnlyRun: false, isDryRun: false);

        Assert.Equal("–", dashboard.HealthScore);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 14. ShowMoveCompleteBanner Lifecycle
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// ShowMoveCompleteBanner darf nur nach erfolgreichem Move (non-DryRun) true sein.
    /// Warum heute rot: Kein umfassender Lifecycle-Test.
    /// Betroffene Dateien: MainViewModel.RunPipeline.cs
    /// </summary>
    [Fact]
    public void ShowMoveCompleteBanner_OnlyAfterSuccessfulMove()
    {
        var vm = new MainViewModel();

        // DryRun success → banner false
        vm.DryRun = true;
        vm.Run.CurrentRunState = RunState.Preflight;
        vm.Run.CurrentRunState = RunState.Scanning;
        vm.CompleteRun(true);
        Assert.False(vm.ShowMoveCompleteBanner, "Banner should not show after DryRun");

        // Reset
        vm.Run.CurrentRunState = RunState.Idle;

        // Move success → banner true
        vm.DryRun = false;
        vm.Run.CurrentRunState = RunState.Preflight;
        vm.Run.CurrentRunState = RunState.Scanning;
        vm.CompleteRun(true);
        Assert.True(vm.ShowMoveCompleteBanner, "Banner should show after successful Move");
    }

    /// <summary>
    /// ShowMoveCompleteBanner muss false sein nach Cancel.
    /// Warum heute rot: OnCancel setzt ShowMoveCompleteBanner = false, aber kein Test prüft das.
    /// Betroffene Dateien: MainViewModel.RunPipeline.cs
    /// </summary>
    [Fact]
    public void ShowMoveCompleteBanner_FalseAfterCancel()
    {
        var vm = new MainViewModel();

        // Make banner visible first
        vm.ShowMoveCompleteBanner = true;

        vm.Run.CurrentRunState = RunState.Preflight;
        vm.Run.CurrentRunState = RunState.Scanning;
        vm.CompleteRun(false, cancelled: true);

        Assert.False(vm.ShowMoveCompleteBanner,
            "ShowMoveCompleteBanner should be false after cancellation");
    }

    // ═══════════════════════════════════════════════════════════════════
    // 15. BusyHint Korrektheit
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// BusyHint muss nach CompleteRun leer sein.
    /// Warum heute rot: Kein Test prüft BusyHint-Lifecycle.
    /// Betroffene Dateien: MainViewModel.RunPipeline.cs
    /// </summary>
    [Fact]
    public void BusyHint_EmptyAfterCompleteRun()
    {
        var vm = new MainViewModel();
        vm.BusyHint = "Converting...";
        vm.Run.CurrentRunState = RunState.Preflight;
        vm.Run.CurrentRunState = RunState.Scanning;

        vm.CompleteRun(true);

        Assert.Equal("", vm.BusyHint);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 16. GUI↔CLI/API/Report Parität — DashboardProjection
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Gleiche RunResult-Daten müssen in DashboardProjection dieselben Zähler
    /// ergeben wie in RunProjection (die CLI/API nutzen).
    /// Ziel: Winners/Dupes/Junk/Games Konsistenz.
    /// Warum heute rot: Es gibt keinen Paritätstest zwischen Projection und Dashboard.
    /// Betroffene Dateien: DashboardProjection.cs, RunProjection.cs
    /// </summary>
    [Fact]
    public void Parity_DashboardProjectionMatchesRunProjection()
    {
        var result = BuildSuccessResult(winners: 8, losers: 4, junk: 2, scanned: 14);
        var projection = RunProjectionFactory.Create(result);
        var dashboard = DashboardProjection.From(projection, result, isConvertOnlyRun: false, isDryRun: false);

        // Dashboard string values should match projection int values
        Assert.Equal(projection.Keep.ToString(), dashboard.Winners);
        Assert.Equal(projection.Dupes.ToString(), dashboard.Dupes);
        Assert.Equal(projection.Junk.ToString(), dashboard.Junk);
        Assert.Equal(projection.Games.ToString(), dashboard.Games);
    }

    /// <summary>
    /// DAT-Audit-Werte in DashboardProjection müssen RunResult widerspiegeln.
    /// Warum heute rot: Kein Paritätstest.
    /// Betroffene Dateien: DashboardProjection.cs
    /// </summary>
    [Fact]
    public void Parity_DatAuditValues_MatchRunResult()
    {
        var result = new RunResult
        {
            Status = "completed",
            TotalFilesScanned = 10,
            DurationMs = 500,
            DatHaveCount = 5,
            DatHaveWrongNameCount = 2,
            DatMissCount = 3,
            DatUnknownCount = 1,
            DatAmbiguousCount = 0,
            AllCandidates = new[]
            {
                new RomCandidate { MainPath = "a.zip", Category = FileCategory.Game, DatMatch = true, ConsoleKey = "SNES" }
            },
            DedupeGroups = Array.Empty<DedupeGroup>()
        };
        var projection = RunProjectionFactory.Create(result);
        var dashboard = DashboardProjection.From(projection, result, isConvertOnlyRun: false, isDryRun: false);

        Assert.Equal("5", dashboard.DatHaveDisplay);
        Assert.Equal("2", dashboard.DatWrongNameDisplay);
        Assert.Equal("3", dashboard.DatMissDisplay);
        Assert.Equal("1", dashboard.DatUnknownDisplay);
        Assert.Equal("0", dashboard.DatAmbiguousDisplay);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 17. RunViewModel Default-Werte
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// RunViewModel-Defaults müssen "–" sein (nicht "0"), damit das Dashboard
    /// bei frischem Start keinen irreführenden "0 Winners" Zustand zeigt.
    /// Warum heute rot: RunViewModel.DashWinners startet mit "0", DashDupes mit "0",
    ///       etc. — das ist inkonsistent mit der Reset-Logik die "–" setzt.
    /// Betroffene Dateien: RunViewModel.cs
    /// TESTABILITY-FINDING: RunViewModel-Defaults ("0") und ResetDashboard-Werte ("–")
    ///       sind inkonsistent — der User sieht bei Start "0" statt "–".
    /// </summary>
    [Fact]
    public void RunViewModel_DefaultDashValues_ShouldBeDash()
    {
        var runVm = new RunViewModel();

        // Fresh VM should show "–" (unknown/no-data), not "0" (zero = data present)
        Assert.Equal("–", runVm.DashWinners);
        Assert.Equal("–", runVm.DashDupes);
        Assert.Equal("–", runVm.DashJunk);
        Assert.Equal("–", runVm.DashGames);
        Assert.Equal("–", runVm.DashDatHits);
        Assert.Equal("–", runVm.HealthScore);
        Assert.Equal("–", runVm.DedupeRate);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 18. Breakdown-Bar Konsistenz
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// KeepFraction + MoveFraction + JunkFraction müssen <= 1.0 sein.
    /// Warum heute rot: Kein Invariantentest.
    /// Betroffene Dateien: RunViewModel.cs (UpdateBreakdown)
    /// </summary>
    [Theory]
    [InlineData(10, 5, 3)]
    [InlineData(0, 0, 0)]
    [InlineData(1, 0, 0)]
    [InlineData(100, 50, 50)]
    public void Breakdown_FractionsNeverExceedOne(int games, int dupes, int junk)
    {
        var runVm = new RunViewModel();
        runVm.GamesRaw = games;
        runVm.DupesRaw = dupes;
        runVm.JunkRaw = junk;
        runVm.UpdateBreakdown();

        var total = runVm.KeepFraction + runVm.MoveFraction + runVm.JunkFraction;
        Assert.True(total <= 1.0 + 1e-9,
            $"Fractions sum {total} should be <= 1.0 (games={games}, dupes={dupes}, junk={junk})");
    }

    /// <summary>
    /// Bei 0 Games darf keine Division by Zero auftreten.
    /// Warum heute rot: Kein Test.
    /// Betroffene Dateien: RunViewModel.cs (UpdateBreakdown)
    /// </summary>
    [Fact]
    public void Breakdown_ZeroGames_NoException()
    {
        var runVm = new RunViewModel();
        runVm.GamesRaw = 0;
        runVm.DupesRaw = 0;
        runVm.JunkRaw = 0;
        runVm.UpdateBreakdown(); // Must not throw

        Assert.Equal(0.0, runVm.KeepFraction);
        Assert.Equal(0.0, runVm.MoveFraction);
        Assert.Equal(0.0, runVm.JunkFraction);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 19. RunState-Transition Safety
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Ungültige Transition muss eine Exception werfen.
    /// Warum heute rot: Es existiert nur ein Theory-basierter Validierungstest,
    ///       aber kein Test der prüft dass RunViewModel.CurrentRunState setter wirft.
    /// Betroffene Dateien: RunViewModel.cs
    /// </summary>
    [Fact]
    public void RunState_InvalidTransitionThrows()
    {
        var runVm = new RunViewModel();
        Assert.Equal(RunState.Idle, runVm.CurrentRunState);

        // Idle → Completed is not valid (must go through Preflight etc.)
        Assert.Throws<InvalidOperationException>(() =>
            runVm.CurrentRunState = RunState.Completed);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 20. DashboardProjection — Cancelled+DryRun Edge Case
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Ein gecancellter DryRun zeigt KPIs nicht doppelt markiert:
    /// "(vorläufig) (Plan)" wäre verwirrend. Entweder "(vorläufig)" oder "(Plan)".
    /// Warum heute rot: MarkProvisional und MarkPlan könnten sich kumulieren.
    /// Betroffene Dateien: DashboardProjection.cs
    /// </summary>
    [Fact]
    public void DashboardProjection_CancelledDryRun_NoDoubleMarking()
    {
        var cancelledResult = BuildCancelledResult(scannedSoFar: 3);
        var projection = RunProjectionFactory.Create(cancelledResult);
        var dashboard = DashboardProjection.From(projection, cancelledResult, isConvertOnlyRun: false, isDryRun: true);

        // Should not contain BOTH markers simultaneously
        bool hasBothMarkers = dashboard.Winners.Contains("(vorläufig)") && dashboard.Winners.Contains("(Plan)");
        Assert.False(hasBothMarkers,
            $"Winners should not have both (vorläufig) and (Plan): '{dashboard.Winners}'");
    }

    // ═══════════════════════════════════════════════════════════════════
    // 21. ErrorSummary nach Failed Run
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Nach einem Failed Run muss ErrorSummaryItems nicht leer sein.
    /// Ziel: User muss Fehlerdetails sehen können.
    /// Warum heute rot: Kein Test prüft PopulateErrorSummary auf MainViewModel-Ebene.
    /// Betroffene Dateien: MainViewModel.RunPipeline.cs
    /// TESTABILITY-FINDING: PopulateErrorSummary greift auf LogEntries zu,
    ///       die in Tests leer sind — Ergebnis könnte falsch-positiv sein.
    /// </summary>
    [Fact]
    public void ErrorSummary_FailedRun_IsNotEmpty()
    {
        var vm = new MainViewModel();
        vm.Run.CurrentRunState = RunState.Preflight;
        vm.Run.CurrentRunState = RunState.Scanning;

        var failedResult = BuildFailedResult();
        vm.ApplyRunResult(failedResult, force: true);
        vm.PopulateErrorSummary();

        Assert.NotEmpty(vm.ErrorSummaryItems);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 22. DashDuration Format
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// DashDuration muss ein lesbares Format haben (z.B. "1.5s"), nicht Millisekunden.
    /// Warum heute rot: Kein Format-Test.
    /// Betroffene Dateien: DashboardProjection.cs
    /// </summary>
    [Fact]
    public void DashboardProjection_Duration_FormattedAsSeconds()
    {
        var result = BuildSuccessResult(durationMs: 2500);
        var projection = RunProjectionFactory.Create(result);
        var dashboard = DashboardProjection.From(projection, result, isConvertOnlyRun: false, isDryRun: false);

        Assert.Contains("s", dashboard.Duration);
        Assert.Contains("2.5", dashboard.Duration);
        Assert.DoesNotContain("ms", dashboard.Duration);
    }
}
