# ADR-0013: Zielarchitektur Kernfunktionen – Konsolidierte Referenz

> **Status:** Proposed  
> **Datum:** 2026-03-20  
> **Scope:** Scan, Classification, GameKey, Grouping, Winner Selection, Sorting, DAT, Conversion, Move/Trash/Restore/Undo, Orchestrator, Shared Logic  
> **Vorgänger:** ADR-005, ADR-007, ADR-010, ADR-012  
> **Zweck:** Einziges maßgebliches Dokument für die Zielarchitektur der 11 Kernfunktionen. Ersetzt keine ADRs, sondern konsolidiert ihre Zielbilder in eine implementierbare Referenz.

---

## 1. Executive Design

### 1.1 Hauptidee

**Fünf Verarbeitungsstufen, streng sequentiell, mit typisierten Zwischenergebnissen:**

```
Raw I/O  →  Enrichment  →  Decision  →  Action  →  Evidence
(Scan)      (Classify,      (Group,      (Move,      (Audit,
             Score,          Select,      Convert,    Report,
             Match)          Sort)        Trash)      Sidecar)
```

Jede Stufe produziert ein **immutables Ergebnis-Objekt**, das die nächste Stufe als Input konsumiert. Kein Rückwärtsfluss, keine Mutation, keine übergreifende Zustandsänderung.

### 1.2 Wichtigste Architekturentscheidungen

| # | Entscheidung | Begründung |
|---|---|---|
| **D-01** | Immutable `RomCandidate` als zentrales Fachmodell (sealed record) | Eliminiert Mutations-Bugs. Einmal erzeugt = Identity fixiert. |
| **D-02** | `RunResultBuilder` → `RunResult` (immutable nach `.Build()`) | Builder akkumuliert pro Phase; finales Record enthält keine Setter. |
| **D-03** | `RunProjection` als **einzige** Quelle für abgeleitete KPIs | Alle Consumer (GUI/CLI/API/Report) lesen ausschließlich von `RunProjection`. Keine eigene Zählung. |
| **D-04** | Category-aware Winner Selection (GAME > BIOS > JUNK > UNKNOWN) | SelectWinner filtert zuerst auf beste Kategorie, dann Multi-Kriterien-Sort innerhalb. |
| **D-05** | Phase-Handler-Pattern mit typisierten Ein-/Ausgaben | Jede Phase = `IPhaseStep` mit eigenem Result-Typ. Orchestrator hat null Fachlogik. |
| **D-06** | Audit-Trail als zwingender Querschnitt | Jede physische Dateibewegung erzeugt einen Audit-Record. Kein Move ohne Audit. |
| **D-07** | Getrennte Junk- und Dedupe-MoveResults innerhalb von `RunResult` | Nie addiert, nie vermischt. Separate Zähler, separate Audit-Actions. |
| **D-08** | Single `RunOptionsFactory` für alle Entry Points | Eine Factory, drei Adapter (`IRunOptionsSource`). Eliminiert Option-Drift. |
| **D-09** | `IRunEnvironment` als Interface statt konkrete Typen | Testbar via Mock. Keine Leaks von `FileSystemAdapter`/`AuditCsvStore` nach oben. |
| **D-10** | `ProjectionFactory` im Core (pure, deterministisch) | KPI-Berechnung ist Domänenlogik. Keine Infrastructure-Abhängigkeit. |

### 1.3 Kritischste Verantwortungstrennungen

| Grenze | Links (darf) | Rechts (darf) | Niemals übertreten |
|---|---|---|---|
| **Pure ↔ I/O** | Core: Scoring, Grouping, Winner | Infrastructure: File-Ops, Tools, XML | Core ruft nie `File.*`, `Directory.*`, `Process.*` auf |
| **Decision ↔ Action** | SelectWinner, SortDecision | MovePhase, ConvertPhase | Decision-Logic verschiebt nie Dateien |
| **Enrichment ↔ Mutation** | CandidateFactory erzeugt | RunResultBuilder baut | Candidates sind nach Konstruktion unveränderlich |
| **Orchestrator ↔ Fachlogik** | Phase-Komposition, Loop | Scoring, Classification, Regex | Orchestrator enthält null Regex, null Scoring, null Kategorisierung |
| **Entry Point ↔ Kernlogik** | DI-Wiring, UI-Darstellung | Alles in Core/Infrastructure | Entry Points berechnen nie eigene KPIs, Scores oder Status |

---

## 2. Zielobjekte und Services

### 2.1 Core-Schicht (Pure, Deterministisch, Kein I/O)

#### `RomCandidate` *(sealed record – Contracts)*

| Aspekt | Spezifikation |
|---|---|
| **Zweck** | Zentrales Fachmodell, repräsentiert eine ROM-Datei mit allen Enrichment-Ergebnissen |
| **Inputs** | MainPath, Extension, SizeBytes, Category, GameKey, Region, alle Scores, DatMatch, ConsoleKey |
| **Outputs** | Immutable Record – einmal erzeugt, nie geändert |
| **Verantwortung** | Datenträger aller Enrichment-Ergebnisse |
| **Darf NICHT** | Scores nachträglich ändern, I/O ausführen, null-GameKey haben |

```csharp
public sealed record RomCandidate(
    string MainPath,
    string Extension,
    long SizeBytes,
    FileCategory Category,
    string GameKey,
    string Region,
    int RegionScore,
    int FormatScore,
    long VersionScore,
    int HeaderScore,
    int CompletenessScore,
    long SizeTieBreakScore,
    bool DatMatch,
    string ConsoleKey,
    int ClassificationConfidence = 100,
    string? ClassificationReasonCode = null);
```

**Invarianten:**
- `MainPath` darf nie null oder leer sein
- `GameKey` darf nie null oder Whitespace sein (SHA256-Fallback für pathologische Fälle)
- `Category` ist `FileCategory` (enum), nie String

---

#### `CandidateFactory` *(static, pure – Core/Classification)*

| Aspekt | Spezifikation |
|---|---|
| **Zweck** | Single Point of Construction für `RomCandidate` |
| **Inputs** | FilePath, SizeBytes, alle Score-Ergebnisse, Category, GameKey, Region, DatMatch, ConsoleKey |
| **Outputs** | `RomCandidate` (immutable) |
| **Verantwortung** | Validierung (kein leerer GameKey, kein null-Path), BIOS-Key-Isolation (`__BIOS__{key}`) |
| **Darf NICHT** | I/O, Hashing, Score-Berechnung |

---

#### `FileClassifier` *(static, pure – Core/Classification)*

