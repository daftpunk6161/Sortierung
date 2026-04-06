using System.Collections.ObjectModel;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Orchestration;
using RomCleanup.UI.Wpf.Models;
using RomCleanup.UI.Wpf.Services;
using RomCleanup.UI.Wpf.ViewModels;
using Xunit;
using RunState = RomCleanup.UI.Wpf.Models.RunState;

namespace RomCleanup.Tests;

/// <summary>
/// Coverage tests for RunViewModel: state transitions, progress clamping,
/// UpdateBreakdown, CompleteRun branches, FilterDedupeGroupItem, PopulateErrorSummary,
/// RefreshStatus, GetPhaseDetail.
/// </summary>
public sealed class RunViewModelCoverageTests
{
    private static RunViewModel Create() => new();

    private static DedupeEntryItem MakeEntry(string fileName, bool isWinner = false) => new()
    {
        FileName = fileName,
        Region = "EU",
        RegionScore = 100,
        FormatScore = 50,
        VersionScore = 1,
        DecisionClass = "StrongWin",
        EvidenceTier = "DatHash",
        PrimaryMatchKind = "ExactDatHash",
        PlatformFamily = "Nintendo",
        IsWinner = isWinner
    };

    private sealed class StubTheme : IThemeService
    {
        public AppTheme Current { get; private set; } = AppTheme.Dark;
        public bool IsDark => true;
        public IReadOnlyList<AppTheme> AvailableThemes { get; } = Enum.GetValues<AppTheme>();
        public void ApplyTheme(AppTheme theme) => Current = theme;
        public void ApplyTheme(bool dark) { }
        public void Toggle() { }
    }

    private sealed class StubDialog : IDialogService
    {
        public string? BrowseFolder(string t) => null;
        public string? BrowseFile(string t, string f) => null;
        public string? SaveFile(string t, string f, string? d) => null;
        public bool Confirm(string m, string t) => true;
        public void Info(string m, string t) { }
        public void Error(string m, string t) { }
        public ConfirmResult YesNoCancel(string m, string t) => ConfirmResult.Yes;
        public string ShowInputBox(string p, string t, string d) => d;
        public void ShowText(string t, string c) { }
        public bool DangerConfirm(string t, string m, string c, string b) => true;
        public bool ConfirmConversionReview(string t, string s, IReadOnlyList<Contracts.Models.ConversionReviewEntry> e) => true;
        public bool ConfirmDatRenamePreview(IReadOnlyList<Contracts.Models.DatAuditEntry> r) => true;
    }

    private sealed class StubSettings : ISettingsService
    {
        public string? LastAuditPath { get; set; }
        public string LastTheme { get; set; } = "Dark";
        public SettingsDto? Load() => null;
        public void LoadInto(MainViewModel vm) { }
        public bool SaveFrom(MainViewModel vm, string? lastAuditPath) => true;
    }

    private static SetupViewModel CreateSetup() => new(new StubTheme(), new StubDialog(), new StubSettings());

    #region Progress clamping

    [Fact]
    public void Progress_ClampsAbove100()
    {
        var vm = Create();
        vm.Progress = 150;
        Assert.Equal(100d, vm.Progress);
    }

    [Fact]
    public void Progress_ClampsBelow0()
    {
        var vm = Create();
        vm.Progress = -10;
        Assert.Equal(0d, vm.Progress);
    }

    [Fact]
    public void Progress_AcceptsValidValue()
    {
        var vm = Create();
        vm.Progress = 55.5;
        Assert.Equal(55.5, vm.Progress);
    }

    #endregion

    #region UpdateBreakdown

    [Fact]
    public void UpdateBreakdown_ZeroTotal_AllFractionsZero()
    {
        var vm = Create();
        vm.GamesRaw = 0;
        vm.DupesRaw = 0;
        vm.JunkRaw = 0;
        vm.UpdateBreakdown();
        Assert.Equal(0d, vm.KeepFraction);
        Assert.Equal(0d, vm.MoveFraction);
        Assert.Equal(0d, vm.JunkFraction);
    }

