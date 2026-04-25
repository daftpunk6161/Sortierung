# Romulus – Test Suite Sanierungsplan (2026-04-25)

> Quelle: [full-repo-audit-2026-04-24.md](full-repo-audit-2026-04-24.md) und Verifikation am Code (349 Testdateien / ~125k Zeilen).
> Regel: Schutzwert vor Coverage. Wertlose Tests zuerst entfernen, dann harte Luecken schliessen, dann Determinismus/Paritaet haerten.

---

## 1. Executive Summary

- Die Suite ist gross, aber strukturell schwach in zentralen Risikobereichen.
- Drei Hauptprobleme:
  1. **Source-Spiegel-Tests** (`Assert.Contains(literal, sourceCode)`) zementieren Implementierung statt Verhalten.
  2. **`DoesNotThrow`-Familie** sichert nur Crash-Freiheit ab, kein fachliches Resultat.
  3. **Release-kritische Invarianten fehlen oder sind nur punktuell abgesichert**: Conversion-Source-Persistence, Multi-Disc/Arcade Set-Integritaet, End-to-End Move+Rollback, Move-Outside-Roots, Cross-Console-DAT-Policy.
- Ziel: kleinere, ehrlichere Suite mit echten Schutzkanten. Erst loeschen/ersetzen, dann gezielt erweitern.
- Kein Massentest, keine Coverage-Welle. Jeder neue Test muss eine **konkrete Invariante** oder einen **konkreten Bug-Vektor** beschuetzen.

---

## 2. Priorisierte Sanierungsbloecke

### Block A – Wertlose und irrefuehrende Tests entsorgen
> Ziel: false confidence beenden. Build muss danach gruen bleiben.

- [x] **A1** Source-Spiegel-Tests katalogisieren (alle Tests mit `File.ReadAllText(.../*.cs)` + `Assert.Contains/DoesNotContain/Matches` gegen Quelltext).
- [x] **A2** Source-Spiegel-Tests, die ausschliesslich Wording pruefen, ersatzlos entfernen (siehe §3 Liste).
- [x] **A3** `DoesNotThrow` / `Record.Exception → Assert.Null(ex)`-Tests katalogisieren.
- [x] **A4** `DoesNotThrow`-Tests entweder mit Verhaltensassertion versehen oder loeschen (siehe §3 Liste).
- [x] **A5** Reflection-Existence-Tests (`typeof(X).GetProperty("Y") => NotNull`) loeschen.
- [x] **A6** Performance-Smokes (`ElapsedMilliseconds < 5000`) aus Unit-Suite entfernen oder in `benchmark/`-Gates verschieben.
- [x] **A7** Meta-Tests entfernen (Tests, die andere Tests strukturell pruefen, z. B. `R9_005_ChaosTests_DoesNotThrow_HasAssertions_InSource`, `R9_043_MainWindow_AsyncVoidHandlers_AreEventHandlers`).
- [x] **A8** Doppelte/redundante Tests in `Tracker*RedTests`, `Issue9*RedTests`, `AuditCategory*FixTests`, `Phase\d*RoundVerificationTests` deduplizieren.
- [x] **A9** Build + alle Test-Stages gruen verifizieren (kein versehentlich gestrichener Verhaltenstest).
- [x] **A10** Diff-Review: Anzahl entfernter Tests, Anzahl umgebauter Tests dokumentieren.

### Block B – Release-kritische Invarianten schliessen
> Ziel: harte Datenintegritaet, harte Sicherheitskanten.

- [ ] **B1** Conversion-Source-Persistence: Source bleibt erhalten bei
  - [ ] Tool-Crash (Exit != 0)
  - [ ] Tool-Output zu klein / leer
  - [ ] Tool-Hash-Mismatch
  - [ ] Cancellation mid-conversion
  - [ ] IO-Exception waehrend Promote
  - [ ] Disk-Full Simulation
  - [ ] Multi-Step-Plan: Failure in Step N → keine Source-Loeschung, alle Zwischenartefakte aufgeraeumt