| Aspekt | Spezifikation |
|---|---|
| **Zweck** | Klassifiziert ROM-Dateinamen in `FileCategory` |
| **Inputs** | `string baseName`, `bool aggressiveJunk` |
| **Outputs** | `FileCategory` (enum: Game, Bios, NonGame, Junk, Unknown) |
| **Verantwortung** | Regex-basierte Mustererkennung mit ReDoS-Schutz (500ms Timeout) |
| **Darf NICHT** | I/O, Unknown→Game mappen, Dateien lesen |

---

#### `GameKeyNormalizer` *(static, pure – Core/GameKeys)*

| Aspekt | Spezifikation |
|---|---|
| **Zweck** | Erzeugt normalisierten Gruppierungsschlüssel aus ROM-Dateinamen |
| **Inputs** | `string baseName`, Tag-Patterns, Aliases |
| **Outputs** | `string gameKey` (nie null, nie leer – SHA256-Fallback garantiert) |
| **Verantwortung** | ASCII-Fold, Tag-Strip, Alias-Lookup, Artikel-Normalisierung, Disc-Padding, Whitespace-Normalisierung |
| **Darf NICHT** | I/O, Category-Logik, Region-Scoring |

**Invariante:** Identische Inputs → identischer GameKey (deterministisch, LRU-cached mit 50k Einträgen).

---

#### `RegionDetector` *(static, pure – Core/Regions)*

| Aspekt | Spezifikation |
|---|---|
| **Zweck** | Extrahiert Region-Tag aus ROM-Dateinamen |
| **Inputs** | `string fileName` |
| **Outputs** | `string region` (EU, US, JP, WORLD, UNKNOWN, …) |
| **Verantwortung** | Ordered-Rule-Matching (24+ Regeln), Multi-Region-Detection, Token-Parsing |
| **Darf NICHT** | Scoring, I/O |

---

#### `FormatScorer` *(static, pure – Core/Scoring)*

| Aspekt | Spezifikation |
|---|---|
| **Zweck** | Berechnet Format-, Region- und Size-Tiebreak-Scores |
| **Inputs** | Extension, Region + PreferOrder, SizeBytes + DiscType |
| **Outputs** | `int` oder `long` Score-Wert |
| **Verantwortung** | Deterministische numerische Bewertung |
| **Darf NICHT** | I/O, Dateien lesen, Reihenfolge-Abhängigkeit vom Dateisystem |

---

#### `VersionScorer` *(sealed, pure – Core/Scoring)*

| Aspekt | Spezifikation |
|---|---|
| **Zweck** | Berechnet Version/Revision-Score aus ROM-Dateinamen |
| **Inputs** | `string baseName` |
| **Outputs** | `long versionScore` |
| **Verantwortung** | Verified-Dump [!], Revision a–z, Version v1.0, Sprach-Bonus |
| **Darf NICHT** | I/O |

---

#### `CompletenessScorer` *(static, pure – Core/Scoring)*

| Aspekt | Spezifikation |
|---|---|
| **Zweck** | Bewertet Archiv-/Set-Vollständigkeit |
| **Inputs** | `bool datMatch`, `bool isCompleteSet`, `bool isStandalone` |
| **Outputs** | `int completenessScore` |
| **Verantwortung** | DAT-Match → +50, Vollständiges Set → +50, Unvollständig → -50, Standalone → +25 |
| **Darf NICHT** | I/O, Pipeline-Kontext kennen |

**Kritisch:** Diese Logik muss aus `EnrichmentPipelinePhase` in Core/Scoring extrahiert werden. Sie ist Domänenlogik, nicht Infrastructure.

---

#### `DeduplicationEngine` *(static, pure – Core/Deduplication)*

| Aspekt | Spezifikation |
|---|---|
| **Zweck** | Gruppiert Candidates nach GameKey, wählt Winner pro Gruppe |
| **Inputs** | `IReadOnlyList<RomCandidate>` |
| **Outputs** | `IReadOnlyList<DedupeResult>` |
| **Verantwortung** | Grouping, Category-Filter, Multi-Kriterien-Sort, deterministische Winner-Selection |
| **Darf NICHT** | I/O, Dateien bewegen, Scores berechnen (nur vergleichen) |

**Winner-Selection-Kette (exakter Algorithmus):**

```
1. FilterToBestCategory(GAME > BIOS > NonGame > JUNK > UNKNOWN)
2. OrderByDescending(CompletenessScore)
3. ThenByDescending(DatMatch ? 1 : 0)
4. ThenByDescending(RegionScore)
5. ThenByDescending(HeaderScore)
6. ThenByDescending(VersionScore)
7. ThenByDescending(FormatScore)
8. ThenByDescending(SizeTieBreakScore)
9. ThenBy(MainPath, OrdinalIgnoreCase)
10. ThenBy(MainPath, Ordinal)  ← case-sensitiver finaler Tiebreaker
11. .First()
```

**Invarianten:**
- Gleiche Inputs → gleicher Winner (deterministisch, getestet mit 10× Shuffle)
- Kein Winner darf in einer anderen Gruppe Loser sein (Cross-Group-Guard)
- Leere oder Whitespace-GameKeys erzeugen keine Gruppen

---

#### `SortDecision` *(static, pure – Core – NEU)*

| Aspekt | Spezifikation |
|---|---|
| **Zweck** | Bestimmt Zielverzeichnis für eine Datei basierend auf ConsoleKey |
| **Inputs** | `RomCandidate`, `string rootPath`, Console-Maps |
| **Outputs** | `SortTarget { ConsoleKey, TargetDirectory }` oder `null` |
| **Verantwortung** | Rein fachliche Sortierungsentscheidung |
| **Darf NICHT** | Dateien bewegen, I/O |

---

#### `ProjectionFactory` *(static, pure – Core – NEU)*

| Aspekt | Spezifikation |
|---|---|
| **Zweck** | Berechnet abgeleitete KPIs einmalig aus `RunResult` |
| **Inputs** | `RunResult` |
| **Outputs** | `RunProjection` (sealed record, immutable) |
| **Verantwortung** | HealthScore, DedupeRate, TotalErrorCount, GameCount – EINE Berechnung, EIN Ort |
| **Darf NICHT** | I/O, Infrastructure-Abhängigkeiten |

**Regel:** Alle Consumer (GUI, CLI, API, Report) lesen ausschließlich `RunProjection`. Kein Entry Point berechnet eigene Metriken.

---

#### `RuleEngine` *(sealed, pure – Core/Rules)*

