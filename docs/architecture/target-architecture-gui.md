# Zielarchitektur GUI / UI / UX

## 1. Executive Design

### Leitprinzip

Die GUI ist ein **dünner, reaktiver Konsument** von immutable Projection-Objekten.
Sie erzeugt keine fachlichen Wahrheiten, berechnet keine KPIs und trifft keine Dedupe-/Sort-/Classification-Entscheidungen.
Jeder sichtbare Wert auf dem Dashboard stammt **ausschliesslich** aus einem typisierten Projection-Record.

### Architektur-Dreieck

```
┌──────────────────┐
│     XAML View     │  darf: Binding, Layout, Animation, Converter (rein visuell)
│                   │  darf NICHT: Business-Logik, Scoring, KPI-Herleitung
└───────┬──────────┘
        │ DataBinding (OneWay / TwoWay)
        ▼
┌──────────────────┐
│    ViewModel      │  darf: Zustandsverwaltung, Command-Routing, Projection-Anwendung
│                   │  darf NICHT: fachliche Berechnung, I/O, Tool-Aufrufe
└───────┬──────────┘
        │ konsumiert
        ▼
┌──────────────────┐
│   Projection      │  immutable Records, erzeugt von Infrastructure/Core
│   Records         │  RunProjection, DashboardProjection, StatusProjection, etc.
└───────┬──────────┘
        │ erzeugt von
        ▼
┌──────────────────┐
│  Infrastructure   │  RunProjectionFactory, RunOrchestrator, etc.
│  / Core           │  einzige Quelle fachlicher Wahrheit
└──────────────────┘
```

### Kern-Garantien

| Garantie | Regel |
|----------|-------|
| **Projection-Only Dashboard** | Kein Dash-Wert darf ausserhalb von `DashboardProjection.From()` berechnet werden |
| **State-Machine-gesicherter Lifecycle** | Jede RunState-Transition muss `RunStateMachine.IsValidTransition()` passieren |
| **Konsistente Defaults** | Alle Dash-Properties starten mit `"–"` (kein Run ≠ "0") |
| **Atomare Zustandsübergänge** | `CompleteRun()` setzt ALLE terminal-relevanten Properties in einem Aufruf |
| **Cancel-Sicherheit** | Cancel setzt Banner, BusyHint, Summary und State in einer atomaren Methode |
| **Keine Double-Marking** | Provisional und Plan sind mutually exclusive Marker |
| **Command-Enablement aus State** | CanExecute leitet sich nur aus `RunState`, `IsBusy`, `CanRollback`, `Roots.Count` etc. ab — nie aus GUI-Widgets |

---

## 2. Zielobjekte und UI-Modelle

### 2.1 RunScreenState (vorhanden als `RunState` Enum — Ziel: erweitern)

```
┌──────────────────┐
│   RunScreenState  │
│                   │
│  ─ Idle           │  Kein Run aktiv, Dashboard zeigt "–" oder letztes Ergebnis
│  ─ Preflight      │  Validierung läuft, UI gesperrt
│  ─ Scanning       │  Dateien werden enumeriert
│  ─ Deduplicating  │  Gruppen + Winner-Selection
│  ─ Sorting        │  Konsolen-Zuordnung
│  ─ Moving         │  Dateien werden verschoben (nur Execute)
│  ─ Converting     │  Format-Konvertierung
│  ─ Completed      │  Erfolgreicher Move-Run
│  ─ CompletedDryRun│  Erfolgreicher Preview-Run
│  ─ Failed         │  Fehler aufgetreten
│  ─ Cancelled      │  Vom User abgebrochen
│  ─ RollbackActive │→ NEU: Rollback läuft (aktuell fehlt expliziter State)
└──────────────────┘
```

**State-Transitions (Ziel-Matrix):**

| Von | Nach | Auslöser |
|-----|------|----------|
| `Idle` | `Preflight` | RunCommand |
| `Preflight` | `Scanning` | Orchestrator-Progress |
| `Scanning` | `Deduplicating` | Phase-Übergang |
| `Deduplicating` | `Sorting` / `Moving` / `Converting` | Phase-Übergang (je nach RunOptions) |
| `Sorting` | `Moving` / `Converting` | Phase-Übergang |
| `Moving` | `Converting` / `Completed` | Phase-Übergang / Abschluss |
| `Converting` | `Completed` / `CompletedDryRun` | Abschluss |
| _Jeder aktive State_ | `Failed` / `Cancelled` | Exception / Cancel-Token |
| `Completed` / `CompletedDryRun` / `Failed` / `Cancelled` | `Idle` / `Preflight` | Neuer Run / Re-Run |
| `Idle` / `Completed` / `CompletedDryRun` | `RollbackActive` | RollbackCommand |
| `RollbackActive` | `Idle` | Rollback abgeschlossen |

**Abgeleitete UI-Zustände (computed, nie gespeichert):**

| Property | Ableitung | Zweck |
|----------|-----------|-------|
| `IsBusy` | `State in {Preflight, Scanning, Deduplicating, Sorting, Moving, Converting, RollbackActive}` | Command-Enablement, Progress-Overlay |
| `IsIdle` | `State == Idle` | Default-UI |
| `IsTerminal` | `State in {Completed, CompletedDryRun, Failed, Cancelled}` | Dashboard-Anzeige aktiv |
| `HasRunResult` | `IsTerminal AND AllCandidates.Count > 0` | Ergebnis-Views sichtbar |
| `ShowStartMoveButton` | `State == CompletedDryRun AND !HasBlockingValidationErrors` | Smart Action Bar |
| `IsPartialResult` | `State in {Failed, Cancelled} AND HasRunResult` | Provisional-Marker im Dashboard |

### 2.2 DashboardProjection (vorhanden — Ziel: bereinigt + nach Infrastructure verschoben)

