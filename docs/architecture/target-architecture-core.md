# Zielarchitektur Kernfunktionen

## 1. Executive Design

### Leitsatz

Jede fachliche Entscheidung existiert genau einmal.
Jedes Kernobjekt ist immutable nach Erzeugung.
Jede Phase produziert ein typisiertes Result, das unveränderlich in die nächste Phase fliesst.
GUI, CLI und API sind reine Konsumenten — sie dürfen nichts herleiten, was Core oder Infrastructure bereits entscheidet.

### Architektur-Prinzipien

```
Entry Points (GUI / CLI / API)
    │  konsumieren nur: RunProjection, RunResult, PipelineProgress
    │  dürfen NICHT: Scores berechnen, GameKeys normalisieren, Gruppen bilden
    │
Infrastructure (Orchestration, Adapter, I/O)
    │  orchestriert Phasen, erbringt I/O, baut RunResult
    │  dürfen NICHT: Scoring-Regeln ändern, Winner-Logik enthalten,
    │                Classification-Sonderregeln einführen
    │
Core (pure, deterministic, I/O-free)
    │  alle fachlichen Entscheidungen: Key, Region, Score, Winner, Sort, Classify
    │  dürfen NICHT: Dateien lesen, Tools aufrufen, Netzwerk oder Prozesse starten
    │
Contracts (Interfaces, Models, Fehlerverträge)
    keine Logik, keine I/O
```

### Kerngarantien

| Garantie | Bedeutung |
|----------|-----------|
| **Determinismus** | Gleiche Eingaben → gleiche Ausgaben. Keine Seed-, Zeit- oder Reihenfolgeabhängigkeit |
| **Single Source of Truth** | Eine fachliche Wahrheit pro Domäne (Key, Region, Score, Winner, Sort, Classification) |
| **Immutability** | Alle Result-Objekte sind records/init-only. Mutation nur in Builder-Objekten vor Materialisierung |
| **Phasenvertrag** | Jede Phase hat typisierte Eingabe + Ausgabe. Keine Side-Channel-Kommunikation |
| **Preview-Execute-Parität** | DryRun und Execute durchlaufen dieselbe Fachlogik. Nur I/O-Operationen werden unterdrückt |

---

## 2. Zielobjekte und Services

### 2.1 Vertrags-Schicht (Contracts)

#### Kern-Enums (immutable, zentral)

| Enum | Datei | Werte | Bemerkung |
|------|-------|-------|-----------|
| `RunMode` | `Contracts/Models/RunMode.cs` | `DryRun, Execute, ConvertOnly` | **NEU** — ersetzt String-basierte Mode-Prüfung |
| `FileCategory` | vorhanden | `Game, Bios, NonGame, Junk, Unknown` | stabil |
| `DecisionClass` | vorhanden | `Unknown, Game, Bios, NonGame, Junk` | stabil |
| `SortDecision` | vorhanden → umbenennen | `Sortable, DatVerified, Review, Blocked, Unknown` | Rename `Sort`→`Sortable` für Klarheit |
| `ConversionOutcome` | vorhanden | `Success, Skipped, Error, Blocked, Review` | stabil |
| `DatAuditStatus` | vorhanden | `Have, HaveWrongName, Miss, Unknown, Ambiguous` | stabil |
| `EvidenceTier` | vorhanden | `Tier0`–`Tier4` | stabil |
| `PlatformFamily` | vorhanden | `Cartridge, Disc, Computer, Arcade...` | stabil |

#### Kern-Records (immutable)

##### `ScannedFileEntry` — Roher Scan-Output (vorhanden, stabil)
```
Path, Root, Extension, SizeBytes?, LastWriteUtc?
```

##### `ClassificationResult` — **NEU**
```csharp
public sealed record ClassificationResult(
    string ConsoleKey,
    FileCategory Category,
    DecisionClass DecisionClass,
    string ClassificationReasonCode,
    int ClassificationConfidence,
    int DetectionConfidence,
    bool DetectionConflict,
    ConflictType DetectionConflictType,
    bool HasHardEvidence,
    PlatformFamily PlatformFamily,
    EvidenceTier EvidenceTier);
```
**Zweck:** Entkoppelt das Klassifikationsergebnis von der vollen RomCandidate-Erzeugung. Erlaubt isoliertes Testen der Classification-Pipeline.

##### `GameIdentity` — **NEU**
```csharp
public sealed record GameIdentity(
    string GameKey,
    string Region,
    int RegionScore);
```
**Zweck:** Kapselt die deterministische GameKey-Normalisierung + Region-Erkennung. Wird von `GameIdentityResolver` (Core) erzeugt und in CandidateFactory konsumiert.

