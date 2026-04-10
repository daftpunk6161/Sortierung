# ADR-006: Zielarchitektur GUI / UI / UX

> **Status:** Proposed
> **Datum:** 2026-03-17
> **Kontext:** HardGuiInvariantTests (30 Tests), FINAL_CONSOLIDATED_AUDIT.md, ADR-005 (Core-Zielarchitektur)
> **Scope:** WPF Views, ViewModels, Commands, UI State, Dashboard, Progress, Cancel/Resume/Rollback/Re-Run, Trennung UI ↔ Business-Logik
> **Vorgänger:** ADR-004 (Clean Architecture), ADR-005 (Core-Zielarchitektur)

---

## 1. Executive Design

### Hauptidee

**Jede UI-Entscheidung wird durch ein Projection-Objekt getroffen, nie durch das ViewModel selbst.**

Das ViewModel ist ein **dünner Dispatcher**: es nimmt Commands entgegen, delegiert an Services, und bindet immutable Projections an die View. Es enthält keine Berechnungslogik, keine String-Formatierung, keine Geschäftsregeln.

### Ist-Zustand (Befunde)

| # | Problem | Ort | Impact |
|---|---------|-----|--------|
| G-01 | **MainViewModel und RunViewModel duplizieren ~400 Zeilen** identischer Logik (RunState Machine, Rollback-Stack, Progress, Status-Indikatoren, Tool-Status, PopulateErrorSummary) | MainViewModel.RunPipeline.cs (~500 Zeilen), RunViewModel.cs (~500 Zeilen) | Divergenz-Risiko bei jedem Fix, nachgewiesen durch GUI-INV-03 (HasRunResult-Divergenz, inzwischen gefixt) |
| G-02 | **ApplyRunResult ist eine 100-Zeilen-Methode** die RunResult → 15+ Dashboard-Properties mappt, ConsoleDistribution berechnet, DedupeGroupItems aufbaut, MoveConsequenceText setzt — alles inline im ViewModel | MainViewModel.RunPipeline.cs Zeilen 839–960 | Nicht testbar ohne vollständiges ViewModel; Logik und Formatierung vermischt |
| G-03 | **RunResultSummary existiert bereits** als immutable Record (Models/RunResultSummary.cs) mit HealthScore und DedupeRate, wird aber **nirgends verwendet** — ApplyRunResult berechnet alles inline neu | RunResultSummary.cs, MainViewModel.RunPipeline.cs | Toter Code; HealthScore-Formel existiert dreifach (RunResultSummary, FeatureService, ApplyRunResult) |
| G-04 | **IRunService.BuildOrchestrator nimmt MainViewModel entgegen** — Infrastructure-naher Service hat direkte Abhängigkeit auf WPF-ViewModel-Typ | IRunService.cs, RunService.cs | Zirkuläre Kopplung; RunService kann nicht ohne WPF-Assembly getestet werden |
| G-05 | **Settings existieren dreifach**: MainViewModel.Settings.cs, SetupViewModel, SettingsDto — gleiche Properties, keine Single Source of Truth | MainViewModel.Settings.cs (~200 Zeilen), SetupViewModel.cs (~300 Zeilen) | Divergenz bei jeder neuen Setting-Property |
| G-06 | **Status-Refresh-Logik (RefreshStatus)** existiert in MainViewModel (~60 Zeilen) UND RunViewModel (~70 Zeilen) mit leicht unterschiedlichem Verhalten (Validation-Integration fehlt in RunVM) | MainViewModel.RunPipeline.cs, RunViewModel.cs | Inkonsistente Statusanzeige je nach Binding-Ziel |
| G-07 | **Inline Localization** in MainViewModel (`"Keine Ordner"`, `"Startbereit ✓"`) neben LocalizationService-Calls in RunViewModel (`_loc["Run.Ready"]`) | MainViewModel.RunPipeline.cs vs RunViewModel.cs | Inkonsistent; UI-Sprache wechselt nicht konsistent |
| G-08 | **Code-Behind in MainWindow.xaml.cs** enthält Orchestrierungs-Logik (OnRunRequested → ExecuteAndRefreshAsync → resultView.RefreshReportPreview + NavigateTo) | MainWindow.xaml.cs Zeilen 148–180 | View-Logik statt ViewModel-Logik; nicht testbar |