**Aktueller Stand:** Definiert in `Romulus.UI.Wpf/Models/DashboardProjection.cs`
**Ziel:** Verschieben nach `Romulus.Infrastructure/Orchestration/DashboardProjection.cs`

**Warum verschieben:**
- Die API baut in `DashboardDataBuilder` exakt dieselben KPIs nochmal
- CLI-Output könnte dieselben Display-Strings nutzen
- Architekturverletzung: Entry Point (WPF) zieht von Infrastructure, aber Projection-Erzeugung gehört in Infrastructure

**Ziel-Struktur:**

```csharp
// Infrastructure/Orchestration/DashboardProjection.cs
public sealed record DashboardProjection(
    // ── KPI-Kernwerte (formatierte Strings) ──
    string Winners,
    string Dupes,
    string Junk,
    string Games,
    string DatHits,
    string HealthScore,
    string DedupeRate,
    string Duration,

    // ── DAT-Sektion ──
    string DatHaveDisplay,
    string DatWrongNameDisplay,
    string DatMissDisplay,
    string DatUnknownDisplay,
    string DatAmbiguousDisplay,

    // ── Conversion-Sektion ──
    string ConvertedDisplay,
    string ConvertBlockedDisplay,
    string ConvertReviewDisplay,
    string ConvertSavedBytesDisplay,

    // ── DAT-Rename-Sektion ──
    string DatRenameProposedDisplay,
    string DatRenameExecutedDisplay,
    string DatRenameFailedDisplay,

    // ── Move-Summary ──
    string MoveConsequenceText,

    // ── Qualität-Marker ──
    ResultQuality Quality,       // NEU: enum statt doppeltem MarkPlan/MarkProvisional

    // ── Detail-Projektionen (GUI-spezifisch, bleiben hier) ──
    IReadOnlyList<ConsoleDistributionItem> ConsoleDistribution,
    IReadOnlyList<DedupeGroupItem> DedupeGroups)
{
    public static DashboardProjection From(
        RunProjection projection,
        RunResult result,
        bool isConvertOnlyRun,
        bool isDryRun = false);
}

/// ResultQuality ersetzt das doppelte MarkProvisional + MarkPlan Stacking
public enum ResultQuality
{
    Final,          // Execute, erfolgreich → Werte ohne Marker
    Plan,           // DryRun, erfolgreich → "(Plan)"
    Provisional,    // Cancel/Fail mit Daten → "(vorläufig)"
    Empty           // Kein Ergebnis → "–"
}
```

**Kernänderung:** `MarkProvisional` und `MarkPlan` stacken nicht mehr.
`ResultQuality` ist ein einzelner enum-Wert, der die Marker-Logik zentralisiert:

```csharp
private static string ApplyQualityMarker(string value, ResultQuality quality) => quality switch
{
    ResultQuality.Plan => $"{value} (Plan)",
    ResultQuality.Provisional => $"{value} (vorläufig)",
    ResultQuality.Empty => "–",
    _ => value
};
```

### 2.3 ProgressProjection (vorhanden — Ziel: erweitern)

```csharp
public sealed record ProgressProjection(
    double Progress,            // 0–100
    string ProgressText,        // "42%"
    string CurrentPhase,        // "Scanning", "Deduplicating", etc.
    string CurrentFile,         // aktuell verarbeitete Datei
    RunState CurrentRunState,
    bool IsBusy,
    string BusyHint,            // "Vorschau wird erstellt..."
    // NEU:
    TimeSpan Elapsed,           // Laufzeit seit Start
    int ProcessedFiles,         // verarbeitete Dateien
    int TotalFiles)             // geschätzte Gesamtdateien (0 = unbekannt)
{
    public static readonly ProgressProjection Idle = new(
        0, "–", "–", "–", RunState.Idle, false, "",
        TimeSpan.Zero, 0, 0);
}
```

### 2.4 BannerProjection (vorhanden — Ziel: erweitern)

```csharp
public sealed record BannerProjection(
    bool ShowDryRunBanner,          // "Dies ist eine Vorschau"
    bool ShowMoveCompleteBanner,    // "Verschiebung abgeschlossen"
    bool ShowConfigChangedBanner,   // "Konfiguration geändert, Vorschau veraltet"
    // NEU:
    bool ShowCancelledBanner,       // "Run wurde abgebrochen"
    bool ShowFailedBanner,          // "Run fehlgeschlagen"
    bool ShowRollbackCompleteBanner,// "Rollback abgeschlossen"
    string? BannerMessage,          // Optionaler Detailtext
    BannerSeverity Severity)        // Info / Warning / Error
{
    public static readonly BannerProjection None = new(
        false, false, false, false, false, false, null, BannerSeverity.Info);
}

public enum BannerSeverity { Info, Warning, Error }
```

### 2.5 CommandState / CanExecute-Regeln (Ziel-Modell)

**Prinzip:** Jedes Command leitet seinen `CanExecute`-Zustand aus **maximal 3 expliziten Properties** ab. Keine verschachtelten Bedingungen.

| Command | CanExecute | Abhängige Properties |
|---------|------------|---------------------|
| `RunCommand` | `!IsBusy && Roots.Count > 0 && !HasBlockingValidationErrors && (DryRun \|\| CanStartMoveWithCurrentPreview)` | `IsBusy`, `Roots.Count`, `HasBlockingValidationErrors`, `DryRun`, `CanStartMoveWithCurrentPreview` |
| `CancelCommand` | `IsBusy` | `IsBusy` |
| `RollbackCommand` | `!IsBusy && CanRollback` | `IsBusy`, `CanRollback` |
| `StartMoveCommand` | `ShowStartMoveButton && !HasBlockingValidationErrors` | `ShowStartMoveButton`, `HasBlockingValidationErrors` |
| `QuickPreviewCommand` | `!IsBusy && Roots.Count > 0 && !HasBlockingValidationErrors` | `IsBusy`, `Roots.Count`, `HasBlockingValidationErrors` |
| `ConvertOnlyCommand` | `!IsBusy && Roots.Count > 0 && !HasBlockingValidationErrors` | `IsBusy`, `Roots.Count`, `HasBlockingValidationErrors` |
| `OpenReportCommand` | `!string.IsNullOrEmpty(LastReportPath)` | `LastReportPath` |
| `AddRootCommand` | `!IsBusy` | `IsBusy` |
| `RemoveRootCommand` | `!IsBusy && SelectedRoot != null` | `IsBusy`, `SelectedRoot` |

