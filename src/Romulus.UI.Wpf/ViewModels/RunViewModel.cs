using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using Romulus.Contracts.Models;
using Romulus.UI.Wpf.Models;
using RunState = Romulus.UI.Wpf.Models.RunState;

namespace Romulus.UI.Wpf.ViewModels;

/// <summary>
/// GUI-024/025: Run pipeline + dashboard ViewModel — extracted from MainViewModel.RunPipeline.cs.
/// Manages RunState machine, dashboard counters, analyse data (console distribution, dedupe groups).
/// Progress, status indicators and tool labels are owned by MainViewModel.RunPipeline.cs.
/// </summary>
public sealed class RunViewModel : ObservableObject
{
    private readonly object _collectionLock = new();

    /// <summary>Raised when command CanExecute should be re-evaluated.</summary>
    public event Action? CommandRequeryRequested;

    public RunViewModel()
    {
        BindingOperations.EnableCollectionSynchronization(ConsoleDistribution, _collectionLock);
        BindingOperations.EnableCollectionSynchronization(DedupeGroupItems, _collectionLock);
        DedupeGroupItemsView = CollectionViewSource.GetDefaultView(DedupeGroupItems);
        DedupeGroupItemsView.Filter = FilterDedupeGroupItem;
    }

    // ═══ RUN RESULT STATE ═══════════════════════════════════════════════