### Ziel-Zustand (Architektur-Diagramm)

```
┌─────────────────────────────────────────────────────────────────────┐
│ Views (XAML + Code-Behind)                                         │
│  MainWindow.xaml   StartView   SettingsView   ResultView           │
│  ProgressView      SortView    ToolsView      WizardView           │
│                                                                     │
│  ERLAUBT: DataBinding, DataTrigger, VisualStateManager,            │
│           Drag&Drop-Handling, Focus/Keyboard, Animation            │
│  VERBOTEN: Business-Logik, String-Formatierung, Berechnungen,      │
│            direkte RunResult-Zugriffe, Service-Aufrufe              │
└──────────────────────────────┬──────────────────────────────────────┘
                               │ {Binding} / Commands / DataTrigger
┌──────────────────────────────▼──────────────────────────────────────┐
│ ViewModels (ObservableObject, RelayCommand)                         │
│                                                                     │
│  MainViewModel (Shell)                                              │
│  ├── Navigation, ChildVM-Wiring, FeatureCommand-Registry           │
│  ├── Roots (ObservableCollection<string>)                          │
│  ├── LogEntries, Notifications                                      │
│  └── Commands: Run, Cancel, Rollback, AddRoot, RemoveRoot, ...     │
│                                                                     │
│  SetupViewModel ← Single Source of Truth für alle Settings         │
│  ToolsViewModel ← Tool-Katalog, Suche, Kategorien                 │
│  RunViewModel ← RunState, Progress, Dashboard, ErrorSummary        │
│                                                                     │
│  ERLAUBT: Property-Setter, Command-Dispatch, Projection-Binding,   │
│           Event-Delegation an Services                              │
│  VERBOTEN: HealthScore-Berechnung, DedupeRate-Formatierung,        │
│            ConsoleDistribution-Aggregation, I/O, File.Exists        │
└──────────────────────────────┬──────────────────────────────────────┘
                               │ Projection Objects (immutable records)
┌──────────────────────────────▼──────────────────────────────────────┐
│ Projections (UI.Wpf/Models/)                                        │
│                                                                     │
│  DashboardProjection     ← RunResult → KPI-Werte, formattiert     │
│  ProgressProjection      ← Phase + Progress + Text                 │
│  ErrorSummaryProjection  ← RunResult + LogEntries → UiError[]      │
│  StatusProjection        ← Config-State → StatusLevel + Text       │
│  BannerProjection        ← RunState + DryRun → Banner-Sichtbarkeit│
│  MoveGateProjection      ← Fingerprint + RunState → Gate-Text     │
│                                                                     │
│  Reine Funktionen, kein State, kein ObservableObject.              │
│  Input: RunResult | RunState | Config → Output: formatiertes DTO   │
└──────────────────────────────┬──────────────────────────────────────┘
                               │ RunResult, RunOptions, RomCandidate
┌──────────────────────────────▼──────────────────────────────────────┐
│ Services (UI.Wpf/Services/)                                         │
│                                                                     │
│  IRunService        ← Orchestrator-Building + Execution            │
│  ISettingsService   ← Load/Save, keine VM-Abhängigkeit            │
│  IThemeService      ← Theme-State                                  │
│  IDialogService     ← Modale Dialoge                               │
│  RollbackService    ← Audit-basierter Undo                        │
│  WatchService       ← Dateisystem-Überwachung                     │
│                                                                     │
│  VERBOTEN: Direkte ViewModel-Referenzen als Parameter              │
└──────────────────────────────┬──────────────────────────────────────┘
                               │
┌──────────────────────────────▼──────────────────────────────────────┐
│ Infrastructure (Romulus.Infrastructure)                          │
│  RunOrchestrator, FileSystemAdapter, AuditCsvStore, ...            │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 2. Zielobjekte und UI-Modelle

### 2.1 RunScreenState (ersetzt duale RunState-Maschine)

```csharp
// UI.Wpf/Models/RunScreenState.cs
// ZIEL: Eine einzige RunState-Maschine für die gesamte UI.
// RunViewModel besitzt sie, MainViewModel delegiert per Run.CurrentRunState.