| Aspekt | Spezifikation |
|---|---|
| **Zweck** | Evaluiert benutzerdefinierte Klassifikationsregeln |
| **Inputs** | `IReadOnlyList<ClassificationRule>`, Item-Properties |
| **Outputs** | `RuleMatchResult` (First-Match-Wins) |
| **Verantwortung** | Syntax-Validierung, AND-Logik für Conditions, Regex-Cache (max 1024), ReDoS-Timeout (500ms) |
| **Darf NICHT** | I/O, direkte Dateisystem-Zugriffe |

---

### 2.2 Infrastructure-Schicht (I/O, Seiteneffekte, Phasen)

#### `ScanPhase` : `IPhaseStep`

| Aspekt | Spezifikation |
|---|---|
| **Zweck** | Enumeriert Dateien aus Roots, eliminiert doppelte Pfade, erkennt Sets |
| **Inputs** | RunOptions (Roots, Extensions) |
| **Outputs** | `ScanPhaseResult { Files, SetMemberPaths, OverlappingPathsSkipped }` |
| **Verantwortung** | File-Enumeration (streaming via `IAsyncEnumerable`), Path-Normalisierung, Root-Overlap-Dedup, Set-Member-Erkennung, Reparse-Point-Blocking |
| **Darf NICHT** | Dateien verschieben, Scores berechnen, Gruppieren |

---

#### `EnrichmentPhase` : `IPhaseStep`

| Aspekt | Spezifikation |
|---|---|
| **Zweck** | Reichert gescannte Dateien mit Classification, GameKey, Scores, DAT-Match an |
| **Inputs** | `ScanPhaseResult.Files`, Options, Core-Services |
| **Outputs** | `EnrichmentPhaseResult { AllCandidates, ProcessingCandidates }` |
| **Verantwortung** | Ruft Core-Services auf (FileClassifier → GameKeyNormalizer → RegionDetector → Scorer → ConsoleDetector → DatLookup → CandidateFactory), OnlyGames-Filter |
| **Darf NICHT** | Eigene Scoring-Logik, Dateien bewegen, Gruppieren |

---

#### `DedupePhase` : `IPhaseStep`

| Aspekt | Spezifikation |
|---|---|
| **Zweck** | Gruppiert und wählt Winner |
| **Inputs** | `EnrichmentPhaseResult.ProcessingCandidates` |
| **Outputs** | `DedupePhaseResult { AllGroups, GameGroups, LoserCount }` |
| **Verantwortung** | Delegiert an `DeduplicationEngine.Deduplicate()` |
| **Darf NICHT** | I/O, Dateien bewegen, eigene Winner-Logik |

---

#### `JunkRemovalPhase` : `IPhaseStep`

| Aspekt | Spezifikation |
|---|---|
| **Zweck** | Verschiebt standalone Junk-Dateien in Trash |
| **Inputs** | Standalone-Junk aus Enrichment, RunOptions |
| **Outputs** | `JunkPhaseResult { MoveResult, RemovedPaths }` |
| **Verantwortung** | Junk-Only Moves, Audit-Trail pro Move |
| **Darf NICHT** | Dedupe-Losers bewegen, Scores berechnen |

---

#### `DedupeMovePhase` : `IPhaseStep`

| Aspekt | Spezifikation |
|---|---|
| **Zweck** | Verschiebt Dedupe-Losers in Trash |
| **Inputs** | Losers aus DedupePhaseResult, RunOptions |
| **Outputs** | `MovePhaseResult { MoveCount, FailCount, SkipCount }` |
| **Verantwortung** | Loser-Moves, ConflictPolicy, Audit-Trail pro Move |
| **Darf NICHT** | Junk bewegen, Scores berechnen |

---

#### `ConsoleSortPhase` : `IPhaseStep`

| Aspekt | Spezifikation |
|---|---|
| **Zweck** | Sortiert Winners in Konsolen-Unterverzeichnisse |
| **Inputs** | Roots, ConsoleDetector, Extensions |
| **Outputs** | `ConsoleSortResult { Moved, Unknown, FailCount }` |
| **Verantwortung** | Console-Detection, Move, Audit-Trail (bisher fehlend – muss ergänzt werden) |
| **Darf NICHT** | Dedupe-Logik, Scoring |

---

#### `ConvertPhase` : `IPhaseStep`

| Aspekt | Spezifikation |
|---|---|
| **Zweck** | Konvertiert Winner-Dateien in Zielformat |
| **Inputs** | Winners, IFormatConverter, RunOptions |
| **Outputs** | `ConvertPhaseResult { Converted, Errors, Skipped }` |
| **Verantwortung** | Convert → Verify → (Source-zu-Trash NUR bei Verify-Success) → Audit |
| **Darf NICHT** | Scores berechnen, Gruppieren |

**Kritische Regel (P0-A):**
```
if (convert.Success && verify.Success) → converted++, source → trash
if (convert.Success && verify.Fail)    → errors++, source BLEIBT
if (convert.Skipped)                   → skipped++
else                                   → errors++
```
Niemals `converted++` VOR Verify. Niemals Source löschen bei Verify-Failure.

**Invariante:** `Converted + Errors + Skipped == Attempted`

---

#### `ReportPhase` : `IPhaseStep`

| Aspekt | Spezifikation |
|---|---|
| **Zweck** | Generiert HTML/CSV-Reports |
| **Inputs** | `RunResult`, `RunProjection`, Options |
| **Outputs** | `ReportPhaseResult { ReportPath }` |
| **Verantwortung** | Report-Erzeugung mit HTML-Escaping, CSV-Injection-Schutz |
| **Darf NICHT** | Eigene KPI-Berechnung – liest ausschließlich `RunProjection` |

---

#### `AuditSealPhase` : `IPhaseStep`

| Aspekt | Spezifikation |
|---|---|
| **Zweck** | Schließt Audit-Trail ab mit HMAC-signiertem Sidecar |
| **Inputs** | AuditPath, `IAuditStore` |
| **Outputs** | `AuditMetadata` |
| **Verantwortung** | Flush, Sidecar-Schreibung, HMAC-Signatur |
| **Darf NICHT** | Fachentscheidungen treffen |

---

#### `RunOrchestrator` *(Infrastructure/Orchestration)*

| Aspekt | Spezifikation |
|---|---|
| **Zweck** | Führt die Phase-Pipeline aus |
| **Inputs** | `RunOptions`, `IRunEnvironment`, `CancellationToken` |
| **Outputs** | `RunResult` (immutable) |
| **Verantwortung** | Phase-Plan von `IPhasePlanBuilder` holen, foreach Phase execute, `RunResultBuilder.Append()`, Timing, Progress-Callbacks |
| **Darf NICHT** | Scoring, Classification, Regex, Report-Generierung, Enrichment, Audit-Sidecar-Schreibung |