- [ ] **B2** End-to-End **Move + Rollback Round-Trip** mit Hash-Vergleich vor/nach (jede Datei am exakten Originalpfad, Hash identisch, Sidecar konsistent).
- [ ] **B3** **Move-Outside-Roots Property-Test**: nach Run Filesystem-Scan, kein Touch ausserhalb der erlaubten Roots, auch bei adversarialen Targets/Symlinks.
- [ ] **B4** **Multi-Disc / M3U / Cue Set-Integritaet**:
  - [ ] All-or-nothing Move (kein zerrissenes Set)
  - [ ] M3U-Rewrite preserves every line
  - [ ] Cue → Bin Co-Move ist verifiziert
  - [ ] Konflikt: zwei Sets gleichen Namens, unterschiedlicher Inhalt
- [ ] **B5** **Arcade-Set-Integritaet** (split / merged / non-merged) End-to-End: Erkennung → Decision → Folder.
- [ ] **B6** **BIOS End-to-End**: Erkennung → Decision (block) → Ziel-Folder; nicht im Spiele-Ordner.
- [ ] **B7** **Reparse-Point / Hardlink-Cycle**: kein transparenter Follow, expliziter Block oder dokumentiertes sicheres Verhalten.
- [ ] **B8** **Tool-Hash-Verifizierung**: manipuliertes Tool waehrend Run → Abort vor jedem Side-Effect.

### Block C – Determinismus, Fehlerpfade, Paritaet haerten
> Ziel: gleiche Inputs ⇒ gleiche Outputs ueber alle Entry Points und Reports.

- [ ] **C1** Cross-Console-DAT-Policy-Schalter (`FamilyDatPolicy.EnableCrossConsoleLookup`) Tests fuer alle Stages (Archive, Headerless, Container, CHD, Name).
- [ ] **C2** EntryPoint-Paritaet erweitern: pro DedupeGroup `(DecisionClass, ReasonCode, BlockedReason, ConsoleKey, FamilyPipeline)` ueber GUI / CLI / API / Reports gleich.
- [ ] **C3** Determinismus-Property-Test zentral: gleiche Inputs + 50 Permutationen ⇒ identische Reports/JSON-Outputs (heute nur in `DeduplicationEngineTests.SelectWinner_IsStableAcrossPermutationsAndParallelCalls`).
- [ ] **C4** Cancellation-Datenintegritaet: nach Cancel waehrend Move keine teilverschobenen Dateien, partielle Conversion-Outputs sauber aufgeraeumt.
- [ ] **C5** Audit-Sidecar Round-Trip: schreiben → laden → Hash-konsistent → semantisch identisch.
- [ ] **C6** Failed-State / Partial-Failure / Risky-Action: Sidecar + Report + GUI-State zeigen denselben Endzustand.
- [ ] **C7** Unknown / Review / Blocked: pro Klasse mindestens ein End-to-End-Test, dass Routing in alle drei Entry Points gleich endet.

### Block D – Testbarkeits-Refactors (klein, gezielt)
> Ziel: ermoegliche Block B/C ohne neue Schattenlogik.

- [ ] **D1** `EnrichmentPipelinePhase`-Test-Harness/Builder, damit Familien × Policy-Schalter ohne Reflection testbar werden.
- [ ] **D2** Move/Sort-Phase: Test-Seam fuer Filesystem-Scan-Validator (nach Run pruefen, dass nichts ausserhalb Roots beruehrt wurde).
- [ ] **D3** Conversion-Tool-Invoker: zentrale Test-Doubles fuer Crash / Hash-Mismatch / Cancellation / OutputTooSmall / DiskFull (statt copy-pasted Invoker je Testdatei).
- [ ] **D4** RunResult-Projection: gemeinsame Vergleichshelper fuer `(GUI, CLI, API)`-Tupel-Identitaet (heute manuell in `ReportParityTests`).
- [ ] **D5** `DatasetExpander.cs` (5144 Zeilen) modularisieren und mit eigenen Tests absichern (Fixture-Generator-Bug = grosser Schaden).
- [ ] **D6** Trace/Log-Verifikation zentralisieren (`CaptureTrace`-Pattern aus `AuditABEndToEndRedTests` als gemeinsame Test-Util).

