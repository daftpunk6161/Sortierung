# ADR-005: Zielarchitektur Kernfunktionen

> **Status:** Proposed
> **Datum:** 2026-03-17
> **Kontext:** FINAL_CONSOLIDATED_AUDIT.md, 8 Kernursachen, 42 konsolidierte Findings
> **Scope:** Scan, Classification, GameKey, Grouping, Dedupe, Sorting, DAT, Conversion, Move/Undo, Orchestrator, Shared Logic

---

## 1. Executive Design

### Hauptidee

**Jede fachliche Entscheidung wird genau einmal, an genau einer Stelle getroffen.**

Das System wird in fünf klar getrennte Verarbeitungsstufen zerlegt:

```
Raw I/O → Enrichment → Decision → Action → Evidence
```

1. **Raw I/O**: Scan, Enumerate, Hash — produziert rohe Fakten
2. **Enrichment**: Classify, Score, Match — reichert Fakten an, produziert immutable Candidates
3. **Decision**: Group, Select Winner, Sort-Ziel — rein deterministisch, keine Seiteneffekte
4. **Action**: Move, Convert, Trash — einzige Stufe mit physischen Seiteneffekten
5. **Evidence**: Audit, Report, Sidecar — dokumentiert was tatsächlich passiert ist

### Wichtigste Architekturentscheidungen

| # | Entscheidung | Begründung |
|---|-------------|------------|
| D-01 | **Immutable `RomCandidate` als zentrales Fachmodell** | `sealed record` statt `sealed class` — nach Konstruktion nicht mehr veränderbar. Eliminiert Mutation-Bugs (KU-2). |
| D-02 | **`RunResultBuilder` → `RunResult` (immutable)** | Builder akkumuliert während der Pipeline; `.Build()` erzeugt finales, unveränderliches Result. Eliminiert die Klasse der „partielle Daten gelesen"-Bugs. |
| D-03 | **`RunProjection` als einzige Quelle für abgeleitete KPIs** | Berechnet einmal aus `RunResult`. Alle Consumer (GUI/CLI/API/Report) lesen nur von `RunProjection`. Eliminiert KU-1 komplett. |
| D-04 | **Category-aware Winner Selection** | `SelectWinner` erhält explizites Category-Scoring. BIOS darf GAME nie schlagen. Eliminiert P2-03/R-01. |
| D-05 | **Phase-Handler-Pattern statt monolithischer Orchestrator** | Jede Phase wird ein eigenes `IPipelinePhase<TIn, TOut>`. Orchestrator komponiert Phasen, enthält keine Fachlogik. |
| D-06 | **Audit-Trail als Querschnittskonzern** | `IAuditTrail` wird in jede Action-Phase injiziert, nicht optional. Jede physische Dateibewegung erzeugt einen Audit-Record. |
| D-07 | **Getrennte Junk- und Dedupe-MoveResults** | `RunResult` hat `JunkMoveResult` und `DedupeMoveResult` — nie addiert, nie vermischt. |

### Kritischste Verantwortungstrennungen

| Grenze | Links | Rechts | Niemals übertreten |
|--------|-------|--------|-------------------|
| Pure ↔ I/O | Core (deterministisch) | Infrastructure (Seiteneffekte) | Core darf nie `File.*`, `Directory.*`, `Process.*` aufrufen |
| Decision ↔ Action | SelectWinner, SortDecision | MovePhase, ConvertPhase | Decision-Logic darf nie Dateien bewegen |
| Enrichment ↔ Mutation | Candidate-Konstruktion | RunResult-Schreibung | Candidates sind nach Konstruktion immutable |
| Orchestrator ↔ Fachlogik | Phase-Komposition | Scoring, Classification | Orchestrator enthält null Regex, null Scoring, null Kategorisierung |

---

## 2. Zielobjekte und Services

### 2.1 Core-Schicht (Pure, Deterministisch, Kein I/O)

#### `RomCandidate` (sealed record)

- **Zweck**: Zentrales Fachmodell, repräsentiert eine ROM-Datei mit all ihren Scores
- **Inputs**: MainPath, Extension, SizeBytes, Category, GameKey, Region, alle Scores, DatMatch, ConsoleKey, CompletenessScore
- **Outputs**: Immutable Record — einmal erzeugt, nie geändert
- **Verantwortung**: Datenträger aller Enrichment-Ergebnisse für eine Datei
- **Darf NICHT**: Scores nachträglich ändern, I/O ausführen, null-GameKey haben

```csharp
public sealed record RomCandidate(
    string MainPath,
    string Extension,
    long SizeBytes,
    FileCategory Category,         // enum statt string
    string GameKey,
    string Region,
    int RegionScore,
    int FormatScore,
    long VersionScore,
    int HeaderScore,
    int CompletenessScore,
    long SizeTieBreakScore,
    bool DatMatch,
    string ConsoleKey);
```