##### `ScoringResult` — **NEU**
```csharp
public sealed record ScoringResult(
    int FormatScore,
    long VersionScore,
    int HeaderScore,
    int CompletenessScore,
    long SizeTieBreakScore);
```
**Zweck:** Bündelt alle Score-Dimensionen in ein getestetes Objekt. Eliminiert lose Parameter-Listen.

##### `DatMatchResult` — **NEU**
```csharp
public sealed record DatMatchResult(
    bool IsMatch,
    string? DatGameName,
    string? DatRomFileName,
    DatAuditStatus AuditStatus,
    MatchKind PrimaryMatchKind,
    MatchEvidence Evidence);
```
**Zweck:** Formalisiert das DAT-Lookup-Ergebnis. Wird in EnrichmentPhase und DatAuditPhase konsumiert. Verhindert, dass zwei Stellen DAT-Matching parallel interpretieren.

##### `RomCandidate` — vorhanden, stabil
Record bleibt wie gehabt (~30 Felder). Änderung: Factory akzeptiert `ClassificationResult`, `GameIdentity`, `ScoringResult`, `DatMatchResult` statt loser Parameter.

##### `DedupeGroup` — vorhanden, stabil
```
Winner, Losers[], GameKey, CrossGroupFilteredCount
```

##### `WinnerDecision` — **NEU**
```csharp
public sealed record WinnerDecision(
    RomCandidate Winner,
    IReadOnlyList<RomCandidate> Losers,
    string GroupKey,
    string DecisionReason);
```
**Zweck:** Macht die Winner-Entscheidung transparent und auditierbar. `DecisionReason` dokumentiert den Tiebreak-Pfad (z.B. "RegionScore > FormatScore > MainPath").

##### `SortingDecision` — **NEU**
```csharp
public sealed record SortingDecision(
    string FilePath,
    string ConsoleKey,
    SortDecision Decision,
    string? TargetDirectory,
    string? Reason);
```
**Zweck:** Formalisiert die Sort-Entscheidung pro Kandidat. Erlaubt Preview von Sort-Aktionen ohne I/O.

##### `ConversionResult` — vorhanden, stabil
```
SourcePath, TargetPath?, Outcome, Plan?, SourceIntegrity, Safety,
VerificationResult, DurationMs, SourceBytes?, TargetBytes?, AdditionalTargetPaths
```

##### `MovePhaseResult` — vorhanden, stabil
```
MoveCount, FailCount, SavedBytes, SkipCount, MovedSourcePaths?
```

##### `RestoreResult` — **NEU** (formalisiert AuditRollbackResult)
```csharp
public sealed record RestoreResult(
    int RestoredCount,
    int FailedCount,
    int SkippedCount,
    IReadOnlyList<PathMutation> RestoredPaths,
    IReadOnlyList<string> Errors);
```
**Zweck:** Typisierte Rückgabe für Rollback-Operationen. Ersetzt die bisherige `AuditRollbackResult` mit string-basierter Fehlerbehandlung.

##### `RunResult` — vorhanden, **Refactor-Ziel**
Aktuell ~40 flache Properties. Ziel: Strukturierung in Sub-Records:

```csharp
public sealed class RunResult
{
    public string Status { get; init; }
    public int ExitCode { get; init; }
    public RunMode Mode { get; init; }              // NEU: typisiert statt string
    public long DurationMs { get; init; }
    public string? ReportPath { get; init; }

    // Phasen-Ergebnisse als Sub-Records
    public OperationResult? Preflight { get; init; }
    public ScanPhaseResult Scan { get; init; }       // NEU: kapselt TotalFilesScanned + AllCandidates
    public DedupePhaseResult Dedupe { get; init; }   // NEU: kapselt Groups, WinnerCount, LoserCount
    public MovePhaseResult? Move { get; init; }
    public MovePhaseResult? JunkMove { get; init; }
    public ConversionPhaseResult? Conversion { get; init; }  // NEU: kapselt alle Convert-Felder
    public DatPhaseResult? Dat { get; init; }        // NEU: kapselt alle Dat-Felder
    public ConsoleSortResult? Sort { get; init; }
    public PhaseMetricsResult? PhaseMetrics { get; init; }
}
```

Neue Sub-Records:

```csharp
public sealed record ScanPhaseResult(
    int TotalFilesScanned,
    int FilteredNonGameCount,
    IReadOnlyList<RomCandidate> AllCandidates);

public sealed record DedupePhaseResult(
    int GroupCount,
    int WinnerCount,
    int LoserCount,
    int UnknownCount,
    IReadOnlyDictionary<string, int> UnknownReasonCounts,
    IReadOnlyList<DedupeGroup> DedupeGroups);

public sealed record ConversionPhaseResult(
    int ConvertedCount,
    int ErrorCount,
    int SkippedCount,
    int BlockedCount,
    int ReviewCount,
    int LossyWarningCount,
    int VerifyPassedCount,
    int VerifyFailedCount,
    long SavedBytes,
    ConversionReport? Report);

public sealed record DatPhaseResult(
    DatAuditResult? AuditResult,
    int HaveCount,
    int HaveWrongNameCount,
    int MissCount,
    int UnknownCount,
    int AmbiguousCount,
    int RenameProposedCount,
    int RenameExecutedCount,
    int RenameSkippedCount,
    int RenameFailedCount,
    IReadOnlyList<PathMutation> RenamePathMutations);
```

##### `RunProjection` — vorhanden, bleibt
Channel-neutrales Aggregat mit 44 numerischen Feldern. Wird von `RunProjectionFactory.Create(RunResult)` erzeugt. **Einzige Quelle** für KPI-Werte in allen Entry Points.

##### `DashboardProjection` → **Verschieben nach Infrastructure**
Aktuell in `Romulus.UI.Wpf/Models/`. Muss nach `Romulus.Infrastructure/Orchestration/` oder als Contracts-Model, weil:
- API-Dashboard (`DashboardDataBuilder`) baut parallel die gleichen Zahlen
- CLI-Output braucht dieselben formatierten Werte
- Architekturverletzung: Entry Point hängt an Infrastructure-Typen

### 2.2 Core-Schicht — Fachliche Services

| Service | Typ | Zustand | Verantwortung |
|---------|-----|---------|---------------|
| `GameKeyNormalizer` | static | vorhanden | Normalisierung Dateiname → GameKey |
| `GameIdentityResolver` | **NEU static** | – | Erzeugt `GameIdentity` (Key + Region + Score) aus Pfad + RegionRules |
| `RegionDetector` | static | vorhanden | Region-Erkennung aus Filename-Tags |
| `FormatScorer` | static | vorhanden | Format-Score nach Extension |
| `VersionScorer` | sealed class | vorhanden | Versions-Score nach Tags |
| `CompletenessScorer` | static | vorhanden | Completeness-Score aus Extension + Zubehördateien |
| `HealthScorer` | static | vorhanden | Health-Score aus RunProjection |
| `DeduplicationEngine` | static | vorhanden | Grouping + Winner-Selection + DedupeGroup-Erzeugung |
| `FileClassifier` | static | vorhanden | FileCategory-Entscheidung |
| `DecisionResolver` | static | vorhanden | DAT-first Recognition Escalation |
| `ConsoleDetector` | static | vorhanden | Konsolen-Erkennung mit Hypothesen-Auflösung |
| `HypothesisResolver` | static | vorhanden | Konfliktauflösung zwischen Detection-Hypothesen |
| `CandidateFactory` | static | vorhanden → **Refactor** | Erzeugt `RomCandidate`. **Ziel:** Akzeptiert Sub-Records statt 20+ Parameter |
| `ConversionPlanner` | sealed | vorhanden | Graph-basierte Conversion-Planung |
| `ConversionPolicyEvaluator` | sealed | vorhanden | Policy-Entscheidung für Conversion |
| `ConversionConditionEvaluator` | sealed | vorhanden | Condition-Checks für Conversion |
| `SourceIntegrityClassifier` | static | vorhanden | Source-Integritäts-Bewertung |
| `DatAuditClassifier` | static | vorhanden | DAT-Audit-Entscheidungen |
| `DatRenamePolicy` | — | vorhanden | DAT-Rename-Regeln |
| `RuleEngine` | static | vorhanden | Regel-Auswertung aus rules.json |

**Zentralregel:** Alle Services in Core bleiben **deterministisch und I/O-frei**. Keine Änderung an dieser Garantie.

### 2.3 Infrastructure-Schicht — Orchestration & Adapter

#### Pipeline-Phasen (IPipelinePhase<TIn, TOut>)

