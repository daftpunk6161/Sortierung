using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.Orchestration;
using RomCleanup.UI.Wpf.Models;
using RomCleanup.UI.Wpf.Services;
using RunState = RomCleanup.UI.Wpf.Models.RunState;

namespace RomCleanup.UI.Wpf.ViewModels;

public sealed partial class MainViewModel
{
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

            if (SetField(ref _runState, value))
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

    public bool ShowStartMoveButton => _runState == RunState.CompletedDryRun && !IsBusy;

    public bool HasRunResult => _runState is RunState.Completed or RunState.CompletedDryRun;

    // ═══ ROLLBACK HISTORY (UX-010) ══════════════════════════════════════
    private readonly Stack<string> _rollbackUndoStack = new();
    private readonly Stack<string> _rollbackRedoStack = new();

    public bool HasRollbackUndo => _rollbackUndoStack.Count > 0;
    public bool HasRollbackRedo => _rollbackRedoStack.Count > 0;

    public void PushRollbackUndo(string auditPath)
    {
        _rollbackUndoStack.Push(auditPath);
        _rollbackRedoStack.Clear();
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
    public double Progress { get => _progress; set => SetField(ref _progress, value); }

    private string _progressText = "";
    public string ProgressText { get => _progressText; set => SetField(ref _progressText, value); }

    private string _perfPhase = "Phase: –";
    public string PerfPhase { get => _perfPhase; set => SetField(ref _perfPhase, value); }

    private string _perfFile = "Datei: –";
    public string PerfFile { get => _perfFile; set => SetField(ref _perfFile, value); }

    private string _busyHint = "";
    public string BusyHint { get => _busyHint; set => SetField(ref _busyHint, value); }

    // ═══ MISC UI STATE ══════════════════════════════════════════════════
    private string? _selectedRoot;
    public string? SelectedRoot
    {
        get => _selectedRoot;
        set { SetField(ref _selectedRoot, value); DeferCommandRequery(); }
    }

    private bool _canRollback;
    public bool CanRollback
    {
        get => _canRollback;
        set { SetField(ref _canRollback, value); DeferCommandRequery(); }
    }

    private string _lastReportPath = "";
    public string LastReportPath
    {
        get => _lastReportPath;
        set { SetField(ref _lastReportPath, value); DeferCommandRequery(); }
    }

    private bool _showDryRunBanner = true;
    public bool ShowDryRunBanner { get => _showDryRunBanner; set => SetField(ref _showDryRunBanner, value); }

    private bool _showMoveCompleteBanner;
    public bool ShowMoveCompleteBanner { get => _showMoveCompleteBanner; set => SetField(ref _showMoveCompleteBanner, value); }

    // ═══ STATUS INDICATORS ══════════════════════════════════════════════
    private string _statusRoots = "Roots: –";
    public string StatusRoots { get => _statusRoots; set => SetField(ref _statusRoots, value); }

    private string _statusTools = "Tools: –";
    public string StatusTools { get => _statusTools; set => SetField(ref _statusTools, value); }

    private string _statusDat = "DAT: –";
    public string StatusDat { get => _statusDat; set => SetField(ref _statusDat, value); }

    private string _statusReady = "Status: –";
    public string StatusReady { get => _statusReady; set => SetField(ref _statusReady, value); }

    private string _statusRuntime = "Laufzeit: –";
    public string StatusRuntime { get => _statusRuntime; set => SetField(ref _statusRuntime, value); }

    private StatusLevel _rootsStatusLevel = StatusLevel.Missing;
    public StatusLevel RootsStatusLevel { get => _rootsStatusLevel; set => SetField(ref _rootsStatusLevel, value); }

    private StatusLevel _toolsStatusLevel = StatusLevel.Missing;
    public StatusLevel ToolsStatusLevel { get => _toolsStatusLevel; set => SetField(ref _toolsStatusLevel, value); }

    private StatusLevel _datStatusLevel = StatusLevel.Missing;
    public StatusLevel DatStatusLevel { get => _datStatusLevel; set => SetField(ref _datStatusLevel, value); }

    private StatusLevel _readyStatusLevel = StatusLevel.Missing;
    public StatusLevel ReadyStatusLevel { get => _readyStatusLevel; set => SetField(ref _readyStatusLevel, value); }

    // ═══ TOOL STATUS LABELS (P1-007: VM-bound instead of x:Name TextBlocks) ═══
    private string _chdmanStatusText = "–";
    public string ChdmanStatusText { get => _chdmanStatusText; set => SetField(ref _chdmanStatusText, value); }

    private string _dolphinStatusText = "–";
    public string DolphinStatusText { get => _dolphinStatusText; set => SetField(ref _dolphinStatusText, value); }

    private string _sevenZipStatusText = "–";
    public string SevenZipStatusText { get => _sevenZipStatusText; set => SetField(ref _sevenZipStatusText, value); }

    private string _psxtractStatusText = "–";
    public string PsxtractStatusText { get => _psxtractStatusText; set => SetField(ref _psxtractStatusText, value); }

    private string _cisoStatusText = "–";
    public string CisoStatusText { get => _cisoStatusText; set => SetField(ref _cisoStatusText, value); }

    // ═══ DASHBOARD COUNTERS ═════════════════════════════════════════════
    private string _dashMode = "–";
    public string DashMode { get => _dashMode; set => SetField(ref _dashMode, value); }

    private string _dashWinners = "0";
    public string DashWinners { get => _dashWinners; set => SetField(ref _dashWinners, value); }

    private string _dashDupes = "0";
    public string DashDupes { get => _dashDupes; set => SetField(ref _dashDupes, value); }

    private string _dashJunk = "0";
    public string DashJunk { get => _dashJunk; set => SetField(ref _dashJunk, value); }

    private string _dashDuration = "00:00";
    public string DashDuration { get => _dashDuration; set => SetField(ref _dashDuration, value); }

    private string _healthScore = "–";
    public string HealthScore { get => _healthScore; set => SetField(ref _healthScore, value); }

    private string _dashGames = "0";
    public string DashGames { get => _dashGames; set => SetField(ref _dashGames, value); }

    private string _dashDatHits = "0";
    public string DashDatHits { get => _dashDatHits; set => SetField(ref _dashDatHits, value); }

    private string _dedupeRate = "–";
    public string DedupeRate { get => _dedupeRate; set => SetField(ref _dedupeRate, value); }

    // ═══ STEP INDICATOR ═════════════════════════════════════════════════
    private int _currentStep;
    public int CurrentStep { get => _currentStep; set => SetField(ref _currentStep, value); }

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
    public string StepLabel1 { get => _stepLabel1; set => SetField(ref _stepLabel1, value); }

    private string _stepLabel2 = "Bereit";
    public string StepLabel2 { get => _stepLabel2; set => SetField(ref _stepLabel2, value); }

    private string _stepLabel3 = "F5 drücken";
    public string StepLabel3 { get => _stepLabel3; set => SetField(ref _stepLabel3, value); }

    // ═══ RUN COMMAND HANDLERS ═══════════════════════════════════════════

    /// <summary>Confirm before destructive Move operations (uses injected IDialogService).</summary>
    public bool ConfirmMoveDialog()
    {
        return _dialog.Confirm(
            $"Modus 'Move' verschiebt Dateien in den Papierkorb.\n"
            + $"Roots: {string.Join(", ", Roots)}\n\nFortfahren?",
            "Move bestätigen");
    }

    private void OnRun()
    {
        CurrentRunState = RunState.Preflight;
        BusyHint = DryRun ? (IsSimpleMode ? "Vorschau läuft…" : "DryRun läuft…") : "Move läuft…";
        DashMode = DryRun ? (IsSimpleMode ? "Vorschau" : "DryRun") : "Move";
        Progress = 0;
        ProgressText = "0%";
        PerfPhase = "Phase: –";
        PerfFile = "Datei: –";
        ShowMoveCompleteBanner = false;

        RunRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnCancel()
    {
        var cts = Volatile.Read(ref _cts);
        try { cts?.Cancel(); } catch (ObjectDisposedException) { }
        CurrentRunState = RunState.Cancelled;
        BusyHint = "Abbruch angefordert…";
    }

    private async void OnRollback()
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
            AddLog($"Rollback: {restored.Count} Dateien wiederhergestellt.", "INFO");
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
        if (reportPath is not null)
            LastReportPath = reportPath;
        if (success && DryRun)
        {
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
    }

    /// <summary>Set up a new CancellationTokenSource for a run.</summary>
    public CancellationToken CreateRunCancellation()
    {
        var newCts = new CancellationTokenSource();
        var oldCts = Interlocked.Exchange(ref _cts, newCts);
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
        int toolCount = (hasChdman ? 1 : 0) + (has7z ? 1 : 0) + (hasDolphin ? 1 : 0);
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
        ReadyStatusLevel = !hasRoots ? StatusLevel.Blocked
            : ToolsStatusLevel == StatusLevel.Warning ? StatusLevel.Warning
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
        if (!DryRun && ConfirmMove && !ConfirmMoveDialog())
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
                return RunService.BuildOrchestrator(this, msg =>
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
                () => RunService.ExecuteRun(orchestrator, runOptions, auditPath, reportPath, ct), ct);

            ApplyRunResult(svcResult.Result);
            LastAuditPath = auditPath;

            if (!DryRun && auditPath is not null && File.Exists(auditPath))
                PushRollbackUndo(auditPath);

            if (svcResult.ReportPath is not null)
                AddLog($"Report: {svcResult.ReportPath}", "INFO");

            if (!ct.IsCancellationRequested)
            {
                AddLog("Lauf abgeschlossen.", "INFO");
                CompleteRun(true, reportPath);
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
            AddLog($"Fehler: {ex.Message}", "ERROR");
            CompleteRun(false);
        }
        finally
        {
            _watchService.FlushPendingIfNeeded();
        }
    }

    // ═══ WATCH-MODE ════════════════════════════════════════════════════

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

    /// <summary>Dispose watch-mode resources.</summary>
    public void CleanupWatchers()
    {
        _watchService.RunTriggered -= OnWatchRunTriggered;
        _watchService.Dispose();
    }

    // ═══ EVENTS ═════════════════════════════════════════════════════════
    public event EventHandler? RunRequested;

    // ═══ RESULT METHODS ═════════════════════════════════════════════════

    /// <summary>Build the error summary items for the protocol tab.</summary>
    public void PopulateErrorSummary()
    {
        ErrorSummaryItems.Clear();

        var issues = LogEntries
            .Skip(_runLogStartIndex)
            .Where(e => e.Level is "WARN" or "ERROR")
            .Select(e => $"[{e.Level}] {e.Text}")
            .ToList();

        if (LastRunResult is not null)
        {
            if (LastRunResult.Status == "blocked")
                issues.Insert(0, $"[BLOCKED] Preflight: {LastRunResult.Preflight?.Reason}");

            if (LastRunResult.MoveResult is { FailCount: > 0 } mv)
                issues.Insert(0, $"[ERROR] {mv.FailCount} Dateien konnten nicht verschoben werden");

            var junk = LastCandidates.Count(c => c.Category == "JUNK");
            if (junk > 0)
                issues.Insert(0, $"[WARN] {junk} Junk-Dateien erkannt");

            var unverified = LastCandidates.Count(c => !c.DatMatch);
            if (unverified > 0 && LastCandidates.Count > 0)
                issues.Insert(0, $"[INFO] {unverified}/{LastCandidates.Count} Dateien ohne DAT-Verifizierung");
        }

        if (issues.Count == 0)
        {
            ErrorSummaryItems.Add("✓ Keine Fehler oder Warnungen.");
            if (LastRunResult is not null)
                ErrorSummaryItems.Add($"Report geladen: {LastRunResult.WinnerCount} Winner, {LastRunResult.LoserCount} Dupes");
            return;
        }

        foreach (var issue in issues.Take(50))
            ErrorSummaryItems.Add(issue);
        if (issues.Count > 50)
            ErrorSummaryItems.Add($"… und {issues.Count - 50} weitere");
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
        HealthScore = total > 0 ? $"{100.0 * result.WinnerCount / total:F0}%" : "–";

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
    }
}