**Ziel: ≤250 LOC.** Aktuell ~800 LOC – Differenz wandert in Phase-Steps.

```csharp
public RunResult Execute(RunOptions options, IRunEnvironment env, CancellationToken ct)
{
    var sw = Stopwatch.StartNew();
    var state = new PipelineState();
    var builder = new RunResultBuilder();
    var plan = _planBuilder.Build(options, env);

    foreach (var step in plan)
    {
        ct.ThrowIfCancellationRequested();
        _onProgress?.Invoke($"[{step.Name}]");
        var result = step.Execute(state, ct);
        builder.Append(step.Name, result);
    }

    builder.DurationMs = sw.ElapsedMilliseconds;
    return builder.Build();
}
```

---

#### `PhasePlanBuilder` : `IPhasePlanBuilder`

| Aspekt | Spezifikation |
|---|---|
| **Zweck** | Erzeugt konditionalen Phase-Plan basierend auf RunOptions |
| **Inputs** | `RunOptions`, `IRunEnvironment` |
| **Outputs** | `IReadOnlyList<IPhaseStep>` |
| **Verantwortung** | ConvertOnly-Branch, Conditional Phases (JunkRemoval nur wenn RemoveJunk, ConsoleSort nur wenn SortConsole, Convert nur wenn ConvertFormat gesetzt) |
| **Darf NICHT** | Phasen ausführen – nur planen |

---

#### `RunOptionsFactory` *(Infrastructure/Orchestration)*

| Aspekt | Spezifikation |
|---|---|
| **Zweck** | Einziger Ort, an dem `RunOptions` aus User-Input gebaut wird |
| **Inputs** | `IRunOptionsSource` (implementiert von CLI/API/WPF-Adapter) |
| **Outputs** | `RunOptions` (immutable) |
| **Verantwortung** | Feld-Mapping, Default-Werte, Validierung |
| **Darf NICHT** | Entry-Point-spezifische Logik |

---

#### `RollbackService` *(Infrastructure/Audit)*

| Aspekt | Spezifikation |
|---|---|
| **Zweck** | Macht physische Dateibewegungen rückgängig anhand des Audit-Trails |
| **Inputs** | Audit-Pfad, erlaubte Roots, DryRun-Flag |
| **Outputs** | `RollbackResult { RestoredCount, FailCount, Warnings }` |
| **Verantwortung** | Audit-CSV lesen, HMAC verifizieren, Reverse-Order-Processing, Sidecar-Pflicht, Reparse-Point-Check vor Move |
| **Darf NICHT** | Ohne `.meta.json`-Sidecar rollbacken, Reparse-Points folgen |

---

### 2.3 Result-Objekte (alle immutable nach Konstruktion)

| Record | Schicht | Zweck | Sender | Consumer |
|---|---|---|---|---|
| `ScanPhaseResult` | Contracts | Scan-Output | `ScanPhase` | `EnrichmentPhase` |
| `EnrichmentPhaseResult` | Contracts | Angereicherte Candidates | `EnrichmentPhase` | `DedupePhase`, `JunkRemovalPhase` |
| `DedupePhaseResult` | Contracts | Gruppen + Winner/Losers | `DedupePhase` | `DedupeMovePhase`, Report |
| `JunkPhaseResult` | Contracts | Junk-Move-Ergebnisse | `JunkRemovalPhase` | Report |
| `MovePhaseResult` | Contracts | Dedupe-Move-Ergebnisse | `DedupeMovePhase` | Report |
| `ConsoleSortResult` | Contracts | Sort-Ergebnisse | `ConsoleSortPhase` | Report |
| `ConvertPhaseResult` | Contracts | Konvertierungs-Ergebnisse | `ConvertPhase` | Report |
| `RunResult` | Contracts | Gesamtergebnis (alle Phasen) | `RunResultBuilder.Build()` | `ProjectionFactory` |
| `RunProjection` | Contracts | Abgeleitete KPIs | `ProjectionFactory` | GUI, CLI, API, Report |
| `RollbackResult` | Contracts | Undo-Ergebnisse | `RollbackService` | GUI, CLI, API |

---

### 2.4 Shared Logic für GUI / CLI / API

Die folgenden Komponenten sind **Entry-Point-neutral** und werden von allen drei Frontends identisch genutzt:

```
┌─────────────────────────────────────────────────────┐
│  Shared (Infrastructure, via DI)                     │
│                                                      │
│  RunOptionsFactory     ← baut RunOptions             │
│  RunEnvironmentFactory ← baut RunEnvironment         │
│  RunOrchestrator       ← führt Pipeline aus          │
│  ProjectionFactory     ← leitet KPIs ab (Core)       │
│  RollbackService       ← Undo                        │
│  SettingsLoader        ← User-Settings laden/sichern │
│  SharedServiceRegistration.AddRomulusCore()       │
│    ← DI-Registrierung aller Services                 │
└─────────────────────────────────────────────────────┘
```

**Entry Points registrieren NUR ihre eigene Shell:**

| Entry Point | Eigene Registrations |
|---|---|
| **WPF** | MainViewModel, MainWindow, IDialogService, IAppState, Theme/Locale-Services |
| **API** | RunLifecycleManager, Middleware, Rate Limiter, ApiRunResultMapper |
| **CLI** | — (kein DI-Container nötig, nutzt Factory direkt) |

**Adapter für `IRunOptionsSource`:**

| Entry Point | Adapter | Quelle |
|---|---|---|
| CLI | `CliRunOptionsAdapter` | CLI-Args |
| API | `ApiRunOptionsAdapter` | `ApiRunRequest` DTO |
| WPF | `ViewModelRunOptionsAdapter` | `MainViewModel` Properties |

---

## 3. Fachlicher Datenfluss

### 3.1 Standard-Pipeline

