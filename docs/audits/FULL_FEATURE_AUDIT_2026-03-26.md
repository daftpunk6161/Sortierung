# Full Feature Audit & Rehabilitation Plan – Romulus

**Datum:** 2026-03-26  
**Auditor:** Feature-Audit (Architect Mode)  
**Scope:** Gesamter Feature-Bestand in `src/` (Contracts, Core, Infrastructure, CLI, API, GUI)  
**Methodik:** Tiefenanalyse aller Schichten, Cross-Reference GUI/CLI/API-Parität, Code-Level-Prüfung

---

## 1. Executive Verdict

### Gesamtzustand: **Funktional, aber mit erheblichem Ballast und versteckten Lücken**

Romulus hat einen **soliden Kern** (GameKey, Region, Scoring, Deduplication, Pipeline) und eine **ambitionierte GUI**. Die Architektur ist grundsätzlich sauber geschichtet. Die Sicherheitsmaßnahmen (Path Traversal, CSV Injection, Tool-Hash-Verifizierung, HMAC Audit Signing) sind ernst genommen.

**Hauptprobleme:**

1. **Feature-Inflation ohne Konsolidierung:** 73+ GUI-Commands, davon 6 Stubs/Experimentell, 4 verwaiste Infrastructure-Services, 10+ Features die nur in Preview/Analyse existieren aber nie in die Pipeline integriert wurden
2. **Entry-Point-Divergenz:** CLI hat kein Rollback, API hat kein JSONL-Logging, GUI hat Features die CLI/API nie sehen (100+ FeatureCommands), Validierung ist inkonsistent
3. **Verwaiste Services:** InsightsEngine, CatchGuardService, RunHistoryService, ScanIndexService — gebaut, getestet, aber nie in Produktion verdrahtet
4. **Konversions-Pipeline Duplikation:** ConvertOnlyPipelinePhase und WinnerConversionPipelinePhase enthalten fast identische Konvertierungslogik
5. **Experimentelle Features als fertig präsentiert:** GPU Hashing, Parallel Hashing, FTP Source, Plugin System, Cloud Sync, Cron Auto-Execute — im UI als Buttons sichtbar, aber nicht funktional

### Wichtigste Risiken:

| # | Risiko | Schwere |
|---|--------|---------|
| R1 | CLI erlaubt Symlinks/Junctions als Roots, API blockt sie → inkonsistentes Sicherheitsmodell | **P0** |
| R2 | API Idempotency-Fingerprint fehlt EnableDatAudit/EnableDatRename → stille Duplikate | **P0** |
| R3 | 6 GUI-Features präsentieren sich als funktional, sind aber Stubs → Nutzerverwirrung | **P1** |
| R4 | ConvertOnly hat kein Set-Member-Tracking → orphaned BIN/TRACK bei CUE-Konvertierung | **P1** |
| R5 | DatRename hat keine Konfliktlösung → stille Übersprünge ohne Audit | **P1** |
| R6 | CLI hat keinen Rollback → irreversible Move-Operationen | **P1** |

### Kurzfazit:

Der Kern ist **release-fähig** mit gezielten Fixes (R1–R6). Der Feature-Ballast (verwaiste Services, Stub-Commands, experimentelle Features) muss **entweder fertiggebaut oder sauber deaktiviert** werden. Die Konversions-Duplikation und die verwaisten Services erzeugen **Wartungsschulden** die vor v1.0 bereinigt werden sollten.

---

## 2. Vollständiges Feature-Inventar

### 2.1 Core Features (Pipeline-Kern)

| # | Feature | Bereich | Beschreibung | Status |
|---|---------|---------|-------------|--------|
| C01 | File Scanning | Core/Pipeline | Streaming-Scan mit IAsyncEnumerable, Extension-Filter, Blocklist | ✅ Vollständig |
| C02 | GameKey Normalization | Core/GameKeys | ASCII-Fold, Tag-Stripping, Alias-Maps, Deterministic Fallback | ✅ Vollständig |
| C03 | Region Detection | Core/Regions | 40+ Regex-Rules, Token-Parsing, Fallback-Chain | ✅ Vollständig |
| C04 | File Classification | Core/Classification | BIOS/Junk/NonGame/Game Categorization, 8 Regex-Pattern | ✅ Vollständig |
| C05 | Console Detection | Core/Classification | Multi-Signal (Extension, Folder, Header), LRU-Cache | ✅ Vollständig |
| C06 | Format Scoring | Core/Scoring | Extension-Score 300–900, Region-Score 0–1000 | ✅ Vollständig |
| C07 | Version Scoring | Core/Scoring | Verified [!], Revision, Version-Parse, Language-Bonus | ✅ Vollständig |
| C08 | Completeness Scoring | Core/Scoring | CUE/GDI/CCD Set-Integrität, DAT-Match-Bonus | ✅ Vollständig |
| C09 | Deduplication Engine | Core/Deduplication | Multi-Criteria Winner Selection, deterministische Sortierung | ✅ Vollständig |
| C10 | Rule Engine | Core/Rules | User-definierte Classification Rules, Priority-basiert | ✅ Vollständig |
| C11 | Set Parsing | Core/SetParsing | CUE/GDI/CCD/MDS/M3U Member-Resolution | ✅ Vollständig |
| C12 | Safe Regex | Core | Timeout-Safe Wrapper gegen ReDoS | ✅ Vollständig |
| C13 | LRU Cache | Core/Caching | Thread-safe, O(1), bounded eviction | ✅ Vollständig |

### 2.2 Recognition & Detection Features

| # | Feature | Bereich | Beschreibung | Status |
|---|---------|---------|-------------|--------|
| D01 | Header Analysis | Core/Classification | Cartridge + Disc Header Detection | ✅ Vollständig |
| D02 | Content Signature Classification | Core/Classification | Magic-Byte-basierte Formaterkennung | ✅ Vollständig |
| D03 | Hypothesis Resolution | Core/Classification | Multi-Signal Confidence Merging | ✅ Vollständig |
| D04 | Aggressive Junk Detection | Core/Classification | WIP/Dev-Build zusätzliche Pattern | ✅ Vollständig |

### 2.3 Conversion Features