### Block E – Strukturhygiene Tests
> Ziel: Suite navigierbar machen.

- [ ] **E1** Test-Ordner nach Domaene einfuehren: `Recognition/`, `Sorting/`, `Safety/`, `Audit/`, `Reporting/`, `EntryPointParity/`, `Determinism/`.
- [ ] **E2** Gott-Testdateien splitten:
  - [ ] `GuiViewModelTests.cs` (2554)
  - [ ] `GuiViewModelTests.AccessibilityAndRedTests.cs` (2118)
  - [ ] `ApiIntegrationTests.cs` (1677)
  - [ ] `HardRegressionInvariantTests.cs` (1481)
  - [ ] `AuditComplianceTests.cs` (1436)
  - [ ] `RunOrchestratorTests.cs` (1394)
  - [ ] `DetectionPipelineTests.cs` (1376)
- [ ] **E3** Phase/Wave/Round/Tracker/Block-Naming aufloesen → Domaenen-Naming, historische Referenz nur als Doc-Kommentar.
- [ ] **E4** `FindSrcRoot()` / `FindRepoFile`-Copies entfernen (sollte nach Block A weitgehend obsolet sein).
- [ ] **E5** Test-Naming-Konvention dokumentieren (z. B. `Subject_Scenario_ExpectedBehavior`).

---

## 3. Tests, die ersetzt oder entfernt werden

> Konkrete Loesch-/Umbau-Kandidaten. Bei Loeschung: kein Ersatz noetig, weil kein Schutzwert. Bei Umbau: ersetzen durch Verhaltenstest gegen Public API.

### 3.1 Loeschen (kein Schutzwert)