// RunState enum bleibt unverändert:
// Idle, Preflight, Scanning, Deduplicating, Sorting, Moving, Converting,
// Completed, CompletedDryRun, Failed, Cancelled

// IsValidTransition bleibt unverändert, lebt NUR in RunViewModel.
// MainViewModel.IsValidTransition wird entfernt.
// MainViewModel.CurrentRunState wird zu: Run.CurrentRunState (Delegation).
```

**Regel:** RunState existiert exakt einmal — im `RunViewModel`. Alle anderen VMs und Views binden an `Run.CurrentRunState`.

### 2.2 DashboardProjection (ersetzt ApplyRunResult-Inline-Logik)

```csharp
// UI.Wpf/Models/DashboardProjection.cs
public sealed record DashboardProjection
{
    public required string Winners { get; init; }
    public required string Dupes { get; init; }
    public required string Junk { get; init; }
    public required string Games { get; init; }
    public required string DatHits { get; init; }
    public required string Duration { get; init; }
    public required string HealthScore { get; init; }
    public required string DedupeRate { get; init; }
    public required string Mode { get; init; }
    public required string MoveConsequenceText { get; init; }
    public required IReadOnlyList<ConsoleDistributionItem> ConsoleDistribution { get; init; }
    public required IReadOnlyList<DedupeGroupItem> DedupeGroups { get; init; }

    // Factory: reine Funktion, kein State
    public static DashboardProjection FromRunResult(
        RunResult result,
        bool isConvertOnly,
        bool isDryRun,
        string mode)
    {
        // Gesamte Logik aus ApplyRunResult hierher verschoben.
        // HealthScore via FeatureService.CalculateHealthScore (Single Source).
        // DedupeRate-Berechnung mit dedupeDenominator <= 0 Guard.
        // ConsoleDistribution-Aggregation.
        // DedupeGroupItems-Mapping.
        // MoveConsequenceText-Logik.
        // Alles pure, testbar, deterministisch.
    }

    // Leerer Zustand für Reset
    public static DashboardProjection Empty { get; } = new()
    {
        Winners = "–", Dupes = "–", Junk = "–", Games = "–",
        DatHits = "–", Duration = "–", HealthScore = "–",
        DedupeRate = "–", Mode = "–", MoveConsequenceText = "",
        ConsoleDistribution = [], DedupeGroups = []
    };
}
```

**Regel:** `RunResultSummary.cs` wird entfernt. `DashboardProjection` ist die einzige Projection. `FeatureService.CalculateHealthScore` ist die einzige HealthScore-Implementierung.

### 2.3 ProgressProjection

```csharp
// UI.Wpf/Models/ProgressProjection.cs
public sealed record ProgressProjection(
    double Percent,          // 0-100, geclampt
    string Text,             // "42%" oder "Scanning file.zip"
    string Phase,            // "Phase: [Scan]"
    string File,             // "Datei: file.zip"
    string BusyHint);        // "DryRun läuft…"
```

**Regel:** `Progress`, `ProgressText`, `PerfPhase`, `PerfFile`, `BusyHint` werden zu einer einzigen `CurrentProgress`-Property vom Typ `ProgressProjection`.

### 2.4 StatusProjection (ersetzt RefreshStatus-Duplikation)

```csharp
// UI.Wpf/Models/StatusProjection.cs
public sealed record StatusProjection
{
    public required StatusDot Roots { get; init; }
    public required StatusDot Tools { get; init; }
    public required StatusDot Dat { get; init; }
    public required StatusDot Ready { get; init; }
    public required IReadOnlyList<ToolStatus> ToolStatuses { get; init; }
    public required StepIndicator Step { get; init; }

    // Factory: reine Funktion
    public static StatusProjection Compute(
        int rootCount,
        ToolPathConfig tools,
        DatConfig dat,
        bool hasBlockingValidation,
        bool hasWarnings,
        RunState runState,
        ILocalizationService loc)
    {
        // Gesamte RefreshStatus-Logik hierher.
        // Wird EINMAL aufgerufen, gibt immutable Record zurück.
    }
}