**Command-Requery-Trigger:**

Zentrale `DeferCommandRequery()`-Methode wird aufgerufen bei:
1. `RunState`-Änderung
2. `Roots.CollectionChanged`
3. `SelectedRoot`-Änderung
4. `CanRollback`-Änderung
5. `LastReportPath`-Änderung
6. Validierungs-Änderungen

**Ziel:** `DeferCommandRequery` wird **einmal pro State-Transition** aufgerufen, nicht bei jedem einzelnen Property-Change.

### 2.6 RunOutcome → UI Mapping

**Zentrale Mapping-Tabelle (determiniert Banner, Summary, Navigation, Dashboard-Quality):**

| RunResult.Status | RunState | ResultQuality | Banner | RunSummaryText | Navigation | Dashboard |
|------------------|----------|---------------|--------|----------------|------------|-----------|
| `"completed"` + DryRun | `CompletedDryRun` | `Plan` | `ShowDryRunBanner` | "Vorschau: {dupes} Dupes, {junk} Junk" | → Library/Dashboard | Werte + "(Plan)" |
| `"completed"` + Execute | `Completed` | `Final` | `ShowMoveCompleteBanner` | "{moved} Dateien verschoben" | → Library/Dashboard | Werte ohne Marker |
| `"cancelled"` + Daten | `Cancelled` | `Provisional` | `ShowCancelledBanner` | "Abgebrochen (unvollständig)" | → Library/Dashboard | Werte + "(vorläufig)" |
| `"cancelled"` + keine Daten | `Cancelled` | `Empty` | `ShowCancelledBanner` | "Abgebrochen" | → Library/Dashboard | Alle "–" |
| `"failed"` + Daten | `Failed` | `Provisional` | `ShowFailedBanner` | "Fehler aufgetreten" | → Library/Dashboard | Werte + "(vorläufig)" |
| `"failed"` + keine Daten | `Failed` | `Empty` | `ShowFailedBanner` | "Fehler" | → Library/Dashboard | Alle "–" |
| `"blocked"` | `Failed` | `Empty` | `ShowFailedBanner` | "Preflight blockiert: {reason}" | → Library/Dashboard | Alle "–" |

**Rollback-Ergebnis:**

| Szenario | RunSummaryText | Banner | Dashboard |
|----------|---------------|--------|-----------|
| Rollback erfolgreich (0 Fehler) | "Rollback: {n} Dateien wiederhergestellt" | `ShowRollbackCompleteBanner` | Reset auf "–" |
| Rollback teilweise ({n} Fehler) | "Rollback: {ok} ok, {fail} fehlschlagen" | `ShowRollbackCompleteBanner` (Warning) | Reset auf "–" |
| Rollback fehlschlagen | "Rollback fehlgeschlagen: {error}" | `ShowFailedBanner` | Unverändertes Dashboard |

### 2.7 ErrorSummaryModel (vorhanden als `ErrorSummaryProjection`)

**Ziel-Verfeinerung:**

```csharp
public sealed record UiError(
    string Code,                // "IO-MOVE", "CONVERT-ERR", "DAT-UNVERIFIED", etc.
    string Text,                // Lesbarer Text
    UiErrorSeverity Severity,   // Info, Warning, Error, Blocked
    string? FixHint = null);    // Optionaler Behebungshinweis

public enum UiErrorSeverity { Info, Warning, Error, Blocked }
```

**Erzeugung:** Ausschliesslich durch `ErrorSummaryProjection.Build(result, candidates, runLogs)`.
ViewModels rufen nur `PopulateErrorSummary()` auf, das intern `ErrorSummaryProjection.Build()` delegiert.

**Keine manuellen Fehler-Einträge im ViewModel**.

### 2.8 Modusabhängige KPI-Sichtbarkeit

| KPI | Standard-Dedupe | ConvertOnly | DryRun |
|-----|-----------------|-------------|--------|
| Winners | ✅ sichtbar | "–" (hidden) | ✅ + "(Plan)" |
| Dupes | ✅ sichtbar | "–" (hidden) | ✅ + "(Plan)" |
| Junk | ✅ sichtbar | "–" (hidden) | ✅ + "(Plan)" |
| Games | ✅ sichtbar | "–" (hidden) | ✅ + "(Plan)" |
| DatHits | ✅ sichtbar | "–" (hidden) | ✅ + "(Plan)" |
| HealthScore | ✅ sichtbar | "–" (hidden) | ✅ + "(Plan)" |
| DedupeRate | ✅ sichtbar | "–" (hidden) | ✅ + "(Plan)" |
| ConvertedDisplay | ✅ sichtbar | ✅ sichtbar | ✅ + "(Plan)" |
| ConvertSavedBytes | ✅ sichtbar | ✅ sichtbar | ✅ + "(Plan)" |
| Duration | ✅ sichtbar | ✅ sichtbar | ✅ sichtbar |
| MoveConsequenceText | ✅ sichtbar | "Nur Konvertierung" | ✅ + "(Plan)" |

**Regel:** `DashboardProjection.From()` entscheidet über Sichtbarkeit. ViewModels prüfen NICHT `ConvertOnly` selbst. Die XAML-View bindet blind an den String — `"–"` bedeutet "kein Wert".

