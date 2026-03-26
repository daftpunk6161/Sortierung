# ADR-0011: Zielarchitektur Tests / Testbarkeit

**Status:** Proposed  
**Datum:** 2026-03-19  
**Kontext:** 136 Testdateien, 5.200+ xUnit-Tests, ADR-0004/0005/0010, FINAL_CONSOLIDATED_AUDIT.md  
**Scope:** Test-Seams, Pflicht-Invarianten, Cross-Output-Parity, Replay/Audit-Tests, Isolierbarkeit  
**Vorgänger:** ADR-0005 (Core Target), ADR-0010 (Safety/IO/Security Target)

---

## 1. Executive Design

### Problem

Die aktuelle Testsuite (5.200+ Tests, 136 Dateien) liefert eine breite Basis, hat aber **strukturelle Lücken**, die eine vollständige Regressionserkennung bei Kernlogik-Änderungen verhindern:

| # | Lücke | Risiko | Betrifft |
|---|-------|--------|----------|
| L-01 | **Core Set-Parser rufen `File.*` direkt auf** — nicht testbar ohne echtes Dateisystem | P1 | `CueSetParser`, `GdiSetParser`, `CcdSetParser`, `M3uPlaylistParser` |
| L-02 | **RunOptions-Konstruktion 3× unabhängig implementiert** — keine Parity-Garantie | P0 | `CliOptionsMapper`, `RunManager`, `RunService` |
| L-03 | **40+ Test-Doubles ohne gemeinsame Basis** — inkonsistente Semantik, Wartungslast | P2 | Alle `*Tests.cs` mit eigenen Fakes |
| L-04 | **Kein Snapshot-/Replay-Test für deterministische Reproduzierbarkeit** | P1 | `RunOrchestrator.Execute()` |
| L-05 | **Pipeline-Phasen binden statische Core-Methoden direkt** — Enrichment, Dedupe, Move nicht isolierbar | P2 | `EnrichmentPipelinePhase`, `DeduplicatePipelinePhase` |
| L-06 | **Nullable Dependencies im Orchestrator ungetestet** — null Converter + ConvertFormat = ? | P1 | `RunOrchestrator` constructor |
| L-07 | **IFormatConverter.Verify-Exception nicht gefangen** — orphaned files bei throw | P1 | `WinnerConversionPipelinePhase` |
| L-08 | **Report-Invariante wird bei ConvertOnly übersprungen** | P2 | `RunReportWriter` |
| L-09 | **ConsoleSort-Atomizität bei Exceptions ungetestet** | P2 | `ConsoleSorter` |
| L-10 | **Audit-Replay: kein End-to-End Roundtrip-Test** (Run → Audit → Rollback → Verify) | P1 | `AuditCsvStore`, `MovePipelinePhase` |

### Designprinzipien

1. **Jede fachliche Entscheidungsstufe muss isoliert testbar sein** — ohne Orchestrator, ohne Dateisystem, ohne andere Stufen.
2. **Cross-Output-Parität ist so wichtig wie Code-Korrektheit** — CLI/API/GUI müssen beweisbar identische Ergebnisse liefern.
3. **Testdoubles sind Infrastruktur, nicht Pattern-Duplikate** — gemeinsame Basisklassen statt 40 separate Implementierungen.
4. **Determinismus muss reproduzierbar abgesichert werden** — durch Snapshot-Tests, nicht nur durch Einzelassertions.
5. **Audit-Trail ist sicherheitskritisch** — muss als vollständiger Roundtrip (Write → Verify → Tamper → Detect → Rollback) getestet sein.

### Zielzustand — Test-Architektur-Schichten