public sealed record StatusDot(StatusLevel Level, string Text);
public sealed record ToolStatus(string ToolName, string StatusText);
public sealed record StepIndicator(int CurrentStep, string Label1, string Label2, string Label3);
```

**Regel:** `RefreshStatus()` wird entfernt aus MainViewModel UND RunViewModel. Stattdessen: `StatusProjection.Compute(...)` → Ergebnis an `Run.CurrentStatus` binden.

### 2.5 ErrorSummaryProjection (ersetzt PopulateErrorSummary-Duplikation)

```csharp
// UI.Wpf/Models/ErrorSummaryProjection.cs
public static class ErrorSummaryProjection
{
    public static IReadOnlyList<UiError> Build(
        RunResult? result,
        IReadOnlyList<RomCandidate> candidates,
        IEnumerable<LogEntry> runLogs)
    {
        // Gesamte PopulateErrorSummary-Logik hierher.
        // Reine Funktion: Input → Output, kein State.
        // Einzige Implementierung — nicht in MainVM UND RunVM.
    }
}
```

### 2.6 BannerProjection

```csharp
// UI.Wpf/Models/BannerProjection.cs
public sealed record BannerProjection(
    bool ShowDryRunBanner,
    bool ShowMoveCompleteBanner,
    bool ShowCancelledBanner,
    bool ShowFailedBanner,
    bool ShowBlockedBanner,
    string? BannerMessage);

public static class BannerProjection
{
    public static BannerProjection Compute(
        RunState state,
        bool isDryRun,
        bool isConvertOnly,
        RunResult? lastResult)
    {
        return new(
            ShowDryRunBanner: state == RunState.CompletedDryRun,
            ShowMoveCompleteBanner: state == RunState.Completed && !isDryRun && !isConvertOnly,
            ShowCancelledBanner: state == RunState.Cancelled,
            ShowFailedBanner: state == RunState.Failed,
            ShowBlockedBanner: lastResult?.Status == "blocked",
            BannerMessage: state switch
            {
                RunState.Cancelled => "Lauf abgebrochen.",
                RunState.Failed => lastResult?.Preflight?.Reason ?? "Lauf fehlgeschlagen.",
                _ => null
            });
    }
}
```

### 2.7 CommandState / CanExecute-Regeln

```
┌──────────────────────────┬──────────────────────────────────────────────────┐
│ Command                  │ CanExecute-Bedingung                            │
├──────────────────────────┼──────────────────────────────────────────────────┤
│ RunCommand               │ !IsBusy && Roots.Count > 0                      │
│                          │ && !HasBlockingValidationErrors                  │
│                          │ && (DryRun || CanStartMoveWithCurrentPreview)    │
├──────────────────────────┼──────────────────────────────────────────────────┤
│ CancelCommand            │ IsBusy                                           │
├──────────────────────────┼──────────────────────────────────────────────────┤
│ RollbackCommand          │ !IsBusy && CanRollback                           │
├──────────────────────────┼──────────────────────────────────────────────────┤
│ StartMoveCommand         │ CanStartMoveWithCurrentPreview                   │
│                          │ && !HasBlockingValidationErrors                  │
├──────────────────────────┼──────────────────────────────────────────────────┤
│ QuickPreviewCommand      │ Roots.Count > 0 && !IsBusy                      │
│                          │ && !HasBlockingValidationErrors                  │
├──────────────────────────┼──────────────────────────────────────────────────┤
│ ConvertOnlyCommand       │ Roots.Count > 0 && !IsBusy                      │
│                          │ && !HasBlockingValidationErrors                  │
├──────────────────────────┼──────────────────────────────────────────────────┤
│ AddRootCommand           │ !IsBusy                                           │
├──────────────────────────┼──────────────────────────────────────────────────┤
│ RemoveRootCommand        │ !IsBusy && SelectedRoot is not null              │
├──────────────────────────┼──────────────────────────────────────────────────┤
│ OpenReportCommand        │ !string.IsNullOrEmpty(LastReportPath)            │
├──────────────────────────┼──────────────────────────────────────────────────┤
│ WatchApplyCommand        │ !IsBusy                                           │
└──────────────────────────┴──────────────────────────────────────────────────┘

CanStartMoveWithCurrentPreview :=
    !IsBusy
    && Roots.Count > 0
    && Run.CurrentRunState == RunState.CompletedDryRun
    && _lastSuccessfulPreviewFingerprint == BuildPreviewConfigurationFingerprint()
