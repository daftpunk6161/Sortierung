# Romulus — Offene Implementierungsthemen

Stand: 2026-03-24  
Quelle: Analyse aller Plan-Dokumente in `plan/` gegen aktuelle Codebase.

---

## 1. Conversion Engine (feature-conversion-engine-1.md) — ✅ Abgeschlossen

Phasen 1–6 (TASK-001 bis TASK-055) sind komplett umgesetzt.  
Status im Plan: **Completed**.

### Verbleibende Out-of-Scope Epics (eigene Arbeitspakete)

- [ ] **EPIC-01**: PS2 SYSTEM.CNF-Analyse (robuste CD/DVD-Erkennung statt 700MB-Heuristik)
- [ ] **EPIC-02**: RVZ-Verify-Verbesserung (dolphintool dry-convert als Integrity-Check)
- [ ] **EPIC-03**: Parallele Conversion (Thread-Pool für Batch-Conversion)
- [ ] **EPIC-04**: MDF/MDS/NRG-Support (nicht integrierte Tools)
- [ ] **EPIC-05**: Conversion-Preview-UI (dediziertes Conversion-Tab in WPF)
- [ ] **EPIC-06**: ciso-Tool-Integration (CSO→ISO Decompression) — blockiert CSO→ISO→CHD Kette

---

## 2. DAT-Audit & Rename (feature-dat-audit-rename-1.md) — ✅ Abgeschlossen

Alle 7 Phasen (TASK-001 bis TASK-055) sind komplett umgesetzt (Stand 2026-03-25).  
Status im Plan: **Planned** (Header-Status veraltet, tatsächlich fertig).

### Verbleibende Out-of-Scope Epics