---

## 3. Datenfluss in der GUI

### 3.1 Run-Pipeline-Fluss (User → Result)

```
┌────────────────────────────────────────────────────────────────────┐
│  User klickt "Start" / F5                                          │
│  → RunCommand.Execute()                                             │
└───────────┬────────────────────────────────────────────────────────┘
            │
            ▼
┌────────────────────────────────────────────────────────────────────┐
│  OnRun() — MainViewModel.RunPipeline.cs                            │
│  1. Validierungs-Gate (HasBlockingValidationErrors)                │
│  2. Move-Gate (DryRun || CanStartMoveWithCurrentPreview)           │
│  3. CurrentRunState ← Preflight                                    │
│  4. ResetDashboardForNewRun()    ← ALLE Dash-Werte auf "–"        │
│  5. BusyHint ← kontextabhängig                                     │
│  6. Fire RunRequested-Event                                         │
└───────────┬────────────────────────────────────────────────────────┘
            │
            ▼
┌────────────────────────────────────────────────────────────────────┐
│  MainWindow.Code-Behind: OnRunRequested()                          │
│  → Startet ExecuteRunFromViewModelAsync() als Task                  │
│    (nur Tray-Updates, kein Business)                                │
└───────────┬────────────────────────────────────────────────────────┘
            │
            ▼
┌────────────────────────────────────────────────────────────────────┐
│  ExecuteRunAsync() — MainViewModel.RunPipeline.cs                  │
│                                                                     │
│  Thread Pool:                                                       │
│  ┌──────────────────────────────────────────┐                      │
│  │ RunService.BuildOrchestrator(vm, progressCb)                    │
│  │ → RunOrchestrator + RunOptions + Pfade                          │
│  └──────────────────────────────────────────┘                      │
│                                                                     │
│  UI Thread:                                                         │
│  ┌──────────────────────────────────────────┐                      │
│  │ Progress Callback (100ms throttled):     │                      │
│  │ → ApplyProgressMessage()                 │                      │
│  │   → Progress ← estimated %              │                      │
│  │   → ProgressText ← "42%"               │                      │
│  │   → PerfPhase ← "Scanning"             │                      │
│  │   → PerfFile ← "game.zip"              │                      │
│  │   → RunState ← (phase-derived)         │                      │
│  └──────────────────────────────────────────┘                      │
│                                                                     │
│  Thread Pool:                                                       │
│  ┌──────────────────────────────────────────┐                      │
│  │ RunService.ExecuteRun(orchestrator, ...)  │                      │
│  │ → RunResult (immutable)                   │                      │
│  └──────────────────────────────────────────┘                      │
│                                                                     │
│  UI Thread:                                                         │
│  ┌──────────────────────────────────────────┐                      │
│  │ ApplyRunResult(RunResult)                │                      │
│  │ 1. RunProjectionFactory.Create(result)   │  ← Infrastructure   │
│  │ 2. DashboardProjection.From(projection)  │  ← Infrastructure   │
│  │ 3. Alle DashXxx ← dashboard.Xxx         │  ← Rein zuweisend   │
│  │ 4. ConsoleDistribution ← dashboard      │                      │
│  │ 5. DedupeGroupItems ← dashboard          │                      │
│  └──────────────────────────────────────────┘                      │
│                                                                     │
│  ┌──────────────────────────────────────────┐                      │
│  │ CompleteRun(success, reportPath, cancelled)                     │
│  │ → RunState ← terminal state              │                      │
│  │ → Banner ← RunOutcome-abhängig           │                      │
│  │ → RunSummaryText ← RunOutcome-abhängig   │                      │
│  │ → CanRollback ← audit check              │                      │
│  │ → Navigation → Library/Dashboard          │                      │
│  └──────────────────────────────────────────┘                      │
└────────────────────────────────────────────────────────────────────┘
```

### 3.2 Cancel-Fluss

```
User klickt "Cancel" / Esc
  │
  ▼
OnCancel()
  ├─ _cts?.Cancel() (unter Lock)
  ├─ CurrentRunState ← Cancelled
  ├─ BusyHint ← "Abbruch angefordert..."
  ├─ ShowMoveCompleteBanner ← false       ← MUSS hier passieren
  └─ SetRunSummary("Abgebrochen", Warning)

  ... ExecuteRunAsync() fängt OperationCanceledException ...

  ├─ ApplyRunResult(partialResult, force: true)
  ├─ PopulateErrorSummary()
  └─ CompleteRun(false, reportPath, cancelled: true)
       ├─ CurrentRunState bestätigt als Cancelled
       ├─ ShowCancelledBanner ← true         ← NEU
       ├─ RunSummaryText ← "Abgebrochen (unvollständig)"
       └─ Navigation → Library/Dashboard
```

### 3.3 Rollback-Fluss

```
User klickt "Rollback" / Ctrl+Z
  │
  ▼
OnRollbackAsync()
  ├─ Confirm-Dialog
  ├─ Integrity-Check (VerifyTrashIntegrity)
  │   └─ Bei Problemen: zweiter Confirm-Dialog
  │
  ├─ CurrentRunState ← RollbackActive      ← NEU (bisher fehlend)
  │
  ├─ Thread Pool: RollbackService.Execute()
  │   └─ → RestoreResult (immutable)
  │
  ├─ UI Thread:
  │   ├─ ResetDashboardForNewRun()
  │   ├─ CanRollback ← false
  │   ├─ ShowMoveCompleteBanner ← false
  │   ├─ ShowRollbackCompleteBanner ← true  ← NEU
  │   ├─ RunSummaryText ← "Rollback: {n} wiederhergestellt"  ← MUSS gesetzt werden (bisher leer)
  │   ├─ CurrentRunState ← Idle
  │   └─ Navigation → Library/Dashboard
  │
  └─ Bei Fehler:
      ├─ SetRunSummary(error, Error)
      └─ CurrentRunState ← Idle (nicht Failed, da Rollback != Run)
```