| Phase | Input | Output | Schicht |
|-------|-------|--------|---------|
| `ScanPipelinePhase` | `RunOptions` | `List<ScannedFileEntry>` | Infrastructure |
| `EnrichmentPipelinePhase` | `EnrichmentPhaseInput` | `List<RomCandidate>` | Infrastructure |
| `DeduplicatePipelinePhase` | `List<RomCandidate>` | `DedupePhaseResult` | Infrastructure |
| `JunkRemovalPipelinePhase` | Gruppen + Optionen | `MovePhaseResult` | Infrastructure |
| `DatAuditPipelinePhase` | Kandidaten + DatIndex | `DatAuditResult` | Infrastructure |
| `DatRenamePipelinePhase` | DatAudit + Optionen | `DatRenameResult` | Infrastructure |
| `MovePipelinePhase` | Gruppen + Optionen | `MovePhaseResult` | Infrastructure |
| `ConsoleSortStep` | Enriched-Dicts | `ConsoleSortResult` | Infrastructure |
| `WinnerConversionPipelinePhase` | Winner + Converter | `ConversionPhaseResult` | Infrastructure |
| `ConvertOnlyPipelinePhase` | All Candidates + Converter | `ConversionPhaseResult` | Infrastructure |

#### Orchestration-Services

| Service | Verantwortung |
|---------|---------------|
| `RunOrchestrator` | Phasen-Koordination, Cancellation, Error-Handling, RunResult-Aufbau |
| `RunResultBuilder` | Mutable Builder → Frozen `RunResult` via `Build()` |
| `RunEnvironmentBuilder` | Console-Mapping, DAT-Bridging, Tool-Init |
| `RunProjectionFactory` | `RunResult` → `RunProjection` (single source of truth für KPIs) |
| `RunOptionsFactory` | Settings → `RunOptions` |
| `PhasePlanBuilder` | Konfiguriert Phase-Reihenfolge basierend auf RunMode |

#### Adapter

| Adapter | Interface | Verantwortung |
|---------|-----------|---------------|
| `FileSystemAdapter` | `IFileSystem` | Dateisystem-Operationen mit Safety-Checks |
| `AuditCsvStore` | `IAuditStore` | Write-Ahead Audit-Trail, Rollback |
| `FileHashService` | — | SHA1/MD5/CRC32 Hashing |
| `HeaderlessHasher` | `IHeaderlessHasher` | Header-stripped Hashing |
| `ToolRunnerAdapter` | `IToolRunner` | Externe Tool-Ausführung |
| `FormatConverterAdapter` | `IFormatConverter` | Format-Konvertierung über Tools |
| `ConversionExecutor` | `IConversionExecutor` | Orchestriert einzelne Conversions |
| `SafetyValidator` | — | Pfad-Validierung, Root-Policy |
| `RollbackService` | — (→ **Ziel: Interface**) | Audit-basierter Rollback |
| `ConsoleSorter` | — (→ **Ziel: Interface**) | Konsolen-basiertes Filesystem-Sorting |

---

## 3. Fachlicher Datenfluss