```
┌─────────────────────────────────────────────────────────────────┐
│                    Journey / Parity Tests                       │
│  Identischer RunOptions-Input → CLI/API/GUI → Identische KPIs  │
├─────────────────────────────────────────────────────────────────┤
│                  Orchestrator Integration Tests                  │
│  DryRun↔Move Parität, Phase-Komposition, Cancellation          │
├────────────────┬────────────────────────────┬───────────────────┤
│ Phase Isolation │  Core Determinism Suite    │ Safety Red Tests  │
│ Enrichment     │  GameKey Snapshot          │ Path Traversal    │
│ Dedupe         │  Winner Snapshot           │ Zip-Slip          │
│ Move           │  Region Snapshot           │ Rollback Guards   │
│ Convert        │  Score Snapshot            │ CSV-Injection     │
│ Sort           │  Report Invariant          │ ADS-Rejection     │
├────────────────┴────────────────────────────┴───────────────────┤
│                    Shared Test Infrastructure                    │
│  TestFixtures.InMemoryFs, TestFixtures.TrackingAudit,          │
│  TestFixtures.NullConverter, TestFixtures.ScenarioBuilder       │
└─────────────────────────────────────────────────────────────────┘
```

---

## 2. Test-Seams

### 2.1 Existierende Seams (✅ OK)

| Seam | Interface | Adapter | Test-Double verfügbar |
|------|-----------|---------|----------------------|
| Dateisystem | `IFileSystem` | `FileSystemAdapter` | ✅ 19+ Varianten |
| Audit-Trail | `IAuditStore` | `AuditCsvStore` | ✅ 2+ Varianten |
| Formatkonvertierung | `IFormatConverter` | `FormatConverterAdapter` | ✅ 6 Varianten |
| Externe Tools | `IToolRunner` | `ToolRunnerAdapter` | ✅ 9 Varianten |
| Dialoge | `IDialogService` | WPF-Impl | ✅ 8 Varianten |
| DAT-Index | `IDatRepository` | `DatRepositoryAdapter` | ✅ vorhanden |
| App-State | `IAppState` | `AppStateStore` | ✅ vorhanden |

### 2.2 Fehlende Seams (🔴 zu schaffen)

| # | Fehlende Seam | Begründung | Maßnahme |
|---|--------------|------------|----------|
| S-01 | **`IFileReader` für Core Set-Parser** | `CueSetParser.GetRelatedFiles()` ruft `File.Exists()` / `File.ReadLines()` direkt auf. Core darf kein I/O enthalten (ADR-0005 §D-01). | **Neues Interface** `IFileReader` in Contracts mit `bool Exists(string)`, `IEnumerable<string> ReadLines(string)`. Set-Parser erhalten `IFileReader` per Parameter. |
| S-02 | **`ITimeProvider` für Orchestrator** | `DateTime.UtcNow` in `RunOrchestrator:304` verhindert deterministische Cancelled-Sidecar-Tests. | Interface `ITimeProvider` in Contracts mit `DateTimeOffset UtcNow { get; }`. Injection in RunOrchestrator. .NET 8+ `TimeProvider` als Alternative evaluieren. |
| S-03 | **`IEnvironment` für Settings/Safety** | 6+ direkte `Environment.GetFolderPath()` Aufrufe in Infrastructure. Kein Problem für Isolierbarkeit (Infrastructure = Adapter-Schicht), aber erschwert portable Tests. | Optionale Maßnahme — nur umsetzen wenn gezielt getestet werden soll. Priorität P3. |
| S-04 | **`IRunOptionsFactory` für Entry-Point-Parität** | RunOptions wird 3× unabhängig konstruiert (CLI: `CliOptionsMapper`, API: `RunManager` inline, WPF: `RunService`). Kein gemeinsamer Vertrag. | **Neues Interface** `IRunOptionsFactory` mit `RunOptions Build(RunOptionsRequest)` — einheitlicher Input-Typ für alle Entry Points. Siehe §2.3. |

### 2.3 RunOptions-Parität — Architekturentscheidung

**Ist-Zustand:**

```
CLI:  CliArgsParser → CliRunOptions → CliOptionsMapper.Map(cli, settings) → RunOptions
API:  RunRequest (HTTP) → RunManager inline new RunOptions{...}      → RunOptions
WPF:  MainViewModel  → RunService.BuildEnvironmentAndOptions()       → RunOptions  
```

Drei separate Baulogiken mit folgenden Divergenzen:

| Feld | CLI | API | WPF | Divergenz |
|------|-----|-----|-----|-----------|
| `ConvertFormat` | bool → `"auto"` | Direktkopie (string) | null-check → `"auto"` | 🔴 API empfängt Rohwert |
| `DatRoot` | Settings-Merge | Direktkopie | Null-Whitespace-Guard | 🟡 Whitespace-Handling |
| `TrashRoot` | Direktkopie | Direktkopie | Null-Whitespace-Guard | 🟡 Konsistenz |
| `AuditPath` | `ArtifactPathResolver` + Timestamp + GUID | `GetArtifactPaths()` | `ArtifactPathResolver` + bedingtes Format | 🟡 Pfadformat |
| `Extensions` | ❓ Undokumentiert für 0 Extensions | Direktkopie | Fallback `DefaultExtensions` | 🔴 Zero-Extension-Fall |
| `PreferRegions` | Settings-Merge | Direktkopie (Default: EU/US/WORLD/JP) | `vm.GetPreferredRegions()` | 🟡 Default-Divergenz |

**Zielzustand:**

```csharp
// In RomCleanup.Contracts oder RomCleanup.Infrastructure
public static class RunOptionsBuilder
{
    public static RunOptions Build(RunOptionsRequest request, RomCleanupSettings? settings = null)
    {
        // EINZIGE Stelle für: ConvertFormat-Normalisierung, Extension-Defaults,
        // Region-Merge, AuditPath-Generierung, Null-Guards
    }
}

// Jeder Entry Point mappt nur auf RunOptionsRequest:
//   CLI:  CliRunOptions  → RunOptionsRequest
//   API:  RunRequest     → RunOptionsRequest
//   WPF:  ViewModel      → RunOptionsRequest
```

**Pflichttest für diese Seam:**
```
Parametrisierter Test: Identische Eingabewerte über alle 3 Request-Typen → 
Identische RunOptions (Feld-für-Feld Vergleich)
```

---

## 3. Pflicht-Testarten

### 3.1 Core Determinism Snapshot Suite (🔴 NEU — P0)

**Zweck:** Fängt jede stille Verhaltensänderung in der Kernlogik ab.

**Prinzip:** Bekannter Input → bekannter Output. Snapshot-Dateien werden im Repo versioniert.

| Test-Scope | Methode | Input | Erwarteter Snapshot |
|------------|---------|-------|---------------------|
| **GameKey-Snapshots** | `GameKeyNormalizer.Normalize()` | 200+ Dateinamen (reale ROM-Namen) | Exakter GameKey pro Name |
| **Region-Snapshots** | `RegionDetector.GetRegionTag()` | 100+ Dateinamen | Exaktes Region-Tag |
| **Winner-Snapshots** | `DeduplicationEngine.Deduplicate()` | 50+ Gruppen à 3-5 Kandidaten | Exakter Winner pro Gruppe |
| **Score-Snapshots** | `FormatScorer.Get*()`, `VersionScorer.*` | 100+ (Extension, Name)-Paare | Exakter Score |
| **Classification-Snapshots** | `FileClassifier.Analyze()` | 200+ Dateinamen | Exakte Category |

**Format:** JSON-Datei pro Scope unter `src/RomCleanup.Tests/Snapshots/`:

```json
// Snapshots/gamekey-snapshots.json
[
  { "input": "Super Mario World (USA) (Rev 1).sfc", "expected": "super mario world" },
  { "input": "Sonic The Hedgehog (Europe).md", "expected": "sonic the hedgehog" }
]
```

**Test-Pattern:**

```csharp
[Theory]
[MemberData(nameof(LoadGameKeySnapshots))]
public void GameKeyNormalize_Snapshot_MatchesApproved(string input, string expected)
{
    Assert.Equal(expected, GameKeyNormalizer.Normalize(input));
}
```

**Update-Workflow:** Snapshots dürfen nur bewusst aktualisiert werden. Jede Diff-Zeile in einem Snapshot-File ist ein bewusster Review-Punkt im PR.

### 3.2 Cross-Output Parity Tests (🟡 ERWEITERN — P0)

