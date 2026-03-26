---
goal: DAT-gesteuerte ROM-Reparatur – Audit, Rename und Header-Extraktion
version: 1.0
date_created: 2026-03-23
last_updated: 2026-03-23
owner: daftpunk6161
status: Planned
tags: [feature, architecture, migration, epic]
---

# Implementierungsplan – DAT-gesteuerte ROM-Reparatur

![Status: Planned](https://img.shields.io/badge/status-Planned-blue)

Dieser Plan überführt das Epic "[EPIC] DAT-gesteuerte ROM-Reparatur" (#40–#47) in eine umsetzbare, risikoarme und testbare Implementation. Er basiert auf dem Epic-Review (NO-GO-Urteil wegen Scope-Mixing), dem Fachkonzept (formale Definitionen Have/Miss/Unknown/HaveWrongName/Ambiguous) und der Zielarchitektur (DatAudit als eigene Pipeline-Phase, DatRename nach Deduplicate).

**Kernentscheidungen:**

- Das ursprüngliche Epic wird in **3 getrennte Epics** aufgeteilt (Audit+Rename, Cross-Root, Archive Rebuild)
- **#47 Archive Rebuild** und **#46 Fix from Elsewhere** werden aus diesem Plan **entfernt** (eigenes Epic)
- **#44 GUI** kommt **nach** Core/CLI/API, nicht parallel
- **#45 Headerless Hashing** ist **Voraussetzung** für korrektes DAT-Matching bei NES/SNES/Lynx/7800
- **Phase 1** schafft die Datengrundlage, bevor irgendeine Aktion (Rename/Repair) stattfindet

---

## 1. Requirements & Constraints

### Funktionale Anforderungen

- **REQ-001**: Jede ROM muss gegen den DAT-Index klassifiziert werden: `Have`, `HaveWrongName`, `Miss`, `Unknown`, `Ambiguous`
- **REQ-002**: ROMs mit `HaveWrongName` können per DAT-gestütztem Rename auf den kanonischen Namen gebracht werden
- **REQ-003**: Header-Analyse und Headerless Hashing müssen für NES, SNES, Atari 7800, Atari Lynx verfügbar sein
- **REQ-004**: DatAudit-Report als eigenständiger Output (CLI JSON, API Response, GUI Tab, CSV/HTML Export)
- **REQ-005**: Preview/DryRun → Summary → Bestätigung → Execute → Report → Undo-Fähigkeit
- **REQ-006**: Kein `Wrong`-Status in Phase 1 – kein Fuzzy-Matching, nur exakte Hash-Treffer
- **REQ-007**: RomCandidate muss den berechneten Hash speichern (neues Property `Hash`)
- **REQ-008**: DatIndex muss optional den ROM-Dateinamen aus der DAT speichern (für HaveWrongName-Erkennung)

### Sicherheitsanforderungen

- **SEC-001**: DatRename darf keine Path-Traversal-Lücke öffnen – `ResolveChildPathWithinRoot()` vor jedem Rename
- **SEC-002**: Sanitisierung: Ungültige Zeichen, NTFS ADS, Reserved Names, MAX_PATH-Prüfung
- **SEC-003**: Header-Repair: Backup vor Schreiboperation (.bak), Audit-Logging jeder Änderung
- **SEC-004**: Archiv-Extraktion: Zip-Slip-Schutz bei Header-Analyse von ZIP/7z-Inhalten
- **SEC-005**: CSV-Injection-Prevention in Audit/Report-Feldern (bestehend via `SanitizeCsvField`)

### Architektur-Constraints

- **CON-001**: Dependency-Richtung: Entry Points → Infrastructure → Core → Contracts (nie umgekehrt)
- **CON-002**: Keine Businesslogik in WPF Code-Behind – MVVM erzwingen
- **CON-003**: Core darf keine I/O-Abhängigkeiten haben
- **CON-004**: Alle Entry Points (CLI/API/GUI) müssen identische fachliche Ergebnisse liefern (Parität)
- **CON-005**: Determinismus: Gleiche Inputs → gleiche Outputs für DatAudit, DatRename, Winner Selection

### Richtlinien

- **GUD-001**: CLI-first – Core → Infrastructure → CLI → API → GUI (validiere Modelle vor UI-Binding)
- **GUD-002**: Jede Phase muss isoliert testbar sein (Unit + Integration)
- **GUD-003**: Feature Flags in `RunOptions` für schrittweises Aktivieren (`EnableDatAudit`, `EnableDatRename`)
- **GUD-004**: Bestehende Pipeline-Phasen dürfen nicht gebrochen werden (additiv, nicht destruktiv)

### Patterns

- **PAT-001**: `IPipelinePhase<TIn, TOut>` für neue Phasen (DatAudit, DatRename)
- **PAT-002**: `PipelineState`-Erweiterung für neue Phase-Outputs
- **PAT-003**: `StandardPhaseStepActions`-Erweiterung für neue Func-Einträge
- **PAT-004**: `PhasePlanBuilder`-Erweiterung mit Conditional Logic
- **PAT-005**: `AuditCsvStore` für Rename-Operationen (Rollback-Fähigkeit)
- **PAT-006**: `PhaseStepResult.Ok()`/`.Skipped()` Pattern für Phase-Ergebnisse

---

## 2. Bewertung der bestehenden Sub-Issues

### #40 Header-Analyse aus WPF in Core extrahieren

| Aspekt | Bewertung |
|--------|-----------|
| **Status** | Falsch geschnitten – mischt Extraktion mit neuer Funktionalität |
| **Problem** | Enthält sowohl die reine Extraktion der bestehenden WPF-Logik ALS AUCH die neue HeaderSizeMap für Headerless Hashing – das sind zwei verschiedene Aufgaben |
| **Empfehlung** | **Aufteilen in #40a und #40b** |
| **#40a** | Daten-Grundlage: `HeaderSizeMap` (Konsole→Bytes to skip) als pure Core-Logik + `HeaderlessHasher` in Infrastructure. Ist Voraussetzung für korrekte DAT-Matches bei NES/SNES/7800/Lynx |
| **#40b** | Repair-Extraktion: `AnalyzeHeader`, `RepairNesHeader`, `RemoveCopierHeader`, `DetectPatchFormat` aus `FeatureService.Security.cs` nach Core/Infrastructure verschieben. Kann NACH DatAudit kommen |

### #41 DAT-Rename Service portieren

| Aspekt | Bewertung |
|--------|-----------|
| **Status** | Grundsätzlich korrekt, aber braucht Vorarbeit |
| **Problem** | DatRename benötigt: (1) Hash im RomCandidate, (2) ROM-Filename im DatIndex, (3) DatAudit-Status `HaveWrongName`, (4) `RenameItemSafely` in IFileSystem. Ohne diese 4 Voraussetzungen ist Rename nicht sinnvoll implementierbar |
| **Empfehlung** | Bleibt als eigenes Issue, aber NACH DatAudit-Phase, NACH DatIndex-Erweiterung |
| **Abhängigkeit** | Benötigt abgeschlossene DatAudit-Phase und erweiterten DatIndex |

### #42 Have/Miss/Wrong Report

| Aspekt | Bewertung |
|--------|-----------|
| **Status** | Falsch betitelt – Phase 1 hat kein "Wrong" |
| **Problem** | "Wrong" impliziert Fuzzy-Matching, was nicht-deterministisch ist. Phase 1 kennt nur: Have, HaveWrongName, Miss, Unknown, Ambiguous |
| **Empfehlung** | **Umbenennen zu "DatAudit Report"**. Ist der zentrale Output der DatAudit-Phase. Umfasst CLI/API/GUI-Parität |
| **Schnitt** | Zu groß – muss aufgeteilt werden in: (1) DatAudit-Phase + Modelle, (2) CLI-Output, (3) API-Endpoint, (4) GUI-Tab, (5) CSV/HTML-Report-Erweiterung |

### #43 RunOrchestrator-Phase Repair + CLI --repair

| Aspekt | Bewertung |
|--------|-----------|
| **Status** | Verfrüht – mischt Audit und Repair in einer Phase |
| **Problem** | "Repair" suggeriert aktive Änderungen (Rename + Header-Repair). Aber DatAudit (rein analytisch) muss zuerst stehen und validiert sein |
| **Empfehlung** | **Aufteilen**: (1) DatAudit-Phase im Orchestrator (read-only, keine Dateiänderungen), (2) DatRename-Phase (Dateioperationen), (3) HeaderRepair-Phase (optional, eigenes Epic/Phase 2) |
| **Reihenfolge** | Audit (read-only) → Rename (reversibel) → Repair (irreversibel aber mit Backup) |

### #44 GUI Repair Integration in WPF

| Aspekt | Bewertung |
|--------|-----------|
| **Status** | Korrekt als letztes Issue positioniert |
| **Problem** | GUI macht nur dann Sinn, wenn Core/CLI/API stabil sind und die Modelle validiert wurden |
| **Empfehlung** | Bleibt am Ende, aber aufteilen: (1) DatAudit-Tab (read-only Anzeige), (2) DatRename-Aktion (mit Preview/Confirm-Flow) |
| **Risiko** | GUI ist das komplexeste Stück – MVVM + async + Preview/Execute/Undo |

### #45 Headerless Hashing

| Aspekt | Bewertung |
|--------|-----------|
| **Status** | **FALSCH EINGEORDNET** – ist als späteres Issue geplant, muss aber VOR DatAudit kommen |
| **Problem** | No-Intro DATs hashen NES/SNES/7800/Lynx **ohne Header**. Ohne Headerless Hashing sind DAT-Matches für diese Systeme systematisch falsch. Ein DatAudit ohne Headerless Hashing liefert für ~4 der 10 unterstützten Systeme nur NoMatch |
| **Empfehlung** | **VOR DatAudit** implementieren. Ist die technische Grundlage für korrekte Matches |
| **Kritischer Pfad** | HeaderSizeMap → HeaderlessHasher → EnrichmentPipelinePhase-Integration → dann DatAudit |

### #46 Fix from Elsewhere (Cross-Root-Suche)

| Aspekt | Bewertung |
|--------|-----------|
| **Status** | **Eigenes Epic** – nicht Teil des DAT-Audit/Rename-Scopes |
| **Problem** | Cross-Root-Suche ist ein komplett anderes Feature (Suche über mehrere Verzeichnisse, Duplikaterkennung über Roots hinweg). Hat eigene Safety-Implikationen (welcher Root darf was?) |
| **Empfehlung** | **Aus diesem Epic entfernen**. Eigenes Epic "Cross-Root Matching & Repair" |
| **Voraussetzung** | Benötigt stabiles DatAudit als Grundlage (Miss-Liste) |

### #47 Archive Rebuild

| Aspekt | Bewertung |
|--------|-----------|
| **Status** | **Eigenes Epic** – technisch und fachlich unabhängig |
| **Problem** | Archive Rebuild (ZIP/7z → korrekte Struktur) hat eigene Komplexität: Extraktion, Re-Archivierung, Tool-Abhängigkeit (7z), Safety für temporäre Dateien. Mischt sich nicht mit DAT-Audit/Rename |
| **Empfehlung** | **Aus diesem Epic entfernen**. Eigenes Epic "Archive Rebuild & Restructuring" |
| **Voraussetzung** | Benötigt DatRename als Grundlage (korrekter Name vor Re-Archivierung) |

---

## 3. Empfohlene neue Issue-Struktur

### Epic A: DAT-Audit & Rename (dieses Epic)

Scope: Alles was nötig ist, um ROMs gegen DAT-Dateien zu klassifizieren und korrekt zu benennen.

### Phase 1 – Datengrundlage (Models, Core, Infrastructure)

| Issue | Titel | Scope |
|-------|-------|-------|
| **A-01** | Contracts: DatAuditStatus, DatAuditEntry, DatRenameProposal Modelle | Neue Types in `RomCleanup.Contracts/Models/` |
| **A-02** | Core: HeaderSizeMap + Headerless Hash-Logik | Pure Mapping console→skip bytes in Core, `IHeaderlessHasher` Port |
| **A-03** | Infrastructure: HeaderlessHasher-Implementierung | `HeaderlessHasher` liest Datei, skippt N Bytes, hasht Rest. Integration mit `FileHashService` |
| **A-04** | DatIndex erweitern: ROM-Filename speichern | `Add(consoleKey, hash, gameName, romFileName?)` + `LookupWithFilename()` |
| **A-05** | RomCandidate erweitern: Hash-Property + DatAuditStatus | Neues `Hash` Property, neues `DatGameName` Property, neues `DatAuditStatus` Property |

### Phase 2 – DatAudit-Phase (Read-Only-Analyse)

| Issue | Titel | Scope |
|-------|-------|-------|
| **A-06** | Core: DatAuditClassifier (pure Logik) | Input: Hash + DatIndex → Output: DatAuditStatus (Have/HaveWrongName/Miss/Unknown/Ambiguous). Pure, keine I/O |
| **A-07** | Infrastructure: DatAuditPipelinePhase | Neue `IPipelinePhase<DatAuditInput, DatAuditResult>`. Integriert HeaderlessHasher. Setzt DatAuditStatus auf RomCandidate |
| **A-08** | Orchestrator: DatAudit-Phase einbinden | `PipelineState` erweitern um `DatAuditResult`. `PhasePlanBuilder` erweitern. `StandardPhaseStepActions` erweitern. Conditional: `EnableDatAudit && DatIndex != null` |
| **A-09** | RunResult/RunProjection erweitern | Neue DAT-Audit-Metriken: HaveCount, HaveWrongNameCount, MissCount, UnknownCount, AmbiguousCount |

### Phase 3 – DatAudit Outputs (CLI/API/Report)

| Issue | Titel | Scope |
|-------|-------|-------|
| **A-10** | CLI: `--dat-audit` Output + DryRun JSON-Erweiterung | JSON-Ausgabe mit DatAudit-Ergebnissen, Summary-Zeile |
| **A-11** | API: DatAudit-Endpoint + ApiRunResult-Erweiterung | `GET /api/dat-audit` oder Erweiterung des bestehenden Run-Endpoints |
| **A-12** | Reporting: DatAudit in CSV/HTML/JSON Reports | ReportSummary erweitern, HTML-Cards, CSV-Spalten |

### Phase 4 – DatRename (Aktiv, aber reversibel)

| Issue | Titel | Scope |
|-------|-------|-------|
| **A-13** | IFileSystem: `RenameItemSafely()` hinzufügen | Neue Methode in `IFileSystem` + `FileSystemAdapter`. Path-Traversal, MAX_PATH, Conflict, Audit-Logging |
| **A-14** | Core: DatRenamePolicy (pure Logik) | Input: AuditStatus + DatGameName + aktueller Filename → Output: RenameDecision (Rename/Skip/Conflict/PathTooLong) |
| **A-15** | Infrastructure: DatRenamePipelinePhase | Neue Pipeline-Phase. Filtert `HaveWrongName` Candidates. Sanitisiert Namen. Führt Rename durch. Audit-Logging via `IAuditStore` |
| **A-16** | Orchestrator: DatRename-Phase einbinden | PhasePlanBuilder-Erweiterung. Conditional: `EnableDatRename && Mode == "Move"`. NACH Deduplicate, VOR ConsoleSort |
| **A-17** | CLI: `--dat-rename` Flag + DryRun/Execute | Neues Flag in CLI-Options, JSON-Output für geplante Renames |

### Phase 5 – Header-Repair-Extraktion (Tech Debt)

| Issue | Titel | Scope |
|-------|-------|-------|
| **A-18** | Core: HeaderAnalyzer aus WPF extrahieren | `AnalyzeHeader()` → Core als pure Logik. Models: `RomHeaderInfo` nach Contracts |
| **A-19** | Infrastructure: HeaderRepairService | `RepairNesHeader()`, `RemoveCopierHeader()` aus WPF nach Infrastructure. Backup-Logik, Audit-Logging |
| **A-20** | WPF: FeatureService.Security.cs entschlacken | Bestehende Methoden durch Delegation an Core/Infrastructure ersetzen. Code-Behind-Violation auflösen |

### Phase 6 – GUI (WPF)

| Issue | Titel | Scope |
|-------|-------|-------|
| **A-21** | GUI: DatAudit-Tab (Read-Only) | Neuer Tab mit Have/Miss/Unknown-Übersicht. DashboardProjection erweitern. ViewModel + MVVM |
| **A-22** | GUI: DatRename-Aktion (Preview/Confirm/Execute) | Preview → Summary → Bestätigung → Execute → Report. Undo via Audit-Rollback |

### Eigene Epics (NICHT in diesem Plan)

| Epic | Titel | Voraussetzung |
|------|-------|---------------|
| **Epic B** | Cross-Root Matching & Repair (#46) | Stabiles DatAudit (Epic A Phase 2) |
| **Epic C** | Archive Rebuild & Restructuring (#47) | Stabiles DatRename (Epic A Phase 4) |

---

## 4. Reihenfolge der Umsetzung

### Implementation Phase 1 – Datengrundlage

- GOAL-001: Alle Modelle, Ports und Core-Primitives für DAT-Audit bereitstellen. Nach dieser Phase kompiliert das Projekt mit den neuen Types, aber ohne neue Laufzeitlogik.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-001 | `DatAuditStatus` Enum anlegen in `src/RomCleanup.Contracts/Models/DatAuditModels.cs`: `Have`, `HaveWrongName`, `Miss`, `Unknown`, `Ambiguous` | x | 2026-03-24 |
| TASK-002 | `DatAuditEntry` Record anlegen: `(string FilePath, string Hash, DatAuditStatus Status, string? DatGameName, string? DatRomFileName, string ConsoleKey, int Confidence)` | x | 2026-03-24 |
| TASK-003 | `DatRenameProposal` Record anlegen: `(string SourcePath, string TargetFileName, DatAuditStatus Status, string? ConflictReason)` | x | 2026-03-24 |
| TASK-004 | `DatAuditResult` Record anlegen: `(IReadOnlyList<DatAuditEntry> Entries, int HaveCount, int HaveWrongNameCount, int MissCount, int UnknownCount, int AmbiguousCount)` | x | 2026-03-24 |
| TASK-005 | `RomCandidate` erweitern: `string? Hash { get; init; }`, `string? DatGameName { get; init; }`, `DatAuditStatus DatAuditStatus { get; init; } = DatAuditStatus.Unknown` | x | 2026-03-24 |
| TASK-006 | `DatIndex.Add()` erweitern: Optionaler `string? romFileName = null` Parameter. Neues internes Dictionary `ConcurrentDictionary<string, ConcurrentDictionary<string, (string GameName, string? RomFileName)>>` | x | 2026-03-24 |
| TASK-007 | `DatIndex.LookupWithFilename()` Methode: Returns `(string GameName, string? RomFileName)?` | x | 2026-03-24 |
| TASK-008 | `DatRepositoryAdapter.ParseDatFile()` erweitern: ROM `name` Attribut aus `<rom>` Elementen extrahieren und an `DatIndex.Add()` als `romFileName` übergeben | x | 2026-03-24 |
| TASK-009 | Unit Tests: DatAuditStatus-Enum-Vollständigkeit, DatIndex.LookupWithFilename()-Tests, DatRepositoryAdapter-romFileName-Tests | x | 2026-03-24 |

### Implementation Phase 2 – Headerless Hashing

- GOAL-002: Korrekte DAT-Matches für NES/SNES/Atari 7800/Atari Lynx durch Header-Skipping sicherstellen. Dies ist die technische Voraussetzung für eine korrekte DatAudit-Phase.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-010 | `HeaderSizeMap` in `src/RomCleanup.Core/Classification/HeaderSizeMap.cs` anlegen: `static IReadOnlyDictionary<string, int>` mit Mapping `{ "NES" → 16, "SNES" → 512 (nur wenn copier header erkannt), "ATARI7800" → 128, "ATARILYNX" → 64 }`. Methode `GetSkipBytes(string consoleKey, byte[] headerBytes) → int` | x | 2026-03-24 |
| TASK-011 | `IHeaderlessHasher` Port in `src/RomCleanup.Contracts/Ports/IHeaderlessHasher.cs`: `string? ComputeHeaderlessHash(string filePath, string consoleKey, string hashType = "SHA1")` | x | 2026-03-24 |
| TASK-012 | `HeaderlessHasher` Implementierung in `src/RomCleanup.Infrastructure/Hashing/HeaderlessHasher.cs`: Öffnet FileStream, seeked über Header-Bytes, hasht den Rest. Nutzt `CartridgeHeaderDetector.Detect()` zur Konsol-Erkennung, dann `HeaderSizeMap.GetSkipBytes()` für Skip-Offset. LRU-Cache | x | 2026-03-24 |
| TASK-013 | `EnrichmentPipelinePhase` erweitern: Wenn `IHeaderlessHasher` verfügbar und Konsole in HeaderSizeMap → headerless Hash berechnen und für DAT-Lookup verwenden. Fallback auf normalen Hash | x | 2026-03-24 |
| TASK-014 | Unit Tests: HeaderSizeMap-Vollständigkeit, HeaderlessHasher mit Mock-Dateien (NES 16 Byte iNES Header, SNES 512 Byte Copier Header), EnrichmentPipelinePhase-Integration | x | 2026-03-24 |
| TASK-015 | Regression: Bestehende DAT-Match-Tests müssen weiterhin grün sein. Headerless Hashing darf bei Konsolen OHNE Header-Mapping nicht aktiviert werden | x | 2026-03-24 |

### Implementation Phase 3 – DatAudit Core + Pipeline-Phase

- GOAL-003: Read-only DatAudit-Phase, die alle ROMs gegen den DatIndex klassifiziert und das Ergebnis auf PipelineState speichert. Keine Dateiänderungen.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-016 | `DatAuditClassifier` in `src/RomCleanup.Core/Audit/DatAuditClassifier.cs`: Pure static Methode `Classify(string? hash, string? headerlessHash, string actualFileName, string consoleKey, DatIndex datIndex) → DatAuditStatus`. Logik: (1) Hash in DatIndex? → (2) ROM-Filename match? → Have/HaveWrongName. (3) Kein Hash-Match → Unknown. (4) Multi-Console-Treffer → Ambiguous | x | 2026-03-24 |
| TASK-017 | `DatAuditPipelinePhase` in `src/RomCleanup.Infrastructure/Orchestration/DatAuditPipelinePhase.cs`: Implementiert `IPipelinePhase<DatAuditInput, DatAuditResult>`. Iteriert über `PipelineState.AllCandidates`, ruft `DatAuditClassifier.Classify()` pro Candidate, setzt `RomCandidate.DatAuditStatus` + `DatGameName`, aggregiert Counts zu `DatAuditResult` | x | 2026-03-24 |
| TASK-018 | `PipelineState` erweitern: `DatAuditResult? DatAuditResult { get; private set; }` + Setter `SetDatAuditOutput(DatAuditResult result)` | x | 2026-03-24 |
| TASK-019 | `StandardPhaseStepActions` erweitern: `public Func<PipelineState, CancellationToken, PhaseStepResult>? DatAudit { get; init; }` (nullable, weil optional) | x | 2026-03-24 |
| TASK-020 | `PhasePlanBuilder` erweitern: Neue Phase `DatAuditPhaseStep` einfügen **VOR** `DeduplicatePhaseStep`. Conditional: `options.EnableDatAudit && datIndex != null` | x | 2026-03-24 |
| TASK-021 | `RunOptions` erweitern: `public bool EnableDatAudit { get; init; }` + `public bool EnableDatRename { get; init; }` | x | 2026-03-24 |
| TASK-022 | RunOrchestrator: DatAudit-Phase-Step verdrahten in `RunOrchestrator.StandardPhaseSteps.cs`. Lambda erstellen, das `DatAuditPipelinePhase.Execute()` aufruft | x | 2026-03-24 |
| TASK-023 | Unit Tests: DatAuditClassifier – alle 5 Status-Pfade (Have, HaveWrongName, Miss/Unknown, Ambiguous). Edge Cases: leerer Hash, null DatIndex, unbekannte Konsole. Determinismus-Test: gleicher Input → gleicher Output | x | 2026-03-24 |
| TASK-024 | Integration Tests: DatAuditPipelinePhase mit realistischen RomCandidates und DatIndex. Prüfe: Counts stimmen, Status korrekt gesetzt, kein Side Effect auf Dateisystem | x | 2026-03-24 |

### Implementation Phase 4 – Metrics + Outputs (RunResult, CLI, API, Reports)

- GOAL-004: DatAudit-Ergebnisse über alle Entry Points sichtbar machen. CLI-First, dann API, dann Reports.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-025 | `RunResult` erweitern: `DatAuditResult? DatAuditResult { get; init; }`, `int DatHaveCount { get; init; }`, `int DatHaveWrongNameCount { get; init; }`, `int DatMissCount { get; init; }`, `int DatUnknownCount { get; init; }`, `int DatAmbiguousCount { get; init; }` | x | 2026-03-24 |
| TASK-026 | `RunProjection` erweitern: Entsprechende Properties aus RunResult übernehmen | x | 2026-03-24 |
| TASK-027 | `RunResultBuilder` erweitern: DatAudit-Metriken aus PipelineState aggregieren | x | 2026-03-24 |
| TASK-028 | CLI: `--dat-audit` Flag. DryRun-JSON erweitern um `datAudit:` Block mit `{ have: N, haveWrongName: N, miss: N, unknown: N, ambiguous: N, entries: [...] }`. Bestehende CLI-Ausgabe um Summary-Zeile ergänzen | x | 2026-03-24 |
| TASK-029 | API: `ApiRunResult` um DAT-Audit-Felder erweitern. OpenAPI-Spec aktualisieren. `/api/run` Response enthält DatAudit-Daten | x | 2026-03-24 |
| TASK-030 | Reports: `ReportSummary` erweitern. HTML-Report: Neuer "DAT Audit" Card-Block mit Pie-Chart der Status-Verteilung. CSV: Neue Spalten `DatAuditStatus`, `DatGameName` | x | 2026-03-24 |
| TASK-031 | Parität-Tests: CLI-JSON, API-Response und HTML-Report zeigen identische Zahlen. Mindestens 3 Tests pro Entry Point | x | 2026-03-24 |

### Implementation Phase 5 – DatRename (Aktive Operationen)

- GOAL-005: DAT-gestütztes Rename für ROMs mit Status `HaveWrongName`. Sichere, reversible Dateioperationen mit Audit-Trail.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-032 | `IFileSystem.RenameItemSafely()` hinzufügen in Contracts: `string? RenameItemSafely(string sourcePath, string newFileName)`. Default-Implementation: `throw new NotSupportedException()` | x | 2026-03-24 |
| TASK-033 | `FileSystemAdapter.RenameItemSafely()` implementieren: (1) `ResolveChildPathWithinRoot()` für neuen Pfad, (2) Ungültige Zeichen sanitisieren via `Path.GetInvalidFileNameChars()`, (3) MAX_PATH prüfen (<260), (4) Conflict-Check (Ziel existiert? → `__DUP{N}` Suffix oder Skip je nach Policy), (5) `File.Move(source, dest)`, (6) Return: tatsächlicher Zielpfad oder null | x | 2026-03-24 |
| TASK-034 | `DatRenamePolicy` in `src/RomCleanup.Core/Audit/DatRenamePolicy.cs`: Pure static Methode `EvaluateRename(DatAuditEntry entry, string currentFileName) → DatRenameProposal`. Prüft: Ist Status `HaveWrongName`? Ist neuer Name gültig? Pfadlänge? Extension-Bewahrung | x | 2026-03-24 |
| TASK-035 | `DatRenamePipelinePhase` in `src/RomCleanup.Infrastructure/Orchestration/DatRenamePipelinePhase.cs`: Implementiert `IPipelinePhase<DatRenameInput, DatRenameResult>`. (1) Filtert HaveWrongName-Candidates, (2) DatRenamePolicy pro Candidate, (3) DryRun: nur Proposals sammeln, (4) Execute: `RenameItemSafely()` + `IAuditStore.AppendAuditRow()` mit Action="DatRename" | x | 2026-03-24 |
| TASK-036 | PhasePlanBuilder: `DatRenamePhaseStep` einfügen NACH `DeduplicatePhaseStep`, VOR `MovePhaseStep`. Conditional: `options.EnableDatRename && options.Mode == "Move"` | x | 2026-03-24 |
| TASK-037 | RunOrchestrator: DatRename-Phase-Step verdrahten. RunResult um `DatRenameResult` erweitern | x | 2026-03-24 |
| TASK-038 | CLI: `--dat-rename` Flag. DryRun-JSON um `datRenameProposals: [...]` erweitern. Execute-Mode: Rename durchführen + Summary | x | 2026-03-24 |
| TASK-039 | API: DatRename in ApiRunResult. OpenAPI-Spec aktualisieren | x | 2026-03-24 |
| TASK-040 | Audit-Rollback: DatRename-Operationen sind via bestehender `AuditCsvStore.Rollback()` rückgängig machbar. Integrationstest: Rename → Rollback → Dateien am Originalplatz | x | 2026-03-24 |
| TASK-041 | Unit Tests: DatRenamePolicy – alle Pfade (Rename/Skip/Conflict/PathTooLong/ExtensionPreserved). FileSystemAdapter.RenameItemSafely – Path-Traversal, MAX_PATH, Conflict, ADS-Block | x | 2026-03-24 |
| TASK-042 | Integration Tests: DatRenamePipelinePhase mit temporären Dateien. DryRun vs Execute. Audit-Logging korrekt. Rollback funktioniert | x | 2026-03-24 |

### Implementation Phase 6 – Header-Repair Extraktion (Tech Debt)

- GOAL-006: Header-Analyse und Repair-Logik aus WPF nach Core/Infrastructure verschieben. Architektur-Violation in FeatureService.Security.cs auflösen.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-043 | `RomHeaderInfo` Record nach `src/RomCleanup.Contracts/Models/` verschieben (bereits in WPF definiert, muss nach Contracts) | x | 2026-03-24 |
| TASK-044 | `HeaderAnalyzer` static class in `src/RomCleanup.Core/Classification/HeaderAnalyzer.cs`: Enthält `AnalyzeHeader(byte[] headerBytes, long fileSize) → RomHeaderInfo?`. Pure Logik, kein FileSystem-Zugriff | x | 2026-03-24 |
| TASK-045 | `IHeaderRepairService` Port in Contracts: `RepairNesHeader(string path) → bool`, `RemoveCopierHeader(string path) → bool`. Beide mit Backup-Pflicht | x | 2026-03-24 |
| TASK-046 | `HeaderRepairService` in `src/RomCleanup.Infrastructure/Hashing/HeaderRepairService.cs`: Implementiert `IHeaderRepairService`. Nutzt `IFileSystem.CopyFile()` für Backup. Audit-Logging via `IAuditStore` | x | 2026-03-24 |
| TASK-047 | `FeatureService.Security.cs` entschlacken: `AnalyzeHeader()` → delegiert an `HeaderAnalyzer` (Core). `RepairNesHeader()` / `RemoveCopierHeader()` → delegiert an `IHeaderRepairService` (DI). Kein Code-Behind mehr für Businesslogik | x | 2026-03-24 |
| TASK-048 | Unit Tests: HeaderAnalyzer mit Byte-Arrays (NES iNES Header, SNES LoROM, N64 BE/LE/Swap, GBA). HeaderRepairService mit Mock-IFileSystem | x | 2026-03-24 |
| TASK-049 | Regression: Bestehende WPF-Funktionalität muss nach Migration identisch funktionieren | x | 2026-03-24 |

### Implementation Phase 7 – GUI (WPF)

- GOAL-007: DatAudit-Tab und DatRename-Aktion in der GUI. MVVM, async, Preview/Confirm/Execute-Flow.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-050 | `DashboardProjection` erweitern: `DatHaveDisplay`, `DatMissDisplay`, `DatWrongNameDisplay`, `DatUnknownDisplay` | x | 2026-03-24 |
| TASK-051 | `DatAuditViewModel` in `src/RomCleanup.UI.Wpf/ViewModels/`: Bindable Properties für DatAudit-Ergebnisse. Filter/Sort-Logik (nach Status, Konsole). Exportkommando | x | 2026-03-25 |
| TASK-052 | DatAudit-Tab (XAML): DataGrid mit DatAuditEntry-Zeilen. Status-Badge pro Zeile (farbcodiert). Filter-Kombobox. Summary-Leiste oben | x | 2026-03-25 |
| TASK-053 | DatRename-Aktion: Preview-Button → zeigt Rename-Proposals in Dialog → Confirm-Button → Execute → Ergebnis-Dialog mit Undo-Button | x | 2026-03-24 |
| TASK-054 | Styles: Status-Farben in ResourceDictionary (Have=grün, Miss=rot, WrongName=orange, Unknown=grau, Ambiguous=gelb) | x | 2026-03-25 |
| TASK-055 | Smoke-Test: DatAudit-Tab zeigt Daten korrekt an, DatRename Preview/Execute-Flow komplett durchlaufen | x | 2026-03-24 |

---

## 5. Abhängigkeiten

```
Phase 1 (Datengrundlage)
  ├── A-01 Contracts Models ──────┐
  ├── A-02 HeaderSizeMap (Core) ──┤
  ├── A-04 DatIndex erweitern ────┤ → Phase 2 (Headerless Hashing)
  ├── A-05 RomCandidate erweitern ┤     ├── A-03 HeaderlessHasher (Infra)
  └── A-08/09 DatRepoAdapter ────┘     └── TASK-013 Enrichment-Integration
                                              │
                                              ▼
                                   Phase 3 (DatAudit Phase)
                                     ├── A-06 DatAuditClassifier (Core)
                                     ├── A-07 DatAuditPipelinePhase (Infra)
                                     ├── A-08 Orchestrator Integration
                                     └── A-09 RunResult/RunProjection
                                              │
                                              ▼
                                   Phase 4 (Outputs)
                                     ├── A-10 CLI ──────────┐
                                     ├── A-11 API ──────────┤ → Phase 5 (DatRename)
                                     └── A-12 Reports ──────┘     ├── A-13 RenameItemSafely
                                                                   ├── A-14 DatRenamePolicy
                                                                   ├── A-15 DatRenamePipelinePhase
                                                                   └── A-16–A-17 Orchestr. + CLI
                                                                         │
                                                     ┌────────────────────┤
                                                     ▼                    ▼
                                          Phase 6 (Header-Repair)   Phase 7 (GUI)
                                            ├── A-18 HeaderAnalyzer    ├── A-21 DatAudit-Tab
                                            ├── A-19 HeaderRepairSvc   └── A-22 DatRename-Aktion
                                            └── A-20 WPF Cleanup
```

**Kritische Abhängigkeiten:**

- **DEP-001**: Phase 2 (Headerless Hashing) MUSS vor Phase 3 (DatAudit) abgeschlossen sein – sonst sind NES/SNES/7800/Lynx-Matches systematisch falsch
- **DEP-002**: Phase 1 (Models) MUSS vor allem anderen abgeschlossen sein – alle folgenden Phasen nutzen die Contracts
- **DEP-003**: Phase 3 (DatAudit) MUSS vor Phase 5 (DatRename) abgeschlossen sein – Rename braucht DatAuditStatus
- **DEP-004**: Phase 4 (CLI/API/Reports) MUSS vor Phase 7 (GUI) abgeschlossen sein – GUI bindet an dieselben Modelle
- **DEP-005**: Phase 5 (DatRename) MUSS `IFileSystem.RenameItemSafely()` haben (TASK-032/033)
- **DEP-006**: Phase 6 (Header-Repair) und Phase 7 (GUI) sind **unabhängig** voneinander und können parallel laufen
- **DEP-007**: `DatRepositoryAdapter.ParseDatFile()` muss ROM-Filename extrahieren BEVOR DatAudit HaveWrongName korrekt klassifizieren kann

---

## 6. Risiken pro Phase

### Phase 1 – Datengrundlage

- **RISK-001**: DatIndex-Erweiterung bricht bestehende Tests. *Mitigation*: `romFileName` ist optional (null default), bestehende `Add(key, hash, name)` Signatur bleibt als Overload erhalten, interne Datenstruktur-Änderung `string → (string, string?)` erfordert Anpassung aller Lookup-Methoden.
- **RISK-002**: RomCandidate ist immutable (`init`-Properties) – Erweitern erzwingt Default-Werte an allen existierenden Konstruktionsstellen. *Mitigation*: Default `DatAuditStatus.Unknown` und `Hash = null`, kein Breaking Change.

### Phase 2 – Headerless Hashing

- **RISK-003**: Falsche Header-Größe führt zu systematischen Hash-Mismatches. *Mitigation*: Strikte Validierung gegen No-Intro Referenz-DATs für alle 4 Systeme. Benchmark-Testset mit bekannten Hashes.
- **RISK-004**: SNES Copier Header ist **optional** (nicht alle SNES-ROMs haben ihn). `GetSkipBytes()` muss den Header per Magic Bytes erkennen, nicht per Konsole allein annehmen. *Mitigation*: SNES-Sonderlogik: Prüfe `fileSize % 1024 == 512` VOR Skip-Entscheidung.

### Phase 3 – DatAudit Core

- **RISK-005**: Ambiguous-Erkennung (Multi-Console-Treffer) könnte zu viele False Positives erzeugen. *Mitigation*: Nur wenn `LookupAny()` mehrere ConsoleKeys für denselben Hash zurückgibt UND kein spezifischer ConsoleKey aus der Enrichment-Phase bekannt ist.
- **RISK-006**: DatAudit-Phase verlangsamt die Pipeline signifikant (Hash-Berechnung für jeden Candidate). *Mitigation*: Headerless Hash nur für relevante Konsolen, LRU-Cache, Reuse von bereits in Enrichment berechneten Hashes.

### Phase 4 – Outputs

- **RISK-007**: CLI/API/GUI-Parität schwer durchzusetzen bei neuen Metriken. *Mitigation*: Parität-Tests als Gate. `RunProjection` als Single Source of Truth.

### Phase 5 – DatRename

- **RISK-008**: Rename-Konflikte (Zieldatei existiert bereits) in Batch-Operationen. *Mitigation*: Conflict Policy aus RunOptions respektieren (`Rename/Skip/Error`). `__DUP{N}` Suffix-Pattern aus `MoveItemSafely` übernehmen.
- **RISK-009**: Audit-Rollback nach partiellem Rename (Crash mitten in Batch). *Mitigation*: Jedes Einzelrename wird sofort in Audit-CSV geschrieben (Flush nach jedem Row). Rollback auf Einzelrename-Basis.

### Phase 6 – Header Repair

- **RISK-010**: Header-Repair ist **irreversibel** für die Original-Datei (auch mit .bak). *Mitigation*: .bak-Pflicht, Audit-Logging, DryRun-Modus, GUI-Bestätigung.

### Phase 7 – GUI

- **RISK-011**: async Pipeline + UI-Updates → Deadlock-Risiko. *Mitigation*: Dispatcher-Pattern, `ConfigureAwait(false)` in Services.

---

## 7. Exit-Kriterien pro Phase

### Phase 1 – Datengrundlage
- [ ] Alle neuen Types kompilieren und haben Doku-Kommentare
- [ ] `DatIndex.Add()` akzeptiert optionalen `romFileName`
- [ ] `DatIndex.LookupWithFilename()` gibt `(GameName, RomFileName?)` zurück
- [ ] `DatRepositoryAdapter` extrahiert ROM-Filename aus `<rom name="...">` Elementen
- [ ] `RomCandidate` hat `Hash`, `DatGameName`, `DatAuditStatus` Properties
- [ ] Alle bestehenden Tests grün (`dotnet test src/RomCleanup.Tests/RomCleanup.Tests.csproj --nologo`)
- [ ] Mindestens 10 neue Unit Tests für die neuen Types

### Phase 2 – Headerless Hashing
- [ ] `HeaderSizeMap` deckt NES (16), SNES (512 conditional), Atari 7800 (128), Atari Lynx (64) ab
- [ ] `HeaderlessHasher` produziert korrekte Hashes für bekannte No-Intro Referenz-ROMs
- [ ] `EnrichmentPipelinePhase` nutzt Headerless Hash als primären DAT-Lookup, normalen Hash als Fallback
- [ ] Kein Headerless Hashing für Konsolen ohne HeaderSizeMap-Eintrag
- [ ] Benchmark-Test: Mindestens 10 bekannte NES/SNES-ROMs matchen korrekt gegen No-Intro DAT
- [ ] Alle bestehenden Tests grün

### Phase 3 – DatAudit Core
- [ ] `DatAuditClassifier` klassifiziert korrekt: Have, HaveWrongName, Miss, Unknown, Ambiguous
- [ ] Determinismus: 3× gleicher Input → 3× identischer Output
- [ ] `DatAuditPipelinePhase` integriert in Pipeline VOR Deduplicate
- [ ] `PipelineState.DatAuditResult` wird korrekt gesetzt
- [ ] Kein Dateisystem-Side-Effect (read-only Phase)
- [ ] Alle bestehenden Tests grün
- [ ] Mindestens 15 neue Tests (5 Status-Pfade × 3 Varianten)

### Phase 4 – Outputs
- [ ] CLI `--dat-audit` zeigt Summary + JSON
- [ ] API Response enthält DatAudit-Felder
- [ ] HTML-Report hat DatAudit-Card
- [ ] CSV-Report hat `DatAuditStatus`, `DatGameName` Spalten
- [ ] Parität: CLI-JSON-Zahlen == API-Response-Zahlen == HTML-Report-Zahlen
- [ ] Mindestens 3 Parität-Tests

### Phase 5 – DatRename
- [ ] `RenameItemSafely()` funktioniert mit Path-Traversal-Schutz, MAX_PATH, Conflict, ADS-Block
- [ ] DatRename nur für `HaveWrongName`-Candidates
- [ ] DryRun zeigt Proposals ohne Dateisystem-Änderung
- [ ] Execute führt Rename durch + Audit-Logging
- [ ] Rollback stellt Originalnamen wieder her
- [ ] Extension wird beibehalten
- [ ] Alle bestehenden Tests grün
- [ ] Mindestens 12 neue Tests (Rename, Conflict, PathTooLong, Rollback, DryRun)

### Phase 6 – Header Repair
- [ ] `HeaderAnalyzer` ist pure Core-Logik ohne I/O
- [ ] `HeaderRepairService` nutzt IFileSystem + IAuditStore
- [ ] `FeatureService.Security.cs` delegiert an Core/Infrastructure
- [ ] .bak-Backup vor jedem Repair
- [ ] Alle bestehenden WPF-Features funktionieren wie vorher

### Phase 7 – GUI
- [ ] DatAudit-Tab zeigt Have/Miss/WrongName/Unknown/Ambiguous mit Farb-Badges
- [ ] DatRename Preview → Confirm → Execute → Report → Undo Flow komplett
- [ ] MVVM: Keine Businesslogik im Code-Behind
- [ ] Async: Kein UI-Thread-Blocking
- [ ] Smoke-Test bestanden

---

## 8. Was in ein eigenes Epic gehört

### Epic B: Cross-Root Matching & Repair (#46)

**Warum eigenständig:**
- Cross-Root-Suche hat eigene Safety-Implikationen (Root-Isolation, wer darf wohin verschieben)
- Erfordert neue UI-Konzepte (Multi-Root-Auswahl, Source/Target-Mapping)
- Miss-Liste aus DatAudit ist Voraussetzung, aber die Suche selbst ist ein anderes Feature

**Scope:**
- Multi-Root-Konfiguration
- Hash-basierte Suche über andere Roots (haben wir die fehlende ROM in einem anderen Verzeichnis?)
- Move/Copy mit Root-Isolation
- Eigene Audit-Trail

**Voraussetzung:** Epic A Phase 3 (DatAudit) abgeschlossen

---

### Epic C: Archive Rebuild & Restructuring (#47)

**Warum eigenständig:**
- Archive Rebuild erfordert Tool-Integration (7z, zip)
- Eigene Safety-Logik für temporäre Dateien, Extraktion, Re-Archivierung
- Zip-Slip-Risiko bei jeder Extraktion
- Kann nur sinnvoll nach DatRename erfolgen (erst richtiger Name, dann korrekte Archivstruktur)

**Scope:**
- Archiv-Extraktion mit Validierung
- ROM-Rename innerhalb Archiv
- Re-Archivierung mit korrekter Struktur
- Integrität-Verifikation nach Rebuild

**Voraussetzung:** Epic A Phase 5 (DatRename) abgeschlossen

---

## 9. Was sofort begonnen werden sollte

### Sofort (diese Woche)

1. **TASK-001 bis TASK-004** (Contracts Models) – reine Typ-Definitionen, keine Abhängigkeiten, kein Risiko. Schafft die Grundlage für alles Weitere.

2. **TASK-010** (HeaderSizeMap) – pure Core-Logik, ein statisches Dictionary. Keine Abhängigkeit, sofort testbar.

3. **TASK-005** (RomCandidate erweitern) – kann parallel zu TASK-001 erfolgen, da nur neue `init` Properties mit Defaults.

### Kurzfristig (nächste Woche)

4. **TASK-006 bis TASK-009** (DatIndex erweitern + DatRepositoryAdapter) – erfordert sorgfältige Arbeit wegen bestehender Datenstruktur-Änderung. Regression-Risiko moderat.

5. **TASK-011 bis TASK-015** (Headerless Hashing) – kann parallel zu DatIndex-Erweiterung laufen (anderes Subsystem). Braucht nur HeaderSizeMap aus TASK-010.

### Was NICHT sofort begonnen werden sollte

- **GUI (Phase 7)** – zu früh, Modelle noch nicht validiert
- **DatRename (Phase 5)** – ohne DatAudit keine Grundlage
- **Header-Repair-Extraktion (Phase 6)** – ist Tech Debt, nicht feature-kritisch für Phase 1-4

---

## 3. Alternatives

- **ALT-001**: DatAudit in EnrichmentPipelinePhase integrieren statt eigene Phase. *Verworfen*: EnrichmentPipelinePhase ist bereits komplex (Archive-Handling, Console-Detection, Hash-Berechnung). DatAudit als Erweiterung würde Single Responsibility verletzen und Testbarkeit verschlechtern.
- **ALT-002**: Wrong-Status in Phase 1 via Fuzzy Name Matching. *Verworfen*: Nicht-deterministisch, hohe False-Positive-Rate, widerspricht CON-005. Wird ggf. in Phase 2 als opt-in Feature nachgereicht.
- **ALT-003**: DatRename vor DatAudit. *Verworfen*: Rename ohne vorherige Klassifikation ist blind. Audit muss zuerst statusbasiert entscheiden, welche ROMs umbenannt werden sollen.
- **ALT-004**: HeaderSizeMap als JSON-Datendatei statt Code. *Verworfen*: Die Map ist klein (4 Einträge), ändert sich selten, und die SNES-Sonderlogik (conditional skip) erfordert Code-Logik, kein reines Data-Mapping.
- **ALT-005**: GUI parallel zu CLI/API entwickeln. *Verworfen*: MVVM-Bindings an instabile Modelle führen zu doppelter Arbeit. CLI-first validiert die Datenstruktur bevor UI-Komplexität dazukommt.

---

## 4. Dependencies

- **DEP-001**: `CartridgeHeaderDetector` (existiert, Core) – wird von `HeaderSizeMap` und `HeaderlessHasher` konsumiert
- **DEP-002**: `DatIndex` (existiert, Contracts) – wird erweitert, bestehendes Interface muss backward-compatible bleiben
- **DEP-003**: `DatRepositoryAdapter` (existiert, Infrastructure) – ROM-Filename-Extraction erfordert Erweiterung der XML-Parsing-Logik
- **DEP-004**: `FileHashService` (existiert, Infrastructure) – wird von `HeaderlessHasher` als Referenz genutzt
- **DEP-005**: `EnrichmentPipelinePhase` (existiert, Infrastructure) – muss `IHeaderlessHasher` konsumieren
- **DEP-006**: `PhasePlanBuilder` (existiert, Infrastructure) – muss 2 neue Phase Steps (DatAudit, DatRename) einordnen
- **DEP-007**: `IAuditStore` / `AuditCsvStore` (existiert, Infrastructure) – wird von DatRename für Audit-Trail genutzt
- **DEP-008**: `IFileSystem` / `FileSystemAdapter` (existiert, Contracts/Infrastructure) – muss `RenameItemSafely()` erhalten
- **DEP-009**: Keine externe Paket-Abhängigkeit erforderlich – alle benötigten Libraries sind bereits im Projekt

---

## 5. Files

### Neue Dateien

- **FILE-001**: `src/RomCleanup.Contracts/Models/DatAuditModels.cs` – DatAuditStatus, DatAuditEntry, DatRenameProposal, DatAuditResult
- **FILE-002**: `src/RomCleanup.Core/Classification/HeaderSizeMap.cs` – Console→SkipBytes Mapping
- **FILE-003**: `src/RomCleanup.Contracts/Ports/IHeaderlessHasher.cs` – Port-Interface
- **FILE-004**: `src/RomCleanup.Infrastructure/Hashing/HeaderlessHasher.cs` – Implementierung
- **FILE-005**: `src/RomCleanup.Core/Audit/DatAuditClassifier.cs` – Pure Klassifikationslogik
- **FILE-006**: `src/RomCleanup.Infrastructure/Orchestration/DatAuditPipelinePhase.cs` – Pipeline-Phase
- **FILE-007**: `src/RomCleanup.Core/Audit/DatRenamePolicy.cs` – Pure Rename-Entscheidungslogik
- **FILE-008**: `src/RomCleanup.Infrastructure/Orchestration/DatRenamePipelinePhase.cs` – Pipeline-Phase
- **FILE-009**: `src/RomCleanup.Core/Classification/HeaderAnalyzer.cs` – Extrahierte Header-Analyse

### Geänderte Dateien

- **FILE-010**: `src/RomCleanup.Contracts/Models/DatIndex.cs` – Add() Erweiterung, LookupWithFilename()
- **FILE-011**: `src/RomCleanup.Contracts/Models/RomCandidate.cs` – Hash, DatGameName, DatAuditStatus Properties
- **FILE-012**: `src/RomCleanup.Contracts/Models/RunExecutionModels.cs` – RunOptions + RunResult Erweiterung
- **FILE-013**: `src/RomCleanup.Contracts/Ports/IFileSystem.cs` – RenameItemSafely()
- **FILE-014**: `src/RomCleanup.Infrastructure/Dat/DatRepositoryAdapter.cs` – ROM-Filename-Extraktion
- **FILE-015**: `src/RomCleanup.Infrastructure/Orchestration/EnrichmentPipelinePhase.cs` – Headerless Hash Integration
- **FILE-016**: `src/RomCleanup.Infrastructure/Orchestration/PhasePlanning.cs` – PipelineState, PhasePlanBuilder, StandardPhaseStepActions
- **FILE-017**: `src/RomCleanup.Infrastructure/Orchestration/RunOrchestrator.StandardPhaseSteps.cs` – Neue Phase Steps
- **FILE-018**: `src/RomCleanup.Infrastructure/Orchestration/RunOrchestrator.PreviewAndPipelineHelpers.cs` – DatAudit/Rename Metrics
- **FILE-019**: `src/RomCleanup.Infrastructure/Orchestration/RunResultBuilder.cs` – DatAudit-Metriken aggregieren
- **FILE-020**: `src/RomCleanup.Infrastructure/Orchestration/RunProjection.cs` – Neue Properties
- **FILE-021**: `src/RomCleanup.Infrastructure/FileSystem/FileSystemAdapter.cs` – RenameItemSafely()
- **FILE-022**: `src/RomCleanup.Infrastructure/Reporting/ReportGenerator.cs` – DatAudit-Abschnitt
- **FILE-023**: `src/RomCleanup.Infrastructure/Reporting/RunReportWriter.cs` – DatAudit-Spalten
- **FILE-024**: `src/RomCleanup.CLI/CliOutputWriter.cs` – DatAudit JSON + Summary
- **FILE-025**: `src/RomCleanup.Api/ApiRunResultMapper.cs` – DatAudit-Mapping
- **FILE-026**: `src/RomCleanup.Api/OpenApiSpec.cs` – DatAudit-Felder
- **FILE-027**: `src/RomCleanup.Api/RunManager.cs` – DatAudit-Flags durchreichen
- **FILE-028**: `src/RomCleanup.UI.Wpf/Services/FeatureService.Security.cs` – Delegation an Core/Infrastructure
- **FILE-029**: `src/RomCleanup.UI.Wpf/Models/DashboardProjection.cs` – DatAudit-Display-Properties

### Neue Test-Dateien

- **FILE-030**: `src/RomCleanup.Tests/DatAudit/DatAuditClassifierTests.cs`
- **FILE-031**: `src/RomCleanup.Tests/DatAudit/DatAuditPipelinePhaseTests.cs`
- **FILE-032**: `src/RomCleanup.Tests/DatAudit/DatRenameTests.cs`
- **FILE-033**: `src/RomCleanup.Tests/DatAudit/HeaderlessHasherTests.cs`
- **FILE-034**: `src/RomCleanup.Tests/DatAudit/HeaderSizeMapTests.cs`
- **FILE-035**: `src/RomCleanup.Tests/DatAudit/DatAuditParityTests.cs`
- **FILE-036**: `src/RomCleanup.Tests/DatAudit/FileSystemRenameTests.cs`

---

## 6. Testing

- **TEST-001**: DatAuditClassifier – 5 Status-Pfade (Have, HaveWrongName, Miss, Unknown, Ambiguous) mit je 3 Varianten
- **TEST-002**: DatAuditClassifier – Determinismus: gleicher Input 3× → identischer Output
- **TEST-003**: HeaderSizeMap – alle 4 Konsolen korrekt gemappt, unbekannte Konsole → 0 skip bytes
- **TEST-004**: HeaderlessHasher – NES iNES header (16B skip), SNES copier header (512B skip conditional), Atari 7800 (128B), Atari Lynx (64B)
- **TEST-005**: HeaderlessHasher – Datei ohne Header → normaler Hash als Fallback
- **TEST-006**: DatIndex.LookupWithFilename – Gibt korrektes Tuple zurück, Backward-Compat mit altem Add()
- **TEST-007**: DatRepositoryAdapter – Extrahiert `<rom name="...">` korrekt
- **TEST-008**: DatAuditPipelinePhase – PipelineState.DatAuditResult korrekt gesetzt, kein Dateisystem-Side-Effect
- **TEST-009**: RomCandidate.Hash – Wird in Enrichment korrekt gesetzt, Default ist null
- **TEST-010**: EnrichmentPipelinePhase – Headerless Hash wird für NES/SNES genutzt, normaler Hash für N64/GBA
- **TEST-011**: DatRenamePolicy – Rename/Skip/Conflict/PathTooLong/ExtensionPreserved Pfade
- **TEST-012**: FileSystemAdapter.RenameItemSafely – Path-Traversal-Block, ADS-Block, Reserved Names, MAX_PATH, Conflict-Suffix
- **TEST-013**: DatRenamePipelinePhase – DryRun vs Execute, Audit-Logging, partial failure handling
- **TEST-014**: Audit-Rollback – DatRename → Rollback → Originalnamen wiederhergestellt
- **TEST-015**: Parität – CLI/API/Report zeigen identische DatAudit-Zahlen (min. 3 Szenarien)
- **TEST-016**: Regression – Alle bestehenden Pipeline-Tests bleiben grün nach jeder Phase
- **TEST-017**: HeaderAnalyzer (Core) – alle unterstützten Konsolen per Byte-Array
- **TEST-018**: HeaderRepairService – NES dirty header repair mit Backup-Prüfung
- **TEST-019**: GUI Smoke – DatAudit-Tab rendert, DatRename Preview/Execute Flow

---

## 7. Risks & Assumptions

### Risiken

- **RISK-001**: DatIndex interne Datenstruktur-Änderung (`string → (string, string?)`) bricht bestehende Serialisierung oder Vergleichslogik. *Mitigation*: Overload-Pattern, Regression-Gate.
- **RISK-002**: Headerless Hashing erzeugt falsche Hashes bei ROMs mit unbekanntem Header-Format. *Mitigation*: HeaderSizeMap.GetSkipBytes() gibt 0 für unbekannte Konsolen zurück → Fallback auf normalen Hash.
- **RISK-003**: Performance-Degradation durch doppeltes Hashing (normal + headerless). *Mitigation*: Cache-Reuse, nur für 4 Konsolen aktiv, LRU-Cache in HeaderlessHasher.
- **RISK-004**: SNES-Copier-Header-Erkennung ist nicht 100% zuverlässig (`fileSize % 1024 == 512` ist eine Heuristik). *Mitigation*: Nur ein Hash-Versuch OHNE Header, dann Fallback auf Datei-Hash MIT Header. Kein Datenverlust durch falsche Erkennung.
- **RISK-005**: Rename-Batch mit Konflikten kann zu inkonsistentem Zustand führen. *Mitigation*: Einzelrename-Audit-Trail, Rollback auf Row-Ebene.
- **RISK-006**: RunOptions bekommt viele neue Flags – UI wird unübersichtlich. *Mitigation*: Expert-Modus in GUI, sinnvolle Defaults, CLI-Flags nur bei expliziter Nutzung.

### Annahmen

- **ASSUMPTION-001**: No-Intro DATs verwenden SHA1 als primären Hash-Typ. Die bestehende `hashType = "SHA1"` Default-Konfiguration ist korrekt.
- **ASSUMPTION-002**: Die bestehende `DatIndex.LookupAny()` Determinismus (ordinal-sorted ConsoleKey-Iteration) reicht für Ambiguous-Erkennung.
- **ASSUMPTION-003**: `RomCandidate` bleibt als `sealed class` mit `init`-Properties (kein Builder-Pattern nötig).
- **ASSUMPTION-004**: Die bestehende `AuditCsvStore.Rollback()`-Logik funktioniert korrekt für DatRename-Operationen (gleicher CSV-Format, gleiche Rollback-Semantik).
- **ASSUMPTION-005**: Die 4 Header-Konsolen (NES, SNES, Atari 7800, Atari Lynx) decken den Großteil der No-Intro-inkompatiblen ROMs ab. Weitere Konsolen können später ergänzt werden.
- **ASSUMPTION-006**: WPF-Extraktion (Phase 6) kann ohne Breaking Change für bestehende GUI-Nutzer erfolgen – Verhalten bleibt identisch, nur der Code-Ort ändert sich.

---

## 8. Related Specifications / Further Reading

- [Epic-Review (Konversation)](.) – NO-GO-Urteil, 4-Epic-Split-Empfehlung
- [Fachkonzept (Konversation)](.) – Formale Definitionen Have/HaveWrongName/Miss/Unknown/Ambiguous
- [Zielarchitektur (Konversation)](.) – DatAudit als eigene Phase, DatRename nach Deduplicate
- [Conversion Engine Plan](plan/feature-conversion-engine-1.md) – Referenz für Pipeline-Erweiterungsmuster
- [No-Intro DAT-o-Matic](https://datomatic.no-intro.org/) – DAT-Datenquelle und Hash-Referenz
- [copilot-instructions.md](.github/copilot-instructions.md) – Projektweite Architektur- und Sicherheitsregeln