### 3.4 Dashboard-Binding-Fluss

**IST-Zustand (Problem):**

```
RunViewModel:   private string _dashWinners = "0";     ← Default "0"
MainViewModel:  ResetDashboardForNewRun() setzt "–";   ← Inkonsistenz
```

**ZIEL-Zustand:**

```
RunViewModel:   private string _dashWinners = "–";     ← Default "–"
                                                        (kein Run = kein Wert, nicht "0")
MainViewModel:  ResetDashboardForNewRun() setzt "–";   ← Konsistent
DashboardProjection.From():                             ← Einzige Quelle für echte Werte
```

**Property-Kette (Ziel):**

```
DashboardProjection                          RunViewModel                 MainWindow XAML
────────────────────                         ────────────────             ──────────────────
.Winners = "42 (Plan)"  ──Apply──→  Run.DashWinners = "42 (Plan)"  ──Bind──→  Text="{Binding DashWinners}"
.Quality = Plan         ──Apply──→  Run.ResultQuality = Plan        ──Bind──→  Foreground via Converter
```

**Ziel:** `MainViewModel` setzt `Run.DashWinners = dashboard.Winners` direkt. Die Dashboard-Anzeige ergibt sich rein aus dem Projection-Record.

### 3.5 Property-Forwarding (MainViewModel ↔ RunViewModel)

**IST:** MainViewModel hat ~20 `DashXxx`-Properties die nur durchleiten:
```csharp
public string DashWinners { get => Run.DashWinners; set => Run.DashWinners = value; }
```

**ZIEL:** Das Forwarding-Pattern ist akzeptabel (verhindert dass Views MainViewModel.Run.DashWinners binden müssen). Aber:

1. **Konsistente Defaults:** RunViewModel startet ALLE Dash-Properties mit `"–"`
2. **Atomares Apply:** `ApplyDashboard(DashboardProjection)` setzt ALLE Werte in einem Block
3. **Kein manuelles Dashboard-Zusammenbauen:** `DashWinners = projection.Keep.ToString()` ist verboten — nur `DashWinners = dashboard.Winners`

---

## 4. Trennung View / ViewModel / Projection / Core

### 4.1 Was in Views (XAML) liegen darf

| Erlaubt | Beispiel |
|---------|----------|
| Layout (Grid, StackPanel, etc.) | Panels, Margins, Padding |
| DataBinding (OneWay, TwoWay) | `{Binding DashWinners}` |
| Visibility-Binding | `{Binding IsBusy, Converter={StaticResource BoolToVisibility}}` |
| Style-Trigger | `DataTrigger` für Farbe basierend auf `StatusLevel` |
| Value-Converter (rein visuell) | `BoolToVisibilityConverter`, `StatusLevelToBrushConverter` |
| Animation | Storyboard für Fortschrittsbalken |
| Input-Events → Command-Binding | `Command="{Binding RunCommand}"` |
| `x:Name` für Focus-Management | Focus-Routing bei Keyboard-Navigation |

| Verboten | Grund |
|----------|-------|
| Business-Logik | Gehört in Core |
| KPI-Berechnung | Gehört in DashboardProjection |
| String-Formatierung von Zahlen | Gehört in DashboardProjection |
| RunState-Checks | Gehört in ViewModel |
| File-I/O | Gehört in Infrastructure |
| Score-Interpretation | Gehört in Core |
| Converter mit fachlichen Regeln | Gehört in ViewModel/Projection |

### 4.2 Was in ViewModels liegen darf

| Erlaubt | Beispiel |
|---------|----------|
| Observable Properties | `DashWinners`, `CurrentRunState`, `IsBusy` |
| Commands (RelayCommand) | `RunCommand`, `CancelCommand` |
| State-Übergänge | `CurrentRunState = RunState.Scanning` |
| Projection anwenden | `DashWinners = dashboard.Winners` |
| Progress-Updates empfangen | `ApplyProgressMessage(msg)` |
| RunService aufrufen | `_runService.ExecuteRun(...)` |
| Dialoge anfordern | `_dialog.Confirm(...)` (über Interface) |
| Navigation triggern | `Shell.NavigateTo("Library")` |
| Logging | `AddLog(message, level)` |
| Collection-Updates | `ConsoleDistribution.Clear(); ...Add(item)` |

| Verboten | Grund |
|----------|-------|
| GameKey-Normalisierung | Core |
| Score-Berechnung | Core |
| Region-Erkennung | Core |
| Winner-Selection | Core |
| DAT-Matching | Core/Infrastructure |
| Dedupe-Grouping | Core |
| KPI-Aggregation (HealthScore, DedupeRate) | RunProjectionFactory |
| Report-Erzeugung | Infrastructure |
| File-System-Operationen | Infrastructure |
| Tool-Aufrufe | Infrastructure |
| Audit-Trail schreiben | Infrastructure |
| Hash-Berechnung | Infrastructure |

### 4.3 Was in Projection-Records liegen darf

| Erlaubt | Beispiel |
|---------|----------|
| Formatierte Display-Strings | `"42 (Plan)"`, `"3,2 GB"`, `"85%"` |
| Quality-Marker | `ResultQuality.Plan` |
| Aggregierte KPI-Werte | `Winners`, `DedupeRate` |
| UI-Collection-Projektionen | `ConsoleDistribution`, `DedupeGroups` |
| Factory-Methode `From()` | Erzeugt immutable Record aus RunProjection + RunResult |
| Lokalisierte Labels | Formatierung mit i18n-Keys |