**Existierend:**
- `PreviewExecuteParityTests.cs` — DryRun vs. Move KPI-Parität ✅
- `ReportParityTests.cs` — Report-Zahlen vs. RunResult ✅ (1 vorbestehender Fehler)
- `ApiRedPhaseTests.cs` — API-Felder vs. CLI ✅
- `CliRedPhaseTests.cs` — CLI-Output-Konsistenz ✅
- `KpiChannelParityBacklogTests.cs` — KPI-Backlog ✅

**Fehlend (Pflicht):**

| # | Test | Was er prüft | Warum Pflicht |
|---|------|-------------|---------------|
| P-01 | **RunOptions-Feld-Parität** | Gleicher User-Intent als CLI-Args / API-Request / WPF-VM → identisches `RunOptions`-Objekt | Eliminiert L-02 — beweist, dass die 3 Baulogiken konvergieren |
| P-02 | **RunResult-Snapshot-Parität** | `RunOrchestrator.Execute(fixedOptions)` → JSON-Snapshot von `RunResult` | Erkennt jede stille Änderung am Gesamtergebnis |
| P-03 | **RunProjection-Konsistenz** | `RunProjection` aus `RunResult` → Alle KPI-Felder addieren sich zu `TotalFiles` | Report-Invariante ist die letzte Verteidigungslinie |
| P-04 | **DryRun-Report ↔ Move-Report Zahlen-Identität** | DryRun-Report und Move-Report erzeugen exakt gleiche Dedup-Gruppen/Counts | Erkennt Divergenz zwischen Preview und Execution |

### 3.3 Replay / Audit Roundtrip Tests (🔴 NEU — P1)

**Zweck:** Beweist, dass der Audit-Trail als vollständiges Rollback-Medium funktioniert.

| # | Test | Ablauf | Assertion |
|---|------|--------|-----------|
| A-01 | **Full Roundtrip** | Scan → Dedupe → Move → Audit-CSV → Rollback(dryRun=false) → Verify | Alle Dateien am Originalplatz. Kein Datenverlust. |
| A-02 | **Tampered Audit Reject** | Move → Audit-CSV → CSV-Zeile ändern → Rollback | HMAC-Fehler. Kein Rollback. |
| A-03 | **Partial Rollback** | Move 10 Dateien → 5 Quellen löschen → Rollback | 5 restored, 5 skipped. Report korrekt. |
| A-04 | **Replay Determinismus** | 2× Execute mit identischem Input → 2× Audit-CSV vergleichen | Identische Zeilen (exklusive Timestamps). |
| A-05 | **Sidecar-Pflicht** | Move → `.meta.json` löschen → Rollback | Rollback verweigert (Guard aus ADR-0010). |

### 3.4 Phase-Isolation Tests (🟡 ERWEITERN — P1)

**Existierend:** `PipelinePhaseIsolationTests.cs` — 7 Tests ✅

**Fehlend (Pflicht):**

| # | Phase | Test | Was er prüft |
|---|------|------|-------------|
| PI-01 | Enrichment | **DatIndex-Konflikt** | DatIndex liefert ConsoleKey ≠ Filename-ConsoleKey → welcher gewinnt? |
| PI-02 | Enrichment | **Null-HashService-Graceful** | `hashService=null`, `EnableDat=true` → kein NPE, DatMatch=false |
| PI-03 | Move | **Audit-Skip bei FindRoot-Failure** | `loser.MainPath` außerhalb aller Roots → failCount++, **keine** Audit-Zeile |
| PI-04 | Move | **Audit-Skip bei MoveItemSafely-Null** | Move gibt null zurück → failCount++, **keine** Audit-Zeile |
| PI-05 | WinnerConversion | **Verify-Exception → Orphan** | `Verify()` wirft → konvertierte Datei bleibt liegen (kein Cleanup) |
| PI-06 | ConsoleSorter | **Exception-Atomizität** | Set-Member 2/3 Move wirft → Member 1 wird zurückgerollt |
| PI-07 | Report | **ConvertOnly-Projection** | ConvertOnly-Modus → Report-Invariante muss trotzdem gelten |

