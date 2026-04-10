# ADR-012: Zielarchitektur Repo / Orchestrator / Pipeline

> **Status:** Proposed  
> **Datum:** 2026-03-19  
> **Scope:** Composition Root, Orchestrator, Pipeline-Phasen, Result-Objekte, mutable State, Entry Points  
> **Kontext:** ADR-005 (Core-Zielarchitektur), Wave-1/Wave-2 Refactor (AV1–AV4, OR1–OR3, P1–P7)  
> **Entscheidungstreiber:** Determinismus, Single Source of Truth, Testbarkeit, Entry-Point-Parität

---

## 1. Executive Design

### Ist-Zustand: Problembild

```
┌──────────────────────────────────────────────────────────────────────┐
│  HEUTE                                                               │
│                                                                      │
│  App.xaml.cs ─── 11 Singletons, keine shared Registrations          │
│  Program.cs (API) ─── eigene DI, eigener RunManager, eigener Build  │
│  Program.cs (CLI) ─── inline Setup, kein DI-Container               │
│                                                                      │
│  RunOrchestrator (850 LOC)                                           │
│    ├── Preflight-Logic inline                                        │
│    ├── Scan + Enrichment + Materialize inline                        │
│    ├── BuildStandardPhasePlan() mit Lambda-Closures + Callbacks      │
│    ├── Deferred-Service-Analysis inline (4 Previews)                 │
│    ├── Report-Generierung inline                                     │
│    ├── Audit-Sidecar inline                                          │
│    └── RunResultBuilder: 20+ mutable Properties                      │
│                                                                      │
│  RunEnvironmentBuilder.Build() → RunEnvironment                      │
│    └── gibt konkrete Typen zurück (FileSystemAdapter, AuditCsvStore) │
│    └── wird in WPF/API/CLI jeweils separat aufgerufen                │
│                                                                      │
│  RunManager (API, 800 LOC)                                           │
│    ├── Lifecycle (Create/Reuse/Cancel/Wait)                          │
│    ├── RunOptions-Bau inline                                         │
│    ├── Orchestrator-Bau inline                                       │
│    ├── RunProjection-Mapping inline                                  │
│    └── ApiRunResult-Bau mit 30+ Feldern                              │
│                                                                      │
│  RunService (WPF, 200 LOC)                                           │
│    ├── RunOptions-Bau aus ViewModel inline                           │
│    ├── RunEnvironment-Bau inline                                     │
│    ├── ReportPath-Fallback-Logik (50 LOC)                            │
│    └── AppState-Tracking inline                                      │
│                                                                      │
│  Result-Fluss:                                                       │
│    RunResultBuilder → RunResult → RunProjection → ApiRunResult       │
│    (4 Objekte, 3 Mappings, >80 Felder gesamt)                       │
└──────────────────────────────────────────────────────────────────────┘
```

**Kernprobleme:**

| # | Problem | Risiko |
|---|---------|--------|
| **P1** | Orchestrator enthält Fachlogik (Enrichment-Materialisierung, Deferred-Analysis, Report-Generierung, Sidecar-Schreibung) | Testbarkeit↓, SRP-Verletzung |
| **P2** | `RunResultBuilder` hat 20+ mutable Properties mit Zuweisungen über 600 LOC verstreut | Inkonsistente Teilzustände, Race-Potenzial |
| **P3** | `BuildStandardPhasePlan()` nutzt Closures + Setter-Callbacks statt typisierter Zwischen-Ergebnisse | Unlesbar, fragil, kein typisierter Datenfluss |
| **P4** | 3 Entry Points bauen RunOptions/RunEnvironment **jeweils inline** statt über shared Factory | Option-Drift, Paritätsverlust |
| **P5** | `RunEnvironment` gibt **konkrete** Typen zurück (FileSystemAdapter, AuditCsvStore) statt Interfaces | DI-Inversion verletzt, nicht mockbar |
| **P6** | API-`RunManager` ist Lifecycle + Builder + Executor + Mapper in einer 800-LOC-Klasse | SRP, Testbarkeit |
| **P7** | RunProjection wird in CLI/WPF teilweise umgangen, Felder direkt von RunResult gelesen | Parität gebrochen |

### Zielzustand