    [Fact]
    public void UpdateBreakdown_AllKeep()
    {
        var vm = Create();
        vm.GamesRaw = 100;
        vm.DupesRaw = 0;
        vm.JunkRaw = 0;
        vm.UpdateBreakdown();
        Assert.Equal(1d, vm.KeepFraction);
        Assert.Equal(0d, vm.MoveFraction);
        Assert.Equal(100, vm.KeepCount);
    }

    [Fact]
    public void UpdateBreakdown_MixedValues()
    {
        var vm = Create();
        vm.GamesRaw = 100;
        vm.DupesRaw = 30;
        vm.JunkRaw = 10;
        vm.UpdateBreakdown();
        Assert.Equal(60, vm.KeepCount);
        Assert.Equal(30, vm.MoveCount);
        Assert.Equal(10, vm.JunkCount);
        Assert.Equal(0.60, vm.KeepFraction, 2);
        Assert.Equal(0.30, vm.MoveFraction, 2);
        Assert.Equal(0.10, vm.JunkFraction, 2);
    }

    [Fact]
    public void UpdateBreakdown_DupesPlusJunkExceedTotal_KeepClampsToZero()
    {
        var vm = Create();
        vm.GamesRaw = 50;
        vm.DupesRaw = 40;
        vm.JunkRaw = 20;
        vm.UpdateBreakdown();
        Assert.Equal(0, vm.KeepCount); // Math.Max(0, 50-40-20)
    }

    #endregion

    #region State transitions (via RunViewModel)

    [Fact]
    public void TransitionTo_ValidPath_Updates()
    {
        var vm = Create();
        vm.TransitionTo(RunState.Preflight);
        Assert.Equal(RunState.Preflight, vm.CurrentRunState);
        Assert.True(vm.IsBusy);
        Assert.False(vm.IsIdle);
    }

    [Fact]
    public void TransitionTo_InvalidPath_Throws()
    {
        var vm = Create();
        Assert.Throws<InvalidOperationException>(() => vm.TransitionTo(RunState.Moving));
    }

    [Fact]
    public void CancelRun_FromBusy_SetsCancelled()
    {
        var vm = Create();
        vm.TransitionTo(RunState.Preflight);
        vm.CancelRun();
        Assert.Equal(RunState.Cancelled, vm.CurrentRunState);
        Assert.NotEmpty(vm.BusyHint);
    }

    #endregion

    #region Computed properties

    [Fact]
    public void IsBusy_ForAllBusyStates()
    {
        // Test each busy state by navigating through valid transitions
        var busyStates = new[] { RunState.Preflight, RunState.Scanning, RunState.Deduplicating,
            RunState.Sorting, RunState.Moving, RunState.Converting };

        foreach (var busy in busyStates)
        {
            var vm = Create();
            // Navigate through valid path to reach each state
            vm.TransitionTo(RunState.Preflight);
            if (busy == RunState.Preflight) { Assert.True(vm.IsBusy, $"Expected busy for {busy}"); continue; }
            vm.TransitionTo(RunState.Scanning);
            if (busy == RunState.Scanning) { Assert.True(vm.IsBusy, $"Expected busy for {busy}"); continue; }
            vm.TransitionTo(RunState.Deduplicating);
            if (busy == RunState.Deduplicating) { Assert.True(vm.IsBusy, $"Expected busy for {busy}"); continue; }
            vm.TransitionTo(RunState.Sorting);
            if (busy == RunState.Sorting) { Assert.True(vm.IsBusy, $"Expected busy for {busy}"); continue; }
            vm.TransitionTo(RunState.Moving);
            if (busy == RunState.Moving) { Assert.True(vm.IsBusy, $"Expected busy for {busy}"); continue; }
            vm.TransitionTo(RunState.Converting);
            Assert.True(vm.IsBusy, $"Expected busy for {busy}");
        }
    }