**Kritische Änderung:** `Category` wird `FileCategory` (enum), nicht `string`. Eliminiert den `_ => "GAME"` Bug.

#### `FileClassifier` (static, pure)

- **Zweck**: Klassifiziert ROM-Dateinamen in `FileCategory { Game, Bios, Junk, Unknown }`
- **Inputs**: `string baseName, bool aggressiveJunk`
- **Outputs**: `FileCategory` (enum)
- **Verantwortung**: Regex-basierte Mustererkennung
- **Darf NICHT**: I/O, Dateien lesen, `Unknown` zu `Game` mappen
- **Status IST**: ✅ Korrekt implementiert. Bug liegt im Aufrufer (Orchestrator-Switch).

#### `GameKeyNormalizer` (static, pure)

- **Zweck**: Erzeugt den normalisierten Gruppierungsschlüssel aus ROM-Dateinamen
- **Inputs**: `string baseName, IReadOnlyList<Regex> tagPatterns, aliases, ...`
- **Outputs**: `string gameKey` (nie null, nie leer)
- **Verantwortung**: ASCII-Folding, Tag-Stripping, Alias-Lookup, Whitespace-Normalisierung
- **Darf NICHT**: I/O, Category-Logik, Region-Scoring
- **Offen**: BIOS-Tag `(BIOS)` wird gestrippt → Collision mit GAME-Titeln gleichen Namens. **Lösung**: BIOS-Dateien erhalten Prefix `__BIOS__` im GameKey.

#### `RegionDetector` (static, pure)

- **Zweck**: Extrahiert Region-Tag aus ROM-Dateinamen
- **Inputs**: `string fileName`
- **Outputs**: `string region` (EU, US, JP, WORLD, UNKNOWN, ...)
- **Verantwortung**: Ordered-Rule-Matching, Multi-Region-Detection, Token-Parsing
- **Darf NICHT**: Scoring, I/O

#### `FormatScorer` (static, pure)

- **Zweck**: Berechnet Format-, Region-, Size-Tiebreak-Scores
- **Inputs**: Extension, Region + PreferOrder, SizeBytes + Type
- **Outputs**: `int` oder `long` Score-Werte
- **Verantwortung**: Deterministische numerische Bewertung
- **Darf NICHT**: I/O, Dateien lesen, sich auf Dateisystem-Reihenfolge verlassen

#### `VersionScorer` (sealed class, pure)

- **Zweck**: Berechnet Version/Revision-Score aus ROM-Dateinamen
- **Inputs**: `string baseName`
- **Outputs**: `long versionScore`
- **Verantwortung**: Verified-Dump [!], Revision a-z, Version v1.0, Sprach-Bonus
- **Darf NICHT**: I/O

#### `DeduplicationEngine` (static, pure)

- **Zweck**: Gruppiert Candidates nach GameKey und wählt Winner
- **Inputs**: `IReadOnlyList<RomCandidate>`
- **Outputs**: `IReadOnlyList<DedupeGroup>`
- **Verantwortung**: Grouping, Winner-Selection, deterministische Sortierung
- **Darf NICHT**: I/O, Dateien verschieben, Scores berechnen (nur vergleichen)

**Kritische Änderung: Category-aware `SelectWinner`**

```csharp
public static RomCandidate? SelectWinner(IReadOnlyList<RomCandidate> items)
{
    if (items is null || items.Count == 0) return null;
    if (items.Count == 1) return items[0];

    // Phase 1: Filter to highest-priority category
    // GAME > BIOS > JUNK > UNKNOWN
    var candidates = FilterToBestCategory(items);

    // Phase 2: Multi-criteria sort within category
    return candidates
        .OrderByDescending(x => x.CompletenessScore)
        .ThenByDescending(x => x.DatMatch ? 1 : 0)
        .ThenByDescending(x => x.RegionScore)
        .ThenByDescending(x => x.HeaderScore)
        .ThenByDescending(x => x.VersionScore)
        .ThenByDescending(x => x.FormatScore)
        .ThenByDescending(x => x.SizeTieBreakScore)
        .ThenBy(x => x.MainPath, StringComparer.OrdinalIgnoreCase)
        .First();
}

private static IReadOnlyList<RomCandidate> FilterToBestCategory(
    IReadOnlyList<RomCandidate> items)
{
    // Prefer GAME over everything
    var games = items.Where(x => x.Category == FileCategory.Game).ToList();
    if (games.Count > 0) return games;
    var bios = items.Where(x => x.Category == FileCategory.Bios).ToList();
    if (bios.Count > 0) return bios;
    return items.ToList();
}
```

#### `DedupeGroup` (sealed record, NEW)

Ersetzt das mutable `DedupeResult`:

```csharp
public sealed record DedupeGroup(
    string GameKey,
    RomCandidate Winner,
    IReadOnlyList<RomCandidate> Losers);
```