```

**Regel:** `IsBusy` und `IsIdle` werden NUR aus `Run.CurrentRunState` abgeleitet. Keine separaten Bool-Flags.

### 2.8 RunOutcome Mapping auf UI

```
┌─────────────────────┬────────────────────┬───────────────┬───────────────────────┐
│ RunState            │ Banner             │ Dashboard     │ Commands Enabled      │
├─────────────────────┼────────────────────┼───────────────┼───────────────────────┤
│ Idle                │ –                  │ Empty/Last    │ Run, AddRoot          │
│ Preflight..Convert  │ BusyHint           │ Live-Progress │ Cancel only           │
│ CompletedDryRun     │ DryRunBanner       │ Full KPIs     │ Run, StartMove, Roll  │
│ Completed           │ MoveCompleteBanner │ Full KPIs     │ Run, Rollback, Report │
│ Failed              │ FailedBanner       │ Partial/Last  │ Run (retry)           │
│ Cancelled           │ CancelledBanner    │ Partial/Last  │ Run (retry)           │
└─────────────────────┴────────────────────┴───────────────┴───────────────────────┘
```

### 2.9 Modusabhängige KPI-Sichtbarkeit

```
┌───────────────┬────────┬────────┬───────┬──────────┬──────────┬───────────┐
│ Modus         │ Winner │ Dupes  │ Junk  │ Health   │ DatHits  │ DedupeRate│
├───────────────┼────────┼────────┼───────┼──────────┼──────────┼───────────┤
│ DryRun        │ ✓      │ ✓      │ ✓     │ ✓        │ ✓        │ ✓         │
│ Move          │ ✓      │ ✓      │ ✓     │ ✓        │ ✓        │ ✓         │
│ ConvertOnly   │ –      │ –      │ –     │ –        │ –        │ –         │
│ Blocked       │ –      │ –      │ –     │ –        │ –        │ –         │
│ Failed        │ partial│ partial│ part. │ partial  │ partial  │ partial   │
│ Cancelled     │ –/part.│ –/part.│ –/p.  │ –/part.  │ –/part.  │ –/part.   │
└───────────────┴────────┴────────┴───────┴──────────┴──────────┴───────────┘
```

`DashboardProjection.FromRunResult()` entscheidet anhand dieser Matrix.

---

## 3. Datenfluss in der GUI

### 3.1 Run-Pipeline Datenfluss (Ziel)

```
User klickt "Run" oder drückt F5
    │
    ▼
MainViewModel.OnRun()
    ├── Validation-Gate (HasBlockingValidationErrors?)
    ├── Move-Gate (DryRun || CanStartMoveWithCurrentPreview?)
    ├── Run.TransitionTo(RunState.Preflight)
    ├── Run.CurrentProgress = ProgressProjection.Starting(mode)
    ├── Run.CurrentDashboard = DashboardProjection.Empty
    └── raise RunRequested event
         │
         ▼
MainWindow.OnRunRequested (Code-Behind, MINIMAL)
    │  Einzige Aufgabe: await _vm.ExecuteRunAsync()
    │                    + Post-Run Navigation
    ▼
MainViewModel.ExecuteRunAsync (async Task)
    ├── CancellationToken erstellen
    ├── IRunService.BuildOrchestrator(RunOptionsDto, onProgress)  ← NICHT ViewModel!
    │       └── RunOptionsDto: reines DTO gebaut aus Setup-Properties
    ├── Progress-Callback → Run.CurrentProgress = new ProgressProjection(...)
    ├── IRunService.ExecuteRun(orchestrator, options, ct)
    ├── RunResult empfangen
    │
    ├── Run.CurrentDashboard = DashboardProjection.FromRunResult(result, ...)
    ├── Run.ErrorSummary = ErrorSummaryProjection.Build(result, candidates, logs)
    ├── Run.CurrentBanner = BannerProjection.Compute(state, dryRun, ...)
    └── CompleteRun(success, reportPath, cancelled)