```
                         RunOptions (immutable)
                              │
               ┌──────────────▼──────────────────┐
               │  PREFLIGHT                       │
               │  • Root-Existenz validieren      │
               │  • Audit-Schreibzugriff prüfen   │
               │  • Tool-Verfügbarkeit prüfen     │
               │  → OperationResult (ok|blocked)  │
               └──────────────┬──────────────────┘
                              │ ok
               ┌──────────────▼──────────────────┐
               │  SCAN PHASE                      │
               │  • IAsyncEnumerable streaming    │
               │  • Extension-Filter              │
               │  • Root-Overlap-Dedup            │
               │  • Set-Member-Erkennung          │
               │  • Reparse-Point-Block           │
               │  → ScanPhaseResult               │
               └──────────────┬──────────────────┘
                              │
               ┌──────────────▼──────────────────┐
               │  ENRICHMENT PHASE                │
               │  Für jede ScannedFile:           │
               │   1. FileClassifier.Classify()   │
               │   2. GameKeyNormalizer.Normalize()│
               │   3. RegionDetector.Detect()     │
               │   4. FormatScorer.*()            │
               │   5. VersionScorer.*()           │
               │   6. CompletenessScorer.*()      │
               │   7. ConsoleDetector.Detect()    │
               │   8. Hash → DatIndex.Lookup()    │
               │   9. CandidateFactory.Create()   │
               │  OnlyGames-Filter (optional)     │
               │  → EnrichmentPhaseResult         │
               │    { AllCandidates,              │
               │      ProcessingCandidates }      │
               └──────────────┬──────────────────┘
                              │
               ┌──────────────▼──────────────────┐
               │  DEDUPE PHASE  (pure, no I/O)    │
               │  • DeduplicationEngine           │
               │    .Deduplicate(candidates)      │
               │  • Category-aware SelectWinner   │
               │  • Cross-Group-Guard             │
               │  → DedupePhaseResult             │
               │    { AllGroups, GameGroups,       │
               │      LoserCount }                │
               └──────────────┬──────────────────┘
                              │
          ┌───────────────────┼───────────────────┐
          │                   │                   │
          ▼ (if RemoveJunk)   ▼ (if Mode=Move)   │
  ┌───────────────┐  ┌───────────────────┐       │
  │ JUNK REMOVAL  │  │ DEDUPE MOVE       │       │
  │ • Move junk   │  │ • Move losers     │       │
  │   → trash     │  │   → trash         │       │
  │ • Audit/move  │  │ • ConflictPolicy  │       │
  │ → JunkPhase   │  │ • Audit/move      │       │
  │   Result      │  │ → MovePhaseResult │       │
  └───────┬───────┘  └─────────┬─────────┘       │
          │                    │                  │
          └────────┬───────────┘                  │
                   │                              │
                   ▼ (if SortConsole)              │
          ┌───────────────────┐                   │
          │ CONSOLE SORT      │                   │
          │ • Sort by console │                   │
          │ • Audit/move      │                   │
          │ → ConsoleSortResult                   │
          └───────┬───────────┘                   │
                  │                               │
                  ▼ (if ConvertFormat)             │
          ┌───────────────────┐                   │
          │ CONVERT PHASE     │                   │
          │ • Convert winners │                   │
          │ • Verify output   │                   │
          │ • Source→trash    │                   │
          │   ONLY if verify  │                   │
          │   success         │                   │
          │ • Audit/convert   │                   │
          │ → ConvertPhase    │                   │
          │   Result          │                   │
          └───────┬───────────┘                   │
                  │                               │
                  └───────────┬───────────────────┘
                              │
               ┌──────────────▼──────────────────┐
               │  FINALIZE                        │
               │  • RunResultBuilder.Build()      │
               │    → RunResult (immutable)       │
               │  • ProjectionFactory.Create()    │
               │    → RunProjection (immutable)   │
               └──────────────┬──────────────────┘
                              │
               ┌──────────────▼──────────────────┐
               │  REPORT PHASE                    │
               │  • HTML/CSV aus RunProjection    │
               │  → ReportPhaseResult             │
               └──────────────┬──────────────────┘
                              │
               ┌──────────────▼──────────────────┐
               │  AUDIT SEAL                      │
               │  • Flush + HMAC-Sidecar          │
               │  → AuditMetadata                 │
               └──────────────────────────────────┘
```

### 3.2 ConvertOnly-Pipeline (Sonderfall)

```
RunOptions (ConvertOnly=true)
    │
    ▼
PREFLIGHT → SCAN → ENRICHMENT → CONVERT PHASE → REPORT → AUDIT SEAL
```

Kein Dedupe, kein Junk-Removal, kein ConsoleSort. Nur Scan → Enrich → Convert.

### 3.3 DryRun-Pipeline

Identisch zur Standard-Pipeline, aber:
- `DedupeMovePhase` serialisiert geplante Moves statt sie auszuführen
- `JunkRemovalPhase` serialisiert geplante Removals
- **Entscheidungslogik ist identisch** – nur die Action-Stufe ist Side-Effect-frei

---

## 4. Schichtentrennung

### 4.1 Core (`Romulus.Core`) – Pure Domain Logic

**Gehört hierhin:**

| Klasse / Modul | Ordner |
|---|---|
| `FileClassifier` | `Classification/` |
| `CandidateFactory` | `Classification/` |
| `ConsoleDetector` | `Classification/` |
| `ExtensionNormalizer` | `Classification/` |
| `DiscHeaderDetector` | `Classification/` |
| `GameKeyNormalizer` | `GameKeys/` |
| `RegionDetector` | `Regions/` |
| `FormatScorer` | `Scoring/` |
| `VersionScorer` | `Scoring/` |
| `CompletenessScorer` | `Scoring/` |
| `HealthScorer` | `Scoring/` |
| `DeduplicationEngine` | `Deduplication/` |
| `SortDecision` **(NEU)** | `Sorting/` |
| `ProjectionFactory` **(NEU)** | `Projection/` |
| `RuleEngine` | `Rules/` |
| `SetParsing` (CUE/GDI/CCD/M3U) | `SetParsing/` |
| `LruCache` | `Caching/` |

**Niemals-Regeln:**
1. Kein `System.IO.File`, `System.IO.Directory`, `System.Diagnostics.Process`
2. Kein `HttpClient`, `WebRequest`, `Socket`
3. Keine mutable static State-Variablen (außer compiled Regex + LRU-Cache)
4. Kein Zugriff auf Infrastructure- oder Entry-Point-Namespaces
5. Jede public Methode ist deterministisch: gleiche Inputs → gleiche Outputs

### 4.2 Contracts (`Romulus.Contracts`) – Shared Types

**Gehört hierhin:**
- Alle Result-Records (RomCandidate, DedupeResult, RunResult, RunProjection, MovePhaseResult, ConvertPhaseResult, ScanPhaseResult, ConsoleSortResult, …)
- `FileCategory` enum, `RunOutcome` enum
- Port-Interfaces: `IFileSystem`, `IAuditStore`, `IFormatConverter`, `IDatRepository`, `IToolRunner`, `IAsyncFileScanner`, `IAppState`
- Error-Contracts: `OperationError`, `ErrorKind`, `ErrorClassifier`