#### `SortDecision` (static, pure, NEW)

- **Zweck**: Bestimmt das Zielverzeichnis für eine Datei basierend auf ConsoleKey
- **Inputs**: `RomCandidate candidate, string rootPath`
- **Outputs**: `SortTarget { ConsoleKey, TargetDirectory }` oder `null` für UNKNOWN
- **Verantwortung**: Rein fachliche Sortierungsentscheidung
- **Darf NICHT**: Dateien bewegen, I/O

#### `CandidateFactory` (static, pure, NEW)

- **Zweck**: Konstruiert `RomCandidate` aus Roh-Scan-Daten + Enrichment-Ergebnissen
- **Inputs**: FilePath, SizeBytes, alle Score-Ergebnisse, Category, GameKey, Region, DatMatch, ConsoleKey
- **Outputs**: `RomCandidate` (immutable)
- **Verantwortung**: Validierung (kein leerer GameKey, kein null-Path), korrekte Mapping (`FileCategory.Unknown` → `FileCategory.Unknown`, NICHT `Game`)
- **Darf NICHT**: I/O, Dateien hashen, Scores berechnen

```csharp
public static class CandidateFactory
{
    public static RomCandidate Create(
        string normalizedPath, string extension, long sizeBytes,
        FileCategory category, string gameKey, string region,
        int regionScore, int formatScore, long versionScore,
        int headerScore, int completenessScore, long sizeTieBreakScore,
        bool datMatch, string consoleKey)
    {
        // BIOS GameKey collision prevention
        var effectiveKey = category == FileCategory.Bios
            ? $"__BIOS__{gameKey}"
            : gameKey;

        return new RomCandidate(
            MainPath: normalizedPath,
            Extension: extension,
            SizeBytes: sizeBytes,
            Category: category,
            GameKey: effectiveKey,
            Region: region,
            RegionScore: regionScore,
            FormatScore: formatScore,
            VersionScore: versionScore,
            HeaderScore: headerScore,
            CompletenessScore: completenessScore,
            SizeTieBreakScore: sizeTieBreakScore,
            DatMatch: datMatch,
            ConsoleKey: consoleKey);
    }
}
```

---

### 2.2 Infrastructure-Schicht (I/O, Seiteneffekte)

#### `ScanPhase` : `IPipelinePhase<RunOptions, ScanResult>` (NEW)

- **Zweck**: Enumeriert Dateien aus Roots, eliminiert Duplikate, erkennt Sets
- **Inputs**: `RunOptions` (Roots, Extensions, CancellationToken)
- **Outputs**: `ScanResult` (sealed record)
- **Verantwortung**: File-Enumeration, Path-Normalisierung, Overlapping-Root-Dedup, Set-Member-Erkennung, Reparse-Point-Blocking
- **Darf NICHT**: Dateien verschieben, Scores berechnen, Gruppieren

```csharp
public sealed record ScanResult(
    IReadOnlyList<ScannedFile> Files,
    IReadOnlySet<string> SetMemberPaths,
    int OverlappingPathsSkipped);

public sealed record ScannedFile(
    string NormalizedPath,
    string Extension,
    long SizeBytes);
```

#### `EnrichmentPhase` : `IPipelinePhase<ScanResult, IReadOnlyList<RomCandidate>>` (NEW)

- **Zweck**: Reichert gescannte Dateien mit Classification, GameKey, Scores, DAT-Match an
- **Inputs**: `ScanResult`, Options (AggressiveJunk, PreferRegions, HashType)
- **Outputs**: `IReadOnlyList<RomCandidate>` (immutable)
- **Verantwortung**: Aufruft Core-Services (Classifier, Normalizer, Scorer, RegionDetector), konstruiert Candidates via `CandidateFactory`
- **Darf NICHT**: Dateien bewegen, Gruppieren, Winner auswählen

#### `DedupePhase` : `IPipelinePhase<IReadOnlyList<RomCandidate>, DedupePhaseResult>` (NEW)

- **Zweck**: Gruppiert und wählt Winner
- **Inputs**: `IReadOnlyList<RomCandidate>`
- **Outputs**: `DedupePhaseResult` (sealed record)
- **Verantwortung**: Delegiert an `DeduplicationEngine`, trennt Game-Gruppen von BIOS/JUNK
- **Darf NICHT**: I/O, Dateien bewegen

```csharp
public sealed record DedupePhaseResult(
    IReadOnlyList<DedupeGroup> GameGroups,
    IReadOnlyList<DedupeGroup> BiosGroups,
    IReadOnlyList<RomCandidate> StandaloneJunk,
    int TotalGroups);
```

#### `JunkRemovalPhase` : `IPipelinePhase<..., MovePhaseResult>` (NEW)

