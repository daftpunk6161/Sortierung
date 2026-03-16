using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows.Input;
using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.Orchestration;
using RomCleanup.UI.Wpf.Models;
using RomCleanup.UI.Wpf.Services;
using RunState = RomCleanup.UI.Wpf.Models.RunState;

namespace RomCleanup.UI.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private static readonly HashSet<string> PreviewRelevantPropertyNames =
    [
        nameof(SortConsole),
        nameof(AliasKeying),
        nameof(AggressiveJunk),
        nameof(UseDat),
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
        set { _lastCandidates = value; OnPropertyChanged(); }
    }

    private ObservableCollection<DedupeResult> _lastDedupeGroups = [];
    public ObservableCollection<DedupeResult> LastDedupeGroups
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

    private string? _lastSuccessfulPreviewFingerprint;
    public bool CanStartCurrentRun =>
        !IsBusy &&
        Roots.Count > 0 &&
        !HasBlockingValidationErrors &&
        (DryRun || CanStartMoveWithCurrentPreview);

    public bool CanStartMoveWithCurrentPreview =>
        !IsBusy &&
        Roots.Count > 0 &&
        _runState == RunState.CompletedDryRun &&
        string.Equals(_lastSuccessfulPreviewFingerprint, BuildPreviewConfigurationFingerprint(), StringComparison.Ordinal);

    public string MoveApplyGateText => CanStartMoveWithCurrentPreview
        ? "Danger-Zone freigeschaltet: Diese exakte Konfiguration wurde bereits als Vorschau geprüft. Move ist jetzt erlaubt."
        : string.IsNullOrEmpty(_lastSuccessfulPreviewFingerprint)
            ? "Move gesperrt: Führe zuerst eine Vorschau für die aktuelle Konfiguration aus."
            : "Move gesperrt: Die Konfiguration wurde seit der letzten Vorschau geändert. Bitte Vorschau erneut ausführen.";

    // ═══ RUN STATE (UX-002: explicit state machine) ════════════════════
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
                DeferCommandRequery();
            }
        }
    }

    /// <summary>RF-007: Checks whether the state transition is valid.</summary>
    internal static bool IsValidTransition(RunState from, RunState to)
    {
        if (from == to) return true;
        return (from, to) switch
        {
            // From Idle: can start preflight, or be cancelled/failed
            (RunState.Idle, RunState.Preflight) => true,
            // Pipeline forward progression
            (RunState.Preflight, RunState.Scanning) => true,
            (RunState.Scanning, RunState.Deduplicating) => true,
            (RunState.Deduplicating, RunState.Sorting) => true,
            (RunState.Sorting, RunState.Moving) => true,
            (RunState.Moving, RunState.Converting) => true,
            // Completion from any active phase
            (RunState.Preflight or RunState.Scanning or RunState.Deduplicating or
             RunState.Sorting or RunState.Moving or RunState.Converting,
             RunState.Completed or RunState.CompletedDryRun or RunState.Failed or RunState.Cancelled) => true,
            // Skip phases (e.g. no conversion step)
            (RunState.Scanning, RunState.Sorting or RunState.Moving or RunState.Converting) => true,
            (RunState.Deduplicating, RunState.Moving or RunState.Converting) => true,
            (RunState.Sorting, RunState.Converting) => true,
            // Reset from terminal states back to Idle
            (RunState.Completed or RunState.CompletedDryRun or RunState.Failed or RunState.Cancelled,
             RunState.Idle or RunState.Preflight) => true,
            _ => false,
        };
    }

    public bool IsBusy => _runState is RunState.Preflight or RunState.Scanning
        or RunState.Deduplicating or RunState.Sorting or RunState.Moving or RunState.Converting;
    public bool IsIdle => !IsBusy;

    public bool ShowStartMoveButton => CanStartMoveWithCurrentPreview;

    public bool HasRunResult => _runState is RunState.Completed or RunState.CompletedDryRun;

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
    public double Progress { get => _progress; set => SetProperty(ref _progress, value); }

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

    // ═══ STATUS INDICATORS ══════════════════════════════════════════════
    private string _statusRoots = "Roots: –";
    public string StatusRoots { get => _statusRoots; set => SetProperty(ref _statusRoots, value); }

    private string _statusTools = "Tools: –";
    public string StatusTools { get => _statusTools; set => SetProperty(ref _statusTools, value); }

    private string _statusDat = "DAT: –";
    public string StatusDat { get => _statusDat; set => SetProperty(ref _statusDat, value); }

    private string _statusReady = "Status: –";
    public string StatusReady { get => _statusReady; set => SetProperty(ref _statusReady, value); }

    private string _statusRuntime = "Laufzeit: –";
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

    private string _dedupeRate = "–";
    public string DedupeRate { get => _dedupeRate; set => SetProperty(ref _dedupeRate, value); }

    // ═══ GUI-070-073: ANALYSE SCREEN DATA ═══════════════════════════════
    public ObservableCollection<Models.ConsoleDistributionItem> ConsoleDistribution { get; } = [];
    public ObservableCollection<Models.DedupeGroupItem> DedupeGroupItems { get; } = [];

    // GUI-074: Move consequence text
    private string _moveConsequenceText = "";
    public string MoveConsequenceText { get => _moveConsequenceText; set => SetProperty(ref _moveConsequenceText, value); }

    // ═══ STEP INDICATOR ═════════════════════════════════════════════════
    private int _currentStep;
    public int CurrentStep { get => _currentStep; set => SetProperty(ref _currentStep, value); }

    // RD-004: Phase detail tooltips for interactive stepper
    /// <summary>Returns a detail string for the given pipeline phase (1–7). Used by stepper tooltips.</summary>
    public string GetPhaseDetail(int phase) => phase switch
    {
        1 => HasRunResult && LastRunResult is { } r
            ? $"Preflight: {(r.Preflight?.ShouldReturn != true ? "✓ OK" : $"✗ Blockiert – {r.Preflight?.Reason}")}"
            : "Preflight: Konfiguration und Pfade prüfen",
        2 => HasRunResult && LastRunResult is { } r2
            ? $"Scan: {r2.TotalFilesScanned} Dateien gefunden"
            : "Scan: ROM-Verzeichnisse durchsuchen",
        3 => HasRunResult && LastRunResult is { } r3
            ? $"Dedupe: {r3.WinnerCount} behalten, {r3.LoserCount} Duplikate"
            : "Dedupe: Duplikate erkennen und beste Version wählen",
        4 => "Sort: Dateien nach Konsole gruppieren",
        5 => HasRunResult && LastRunResult?.MoveResult is { } mv
            ? $"Move: {mv.MoveCount} verschoben, {mv.FailCount} Fehler"
            : "Move: Duplikate in Papierkorb verschieben",
        6 => HasRunResult && LastRunResult is { } r6 && r6.ConvertedCount > 0
            ? $"Convert: {r6.ConvertedCount} Dateien konvertiert"
            : "Convert: Formate optimieren (CHD/RVZ/ZIP)",
        7 => HasRunResult ? $"Fertig – Dauer: {DashDuration}" : "Fertig: Ergebnis und Report",
        _ => ""
    };

    private string _stepLabel1 = "Keine Ordner";
    public string StepLabel1 { get => _stepLabel1; set => SetProperty(ref _stepLabel1, value); }

    private string _stepLabel2 = "Bereit";
    public string StepLabel2 { get => _stepLabel2; set => SetProperty(ref _stepLabel2, value); }

    private string _stepLabel3 = "F5 drücken";
    public string StepLabel3 { get => _stepLabel3; set => SetProperty(ref _stepLabel3, value); }

    // ═══ RUN COMMAND HANDLERS ═══════════════════════════════════════════

    /// <summary>Confirm before destructive Move operations (uses injected IDialogService).</summary>
    public bool ConfirmMoveDialog()
    {
        // V2-M20: Show statistics in move confirmation dialog
        return _dialog.Confirm(
            $"Modus 'Move' verschiebt Dateien in den Papierkorb.\n"
            + $"Roots: {string.Join(", ", Roots)}\n"
            + $"Gewinner: {DashWinners} | Duplikate: {DashDupes} | Junk: {DashJunk}\n\nFortfahren?",
            "Move bestätigen");
    }

    private void OnRun()
    {
        if (HasBlockingValidationErrors)
        {
            var blockingValidationMessage = GetBlockingValidationMessage();
            AddLog(blockingValidationMessage, "WARN");
            _dialog.Info(blockingValidationMessage, "Start gesperrt");
            DeferCommandRequery();
            return;
        }

        if (!DryRun && !ConvertOnly && !CanStartMoveWithCurrentPreview)
        {
            AddLog(MoveApplyGateText, "WARN");
            _dialog.Info(MoveApplyGateText, "Move gesperrt");
            DeferCommandRequery();
            return;
        }

        CurrentRunState = RunState.Preflight;
        BusyHint = ConvertOnly ? "Konvertierung läuft…" : DryRun ? (IsSimpleMode ? "Vorschau läuft…" : "DryRun läuft…") : "Move läuft…";
        DashMode = ConvertOnly ? "Convert" : DryRun ? (IsSimpleMode ? "Vorschau" : "DryRun") : "Move";
        Progress = 0;
        ProgressText = "0%";
        PerfPhase = "Phase: –";
        PerfFile = "Datei: –";
        ShowMoveCompleteBanner = false;

        RunRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnCancel()
    {
        // V2-THR-H02: Use lock to prevent race between Cancel and CreateRunCancellation/Dispose
        CancellationTokenSource? cts;
        lock (_ctsLock)
        {
            cts = _cts;
        }
        try { cts?.Cancel(); } catch (ObjectDisposedException) { }
        CurrentRunState = RunState.Cancelled;
        BusyHint = "Abbruch angefordert…";
    }

    private async Task OnRollbackAsync()
    {
        if (!_dialog.Confirm("Letzten Lauf rückgängig machen?", "Rollback bestätigen"))
            return;

        if (string.IsNullOrEmpty(LastAuditPath) || !File.Exists(LastAuditPath))
        {
            AddLog("Keine Audit-Datei gefunden — Rollback nicht möglich.", "WARN");
            return;
        }

        try
        {
            var auditPathCopy = LastAuditPath;
            var roots = Roots.ToList();
            var restored = await Task.Run(() => RollbackService.Execute(auditPathCopy, roots));
            AddLog($"Rollback: {restored.RolledBack} Dateien wiederhergestellt.", "INFO");
            CanRollback = false;
            ShowMoveCompleteBanner = false;
        }
        catch (Exception ex)
        {
            AddLog($"Rollback-Fehler: {ex.Message}", "ERROR");
        }
    }

    private void OnAddRoot()
    {
        var folder = _dialog.BrowseFolder("ROM-Ordner auswählen");
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
        if (!string.IsNullOrEmpty(LastReportPath) && System.IO.File.Exists(LastReportPath))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = LastReportPath,
                UseShellExecute = true
            });
        }
    }

    /// <summary>Complete a run (call from UI thread when orchestration finishes).</summary>
    public void CompleteRun(bool success, string? reportPath = null)
    {
        BusyHint = "";
        ConvertOnly = false; // Reset transient flag
        LastReportPath = reportPath ?? string.Empty;
        if (success && DryRun)
        {
            _lastSuccessfulPreviewFingerprint = BuildPreviewConfigurationFingerprint();
            CurrentRunState = RunState.CompletedDryRun;
        }
        else if (success && !DryRun)
        {
            CurrentRunState = RunState.Completed;
            CanRollback = true;
            ShowMoveCompleteBanner = true;
        }
        else
        {
            CurrentRunState = RunState.Failed;
        }
        RefreshStatus();
        OnMovePreviewGateChanged();
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
        StatusRoots = hasRoots ? $"{Roots.Count} Ordner konfiguriert" : "Keine Ordner";
        StepLabel1 = hasRoots ? $"{Roots.Count} Ordner" : "Keine Ordner";

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
        StatusTools = ToolsStatusLevel == StatusLevel.Ok ? $"{toolCount} Tools gefunden"
            : ToolsStatusLevel == StatusLevel.Warning ? "Tools nicht gefunden" : "Keine Tools";

        // P1-007: Update tool status labels
        ChdmanStatusText = string.IsNullOrWhiteSpace(ToolChdman) ? "–" : hasChdman ? "✓ Gefunden" : "✗ Nicht gefunden";
        DolphinStatusText = string.IsNullOrWhiteSpace(ToolDolphin) ? "–" : hasDolphin ? "✓ Gefunden" : "✗ Nicht gefunden";
        SevenZipStatusText = string.IsNullOrWhiteSpace(Tool7z) ? "–" : has7z ? "✓ Gefunden" : "✗ Nicht gefunden";
        PsxtractStatusText = string.IsNullOrWhiteSpace(ToolPsxtract) ? "–" : hasPsxtract ? "✓ Gefunden" : "✗ Nicht gefunden";
        CisoStatusText = string.IsNullOrWhiteSpace(ToolCiso) ? "–" : hasCiso ? "✓ Gefunden" : "✗ Nicht gefunden";

        // DAT
        bool datRootValid = !string.IsNullOrWhiteSpace(DatRoot) && Directory.Exists(DatRoot);
        DatStatusLevel = !UseDat ? StatusLevel.Missing
            : datRootValid ? StatusLevel.Ok
            : !string.IsNullOrWhiteSpace(DatRoot) ? StatusLevel.Warning
            : StatusLevel.Warning;
        StatusDat = DatStatusLevel == StatusLevel.Ok ? "DAT aktiv"
            : DatStatusLevel == StatusLevel.Warning ? "DAT-Pfad ungültig" : "DAT deaktiviert";

        // Overall readiness
        var validationSummary = GetValidationSummary();
        ReadyStatusLevel = !hasRoots || validationSummary.HasBlockers ? StatusLevel.Blocked
            : ToolsStatusLevel == StatusLevel.Warning || DatStatusLevel == StatusLevel.Warning || validationSummary.HasWarnings ? StatusLevel.Warning
            : StatusLevel.Ok;
        StatusReady = ReadyStatusLevel switch
        {
            StatusLevel.Ok => "Startbereit ✓",
            StatusLevel.Warning => "Startbereit (Warnung) ⚠",
            StatusLevel.Blocked => "Nicht bereit ✗",
            _ => "Status: –"
        };

        // Step indicator with RunState-awareness
        if (IsBusy)
        {
            CurrentStep = 2;
            StepLabel3 = _runState switch
            {
                RunState.Preflight => "Prüfe…",
                RunState.Scanning => "Scanne…",
                RunState.Deduplicating => "Dedupliziere…",
                RunState.Sorting => "Sortiere…",
                RunState.Moving => "Verschiebe…",
                RunState.Converting => "Konvertiere…",
                _ => "Läuft…"
            };
        }
        else if (_runState is RunState.Completed or RunState.CompletedDryRun)
        {
            CurrentStep = 3;
            StepLabel3 = _runState == RunState.CompletedDryRun ? "Vorschau fertig" : "Abgeschlossen";
        }
        else
        {
            CurrentStep = hasRoots ? 1 : 0;
            StepLabel3 = "F5 drücken";
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
            AddLog("Initialisierung…", "INFO");

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

            var svcResult = await Task.Run(
                () => _runService.ExecuteRun(orchestrator, runOptions, auditPath, reportPath, ct), ct);

            ApplyRunResult(svcResult.Result);
            LastAuditPath = auditPath;

            if (!DryRun && auditPath is not null && File.Exists(auditPath))
                PushRollbackUndo(auditPath);

            if (svcResult.ReportPath is not null)
                AddLog($"Report: {svcResult.ReportPath}", "INFO");

            if (!ct.IsCancellationRequested)
            {
                AddLog("Lauf abgeschlossen.", "INFO");
                CompleteRun(true, svcResult.ReportPath);
                PopulateErrorSummary();
            }
            else
            {
                AddLog("Lauf abgebrochen.", "WARN");
                CompleteRun(false);
            }
        }
        catch (OperationCanceledException)
        {
            AddLog("Lauf abgebrochen.", "WARN");
            CompleteRun(false);
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
                    AddLog($"Scheduler: Geplanter Lauf gestartet.", "INFO");
                    DryRun = true;
                    RunCommand.Execute(null);
                }
            }, null);
        }, null, interval, interval);

        AddLog($"Scheduler: Alle {SchedulerIntervalDisplay} wird ein DryRun gestartet.", "INFO");
    }

    private void ToggleWatchMode()
    {
        if (Roots.Count == 0)
        { AddLog("Keine Root-Ordner für Watch-Mode.", "WARN"); return; }

        _watchService.IsBusyCheck = () => IsBusy;

        var count = _watchService.Start(Roots);
        IsWatchModeActive = _watchService.IsActive;
        if (count == 0)
        {
            AddLog("Watch-Mode deaktiviert.", "INFO");
            _dialog.Info("Watch-Mode wurde deaktiviert.\n\nDateiüberwachung gestoppt.", "Watch-Mode");
        }
        else
        {
            AddLog($"Watch-Mode aktiviert für {count} Ordner. Änderungen werden überwacht.", "INFO");
            _dialog.Info($"Watch-Mode ist aktiv!\n\nÜberwachte Ordner:\n{string.Join("\n", Roots)}\n\nBei Dateiänderungen wird automatisch ein DryRun gestartet.\n\nErneut klicken zum Deaktivieren.",
                "Watch-Mode");
        }
    }

    private void OnWatchRunTriggered()
    {
        if (Roots.Count > 0)
        {
            AddLog("Watch-Mode: Änderungen erkannt, starte DryRun…", "INFO");
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

        var issues = new List<UiError>();

        // Collect log-based warnings/errors
        foreach (var e in LogEntries.Skip(_runLogStartIndex))
        {
            if (e.Level is "WARN")
                issues.Add(new UiError("RUN-WARN", e.Text, UiErrorSeverity.Warning));
            else if (e.Level is "ERROR")
                issues.Add(new UiError("RUN-ERR", e.Text, UiErrorSeverity.Error));
        }

        if (LastRunResult is not null)
        {
            if (LastRunResult.Status == "blocked")
                issues.Insert(0, new UiError("RUN-BLOCKED", $"Preflight: {LastRunResult.Preflight?.Reason}", UiErrorSeverity.Blocked));

            if (LastRunResult.MoveResult is { FailCount: > 0 } mv)
                issues.Insert(0, new UiError("IO-MOVE", $"{mv.FailCount} Dateien konnten nicht verschoben werden", UiErrorSeverity.Error));

            var junk = LastCandidates.Count(c => c.Category == "JUNK");
            if (junk > 0)
                issues.Insert(0, new UiError("RUN-JUNK", $"{junk} Junk-Dateien erkannt", UiErrorSeverity.Warning));

            var unverified = LastCandidates.Count(c => !c.DatMatch);
            if (unverified > 0 && LastCandidates.Count > 0)
                issues.Insert(0, new UiError("DAT-UNVERIFIED", $"{unverified}/{LastCandidates.Count} Dateien ohne DAT-Verifizierung", UiErrorSeverity.Info));
        }

        if (issues.Count == 0)
        {
            ErrorSummaryItems.Add(new UiError("RUN-OK", "Keine Fehler oder Warnungen.", UiErrorSeverity.Info));
            if (LastRunResult is not null)
                ErrorSummaryItems.Add(new UiError("RUN-STATS", $"Report geladen: {LastRunResult.WinnerCount} Winner, {LastRunResult.LoserCount} Dupes", UiErrorSeverity.Info));
            return;
        }

        foreach (var issue in issues.Take(50))
            ErrorSummaryItems.Add(issue);
        if (issues.Count > 50)
            ErrorSummaryItems.Add(new UiError("RUN-TRUNC", $"… und {issues.Count - 50} weitere", UiErrorSeverity.Warning));
    }

    /// <summary>Apply run results from orchestrator to all dashboard/state properties.</summary>
    public void ApplyRunResult(RunResult result)
    {
        LastRunResult = result;
        LastCandidates = new ObservableCollection<RomCandidate>(result.AllCandidates);
        LastDedupeGroups = new ObservableCollection<DedupeResult>(result.DedupeGroups);
        RefreshToolLockState();

        Progress = 100;
        DashWinners = result.WinnerCount.ToString();
        DashDupes = result.LoserCount.ToString();
        var junkCount = result.AllCandidates.Count(c => c.Category == "JUNK");
        DashJunk = junkCount.ToString();
        DashDuration = $"{result.DurationMs / 1000.0:F1}s";
        var total = result.AllCandidates.Count;
        var verified = result.AllCandidates.Count(c => c.DatMatch);
        HealthScore = total > 0
            ? $"{FeatureService.CalculateHealthScore(total, result.LoserCount, junkCount, verified)}%"
            : "–";

        var gameCount = result.DedupeGroups.Count;
        DashGames = gameCount.ToString();
        var datHits = result.AllCandidates.Count(c => c.DatMatch);
        DashDatHits = datHits.ToString();
        DedupeRate = gameCount > 0 ? $"{100.0 * result.LoserCount / (result.WinnerCount + result.LoserCount):F0}%" : "–";

        if (result.Status == "blocked")
        {
            AddLog($"Preflight blockiert: {result.Preflight?.Reason}", "ERROR");
        }
        else
        {
            AddLog($"Scan: {result.TotalFilesScanned} Dateien", "INFO");
            AddLog($"Dedupe: Keep={result.WinnerCount}, Move={result.LoserCount}, Junk={junkCount}", "INFO");
            if (result.MoveResult is { } mv)
                AddLog($"Verschoben: {mv.MoveCount}, Fehler: {mv.FailCount}", mv.FailCount > 0 ? "WARN" : "INFO");
            if (result.ConvertedCount > 0)
                AddLog($"Konvertiert: {result.ConvertedCount}", "INFO");
        }

        // GUI-071: Console distribution bars
        ConsoleDistribution.Clear();
        var consoleCounts = result.AllCandidates
            .Where(c => !string.IsNullOrEmpty(c.ConsoleKey))
            .GroupBy(c => c.ConsoleKey)
            .Select(g => (Key: g.Key, Count: g.Count()))
            .OrderByDescending(x => x.Count)
            .Take(20)
            .ToList();
        int maxCount = consoleCounts.Count > 0 ? consoleCounts[0].Count : 1;
        foreach (var (key, count) in consoleCounts)
            ConsoleDistribution.Add(new Models.ConsoleDistributionItem
            {
                ConsoleKey = key,
                DisplayName = key,
                FileCount = count,
                Fraction = (double)count / maxCount
            });

        // GUI-072/073: Dedup decision browser
        DedupeGroupItems.Clear();
        foreach (var grp in result.DedupeGroups.Take(200))
        {
            DedupeGroupItems.Add(new Models.DedupeGroupItem
            {
                GameKey = grp.GameKey,
                Winner = new Models.DedupeEntryItem
                {
                    FileName = System.IO.Path.GetFileName(grp.Winner.MainPath),
                    Region = grp.Winner.Region,
                    RegionScore = grp.Winner.RegionScore,
                    FormatScore = grp.Winner.FormatScore,
                    VersionScore = grp.Winner.VersionScore,
                    IsWinner = true
                },
                Losers = grp.Losers.Select(l => new Models.DedupeEntryItem
                {
                    FileName = System.IO.Path.GetFileName(l.MainPath),
                    Region = l.Region,
                    RegionScore = l.RegionScore,
                    FormatScore = l.FormatScore,
                    VersionScore = l.VersionScore,
                    IsWinner = false
                }).ToList()
            });
        }

        // GUI-074: Move consequence text
        MoveConsequenceText = result.LoserCount > 0
            ? $"{result.LoserCount} Dateien werden in den Papierkorb verschoben"
            : "Keine Dateien zum Verschieben";

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
    }

    private string BuildPreviewConfigurationFingerprint()
    {
        var builder = new StringBuilder();
        builder.Append("roots=").AppendJoin(";", Roots).Append('|');
        builder.Append("regions=").AppendJoin(";", GetPreferredRegions()).Append('|');
        builder.Append("extensions=").AppendJoin(";", GetSelectedExtensions()).Append('|');
        builder.Append("sortConsole=").Append(SortConsole).Append('|');
        builder.Append("aliasKeying=").Append(AliasKeying).Append('|');
        builder.Append("aggressiveJunk=").Append(AggressiveJunk).Append('|');
        builder.Append("useDat=").Append(UseDat).Append('|');
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

    private void OnMovePreviewGateChanged()
    {
        OnPropertyChanged(nameof(CanStartMoveWithCurrentPreview));
        OnPropertyChanged(nameof(ShowStartMoveButton));
        OnPropertyChanged(nameof(MoveApplyGateText));
        DeferCommandRequery();
    }
}