| # | Feature | Bereich | Beschreibung | Status |
|---|---------|---------|-------------|--------|
| V01 | Conversion Graph | Core/Conversion | Dijkstra Shortest Path, Multi-Step Chains | ✅ Vollständig |
| V02 | Conversion Planner | Core/Conversion | Policy + Safety + Integrity → ConversionPlan | ✅ Vollständig |
| V03 | Conversion Executor | Infra/Conversion | Multi-Step Execution, Intermediate Cleanup | ✅ Vollständig |
| V04 | chdman Invoker | Infra/ToolInvokers | createcd/createdvd, Auto-Downgrade, Verify | ✅ Vollständig |
| V05 | dolphintool Invoker | Infra/ToolInvokers | GC/Wii → RVZ, Hardcoded Compression Params | ⚠️ Funktional, nicht konfigurierbar |
| V06 | 7-Zip Invoker | Infra/ToolInvokers | Cartridge → ZIP Archivierung | ✅ Vollständig |
| V07 | psxtract Invoker | Infra/ToolInvokers | PBP → CHD (PS1/PSP) | ⚠️ Schwache Verifizierung (nur File-Exists) |
| V08 | PBP Encryption Detection | Infra/Conversion | PSP DATA Section Byte-Check | ✅ Vollständig |
| V09 | Conversion Policy Evaluation | Core/Conversion | Console-spezifische Policies, Hard-Overrides | ✅ Vollständig |
| V10 | Source Integrity Classification | Core/Conversion | Lossless/Lossy/Unknown Mapping | ✅ Vollständig |
| V11 | Winner Conversion Phase | Infra/Orchestration | Post-Dedupe Conversion | ✅ Vollständig |
| V12 | ConvertOnly Phase | Infra/Orchestration | Standalone Conversion (ohne Dedupe) | ⚠️ Kein Set-Member-Tracking |
| V13 | Conversion Registry | Infra/Conversion | JSON-basierte Capability-Datenbank | ✅ Vollständig |

### 2.4 Sorting / Move / Restore Features

| # | Feature | Bereich | Beschreibung | Status |
|---|---------|---------|-------------|--------|
| S01 | Move Pipeline Phase | Infra/Orchestration | Loser → Trash mit Set-Member Co-Move | ✅ Vollständig |
| S02 | Junk Removal Phase | Infra/Orchestration | Standalone-Junk → eigenen Trash | ✅ Vollständig |
| S03 | Console Sorting | Infra/Sorting | Dateien → Console-Subfolder, SortDecision-Routing | ✅ Vollständig |
| S04 | ZIP Sorting | Infra/Sorting | ZIP-Inhalt-basierte PS1/PS2 Sortierung | ⚠️ Mixed-Content-ZIPs fragil |
| S05 | DAT Rename | Infra/Orchestration | HaveWrongName → korrekter Dateiname | ⚠️ Keine Konfliktlösung |
| S06 | Rollback / Undo | Infra/Audit | HMAC-signierter Audit Trail → Reversal | ✅ Vollständig (GUI+API) |
| S07 | Conflict Policy | Infra/FileSystem | Rename/Skip/Overwrite DUP-Handling | ✅ Vollständig |

### 2.5 DAT / Hashing / Verification Features

| # | Feature | Bereich | Beschreibung | Status |
|---|---------|---------|-------------|--------|
| H01 | File Hashing | Infra/Hashing | SHA1/SHA256/MD5/CRC32, LRU-Cache | ✅ Vollständig |
| H02 | CHD Header Hash Extraction | Infra/Hashing | CHD v5 Raw-SHA1 ohne Full-Read | ✅ Vollständig |
| H03 | Archive Hashing | Infra/Hashing | ZIP in-memory, 7z via Temp-Extraction | ⚠️ 7z Temp Zip-Slip Risiko |
| H04 | Headerless Hashing | Infra/Hashing | iNES/SNES/7800/Lynx Header-Skip | ✅ Vollständig |
| H05 | Header Repair | Infra/Hashing | iNES Header-Zeroing, SNES Copier-Removal | ⚠️ Begrenzte Konsolenabdeckung |
| H06 | Parallel Hashing | Infra/Hashing | Task-basiert, Auto-Throttle | ✅ Vollständig |
| H07 | DAT Index | Contracts/Models | ConcurrentDictionary, Cross-Console Lookup | ✅ Vollständig |
| H08 | DAT Audit Phase | Infra/Orchestration | Hash → Have/Miss/Ambiguous Classification | ✅ Vollständig |
| H09 | DAT Audit Classifier | Core/Audit | Headerless-First, Filename-Vergleich | ✅ Vollständig |
| H10 | DAT Source Service | Infra/Dat | XML Streaming Parse, Index-Aufbau | ✅ Vollständig |

### 2.6 Reporting / Audit / Benchmark Features

| # | Feature | Bereich | Beschreibung | Status |
|---|---------|---------|-------------|--------|
| R01 | HTML Report | Infra/Reporting | Styled Table, HTML-Escaped, Summary Stats | ✅ Vollständig |
| R02 | CSV Report | Infra/Reporting | RFC 4180, Injection-Prevention | ✅ Vollständig |
| R03 | JSON Report | Infra/Reporting | Structured Serialization | ✅ Vollständig |
| R04 | Audit CSV Trail | Infra/Audit | Per-File Append, HMAC-Signed Sidecar | ✅ Vollständig |
| R05 | JSONL Structured Logging | Infra/Logging | Rotation, GZIP, Correlation-ID | ✅ Vollständig (nur CLI) |
| R06 | Phase Metrics | Infra/Metrics | Per-Phase Timing, Item Counts | ✅ Vollständig |
| R07 | Run Projection | Infra/Orchestration | Channel-neutral Metrics für GUI/CLI/API | ✅ Vollständig |
| R08 | Benchmark Infrastructure | Tests/Benchmark | Ground Truth, Baseline, Regression Gates, Quality Gates | ✅ Vollständig |

### 2.7 GUI-exklusive Features

| # | Feature | Bereich | Beschreibung | Status |
|---|---------|---------|-------------|--------|
| G01 | 5-Area Navigation Shell | UI.Wpf | MissionControl/Library/Config/Tools/System | ✅ Vollständig |
| G02 | Command Palette (Ctrl+K) | UI.Wpf | Fuzzy Search, 73 Commands | ✅ Vollständig |
| G03 | First-Run Wizard | UI.Wpf | Guided Setup, Region Selection | ✅ Vollständig |
| G04 | Inspector Context Wing | UI.Wpf | Selected ROM Detail mit Scores | ✅ Vollständig |
| G05 | DAT Audit View | UI.Wpf | Read-Only Table, Filter, Status-Counts | ✅ Vollständig |
| G06 | Conversion Preview | UI.Wpf | Pre-Convert Items, Safety Rating | ✅ Vollständig |
| G07 | Extension/Console Filters | UI.Wpf | Grouped Checkboxes, Presets | ✅ Vollständig |
| G08 | Region Priority Drag-Reorder | UI.Wpf | Drag-Drop Ranking | ✅ Vollständig |
| G09 | Danger Confirm Dialog | UI.Wpf | Type-to-Confirm für destruktive Aktionen | ✅ Vollständig |
| G10 | Preset Quick Actions | UI.Wpf | SafeDryRun/FullSort/Convert Shortcuts | ✅ Vollständig |
| G11 | ScottPlot Charts | UI.Wpf | Console Distribution Pie, Keep/Move/Junk Bar | ✅ Vollständig |
| G12 | Shortcut Sheet (F1) | UI.Wpf | Keyboard Reference Overlay | ✅ Vollständig |
| G13 | Auto-Save Settings | UI.Wpf | 2s Debounce, JSON-Persist | ✅ Vollständig |
| G14 | Watch Mode (Ctrl+W) | UI.Wpf | FileSystemWatcher-basiertes Auto-Trigger | ✅ Vollständig |
| G15 | Move Gate Checks | UI.Wpf | Pre-Move Validation Checklist | ✅ Vollständig |