- **Zweck**: Verschiebt standalone Junk-Dateien in Trash
- **Inputs**: `DedupePhaseResult.StandaloneJunk`, RunOptions
- **Outputs**: `MovePhaseResult` (immutable record)
- **Verantwortung**: Junk-Only Moves, Audit-Trail
- **Darf NICHT**: Dedupe-Losers bewegen, Scores berechnen

#### `DedupeMovePhase` : `IPipelinePhase<..., MovePhaseResult>` (NEW)

- **Zweck**: Verschiebt Dedupe-Losers in Trash
- **Inputs**: `DedupePhaseResult.GameGroups[].Losers`, RunOptions
- **Outputs**: `MovePhaseResult` (immutable record)
- **Verantwortung**: Loser-Moves, ConflictPolicy, Audit-Trail
- **Darf NICHT**: Junk bewegen, Scores berechnen

#### `ConvertPhase` : `IPipelinePhase<..., ConvertPhaseResult>` (NEW)

- **Zweck**: Konvertiert Winner-Dateien in Zielformat
- **Inputs**: Winners, IFormatConverter, RunOptions
- **Outputs**: `ConvertPhaseResult` (sealed record)
- **Verantwortung**: Convert → Verify → (Source-zu-Trash nur bei Verify-Success) → Audit
- **Darf NICHT**: Scores berechnen, Gruppieren

```csharp
public sealed record ConvertPhaseResult(
    int Converted,
    int Errors,
    int Skipped)
{
    // INVARIANT: Converted + Errors + Skipped == Attempted
}
```

**Kritische Regel (P0-A)**: Increment-Logik:

```
if (convert.Success && verify.Success) → converted++, source → trash
else if (convert.Success && verify.Fail) → errors++, source BLEIBT
else if (convert.Skipped) → skipped++
else → errors++
```

Niemals `converted++` VOR Verify. Niemals Source löschen bei Verify-Failure.

#### `ConsoleSortPhase` : `IPipelinePhase<..., ConsoleSortResult>` (NEW)

- **Zweck**: Sortiert Dateien in Konsolen-Unterverzeichnisse
- **Inputs**: Roots, ConsoleDetector, Extensions
- **Outputs**: `ConsoleSortResult` (exists, extend with FailCount)
- **Verantwortung**: Console-Detection, Move, **Audit-Trail** (bisher fehlend!)
- **Darf NICHT**: Dedupe-Logik, Scoring

#### `AuditTrailService` (sealed class, NEW, Querschnitt)

- **Zweck**: Schreibt Audit-Records für JEDE physische Dateibewegung
- **Inputs**: AuditPath, RootPath, OldPath, NewPath, Action, Category, Reason
- **Outputs**: Void (Seiteneffekt: CSV-Append)
- **Verantwortung**: CSV-Injection-Prävention, Action-Typisierung, Sidecar-Signatur
- **Darf NICHT**: Move-Entscheidungen treffen

Actions: `MOVE`, `JUNK_REMOVE`, `CONSOLE_SORT`, `CONVERT`, `CONVERT_FAILED`, `SKIP`, `ROLLBACK`

---

### 2.3 Result-Objekte (alle immutable)

#### `RunResult` (sealed record, Builder-Pattern)

```csharp
public sealed record RunResult
{
    public required RunOutcome Outcome { get; init; }
    public required int ExitCode { get; init; }
    public required OperationResult Preflight { get; init; }

    // Phase results
    public required ScanResult Scan { get; init; }
    public required IReadOnlyList<RomCandidate> Candidates { get; init; }
    public required DedupePhaseResult Dedupe { get; init; }
    public required MovePhaseResult JunkMoves { get; init; }
    public required MovePhaseResult DedupeMoves { get; init; }
    public ConsoleSortResult? ConsoleSort { get; init; }
    public ConvertPhaseResult? Conversion { get; init; }

    public required long DurationMs { get; init; }
    public PhaseMetricsResult? PhaseMetrics { get; init; }
    public string? ReportPath { get; init; }
    public string? AuditPath { get; init; }
}
```

**`RunResultBuilder`** (internal, mutable):

```csharp
internal sealed class RunResultBuilder
{
    // Mutable fields, set by phases
    public RunOutcome Outcome { get; set; }
    public OperationResult? Preflight { get; set; }
    public ScanResult? Scan { get; set; }
    // ... etc ...

    public RunResult Build()
    {
        // Validate invariants before sealing
        Debug.Assert(Scan is not null);
        Debug.Assert(Preflight is not null);
        return new RunResult { ... };
    }
}
```

#### `RunProjection` (sealed record, NEW — Single Source of Truth für KPIs)