```
┌──────────────────────────────────────────────────────────────────────┐
│  ZIEL                                                                │
│                                                                      │
│  SharedServiceRegistration.AddRomulusCore(services, config)       │
│    └── Registriert alle Contracts/Core/Infrastructure-Services       │
│    └── Entry Points registrieren NUR ihre eigene Shell               │
│                                                                      │
│  RunOptionsFactory.Create(source) → RunOptions                       │
│    └── Single builder für alle Entry Points                          │
│    └── Source: CliArgs | ApiRequest | ViewModel (via Interface)       │
│                                                                      │
│  RunOrchestrator (≤250 LOC, KEINE Fachlogik)                        │
│    └── PhasePlan aus PhasePlanBuilder.Build(options)                 │
│    └── Foreach phase: result = phase.Execute(prevResult)             │
│    └── RunResultBuilder.Append(phaseResult) pro Phase                │
│    └── return builder.Build()                                        │
│                                                                      │
│  PhasePlanBuilder.Build(options) → IReadOnlyList<IPhaseStep>         │
│    └── Conditional: ConvertOnly → [Scan, Enrich, Convert]            │
│    └── Standard:    [Scan, Enrich, Dedupe, Junk?, Move?, Sort?,      │
│                      Convert?, Report, AuditSeal]                    │
│                                                                      │
│  Phase-Datenfluss (streng getypt):                                   │
│    ScanOutput → EnrichmentOutput → DedupeOutput → ...                │
│    Jede Phase kennt nur ihren Input und Output                       │
│                                                                      │
│  RunResult (sealed record, immutable nach Build)                     │
│    └── RunProjection = RunProjectionFactory.Create(RunResult)        │
│    └── ALLE Consumer lesen NUR RunProjection                         │
│                                                                      │
│  Entry Points:                                                       │
│    CLI:  parse → RunOptionsFactory → orchestrator.Execute → project  │
│    API:  validate → RunOptionsFactory → lifecycle → execute → project│
│    WPF:  VM → RunOptionsFactory → execute on BG thread → project    │
└──────────────────────────────────────────────────────────────────────┘
```

---

## 2. Zielobjekte und Services

### 2.1 Composition Root

#### `SharedServiceRegistration` (neue statische Klasse in Infrastructure)

```csharp
namespace Romulus.Infrastructure;

public static class SharedServiceRegistration
{
    public static IServiceCollection AddRomulusCore(
        this IServiceCollection services,
        RomulusConfig config)
    {
        // Contracts Ports
        services.AddSingleton<IFileSystem, FileSystemAdapter>();
        services.AddSingleton<IAuditStore>(sp =>
            new AuditCsvStore(
                sp.GetRequiredService<IFileSystem>(),
                config.OnWarning ?? (_ => { }),
                AuditSecurityPaths.GetDefaultSigningKeyPath()));

        // Core (pure, stateless – können Singleton sein)
        services.AddSingleton<IDeduplicationEngine, DeduplicationEngine>();

        // Infrastructure
        services.AddSingleton<IToolRunner>(sp =>
            new ToolRunnerAdapter(config.ToolHashesPath));
        services.AddSingleton<IRunEnvironmentFactory, RunEnvironmentFactory>();
        services.AddSingleton<IRunOptionsFactory, RunOptionsFactory>();
        services.AddSingleton<IPhasePlanBuilder, PhasePlanBuilder>();

        // Orchestrator
        services.AddTransient<IRunOrchestrator, RunOrchestrator>();

        return services;
    }
}
```

**Regel:** Entry Points registrieren NUR eigene Shell-Concerns:

| Entry Point | Eigene Registrations |
|-------------|---------------------|
| **WPF** | IThemeService, ILocalizationService, ISettingsService, IDialogService, MainViewModel, MainWindow, IAppState |
| **API** | RunLifecycleManager (ersetzt RunManager), Middleware, Rate Limiter |
| **CLI** | — (braucht keinen DI-Container, nutzt Factory direkt) |

### 2.2 RunOptionsFactory

Eliminiert die 3-fache inline-Konstruktion:

```csharp
namespace Romulus.Infrastructure.Orchestration;

public interface IRunOptionsSource
{
    IReadOnlyList<string> Roots { get; }
    string Mode { get; }
    string[] PreferRegions { get; }
    IReadOnlyList<string> Extensions { get; }
    bool RemoveJunk { get; }
    bool OnlyGames { get; }
    bool KeepUnknownWhenOnlyGames { get; }
    bool AggressiveJunk { get; }
    bool SortConsole { get; }
    bool EnableDat { get; }
    string? DatRoot { get; }
    string? HashType { get; }
    string? ConvertFormat { get; }
    bool ConvertOnly { get; }
    string? TrashRoot { get; }
    string ConflictPolicy { get; }
}

public sealed class RunOptionsFactory : IRunOptionsFactory
{
    public RunOptions Create(IRunOptionsSource source, string? auditPath, string? reportPath)
    {
        return new RunOptions
        {
            Roots = source.Roots.ToList(),
            Mode = source.Mode,
            PreferRegions = source.PreferRegions,
            Extensions = source.Extensions.Count > 0
                ? source.Extensions
                : RunOptions.DefaultExtensions,
            // ... alle Felder 1:1 gemappt ...
            AuditPath = auditPath,
            ReportPath = reportPath,
            ConflictPolicy = source.ConflictPolicy
        };
    }
}
```

**Adapter pro Entry Point:**

- `CliRunOptions` implementiert `IRunOptionsSource` (existiert bereits)
- `ApiRequest` → `ApiRunOptionsAdapter : IRunOptionsSource`
- `MainViewModel` → `ViewModelRunOptionsAdapter : IRunOptionsSource`

### 2.3 RunEnvironment (Interface-basiert)

```csharp
// NEU: Interface statt sealed class mit konkreten Typen
public interface IRunEnvironment
{
    IFileSystem FileSystem { get; }
    IAuditStore AuditStore { get; }
    ConsoleDetector? ConsoleDetector { get; }
    FileHashService? HashService { get; }
    IFormatConverter? Converter { get; }
    DatIndex? DatIndex { get; }
}

// Factory statt static Build()
public interface IRunEnvironmentFactory
{
    IRunEnvironment Create(RunOptions options, Action<string>? onWarning = null);
}
```

**Vorteil:** Testbar via Mock; keine Leaks von FileSystemAdapter/AuditCsvStore nach oben.

### 2.4 PhasePlanBuilder

Ersetzt `BuildStandardPhasePlan()` mit seinen Closure-Callbacks:

```csharp
public interface IPhasePlanBuilder
{
    IReadOnlyList<IPhaseStep> Build(RunOptions options, IRunEnvironment env);
}

public interface IPhaseStep
{
    string Name { get; }
    PhaseStepResult Execute(PipelineState state, CancellationToken ct);
}

// Typisierter akkumulierender Zustand, den Phasen LESEN und ERWEITERN
public sealed class PipelineState
{
    // Immutable nach Zuweisung (set-once pattern)
    public IReadOnlyList<RomCandidate>? AllCandidates { get; private set; }
    public IReadOnlyList<RomCandidate>? ProcessingCandidates { get; private set; }
    public IReadOnlyList<DedupeResult>? AllGroups { get; private set; }
    public IReadOnlyList<DedupeResult>? GameGroups { get; private set; }
    public IReadOnlySet<string>? JunkRemovedPaths { get; private set; }

    public void SetScanOutput(IReadOnlyList<RomCandidate> all, IReadOnlyList<RomCandidate> processing)
    {
        if (AllCandidates is not null)
            throw new InvalidOperationException("ScanOutput already set");
        AllCandidates = all;
        ProcessingCandidates = processing;
    }

    public void SetDedupeOutput(IReadOnlyList<DedupeResult> allGroups, IReadOnlyList<DedupeResult> gameGroups)
    {
        if (AllGroups is not null)
            throw new InvalidOperationException("DedupeOutput already set");
        AllGroups = allGroups;
        GameGroups = gameGroups;
    }

    public void SetJunkPaths(IReadOnlySet<string> paths)
    {
        if (JunkRemovedPaths is not null)
            throw new InvalidOperationException("JunkPaths already set");
        JunkRemovedPaths = paths;
    }
}
```

**Vorteil gegenüber Closures:**
- Typisiert statt `Func<>` / `Action<>`
- Set-Once guarantiert Forward-Only Datenfluss
- Testbar: man kann `PipelineState` mit Test-Daten vorladen

---

## 3. Pipeline-Datenfluss

### 3.1 Phasensequenz (Zielzustand)