| Verboten | Grund |
|----------|-------|
| Mutable State | Projections sind Records — immutable |
| I/O | Projections sind pure Funktionen |
| Seiteneffekte | Kein Logging, kein File-Zugriff |
| UI-Framework-Abhängigkeiten | Kein WPF, kein Dispatcher |

### 4.4 Was in Code-Behind liegen darf

| Erlaubt | Beispiel |
|---------|----------|
| `InitializeComponent()` | Standard |
| Window-Lifecycle | `OnLoaded`, `OnClosing` |
| Tray-Integration | `TrayService.ShowBalloonTip()` |
| Focus-Management | Keyboard-Focus setzen |
| Window-Sizing | Responsive Layout-Anpassung |
| Task-Koordination | `_activeRunTask = ExecuteRunFromViewModelAsync()` |

| Verboten | Grund |
|----------|-------|
| Run-Start-Logik | Gehört in ViewModel |
| Cancellation-Logik | Gehört in ViewModel |
| Dashboard-Updates | Gehört in ViewModel |
| Ergebnis-Interpretation | Gehört in ViewModel/Projection |
| Service-Aufrufe | Gehört in ViewModel |

### 4.5 Verantwortlichkeits-Matrix (Ziel)

| Aufgabe | View | ViewModel | Projection | Core | Infra |
|---------|------|-----------|------------|------|-------|
| Layout | ✅ | | | | |
| DataBinding | ✅ | | | | |
| State-Management | | ✅ | | | |
| Command-Routing | | ✅ | | | |
| Dash-Werte anzeigen | ✅ bind | ✅ forward | ✅ erzeugt | | |
| KPI-Berechnung | | | | | ✅ RunProjectionFactory |
| Dashboard-String-Formatierung | | | ✅ | | |
| Quality-Marker | | | ✅ | | |
| Error-Summary | | ✅ trigger | ✅ erzeugt | | |
| Progress-Estimation | | ✅ | | | ✅ PhaseWeights |
| Fachliche Entscheidung | | | | ✅ | |
| I/O (Move, Hash, Scan) | | | | | ✅ |

---

## 5. Zu entfernende Altlogik

### 5.1 Harte Kandidaten (bekannte Bugs / Inkonsistenzen)