```csharp
public sealed record RunProjection
{
    // Raw counts (from RunResult)
    public int TotalFilesScanned { get; init; }
    public int GroupCount { get; init; }
    public int WinnerCount { get; init; }
    public int PlannedLoserCount { get; init; }

    // Dedupe KPIs
    public int DedupeMoveCount { get; init; }
    public int DedupeFailCount { get; init; }
    public int DedupeSkipCount { get; init; }

    // Junk KPIs
    public int JunkMoveCount { get; init; }
    public int JunkFailCount { get; init; }

    // Conversion KPIs
    public int ConvertedCount { get; init; }
    public int ConvertErrorCount { get; init; }
    public int ConvertSkippedCount { get; init; }

    // DAT KPIs
    public int DatVerifiedCount { get; init; }
    public int DatSkippedCount { get; init; }

    // Derived KPIs (berechnet einmal)
    public int GameCount { get; init; }           // Gruppen mit ≥1 GAME Candidate
    public int BiosGroupCount { get; init; }
    public double HealthScore { get; init; }       // EINE Formel
    public double DedupeRate { get; init; }
    public int TotalErrorCount { get; init; }      // Move + Convert Fehler
    public RunOutcome Outcome { get; init; }

    // Console Sort KPIs
    public int ConsoleSortMoved { get; init; }
    public int ConsoleSortUnknown { get; init; }
}
```

#### `ProjectionFactory` (static, pure, NEW)

```csharp
public static class ProjectionFactory
{
    public static RunProjection Create(RunResult result)
    {
        return new RunProjection
        {
            TotalFilesScanned = result.Scan.Files.Count,
            GroupCount = result.Dedupe.TotalGroups,
            WinnerCount = result.Dedupe.GameGroups.Count,
            PlannedLoserCount = result.Dedupe.GameGroups.Sum(g => g.Losers.Count),

            DedupeMoveCount = result.DedupeMoves.MoveCount,
            DedupeFailCount = result.DedupeMoves.FailCount,
            DedupeSkipCount = result.DedupeMoves.SkipCount,

            JunkMoveCount = result.JunkMoves.MoveCount,
            JunkFailCount = result.JunkMoves.FailCount,

            ConvertedCount = result.Conversion?.Converted ?? 0,
            ConvertErrorCount = result.Conversion?.Errors ?? 0,
            ConvertSkippedCount = result.Conversion?.Skipped ?? 0,

            GameCount = result.Dedupe.GameGroups.Count(g =>
                g.Winner.Category == FileCategory.Game),
            HealthScore = CalculateHealthScore(result),
            DedupeRate = CalculateDedupeRate(result),
            TotalErrorCount = (result.DedupeMoves.FailCount)
                            + (result.JunkMoves.FailCount)
                            + (result.Conversion?.Errors ?? 0),
            Outcome = result.Outcome,
            // ...
        };
    }

    private static double CalculateHealthScore(RunResult result)
    {
        var total = result.Scan.Files.Count;
        if (total == 0) return 100.0;
        var winners = result.Dedupe.GameGroups.Count;
        return Math.Round(100.0 * winners / total, 1);
    }
}
```

**Alle Consumer (GUI, CLI, API, Report) lesen ausschließlich von `RunProjection`.**

---

### 2.4 Pipeline-Interface

```csharp
public interface IPipelinePhase<TIn, TOut>
{
    string Name { get; }
    TOut Execute(TIn input, PipelineContext context, CancellationToken ct);
}

public sealed class PipelineContext
{
    public RunOptions Options { get; init; } = null!;
    public Action<string>? OnProgress { get; init; }
    public PhaseMetricsCollector Metrics { get; init; } = new();
    public IAuditStore AuditStore { get; init; } = null!;
}
```

---

## 3. Fachlicher Datenfluss