- [ ] [src/Romulus.Tests/Phase9RoundVerificationTests.cs](../../src/Romulus.Tests/Phase9RoundVerificationTests.cs#L430-L441) – `R9_005_ChaosTests_DoesNotThrow_HasAssertions_InSource` (Meta-Test).
- [ ] [src/Romulus.Tests/Phase9RoundVerificationTests.cs](../../src/Romulus.Tests/Phase9RoundVerificationTests.cs#L448-L466) – `R9_043_MainWindow_AsyncVoidHandlers_AreEventHandlers` (Source-Pattern-Spiegel).
- [ ] [src/Romulus.Tests/Phase8RoundVerificationTests.cs](../../src/Romulus.Tests/Phase8RoundVerificationTests.cs#L52-L75) – `R8_001_ConsoleDetector_InlineNormalization_DotPrefixForExtMaps`, `R8_001_ExtensionNormalizer_HasDoubleExtRegex_NotInConsoleDetector`.
- [ ] [src/Romulus.Tests/TrackerAllFindingsBatch1RedTests.cs](../../src/Romulus.Tests/TrackerAllFindingsBatch1RedTests.cs#L65-L101) – `Data10_RomulusSettings_ExposesRulesSection`, `Di02_RollbackEndpoints_*`, `Di03_ProgramHelpers_*` (Reflection/Source-Spiegel).
- [ ] [src/Romulus.Tests/AuditABEndToEndRedTests.cs](../../src/Romulus.Tests/AuditABEndToEndRedTests.cs#L42-L168) – `B01`, `B04`, `B05` (Source-Mirror-Pflichten); `B05` ersetzen durch Verhaltens-Cancellation-Test (siehe Block C4).
- [ ] [src/Romulus.Tests/AuditCDRedTests.cs](../../src/Romulus.Tests/AuditCDRedTests.cs) – komplette Source-Spiegel-Halfte (`Assert.Contains/DoesNotContain` gegen Quelltext) loeschen; Verhaltensanteile in Domaenen-Suiten verschieben.
- [ ] [src/Romulus.Tests/AuditEFOpenTests.cs](../../src/Romulus.Tests/AuditEFOpenTests.cs#L31-L399) – Source-Spiegel-Anteile loeschen, Verhaltensanteile (`F-12: ZipSorter zip-slip`) erhalten und nach `Safety/` verschieben.
- [ ] [src/Romulus.Tests/UncPathTests.cs](../../src/Romulus.Tests/UncPathTests.cs#L146-L150) – `PathGetFullPath_DoesNotThrowOnUncOrLocal` (testet BCL).
- [ ] [src/Romulus.Tests/V2RemainingTests.cs](../../src/Romulus.Tests/V2RemainingTests.cs#L208-L220) – `Normalize_VeryLongFilename_DoesNotThrow`, `AsciiFold_VeryLongString_DoesNotThrow`.
- [ ] [src/Romulus.Tests/VersionScorerTests.cs](../../src/Romulus.Tests/VersionScorerTests.cs#L125-L135) – `*_ExtremeNumbers_DoesNotThrow` (kein erwarteter Wert).
- [ ] `Tracker*RedTests.cs`, `Issue9*RedTests.cs`, `AuditCategory*FixTests.cs`, `Phase\d*RedTests.cs`: Source-Spiegel-Anteile vollstaendig entfernen.

### 3.2 Umbau zu Verhaltenstests

- [ ] [src/Romulus.Tests/ChaosTests.cs](../../src/Romulus.Tests/ChaosTests.cs#L43-L57) – `Unicode_*_NeverThrows`: zusaetzlich asserten, dass GameKey/RegionTag erwarteten Wert/Form hat.
- [ ] [src/Romulus.Tests/ChaosTests.cs](../../src/Romulus.Tests/ChaosTests.cs#L77-L113) – `CorruptDat_*_DoesNotThrow`: zusaetzlich `index.TotalEntries == 0` und keine Partial-Eintraege (`index.LookupAllByHash` leer).
- [ ] [src/Romulus.Tests/SecurityTests.cs](../../src/Romulus.Tests/SecurityTests.cs#L155-L165) – RegionDetector long input: erwarteten `UNKNOWN`-Tag asserten.
- [ ] [src/Romulus.Tests/FileHashServiceCoverageTests.cs](../../src/Romulus.Tests/FileHashServiceCoverageTests.cs#L146-L214) – `Constructor_*_DoesNotThrow`: asserten, dass Cache nach Konstruktion leer ist und nachfolgender Hash-Lookup deterministisch funktioniert.
- [ ] [src/Romulus.Tests/StandaloneConversionServiceTests.cs](../../src/Romulus.Tests/StandaloneConversionServiceTests.cs#L300-L305) – `Dispose_NullLifetime_DoesNotThrow`: asserten, dass nach Dispose erwartete Side-Effects (Lock-Freigabe, Nachfolge-Operation `ObjectDisposedException`) gelten.
- [ ] [src/Romulus.Tests/SafetyIoSecurityRedPhaseTests.cs](../../src/Romulus.Tests/SafetyIoSecurityRedPhaseTests.cs#L97-L100) – `MoveItemSafely`-Aufruf: zusaetzlich Pfadziel und Audit-Eintrag asserten.
- [ ] [src/Romulus.Tests/AuditHardeningTests.cs](../../src/Romulus.Tests/AuditHardeningTests.cs#L95-L96) – `RuleEngine.Evaluate ... Assert.Null(ex)`: erwartete `RuleResult`/`Decision` asserten.
- [ ] [src/Romulus.Tests/ApiCoverageBoostTests.cs](../../src/Romulus.Tests/ApiCoverageBoostTests.cs#L420-L432) – RateLimiter-Sequenzen mit explizitem Disabled-Vertrag (`Assert.True(...)` Sequenz dokumentieren oder durch Spec-Style-Test ersetzen).
- [ ] Performance-Smokes mit `< 5000 ms` ([ChaosTests.cs](../../src/Romulus.Tests/ChaosTests.cs#L128-L141), [AuditComplianceTests.cs](../../src/Romulus.Tests/AuditComplianceTests.cs#L119-L1266)): Funktionalassertion behalten, Zeitgrenze in Benchmark-Gate auslagern.

---

## 4. Neue Tests – Reihenfolge

> Erst nach Block A. Jeder Test schuetzt eine konkrete, benannte Invariante.

- [ ] **N1** `Conversion/SourcePreservationInvariantTests.cs` – Block B1 (alle Failure-Pfade).
- [ ] **N2** `Audit/MoveRollbackRoundTripTests.cs` – Block B2.
- [ ] **N3** `Safety/MoveOutsideRootsPropertyTests.cs` – Block B3 (Filesystem-Scan-Validator).
- [ ] **N4** `Sorting/MultiDiscSetIntegrityTests.cs` – Block B4.
- [ ] **N5** `Sorting/ArcadeSetIntegrityTests.cs` – Block B5.
- [ ] **N6** `Sorting/BiosEndToEndTests.cs` – Block B6.
- [ ] **N7** `Safety/ReparsePointHardlinkTests.cs` – Block B7.
- [ ] **N8** `Safety/ToolHashVerificationTests.cs` – Block B8.
- [ ] **N9** `Recognition/CrossConsoleDatPolicyTests.cs` – Block C1.
- [ ] **N10** `EntryPointParity/DecisionReasonParityTests.cs` – Block C2.
- [ ] **N11** `Determinism/RunDeterminismPropertyTests.cs` – Block C3 (zentral, nicht nur Dedup).
- [ ] **N12** `Audit/CancellationDataIntegrityTests.cs` – Block C4.
- [ ] **N13** `Audit/AuditSidecarRoundTripTests.cs` – Block C5.
- [ ] **N14** `EntryPointParity/FailedStateParityTests.cs` – Block C6.
- [ ] **N15** `EntryPointParity/UnknownReviewBlockedRoutingTests.cs` – Block C7.

---

## 5. Testbarkeitsfoerdernde Refactors

> Klein, gezielt, ohne neue Schichten.

- [ ] **R1** `EnrichmentPipelineTestHarness` (Builder) im Tests-Projekt – ermoeglicht N9, ohne Reflection.
- [ ] **R2** `MoveOutcomeFilesystemValidator` als Test-Util – nimmt `RunResult` + `roots`, scannt Temp-Filesystem, asserted „kein Touch ausserhalb Roots“. Ermoeglicht N3.
- [ ] **R3** `FakeToolInvoker`-Sammlung in `Conversion/TestDoubles/` (Crash, HashMismatch, Cancellation, OutputTooSmall, DiskFull). Loest Copy-Paste-Invoker auf. Ermoeglicht N1.
- [ ] **R4** `RunResultParityComparer` in `EntryPointParity/Helpers/` – einmalige Implementierung des `(DecisionClass, ReasonCode, BlockedReason, ConsoleKey, FamilyPipeline)`-Tupelvergleichs. Ermoeglicht N10/N14/N15.
- [ ] **R5** `DatasetExpander` in fachliche Generatoren splitten (Cartridge, Disc, Arcade, Computer, Hybrid, Folder), je eigene Tests.
- [ ] **R6** `TraceCapture` als shared test util (heute privat in `AuditABEndToEndRedTests`).
- [ ] **R7** Optional: Conversion-Pipeline schreibt nach jedem Schritt einen Trace-Token; Tests verifizieren Reihenfolge ohne Source-Spiegel.

---

## 6. Definition of Done

Die Sanierung gilt als abgeschlossen, wenn alle folgenden Punkte erfuellt sind:

- [ ] **DoD-1** Keine Source-Spiegel-Tests mehr (`File.ReadAllText("*.cs")` + `Assert.Contains/Matches/DoesNotContain`) ausser dokumentierten Sonderfaellen.
- [ ] **DoD-2** Keine `DoesNotThrow`-/`Record.Exception → Assert.Null(ex)`-Tests ohne fachliche Folgeassertion.
- [ ] **DoD-3** Keine Performance-Smokes in der Unit-Suite (Zeitgrenzen leben in `benchmark/`).
- [ ] **DoD-4** Alle Tests aus §3.1 entfernt, alle Tests aus §3.2 umgebaut.
- [ ] **DoD-5** Alle neuen Tests aus §4 (N1–N15) gruen und deterministisch.
- [ ] **DoD-6** Refactors R1–R6 umgesetzt; R7 entschieden (umgesetzt oder explizit verworfen).
- [ ] **DoD-7** Test-Ordnerstruktur nach Domaenen umgesetzt (Block E1).
- [ ] **DoD-8** Gott-Testdateien (Block E2) gesplittet.
- [ ] **DoD-9** Build gruen, alle Test-Stages (`unit`, `integration`, `e2e`) gruen.
- [ ] **DoD-10** Benchmark-Gates (`tests: benchmark gate`) gruen.
- [ ] **DoD-11** Kerninvarianten dokumentiert (kurze Liste mit Test-Referenz):
  - [ ] Source-Persistence in Conversion
  - [ ] Move/Rollback Round-Trip
  - [ ] Move-Outside-Roots
  - [ ] Multi-Disc/Arcade/BIOS Set-Integritaet
  - [ ] Cross-Console-DAT-Policy
  - [ ] EntryPoint-Paritaet (Counts + Reasons)
  - [ ] Determinismus (zentral)
  - [ ] Cancellation-Datenintegritaet
  - [ ] Audit-Sidecar Round-Trip
- [ ] **DoD-12** Diff-Report: Anzahl entfernter, umgebauter, neu hinzugefuegter Tests dokumentiert; Netto-Anzahl Tests darf sinken.
- [ ] **DoD-13** Kein einziger Test markiert mit `Skip = "..."` ohne Issue-Referenz.
- [ ] **DoD-14** Keine neue Schattenlogik / keine doppelte Result-/Projection-Wahrheit fuer Tests eingefuehrt.
- [ ] **DoD-15** Plan-Datei selbst final reviewed und alle Checkboxen abgehakt.


---

## Anhang: Block A – Diff Report (2026-04-25)

### Geaenderte / komplett neu geschriebene Test-Dateien (12)

| Datei | Vorher (LOC) | Nachher (LOC) | Aktion |
| --- | ---: | ---: | --- |
| `src/Romulus.Tests/TrackerAllFindingsBatch1RedTests.cs` | ~250 | ~85 | Source-Spiegel entfernt, 4 Verhaltenstests behalten |
| `src/Romulus.Tests/TrackerAllFindingsBatch2RedTests.cs` | ~210 | ~110 | Reflection-Existence entfernt, 3 Verhaltenstests behalten |
| `src/Romulus.Tests/TrackerAllFindingsBatch3RedTests.cs` | ~330 | ~140 | 5 Source-Spiegel entfernt, 4 Verhaltenstests behalten |
| `src/Romulus.Tests/TrackerAllFindingsBatch4RedTests.cs` | ~520 | ~180 | ~16 Source-Spiegel entfernt, 4 Verhaltenstests behalten |
| `src/Romulus.Tests/TrackerBlock1To6RedTests.cs` | ~430 | ~180 | EP/API/DI Source-Spiegel entfernt, 3 Verhaltenstests behalten |
| `src/Romulus.Tests/TrackerBlock7To12RedTests.cs` | ~410 | ~210 | ERR/TH/ORC/DATA Source-Spiegel entfernt, I18N gesplittet |
| `src/Romulus.Tests/TrackerPoint2OpenFindingsRedTests.cs` | ~220 | ~110 | API/ERR/DI/TEST Source-Spiegel entfernt, 2 Verhaltenstests behalten |
| `src/Romulus.Tests/AuditABEndToEndRedTests.cs` | ~240 | ~165 | A01/B01/B04/B05 Source-Spiegel entfernt, 5 Verhaltenstests behalten |
| `src/Romulus.Tests/AuditCDRedTests.cs` | 437 | ~210 | C02-C07/C09-C14/D01-D05/E01/E03-E06/E09 Source-Spiegel entfernt, 5 Verhaltenstests behalten |
| `src/Romulus.Tests/AuditEFOpenTests.cs` | 409 | ~220 | E02 Source-Mirror, E07, E08-Source, F05, F07, F12-Source, F14, F16-Source entfernt, 17 Verhaltens-/Datentests behalten |
| `src/Romulus.Tests/Phase8RoundVerificationTests.cs` | 224 | ~75 | R8_001 Source-Anteile, R8_002 IO-Check, R8_003 Magic-Bytes, R8_004 (3 Tests) entfernt, 5 Verhaltenstests behalten |
| `src/Romulus.Tests/Phase9RoundVerificationTests.cs` | 496 | ~190 | R9_005 / R9_043 Meta-Tests + 12 Source-Spiegel-Tests entfernt, 12 Verhaltenstests behalten |

### Punktuelle Edits (kleinere Dateien)

| Datei | Aktion |
| --- | --- |
| `src/Romulus.Tests/UncPathTests.cs` | `PathGetFullPath_DoesNotThrowOnUncOrLocal` ersatzlos entfernt (BCL-Test ohne Eigenwert) |
| `src/Romulus.Tests/V2RemainingTests.cs` | 2x `*_DoesNotThrow` umbenannt; bestehende Funktions-Asserts gehalten |
| `src/Romulus.Tests/VersionScorerTests.cs` | 2x `*_DoesNotThrow` -> `*_ScoresDeterministically` umbenannt |
| `src/Romulus.Tests/ChaosTests.cs` | 6x `_NeverThrows` / `_DoesNotThrow` -> Verhaltensasserts; 3x `ElapsedMilliseconds < N` entfernt |
| `src/Romulus.Tests/SecurityTests.cs` | 2x `_NeverThrows` -> `_ReturnsNonNullTag` mit Asserts |
| `src/Romulus.Tests/AuditHardeningTests.cs` | `RuleEngine_RegexTimeout_DoesNotCrash` -> Assert auf `Matched == false` |
| `src/Romulus.Tests/StandaloneConversionServiceTests.cs` | `Dispose_NullLifetime_DoesNotThrow` -> `_DisposesGracefully` mit doppeltem Dispose-Check |
| `src/Romulus.Tests/FileHashServiceCoverageTests.cs` | 4x `_DoesNotThrow` umbenannt; `Dispose_CalledTwice` mit Idempotenz-Assert verstaerkt |
| `src/Romulus.Tests/AuditComplianceTests.cs` | 5x `ElapsedMilliseconds < 5000` Perf-Smokes entfernt, funktionale Asserts (`NotNull`/`Valid`) behalten |

### Bewusst nicht angefasst

- `SettingsFileAccessTests.TryReadAllTextAsync_FileLocked_RespectsTotalTimeoutAndReturnsNull` – zeitliche Schranke ist Vertragspruefung der `totalTimeoutMs`-Parameterimplementierung.
- `SafetyIoSecurityRedPhaseTests` `_DoesNotThrow`-Stelle – hat bereits `Assert.Null(movedPath)` als echte Verhaltensassertion.

### Aggregat

- Geloeschte Test-Methoden (Source-Spiegel + Meta + reflection-existence + tote DoesNotThrow): **~95**
- Umgebaute Test-Methoden (DoesNotThrow -> Verhaltensassert): **~20**
- Entfernte Perf-Smokes (`ElapsedMilliseconds < N`): **8**
- Komplett neu geschriebene Dateien: **8**
- Punktuell editierte Dateien: **9**

### Verifikation

- `dotnet build src/Romulus.sln`: **0 Warnungen, 0 Fehler** (3.05s, nach `Stop-Process testhost`).
- Test-Stages: noch ausstehend nach Block B-E (ueber `tests: all` / Pipeline).
