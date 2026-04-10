using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows.Input;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Analysis;
using Romulus.Infrastructure.Conversion;
using Romulus.Infrastructure.Dat;
using Romulus.Infrastructure.Index;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Paths;
using Romulus.UI.Wpf.Models;
using Romulus.UI.Wpf.Services;
using RunState = Romulus.UI.Wpf.Models.RunState;

namespace Romulus.UI.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private static readonly HashSet<string> PreviewRelevantPropertyNames =
    [
        nameof(RemoveJunk),
        nameof(OnlyGames),
        nameof(KeepUnknownWhenOnlyGames),
        nameof(SortConsole),
        nameof(AliasKeying),
        nameof(AggressiveJunk),
        nameof(UseDat),
        nameof(EnableDatRename),
        nameof(ApproveReviews),
        nameof(ApproveConversionReview),
        nameof(DatRoot),
        nameof(DatHashType),
        nameof(ConvertEnabled),
        nameof(TrashRoot),
        nameof(AuditRoot),
        nameof(ToolChdman),
        nameof(ToolDolphin),
        nameof(Tool7z),
        nameof(ToolPsxtract),
        nameof(ToolCiso),
        nameof(ConflictPolicy),
        nameof(PreferEU),
        nameof(PreferUS),
        nameof(PreferJP),
        nameof(PreferWORLD),
        nameof(PreferDE),
        nameof(PreferFR),
        nameof(PreferIT),
        nameof(PreferES),
        nameof(PreferAU),
        nameof(PreferASIA),
        nameof(PreferKR),
        nameof(PreferCN),
        nameof(PreferBR),
        nameof(PreferNL),
        nameof(PreferSE),
        nameof(PreferSCAN),
        nameof(DryRun)
    ];

    // ═══ RUN RESULT STATE ═══════════════════════════════════════════════
    private int _runLogStartIndex;
    private int _isExecutingRun; // Single-flight guard for ExecuteRunAsync
    private ObservableCollection<RomCandidate> _lastCandidates = [];
    public ObservableCollection<RomCandidate> LastCandidates
    {
        get => _lastCandidates;
        set { _lastCandidates = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasRunData)); Run.LastCandidates = value; }
    }

    public bool HasRunData => Run.HasRunData;

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
        set
        {
            _lastRunResult = value;
            OnPropertyChanged();
            // HasRunResult depends on LastRunResult for Cancelled/Failed states
            if (Run.CurrentRunState is RunState.Cancelled or RunState.Failed)
                OnPropertyChanged(nameof(HasRunResult));
        }
    }

    private string? _lastAuditPath;
    public string? LastAuditPath
    {
        get => _lastAuditPath;
        set
        {
            _lastAuditPath = value;
            OnPropertyChanged();
            CanRollback = _runService.HasVerifiedRollback(_lastAuditPath);
            RefreshToolSurfaceState();
        }
    }

    private string? _lastSuccessfulPreviewFingerprint;
    public bool CanStartCurrentRun =>
        !IsBusy &&
        Roots.Count > 0 &&
        !HasBlockingValidationErrors &&
        (DryRun || CanStartMoveWithCurrentPreview);

    public bool CanStartMoveWithCurrentPreview =>
        !IsBusy &&
        Roots.Count > 0 &&
        Run.CurrentRunState == RunState.CompletedDryRun &&
        string.Equals(_lastSuccessfulPreviewFingerprint, BuildPreviewConfigurationFingerprint(), StringComparison.Ordinal);

    public string MoveApplyGateText => CanStartMoveWithCurrentPreview
        ? _loc["Run.MoveApplyGate.Unlocked"]
        : string.IsNullOrEmpty(_lastSuccessfulPreviewFingerprint)
            ? _loc["Run.MoveApplyGate.LockedNoPrev"]
            : _loc["Run.MoveApplyGate.LockedChanged"];

    /// <summary>
    /// TASK-176: Visible banner when config changed after DryRun.
    /// Shows when: preview fingerprint exists AND current config doesn't match.
    /// </summary>
    public bool ShowConfigChangedBanner =>
        !string.IsNullOrEmpty(_lastSuccessfulPreviewFingerprint) &&
        !string.Equals(_lastSuccessfulPreviewFingerprint, BuildPreviewConfigurationFingerprint(), StringComparison.Ordinal);

    public bool IsMovePhaseApplicable => !DryRun && !ConvertOnly;
    public bool IsConvertPhaseApplicable => ConvertOnly || (!DryRun && ConvertEnabled);

    public bool ShowSkippedPhaseInfo => !IsMovePhaseApplicable || !IsConvertPhaseApplicable;

    public string SkippedPhaseInfoText => (!IsMovePhaseApplicable, !IsConvertPhaseApplicable) switch
    {
        (true, true) => _loc["Phase.Skipped.MoveConvert"],
        (true, false) => _loc["Phase.Skipped.MoveOnly"],
        (false, true) => _loc["Phase.Skipped.ConvertOnly"],
        _ => string.Empty
    };

    // ═══ RUN STATE — delegated to RunViewModel (TASK-122 / ADR-0006) ══
    // RunState is owned exclusively by RunViewModel.
    // MainViewModel exposes delegation properties for XAML binding compatibility.

    public RunState CurrentRunState
    {
        get => Run.CurrentRunState;
        set => Run.CurrentRunState = value;
    }

    /// <summary>RF-007: Checks whether the state transition is valid.</summary>
    internal static bool IsValidTransition(RunState from, RunState to)
        => RunStateMachine.IsValidTransition(from, to);

    public bool IsBusy => Run.IsBusy;
    public bool IsIdle => Run.IsIdle;

    public bool ShowStartMoveButton => CanStartMoveWithCurrentPreview;

    public bool ShowResultMoveButton => ShowStartMoveButton && !ShowSmartActionBar;

    public bool ShowActionBarMoveButton => ShowStartMoveButton && ShowSmartActionBar;

    public bool CanExecuteInlineStartMove =>
        Shell.ShowMoveInlineConfirm && DateTime.UtcNow >= _inlineMoveUnlockAtUtc;

    public string InlineMoveConfirmHint =>
        !Shell.ShowMoveInlineConfirm
            ? string.Empty
            : CanExecuteInlineStartMove
                ? _loc["Result.InlineConfirmReady"]
                : _loc["Result.InlineConfirmWaiting"];

    public bool ShowSmartActionBar =>
        IsBusy ||
        (!string.Equals(Shell.SelectedNavTag, "MissionControl", StringComparison.Ordinal) &&
         !(string.Equals(Shell.SelectedNavTag, "Library", StringComparison.Ordinal)
           && string.Equals(Shell.SelectedSubTab, "Results", StringComparison.Ordinal)));

    public bool HasRunResult =>
        Run.HasRunResult ||
        (Run.CurrentRunState is RunState.Cancelled or RunState.Failed
         && (LastRunResult?.TotalFilesScanned ?? 0) > 0);

    /// <summary>TASK-115: Localized display text for the current RunState (SmartActionBar status).</summary>
    public string RunStateDisplayText => Run.CurrentRunState switch
    {
        RunState.Idle           => _loc["State.Idle"],
        RunState.Preflight      => _loc["Phase.Preflight"],
        RunState.Scanning       => _loc["Phase.Scan"],
        RunState.Deduplicating  => _loc["Phase.Dedupe"],
        RunState.Sorting        => _loc["Phase.Sort"],
        RunState.Moving         => _loc["Phase.Move"],
        RunState.Converting     => _loc["Phase.Convert"],
        RunState.Completed      => _loc["State.Completed"],
        RunState.CompletedDryRun => _loc["State.CompletedDryRun"],
        RunState.Failed         => _loc["State.Failed"],
        RunState.Cancelled      => _loc["State.Cancelled"],
        _                       => Run.CurrentRunState.ToString(),
    };

    /// <summary>TASK-122: Forward RunViewModel property changes to MainViewModel for XAML bindings.</summary>
    private static readonly HashSet<string> _forwardedRunProperties =
    [
        nameof(CurrentRunState), nameof(IsBusy), nameof(IsIdle),
        nameof(HasRunResult)
    ];

    private void OnRunPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null) return;

        if (_forwardedRunProperties.Contains(e.PropertyName))
        {
            OnPropertyChanged(e.PropertyName);
        }

        // RunState changes also affect derived MainViewModel-only properties
        if (e.PropertyName == nameof(CurrentRunState))
        {
            if (Run.CurrentRunState == RunState.Preflight)
                ResetDashboardForNewRun();

            OnPropertyChanged(nameof(RunStateDisplayText));
            OnPropertyChanged(nameof(ShowStartMoveButton));
            OnPropertyChanged(nameof(ShowResultMoveButton));
            OnPropertyChanged(nameof(ShowActionBarMoveButton));
            OnPropertyChanged(nameof(ShowSmartActionBar));
            OnPropertyChanged(nameof(CanStartCurrentRun));
            OnPropertyChanged(nameof(CanStartMoveWithCurrentPreview));
            RefreshToolSurfaceState();
            DeferCommandRequery();
        }

        if (e.PropertyName is nameof(IsBusy) or nameof(IsIdle))
        {
            OnPropertyChanged(nameof(ShowSmartActionBar));
            OnPropertyChanged(nameof(ShowResultMoveButton));
            OnPropertyChanged(nameof(ShowActionBarMoveButton));
        }
    }

    /// <summary>GUI-065: True when no roots are configured (for StartView hero drop-zone).</summary>
    public bool HasNoRoots => Roots.Count == 0;

    // ═══ ROLLBACK HISTORY (UX-010) ══════════════════════════════════════
    private const int MaxRollbackDepth = 50; // V2-M02: Bounded undo/redo stack
    private readonly Stack<string> _rollbackUndoStack = new();
    private readonly Stack<string> _rollbackRedoStack = new();

    public bool HasRollbackUndo => _rollbackUndoStack.Count > 0;
    public bool HasRollbackRedo => _rollbackRedoStack.Count > 0;

    public void PushRollbackUndo(string auditPath)
    {
        _rollbackUndoStack.Push(auditPath);
        _rollbackRedoStack.Clear();
        // V2-M02: Trim stack to max depth
        while (_rollbackUndoStack.Count > MaxRollbackDepth)
        {
            // Remove oldest entries by rebuilding the stack
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

    private string _perfPhase = "–";
    public string PerfPhase { get => _perfPhase; set => SetProperty(ref _perfPhase, value); }

    private string _perfFile = "–";
    public string PerfFile { get => _perfFile; set => SetProperty(ref _perfFile, value); }

    private string _busyHint = "";
    public string BusyHint { get => _busyHint; set => SetProperty(ref _busyHint, value); }

    // Tracks in-phase progress so long scan phases don't appear stuck on a fixed percentage.
    private string _progressPhaseKey = string.Empty;
    private int _progressPhaseEventCount;
    private DateTime _progressPhaseStartedUtc = DateTime.MinValue;
    private readonly Dictionary<UiProgressPhase, (double Start, double End)> _progressPhaseRanges = [];

    private enum UiProgressPhase
    {
        Preflight,
        Scan,
        Dedupe,
        Move,
        Sort,
        Convert,
        Report
    }

    // ═══ MISC UI STATE ══════════════════════════════════════════════════
    private string? _selectedRoot;
    public string? SelectedRoot
    {
        get => _selectedRoot;
        set { SetProperty(ref _selectedRoot, value); DeferCommandRequery(); }
    }

    private bool _canRollback;
    public bool CanRollback
    {
        get => _canRollback;
        set
        {
            SetProperty(ref _canRollback, value);
            OnPropertyChanged(nameof(ShowSmartActionBar));
            RefreshToolSurfaceState();
            DeferCommandRequery();
        }
    }

    private string _lastReportPath = "";
    public string LastReportPath
    {
        get => _lastReportPath;
        set { SetProperty(ref _lastReportPath, value); DeferCommandRequery(); }
    }

    private bool _showDryRunBanner = true;
    public bool ShowDryRunBanner { get => _showDryRunBanner; set => SetProperty(ref _showDryRunBanner, value); }

    private bool _showMoveCompleteBanner;
    public bool ShowMoveCompleteBanner { get => _showMoveCompleteBanner; set => SetProperty(ref _showMoveCompleteBanner, value); }

    // ═══ RESULT DISPLAY (delegated to RunViewModel) ════════════════════
    public bool IsResultPerfDetailsExpanded
    {
        get => Run.IsResultPerfDetailsExpanded;
        set => Run.IsResultPerfDetailsExpanded = value;
    }

    public string RunSummaryText
    {
        get => Run.RunSummaryText;
        set => Run.RunSummaryText = value;
    }

    public UiErrorSeverity RunSummarySeverity
    {
        get => Run.RunSummarySeverity;
        set => Run.RunSummarySeverity = value;
    }

    public bool HasRunSummary => Run.HasRunSummary;

    private bool _isConvertOnlyDashboard;
    public bool IsConvertOnlyDashboard
    {
        get => _isConvertOnlyDashboard;
        set
        {
            if (SetProperty(ref _isConvertOnlyDashboard, value))
                OnPropertyChanged(nameof(ShowDedupeDashboard));
        }
    }

    public bool ShowDedupeDashboard => !IsConvertOnlyDashboard;

    private string _dashboardContextHint = string.Empty;
    public string DashboardContextHint
    {
        get => _dashboardContextHint;
        set
        {
            if (SetProperty(ref _dashboardContextHint, value))
                OnPropertyChanged(nameof(HasDashboardContextHint));
        }
    }

    private UiErrorSeverity _dashboardContextSeverity = UiErrorSeverity.Info;
    public UiErrorSeverity DashboardContextSeverity
    {
        get => _dashboardContextSeverity;
        set => SetProperty(ref _dashboardContextSeverity, value);
    }

    public bool HasDashboardContextHint => !string.IsNullOrWhiteSpace(DashboardContextHint);

    public bool HasActionableErrorSummary => ErrorSummaryItems.Any(static issue => issue.Severity != UiErrorSeverity.Info);

    public IReadOnlyList<UiError> ActionableErrorSummaryItems => ErrorSummaryItems
        .Where(static issue => issue.Severity != UiErrorSeverity.Info)
        .Take(5)
        .ToList();

    public string ActionableErrorSummaryTitle => _loc.Format("Result.ErrorSummaryHeadline", ActionableErrorSummaryItems.Count);

    public string RollbackActionHint => CanRollback
        ? _loc["Run.RollbackHintEnabled"]
        : _loc["Run.RollbackHintDisabled"];

    // ═══ STATUS INDICATORS ══════════════════════════════════════════════
    private string _statusRoots = "–";
    public string StatusRoots { get => _statusRoots; set => SetProperty(ref _statusRoots, value); }

    private string _statusTools = "–";
    public string StatusTools { get => _statusTools; set => SetProperty(ref _statusTools, value); }

    private string _statusDat = "–";
    public string StatusDat { get => _statusDat; set => SetProperty(ref _statusDat, value); }

    private string _statusReady = "–";
    public string StatusReady { get => _statusReady; set => SetProperty(ref _statusReady, value); }

    private string _statusRuntime = "–";
    public string StatusRuntime { get => _statusRuntime; set => SetProperty(ref _statusRuntime, value); }

    private StatusLevel _rootsStatusLevel = StatusLevel.Missing;
    public StatusLevel RootsStatusLevel { get => _rootsStatusLevel; set => SetProperty(ref _rootsStatusLevel, value); }

    private StatusLevel _toolsStatusLevel = StatusLevel.Missing;
    public StatusLevel ToolsStatusLevel { get => _toolsStatusLevel; set => SetProperty(ref _toolsStatusLevel, value); }

    private StatusLevel _datStatusLevel = StatusLevel.Missing;
    public StatusLevel DatStatusLevel { get => _datStatusLevel; set => SetProperty(ref _datStatusLevel, value); }

    private StatusLevel _readyStatusLevel = StatusLevel.Missing;
    public StatusLevel ReadyStatusLevel { get => _readyStatusLevel; set => SetProperty(ref _readyStatusLevel, value); }

    // ═══ TOOL STATUS LABELS (P1-007: VM-bound instead of x:Name TextBlocks) ═══
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

    // ═══ DASHBOARD COUNTERS (delegated to RunViewModel) ════════════════
    public string DashMode
    {
        get => Run.DashMode;
        set
        {
            Run.DashMode = value;
            if (string.Equals(value, "Rollback", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(RunSummaryText))
            {
                SetRunSummary(
                    _loc.Format("Result.Summary.RollbackDone", 0, 0, 0, 0),
                    UiErrorSeverity.Info);
            }
        }
    }
    public string DashWinners { get => Run.DashWinners; set => Run.DashWinners = value; }
    public string DashDupes { get => Run.DashDupes; set => Run.DashDupes = value; }
    public string DashJunk { get => Run.DashJunk; set => Run.DashJunk = value; }
    public string DashDuration { get => Run.DashDuration; set => Run.DashDuration = value; }
    public string HealthScore { get => Run.HealthScore; set => Run.HealthScore = value; }
    public string DashGames { get => Run.DashGames; set => Run.DashGames = value; }
    public string DashDatHits { get => Run.DashDatHits; set => Run.DashDatHits = value; }
    public string DashDatHave { get => Run.DashDatHave; set => Run.DashDatHave = value; }
    public string DashDatWrongName { get => Run.DashDatWrongName; set => Run.DashDatWrongName = value; }
    public string DashDatMiss { get => Run.DashDatMiss; set => Run.DashDatMiss = value; }
    public string DashDatUnknown { get => Run.DashDatUnknown; set => Run.DashDatUnknown = value; }
    public string DashDatAmbiguous { get => Run.DashDatAmbiguous; set => Run.DashDatAmbiguous = value; }
    public string DashDatRenameProposed { get => Run.DashDatRenameProposed; set => Run.DashDatRenameProposed = value; }
    public string DashDatRenameExecuted { get => Run.DashDatRenameExecuted; set => Run.DashDatRenameExecuted = value; }
    public string DashDatRenameFailed { get => Run.DashDatRenameFailed; set => Run.DashDatRenameFailed = value; }
    public string DashConverted { get => Run.DashConverted; set => Run.DashConverted = value; }
    public string DashConvertBlocked { get => Run.DashConvertBlocked; set => Run.DashConvertBlocked = value; }
    public string DashConvertReview { get => Run.DashConvertReview; set => Run.DashConvertReview = value; }
    public string DashConvertSaved { get => Run.DashConvertSaved; set => Run.DashConvertSaved = value; }
    public string DedupeRate { get => Run.DedupeRate; set => Run.DedupeRate = value; }

    // ═══ ANALYSE DATA (delegated to RunViewModel) ═══════════════════════
    public ObservableCollection<Models.ConsoleDistributionItem> ConsoleDistribution => Run.ConsoleDistribution;
    public ObservableCollection<Models.DedupeGroupItem> DedupeGroupItems => Run.DedupeGroupItems;
    public string MoveConsequenceText { get => Run.MoveConsequenceText; set => Run.MoveConsequenceText = value; }

    // ═══ STEP INDICATOR ═════════════════════════════════════════════════
    private int _currentStep;
    public int CurrentStep { get => _currentStep; set => SetProperty(ref _currentStep, value); }

    // RD-004: Phase detail tooltips for interactive stepper
    /// <summary>Returns a detail string for the given pipeline phase (1–7). Used by stepper tooltips.</summary>
    public string GetPhaseDetail(int phase) => phase switch
    {
        1 => HasRunResult && LastRunResult is { } r
            ? $"Preflight: {(r.Preflight?.ShouldReturn != true ? "✓ OK" : $"✗ {r.Preflight?.Reason}")}"
            : _loc["Phase.Preflight.Desc"],
        2 => HasRunResult && LastRunResult is { } r2
            ? $"Scan: {r2.TotalFilesScanned} {_loc["Step.Scanning"]}"
            : _loc["Phase.Scan.Desc"],
        3 => HasRunResult && LastRunResult is { } r3
            ? $"Dedupe: {r3.WinnerCount} / {r3.LoserCount}"
            : _loc["Phase.Dedupe.Desc"],
        4 when !IsMovePhaseApplicable => _loc["Phase.Move.Skipped"],
        4 => HasRunResult && LastRunResult?.MoveResult is { } mv
            ? $"Move: {mv.MoveCount} / {mv.FailCount}"
            : _loc["Phase.Move.Desc"],
        5 => _loc["Phase.Sort.Desc"],
        6 when !IsConvertPhaseApplicable => _loc["Phase.Convert.Skipped"],
        6 => HasRunResult && LastRunResult is { } r6 && r6.ConvertedCount > 0
            ? $"Convert: {r6.ConvertedCount}"
            : _loc["Phase.Convert.Desc"],
        7 => HasRunResult ? $"{_loc["Step.Completed"]} – {DashDuration}" : _loc["Phase.Done.Desc"],
        _ => ""
    };

    private string _stepLabel1 = "";
    public string StepLabel1 { get => _stepLabel1; set => SetProperty(ref _stepLabel1, value); }

    private string _stepLabel2 = "";
    public string StepLabel2 { get => _stepLabel2; set => SetProperty(ref _stepLabel2, value); }

    private string _stepLabel3 = "";
    public string StepLabel3 { get => _stepLabel3; set => SetProperty(ref _stepLabel3, value); }

    // ═══ RUN COMMAND HANDLERS ═══════════════════════════════════════════

    /// <summary>Confirm before destructive Move operations (uses injected IDialogService).</summary>
    public bool ConfirmMoveDialog()
    {
        var message =
            $"{MoveConsequenceText}\n\n"
            + $"Roots: {string.Join(", ", Roots)}\n"
            + $"{DashWinners} | {DashDupes} | {DashJunk}\n\n"
            + _loc["Dialog.ConfirmMove.Warning"];

        return _dialog.DangerConfirm(
            _loc["Dialog.ConfirmMove.Title"],
            message,
            _loc["Dialog.ConfirmMove.BtnLabel"],
            _loc["Dialog.ConfirmMove.BtnConfirm"]);
    }

    private async Task<(bool Proceed, bool ApproveConversionReview)> ConfirmConversionReviewDialogAsync(RunOptions runOptions, CancellationToken cancellationToken)
    {
        if (DryRun || runOptions.ConvertFormat is null)
            return (true, false);

        if (runOptions.Mode != RunConstants.ModeMove && !runOptions.ConvertOnly)
            return (true, false);

        if (runOptions.ConvertOnly)
            return (true, false);

        if (LastDedupeGroups.Count == 0)
            return (true, false);

        var reviewEntries = await Task.Run(() => BuildConversionReviewEntries(runOptions, cancellationToken), cancellationToken);
        if (reviewEntries.Count == 0)
            return (true, false);

        var summary = _loc["Dialog.ConfirmConversion.Summary"];

        var confirmed = _dialog.ConfirmConversionReview(
            _loc["Dialog.ConfirmConversion.Title"],
            summary,
            reviewEntries);

        if (!confirmed)
            AddLog(_loc["Log.ConversionCancelled"], "WARN");

        return (confirmed, confirmed);
    }

    private Task<bool> ConfirmDatRenamePreviewDialogAsync(RunOptions runOptions, CancellationToken cancellationToken)
    {
        if (DryRun || runOptions.Mode != RunConstants.ModeMove || !runOptions.EnableDatRename)
            return Task.FromResult(true);

        cancellationToken.ThrowIfCancellationRequested();

        var auditEntries = LastRunResult?.DatAuditResult?.Entries;
        if (auditEntries is null || auditEntries.Count == 0)
            return Task.FromResult(true);

        var renameProposals = auditEntries
            .Where(static e => e.Status == DatAuditStatus.HaveWrongName && !string.IsNullOrWhiteSpace(e.DatRomFileName))
            .Where(e => !string.Equals(Path.GetFileName(e.FilePath), e.DatRomFileName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (renameProposals.Count == 0)
            return Task.FromResult(true);

        var confirmed = _dialog.ConfirmDatRenamePreview(renameProposals);
        if (!confirmed)
            AddLog(_loc["Log.DatRenameCancelled"], "WARN");

        return Task.FromResult(confirmed);
    }

    private IReadOnlyList<ConversionReviewEntry> BuildConversionReviewEntries(RunOptions runOptions, CancellationToken cancellationToken)
    {
        var dataDir = FeatureService.ResolveDataDirectory() ?? RunEnvironmentBuilder.ResolveDataDir();
        var settings = RunEnvironmentBuilder.LoadSettings(dataDir);
        using var env = RunEnvironmentBuilder.Build(runOptions, settings, dataDir);

        if (env.Converter is not FormatConverterAdapter converter)
            return Array.Empty<ConversionReviewEntry>();

        var entries = new List<ConversionReviewEntry>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in LastDedupeGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var winner = group.Winner;
            if (winner is null || string.IsNullOrWhiteSpace(winner.MainPath))
                continue;

            if (!seenPaths.Add(winner.MainPath))
                continue;

            if (!File.Exists(winner.MainPath))
                continue;

            var plan = converter.PlanForConsole(winner.MainPath, winner.ConsoleKey ?? string.Empty);
            if (plan is null || !plan.RequiresReview)
                continue;

            entries.Add(new ConversionReviewEntry(
                winner.MainPath,
                plan.FinalTargetExtension,
                BuildConversionSafetyReason(plan)));
        }

        return entries;
    }

    private string BuildConversionSafetyReason(ConversionPlan plan)
    {
        if (plan.Policy == ConversionPolicy.ManualOnly)
            return "Policy=ManualOnly";
        if (plan.Safety == ConversionSafety.Risky)
            return "Safety=Risky";
        if (plan.SourceIntegrity == SourceIntegrity.Lossy)
            return "SourceIntegrity=Lossy";
        return _loc["Conversion.ReviewRequired"];
    }

    private void ShowInfoDialogNonBlocking(string message, string title)
    {
        _ = Task.Run(() => _dialog.Info(message, title));
    }

    private void OnRun()
    {
        if (HasBlockingValidationErrors)
        {
            var blockingValidationMessage = GetBlockingValidationMessage();
            AddLog(blockingValidationMessage, "WARN");
            ShowInfoDialogNonBlocking(blockingValidationMessage, _loc["Dialog.Info.StartBlocked"]);
            DeferCommandRequery();
            return;
        }

        if (!DryRun && !ConvertOnly && !CanStartMoveWithCurrentPreview)
        {
            AddLog(MoveApplyGateText, "WARN");
            ShowInfoDialogNonBlocking(MoveApplyGateText, _loc["Dialog.Info.MoveBlocked"]);
            DeferCommandRequery();
            return;
        }

        CurrentRunState = RunState.Preflight;
        ResetDashboardForNewRun();
        BusyHint = ConvertOnly ? _loc["Progress.BusyHint.Converting"] : DryRun ? (IsSimpleMode ? _loc["Progress.BusyHint.Preview"] : _loc["Progress.BusyHint.DryRun"]) : _loc["Progress.BusyHint.Move"];
        DashMode = ConvertOnly ? _loc["Result.Mode.ConvertOnly"] : DryRun ? (IsSimpleMode ? _loc["Progress.BusyHint.Preview"] : "DryRun") : "Move";
        Progress = 0;
        ProgressText = "0%";
        PerfPhase = "–";
        PerfFile = "–";
        ResetRunProgressEstimator();
        ShowMoveCompleteBanner = false;
        RunSummaryText = "";
        RunSummarySeverity = UiErrorSeverity.Info;

        RunRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnCancel()
    {
        ResetInlineMoveConfirmDebounce();
        // F-02 FIX: Cancel under lock to prevent race with CreateRunCancellation/Dispose
        lock (_ctsLock)
        {
            try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
        }
        CurrentRunState = RunState.Cancelled;
        Progress = 0;
        ProgressText = string.Empty;
        BusyHint = _loc["Progress.BusyHint.CancelRequested"];
        ShowMoveCompleteBanner = false;
        SetRunSummary(_loc["Result.Summary.Cancelled"], UiErrorSeverity.Warning);
    }

    private async Task OnRollbackAsync()
    {
        var restoreCount = (LastRunResult?.MoveResult?.MoveCount ?? 0)
            + (LastRunResult?.JunkMoveResult?.MoveCount ?? 0);
        var rollbackPreview = _loc.Format(
            "Dialog.Rollback.Preview",
            MoveConsequenceText,
            DashWinners,
            DashDupes,
            DashJunk,
            restoreCount,
            TrashRoot);

        if (!_dialog.Confirm(rollbackPreview, _loc["Dialog.Rollback.Title"]))
            return;

        if (string.IsNullOrEmpty(LastAuditPath) || !File.Exists(LastAuditPath))
        {
            AddLog(_loc["Log.RollbackNoAudit"], "WARN");
            return;
        }

        try
        {
            var auditPathCopy = LastAuditPath;
            var roots = Roots.ToList();

            var integrity = await Task.Run(() => Romulus.Infrastructure.Audit.RollbackService.VerifyTrashIntegrity(auditPathCopy, roots));
            if (integrity.Failed > 0 || integrity.SkippedMissingDest > 0 || integrity.SkippedUnsafe > 0)
            {
                var integrityWarning =
                    $"Rollback integrity check found issues. Missing trash files: {integrity.SkippedMissingDest}, unsafe entries: {integrity.SkippedUnsafe}, failures: {integrity.Failed}. " +
                    "Continue anyway?";

                if (!_dialog.Confirm(integrityWarning, _loc["Dialog.Rollback.Title"]))
                {
                    AddLog("Rollback aborted after integrity preflight warning.", "WARN");
                    return;
                }
            }

            var restored = await Task.Run(() => Romulus.Infrastructure.Audit.RollbackService.Execute(auditPathCopy, roots));
            AddLog(_loc.Format("Log.RollbackDone", restored.RolledBack, restored.SkippedMissingDest, restored.SkippedCollision, restored.Failed),
                restored.Failed > 0 ? "WARN" : "INFO");
            CanRollback = false;
            ShowMoveCompleteBanner = false;
            // SEC-002: Invalidate preview fingerprint — filesystem state changed, old preview is stale
            _lastSuccessfulPreviewFingerprint = null;
            OnMovePreviewGateChanged();
            ResetDashboardForNewRun();
            DashMode = "Rollback";
            Shell.SelectedNavTag = "Analyse";
            SelectedResultSection = "Dashboard";
            SetRunSummary(
                _loc.Format("Result.Summary.RollbackDone", restored.RolledBack, restored.SkippedMissingDest, restored.SkippedCollision, restored.Failed),
                restored.Failed > 0 ? UiErrorSeverity.Warning : UiErrorSeverity.Info);
        }
        catch (Exception ex)
        {
            AddLog(_loc.Format("Log.RollbackError", ex.Message), "ERROR");
            SetRunSummary(_loc.Format("Log.RollbackFailed", ex.Message), UiErrorSeverity.Error);
        }
    }

    private void OnAddRoot()
    {
        var folder = _dialog.BrowseFolder(_loc["Dialog.BrowseFolder.RomTitle"]);
        if (folder is not null && !Roots.Contains(folder))
        {
            Roots.Add(folder);
        }
    }

    private void OnRemoveRoot()
    {
        if (SelectedRoot is not null)
            Roots.Remove(SelectedRoot);
    }

    private void OnOpenReport()
    {
        if (string.IsNullOrWhiteSpace(LastReportPath) || !System.IO.File.Exists(LastReportPath))
        {
            AddLog(_loc["Log.ReportMissing"], "WARN");
            return;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(LastReportPath);
        }
        catch
        {
            AddLog(_loc["Log.ReportInvalidPath"], "WARN");
            return;
        }

        var extension = Path.GetExtension(fullPath);
        if (!string.Equals(extension, ".html", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".htm", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".log", StringComparison.OrdinalIgnoreCase))
        {
            AddLog(_loc.Format("Log.ReportTypeNotAllowed", extension!), "WARN");
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = fullPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AddLog(_loc.Format("Log.ReportOpenFailed", ex.Message), "WARN");
        }
    }

    /// <summary>Complete a run (call from UI thread when orchestration finishes).</summary>
    public void CompleteRun(bool success, string? reportPath = null, bool cancelled = false, bool completedWithErrors = false)
    {
        ResetInlineMoveConfirmDebounce();
        BusyHint = "";
        ConvertOnly = false; // Reset transient flag
        var resolvedReportPath = string.IsNullOrWhiteSpace(reportPath) ? null : reportPath;
        LastReportPath = resolvedReportPath ?? string.Empty;
        CanRollback = _runService.HasVerifiedRollback(LastAuditPath);
        if (success && DryRun)
        {
            _lastSuccessfulPreviewFingerprint = BuildPreviewConfigurationFingerprint();
            CurrentRunState = RunState.CompletedDryRun;
            Shell.SelectedNavTag = "Analyse";
            SelectedResultSection = "Dashboard";
            IsResultPerfDetailsExpanded = true;
            SetRunSummary(
                string.Concat(
                    _loc.Format("Result.Summary.PreviewDone", DashDupes, DashJunk),
                    " ",
                    _loc["Result.Summary.PreviewShortcutHint"]),
                UiErrorSeverity.Info);
        }
        else if (success && !DryRun)
        {
            CurrentRunState = RunState.Completed;
            CanRollback = _runService.HasVerifiedRollback(LastAuditPath);
            ShowMoveCompleteBanner = true;
            Shell.SelectedNavTag = "Analyse";
            SelectedResultSection = "Dashboard";
            IsResultPerfDetailsExpanded = true;
            SetRunSummary(_loc.Format("Result.Summary.ChangesApplied", MoveConsequenceText), UiErrorSeverity.Info);
        }
        else if (cancelled)
        {
            CurrentRunState = RunState.Cancelled;
            ShowMoveCompleteBanner = false;
            Shell.SelectedNavTag = "Analyse";
            SelectedResultSection = "Dashboard";
            SetRunSummary(BuildCancelledRunSummary(), UiErrorSeverity.Warning);
        }
        else if (completedWithErrors)
        {
            CurrentRunState = RunState.Failed;
            Shell.SelectedNavTag = "Analyse";
            SelectedResultSection = "Dashboard";
            IsResultPerfDetailsExpanded = true;
            SetRunSummary(_loc["Result.Summary.CompletedWithErrors"], UiErrorSeverity.Warning);
        }
        else
        {
            CurrentRunState = RunState.Failed;
            Shell.SelectedNavTag = "Analyse";
            SelectedResultSection = "Dashboard";
            SetRunSummary(_loc["Result.Summary.Failed"], UiErrorSeverity.Error);
        }
        RefreshStatus();
        OnMovePreviewGateChanged();
    }

    private string BuildCancelledRunSummary()
    {
        var phase = string.IsNullOrWhiteSpace(PerfPhase) || PerfPhase == "–"
            ? _loc["State.Cancelled"]
            : PerfPhase;

        var movedFiles = (LastRunResult?.MoveResult?.MoveCount ?? 0)
            + (LastRunResult?.JunkMoveResult?.MoveCount ?? 0);

        if (movedFiles <= 0)
            return _loc.Format("Result.Summary.CancelledInPhase", phase);

        var rollbackHint = CanRollback
            ? _loc["Result.Summary.RollbackAvailable"]
            : _loc["Result.Summary.RollbackUnavailable"];

        return _loc.Format("Result.Summary.CancelledInPhaseMoved", phase, movedFiles, rollbackHint);
    }

    private static string? TryFindLatestReportPath()
    {
        var dirs = new[]
        {
            AppStoragePathResolver.ResolveRoamingPath(Romulus.Contracts.AppIdentity.ArtifactDirectories.Reports),
            Path.Combine(Directory.GetCurrentDirectory(), Romulus.Contracts.AppIdentity.ArtifactDirectories.Reports)
        };

        string? bestFile = null;
        DateTime bestTime = DateTime.MinValue;

        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir))
                continue;

            try
            {
                var files = Directory.GetFiles(dir, "*.html", SearchOption.TopDirectoryOnly);
                foreach (var file in files)
                {
                    var mtime = File.GetLastWriteTimeUtc(file);
                    if (mtime > bestTime)
                    {
                        bestTime = mtime;
                        bestFile = file;
                    }
                }
            }
            catch
            {
                // best effort only
            }
        }

        return bestFile;
    }

    /// <summary>Set up a new CancellationTokenSource for a run.</summary>
    public CancellationToken CreateRunCancellation()
    {
        var newCts = new CancellationTokenSource();
        CancellationTokenSource? oldCts;
        lock (_ctsLock)
        {
            oldCts = _cts;
            _cts = newCts;
        }
        // Dispose outside lock — old CTS is no longer reachable from OnCancel
        try { oldCts?.Dispose(); } catch (ObjectDisposedException) { }
        return newCts.Token;
    }

    /// <summary>Transition to a new run phase (call from code-behind during orchestration).</summary>
    public void TransitionTo(RunState newState)
    {
        CurrentRunState = newState;
    }

    /// <summary>Recompute all status dot indicators.</summary>
    public void RefreshStatus()
    {
        // Roots
        bool hasRoots = Roots.Count > 0;
        RootsStatusLevel = hasRoots ? StatusLevel.Ok : StatusLevel.Missing;
        StatusRoots = hasRoots ? _loc.Format("Status.RootsConfigured", Roots.Count) : _loc["Status.Roots.None"];
        StepLabel1 = hasRoots ? _loc.Format("Step.Roots", Roots.Count) : _loc["Step.NoRoots"];

        // Tools
        bool hasChdman = !string.IsNullOrWhiteSpace(ToolChdman) && File.Exists(ToolChdman);
        bool has7z = !string.IsNullOrWhiteSpace(Tool7z) && File.Exists(Tool7z);
        bool hasDolphin = !string.IsNullOrWhiteSpace(ToolDolphin) && File.Exists(ToolDolphin);
        bool hasPsxtract = !string.IsNullOrWhiteSpace(ToolPsxtract) && File.Exists(ToolPsxtract);
        bool hasCiso = !string.IsNullOrWhiteSpace(ToolCiso) && File.Exists(ToolCiso);
        bool anyToolSpecified = !string.IsNullOrWhiteSpace(ToolChdman) || !string.IsNullOrWhiteSpace(Tool7z);
        int toolCount = (hasChdman ? 1 : 0) + (has7z ? 1 : 0) + (hasDolphin ? 1 : 0) + (hasPsxtract ? 1 : 0) + (hasCiso ? 1 : 0);
        ToolsStatusLevel = (hasChdman || has7z) ? StatusLevel.Ok
            : (anyToolSpecified || ConvertEnabled) ? StatusLevel.Warning
            : StatusLevel.Missing;
        StatusTools = ToolsStatusLevel == StatusLevel.Ok ? _loc.Format("Status.ToolsFound", toolCount)
            : ToolsStatusLevel == StatusLevel.Warning ? _loc["Status.ToolsNotFound"] : _loc["Status.Tools.None"];

        // P1-007: Update tool status labels
        ChdmanStatusText = string.IsNullOrWhiteSpace(ToolChdman) ? "–" : hasChdman ? _loc["Tool.Status.Found"] : _loc["Tool.Status.NotFound"];
        DolphinStatusText = string.IsNullOrWhiteSpace(ToolDolphin) ? "–" : hasDolphin ? _loc["Tool.Status.Found"] : _loc["Tool.Status.NotFound"];
        SevenZipStatusText = string.IsNullOrWhiteSpace(Tool7z) ? "–" : has7z ? _loc["Tool.Status.Found"] : _loc["Tool.Status.NotFound"];
        PsxtractStatusText = string.IsNullOrWhiteSpace(ToolPsxtract) ? "–" : hasPsxtract ? _loc["Tool.Status.Found"] : _loc["Tool.Status.NotFound"];
        CisoStatusText = string.IsNullOrWhiteSpace(ToolCiso) ? "–" : hasCiso ? _loc["Tool.Status.Found"] : _loc["Tool.Status.NotFound"];

        // DAT
        bool datRootValid = !string.IsNullOrWhiteSpace(DatRoot) && Directory.Exists(DatRoot);
        DatStatusLevel = !UseDat ? StatusLevel.Missing
            : datRootValid ? StatusLevel.Ok
            : !string.IsNullOrWhiteSpace(DatRoot) ? StatusLevel.Warning
            : StatusLevel.Warning;
        StatusDat = DatStatusLevel == StatusLevel.Ok ? _loc["Status.DatActive"]
            : DatStatusLevel == StatusLevel.Warning ? _loc["Status.DatPathInvalid"] : _loc["Status.DatDisabled"];

        // Overall readiness
        var validationSummary = GetValidationSummary();
        ReadyStatusLevel = !hasRoots || validationSummary.HasBlockers ? StatusLevel.Blocked
            : ToolsStatusLevel == StatusLevel.Warning || DatStatusLevel == StatusLevel.Warning || validationSummary.HasWarnings ? StatusLevel.Warning
            : StatusLevel.Ok;
        StatusReady = ReadyStatusLevel switch
        {
            StatusLevel.Ok => _loc["Status.Ready.Ok"],
            StatusLevel.Warning => _loc["Status.Ready.Warning"],
            StatusLevel.Blocked => _loc["Status.Ready.Blocked"],
            _ => _loc["Status.Default"]
        };

        // Step indicator with RunState-awareness
        if (IsBusy)
        {
            CurrentStep = 2;
            StepLabel3 = Run.CurrentRunState switch
            {
                RunState.Preflight => _loc["Step.Preflight"],
                RunState.Scanning => _loc["Step.Scanning"],
                RunState.Deduplicating => _loc["Step.Deduplicating"],
                RunState.Sorting => _loc["Step.Sorting"],
                RunState.Moving => _loc["Step.Moving"],
                RunState.Converting => _loc["Step.Converting"],
                _ => _loc["Step.Running"]
            };
        }
        else if (Run.CurrentRunState is RunState.Completed or RunState.CompletedDryRun)
        {
            CurrentStep = 3;
            StepLabel3 = Run.CurrentRunState == RunState.CompletedDryRun ? _loc["Step.PreviewDone"] : _loc["Step.Done"];
        }
        else
        {
            CurrentStep = hasRoots ? 1 : 0;
            StepLabel3 = _loc["Step.PressF5"];
        }

        RefreshToolSurfaceState();
    }

    // ═══ RUN PIPELINE EXECUTION ═════════════════════════════════════════

    /// <summary>Execute the full run pipeline (scan, dedupe, sort, convert, move).</summary>
    public async Task ExecuteRunAsync()
    {
        // Single-flight guard: prevent concurrent pipeline execution
        if (Interlocked.CompareExchange(ref _isExecutingRun, 1, 0) != 0) return;
        try
        {
            await ExecuteRunCoreAsync();
        }
        finally
        {
            Interlocked.Exchange(ref _isExecutingRun, 0);
        }
    }

    private async Task ExecuteRunCoreAsync()
    {
        if (!DryRun && !ConvertOnly && ConfirmMove && !ConfirmMoveDialog())
        {
            // SEC-001: Reset transient flags on dialog decline before returning to Idle
            ConvertOnly = false;
            BusyHint = "";
            CurrentRunState = RunState.Idle;
            return;
        }

        var ct = CreateRunCancellation();
        try
        {
            _runLogStartIndex = LogEntries.Count;
            AddLog(_loc["Log.Initializing"], "INFO");

            var (orchestrator, runOptions, auditPath, reportPath) = await Task.Run(() =>
            {
                DateTime lastProgressUpdate = DateTime.MinValue;
                string lastProgressPhaseKey = string.Empty;
                return _runService.BuildOrchestrator(this, msg =>
                {
                    var now = DateTime.UtcNow;
                    if (!ShouldDispatchProgressMessage(msg, now, lastProgressUpdate, lastProgressPhaseKey))
                        return;

                    lastProgressUpdate = now;
                    if (TrySplitProgressMessage(msg, out var phaseKey, out _))
                        lastProgressPhaseKey = phaseKey;

                    if (_syncContext is null)
                    {
                        ApplyProgressMessage(msg);
                        return;
                    }

                    _syncContext.Post(_ => ApplyProgressMessage(msg), null);
                });
            }, ct);
            ConfigureRunProgressPlan(runOptions);

            var conversionReviewDecision = await ConfirmConversionReviewDialogAsync(runOptions, ct);
            if (!conversionReviewDecision.Proceed)
            {
                // SEC-001: Reset transient flags on dialog decline before returning to Idle
                ConvertOnly = false;
                BusyHint = "";
                CurrentRunState = RunState.Idle;
                return;
            }

            runOptions = RunOptionsBuilder.WithApproveConversionReview(
                runOptions,
                conversionReviewDecision.ApproveConversionReview);

            if (!await ConfirmDatRenamePreviewDialogAsync(runOptions, ct))
            {
                // SEC-001: Reset transient flags on dialog decline before returning to Idle
                ConvertOnly = false;
                BusyHint = "";
                CurrentRunState = RunState.Idle;
                return;
            }

            var svcResult = await Task.Run(
                () => _runService.ExecuteRun(orchestrator, runOptions, auditPath, reportPath, ct), ct);

            LastAuditPath = auditPath;
            var runWasCancelled = ct.IsCancellationRequested ||
                                  string.Equals(svcResult.Result.Status, "cancelled", StringComparison.OrdinalIgnoreCase);

            if (runWasCancelled)
            {
                ApplyRunResult(svcResult.Result, force: true);
                PopulateErrorSummary();
                AddLog(_loc["Log.RunCancelled"], "WARN");
                CompleteRun(false, svcResult.ReportPath, cancelled: true);
                return;
            }

            ApplyRunResult(svcResult.Result);

            if (!DryRun && auditPath is not null && File.Exists(auditPath))
                PushRollbackUndo(auditPath);

            if (svcResult.ReportPath is not null)
                AddLog(_loc.Format("Log.ReportPath", svcResult.ReportPath), "INFO");

            if (!ct.IsCancellationRequested)
            {
                var isPartialFailure = string.Equals(svcResult.Result.Status,
                    Contracts.RunConstants.StatusCompletedWithErrors, StringComparison.OrdinalIgnoreCase);

                if (isPartialFailure)
                {
                    AddLog(_loc["Log.RunCompletedWithErrors"], "WARN");
                    CompleteRun(false, svcResult.ReportPath, completedWithErrors: true);
                }
                else
                {
                    AddLog(_loc["Log.RunComplete"], "INFO");
                    CompleteRun(true, svcResult.ReportPath);
                }
                PopulateErrorSummary();
            }
            else
            {
                AddLog(_loc["Log.RunCancelled"], "WARN");
                CompleteRun(false, cancelled: true);
            }
        }
        catch (OperationCanceledException)
        {
            AddLog(_loc["Log.RunCancelled"], "WARN");
            CompleteRun(false, cancelled: true);
        }
        catch (Exception ex)
        {
            // V2-M07: Use ErrorClassifier for structured error reporting
            var error = Romulus.Contracts.Errors.ErrorClassifier.FromException(ex, "GUI");
            AddLog($"[{error.Kind}] {error.Code}: {error.Message}", "ERROR");
            CompleteRun(false);
        }
        finally
        {
            _watchService.FlushPendingIfNeeded();
            _scheduleService.FlushPendingIfNeeded();
        }
    }

    // ═══ EVENTS ═════════════════════════════════════════════════════════
    public event EventHandler? RunRequested;

    // ═══ RESULT METHODS ═════════════════════════════════════════════════

    /// <summary>Build the error summary items for the protocol tab.</summary>
    public void PopulateErrorSummary()
    {
        ErrorSummaryItems.Clear();

        var projected = ErrorSummaryProjection.Build(
            LastRunResult,
            LastCandidates,
            LogEntries.Skip(_runLogStartIndex));

        foreach (var issue in projected)
            ErrorSummaryItems.Add(issue);

        OnPropertyChanged(nameof(HasActionableErrorSummary));
        OnPropertyChanged(nameof(ActionableErrorSummaryItems));
        OnPropertyChanged(nameof(ActionableErrorSummaryTitle));
    }

    /// <summary>Apply run results from orchestrator to all dashboard/state properties.</summary>
    public void ApplyRunResult(RunResult result, bool force = false)
    {
        if (!force && CurrentRunState is RunState.Failed or RunState.Cancelled)
        {
            AddLog(_loc["Log.ResultIgnored"], "WARN");
            return;
        }

        LastRunResult = result;
        var projectedArtifacts = RunArtifactProjection.Project(result);
        LastCandidates = new ObservableCollection<RomCandidate>(projectedArtifacts.AllCandidates);
        LastDedupeGroups = new ObservableCollection<DedupeGroup>(projectedArtifacts.DedupeGroups);
        RefreshToolLockState();
        var projection = RunProjectionFactory.Create(result);

        Progress = 100;
        var isConvertOnlyRun = ConvertOnly ||
                               (result.MoveResult is null && result.JunkMoveResult is null &&
                                (result.ConvertedCount > 0 || result.ConvertErrorCount > 0 || result.ConvertSkippedCount > 0 || result.ConvertBlockedCount > 0));
        var isDryRun = DryRun;
        var hasCandidates = projectedArtifacts.AllCandidates.Count > 0;
        var dashboard = DashboardProjection.From(projection, result, isConvertOnlyRun, isDryRun);

        DashWinners = dashboard.Winners;
        DashDupes = dashboard.Dupes;
        DashJunk = dashboard.Junk;
        DashDuration = dashboard.Duration;
        HealthScore = dashboard.HealthScore;
        DashGames = dashboard.Games;
        DashDatHits = dashboard.DatHits;
        DashDatHave = dashboard.DatHaveDisplay;
        DashDatWrongName = dashboard.DatWrongNameDisplay;
        DashDatMiss = dashboard.DatMissDisplay;
        DashDatUnknown = dashboard.DatUnknownDisplay;
        DashDatAmbiguous = dashboard.DatAmbiguousDisplay;
        DashDatRenameProposed = dashboard.DatRenameProposedDisplay;
        DashDatRenameExecuted = dashboard.DatRenameExecutedDisplay;
        DashDatRenameFailed = dashboard.DatRenameFailedDisplay;
        DashConverted = dashboard.ConvertedDisplay;
        DashConvertBlocked = dashboard.ConvertBlockedDisplay;
        DashConvertReview = dashboard.ConvertReviewDisplay;
        DashConvertSaved = dashboard.ConvertSavedBytesDisplay;
        DedupeRate = dashboard.DedupeRate;
        IsConvertOnlyDashboard = isConvertOnlyRun;

        if (isConvertOnlyRun)
        {
            DashboardContextHint = _loc["Result.Context.ConvertOnly"];
            DashboardContextSeverity = UiErrorSeverity.Info;
        }
        else if (string.Equals(result.Status, RunConstants.StatusCancelled, StringComparison.OrdinalIgnoreCase) && hasCandidates)
        {
            DashboardContextHint = _loc["Result.Context.CancelledPartial"];
            DashboardContextSeverity = UiErrorSeverity.Warning;
        }
        else if (string.Equals(result.Status, RunConstants.StatusCancelled, StringComparison.OrdinalIgnoreCase) && !hasCandidates)
        {
            DashboardContextHint = _loc["Result.Context.CancelledNoData"];
            DashboardContextSeverity = UiErrorSeverity.Warning;
        }
        else if (isDryRun)
        {
            DashboardContextHint = _loc["Result.Context.Preview"];
            DashboardContextSeverity = UiErrorSeverity.Info;
        }
        else if (string.Equals(result.Status, RunConstants.StatusOk, StringComparison.OrdinalIgnoreCase)
                 || string.Equals(result.Status, RunConstants.StatusCompletedWithErrors, StringComparison.OrdinalIgnoreCase)
                 || string.IsNullOrWhiteSpace(result.Status))
        {
            DashboardContextHint = _loc["Result.Context.MoveCompleted"];
            DashboardContextSeverity = UiErrorSeverity.Info;
        }
        else
        {
            DashboardContextHint = string.Empty;
            DashboardContextSeverity = UiErrorSeverity.Info;
        }

        // Raw int values for chart rendering (display strings contain suffixes like "(vorläufig) (Plan)")
        Run.GamesRaw = projection.Games;
        Run.DupesRaw = projection.Dupes;
        Run.JunkRaw = projection.Junk;
        Run.UpdateBreakdown();

        // A-21: Load DatAudit results into DatAuditViewModel
        DatAudit.LoadResult(result.DatAuditResult);

        if (result.Status == "blocked")
        {
            AddLog(_loc.Format("Log.PreflightBlocked", result.Preflight?.Reason ?? ""), "ERROR");
        }
        else
        {
            // F-P2-01: Surface preflight warnings in GUI log
            if (result.Preflight?.Warnings is { Count: > 0 } warnings)
            {
                foreach (var w in warnings)
                    AddLog(_loc.Format("Log.PreflightWarning", w), "WARN");
            }

            AddLog(_loc.Format("Log.ScanCount", result.TotalFilesScanned), "INFO");
            AddLog(_loc.Format("Log.DedupeCount", projection.Keep, projection.Dupes, projection.Junk), "INFO");
            if (result.MoveResult is { } mv)
                AddLog(_loc.Format("Log.MoveCount", mv.MoveCount, mv.FailCount), mv.FailCount > 0 ? "WARN" : "INFO");
            if (result.ConsoleSortResult is { } sort)
            {
                var sortFailures = GetConsoleSortFailureCount(sort);
                AddLog(_loc.Format("Log.SortCount", sort.Moved, sortFailures, sort.Unknown), sortFailures > 0 ? "WARN" : "INFO");
            }
            if (result.ConvertedCount > 0)
                AddLog(_loc.Format("Log.ConvertCount", result.ConvertedCount), "INFO");
            if (result.ConvertErrorCount > 0)
                AddLog(_loc.Format("Log.ConvertErrorCount", result.ConvertErrorCount), "WARN");
            if (result.ConvertBlockedCount > 0)
                AddLog(_loc.Format("Log.ConvertBlockedCount", result.ConvertBlockedCount), "WARN");

            // F-P1-03: Per-file conversion details in GUI log (parity with CLI/API)
            if (result.ConversionReport is { Results.Count: > 0 })
            {
                foreach (var cr in result.ConversionReport.Results)
                {
                    var detail = cr.Outcome switch
                    {
                        ConversionOutcome.Error => _loc.Format("Log.ConvertDetailError", Path.GetFileName(cr.SourcePath) ?? "", cr.Reason ?? ""),
                        ConversionOutcome.Blocked => _loc.Format("Log.ConvertDetailBlocked", Path.GetFileName(cr.SourcePath) ?? "", cr.Reason ?? ""),
                        _ => null
                    };
                    if (detail is not null)
                        AddLog(detail, "WARN");
                }
            }
        }

        // GUI-071: Console distribution bars
        ConsoleDistribution.Clear();
        foreach (var item in dashboard.ConsoleDistribution)
            ConsoleDistribution.Add(item);

        // GUI-072/073: Dedup decision browser
        DedupeGroupItems.Clear();
        foreach (var item in dashboard.DedupeGroups)
            DedupeGroupItems.Add(item);

        // GUI-074: Move consequence text
        MoveConsequenceText = dashboard.MoveConsequenceText;

        OnMovePreviewGateChanged();
    }

    private void WirePreviewGateObservers()
    {
        foreach (var filter in ExtensionFilters)
            filter.PropertyChanged += OnExtensionFilterChanged;
    }

    private void OnExtensionFilterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Models.ExtensionFilterItem.IsChecked))
        {
            InvalidateWizardAnalysis();
            OnMovePreviewGateChanged();
        }
    }

    private void OnConfigurationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not null && PreviewRelevantPropertyNames.Contains(e.PropertyName))
            OnMovePreviewGateChanged();

        if (e.PropertyName is nameof(DryRun) or nameof(ConvertOnly) or nameof(ConvertEnabled))
        {
            OnPropertyChanged(nameof(IsMovePhaseApplicable));
            OnPropertyChanged(nameof(IsConvertPhaseApplicable));
            OnPropertyChanged(nameof(ShowSkippedPhaseInfo));
            OnPropertyChanged(nameof(SkippedPhaseInfoText));
            OnPropertyChanged(nameof(ShowResultMoveButton));
            OnPropertyChanged(nameof(ShowActionBarMoveButton));
        }

        if (e.PropertyName is nameof(AggressiveJunk))
            InvalidateWizardAnalysis();
    }

    private string BuildPreviewConfigurationFingerprint()
    {
        var builder = new StringBuilder();
        builder.Append("roots=").AppendJoin(";", Roots).Append('|');
        builder.Append("regions=").AppendJoin(";", GetPreferredRegions()).Append('|');
        builder.Append("extensions=").AppendJoin(";", GetSelectedExtensions()).Append('|');
        builder.Append("removeJunk=").Append(RemoveJunk).Append('|');
        builder.Append("onlyGames=").Append(OnlyGames).Append('|');
        builder.Append("keepUnknownWhenOnlyGames=").Append(KeepUnknownWhenOnlyGames).Append('|');
        builder.Append("sortConsole=").Append(SortConsole).Append('|');
        builder.Append("aliasKeying=").Append(AliasKeying).Append('|');
        builder.Append("aggressiveJunk=").Append(AggressiveJunk).Append('|');
        builder.Append("useDat=").Append(UseDat).Append('|');
        builder.Append("enableDatRename=").Append(EnableDatRename).Append('|');
        builder.Append("approveReviews=").Append(ApproveReviews).Append('|');
        builder.Append("approveConversionReview=").Append(ApproveConversionReview).Append('|');
        builder.Append("datRoot=").Append(DatRoot).Append('|');
        builder.Append("datHashType=").Append(DatHashType).Append('|');
        builder.Append("convertEnabled=").Append(ConvertEnabled).Append('|');
        builder.Append("trashRoot=").Append(TrashRoot).Append('|');
        builder.Append("auditRoot=").Append(AuditRoot).Append('|');
        builder.Append("toolChdman=").Append(ToolChdman).Append('|');
        builder.Append("toolDolphin=").Append(ToolDolphin).Append('|');
        builder.Append("tool7z=").Append(Tool7z).Append('|');
        builder.Append("toolPsxtract=").Append(ToolPsxtract).Append('|');
        builder.Append("toolCiso=").Append(ToolCiso).Append('|');
        builder.Append("conflictPolicy=").Append(ConflictPolicy);
        return builder.ToString();
    }

    private void ResetDashboardForNewRun()
    {
        LastRunResult = null;
        LastCandidates = [];
        LastDedupeGroups = [];
        ErrorSummaryItems.Clear();
        ConsoleDistribution.Clear();
        DedupeGroupItems.Clear();

        DashWinners = "–";
        DashDupes = "–";
        DashJunk = "–";
        DashDuration = "00:00";
        HealthScore = "–";
        DashGames = "–";
        DashDatHits = "–";
        DashDatHave = "–";
        DashDatWrongName = "–";
        DashDatMiss = "–";
        DashDatUnknown = "–";
        DashDatAmbiguous = "–";
        DashDatRenameProposed = "–";
        DashDatRenameExecuted = "–";
        DashDatRenameFailed = "–";
        DashConverted = "–";
        DashConvertBlocked = "–";
        DashConvertReview = "–";
        DashConvertSaved = "–";
        DedupeRate = "–";
        IsConvertOnlyDashboard = false;
        DashboardContextHint = string.Empty;
        DashboardContextSeverity = UiErrorSeverity.Info;
        MoveConsequenceText = "";
        Progress = 0;
        ProgressText = "0%";
        RunSummaryText = "";
        RunSummarySeverity = UiErrorSeverity.Info;
        OnPropertyChanged(nameof(HasActionableErrorSummary));
        OnPropertyChanged(nameof(ActionableErrorSummaryItems));
        OnPropertyChanged(nameof(ActionableErrorSummaryTitle));
        RefreshToolSurfaceState();
    }

    private void OnMovePreviewGateChanged()
    {
        if (!CanStartMoveWithCurrentPreview && !DryRun && !ConvertOnly && !string.IsNullOrEmpty(_lastSuccessfulPreviewFingerprint))
            MoveConsequenceText = _loc["Config.Changed.MoveGate"];

        OnPropertyChanged(nameof(CanStartMoveWithCurrentPreview));
        OnPropertyChanged(nameof(ShowStartMoveButton));
        OnPropertyChanged(nameof(ShowResultMoveButton));
        OnPropertyChanged(nameof(ShowActionBarMoveButton));
        OnPropertyChanged(nameof(MoveApplyGateText));
        OnPropertyChanged(nameof(ShowConfigChangedBanner));
        OnPropertyChanged(nameof(RollbackActionHint));
        DeferCommandRequery();
    }

    private void SetRunSummary(string text, UiErrorSeverity severity)
    {
        RunSummaryText = text;
        RunSummarySeverity = severity;
    }
}
