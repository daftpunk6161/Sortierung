---
goal: 'Konsolidierter Deep-Dive Bug-Audit: Alle 212 Findings aus 3 Audit-Runden (Feature, UX/UI, Remaining)'
version: 1.0
date_created: 2026-03-12
last_updated: 2026-03-13
owner: RomCleanup Team
status: 'Planned'
tags: [bug-audit, consolidated, security, ux, ui, core, infrastructure, cli, api, wpf, deep-dive]
---

# Konsolidierter Deep-Dive Bug-Audit — Alle Bereiche

![Status: Planned](https://img.shields.io/badge/status-Planned-blue)

Konsolidierung aller drei Deep-Dive Bug-Audits in ein einziges Tracking-Dokument mit Checkboxen.

| Audit | Datei | Tasks | Runden |
|-------|-------|-------|--------|
| 1 — Features & Security | `feature-deep-dive-bug-audit-1.md` | TASK-001–083 | 1–12 |
| 2 — UX/UI | `feature-deep-dive-ux-ui-audit-2.md` | TASK-084–149 | 13–21 |
| 3 — Core/Infra/CLI/API | `feature-deep-dive-remaining-audit-3.md` | TASK-150–212 | 22–30 |

**Gesamt: 212 Tasks in 30 Runden**

## Restluecke-Abschlussplan

- Ausfuehrungsplan fuer verbleibende Luecken: [feature-restluecke-audit-1.md](feature-restluecke-audit-1.md)
- Fokusbereiche: API-Integrationstests, fail-closed Tool-Hashing, Quarantine-Root-Guards, DAT Async-Haertung, Insights-Drift, WPF-A11y/HC, CI-Governance.

| Priorität | Anzahl | Bedeutung |
|-----------|--------|-----------|
| P0 | 11 | Release-Blocker |
| P1 | 55 | Hoch |
| P2 | 101 | Mittel |
| P3 | 56 | Niedrig |

---

## Prioritäts-Übersicht (Quick Reference)

### P0 — Release-Blocker (11)
- [x] TASK-020: XXE in OnDatDiffViewer
- [x] TASK-021: XXE in OnArcadeMergeSplit
- [x] TASK-022: XXE in FeatureService.CompareDatFiles
- [x] TASK-023: XXE in FeatureService.LoadDatGameNames
- [x] TASK-031: Konsolen-Filter-Checkboxen nie ausgewertet
- [x] TASK-032: SimpleMode Region-Auswahl nie übersetzt
- [x] TASK-084: ~30 Konsolen-Filter-Checkboxen ohne Binding
- [x] TASK-085: ~18 Dateityp-Filter-Checkboxen ohne Binding
- [x] TASK-150: ReDoS-Risiko in RuleEngine.TryRegexMatch
- [x] TASK-164: RunOrchestrator.ScanFiles normalisiert GameKey MIT Dateiendung

### P1 — Hoch (55)
- [x] TASK-001, TASK-011, TASK-012, TASK-013, TASK-014, TASK-024, TASK-025, TASK-026, TASK-027, TASK-028, TASK-033, TASK-034, TASK-035, TASK-037, TASK-041, TASK-042, TASK-051, TASK-056, TASK-064, TASK-071, TASK-072
- [x] TASK-086, TASK-087, TASK-094, TASK-095, TASK-103, TASK-117, TASK-118, TASK-131, TASK-132, TASK-138, TASK-139
- [x] TASK-102
- [x] TASK-110, TASK-111
- [x] TASK-151, TASK-152, TASK-158, TASK-163, TASK-170, TASK-171, TASK-197, TASK-198, TASK-205

---

## 2. Implementation Steps

---

### AUDIT 1 — Features & Security (TASK-001–083)

---

### Runde 1 — DiscHeaderDetector + Binary Parsing

GOAL-001: Alle Bugs in `DiscHeaderDetector.cs` (Core/Classification, ~430 Zeilen)

- [x] **TASK-001** — P1 – ReDoS-Risiko in ResolveConsoleFromText: 15× `Regex.IsMatch()` mit nicht-kompilierten Patterns. Fix: Statische compiled Regex mit matchTimeout.
- [x] **TASK-002** — P2 – Cache-Key ohne Pfad-Normalisierung: `_isoCache`/`_chdCache` verwenden Dateipfad ohne `Path.GetFullPath()`. Fix: Key via `Path.GetFullPath(path).ToUpperInvariant()` normalisieren.
- [x] **TASK-003** — P2 – ReadAtLeast partial read nicht geprüft: Rückgabewert von `ReadAtLeast` in `ScanDiscImage()` wird ignoriert. Fix: `scanSize` auf tatsächlich gelesene Bytes korrigieren.
- [x] **TASK-004** — P3 – 3DO False-Positive-Risiko: Nur 6 Bytes geprüft. Fix: Zusätzlich Opera-FS-Marker prüfen.
- [x] **TASK-005** — P3 – ScanChdMetadata ReadAtLeast analog: Gleicher Bug wie TASK-003.
- [x] **TASK-006** — P2 – Kein Schutz gegen extrem große Dateien im Batch-Modus: `DetectBatch()` ohne Throttling/Progress. Fix: `IProgress<int>` Parameter.

### Runde 2 — DatIndex Datenstruktur

GOAL-002: Alle Bugs in `DatIndex.cs` (Contracts/Models, ~66 Zeilen)

- [x] **TASK-007** — P2 – TotalEntries O(n) pro Zugriff: Iteriert über alle inneren Dictionaries bei jedem Getter-Aufruf. Fix: `Interlocked.Increment`-basierter Counter.
- [x] **TASK-008** — P3 – Keine Size-Limitierung: Kein Maximum für Einträge → OOM bei bösartigem DAT. Fix: `maxEntriesPerConsole` Parameter.
- [x] **TASK-009** — P3 – Doppelte ToLowerInvariant-Normalisierung: Redundant da Comparer case-insensitive. Fix: Entweder Normalisierung oder Comparer entfernen.
- [x] **TASK-010** — P3 – ConsoleKeys enumeriert live: Snapshot bei jedem Aufruf. Kein Fix nötig, nur Dokumentation.

### Runde 3 — CrossRootDeduplicator + FolderDeduplicator

GOAL-003: Alle Bugs in `CrossRootDeduplicator.cs` (~85 Zeilen) und `FolderDeduplicator.cs` (~500 Zeilen)

- [x] **TASK-011** — P1 – CrossRoot Winner-Selection ignoriert Region/Version/DatMatch: Nur FormatScore + SizeBytes. Fix: Dieselbe Scoring-Logik wie `DeduplicationEngine` verwenden.
- [x] **TASK-012** — P1 – FolderDeduplicator MD5 für PS3-Hashing: MD5 kryptographisch gebrochen. Fix: SHA256 statt MD5.
- [x] **TASK-013** — P1 – DeduplicatePs3 erstellt Verzeichnis in DryRun: `EnsureDirectory` ohne mode-Prüfung. Fix: mode-Parameter hinzufügen.
- [x] **TASK-014** — P1 – DeduplicateByBaseName: Destination nicht path-validated: Manipulierter Ordnername `..\..\Windows` möglich. Fix: Auch Destination via `ResolveChildPathWithinRoot()` validieren.
- [x] **TASK-015** — P2 – GetFolderBaseKey: Case-Folding erst am Ende: Kein echter Bug, Code-Klarheit verbessern.
- [x] **TASK-016** — P2 – PS3 Winner-Selection ist ordnungsabhängig: `Directory.GetDirectories()` nicht deterministisch. Fix: Alle Ordner sammeln, dann deterministisch sortieren.
- [x] **TASK-017** — P2 – FindDuplicates: 3× materialisiert mit ToList(): Doppelte Materialisierung. Fix: Einmal ToList() + let-Variable.
- [x] **TASK-018** — P3 – GetPs3FolderHash: File.ReadAllBytes für große Dateien: EBOOT.BIN kann GB groß sein. Fix: Streaming-basiertes Hashing.
- [x] **TASK-019** — P3 – CountFilesRecursive schlucken alle Exceptions: Fix: Nur IOException/UnauthorizedAccessException fangen.

### Runde 4 — WPF Features: Security & Core

GOAL-004: Sicherheits- und Korrektheits-Bugs in 81 WPF-Handlern und FeatureService

- [x] **TASK-020** — P0 – XXE in OnDatDiffViewer: `XDocument.Load(fileA/B)` ohne DTD-Processing zu deaktivieren. Fix: `XmlReaderSettings` mit `DtdProcessing.Prohibit`.
- [x] **TASK-021** — P0 – XXE in OnArcadeMergeSplit: Identisches Problem. Fix: Identisch zu TASK-020.
- [x] **TASK-022** — P0 – XXE in FeatureService.CompareDatFiles: Fix: Zentrale `SafeLoadXDocument()` Helper-Methode.
- [x] **TASK-023** — P0 – XXE in FeatureService.LoadDatGameNames: Fix: Dieselbe zentrale Helper-Methode.
- [x] **TASK-024** — P1 – OnTosecDat: File.Copy ohne Pfad-Validierung: Dateiname wie `..\..\config\settings.json` möglich. Fix: `Path.GetFileName()` + `Path.GetFullPath()` validieren.
- [x] **TASK-025** — P1 – OnCustomDatEditor: Nicht-atomares Datei-Splicing: Datenverlust bei Crash. Fix: Atomic Write Pattern (Temp+Move).
- [x] **TASK-026** — P1 – OnHeaderRepair: File.WriteAllBytes ohne Path-Traversal-Check: Fix: Pfad gegen Roots validieren.
- [x] **TASK-027** — P1 – RepairNesHeader: File.ReadAllBytes für beliebig große Dateien: Fix: Nur Header lesen, nicht gesamte Datei.
- [x] **TASK-028** — P1 – RemoveCopierHeader: Backup .bak überschreibt vorherige: Fix: Timestamped Backup-Name.
- [x] **TASK-029** — P2 – OnMobileWebUI: Detached Process nie beendet: Fix: Process-Referenz speichern, bei Close beenden.
- [x] **TASK-030** — P2 – GetContextMenuRegistryScript: Pfad-Escaping unvollständig: Fix: Anführungszeichen escapen.

### Runde 5 — WPF Features: UI State & Korrektheit

GOAL-005: UI-State-, Korrektheits- und Determinismus-Bugs

- [x] **TASK-031** — P0 – Konsolen-Filter-Checkboxen nie ausgewertet: XAML-Checkboxen ohne Binding, kein Code liest IsChecked. Fix: VM-Properties + XAML-Binding oder Controls entfernen.
- [x] **TASK-032** — P0 – SimpleMode Region-Auswahl nie in PreferXX übersetzt: `SimpleRegionIndex` wird nie in `PreferEU/US/JP` übersetzt. Fix: Im Setter der Property umsetzen.
- [x] **TASK-033** — P1 – _conflictPolicy Dead State: Policy-Dialog hat keinen Effekt. Fix: In RunOptions integrieren.
- [x] **TASK-034** — P1 – Rollback Undo/Redo-Stacks sind Platzhalter: Keine echte Undo-Logik implementiert. Fix: Audit-CSV parsen → Moves rückgängig.
- [x] **TASK-035** — P1 – Watch Mode: Events während Move-Lauf verloren: Fix: Events in Queue einreihen.
- [x] **TASK-036** — P2 – ShowTextDialog: Hard-coded Dark-Theme-Farben: Fix: Farben aus ResourceDictionary lesen.
- [x] **TASK-037** — P2 – CreateRunCancellation: Race Condition bei Dispose: Fix: `Interlocked.Exchange` + Try/Catch.
- [x] **TASK-038** — P2 – IsBusy nicht in allen Fehlerpfaden zurückgesetzt: Fix: `try/finally { IsBusy = false; }`.
- [x] **TASK-039** — P2 – OnClosing: Rekursiver Aufruf: Fix: Guard-Flag `_isClosing`.
- [x] **TASK-040** — P2 – ExportRetroArchPlaylist: Backslash-Kompatibilität: Fix: `.Replace('\\', '/')`.

### Runde 6 — WPF Features: FeatureService Methoden

GOAL-006: Bugs in 49 public static Methoden von `FeatureService.cs`

- [x] **TASK-041** — P1 – AnalyzeHeader: SNES-Erkennung False Positive: Jede Datei ≥32KB mit druckbarem ASCII an bestimmten Offsets wird erkannt. Fix: SNES-Checksum-Felder validieren.
- [x] **TASK-042** — P1 – CreateBaseline: Sequentielles Hashing statt Batch: Fix: `Parallel.ForEachAsync`.
- [x] **TASK-043** — P2 – DetectConsoleFromPath: `parts[^2]` IndexOutOfRange bei flachen Pfaden: Fix: Array-Länge prüfen.
- [x] **TASK-044** — P2 – LoadLocale: Relatives Pfad-Probing unsicher: 5× Parent-Navigation. Fix: Embedded Resources oder konfigurierter Pfad.
- [x] **TASK-045** — P2 – AnalyzeStorageTiers: `File.Exists` + `new FileInfo` pro Kandidat: 100k Syscalls bei 50k Kandidaten. Fix: FileInfo einmal erstellen.
- [x] **TASK-046** — P2 – CronFieldMatch: Step-Berechnung falsch für Ranges: `10-30/5` ignoriert Startpunkt. Fix: `(value - lo) % step == 0`.
- [x] **TASK-047** — P2 – ExportCollectionCsv: CSV-Injection nicht vollständig verhindert: Kein Check auf `=`, `+`, `-`, `@`. Fix: Führendes Hochkomma prefixen.
- [x] **TASK-048** — P2 – CompareDatFiles: Duplicate Code mit OnDatDiffViewer: Fix: OnDatDiffViewer sollte CompareDatFiles aufrufen.
- [x] **TASK-049** — P3 – ClassifyGenre: Naive Keyword-Klassifikation: `gun` matcht `Gundam`. Fix: Wortgrenzen-Matching `\bgun\b`.
- [x] **TASK-050** — P3 – SearchCommands: Levenshtein ohne Längenbeschränkung: 10k-Zeichen-Query → große Allokation. Fix: Query auf max. 50 Zeichen.

### Runde 7 — WPF Features: Analyse & Reports

GOAL-007: Bugs in Analyse- und Report-Features

- [x] **TASK-051** — P1 – GetDuplicateInspector: Audit-CSV ohne Encoding-Erkennung: Fix: `Encoding.UTF8` explizit.
- [x] **TASK-052** — P2 – GetConversionEstimate: Kompressionsraten hart codiert: Fix: Externalisieren oder Disclaimer.
- [x] **TASK-053** — P2 – CalculateHealthScore: Division durch Null möglich: Fix: Guard `if (totalFiles == 0) return 0;`.
- [x] **TASK-054** — P2 – CheckIntegrity: Keine Fehlermeldung wenn kein Baseline existiert: Fix: Explizite Meldung.
- [x] **TASK-055** — P3 – BuildCloneTree: Limitierung auf 50 ohne Sortierung: Fix: `OrderByDescending(g => g.Losers.Count).Take(50)`.

### Runde 8 — WPF Features: Infrastruktur & Deployment

GOAL-008: Bugs in Infrastruktur-, Deployment- und Konfigurations-Features

- [x] **TASK-056** — P1 – GenerateDockerfile: Expose 5000 ohne TLS: API-Key im Klartext. Fix: HTTPS konfigurieren.
- [x] **TASK-057** — P2 – OnPluginMarketplace: Dummy-Implementierung: Fix: Als „Coming Soon" beschriften.
- [x] **TASK-058** — P2 – OnDockerContainer: API-Key als Env-Variable: Fix: Docker Secrets oder Warnhinweis.
- [x] **TASK-059** — P2 – OnWindowsContextMenu: Pfad absolut, bei Verschiebung ungültig: Fix: Hinweis ausgeben.
- [x] **TASK-060** — P2 – OnFtpSource: FTP ohne Verschlüsselung: Fix: Warnhinweis bei unverschlüsseltem FTP.
- [x] **TASK-061** — P2 – SettingsService: LoadInto synchron auf UI-Thread: Fix: `await Task.Run(...)`.
- [x] **TASK-062** — P3 – SettingsService: Kein Versions-Feld für Migration: Fix: `"version": 1` + Migrations-Logik.
- [x] **TASK-063** — P3 – IsPortableMode: Marker-Datei relativ zu BaseDirectory: Fix: Dokumentieren.

### Runde 9 — CI/CD Pipeline

GOAL-009: Lücken in `test-pipeline.yml`

- [x] **TASK-064** — P1 – Coverage-Gate ist kosmetisch: Kein Minimum-Threshold enforced. Fix: coverlet --threshold 50.
- [ ] **TASK-065** — P2 – Nur Windows-CI: Keine Linux/macOS-Tests. Fix: Matrix-Build.
- [x] **TASK-066** — P2 – Kein NuGet-Cache: Fix: `actions/cache@v4`.
- [x] **TASK-067** — P2 – Governance prüft nur .csproj References: Kein grep auf using-Statements. Fix: Zusätzlichen Check.
- [ ] **TASK-068** — P3 – Keine Mutation-Tests: Fix: `dotnet-stryker` als optionaler Job.
- [x] **TASK-069** — P3 – dotnet-version '10.0.x' kann Preview-SDKs einschließen: Fix: `include-prerelease: false`.
- [ ] **TASK-070** — P3 – Kein SBOM-Generierung: Fix: `dotnet CycloneDX` als Step.

### Runde 10 — WPF Features: Konfiguration & Profile

GOAL-010: Bugs in Profil-, Konfigurations- und Lokalisierungs-Features

- [x] **TASK-071** — P1 – OnProfileSave/Load: Settings ohne Schema-Validierung importiert: Fix: JSON gegen Schema validieren.
- [x] **TASK-072** — P1 – OnConfigImport: Überschreibt Settings ohne Backup: Fix: Vorher .bak erstellen.
- [x] **TASK-073** — P2 – OnApplyLocale: UI-Strings nicht vollständig aktualisiert: Fix: Binding-Refresh triggern.
- [x] **TASK-074** — P2 – OnAutoProfile: Console-Detection heuristisch: Fix: Bei unbekanntem Key Benutzer fragen.
- [x] **TASK-075** — P2 – ExportUnified: JSON enthält sensitive Pfade: Fix: Pfade anonymisieren oder Warnhinweis.

### Runde 11 — WPF Features: Sammlung & Visualisierung

GOAL-011: Bugs in Sammlungs- und Visualisierungs-Features

- [x] **TASK-076** — P2 – OnCoverScraper: Placeholder-Implementierung: Fix: Als „Coming Soon" beschriften.
- [x] **TASK-077** — P2 – OnPlaytimeTracker: LastAccessTime als Spielzeit-Proxy unzuverlässig: Fix: Disclaimer.
- [x] **TASK-078** — P2 – BuildVirtualFolderPreview: DetectConsoleFromPath IndexOutOfRange: Fix: Siehe TASK-043.
- [x] **TASK-079** — P3 – OnCollectionSharing: Export enthält absolute Pfade: Fix: Relative Pfade exportieren.

### Runde 12 — Zusammenfassung Audit 1

GOAL-012: Konsolidierung nach Priorität

- [x] **TASK-080** — Konsolidierung P0 (6 Findings): TASK-020, 021, 022, 023, 031, 032
- [x] **TASK-081** — Konsolidierung P1 (22 Findings): Security → Korrektheit → UX
- [x] **TASK-082** — Konsolidierung P2 (38 Findings)
- [x] **TASK-083** — Konsolidierung P3 (17 Findings)

---

### AUDIT 2 — UX/UI (TASK-084–149)

---

### Runde 13 — Data-Binding-Gaps / Dead UI

GOAL-013: XAML-Controls ohne Binding identifizieren

- [x] **TASK-084** — P0 – ~30 Konsolen-Filter-Checkboxen ohne Binding: `chkConsPS1`, `chkConsPS2` etc. alle ohne `IsChecked="{Binding ...}"`. Filter komplett wirkungslos. Fix: VM-Properties + XAML-Binding.
- [x] **TASK-085** — P0 – ~18 Dateityp-Filter-Checkboxen ohne Binding: `chkExtChd`, `chkExtIso` etc. Fix: Analog TASK-084.
- [x] **TASK-086** — P1 – cmbDatHash nicht an VM.DatHashType gebunden: Fix: `SelectedValue="{Binding DatHashType, Mode=TwoWay}"`.
- [x] **TASK-087** — P1 – cmbLogLevel nicht an VM.LogLevel gebunden: Fix: Analog TASK-086.
- [x] **TASK-088** — P2 – cmbQuickProfile komplett leer: Keine ItemsSource. Fix: Profil-Liste laden oder Control entfernen.
- [x] **TASK-089** — P2 – cmbLocale nicht gebunden: Fix: SelectedValue binden.
- [x] **TASK-090** — P2 – chkWatchMode nicht gebunden: Checkbox und Watcher-Zustand divergieren. Fix: VM-Property `IsWatchModeActive`.
- [x] **TASK-091** — P2 – Step-Indicator-Dots nie aktualisiert: Immer „leer". Fix: VM-Properties + Converter.
- [x] **TASK-092** — P3 – listErrorSummary Items.Add statt Binding: MVVM-Verletzung. Fix: ObservableCollection.
- [x] **TASK-093** — P3 – Performance-Labels via x:Name statt Binding: Zeigen dauerhaft Defaults. Fix: VM-Properties binden.

### Runde 14 — Theme-Konsistenz & Visual Issues

GOAL-014: Visuelle Inkonsistenzen und hardcodierte Farben

- [x] **TASK-094** — P1 – ShowTextDialog ignoriert Theme: Hardcodierte Dark-Farben. 40+ Feature-Buttons betroffen. Fix: `DynamicResource` aus Theme.
- [x] **TASK-095** — P1 – MessageBox.Show() bricht Theme: 5+ Stellen mit Standard-Windows-Chrome. Fix: Eigenes themed Dialog-Fenster.
- [x] **TASK-096** — P2 – SynthwaveDark ProgressBar hat Neon-Glow, Light nicht: Design-Inkonsistenz. Fix: Dokumentieren oder angleichen.
- [x] **TASK-097** — P2 – Expander-Header nicht custom-gestylt: Default-Pfeil unsichtbar im Dark-Theme. Fix: Custom ControlTemplate.
- [x] **TASK-098** — P2 – ScrollBar nur Width/Background gestylt: Standard-Thumb im Dark-Theme. Fix: Minimales ControlTemplate.
- [x] **TASK-099** — P3 – ToolTip-Style fehlt: Standard-WPF-ToolTips brechen Theme. Fix: ToolTip-Style.
- [x] **TASK-100** — P3 – SectionCard Shadow-Unterschied zwischen Themes: Akzeptabel, dokumentieren.
- [x] **TASK-101** — P3 – Keine Transition beim Theme-Wechsel: Optional Crossfade.

### Runde 15 — Accessibility & Keyboard-Navigation

GOAL-015: Screenreader, Keyboard, High-Contrast

- [x] **TASK-102** — P1 – 80+ Feature-Buttons mit AutomationProperties.Name versehen (erledigt in Session 5).
- [x] **TASK-103** — P1 – Keyboard-Shortcuts nur 4 von 14 definiert: 10 Shortcuts dokumentiert aber nicht verdrahtet. Fix: Alle als KeyBinding deklarieren.
- [x] **TASK-104** — P2 – Kein TabIndex auf Feature-Buttons: Tab-Reihenfolge nicht-intuitiv. Fix: Logische TabIndex-Gruppen.
- [x] **TASK-105** — P2 – Progress ohne LiveSetting: Screenreader ignoriert Fortschritt. Fix: `LiveSetting="Polite"` auf Progress.
- [x] **TASK-106** — P2 – High-Contrast-Theme implementiert (ThemeService.BuildHighContrastDictionary, WCAG AAA 7:1 Kontrast).
- [x] **TASK-107** — P2 – WebBrowser-Control nicht accessible: Fix: AutomationProperties setzen, langfristig WebView2.
- [x] **TASK-108** — P3 – Keine Focus-Rückkehr nach ShowTextDialog: Fix: `(sender as Button)?.Focus()`.
- [x] **TASK-109** — P3 – Kein Tooltip auf Simple-Mode-Controls: Fix: Descriptive ToolTips.

### Runde 16 — MVVM-Violations & Code-Behind

GOAL-016: MVVM-Pattern-Verletzungen

- [x] **TASK-110** — P1 – MainWindow.xaml.cs ~694 Zeilen Code-Behind (von ~2920 reduziert). FeatureCommandService extrahiert.
- [x] **TASK-111** — P1 – 65+ Feature-Buttons via BindFeatureCommand an FeatureCommands-Dictionary gebunden.
- [x] **TASK-112** — P2 – webReportPreview.Navigate() direkte UI-Manipulation: WebBrowser hat kein Binding-API — dokumentieren.
- [x] **TASK-113** — P2 – Drag-Drop in Code-Behind: WPF DragDrop hat kein natives Binding — dokumentieren.
- [x] **TASK-114** — P2 – _conflictPolicy Feld nie verwendet: Fix: RunOptions.ConflictPolicy Property.
- [x] **TASK-115** — P3 – StatusBarService ist überflüssiger Wrapper: Fix: Klasse entfernen.
- [x] **TASK-116** — P3 – _rollbackUndoStack nie befüllt: Undo/Redo-Buttons immer wirkungslos. Fix: Nach Rollback auf Stack pushen.

### Runde 17 — Feature-Completeness / Phantom-Features

GOAL-017: UI-sichtbare Features ohne echte Funktionalität

- [x] **TASK-117** — P1 – Locale-Wechsel ändert nur Window.Title: Keine XAML-Lokalisierung. Fix: Vollständige i18n oder entfernen.
- [x] **TASK-118** — P1 – Scheduler zeigt nur Cron-Match, plant nichts: Kein Timer/Job. Fix: Implementieren oder als „Cron-Tester" umbenennen.
- [x] **TASK-119** — P2 – GPU-Hashing setzt Env-Variable die niemand liest: Fix: Implementieren oder als „Nicht implementiert" markieren.
- [x] **TASK-120** — P2 – Parallel-Hashing setzt Env-Variable die niemand liest: Fix: Analog TASK-119.
- [x] **TASK-121** — P2 – Cloud-Sync ist reines Status-Display: Fix: Button-Label ändern.
- [x] **TASK-122** — P2 – FTP-Source parst nur URL, verbindet nicht: Fix: Implementieren oder markieren.
- [x] **TASK-123** — P2 – Plugin-System lädt keine Plugins: Fix: Manifest-Validierung oder als „Geplant" markieren.
- [x] **TASK-124** — P3 – Docker-Button generiert nur Text: Fix: SaveFile-Dialog anbieten.
- [x] **TASK-125** — P3 – Spielzeit-Tracker liest nur .lrtl Dateien: Akzeptabel als MVP.
- [x] **TASK-126** — P3 – Cover-Scraper matcht nur nach exaktem Dateinamen: Fix: `GameKeyNormalizer.Normalize()` auf Cover-Name anwenden.

### Runde 18 — Layout & Responsiveness

GOAL-018: Layout-Probleme bei verschiedenen DPI/Fenstergrößen

- [x] **TASK-127** — P2 – Feature-Buttons haben fixe Width-Werte: Text abgeschnitten bei hoher DPI. Fix: `MinWidth` + `Padding` statt fixem Width.
- [x] **TASK-128** — P2 – Kein Auto-Scroll im Log-ListBox: Fix: `ScrollIntoView` bei CollectionChanged.
- [x] **TASK-129** — P3 – MainWindow MinWidth/MinHeight könnten knapp sein (720×520): Fix: Testen, ggf. 800 erhöhen.
- [ ] **TASK-130** — P3 – WebBrowser (IE) veraltet: Fix: Migration zu WebView2.

### Runde 19 — Input-Validierung & Fehler-UX

GOAL-019: Fehlende Eingabevalidierung und Fehler-Feedback

- [x] **TASK-131** — P1 – Tool-Pfade TextBox ohne Existenz-Validierung: Fehler erst zur Laufzeit. Fix: INotifyDataErrorInfo + `File.Exists()`.
- [x] **TASK-132** — P1 – DatRoot/TrashRoot/AuditRoot ohne Pfad-Validierung: Fix: `Directory.Exists()`-Check mit Debounce.
- [x] **TASK-133** — P2 – Interaction.InputBox() komplett unthematisiert: VB6-Relikt. Fix: Eigener InputDialog.xaml.
- [x] **TASK-134** — P2 – Keine Fehler-Templates auf TextBox-Inputs: Fix: ErrorTemplate in Themes.
- [x] **TASK-135** — P2 – OnCustomDatEditor keine Hash-Validierung: `abc` als CRC32 möglich. Fix: Regex-Prüfung.
- [x] **TASK-136** — P2 – Data-Directory-Resolution fragil: 5× Parent-Navigation wiederholt sich. Fix: Zentraler `DataDirectoryResolver` Service.
- [x] **TASK-137** — P3 – OnClosing async-Pattern mit re-entrantem Close(): Fix: Boolean Guard `_isClosing`.

### Runde 20 — System-Tray, Watch-Mode & Spezial-Features

GOAL-020: Sonder-Features (Tray, Watch, Backup, Integrity)

- [x] **TASK-138** — P1 – System-Tray Icon-Handle nicht freigegeben: GDI-Leak. Fix: `DestroyIcon` oder .ico aus Resource.
- [x] **TASK-139** — P1 – Watch-Mode Throttling unzureichend: 5s Debounce ohne Max-Wait. Fix: 30s maximale Wartezeit.
- [x] **TASK-140** — P2 – Backup-Manager kopiert ohne Verzeichnisstruktur: Namenskollision → IOException. Fix: Relative Struktur beibehalten.
- [x] **TASK-141** — P2 – CleanupOldBackups löscht rekursiv ohne Bestätigung: Fix: Confirm-Callback.
- [x] **TASK-142** — P2 – Integrity-Baseline speichert absolute Pfade: Fix: Relative Pfade.
- [x] **TASK-143** — P3 – System-Tray StateChanged-Handler wird mehrfach registriert: Fix: Nur einmal registrieren.
- [x] **TASK-144** — P3 – PresetFullSort setzt DryRun=true: Fix: `DryRun = false` oder Label ändern.
- [x] **TASK-145** — P3 – App.xaml.cs hat keinen globalen Exception-Handler: Fix: `DispatcherUnhandledException` Handler.

### Runde 21 — Zusammenfassung Audit 2

GOAL-021: Konsolidierung UX/UI

- [x] **TASK-146** — Konsolidierung P0 (3 Findings): TASK-084, 085 + Overlap mit TASK-031/032
- [x] **TASK-147** — Konsolidierung P1 (14 Findings): Binding → Security/Leak → Theme → Validation → Accessibility → Refactoring
- [x] **TASK-148** — Konsolidierung P2 (28 Findings)
- [x] **TASK-149** — Konsolidierung P3 (17 Findings)

---

### AUDIT 3 — Core/Infrastructure/CLI/API (TASK-150–212)

---

### Runde 22 — Core Layer: GameKeys, Regions, Rules, Scoring

GOAL-022: Bugs in Core-Modulen (GameKeyNormalizer, RegionDetector, RuleEngine, VersionScorer)

- [x] **TASK-150** — P0 – ReDoS-Risiko in RuleEngine.TryRegexMatch: User-Patterns aus rules.json ohne Timeout/Compiled. Fix: Regex-Cache + `RegexOptions.Compiled` + `matchTimeout`.
- [x] **TASK-151** — P1 – GameKeyNormalizer erzeugt nicht-deterministische Keys für leere Namen: `Guid.NewGuid()` bei leerem Ergebnis bricht Determinismus. Fix: Deterministischer Fallback `__EMPTY_{fileName}`.
- [x] **TASK-152** — P1 – GameKeyNormalizer MsDosTrailingParenRegex while ohne Iterationslimit: Fix: `maxIterations = 20`.
- [x] **TASK-153** — P2 – RegionDetector UK-Sonderbehandlung mit nicht-kompilierter Regex: Fix: Statische compiled Regex.
- [x] **TASK-154** — P2 – VersionScorer erstellt nicht-kompilierte Regex pro Aufruf: Fix: Statische compiled Regex.
- [x] **TASK-155** — P2 – RuleEngine.EvaluateCondition CultureInfo-abhängiges Number-Parsing: `1.5` wird mit deutschem Locale zu `15`. Fix: `CultureInfo.InvariantCulture`.
- [x] **TASK-156** — P3 – RuleEngine.EvaluateCondition ToLowerInvariant() bei jedem Aufruf: Fix: Op beim Deserialisieren normalisieren.
- [x] **TASK-157** — P3 – ConsoleDetector custom GetRelativePath statt Path.GetRelativePath: Fix: BCL-Methode verwenden.

### Runde 23 — Infrastructure: Audit & Signing

GOAL-023: Bugs in AuditCsvStore und AuditSigningService

- [x] **TASK-158** — P1 – Inkonsistente Action-Casing zwischen AuditCsvStore und AuditSigningService: `"Move"` vs `"MOVE"` → Rollback findet nie Zeilen. Fix: Case-insensitive Vergleiche.
- [x] **TASK-159** — P2 – ParseCsvLine dupliziert in beiden Klassen: DRY-Violation. Fix: `AuditCsvParser` extrahieren.
- [x] **TASK-160** — P2 – AuditSigningService._persistedKey ist static: Singleton-Annahme. Fix: Instanz-Feld.
- [x] **TASK-161** — P2 – Key-Datei ohne ACL-Einschränkung: HMAC-Key für alle lesbar. Fix: `FileSecurity` Permissions.
- [x] **TASK-162** — P3 – SanitizeCsvField bricht negative Zahlen: `-1024` wird zu `'-1024`. Fix: Numerische Werte ausnehmen.

### Runde 24 — Infrastructure: Orchestration & FileSystem

GOAL-024: Bugs in RunOrchestrator und FileSystemAdapter

- [x] **TASK-163** — P1 – RunOrchestrator._normalizedRoots ist static ohne Cleanup: Memory-Leak in Long-Running-Server. Fix: Instanz-Feld oder Clear().
- [x] **TASK-164** — P0 – RunOrchestrator.ScanFiles normalisiert GameKey MIT Dateiendung: `.zip` und `.7z` desselben Spiels in verschiedenen Gruppen → keine Deduplizierung. Fix: `Path.GetFileNameWithoutExtension`.
- [x] **TASK-165** — P2 – ScanFiles setzt sizeBytes=0 bei FileInfo-Fehler: 0-Byte verliert Size-Tiebreak, Fehler nicht geloggt. Fix: Warnung loggen.
- [x] **TASK-166** — P2 – ExecutionHelpers.IsBlocklisted quadratische Komplexität: Fix: `HashSet<string>`.
- [x] **TASK-167** — P3 – GetDiscExtensions() allokiert neues HashSet pro Aufruf: Fix: Statisches Feld.
- [x] **TASK-168** — P2 – FileSystemAdapter.CopyFile erstellt kein Zielverzeichnis: `DirectoryNotFoundException`. Fix: `EnsureDirectory` vor Copy.
- [x] **TASK-169** — P3 – GetFilesSafe nicht-deterministische Reihenfolge: Fix: Optional sortieren oder dokumentieren.

### Runde 25 — Infrastructure: Hashing & DAT

GOAL-025: Bugs in Hash- und DAT-Services

- [x] **TASK-170** — P1 – ParallelHasher Sync-over-Async Deadlock: `.GetAwaiter().GetResult()` auf WPF UI-Thread. Fix: Methode async oder `Task.Run()`.
- [x] **TASK-171** — P1 – DatSourceService Sync-over-Async Deadlock: Fix: Analog TASK-170.
- [x] **TASK-172** — P2 – DatRepositoryAdapter Console.Error.WriteLine: Nicht testbar, in GUI unsichtbar. Fix: Injizierbare Log-Action.
- [x] **TASK-173** — P2 – DatRepositoryAdapter games[gameName] überschreibt Duplikate: Fix: Merge statt Overwrite.
- [x] **TASK-174** — P2 – ArchiveHashService 7z-Stdout-Parsing fragil: Fix: `-slt` Format oder Regex-Parsing.
- [x] **TASK-175** — P3 – ArchiveHashService Temp-Dateien bei Crash: Fix: Prefix + Cleanup beim Start.

### Runde 26 — Infrastructure: Conversion & Tools

GOAL-026: Bugs in FormatConverter, ConversionPipeline und ToolRunner

- [x] **TASK-176** — P2 – FormatConverterAdapter.Verify für RVZ nur Existenz+Größe: 1-Byte-Datei passiert. Fix: Magic-Bytes prüfen.
- [x] **TASK-177** — P2 – ConversionPipeline.CheckDiskSpace UNC-Fallback gibt long.MaxValue: Fix: Warning statt Ok.
- [x] **TASK-178** — P2 – ToolRunnerAdapter._toolHashes Lazy-Loading nicht thread-safe: Race Condition. Fix: `Lazy<T>` oder lock.
- [x] **TASK-179** — P2 – ToolRunnerAdapter Timeout hardkodiert 30 Min: PS2 DVD > 45 Min. Fix: Konfigurierbarer Parameter.
- [x] **TASK-180** — P3 – ConversionPipeline.BuildToolArguments Default unspezifisch: Fix: `InvalidOperationException`.
- [x] **TASK-181** — P3 – ToolRunnerAdapter ohne tool-hashes.json: Keine Tools ausführbar. Fix: Konfigurierbar erlauben mit Warning.

### Runde 27 — Infrastructure: Support-Services

GOAL-027: Bugs in EventBus, History, State, Logging, Sorting, Analytics

- [x] **TASK-182** — P2 – EventBus.Publish schluckt alle Handler-Exceptions: Leerer catch. Fix: Error-Callback.
- [x] **TASK-183** — P2 – EventBus.Subscribe: Interlocked.Increment innerhalb lock: Unnötig. Fix: `_sequence++`.
- [x] **TASK-184** — P2 – RunHistoryService: File.GetLastWriteTime (nicht UTC): Zeitzonen-Probleme. Fix: `GetLastWriteTimeUtc`.
- [x] **TASK-185** — P2 – ScanIndexService Fingerprint case-sensitive auf Windows: Fix: `ToUpperInvariant()`.
- [x] **TASK-186** — P2 – ConsoleSorter.FindActualDestination O(10000) lineare Suche: Fix: Directory.GetFiles einmal.
- [x] **TASK-187** — P2 – InsightsEngine Winner-Logik weicht von DeduplicationEngine ab: Fix: `SelectWinner()` direkt aufrufen.
- [x] **TASK-188** — P3 – AppStateStore.NotifyWatchers Snapshot-Timing: Kein Bug, dokumentieren.
- [x] **TASK-189** — P3 – JsonlLogWriter.RotateIfNeeded Race bei Concurrent: Fix: Retry oder Copy+Truncate.
- [x] **TASK-190** — P3 – InsightsEngine.SanitizeCsv prefixed '-' (bricht Dateinamen): Fix: Nur `=`, `+`, `@` prefixen.

### Runde 28 — Infrastructure: Safety, Config, Diagnostics, Quarantine

GOAL-028: Bugs in SafetyValidator, SettingsLoader, CatchGuard, QuarantineService

- [x] **TASK-191** — P2 – SafetyValidator Conservative-Profil blockiert UserProfile: Viele Users haben ROMs unter UserProfile. Fix: Spezifischere Pfade blockieren.
- [x] **TASK-192** — P2 – SettingsLoader.ValidateToolPath keine Path-Traversal-Prüfung: `cmd.exe` als Tool-Pfad möglich. Fix: System-Verzeichnisse ausschließen.
- [x] **TASK-193** — P2 – MergeFromDefaults/MergeFromUserSettings unterschiedliche Strukturen: Fix: Dokumentieren oder beide unterstützen.
- [x] **TASK-194** — P3 – QuarantineService.Restore Path-Traversal-Check zu strikt: `Game..Edition` blockiert wegen `..`. Fix: `Path.GetFullPath` statt String-Contains.
- [x] **TASK-195** — P3 – CatchGuardService.Guard Logging-Level ignoriert: Fix: `level`-Parameter hinzufügen.
- [x] **TASK-196** — P3 – HardlinkService.BuildPlan keine Path-Traversal-Validierung: Fix: `ResolveChildPathWithinRoot`.

### Runde 29 — Entry Points: CLI & REST API

GOAL-029: Bugs in CLI und API

- [x] **TASK-197** — P1 – CLI dataDir-Auflösung mit ".." fragil: Funktioniert nur in Dev-Struktur. Fix: Primär neben Exe suchen.
- [x] **TASK-198** — P1 – API RunManager erstellt pro Run neue Service-Instanzen (kein DI): Fix: DI-Registrierung.
- [x] **TASK-199** — P2 – API SSE-Stream JSON-String-Vergleich: Fragil. Fix: Hash über relevante Felder.
- [x] **TASK-200** — P2 – API keine Validation auf PreferRegions: XSS in Reports möglich. Fix: Allowlist-Validierung.
- [x] **TASK-201** — P2 – CLI Extensions-Default nicht aus settings.json: Fix: Settings-Extensions mergen.
- [x] **TASK-202** — P2 – API fängt alle Exceptions inkl. Security-Exceptions: Fix: Exception-Message in RunResult, spezieller Error-Code.
- [x] **TASK-203** — P3 – CLI keine Path-Traversal-Validierung auf Roots: Fix: `Path.GetFullPath` + System-Dir-Check.
- [x] **TASK-204** — P3 – API kein X-Api-Version Header: Fix: Header global setzen.

### Runde 30 — Contract-Layer & Cross-Cutting

GOAL-030: Architekturelle und Konsistenz-Bugs

- [x] **TASK-205** — P1 – API RunResult shadowed Infrastructure RunResult: Gleicher Name, andere Properties. Fix: Umbenennen zu `ApiRunResult`.
- [x] **TASK-206** — P2 – ConversionPipelineDef.Steps IReadOnlyList aber Elemente mutable: Fix: Separate ConversionPipelineResult-Klasse eingeführt.
- [x] **TASK-207** — P2 – PipelineStep.Status mutiert durch PipelineEngine: Fix: PipelineStepOutcome als separates Result-Objekt.
- [x] **TASK-208** — P2 – Kein zentraler Ort für Default-Extensions: 3+ Stellen pflegen. Fix: Einzige Source of Truth in Contracts.
- [x] **TASK-209** — P3 – ErrorClassifier: PathTooLongException als Transient (Retry sinnlos): Fix: Als Recoverable klassifizieren.
- [x] **TASK-210** — P3 – RomCandidate.Type → ConsoleKey umbenannt in allen 7+ Dateien.
- [x] **TASK-211** — P3 – EventPayload.Timestamp ist string statt DateTime: Fix: DateTime verwenden.
- [x] **TASK-212** — P3 – PhaseMetricsCollector.Export nicht atomic: Fix: Temp+Move wie ScanIndexService.

---

## 3. Alternatives

- **ALT-001**: XXE-Fix einzeln pro Stelle statt zentrale Helper-Methode. Abgelehnt: 6 Stellen → zentrale Methode.
- **ALT-002**: MD5 in PS3-Hashing beibehalten. Abgelehnt: Projekt-Guideline verbietet gebrochene Algorithmen.
- **ALT-003**: Coverage-Gate als separate GitHub Action. Möglich, aber unnötige Komplexität.
- **ALT-004**: Placeholder-Features komplett entfernen. Möglich, aber „Coming Soon" ist weniger destruktiv.
- **ALT-005**: FolderDeduplicator Winner an DeduplicationEngine delegieren. Erfordert Interface-Refactoring.
- **ALT-006**: Dead-UI-Controls sofort entfernen. Abgelehnt: Besser Bindings implementieren.
- **ALT-007**: WPF-Toolkit (MahApps.Metro) statt eigene Dialoge. Abgelehnt: Zu schwere Dependency.
- **ALT-008**: CefSharp statt WebView2. Abgelehnt: ~200MB vs. Edge bereits installiert.
- **ALT-009**: `GeneratedRegex` statt manueller Cache in RuleEngine. Erfordert Compile-Time-Patterns.
- **ALT-010**: API RunManager DI via Service Locator. Widerspricht Clean-Architecture-Prinzipien.

---

## 4. Dependencies

- **DEP-001**: `System.Xml.XmlReaderSettings` — bereits in .NET 10 BCL
- **DEP-002**: `dotnet-reportgenerator-globaltool` — für Coverage-Threshold in CI
- **DEP-003**: `dotnet-stryker` — Mutation-Testing (optional)
- **DEP-004**: `Microsoft.Web.WebView2` — für WebBrowser-Ersatz (TASK-130)
- **DEP-005**: INotifyDataErrorInfo — bereits in .NET BCL
- **DEP-006**: BenchmarkDotNet — für Performance-Tests (optional)
- **DEP-007**: xUnit + FluentAssertions — bereits vorhanden

---

## 5. Files

### Core Layer
- `src/RomCleanup.Core/Classification/DiscHeaderDetector.cs` — TASK-001–006
- `src/RomCleanup.Core/GameKeys/GameKeyNormalizer.cs` — TASK-151, 152
- `src/RomCleanup.Core/Regions/RegionDetector.cs` — TASK-153
- `src/RomCleanup.Core/Rules/RuleEngine.cs` — TASK-150, 155, 156
- `src/RomCleanup.Core/Scoring/VersionScorer.cs` — TASK-154
- `src/RomCleanup.Core/Classification/ConsoleDetector.cs` — TASK-157

### Contracts Layer
- `src/RomCleanup.Contracts/Models/DatIndex.cs` — TASK-007–010
- `src/RomCleanup.Contracts/Models/RomCandidate.cs` — TASK-210
- `src/RomCleanup.Contracts/Models/ServiceModels.cs` — TASK-206
- `src/RomCleanup.Contracts/Models/PipelineModels.cs` — TASK-207
- `src/RomCleanup.Contracts/Models/EventBusModels.cs` — TASK-211
- `src/RomCleanup.Contracts/Errors/ErrorClassifier.cs` — TASK-209

### Infrastructure Layer
- `src/RomCleanup.Infrastructure/Deduplication/CrossRootDeduplicator.cs` — TASK-011
- `src/RomCleanup.Infrastructure/Deduplication/FolderDeduplicator.cs` — TASK-012–019
- `src/RomCleanup.Infrastructure/Audit/AuditCsvStore.cs` — TASK-158, 159, 162
- `src/RomCleanup.Infrastructure/Audit/AuditSigningService.cs` — TASK-158–161
- `src/RomCleanup.Infrastructure/Orchestration/RunOrchestrator.cs` — TASK-163–165, 208
- `src/RomCleanup.Infrastructure/Orchestration/ExecutionHelpers.cs` — TASK-166, 167
- `src/RomCleanup.Infrastructure/FileSystem/FileSystemAdapter.cs` — TASK-168, 169
- `src/RomCleanup.Infrastructure/Hashing/ParallelHasher.cs` — TASK-170
- `src/RomCleanup.Infrastructure/Hashing/ArchiveHashService.cs` — TASK-174, 175
- `src/RomCleanup.Infrastructure/Dat/DatRepositoryAdapter.cs` — TASK-172, 173
- `src/RomCleanup.Infrastructure/Dat/DatSourceService.cs` — TASK-171
- `src/RomCleanup.Infrastructure/Conversion/FormatConverterAdapter.cs` — TASK-176
- `src/RomCleanup.Infrastructure/Conversion/ConversionPipeline.cs` — TASK-177, 180
- `src/RomCleanup.Infrastructure/Tools/ToolRunnerAdapter.cs` — TASK-178, 179, 181
- `src/RomCleanup.Infrastructure/Events/EventBus.cs` — TASK-182, 183
- `src/RomCleanup.Infrastructure/History/RunHistoryService.cs` — TASK-184
- `src/RomCleanup.Infrastructure/History/ScanIndexService.cs` — TASK-185
- `src/RomCleanup.Infrastructure/Sorting/ConsoleSorter.cs` — TASK-186
- `src/RomCleanup.Infrastructure/Analytics/InsightsEngine.cs` — TASK-187, 190
- `src/RomCleanup.Infrastructure/State/AppStateStore.cs` — TASK-188
- `src/RomCleanup.Infrastructure/Logging/JsonlLogWriter.cs` — TASK-189
- `src/RomCleanup.Infrastructure/Safety/SafetyValidator.cs` — TASK-191
- `src/RomCleanup.Infrastructure/Configuration/SettingsLoader.cs` — TASK-192, 193
- `src/RomCleanup.Infrastructure/Quarantine/QuarantineService.cs` — TASK-194
- `src/RomCleanup.Infrastructure/Diagnostics/CatchGuardService.cs` — TASK-195
- `src/RomCleanup.Infrastructure/Linking/HardlinkService.cs` — TASK-196
- `src/RomCleanup.Infrastructure/Metrics/PhaseMetricsCollector.cs` — TASK-212
- `src/RomCleanup.Infrastructure/Reporting/ReportGenerator.cs` — (Audit 1 Coverage)

### WPF Layer
- `src/RomCleanup.UI.Wpf/MainWindow.xaml` — TASK-031, 032, 084, 085, 086–091, 097, 102–107, 127
- `src/RomCleanup.UI.Wpf/MainWindow.xaml.cs` — TASK-020–030, 033–040, 071–078, 094, 095, 110–114, 117–126, 131–145
- `src/RomCleanup.UI.Wpf/ViewModels/MainViewModel.cs` — TASK-032, 037, 092, 093, 111, 116
- `src/RomCleanup.UI.Wpf/Services/FeatureService.cs` — TASK-022, 023, 030, 041–050, 051–055, 078, 126, 140–142
- `src/RomCleanup.UI.Wpf/Services/SettingsService.cs` — TASK-061, 062
- `src/RomCleanup.UI.Wpf/Services/ThemeService.cs` — TASK-101
- `src/RomCleanup.UI.Wpf/Services/StatusBarService.cs` — TASK-115
- `src/RomCleanup.UI.Wpf/Themes/SynthwaveDark.xaml` — TASK-096–098
- `src/RomCleanup.UI.Wpf/Themes/Light.xaml` — TASK-096–098
- `src/RomCleanup.UI.Wpf/App.xaml.cs` — TASK-145

### Entry Points
- `src/RomCleanup.CLI/Program.cs` — TASK-197, 201, 203
- `src/RomCleanup.Api/Program.cs` — TASK-199, 200, 204
- `src/RomCleanup.Api/RunManager.cs` — TASK-198, 202, 205

### CI/CD
- `.github/workflows/test-pipeline.yml` — TASK-064–070

---

## 6. Testing

- **TEST-001**: ReDoS-Pattern-Tests: TASK-001, 150 — adversarial Regex mit Timeout-Assertion
- **TEST-002**: XXE-Tests: TASK-020–023 — XML mit externen Entities muss abgelehnt werden
- **TEST-003**: Path-Traversal-Tests: TASK-014, 024, 026, 192, 194, 196, 203 — `../`-Angriffe
- **TEST-004**: Determinismus-Tests: TASK-151, 164 — gleiche Inputs = gleiche Outputs
- **TEST-005**: Sync-over-Async Deadlock-Tests: TASK-170, 171 — Timeout-basiert
- **TEST-006**: Data-Binding-Tests: TASK-084–091 — UI-Automation oder VM-Property-Checks
- **TEST-007**: Theme-Tests: TASK-094, 095 — Visueller Vergleich beider Themes
- **TEST-008**: Accessibility-Tests: TASK-102–107 — Accessibility Insights Scan
- **TEST-009**: CSV-Injection-Tests: TASK-047, 162, 190 — führende Sonderzeichen
- **TEST-010**: CultureInfo-Tests: TASK-155 — de-DE Locale mit Dezimalkomma

---

## 7. Risks & Assumptions

- **RISK-001**: P0-XXE-Fixes (TASK-020–023) erfordern neue `SafeLoadXDocument`-Helper → Breaking Change wenn externe Caller existieren
- **RISK-002**: TASK-164 (GameKey mit Extension) behebt einen fundamentalen Deduplizierungs-Bug → alle bisherigen Audit-Logs werden ungültig (andere Gruppierung)
- **RISK-003**: MVVM-Refactoring (TASK-110, 111) ist umfangreich (~3200 Zeilen) → hohes Regressions-Risiko, stufenweises Vorgehen empfohlen
- **RISK-004**: P1 Sync-over-Async Deadlocks (TASK-170, 171) treten nur auf WPF UI-Thread auf, nicht in CLI/API → Priorität hängt von UI-Nutzung ab
- **ASSUMPTION-001**: Alle Findings basieren auf Code-Review, nicht auf Runtime-Tests → manche Bugs treten nur unter spezifischen Bedingungen auf
- **ASSUMPTION-002**: WPF-Layer (Audit 1 + 2) ist der größte Bug-Hotspot (~130 von 212 Findings) → hier liegt der größte ROI für Fixes
- **ASSUMPTION-003**: P0-Fixes sind unabhängig voneinander implementierbar → kein sequentielles Blocking

---

## 8. Related Specifications / Further Reading

- [plan/feature-deep-dive-bug-audit-1.md](plan/feature-deep-dive-bug-audit-1.md) — Original Audit 1
- [plan/feature-deep-dive-ux-ui-audit-2.md](plan/feature-deep-dive-ux-ui-audit-2.md) — Original Audit 2
- [plan/feature-deep-dive-remaining-audit-3.md](plan/feature-deep-dive-remaining-audit-3.md) — Original Audit 3
- [docs/REVIEW_CHECKLIST.md](docs/REVIEW_CHECKLIST.md) — Code Review Checkliste
- [docs/TEST_STRATEGY.md](docs/TEST_STRATEGY.md) — Test-Strategie
- [docs/ARCHITECTURE_MAP.md](docs/ARCHITECTURE_MAP.md) — Architektur-Übersicht