```
┌─────────────────────────────────────────────────────────────────────┐
│                        RunOptions + RunMode                         │
│  (Roots, Extensions, RegionPriority, Flags, ConvertOnly, DryRun)   │
└────────────┬────────────────────────────────────────────────────────┘
             │
             ▼
┌────────────────────────────────────┐
│   Phase 0: Preflight               │
│   → OperationResult (go / blocked) │
└────────────┬───────────────────────┘
             │
             ▼
┌────────────────────────────────────┐
│   Phase 1: Scan                    │
│   RunOptions → List<ScannedFileEntry>                               │
│   • FileSystem-Enumeration, Extension-Filter                        │
│   • Deterministische Sortierung nach Pfad                           │
│   OUTPUT: ScanPhaseResult                                           │
└────────────┬───────────────────────┘
             │
             ▼
┌────────────────────────────────────────────────────────────────────┐
│   Phase 2: Enrichment                                              │
│   ScannedFileEntry[] → RomCandidate[]                              │
│                                                                     │
│   Pro Datei (parallelisierbar):                                    │
│   ┌─────────────────────────────────────────┐                      │
│   │ ConsoleDetector.DetectWithConfidence()   │→ ClassificationResult│
│   │ GameKeyNormalizer.Normalize()            │                      │
│   │ RegionDetector.Detect()                  │→ GameIdentity        │
│   │ FormatScorer + VersionScorer + etc.      │→ ScoringResult       │
│   │ FileHashService (SHA1/CRC32)             │                      │
│   │ DatIndex.Lookup()                        │→ DatMatchResult      │
│   │ FileClassifier → DecisionResolver        │                      │
│   │ CandidateFactory.Create(                 │                      │
│   │   classification, identity, scoring,     │                      │
│   │   datMatch, rawEntry)                    │→ RomCandidate        │
│   └─────────────────────────────────────────┘                      │
│                                                                     │
│   OUTPUT: List<RomCandidate>                                        │
└────────────┬───────────────────────────────────────────────────────┘
             │
             ├─── If RunMode == ConvertOnly ──────────────┐
             │                                             ▼
             │                            ┌───────────────────────────┐
             │                            │ ConvertOnlyPipelinePhase  │
             │                            │ → ConversionPhaseResult   │
             │                            └──────────┬────────────────┘
             │                                       │
             │                                       ▼ → RunResult
             │
             ▼ (Standard-Pipeline)
┌────────────────────────────────────┐
│   Phase 3: DAT Audit (optional)    │
│   Candidates + DatIndex            │
│   → DatAuditResult                 │
│   → DatPhaseResult                 │
└────────────┬───────────────────────┘
             │
             ▼
┌────────────────────────────────────────────────────────┐
│   Phase 4: Deduplication                                │
│   List<RomCandidate> → DedupePhaseResult               │
│                                                         │
│   Core-Logik (DeduplicationEngine):                    │
│   1. GroupBy: ConsoleKey||GameKey (composite)           │
│   2. SelectWinner: deterministische Multi-Criteria-Sort │
│      Completeness > DatMatch > RegionScore >            │
│      HeaderScore > VersionScore > FormatScore >         │
│      SizeTieBreak > MainPath                            │
│   3. → DedupeGroup[] (Winner + Losers)                  │
│                                                         │
│   OUTPUT: DedupePhaseResult (Groups, GameGroups, Counts)│
└────────────┬───────────────────────────────────────────┘
             │
             ▼
┌────────────────────────────────────┐
│   Phase 5: Junk Removal            │
│   JunkCandidates → MovePhaseResult │
│   (Junk → Trash mit Audit-Trail)   │
└────────────┬───────────────────────┘
             │
             ▼
┌────────────────────────────────────┐
│   Phase 6: DAT Rename (optional)   │
│   DatAudit-Ergebnisse → Renames    │
│   → PathMutation[], Counts         │
└────────────┬───────────────────────┘
             │
             ▼
┌────────────────────────────────────────────────────────────────┐
│   Phase 7: Move                                                 │
│   DedupeGroups (Losers) → MovePhaseResult                      │
│                                                                  │
│   1. WRITE-AHEAD: AuditStore.Append(MOVE_PENDING)               │
│   2. FileSystem.Move(loser → trash)                             │
│   3. AuditStore.Append(MOVE_SUCCESS/MOVE_FAILED)                │
│                                                                  │
│   DryRun: Skip I/O, aber gleiche fachliche Entscheidungen       │
│   OUTPUT: MovePhaseResult (counts, moved paths)                  │
└────────────┬───────────────────────────────────────────────────┘
             │
             ▼
┌────────────────────────────────────┐
│   Phase 8: Console Sort            │
│   Winners → ConsoleSortResult      │
│   (Dateien → Console-Verzeichnisse)│
└────────────┬───────────────────────┘
             │
             ▼
┌────────────────────────────────────┐
│   Phase 9: Winner Conversion       │
│   Winners → ConversionPhaseResult  │
│   (Format-Konvertierung mit Verify)│
└────────────┬───────────────────────┘
             │
             ▼
┌────────────────────────────────────────────────┐
│   Finalize                                      │
│   RunResultBuilder.Build() → RunResult          │
│   RunProjectionFactory.Create() → RunProjection │
│   RunReportWriter.Write()                       │
│   AuditStore.Seal()                             │
└─────────────────────────────────────────────────┘
```

### Undo/Restore-Fluss (separat)

```
Trigger: User wählt Audit-Datei → Rollback
    │
    ▼
RollbackService.Execute(auditPath, roots)
    ├─ Sidecar-Integrität verifizieren
    ├─ CSV lesen, Zeilen rückwärts
    ├─ Root-Validierung (Safety)
    ├─ FileSystem.Move(trash → original)
    └─ → RestoreResult (counts, errors, restored paths)
```

---

## 4. Schichtentrennung

### Was wohin gehört