```

### 3.2 Live-State vs. Final-State Trennung

| Aspekt | Live-State (während Run) | Final-State (nach Run) |
|--------|--------------------------|------------------------|
| **Quelle** | Progress-Callback vom RunOrchestrator | RunResult nach Orchestrator-Return |
| **Objekt** | `ProgressProjection` (wird 10×/s überschrieben) | `DashboardProjection` (einmal gesetzt, immutable) |
| **Binding** | `Run.CurrentProgress.Percent`, `.Text` | `Run.CurrentDashboard.Winners`, `.HealthScore` |
| **Thread** | Callback auf ThreadPool → `_syncContext.Post` | UI-Thread nach `await` |
| **Lebensdauer** | Ersetzt bei jedem Callback | Bleibt bis zum nächsten Run oder Reset |

### 3.3 Cancel-Flow

```
User klickt "Cancel"
    │
    ▼
MainViewModel.OnCancel()
    ├── _cts.Cancel()    (thread-safe via _ctsLock)
    ├── Run.TransitionTo(RunState.Cancelled)
    └── Run.CurrentProgress = ProgressProjection.Cancelled()
         │
         ▼
ExecuteRunAsync catch(OperationCanceledException)
    ├── Run.CurrentBanner = BannerProjection.Compute(Cancelled, ...)
    ├── CompleteRun(success: false, cancelled: true)
    └── Dashboard behält letzten Final-State (NICHT leeren)
```

### 3.4 Rollback-Flow

```
User klickt "Rollback"
    │
    ▼
MainViewModel.OnRollbackAsync()
    ├── _dialog.Confirm("Rückgängig machen?")
    ├── RollbackService.Execute(auditPath, roots)  ← Background Task
    ├── RestoreResult empfangen
    │       (RolledBack, SkippedMissingDest, SkippedCollision, Failed)
    ├── Run.CurrentDashboard = DashboardProjection.Empty
    ├── Run.CurrentBanner = BannerProjection.RollbackComplete(restoreResult)
    └── AddLog(...)