| # | Was | Wo | Problem | Fix |
|---|-----|----|---------|-----|
| 1 | `RunViewModel` Defaults `"0"` statt `"–"` | [RunViewModel.cs](src/Romulus.UI.Wpf/ViewModels/RunViewModel.cs#L208-L226) | Inkonsistent mit `ResetDashboardForNewRun()`. Frischer ViewModel zeigt "0" für Winners/Dupes/Junk/Games/DatHits — suggeriert "0 ROMs gefunden" statt "kein Run" | **Alle `= "0"` → `= "–"`** |
| 2 | `MarkProvisional` + `MarkPlan` stacken | [DashboardProjection.cs](src/Romulus.UI.Wpf/Models/DashboardProjection.cs) | Cancelled DryRun → `"3 (vorläufig) (Plan)"` — doppelter Marker, verwirrend | **`ResultQuality`-Enum, mutually exclusive** |
| 3 | `ShowMoveCompleteBanner` bleibt nach Cancel | [MainViewModel.RunPipeline.cs](src/Romulus.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs) `CompleteRun` | Cancel setzt Banner nicht auf false | **`CompleteRun(cancelled: true)` muss `ShowMoveCompleteBanner = false` setzen** |
| 4 | Rollback setzt keinen `RunSummaryText` | [MainViewModel.RunPipeline.cs](src/Romulus.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs) `OnRollbackAsync` | Nach Rollback: RunSummaryText ist leer — User hat keinen Status | **`SetRunSummary(...)` in Rollback-Abschluss** |
| 5 | Dashboard wird bei Re-Run nicht vor ApplyRunResult zurückgesetzt | [MainViewModel.RunPipeline.cs](src/Romulus.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs) | `OnRun()` setzt Reset, aber bei sehr schnellen Runs kann `ApplyRunResult` die alten Werte sehen | **`ResetDashboardForNewRun()` MUSS zu Beginn von `ExecuteRunAsync()` UND in `OnRun()` aufgerufen werden** |
| 6 | Cancel ohne Kandidaten zeigt "0" statt "–" | [DashboardProjection.cs](src/Romulus.UI.Wpf/Models/DashboardProjection.cs) | `IsPartialCancelledOrFailed` prüft nur `hasCandidates` — bei 0 Kandidaten fällt Quality auf `Final` statt `Empty` | **Explizite `Empty`-Quality wenn `AllCandidates.Count == 0` und Status = cancelled/failed** |

### 5.2 Strukturelle Verbesserungen

| # | Was | Wo | Problem | Fix |
|---|-----|----|---------|-----|
| 7 | `DashboardProjection` lebt in WPF | `UI.Wpf/Models/` | API baut eigene Variante; Architekturverletzung | **Nach `Infrastructure/Orchestration/` verschieben** |
| 8 | `RunRequested`-Event → Code-Behind | MainViewModel + MainWindow | ViewModel feuert Event, Code-Behind startet Task. Besser: ViewModel startet selbst | **`RunCommand` startet `ExecuteRunAsync()` direkt, Code-Behind nur für Tray-Notification** |
| 9 | ~20 DashXxx-Forwarding-Properties | MainViewModel.RunPipeline.cs | Boilerplate, fehleranfällig bei Erweiterung | **Akzeptabel, aber `ApplyDashboard(DashboardProjection)` Methode statt einzelner Zuweisungen** |
| 10 | Kein expliziter `RollbackActive`-State | RunStateMachine | Rollback blockiert UI, aber State ist undefiniert | **`RollbackActive`-State ergänzen** |
| 11 | `ConvertOnly`-Flag wird in `CompleteRun` zurückgesetzt | MainViewModel.RunPipeline.cs | Transient-Flag Seiteneffekt in Terminal-Methode — fragil | **Reset in `OnRun()` statt in `CompleteRun()`** |
| 12 | Mode-Erkennung als String-Check | RunOptions.Mode, RunResult.Status | `"DryRun"`, `"cancelled"` als Strings → keine Compile-Time Safety | **`RunMode` Enum (siehe Kern-Architektur-Dokument)** |
| 13 | Progress-State-Updates über String-Parsing | `UpdateCurrentRunStateFromProgress(msg)` | Phase-Name aus Progress-String extrahiert → fragil | **Typed `PhaseProgressEvent` Record statt String-Message** |

### 5.3 Nicht anfassen (funktioniert korrekt)

| Was | Grund |
|-----|-------|
| `RunStateMachine` Transitions-Tabelle | Korrekt und gut getestet (bis auf fehlenden RollbackActive) |
| `ErrorSummaryProjection.Build()` | Sauber strukturiert, gute Priorisierung |
| `StatusProjection` Pattern | Immutable, Factory-basiert |
| Command-Palette-Architektur | Sauber entkoppelt (ViewModel + fuzzy search) |
| Theme-System | ResourceDictionaries, saubere Abstraktion |
| Auto-Save Debounce | 2s Timer, PropertyChanged-basiert |
| Feature-Command-System | String-Key-Routing, partials, sauber trennbar |
| CancellationToken Lock-Pattern | Thread-sicher, race-condition-frei |

---

## 6. Migrationshinweise

### Priorität 1 — Sofort umsetzbar (bekannte Bugs)

#### 6.1 RunViewModel Defaults: `"0"` → `"–"`

**Aufwand:** Trivial (5 Min)
**Risiko:** Minimal
**Dateien:** [RunViewModel.cs](src/Romulus.UI.Wpf/ViewModels/RunViewModel.cs)

```csharp
// VORHER:
private string _dashWinners = "0";
private string _dashDupes = "0";
private string _dashJunk = "0";
private string _dashGames = "0";
private string _dashDatHits = "0";

// NACHHER:
private string _dashWinners = "–";
private string _dashDupes = "–";
private string _dashJunk = "–";
private string _dashGames = "–";
private string _dashDatHits = "–";
```

#### 6.2 ShowMoveCompleteBanner in CompleteRun(cancelled)

**Aufwand:** Trivial
**Risiko:** Minimal
**Datei:** [MainViewModel.RunPipeline.cs](src/Romulus.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs)

In `CompleteRun()`: Im `cancelled`-Branch `ShowMoveCompleteBanner = false;` einfügen.

#### 6.3 Rollback: RunSummaryText setzen

**Aufwand:** Trivial
**Risiko:** Minimal
**Datei:** [MainViewModel.RunPipeline.cs](src/Romulus.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs)

Im `OnRollbackAsync()` nach erfolgreichem Rollback:
```csharp
SetRunSummary(
    _loc.Format("Result.Summary.RollbackDone", restored.RolledBack, ...),
    restored.Failed > 0 ? UiErrorSeverity.Warning : UiErrorSeverity.Info);
```
**Bereits implementiert** im try-Block, aber der Pfad muss verifiziert werden — Red-Phase Test zeigt, dass es nicht korrekt greift wenn der Rollback über den ViewModel-Pfad simuliert wird.

#### 6.4 Double-Marking eliminieren → ResultQuality Enum

**Aufwand:** Klein (1–2h)
**Risiko:** Gering
**Dateien:** DashboardProjection.cs, Tests

1. `ResultQuality` Enum anlegen
2. `From()` berechnet Quality einmalig:
   - `cancelled/failed + no data → Empty`
   - `cancelled/failed + data → Provisional`
   - `dryRun → Plan`
   - `else → Final`
3. `ApplyQualityMarker(value, quality)` ersetzt doppeltes `MarkProvisional(MarkPlan(...))`
4. Tests aktualisieren

#### 6.5 Cancel ohne Daten: "–" statt "0"

**Aufwand:** Klein
**Risiko:** Gering
**Datei:** DashboardProjection.cs

In `From()`: Wenn `Result.AllCandidates.Count == 0` und Status = cancelled/failed → alle Werte "–" statt "0".

### Priorität 2 — Mittelfristige Verbesserungen

#### 6.6 DashboardProjection nach Infrastructure verschieben

**Aufwand:** Klein (Namespace-Move + using-Updates)
**Risiko:** Gering
**Dateien:** DashboardProjection.cs, ConsoleDistributionItem.cs, DedupeGroupItem.cs → Infrastructure/Orchestration/

1. Dateien verschieben
2. Namespace ändern
3. Using-Statements in WPF updaten
4. API `DashboardDataBuilder` kann `DashboardProjection.From()` nutzen

#### 6.7 `ApplyDashboard()` atomare Methode

**Aufwand:** Klein
**Risiko:** Gering
**Datei:** MainViewModel.RunPipeline.cs

```csharp
private void ApplyDashboard(DashboardProjection dashboard)
{
    DashWinners = dashboard.Winners;
    DashDupes = dashboard.Dupes;
    DashJunk = dashboard.Junk;
    DashDuration = dashboard.Duration;
    HealthScore = dashboard.HealthScore;
    DashGames = dashboard.Games;
    DashDatHits = dashboard.DatHits;
    // ... alle weiteren Felder
    
    ConsoleDistribution.Clear();
    foreach (var item in dashboard.ConsoleDistribution)
        ConsoleDistribution.Add(item);
    
    DedupeGroupItems.Clear();
    foreach (var item in dashboard.DedupeGroups)
        DedupeGroupItems.Add(item);
    
    MoveConsequenceText = dashboard.MoveConsequenceText;
}
```

Reduziert `ApplyRunResult()` auf:
```csharp
var dashboard = DashboardProjection.From(projection, result, isConvertOnlyRun, isDryRun);
ApplyDashboard(dashboard);
```

#### 6.8 `RollbackActive` RunState

**Aufwand:** Klein
**Risiko:** Gering
**Dateien:** RunState enum, RunStateMachine, OnRollbackAsync

1. `RollbackActive` zum Enum hinzufügen
2. Transitionen in RunStateMachine: `Idle/Completed/CompletedDryRun → RollbackActive → Idle`
3. `IsBusy` um `RollbackActive` erweitern
4. `OnRollbackAsync`: State vor Rollback setzen, nach Abschluss auf Idle

#### 6.9 RunRequested-Event eliminieren

**Aufwand:** Mittel
**Risiko:** Gering
**Dateien:** MainViewModel.cs, MainViewModel.RunPipeline.cs, MainWindow.xaml.cs

1. `OnRun()` startet `ExecuteRunAsync()` direkt als fire-and-forget Task
2. Code-Behind registriert sich für PropretyChanged auf `CurrentRunState` für Tray-Updates
3. `RunRequested`-Event entfernen

### Priorität 3 — Langfristige Konsolidierung

#### 6.10 Typed PhaseProgressEvent statt String-Parsing

**Aufwand:** Mittel
**Risiko:** Mittel (betrifft Orchestrator-Callback)

```csharp
public sealed record PhaseProgressEvent(
    string Phase,           // "Scanning", "Deduplicating", etc.
    string? CurrentFile,
    int ProcessedCount,
    int? TotalEstimate,
    double ProgressPercent);
```

1. RunOrchestrator feuert `PhaseProgressEvent` statt String
2. ViewModel empfängt typisiert, kein String-Parsing mehr
3. Eliminiert `TrySplitProgressMessage()` und `UpdateCurrentRunStateFromProgress()`

#### 6.11 BannerProjection als zentrale Banner-Quelle

**Aufwand:** Mittel
**Risiko:** Gering

1. `CompleteRun()` baut `BannerProjection` statt einzelner Bool-Properties
2. ViewModel bindet `ShowDryRunBanner` etc. an BannerProjection
3. Eliminiert verteilte Banner-Zuweisung über 3 Methoden

#### 6.12 ViewModel-Aufteilung evaluieren

**Aufwand:** Hoch (wenn durchgeführt)
**Risiko:** Mittel

`MainViewModel` hat aktuell ~80+ Properties, 4 Partial-Files, ~30 Commands. Prüfen ob sinnvolle Aufteilung möglich:
- `RunPipelineViewModel` → Run-Steuerung, Dashboard
- `ConfigurationViewModel` → Settings, Paths, Flags
- `NavigationViewModel` → Shell-State (bereits `ShellViewModel`)

**Nur durchführen wenn MainViewModel nachweislich die Wartbarkeit blockiert.**

---

## Anhang: Invarianten-Matrix GUI

| Invariante | Absicherung |
|------------|-------------|
| Alle Dash-Properties starten mit "–" (nicht "0") | Unit-Test: frischer RunViewModel |
| `MarkProvisional` und `MarkPlan` stacken nie | Unit-Test: DashboardProjection.From() mit cancelled + DryRun |
| `ShowMoveCompleteBanner = false` nach Cancel | Unit-Test: CompleteRun(cancelled: true) |
| `RunSummaryText != ""` nach Rollback | Unit-Test: OnRollbackAsync() erfolgreicher Pfad |
| Dashboard = "–" bei Cancel ohne Kandidaten | Unit-Test: DashboardProjection.From() mit 0 Kandidaten + cancelled |
| `ResetDashboardForNewRun()` setzt ALLE 16 Dash-Properties | Unit-Test: verify alle Properties == "–" nach Reset |
| RunCommand disabled während IsBusy | Unit-Test: RunCommand.CanExecute(null) bei jedem Busy-State |
| RollbackCommand disabled während IsBusy | Unit-Test: analog |
| CancelCommand enabled nur während IsBusy | Unit-Test: analog |
| Kein RunState-Übergang ohne IsValidTransition | Invarianten-Test: alle 11×11 Kombinationen |
| CompleteRun setzt IMMER RunState vor Navigation | Integration-Test |
| ConvertOnly = false nach CompleteRun (beliebig) | Unit-Test |
| Progress = 100 nach ApplyRunResult | Unit-Test |
| IsBusy = false nach CompleteRun | Unit-Test via State-Machine |
| `DashboardProjection.From()` ist pure Funktion (kein State) | Deterministisch per Definition (Record) |

## Anhang: View-ViewModel-Zuordnung (Ziel)

| View | ViewModel | Projection-Quelle |
|------|-----------|--------------------|
| StartView | MainViewModel | StatusProjection |
| ProgressView | MainViewModel (Run.*) | ProgressProjection |
| ResultView | MainViewModel (Run.*) | DashboardProjection, RunResult |
| DecisionsView | MainViewModel (Run.*) | DedupeGroupItems |
| LibrarySafetyView | MainViewModel | ErrorSummaryProjection |
| DatAuditView | DatAuditViewModel | DatAuditResult |
| ConfigRegionsView | SetupViewModel | — (direct binding) |
| ConfigOptionsView | SetupViewModel | StatusProjection |
| ConfigProfilesView | MainViewModel | RunProfileDocument[] |
| ExternalToolsView | SetupViewModel | StatusProjection |
| ToolsView | ToolsViewModel | FeatureCommandKeys |
| DatCatalogView | DatCatalogViewModel | DatCatalogEntry[] |
| CommandBar | MainViewModel | RunState, IsBusy |
| SmartActionBar | MainViewModel | MoveGateProjection |
| CommandPaletteView | CommandPaletteViewModel | — |
| SystemActivityView | MainViewModel | LogEntry[] |
| SystemAppearanceView | — | — (direct binding) |