| Fachliche Funktion | Schicht | Konkrete Klasse/Record |
|--------------------|---------|------------------------|
| GameKey-Normalisierung | **Core** | `GameKeyNormalizer` |
| Region-Erkennung | **Core** | `RegionDetector` |
| GameIdentity-Erzeugung (Key+Region+Score) | **Core** | `GameIdentityResolver` (**NEU**) |
| Konsolen-Erkennung | **Core** | `ConsoleDetector` |
| Hypothesen-Auflösung | **Core** | `HypothesisResolver` |
| File-Klassifikation | **Core** | `FileClassifier`, `DecisionResolver` |
| Scoring (Format, Version, Header, Completeness) | **Core** | `FormatScorer`, `VersionScorer`, etc. |
| Winner-Selection | **Core** | `DeduplicationEngine.SelectWinner` |
| Grouping (ConsoleKey‖GameKey) | **Core** | `DeduplicationEngine.Deduplicate` |
| Conversion-Planung | **Core** | `ConversionPlanner`, `ConversionPolicyEvaluator` |
| DAT-Audit-Klassifikation | **Core** | `DatAuditClassifier` |
| DAT-Rename-Policy | **Core** | `DatRenamePolicy` |
| Set-Parsing (CUE/CCD/GDI/MDS/M3U) | **Core** | `*SetParser` |
| Regel-Auswertung | **Core** | `RuleEngine` |
| | | |
| Datei-Scan (Enumeration) | **Infrastructure** | `ScanPipelinePhase` |
| Enrichment (Orchestrierung der Core-Logik) | **Infrastructure** | `EnrichmentPipelinePhase` |
| Hashing (SHA1/MD5/CRC32) | **Infrastructure** | `FileHashService`, `ArchiveHashService` |
| DAT-Index-Aufbau | **Infrastructure** | `RunEnvironmentBuilder` |
| DAT-Index-Lookup | **Infrastructure** | `DatIndex` (injiziert in Core via Interface) |
| Dateisystem-Operationen | **Infrastructure** | `FileSystemAdapter` |
| Tool-Ausführung | **Infrastructure** | `ToolRunnerAdapter` |
| Audit-Trail | **Infrastructure** | `AuditCsvStore` |
| Rollback | **Infrastructure** | `RollbackService` |
| Report-Erzeugung | **Infrastructure** | `RunReportWriter`, `ReportGenerator` |
| Phasen-Koordination | **Infrastructure** | `RunOrchestrator` |
| RunResult-Aufbau | **Infrastructure** | `RunResultBuilder` |
| KPI-Projektion | **Infrastructure** | `RunProjectionFactory` |
| Dashboard-Projektion | **Infrastructure** (→ verschieben) | `DashboardProjection` |
| | | |
| ViewModel-Binding | **GUI** | `MainViewModel`, `RunViewModel` |
| CLI-Argument-Parsing | **CLI** | `CliArgsParser` |
| CLI-Output-Formatierung | **CLI** | `CliOutputWriter` |
| API-Request/Response-Mapping | **API** | `ApiRunResultMapper` |
| API-Lifecycle | **API** | `RunManager`, `RunLifecycleManager` |

### Was NIE in GUI / CLI / API liegen darf

| Verbotene Logik | Grund |
|-----------------|-------|
| GameKey-Normalisierung | Core-Determinismus |
| Region-Scoring | Core-Determinismus |
| Winner-Selection/-Logik | Core-Determinismus |
| Dedupe-Grouping | Core-Determinismus |
| Classification-Entscheidungen | Core-Determinismus |
| Score-Berechnung jeder Art | Core-Determinismus |
| DatMatch-Interpretation | Zentrale Wahrheit |
| SortDecision-Routing | Zentrale Wahrheit |
| MovePhaseResult-Herleitung | Phasenvertrag |
| KPI-Aggregation (HealthScore, DedupeRate, FailCount) | `RunProjectionFactory` |
| RunResult-Interpretation (Success/Fail-Logik) | `RunProjectionFactory` |

### Erlaubte Logik in Entry Points

| Erlaubte Logik | Beispiel |
|----------------|----------|
| Formatierung von Zahlen → Strings | `"42%" `, `"3,2 GB"` |
| Lokalisierung von Labels | i18n-Keys → Display-Strings |
| UI-State-Management (RunState-Machine) | Idle → Scanning → Completed |
| Conditional Visibility | `HasRunResult`, `IsBusy` |
| Progress-Updates | `ProgressProjection` aus Phase-Callbacks |
| User-Input-Validierung | Pfade, Extensions, Settings-Plausibilität |
| Mapping: ViewModel → RunOptions | `CliOptionsMapper`, `ApiRunConfigurationMapper` |

---

## 5. Zu entfernende Altlogik

### 5.1 Harte Kandidaten