```

---

## 4. Trennung View / ViewModel / Projection / Core

### 4.1 Was NIEMALS in Views liegen darf

| Verboten in Views | Begründung |
|-------------------|------------|
| `if (result.LoserCount > 0)` | Geschäftslogik → Projection |
| `$"{score}%"` String-Formatierung | Formatierung → Projection |
| `File.Exists(path)` | I/O → Service |
| `RunOrchestrator` Instantiierung | Orchestrierung → ViewModel + Service |
| `NavigateTo("Analyse")` wenn Run fertig | Kann als AutoNavigate-Flag im ViewModel modelliert werden |
| `resultView.RefreshReportPreview()` | View-to-View-Kommunikation → Event/Messenger |

**Was in Views ERLAUBT bleibt:**
- DataBinding (`{Binding Run.CurrentDashboard.Winners}`)
- DataTrigger für Visibility basierend auf Projection-Properties
- Drag&Drop-Handling (rohe UI-Events)
- Focus-Management, Keyboard-Shortcuts
- Animation-Trigger

### 4.2 Was NIEMALS in ViewModels liegen darf

| Verboten in ViewModels | Begründung |
|------------------------|------------|
| `HealthScore = total > 0 ? $"{...}%" : "–"` | Berechnung + Formatierung → `DashboardProjection.FromRunResult()` |
| `ConsoleDistribution.Clear(); ... GroupBy(...) ...` | Aggregation → `DashboardProjection.FromRunResult()` |
| `DedupeGroupItems.Clear(); ... Select(...) ...` | Mapping → `DashboardProjection.FromRunResult()` |
| `MoveConsequenceText = isConvertOnly ? ...` | Bedingte Formatierung → `DashboardProjection` |
| `File.Exists(ToolChdman)` | I/O → `StatusProjection.Compute()` oder SetupVM-Validation |
| `Directory.Exists(DatRoot)` | I/O → `StatusProjection.Compute()` oder SetupVM-Validation |
| `RefreshStatus()` mit 60 Zeilen Inline-Logik | → `StatusProjection.Compute()` |
| Doppelte State-Machines | → Eine State-Machine in RunViewModel |

**Was in ViewModels ERLAUBT bleibt:**
- Property-Setter (`SetProperty`)
- Command-Handler die an Services delegieren
- Projection-Objekte zuweisen (`Run.CurrentDashboard = DashboardProjection.FromRunResult(...)`)
- Event-Raising (RunRequested, PropertyChanged)
- Collection-Management (Roots, LogEntries)

### 4.3 Was NUR aus Projection-Objekten kommen darf

| Information | Quelle | NICHT aus |
|-------------|--------|-----------|
| Dashboard-KPIs (Winners, Dupes, etc.) | `DashboardProjection` | inline `result.WinnerCount.ToString()` |
| HealthScore | `DashboardProjection` → `FeatureService.CalculateHealthScore` | inline Formel |
| DedupeRate | `DashboardProjection` | inline Berechnung |
| ConsoleDistribution | `DashboardProjection.ConsoleDistribution` | inline GroupBy |
| ErrorSummary | `ErrorSummaryProjection.Build()` | inline PopulateErrorSummary() |
| StatusDots | `StatusProjection.Compute()` | inline RefreshStatus() |
| Banner-Sichtbarkeit | `BannerProjection.Compute()` | inline Bool-Flags |
| MoveConsequenceText | `DashboardProjection.MoveConsequenceText` | inline Conditional |

---

## 5. Zu entfernende Altlogik

### 5.1 Zu löschender Code

| Datei | Zu entfernen | Ersetzt durch |
|-------|-------------|---------------|
| `MainViewModel.RunPipeline.cs` | `CurrentRunState` Property + `IsValidTransition()` | Delegation an `Run.CurrentRunState` |
| `MainViewModel.RunPipeline.cs` | `IsBusy`, `IsIdle`, `HasRunResult` | Delegation an `Run.IsBusy` etc. |
| `MainViewModel.RunPipeline.cs` | `ApplyRunResult()` (100 Zeilen) | `DashboardProjection.FromRunResult()` |
| `MainViewModel.RunPipeline.cs` | `PopulateErrorSummary()` (40 Zeilen) | `ErrorSummaryProjection.Build()` |
| `MainViewModel.RunPipeline.cs` | `RefreshStatus()` (60 Zeilen) | `StatusProjection.Compute()` |
| `MainViewModel.RunPipeline.cs` | 15 Dashboard-Properties (`DashWinners`, `DashDupes`, ...) | `Run.CurrentDashboard` (one Projection) |
| `MainViewModel.RunPipeline.cs` | Rollback-Stacks (`_rollbackUndoStack`, `_rollbackRedoStack`) | `Run.PushRollbackUndo()` etc. |
| `MainViewModel.RunPipeline.cs` | Progress-Properties (`Progress`, `ProgressText`, `PerfPhase`, `PerfFile`, `BusyHint`) | `Run.CurrentProgress` |
| `MainViewModel.RunPipeline.cs` | Status-Properties (`StatusRoots`, `StatusTools`, ..., `ChdmanStatusText`, ...) | `Run.CurrentStatus` |
| `MainViewModel.Settings.cs` | Settings-Properties (Duplikat von SetupViewModel) | `Setup.*` Delegation |
| `RunViewModel.cs` | `PopulateErrorSummary()` (Duplikat) | `ErrorSummaryProjection.Build()` |
| `RunViewModel.cs` | `RefreshStatus()` (Duplikat) | `StatusProjection.Compute()` |
| `Models/RunResultSummary.cs` | Gesamte Datei (unused) | `DashboardProjection` |

### 5.2 Zu refaktorierender Code

| Element | Ist-Zustand | Ziel-Zustand |
|---------|-------------|-------------|
| `IRunService.BuildOrchestrator(MainViewModel vm)` | Nimmt ViewModel als Parameter | Nimmt `RunOptionsDto` (reines DTO) |
| `MainWindow.ExecuteAndRefreshAsync()` | Enthält Navigation + ReportPreview | ViewModel setzt `AutoNavigateTarget`; View reagiert per DataTrigger |
| `CompleteRun()` existiert in MainVM + RunVM | Zwei Signaturen, leicht divergent | Nur in RunViewModel, MainVM delegiert |
| Inline-Strings in MainViewModel | `"Startbereit ✓"`, `"Keine Ordner"` | Alle über `ILocalizationService` |

---

## 6. Migrationshinweise

### 6.1 Reihenfolge (Bottom-Up, eine Sache pro Schritt)

| Phase | Aufgabe | Risiko | Testbar? |
|-------|---------|--------|----------|
| **M-01** | `DashboardProjection.FromRunResult()` als pure Funktion extrahieren, Unit-Tests schreiben | Niedrig | ✓ Pure Funktion, 100% testbar |
| **M-02** | `ErrorSummaryProjection.Build()` extrahieren, Tests | Niedrig | ✓ Pure Funktion |
| **M-03** | `StatusProjection.Compute()` extrahieren, Tests | Niedrig | ✓ Pure Funktion |
| **M-04** | `BannerProjection.Compute()` extrahieren, Tests | Niedrig | ✓ Pure Funktion |
| **M-05** | `RunViewModel` als Single Owner von RunState, Progress, Dashboard, Status, ErrorSummary | Mittel | ✓ Existing Tests anpassen |
| **M-06** | `MainViewModel.RunPipeline.cs` entkernen: Dashboard/Status/Error-Properties → Run.* Delegation | Hoch | ✓ XAML-Bindings müssen angepasst werden |
| **M-07** | `IRunService.BuildOrchestrator(RunOptionsDto)` statt `MainViewModel` | Mittel | ✓ RunOptionsDto ist testbar |
| **M-08** | Settings-Duplikation auflösen: MainVM.Settings.cs → Setup.* Delegation | Mittel | ✓ |
| **M-09** | Inline-Strings → `ILocalizationService` in MainViewModel | Niedrig | ✓ |
| **M-10** | `RunResultSummary.cs` löschen | Niedrig | ✓ Compile-Fehler zeigt uses |

### 6.2 Invarianten für jeden Migrationsschritt

Vor jedem Merge muss gelten:
1. **3200+ Tests grün** (kein Testcount-Rückgang)
2. **HardGuiInvariantTests** bleiben grün (30 Regression Guards)
3. **Keine neue Code-Duplikation** (jede Projection-Logik existiert genau einmal)
4. **HealthScore-Formel** existiert genau einmal (`FeatureService.CalculateHealthScore`)
5. **RunState-Machine** existiert genau einmal (`RunViewModel.IsValidTransition`)
6. **XAML-Bindings** sind syntaktisch gültig (Build ohne Warnings)

### 6.3 XAML-Binding-Migration

Bestehende Bindings wie `{Binding DashWinners}` auf `MainViewModel` müssen migriert werden zu:

```xml
<!-- IST (direkt auf MainViewModel) -->
<TextBlock Text="{Binding DashWinners}" />