    [Fact]
    public void ShowStartMoveButton_OnlyWhenCompletedDryRun()
    {
        var vm = Create();
        Assert.False(vm.ShowStartMoveButton);
        vm.TransitionTo(RunState.Preflight);
        vm.TransitionTo(RunState.CompletedDryRun);
        Assert.True(vm.ShowStartMoveButton);
    }

    [Fact]
    public void HasRunResult_TrueForCompletedStates()
    {
        var vm = Create();
        Assert.False(vm.HasRunResult);
        vm.TransitionTo(RunState.Preflight);
        vm.TransitionTo(RunState.Completed);
        Assert.True(vm.HasRunResult);
    }

    [Fact]
    public void HasRunData_FalseWhenEmpty_TrueWithCandidates()
    {
        var vm = Create();
        Assert.False(vm.HasRunData);
        vm.LastCandidates = new ObservableCollection<RomCandidate>(
            [new RomCandidate { MainPath = @"C:\test.zip" }]);
        Assert.True(vm.HasRunData);
    }

    [Fact]
    public void HasRunSummary_FalseWhenEmpty_TrueWithText()
    {
        var vm = Create();
        Assert.False(vm.HasRunSummary);
        vm.RunSummaryText = "All good";
        Assert.True(vm.HasRunSummary);
    }

    [Fact]
    public void RollbackActionHint_ReflectsCanRollback()
    {
        var vm = Create();
        Assert.Contains("Kein Rollback", vm.RollbackActionHint);
        vm.CanRollback = true;
        Assert.Contains("rückgängig", vm.RollbackActionHint);
    }

    #endregion

    #region CompleteRun

    [Fact]
    public void CompleteRun_SuccessDryRun_CompletedDryRun()
    {
        var vm = Create();
        vm.TransitionTo(RunState.Preflight);
        vm.CompleteRun(true, true, @"C:\report.html");
        Assert.Equal(RunState.CompletedDryRun, vm.CurrentRunState);
        Assert.Equal(@"C:\report.html", vm.LastReportPath);
    }

    [Fact]
    public void CompleteRun_SuccessNonDryRun_CompletedWithRollback()
    {
        var vm = Create();
        vm.TransitionTo(RunState.Preflight);
        vm.CompleteRun(true, false);
        Assert.Equal(RunState.Completed, vm.CurrentRunState);
        Assert.True(vm.CanRollback);
        Assert.True(vm.ShowMoveCompleteBanner);
    }

    [Fact]
    public void CompleteRun_Failure_Failed()
    {
        var vm = Create();
        vm.TransitionTo(RunState.Preflight);
        vm.CompleteRun(false, false);
        Assert.Equal(RunState.Failed, vm.CurrentRunState);
    }

    [Fact]
    public void CompleteRun_AlreadyCancelled_RemainsCancel()
    {
        var vm = Create();
        vm.TransitionTo(RunState.Preflight);
        vm.CancelRun();
        vm.CompleteRun(true, true);
        Assert.Equal(RunState.Cancelled, vm.CurrentRunState);
    }

    [Fact]
    public void CompleteRun_NullReport_ClearsHintOnly()
    {
        var vm = Create();
        vm.TransitionTo(RunState.Preflight);
        vm.BusyHint = "working";
        vm.CompleteRun(true, true); // no reportPath
        Assert.Empty(vm.BusyHint);
    }

    #endregion

    #region FilterDedupeGroupItem

    [Fact]
    public void DecisionSearchText_EmptyMatchesAll()
    {
        var vm = Create();
        vm.DedupeGroupItems.Add(new DedupeGroupItem
        {
            GameKey = "Mario",
            Winner = MakeEntry("mario.zip", true),
            Losers = []
        });
        vm.DecisionSearchText = "";
        // All items should be visible
        Assert.True(vm.DedupeGroupItemsView.Cast<object>().Any());
    }