| Was | Wo | Problem | Aktion |
|-----|----|---------|--------|
| String-basierter `RunOptions.Mode` | `RunExecutionModels.cs` | Kein Compile-time Safety; DryRun/Execute/ConvertOnly als Strings | **Ersetzen durch `RunMode` Enum** |
| `DashboardProjection` in WPF | `UI.Wpf/Models/` | Architekturverletzung; API-Dashboard baut parallel gleiche Daten | **Nach Infrastructure verschieben** |
| CandidateFactory mit 20+ Parametern | `Core/Classification/` | Fragil, schwer testbar, keine Gruppierung | **Refactor auf Sub-Records** |
| `RunViewModel` Default-Werte `"0"` | `UI.Wpf/ViewModels/` | Inkonsistent mit `ResetDashboardForNewRun()` → `"–"` | **Konsistente Defaults `"–"`** |
| `DashboardProjection.MarkProvisional` + `MarkPlan` stacken | `UI.Wpf/Models/` | `"3 (vorläufig) (Plan)"` — Double-Marking | **Mutual-exclusive Marker-Logik** |

### 5.2 Weiche Kandidaten (bei Gelegenheit)

| Was | Wo | Problem | Aktion |
|-----|----|---------|--------|
| CLI ohne DI-Container | `CLI/Program.cs` | Manuelle Objekt-Erzeugung, schwer testbar | **Optionaler `ServiceCollection`-Aufbau** |
| `RollbackService` als static | `Infrastructure/Audit/` | Nicht mockbar in Tests | **Interface + DI** |
| `ConsoleSorter` ohne Interface | `Infrastructure/Sorting/` | Nicht mockbar | **Interface + DI** |
| `EnrichmentPhaseInput` all-optional | `Infrastructure/Orchestration/` | Stille Degradation ohne Warnung | **Validation-Contract + Logging** |
| `RunResult` ~40 flache Properties | `Contracts/Models/` | Unstrukturiert, schwer erweiterbar | **Sub-Records (ScanPhaseResult, DedupePhaseResult, etc.)** |
| Mehrere static Analysis-Services | `Infrastructure/Analysis/` | Schwer testbar, keine DI-Integration | **Prüfen ob Interface sinnvoll** |

### 5.3 Nicht anfassen (stabil und korrekt)

| Was | Grund |
|-----|-------|
| `DeduplicationEngine.SelectWinner` | Deterministisch, gut getestet, klar strukturiert |
| `GameKeyNormalizer.Normalize` | Stabil, umfangreiche Test-Abdeckung |
| `RegionDetector.Detect` | Stabil |
| Write-Ahead Audit-Pattern | Sicherheitskritisch, korrekt implementiert |
| `RomCandidate` Record-Struktur | Stabil, immutable, gut genutzt |
| `IPipelinePhase<TIn, TOut>` Interface | Saubere Abstraktion |
| `RunProjectionFactory.Create` | Single Source of Truth für KPIs |
| `PhasePlanBuilder` | Flexible Phase-Konfiguration |

---

## 6. Migrationshinweise

### Priorität 1 — Sofort umsetzbar, hoher Nutzen

#### 6.1 `RunMode` Enum einführen

**Aufwand:** Klein
**Risiko:** Gering (rein additiv, dann sukzessive ersetzen)

```csharp
// Contracts/Models/RunMode.cs
public enum RunMode { DryRun, Execute, ConvertOnly }
```

1. Enum in Contracts anlegen
2. `RunOptions` um `RunMode Mode` Property erweitern (init-only)
3. `RunOptionsBuilder` / `RunOptionsFactory` befüllen
4. Sukzessive String-Checks (`== "DryRun"`) durch Enum-Checks ersetzen
5. Alte String-Property als `[Obsolete]` markieren, später entfernen

#### 6.2 `DashboardProjection` nach Infrastructure verschieben

**Aufwand:** Klein
**Risiko:** Gering (nur Namespace-Änderung + using-Updates)

1. `DashboardProjection.cs` nach `Infrastructure/Orchestration/` verschieben
2. Namespace auf `Romulus.Infrastructure.Orchestration` ändern
3. Using-Statements in WPF updaten
4. API-`DashboardDataBuilder` kann jetzt `DashboardProjection.From()` nutzen statt eigene Berechnung

#### 6.3 `RunViewModel` Default-Werte korrigieren

**Aufwand:** Trivial
**Risiko:** Gering

1. Alle `DashWinners = "0"` etc. → `"–"` ändern
2. Sicherstellen dass `ResetDashboardForNewRun()` und Konstruktor konsistent sind