**Niemals-Regeln:**
1. Keine Implementierungen (nur Interfaces + Records/Enums)
2. Keine Abhängigkeit auf Core, Infrastructure oder Entry Points

### 4.3 Infrastructure (`Romulus.Infrastructure`) – I/O + Orchestration

**Gehört hierhin:**

| Bereich | Verantwortung |
|---|---|
| `Orchestration/` | RunOrchestrator, PhasePlanBuilder, RunEnvironmentFactory, RunOptionsFactory, RunResultBuilder, alle IPhaseStep-Implementierungen |
| `FileSystem/` | FileSystemAdapter (PathGuard, MoveGuard, Reparse-Block) |
| `Audit/` | AuditCsvStore, AuditSigningService, RollbackService |
| `Dat/` | DatRepositoryAdapter, DatSourceService |
| `Hashing/` | FileHashService, ParallelHasher |
| `Conversion/` | FormatConverterAdapter |
| `Tools/` | ToolRunnerAdapter |
| `Sorting/` | ConsoleSorter, ZipSorter |
| `Reporting/` | ReportGenerator, RunReportWriter |
| `Safety/` | SafetyValidator |
| `Configuration/` | SettingsLoader |
| `Logging/` | JsonlLogWriter |
| `Metrics/` | PhaseMetricsCollector |
| `History/` | RunHistoryService, ScanIndexService |
| `State/` | AppStateStore |

**Niemals-Regeln:**
1. Keine eigene Scoring-Logik, keine Regex-basierte Klassifikation
2. Keine eigene Winner-Selection-Logik
3. Keine eigenständige KPI-Berechnung (nur `ProjectionFactory` aus Core nutzen)
4. I/O-Operationen laufen durch Port-Interfaces

### 4.4 Entry Points (CLI, API, WPF) – Thin Wrappers

**Gehört hierhin:**
- Argument-Parsing / Request-Deserialization
- DI-Wiring (`SharedServiceRegistration.AddRomulusCore()` + eigene Shell-Services)
- `RunOrchestrator.Execute()` aufrufen
- `RunProjection` lesen und darstellen
- UI/UX-spezifische Concerns (Progress-Bar, SSE-Stream, Dashboard-Binding)
- Entry-Point-spezifische Adapter (`IRunOptionsSource`-Implementierungen)

**Niemals-Regeln:**
1. Keine eigene KPI-Berechnung (`AllCandidates.Count(c => …)` ist VERBOTEN)
2. Keine eigene HealthScore / GameCount / DedupeRate-Formel
3. Keine eigene Status-Ableitung aus RunResult
4. Kein direkter Zugriff auf `RunResult`-Interna – nur über `RunProjection`
5. Keine eigene RunOptions-Konstruktion – nur über `RunOptionsFactory`
6. Keine eigene Scoring-, Classification- oder Winner-Logik

---

## 5. Zu entfernende Altlogik

### 5.1 Konkretes Inventar

| Typ | Konkret | Wohin | Risiko |
|---|---|---|---|
| **Inline-Enrichment im Orchestrator** | `MaterializeEnrichedCandidates()` (~50 LOC) | → `EnrichmentPhase` als eigener Step | Mittel |
| **Inline-Report-Generierung** | `GenerateReport()` + Fallback-Logik (~40 LOC in Orchestrator) | → `ReportPhaseStep` | Gering |
| **Inline-Audit-Sidecar** | Normal + Cancel-Sidecar (~30 LOC in Orchestrator) | → `AuditSealPhaseStep` | Gering |
| **Closure-basierte Phase-Planung** | `BuildStandardPhasePlan()` (~120 LOC) | → `PhasePlanBuilder.Build()` | Mittel |
| **Inline-OnlyGames-Filter** | 15 LOC in Execute-Methode | → `EnrichmentPhase` Post-Filter | Gering |
| **Deferred-Service-Analysis** | 4× Try/Catch (~80 LOC) | → eigener `DeferredAnalysisPhaseStep` oder eliminieren | Gering |
| **3× RunOptions-Inline-Bau** | WPF (40 LOC), API (60 LOC), CLI (inline) | → `RunOptionsFactory` + `IRunOptionsSource`-Adapter | Mittel |
| **3× RunEnvironment-Inline-Bau** | WPF/API/CLI jeweils eigener Setup-Block | → `IRunEnvironmentFactory.Create()` | Mittel |
| **API-eigene Result-Mappings** | `ApiRunResult`-Bau (40 LOC in RunManager) + fehlende Felder | → `ApiRunResultMapper.Map(RunProjection)` | Mittel |
| **WPF-ReportPath-Fallback** | `ResolveReportPath()` (50 LOC in RunService) | → `ReportPathResolver` (shared) | Gering |
| **CompletenessScore in EnrichmentPipelinePhase** | `CalculateCompletenessScore()` – ist Domain-Scoring | → `Core/Scoring/CompletenessScorer` | Gering |
| **FolderDeduplicator.GetFolderBaseKey()** | Eigene Key-Normalisierung ähnlich GameKeyNormalizer | → `Core/GameKeys/FolderKeyNormalizer` | Mittel |
| **Hardcoded Extension-Sets in ZipSorter** | PS1/PS2-Extension-Sets als Konstanten | → `consoles.json` Config | Gering |
| **Hardcoded BestFormats in FormatConverterAdapter** | 46 Console-Mappings als Dictionary-Literal | → Datendatei / Config | Gering |
| **Mutable RunResultBuilder mit 20+ Settern** | Zuweisungen über 600 LOC verstreut | → Append-Pattern (`builder.Append(phaseName, result)`) | Mittel |

### 5.2 Muster, die nie bestehen bleiben dürfen

1. **Entry Point berechnet eigene Metriken** → immer `RunProjection` lesen
2. **Scoring-Logik außerhalb Core** → immer in `Core/Scoring/`
3. **Key-Normalisierung außerhalb Core** → immer in `Core/GameKeys/`
4. **RunOptions-Bau außerhalb Factory** → immer über `RunOptionsFactory`
5. **Phase-Logik inline im Orchestrator** → immer als eigener `IPhaseStep`
6. **Move ohne Audit-Record** → jede physische Dateibewegung muss auditiert werden
7. **`converted++` vor Verify** → nie inkrementieren ohne Verifikation
8. **Rollback ohne Sidecar** → nie Dateien zurückbewegen ohne HMAC-verifiziertes `.meta.json`

---

## 6. Migrationshinweise