### 2.8 CLI-exklusive Features

| # | Feature | Bereich | Beschreibung | Status |
|---|---------|---------|-------------|--------|
| L01 | Argument Parser | CLI | 30+ Flags, Validation, Usage Help | ✅ Vollständig |
| L02 | Settings Merge | CLI | CLI-Flags → settings.json Overlay | ✅ Vollständig |
| L03 | DryRun JSON Output | CLI | Structured CliDryRunOutput für Machine-Parsing | ✅ Vollständig |
| L04 | Non-Interactive Guard | CLI | --yes Required für Move bei stdin Redirect | ✅ Vollständig |
| L05 | Exit Code System | CLI | 0/1/2/3 für Success/Error/Cancelled/Preflight | ✅ Vollständig |

### 2.9 API-exklusive Features

| # | Feature | Bereich | Beschreibung | Status |
|---|---------|---------|-------------|--------|
| A01 | REST Endpoints | API | 7 Endpoints (health, runs CRUD, stream, rollback) | ✅ Vollständig |
| A02 | Idempotency | API | SHA256 Fingerprint + X-Idempotency-Key | ⚠️ Fingerprint unvollständig |
| A03 | SSE Progress Stream | API | Live Progress Events + Heartbeat | ✅ Vollständig |
| A04 | Rate Limiting | API | Per-Client Fixed-Window | ✅ Vollständig |
| A05 | Client Binding | API | X-Client-Id Ownership | ✅ Vollständig |
| A06 | Loopback Security | API | Default 127.0.0.1, --AllowInsecureNetwork | ✅ Vollständig |
| A07 | Run Lifecycle Management | API | Max 100 Runs, Auto-Evict, Recovery State | ✅ Vollständig |
| A08 | OpenAPI Spec | API | Embedded 3.0.3 JSON | ✅ Vollständig |

### 2.10 Settings / Filters / UX Features

| # | Feature | Bereich | Beschreibung | Status |
|---|---------|---------|-------------|--------|
| X01 | Settings Loader | Infra/Configuration | defaults.json + %APPDATA% + Env-Override | ✅ Vollständig |
| X02 | Safety Profiles | Infra/Safety | Conservative/Balanced/Expert | ✅ Vollständig |
| X03 | Tool Discovery | Infra/Tools | ProgramFiles-First, PATH-Fallback, Hash-Verify | ✅ Vollständig |
| X04 | Path Safety | Infra/FileSystem | Traversal-Guard, Reparse-Block, DUP-Suffix | ✅ Vollständig |
| X05 | Artifact Path Resolver | Infra/Paths | SHA256-Fingerprint Multi-Root Artifacts | ✅ Vollständig |
| X06 | Quarantine Candidate Detection | Infra/Quarantine | Rule-based Flagging (analyse-only) | ✅ Vollständig |

### 2.11 Quality / Safety / CI Features

| # | Feature | Bereich | Beschreibung | Status |
|---|---------|---------|-------------|--------|
| Q01 | Benchmark Regression Gates | Tests | Baseline ± Tolerance, Anti-Gaming | ✅ Vollständig |
| Q02 | Ground Truth Comparison | Tests | Expected ↔ Actual Scoring | ✅ Vollständig |
| Q03 | Quality Gates | Tests | M1–M7 Metrics, FalseConfidenceRate | ✅ Vollständig |
| Q04 | Pipeline Invariant Tests | Tests | Phase Ordering, Set-Member, Determinism | ✅ Vollständig |
| Q05 | Security Tests | Tests | Path Traversal, Injection, Tool Security | ✅ Vollständig |

### 2.12 Accessibility / Themes

| # | Feature | Bereich | Beschreibung | Status |
|---|---------|---------|-------------|--------|
| T01 | 6 Themes | UI.Wpf/Themes | Synthwave, CleanDark, ArcadeNeon, RetroCRT, HighContrast, Light | ✅ Vollständig |
| T02 | Design Tokens | UI.Wpf/Themes | Brush/Font/Spacing Central | ✅ Vollständig |
| T03 | Theme Toggle | UI.Wpf | Ctrl+T Runtime-Switch | ✅ Vollständig |

### 2.13 Experimental / Stub / Partial Features

| # | Feature | Bereich | Beschreibung | Status |
|---|---------|---------|-------------|--------|
| E01 | GPU Hashing | UI.Wpf | Setzt Env-Var, kein Backend | ❌ **Stub** |
| E02 | Parallel Hashing Toggle | UI.Wpf | Setzt Env-Var, kein Backend | ❌ **Stub** |
| E03 | FTP Source | UI.Wpf | URL-Validierung, kein Download | ❌ **Stub** |
| E04 | Plugin Manager | UI.Wpf | Manifest-Parsing, kein Plugin-Laden | ❌ **Stub** |
| E05 | Cloud Sync | UI.Wpf | OneDrive/Dropbox Detection, kein Sync | ❌ **Stub** |
| E06 | Cron Auto-Execute | UI.Wpf | Expression Validation, keine Ausführung | ❌ **Stub** |

### 2.14 Verwaiste / Nicht-integrierte Services

| # | Service | Bereich | Beschreibung | Status |
|---|---------|---------|-------------|--------|
| O01 | InsightsEngine | Infra/Analytics | Dashboard-Analytik | ❌ **Verwaist** (nur Tests) |
| O02 | CatchGuardService | Infra/Diagnostics | Error-Klassifikation + Logging | ❌ **Verwaist** (nur Tests) |
| O03 | RunHistoryService | Infra/History | Run-Verlauf Browser | ❌ **Verwaist** (nur Tests) |
| O04 | ScanIndexService | Infra/History | Scan-Fingerprint-Index | ❌ **Verwaist** (nur Tests) |

---

## 3. Tiefenanalyse pro Feature-Bereich

### 3.1 Core Pipeline (C01–C13)