```
RunOptions
  │
  ▼
┌──────────────────────────────────────────────────────────────────┐
│ PREFLIGHT (validate roots, audit-dir, tools)                     │
│ Output: OperationResult (ok | blocked)                          │
└──────────────────────────┬───────────────────────────────────────┘
                           │ ok
                           ▼
┌──────────────────────────────────────────────────────────────────┐
│ SCAN PHASE                                                       │
│ - Enumerate files from Roots (recursive, skip reparse)           │
│ - Overlapping-Root-Dedup via HashSet<normalizedPath>            │
│ - Discover set memberships (CUE→BIN, GDI→tracks, ...)           │
│ Output: ScanResult { Files, SetMemberPaths }                     │
└──────────────────────────┬───────────────────────────────────────┘
                           │
                           ▼
┌──────────────────────────────────────────────────────────────────┐
│ ENRICHMENT PHASE                                                 │
│ For each ScannedFile:                                            │
│   1. FileClassifier.Classify(baseName) → FileCategory            │
│   2. GameKeyNormalizer.Normalize(baseName) → gameKey              │
│   3. RegionDetector.GetRegionTag(baseName) → region              │
│   4. FormatScorer.GetRegionScore(region, prefs) → regionScore    │
│   5. FormatScorer.GetFormatScore(ext) → formatScore             │
│   6. VersionScorer.GetVersionScore(baseName) → versionScore     │
│   7. ConsoleDetector.Detect(path, root) → consoleKey            │
│   8. FileHashService.GetHash() → hash → DatIndex.Lookup()       │
│   9. CandidateFactory.Create(...) → RomCandidate (immutable)     │
│ Remove set-member candidates                                     │
│ Output: IReadOnlyList<RomCandidate>                              │
└──────────────────────────┬───────────────────────────────────────┘
                           │
                           ▼
┌──────────────────────────────────────────────────────────────────┐
│ DEDUPE PHASE (pure logic, no I/O)                                │
│   1. DeduplicationEngine.Deduplicate(candidates)                 │
│      - Group by GameKey (case-insensitive)                       │
│      - Skip empty/whitespace keys                                │
│      - SelectWinner: Category-filter → multi-criteria sort       │
│   2. Partition into GameGroups, BiosGroups, StandaloneJunk       │
│ Output: DedupePhaseResult                                        │
└──────────────────────────┬───────────────────────────────────────┘
                           │
               ┌───────────┴───────────┐
               │                       │
               ▼                       ▼
  ┌─────────────────────┐  ┌─────────────────────┐
  │ JUNK REMOVAL PHASE  │  │ DEDUPE MOVE PHASE   │
  │ - Move standalone   │  │ - Move losers to    │
  │   junk → _TRASH_JUNK│  │   _TRASH_DEDUPE     │
  │ - Audit per move    │  │ - ConflictPolicy    │
  │ - SkipCount         │  │ - Audit per move    │
  │ Output:             │  │ Output:             │
  │   MovePhaseResult   │  │   MovePhaseResult   │
  └─────────┬───────────┘  └──────────┬──────────┘
            │                         │
            └───────────┬─────────────┘
                        │
                        ▼
           ┌─────────────────────────┐
           │ CONSOLE SORT PHASE      │ (optional)
           │ - Sort by ConsoleKey    │
           │ - Audit per sort-move   │
           │ Output:                 │
           │   ConsoleSortResult     │
           └────────────┬────────────┘
                        │
                        ▼
           ┌─────────────────────────┐
           │ CONVERT PHASE           │ (optional)
           │ - Convert → Verify      │
           │ - Source → Trash ONLY   │
           │   if Verify.Success     │
           │ - Audit per convert     │
           │ Output:                 │
           │   ConvertPhaseResult    │
           └────────────┬────────────┘
                        │
                        ▼
           ┌─────────────────────────┐
           │ FINALIZE                │
           │ - RunResultBuilder      │
           │   .Build() → RunResult  │
           │ - ProjectionFactory     │
           │   .Create() →           │
           │   RunProjection         │
           │ - Report, Sidecar       │
           └─────────────────────────┘
```

---

## 4. Schichtentrennung

### Core (`Romulus.Core`) — Pure Domain Logic

**Gehört hierhin:**
- `FileClassifier` (Category-Erkennung)
- `GameKeyNormalizer` (Key-Normalisierung)
- `RegionDetector` (Region-Erkennung)
- `FormatScorer` (Format/Region/Size-Scoring)
- `VersionScorer` (Version/Revision/Language-Scoring)
- `DeduplicationEngine` (Grouping + Winner Selection)
- `CandidateFactory` (Candidate-Konstruktion + Validierung)
- `SortDecision` (Ziel-Ordner-Bestimmung)
- `ProjectionFactory` (KPI-Berechnung)
- `RuleEngine` (User-definierte Klassifikationsregeln)
- `SetParsing` (CUE/GDI/CCD/M3U Parser)
- `LruCache` (generic Cache)

**Niemals-Regeln für Core:**
1. ❌ Kein `System.IO.File`, `System.IO.Directory`, `System.Diagnostics.Process`
2. ❌ Kein `HttpClient`, `WebRequest`, `Socket`
3. ❌ Keine mutablen static State-Variables (außer compiled Regex-Cache)
4. ❌ Kein Zugriff auf Infrastructure- oder Entry-Point-Namespaces
5. ✅ Jede public Methode ist deterministisch: gleiche Inputs → gleiche Outputs

### Contracts (`Romulus.Contracts`) — Shared Types

**Gehört hierhin:**
- `RomCandidate` (sealed record)
- `DedupeGroup` (sealed record)
- `FileCategory` (enum)
- `RunOutcome` (enum)
- `RunResult` (sealed record)
- `RunProjection` (sealed record)
- `MovePhaseResult` (sealed record)
- `ConvertPhaseResult` (sealed record)
- `ScanResult` (sealed record)
- `ConsoleSortResult` (sealed record)
- Port-Interfaces: `IFileSystem`, `IAuditStore`, `IFormatConverter`, `IDatRepository`
- Error-Contracts: `OperationError`, `ErrorKind`

**Niemals-Regeln für Contracts:**
1. ❌ Keine Implementierungen (nur Interfaces + Records/Enums)
2. ❌ Keine Abhängigkeiten auf Core, Infrastructure oder Entry Points

