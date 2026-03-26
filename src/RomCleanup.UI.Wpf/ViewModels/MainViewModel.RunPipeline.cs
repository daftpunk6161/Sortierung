using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows.Input;
using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.Conversion;
using RomCleanup.Infrastructure.Orchestration;
using RomCleanup.UI.Wpf.Models;
using RomCleanup.UI.Wpf.Services;
using RunState = RomCleanup.UI.Wpf.Models.RunState;

namespace RomCleanup.UI.Wpf.ViewModels;

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
        set { _lastRunResult = value; OnPropertyChanged(); }
    }

    private string? _lastAuditPath;
    public string? LastAuditPath
    {
        get => _lastAuditPath;
        set
        {
            _lastAuditPath = value;
            OnPropertyChanged();
            CanRollback = !string.IsNullOrEmpty(_lastAuditPath) && File.Exists(_lastAuditPath);
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

    public bool HasRunResult => Run.HasRunResult;

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
        nameof(HasRunResult), nameof(ShowStartMoveButton)
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
            OnPropertyChanged(nameof(RunStateDisplayText));
            OnPropertyChanged(nameof(ShowStartMoveButton));
            OnPropertyChanged(nameof(CanStartCurrentRun));
            OnPropertyChanged(nameof(CanStartMoveWithCurrentPreview));
            DeferCommandRequery();
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
        set { SetProperty(ref _canRollback, value); DeferCommandRequery(); }
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

    public string RollbackActionHint => Run.RollbackActionHint;

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
    public string DashMode { get => Run.DashMode; set => Run.DashMode = value; }
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
        4 => _loc["Phase.Sort.Desc"],
        5 when !IsMovePhaseApplicable => _loc["Phase.Move.Skipped"],
        5 => HasRunResult && LastRunResult?.MoveResult is { } mv
            ? $"Move: {mv.MoveCount} / {mv.FailCount}"
            : _loc["Phase.Move.Desc"],
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

    private async Task<bool> ConfirmConversionReviewDialogAsync(RunOptions runOptions, CancellationToken cancellationToken)
    {
        if (DryRun || runOptions.ConvertFormat is null)
            return true;

        if (runOptions.Mode != "Move" && !runOptions.ConvertOnly)
            return true;

        if (runOptions.ConvertOnly)
            return true;

        if (LastDedupeGroups.Count == 0)
            return true;

        var reviewEntries = await Task.Run(() => BuildConversionReviewEntries(runOptions, cancellationToken), cancellationToken);
        if (reviewEntries.Count == 0)
            return true;

        var summary = _loc["Dialog.ConfirmConversion.Summary"];

        var confirmed = DialogService.ConfirmConversionReview(
            _loc["Dialog.ConfirmConversion.Title"],
            summary,
            reviewEntries);

        if (!confirmed)
            AddLog(_loc["Log.ConversionCancelled"], "WARN");

        return confirmed;
    }

    private Task<bool> ConfirmDatRenamePreviewDialogAsync(RunOptions runOptions, CancellationToken cancellationToken)
    {
        if (DryRun || runOptions.Mode != "Move" || !runOptions.EnableDatRename)
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
        var env = RunEnvironmentBuilder.Build(runOptions, settings, dataDir);

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

    private void OnRun()
    {
        if (HasBlockingValidationErrors)
        {
            var blockingValidationMessage = GetBlockingValidationMessage();
            AddLog(blockingValidationMessage, "WARN");
            _dialog.Info(blockingValidationMessage, _loc["Dialog.Info.StartBlocked"]);
            DeferCommandRequery();
            return;
        }

        if (!DryRun && !ConvertOnly && !CanStartMoveWithCurrentPreview)
        {
            AddLog(MoveApplyGateText, "WARN");
            _dialog.Info(MoveApplyGateText, _loc["Dialog.Info.MoveBlocked"]);
            DeferCommandRequery();
            return;
        }

        CurrentRunState = RunState.Preflight;
        ResetDashboardForNewRun();
        BusyHint = ConvertOnly ? _loc["Progress.BusyHint.Converting"] : DryRun ? (IsSimpleMode ? _loc["Progress.BusyHint.Preview"] : _loc["Progress.BusyHint.DryRun"]) : _loc["Progress.BusyHint.Move"];
        DashMode = ConvertOnly ? "Convert" : DryRun ? (IsSimpleMode ? _loc["Progress.BusyHint.Preview"] : "DryRun") : "Move";
        Progress = 0;
        ProgressText = "0%";
        PerfPhase = "–";
        PerfFile = "–";
        ShowMoveCompleteBanner = false;
        RunSummaryText = "";
        RunSummarySeverity = UiErrorSeverity.Info;

        RunRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnCancel()
    {
        Shell.ShowMoveInlineConfirm = false;
        // F-02 FIX: Cancel under lock to prevent race with CreateRunCancellation/Dispose
        lock (_ctsLock)
        {
            try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
        }
        CurrentRunState = RunState.Cancelled;
        BusyHint = _loc["Progress.BusyHint.CancelRequested"];
        ShowMoveCompleteBanner = false;
        SetRunSummary(_loc["Result.Summary.Cancelled"], UiErrorSeverity.Warning);
    }

    private async Task OnRollbackAsync()
    {
        var rollbackPreview = _loc.Format("Dialog.Rollback.Preview", MoveConsequenceText, DashWinners, DashDupes, DashJunk);

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
            var restored = await Task.Run(() => RomCleanup.Infrastructure.Audit.RollbackService.Execute(auditPathCopy, roots));
            AddLog(_loc.Format("Log.RollbackDone", restored.RolledBack, restored.SkippedMissingDest, restored.SkippedCollision, restored.Failed),
                restored.Failed > 0 ? "WARN" : "INFO");
            CanRollback = false;
            ShowMoveCompleteBanner = false;
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
    public void CompleteRun(bool success, string? reportPath = null, bool cancelled = false)
    {
        Shell.ShowMoveInlineConfirm = false;
        BusyHint = "";
        ConvertOnly = false; // Reset transient flag
        var resolvedReportPath = string.IsNullOrWhiteSpace(reportPath) ? null : reportPath;
        LastReportPath = resolvedReportPath ?? string.Empty;
        CanRollback = !string.IsNullOrEmpty(LastAuditPath) && File.Exists(LastAuditPath);
        if (success && DryRun)
        {
            _lastSuccessfulPreviewFingerprint = BuildPreviewConfigurationFingerprint();
            CurrentRunState = RunState.CompletedDryRun;
            Shell.SelectedNavTag = "Analyse";
            SelectedResultSection = "Dashboard";
            IsResultPerfDetailsExpanded = true;
            SetRunSummary(
                _loc.Format("Result.Summary.PreviewDone", DashDupes, DashJunk),
                UiErrorSeverity.Info);
        }
        else if (success && !DryRun)
        {
            CurrentRunState = RunState.Completed;
            CanRollback = true;
            ShowMoveCompleteBanner = true;
            Shell.SelectedNavTag = "Analyse";
            SelectedResultSection = "Dashboard";
            IsResultPerfDetailsExpanded = true;
            SetRunSummary(_loc.Format("Result.Summary.ChangesApplied", MoveConsequenceText), UiErrorSeverity.Info);
        }
        else if (cancelled)
        {
            CurrentRunState = RunState.Cancelled;
            Shell.SelectedNavTag = "Analyse";
            SelectedResultSection = "Dashboard";
            SetRunSummary(_loc["Result.Summary.CancelledPartial"], UiErrorSeverity.Warning);
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

    private static string? TryFindLatestReportPath()
    {
        var dirs = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), RomCleanup.Contracts.AppIdentity.AppFolderName, "reports"),
            Path.Combine(Directory.GetCurrentDirectory(), "reports")
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
    }

    // ═══ RUN PIPELINE EXECUTION ═════════════════════════════════════════

    /// <summary>Execute the full run pipeline (scan, dedupe, sort, convert, move).</summary>
    public async Task ExecuteRunAsync()
    {
        if (!DryRun && !ConvertOnly && ConfirmMove && !ConfirmMoveDialog())
        {
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
                return _runService.BuildOrchestrator(this, msg =>
                {
                    var now = DateTime.UtcNow;
                    if ((now - lastProgressUpdate).TotalMilliseconds < 100) return;
                    lastProgressUpdate = now;
                    _syncContext?.Post(_ =>
                    {
                        var phaseProgress = EstimatePhaseProgress(msg);
                        if (phaseProgress >= 0)
                            Progress = phaseProgress;
                        ProgressText = msg;
                        if (msg.StartsWith("[") && msg.Contains(']'))
                        {
                            var phase = msg[..(msg.IndexOf(']') + 1)];
                            PerfPhase = $"Phase: {phase}";
                            var rest = msg[(msg.IndexOf(']') + 1)..].Trim();
                            if (rest.Length > 0) PerfFile = $"Datei: {rest}";
                        }
                        AddLog(msg, "INFO");
                    }, null);
                });
            }, ct);

            if (!await ConfirmConversionReviewDialogAsync(runOptions, ct))
            {
                CurrentRunState = RunState.Idle;
                return;
            }

            if (!await ConfirmDatRenamePreviewDialogAsync(runOptions, ct))
            {
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
                AddLog(_loc["Log.RunComplete"], "INFO");
                CompleteRun(true, svcResult.ReportPath);
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
            var error = RomCleanup.Contracts.Errors.ErrorClassifier.FromException(ex, "GUI");
            AddLog($"[{error.Kind}] {error.Code}: {error.Message}", "ERROR");
            CompleteRun(false);
        }
        finally
        {
            _watchService.FlushPendingIfNeeded();
        }
    }

    // ═══ WATCH-MODE ════════════════════════════════════════════════════

    private System.Threading.Timer? _schedulerTimer;

    /// <summary>GUI-109: Start/stop periodic scheduled runs.</summary>
    public void ApplyScheduler()
    {
        _schedulerTimer?.Dispose();
        _schedulerTimer = null;

        if (SchedulerIntervalMinutes <= 0 || Roots.Count == 0) return;

        var interval = TimeSpan.FromMinutes(SchedulerIntervalMinutes);
        _schedulerTimer = new System.Threading.Timer(_ =>
        {
            _syncContext?.Post(_ =>
            {
                if (!IsBusy && Roots.Count > 0)
                {
                    AddLog(_loc["Log.ScheduledRunStarted"], "INFO");
                    DryRun = true;
                    RunCommand.Execute(null);
                }
            }, null);
        }, null, interval, interval);

        AddLog(_loc.Format("Log.SchedulerActive", SchedulerIntervalDisplay), "INFO");
    }

    private void ToggleWatchMode()
    {
        if (Roots.Count == 0)
        { AddLog(_loc["Log.WatchModeNoRoots"], "WARN"); return; }

        _watchService.IsBusyCheck = () => IsBusy;

        var count = _watchService.Start(Roots);
        IsWatchModeActive = _watchService.IsActive;
        if (count == 0)
        {
            AddLog(_loc["Log.WatchModeDeactivated"], "INFO");
            _dialog.Info(_loc["Dialog.Watch.Deactivated"], _loc["Dialog.Watch.Title"]);
        }
        else
        {
            AddLog(_loc.Format("Log.WatchModeActivated", count), "INFO");
            _dialog.Info(_loc.Format("Dialog.Watch.Activated", string.Join("\n", Roots)),
                _loc["Dialog.Watch.Title"]);
        }
    }

    private void OnWatchRunTriggered()
    {
        if (Roots.Count > 0)
        {
            AddLog(_loc["Log.WatchTriggered"], "INFO");
            DryRun = true;
            RunCommand.Execute(null);
        }
    }

    /// <summary>GUI-115: Dispose watch-mode and scheduler resources — unsubscribe all events.</summary>
    public void CleanupWatchers()
    {
        _watchService.RunTriggered -= OnWatchRunTriggered;
        _watchService.WatcherError -= OnWatcherError;
        _watchService.Dispose();
        _schedulerTimer?.Dispose();
        _schedulerTimer = null;
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
        LastCandidates = new ObservableCollection<RomCandidate>(result.AllCandidates);
        LastDedupeGroups = new ObservableCollection<DedupeGroup>(result.DedupeGroups);
        RefreshToolLockState();
        var projection = RunProjectionFactory.Create(result);

        Progress = 100;
        var isConvertOnlyRun = ConvertOnly ||
                               (result.MoveResult is null && result.JunkMoveResult is null &&
                                (result.ConvertedCount > 0 || result.ConvertErrorCount > 0 || result.ConvertSkippedCount > 0));
        var dashboard = DashboardProjection.From(projection, result, isConvertOnlyRun);

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
        DedupeRate = dashboard.DedupeRate;

        // GUI-021: Sync to MissionControl child VM
        MissionControl.UpdateLastRun(dashboard.Winners, dashboard.Dupes, dashboard.Junk, dashboard.Duration);

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
            OnMovePreviewGateChanged();
    }

    private void OnConfigurationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not null && PreviewRelevantPropertyNames.Contains(e.PropertyName))
            OnMovePreviewGateChanged();

        if (e.PropertyName is nameof(DryRun) or nameof(ConvertOnly) or nameof(ConvertEnabled))
        {
            OnPropertyChanged(nameof(IsMovePhaseApplicable));
            OnPropertyChanged(nameof(IsConvertPhaseApplicable));
        }
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
        DedupeRate = "–";
        MoveConsequenceText = "";
        Progress = 0;
        ProgressText = "0%";
        RunSummaryText = "";
        RunSummarySeverity = UiErrorSeverity.Info;
    }

    private void OnMovePreviewGateChanged()
    {
        if (!CanStartMoveWithCurrentPreview && !DryRun && !ConvertOnly && !string.IsNullOrEmpty(_lastSuccessfulPreviewFingerprint))
            MoveConsequenceText = _loc["Config.Changed.MoveGate"];

        OnPropertyChanged(nameof(CanStartMoveWithCurrentPreview));
        OnPropertyChanged(nameof(ShowStartMoveButton));
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

    private static double EstimatePhaseProgress(string message)
    {
        if (string.IsNullOrEmpty(message) || !message.StartsWith("[", StringComparison.Ordinal))
            return -1;

        return message switch
        {
            var m when m.StartsWith("[Preflight]", StringComparison.OrdinalIgnoreCase) => 8,
            var m when m.StartsWith("[Scan]", StringComparison.OrdinalIgnoreCase) => 22,
            var m when m.StartsWith("[Dedupe]", StringComparison.OrdinalIgnoreCase) => 40,
            var m when m.StartsWith("[Junk]", StringComparison.OrdinalIgnoreCase) => 50,
            var m when m.StartsWith("[Move]", StringComparison.OrdinalIgnoreCase) => 65,
            var m when m.StartsWith("[Sort]", StringComparison.OrdinalIgnoreCase) => 78,
            var m when m.StartsWith("[Convert]", StringComparison.OrdinalIgnoreCase) => 90,
            var m when m.StartsWith("[Report]", StringComparison.OrdinalIgnoreCase) => 96,
            var m when m.StartsWith("[Fertig]", StringComparison.OrdinalIgnoreCase) => 100,
            _ => -1,
        };
    }

    private static int GetConsoleSortFailureCount(object sortResult)
    {
        var type = sortResult.GetType();

        if (type.GetProperty("Failed")?.GetValue(sortResult) is int failed)
            return failed;

        if (type.GetProperty("FailCount")?.GetValue(sortResult) is int failCount)
            return failCount;

        return 0;
    }
}
