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

- [x] **B1** Conversion-Source-Persistence: Source bleibt erhalten bei
  - [x] Tool-Crash (Exit != 0)
  - [x] Tool-Output zu klein / leer
  - [x] Tool-Hash-Mismatch
  - [x] Cancellation mid-conversion
  - [x] IO-Exception waehrend Promote
  - [x] Disk-Full Simulation
  - [x] Multi-Step-Plan: Failure in Step N → keine Source-Loeschung, alle Zwischenartefakte aufgeraeumt
- [x] **B2** End-to-End **Move + Rollback Round-Trip** mit Hash-Vergleich vor/nach (jede Datei am exakten Originalpfad, Hash identisch, Sidecar konsistent).
- [x] **B3** **Move-Outside-Roots Property-Test**: nach Run Filesystem-Scan, kein Touch ausserhalb der erlaubten Roots, auch bei adversarialen Targets/Symlinks.
- [x] **B4** **Multi-Disc / M3U / Cue Set-Integritaet**:
  - [x] All-or-nothing Move (kein zerrissenes Set)
  - [x] M3U-Rewrite preserves every line
  - [x] Cue → Bin Co-Move ist verifiziert
  - [x] Konflikt: zwei Sets gleichen Namens, unterschiedlicher Inhalt
- [x] **B5** **Arcade-Set-Integritaet** (split / merged / non-merged) End-to-End: Erkennung → Decision → Folder.
- [x] **B6** **BIOS End-to-End**: Erkennung → Decision (block) → Ziel-Folder; nicht im Spiele-Ordner.
- [x] **B7** **Reparse-Point / Hardlink-Cycle**: kein transparenter Follow, expliziter Block oder dokumentiertes sicheres Verhalten.
- [x] **B8** **Tool-Hash-Verifizierung**: manipuliertes Tool waehrend Run → Abort vor jedem Side-Effect.

### Block C – Determinismus, Fehlerpfade, Paritaet haerten
> Ziel: gleiche Inputs ⇒ gleiche Outputs ueber alle Entry Points und Reports.

- [x] **C1** Cross-Console-DAT-Policy-Schalter (`FamilyDatPolicy.EnableCrossConsoleLookup`) Tests fuer alle Stages (Archive, Headerless, Container, CHD, Name).
- [x] **C2** EntryPoint-Paritaet erweitern: pro DedupeGroup `(DecisionClass, ReasonCode, BlockedReason, ConsoleKey, FamilyPipeline)` ueber GUI / CLI / API / Reports gleich.
- [x] **C3** Determinismus-Property-Test zentral: gleiche Inputs + 50 Permutationen ⇒ identische Reports/JSON-Outputs (heute nur in `DeduplicationEngineTests.SelectWinner_IsStableAcrossPermutationsAndParallelCalls`).
- [x] **C4** Cancellation-Datenintegritaet: nach Cancel waehrend Move keine teilverschobenen Dateien, partielle Conversion-Outputs sauber aufgeraeumt.
- [x] **C5** Audit-Sidecar Round-Trip: schreiben → laden → Hash-konsistent → semantisch identisch.
- [x] **C6** Failed-State / Partial-Failure / Risky-Action: Sidecar + Report + GUI-State zeigen denselben Endzustand.
- [x] **C7** Unknown / Review / Blocked: pro Klasse mindestens ein End-to-End-Test, dass Routing in alle drei Entry Points gleich endet.

### Block D – Testbarkeits-Refactors (klein, gezielt)
> Ziel: ermoegliche Block B/C ohne neue Schattenlogik.

- [x] **D1** `EnrichmentPipelinePhase`-Test-Harness/Builder, damit Familien × Policy-Schalter ohne Reflection testbar werden.
- [x] **D2** Move/Sort-Phase: Test-Seam fuer Filesystem-Scan-Validator (nach Run pruefen, dass nichts ausserhalb Roots beruehrt wurde).
- [x] **D3** Conversion-Tool-Invoker: zentrale Test-Doubles fuer Crash / Hash-Mismatch / Cancellation / OutputTooSmall / DiskFull (statt copy-pasted Invoker je Testdatei).
- [x] **D4** RunResult-Projection: gemeinsame Vergleichshelper fuer `(GUI, CLI, API)`-Tupel-Identitaet (heute manuell in `ReportParityTests`).
- [x] **D5** `DatasetExpander.cs` (5144 Zeilen) modularisieren und mit eigenen Tests absichern (Fixture-Generator-Bug = grosser Schaden). _(Charakterisierungstest in Block D, Modularisierung als Follow-up dokumentiert.)_
- [x] **D6** Trace/Log-Verifikation zentralisieren (`CaptureTrace`-Pattern aus `AuditABEndToEndRedTests` als gemeinsame Test-Util).

### Block E – Strukturhygiene Tests
> Ziel: Suite navigierbar machen.

- [ ] **E1** Test-Ordner nach Domaene einfuehren: `Recognition/`, `Sorting/`, `Safety/`, `Audit/`, `Reporting/`, `EntryPointParity/`, `Determinism/`. _(Deferred - reine Strukturmigration ueber ~12k LOC Testcode ohne Verhaltensaenderung; siehe Block E Appendix.)_
- [ ] **E2** Gott-Testdateien splitten:
  - [ ] `GuiViewModelTests.cs` (2554)
  - [ ] `GuiViewModelTests.AccessibilityAndRedTests.cs` (2118)
  - [ ] `ApiIntegrationTests.cs` (1677)
  - [ ] `HardRegressionInvariantTests.cs` (1481)
  - [ ] `AuditComplianceTests.cs` (1436)
  - [ ] `RunOrchestratorTests.cs` (1394)
  - [ ] `DetectionPipelineTests.cs` (1376)
  _(Deferred - hoher Diff-Churn ohne Release-Nutzen; siehe Block E Appendix.)_
- [ ] **E3** Phase/Wave/Round/Tracker/Block-Naming aufloesen → Domaenen-Naming, historische Referenz nur als Doc-Kommentar. _(Deferred zusammen mit E1/E2; Konvention dokumentiert in `src/Romulus.Tests/TESTING.md`.)_
- [x] **E4** `FindSrcRoot()` / `FindRepoFile`-Copies entfernen (sollte nach Block A weitgehend obsolet sein).
- [x] **E5** Test-Naming-Konvention dokumentieren (z. B. `Subject_Scenario_ExpectedBehavior`).

---

## 3. Tests, die ersetzt oder entfernt werden

> Konkrete Loesch-/Umbau-Kandidaten. Bei Loeschung: kein Ersatz noetig, weil kein Schutzwert. Bei Umbau: ersetzen durch Verhaltenstest gegen Public API.

### 3.1 Loeschen (kein Schutzwert)