### Priorität 2 — Mittelfristiger Refactor

#### 6.4 `CandidateFactory` auf Sub-Records umstellen

**Aufwand:** Mittel
**Risiko:** Mittel (viele Aufrufstellen in Tests)

1. `ClassificationResult`, `GameIdentity`, `ScoringResult`, `DatMatchResult` als Records anlegen
2. Neue `CandidateFactory.Create(ScannedFileEntry, ClassificationResult, GameIdentity, ScoringResult, DatMatchResult)` Überladung
3. `EnrichmentPipelinePhase.MapToCandidate()` umstellen
4. Alte Überladung als `[Obsolete]` markieren
5. Tests sukzessive migrieren

#### 6.5 `RunResult` strukturieren

**Aufwand:** Mittel
**Risiko:** Mittel (alle Konsumenten müssen angepasst werden)

1. Sub-Records (`ScanPhaseResult`, `DedupePhaseResult`, `ConversionPhaseResult`, `DatPhaseResult`) anlegen
2. `RunResultBuilder` um Sub-Builder erweitern
3. `RunProjectionFactory.Create()` auf Sub-Records umstellen
4. Entry Points sukzessive auf strukturierten Zugriff umstellen
5. Alte flache Properties als `[Obsolete]` markieren

#### 6.6 `WinnerDecision` einführen

**Aufwand:** Klein
**Risiko:** Gering (additiv)

1. `WinnerDecision` Record anlegen
2. `DeduplicationEngine.SelectWinner` um `DecisionReason` erweitern
3. `DedupeGroup` um optionales `WinnerDecision? Decision` Property erweitern
4. Reports und Preview nutzen `DecisionReason` für Transparenz

### Priorität 3 — Langfristige Hygiene

#### 6.7 CLI DI-Container

**Aufwand:** Mittel
**Risiko:** Gering

1. `Microsoft.Extensions.DependencyInjection` hinzufügen
2. `ServiceCollection` + `AddRomulusCore()` in CLI-Main aufrufen
3. Manuelle Objekt-Erzeugung eliminieren
4. Test-Isolation verbessern

#### 6.8 Static Services → Interface + DI

Nur für Services, die in Tests gemockt werden müssen:
- `RollbackService` → `IRollbackService`
- `ConsoleSorter` → `IConsoleSorter`
- Analysis-Services → selektiv evaluieren

#### 6.9 `EnrichmentPhaseInput` Validierungs-Contract

1. `EnrichmentPhaseInput.Validate()` Methode oder Builder
2. Logging bei degradierter Enrichment-Konfiguration
3. Kein stilles Fallback ohne Warnung

---

## Anhang: Invarianten-Matrix

Diese Invarianten müssen bei jeder Migration bestehen bleiben:

| Invariante | Verantwortung | Absicherung |
|------------|---------------|-------------|
| GameKey(a) == GameKey(b) bei identischem Input | `GameKeyNormalizer` (Core) | Unit-Tests |
| Region(a) == Region(b) bei identischem Filename | `RegionDetector` (Core) | Unit-Tests |
| Winner(group) deterministisch bei gleichen Kandidaten | `DeduplicationEngine` (Core) | Invarianten-Tests |
| Preview zeigt gleiche Entscheidungen wie Execute | `RunOrchestrator` + RunMode-Check | Integration-Tests |
| Kein Move ausserhalb erlaubter Roots | `AllowedRootPathPolicy` (Infrastructure) | Security-Tests |
| KPIs in GUI == CLI == API | `RunProjectionFactory` (Infrastructure) | Parität-Tests |
| Audit-Trail vollständig bei Crash | Write-Ahead Pattern | Integration-Tests |
| Rollback restauriert exakt den Pre-Move-Zustand | `RollbackService` + `AuditCsvStore` | Roundtrip-Tests |
| Empty GameKey → excluded from grouping | `DeduplicationEngine` | Edge-Tests |
| BIOS-Isolation via `__BIOS__` Key-Prefix | `CandidateFactory` (Core) | Unit-Tests |
| Composite GroupKey = `ConsoleKey||GameKey` | `DeduplicationEngine` (Core) | Unit-Tests |
| DatMatch-Ergebnis identisch in Enrichment und DatAudit | `DatMatchResult` zentral | Integration-Tests |
| ConvertOnly überspringt Dedupe/Move/Sort vollständig | `RunOrchestrator` + `PhasePlanBuilder` | Mode-Tests |
| Cancelled Run → partial results + audit sidecar | `RunOrchestrator` | Cancellation-Tests |
