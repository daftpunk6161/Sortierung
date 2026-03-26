---
goal: "Romulus Release Roadmap — Strukturierter Implementierungsplan aller offenen Themen aus OPEN_ITEMS.md (§1–§26)"
version: 1.1
date_created: 2026-03-25
last_updated: 2026-03-25
owner: daftpunk6161
status: 'In Progress'
tags: ['architecture', 'feature', 'infrastructure', 'chore', 'bug']
---

# Introduction

![Status: Planned](https://img.shields.io/badge/status-Planned-blue)

Dieser Plan konsolidiert alle ~280 offenen Items aus [OPEN_ITEMS.md](OPEN_ITEMS.md) (§1–§26) sowie **52 Findings aus dem 10-Etappen-QA-Audit** (Etappen 1–9 + Gesamt-Audit) in einen priorisierten, phasenbasierten Implementierungsplan.  
Die Sequenzierung folgt dem Prinzip **Korrektheit → Sicherheit → Qualität → Architektur → UX → Polish** und respektiert die Kernregel des Projekts: *Stabilität vor Feature-Hype*.

**8 Phasen** mit klaren Abhängigkeiten, wobei Tasks innerhalb einer Phase parallelisierbar sind, sofern keine intra-Phase-Dependency deklariert ist.

### Phasen-Übersicht

| Phase | Titel | Priorität | Quellen (§) + Audit | Tasks | Fortschritt |
|-------|-------|-----------|---------------------|-------|-------------|
| 1 | Security & Critical Bug Fixes | P0 — Release-Blocker | §16, §22, Audit P1-01/P1-03/E6 | 22 | **22/22 ✅** |
| 2 | Recognition Quality — Category & Sorting | P0 — Release-Blocker | §20, §21 | 19 | **19/19 ✅** |
| 3 | Detection Recall & Stub-Generatoren | P1 — Hoch | §24 | 11 | **11/11 ✅** |
| 4 | Conversion Domain Completion | P1 — Hoch | §22, §23, Audit P1-06/E9 | 22 | **22/22 ✅** |
| 5 | Core & Pipeline Architecture | P2 — Mittel | §12, §14, §15, §17, §18, Audit P1-02/P1-04/P1-05/E2-E9 | 39 | **26/39** |
| 6 | Benchmark & Quality Assurance | P2 — Mittel | §3–§7, §19, §25, §26, Audit E9 | 29 | **28/29** |
| 7 | GUI/UX Overhaul | P3 — Normal | §8, §9, §13, §23 (UX), Audit P1-07/UJ | 19 | **19/19 ✅** |
| 8 | Polish, Accessibility & Epics | P3 — Normal | §1, §2, §10, §11 | 16 | **7/16** |

> **Hinweis:** Tasks TASK-144 bis TASK-176 stammen aus dem konsolidierten 10-Etappen-Audit (Etappen 1–9 + Gesamt-Audit). Sie sind mit `AUDIT` Prefix und Finding-ID gekennzeichnet.

---

## 1. Requirements & Constraints

### Nicht verhandelbar (aus `.claude/rules/cleanup.instructions.md`)

- **REQ-001**: Kein Datenverlust — Standard ist Move-to-Trash / Audit / Undo-fähig
- **REQ-002**: Determinismus — gleiche Inputs → gleiche Outputs (GameKey, Region, Score, Winner, Preview ↔ Execute)
- **REQ-003**: Keine doppelte Logik — Single Source of Truth für jede Geschäftsregel
- **REQ-004**: Preview/Execute/Report-Parität über GUI/CLI/API
- **REQ-005**: Keine Alibi-Tests — Tests müssen reale Fehler finden können

### Sicherheit

- **SEC-001**: Path Traversal verhindern — Root-validierte Pfadauflösung vor Move/Copy/Delete
- **SEC-002**: Zip-Slip verhindern — Archive Entries gegen Zielpfad validieren
- **SEC-003**: NTFS Reparse Points / ADS blockieren
- **SEC-004**: CSV-Injection in Exports verhindern
- **SEC-005**: HTML-Escaping in Reports durchsetzen
- **SEC-006**: Externe Tools: Hash-Verifizierung, Exit-Code-Prüfung, Timeout/Retry

### Architektur

- **CON-001**: Dependency-Richtung: Entry Points → Infrastructure → Core → Contracts (nie umgekehrt)
- **CON-002**: Keine I/O in `RomCleanup.Core`
- **CON-003**: MVVM in WPF — keine Businesslogik in Code-Behind
- **CON-004**: Services per Constructor Injection
- **CON-005**: .NET 10, C# 14, `net10.0` / `net10.0-windows`

### Patterns

- **PAT-001**: TDD-Workflow: Red → Green → Refactor
- **PAT-002**: Run-Ergebnisse über `RunProjection` (eine zentrale KPI-Quelle)
- **PAT-003**: Pipeline Phases als typisierte Steps mit `IPipelinePhase<TIn,TOut>`
- **PAT-004**: Conversion Graph als datengesteuerte Routing-Engine

### Guidelines

- **GUD-001**: Änderungen nur mit klarem Nutzen — kein kosmetisches Refactoring als Selbstzweck
- **GUD-002**: Jede Phase muss eigenständig testbar und merge-fähig sein
- **GUD-003**: Größere Umbauten schrittweise — Feature-Parität vor und nach jeder Phase gewährleisten

---

## 2. Implementation Steps

### Phase 1 — Security & Critical Bug Fixes

- GOAL-001: Alle P0/P1-Sicherheitslücken schließen und aktive Bugs in der Conversion-Engine beheben. Das System darf keine bekannten Pfad-Traversal-, ADS- oder Zip-Bomb-Lücken mehr enthalten.

- [x] **TASK-001**: **§16 P0**: Destination-Root-Containment bei Move-Operationen — `MoveItemSafely(source, dest, allowedRoot)` Overload in `IFileSystem` + `FileSystemAdapter`. NFC-normalisierte Pfad-Prefix-Validierung vor Move.
- [x] **TASK-002**: **§16 P1**: NTFS Alternate Data Streams blockieren — `:`-Check nach Drive-Letter-Stripping in `SafetyValidator.NormalizePath()`.
- [x] **TASK-003**: **§16 P1**: Extended-Length-Prefix `\\?\` und `\\.\` in `NormalizePath` abgelehnt. War bereits implementiert.
- [x] **TASK-004**: **§16 P1**: Rollback ohne `.meta.json`-Sidecar blockiert. War bereits implementiert in `AuditSigningService.Rollback()`.
- [x] **TASK-005**: **§16 P1**: Rollback über Reparse-Points — `IsReparsePoint`-Check. War bereits implementiert.
- [x] **TASK-006**: **§16 P2**: Trailing-Dot/Space-Rejection per Segment in `SafetyValidator.NormalizePath()`. Äußere Leerzeichen werden sicher getrimmt.
- [x] **TASK-007**: **§16 P2**: ReadOnly-Attribut vor Delete. War bereits implementiert in `FileSystemAdapter.DeleteFile()`.
- [x] **TASK-008**: **§16 P2**: Locked-File Handling — `MoveItemSafely` gibt `null` zurück. War bereits implementiert.
- [x] **TASK-009**: **§16 P2**: Zip-Bomb Compression-Ratio-Limit — `MaxCompressionRatio = 50.0` (strenger als geplante 100.0). War bereits implementiert.
- [x] **TASK-010**: **§16 P2**: DTD Processing — `DtdProcessing.Prohibit` mit Fallback auf `Ignore`. War bereits implementiert.
- [x] **TASK-011**: **§22 L7 — BUG BEHOBEN**: ARCADE/NEOGEO in `BlockedAutoSystems`. War bereits implementiert.
- [x] **TASK-012**: **§22 L2**: Multi-File-Conversion atomar — deterministische CUE-Sortierung + `ConvertMultiCueArchive()` mit Rollback bei Fehler.
- [x] **TASK-013**: Tests in `SafetyIoSecurityPhase1Tests.cs` — 32 Tests für TASK-001 bis TASK-012.
- [x] **TASK-014**: Tests: ARCADE/NEOGEO Regression-Tests in `SafetyIoSecurityPhase1Tests.cs`.
- [x] **TASK-015**: Tests: Multi-File atomicity / deterministic CUE sort Test in `SafetyIoSecurityPhase1Tests.cs`.
- [x] **TASK-016**: Verify: `dotnet test` — 5468 passed, 0 failures.

#### Audit-Integration Phase 1

- [x] **TASK-144**: **AUDIT P1-01**: PreferRegions-Divergenz behoben — `RunConstants.DefaultPreferRegions` als Single Source of Truth. `RunOptionsBuilder.Normalize()` mit Trim/Upper/Dedup/Fallback. `SettingsDto` referenziert zentrale Konstante.
- [x] **TASK-145**: **AUDIT P1-03**: Sidecar-Status spiegelt `RunOutcome` — `WriteCompletedAuditSidecar()` akzeptiert `RunOutcome?` Parameter, schreibt `completed`, `completed_with_errors` oder `failed`.
- [x] **TASK-146**: **AUDIT S-04/E6**: HMAC-Key wird persistent in Datei gespeichert (`keyFilePath`). War bereits implementiert.
- [x] **TASK-147**: **AUDIT T-02/S-01/E5-E6**: Write-Ahead Audit Pattern — `MOVE_PENDING` vor Move, `Move` bei Erfolg, `MOVE_FAILED` bei Fehler. Bestehende Audit-Invarianztests angepasst.
- [x] **TASK-148**: Tests in `SafetyIoSecurityPhase1Tests.cs` — PreferRegions-Parität, Sidecar-Status, HMAC-Persistenz, Write-Ahead-Audit-Pattern.

**Abnahmekriterien Phase 1:**
- Kein Move außerhalb erlaubter Roots möglich
- ADS/Reparse/ExtendedPrefix werden rejected
- ARCADE/NEOGEO-ZIPs werden nicht mehr konvertiert
- Multi-File-Sets werden als atomische Einheit behandelt
- PreferRegions identisch über alle 3 Entry Points
- Sidecar-Status spiegelt tatsächlichen RunOutcome
- Alle Security-Tests grün

---

### Phase 2 — Recognition Quality: Category & Sorting

- GOAL-002: Kategorie-Erkennungsrate von 23 % auf >85 % heben. Unsafe Sort Rate von 12,67 % auf <3 % senken. 4-Gate-Modell für Sorting-Entscheidungen implementieren.

**Abhängigkeit:** Phase 1 muss abgeschlossen sein (Safety-Basics für Safe Move).

- [x] **TASK-017**: **§20 Fix 2**: `IsLikelyJunkName` aus `ConsoleDetector` (`src/RomCleanup.Core/Classification/ConsoleDetector.cs`) entfernen. Kategorie-Achse und Konsolen-Achse vollständig entkoppeln.
- [x] **TASK-018**: **§20 Fix 1**: Extension-Aware `FileClassifier` — neues Overload `Classify(string baseName, string extension, long sizeBytes)` in `src/RomCleanup.Core/Classification/FileClassifier.cs`. Extension-basierte Junk-Erkennung (`.txt`, `.jpg`, `.exe`, `.nfo` → NonGame/Junk).
- [x] **TASK-019**: **§20 Fix 3**: Size-Validation in `EnrichmentPipelinePhase` — 0-Byte-Dateien als `Category.Unknown` klassifizieren (`src/RomCleanup.Infrastructure/Orchestration/` phase step).
- [x] **TASK-020**: **§20 Fix 4**: Non-ROM-Extension-Blocklist in `StreamingScanPipelinePhase` — `.txt`, `.jpg`, `.exe`, `.html`, `.url`, `.nfo` direkt überspringen.
- [x] **TASK-021**: **§20 Fix 5**: Konsolen-Aware Junk-Sortierung — `_TRASH_JUNK/{ConsoleKey}/` statt flachem Ordner in `ConsoleSorter`-Logik (`src/RomCleanup.Infrastructure/Sorting/`).
- [x] **TASK-022**: **§20 Fix 6**: `JunkClassified`-Metrik + `categoryRecognitionRate` in `MetricsAggregator` (`src/RomCleanup.Tests/Benchmark/MetricsAggregator.cs`).
- [x] **TASK-023**: **§20 Fix 7–10**: `NonGame`-Kategorie, `ArchiveContent`-Check, `CategoryOverride` in `consoles.json`, Confidence-Penalty bei Unknown+ambiguous.
- [x] **TASK-024**: **§21**: Hard/Soft Evidence Classification — `DetectionHypothesis` (`src/RomCleanup.Core/Classification/DetectionHypothesis.cs`) um `IsHardEvidence`-Flag erweitern. Hard = DatHash, DiscHeader, CartridgeHeader, UniqueExtension.
- [x] **TASK-025**: **§21**: Soft-Only Confidence Cap = 65 in `HypothesisResolver` (`src/RomCleanup.Core/Classification/HypothesisResolver.cs`). Single-Source Caps pro Detection-Source.
- [x] **TASK-026**: **§21**: `SortDecision` Enum (`Sort`, `Review`, `Blocked`, `DatVerified`) in `src/RomCleanup.Contracts/Models/SortingModels.cs`.
- [x] **TASK-027**: **§21**: `ConsoleDetectionResult` erweitern — `HasHardEvidence`, `IsSoftOnly`, `SortDecision` Felder. `CandidateFactory` um SortDecision-Berechnung erweitern.
- [x] **TASK-028**: **§21**: 4-Gate-Modell implementieren — Category Gate → Confidence Gate → Evidence Gate → Conflict Gate. Integration in `StandardPhaseSteps`.
- [x] **TASK-029**: **§21**: `ConsoleSorter` berücksichtigt `SortDecision` statt nur Confidence ≥ 80. Review-Bucket: Dateien mit `SortDecision.Review` in separates Verzeichnis.
- [x] **TASK-030**: **§21**: `MetricsAggregator` um SortDecision-Counts erweitern (SortCount, ReviewCount, BlockedCount). `GroundTruthComparator` um SortDecision-Verdikt.
- [x] **TASK-031**: **§21**: CLI/API/GUI-Parität für Review-Bucket sicherstellen. Review-Items müssen in DryRun, Move-Summary und API-Response erscheinen.
- [x] **TASK-032**: Tests: RED-Tests für Category-Fixes (TASK-017 bis TASK-023) — `FileClassifierTests.cs`, neue Category-Benchmark-Assertions.
- [x] **TASK-033**: Tests: RED-Tests für 4-Gate-Modell und SortDecision — `HypothesisResolverTests.cs`, `ConsoleSorterTests.cs`.
- [x] **TASK-034**: Benchmark: CategoryRecognitionRate Gate ≥ 85 % und UnsafeSortRate Gate ≤ 3 % in QualityGateTests.
- [x] **TASK-035**: Verify: `dotnet test src/RomCleanup.Tests/ --nologo` — 0 Failures. Benchmark-Gates grün.

**Abnahmekriterien Phase 2:**
- CategoryRecognitionRate ≥ 85 % im Benchmark
- UnsafeSortRate ≤ 3 %
- Gate-Abfangrate > 0 % (Review + Blocked Buckets nicht leer)
- SortDecision bestimmt Ziel statt Confidence-only
- Extension-basierte Junk-Erkennung aktiv

---

### Phase 3 — Detection Recall & Stub-Generatoren

- GOAL-003: 48 von 60 Missed Entries recovern. Neue Stub-Generatoren für fehlende Disc-Systeme. PS3-Detection implementieren.

**Abhängigkeit:** Phase 2 (Category/Evidence-Klassifikation muss stehen).

- [x] **TASK-036**: **§24 RC-1**: Filename-Collision behoben — `BuildRelativePath` nutzt `{dir}/{entryId}/{file}` für eindeutige Pfade. `BenchmarkFixture` generiert auch Holdout-Stubs.
- [x] **TASK-037**: **§24 RC-2**: `StubGeneratorDispatch` Fallback-Kaskade validiert (Stub → PrimaryMethod → ConsoleMap → ExtOnly).
- [x] **TASK-038**: **§24 RC-3a**: `OperaFsGenerator` für 3DO — Opera-FS-Signatur mit Record-Version-Byte (0x01). Fix: `data[6] = 0x01`.
- [x] **TASK-039**: **§24 RC-3b**: `BootSectorTextGenerator` — 5 Varianten (NEOCD, PCECD, PCFX, JAGCD, CD32) getestet.
- [x] **TASK-040**: **§24 RC-3c**: `XdvdfsGenerator` — XBOX und X360 Varianten getestet.
- [x] **TASK-041**: **§24 RC-3d**: `Ps3PvdGenerator` — PVD mit PS3-Marker getestet.
- [x] **TASK-042**: **§24 RC-3e**: `StubGeneratorRegistry` 19+ Disc-Systeme registriert, `DiscMap` validiert.
- [x] **TASK-043**: **§24 RC-4**: PS3-Detection via `RxPs3Marker` in `DiscHeaderDetector` validiert (6 Tests).
- [x] **TASK-044**: **§24 RC-5**: `.bin`-UNKNOWN-Limit dokumentiert in `benchmark/docs/detection-limits.md`.
- [x] **TASK-045**: Benchmark-Gate: Easy/Medium Missed ≤ 12 verifiziert (3 medium misses). 26 Total: 17 hard/adversarial = bekannte Limits.
- [x] **TASK-046**: PS3-Detection-Tests in `Phase3DetectionRecallTests.cs` (6 PS3-spezifische Tests).

**Abnahmekriterien Phase 3:**
- ✅ Missed Entries (easy/medium) ≤ 12 — Ist: 3 (alle medium, `.bin`-Dateien ohne Kontext)
- ✅ PS3-ISOs werden korrekt erkannt (6 Tests: PS3_DISC.SFB, PS3_GAME, PS3VOLUME, PLAYSTATION3, Not-PS2, Not-PS1)
- ✅ Alle neuen Stub-Generatoren erzeugen valide, detektierbare Stubs (OperaFs, BootSector, Xdvdfs, Ps3Pvd)
- ✅ Ground-Truth Pfade eindeutig (`BuildRelativePath` mit `{dir}/{entryId}/{file}` Pattern)
- ✅ 28 Phase-3-Tests + 5514 Gesamttests grün

---

### Phase 4 — Conversion Domain Completion

- GOAL-004: ConversionPolicy für alle 65 Systeme definieren. Verbleibende Lücken L3–L12 schließen. Conversion UX-Modell (Metriken, Plan-Preview, Regeln) implementieren.

**Abhängigkeit:** Phase 1 (L7/L2 Bug Fixes), Phase 2 (Category als Input für ConversionPolicy).

- [x] **TASK-047**: **§22 L1**: `ConversionPolicy`-Feld in `data/consoles.json` für alle 65 Systeme einführen (`Auto`/`ArchiveOnly`/`ManualOnly`/`None`). `ConversionRegistryLoader` (`src/RomCleanup.Infrastructure/Conversion/ConversionRegistryLoader.cs`) parst das neue Feld.
- [x] **TASK-048**: **§22 L3**: PS2 CD vs DVD Heuristik — `createcd` für <700 MB, `createdvd` für ≥700 MB. Threshold aus `ConversionThresholds.CdImageThresholdBytes` in `src/RomCleanup.Contracts/Models/ConversionPolicyModels.cs`.
- [x] **TASK-049**: **§22 L4**: CSO→CHD Pipeline — CSO→ISO Decompression als Zwischenschritt. Prüfen ob ciso/maxcso integrierbar (Tool-Availability-Check).
- [x] **TASK-050**: **§22 L5**: NKit-Warning — `SourceIntegrityClassifier` (`src/RomCleanup.Core/Conversion/SourceIntegrityClassifier.cs`) erkennt NKit als `Lossy`. `ConversionSafety = Risky`. Nutzer-Warnung in GUI/CLI/API.
- [x] **TASK-051**: **§22 L6**: CDI-Input als `ManualOnly` behandeln — DiscJuggler-Format hat Truncation-Risiko. ConversionPolicyEvaluator returns `ManualOnly`.
- [x] **TASK-052**: **§22 L8**: 38 Systeme ohne `ConversionTarget` in `consoles.json` ergänzen. Default = `None` für unbekannte Systeme.
- [x] **TASK-053**: **§22 L9**: RVZ-Verifizierung verbessern — `dolphintool verify` statt nur Magic-Byte + Size in `DolphinToolInvoker` (`src/RomCleanup.Infrastructure/Conversion/ToolInvokers/DolphinToolInvoker.cs`).
- [x] **TASK-054**: **§22 L10/L12**: Format-Prioritäten → Single Source of Truth. `FormatConverterAdapter.DefaultBestFormats` und `FeatureService.GetTargetFormat()` aus gleicher JSON-Config lesen.
- [x] **TASK-055**: **§22 L11**: Compression-Ratios aus Hardcoded-Werten nach `data/conversion-registry.json` auslagern.
- [x] **TASK-056**: **§22 Matrix**: Lossy→Lossy-Pfade blockieren (CSO→WBFS, NKit→GCZ). `ConversionGraph` Edge-Weight = `Blocked` für Lossy→Lossy.
- [x] **TASK-057**: **§22 Matrix**: Encrypted PBP erkennen und blockieren. `.wad`/`.dol` als Skip markieren.
- [x] **TASK-058**: **§22 Matrix**: Multi-Disc-Set (M3U) als atomische Einheit konvertieren. Kein Teilconvert.
- [x] **TASK-059**: **§23**: `ConversionPlan`-Objekt vor Ausführung berechnen — `ConversionPlanner.CreatePlan()` (`src/RomCleanup.Core/Conversion/ConversionPlanner.cs`) liefert Preview-fähigen Plan.
- [x] **TASK-060**: **§23**: Fehlende Metriken in `RunProjection` — `ConvertBlockedCount`, `ConvertLossyWarningCount`, `ConvertVerifyPassedCount`, `ConvertVerifyFailedCount`, `ConvertSavedBytes`.
- [x] **TASK-061**: **§23 R-01 bis R-10**: 10 normative Conversion-Regeln als Invarianten-Tests implementieren. Jede Regel als eigener Test in `ConversionInvariantTests.cs`.
- [x] **TASK-062**: **§23 CLI**: DryRun JSON-Output pro Konvertierungsplan und Move-Summary in `CliOutputWriter` (`src/RomCleanup.CLI/CliOutputWriter.cs`).
- [x] **TASK-063**: **§23 API**: `ConversionReport` in `ApiRunResult` — `ApiRunResultMapper` (`src/RomCleanup.Api/ApiRunResultMapper.cs`) erweitern.
- [x] **TASK-064**: Tests: ConversionPolicy-Tests für alle 65 Systeme (Policy aus consoles.json geladen, korrekte Werte).
- [x] **TASK-065**: Tests: ConversionPlan Invarianten (Lossy-Blocking, PBP-Encryption, M3U-Atomicity).
- [x] **TASK-066**: Verify: `dotnet test src/RomCleanup.Tests/ --nologo` — 0 Failures. Conversion-Parität GUI/CLI/API.

#### Audit-Integration Phase 4

- [x] **TASK-149**: **AUDIT P1-06/E9-01**: CUE-Selektion nicht-deterministisch — `Directory.GetFiles()[0]` in `FormatConverterAdapter.cs` (L541-553) liefert unsortiert den ersten Treffer. Bei mehreren `.cue`-Dateien im selben Verzeichnis ist die Auswahl zufällig. Fix: `.Order().First()` (alphabetische Auswahl). Betrifft: `FormatConverterAdapter.cs`.
- [x] **TASK-150**: **AUDIT E9-02**: 7z-Hash-Reihenfolge nicht deterministisch — `ArchiveHashService` iteriert Einträge in Archiv-interner Reihenfolge ohne Sortierung. Gleicher Archiv-Inhalt ergibt unterschiedliche Gesamthashes je nach Erstellreihenfolge. Fix: Entries vor Hashing alphabetisch sortieren. Betrifft: `ArchiveHashService.cs`.

**Abnahmekriterien Phase 4:**
- Alle 65 Systeme haben ConversionPolicy in consoles.json
- Lossy→Lossy-Pfade blockiert
- ConversionPlan als Preview verfügbar (DryRun, CLI, API)
- CUE-Selektion und 7z-Hashing deterministisch
- 10 Conversion-Regeln als Invarianten geprüft
- ConversionReport in CLI- und API-Output

---

### Phase 5 — Core & Pipeline Architecture

- GOAL-005: Core-Typen modernisieren (RomCandidate als Record, RunProjection als Single KPI-Source). Orchestrator von 380 auf ≤250 LOC ausdünnen. Entry Points (CLI, API) architekturell bereinigen. Test-Seams einführen.

**Abhängigkeit:** Phase 2 (SortDecision in RomCandidate), Phase 4 (ConversionPlan-Integration in Pipeline).

- [ ] **TASK-067**: **§12**: `RomCandidate` von `sealed class` auf `sealed record` in `src/RomCleanup.Contracts/Models/RomCandidate.cs`. `FileCategory` als Enum durchgängig.
- [ ] **TASK-068**: **§12**: `CandidateFactory` als Single-Point-of-Construction mit `__BIOS__` Prefix. SHA256-Fallback für Null/Whitespace-GameKey.
- [ ] **TASK-069**: **§12**: `FilterToBestCategory` vor Winner-Selection (GAME > BIOS > NonGame > JUNK > UNKNOWN). `DedupeGroup` als sealed record.
- [ ] **TASK-070**: **§12**: `RunProjection` als einzige KPI-Quelle. `ProjectionFactory` (static, pure) in Core. RunResult über `RunResultBuilder` (Append-Pattern).
- [ ] **TASK-071**: **§12**: `IPipelinePhase<TIn,TOut>` Interface. Typisierte Phases: Scan, Enrichment, Dedupe, JunkRemoval, DedupeMove, Convert, ConsoleSort.
- [ ] **TASK-072**: **§12**: Conversion-Invariante nachschärfen — nie `converted++` vor Verify, nie Source löschen bei Verify-Failure. `Converted + Errors + Skipped == Attempted`.
- [ ] **TASK-073**: **§18 Welle A**: `IRunEnvironment` Interface + Factory. `RunOptionsFactory` + `IRunOptionsSource` für alle 3 Entry Points. `SharedServiceRegistration.AddRomCleanupCore()`.
- [ ] **TASK-074**: **§18 Welle B**: `PipelineState` (set-once Container). `PhaseStepResult` + typisierte Phase-Results. `PhasePlanBuilder` statt Closure-basiertem Phase-Plan.
- [ ] **TASK-075**: **§18 Welle C**: Orchestrator ausdünnen — `ReportPhaseStep`, `AuditSealPhaseStep`, `DeferredAnalysisPhaseStep` extrahieren. `BuildStandardPhasePlan()` → `PhasePlanBuilder.Build()`.
- [ ] **TASK-076**: **§18 Welle D**: `RunManager` → `RunLifecycleManager` + `ApiRunResultMapper` ✅ (bereits extrahiert — verifizieren). CLI `Program.cs` auf Factory-Pattern. `ReportPathResolver` als shared Service.
- [x] **TASK-077**: **§18**: Toter Code entfernen — `PipelineEngine` + `PipelineModels`, `EventBus` + `EventBusModels` (falls vorhanden). ✅ Dateien existieren bereits nicht mehr.
- [ ] **TASK-078**: **§14**: CLI-Architektur verifizieren — `CliArgsParser`, `CliOptionsMapper`, `CliOutputWriter` existieren bereits. Strenge Wert-Validierung (Flag ohne Wert → Exit Code 3) ergänzen. `CliDryRunOutput` als typisiertes Record.
- [ ] **TASK-079**: **§15**: API-Parity — `RunRequest` um `ConflictPolicy`, `ConvertOnly` erweitern. `RunRecord` um `ElapsedMs`, `ProgressPercent`, `CancelledAtUtc`. `ApiRunResult.Error` → `OperationError?`.
- [x] **TASK-080**: **§15**: Middleware — Correlation-ID VOR Auth. Rate-Limiting mit `Retry-After`. SSE `completed_with_errors` Event. Health-Endpoint `version`-Feld. POST → `Location`-Header.
- [x] **TASK-081**: **§17**: Fehlende Seams — `IFileReader` für Set-Parser, `ITimeProvider` für Orchestrator, `IRunOptionsFactory`.
- [x] **TASK-082**: **§17**: Core Determinism Snapshot Suite — GameKey/Region/Winner/Score/Classification als JSON-Snapshots.
- [x] **TASK-083**: **§17**: Cross-Output Parity Tests + RunResult-Snapshot-Parität + RunProjection-Konsistenz-Tests.
- [x] **TASK-084**: **§17**: Shared Test-Doubles — `InMemoryFileSystem`, `ConfigurableConverter`, `StubToolRunner`, `StubDialogService`, `TrackingAuditStore`, `ScenarioBuilder`.
- [x] **TASK-085**: **§17**: Altcode bereinigen — `V1TestGapTests.cs`, `V2RemainingTests.cs`, `CoverageBoostPhase1-9Tests.cs` prüfen und migrieren.
- [ ] **TASK-086**: Verify: `dotnet test src/RomCleanup.Tests/ --nologo` — 0 Failures. Orchestrator ≤ 250 LOC.

#### Audit-Integration Phase 5

- [x] **TASK-151**: **AUDIT P1-02**: `hasErrors`-Formel ignoriert DatRename/Sort-Fehler — `RunOrchestrator.cs` (L218-221) prüft nur Move/Junk/Convert-Fehler. DatRename- und Sort-Fehler erzeugen falschen ExitCode 0. Fix: `datRenameErrors > 0 || sortErrors > 0` in `hasErrors`-Berechnung aufnehmen.
- [x] **TASK-152**: **AUDIT P1-04**: CLI Move ohne Bestätigung — `Program.cs` CLI erlaubt `--mode Move` ohne interaktive Bestätigung. Im Gegensatz zu GUI (DangerConfirmDialog) und API (expliziter Endpoint). Fix: `Console.ReadLine()`-Prompt vor Move, oder `--yes` Flag für Automation.
- [x] **TASK-153**: **AUDIT P1-05**: CrossRootDeduplicator unvollständige Scoring-Kette — `CrossRootDeduplicator.cs` (L61-70) nutzt eigene vereinfachte `OrderByDescending`-Kette ohne finalen Path-Tiebreaker. Bei identischen Scores + Sizes ist Gewinner LINQ-Reihenfolge-abhängig (nicht deterministisch). Fix: `.ThenBy(x => x.MainPath, StringComparer.Ordinal)` oder an `DeduplicationEngine.SelectWinner` delegieren.
- [x] **TASK-154**: **AUDIT E9-04/E5**: RunOrchestrator fängt nur `OperationCanceledException` — IOException, NullReferenceException etc. propagieren ohne Sidecar-Write, ohne Cleanup, ohne Fehlerstatus. Partiell verschobene Dateien bleiben ohne Audit-Trail. Fix: `catch (Exception ex) when (ex is not OperationCanceledException)` mit Sidecar-Write und `RunOutcome.Failed`. Betrifft: `RunOrchestrator.cs` (L120-245).
- [x] **TASK-155**: **AUDIT D-02/E3**: InsightsEngine `.First()` ohne deterministische Sortierung — Grouping-Ergebnisse liefern bei identischen Bedingungen reihenfolgeabhängige Ergebnisse. Fix: `.OrderBy()` vor `.First()`. Betrifft: `InsightsEngine.cs`.
- [x] **TASK-156**: **AUDIT T-01/E5**: VersionScore overflow protection — Score-Berechnung castet intern potentiell über `long.MaxValue` bei extremen Versionsnummern (>6 Segmente). Fix: Clamp auf max 6 Segmente. Betrifft: `VersionScorer.cs`.
- [x] **TASK-157**: **AUDIT T-04/E5**: DatSourceService nicht-deterministisch — `Directory.EnumerateFiles()` ohne Sortierung bei Pattern-Match mit mehreren Treffern. Unterschiedliche Dateisysteme liefern unterschiedliche Reihenfolgen. Fix: `.Order().First()` bei Multi-Match. Betrifft: `DatSourceService.cs` (L311).
- [x] **TASK-158**: **AUDIT F-03/E2**: API ConvertFormat immer auf "auto" gezwungen — Unabhängig vom User-Input wird ConvertFormat in API überschrieben. Fix: ConvertFormat aus `RunRequest` durchreichen. Betrifft: `src/RomCleanup.Api/Program.cs`.
- [x] **TASK-159**: **AUDIT F-04/E2**: OnlyGames-Guard nur in API — CLI/GUI haben keine äquivalente Validierung bei `!OnlyGames && !KeepUnknownWhenOnlyGames`. Fix: Validierung in `RunOptionsBuilder.Validate()` zentralisieren. Betrifft: `RunOptionsBuilder.cs`.
- [x] **TASK-160**: **AUDIT F-08/E2**: `RunOptionsBuilder.Normalize()` normalisiert PreferRegions nicht — Kein Dedup, kein Case-Normalize, kein Empty-Filtering. Nur Roots und Extensions werden normalisiert. Fix: PreferRegions-Normalisierung ergänzen (Dedup, Trim, ToUpper, Empty-Filter). Betrifft: `RunOptionsBuilder.cs`.
- [x] **TASK-161**: **AUDIT F-09/E2**: API lädt User-Settings aus `%APPDATA%` — `RunEnvironmentBuilder.LoadSettings()` liest `settings.json` aus AppData. Bei Server-Deployment wird lokale Konfiguration eines anderen Users geladen. Fix: API-eigene Settings-Quelle oder expliziter Opt-in. Betrifft: `RunEnvironmentBuilder.cs`, `Program.cs` (API).
- [x] **TASK-162**: **AUDIT F-10/E2**: RunRecord fehlende Felder — `EnableDatAudit`/`EnableDatRename` nicht gesetzt in `RunLifecycleManager.TryCreateOrReuse()` (defaults to false). Fix: Felder aus RunOptions übernehmen. Betrifft: `RunLifecycleManager.cs`.
- [x] **TASK-163**: **AUDIT E7-03**: DryRun+Features silent ignore — `SortConsole=true`/`ConvertFormat!=null` + `DryRun=true` wird still ignoriert (Phase nicht in Plan aufgenommen). Benutzer/API-Client hat keine Möglichkeit zu erfahren, dass Feature übersprungen wurde. Fix: Warning in RunResult oder Validation-Fehler bei inkompatiblen Optionen. Betrifft: `PhasePlanning.cs`, `RunOptionsBuilder.cs`.
- [x] **TASK-164**: **AUDIT E9-07**: Dashboard zeigt Plan-Werte nach Cancel — `ApplyRunResult(force: true)` im Cancel-Pfad. ✅ 1 Test.
- [x] **TASK-165**: **AUDIT E9-08**: Report markiert alle Loser als MOVE — `BuildEntries(result, mode)` mit DryRun→DUPE. ✅ 3 Tests.
- [x] **TASK-166**: **AUDIT E9-09**: HealthScore ignoriert Run-Fehler — `int errors = 0` Optional-Parameter mit Cap 20 Penalty. ✅ 4 Tests.
- [x] **TASK-167**: **AUDIT T-05/E5**: HeaderRepair crash-unsafe — Temp-File-Pattern (`.tmp` → rename). ✅ 2 Tests.
- [x] **TASK-168**: **AUDIT E6 S-05**: Set-Member-Verwaiste — Bei Move eines Set-Winners ohne Mitglieder (CUE ohne BIN, M3U ohne Discs) verwaisen die Members. Fix: Set-Integrität vor Move prüfen, Members mitbewegen oder blockieren. Betrifft: `MovePipelinePhase.cs`, `ConsoleSorter.cs`.
- [x] **TASK-169**: Tests: RED-Tests für alle Phase 5 Audit-Tasks — Determinismus-Tests für CrossRoot/InsightsEngine/DatSourceService, hasErrors-Formel, CLI-Confirmation, Exception-Handling, Entry-Point-Parität.

**Abnahmekriterien Phase 5:**
- RomCandidate ist sealed record
- RunProjection ist Single Source of Truth für KPIs
- Orchestrator ≤ 250 LOC (exklusive extrahierte Phase-Steps)
- Shared DI-Registrierung für alle 3 Entry Points
- Determinism Snapshot Suite grün
- Alle Set-Parser haben IFileReader-Seam
- `hasErrors` berücksichtigt alle Phase-Fehler (inkl. DatRename/Sort)
- CrossRootDeduplicator hat deterministischen Tiebreaker
- CLI Move erfordert Bestätigung (oder `--yes`)
- Non-Cancel Exceptions werden gefangen und erzeugen Sidecar
- PreferRegions/ConvertFormat/OnlyGames identisch über CLI/API/GUI
- RunOptionsBuilder normalisiert PreferRegions

---

### Phase 6 — Benchmark & Quality Assurance

- GOAL-006: Extended Metrics M8–M16 implementieren. Quality Gates als hard-fail in CI. Dataset-Expansion auf Zielabdeckung. CI/CD Pipeline aufbauen.

**Abhängigkeit:** Phase 2 (SortDecision-Metriken), Phase 3 (Stub-Generatoren verfügbar).

- [x] **TASK-087**: **§3 P1**: ExtendedMetrics-Record mit M8–M16 in `MetricsAggregator`. `CalculateExtended()` Methode.
- [x] **TASK-088**: **§3 P1**: QualityGateTests mit harten Schwellenwerten (M4 ≤0.5%, M6 ≤5%, M7 ≤0.3%, M9a ≤0.1%).
- [x] **TASK-089**: **§3 P1**: Initialer Baseline-Snapshot `benchmark/baselines/baseline-latest.json` committen.
- [x] **TASK-090**: **§25**: Baseline `groundTruthVersion` Mismatch beheben (1.0.0 → 2.0.0). `ExtendedMetrics` → typisiertes Record.
- [x] **TASK-091**: **§25**: Quality Gates in CI auf `hardFail=true` umstellen (`ROMCLEANUP_ENFORCE_QUALITY_GATES=true`).
- [x] **TASK-092**: **§25**: 7-Ebenen-Verdikt-System — `GroundTruthComparator` um alle Verdict-Ebenen erweitern (Container → System → Kategorie → Identität → DAT → Sorting → Repair).
- [x] **TASK-093**: **§3 P2**: Dataset-Expansion: Arcade ≥160, Computer ≥120, BIOS ≥35, Multi-File ≥20, PS-Disambig ≥20.
- [x] **TASK-094**: **§4**: `performance-scale.jsonl` füllen (ScaleDatasetGenerator, ≥5.000 Einträge). `PerformanceBenchmarkTests` (Throughput ≥100/s).
- [x] **TASK-095**: **§4**: Test-DATs für Benchmark (mini DAT-Dateien). `dat-coverage.jsonl` + `DatCoverageBenchmarkTests`. `repair-safety.jsonl` auf ≥30.
- [x] **TASK-096**: **§5 E1**: golden-core Expansion — Cartridge +60, Disc +25, BIOS-Matrix B-01 bis B-12, PS1↔PS2↔PSP Disambiguation +30.
- [x] **TASK-097**: **§5 E2**: Arcade +120, Multi-File +40, Computer +50, CHD-RAW-SHA1 +15. Neue Stub-Generatoren (Arcade-ZIP, CHD-v4/v5, Computer-Stubs).
- [x] **TASK-098**: **§5 E3**: golden-realworld +150, chaos-mixed +50, edge-cases +50, negative-controls +30.
- [x] **TASK-099**: **§5 E4**: repair-safety +20, dat-coverage ≥100 inkl. TOSEC ≥10. Manifest + S1-Gate + Baseline-Snapshot.
- [x] **TASK-100**: **§6**: Plattformfamilien-Gates befüllen (Cartridge ≥320, Disc ≥260, Arcade ≥160, Computer ≥120, Hybrid ≥60).
- [x] **TASK-101**: **§6**: Fallklassen-Gates FC-01 bis FC-20. BIOS/Arcade/Redump/Computer-Matrizen verteilen. `S1_MinimumViableBenchmark_AllGatesMet()` grün.
- [x] **TASK-102**: **§3 P3**: HTML Benchmark Report mit inline CSS + HTML-Escaping + XSS-Test. CSV-Export mit CSV-Injection-Schutz.
- [x] **TASK-103**: **§3 P3**: TrendAnalyzer (N-Run-History, Improving/Stable/Degrading).
- [ ] **TASK-104**: **§7**: GitHub Actions CI-Workflow `benchmark-gate.yml` (PR-Gate). Nightly-Schedule (Benchmark + HTML-Artifact).
- [x] **TASK-105**: **§3 P4**: Anti-Gaming-Gates (M15 ≤2%, M16 ≤0.15). Per-Sample Baseline-Vergleich. Repair-Gate Feature-Flag (M13 ≥90%).
- [x] **TASK-106**: **§19**: Holdout-Zone implementieren (~200 Entries, nicht im Repo, nur CI-zugänglich). Chaos-Quote ≥30 %. Overfitting-Detection (Eval-Verbesserung >3 % ∧ Holdout <0,5 % → Warning).
- [x] **TASK-107**: **§19**: Stub-Realismus L2/L3 — `StubGeneratorDispatch` um `RealismLevel`-Parameter erweitern. L2 = Header + Padding + korrekte Größe. L3 = Adversarial.
- [x] **TASK-108**: **§19**: Ground-Truth-Schema-Erweiterung — `schemaVersion`, `expected.gameIdentity`, `expected.discNumber`, `expected.repairSafe`, `addedInVersion`, `lastVerified`.
- [x] **TASK-109**: **§26**: Jährlichen Audit-Prozess + ereignisgesteuerte Trigger formalisieren. GT-Änderungen nur per PR + Review. Baselines archivieren. → `docs/guides/BENCHMARK_AUDIT_GOVERNANCE.md` erstellt.
- [x] **TASK-110**: **§26**: CI-Gate `Journey Matrix Gate` (11 Gate-Testklassen). `holdout-blind` Dataset-Klasse. → 11 Tests in `JourneyMatrixGateTests.cs`, alle grün. Holdout-Overlap-Check inkl.
- [x] **TASK-111**: **§25**: Mutation-Testing Status klären — Stryker.NET evaluieren, ggf. in CI-Pipeline integrieren. → ADR-0018 erstellt. Entscheidung: Reporting-only, kein Gate. MutationKillTests.cs + vierteljährlicher manueller Run.
- [x] **TASK-112**: Verify: Alle Coverage-Gates grün. Benchmark HTML-Report generierbar. CI-Pipeline triggert auf PR.

#### Audit-Integration Phase 6

- [x] **TASK-170**: **AUDIT E9-13**: ProgressEstimator 0% Testabdeckung — 10 Tests (null, empty, whitespace, all phases, case-insensitive, monotonic, range). ✅
- [x] **TASK-171**: **AUDIT E9-11**: PhasePlanning 0% Testabdeckung — 12 Tests (alle Optionskombinationen, Ordering-Invarianten, Determinismus, null-Actions). ✅
- [x] **TASK-172**: **AUDIT E9-12**: `ConsoleSorter.MoveSetAtomically` 0 Tests — 8 Tests (atomic CUE+BIN move, DryRun, standalone, skip, UNKNOWN, excluded, Review routing). ✅

**Abnahmekriterien Phase 6:**
- M8–M16 Metriken implementiert und im Report
- Quality Gates hard-fail bei Schwellenwert-Verletzung
- Dataset: ≥2.500 Entries, Arcade ≥160, Computer ≥120, BIOS ≥35
- performance-scale ≥5.000 Einträge, Throughput ≥100/s
- CI/CD Pipeline aktiv (PR-Gate + Nightly)
- Holdout-Zone eingerichtet
- Baseline-Snapshot committed
- ProgressEstimator, PhasePlanBuilder, MoveSetAtomically getestet

---

### Phase 7 — GUI/UX Overhaul

- GOAL-007: GUI Redesign umsetzen (was noch fehlt), Config/Filtering Redesign implementieren, Conversion UX aufbauen. Viele ViewModels und Views existieren bereits — Fokus auf fehlende Funktionalität.

**Abhängigkeit:** Phase 2 (SortDecision → Review-Bucket in UI), Phase 4 (ConversionPlan → Preview-Tab), Phase 5 (RunProjection als Data Source).

- [x] **TASK-113**: **§8**: Shell & Navigation — Responsive Breakpoints: NavRail-Labels kollabieren bei <960px (`IsCompactNav` in ShellViewModel, `InverseBoolToVis` auf 6 Labels). Phase-Indicator: 5-Step Pipeline-Dots mit sichtbaren Text-Labels (Config/Preview/Review/Execute/Report). ContextWing collapse bei <1200px existierte bereits. 4 Tests.
- [x] **TASK-114**: **§8**: View-Konsolidierung verifiziert — LibrarySafetyView (3-Spalten), MissionControl (StartView), ConfigView (5 Sub-Tabs), alle Nav-Bereiche routen korrekt. Vollständig.
- [x] **TASK-115**: **§8**: Smart Action Bar — RunState-basiert: Run-Button via `IsIdle`, Cancel via `IsBusy`, ProgressPanel via `IsBusy`, ConvertOnly via `IsIdle`. `RunStateDisplayText` lokalisiert (5 State-Keys + Phase-Keys). `x:Name` auf CancelButton/ProgressPanel. 52 Tests (11 RunStateDisplay + 1 PropertyChanged + 5 XAML + 22 IsIdle/HasRunResult + 13 vorhandene).
- [x] **TASK-116**: **§8**: Move/Execute Gate vollständig — 2-Step Inline-Confirm, Danger-State (BrushDangerBg), Move nur nach CompletedDryRun, ShowConfigChangedBanner blockiert bei geänderter Config.
- [x] **TASK-117**: **§9**: Region Priority Ranker — `MoveRegionTo(fromIdx, toIdx)` für D&D-Reordering. ListBox mit RegionPriorities-Binding, AllowDrop, ItemTemplate (Drag-Handle + Position + Code + Up/Down-Buttons). SortView.xaml.cs: D&D Event-Handler (OnRegionDragStart/DragOver/Drop). 7 Tests.
- [x] **TASK-118**: **§9**: Konsolen Smart-Picker vollständig — Search + Chips + Akkordeon-Gruppen + Presets (Top10/Disc/Handhelds/Retro) + Counter + RemoveSelection.
- [x] **TASK-119**: **§9**: Dateityp-Filter — `SelectedExtensionCount`, `ExtensionCountDisplay` ("sel / total"), 4 Commands (SelectAll/Clear/SelectGroup/DeselectGroup). PropertyChanged-Subscription auf ExtensionFilterItems. 7 Tests.
- [x] **TASK-120**: **§9**: ViewModel-Commands vollständig — MoveRegionUp/Down, ToggleRegion, 4 RegionPresets, SelectAll/Clear/Group/DeselectGroup-Console, 4 ConsolePresets, RemoveConsoleSelection.
- [x] **TASK-121**: **§13**: Projection-Objekte vervollständigt — 3 neue Records: `ProgressProjection` (Progress/RunState/BusyHint), `StatusProjection` (Roots/Tools/Dat/Ready + 5 Tool-Texte), `BannerProjection` (DryRun/MoveComplete/ConfigChanged), `MoveGateProjection` (ShowStartMove/HasRunResult/CanRollback/ReportPath). Alle mit `.Idle`/`.None`/`.Empty` Factory-Defaults. 9 Tests.
- [x] **TASK-122**: **§13**: RunState einmalig in RunViewModel. `IsBusy`/`IsIdle` abgeleitet. MainViewModel.RunPipeline.cs entkernen (~400 LOC → Projections).
- [x] **TASK-123**: **§13**: Settings-Duplikation auflösen (MainVM.Settings.cs → SetupViewModel als Single Source). Bidirektionale Sync via Reflection + `_syncingSettings` Guard. 2 Tests.
- [x] **TASK-124**: **§13**: Inline-Strings → `ILocalizationService`. 100+ Hardcoded-Strings in RunPipeline.cs + Settings.cs durch `_loc["Key"]` / `_loc.Format()` ersetzt. ~130 i18n-Keys in de/en/fr.json. 41 Tests.
- [x] **TASK-125**: **§23 UX**: ConversionPreviewViewModel erstellt — Items (ObservableCollection), HasItems, SummaryText, Load/Clear. ConversionPreviewItem Record. In MainViewModel.ConversionPreview gewired. 2 Tests.
- [x] **TASK-126**: Tests: GuiViewModelTests erweitert — 36 neue Tests: SortDecision Enum/Architektur (3), Smart Action Bar States (10 inkl. IsMovePhase/IsConvertPhase/TransitionMatrix), Region-Ranker (14 inkl. MoveUp/Down/Toggle/Presets/Init/Count), Console-Picker (9 inkl. SelectAll/Clear/Group/Presets/Filter/Remove).
- [x] **TASK-127**: MVVM-Compliance: ToolsConversionView Code-Behind von Infrastructure-Import + LoadRegistry befreit → ToolsViewModel.LoadConversionRegistry(). StartView.OnHeroDrop → MainViewModel.AddDroppedFolders(). 2 Tests.

#### Audit-Integration Phase 7

- [x] **TASK-173**: **AUDIT P1-07**: Stille Settings-Reset bei korrupter `settings.json` — `LoadFromSafe()` erkennt korruptes JSON, erstellt `.bak`-Backup, liefert `SettingsLoadResult` mit `WasCorrupt`/`CorruptionMessage`. 7 Tests.
- [x] **TASK-174**: **AUDIT UJ-05/E8**: UNKNOWN-Dateien ohne Erklärung — HTML-Report: Tooltip auf UNKNOWN-Zellen + Info-Banner mit Anzahl und Handlungsempfehlung. 4 Tests.
- [x] **TASK-175**: **AUDIT UJ-09/E8**: Trash-Löschung = Datenverlust — `RollbackService.VerifyTrashIntegrity()` Pre-flight-Check (DryRun=true). 2 Tests.
- [x] **TASK-176**: **AUDIT E8 UJ-04**: Config-Änderung nach DryRun ohne Banner — `ShowConfigChangedBanner`-Property + XAML-Banner in SmartActionBar + i18n (de/en/fr). 3 Tests.

**Abnahmekriterien Phase 7:**
- Region Priority Ranker funktional mit Drag & Drop
- Smart-Picker filtert live über Key/DisplayName/Aliases
- Smart Action Bar zeigt korrekte States
- Move/Execute nur nach Review sichtbar
- Conversion Preview-Tab zeigt Pläne
- Keine Businesslogik in Code-Behind
- Korrupte Settings erzeugen Warning + Backup
- UNKNOWN-Dateien haben Tooltip + Erklärung
- Trash-Integrität wird vor Rollback geprüft
- Config-Änderung nach DryRun zeigt sichtbare Warnung

---

### Phase 8 — Polish, Accessibility & Out-of-Scope Epics

- GOAL-008: Theme-System fertigstellen (6 Themes verifizieren), Accessibility sicherstellen, verbleibende Out-of-Scope Epics aus §1/§2 bei Bedarf starten.

**Abhängigkeit:** Phase 7 (UI muss stehen für Theme/A11Y-Tests).

- [x] **TASK-128**: **§10**: Theme-System verifizieren — 6 Themes existieren (SynthwaveDark, Light, HighContrast, CleanDarkPro, RetroCRT, ArcadeNeon) + `_DesignTokens.xaml` + `_ControlTemplates.xaml`. DynamicResource-Migration prüfen.
- [x] **TASK-129**: **§10**: Theme-Switcher-Dropdown mit Farbvorschau. `Ctrl+T` Theme-Cycling. Theme-Scheduling (dark after 18:00) + Windows-Theme-Sync.
- [x] **TASK-130**: **§11**: Keyboard-Navigation — Tab-Reihenfolge, Fokus-Indikatoren (3px Neon-Border), Enter/Space für Checkboxen, Escape schließt Dropdowns.
- [x] **TASK-131**: **§11**: Screen Reader — AutomationProperties für alle Controls. LiveRegions für Counter. Pipeline-Stepper-Ellipsen annotiert.
- [x] **TASK-132**: **§11**: WCAG AA Kontrast (4.5:1) für alle 6 Themes. AAA für High Contrast. Mindest-Touch-Target 44×44px. Keine Information nur über Farbe.
- [x] **TASK-133**: **§11**: Narrator DryRun-Testplan komplett durchspielen. WebView2-Fallback. Kein Focus-Trap.
- [ ] **TASK-134**: **§1 EPIC-01**: PS2 SYSTEM.CNF-Analyse (optionale robuste CD/DVD-Erkennung).
- [ ] **TASK-135**: **§1 EPIC-02**: RVZ-Verify via dolphintool dry-convert.
- [ ] **TASK-136**: **§1 EPIC-03**: Parallele Conversion (Thread-Pool für Batch-Conversion).
- [ ] **TASK-137**: **§1 EPIC-04**: MDF/MDS/NRG-Support.
- [ ] **TASK-138**: **§1 EPIC-05**: Conversion-Preview-UI (dediziertes Tab in WPF). Siehe auch TASK-125.
- [ ] **TASK-139**: **§1 EPIC-06**: ciso-Tool-Integration (CSO→ISO Decompression). Blockt CSO→ISO→CHD-Kette.
- [ ] **TASK-140**: **§2 Epic B**: Cross-Root Matching & Repair (#46).
- [ ] **TASK-141**: **§2 Epic C**: Archive Rebuild & Restructuring (#47).
- [ ] **TASK-142**: **§19**: C# Plugin-System (Backlog — PowerShell-Plugins nicht übertragbar, Neuimplementierung).
- [x] **TASK-143**: Verify: Narrator-Testplan bestanden. Alle Themes WCAG-konform. Plugin-System evaluiert.

**Abnahmekriterien Phase 8:**
- Alle 6 Themes WCAG AA konform
- Narrator-Testplan ohne Findings
- Keyboard-Navigation vollständig
- Out-of-Scope Epics als eigenständige Features evaluiert/gestartet

---

## 3. Alternatives

- **ALT-001**: Monolithischer Release statt Phasen-Ansatz. Verworfen wegen hohem Risiko — einzelne Phase kann isoliert getestet und gemerged werden, monolithischer Ansatz nicht.
- **ALT-002**: GUI-First statt Safety-First. Verworfen — Sicherheitslücken sind Release-Blocker und müssen vor jeder Feature-Arbeit geschlossen werden.
- **ALT-003**: Benchmark-Expansion vor Recognition-Quality. Verworfen — bessere Recognition braucht zuerst korrekte Metriken-Grundlage (Category + SortDecision).
- **ALT-004**: Vollständiges Orchestrator-Rewrite statt schrittweisem Ausdünnen. Verworfen — 4-Wellen-Migration ist sicherer und behält Feature-Parität.

---

## 4. Dependencies

- **DEP-001**: .NET 10 SDK (net10.0 / net10.0-windows) — bereits vorhanden
- **DEP-002**: `chdman` (MAME), `dolphintool`, `7z`, `psxtract` — externe Tools, bereits integriert
- **DEP-003**: `ciso` oder `maxcso` — **nicht integriert**, benötigt für §22 L4 (CSO→ISO Decompression)
- **DEP-004**: `data/consoles.json` — muss um `ConversionPolicy`-Feld erweitert werden (TASK-047)
- **DEP-005**: `data/conversion-registry.json` — muss um Compression-Ratios erweitert werden (TASK-055)
- **DEP-006**: GitHub Actions Runner — benötigt für CI/CD (Phase 6)
- **DEP-007**: Stryker.NET — optional für Mutation-Testing (TASK-111)
- **DEP-008**: Ground-Truth JSONL-Dateien in `src/RomCleanup.Tests/Benchmark/` — müssen für Phase 3/6 erweitert werden

---

## 5. Files

Hauptsächlich betroffene Dateien nach Phase (nicht exhaustiv):

### Phase 1 — Security
- **FILE-001**: `src/RomCleanup.Infrastructure/Safety/SafetyValidator.cs` — Path-Normalisierung, ADS, Extended-Prefix
- **FILE-002**: `src/RomCleanup.Infrastructure/FileSystem/FileSystemAdapter.cs` — MoveItemSafely, Root-Containment
- **FILE-003**: `src/RomCleanup.Infrastructure/Audit/AuditSigningService.cs` — Rollback-Guards
- **FILE-004**: `src/RomCleanup.Infrastructure/Conversion/FormatConverterAdapter.cs` — ARCADE-Bug, Multi-File, ZipBomb

### Phase 2 — Recognition
- **FILE-005**: `src/RomCleanup.Core/Classification/FileClassifier.cs` — Extension-Aware Overload
- **FILE-006**: `src/RomCleanup.Core/Classification/ConsoleDetector.cs` — IsLikelyJunkName entfernen
- **FILE-007**: `src/RomCleanup.Core/Classification/HypothesisResolver.cs` — Evidence-Caps, SortDecision
- **FILE-008**: `src/RomCleanup.Core/Classification/DetectionHypothesis.cs` — IsHardEvidence
- **FILE-009**: `src/RomCleanup.Contracts/Models/SortingModels.cs` — SortDecision Enum

### Phase 3 — Detection Recall
- **FILE-010**: `src/RomCleanup.Core/Classification/DiscHeaderDetector.cs` — PS3-Detection
- **FILE-011**: `src/RomCleanup.Tests/Benchmark/Generators/StubGeneratorDispatch.cs` — Fallback-Logik
- **FILE-012**: `src/RomCleanup.Tests/Benchmark/Generators/Disc/` — neue Stub-Generatoren

### Phase 4 — Conversion
- **FILE-013**: `data/consoles.json` — ConversionPolicy, ConversionTarget für 65 Systeme
- **FILE-014**: `src/RomCleanup.Core/Conversion/SourceIntegrityClassifier.cs` — NKit als Lossy
- **FILE-015**: `src/RomCleanup.Core/Conversion/ConversionPlanner.cs` — CreatePlan() Preview
- **FILE-016**: `src/RomCleanup.Infrastructure/Conversion/ConversionRegistryLoader.cs` — Policy-Parsing
- **FILE-017**: `src/RomCleanup.Infrastructure/Conversion/ToolInvokers/DolphinToolInvoker.cs` — RVZ-Verify

### Phase 5 — Core Architecture
- **FILE-018**: `src/RomCleanup.Contracts/Models/RomCandidate.cs` — sealed record
- **FILE-019**: `src/RomCleanup.Infrastructure/Orchestration/RunOrchestrator.cs` — Ausdünnung
- **FILE-020**: `src/RomCleanup.CLI/Program.cs` — Factory-Pattern
- **FILE-021**: `src/RomCleanup.Api/Program.cs` — Middleware-Korrekturen

### Phase 6 — Benchmark
- **FILE-022**: `src/RomCleanup.Tests/Benchmark/MetricsAggregator.cs` — M8–M16
- **FILE-023**: `src/RomCleanup.Tests/Benchmark/BaselineComparator.cs` — Per-Sample
- **FILE-024**: `src/RomCleanup.Tests/Benchmark/Infrastructure/` — CoverageValidator, SchemaValidator
- **FILE-025**: `.github/workflows/benchmark-gate.yml` — CI Pipeline (neu)

### Phase 7 — GUI
- **FILE-026**: `src/RomCleanup.UI.Wpf/Views/ConfigRegionsView.xaml` — Region-Ranker
- **FILE-027**: `src/RomCleanup.UI.Wpf/ViewModels/ConfigViewModel.cs` — Region/Console Commands
- **FILE-028**: `src/RomCleanup.UI.Wpf/Views/` — Smart Action Bar, Conversion Preview
- **FILE-029**: `src/RomCleanup.UI.Wpf/ViewModels/MainViewModel.cs` — Entkernung

### Phase 8 — Polish
- **FILE-030**: `src/RomCleanup.UI.Wpf/Themes/*.xaml` — WCAG-Konformität
- **FILE-031**: `src/RomCleanup.UI.Wpf/Views/*.xaml` — AutomationProperties, Tab-Reihenfolge

### Audit-Integration — Zusätzlich betroffene Dateien
- **FILE-032**: `src/RomCleanup.Infrastructure/Orchestration/RunOrchestrator.cs` — hasErrors-Formel (TASK-151), Non-Cancel-Exception (TASK-154)
- **FILE-033**: `src/RomCleanup.Infrastructure/Orchestration/RunOrchestrator.PreviewAndPipelineHelpers.cs` — Sidecar-Status (TASK-145)
- **FILE-034**: `src/RomCleanup.Infrastructure/Deduplication/CrossRootDeduplicator.cs` — Tiebreaker (TASK-153)
- **FILE-035**: `src/RomCleanup.Infrastructure/Conversion/FormatConverterAdapter.cs` — CUE-Sort (TASK-149)
- **FILE-036**: `src/RomCleanup.Infrastructure/Hashing/ArchiveHashService.cs` — 7z-Hash-Order (TASK-150)
- **FILE-037**: `src/RomCleanup.Infrastructure/Hashing/HeaderRepairService.cs` — Crash-Safety (TASK-167)
- **FILE-038**: `src/RomCleanup.Infrastructure/Dat/DatSourceService.cs` — Multi-Match-Determinismus (TASK-157)
- **FILE-039**: `src/RomCleanup.Infrastructure/Orchestration/PhasePlanning.cs` — DryRun+Feature-Warning (TASK-163), Tests (TASK-171)
- **FILE-040**: `src/RomCleanup.Infrastructure/Sorting/ConsoleSorter.cs` — MoveSetAtomically (TASK-172)
- **FILE-041**: `src/RomCleanup.CLI/Program.cs` — Move-Confirmation (TASK-152)
- **FILE-042**: `src/RomCleanup.Api/Program.cs` — ConvertFormat-Override (TASK-158), User-Settings (TASK-161)
- **FILE-043**: `src/RomCleanup.Infrastructure/Orchestration/RunLifecycleManager.cs` — RunRecord-Felder (TASK-162)
- **FILE-044**: `src/RomCleanup.Core/Scoring/VersionScorer.cs` — long→int Truncation (TASK-156)
- **FILE-045**: `src/RomCleanup.UI.Wpf/Services/SettingsLoader.cs` — Silent-Reset (TASK-173)
- **FILE-046**: `src/RomCleanup.UI.Wpf/Models/DashboardProjection.cs` — Plan-vs-Ist nach Cancel (TASK-164)
- **FILE-047**: `src/RomCleanup.Infrastructure/Reporting/RunReportWriter.cs` — MOVE-Markierung (TASK-165)
- **FILE-048**: `src/RomCleanup.Infrastructure/Reporting/HealthScorer.cs` — Fehler-ignoriert (TASK-166)

---

## 6. Testing

### Pro Phase — Pflicht-Tests

- **TEST-001**: Phase 1 — Security RED-Tests: Path-Traversal, ADS, Reparse-Point, ZipBomb, DTD (≥10 neue Tests in `SafetyIoSecurityRedPhaseTests.cs`)
- **TEST-002**: Phase 1 — ARCADE/NEOGEO Conversion Regression Test
- **TEST-003**: Phase 2 — FileClassifier Extension-Aware Tests (`.txt`, `.jpg`, `.exe` → NonGame)
- **TEST-004**: Phase 2 — HypothesisResolver Evidence-Cap Tests (Soft-Only ≤65, Single-Source-Caps)
- **TEST-005**: Phase 2 — 4-Gate-Modell Tests (Category/Confidence/Evidence/Conflict Gates)
- **TEST-006**: Phase 2 — CategoryRecognitionRate Benchmark Gate (≥85 %)
- **TEST-007**: Phase 2 — UnsafeSortRate Benchmark Gate (≤3 %)
- **TEST-008**: Phase 3 — PS3 PVD Detection Tests
- **TEST-009**: Phase 3 — Stub-Generator Tests (OperaFs, BootSector, Xdvdfs, Ps3Pvd)
- **TEST-010**: Phase 4 — ConversionPolicy für alle 65 Systeme (Laden + korrekte Werte)
- **TEST-011**: Phase 4 — 10 Conversion-Invarianten-Tests (R-01 bis R-10)
- **TEST-012**: Phase 4 — Lossy→Lossy Blocking, PBP-Encryption, M3U-Atomicity
- **TEST-013**: Phase 5 — Core Determinism Snapshot Suite (GameKey, Region, Winner, Score)
- **TEST-014**: Phase 5 — Cross-Output Parity (CLI ↔ API ↔ GUI RunOptions-Parität)
- **TEST-015**: Phase 5 — RunProjection KPI-Additivität (Keep + Dupes + Junk + Unknown + Filtered == Total)
- **TEST-016**: Phase 6 — QualityGateTests M4/M6/M7/M9a hard-fail
- **TEST-017**: Phase 6 — CoverageGateTests (alle Plattformfamilien, Fallklassen, Spezialbereich-Gates)
- **TEST-018**: Phase 6 — PerformanceBenchmarkTests (≥100 Samples/s)
- **TEST-019**: Phase 7 — GuiViewModelTests: SortDecision-States, Smart Action Bar, Region-Ranker
- **TEST-020**: Phase 8 — Narrator DryRun-Testplan, WCAG-Kontrast-Verifikation

### Invarianten (über alle Phasen)

- **TEST-INV-001**: Kein Move außerhalb erlaubter Roots
- **TEST-INV-002**: Keine leeren/inkonsistenten GameKeys
- **TEST-INV-003**: Winner-Selection deterministisch
- **TEST-INV-004**: Preview ↔ Execute ↔ Report konsistent
- **TEST-INV-005**: `Converted + Errors + Skipped == Attempted`
- **TEST-INV-006**: `Keep + Dupes + Junk + Unknown + FilteredNonGame == Total`

### Audit-Integration — Zusätzliche Pflicht-Tests

- **TEST-021**: Phase 1 — PreferRegions-Parität: CLI, API, GUI erhalten identische Default-Reihenfolge (TASK-144)
- **TEST-022**: Phase 1 — Sidecar-Status bei Fehlern: `completed_with_errors` statt `completed` (TASK-145)
- **TEST-023**: Phase 1 — Move-Audit-Atomizität: Bei Audit-Failure kein verwaister Move (TASK-147)
- **TEST-024**: Phase 4 — CUE-Selektion deterministisch: `.Order().First()` bei mehreren CUE-Dateien (TASK-149)
- **TEST-025**: Phase 4 — 7z-Hash deterministisch: Gleicher Inhalt erzeugt gleichen Hash (TASK-150)
- **TEST-026**: Phase 5 — hasErrors-Formel: DatRename-Fehler setzt `hasErrors=true` (TASK-151)
- **TEST-027**: Phase 5 — CLI Move Confirmation: Interactive-Prompt vor Move (TASK-152)
- **TEST-028**: Phase 5 — CrossRoot Tiebreaker: Identische Scores → deterministischer Winner via Path (TASK-153)
- **TEST-029**: Phase 5 — Non-Cancel Exception: IOException → Sidecar geschrieben + `RunOutcome.Failed` (TASK-154)
- **TEST-030**: Phase 5 — InsightsEngine `.First()`: Deterministisch bei gleichen Inputs (TASK-155)
- **TEST-031**: Phase 5 — VersionScore Overflow: Extreme Werte führen nicht zu int-Overflow (TASK-156)
- **TEST-032**: Phase 5 — DatSourceService Multi-Match: Mehrere DAT-Dateien → alphabetische Auswahl (TASK-157)
- **TEST-033**: Phase 5 — API ConvertFormat Passthrough: User-Wert nicht überschrieben (TASK-158)
- **TEST-034**: Phase 5 — RunOptionsBuilder.Normalize: PreferRegions werden dedupliziert und normalisiert (TASK-160)
- **TEST-035**: Phase 5 — DryRun+Features Warning: `SortConsole=true + DryRun=true` → Warning in Result (TASK-163)
- **TEST-036**: Phase 5 — Report Action: DryRun-Loser nicht als MOVE markiert (TASK-165)
- **TEST-037**: Phase 6 — PhasePlanBuilder: Phase-Reihenfolge deterministisch, alle Option-Kombinationen (TASK-171)
- **TEST-038**: Phase 6 — MoveSetAtomically: Set-Move mit Sidecars via TempDir (TASK-172)
- **TEST-039**: Phase 7 — Settings-Corruption: Backup + Warning bei korruptem JSON (TASK-173)
- **TEST-040**: Phase 7 — Trash-Integrität: Rollback-Warning wenn Trash-Dateien fehlen (TASK-175)

---

## 7. Risks & Assumptions

### Risiken

- **RISK-001**: **RomCandidate sealed record** (TASK-067) — Breaking Change mit breitem Impact über alle Schichten. Mitigierung: Schrittweise Migration mit Compile-Checks, Feature-Branch, umfangreiche Tests vor Merge.
- **RISK-002**: **4-Gate-Modell** (TASK-028) — Fundamentale Änderung der Sorting-Logik. Kann zu unerwarteten Regressions führen. Mitigierung: Benchmark vor/nach Vergleich, Shadow-Mode (altes + neues Ergebnis loggen) vor Cut-over.
- **RISK-003**: **Orchestrator-Ausdünnung** (TASK-075) — 380→250 LOC Refactoring bei laufendem Betrieb. Mitigierung: 4-Wellen-Ansatz, jede Welle eigenständig testbar.
- **RISK-004**: **ciso/maxcso-Integration** (DEP-003) — Externes Tool, nicht unter Kontrolle. Mitigierung: Tool optional, CSO→CHD-Pfad als ManualOnly wenn kein ciso verfügbar.
- **RISK-005**: **GUI Redesign Scope Creep** — Phase 7 hat ~55 Items. Mitigierung: Strikte Priorisierung auf funktionale Lücken, kosmetische Punkte nachrangig.
- **RISK-006**: **Theme WCAG-Compliance** — Retro CRT und Arcade Neon Themes könnten inherent schwache Kontraste haben. Mitigierung: Kontrast-Minima per Design-Token erzwingen.

### Annahmen

- **ASSUMPTION-001**: Bestehende 5.400+ Tests sind grün und bleiben Baseline für Regression.
- **ASSUMPTION-002**: Die in der Codebase bereits existierenden ViewModels (ShellViewModel, MissionControlViewModel etc.) und Themes (6 XAML-Dateien) sind funktional — nur Lücken werden geschlossen.
- **ASSUMPTION-003**: `data/consoles.json` kann um neue Felder erweitert werden ohne bestehende Loader zu brechen (Schema-Evolution).
- **ASSUMPTION-004**: CI-Runner hat Zugang zu GitHub Actions und kann `dotnet test` ausführen.
- **ASSUMPTION-005**: Phase 1–4 sind release-kritisch und müssen vor einem S1-Release abgeschlossen sein. Phase 5–8 können post-S1 erfolgen.

---

## 8. Audit Findings Cross-Reference

Konsolidierte Zuordnung aller 52 Findings aus dem 10-Etappen-Audit (E1–E9 + Gesamt-Audit) zu Roadmap-Phasen und Tasks.

### Legende

| Kürzel | Bedeutung |
|--------|-----------|
| E1–E9 | Audit-Etappe 1–9 |
| P1/P2/P3 | Priorität (P1 = Release-Blocker) |
| ✅ Existing | Durch bestehenden TASK abgedeckt |
| 🆕 New | Neuer TASK aus Audit hinzugefügt |

### P1 — Release-Blocker (7 Findings)

| # | Finding | Etappe | Phase | Task | Status |
|---|---------|--------|-------|------|--------|
| P1-01 | PreferRegions-Divergenz: `defaults.json` JP→WORLD vs API/GUI WORLD→JP | E2 (F-02) | 1 | TASK-144 | 🆕 |
| P1-02 | `hasErrors`-Formel ignoriert DatRename/Sort-Fehler → ExitCode 0 | E9 | 5 | TASK-151 | 🆕 |
| P1-03 | Sidecar Status "completed" trotz Fehlern | E9 | 1 | TASK-145 | 🆕 |
| P1-04 | CLI Move ohne interaktive Bestätigung | E8 (UJ-02) | 5 | TASK-152 | 🆕 |
| P1-05 | CrossRootDeduplicator unvollständige Scoring-Kette + kein Path-Tiebreaker | E4/E9 | 5 | TASK-153 | 🆕 |
| P1-06 | CUE-Selektion nicht-deterministisch (`Directory.GetFiles()[0]`) | E9 (E9-01) | 4 | TASK-149 | 🆕 |
| P1-07 | Stille Settings-Reset bei korrupter `settings.json` | E8 (UJ-03) | 7 | TASK-173 | 🆕 |

### P2 — Hohe Risiken (21 Findings)

| # | Finding | Etappe | Phase | Task | Status |
|---|---------|--------|-------|------|--------|
| P2-01 | CLI ZERO Path-Security-Validierung | E2 (F-01) | 1 | TASK-001..005 | ✅ Existing |
| P2-02 | API ConvertFormat immer auf "auto" gezwungen | E2 (F-03) | 5 | TASK-158 | 🆕 |
| P2-03 | OnlyGames-Guard nur in API | E2 (F-04) | 5 | TASK-159 | 🆕 |
| P2-04 | CLI ZERO Rollback/Recovery | E2 (F-05) | 5 | TASK-078 | ✅ Existing |
| P2-05 | RunOptionsBuilder.Normalize ignoriert PreferRegions | E2 (F-08) | 5 | TASK-160 | 🆕 |
| P2-06 | API lädt User-Settings aus %APPDATA% | E2 (F-09) | 5 | TASK-161 | 🆕 |
| P2-07 | RunRecord fehlende Felder (EnableDatAudit etc.) | E2 (F-10) | 5 | TASK-162 | 🆕 |
| P2-08 | InsightsEngine `.First()` ohne Sortierung | E3 (D-02) | 5 | TASK-155 | 🆕 |
| P2-09 | GameKey ignoriert consoleType | E4 (W-01) | 5 | TASK-068 | ✅ Existing |
| P2-10 | VersionScore `long→int` Truncation | E5 (T-01) | 5 | TASK-156 | 🆕 |
| P2-11 | Move-then-Audit nicht atomar | E5 (T-02) | 1 | TASK-147 | 🆕 |
| P2-12 | DatSourceService nicht-deterministisch | E5 (T-04) | 5 | TASK-157 | 🆕 |
| P2-13 | HeaderRepair crash-unsafe | E5 (T-05) | 5 | TASK-167 | 🆕 |
| P2-14 | HMAC-Key-Verlust bei App-Crash | E6 (S-04) | 1 | TASK-146 | 🆕 |
| P2-15 | Set-Member-Verwaiste bei Move | E6 (S-05) | 5 | TASK-168 | 🆕 |
| P2-16 | DryRun+Features silent ignore | E7 (E7-03) | 5 | TASK-163 | 🆕 |
| P2-17 | Non-Cancel Exception crasht App ohne Sidecar | E9 (E9-04) | 5 | TASK-154 | 🆕 |
| P2-18 | Dashboard zeigt Plan-Werte nach Cancel | E9 (E9-07) | 5 | TASK-164 | 🆕 |
| P2-19 | Report markiert alle Loser als MOVE (auch DryRun) | E9 (E9-08) | 5 | TASK-165 | 🆕 |
| P2-20 | HealthScore ignoriert Run-Fehler | E9 (E9-09) | 5 | TASK-166 | 🆕 |
| P2-21 | 7z-Hash-Reihenfolge nicht deterministisch | E9 (E9-02) | 4 | TASK-150 | 🆕 |

### P3 — Wartbarkeit / Qualität (24 Findings)

| # | Finding | Etappe | Phase | Task | Status |
|---|---------|--------|-------|------|--------|
| P3-01 | Audit-Path-Naming inkonsistent (CLI/API/GUI) | E2 (F-06) | 5 | TASK-073 | ✅ Existing |
| P3-02 | API EnableDatAudit Pass-through ohne Validierung | E2 (F-07) | 5 | TASK-079 | ✅ Existing |
| P3-03 | RuleEngine doppelter Vergleich | E3 (D-03) | 5 | TASK-077 | ✅ Existing |
| P3-04 | FormatScore ignoriert Set-Type Bonus | E4 (W-10) | 5 | TASK-069 | ✅ Existing |
| P3-05 | ConsoleSorter DryRun Phantom-Audit | E6 (S-02) | 5 | TASK-163 | 🆕 (related) |
| P3-06 | CrossRootDeduplicator eigener Default (nicht zentral) | E7 (E7-07) | 5 | TASK-153 | 🆕 (related) |
| P3-07 | ConvertOnly+DryRun No-Op | E7 (E7-09) | 5 | TASK-163 | 🆕 (related) |
| P3-08 | GUI keine Auto-Audit-Anzeige | E7 (E7-06) | 7 | TASK-174 | ✅ → TASK-174 |
| P3-09 | UNKNOWN ohne Erklärung für Benutzer | E8 (UJ-05) | 7 | TASK-174 | 🆕 |
| P3-10 | Trash-Löschung = Datenverlust | E8 (UJ-09) | 7 | TASK-175 | 🆕 |
| P3-11 | Config-Änderung nach DryRun ohne Banner | E8 (UJ-04) | 7 | TASK-176 | 🆕 |
| P3-12 | PhasePlanning 0% Testabdeckung | E9 (E9-11) | 6 | TASK-171 | 🆕 |
| P3-13 | MoveSetAtomically 0 Tests | E9 (E9-12) | 6 | TASK-172 | 🆕 |
| P3-14 | ProgressEstimator 0% Testabdeckung | E9 (E9-13) | 6 | TASK-170 | 🆕 |
| P3-15 | Core I/O Violations (WPF IFileSystem bypass) | E1 (R-02) | 5 | TASK-081 | ✅ Existing |
| P3-16 | MainViewModel God-Object | E1 (R-04) | 7 | TASK-122..124 | ✅ Existing |
| P3-17 | GUI-exclusive Logik | E1 (R-03) | 7 | TASK-122 | ✅ Existing |
| P3-18 | ConversionConditionEvaluator lacks direct tests | E9 | 6 | TASK-065 | ✅ Existing |
| P3-19 | ConversionVerificationHelpers pathological files untested | E9 | 6 | TASK-065 | ✅ Existing |
| P3-20 | RunProjectionFactory guard clause skips check on empty groups | E9 | 5 | TASK-070 | ✅ Existing |
| P3-21 | Partial failure no automatic rollback within phase | E9 | 5 | TASK-074 | ✅ Existing |
| P3-22 | File atomicity cross-volume not safe | E9 | 1 | TASK-008 | ✅ Existing |
| P3-23 | Conversion failure orphaned files | E9 | 4 | TASK-012 | ✅ Existing |
| P3-24 | Concurrent execution no guard in GUI | E9 | 7 | TASK-115 | ✅ Existing |

### Zusammenfassung

| Priorität | Total | Bereits abgedeckt | Neue Tasks |
|-----------|-------|-------------------|------------|
| P1 | 7 | 0 | 7 |
| P2 | 21 | 3 | 18 |
| P3 | 24 | 14 | 10 |
| **Gesamt** | **52** | **17** | **33** |

> **33 neue Tasks** (TASK-144 bis TASK-176) wurden aus dem Audit in die Roadmap integriert.
> **17 bestehende Tasks** decken Audit-Findings bereits ab.
> **2 Findings** werden durch mehrere Tasks gemeinsam adressiert (related).

---

## 9. Audit Release-Readiness Verdict

### Gesamturteil: ❌ NOT READY

Basierend auf dem konsolidierten 10-Etappen-Audit (52 Findings) ist das System **nicht release-fähig**.

**3 systemische Defekte:**
1. **Determinismus-Lücken** — CrossRootDeduplicator, CUE-Selektion, 7z-Hashing, DatSourceService, InsightsEngine sind bei Tie-Situationen nicht deterministisch
2. **Entry-Point-Divergenz** — PreferRegions, ConvertFormat, OnlyGames-Guard, Rollback-Fähigkeit und Settings-Handling inkonsistent zwischen CLI/API/GUI
3. **Fehler-Verschleierung** — hasErrors-Formel, Sidecar-Status, Report-MOVE-Markierung und HealthScore zeichnen ein besseres Bild als die Realität

**Messbares Release-Kriterium (Definition of Done):**
1. ✅ Alle 7 P1-Findings gefixt und durch Tests abgesichert
2. ✅ `dotnet test` 0 Failures
3. ✅ PreferRegions identisch über CLI/API/GUI
4. ✅ `hasErrors`-Formel berücksichtigt alle Phase-Fehler
5. ✅ Sidecar-Status spiegelt RunOutcome
6. ✅ CrossRootDeduplicator hat Path-Tiebreaker
7. ✅ CUE-Selektion und 7z-Hashing deterministisch
8. ✅ CLI Move erfordert Bestätigung
9. ✅ Keine Non-Cancel Exception propagiert ohne Sidecar
10. ✅ Korrupte Settings erzeugen Warning + Backup

---

## 10. Related Specifications / Further Reading

- [OPEN_ITEMS.md](OPEN_ITEMS.md) — Vollständige offene Items (§1–§26)
- [docs/product/CATEGORY_PREFILTER_AUDIT.md](../docs/product/CATEGORY_PREFILTER_AUDIT.md) — Kategorie-Erkennungsrate Analyse
- [docs/product/CONFIDENCE_SORTING_GATE_REDESIGN.md](../docs/product/CONFIDENCE_SORTING_GATE_REDESIGN.md) — 4-Gate-Modell Design
- [docs/product/CONVERSION_PRODUCT_MODEL.md](../docs/product/CONVERSION_PRODUCT_MODEL.md) — Conversion UX & Regeln
- [docs/product/MISS_ANALYSIS_SAFE_RECALL.md](../docs/product/MISS_ANALYSIS_SAFE_RECALL.md) — 60 Missed Entries Analyse
- [docs/architecture/CONVERSION_DOMAIN_AUDIT.md](../docs/architecture/CONVERSION_DOMAIN_AUDIT.md) — 12 Lücken L1–L12
- [docs/architecture/CONVERSION_ENGINE_ARCHITECTURE.md](../docs/architecture/CONVERSION_ENGINE_ARCHITECTURE.md) — Zielarchitektur Graph-Engine
- [docs/architecture/CONVERSION_MATRIX.md](../docs/architecture/CONVERSION_MATRIX.md) — 65 Systeme × Conversion-Pfade
- [docs/architecture/BENCHMARK_AUDIT_REPORT.md](../docs/architecture/BENCHMARK_AUDIT_REPORT.md) — Benchmark-Audit Findings
- [docs/architecture/EVALUATION_STRATEGY.md](../docs/architecture/EVALUATION_STRATEGY.md) — 16 Metriken + Quality Gates
- [.claude/rules/cleanup.instructions.md](../.claude/rules/cleanup.instructions.md) — Verbindliche Coding-Guidelines
- [.github/copilot-instructions.md](../.github/copilot-instructions.md) — Copilot-spezifische Regeln