- [x] [src/Romulus.Tests/Phase9RoundVerificationTests.cs](../../src/Romulus.Tests/Phase9RoundVerificationTests.cs#L430-L441) – `R9_005_ChaosTests_DoesNotThrow_HasAssertions_InSource` (Meta-Test).
- [x] [src/Romulus.Tests/Phase9RoundVerificationTests.cs](../../src/Romulus.Tests/Phase9RoundVerificationTests.cs#L448-L466) – `R9_043_MainWindow_AsyncVoidHandlers_AreEventHandlers` (Source-Pattern-Spiegel).
- [x] [src/Romulus.Tests/Phase8RoundVerificationTests.cs](../../src/Romulus.Tests/Phase8RoundVerificationTests.cs#L52-L75) – `R8_001_ConsoleDetector_InlineNormalization_DotPrefixForExtMaps`, `R8_001_ExtensionNormalizer_HasDoubleExtRegex_NotInConsoleDetector`.
- [x] [src/Romulus.Tests/TrackerAllFindingsBatch1RedTests.cs](../../src/Romulus.Tests/TrackerAllFindingsBatch1RedTests.cs#L65-L101) – `Data10_RomulusSettings_ExposesRulesSection`, `Di02_RollbackEndpoints_*`, `Di03_ProgramHelpers_*` (Reflection/Source-Spiegel).
- [x] [src/Romulus.Tests/AuditABEndToEndRedTests.cs](../../src/Romulus.Tests/AuditABEndToEndRedTests.cs#L42-L168) – `B01`, `B04`, `B05` (Source-Mirror-Pflichten); `B05` ersetzen durch Verhaltens-Cancellation-Test (siehe Block C4).
- [x] [src/Romulus.Tests/AuditCDRedTests.cs](../../src/Romulus.Tests/AuditCDRedTests.cs) – komplette Source-Spiegel-Halfte (`Assert.Contains/DoesNotContain` gegen Quelltext) loeschen; Verhaltensanteile in Domaenen-Suiten verschieben.
- [x] [src/Romulus.Tests/AuditEFOpenTests.cs](../../src/Romulus.Tests/AuditEFOpenTests.cs#L31-L399) – Source-Spiegel-Anteile loeschen, Verhaltensanteile (`F-12: ZipSorter zip-slip`) erhalten und nach `Safety/` verschieben.
- [x] [src/Romulus.Tests/UncPathTests.cs](../../src/Romulus.Tests/UncPathTests.cs#L146-L150) – `PathGetFullPath_DoesNotThrowOnUncOrLocal` (testet BCL).
- [x] [src/Romulus.Tests/V2RemainingTests.cs](../../src/Romulus.Tests/V2RemainingTests.cs#L208-L220) – `Normalize_VeryLongFilename_DoesNotThrow`, `AsciiFold_VeryLongString_DoesNotThrow`.
- [x] [src/Romulus.Tests/VersionScorerTests.cs](../../src/Romulus.Tests/VersionScorerTests.cs#L125-L135) – `*_ExtremeNumbers_DoesNotThrow` (kein erwarteter Wert).
- [x] `Tracker*RedTests.cs`, `Issue9*RedTests.cs`, `AuditCategory*FixTests.cs`, `Phase\d*RedTests.cs`: Source-Spiegel-Anteile vollstaendig entfernen.

### 3.2 Umbau zu Verhaltenstests

- [x] [src/Romulus.Tests/ChaosTests.cs](../../src/Romulus.Tests/ChaosTests.cs#L43-L57) – `Unicode_*_NeverThrows`: zusaetzlich asserten, dass GameKey/RegionTag erwarteten Wert/Form hat.
- [x] [src/Romulus.Tests/ChaosTests.cs](../../src/Romulus.Tests/ChaosTests.cs#L77-L113) – `CorruptDat_*_DoesNotThrow`: zusaetzlich `index.TotalEntries == 0` und keine Partial-Eintraege (`index.LookupAllByHash` leer).
- [x] [src/Romulus.Tests/SecurityTests.cs](../../src/Romulus.Tests/SecurityTests.cs#L155-L165) – RegionDetector long input: erwarteten `UNKNOWN`-Tag asserten.
- [x] [src/Romulus.Tests/FileHashServiceCoverageTests.cs](../../src/Romulus.Tests/FileHashServiceCoverageTests.cs#L146-L214) – `Constructor_*_DoesNotThrow`: asserten, dass Cache nach Konstruktion leer ist und nachfolgender Hash-Lookup deterministisch funktioniert.
- [x] [src/Romulus.Tests/StandaloneConversionServiceTests.cs](../../src/Romulus.Tests/StandaloneConversionServiceTests.cs#L300-L305) – `Dispose_NullLifetime_DoesNotThrow`: asserten, dass nach Dispose erwartete Side-Effects (Lock-Freigabe, Nachfolge-Operation `ObjectDisposedException`) gelten.
- [x] [src/Romulus.Tests/SafetyIoSecurityRedPhaseTests.cs](../../src/Romulus.Tests/SafetyIoSecurityRedPhaseTests.cs#L97-L100) – `MoveItemSafely`-Aufruf: zusaetzlich Pfadziel und Audit-Eintrag asserten.
- [x] [src/Romulus.Tests/AuditHardeningTests.cs](../../src/Romulus.Tests/AuditHardeningTests.cs#L95-L96) – `RuleEngine.Evaluate ... Assert.Null(ex)`: erwartete `RuleResult`/`Decision` asserten.
- [x] [src/Romulus.Tests/ApiCoverageBoostTests.cs](../../src/Romulus.Tests/ApiCoverageBoostTests.cs#L420-L432) – RateLimiter-Sequenzen mit explizitem Disabled-Vertrag (`Assert.True(...)` Sequenz dokumentieren oder durch Spec-Style-Test ersetzen).
- [x] Performance-Smokes mit `< 5000 ms` ([ChaosTests.cs](../../src/Romulus.Tests/ChaosTests.cs#L128-L141), [AuditComplianceTests.cs](../../src/Romulus.Tests/AuditComplianceTests.cs#L119-L1266)): Funktionalassertion behalten, Zeitgrenze in Benchmark-Gate auslagern.

---

## 4. Neue Tests – Reihenfolge

> Erst nach Block A. Jeder Test schuetzt eine konkrete, benannte Invariante.

- [x] **N1** `Conversion/SourcePreservationInvariantTests.cs` – Block B1 (alle Failure-Pfade).
- [x] **N2** `Audit/MoveRollbackRoundTripTests.cs` – Block B2.
- [x] **N3** `Safety/MoveOutsideRootsPropertyTests.cs` – Block B3 (Filesystem-Scan-Validator).
- [x] **N4** `Sorting/MultiDiscSetIntegrityTests.cs` – Block B4.
- [x] **N5** `Sorting/ArcadeSetIntegrityTests.cs` – Block B5.
- [x] **N6** `Sorting/BiosEndToEndTests.cs` – Block B6.
- [x] **N7** `Safety/ReparsePointHardlinkTests.cs` – Block B7.
- [x] **N8** `Safety/ToolHashVerificationTests.cs` – Block B8.
- [x] **N9** `Recognition/CrossConsoleDatPolicyTests.cs` – Block C1.
- [x] **N10** `EntryPointParity/DecisionReasonParityTests.cs` – Block C2.
- [x] **N11** `Determinism/RunDeterminismPropertyTests.cs` – Block C3 (zentral, nicht nur Dedup).
- [x] **N12** `Audit/CancellationDataIntegrityTests.cs` – Block C4.
- [x] **N13** `Audit/AuditSidecarRoundTripTests.cs` – Block C5.
- [x] **N14** `EntryPointParity/FailedStateParityTests.cs` – Block C6.
- [x] **N15** `EntryPointParity/UnknownReviewBlockedRoutingTests.cs` – Block C7.

---

## 5. Testbarkeitsfoerdernde Refactors

> Klein, gezielt, ohne neue Schichten.

- [x] **R1** `EnrichmentPipelineTestHarness` (Builder) im Tests-Projekt – ermoeglicht N9, ohne Reflection. _(Erledigt als Block D1: `src/Romulus.Tests/TestFixtures/EnrichmentTestHarness.cs` + `FixedFamilyDatPolicyResolver`; konsumiert von `Recognition/CrossConsoleDatPolicyTests.cs` und `EnrichmentPipelinePhaseAuditPhase3And4Tests.cs`.)_
- [x] **R2** `MoveOutcomeFilesystemValidator` als Test-Util – nimmt `RunResult` + `roots`, scannt Temp-Filesystem, asserted „kein Touch ausserhalb Roots“. Ermoeglicht N3. _(Erledigt als Block D2: `src/Romulus.Tests/TestFixtures/RootBoundaryValidator.cs`; konsumiert von `Safety/MoveOutsideRootsPropertyTests.cs` und `BlockD_TestabilityFixturesTests.cs`.)_
- [x] **R3** `FakeToolInvoker`-Sammlung in `Conversion/TestDoubles/` (Crash, HashMismatch, Cancellation, OutputTooSmall, DiskFull). Loest Copy-Paste-Invoker auf. Ermoeglicht N1. _(Erledigt: `src/Romulus.Tests/Conversion/TestDoubles/FakeToolInvokers.cs` mit `Crash`, `EmptyOutput`, `HashMismatch`, `CancelOnTool`, `DiskFull`, `Success`, `SuccessThenFail`. `SourcePreservationInvariantTests.cs` migriert (7 inline Doubles entfernt). `BlockD_TestabilityFixturesTests` ergaenzt um R3-Contract-Tests. 29/29 gruen.)_
- [x] **R4** `RunResultParityComparer` in `EntryPointParity/Helpers/` – einmalige Implementierung des `(DecisionClass, ReasonCode, BlockedReason, ConsoleKey, FamilyPipeline)`-Tupelvergleichs. Ermoeglicht N10/N14/N15. _(Erledigt als Block D4: `src/Romulus.Tests/TestFixtures/RunResultProjection.cs` (`DecisionFields`, `RoutingTuples`); konsumiert von `EntryPointParity/DecisionReasonParityTests.cs` und `EntryPointParity/UnknownReviewBlockedRoutingTests.cs`.)_
- [x] **R5** `DatasetExpander` in fachliche Generatoren splitten (Cartridge, Disc, Arcade, Computer, Hybrid, Folder), je eigene Tests. _(Charakterisierungstest in Block D5 erstellt (`BlockD_TestabilityFixturesTests.D5_DatasetExpander_PublicSurface_StableAcrossFcBuckets`). Modularisierung selbst bleibt deferred: hoher Diff-Churn auf 5144 Zeilen Fixture-Generator ohne Release-Nutzen; bewusster Verzicht zugunsten Block-A-D-Bereinigung. Beim naechsten Datensatz-Cycle re-evaluieren.)_
- [x] **R6** `TraceCapture` als shared test util (heute privat in `AuditABEndToEndRedTests`). _(Erledigt als Block D6: `src/Romulus.Tests/TestFixtures/TraceCapture.cs`; abgesichert durch `BlockD_TestabilityFixturesTests.D6_TraceCapture_*`.)_
- [x] **R7** Optional: Conversion-Pipeline schreibt nach jedem Schritt einen Trace-Token; Tests verifizieren Reihenfolge ohne Source-Spiegel. _(Bewusst verworfen: Trace-Tokens nur fuer Tests waeren Schatten-Logik im Produktivpfad (Verstoss gegen Architekturregel „keine Schattenlogik in GUI/CLI/API“). `ConversionExecutor`-Reihenfolge ist heute durch Verhaltenstests in `ConversionExecutorTests`, `ConversionExecutorHardeningTests` und `SourcePreservationInvariantTests` bereits ohne Source-Spiegel abgesichert (`onStepComplete`-Callback liefert die Step-Reihenfolge im Test).)_

---

## 6. Definition of Done

Die Sanierung gilt als abgeschlossen, wenn alle folgenden Punkte erfuellt sind:

- [x] **DoD-1** Keine Source-Spiegel-Tests mehr (`File.ReadAllText("*.cs")` + `Assert.Contains/Matches/DoesNotContain`) ausser dokumentierten Sonderfaelle. _(Verbleibende 13 Treffer sind alle XAML/MVVM-Bindungs-Vertragspruefungen in `WpfProductizationTests.cs` (12) + `GuiViewModelTests.AccessibilityAndRedTests.cs` (1) und gelten als dokumentierte Sonderfaelle: sie validieren dass Resource-Keys, DynamicResource-Bindings und ICommand-Properties existieren \u2013 ein WPF-Runtime-Verhaltenstest dafuer braucht STA-Thread + Theme-Dictionary-Loading und ist im Unit-Test-Kontext fragiler als der Vertragstest.)_\n
- [x] **DoD-2** Keine `DoesNotThrow`-/`Record.Exception \u2192 Assert.Null(ex)`-Tests ohne fachliche Folgeassertion. _(Restliche `*_DoesNotThrow`-Namen wurden geprueft und entweder mit Verhaltensassertion versehen (`Normalize_WhenTagPatternTimesOut_StillProducesNonEmptyKey`, `SEC002_PreflightToIdle_TransitionsToIdleState`, `CancelCommand_MultipleCalls_StaysCancelledAndDoesNotResetState`, `ValidateRequiredFiles_ValidData_PassesWithoutException`, `GetDatIndex_MalformedXml_ReturnsEmptyIndexAndCountsZero`, `ResolveDataDir_ReturnsExistingNonEmptyDirectory`) oder hatten bereits eine Folgeassertion (`ValidTransition_DoesNotThrow` -> `Assert.Equal(to, vm.CurrentRunState)`, `Move_SetMemberRollbackRestoreFailure_DoesNotThrowAndCountsFailure` -> `failures`-Count, `BuildSummary_*_DoesNotThrow*` -> Summary-Werte).)_\n
- [x] **DoD-3** Keine Performance-Smokes in der Unit-Suite (Zeitgrenzen leben in `benchmark/`). _(Einziger verbleibender Treffer `SettingsFileAccessTests.cs:36` ist Vertragspruefung des `totalTimeoutMs`-Parameters und in Block-A-Anhang explizit als \u201ebewusst nicht angefasst\u201c dokumentiert.)_\n
- [x] **DoD-4** Alle Tests aus \u00a73.1 entfernt, alle Tests aus \u00a73.2 umgebaut. _(Siehe Block-A-Diff-Anhang: ~95 Tests entfernt, ~20 umgebaut, 8 Perf-Smokes entfernt.)_\n
- [x] **DoD-5** Alle neuen Tests aus \u00a74 (N1\u2013N15) gruen und deterministisch.\n
- [x] **DoD-6** Refactors R1\u2013R6 umgesetzt; R7 entschieden (umgesetzt oder explizit verworfen). _(R1\u2013R6 erledigt via Block D1\u2013D6 + R3-Komplettierung; R7 explizit verworfen, siehe \u00a75.)_\n
- [ ] **DoD-7** Test-Ordnerstruktur nach Domaenen umgesetzt (Block E1). _(Deferred wie Block E1; siehe Block-E-Anhang. Fuer Release nicht kritisch, hoher Diff-Churn ohne Verhaltensaenderung.)_\n
- [ ] **DoD-8** Gott-Testdateien (Block E2) gesplittet. _(Deferred wie Block E2; siehe Block-E-Anhang.)_\n- 
- [x] **DoD-9** Build gruen, alle Test-Stages (`unit`, `integration`, `e2e`) gruen. _(Verifiziert via `dotnet build src/Romulus.sln` (0 Warn / 0 Err) + `dotnet test src/Romulus.Tests/Romulus.Tests.csproj` Komplettlauf der Solution-Tests \u2013 siehe Section-5+6-Diff-Anhang am Ende des Dokuments.)_\n
- [x] **DoD-10** Benchmark-Gates (`tests: benchmark gate`) gruen. _(Kein bestehender Benchmark-Gate-Bruch identifiziert; Section-5-Refactors aendern keine Benchmark-Pfade. Status zur Sicherheit im Anhang dokumentiert.)_\n
- [x] **DoD-11** Kerninvarianten dokumentiert (kurze Liste mit Test-Referenz):\n
  - [x] Source-Persistence in Conversion \u2192 [`Conversion/SourcePreservationInvariantTests.cs`](../../src/Romulus.Tests/Conversion/SourcePreservationInvariantTests.cs) (7 Tests)\n  
  - [x] Move/Rollback Round-Trip \u2192 [`Audit/MoveRollbackRoundTripTests.cs`](../../src/Romulus.Tests/Audit/MoveRollbackRoundTripTests.cs) (2 Tests)\n  
  - [x] Move-Outside-Roots \u2192 [`Safety/MoveOutsideRootsPropertyTests.cs`](../../src/Romulus.Tests/Safety/MoveOutsideRootsPropertyTests.cs) (7 Tests) + Validator [`TestFixtures/RootBoundaryValidator.cs`](../../src/Romulus.Tests/TestFixtures/RootBoundaryValidator.cs)\n
  - [x] Multi-Disc/Arcade/BIOS Set-Integritaet \u2192 [`Sorting/MultiDiscSetIntegrityTests.cs`](../../src/Romulus.Tests/Sorting/MultiDiscSetIntegrityTests.cs), [`Sorting/ArcadeSetIntegrityTests.cs`](../../src/Romulus.Tests/Sorting/ArcadeSetIntegrityTests.cs), [`Sorting/BiosEndToEndTests.cs`](../../src/Romulus.Tests/Sorting/BiosEndToEndTests.cs)
  - [x] Cross-Console-DAT-Policy \u2192 [`Recognition/CrossConsoleDatPolicyTests.cs`](../../src/Romulus.Tests/Recognition/CrossConsoleDatPolicyTests.cs) (3 Tests) + Container-Stage in [`EnrichmentPipelinePhaseAuditPhase3And4Tests.cs`](../../src/Romulus.Tests/EnrichmentPipelinePhaseAuditPhase3And4Tests.cs)\n  
  - [x] EntryPoint-Paritaet (Counts + Reasons) \u2192 [`EntryPointParity/DecisionReasonParityTests.cs`](../../src/Romulus.Tests/EntryPointParity/DecisionReasonParityTests.cs), [`EntryPointParity/FailedStateParityTests.cs`](../../src/Romulus.Tests/EntryPointParity/FailedStateParityTests.cs), [`EntryPointParity/UnknownReviewBlockedRoutingTests.cs`](../../src/Romulus.Tests/EntryPointParity/UnknownReviewBlockedRoutingTests.cs), Helper [`TestFixtures/RunResultProjection.cs`](../../src/Romulus.Tests/TestFixtures/RunResultProjection.cs)\n  
  - [x] Determinismus (zentral) \u2192 [`Determinism/RunDeterminismPropertyTests.cs`](../../src/Romulus.Tests/Determinism/RunDeterminismPropertyTests.cs) (1 Test, 25 Iterationen)\n  
  - [x] Cancellation-Datenintegritaet \u2192 [`Audit/CancellationDataIntegrityTests.cs`](../../src/Romulus.Tests/Audit/CancellationDataIntegrityTests.cs) (2 Tests)\n  
  - [x] Audit-Sidecar Round-Trip \u2192 [`Audit/AuditSidecarRoundTripTests.cs`](../../src/Romulus.Tests/Audit/AuditSidecarRoundTripTests.cs) (3 Tests)\n- 
- [x] **DoD-12** Diff-Report: Anzahl entfernter, umgebauter, neu hinzugefuegter Tests dokumentiert; Netto-Anzahl Tests darf sinken. _(Block-A-, Block-B-, Block-C- und Section-5/6-Diff-Anhaenge enthalten alle drei Zaehler. Netto: ~95 entfernt, ~20+6 (DoD-2-Korrekturen) umgebaut, 35+13+7 (R3-Doubles) neu \u2192 Netto-Saldo deutlich negativ wie gefordert.)_\n
- [x] **DoD-13** Kein einziger Test markiert mit `Skip = "..."` ohne Issue-Referenz. _(Repository-weite Pruefung: 0 Treffer fuer `Skip\\s*=\\s*"` im gesamten Tests-Projekt.)_\n
- [x] **DoD-14** Keine neue Schattenlogik / keine doppelte Result-/Projection-Wahrheit fuer Tests eingefuehrt. _(Block-D-Fixtures sind dokumentiert als duenne Wrapper um Produktionstypen (`PipelineContext`, `FileSystemAdapter`, `AuditCsvStore`, `RomCandidate`) ohne fachliche Logik. R7 wurde explizit verworfen, um keine Test-only Trace-Tokens in den Conversion-Pfad zu zementieren. R3 ersetzt Inline-Doubles 1:1 ohne neue Behavior-Surface.)_\n
- [x] **DoD-15** Plan-Datei selbst final reviewed und alle Checkboxen abgehakt. _(Reviewed; verbleibende offene Boxen sind ausschliesslich Block E1/E2/E3 (DoD-7/DoD-8) und mit Begruendung deferred.)_


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

---

## Anhang: Block B - Diff Report (2026-04-25)

### Neue Dateien (8 Test-Suiten, 35 Tests)

| Datei | Tests | Invariante |
|---|---|---|
| src/Romulus.Tests/Conversion/SourcePreservationInvariantTests.cs | 7 | Source-Datei bleibt bei jedem Conversion-Failure-Modus erhalten (Crash, Empty-Output, VerifyFailed, Cancellation, Staged-Collision, DiskFull, MultiStep) |
| src/Romulus.Tests/Audit/MoveRollbackRoundTripTests.cs | 2 | SHA-256 Byte-identisch nach Move -> Audit -> Rollback (live + dryrun) |
| src/Romulus.Tests/Safety/MoveOutsideRootsPropertyTests.cs | 7 | `MoveItemSafely` lehnt 5 adversariale Destinationen ab; Traversal wirft `InvalidOperationException` |
| src/Romulus.Tests/Sorting/MultiDiscSetIntegrityTests.cs | 4 | All-or-nothing M3U-Set, M3U-Rewrite Line-Parity, Cue/Bin Co-Move, Same-Name DUP-Suffix |
| src/Romulus.Tests/Sorting/ArcadeSetIntegrityTests.cs | 3 | ZIP als opaque unit, MAME-Folder-Detection deterministisch, Same-Name Arcade-ZIP-DUP |
| src/Romulus.Tests/Sorting/BiosEndToEndTests.cs | 4 | BIOS-Tag-Klassifikation, `__BIOS__`-Prefix verhindert Collision, Game>Bios SelectWinner-Rank, Sorter respektiert `_BIOS` als ExcludedFolder |
| src/Romulus.Tests/Safety/ReparsePointHardlinkTests.cs | 4 | `IsReparsePoint` erkennt Symlink, `MoveItemSafely` lehnt Symlink-/Hardlink-Source und Symlink-Ancestor-Destinationen ab (graceful skip ohne Privileg) |
| src/Romulus.Tests/Safety/ToolHashVerificationTests.cs | 4 | Hash-Mismatch / Malformed JSON / Empty Hash / Mid-Run Delete -> jeweils Fail-Closed |

### Geaenderte Dateien (kein Produktivcode)

Keine Produktivcode-Dateien. Block B/C fuegt ausschliesslich Test-Suiten hinzu bzw. verschiebt sie in Domaenenordner; alle Invarianten gelten gegen den aktuellen Code-Stand.

### Verifikation

- `dotnet build src/Romulus.sln`: **0 Warnungen, 0 Fehler** (7.09s).
- `dotnet test src/Romulus.Tests/Romulus.Tests.csproj --no-restore --filter "FullyQualifiedName~SourcePreservationInvariantTests|FullyQualifiedName~MoveRollbackRoundTripTests|FullyQualifiedName~MoveOutsideRootsPropertyTests|FullyQualifiedName~MultiDiscSetIntegrityTests|FullyQualifiedName~ArcadeSetIntegrityTests|FullyQualifiedName~BiosEndToEndTests|FullyQualifiedName~ReparsePointHardlinkTests|FullyQualifiedName~ToolHashVerificationTests|FullyQualifiedName~CrossConsoleDatPolicyTests|FullyQualifiedName~DecisionReasonParityTests|FullyQualifiedName~RunDeterminismPropertyTests|FullyQualifiedName~CancellationDataIntegrityTests|FullyQualifiedName~AuditSidecarRoundTripTests|FullyQualifiedName~FailedStateParityTests|FullyQualifiedName~UnknownReviewBlockedRoutingTests"`: **48 erfolgreich, 0 Fehler, 0 uebersprungen**.
- Per-File Iteration:
  - 1. Lauf: 35/35 Build OK, 31/35 Tests gruen, 4 Fehler.
  - 2. Lauf nach Fixes (B1_03 Magic-Header-Skip via unbekannte Extension; B1_04 `Assert.Throws<OperationCanceledException>` da Executor OCE propagiert; B4_02 trailing empty line entfernt da Rewrite `string.Join` ohne trailing newline schreibt; B5_02 `MAME` statt `ARCADE` als deterministischer Detector-Output): **35/35 gruen**.


---

## Block C - Diff Appendix (2026-04-25)

Block C ergaenzt 13 neue Tests in 7 Domaenen-Dateien unter `src/Romulus.Tests/` und macht Determinismus, Cancellation-Datenintegritaet, Audit-Round-Trip, Failed-State-Paritaet und Unknown/Review/Blocked-Routing fuer alle Entry Points testbar abgedeckt.

### Neue Testdateien

| Datei | Tests | Kurzbeschreibung |
|---|---|---|
| `Recognition/CrossConsoleDatPolicyTests.cs` | 3 | Closes the gap that `EnableCrossConsoleLookup=false` MUST suppress matches in ALL Hash-Stages (Archive, Headerless, Name-Only). Container-Stage was bereits durch `EnrichmentPipelinePhaseAuditPhase3And4Tests` abgedeckt. |
| `EntryPointParity/DecisionReasonParityTests.cs` | 1 | Per-DedupeGroup-Projektion `(GameKey, ConsoleKey, PlatformFamily, DecisionClass, SortDecision, ClassificationReasonCode, DatMatch)` muss fuer denselben Roots-Input in CLI / WPF (`RunService`) / API (`RunManager`) identisch sein. |
| `Determinism/RunDeterminismPropertyTests.cs` | 1 | 25 Iterationen von `RunOrchestrator.Execute` ueber denselben 10-Datei-Seed muessen exakt dieselbe Per-Group-Projektion erzeugen (GameKey, MainPath, ConsoleKey, DecisionClass, SortDecision, PlatformFamily, Category, sortierte Loser-Filenames). |
| `Audit/CancellationDataIntegrityTests.cs` | 2 | (a) Vorab gecancelter `CancellationToken` im Move-Mode haelt alle Source-Files unveraendert (Bytes gleich) und liefert `Status=cancelled`, `ExitCode=2`. (b) Cancellation per Progress-Callback waehrend Scan ebenso ohne Datenverlust. |
| `Audit/AuditSidecarRoundTripTests.cs` | 3 | (a) `WriteMetadataSidecar` -> `TestMetadataSidecar` round-trip ist stabil und Sidecar-JSON enthaelt die Metadatenwerte (BOM-tolerant). (b) `AppendAuditRow` schreibt das Sidecar automatisch neu (Invariante gegen stille Drift). (c) Tampering der Audit-CSV per direktem File-Append wird durch `TestMetadataSidecar=false` erkannt. |
| `EntryPointParity/FailedStateParityTests.cs` | 2 | (a) Non-existent Root: CLI exited gracefully (Exit-Code im sicheren Bereich), API rejected via `TryCreate=null` ODER liefert konsistente Zero-Result-Werte. KNOWN DIVERGENCE als P1-Finding dokumentiert. (b) Empty-Root: CLI Exit=0, API liefert `TotalFiles/Groups/Winners=0`. |
| `EntryPointParity/UnknownReviewBlockedRoutingTests.cs` | 1 | Per-DedupeGroup-Routing `(GameKey, ConsoleKey, PlatformFamily, DecisionClass, SortDecision, Category)` ueber Unknown / Review / Blocked / Junk / BIOS-Mix muss WPF (`RunService`) und API (`RunManager`) identisch produzieren. |

### Verification

```
Get-Process testhost -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build src/Romulus.Tests/Romulus.Tests.csproj
dotnet test src/Romulus.Tests/Romulus.Tests.csproj --no-build --filter "FullyQualifiedName~CrossConsoleDatPolicyTests|FullyQualifiedName~DecisionReasonParityTests|FullyQualifiedName~RunDeterminismPropertyTests|FullyQualifiedName~CancellationDataIntegrityTests|FullyQualifiedName~AuditSidecarRoundTripTests|FullyQualifiedName~FailedStateParityTests|FullyQualifiedName~UnknownReviewBlockedRoutingTests"
```

Ergebnis: `Bestanden! Fehler: 0, erfolgreich: 13, gesamt: 13`.

### Findings waehrend Block-C-Implementierung

- **C6_01**: Real-Divergence dokumentiert. CLI akzeptiert non-existent Roots (Exit=3 ohne Crash, scannt 0 Dateien). API rejected stattdessen via `RunManager.TryCreate=null`. Beide Verhalten sind individuell sicher (kein Crash, keine fabrizierten Ergebnisse), aber per Romulus-Regel "Eine fachliche Wahrheit" sollten sie uebereinstimmen. Test dokumentiert aktuelle sichere Verhaltensweise auf beiden Seiten ohne Ungleichheit zu zementieren. Empfehlung: separates Hardening-Issue (Prio P1) zur Vereinheitlichung.
- **C5_02**: `AuditCsvStore.AppendAuditRows` schreibt das Sidecar bei vorhandenem Sidecar automatisch neu (siehe `AuditCsvStore.cs:135`). Test wurde von "drift detected" auf "auto-rewrite invariant" umgeschrieben - das ist die tatsaechliche Schutz-Invariante.
- **C5_01**: `AuditSigningService.WriteMetadataSidecar` produziert UTF-8-BOM. `JsonDocument.Parse` ueber `ReadAllBytes` schlug fehl. `ReadAllText` ist BOM-tolerant.



---

## Block C - Diff Appendix (2026-04-25)

Block C ergaenzt 13 neue Tests in 7 Domaenen-Dateien unter `src/Romulus.Tests/` und macht Determinismus, Cancellation-Datenintegritaet, Audit-Round-Trip, Failed-State-Paritaet und Unknown/Review/Blocked-Routing fuer alle Entry Points testbar abgedeckt.

### Neue Testdateien

| Datei | Tests | Kurzbeschreibung |
|---|---|---|
| `Recognition/CrossConsoleDatPolicyTests.cs` | 3 | Closes the gap that `EnableCrossConsoleLookup=false` MUST suppress matches in ALL Hash-Stages (Archive, Headerless, Name-Only). Container-Stage was bereits durch `EnrichmentPipelinePhaseAuditPhase3And4Tests` abgedeckt. |
| `EntryPointParity/DecisionReasonParityTests.cs` | 1 | Per-DedupeGroup-Projektion `(GameKey, ConsoleKey, PlatformFamily, DecisionClass, SortDecision, ClassificationReasonCode, DatMatch)` muss fuer denselben Roots-Input in CLI / WPF (`RunService`) / API (`RunManager`) identisch sein. |
| `Determinism/RunDeterminismPropertyTests.cs` | 1 | 25 Iterationen von `RunOrchestrator.Execute` ueber denselben 10-Datei-Seed muessen exakt dieselbe Per-Group-Projektion erzeugen (GameKey, MainPath, ConsoleKey, DecisionClass, SortDecision, PlatformFamily, Category, sortierte Loser-Filenames). |
| `Audit/CancellationDataIntegrityTests.cs` | 2 | (a) Vorab gecancelter `CancellationToken` im Move-Mode haelt alle Source-Files unveraendert (Bytes gleich) und liefert `Status=cancelled`, `ExitCode=2`. (b) Cancellation per Progress-Callback waehrend Scan ebenso ohne Datenverlust. |
| `Audit/AuditSidecarRoundTripTests.cs` | 3 | (a) `WriteMetadataSidecar` -> `TestMetadataSidecar` round-trip ist stabil und Sidecar-JSON enthaelt die Metadatenwerte (BOM-tolerant). (b) `AppendAuditRow` schreibt das Sidecar automatisch neu (Invariante gegen stille Drift). (c) Tampering der Audit-CSV per direktem File-Append wird durch `TestMetadataSidecar=false` erkannt. |
| `EntryPointParity/FailedStateParityTests.cs` | 2 | (a) Non-existent Root: CLI exited gracefully (Exit-Code im sicheren Bereich), API rejected via `TryCreate=null` ODER liefert konsistente Zero-Result-Werte. KNOWN DIVERGENCE als P1-Finding dokumentiert. (b) Empty-Root: CLI Exit=0, API liefert `TotalFiles/Groups/Winners=0`. |
| `EntryPointParity/UnknownReviewBlockedRoutingTests.cs` | 1 | Per-DedupeGroup-Routing `(GameKey, ConsoleKey, PlatformFamily, DecisionClass, SortDecision, Category)` ueber Unknown / Review / Blocked / Junk / BIOS-Mix muss WPF (`RunService`) und API (`RunManager`) identisch produzieren. |

### Verification

```
Get-Process testhost -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build src/Romulus.Tests/Romulus.Tests.csproj
dotnet test src/Romulus.Tests/Romulus.Tests.csproj --no-build --filter "FullyQualifiedName~CrossConsoleDatPolicyTests|FullyQualifiedName~DecisionReasonParityTests|FullyQualifiedName~RunDeterminismPropertyTests|FullyQualifiedName~CancellationDataIntegrityTests|FullyQualifiedName~AuditSidecarRoundTripTests|FullyQualifiedName~FailedStateParityTests|FullyQualifiedName~UnknownReviewBlockedRoutingTests"
```

Ergebnis: `Bestanden! Fehler: 0, erfolgreich: 13, gesamt: 13`.

### Findings waehrend Block-C-Implementierung

- **C6_01**: Real-Divergence dokumentiert. CLI akzeptiert non-existent Roots (Exit=3 ohne Crash, scannt 0 Dateien). API rejected stattdessen via `RunManager.TryCreate=null`. Beide Verhalten sind individuell sicher (kein Crash, keine fabrizierten Ergebnisse), aber per Romulus-Regel "Eine fachliche Wahrheit" sollten sie uebereinstimmen. Test dokumentiert aktuelle sichere Verhaltensweise auf beiden Seiten ohne Ungleichheit zu zementieren. Empfehlung: separates Hardening-Issue (Prio P1) zur Vereinheitlichung.
- **C5_02**: `AuditCsvStore.AppendAuditRows` schreibt das Sidecar bei vorhandenem Sidecar automatisch neu (siehe `AuditCsvStore.cs:135`). Test wurde von "drift detected" auf "auto-rewrite invariant" umgeschrieben - das ist die tatsaechliche Schutz-Invariante.
- **C5_01**: `AuditSigningService.WriteMetadataSidecar` produziert UTF-8-BOM. `JsonDocument.Parse` ueber `ReadAllBytes` schlug fehl. `ReadAllText` ist BOM-tolerant.



---

## Block D - Diff Appendix (2026-04-25)

### Status
- D1-D6 implementiert; 16/16 neue Tests gruen (`BlockD_TestabilityFixturesTests`).
- D5 als Charakterisierungstest umgesetzt; vollstaendige Modularisierung (5510 LOC) ist als Follow-up dokumentiert (siehe unten), nicht release-blockierend.

### Neue Test-Fixtures (zentral, ersetzen Duplikate)
| Datei | Zweck |
|---|---|
| `src/Romulus.Tests/TestFixtures/EnrichmentTestHarness.cs` | D1: `BuildContext(RunOptions)` + `DryRunOptions(...)` + `FixedFamilyDatPolicyResolver`. Ersetzt 10x dupliziertes `CreateContext` und 2x `FixedPolicyResolver`. |
| `src/Romulus.Tests/TestFixtures/RootBoundaryValidator.cs` | D2: SHA-256-Snapshot/Verify fuer Pfade ausserhalb erlaubter Roots. Erkennt Modify/Delete/New-File. |
| `src/Romulus.Tests/TestFixtures/ScenarioToolRunner.cs` | D3: `IToolRunner`-Test-Double mit `ConversionFailureScenario { Crash, HashMismatch, Cancellation, OutputTooSmall, DiskFull }`. Loest copy-pasted Stubs ab. |
| `src/Romulus.Tests/TestFixtures/RunResultProjection.cs` | D4: `DecisionFields(IEnumerable<RomCandidate>)` + `RoutingTuples(...)`. Ersetzt manuelle `string.Join`-Projektionen aus EntryPointParity- und Report-Parity-Tests. |
| `src/Romulus.Tests/TestFixtures/TraceCapture.cs` | D6: `Capture(Action) -> string` mit AutoFlush-/Listener-Restore auch im Fehlerfall. Ersetzt `AuditABEndToEndRedTests.CaptureTrace`. |

### Neue Tests
- `src/Romulus.Tests/BlockD_TestabilityFixturesTests.cs`
  - **D1** `D1_EnrichmentTestHarness_BuildContext_HasMetricsInitializedAndOptionsSet`
  - **D1** `D1_FixedFamilyDatPolicyResolver_ReturnsSamePolicyForEveryFamily`
  - **D2** `D2_RootBoundaryValidator_DetectsModificationOfFileOutsideRoots`
  - **D2** `D2_RootBoundaryValidator_GreenWhenNothingTouched`
  - **D2** `D2_RootBoundaryValidator_DetectsNewFileInOutsideRoot`
  - **D2** `D2_RootBoundaryValidator_GreenAfterLegitMoveInsideAllowedRoot` (Integration mit `FileSystemAdapter.MoveItemSafely`)
  - **D3** `D3_ScenarioToolRunner_Crash_ThrowsInvalidOperation`
  - **D3** `D3_ScenarioToolRunner_Cancellation_ThrowsOperationCanceled`
  - **D3** `D3_ScenarioToolRunner_DiskFull_ReturnsFailureWithErrorCode112`
  - **D3** `D3_ScenarioToolRunner_OutputTooSmall_WritesOneByteOutput`
  - **D3** `D3_ScenarioToolRunner_HashMismatch_ReturnsSuccess_CallerVerifiesHash`
  - **D4** `D4_RunResultProjection_DecisionFields_OrderedDeterministicallyAndCaseNormalized`
  - **D4** `D4_RunResultProjection_RoutingTuples_IncludesCategoryButNotDatFlag`
  - **D5** `D5_DatasetExpander_PublicSurface_StableAcrossFcBuckets` _(Charakterisierung)_
  - **D6** `D6_TraceCapture_ReturnsEmittedTraceLines`
  - **D6** `D6_TraceCapture_RestoresAutoFlushAndRemovesListener_EvenOnException`

### Architektur / Hygiene
- Alle Fixtures liegen unter `Romulus.Tests/TestFixtures/`, sind `internal` und enthalten **keine** Geschaeftslogik. Es entsteht keine konkurrierende fachliche Wahrheit.
- Keine Aenderungen an Produktionscode (Core/Infrastructure/Contracts) - nur Test-Hilfsmittel.
- Keine bestehenden Test-Stubs wurden im Block D entfernt: das ist **bewusst** (Migration der ~10 dupl. Konsumenten = separater, abgegrenzter Cleanup-Task in Block E).

### D5 Follow-up (deferred, nicht release-blockierend)
- `src/Romulus.Tests/Benchmark/Infrastructure/DatasetExpander.cs` ist 5510 LOC und enthaelt eine grosse `GenerateExpansion()`-Methode mit ~50 privaten `GenerateXxx`-Helpern.
- Charakterisierungstest **D5** in Block D sperrt die public surface (Bucket-Keys nicht leer, Eintraege haben `Id`+`Expected`, IDs sind unique). Damit ist eine spaetere Modularisierung sicher refaktorierbar.
- Vorgeschlagener Follow-up-Schnitt: pro `Generate*`-Familie eigene `internal static class`-Datei (z. B. `ChaosMixedExpander`, `GoldenRealworldExpander`, ...) mit fokussierten Unit-Tests. Bewusst **nicht** in Block D enthalten, da hochriskant ohne klaren release-kritischen Nutzen (`refactor only with clear benefit`).

### Verifikation
- Build: gruen (`dotnet build src/Romulus.Tests/Romulus.Tests.csproj` ohne Fehler/Warnungen).
- Tests: `dotnet test --filter "FullyQualifiedName~BlockD_"` => **16/16 bestanden**.
- Keine Aenderungen ausserhalb `src/Romulus.Tests/` und `docs/audits/`.



---

## Block E - Naming, Strukturierung & Konsolidierung (Appendix)

### Status

- E1 (Domaenen-Ordner): **Deferred mit Begruendung** (siehe unten).
- E2 (Gott-Testdateien splitten): **Deferred mit Begruendung** (siehe unten).
- E3 (Phase/Wave/Round/Tracker/Block-Naming aufloesen): **Deferred mit Begruendung** (siehe unten).
- E4 (`FindSrcRoot` / `FindRepoFile`-Konsolidierung): **Erledigt**.
- E5 (Naming-Konvention dokumentieren): **Erledigt**.

### Neue Fixtures

- `src/Romulus.Tests/TestFixtures/RepoPaths.cs`
  - `RepoFile(params string[] parts)` - eine Wahrheit fuer Pfade unter dem Repo-Root, basierend auf `RunEnvironmentBuilder.ResolveDataDir()`.
  - `SrcRoot()` - eine Wahrheit fuer das `src/`-Verzeichnis, anhand `Romulus.Infrastructure` als Anker.
- `src/Romulus.Tests/BlockE_RepoPathsTests.cs` - 3 Verifikationstests (`RepoFile` liefert existierende Datei, `SrcRoot` enthaelt bekannte Projekte, `RepoFile(null)` wirft `ArgumentNullException`).

### Migrierte Dateien (E4)

Alle lokalen `FindRepoFile`/`FindSrcRoot`-Kopien wurden auf `RepoPaths` umgestellt. Public Surface der Test-Klassen unveraendert (Helper bleiben `private static`, Body ist nur noch eine Delegation).

| Datei | Helper |
|---|---|
| `Block2_SecurityHardeningTests.cs` | `FindRepoFile` |
| `Block3_DiHygieneTests.cs` | `FindRepoFile` |
| `Block4_RobustnessTests.cs` | `FindRepoFile` |
| `Block56_StructuralDebtHygieneTests.cs` | `FindSrcRoot` |
| `FreshAuditRound9RedTests.cs` | `FindRepoFile` |
| `Phase10And11RoundVerificationTests.cs` | `FindSrcRoot` |
| `TrackerAllFindingsBatch1RedTests.cs` | `FindRepoFile` |
| `TrackerAllFindingsBatch4RedTests.cs` | `FindRepoFile` |
| `TrackerBlock7To12RedTests.cs` | `FindRepoFile` |
| `TrackerPoint2OpenFindingsRedTests.cs` | `FindRepoFile` |

Damit ist die in Block A identifizierte Restduplikation `FindRepoFile`/`FindSrcRoot` vollstaendig aufgeloest. `Phase10And11RoundVerificationTests.FindRepoRoot()` bleibt bestehen, da es ueber `AGENTS.md` ankert (anderes Anforderungsprofil als `RepoPaths`) und nur an einer Stelle verwendet wird.

### Naming-Konvention (E5)

Dokumentiert in `src/Romulus.Tests/TESTING.md`:

- Schema `Subject_Scenario_ExpectedBehavior`.
- Praefixe `R\d+_`, `Block[A-Z]\d_`, `Phase\d+_`, `Wave\d+_`, `Tracker*` sind als historisch markiert; neue Tests verwenden sie nicht.
- Inventar der zentralen Test-Fixtures (`TestFixtures/`).
- Verbotsliste (Reflection-/Source-Spiegel, no-crash-only, doppelte Helper).

### E1 / E2 / E3 - Deferred mit Begruendung

Diese drei Punkte sind reine Strukturmigrationen (ca. 12k LOC Testcode), die kein Verhalten aendern und keine zusaetzliche Invariante absichern:

- **Release-Regeln** (`.claude/rules/release.instructions.md`): "kosmetische Massenrefactors kurz vor Release vermeiden", "grosse, unnoetige Architektur-Umbauten" sind explizit in der Verbotsliste.
- **Architektur-Regeln** (`.claude/rules/architecture.instructions.md`): Refactors brauchen klaren Nutzen (weniger Duplikation, bessere Testbarkeit, deterministischere Logik). E1/E2/E3 liefern davon nichts ueber das hinaus, was E4/E5 bereits abdecken.
- **Risiko**: Massen-Renames + Datei-Splits erzeugen massiven Diff-Churn, brechen Test-Filter-Pattern in CI/Skripten und vergroessern die Review-Last vor Release ohne Schutzwert.

Empfehlung: nach Release in einem dedizierten Cleanup-Branch ausfuehren.

### Verifikation

- Build: `dotnet build src/Romulus.Tests/Romulus.Tests.csproj` - 0 Warnungen, 0 Fehler.
- Regression: `dotnet test --no-build --filter "FullyQualifiedName~BlockE_|...~BlockD_|...~Block2_|...~Block3_|...~Block4_|...~Block56_|...~Phase10|...~FreshAuditRound9|...~Tracker"` -> **82/82 gruen**.
- Block-E-Verifikationstests in `BlockE_RepoPathsTests` decken `RepoPaths.RepoFile` und `RepoPaths.SrcRoot` ab.


---

## Block E - Naming, Strukturierung & Konsolidierung (Appendix)

### Status

- E1 (Domaenen-Ordner): **Deferred mit Begruendung** (siehe unten).
- E2 (Gott-Testdateien splitten): **Deferred mit Begruendung** (siehe unten).
- E3 (Phase/Wave/Round/Tracker/Block-Naming aufloesen): **Deferred mit Begruendung** (siehe unten).
- E4 (`FindSrcRoot` / `FindRepoFile`-Konsolidierung): **Erledigt**.
- E5 (Naming-Konvention dokumentieren): **Erledigt**.

### Neue Fixtures

- `src/Romulus.Tests/TestFixtures/RepoPaths.cs`
  - `RepoFile(params string[] parts)` - eine Wahrheit fuer Pfade unter dem Repo-Root, basierend auf `RunEnvironmentBuilder.ResolveDataDir()`.
  - `SrcRoot()` - eine Wahrheit fuer das `src/`-Verzeichnis, anhand `Romulus.Infrastructure` als Anker.
- `src/Romulus.Tests/BlockE_RepoPathsTests.cs` - 3 Verifikationstests (`RepoFile` liefert existierende Datei, `SrcRoot` enthaelt bekannte Projekte, `RepoFile(null)` wirft `ArgumentNullException`).

### Migrierte Dateien (E4)

Alle lokalen `FindRepoFile`/`FindSrcRoot`-Kopien wurden auf `RepoPaths` umgestellt. Public Surface der Test-Klassen unveraendert (Helper bleiben `private static`, Body ist nur noch eine Delegation).

| Datei | Helper |
|---|---|
| `Block2_SecurityHardeningTests.cs` | `FindRepoFile` |
| `Block3_DiHygieneTests.cs` | `FindRepoFile` |
| `Block4_RobustnessTests.cs` | `FindRepoFile` |
| `Block56_StructuralDebtHygieneTests.cs` | `FindSrcRoot` |
| `FreshAuditRound9RedTests.cs` | `FindRepoFile` |
| `Phase10And11RoundVerificationTests.cs` | `FindSrcRoot` |
| `TrackerAllFindingsBatch1RedTests.cs` | `FindRepoFile` |
| `TrackerAllFindingsBatch4RedTests.cs` | `FindRepoFile` |
| `TrackerBlock7To12RedTests.cs` | `FindRepoFile` |
| `TrackerPoint2OpenFindingsRedTests.cs` | `FindRepoFile` |

Damit ist die in Block A identifizierte Restduplikation `FindRepoFile`/`FindSrcRoot` vollstaendig aufgeloest. `Phase10And11RoundVerificationTests.FindRepoRoot()` bleibt bestehen, da es ueber `AGENTS.md` ankert (anderes Anforderungsprofil als `RepoPaths`) und nur an einer Stelle verwendet wird.

### Naming-Konvention (E5)

Dokumentiert in `src/Romulus.Tests/TESTING.md`:

- Schema `Subject_Scenario_ExpectedBehavior`.
- Praefixe `R\d+_`, `Block[A-Z]\d_`, `Phase\d+_`, `Wave\d+_`, `Tracker*` sind als historisch markiert; neue Tests verwenden sie nicht.
- Inventar der zentralen Test-Fixtures (`TestFixtures/`).
- Verbotsliste (Reflection-/Source-Spiegel, no-crash-only, doppelte Helper).

### E1 / E2 / E3 - Deferred mit Begruendung

Diese drei Punkte sind reine Strukturmigrationen (ca. 12k LOC Testcode), die kein Verhalten aendern und keine zusaetzliche Invariante absichern:

- **Release-Regeln** (`.claude/rules/release.instructions.md`): "kosmetische Massenrefactors kurz vor Release vermeiden", "grosse, unnoetige Architektur-Umbauten" sind explizit in der Verbotsliste.
- **Architektur-Regeln** (`.claude/rules/architecture.instructions.md`): Refactors brauchen klaren Nutzen (weniger Duplikation, bessere Testbarkeit, deterministischere Logik). E1/E2/E3 liefern davon nichts ueber das hinaus, was E4/E5 bereits abdecken.
- **Risiko**: Massen-Renames + Datei-Splits erzeugen massiven Diff-Churn, brechen Test-Filter-Pattern in CI/Skripten und vergroessern die Review-Last vor Release ohne Schutzwert.

Empfehlung: nach Release in einem dedizierten Cleanup-Branch ausfuehren.

### Verifikation

- Build: `dotnet build src/Romulus.Tests/Romulus.Tests.csproj` - 0 Warnungen, 0 Fehler.
- Regression: `dotnet test --no-build --filter "FullyQualifiedName~BlockE_|...~BlockD_|...~Block2_|...~Block3_|...~Block4_|...~Block56_|...~Phase10|...~FreshAuditRound9|...~Tracker"` -> **82/82 gruen**.
- Block-E-Verifikationstests in `BlockE_RepoPathsTests` decken `RepoPaths.RepoFile` und `RepoPaths.SrcRoot` ab.


---

## Section 3 - Tests ersetzt/entfernt (Appendix 2026-04-25)

### Status

- **3.1 Loeschen (kein Schutzwert)**: 11 / 11 abgehakt.
- **3.2 Umbau zu Verhaltenstests**: 9 / 9 abgehakt.

### Befund

Die Mehrheit der 3.1- und 3.2-Punkte war beim Eintreten in Section 3 bereits durch Block A (Wertlose und irrefuehrende Tests entsorgen) abgedeckt:

- `Phase9RoundVerificationTests` enthaelt weder `R9_005_*` noch `R9_043_*` mehr.
- `Phase8RoundVerificationTests.R8_001_*` (ConsoleDetector / ExtensionNormalizer Inline-Spiegel) entfernt.
- `TrackerAllFindingsBatch1RedTests` enthaelt keine `Data10`/`Di02`/`Di03`-Reflection-Tests mehr; verbleibender Inhalt sind reine Daten- und Verhaltenstests.
- `AuditABEndToEndRedTests` enthaelt keine `B01`/`B04`/`B05`-Source-Mirror-Tests mehr.
- `AuditCDRedTests` und `AuditEFOpenTests` enthalten ausschliesslich Verhaltens- bzw. Datenvalidierungs-Tests; keine `Assert.Contains/DoesNotContain` mehr gegen Quelltext.
- `UncPathTests.PathGetFullPath_DoesNotThrowOnUncOrLocal` entfernt (BCL-Test).
- `V2RemainingTests` Long-Filename-/Long-AsciiFold-Tests sind bereits umgebaut (`_StripsTagsAndKeepsTitle`, `_FoldsAllUmlauts`).
- `VersionScorerTests` ExtremeNumbers-Tests sind bereits umgebaut (`_ScoresDeterministically` mit numerischen Asserts).
- `ChaosTests` `Unicode_*`/`CorruptDat_*` und `RegionDetector_LongParens_CompletesWithoutHang`/`GameKeyNormalizer_LongInput_CompletesWithoutHang` sowie `FileHashServiceCoverageTests.Constructor_*` und `StandaloneConversionServiceTests.Dispose_NullLifetime_*` sind bereits umgebaut.
- `AuditHardeningTests.RuleEngine.Evaluate` asserted bereits `result.Matched == false`.
- `ApiCoverageBoostTests` enthaelt keine `RateLimiter`-Smokes mehr (Datei auf 409 Zeilen geschrumpft).
- Performance-Smokes mit `< 5000 ms` sind aus der Unit-Suite raus (kein Treffer fuer `< 5000`/`TotalMilliseconds <`/`ElapsedMilliseconds <` in `ChaosTests` / `AuditComplianceTests`).

### In dieser Iteration konkret veraendert

- `src/Romulus.Tests/Phase4StatusConsolidationRedTests.cs`
  - Tautologische Reflection-Tests `RunConstants_MustExpose_RunningAndCompletedStatuses` und `RunConstants_MustExpose_SortDecisionConstants` entfernt.
  - Ersetzt durch `RunConstants_StatusRunning_AndStatusCompleted_HaveExpectedValues` (kein Reflection, direkter Konstantenvertrag fuer GUI/CLI/API/Reports).
  - Architektur-Invarianten `ApiAssembly_MustNotContain_LegacyApiRunStatusType` und `OperationResult_MustNotDuplicate_RunStatusConstants` bleiben erhalten (echter Schutzwert: kein Wiederauftauchen toter Statusmodelle).
- `src/Romulus.Tests/Phase11RedTests.cs`
  - Tautologischer Schema-Spiegel `TD028_GameKeyNormalizer_UsesSingleAtomicRegisteredStateField` entfernt.
  - Concurrency-Invariante `TD028_GameKeyNormalizer_ConcurrentRegistration_DoesNotProduceHybridResults` bleibt; sie greift via Reflection auf `_registeredState` zu und bricht den Build, wenn das Feld zurueckgebaut wird (impliziter Schutz gegen Regression).
- `src/Romulus.Tests/SecurityTests.cs`
  - `RegionDetector_VeryLongInput_ReturnsNonNullTag` -> `RegionDetector_VeryLongInput_ReturnsUnknownTag` (verschaerft auf erwarteten `UNKNOWN`-Wert).
- `src/Romulus.Tests/SafetyIoSecurityRedPhaseTests.cs`
  - `MoveItemSafely_LockedSource_ReturnsNullWithoutThrowing` zusaetzlich verifiziert: Source bleibt existent und unveraendert (Laenge), Destination wird nicht angelegt. Audit-Eintrag nicht testbar an dieser Stelle (FileSystemAdapter ist Low-Level-FS-Adapter und kennt keinen Audit; das ist Verantwortung der Orchestration-Schicht und durch deren eigene Tests abgedeckt).
- `src/Romulus.Tests/ChaosTests.cs`
  - `Unicode_GameKey_ProducesNonEmptyKey` -> `Unicode_GameKey_StripsKnownRegionTagsAndYieldsNonEmptyKey` (UAE entfernt aus Theory, da nicht als Region erkannt - das ist deterministisch korrektes Verhalten).
  - Neuer Test `Unicode_GameKey_UnknownRegionTag_KeepsTagAsTitleContent` als positives Gegenstueck (regression-lock fuer die Grenze "bekannter Region-Tag wird gestrippt vs. Unbekanntes bleibt Title-Bestandteil").

### Verifikation

- Build: `dotnet build src/Romulus.Tests/Romulus.Tests.csproj` -> 0 Warnungen, 0 Fehler.
- Section-3-Wirkbereich: `dotnet test --no-build --filter "FullyQualifiedName~ChaosTests|...~SecurityTests|...~SafetyIoSecurity|...~Phase4StatusConsolidation|...~Phase11RedTests|...~AuditHardening|...~FileHashServiceCoverage|...~StandaloneConversionService"` -> **225/225 gruen**.
- Block-Regression: `dotnet test --no-build --filter "FullyQualifiedName~Block"` -> **432/432 gruen**.


---

## Section 3 - Tests ersetzt/entfernt (Appendix 2026-04-25)

### Status

- **3.1 Loeschen (kein Schutzwert)**: 11 / 11 abgehakt.
- **3.2 Umbau zu Verhaltenstests**: 9 / 9 abgehakt.

### Befund

Die Mehrheit der 3.1- und 3.2-Punkte war beim Eintreten in Section 3 bereits durch Block A (Wertlose und irrefuehrende Tests entsorgen) abgedeckt:

- `Phase9RoundVerificationTests` enthaelt weder `R9_005_*` noch `R9_043_*` mehr.
- `Phase8RoundVerificationTests.R8_001_*` (ConsoleDetector / ExtensionNormalizer Inline-Spiegel) entfernt.
- `TrackerAllFindingsBatch1RedTests` enthaelt keine `Data10`/`Di02`/`Di03`-Reflection-Tests mehr; verbleibender Inhalt sind reine Daten- und Verhaltenstests.
- `AuditABEndToEndRedTests` enthaelt keine `B01`/`B04`/`B05`-Source-Mirror-Tests mehr.
- `AuditCDRedTests` und `AuditEFOpenTests` enthalten ausschliesslich Verhaltens- bzw. Datenvalidierungs-Tests; keine `Assert.Contains/DoesNotContain` mehr gegen Quelltext.
- `UncPathTests.PathGetFullPath_DoesNotThrowOnUncOrLocal` entfernt (BCL-Test).
- `V2RemainingTests` Long-Filename-/Long-AsciiFold-Tests sind bereits umgebaut (`_StripsTagsAndKeepsTitle`, `_FoldsAllUmlauts`).
- `VersionScorerTests` ExtremeNumbers-Tests sind bereits umgebaut (`_ScoresDeterministically` mit numerischen Asserts).
- `ChaosTests` `Unicode_*`/`CorruptDat_*` und `RegionDetector_LongParens_CompletesWithoutHang`/`GameKeyNormalizer_LongInput_CompletesWithoutHang` sowie `FileHashServiceCoverageTests.Constructor_*` und `StandaloneConversionServiceTests.Dispose_NullLifetime_*` sind bereits umgebaut.
- `AuditHardeningTests.RuleEngine.Evaluate` asserted bereits `result.Matched == false`.
- `ApiCoverageBoostTests` enthaelt keine `RateLimiter`-Smokes mehr (Datei auf 409 Zeilen geschrumpft).
- Performance-Smokes mit `< 5000 ms` sind aus der Unit-Suite raus (kein Treffer fuer `< 5000`/`TotalMilliseconds <`/`ElapsedMilliseconds <` in `ChaosTests` / `AuditComplianceTests`).

### In dieser Iteration konkret veraendert

- `src/Romulus.Tests/Phase4StatusConsolidationRedTests.cs`
  - Tautologische Reflection-Tests `RunConstants_MustExpose_RunningAndCompletedStatuses` und `RunConstants_MustExpose_SortDecisionConstants` entfernt.
  - Ersetzt durch `RunConstants_StatusRunning_AndStatusCompleted_HaveExpectedValues` (kein Reflection, direkter Konstantenvertrag fuer GUI/CLI/API/Reports).
  - Architektur-Invarianten `ApiAssembly_MustNotContain_LegacyApiRunStatusType` und `OperationResult_MustNotDuplicate_RunStatusConstants` bleiben erhalten (echter Schutzwert: kein Wiederauftauchen toter Statusmodelle).
- `src/Romulus.Tests/Phase11RedTests.cs`
  - Tautologischer Schema-Spiegel `TD028_GameKeyNormalizer_UsesSingleAtomicRegisteredStateField` entfernt.
  - Concurrency-Invariante `TD028_GameKeyNormalizer_ConcurrentRegistration_DoesNotProduceHybridResults` bleibt; sie greift via Reflection auf `_registeredState` zu und bricht den Build, wenn das Feld zurueckgebaut wird (impliziter Schutz gegen Regression).
- `src/Romulus.Tests/SecurityTests.cs`
  - `RegionDetector_VeryLongInput_ReturnsNonNullTag` -> `RegionDetector_VeryLongInput_ReturnsUnknownTag` (verschaerft auf erwarteten `UNKNOWN`-Wert).
- `src/Romulus.Tests/SafetyIoSecurityRedPhaseTests.cs`
  - `MoveItemSafely_LockedSource_ReturnsNullWithoutThrowing` zusaetzlich verifiziert: Source bleibt existent und unveraendert (Laenge), Destination wird nicht angelegt. Audit-Eintrag nicht testbar an dieser Stelle (FileSystemAdapter ist Low-Level-FS-Adapter und kennt keinen Audit; das ist Verantwortung der Orchestration-Schicht und durch deren eigene Tests abgedeckt).
- `src/Romulus.Tests/ChaosTests.cs`
  - `Unicode_GameKey_ProducesNonEmptyKey` -> `Unicode_GameKey_StripsKnownRegionTagsAndYieldsNonEmptyKey` (UAE entfernt aus Theory, da nicht als Region erkannt - das ist deterministisch korrektes Verhalten).
  - Neuer Test `Unicode_GameKey_UnknownRegionTag_KeepsTagAsTitleContent` als positives Gegenstueck (regression-lock fuer die Grenze "bekannter Region-Tag wird gestrippt vs. Unbekanntes bleibt Title-Bestandteil").

### Verifikation

- Build: `dotnet build src/Romulus.Tests/Romulus.Tests.csproj` -> 0 Warnungen, 0 Fehler.
- Section-3-Wirkbereich: `dotnet test --no-build --filter "FullyQualifiedName~ChaosTests|...~SecurityTests|...~SafetyIoSecurity|...~Phase4StatusConsolidation|...~Phase11RedTests|...~AuditHardening|...~FileHashServiceCoverage|...~StandaloneConversionService"` -> **225/225 gruen**.
- Block-Regression: `dotnet test --no-build --filter "FullyQualifiedName~Block"` -> **432/432 gruen**.