**Sauber:** GameKey, Region, Scoring, Deduplication — die Kernlogik ist deterministisch, gut getestet, und architektonisch korrekt in Core platziert. Keine I/O-Abhängigkeiten in der Logik selbst (CompletenessScorer ist der einzige Grenzfall: ruft SetParsers auf, die I/O abstrahieren).

**Fragwürdig:** Keine.

**Kaputt/Unvollständig:** Keine.

**Redundant:** Keine.

**Release-Risiko:** Keine.

**Bewertung: ✅ Release-fähig.**

### 3.2 Recognition & Detection (D01–D04)

**Sauber:** FileClassifier, ConsoleDetector, HeaderAnalyzer, HypothesisResolver — multi-signal detection mit Confidence-Scoring.

**Fragwürdig:** ContentSignatureClassifier hat begrenzten Magic-Byte-Katalog. Fehlende Formate werden als Unknown klassifiziert — akzeptabel.

**Release-Risiko:** Keine.

**Bewertung: ✅ Release-fähig.**

### 3.3 Conversion (V01–V13)

**Sauber:** ConversionGraph (Dijkstra), ConversionPlanner, ConversionExecutor, chdman/7z Invoker.

**Fragwürdig:**
- **V05 dolphintool:** Compression-Parameter (zstd level 5, 128KB blocks) sind hardcoded, nicht konfigurierbar. Nutzer können Speed/Compression Tradeoff nicht steuern.
- **V07 psxtract:** Verifizierung prüft nur File-Existenz, nicht CHD-Integrität oder Magic-Bytes. Stille Korruption möglich.
- **V12 ConvertOnly:** Hat **kein Set-Member-Tracking**. Wenn eine CUE-Datei konvertiert wird, werden BIN/TRACK-Members nicht mitbewegt. Bei Standalone-Konvertierung können orphaned Files entstehen.

**Redundant:** ConvertOnlyPipelinePhase und WinnerConversionPipelinePhase haben **fast identische Konvertierungsschleifen**. Die gemeinsame Logik sollte in einen Shared Helper extrahiert werden.

**Release-Risiko:**
- **V12 fehlende Set-Member Co-Move bei ConvertOnly → P1** (Dateninkonsistenz)
- **V07 schwache Verifizierung → P2** (stille Korruption bei PBP→CHD)

**Bewertung: ⚠️ Funktional, aber mit bekannten Lücken (V12, V07).**

### 3.4 Sorting / Move / Restore (S01–S07)

**Sauber:** MovePipelinePhase (Write-Ahead Audit, Set-Member Co-Move), JunkRemovalPhase, ConsoleSorter (SortDecision-Routing), Rollback (HMAC-signiert).

**Fragwürdig:**
- **S04 ZipSorter:** Bei Mixed-Content ZIPs (PS1+PS2 Dateien) gewinnt der erste Match. Keine Warnung an Nutzer.
- **S05 DatRename:** Wenn der Zieldateiname bereits existiert, wird der Rename **stillschweigend übersprungen** — kein DUP-Suffix, kein Audit-Eintrag, keine Nutzerwarnung. Das ist ein verlorenes Feature für den Nutzer.

**Kaputt:**
- **S06 Rollback fehlt in CLI** komplett. CLI-Nutzer die `--mode Move` nutzen haben **keinen Weg zur Umkehr** außer manuellem File-Verschieben. Dies verletzt die Projekt-Rule "Kein Datenverlust / Undo-fähiges Verhalten".

**Release-Risiko:**
- **S06 CLI ohne Rollback → P1** (Datenverlust-Risiko für CLI-Nutzer)
- **S05 DatRename ohne Konflikterkennung → P1** (stille Übersprünge)

**Bewertung: ⚠️ Kern funktional, CLI-Rollback ist Release-Blocker.**

### 3.5 DAT / Hashing (H01–H10)

**Sauber:** FileHashService (LRU-Cache, CHD Header-Extraction), HeaderlessHasher, DatAuditClassifier, DatSourceService.

**Fragwürdig:**
- **H03 ArchiveHashService:** 7z-Temp-Extraction hat Zip-Slip-Risiko. Der Temp-Pfad wird zwar validiert, aber nur gegen den Temp-Root — nicht gegen die extrahierten Eintrags-Pfade.
- **H05 HeaderRepairService:** Nur iNES und SNES Copier. Keine GBC, N64, Genesis/MegaDrive Header-Reparatur. Begrenzt, aber dokumentiert.

**Release-Risiko:**
- **H03 7z Zip-Slip → P1** (Sicherheitslücke)

**Bewertung: ⚠️ Funktional, H03 Sicherheitslücke muss geprüft werden.**

### 3.6 Reporting / Audit / Benchmark (R01–R08)

**Sauber:** HTML/CSV/JSON Reports mit korrektem Escaping, Audit CSV mit HMAC, Benchmark-Infrastruktur mit Quality Gates.

**Fragwürdig:**
- **R05 JSONL Logging:** Existiert und funktioniert, aber **nur über CLI erreichbar**. API und GUI haben keinen Zugang zu strukturiertem Logging.
- **R07 RunProjection HealthScore:** Magic Constants in der Formel nicht dokumentiert. Änderungen daran sind riskant.

**Redundant:** Keine.

**Release-Risiko:** Keine Blocker.

**Bewertung: ✅ Release-fähig.**

### 3.7 GUI-exklusive Features (G01–G15)

**Sauber:** Navigation Shell, Command Palette, Inspector, DAT Audit View, Themes, Danger Confirm, Watch Mode — alles sauber MVVM, kein Business Logic in Code-Behind.

**Fragwürdig:**
- 73 Feature Commands im Command Palette sind **ambitioniert**. Davon sind 6 echte **Stubs** (E01–E06) die dem Nutzer als funktionale Buttons präsentiert werden. Das erzeugt **False Expectations**.
- Watch Mode ist funktional, aber ohne automatische Validierung oder Dry-Run vor dem Auto-Execute. Nutzer könnten versehentlich destruktive Operationen auslösen.

**Release-Risiko:**
- **Stub-Commands als funktional präsentiert → P1** (Nutzerverwirrung)

**Bewertung: ⚠️ Kern exzellent, Stubs müssen deaktiviert oder markiert werden.**

### 3.8 CLI (L01–L05)

**Sauber:** Argument Parser, Settings Merge, DryRun JSON, Exit Codes.

**Kaputt:**
- **Kein Rollback.** CLI hat keine `--rollback` Flag. Audit-Dateien werden geschrieben, aber CLI kann sie nicht lesen.
- **Symlink/Junction als Root erlaubt** — API blockt sie, CLI nicht. Inkonsistentes Sicherheitsmodell.