### 3.5 Null-Injection Boundary Tests (🔴 NEU — P1)

**Zweck:** Beweist, dass nullable Orchestrator-Dependencies kein undefiniertes Verhalten erzeugen.

```csharp
[Theory]
[InlineData(true,  null,  "Move")]   // ConvertFormat gesetzt, Converter null
[InlineData(false, null,  "DryRun")] // ConvertFormat null, Converter null (OK-Pfad)
[InlineData(true,  "fake", "Move")]  // SortConsole gesetzt, ConsoleDetector null
public void Execute_WithNullDependencies_NeverThrowsNPE(
    bool convertEnabled, string? convertFormat, string mode) { ... }
```

| Kombination | Erwartung |
|-------------|-----------|
| `converter=null` + `ConvertFormat="auto"` | Phase übersprungen, kein NPE |
| `consoleDetector=null` + `SortConsole=true` | Phase übersprungen, kein NPE |
| `hashService=null` + `EnableDat=true` | Enrichment läuft, DatMatch=false für alle |
| `datIndex=null` + `EnableDat=true` | Enrichment läuft, DatMatch=false für alle |

---

## 4. Zu entfernende Altlogik

### 4.1 Test-Double-Konsolidierung

**Problem:** 40+ Test-Double-Klassen verteilt über Testdateien. Viele implementieren dasselbe Interface mit minimalen Varianten. Jede Änderung an `IFileSystem` erfordert Anpassung in bis zu 19 Klassen.

**Maßnahme:**

Gemeinsame Test-Infrastructure in `src/RomCleanup.Tests/TestFixtures/`:

```
TestFixtures/
├── InMemoryFileSystem.cs       // Ersetzt 19 IFileSystem-Doubles
├── TrackingAuditStore.cs       // Ersetzt 2+ IAuditStore-Doubles  
├── ConfigurableConverter.cs    // Ersetzt 6 IFormatConverter-Doubles
├── StubToolRunner.cs           // Ersetzt 9 IToolRunner-Doubles
├── StubDialogService.cs        // Ersetzt 8 IDialogService-Doubles
├── ScenarioBuilder.cs          // Fluent Builder für PipelineContext + RunOptions
└── SnapshotLoader.cs           // JSON-Snapshot-Laden für Determinismus-Tests
```

**Konsolidierungsstrategie:**

| Schritt | Aktion | Risiko |
|---------|--------|--------|
| 1 | Gemeinsame Doubles erstellen mit **konfigurierbarem Verhalten** (`OnMove`, `OnDelete`, `ShouldFail`) | Keins — additiv |
| 2 | Neue Tests nutzen ausschließlich gemeinsame Doubles | Keins — additiv |
| 3 | Bestehende Tests **pro Datei** auf gemeinsame Doubles umstellen | Mittel — einzeln migrieren, nach jedem File `dotnet test` |
| 4 | Alte Doubles löschen, wenn keine Referenz mehr | Niedrig — Compiler fängt dangling references |

**Nicht löschen:** Test-Doubles mit echtem Spezialverhalten (z.B. `FailingMoveFileSystem` mit positionsabhängigem Fehler). Diese werden zu konfigurierbaren Lambdas im `InMemoryFileSystem`.

### 4.2 Legacy-Test-Dateien — Bereinigung

| Datei | Status | Maßnahme |
|-------|--------|----------|
| `V1TestGapTests.cs` | Migrationslücken-Tests aus PowerShell-Ära | Prüfen ob alle durch typspezifische Tests ersetzt → dann entfernen |
| `V2RemainingTests.cs` | "Restposten"-Tests ohne klare Zuordnung | Einem Owner-File zuordnen oder entfernen |
| `CoverageBoostPhase1-9Tests.cs` | 9 Dateien, entstanden als Boost-Kampagne | Tests in Owner-Files umziehen, Boost-Dateien leeren |

