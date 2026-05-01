using System.Collections.ObjectModel;
using Romulus.Contracts.Models;
using Romulus.UI.Wpf.Models;
using Romulus.UI.Wpf.ViewModels;
using Xunit;
using RunState = Romulus.UI.Wpf.Models.RunState;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for RunViewModel: state transitions, progress clamping,
/// UpdateBreakdown, FilterDedupeGroupItem, dashboard counters, summary.
/// Dead members (CompleteRun, CancelRun, RefreshStatus, PopulateErrorSummary,
/// GetPhaseDetail, status indicators, tool labels) were removed in GUI cleanup.
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

    // Note: Progress clamping is now on MainViewModel.RunPipeline.cs (not RunViewModel).
    // Tests for Progress clamping belong in GuiViewModelTests or a MainViewModel-scope test.

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

    #region State transitions

    [Fact]
    public void CurrentRunState_ValidTransition_Updates()
    {
        var vm = Create();
        vm.CurrentRunState = RunState.Preflight;
        Assert.Equal(RunState.Preflight, vm.CurrentRunState);
        Assert.True(vm.IsBusy);
        Assert.False(vm.IsIdle);
    }

    [Fact]
    public void CurrentRunState_InvalidTransition_Throws()
    {
        var vm = Create();
        Assert.Throws<InvalidOperationException>(() => vm.CurrentRunState = RunState.Moving);
    }

    #endregion

    #region Computed properties

    [Fact]
    public void IsBusy_ForAllBusyStates()
    {
        var busyStates = new[] { RunState.Preflight, RunState.Scanning, RunState.Deduplicating,
            RunState.Sorting, RunState.Moving, RunState.Converting };

        foreach (var busy in busyStates)
        {
            var vm = Create();
            vm.CurrentRunState = RunState.Preflight;
            if (busy == RunState.Preflight) { Assert.True(vm.IsBusy, $"Expected busy for {busy}"); continue; }
            vm.CurrentRunState = RunState.Scanning;
            if (busy == RunState.Scanning) { Assert.True(vm.IsBusy, $"Expected busy for {busy}"); continue; }
            vm.CurrentRunState = RunState.Deduplicating;
            if (busy == RunState.Deduplicating) { Assert.True(vm.IsBusy, $"Expected busy for {busy}"); continue; }
            vm.CurrentRunState = RunState.Sorting;
            if (busy == RunState.Sorting) { Assert.True(vm.IsBusy, $"Expected busy for {busy}"); continue; }
            vm.CurrentRunState = RunState.Moving;
            if (busy == RunState.Moving) { Assert.True(vm.IsBusy, $"Expected busy for {busy}"); continue; }
            vm.CurrentRunState = RunState.Converting;
            Assert.True(vm.IsBusy, $"Expected busy for {busy}");
        }
    }

    [Fact]
    public void HasRunResult_TrueForCompletedStates()
    {
        var vm = Create();
        Assert.False(vm.HasRunResult);
        vm.CurrentRunState = RunState.Preflight;
        vm.CurrentRunState = RunState.Completed;
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

    #region Events

    [Fact]
    public void CommandRequeryRequested_RaisedOnStateChange()
    {
        var vm = Create();
        bool raised = false;
        vm.CommandRequeryRequested += () => raised = true;
        vm.CurrentRunState = RunState.Preflight;
        Assert.True(raised);
    }

    #endregion

    #region Dashboard defaults

    [Fact]
    public void DashboardCounters_DefaultValues()
    {
        var vm = Create();
        Assert.Equal("–", vm.DashMode);
        Assert.Equal("–", vm.DashWinners);
        Assert.Equal("–", vm.DashDupes);
        Assert.Equal("–", vm.DashJunk);
        Assert.Equal("00:00", vm.DashDuration);
        Assert.Equal("–", vm.HealthScore);
    }

    [Fact]
    public void RunSummarySeverity_DefaultInfo()
    {
        Assert.Equal(UiErrorSeverity.Info, Create().RunSummarySeverity);
    }

    #endregion
}
