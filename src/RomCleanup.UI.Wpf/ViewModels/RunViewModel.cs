using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.Orchestration;
using RomCleanup.UI.Wpf.Models;
using RomCleanup.UI.Wpf.Services;
using RunState = RomCleanup.UI.Wpf.Models.RunState;

namespace RomCleanup.UI.Wpf.ViewModels;

/// <summary>
/// GUI-024/025: Run pipeline + dashboard ViewModel — extracted from MainViewModel.RunPipeline.cs.
/// Manages RunState machine, progress, rollback, status indicators, dashboard counters.
/// </summary>
public sealed class RunViewModel : ObservableObject
{
    private readonly ILocalizationService _loc;

    /// <summary>Raised when command CanExecute should be re-evaluated.</summary>
    public event Action? CommandRequeryRequested;

    public RunViewModel(ILocalizationService? loc = null)
    {
        _loc = loc ?? new LocalizationService();
    }

    // ═══ RUN RESULT STATE ═══════════════════════════════════════════════
    private int _runLogStartIndex;

    private ObservableCollection<RomCandidate> _lastCandidates = [];
    public ObservableCollection<RomCandidate> LastCandidates
    {
        get => _lastCandidates;
        set { _lastCandidates = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasRunData)); }
    }

    private ObservableCollection<DedupeGroup> _lastDedupeGroups = [];
    public ObservableCollection<DedupeGroup> LastDedupeGroups
    {
        get => _lastDedupeGroups;
        set { _lastDedupeGroups = value; OnPropertyChanged(); }
    }

    private RunResult? _lastRunResult;
    public RunResult? LastRunResult
    {
        get => _lastRunResult;
        set { _lastRunResult = value; OnPropertyChanged(); }
    }

    private string? _lastAuditPath;
    public string? LastAuditPath
    {
        get => _lastAuditPath;
        set { _lastAuditPath = value; OnPropertyChanged(); }
    }

    // ═══ RUN STATE (explicit state machine) ═════════════════════════════
    private RunState _runState = RunState.Idle;
    public RunState CurrentRunState
    {
        get => _runState;
        set
        {
            if (!IsValidTransition(_runState, value))
                throw new InvalidOperationException(
                    $"RF-007: Invalid RunState transition {_runState} → {value}");

            if (SetProperty(ref _runState, value))
            {
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(IsIdle));
                OnPropertyChanged(nameof(ShowStartMoveButton));
                OnPropertyChanged(nameof(HasRunResult));
                CommandRequeryRequested?.Invoke();
            }
        }
    }

    internal static bool IsValidTransition(RunState from, RunState to)
        => RunStateMachine.IsValidTransition(from, to);

    public bool IsBusy => _runState is RunState.Preflight or RunState.Scanning
        or RunState.Deduplicating or RunState.Sorting or RunState.Moving or RunState.Converting;
    public bool IsIdle => !IsBusy;
    public bool ShowStartMoveButton => _runState == RunState.CompletedDryRun && !IsBusy;
    public bool HasRunResult => _runState is RunState.Completed or RunState.CompletedDryRun;

    // ═══ ROLLBACK HISTORY ═══════════════════════════════════════════════
    private const int MaxRollbackDepth = 50;
    private readonly Stack<string> _rollbackUndoStack = new();
    private readonly Stack<string> _rollbackRedoStack = new();

    public bool HasRollbackUndo => _rollbackUndoStack.Count > 0;
    public bool HasRollbackRedo => _rollbackRedoStack.Count > 0;

    public void PushRollbackUndo(string auditPath)
    {
        _rollbackUndoStack.Push(auditPath);
        _rollbackRedoStack.Clear();
        while (_rollbackUndoStack.Count > MaxRollbackDepth)
        {
            var items = _rollbackUndoStack.ToArray();
            _rollbackUndoStack.Clear();
            for (int i = MaxRollbackDepth - 1; i >= 0; i--)
                _rollbackUndoStack.Push(items[i]);
            break;
        }
        OnPropertyChanged(nameof(HasRollbackUndo));
        OnPropertyChanged(nameof(HasRollbackRedo));
    }

    public string? PopRollbackUndo()
    {
        if (_rollbackUndoStack.Count == 0) return null;
        var path = _rollbackUndoStack.Pop();
        _rollbackRedoStack.Push(path);
        OnPropertyChanged(nameof(HasRollbackUndo));
        OnPropertyChanged(nameof(HasRollbackRedo));
        return path;
    }

    public string? PopRollbackRedo()
    {
        if (_rollbackRedoStack.Count == 0) return null;
        var path = _rollbackRedoStack.Pop();
        _rollbackUndoStack.Push(path);
        OnPropertyChanged(nameof(HasRollbackUndo));
        OnPropertyChanged(nameof(HasRollbackRedo));
        return path;
    }

    // ═══ PROGRESS & PERFORMANCE ═════════════════════════════════════════
    private double _progress;
    public double Progress
    {
        get => _progress;
        set
        {
            var clamped = Math.Clamp(value, 0d, 100d);
            SetProperty(ref _progress, clamped);
        }
    }

    private string _progressText = "";
    public string ProgressText { get => _progressText; set => SetProperty(ref _progressText, value); }

    private string _perfPhase = "Phase: –";
    public string PerfPhase { get => _perfPhase; set => SetProperty(ref _perfPhase, value); }

    private string _perfFile = "Datei: –";
    public string PerfFile { get => _perfFile; set => SetProperty(ref _perfFile, value); }

    private string _busyHint = "";
    public string BusyHint { get => _busyHint; set => SetProperty(ref _busyHint, value); }

    // ═══ MISC UI STATE ══════════════════════════════════════════════════
    private string? _selectedRoot;
    public string? SelectedRoot
    {
        get => _selectedRoot;
        set { SetProperty(ref _selectedRoot, value); CommandRequeryRequested?.Invoke(); }
    }

    private bool _canRollback;
    public bool CanRollback
    {
        get => _canRollback;
        set { SetProperty(ref _canRollback, value); CommandRequeryRequested?.Invoke(); }
    }

    private string _lastReportPath = "";
    public string LastReportPath
    {
        get => _lastReportPath;
        set { SetProperty(ref _lastReportPath, value); CommandRequeryRequested?.Invoke(); }
    }

    private bool _showDryRunBanner = true;
    public bool ShowDryRunBanner { get => _showDryRunBanner; set => SetProperty(ref _showDryRunBanner, value); }

    private bool _showMoveCompleteBanner;
    public bool ShowMoveCompleteBanner { get => _showMoveCompleteBanner; set => SetProperty(ref _showMoveCompleteBanner, value); }

    // ═══ STATUS INDICATORS ══════════════════════════════════════════════
    private string _statusRoots = "";
    public string StatusRoots { get => _statusRoots; set => SetProperty(ref _statusRoots, value); }

    private string _statusTools = "";
    public string StatusTools { get => _statusTools; set => SetProperty(ref _statusTools, value); }

    private string _statusDat = "";
    public string StatusDat { get => _statusDat; set => SetProperty(ref _statusDat, value); }

    private string _statusReady = "";
    public string StatusReady { get => _statusReady; set => SetProperty(ref _statusReady, value); }

    private string _statusRuntime = "";
    public string StatusRuntime { get => _statusRuntime; set => SetProperty(ref _statusRuntime, value); }

    private StatusLevel _rootsStatusLevel = StatusLevel.Missing;
    public StatusLevel RootsStatusLevel { get => _rootsStatusLevel; set => SetProperty(ref _rootsStatusLevel, value); }

    private StatusLevel _toolsStatusLevel = StatusLevel.Missing;
    public StatusLevel ToolsStatusLevel { get => _toolsStatusLevel; set => SetProperty(ref _toolsStatusLevel, value); }

    private StatusLevel _datStatusLevel = StatusLevel.Missing;
    public StatusLevel DatStatusLevel { get => _datStatusLevel; set => SetProperty(ref _datStatusLevel, value); }

    private StatusLevel _readyStatusLevel = StatusLevel.Missing;
    public StatusLevel ReadyStatusLevel { get => _readyStatusLevel; set => SetProperty(ref _readyStatusLevel, value); }

    // ═══ TOOL STATUS LABELS ═════════════════════════════════════════════
    private string _chdmanStatusText = "–";
    public string ChdmanStatusText { get => _chdmanStatusText; set => SetProperty(ref _chdmanStatusText, value); }

    private string _dolphinStatusText = "–";
    public string DolphinStatusText { get => _dolphinStatusText; set => SetProperty(ref _dolphinStatusText, value); }

    private string _sevenZipStatusText = "–";
    public string SevenZipStatusText { get => _sevenZipStatusText; set => SetProperty(ref _sevenZipStatusText, value); }

    private string _psxtractStatusText = "–";
    public string PsxtractStatusText { get => _psxtractStatusText; set => SetProperty(ref _psxtractStatusText, value); }

    private string _cisoStatusText = "–";
    public string CisoStatusText { get => _cisoStatusText; set => SetProperty(ref _cisoStatusText, value); }

    // ═══ DASHBOARD COUNTERS ═════════════════════════════════════════════
    private string _dashMode = "–";
    public string DashMode { get => _dashMode; set => SetProperty(ref _dashMode, value); }

    private string _dashWinners = "0";
    public string DashWinners { get => _dashWinners; set => SetProperty(ref _dashWinners, value); }

    private string _dashDupes = "0";
    public string DashDupes { get => _dashDupes; set => SetProperty(ref _dashDupes, value); }

    private string _dashJunk = "0";
    public string DashJunk { get => _dashJunk; set => SetProperty(ref _dashJunk, value); }

    private string _dashDuration = "00:00";
    public string DashDuration { get => _dashDuration; set => SetProperty(ref _dashDuration, value); }

    private string _healthScore = "–";
    public string HealthScore { get => _healthScore; set => SetProperty(ref _healthScore, value); }

    private string _dashGames = "0";
    public string DashGames { get => _dashGames; set => SetProperty(ref _dashGames, value); }

    private string _dashDatHits = "0";
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

    private string _dedupeRate = "–";
    public string DedupeRate { get => _dedupeRate; set => SetProperty(ref _dedupeRate, value); }

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

    public string RollbackActionHint => CanRollback
        ? "Letzten Move-Lauf rückgängig machen (Ctrl+Z)"
        : "Kein Rollback möglich: Es wurde keine gültige Audit-Datei gefunden.";

    public bool HasRunData => _lastCandidates.Count > 0;

    // ═══ ANALYSE DATA ═══════════════════════════════════════════════════
    public ObservableCollection<ConsoleDistributionItem> ConsoleDistribution { get; } = [];
    public ObservableCollection<DedupeGroupItem> DedupeGroupItems { get; } = [];

    private string _moveConsequenceText = "";
    public string MoveConsequenceText { get => _moveConsequenceText; set => SetProperty(ref _moveConsequenceText, value); }

    // ═══ STEP INDICATOR ═════════════════════════════════════════════════
    private int _currentStep;
    public int CurrentStep { get => _currentStep; set => SetProperty(ref _currentStep, value); }

    public string GetPhaseDetail(int phase) => phase switch
    {
        1 => HasRunResult && LastRunResult is { } r
            ? _loc.Format(r.Preflight?.ShouldReturn != true ? "Run.PreflightOk" : "Run.PreflightBlocked", r.Preflight?.Reason ?? "")
            : _loc["Run.PreflightDesc"],
        2 => HasRunResult && LastRunResult is { } r2
            ? _loc.Format("Run.ScanDone", r2.TotalFilesScanned)
            : _loc["Run.ScanDesc"],
        3 => HasRunResult && LastRunResult is { } r3
            ? _loc.Format("Run.DedupeDone", r3.WinnerCount, r3.LoserCount)
            : _loc["Run.DedupeDesc"],
        4 => _loc["Run.SortDesc"],
        5 => HasRunResult && LastRunResult?.MoveResult is { } mv
            ? _loc.Format("Run.MoveDone", mv.MoveCount, mv.FailCount)
            : _loc["Run.MoveDesc"],
        6 => HasRunResult && LastRunResult is { } r6 && r6.ConvertedCount > 0
            ? _loc.Format("Run.ConvertDone", r6.ConvertedCount)
            : _loc["Run.ConvertDesc"],
        7 => HasRunResult ? _loc.Format("Run.AllDone", DashDuration) : _loc["Run.DoneDesc"],
        _ => ""
    };

    private string _stepLabel1 = "";
    public string StepLabel1 { get => _stepLabel1; set => SetProperty(ref _stepLabel1, value); }

    private string _stepLabel2 = "";
    public string StepLabel2 { get => _stepLabel2; set => SetProperty(ref _stepLabel2, value); }

    private string _stepLabel3 = "";
    public string StepLabel3 { get => _stepLabel3; set => SetProperty(ref _stepLabel3, value); }

    // ═══ CANCELLATION ═══════════════════════════════════════════════════
    private CancellationTokenSource? _cts;
    private readonly object _ctsLock = new();

    public CancellationToken CreateRunCancellation()
    {
        var newCts = new CancellationTokenSource();
        CancellationTokenSource? oldCts;
        lock (_ctsLock)
        {
            oldCts = _cts;
            _cts = newCts;
        }
        try { oldCts?.Dispose(); } catch (ObjectDisposedException) { }
        return newCts.Token;
    }

    public void CancelRun()
    {
        CancellationTokenSource? cts;
        lock (_ctsLock)
        {
            cts = _cts;
        }
        try { cts?.Cancel(); } catch (ObjectDisposedException) { }
        CurrentRunState = RunState.Cancelled;
        BusyHint = _loc["Run.CancelRequested"];
    }

    public void TransitionTo(RunState newState) => CurrentRunState = newState;

    // ═══ RESULT METHODS ═════════════════════════════════════════════════
    // R-03/R-04: ApplyRunResult removed — it contained a divergent HealthScore formula
    // (WinnerCount/Total vs FeatureService.CalculateHealthScore). XAML binds to
    // MainViewModel.ApplyRunResult which uses the correct FeatureService formula.

    /// <summary>Complete a run.</summary>
    public void CompleteRun(bool success, bool dryRun, string? reportPath = null)
    {
        BusyHint = "";
        if (reportPath is not null)
            LastReportPath = reportPath;

        if (CurrentRunState == RunState.Cancelled)
            return;

        if (success && dryRun)
        {
            CurrentRunState = RunState.CompletedDryRun;
        }
        else if (success && !dryRun)
        {
            CurrentRunState = RunState.Completed;
            CanRollback = true;
            ShowMoveCompleteBanner = true;
        }
        else
        {
            CurrentRunState = RunState.Failed;
        }
    }

    /// <summary>Refresh all status dot indicators based on current configuration.</summary>
    public void RefreshStatus(int rootCount, SetupViewModel setup)
    {
        bool hasRoots = rootCount > 0;
        RootsStatusLevel = hasRoots ? StatusLevel.Ok : StatusLevel.Missing;
        StatusRoots = hasRoots ? _loc.Format("Run.RootsConfigured", rootCount) : _loc["Run.NoFolders"];
        StepLabel1 = hasRoots ? _loc.Format("Run.FolderCount", rootCount) : _loc["Run.NoFolders"];

        bool hasChdman = !string.IsNullOrWhiteSpace(setup.ToolChdman) && File.Exists(setup.ToolChdman);
        bool has7z = !string.IsNullOrWhiteSpace(setup.Tool7z) && File.Exists(setup.Tool7z);
        bool hasDolphin = !string.IsNullOrWhiteSpace(setup.ToolDolphin) && File.Exists(setup.ToolDolphin);
        bool hasPsxtract = !string.IsNullOrWhiteSpace(setup.ToolPsxtract) && File.Exists(setup.ToolPsxtract);
        bool hasCiso = !string.IsNullOrWhiteSpace(setup.ToolCiso) && File.Exists(setup.ToolCiso);
        bool anyToolSpecified = !string.IsNullOrWhiteSpace(setup.ToolChdman) || !string.IsNullOrWhiteSpace(setup.Tool7z);
        int toolCount = (hasChdman ? 1 : 0) + (has7z ? 1 : 0) + (hasDolphin ? 1 : 0) + (hasPsxtract ? 1 : 0) + (hasCiso ? 1 : 0);
        ToolsStatusLevel = (hasChdman || has7z) ? StatusLevel.Ok
            : (anyToolSpecified || setup.ConvertEnabled) ? StatusLevel.Warning
            : StatusLevel.Missing;
        StatusTools = ToolsStatusLevel == StatusLevel.Ok ? _loc.Format("Run.ToolsFound", toolCount)
            : ToolsStatusLevel == StatusLevel.Warning ? _loc["Run.ToolsNotFound"] : _loc["Run.NoTools"];

        ChdmanStatusText = string.IsNullOrWhiteSpace(setup.ToolChdman) ? "–" : hasChdman ? _loc["Run.ToolFound"] : _loc["Run.ToolMissing"];
        DolphinStatusText = string.IsNullOrWhiteSpace(setup.ToolDolphin) ? "–" : hasDolphin ? _loc["Run.ToolFound"] : _loc["Run.ToolMissing"];
        SevenZipStatusText = string.IsNullOrWhiteSpace(setup.Tool7z) ? "–" : has7z ? _loc["Run.ToolFound"] : _loc["Run.ToolMissing"];
        PsxtractStatusText = string.IsNullOrWhiteSpace(setup.ToolPsxtract) ? "–" : hasPsxtract ? _loc["Run.ToolFound"] : _loc["Run.ToolMissing"];
        CisoStatusText = string.IsNullOrWhiteSpace(setup.ToolCiso) ? "–" : hasCiso ? _loc["Run.ToolFound"] : _loc["Run.ToolMissing"];

        bool datRootValid = !string.IsNullOrWhiteSpace(setup.DatRoot) && Directory.Exists(setup.DatRoot);
        DatStatusLevel = !setup.UseDat ? StatusLevel.Missing
            : datRootValid ? StatusLevel.Ok
            : !string.IsNullOrWhiteSpace(setup.DatRoot) ? StatusLevel.Warning
            : StatusLevel.Warning;
        StatusDat = DatStatusLevel == StatusLevel.Ok ? _loc["Run.DatActive"]
            : DatStatusLevel == StatusLevel.Warning ? _loc["Run.DatPathInvalid"] : _loc["Run.DatDisabled"];

        ReadyStatusLevel = !hasRoots ? StatusLevel.Blocked
            : ToolsStatusLevel == StatusLevel.Warning ? StatusLevel.Warning
            : StatusLevel.Ok;
        StatusReady = ReadyStatusLevel switch
        {
            StatusLevel.Ok => _loc["Run.Ready"],
            StatusLevel.Warning => _loc["Run.ReadyWarning"],
            StatusLevel.Blocked => _loc["Run.NotReady"],
            _ => ""
        };

        if (IsBusy)
        {
            CurrentStep = 2;
            StepLabel3 = _runState switch
            {
                RunState.Preflight => _loc["Run.PhaseChecking"],
                RunState.Scanning => _loc["Run.PhaseScanning"],
                RunState.Deduplicating => _loc["Run.PhaseDeduplicating"],
                RunState.Sorting => _loc["Run.PhaseSorting"],
                RunState.Moving => _loc["Run.PhaseMoving"],
                RunState.Converting => _loc["Run.PhaseConverting"],
                _ => _loc["Run.PhaseRunning"]
            };
        }
        else if (_runState is RunState.Completed or RunState.CompletedDryRun)
        {
            CurrentStep = 3;
            StepLabel3 = _runState == RunState.CompletedDryRun ? _loc["Run.PreviewDone"] : _loc["Run.Completed"];
        }
        else
        {
            CurrentStep = hasRoots ? 1 : 0;
            StepLabel3 = _loc["Run.PressF5"];
        }
    }

    /// <summary>Build the error summary items for the protocol tab.</summary>
    public void PopulateErrorSummary(
        ObservableCollection<UiError> errorSummaryItems,
        ObservableCollection<LogEntry> logEntries)
    {
        errorSummaryItems.Clear();
        _runLogStartIndex = Math.Min(_runLogStartIndex, logEntries.Count);

        var projected = ErrorSummaryProjection.Build(
            LastRunResult,
            LastCandidates,
            logEntries.Skip(_runLogStartIndex));

        foreach (var issue in projected)
            errorSummaryItems.Add(issue);
    }

    /// <summary>Set the log start index for the current run (used by PopulateErrorSummary).</summary>
    public void MarkRunLogStart(int logCount)
    {
        _runLogStartIndex = logCount;
    }

    // ═══ EVENTS ═════════════════════════════════════════════════════════
    public event EventHandler? RunRequested;

    public void RaiseRunRequested() => RunRequested?.Invoke(this, EventArgs.Empty);
}