**Nicht löschen:** Keine Testdatei löschen, deren Tests nicht nachweislich durch spezifischere Tests ersetzt sind. Coverage-Vergleich vor/nach ist Pflicht.

### 4.3 Core File-I/O — Bereinigung

**Betroffene Dateien:**
- `src/RomCleanup.Core/SetParsing/CueSetParser.cs`
- `src/RomCleanup.Core/SetParsing/GdiSetParser.cs`
- `src/RomCleanup.Core/SetParsing/CcdSetParser.cs`
- `src/RomCleanup.Core/SetParsing/M3uPlaylistParser.cs`
- `src/RomCleanup.Core/SetParsing/MdsSetParser.cs`

**Ist-Zustand:** Direkter Zugriff auf `File.Exists()`, `File.ReadLines()`, `Path.GetFullPath()`.

**Maßnahme:**

```csharp
// NEU in RomCleanup.Contracts/Ports/IFileReader.cs
public interface IFileReader
{
    bool FileExists(string path);
    IEnumerable<string> ReadLines(string path);
}

// Set-Parser VORHER:
public static IReadOnlyList<string> GetRelatedFiles(string cuePath)
{
    if (!File.Exists(cuePath)) return Array.Empty<string>();
    foreach (var line in File.ReadLines(cuePath)) ...
}

// Set-Parser NACHHER:
public static IReadOnlyList<string> GetRelatedFiles(string cuePath, IFileReader reader)
{
    if (!reader.FileExists(cuePath)) return Array.Empty<string>();
    foreach (var line in reader.ReadLines(cuePath)) ...
}
```

**Migrationsstrategie:**
1. `IFileReader` in Contracts anlegen
2. `FileReaderAdapter : IFileReader` in Infrastructure anlegen (delegiert an `System.IO.File`)
3. Set-Parser-Signaturen um `IFileReader`-Parameter erweitern
4. Alle Aufrufer (EnrichmentPipelinePhase, ScanPipelinePhase) anpassen
5. Tests schreiben, die `IFileReader`-Mock verwenden
6. Bestehende Tests anpassen

**Abwärtskompatibilität:** Optional: Overload ohne `IFileReader` beibehalten, der intern `FileReaderAdapter` nutzt. Markiert als `[Obsolete]`.

---

## 5. Migrationshinweise

### 5.1 Priorisierte Reihenfolge

| Phase | Was | Warum zuerst | Aufwand | Risiko |
|-------|-----|-------------|---------|--------|
| **M-01** | Shared Test Doubles (`TestFixtures/`) | Fundament für alle weiteren Tests. Kein Refactoring an Produktionscode. | XS | Keins |
| **M-02** | Snapshot-Tests (§3.1) | Erkennt Regressionen in Core-Logik sofort. Kein Refactoring an Produktionscode. | S | Keins |
| **M-03** | Null-Injection-Tests (§3.5) | Dokumentiert aktuelles Verhalten, deckt NPE-Risiken auf. Kein Refactoring nötig. | S | Keins |
| **M-04** | Phase-Isolation-Tests (§3.4 Erweiterung) | Deckt bekannte Lücken PI-01 bis PI-07 ab. Kein Refactoring nötig. | S | Keins |
| **M-05** | Audit Roundtrip Tests (§3.3) | Beweist Kein-Datenverlust-Invariante E2E. Kein Refactoring nötig. | M | Keins |
| **M-06** | `IFileReader`-Seam in Core (§4.3) | Beseitigt Core-I/O-Verletzung. Produktionscode-Änderung. | M | Mittel — Signaturänderungen |
| **M-07** | `RunOptionsBuilder` Vereinheitlichung (§2.3) | Beseitigt RunOptions-Divergenz. Produktionscode-Änderung in 3 Entry Points. | L | Hoch — alle Entry Points betroffen |
| **M-08** | Test-Double-Konsolidierung (§4.1) | Wartbarkeit. Kein fachliches Risiko, aber hoher mechanischer Aufwand. | L | Niedrig |
| **M-09** | Legacy-Test-Bereinigung (§4.2) | Hygiene. Nur nach Coverage-Nachweis. | M | Mittel — muss Coverage halten |