```
                              RunOptions
                                  │
                    ┌─────────────▼──────────────┐
                    │   PreflightPhase            │
                    │   In:  RunOptions           │
                    │   Out: OperationResult      │
                    └─────────────┬──────────────┘
                                  │
                    ┌─────────────▼──────────────┐
                    │   ScanPhase                 │
                    │   In:  RunOptions           │
                    │   Out: IReadOnlyList<       │
                    │         ScannedFileEntry>   │
                    │   → state.AllCandidates     │
                    │   → state.ProcessingCands   │
                    └─────────────┬──────────────┘
                                  │
                    ┌─────────────▼──────────────┐
                    │   EnrichmentPhase           │
                    │   In:  ScannedFileEntry[]   │
                    │   Out: RomCandidate[]       │
                    │   (Streaming-Materialisiert)│
                    └─────────────┬──────────────┘
                                  │
        ┌─────────────────────────┼──────────────────────────┐
        │ ConvertOnly?            │ Standard                  │
        │                         │                           │
        ▼                         ▼                           │
  ConvertOnlyPhase         DedupePhase                        │
        │                    │                                │
        │                    ├──→ state.AllGroups              │
        │                    └──→ state.GameGroups             │
        │                         │                           │
        │                    ┌────▼────────────────┐          │
        │                    │ JunkRemovalPhase     │          │
        │                    │ (conditional)        │          │
        │                    │ → state.JunkPaths    │          │
        │                    └────┬────────────────┘          │
        │                         │                           │
        │                    ┌────▼────────────────┐          │
        │                    │ MovePhase            │          │
        │                    │ (Mode=Move only)     │          │
        │                    └────┬────────────────┘          │
        │                         │                           │
        │                    ┌────▼────────────────┐          │
        │                    │ ConsoleSortPhase     │          │
        │                    │ (conditional)        │          │
        │                    └────┬────────────────┘          │
        │                         │                           │
        │                    ┌────▼────────────────┐          │
        │                    │ WinnerConvertPhase   │          │
        │                    │ (conditional)        │          │
        │                    └────┬────────────────┘          │
        │                         │                           │
        └──────────┬──────────────┘                           │
                   │                                          │
         ┌─────────▼──────────────┐                           │
         │   ReportPhase          │                           │
         │   (extracted from Orch)│                           │
         └─────────┬──────────────┘                           │
                   │                                          │
         ┌─────────▼──────────────┐                           │
         │   AuditSealPhase       │                           │
         │   (Sidecar + HMAC)     │                           │
         └─────────┬──────────────┘                           │
                   │                                          │
                   ▼                                          │
              RunResult (immutable)                           │
```

### 3.2 Orchestrator (Zielzustand, ≤250 LOC)

```csharp
public sealed class RunOrchestrator : IRunOrchestrator
{
    private readonly IPhasePlanBuilder _planBuilder;
    private readonly Action<string>? _onProgress;

    public RunOrchestrator(IPhasePlanBuilder planBuilder, Action<string>? onProgress = null)
    {
        _planBuilder = planBuilder;
        _onProgress = onProgress;
    }

    public RunResult Execute(RunOptions options, IRunEnvironment env, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var metrics = new PhaseMetricsCollector();
        metrics.Initialize();

        var state = new PipelineState();
        var resultBuilder = new RunResultBuilder();

        var plan = _planBuilder.Build(options, env);

        foreach (var step in plan)
        {
            ct.ThrowIfCancellationRequested();
            _onProgress?.Invoke($"[{step.Name}] Starte…");

            metrics.StartPhase(step.Name);
            var phaseResult = step.Execute(state, ct);
            resultBuilder.Append(step.Name, phaseResult);
            metrics.CompletePhase(phaseResult.ItemCount);

            _onProgress?.Invoke($"[{step.Name}] Abgeschlossen ({phaseResult.ItemCount} Items)");
        }

        sw.Stop();
        resultBuilder.DurationMs = sw.ElapsedMilliseconds;
        resultBuilder.PhaseMetrics = metrics.GetMetrics();

        return resultBuilder.Build();
    }
}
```

**Was der Orchestrator NICHT mehr tut:**
- ❌ Enrichment-Materialisierung (`MaterializeEnrichedCandidates`)
- ❌ Deferred-Service-Analysis (4 × Try/Catch)
- ❌ Report-Generierung (50 LOC + Fallback)
- ❌ Audit-Sidecar-Schreibung (30 LOC + Cancel-Sidecar)
- ❌ OnlyGames-Filterung
- ❌ ConvertOnly-Branching
- ❌ RunOptions-basiertes Conditional-Planning