- [ ] **Epic B**: Cross-Root Matching & Repair (#46) — Suche über mehrere Verzeichnisse
- [ ] **Epic C**: Archive Rebuild & Restructuring (#47) — ZIP/7z → korrekte Struktur

---

## 3. Benchmark Evaluation Pipeline (feature-benchmark-evaluation-pipeline-1.md) — Teilweise offen

Grundinfrastruktur (MetricsAggregator, BaselineComparator, BenchmarkEvaluationRunner, HTML-Reports) vorhanden.

### Phase 1 — Quality Gates & erweiterte Metriken (P0)

- [ ] ExtendedMetrics-Record mit allen 16 M-Werten (M8–M16)
- [ ] `CalculateExtended()` Methode in MetricsAggregator
- [ ] M8 Safe Sort Coverage explizit
- [ ] M9 False Confidence Rate (Wrong ∧ Confidence≥85 ∧ !HasConflict)
- [ ] M10 Console Confusion Index
- [ ] M11 DAT Exact Match Rate (Tag-basiert)
- [ ] M12 DAT Weak Match Rate (Tag-basiert)
- [ ] M13 Repair-Safe Rate (Tag-basiert)
- [ ] M14 Category Accuracy (Category-Vergleich)
- [ ] M15 UNKNOWN→WRONG Migration (Baseline-Delta)
- [ ] M16 Confidence Calibration Error (Bucket-basiert)
- [ ] BenchmarkSampleResult um `ActualCategory` erweitern
- [ ] QualityGateTests (M4/M7/M8/M9a + M6 info) mit harten Schwellenwerten
- [ ] Initialer Baseline-Snapshot `baseline-latest.json` committen
- [ ] Unit-Tests für alle erweiterten Metriken (M8–M16)

### Phase 2 — Dataset-Expansion & Coverage-Lücken (P1)

- [ ] DatasetExpander ausführen & JSONL validieren (Merge-Prüfung)
- [ ] Arcade-Expansion auf ≥160 (`hardFail`)
- [ ] Computer-Expansion auf ≥120 (`hardFail`)
- [ ] BIOS-Expansion: `biosTotal ≥ 35`, `biosSystems ≥ 8`
- [ ] Multi-File-Sets auf ≥20 (`hardFail`)
- [ ] PS-Disambiguation auf ≥20 (`hardFail`)
- [ ] `performance-scale.jsonl` füllen (ScaleDatasetGenerator, ≥5.000 Einträge)
- [ ] PerformanceBenchmarkTests: Throughput ≥100 Samples/s
- [ ] CoverageGate-Tests alle grün

### Phase 3 — Reports, Trend-Analyse & CI-Integration (P2)

- [ ] HTML Benchmark Report (`BenchmarkHtmlReportWriter`) mit inline CSS
- [ ] SEC-001: HTML-Escaping aller dynamischen Werte + XSS-Test
- [ ] Confusion Matrix CSV-Export mit CSV-Injection-Schutz (SEC-002)
- [ ] TrendAnalyzer: N-Run-History mit Richtungsanzeige (Improving/Stable/Degrading)
- [ ] GitHub Actions CI-Workflow `benchmark-gate.yml` (PR-Gate)
- [ ] GitHub Actions Nightly-Job (vollständiger Benchmark + HTML-Artifact)

### Phase 4 — Anti-Gaming & Confidence-Kalibrierung (P3)

- [ ] AntiGamingGateTests (M15 UNKNOWN→WRONG Migration ≤ 2%)
- [ ] AntiGamingGateTests (M16 Confidence Calibration Error ≤ 0.15)
- [ ] Per-Sample Baseline-Vergleich (`ComparePerSample` in BaselineComparator)
- [ ] BenchmarkReport um `PerSampleVerdicts` erweitern
- [ ] Repair-Gate Feature-Flag (M13, RepairSafeRate ≥ 90%)
- [ ] Dokumentation aktualisieren (RECOGNITION_QUALITY_BENCHMARK.md, Plan-Status)

---

## 4. Benchmark Testset (feature-benchmark-testset-1.md) — Teilweise offen

Kerninfrastruktur (~90%) ist implementiert: GroundTruthLoader, StubGenerator, EvaluationRunner, 2.073 Einträge.

### Offene Punkte

- [ ] Manuelle Validierung aller Ground-Truth-Einträge gegen tatsächliches Detection-Verhalten
- [ ] Edge-Case-Coverage: CJK-Zeichen, Sonderzeichen, lange Pfade (>260 Zeichen)
- [ ] Test-DATs für Benchmark: mini DAT-Dateien (NES, SNES, GBA, PS1, PS2, Collision)
- [ ] `dat-coverage.jsonl` Ground-Truth (Hash-Match, Hash-Miss, Collision, Container-vs-Content)
- [ ] `DatCoverageBenchmarkTests.cs` (DatIndex mit Test-DATs, Hash-Matching-Prüfung)
- [ ] `repair-safety.jsonl` auf ≥30 Einträge
- [ ] `RepairSafetyBenchmarkTests.cs` (Confidence-Gating-Szenarien)
- [ ] `ScaleDatasetGenerator.cs` implementieren/ausführen (≥5.000 Einträge)
- [ ] `PerformanceBenchmarkTests.cs` (Throughput-Assertion ≥100 Dateien/s)

---

## 5. Benchmark Coverage Expansion (feature-benchmark-coverage-expansion-1.md) — Teilweise offen

2.073 Einträge vorhanden (Ziel 1.200+ initial erreicht). Expansion-Phasen E1–E4 definieren Ausbau auf breitere Abdeckung.

### Phase E1 — Fundament (250 golden-core, 69/69 Systeme, BIOS, PS-Disambig)

- [ ] Tier-1/2/3/4-Cartridge-Expansion in golden-core (+60 Einträge)
- [ ] Disc-Expansion (+25 Einträge, inkl. Multi-File-Sets, Serial-Detection, Archive-Hash)
- [ ] BIOS-Matrix B-01 bis B-12 befüllen (≥50 Einträge über ≥12 Systeme)
- [ ] PS1↔PS2↔PSP Disambiguation (+30 Einträge)
- [ ] CoverageValidator implementieren/verifizieren
- [ ] Neue Stub-Generatoren für E1 (PSP-PVD, CSO, Directory-Game, Multi-File, GDI, M3U)

### Phase E2 — Tiefe (Arcade ≥200, Multi-File ≥80, Computer ≥150, CHD-RAW-SHA1 ≥25)

- [ ] Arcade-Ausbau: Parent/Clone/BIOS/Split/Merged/Non-Merged/CHD (+120 Einträge)
- [ ] Multi-File/Multi-Disc (+40 Einträge, inkl. Repair-Safety für Multi-File)
- [ ] Computer/PC-Ausbau: AMIGA, C64, ZX, MSX, ATARIST, CPC, PC98, X68K (+50 Einträge)
- [ ] CHD-RAW-SHA1-Ausbau (+15 Einträge)
- [ ] Neue Stub-Generatoren: Arcade-ZIP, CHD-v4/v5, CSO, Computer-Stubs (ADF, D64, TZX, DSK, ST, ATR)

### Phase E3 — Breite (golden-realworld 350, chaos-mixed 200, edge-cases 150, neg-controls 80)

- [ ] golden-realworld System-Tiefe: Tier-1 ≥20/System, Tier-2 ≥8/System (+150 Einträge)
- [ ] chaos-mixed Fallklassen: Falsch benannte, kaputte Sets, Archive-Inner, Headerless (+50 Einträge)
- [ ] edge-cases Disambiguation: GB↔GBC, MD↔32X, SAT↔SCD↔DC, GC↔Wii, Confidence (+50 Einträge)
- [ ] negative-controls: UNKNOWN-expected, Non-ROM-Dateitypen (+30 Einträge)

### Phase E4 — Metriken (repair-safety 70, dat-coverage 100, TOSEC ≥10, Headerless ≥20)

- [ ] repair-safety Confidence-Varianten (+20 Einträge)
- [ ] dat-coverage Validierung ≥100 Einträge (No-Intro ≥25, Redump ≥25, MAME ≥15, TOSEC ≥10)
- [ ] Manifest-Finalisierung mit exakten Coverage-Metriken
- [ ] S1-Gate: ≥1.200 Einträge, 69/69 Systeme, 20/20 Fallklassen verifiziert
- [ ] Baseline-Snapshot `s1-baseline.json` erstellen

---

## 6. Benchmark Coverage Matrix (feature-benchmark-coverage-matrix-impl-1.md) — Teilweise offen

CoverageValidator, `gates.json` und CoverageGateTests sind implementiert. Datenbefüllung fehlt.

### Offene Punkte

- [ ] Plattformfamilien-Gates befüllen und verifizieren (Cartridge ≥320, Disc ≥260, Arcade ≥160, Computer ≥120, Hybrid ≥60)
- [ ] Fallklassen-Gates befüllen (FC-01 bis FC-20 über Hard-Fail-Schwellen)
- [ ] Tier-Tiefe-Gates verifizieren (Tier-1 ≥15/System, Tier-2 ≥5/System, Tier-3 ≥2, Tier-4 ≥1)
- [ ] Spezialbereich-Gates: BIOS ≥35, Arcade-Parent ≥15, Multi-Disc ≥15, PS-Disambig ≥20, TOSEC ≥5, CHD-RAW-SHA1 ≥5
- [ ] BIOS-Matrix B-01 bis B-12 vollständig verteilt auf JSONL-Dateien
- [ ] Arcade-Matrix A-01 bis A-20 verteilt (~200 Einträge)
- [ ] Redump-Matrix R-01 bis R-20 verteilt (~235 Einträge)
- [ ] Computer-Matrix D-01 bis D-12 verteilt (~150 Einträge)
- [ ] Manifest `benchmark/manifest.json` mit vollständigen Coverage-Metriken
- [ ] `S1_MinimumViableBenchmark_AllGatesMet()` Test grün

---

## 7. CI/CD & Infrastruktur (quer über alle Pläne)

- [ ] GitHub Actions Workflow `benchmark-gate.yml` (PR-Gate: QualityGate + BenchmarkRegression + CoverageGate)
- [ ] GitHub Actions Nightly-Schedule (vollständiger Benchmark + HTML-Report als Artifact)
- [ ] Committed Baseline-Snapshot `benchmark/baselines/baseline-latest.json`
- [ ] `performance-scale.jsonl` befüllt (≥5.000 Einträge)

---

## 8. GUI Redesign — Informationsarchitektur & Layout (docs/ux)

Quellen: `full-gui-redesign-romulus.md`, `gui-redesign-analysis.md`  
Empfohlenes Zielbild: **Studio Hybrid Layout** (Konzept C aus beiden Analysen).  
Status: Designkonzept fertig, **keine Implementierung begonnen**.

### Shell & Navigation

- [ ] Studio Hybrid Layout implementieren (3-Spalten: NavRail 72px | Content + DetailDrawer | ContextWing 280px)
- [ ] MainWindow → ShellWindow refactoren (CommandBar + NavRail + TabBar + Content + Wing + ActionRail)
- [ ] Tab-Bar-System (horizontal, kontextabhängig je NavSection) implementieren
- [ ] NavigationService mit Enum-basierter Navigation + History (Back/Forward)
- [ ] Command Palette (Ctrl+K) mit Fuzzy-Search über Aktionen, Settings, Navigation
- [ ] Phase-Indicator (5-Step Pipeline: Configure → Preview → Review → Execute → Report)
- [ ] Responsive Breakpoints (960px–1920px+, NavRail/ContextWing collapse)

### View-Konsolidierung

- [ ] StartView → MissionControlView umgestalten (Dashboard: Sources + Intent + Health + LastRun)
- [ ] ResultView → LibraryView aufteilen (4 Sub-Views: Overview, Decisions, Safety, Report)
- [ ] SortView + SettingsView → ConfigView konsolidieren (4 Tabs: Workflow, Filters, Profiles, Advanced)
- [ ] SettingsView (8 Kategorien) auflösen → ConfigView + ToolsView + SystemView
- [ ] Workflow-Tab eliminieren (Presets/ROMs nur in Mission Control, Region → Config)
- [ ] Log von ResultView nach System.Activity verschieben
- [ ] GameKey-Preview von Settings nach Tools.GameKeyLab verschieben
- [ ] Safety Review View (Blocked / Review Required / Unknown) als neue LibrarySafetyView

### ViewModel-Refactoring

- [ ] MainViewModel (5 Partial-Dateien) aufteilen → ShellVM (schlank: Navigation, Theme, Phase)
- [ ] MissionControlViewModel erstellen (Sources, Intent, Health, LastRun)
- [ ] LibraryViewModel erstellen (Analyse-State, SubTab-Selektion, Filter)
- [ ] ConfigViewModel erstellen (Merge von Setup + Settings-Teilen)
- [ ] SystemViewModel erstellen (Log, Appearance, Automation, About)
- [ ] InspectorViewModel / ContextPanelViewModel erstellen (kontextabhängige Details)
- [ ] CommandPaletteViewModel erstellen (Fuzzy-Search, Command-Registry)
- [ ] RunPipelineViewModel konsolidieren

### Smart Action Bar & Danger-Zone

- [ ] Footer → Smart Action Bar (State-Machine-basiert: IDLE → RUNNING → PREVIEW_DONE → REVIEW_DONE → EXECUTED)
- [ ] Move/Execute NUR nach Review sichtbar, mit Inline-Confirm-Banner
- [ ] Danger-State: ActionBar Hintergrund wechselt zu gedämpftem Rot, Separator zwischen Safe und Danger
- [ ] Move-CTA von 3 Seiten → NUR in Action Bar (ein eindeutiger Einstiegspunkt)
- [ ] Auto-Navigation nach Run-Ende zu Results/Dashboard
- [ ] Button-Abschneidung fixen: MinHeight="52", garantierte Sichtbarkeit (P0-Blocker aus UX-Analyse)

---

## 9. Config / Filtering / Regions Redesign (docs/ux)

Quellen: `config-filtering-redesign-flow.md`, `config-filtering-redesign-jtbd.md`, `config-filtering-redesign-journey.md`  
Status: Design-Specs & Flow-Specification fertig, **keine Implementierung begonnen**.

### Region Priority Ranker (NEU)

- [ ] Config > Regionen: Drag & Drop sortierbare Regionsliste (alle 20+ Regionen)
- [ ] `PreferEU/US/JP/WORLD` Booleans → `ObservableCollection<RegionPriorityItem>` ersetzen
- [ ] Region-Presets: EU-Fokus, US-Fokus, Multi-Region, Alle (1-Klick)
- [ ] Position in Liste → Score-Gewicht (Position 1 = höchster RegionScore)
- [ ] "+ Region hinzufügen" Dropdown (nur nicht-aktive Regionen)
- [ ] Keyboard-Alternative: ↑↓ zum Verschieben, Delete zum Entfernen

### Konsolen Smart-Picker (ÜBERARBEITET)

- [ ] Suchfeld (prominent): Live-Filter über Key, DisplayName, FolderAliases
- [ ] Chip/Tag-Ansicht für ausgewählte Konsolen (mit ✕ zum Entfernen)
- [ ] Hersteller-Akkordeon (zugeklappt per Default, aufklappbar)
- [ ] Schnellauswahl-Buttons: Top 10, Disc-basiert, Handhelds, Retro (<1995)
- [ ] Alle/Keine Buttons pro Herstellergruppe + global
- [ ] Counter-Badge "X von Y Konsolen ausgewählt"
- [ ] Hinweis: "Keine Auswahl = alle Konsolen werden gescannt"

### Dateityp-Filter (VERBESSERT)

- [ ] Counter "X / Y gewählt" hinzufügen
- [ ] Gruppen-Buttons (Disc-Images, Archive, Cartridge) für 1-Klick-Auswahl
- [ ] Hinweis "Keine Auswahl = alle" prominent

### ViewModel-Mapping

- [ ] `ApplyRegionPreset(string)` Command erstellen
- [ ] `MoveRegionUp/Down` Commands erstellen
- [ ] `AvailableRegions` Collection (nicht-aktive Regionen) erstellen
- [ ] `ApplyConsolePreset(string)` Command erstellen
- [ ] `SelectedConsoleCount` Computed Property erstellen
- [ ] `SelectAllInGroup/DeselectAllInGroup` Commands erstellen

---

## 10. Theme-System Ausbau (docs/ux)

Quelle: `full-gui-redesign-romulus.md` (Kapitel 8)  
Aktuell: 3 Themes (Synthwave Dark, Light, High Contrast). Ziel: 6 Themes.

- [ ] `_DesignTokens.xaml` erstellen (theme-agnostisch: Spacing, Radii, Type Scale)
- [ ] `_ControlTemplates.xaml` extrahieren (alle Templates nutzen nur DynamicResource)
- [ ] Bestehende 3 Themes auf neues Token-System migrieren
- [ ] **Clean Dark Pro** Theme erstellen (VS Code Dark+ Stil, kein Neon, kein Glow)
- [ ] **Retro CRT** Theme erstellen (Phosphor-Grün, Scanline-Overlay, Monospace)
- [ ] **Arcade Neon** Theme erstellen (Kräftige Neon-Farben, Gradient-Borders, Magenta)
- [ ] Theme-Switcher-Dropdown mit Farbvorschau-Swatches
- [ ] `Ctrl+T` Theme-Cycling + Command-Palette-Integration
- [ ] Theme-Scheduling (dark after 18:00) + Windows-Theme-Sync

---

## 11. Accessibility — A11Y (docs/ux)

Quellen: `NARRATOR_TEST_PLAN.md`, `config-filtering-redesign-flow.md` (Abschnitt 4)  
Status: Basis-A11Y vorhanden (AutomationProperties, LiveSettings), Erweiterungen offen.

### Keyboard-Navigation (Config/Filtering)

- [ ] Alle interaktiven Elemente per Tab erreichbar
- [ ] Logische Tab-Reihenfolge (Suche → Chips → Gruppen → Items)
- [ ] Sichtbare Fokus-Indikatoren (3px Neon-Border)
- [ ] Enter/Space aktivieren Checkboxen und Buttons
- [ ] Escape schließt Dropdowns

### Screen Reader

- [ ] Suchfeld: `AutomationProperties.Name="Konsole suchen"`
- [ ] Chips: Ansage "SNES ausgewählt. Drücken zum Entfernen"
- [ ] Counter: LiveRegion, wird bei Änderung angesagt
- [ ] Akkordeon-Status: "Nintendo, 12 Konsolen, zugeklappt"
- [ ] Region-Position: "EU, Position 1 von 4"
- [ ] Pipeline-Stepper-Ellipsen: AutomationProperties.Name ergänzen

### Visuelle Accessibility

- [ ] WCAG AA Kontrast (4.5:1) für alle 6 Themes verifizieren (AAA für High Contrast)
- [ ] Mindest-Touch-Target: 44×44px (auch Checkboxen)
- [ ] Keine Information nur über Farbe (Icons + Text)
- [ ] Text bis 200% Zoom ohne Layout-Bruch

### Narrator DryRun-Testplan

- [ ] Narrator DryRun-Workflow komplett durchspielen (NARRATOR_TEST_PLAN.md)
- [ ] WebView2 Report-Vorschau: Alternative Browser-Öffnung sicherstellen
- [ ] Kein Focus-Trap — aus jedem Bereich per Tab herausnavigierbar

---

---

## 12. Core-Zielarchitektur (ADR-005, ADR-013) — Proposed

Umfangreiche Zielarchitektur für Kernfunktionen — Status: **Proposed**, größtenteils nicht umgesetzt.

### Immutable RomCandidate Refactoring

- [ ] `RomCandidate` von `sealed class` auf `sealed record` umstellen
- [ ] `FileCategory` als Enum statt String durchgängig verwenden
- [ ] `CandidateFactory` als Single-Point-of-Construction durchsetzen (BIOS `__BIOS__` Prefix für GameKey-Isolation)
- [ ] Kein `null`/Whitespace-GameKey: SHA256-Fallback garantiert

### Category-aware Winner Selection

- [ ] `FilterToBestCategory` vor Multi-Kriterien-Sort (GAME > BIOS > NonGame > JUNK > UNKNOWN)
- [ ] `DedupeGroup` als `sealed record` statt mutablem `DedupeResult`

### Neue Core-Typen

- [ ] `SortDecision` (static, pure) — bestimmt Zielverzeichnis basierend auf ConsoleKey
- [ ] `RunProjection` als einzige KPI-Quelle für GUI/CLI/API/Report
- [ ] `ProjectionFactory` (static, pure) in Core — zentrale KPI-Berechnung
- [ ] `CompletenessScorer` nach `Core/Scoring/` verschieben (aktuell Domain-Logik in `EnrichmentPipelinePhase`)
- [ ] `FolderDeduplicator.GetFolderBaseKey()` als `FolderKeyNormalizer` nach `Core/GameKeys/`

### RunResult & Builder

- [ ] `RunResult` als sealed record (immutable nach `.Build()`)
- [ ] `RunResultBuilder` — Append-Pattern statt 20+ mutable Properties
- [ ] Getrennte `JunkMoveResult` und `DedupeMoveResult` innerhalb RunResult

### Pipeline Phase-Handler-Pattern

- [ ] `IPipelinePhase<TIn,TOut>` Interface mit typisierten Ein-/Ausgaben
- [ ] `ScanPhase`, `EnrichmentPhase`, `DedupePhase`, `JunkRemovalPhase`, `DedupeMovePhase`, `ConvertPhase`, `ConsoleSortPhase` als typisierte Steps
- [ ] AuditTrailService als Cross-Cutting Concern (CSV-Injection Prevention, Action-Typing)
- [ ] ConsoleSort-Phase braucht Audit-Trail (bisher fehlend)

### Conversion-Phase Invariante

- [ ] Conversion-Regel: nie `converted++` vor Verify, nie Source löschen bei Verify-Failure
- [ ] Invariante: `Converted + Errors + Skipped == Attempted`

---

## 13. GUI-Zielarchitektur (ADR-006) — Proposed

ViewModel-Entkernung und Projection-Pattern — umfangreich, aktuell nicht umgesetzt.

### Projection-Objekte (reine Funktionen, kein State)

- [ ] `DashboardProjection.FromRunResult()` — ersetzt 100-Zeilen ApplyRunResult inline
- [ ] `ProgressProjection` — ersetzt 5 einzelne Progress-Properties
- [ ] `StatusProjection.Compute()` — ersetzt duplizierte RefreshStatus() in MainVM und RunVM
- [ ] `ErrorSummaryProjection.Build()` — ersetzt duplizierte PopulateErrorSummary()
- [ ] `BannerProjection.Compute()` — Banner-Sichtbarkeitslogik zentralisiert
- [ ] `MoveGateProjection` — Fingerprint + RunState → Gate-Text

### ViewModel-Restructuring

- [ ] RunState existiert exakt einmal — nur in RunViewModel, MainVM delegiert
- [ ] `IsBusy`/`IsIdle` aus RunState abgeleitet, keine separaten Bool-Flags
- [ ] MainViewModel.RunPipeline.cs entkernen: ~400 Zeilen Inline-Logik → Projections
- [ ] `IRunService.BuildOrchestrator(RunOptionsDto)` statt `MainViewModel`-Parameter
- [ ] Settings-Duplikation auflösen (MainVM.Settings.cs → SetupViewModel als Single Source of Truth)
- [ ] `RunResultSummary.cs` löschen (toter Code, ersetzt durch DashboardProjection)
- [ ] HealthScore-Formel auf genau eine Implementierung konsolidieren (`FeatureService.CalculateHealthScore`)

### Inline-Lokalisierung

- [ ] Inline-Strings in MainViewModel (`"Startbereit ✓"`, `"Keine Ordner"`) → `ILocalizationService`

### Code-Behind Bereinigung

- [ ] `MainWindow.xaml.cs` Orchestrierungslogik (OnRunRequested → ExecuteAndRefreshAsync) in ViewModel verschieben

---

## 14. CLI-Zielarchitektur (ADR-008) — Proposed

`Program.cs` (850 LOC, 14 Verantwortlichkeiten) auf 4 fokussierte Dateien aufteilen.

### Parser-Extraktion

- [ ] `CliArgsParser.cs` — pure Funktion `Parse(string[]) → CliParseResult`, keine Seiteneffekte
- [ ] Strenge Wert-Validierung: Flag ohne Wert → Exit Code 3, Flag als Wert abgelehnt

### Output-Extraktion

- [ ] `CliOutputWriter.cs` mit typisierten Methoden (DryRun-JSON, Move-Summary, Usage, Errors)
- [ ] `CliDryRunOutput` als typisiertes Record statt anonymes Objekt (Feld-Drift eliminieren)

### Mapper-Extraktion

- [ ] `CliOptionsMapper.cs` — trennt Parsing von Semantik (Settings-Merge, Root-Existenz, System-Pfad-Blockade)

### Shared Setup nach Infrastructure

- [ ] `RunEnvironmentBuilder` als shared Service für CLI/API/WPF — eliminiert 120 Zeilen Setup-Duplikation

---

## 15. API-Zielarchitektur (ADR-009) — Proposed

18 RED-Tests spezifizieren fehlende Features und Korrekturen.

### Entry-Point-Parity (API  ↔ CLI/WPF)

- [ ] API-RunManager: Infrastructure-Initialisierung nachrüsten (consoles.json, DAT-Index, HashService, Converter)
- [ ] `RunRequest` erweitern: `ConflictPolicy`, `ConvertOnly`
- [ ] `RunRecord` erweitern: `ElapsedMs` (computed), `ProgressPercent`, `CancelledAtUtc`
- [ ] `ApiRunResult` → Projection-basiert (kein manuelles Feld-Mapping der 30+ Felder)
- [ ] `ApiRunResult.Error` von `string?` auf `OperationError?` umstellen

### Middleware-Korrekturen

- [ ] Correlation-ID VOR Auth setzen (aktuell danach → 401/429 ohne Correlation-Header)
- [ ] Rate-Limiting mit `Retry-After`-Header
- [ ] SSE: `completed_with_errors` als eigener Event-Name (nicht generisch `completed`)

### Weitere Korrekturen

- [ ] Health-Endpoint: `version`-Feld ergänzen
- [ ] Successful POST → `Location`-Header
- [ ] `DurationMs` bei Cancel korrekt berechnen (nicht 0)
- [ ] `OperationErrorResponse` mit `Utc`-Feld
- [ ] Fingerprint-Update: `ConflictPolicy` + `ConvertOnly` in Fingerprint-Berechnung
- [ ] `RunManager` → `RunLifecycleManager` + `ApiResponseMapper` aufteilen (God-Class 800 LOC)

---

## 16. Safety / IO / Security (ADR-010) — Proposed

11 identifizierte Sicherheitslücken, jeweils durch einen RED-Test spezifiziert.

### P0 — Kritisch

- [ ] Destination-Root-Containment bei Move-Operationen (Destination Escape via `..`)
  - Option: optionaler `allowedRoot`-Parameter in `MoveItemSafely`

### P1 — Hoch

- [ ] NTFS Alternate Data Streams in Pfadauflösung blockieren (`:` in Pfaden)
- [ ] Extended-Length-Prefix (`\\?\`) in `NormalizePath` ablehnen
- [ ] Rollback ohne `.meta.json`-Sidecar blockieren (Sidecar-Pflicht + `force`-Parameter)
- [ ] Rollback über Reparse-Points: `IsReparsePoint`-Check vor Move

### P2 — Mittel

- [ ] Trailing-Dot Windows-Normalisierung in Pfadauflösung blockieren
- [ ] ReadOnly-Attribut vor Delete entfernen
- [ ] Locked-File Handling: `MoveItemSafely` gibt `null` zurück statt IOException
- [ ] Zip-Bomb Compression-Ratio-Limit (`MaxCompressionRatio = 100.0`)
- [ ] `FormatConverterAdapter`: `MaxCompressionRatio`-Konstante hinzufügen
- [ ] DTD Processing: `DtdProcessing.Prohibit` mit Fallback für Legacy-DATs

---

## 17. Test-Zielarchitektur (ADR-011) — Proposed

Strukturelle Testlücken trotz 5.200+ Tests.

### Fehlende Seams

- [ ] `IFileReader` für Core Set-Parser (CueSetParser, GdiSetParser etc. rufen `File.*` direkt auf)
- [ ] `ITimeProvider` für Orchestrator (`DateTime.UtcNow` verhindert deterministische Tests)
- [ ] `IRunOptionsFactory` / `RunOptionsBuilder` — einheitliches RunOptions-Building für alle 3 Entry Points

### Neue Pflicht-Testarten

- [ ] Core Determinism Snapshot Suite (GameKey, Region, Winner, Score, Classification Snapshots in JSON)
- [ ] Cross-Output Parity Tests: RunOptions-Feld-Parität über CLI/API/WPF
- [ ] RunResult-Snapshot-Parität (Orchestrator-Output als JSON-Snapshot)
- [ ] RunProjection-Konsistenz: KPI-Additivität `Keep + Dupes + Junk + Unknown + FilteredNonGame == Total`
- [ ] Audit Roundtrip Tests (Run → Audit → Rollback → Verify, 5 Testszenarien)
- [ ] Null-Injection Boundary Tests (nullable Orchestrator-Dependencies: converter=null etc.)
- [ ] Phase-Isolation-Tests erweitern (7 neue Szenarien: DatIndex-Konflikt, Verify-Exception → Orphan etc.)

### Test-Double-Konsolidierung

- [ ] Shared `InMemoryFileSystem` (ersetzt 19 IFileSystem-Doubles)
- [ ] Shared `ConfigurableConverter` (ersetzt 6 IFormatConverter-Doubles)
- [ ] Shared `StubToolRunner` (ersetzt 9 IToolRunner-Doubles)
- [ ] Shared `StubDialogService` (ersetzt 8 IDialogService-Doubles)
- [ ] Shared `TrackingAuditStore` (ersetzt 2+ IAuditStore-Doubles)
- [ ] ScenarioBuilder (Fluent API für PipelineContext + RunOptions)

### Altcode-Bereinigung

- [ ] Set-Parser (`CueSetParser`, `GdiSetParser`, `CcdSetParser`, `M3uPlaylistParser`, `MdsSetParser`): `File.*`-Aufrufe durch `IFileReader`-Parameter ersetzen
- [ ] V1TestGapTests.cs, V2RemainingTests.cs, CoverageBoostPhase1-9Tests.cs → prüfen und migrieren

---

## 18. Orchestrator / Pipeline / Composition Root (ADR-012) — Proposed

RunOrchestrator (850 LOC) enthält zu viel Fachlogik. 4-Wellen-Migration geplant.

### Welle A — Foundation (kein Risikobereich)

- [ ] `IRunEnvironment` Interface (statt konkrete Typen) + Factory
- [ ] `RunOptionsFactory` + `IRunOptionsSource` — einheitlich für alle Entry Points
- [ ] `SharedServiceRegistration.AddRomCleanupCore()` — gemeinsame DI-Registrierung
- [ ] Entry Points auf shared Service-Registration umstellen

### Welle B — Pipeline-Datenfluss typisieren

- [ ] `PipelineState` (set-once Container für typisierte Zwischenergebnisse)
- [ ] `PhaseStepResult` + typisierte Phase-Results (ScanPhaseResult, DedupePhaseResult etc.)
- [ ] `PhasePlanBuilder` — konditionaler Phase-Plan statt Closure-basiertem `BuildStandardPhasePlan()`
- [ ] Bestehende Phases auf `IPhaseStep`-Interface umstellen

### Welle C — Orchestrator ausdünnen (850 → ≤250 LOC)

- [ ] `ReportPhaseStep` + `AuditSealPhaseStep` extrahieren
- [ ] `DeferredAnalysisPhaseStep` extrahieren
- [ ] Inline-Methoden (ExecuteDedupePhase etc.) in dedizierte Steps verschieben
- [ ] `BuildStandardPhasePlan()` durch `PhasePlanBuilder.Build()` ersetzen
- [ ] `RunResultBuilder` auf Append-Pattern umstellen

### Welle D — Entry Points aufräumen

- [ ] `RunManager` → `RunLifecycleManager` + `ApiRunResultMapper` trennen
- [ ] `RunService` (WPF) auf Factory-Pattern kürzen
- [ ] CLI `Program.cs` auf Factory-Pattern umstellen
- [ ] `ReportPathResolver` als shared Service extrahieren

### Toter Code entfernen

- [ ] `PipelineEngine` + `PipelineModels` (~160 LOC, nie in Production genutzt)
- [ ] `EventBus` + `EventBusModels` (~120 LOC, nie in Orchestrator integriert)

---

## 19. Benchmark-Framework & Testset-Architektur (ADR-015, ADR-016, ADR-017) — Proposed / Teilweise offen

### Quality Gates (P0)

- [ ] `QualityGateTests.cs`: M4/M6/M7/M9a Hard-Fail-Gates als CI-Blocker
- [ ] `ConfidenceCalibrationTests.cs`: M16 Calibration Error

### Testset-Erweiterung

- [ ] BIOS-Fälle erweitern (Ist: ~15-20, Soll: 60)
- [ ] Arcade Parent/Clone/BIOS/Split-Merged erweitern (Ist: ~80-100, Soll: 200)
- [ ] Computer/PC erweitern (Ist: ~40-60, Soll: 150)
- [ ] 4 fehlende Systeme aus consoles.json ergänzen
- [ ] Multi-File/Multi-Disc erweitern (Ist: ~15-20, Soll: 80)
- [ ] Directory-based Games (Wii U RPX, 3DS CIA, DOS) abdecken
- [ ] performance-scale.jsonl befüllen (aktuell 0 Entries)

### Anti-Overfitting

- [ ] Holdout-Zone implementieren (~200 Entries, nicht im Repo, nur CI)
- [ ] Chaos-Quote ≥ 30 % erzwingen (aktuell 28 %, knapp unter Pflicht)

### Stub-Realismus

- [ ] L2-Realistic Stubs (Header + realistisches Padding + korrekte Dateigröße-Klasse)
- [ ] L3-Adversarial Stubs (absichtliche Abweichungen, Alignment-Fehler)
- [ ] `StubGeneratorDispatch` um `RealismLevel`-Parameter erweitern

### Ground-Truth-Schema-Erweiterung

- [ ] `schemaVersion`-Feld hinzufügen
- [ ] `expected.gameIdentity`, `expected.discNumber`, `expected.repairSafe`
- [ ] `addedInVersion`, `lastVerified`

### Plugin-System (ADR-004)

- [ ] C# Plugin-System (⏳ Backlog) — PowerShell-Plugins nicht direkt übertragbar, Neuimplementierung ausstehend

### ADR-007 Restprobleme

- [ ] API Entry-Point-Drift beheben (selbe Infrastructure-Init wie CLI/WPF)
- [ ] ExtensionSets in `ZipSorter` und `BestFormats` in `FormatConverterAdapter` aus JSON-Config statt hardcoded

---

## 20. Kategorie-Erkennung & Pre-Filter (docs/product/CATEGORY_PREFILTER_AUDIT.md)

Kategorie-Erkennungsrate liegt bei **23,158 %** — Release-Blocker.  
7 Root-Causes identifiziert (U1–U7). FileClassifier ist extension-blind und size-blind.  
Default `Game(90)` für alles. 6 konkrete Fixes in 3 Phasen geplant.

### Phase 1 — Kritische Fixes (P0)

- [ ] **Fix 2**: `IsLikelyJunkName` aus `ConsoleDetector` entfernen (Kategorie- und Konsolenachse entkoppeln)
- [ ] **Fix 1**: Extension-Aware `FileClassifier` — neues Overload `Classify(baseName, extension, sizeBytes)`
- [ ] **Fix 3**: Size-Validation in `EnrichmentPipelinePhase` — 0-Byte-Dateien → `Unknown`
- [ ] **Fix 6**: `JunkClassified`-Metrik + `categoryRecognitionRate` in `MetricsAggregator` / Baseline aufnehmen
- [ ] Tests: CategoryRecognitionRate-Gate im Benchmark (Ziel: >85 %)

### Phase 2 — Erweiterte Erkennung

- [ ] **Fix 4**: Non-ROM-Extension-Blocklist in `StreamingScanPipelinePhase` (`.txt`, `.jpg`, `.exe` etc. direkt überspringen)
- [ ] **Fix 5**: Konsolen-Aware Junk-Sortierung: `_TRASH_JUNK/{ConsoleKey}/` statt flachem Junk-Ordner
- [ ] **Fix 7**: `NonGame`-Kategorie für Utilities, Firmware-Updates, Player-Shells
- [ ] **Fix 8**: `ArchiveContent`-basierter Category-Check (ZIP mit `.nfo`/`.txt`/`.url` → Junk-Verdacht)

### Phase 3 — Refinement

- [ ] **Fix 9**: `CategoryOverride`-Feld in `consoles.json` für systemspezifische Regeln (Arcade-ZIPs sind nie Junk)
- [ ] **Fix 10**: Confidence-Penalty bei Category=Unknown + Extension=ambiguous

---

## 21. Confidence & Sorting-Gate Redesign (docs/product/CONFIDENCE_SORTING_GATE_REDESIGN.md)

Wrong Match Rate = Unsafe Sort Rate = **12,674 %**. Gate-Abfangrate für False Positives = **0 %**.  
Umfassendes Redesign der Confidence-Berechnung und Sorting-Gates.

### Evidence-Klassifikation

- [ ] Hard-Evidence Definition: `DatHash`, `DiscHeader`, `CartridgeHeader`, `UniqueExtension`
- [ ] Soft-Evidence Definition: `SerialNumber`, `FolderName`, `ArchiveContent`, `FilenameKeyword`, `AmbiguousExtension`
- [ ] `DetectionHypothesis` um `IsHardEvidence`-Flag erweitern

### Soft-Only Confidence Cap

- [ ] Soft-Only Confidence Cap = 65 implementieren (kein Soft-Stacking über 65)
- [ ] Single-Source Caps pro Detection-Source (z.B. FolderName allein → max 50)
- [ ] `HypothesisResolver` Rewrite mit Evidence-Caps

### 4-Gate-Modell

- [ ] Gate 1 — Category Gate: Category ≠ Game → Sorting blockieren
- [ ] Gate 2 — Confidence Gate: ≥85 → Sort, 65–84 → Review, <65 → Blocked
- [ ] Gate 3 — Evidence Gate: Mindestens ein Hard-Evidence nötig für Sort
- [ ] Gate 4 — Conflict Gate: HasConflict + kein Hard-Evidence → Blocked

### Neue Typen & Erweiterungen

- [ ] `SortDecision` Enum implementieren: `Sort`, `Review`, `Blocked`, `DatVerified`
- [ ] `ConsoleDetectionResult` erweitern: `HasHardEvidence`, `IsSoftOnly`, `SortDecision`
- [ ] `RomCandidate` um `SortDecision`-Feld erweitern
- [ ] `CandidateFactory` um SortDecision-Berechnung erweitern

### Integration

- [ ] `StandardPhaseSteps` auf 4-Gate-Modell umstellen
- [ ] `ConsoleSorter` berücksichtigt `SortDecision` statt nur Confidence ≥ 80
- [ ] `MetricsAggregator` um SortDecision-Counts erweitern (SortCount, ReviewCount, BlockedCount)
- [ ] `GroundTruthComparator` um SortDecision-Verdikt erweitern
- [ ] CLI/API/GUI-Parität für Review-Bucket sicherstellen

---

## 22. Conversion-Domäne — Offene Lücken (docs/architecture/CONVERSION_DOMAIN_AUDIT.md, CONVERSION_ENGINE_ARCHITECTURE.md, CONVERSION_MATRIX.md)

65 Systeme analysiert, 76 Conversion-Pfade definiert. Grundarchitektur (FormatConverterAdapter) vorhanden, aber monolithisch.  
12 Prioritätslücken (L1–L12) identifiziert. Zielarchitektur: graphbasierte, datengesteuerte Conversion-Engine.

### P0 — Release-Blocker

- [ ] **L1**: `ConversionPolicy`-Feld in `consoles.json` für alle 65 Systeme einführen (`Auto`/`ArchiveOnly`/`ManualOnly`/`None`)
- [ ] **L7**: ARCADE/NEOGEO aus `DefaultBestFormats` entfernen — **aktiver Bug** (Set-Integrität wird zerstört)
- [ ] **L2**: Multi-File-Conversion atomar machen (CUE+BIN Paare in ZIP: alle .cue-Dateien finden, nicht nur erste)

### P1 — Hoch

- [ ] **L3**: PS2 CD vs DVD unterscheiden (createcd für <700 MB, createdvd für ≥700 MB; optional SYSTEM.CNF-Analyse)
- [ ] **L4**: CSO→CHD Pipeline: ciso/maxcso als Zwischenschritt-Decompression (CSO→ISO→CHD)
- [ ] **L5**: NKit-Warning implementieren (Lossy-Quelle erkennen, `ConversionSafety = Risky`, Nutzer-Warnung)
- [ ] **L6**: CDI-Input als ManualOnly behandeln (Track-Vollständigkeit ungeprüft, DiscJuggler-Truncation-Risiko)

### P2 — Mittel

- [ ] **L8**: 38 Systeme ohne `ConversionTarget` in consoles.json ergänzen (aktuell stilles Skip)
- [ ] **L9**: RVZ-Verifizierung verbessern (nicht nur Magic-Byte + Size, sondern dolphintool dry-convert)
- [ ] **L10**: Format-Prioritäten Single Source of Truth (FormatConverterAdapter + FeatureService synchronisieren)

### P3 — Wartbarkeit

- [ ] **L11**: Compression-Ratios aus Hardcoded-Werten nach JSON-Config auslagern
- [ ] **L12**: `FeatureService.GetTargetFormat()` und `FormatConverterAdapter.DefaultBestFormats` synchronisieren

### Zielarchitektur (aus CONVERSION_ENGINE_ARCHITECTURE.md)

- [ ] `ConversionGraph` (Core) — gerichteter gewichteter Graph mit Format-Extensions als Knoten, Capabilities als Kanten
- [ ] `ConversionPlanner` (Core) — berechnet optimalen Plan (kürzester sicherer Pfad)
- [ ] `SourceIntegrityClassifier` (Core) — Lossless/Lossy/Unknown klassifizieren
- [ ] `ConversionPolicyEvaluator` (Core) — Policy-Constraints prüfen, Safety zurückgeben
- [ ] `IConversionRegistry` (Contracts) — Port für Capabilities + Policies aus JSON
- [ ] `IConversionPlanner` (Contracts) — Port für Plan-Berechnung
- [ ] `IConversionExecutor` (Contracts) — Port für Step-by-Step-Execution
- [ ] `ConversionRegistryLoader` (Infrastructure) — lädt aus `consoles.json` + `conversion-registry.json`
- [ ] `ConversionExecutor` (Infrastructure) — führt Steps einzeln aus, verwaltet Temp-Dateien, auditiert
- [ ] `FormatConverterAdapter` refactoren: delegiert an Planner + Executor (Rückwärtskompatibilität via IFormatConverter)

### Conversion-Matrix Spezifika (aus CONVERSION_MATRIX.md)

- [ ] Lossy→Lossy-Pfade blockieren (CSO→WBFS, NKit→GCZ → nur Richtung Lossless-Target)
- [ ] Multi-Disc-Set (M3U) als atomische Einheit konvertieren (kein Teilconvert)
- [ ] Encrypted PBP erkennen und blockieren (nicht an psxtract übergeben)
- [ ] `.wad` (WiiWare/VC) und `.dol` (Homebrew) explizit als Skip markieren
- [ ] maxcso als Alternative zu ciso evaluieren (bessere Kompression, schneller)

---

## 23. Conversion UX & Product-Model (docs/product/CONVERSION_PRODUCT_MODEL.md)

Vollständiges Produktmodell mit Policies, Safety-Klassifikation, ConversionPlan, 10 Regeln, UX-Anforderungen.

### ConversionPlan als Preview

- [ ] `ConversionPlan`-Objekt vor Ausführung berechnen und im DryRun anzeigen
- [ ] `ConversionStep`-Modell: Order, Input/Output-Extension, Capability, IsIntermediate
- [ ] `ConversionPlan.RequiresReview` Flag: `ManualOnly` oder `Risky` oder `Lossy` → Nutzerbestätigung

### Fehlende Metriken in RunProjection

- [ ] `ConvertBlockedCount` — Anzahl durch Policy blockierter Konvertierungen
- [ ] `ConvertLossyWarningCount` — Lossy-Quellen mit Warnung
- [ ] `ConvertVerifyPassedCount` / `ConvertVerifyFailedCount` — Verify-Ergebnisse
- [ ] `ConvertSavedBytes` — eingesparter Speicherplatz durch Konvertierung

### UX-Anforderungen (GUI)

- [ ] Preview-Tab: Conversion-Plan pro Datei mit Source → Steps → Target Darstellung
- [ ] Lossy-Badge: visueller Indikator für Lossy-Quellen (⚠️ Icon)
- [ ] Policy-Transparenz: Anzeige warum eine Konvertierung blockiert/erlaubt ist
- [ ] Progress: Conversion-Fortschritt pro Datei und gesamt
- [ ] Post-Run Dashboard: TotalSavedBytes, BySafety-Verteilung, Verify-Status

### CLI-Output-Format

- [ ] DryRun: JSON-Objekte pro Konvertierungsplan (sourcePath, steps, safety, skipReason)
- [ ] Move: Summary mit Converted/Skipped/Errors/Blocked + TotalSavedBytes

### API-Response-Format

- [ ] `ConversionReport` in `ApiRunResult` aufnehmen (TotalPlanned, Converted, Skipped, Errors, Blocked, RequiresReview, TotalSavedBytes)
- [ ] Pro-Datei `ConversionResult` in Ergebnisliste (Plan, Safety, VerificationResult, DurationMs)

### 10 Conversion-Regeln (normativ)

- [ ] R-01: Kein Datenverlust durch Automatik (nur verifizierbare, verlustfreie Pfade bei Auto)
- [ ] R-02: Verify-or-Die (Quelle erst nach bestandener Verifizierung in Trash)
- [ ] R-03: Set-Integrität unantastbar (ARCADE/NEOGEO-ZIPs nie verändern)
- [ ] R-04: Lossy-Quelle braucht Warnung (NKit, CSO, PBP)
- [ ] R-05: Multi-File atomisch (CUE+BIN als Einheit)
- [ ] R-06: Policy-Respekt (None = Block, ManualOnly = Nutzerbestätigung)
- [ ] R-07: Tool-Integrität (SHA256-Hash-Check vor Ausführung)
- [ ] R-08: Deterministisch (gleicher Input + Config = identischer Output)
- [ ] R-09: Cleanup nach Failure (Temp-Dateien aufräumen)
- [ ] R-10: Audit-Trail für jede Konvertierung

---

## 24. Miss-Analyse & Safe Recall (docs/product/MISS_ANALYSIS_SAFE_RECALL.md)

60 Missed Entries analysiert. 48 davon sicher recoverable. 5 Root-Causes identifiziert.

### Root-Cause Fixes (nach Priorität)

#### P1 — Ground-Truth & Dispatch

- [ ] **RC-1**: Filename-Collision in ec-ambiguous beheben (20 Entries betroffen) — einzigartige Dateinamen pro Stub
- [ ] **RC-2**: Missing `primaryMethod` in rs-Entries beheben (15 Entries) — `StubGeneratorDispatch` Fallback-Logik
- [ ] **RC-2b**: `StubGeneratorDispatch`: Fallback auf Header-Generator wenn primaryMethod unbekannt

#### P2 — Neue Stub-Generatoren

- [ ] **RC-3a**: `OperaFsGenerator` für 3DO (Opera-Dateisystem-Signatur)
- [ ] **RC-3b**: `BootSectorTextGenerator` für generische Boot-Sektoren
- [ ] **RC-3c**: `XdvdfsGenerator` für Xbox (XDVDFS-Signatur)
- [ ] **RC-3d**: `Ps3PvdGenerator` für PS3 (PVD mit PS3-Marker)
- [ ] **RC-3e**: Stub-Generatoren für 12 fehlende Disc-Systeme integrieren (PCFX, JAGCD, CD32, etc.)

#### P3 — PS3-Erkennung

- [ ] **RC-4**: PS3-Detection in `DiscHeaderDetector` implementieren (RxPs3Marker Pattern)
- [ ] **RC-4b**: Tests für PS3 PVD-Erkennung (ISO mit "PS3VOLUME" / "PS3_GAME" Marker)

#### Akzeptierte Limits

- [ ] **RC-5**: `.bin` ohne Kontext (12 Entries) als systemische Grenze dokumentieren und als UNKNOWN akzeptieren

### Erwartete Recovery

- P1+P2: 44 von 60 Misses recoverable
- P1+P2+P3: 48 von 60 recoverable (80 %)
- Verbleibend: 12 als UNKNOWN (systemic limit, `.bin` ohne Context)

---

## 25. Benchmark-Audit — Ergänzende Findings (docs/architecture/BENCHMARK_AUDIT_REPORT.md, COVERAGE_GAP_AUDIT.md, EVALUATION_STRATEGY.md, RECOGNITION_QUALITY_BENCHMARK.md)

Ergänzende Punkte, die über §3–§7 und §19 hinausgehen.  
Viele Items in diesen Dokumenten bestätigen/detaillieren bereits in §3–§7 und §19 gelistete Punkte.

### Benchmark-Audit Restfunde

- [ ] Quality Gates in CI von informational auf `hardFail=true` umstellen (ROMCLEANUP_ENFORCE_QUALITY_GATES=true)
- [ ] Baseline `groundTruthVersion` Mismatch beheben ("1.0.0" in Baseline vs "2.0.0" in Manifest)
- [ ] `ExtendedMetrics` von `Dictionary<string, double>` auf typisiertes Record umstellen
- [ ] Mutation-Testing Status klären und ggf. in CI integrieren

### Coverage-Gap-Audit Deltas (nicht in §5/§6 erfasst)

- [ ] Cross-System-Disc Abdeckung verifizieren (Ist: ~50, Soll: ≥50)
- [ ] Headerless ROMs ausbauen (Ist: ~30, Soll: ≥40, Delta: –10)
- [ ] Hybrid-Systeme ausbauen (Ist: ~60, Soll: ≥80, Delta: –20)

### Evaluation-Strategie — Severity-Klassifikation

- [ ] 7-Ebenen-Verdikt-System vollständig implementieren (Container → System → Kategorie → Identität → DAT → Sorting → Repair)
- [ ] Pro-Ebene-Metriken in Benchmark-Output aufnehmen (nicht nur Global-M1–M16)
- [ ] `GroundTruthComparator` um alle 7 Verdict-Ebenen erweitern

---

## 26. Testset-System & Governance (docs/architecture/TESTSET_DESIGN.md, REAL_WORLD_TESTSET_SYSTEM.md, GROUND_TRUTH_SCHEMA.md, DATASET_AUDIT_PROCESS.md, TEST_STRATEGY.md, JOURNEY_TEST_MATRIX.md)

Ergänzende Punkte zur Testset-Architektur und formalen Governance.  
Kernimplementierung bereits in §4/§5/§19 erfasst. Hier: prozessuale und strukturelle Ergänzungen.

### Governance & Audit-Prozess

- [ ] Jährlichen Datensatz-Audit-Prozess formalisieren (Checkliste aus DATASET_AUDIT_PROCESS.md)
- [ ] Ereignisgesteuerte Audit-Trigger implementieren (neues System → ≥3 Entries, Bug-Report → edge-case Entry)
- [ ] Ground-Truth-Änderungen nur per PR + Review (Governance-Regel durchsetzen)
- [ ] Baselines nie überschreiben — Archivierungspflicht für alte Baselines

### Testset-System Ergänzungen

- [ ] `holdout-blind` Dataset-Klasse implementieren (~200 Entries, nicht im Repo, nur CI-zugänglich)
- [ ] Overfitting-Detection: Eval-Verbesserung >3 % ∧ Holdout-Verbesserung <0,5 % → CI-Warning
- [ ] Chaos-Quote ≥ 30 % im Testset erzwingen (Coverage-Gate)
- [ ] `ScaleDatasetGenerator` Parameter finalisieren (20+ Systeme, reale Häufigkeitsverteilung)

### Journey Test Matrix

- [ ] CI-Gate `Journey Matrix Gate` formalisieren (11 Gate-Testklassen aus JOURNEY_TEST_MATRIX.md)
- [ ] Neue Persona-Journeys müssen explizit einer Gate-Klasse zugeordnet werden

---

## Zusammenfassung

| # | Bereich | Status | Offene Items |
|---|---------|--------|-------------|
| 1 | Conversion Engine | ✅ Fertig | 6 Out-of-Scope Epics |
| 2 | DAT Audit & Rename | ✅ Fertig | 2 Out-of-Scope Epics |
| 3 | Benchmark Eval Pipeline | ⚠️ Teilweise offen | ~25 Items (Metriken, Gates, Reports, CI) |
| 4 | Benchmark Testset | ⚠️ Teilweise offen | ~9 Items (Validierung, DAT-Tests, Performance) |
| 5 | Benchmark Coverage Expansion | ⚠️ Teilweise offen | ~15 Items (4 Expansionsphasen) |
| 6 | Benchmark Coverage Matrix | ⚠️ Teilweise offen | ~10 Items (Datenbefüllung, Gate-Validierung) |
| 7 | CI/CD | ❌ Offen | 4 Items (GitHub Actions, Baseline, Performance) |
| 8 | GUI Redesign Layout | ❌ Offen | ~30 Items (Shell, Views, ViewModels, ActionBar) |
| 9 | Config/Filtering Redesign | ❌ Offen | ~20 Items (Region-Ranker, Smart-Picker, Dateityp) |
| 10 | Theme-System Ausbau | ❌ Offen | ~9 Items (3 neue Themes, Token-System, Switcher) |
| 11 | Accessibility (A11Y) | ⚠️ Teilweise offen | ~16 Items (Keyboard, Screen Reader, Narrator-Test) |
| 12 | Core-Zielarchitektur | ❌ Proposed | ~20 Items (Immutable Records, Projections, Phases) |
| 13 | GUI-Zielarchitektur | ❌ Proposed | ~15 Items (Projections, ViewModel-Entkernung) |
| 14 | CLI-Zielarchitektur | ❌ Proposed | ~7 Items (Parser/Output/Mapper-Extraktion) |
| 15 | API-Zielarchitektur | ❌ Proposed | ~15 Items (Parity, Middleware, RunManager-Split) |
| 16 | Safety/IO/Security | ❌ Proposed | ~11 Items (Pfad-Guards, Rollback-Guards, ZipBomb) |
| 17 | Test-Zielarchitektur | ❌ Proposed | ~20 Items (Seams, Snapshots, Double-Konsolidierung) |
| 18 | Orchestrator/Pipeline | ❌ Proposed | ~18 Items (4-Wellen-Migration, toter Code) |
| 19 | Benchmark/Testset-Architektur | ⚠️ Teilweise offen | ~18 Items (Gates, Holdout, Stubs, Schema) |
| 20 | Kategorie-Erkennung & Pre-Filter | ❌ Offen | ~11 Items (6 Fixes in 3 Phasen) |
| 21 | Confidence & Sorting-Gate Redesign | ❌ Offen | ~17 Items (Evidence-Caps, 4-Gate-Modell, SortDecision) |
| 22 | Conversion-Domäne — Offene Lücken | ⚠️ Teilweise offen | ~22 Items (L1–L12, Zielarchitektur, Matrix-Spezifika) |
| 23 | Conversion UX & Product-Model | ❌ Offen | ~22 Items (Plan-Preview, Metriken, UX, CLI/API, 10 Regeln) |
| 24 | Miss-Analyse & Safe Recall | ❌ Offen | ~12 Items (RC-1 bis RC-5, Stub-Generatoren, PS3) |
| 25 | Benchmark-Audit — Ergänzungen | ⚠️ Teilweise offen | ~10 Items (Quality-Gate Enforcement, Verdikt-Ebenen) |
| 26 | Testset-System & Governance | ❌ Offen | ~10 Items (Audit-Prozess, Holdout, Journey-Gates) |