**Release-Risiko:**
- **CLI ohne Rollback → P1**
- **CLI Symlink-Root erlaubt → P0** (Security)

**Bewertung: ⚠️ Funktional, aber Security-Lücke und fehlender Rollback.**

### 3.9 API (A01–A08)

**Sauber:** Run Lifecycle, Rate Limiting, SSE Progress, Loopback Security, Client Binding.

**Kaputt:**
- **A02 Idempotency-Fingerprint:** Fehlt EnableDatAudit und EnableDatRename im SHA256-Hash. Zwei Requests mit unterschiedlichen DAT-Audit/Rename-Flags werden als identisch behandelt → **Silent Bug**.

**Fragwürdig:**
- Kein JSONL Logging Support (CLI hat es, API nicht).
- Move-Mode ohne explizite Bestätigung (CLI benötigt `--yes`, API nicht).

**Release-Risiko:**
- **Idempotency Bug → P0** (stille Duplikate / falsches Ergebnis)

**Bewertung: ⚠️ Kern robust, Idempotency-Bug ist P0.**

### 3.10 Verwaiste Features (O01–O04) und Stubs (E01–E06)

**InsightsEngine:** Gebaut für Dashboard-Analytik (Health, Heatmap, DuplicateInspector). Code existiert, Tests existieren, wird nie in der Pipeline oder GUI direkt instanziiert. Stattdessen bietet FeatureService.Analysis identische Funktionalität nochmals an.

**CatchGuardService:** Error-Klassifikation und strukturiertes Error-Logging. Gebaut, getestet, nie in SharedServiceRegistration oder Orchestrator verdrahtet. Errors werden direkt über Orchestrator-Catch-Blöcke gehandhabt.

**RunHistoryService:** Run-Verlauf Browser. Code-Kommentar sagt explizit "[v2.1 deferred] Not wired into RunOrchestrator pipeline yet." Explizit auf spätere Version verschoben.

**ScanIndexService:** Fingerprint-basierter Scan-Cache für Performance-Optimierung. Nie integriert.

**Bewertung: Diese 4 Services sind tote Wartungslast. Sie sollten entweder integriert oder entfernt werden.**

**Stubs (E01–E06):** GPU Hashing, Parallel Hashing Toggle, FTP Source, Plugin Manager, Cloud Sync, Cron Auto-Execute — alle registriert als UI-Commands, aber ohne funktionales Backend. Sie zeigen dem Nutzer Buttons und Dialog-Texte wie "experimentell" oder "in Planung", was Verwirrung erzeugt.

**Bewertung: Deaktivieren oder aus dem UI entfernen bis funktional.**

---

## 4. Einzelbewertung pro Feature

### P0 — Release-Blocker

#### Feature: API Idempotency Fingerprint (A02)
- **Zweck:** Duplikat-Erkennung für API-Requests
- **Nutzen:** Zentral für zuverlässige API-Nutzung
- **Implementierungsstand:** 95% — fehlt nur EnableDatAudit + EnableDatRename
- **Fachliche Korrektheit:** ❌ Falsch — zwei unterschiedliche Requests werden als gleich behandelt
- **Technische Korrektheit:** Code funktioniert, aber Hash-Input ist unvollständig
- **Integrationsqualität:** Gut in RunLifecycleManager integriert
- **Testlage:** Tests existieren, prüfen aber den Fehlerfall nicht
- **Architekturqualität:** Korrekt platziert
- **Probleme:** EnableDatAudit und EnableDatRename fehlen im BuildRequestFingerprint
- **Entscheidung:** **Behalten, aber sofort reparieren — Release-Blocker**

#### Feature: CLI Reparse-Point Validation
- **Zweck:** Symlinks/Junctions als Root-Pfade blockieren
- **Nutzen:** Sicherheitskritisch
- **Implementierungsstand:** ❌ Fehlt in CLI, vorhanden in API
- **Fachliche Korrektheit:** Inkonsistent zwischen Entry Points
- **Entscheidung:** **Release-Blocker — CLI muss API-Level Validierung bekommen**

### P1 — Schwere Probleme

#### Feature: CLI Rollback (fehlend)
- **Zweck:** Undo von Move-Operationen
- **Nutzen:** **Zentral** — Projekt-Rule "Kein Datenverlust / Undo-fähig" verlangt es
- **Implementierungsstand:** 0% in CLI, 100% in GUI+API
- **Entscheidung:** **Muss vor Release implementiert werden (--rollback Flag)**

#### Feature: ConvertOnly Set-Member Tracking (V12)
- **Zweck:** Bei CUE-Konvertierung BIN/TRACK Files mitführen
- **Nutzen:** Verhindert Waisendateien
- **Implementierungsstand:** In MovePipelinePhase korrekt, in ConvertOnlyPipelinePhase **fehlend**
- **Entscheidung:** **Behalten, aber reparieren**

#### Feature: DatRename Konflikterkennung (S05)
- **Zweck:** Rename-Konflikte erkennen und behandeln
- **Nutzen:** Verhindert stille Feature-Verluste
- **Implementierungsstand:** Rename funktioniert, Konflikt-Handling fehlt
- **Entscheidung:** **Behalten, aber Konfliktlösung (DUP-Suffix oder Nutzerwarnung) hinzufügen**

#### Feature: Conversion Phase Duplikation (V11/V12)
- **Zweck:** Zwei fast identische Konvertierungsschleifen
- **Nutzen:** Wartungsrisiko
- **Implementierungsstand:** Beide funktional, aber dupliziert
- **Entscheidung:** **Konsolidieren — gemeinsame Konvertierungslogik extrahieren**

#### Feature: Stub-Commands im UI (E01–E06)
- **Zweck:** Zukunfts-Features
- **Nutzen:** Aktuell keiner — verwirrt Nutzer
- **Implementierungsstand:** Buttons sichtbar, Backend fehlt
- **Entscheidung:** **Deaktivieren oder aus Command-Palette entfernen bis funktional**

### P2 — Relevante Mängel

#### Feature: 7z Archive Hash Zip-Slip (H03)
- **Zweck:** Hash-Extraction aus 7z-Archiven
- **Nutzen:** DAT-Matching für 7z-Dateien
- **Implementierungsstand:** Funktional, aber Temp-Extraction ohne Entry-Path-Validierung
- **Entscheidung:** **Behalten, aber Entry-Path-Validation hinzufügen**

#### Feature: psxtract Verifizierung (V07)
- **Zweck:** PBP → CHD Konvertierung
- **Implementierungsstand:** Konvertierung funktioniert, Verifizierung prüft nur File-Exists
- **Entscheidung:** **Behalten, aber CHD-Magic-Byte-Check als Verifizierung hinzufügen**