### 6.1 Strategie: Strangler Fig Pattern

Keine Big-Bang-Migration. Neue Strukturen werden parallel eingeführt, alte schrittweise durch neue ersetzt. Jede Welle endet mit vollständig grüner Test-Suite.

### 6.2 Phasenplan (4 Wellen)

#### Welle A: Foundation (kein Risikobereich, keine Logic-Änderung)

| Schritt | Was | Risiko | Blockiert |
|---|---|---|---|
| A-1 | `IRunEnvironment` Interface + `RunEnvironmentFactory` | Gering | Nichts |
| A-2 | `RunOptionsFactory` + `IRunOptionsSource`-Interface | Gering | Nichts |
| A-3 | `SharedServiceRegistration.AddRomulusCore()` erweitern | Gering | Nichts |
| A-4 | Entry Points auf shared Registration umstellen | Mittel | A-1…A-3 |

**Validierung:** Alle 3 Entry Points produzieren identische `RunOptions` für gleiche Inputs.

#### Welle B: Typisierter Pipeline-Datenfluss

| Schritt | Was | Risiko | Blockiert |
|---|---|---|---|
| B-1 | `PipelineState` einführen (set-once Container) | Gering | Nichts |
| B-2 | Typisierte Phase-Results als sealed records einführen | Gering | Nichts |
| B-3 | `PhasePlanBuilder` einführen (initial als Wrapper) | Mittel | B-1, B-2 |
| B-4 | `RunResultBuilder` auf Append-Pattern umstellen | Mittel | B-2 |
| B-5 | `CompletenessScorer` in Core/Scoring extrahieren | Gering | Nichts |

**Validierung:** Pipeline-Output bitidentisch vor/nach Umstellung (Snapshot-Vergleich RunResult).

#### Welle C: Orchestrator ausdünnen (parallelisierbar)

| Schritt | Parallel | Risiko |
|---|---|---|
| C-1 `ReportPhaseStep` extrahieren (**umgesetzt**) | ✅ | Gering |
| C-2 `AuditSealPhaseStep` extrahieren (**umgesetzt**) | ✅ | Gering |
| C-3 `DeferredAnalysisPhaseStep` extrahieren (**umgesetzt**) | ✅ | Gering |
| C-4 Inline-Methoden in Steps verschieben (**teilweise umgesetzt**) | ✅ | Mittel |
| C-5 `BuildStandardPhasePlan()` durch `PhasePlanBuilder.Build()` ersetzen (**umgesetzt, kompatibler Wrapper bleibt**) | ✅ | Mittel |
| C-6 | `ProjectionFactory` in Core einführen | ✅ | Gering |
| C-7 | `SortDecision` in Core einführen | ✅ | Gering |

**Validierung:** Test-Suite grün nach jedem Schritt. Preview/Execute/Report-Parität per Snapshot.

#### Welle D: Entry Points aufräumen

| Schritt | Was | Risiko |
|---|---|---|
| D-1 API `RunManager` → `RunLifecycleManager` + `ApiRunResultMapper` trennen (**umgesetzt**) | `RunLifecycleManager` trägt Lifecycle- und Zustandslogik; `RunManager` bleibt als Kompatibilitäts-Shim mit Orchestrator-Execution bestehen | Mittel |
| D-2 WPF `RunService` auf Factory + Orchestrator kürzen (**umgesetzt**) | Shared `ReportPathResolver` extrahiert, Factory-Nutzung konsolidiert | Gering |
| D-3 CLI auf Factory-Pattern umstellen (**umgesetzt**) | `RunOptionsFactory`/`RunEnvironmentFactory` als zentrale Build-Pfade im Einsatz | Gering |
| D-4 Alle Entry Points auf `RunProjection` als einzige KPI-Quelle umstellen (**umgesetzt**) | KPI-Ausgabe in CLI/API über Projection-Pfad konsolidiert | Mittel |
| D-5 | `FolderKeyNormalizer` in Core extrahieren | Gering |
| D-6 | Hardcoded Config in ZipSorter/FormatConverter externalisieren | Gering |

**Validierung:** API-Lifecycle-Tests, CLI DryRun Parity, WPF RunService-Tests.

### 6.3 Reihenfolge-Zusammenfassung

```
Welle A (Foundation, zuerst)
  ↓
Welle B (Pipeline-Typen, blockiert durch A)
  ↓
Welle C (Phase-Extraktion, parallel nach B)   ←┐
  ↓                                              │ parallel
Welle D (Entry Points, teilw. parallel zu C)  ←┘
```

### 6.4 Was zuerst, was blockiert

| Zuerst | Begründung |
|---|---|
| `IRunEnvironment` Interface (A-1) | Voraussetzung für testbare Phase-Steps |
| `RunOptionsFactory` (A-2) | Eliminiert sofort Entry-Point-Drift |
| `CompletenessScorer` in Core (B-5) | Domain-Logik-Leck beseitigen |
| `ProjectionFactory` in Core (C-6) | Eliminiert 3× eigene KPI-Berechnung |

| Blockierend | Was es blockiert |
|---|---|
| Welle A | Welle B (PhasePlanBuilder braucht IRunEnvironment) |
| Welle B | Welle C (Phase-Steps brauchen typisierte Results + PipelineState) |
| C-4 (alle Steps extrahiert) | C-5 (PhasePlanBuilder ersetzt BuildStandardPhasePlan) |

| Parallel machbar | |
|---|---|
| Alle C-Schritte untereinander (pro Phase unabhängig) | |
| D-5 (FolderKeyNormalizer) zu jedem Zeitpunkt | |
| D-6 (Config-Externalisierung) zu jedem Zeitpunkt | |

### 6.5 Invarianten-Vertrag (nach jeder Welle prüfen)

| # | Invariante | Prüfmethode |
|---|---|---|
| I-01 | Gleiche RunOptions für gleiche Inputs über alle Entry Points | Snapshot-Vergleich |
| I-02 | Deterministischer Winner-Selection Output | Core-Tests + 10× Shuffle |
| I-03 | Preview = Execute = Report (gleiche Entscheidungen) | DryRun vs Move Report-Diff |
| I-04 | RunProjection ist Single Source für alle KPIs | `grep -r "\.AllCandidates\.Count\|\.GameGroups\.Count" src/` = 0 Treffer in Entry Points |
| I-05 | Kein Move außerhalb erlaubter Roots | Safety-Tests |
| I-06 | Audit-Trail vollständig (Normal + Cancel) | Sidecar-Integrationstests |
| I-07 | GAME schlägt BIOS in Winner Selection | Dedupe-Invariante-Tests |
| I-08 | `Converted + Errors + Skipped == Attempted` | Convert-Phase-Tests |
| I-09 | Kein Rollback ohne `.meta.json` | Rollback-Guard-Tests |
| I-10 | Junk-Moves ∩ Dedupe-Moves = ∅ | RunResult-Invariante-Test |