### Infrastructure (`Romulus.Infrastructure`) — I/O + Orchestration

**Gehört hierhin:**
- `RunOrchestrator` (Phase-Komposition, KEINE Fachlogik)
- `ScanPhase`, `EnrichmentPhase`, `DedupePhase`, `JunkRemovalPhase`, `DedupeMovePhase`, `ConsoleSortPhase`, `ConvertPhase`
- `RunResultBuilder`
- `FileSystemAdapter` (IFileSystem-Implementierung)
- `AuditCsvStore` (IAuditStore-Implementierung)
- `FileHashService`, `Crc32`, `ParallelHasher`
- `DatRepositoryAdapter`, `DatSourceService`
- `FormatConverterAdapter`
- `ToolRunnerAdapter`
- `ConsoleSorter`
- `ReportGenerator`, `RunReportWriter`
- `SettingsLoader`
- `JsonlLogWriter`

**Niemals-Regeln für Infrastructure:**
1. ❌ Keine Scoring-Logik, keine Regex-basierte Klassifikation
2. ❌ Keine Winner-Selection-Logik
3. ❌ Keine eigenständige KPI-Berechnung (nur `ProjectionFactory` aus Core nutzen)
4. ✅ I/O-Operationen hinter Port-Interfaces

### Entry Points (CLI, API, WPF) — Thin Wrappers

**Gehört hierhin:**
- Argument-Parsing / Request-Deserialization
- DI-Wiring (Services zusammenbauen)
- `RunOrchestrator.Execute()` aufrufen
- `RunProjection` lesen und darstellen
- UI-spezifische Concerns (Progress-Bar, SSE-Stream, Dashboard-Binding)

**Niemals-Regeln für Entry Points:**
1. ❌ Keine eigene KPI-Berechnung (`AllCandidates.Count(c => ...)` ist VERBOTEN)
2. ❌ Keine eigene HealthScore/Games/DedupeRate-Formel
3. ❌ Keine eigene Status-Ableitung
4. ❌ Kein direkter Zugriff auf `RunResult`-Interna — nur über `RunProjection`
5. ✅ CLI/API/GUI zeigen identische Werte, weil sie dieselbe `RunProjection` lesen

---

## 5. Zu entfernende Altlogik

| Art | Konkret | Warum weg |
|-----|---------|-----------|
| **String-Category** | `Category { get; init; } = "GAME"` als string | Enum `FileCategory` stattdessen. Eliminiert den `_ => "GAME"` Bug. |
| **Inline-Enrichment im Orchestrator** | 50-Zeilen Switch-Block in `ScanFiles` der Classify/Normalize/Score/Hash zusammenwürfelt | Muss in `EnrichmentPhase` + `CandidateFactory` |
| **Mutable-RunResult** | `RunResult` mit 20+ `{ get; set; }` Properties | `RunResultBuilder` (intern, mutable) → `RunResult` (sealed record, immutable) |
| **LoserCount-Overwrite** | `result.LoserCount = moveResult.MoveCount` (Zeile ~302) | `PlannedLoserCount` und `ActualMoveCount` sind verschiedene Dinge |
| **CLI-eigene Games-Zählung** | `AllCandidates.Count(c => c.Category == "GAME")` | Nur `RunProjection.GameCount` lesen |
| **API-eigene Result-Mappings** | `ApiRunResult` mit manueller Feld-Auswahl, 6+ fehlende Felder | `ApiRunResult` liest aus `RunProjection` |
| **Drei HealthScore-Formeln** | GUI/CLI/Report berechnen jeweils eigenen Score | `ProjectionFactory.CalculateHealthScore` = einzige Quelle |
| **GetGameGroups private** | Filtert BIOS-Winner mit GAME-Losern durch | `DedupePhaseResult` hat explizite `GameGroups` / `BiosGroups` |
| **Convert vor Verify counter** | `converted++` vor Verify (P0-A) | Strikt: nur increment bei Verify-Success |
| **restoredPaths.Add außerhalb Guard** | `Rollback` zählt nicht-existente Restores (P1-12) | Add nur innerhalb `if (!dryRun && File.Exists && move.Success)` |

---

## 6. Migrationshinweise

### Phase 0: Sofort (nicht-blockierend, parallel machbar)

| Aufgabe | Dateien | Blockiert | Risiko |
|---------|---------|-----------|--------|
| `FileCategory` enum statt string in `RomCandidate.Category` | `RomCandidate.cs`, `RunOrchestrator.cs`, alle Tests | Nichts | **Niedrig** — suche-und-ersetze `"GAME"` → `FileCategory.Game` |
| `CandidateFactory` extrahieren aus `ScanFiles` | Neue Datei in Core, `RunOrchestrator.cs` | Nichts | **Niedrig** — pure Extraktion |
| Fix `restoredPaths.Add` Guard (P1-12) | `AuditCsvStore.cs` | Nichts | **Niedrig** — 1 Zeile verschieben |
| Fix Convert-Verify Zähllogik (P0-A) | `RunOrchestrator.cs` | Nichts | **Niedrig** — if/else statt sequentiell |