<!-- ZIEL (via RunViewModel.CurrentDashboard Projection) -->
<TextBlock Text="{Binding Run.CurrentDashboard.Winners}" />
```

Da XAML-Binding-Pfade sich ändern, ist Phase M-06 die risikoreichste. Empfehlung:
- Temporär beide Properties parallel vorhalten (alte als Wrapper)
- `[Obsolete]`-Attribut auf alte Properties
- Bindings schrittweise umstellen
- Alte Properties erst entfernen wenn alle XAML-Referenzen migriert sind

### 6.4 Entscheidung: Wann ProgressProjection einführen?

`ProgressProjection` kann als letztes migriert werden, weil:
- Progress wird 10×/s aktualisiert; neues Record-Objekt pro Update erzeugt GC-Druck
- Alternative: `ProgressProjection` als mutable Klasse mit `OnPropertyChanged`
- **Empfehlung:** Progress-Properties bleiben vorerst als einzelne Properties in RunViewModel; Projection nur für Dashboard/Status/Error/Banner

---

## Anhang: Entscheidungstreiber

1. **GUI-INV-03 (HasRunResult-Divergenz)** → Beweist, dass duale State-Machines divergieren
2. **G-02 (ApplyRunResult 100-Zeilen-Methode)** → Nicht testbar ohne vollständiges ViewModel
3. **G-03 (RunResultSummary unused)** → Zeigt, dass die Projection-Idee zwar vorhanden war, aber nicht durchgehalten wurde
4. **G-04 (IRunService nimmt ViewModel)** → Verletzt Dependency-Inversion
5. **3200+ bestehende Tests** → Jede Migration muss schrittweise erfolgen, kein Big-Bang