All dies wandert in `PhasePlanBuilder` und die einzelnen `IPhaseStep`-Implementierungen.

### 3.3 RunResultBuilder (minimiert)

```csharp
// Ziel: Builder kennt nur PhaseStepResults, kein Fach-API
internal sealed class RunResultBuilder
{
    private readonly Dictionary<string, PhaseStepResult> _phaseResults = new();

    public long DurationMs { get; set; }
    public PhaseMetricsResult? PhaseMetrics { get; set; }

    public void Append(string phaseName, PhaseStepResult result)
    {
        _phaseResults[phaseName] = result;
    }

    public RunResult Build()
    {
        // Extrahiert typisierte Ergebnisse aus der Phasen-Map
        var scan = Get<ScanPhaseResult>("Scan");
        var dedupe = Get<DedupePhaseResult>("Deduplicate");
        // ...

        return new RunResult
        {
            Status = DeriveOutcome().ToStatusString(),
            ExitCode = DeriveExitCode(),
            // Felder aus typisierten Phase-Ergebnissen
            TotalFilesScanned = scan?.TotalFiles ?? 0,
            GroupCount = dedupe?.GameGroups.Count ?? 0,
            // ...
            DurationMs = DurationMs,
            PhaseMetrics = PhaseMetrics
        };
    }
}
```

### 3.4 PhaseStepResult (einheitlicher Phase-Return)

```csharp
public sealed class PhaseStepResult
{
    public required string Status { get; init; }     // "ok" | "skipped" | "error"
    public required int ItemCount { get; init; }
    public object? TypedResult { get; init; }        // ScanPhaseResult | DedupePhaseResult | ...
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

// Pro Phase ein typisiertes Result-Objekt:
public sealed record ScanPhaseResult(
    IReadOnlyList<RomCandidate> AllCandidates,
    IReadOnlyList<RomCandidate> ProcessingCandidates,
    int UnknownCount,
    IReadOnlyDictionary<string, int> UnknownReasonCounts,
    int FilteredNonGameCount);

public sealed record DedupePhaseResult(
    IReadOnlyList<DedupeResult> AllGroups,
    IReadOnlyList<DedupeResult> GameGroups,
    int LoserCount);

public sealed record JunkPhaseResult(
    MovePhaseResult MoveResult,
    IReadOnlySet<string> RemovedPaths);

// etc.
```

---

## 4. Zu entfernende Altlogik

### 4.1 Aus RunOrchestrator entfernen

| LOC (ca.) | Methode / Block | Wohin |
|-----------|----------------|-------|
| 50 | `MaterializeEnrichedCandidates()` | → `ScanEnrichPhaseStep` (intern) |
| 80 | `ExecuteDeferredServiceAnalysis()` + 4 Sub-Methoden | → `DeferredAnalysisPhaseStep` (eigener Step oder entfernen wenn rein informativ) |
| 120 | `BuildStandardPhasePlan()` mit Closures | → `PhasePlanBuilder.Build()` |
| 80 | `ExecuteDedupePhase()` / `ExecuteJunkPhaseIfEnabled()` / `ExecuteWinnerConversionPhase()` / `ExecuteConvertOnlyPhase()` | → jeweiliger `IPhaseStep` |
| 40 | `GenerateReport()` + Fallback-Logik | → `ReportPhaseStep` |
| 30 | Audit-Sidecar-Schreibung (normal + cancel) | → `AuditSealPhaseStep` |
| 15 | OnlyGames-Filterung (inline im Execute) | → `ScanEnrichPhaseStep` post-filter |

**Gesamt: ~415 LOC von 850 LOC → Orchestrator schrumpft auf ~250 LOC.**

### 4.2 Aus RunManager (API) entfernen

| LOC (ca.) | Block | Wohin |
|-----------|-------|-------|
| 60 | `ExecuteWithOrchestrator()`: RunOptions-Bau | → `RunOptionsFactory` + `ApiRunOptionsAdapter` |
| 20 | `ExecuteWithOrchestrator()`: RunEnvironment-Bau | → `IRunEnvironmentFactory.Create()` |
| 40 | `ExecuteWithOrchestrator()`: ApiRunResult-Bau | → `ApiRunResultMapper.Map(RunProjection)` |
| 20 | `EstimateProgressPercent()` | → `ProgressEstimator` (shared) |
| 30 | `BuildPhaseMetricsPayload()` / `BuildDedupeGroupsPayload()` | → `ApiRunResultMapper` |