#### Feature: dolphintool Compression Hardcoded (V05)
- **Zweck:** GC/Wii → RVZ
- **Implementierungsstand:** Funktional, Parameter nicht konfigurierbar
- **Entscheidung:** **Behalten — konfigurierbar machen ist P3, nicht P2**

#### Feature: JSONL Logging nur in CLI (R05)
- **Zweck:** Strukturiertes Logging
- **Implementierungsstand:** CLI hat es, API und GUI nicht
- **Entscheidung:** **Behalten, API-Support ist P2 für Observability**

#### Feature: API Move ohne Confirmation (A01)
- **Implementierungsstand:** Move wird ohne Bestätigung akzeptiert
- **Entscheidung:** **Behalten, aber Documentation Warning hinzufügen**

#### Feature: RunResultBuilder manuelles Feld-Mapping
- **Zweck:** 40+ Properties manuell kopiert
- **Implementierungsstand:** Funktional, aber Drift-anfällig
- **Entscheidung:** **Behalten — Konsolidierung ist P3**

### P3 — Nachrangige Punkte

#### Feature: Verwaiste Services (O01–O04)
- **InsightsEngine:** Funktionalität existiert parallel in FeatureService.Analysis
- **CatchGuardService:** Errors werden direkt gehandhabt
- **RunHistoryService:** Explizit auf v2.1 verschoben
- **ScanIndexService:** Performance-Feature für Folge-Scans
- **Entscheidung:** **InsightsEngine und CatchGuardService entfernen (redundant). RunHistory und ScanIndex als v2.1 markieren und in docs/DEFERRED_FEATURES.md dokumentieren.**

#### Feature: ZipSorter Mixed-Content (S04)
- **Entscheidung:** **Behalten — Edge Case dokumentieren**

#### Feature: DatRename Rename ohne Audit (S05)
- **Entscheidung:** **Fällt unter P1 (Konflikterkennung)**

#### Feature: HealthScore Magic Constants (R07)
- **Entscheidung:** **Behalten — Konstanten dokumentieren**

---

## 5. Kritische Problemklassen

### 5.1 Features, die still kaputt sind
1. **API Idempotency:** EnableDatAudit/EnableDatRename fehlen im Fingerprint → stille Request-Duplikate
2. **DatRename Konflikte:** Stille Übersprünge ohne Audit Trail oder Nutzerwarnung

### 5.2 Features, die nur halb existieren
1. **CLI Rollback:** Audit wird geschrieben, kann aber über CLI nicht gelesen werden
2. **ConvertOnly Set-Member:** Konvertierung funktioniert, Set-Members werden ignoriert
3. **GPU Hashing, Parallel Hashing Toggle:** Env-Var wird gesetzt, kein Backend reagiert darauf
4. **FTP Source:** URL wird validiert, Download fehlt komplett
5. **Plugin Manager:** Manifest wird geparst, Plugins werden nicht geladen
6. **Cloud Sync:** Storage-Provider Detection, kein Sync
7. **Cron Auto-Execute:** Expression wird validiert, keine Auto-Ausführung

### 5.3 Features mit Schattenlogik
1. **InsightsEngine vs FeatureService.Analysis:** Beide berechnen HealthScore, DuplicateHeatmap, DuplicateInspector. Ein Codepfad wird nie genutzt (InsightsEngine).
2. **ConvertOnlyPipelinePhase vs WinnerConversionPipelinePhase:** Fast identische Konvertierungsschleifen mit unterschiedlichem Set-Member-Handling.

### 5.4 Features mit schlechter oder fehlender Testlage
1. **API Idempotency-Fingerprint:** Kein Test prüft ob alle Request-Felder im Fingerprint enthalten sind
2. **ConvertOnly Set-Member:** Kein Test prüft ob BIN/TRACK-Members nach CUE-Konvertierung noch vorhanden sind
3. **DatRename Konflikte:** Kein Test prüft Verhalten bei existierendem Zieldateinamen

### 5.5 Features, die unnötige Komplexität erzeugen
1. **73 Feature Commands:** Nur ~50 davon sind echte, funktionale Features. 6 sind Stubs, ~10 sind Randfeatures mit fraglichem Nutzen (PlaytimeTracker, GenreClassification, NAS Optimization)
2. **4 verwaiste Services** erzeugen Wartungslast ohne Gegenwert
3. **FeatureService hat 8 Partial-Dateien** mit 70+ Methoden — monolithisch trotz Partials

### 5.6 Features, die Release-Reife verhindern
1. **CLI Reparse-Point Validation fehlt** (Security)
2. **API Idempotency inkorrekt** (Correctness)
3. **Stub-Commands im UI sichtbar** (UX-Vertrauen)

---

## 6. Konsolidierungs- und Bereinigungsplan

### Zusammenführen

| Was | Warum | Wie |
|-----|-------|-----|
| ConvertOnlyPipelinePhase + WinnerConversionPipelinePhase | Fast identische Konvertierungslogik | Shared Helper `ConversionPhaseHelper.ConvertFiles()` extrahieren, Set-Member-Handling als Parameter |
| InsightsEngine → FeatureService.Analysis | InsightsEngine wird nie aufgerufen, FeatureService.Analysis bietet identische Methoden | InsightsEngine entfernen, Tests anpassen |

### Entfernen

| Was | Warum |
|-----|-------|
| InsightsEngine | Redundant mit FeatureService.Analysis |
| CatchGuardService | Nie in Produktion verdrahtet, Errors werden direkt gehandhabt |

### Deaktivieren / Aus UI entfernen (bis funktional)

| Was | Warum |
|-----|-------|
| E01 GPU Hashing | Kein Backend |
| E02 Parallel Hashing Toggle | Kein Backend |
| E03 FTP Source | Kein Download |
| E04 Plugin Manager | Kein Plugin-Laden |
| E05 Cloud Sync | Kein Sync |
| E06 Cron Auto-Execute | Keine Ausführung |

### Reparieren

| Was | Fix |
|-----|-----|
| API Idempotency Fingerprint | EnableDatAudit + EnableDatRename in BuildRequestFingerprint aufnehmen |
| CLI Reparse-Point Validation | API-Level Reparse-Point-Check nach CliArgsParser portieren |
| CLI Rollback | `--rollback <audit.csv>` Flag + `--rollback-dry-run` implementieren |
| ConvertOnly Set-Member | GetSetMembers() in ConvertOnlyPipelinePhase integrieren |
| DatRename Konflikte | DUP-Suffix oder Skip-Audit + Nutzerwarnung hinzufügen |
| 7z Zip-Slip | Entry-Path gegen Temp-Root validieren vor Extract |
| psxtract Verifizierung | CHD-Magic-Byte-Check nach Konvertierung |