---

## Anhang A: Komponentendiagramm (Zielbild)

```
┌───────────────────────────────────────────────────────────────────────┐
│                         ENTRY POINTS                                  │
│  ┌────────┐  ┌────────┐  ┌────────┐                                  │
│  │  CLI   │  │  API   │  │  WPF   │                                  │
│  │ (thin) │  │ (thin) │  │ (thin) │                                  │
│  └───┬────┘  └───┬────┘  └───┬────┘                                  │
│      │           │           │                                        │
│      ▼           ▼           ▼                                        │
│  IRunOptionsSource ──→ RunOptionsFactory ──→ RunOptions (immutable)   │
│                        RunEnvironmentFactory ──→ IRunEnvironment      │
└──────────────────────────────┬────────────────────────────────────────┘
                               │
                               ▼
┌──────────────────────────────────────────────────────────────────────┐
│                      INFRASTRUCTURE                                   │
│                                                                       │
│  RunOrchestrator (≤250 LOC, keine Fachlogik)                         │
│    └── PhasePlanBuilder.Build() → IReadOnlyList<IPhaseStep>          │
│    └── foreach step: result = step.Execute(state, ct)                │
│    └── RunResultBuilder.Append() → .Build() → RunResult             │
│                                                                       │
│  Phase-Steps:                                                         │
│    ScanPhase → EnrichmentPhase → DedupePhase →                       │
│    JunkRemovalPhase → DedupeMovePhase →                              │
│    ConsoleSortPhase → ConvertPhase →                                 │
│    ReportPhase → AuditSealPhase                                      │
│                                                                       │
│  Port-Implementierungen:                                              │
│    FileSystemAdapter, AuditCsvStore, DatRepositoryAdapter,           │
│    FormatConverterAdapter, ToolRunnerAdapter, FileHashService         │
│                                                                       │
│  Support-Services:                                                    │
│    RollbackService, ConsoleSorter, ReportGenerator,                  │
│    SettingsLoader, SafetyValidator, PhaseMetricsCollector             │
└──────────────────────────────┬───────────────────────────────────────┘
                               │ delegiert an
                               ▼
┌──────────────────────────────────────────────────────────────────────┐
│                           CORE  (pure, deterministisch)               │
│                                                                       │
│  Classification/   GameKeys/     Regions/       Scoring/              │
│   FileClassifier    GameKey       Region         FormatScorer         │
│   CandidateFactory  Normalizer    Detector       VersionScorer       │
│   ConsoleDetector                                CompletenessScorer   │
│   ExtensionNorm.                                 HealthScorer         │
│                                                                       │
│  Deduplication/    Sorting/      Projection/    Rules/                │
│   Deduplication     SortDecision  Projection     RuleEngine           │
│   Engine            (NEU)         Factory (NEU)                       │
│                                                                       │
│  SetParsing/       Caching/                                           │
│   M3u, Cue, Gdi,   LruCache                                         │
│   Ccd, Mds                                                           │
└──────────────────────────────┬───────────────────────────────────────┘
                               │ referenziert nur
                               ▼
┌──────────────────────────────────────────────────────────────────────┐
│                         CONTRACTS                                     │
│                                                                       │
│  Models/           Ports/            Errors/                          │
│   RomCandidate      IFileSystem       OperationError                 │
│   DedupeResult      IAuditStore       ErrorKind                      │
│   RunResult         IFormatConverter  ErrorClassifier                │
│   RunProjection     IDatRepository                                   │
│   MovePhaseResult   IToolRunner                                      │
│   ConvertPhaseResult IAsyncFileScanner                               │
│   ScanPhaseResult   IAppState                                        │
│   FileCategory      IDialogService                                   │
│   RunOutcome                                                         │
│   RunOptions                                                         │
│   OperationResult                                                    │
└──────────────────────────────────────────────────────────────────────┘
```

---

## Anhang B: Mapping IST → ZIEL

| IST | Problem | ZIEL |
|---|---|---|
| Orchestrator 800 LOC mit Inline-Logik | SRP, Testbarkeit | Orchestrator ≤250 LOC, Logik in Phase-Steps |
| 3× RunOptions-Bau (WPF/API/CLI) | Option-Drift | `RunOptionsFactory` + Adapter |
| RunEnvironment mit konkreten Typen | Nicht mockbar | `IRunEnvironment` Interface |
| CompletenessScore in Infrastructure | Domain-Leck | `CompletenessScorer` in Core/Scoring |
| FolderDeduplicator eigene Key-Norm | Doppelte Logik | `FolderKeyNormalizer` in Core/GameKeys |
| Hardcoded Extension-Sets | Code-Änderung für neue Konsolen | Config in `consoles.json` |
| RunResult mutable Felder verstreut | Inkonsistenz-Risiko | Builder → immutable sealed record |
| Eigene KPI-Berechnung in Entry Points | Paritätsverlust | Nur `RunProjection` lesen |
| ConsoleSort ohne Audit | Undo unmöglich | Audit-Trail ergänzen |
| API 8 fehlende RunOptions-Felder | Feature-Drift | `RunOptionsFactory` + vollständiger Adapter |

---

## Anhang C: Entscheidungs-Rückverfolgung

| ADR-013 Referenz | Ursprung |
|---|---|
| D-01 Immutable RomCandidate | ADR-005 D-01 |
| D-02 RunResultBuilder → RunResult | ADR-005 D-02 + ADR-012 §3.3 |
| D-03 RunProjection Single Source | ADR-005 D-03 |
| D-04 Category-aware Selection | ADR-005 D-04 |
| D-05 Phase-Handler-Pattern | ADR-005 D-05 + ADR-012 §2.4/§3 |
| D-06 Audit-Trail Querschnitt | ADR-005 D-06 |
| D-07 Getrennte Move-Results | ADR-005 D-07 |
| D-08 Single RunOptionsFactory | ADR-012 §2.2 |
| D-09 IRunEnvironment Interface | ADR-012 §2.3 |
| D-10 ProjectionFactory in Core | ADR-005 §2.3 + ADR-007 §3.2 |
| Safety / Guards | ADR-010 vollständig |
| Migrationsplan Wellen A–D | ADR-012 §5 (verfeinert) |