    [Fact]
    public void DecisionSearchText_FiltersByGameKey()
    {
        var vm = Create();
        vm.DedupeGroupItems.Add(new DedupeGroupItem
        {
            GameKey = "Mario",
            Winner = MakeEntry("mario.zip", true),
            Losers = []
        });
        vm.DedupeGroupItems.Add(new DedupeGroupItem
        {
            GameKey = "Zelda",
            Winner = MakeEntry("zelda.zip", true),
            Losers = []
        });
        vm.DecisionSearchText = "Mario";
        var visible = vm.DedupeGroupItemsView.Cast<DedupeGroupItem>().ToList();
        Assert.Single(visible);
        Assert.Equal("Mario", visible[0].GameKey);
    }

    [Fact]
    public void DecisionSearchText_FiltersByLoserFileName()
    {
        var vm = Create();
        vm.DedupeGroupItems.Add(new DedupeGroupItem
        {
            GameKey = "Game",
            Winner = MakeEntry("winner.zip", true),
            Losers = [MakeEntry("loser-eu.zip")]
        });
        vm.DecisionSearchText = "loser";
        Assert.Single(vm.DedupeGroupItemsView.Cast<DedupeGroupItem>().ToList());
    }

    #endregion

    #region PopulateErrorSummary

    [Fact]
    public void PopulateErrorSummary_NoResult_ProducesOk()
    {
        var vm = Create();
        var errors = new ObservableCollection<UiError>();
        var logs = new ObservableCollection<LogEntry>();
        vm.PopulateErrorSummary(errors, logs);
        // With no result and no logs, returns OK status
        Assert.True(errors.Count >= 1);
        Assert.Contains(errors, e => e.Severity == UiErrorSeverity.Info);
    }

    [Fact]
    public void PopulateErrorSummary_WithWarningLogs_AddsWarnings()
    {
        var vm = Create();
        vm.MarkRunLogStart(0);
        var errors = new ObservableCollection<UiError>();
        var logs = new ObservableCollection<LogEntry>
        {
            new("Something went WARN", "WARN"),
            new("An ERROR occurred", "ERROR")
        };
        vm.PopulateErrorSummary(errors, logs);
        Assert.True(errors.Count >= 2);
    }

    [Fact]
    public void MarkRunLogStart_SetsIndex()
    {
        var vm = Create();
        vm.MarkRunLogStart(42);
        var errors = new ObservableCollection<UiError>();
        var logs = new ObservableCollection<LogEntry>();
        for (int i = 0; i < 50; i++) logs.Add(new LogEntry($"Log {i}", "INFO"));
        vm.PopulateErrorSummary(errors, logs);
        // Should only process logs from index 42 onward (8 entries)
        Assert.NotEmpty(errors);
    }

    #endregion

    #region GetPhaseDetail

    [Fact]
    public void GetPhaseDetail_NoResult_ReturnsDescriptions()
    {
        var vm = Create();
        for (int p = 1; p <= 7; p++)
        {
            var detail = vm.GetPhaseDetail(p);
            Assert.NotEmpty(detail);
        }
    }

    [Fact]
    public void GetPhaseDetail_WithResult_ReturnsData()
    {
        var vm = Create();
        vm.TransitionTo(RunState.Preflight);
        vm.LastRunResult = new RunResult
        {
            Status = "ok",
            TotalFilesScanned = 100,
            WinnerCount = 60,
            LoserCount = 20,
            MoveResult = new MovePhaseResult(10, 0, 5000L)
        };
        vm.TransitionTo(RunState.Completed);
        var detail2 = vm.GetPhaseDetail(2);
        Assert.NotEmpty(detail2);
    }

    [Fact]
    public void GetPhaseDetail_OutOfRange_EmptyString()
    {
        var vm = Create();
        Assert.Empty(vm.GetPhaseDetail(99));
    }

    #endregion

    #region RefreshStatus