**Gesamt: ~170 LOC → RunManager (→ RunLifecycleManager) schrumpft auf ~600 LOC (rein Lifecycle).**

### 4.3 Aus RunService (WPF) entfernen

| LOC (ca.) | Block | Wohin |
|-----------|-------|-------|
| 40 | `BuildOrchestrator()`: RunOptions-Bau aus VM | → `RunOptionsFactory` + `ViewModelRunOptionsAdapter` |
| 20 | `BuildOrchestrator()`: RunEnvironment-Bau | → `IRunEnvironmentFactory.Create()` |
| 50 | `ResolveReportPath()` Fallback-Logik | → `ReportPathResolver` (shared) |

**Gesamt: ~110 LOC → RunService schrumpft auf ~80 LOC (nur noch AppState-Tracking + Execute-Delegation).**

### 4.4 RunResultBuilder-Felder eliminieren

Der aktuelle `RunResultBuilder` hat 20+ mutable Properties, die über 600 LOC verstreut zugewiesen werden via `result.X = y`.

**Ziel:** Builder kennt nur `Append(phaseName, PhaseStepResult)`. Derived fields werden in `Build()` einmalig aus den typisierten Phase-Results extrahiert. Keine Phase schreibt direkt in den Builder.

### 4.5 RunEnvironment: Konkrete Typen → Interfaces

```
// LÖSCHEN:
public sealed class RunEnvironment
{
    public FileSystemAdapter FileSystem { get; }    // ← konkret
    public AuditCsvStore Audit { get; }              // ← konkret
    ...
}

// ERSETZEN DURCH:
public interface IRunEnvironment
{
    IFileSystem FileSystem { get; }                  // ← Interface
    IAuditStore AuditStore { get; }                   // ← Interface
    ...
}
```

---

## 5. Migrationshinweise

### 5.1 Migrationsstrategie: Strangler Fig Pattern

Keine Big-Bang-Migration. Stattdessen werden neue Strukturen **parallel** eingeführt und der Orchestrator schrittweise ausgedünnt.

### 5.2 Empfohlene Reihenfolge (4 Wellen)

#### Welle A: Foundation (kein Risikobereich, keine Logic-Änderung)

| Schritt | Was | Risiko |
|---------|-----|--------|
| A-1 | `IRunEnvironment` Interface + Adapter einführen | Gering: Typen-Swap |
| A-2 | `RunOptionsFactory` + `IRunOptionsSource` einführen | Gering: Extract Method |
| A-3 | `SharedServiceRegistration.AddRomulusCore()` einführen | Gering: DI-Refactor |
| A-4 | Entry Points auf shared Registration umstellen | Mittel: 3 Dateien gleichzeitig |

**Testmatrix A:** Alle 3 Entry Points produzieren identische `RunOptions` für gleiche Inputs.

#### Welle B: Pipeline-Datenfluss typisieren

| Schritt | Was | Risiko |
|---------|-----|--------|
| B-1 | `PipelineState` einführen (set-once Container) | Gering: neuer Typ |
| B-2 | `PhaseStepResult` + typisierte Phase-Results einführen | Gering: neue Records |
| B-3 | `PhasePlanBuilder` einführen, initial als Wrapper um `BuildStandardPhasePlan` | Mittel: Logik-Move |
| B-4 | Bestehende Phases auf `IPhaseStep` umstellen | Mittel: Interface-Anpassung |

**Testmatrix B:** Pipeline-Output bitidentisch vor/nach Umstellung (Snapshot-Vergleich von RunResult).

#### Welle C: Orchestrator ausdünnen

| Schritt | Was | Risiko |
|---------|-----|--------|
| C-1 | `ReportPhaseStep` + `AuditSealPhaseStep` extrahieren | Gering: Extract |
| C-2 | `DeferredAnalysisPhaseStep` extrahieren | Gering: non-destructive |
| C-3 | Inline-Methoden (`ExecuteDedupePhase` etc.) in Steps verschieben | Mittel: pro Phase |
| C-4 | `BuildStandardPhasePlan()` durch `PhasePlanBuilder.Build()` ersetzen | Mittel: Closure-Elimination |
| C-5 | `RunResultBuilder` auf Append-Pattern umstellen | Mittel: alle Phase-Zuweisungen |

**Testmatrix C:** Komplette Test-Suite grün nach jedem Schritt. Preview/Execute/Report-Parität per Snapshot.