### Phase 1: RunResult immutable machen (blockierend für Phase 2)

| Aufgabe | Dateien | Blockiert | Risiko |
|---------|---------|-----------|--------|
| `RunResultBuilder` einführen | Neue Datei, `RunOrchestrator.cs` | **Phase 2** (Projection braucht immutable Result) | **Mittel** — Orchestrator-Interna ändern sich |
| `RunResult` zu sealed record | `RunResult` Definition in RunOrchestrator.cs | CLI, API, UI, Tests | **Mittel** — Breaking API-Change |
| `MovePhaseResult` Split (Junk/Dedupe) | `RunOrchestrator.cs`, `RunResult` | Tests | **Niedrig** — schon getrennt, formalisieren |

### Phase 2: RunProjection (blockierend für Phase 3)

| Aufgabe | Dateien | Blockiert | Risiko |
|---------|---------|-----------|--------|
| `RunProjection` + `ProjectionFactory` | Neue Dateien in Core/Contracts | **Phase 3** (Consumer müssen umgestellt werden) | **Niedrig** — additive Änderung |
| CLI umstellen auf RunProjection | `CLI/Program.cs` | Nichts | **Niedrig** |
| API umstellen auf RunProjection | `Api/RunManager.cs` | Nichts | **Niedrig** |
| Report umstellen auf RunProjection | `Reporting/RunReportWriter.cs` | Nichts | **Niedrig** |

### Phase 3: Phase-Handler extrahieren (parallel machbar)

| Aufgabe | Parallel | Risiko |
|---------|----------|--------|
| `ScanPhase` extrahieren | ✅ | **Niedrig** — Code-Move |
| `EnrichmentPhase` extrahieren | ✅ | **Niedrig** — Code-Move |
| `DedupePhase` extrahieren | ✅ | **Niedrig** — Code-Move |
| `JunkRemovalPhase` extrahieren | ✅ | **Niedrig** — Code-Move |
| `DedupeMovePhase` extrahieren | ✅ | **Niedrig** — Code-Move |
| `ConvertPhase` extrahieren | ✅ | **Niedrig** — Code-Move |
| `ConsoleSortPhase` extrahieren mit Audit | ✅ | **Mittel** — neuer Audit-Trail |

### Phase 4: DeduplicationEngine category-aware machen

| Aufgabe | Blockiert durch | Risiko |
|---------|-----------------|--------|
| `SelectWinner` Category-Filter | Phase 0 (FileCategory enum) | **Mittel** — Semantik-Change der Winner-Selection |
| BIOS GameKey-Prefix in CandidateFactory | Phase 0 (CandidateFactory) | **Niedrig** |

### Reihenfolge-Zusammenfassung

```
Phase 0 (parallel, sofort): FileCategory-enum, CandidateFactory, P0-A fix, P1-12 fix
    ↓
Phase 1 (blockiert Phase 2): RunResultBuilder, immutable RunResult
    ↓
Phase 2 (blockiert Phase 3): RunProjection, ProjectionFactory
    ↓
Phase 3 (parallel): Phase-Handler extrahieren
Phase 4 (parallel mit Phase 3): Category-aware SelectWinner
```

---

## Anhang: Invarianten-Vertrag

Jeder Build muss diese Invarianten bestehen (automatisiert via Tests):

| # | Invariante | Formel |
|---|-----------|--------|
| I-01 | Kein Duplikat-MainPath | `AllCandidates.Select(c => c.MainPath).Distinct().Count() == AllCandidates.Count` |
| I-02 | Summen-Integrität Dedupe | `Σ(1 + group.Losers.Count) für alle Gruppen == candidates mit nicht-leerem GameKey` |
| I-03 | GAME schlägt BIOS | `∀ group mit GAME+BIOS: Winner.Category == FileCategory.Game` |
| I-04 | Convert-Summe | `Converted + Errors + Skipped == Attempted` |
| I-05 | Move-Summe | `MoveCount + FailCount + SkipCount == PlannedMoves` |
| I-06 | Junk ≠ Dedupe | `JunkMoves ∩ DedupeMoves == ∅` |
| I-07 | Status-Korrektheit | `Outcome == Ok ↔ TotalErrorCount == 0` |
| I-08 | Determinismus | `∀ permutations(inputs): SelectWinner(perm) == SelectWinner(inputs)` |
| I-09 | Projection-Parität | `GUI.HealthScore == CLI.HealthScore == API.HealthScore == Report.HealthScore` |
| I-10 | Kein Unknown→Game | `∀ c in Candidates: c.Category != FileCategory.Unknown ∨ c.Category == FileCategory.Unknown` (explizit behandelt, nie zu Game) |