### 5.2 Invarianten-Register

Folgende Invarianten **müssen** durch Tests abgesichert sein. Jede Verletzung ist ein Release-Blocker:

| ID | Invariante | Test-Scope | Pflicht-Testdatei |
|----|-----------|------------|-------------------|
| **INV-01** | GameKey-Determinismus | `GameKeyNormalizer.Normalize(x)` gibt für identisches `x` immer identisches Ergebnis | `GameKeyNormalizerTests.cs` + Snapshot |
| **INV-02** | Winner-Determinismus | `DeduplicationEngine.Deduplicate(list)` gibt bei gleicher Liste gleichen Winner | `DeduplicationEngineTests.cs` + Snapshot |
| **INV-03** | Region-Determinismus | `RegionDetector.GetRegionTag(name)` ist stabil | `RegionDetectorTests.cs` + Snapshot |
| **INV-04** | Score-Determinismus | Alle Scorer liefern bei gleichem Input gleichen Output | `FormatScorerTests.cs` + `VersionScorerTests.cs` + Snapshot |
| **INV-05** | Preview ↔ Execute Parität | DryRun und Move erzeugen identische Dedup-Gruppen und -Counts | `PreviewExecuteParityTests.cs` |
| **INV-06** | KPI-Additivität | `projection.Keep + projection.Dupes + projection.Junk + projection.Unknown + projection.FilteredNonGameCount == projection.TotalFiles` | `RunProjectionFactoryTests.cs` |
| **INV-07** | Kein Move außerhalb Root | `MoveItemSafely` mit Ziel außerhalb aller Roots → `null` | `FileSystemAdapterTests.cs` |
| **INV-08** | Kein leerer GameKey | `GameKeyNormalizer.Normalize(anything)` → nie `""` oder `null` | `GameKeyNormalizerTests.cs` |
| **INV-09** | Audit-Vollständigkeit | Jede Move-Operation erzeugt exakt eine Audit-Zeile | `MovePhaseAuditInvariantTests.cs` |
| **INV-10** | Rollback-Sicherheit | Rollback stellt nur Dateien nach Root-Containment + HMAC-Verifikation wieder her | `AuditCsvStoreTests.cs` + `SafetyIoRecoveryTests.cs` |
| **INV-11** | CSV-Injection-Schutz | Kein Audit-/Report-Feld beginnt mit `=`, `+`, `-`, `@`, `\t`, `\r` | `AuditSigningServiceTests.cs` + `SecurityTests.cs` |
| **INV-12** | Cross-Entry-Point RunOptions-Äquivalenz | Gleicher User-Intent → gleiche RunOptions über alle Entry Points | `RunOptionsParityTests.cs` (NEU) |

### 5.3 CI-Pipeline-Integration

```yaml
# Ergänzung zu .github/workflows/test-pipeline.yml

  snapshot-check:
    name: Snapshot Determinism Gate
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - run: dotnet test src/RomCleanup.Tests --filter "Category=Snapshot" --nologo
        # Schlägt fehl wenn Snapshot-Files geändert aber nicht committed

  invariant-check:
    name: Invariant Regression Gate
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - run: dotnet test src/RomCleanup.Tests --filter "Category=Invariant" --nologo
        # Alle INV-01 bis INV-12 müssen grün sein
```

### 5.4 Wann ist die Migration abgeschlossen?

| Kriterium | Zielwert |
|-----------|----------|
| Alle 12 Invarianten (INV-01 bis INV-12) durch Tests abgesichert | 12/12 |
| Core-Schicht hat keine direkten `File.*` / `Directory.*` Aufrufe | 0 Violations |
| RunOptions wird nur über `RunOptionsBuilder` konstruiert | 1 Baustelle statt 3 |
| Shared Test Doubles in `TestFixtures/` | 5+ gemeinsame Klassen |
| Snapshot-Tests für GameKey, Region, Winner, Score, Classification | 5 Snapshot-Dateien |
| Audit-Roundtrip-Test (A-01 bis A-05) grün | 5/5 |
| Alle Entry Points konvergieren über `RunOptionsRequest` | 3/3 |
| Legacy-Boost-Tests in Owner-Files migriert | 9 Dateien aufgelöst |