### Dokumentieren und als v2.x markieren

| Was | Warum |
|-----|-------|
| RunHistoryService | Explizit auf v2.1 verschoben |
| ScanIndexService | Performance-Feature für Folge-Scans |
| GPU/Parallel Hashing Backend | Erfordert OpenCL/CUDA Investigation |
| Plugin System | Großes Epic, nicht für v1.0 |
| FTP DAT Download | Netzwerk-Feature, nicht für v1.0 |
| Cloud Sync | Großes Epic, nicht für v1.0 |

---

## 7. Priorisierte Sanierung

### P0 — Sofort (Release-Blocker)

| # | Problem | Aufwand | Dateien |
|---|---------|---------|---------|
| P0-1 | API Idempotency: EnableDatAudit + EnableDatRename in Fingerprint | Klein (1h) | RunLifecycleManager.cs |
| P0-2 | CLI Reparse-Point Validation | Klein (2h) | CliArgsParser.cs |

### P1 — Vor Release zwingend

| # | Problem | Aufwand | Dateien |
|---|---------|---------|---------|
| P1-1 | CLI Rollback implementieren | Mittel (4–8h) | CliArgsParser.cs, Program.cs, AuditSigningService.cs |
| P1-2 | ConvertOnly Set-Member Tracking | Klein (2h) | ConvertOnlyPipelinePhase.cs |
| P1-3 | DatRename Konflikterkennung | Klein (2h) | DatRenamePipelinePhase.cs |
| P1-4 | Conversion Phase Konsolidierung | Mittel (3h) | ConvertOnlyPipelinePhase.cs, WinnerConversionPipelinePhase.cs |
| P1-5 | Stub-Commands deaktivieren | Klein (1h) | FeatureCommandService partial files |
| P1-6 | 7z Zip-Slip Entry-Path Validation | Klein (1h) | ArchiveHashService.cs |

### P2 — Vor Release empfohlen

| # | Problem | Aufwand | Dateien |
|---|---------|---------|---------|
| P2-1 | psxtract CHD-Magic-Byte Verifizierung | Klein (1h) | PsxtractInvoker.cs |
| P2-2 | API JSONL Logging Support | Mittel (3h) | Program.cs (API) |
| P2-3 | API Move Confirmation Flag | Klein (1h) | Program.cs (API), OpenApiSpec.cs |
| P2-4 | HealthScore Konstanten dokumentieren | Klein (0.5h) | RunProjection.cs |

### P3 — Nach Release

| # | Problem | Aufwand | Dateien |
|---|---------|---------|---------|
| P3-1 | InsightsEngine entfernen | Klein (1h) | InsightsEngine.cs, Tests |
| P3-2 | CatchGuardService entfernen | Klein (1h) | CatchGuardService.cs, Tests |
| P3-3 | RunHistoryService/ScanIndex in DEFERRED.md | Dokumentation | docs/ |
| P3-4 | dolphintool Compression konfigurierbar | Mittel (2h) | DolphinToolInvoker.cs |
| P3-5 | RunResultBuilder Drift-Protection | Mittel (2h) | RunResultBuilder.cs |
| P3-6 | FeatureService Partial-Konsolidierung | Groß (8h) | FeatureService.*.cs |

---

## 8. Entscheidungsmatrix

| Feature | Status | Entscheidung | Prio | Nächste Maßnahme |
|---------|--------|-------------|------|------------------|
| GameKey Normalization | ✅ OK | Behalten | — | — |
| Region Detection | ✅ OK | Behalten | — | — |
| File Classification | ✅ OK | Behalten | — | — |
| Console Detection | ✅ OK | Behalten | — | — |
| Scoring (Format/Version/Health/Completeness) | ✅ OK | Behalten | — | — |
| Deduplication Engine | ✅ OK | Behalten | — | — |
| Rule Engine | ✅ OK | Behalten | — | — |
| Set Parsing (CUE/GDI/CCD/MDS/M3U) | ✅ OK | Behalten | — | — |
| Safe Regex | ✅ OK | Behalten | — | — |
| LRU Cache | ✅ OK | Behalten | — | — |
| Header Analysis/Detection | ✅ OK | Behalten | — | — |
| Conversion Graph/Planner | ✅ OK | Behalten | — | — |
| Conversion Executor | ✅ OK | Behalten | — | — |
| chdman Invoker | ✅ OK | Behalten | — | — |
| 7-Zip Invoker | ✅ OK | Behalten | — | — |
| dolphintool Invoker | ⚠️ OK | Behalten | P3 | Compression konfigurierbar |
| psxtract Invoker | ⚠️ Schwach | Behalten, reparieren | P2 | CHD-Magic-Byte-Check |
| PBP Encryption Detection | ✅ OK | Behalten | — | — |
| Conversion Policy | ✅ OK | Behalten | — | — |
| Conversion Registry | ✅ OK | Behalten | — | — |
| **ConvertOnly Phase** | ⚠️ Lücke | **Behalten, reparieren** | **P1** | Set-Member Tracking |
| **WinnerConversion Phase** | ⚠️ Duplikat | **Konsolidieren** | **P1** | Shared Helper extrahieren |
| Move Pipeline Phase | ✅ OK | Behalten | — | — |
| Junk Removal Phase | ✅ OK | Behalten | — | — |
| Console Sorting | ✅ OK | Behalten | — | — |
| ZIP Sorting | ⚠️ Fragil | Behalten | P3 | Mixed-Content dokumentieren |
| **DAT Rename** | ⚠️ Lücke | **Behalten, reparieren** | **P1** | Konflikterkennung |
| **Rollback (GUI+API)** | ✅ OK | Behalten | — | — |
| **Rollback (CLI)** | ❌ Fehlt | **Implementieren** | **P1** | --rollback Flag |
| Conflict Policy | ✅ OK | Behalten | — | — |
| File Hashing | ✅ OK | Behalten | — | — |
| CHD Header Hash | ✅ OK | Behalten | — | — |
| **Archive Hashing (7z)** | ⚠️ Security | **Behalten, reparieren** | **P1** | Zip-Slip Fix |
| Headerless Hashing | ✅ OK | Behalten | — | — |
| Header Repair | ⚠️ Begrenzt | Behalten | P3 | Scope-Erweiterung optional |
| Parallel Hashing | ✅ OK | Behalten | — | — |
| DAT Index | ✅ OK | Behalten | — | — |
| DAT Audit Phase | ✅ OK | Behalten | — | — |
| DAT Source Service | ✅ OK | Behalten | — | — |
| HTML/CSV/JSON Report | ✅ OK | Behalten | — | — |
| Audit CSV Trail | ✅ OK | Behalten | — | — |
| JSONL Logging | ✅ (CLI only) | Behalten | P2 | API-Support |
| Phase Metrics | ✅ OK | Behalten | — | — |
| Run Projection | ✅ OK | Behalten | P3 | Constants dokumentieren |
| Benchmark Infrastructure | ✅ OK | Behalten | — | — |
| GUI Navigation Shell | ✅ OK | Behalten | — | — |
| Command Palette | ✅ OK | Behalten | — | — |
| First-Run Wizard | ✅ OK | Behalten | — | — |
| Inspector Context Wing | ✅ OK | Behalten | — | — |
| DAT Audit View | ✅ OK | Behalten | — | — |
| Watch Mode | ✅ OK | Behalten | — | — |
| Themes (6x) | ✅ OK | Behalten | — | — |
| **API Idempotency** | ❌ Bug | **Sofort reparieren** | **P0** | Fingerprint erweitern |
| **CLI Reparse-Point** | ❌ Security | **Sofort reparieren** | **P0** | API-Level Check portieren |
| API SSE Progress | ✅ OK | Behalten | — | — |
| API Rate Limiting | ✅ OK | Behalten | — | — |
| API Client Binding | ✅ OK | Behalten | — | — |
| Settings Loader | ✅ OK | Behalten | — | — |
| Safety Profiles | ✅ OK | Behalten | — | — |
| Tool Discovery | ✅ OK | Behalten | — | — |
| Path Safety | ✅ OK | Behalten | — | — |
| Quarantine Detection | ✅ OK | Behalten | — | — |
| **GPU Hashing** | ❌ Stub | **Deaktivieren** | **P1** | Aus UI entfernen |
| **Parallel Hashing Toggle** | ❌ Stub | **Deaktivieren** | **P1** | Aus UI entfernen |
| **FTP Source** | ❌ Stub | **Deaktivieren** | **P1** | Aus UI entfernen |
| **Plugin Manager** | ❌ Stub | **Deaktivieren** | **P1** | Aus UI entfernen |
| **Cloud Sync** | ❌ Stub | **Deaktivieren** | **P1** | Aus UI entfernen |
| **Cron Auto-Execute** | ❌ Stub | **Deaktivieren** | **P1** | Aus UI entfernen |
| **InsightsEngine** | ❌ Verwaist | **Entfernen** | P3 | Redundant mit FeatureService |
| **CatchGuardService** | ❌ Verwaist | **Entfernen** | P3 | Nie in Produktion |
| RunHistoryService | ⏸️ Deferred | Späteres Epic (v2.1) | P3 | Dokumentieren |
| ScanIndexService | ⏸️ Deferred | Späteres Epic (v2.1) | P3 | Dokumentieren |