#### Welle D: Entry Points aufräumen

| Schritt | Was | Risiko |
|---------|-----|--------|
| D-1 | `RunManager` → Lifecycle + Mapper trennen (→ `RunLifecycleManager` + `ApiRunResultMapper`) | Mittel |
| D-2 | `RunService` (WPF) auf `IRunOptionsFactory` + `IRunOrchestrator` kürzen | Gering |
| D-3 | CLI `Program.cs` auf Factory-Pattern umstellen | Gering |
| D-4 | `ReportPathResolver` als shared Service extrahieren | Gering |

**Testmatrix D:** API-Lifecycle-Tests, CLI dry-run Parity, WPF RunService-Tests.

### 5.3 Kritische Invarianten (nach jeder Welle prüfen)

| Invariante | Prüfmethode |
|-----------|-------------|
| Gleiche RunOptions für gleiche Inputs über alle 3 Entry Points | Snapshot-Vergleich |
| Deterministischer Winner-Selection Output | Bestehende Core-Tests |
| Preview zeigt gleiche Entscheidungen wie Execute | DryRun vs Move Report-Diff |
| RunProjection ist Single Source für alle KPIs | Grep: kein Entry Point liest direkt von RunResult |
| Kein Move außerhalb erlaubter Roots | Safety-Tests |
| Audit-Trail vollständig bei Normal und Cancel | Sidecar-Integrationstests |

### 5.4 Nicht-Ziele (explizit ausgegrenzt)

| Was | Warum nicht |
|-----|-------------|
| Core-Scoring-Änderungen (GameKey, Region, FormatScore) | Orthogonal, eigenes ADR-005 |
| RomCandidate als sealed record (D-01 aus ADR-005) | Breaking Change, eigene Welle |
| API-Endpoint-Redesign | Orthogonal, eigenes ADR-009 |
| GUI/ViewModel-Restructuring | Orthogonal, eigenes ADR-006 |
| Plugin-System / Modularer Loader | Feature, kein Architektur-Refactor |

### 5.5 Entscheidungstabelle: Wo lebt was?

| Concern | IST | ZIEL |
|---------|-----|------|
| RunOptions-Bau | 3× inline (WPF/API/CLI) | `RunOptionsFactory` (1×) |
| RunEnvironment-Bau | `RunEnvironmentBuilder.Build()` static | `IRunEnvironmentFactory.Create()` (DI) |
| Phase-Planning (conditionals) | `BuildStandardPhasePlan()` im Orchestrator | `PhasePlanBuilder.Build()` |
| Phase-Execution-Loop | Orchestrator mit inline Try/Catch | Orchestrator: nur `foreach + Execute` |
| Enrichment-Materialisierung | Orchestrator inline | `ScanEnrichPhaseStep` |
| Deferred-Analysis | Orchestrator inline | `DeferredAnalysisPhaseStep` oder eliminiert |
| Report-Generierung | Orchestrator inline + Fallback | `ReportPhaseStep` |
| Audit-Sidecar | Orchestrator inline (normal + cancel) | `AuditSealPhaseStep` |
| RunResult-Aufbau | 20+ Property-Zuweisungen verstreut | `RunResultBuilder.Append()` pro Phase |
| RunProjection-Erstellung | Factory, aber teilweise umgangen | Factory, strikt enforced |
| API Result-Mapping | RunManager inline | `ApiRunResultMapper` |
| Progress-Estimation | RunManager inline | `ProgressEstimator` (shared) |
| ReportPath-Fallback | RunService inline (50 LOC) | `ReportPathResolver` (shared) |

---

## Appendix: ADR-Rückverfolgung

| ADR-012 Referenz | Bezug zu ADR-005 |
|-----------------|------------------|
| IPhaseStep / PhasePlanBuilder | Implementiert D-05 (Phase-Handler-Pattern) |
| RunResultBuilder Append-Pattern | Implementiert D-02 (Builder → immutable Result) |
| RunProjection als Single Source | Implementiert D-03 |
| PipelineState set-once | Neues Konzept (in ADR-005 nicht adressiert) |
| SharedServiceRegistration | Neues Konzept (in ADR-005 nicht adressiert) |
| RunOptionsFactory | Neues Konzept (in ADR-005 nicht adressiert) |
| IRunEnvironment Interface | Neues Konzept (in ADR-005 nicht adressiert) |