### 5.5 Risiken und Gegenmaßnahmen

| Risiko | Wahrscheinlichkeit | Impact | Gegenmaßnahme |
|--------|-------------------|--------|----------------|
| Set-Parser-Signaturänderung bricht interne Aufrufer | Hoch | Mittel | Abwärtskompatiblen Overload beibehalten, `[Obsolete]` markieren |
| RunOptionsBuilder-Migration bricht Entry Points | Mittel | Hoch | Feature-Branch pro Entry Point. Parität-Tests als Gate. |
| Test-Double-Konsolidierung ändert Testverhalten | Niedrig | Niedrig | 1 File pro PR, `dotnet test` Gate, Coverage-Diff |
| Snapshot-Files werden zu groß für Reviews | Niedrig | Niedrig | Max 200 Einträge pro Snapshot. Separate Review-Datei. |

---

## Anhang A: Statische Core-Klassen — Testbarkeits-Bewertung

| Klasse | Mockbar? | Testbar? | Aktion nötig? |
|--------|----------|----------|---------------|
| `GameKeyNormalizer` | Nein (statisch) | ✅ Ja — deterministisch, reiner Input→Output | Nein — Snapshot reicht |
| `RegionDetector` | Nein (statisch) | ✅ Ja — deterministisch | Nein — Snapshot reicht |
| `DeduplicationEngine` | Nein (statisch) | ✅ Ja — deterministisch | Nein — Snapshot reicht |
| `FormatScorer` | Nein (statisch) | ✅ Ja — deterministisch | Nein — Snapshot reicht |
| `VersionScorer` | Nein (statisch) | ✅ Ja — deterministisch | Nein — Snapshot reicht |
| `HealthScorer` | Nein (statisch) | ✅ Ja — deterministisch | Nein — Snapshot reicht |
| `CompletenessScorer` | Nein (statisch) | ✅ Ja — deterministisch | Nein — Snapshot reicht |
| `FileClassifier` | Nein (statisch) | ✅ Ja — deterministisch | Nein — Snapshot reicht |
| `CandidateFactory` | Nein (statisch) | ✅ Ja — deterministisch | Nein |
| `RuleEngine` | Nein (statisch) | ✅ Ja — deterministisch | Nein |
| `ConsoleDetector` | Nein (sealed) | ✅ Ja — instanziierbar | Nein |
| Set-Parser (`Cue/Gdi/Ccd/M3u/Mds`) | Nein (statisch) | 🔴 Nein — I/O-abhängig | **Ja — IFileReader-Seam (S-01)** |

**Bewertung:** Die statischen Core-Klassen sind **absichtlich** nicht-mockbar (ADR-0005 §D-01). Ihre Determinismus-Garantie eliminiert den Bedarf an Mocking — Snapshot-Tests sind die korrekte Absicherungsstrategie. Einzige Ausnahme: Set-Parser benötigen `IFileReader`-Paramter.

---

## Anhang B: Test-Double-Inventar (Konsolidierungskandidaten)

| Interface | Aktuelle Doubles | Konsolidierungsziel |
|-----------|-----------------|---------------------|
| `IFileSystem` | 19 Klassen in 12 Dateien | 1 `InMemoryFileSystem` mit konfigurierbarem Verhalten |
| `IFormatConverter` | 6 Klassen in 4 Dateien | 1 `ConfigurableConverter` mit Lambda-Hooks |
| `IToolRunner` | 9 Klassen in 6 Dateien | 1 `StubToolRunner` mit konfigurierbarem Return |
| `IDialogService` | 8 Klassen in 5 Dateien | 1 `StubDialogService` mit konfigurierbaren Responses |
| `IAuditStore` | 2+ Klassen | 1 `TrackingAuditStore` mit Row-Recording + Flush-Tracking |
| `ISettingsService` | 5 Klassen | 1 `InMemorySettingsService` |