    private ObservableCollection<RomCandidate> _lastCandidates = [];
    public ObservableCollection<RomCandidate> LastCandidates
    {
        get => _lastCandidates;
        set { _lastCandidates = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasRunData)); }
    }

    // ═══ RUN STATE (explicit state machine) ═════════════════════════════
    private RunState _runState = RunState.Idle;
    public RunState CurrentRunState
    {
        get => _runState;
        set
        {
            if (!RunStateMachine.IsValidTransition(_runState, value))
                throw new InvalidOperationException(
                    $"RF-007: Invalid RunState transition {_runState} → {value}");

            if (SetProperty(ref _runState, value))
            {
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(IsIdle));
                OnPropertyChanged(nameof(HasRunResult));
                CommandRequeryRequested?.Invoke();
            }
        }
    }

    public bool IsBusy => _runState is RunState.Preflight or RunState.Scanning
        or RunState.Deduplicating or RunState.Sorting or RunState.Moving or RunState.Converting;
    public bool IsIdle => !IsBusy;
    public bool HasRunResult =>
        _runState is RunState.Completed or RunState.CompletedDryRun
        || (_runState is RunState.Cancelled or RunState.Failed
            && (_lastCandidates.Count > 0));

    public bool HasRunData => _lastCandidates.Count > 0;

    // ═══ DASHBOARD COUNTERS ═════════════════════════════════════════════
    private string _dashMode = "–";
    public string DashMode { get => _dashMode; set => SetProperty(ref _dashMode, value); }

    private string _dashWinners = "–";
    public string DashWinners { get => _dashWinners; set => SetProperty(ref _dashWinners, value); }

    private string _dashDupes = "–";
    public string DashDupes { get => _dashDupes; set => SetProperty(ref _dashDupes, value); }

    private string _dashJunk = "–";
    public string DashJunk { get => _dashJunk; set => SetProperty(ref _dashJunk, value); }

    private string _dashDuration = "00:00";
    public string DashDuration { get => _dashDuration; set => SetProperty(ref _dashDuration, value); }

    private string _healthScore = "–";
    public string HealthScore { get => _healthScore; set => SetProperty(ref _healthScore, value); }

    private string _dashGames = "–";
    public string DashGames { get => _dashGames; set => SetProperty(ref _dashGames, value); }

    private string _dashDatHits = "–";
    public string DashDatHits { get => _dashDatHits; set => SetProperty(ref _dashDatHits, value); }

    private string _dashDatHave = "–";
    public string DashDatHave { get => _dashDatHave; set => SetProperty(ref _dashDatHave, value); }

    private string _dashDatWrongName = "–";
    public string DashDatWrongName { get => _dashDatWrongName; set => SetProperty(ref _dashDatWrongName, value); }

    private string _dashDatMiss = "–";
    public string DashDatMiss { get => _dashDatMiss; set => SetProperty(ref _dashDatMiss, value); }

    private string _dashDatUnknown = "–";
    public string DashDatUnknown { get => _dashDatUnknown; set => SetProperty(ref _dashDatUnknown, value); }

    private string _dashDatAmbiguous = "–";
    public string DashDatAmbiguous { get => _dashDatAmbiguous; set => SetProperty(ref _dashDatAmbiguous, value); }

    private string _dashDatRenameProposed = "–";
    public string DashDatRenameProposed { get => _dashDatRenameProposed; set => SetProperty(ref _dashDatRenameProposed, value); }

    private string _dashDatRenameExecuted = "–";
    public string DashDatRenameExecuted { get => _dashDatRenameExecuted; set => SetProperty(ref _dashDatRenameExecuted, value); }

    private string _dashDatRenameFailed = "–";
    public string DashDatRenameFailed { get => _dashDatRenameFailed; set => SetProperty(ref _dashDatRenameFailed, value); }

    private string _dashConverted = "–";
    public string DashConverted { get => _dashConverted; set => SetProperty(ref _dashConverted, value); }

    private string _dashConvertBlocked = "–";
    public string DashConvertBlocked { get => _dashConvertBlocked; set => SetProperty(ref _dashConvertBlocked, value); }

    private string _dashConvertReview = "–";
    public string DashConvertReview { get => _dashConvertReview; set => SetProperty(ref _dashConvertReview, value); }

    private string _dashConvertSaved = "–";
    public string DashConvertSaved { get => _dashConvertSaved; set => SetProperty(ref _dashConvertSaved, value); }

    private string _dedupeRate = "–";
    public string DedupeRate { get => _dedupeRate; set => SetProperty(ref _dedupeRate, value); }

    // ═══ RAW CHART DATA (int values for charts, unaffected by display formatting) ═
    public int GamesRaw { get; set; }
    public int DupesRaw { get; set; }
    public int JunkRaw { get; set; }

    // ═══ BREAKDOWN BAR FRACTIONS (0.0–1.0) ══════════════════════════════
    private int _keepCount;
    public int KeepCount { get => _keepCount; set => SetProperty(ref _keepCount, value); }

    private int _moveCount;
    public int MoveCount { get => _moveCount; set => SetProperty(ref _moveCount, value); }

    private int _junkCount;
    public int JunkCount { get => _junkCount; set => SetProperty(ref _junkCount, value); }

    private double _keepFraction;
    public double KeepFraction { get => _keepFraction; set => SetProperty(ref _keepFraction, value); }

    private double _moveFraction;
    public double MoveFraction { get => _moveFraction; set => SetProperty(ref _moveFraction, value); }

    private double _junkFraction;
    public double JunkFraction { get => _junkFraction; set => SetProperty(ref _junkFraction, value); }

    public void UpdateBreakdown()
    {
        var total = GamesRaw;
        var dupes = DupesRaw;
        var junk = JunkRaw;
        var keep = Math.Max(0, total - dupes - junk);

        KeepCount = keep;
        MoveCount = dupes;
        JunkCount = junk;

        var divisor = Math.Max(1, total);
        KeepFraction = (double)keep / divisor;
        MoveFraction = (double)dupes / divisor;
        JunkFraction = (double)junk / divisor;
    }

    // ═══ RUN SUMMARY ════════════════════════════════════════════════════
    private string _runSummaryText = "";
    public string RunSummaryText
    {
        get => _runSummaryText;
        set
        {
            if (SetProperty(ref _runSummaryText, value))
                OnPropertyChanged(nameof(HasRunSummary));
        }
    }

    private UiErrorSeverity _runSummarySeverity = UiErrorSeverity.Info;
    public UiErrorSeverity RunSummarySeverity
    {
        get => _runSummarySeverity;
        set => SetProperty(ref _runSummarySeverity, value);
    }

    public bool HasRunSummary => !string.IsNullOrWhiteSpace(RunSummaryText);

    // ═══ DISPLAY STATE ══════════════════════════════════════════════════
    private bool _isResultPerfDetailsExpanded = true;
    public bool IsResultPerfDetailsExpanded
    {
        get => _isResultPerfDetailsExpanded;
        set => SetProperty(ref _isResultPerfDetailsExpanded, value);
    }

    private string _moveConsequenceText = "";
    public string MoveConsequenceText { get => _moveConsequenceText; set => SetProperty(ref _moveConsequenceText, value); }

    // ═══ ANALYSE DATA ═══════════════════════════════════════════════════
    public ObservableCollection<ConsoleDistributionItem> ConsoleDistribution { get; } = [];
    public ObservableCollection<DedupeGroupItem> DedupeGroupItems { get; } = [];
    public ICollectionView DedupeGroupItemsView { get; }

    private string _decisionSearchText = "";
    public string DecisionSearchText
    {
        get => _decisionSearchText;
        set
        {
            if (SetProperty(ref _decisionSearchText, value))
                DedupeGroupItemsView.Refresh();
        }
    }

    private bool FilterDedupeGroupItem(object obj)
    {
        if (obj is not DedupeGroupItem group)
            return false;

        var term = _decisionSearchText.Trim();
        if (string.IsNullOrEmpty(term))
            return true;

        return group.GameKey.Contains(term, StringComparison.OrdinalIgnoreCase)
            || group.Winner.FileName.Contains(term, StringComparison.OrdinalIgnoreCase)
            || group.Losers.Any(l => l.FileName.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