    [Fact]
    public void RefreshStatus_NoRoots_ShowsBlocked()
    {
        var vm = Create();
        var setup = CreateSetup();
        vm.RefreshStatus(0, setup);
        Assert.Equal(StatusLevel.Blocked, vm.ReadyStatusLevel);
        Assert.Equal(StatusLevel.Missing, vm.RootsStatusLevel);
    }

    [Fact]
    public void RefreshStatus_WithRoots_NoTools_ShowsReady()
    {
        var vm = Create();
        var setup = CreateSetup();
        vm.RefreshStatus(3, setup);
        Assert.Equal(StatusLevel.Ok, vm.RootsStatusLevel);
    }

    [Fact]
    public void RefreshStatus_DatDisabled_DatStatusMissing()
    {
        var vm = Create();
        var setup = CreateSetup();
        setup.UseDat = false;
        vm.RefreshStatus(1, setup);
        Assert.Equal(StatusLevel.Missing, vm.DatStatusLevel);
    }

    [Fact]
    public void RefreshStatus_WhenBusy_StepIs2()
    {
        var vm = Create();
        vm.TransitionTo(RunState.Preflight);
        var setup = CreateSetup();
        vm.RefreshStatus(1, setup);
        Assert.Equal(2, vm.CurrentStep);
    }

    [Fact]
    public void RefreshStatus_WhenCompleted_StepIs3()
    {
        var vm = Create();
        vm.TransitionTo(RunState.Preflight);
        vm.TransitionTo(RunState.Completed);
        var setup = CreateSetup();
        vm.RefreshStatus(1, setup);
        Assert.Equal(3, vm.CurrentStep);
    }

    #endregion

    #region Events

    [Fact]
    public void CommandRequeryRequested_RaisedOnStateChange()
    {
        var vm = Create();
        bool raised = false;
        vm.CommandRequeryRequested += () => raised = true;
        vm.TransitionTo(RunState.Preflight);
        Assert.True(raised);
    }

    [Fact]
    public void RaiseRunRequested_RaisesEvent()
    {
        var vm = Create();
        bool raised = false;
        vm.RunRequested += (_, _) => raised = true;
        vm.RaiseRunRequested();
        Assert.True(raised);
    }

    #endregion

    #region Property setters

    [Fact]
    public void LastRunResult_Setter_Changes()
    {
        var vm = Create();
        var result = new RunResult { Status = "ok" };
        vm.LastRunResult = result;
        Assert.Same(result, vm.LastRunResult);
    }

    [Fact]
    public void LastAuditPath_Setter_Changes()
    {
        var vm = Create();
        vm.LastAuditPath = @"C:\audit.csv";
        Assert.Equal(@"C:\audit.csv", vm.LastAuditPath);
    }

    [Fact]
    public void ProgressText_Setter_Changes()
    {
        var vm = Create();
        vm.ProgressText = "42%";
        Assert.Equal("42%", vm.ProgressText);
    }

    [Fact]
    public void PerfPhase_Default_HasDash()
    {
        Assert.Contains("–", Create().PerfPhase);
    }

    [Fact]
    public void DashboardCounters_DefaultValues()
    {
        var vm = Create();
        Assert.Equal("–", vm.DashMode);
        Assert.Equal("0", vm.DashWinners);
        Assert.Equal("0", vm.DashDupes);
        Assert.Equal("0", vm.DashJunk);
        Assert.Equal("00:00", vm.DashDuration);
        Assert.Equal("–", vm.HealthScore);
    }

    [Fact]
    public void ShowDryRunBanner_DefaultTrue()
    {
        Assert.True(Create().ShowDryRunBanner);
    }

    [Fact]
    public void SelectedRoot_RaisesCommandRequery()
    {
        var vm = Create();
        bool raised = false;
        vm.CommandRequeryRequested += () => raised = true;
        vm.SelectedRoot = @"C:\Roms";
        Assert.True(raised);
    }

    [Fact]
    public void RunSummarySeverity_DefaultInfo()
    {
        Assert.Equal(UiErrorSeverity.Info, Create().RunSummarySeverity);
    }

    #endregion
}