---

## 9. Konkrete nächste Schritte

### Phase 1 — P0 Release-Blocker (sofort, parallel möglich)

1. **API Idempotency Fix:** `EnableDatAudit` und `EnableDatRename` in `RunLifecycleManager.BuildRequestFingerprint()` aufnehmen + Test
2. **CLI Reparse-Point Validation:** Reparse-Point-Check aus API (`FileAttributes.ReparsePoint`) in `CliArgsParser.cs` Root-Validation portieren + Test

### Phase 2 — P1 Fixes (sequenziell, vor Release)

3. **CLI Rollback:** `--rollback <audit.csv>` und `--rollback-dry-run` Flags in CliArgsParser, Execution in Program.cs via AuditSigningService.Rollback()
4. **ConvertOnly Set-Member:** `PipelinePhaseHelpers.GetSetMembers()` in ConvertOnlyPipelinePhase.Execute() integrieren
5. **DatRename Konflikterkennung:** In DatRenamePipelinePhase.Execute(): wenn Zieldatei existiert → DUP-Suffix via `ResolveChildPathWithinRoot()` oder Audit-Eintrag "SKIP_CONFLICT"
6. **7z Zip-Slip:** In ArchiveHashService: Extracted Entry-Paths gegen Temp-Root per `Path.GetFullPath()` + `StartsWith()` validieren
7. **Conversion Phase Konsolidierung:** Shared `ConvertFileHelper` extrahieren aus ConvertOnly + WinnerConversion
8. **Stub-Commands deaktivieren:** GPU Hashing, Parallel Hashing Toggle, FTP Source, Plugin Manager, Cloud Sync, Cron Auto-Execute aus FeatureCommandService entfernen oder `IsVisible = false` setzen

### Phase 3 — P2 Improvements (vor Release empfohlen)

9. **psxtract Verifizierung stärken:** Nach Konvertierung CHD-Magic-Bytes (4D 46 50 00) prüfen
10. **API JSONL Logging:** Optional JsonlLogWriter in API-Startup integrieren
11. **API Move Confirmation:** Optional `confirm: true` Flag in RunRequest
12. **HealthScore Dokumentation:** Magic Constants in RunProjection.cs kommentieren

### Phase 4 — P3 Cleanup (nach Release)

13. **InsightsEngine entfernen** (redundant mit FeatureService.Analysis)
14. **CatchGuardService entfernen** (nie in Produktion)
15. **RunHistoryService / ScanIndexService** in DEFERRED_FEATURES.md dokumentieren
16. **dolphintool Compression konfigurierbar** machen
17. **RunResultBuilder** Drift-Protection (Generator oder Reflektion?)
18. **FeatureService** Partial-Dateien konsolidieren (8 → 4)
19. **ZipSorter Mixed-Content** Warnung hinzufügen
20. **M3U Member Resolution** Konsistenz zwischen Scan und Dedupe prüfen

### Parallelisierung

- Schritte 1+2 können **parallel** bearbeitet werden (verschiedene Dateien)
- Schritte 3+4+5+6 können **parallel** bearbeitet werden (verschiedene Dateien)
- Schritt 7 hängt von 4 ab (ConvertOnly muss erst Set-Member haben, dann konsolidieren)
- Schritt 8 ist **unabhängig** von allem anderen
- Schritte 9–12 sind alle **unabhängig**
- Schritte 13–20 sind alle **unabhängig**

### Vor Release zwingend bereinigt

| # | Was | Status |
|---|-----|--------|
| 1 | API Idempotency Fingerprint | P0 |
| 2 | CLI Reparse-Point Validation | P0 |
| 3 | CLI Rollback | P1 |
| 4 | ConvertOnly Set-Member | P1 |
| 5 | DatRename Konflikterkennung | P1 |
| 6 | 7z Zip-Slip | P1 |
| 7 | Conversion Phase Duplikation | P1 |
| 8 | Stub-Commands deaktivieren | P1 |

**Gesamtaufwand P0+P1: ~15–20 Arbeitsstunden**
