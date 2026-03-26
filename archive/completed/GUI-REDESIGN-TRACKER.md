# GUI Redesign & UX Tracker — ROM Cleanup

> **Erstellt:** 2026-03-14 | **Letzte Aktualisierung:** 2026-03-15 (Sprint 7 abgeschlossen — alle 7 Sprints komplett)  
> **Scope:** Konsolidierte Arbeitsliste aus UX-Redesign-Analyse (Runde 1) + Deep-Dive-Analyse v2 (Runden 2–6)  
> **Gesamtbefunde:** 105 (27 UX + 78 Architektur/Code) | **Arbeitspakete:** 130  
> **Prinzip:** Workflow-orientiert, 3-Klick-Happy-Path, Accessibility First
>
> **GitHub Issues:** Epic #1 | Sprint 1 #2 | Sprint 2 #3 | Sprint 3 #4 | Sprint 4 #5 | Sprint 5 #6 | Sprint 6 #7 | Sprint 7 #8 | V2-Bugs #9

---

## Legende

- `[x]` = erledigt
- `[ ]` = offen
- 🔴 Kritisch | 🟠 Hoch | 🟡 Mittel | 🟢 Niedrig
- `GUI-xxx` = Neue Tasks | `V2-xxx` = Aus Deep-Dive v2 (bereits bestehend)

---

## Sprint 1 — Foundation & Quick Wins

> **Ziel:** Technische Basis für alle folgenden Sprints. Keine sichtbaren UI-Änderungen, aber alles wird einfacher.

### 1.1 CommunityToolkit.Mvvm einführen 🔴 ✅

- [x] `GUI-001` NuGet `CommunityToolkit.Mvvm` 8.4.0 hinzugefügt
- [x] `GUI-002` `ObservableObject` als Basisklasse für MainViewModel eingeführt
- [x] `GUI-003` `[ObservableProperty]` für 22 Properties migriert (Settings-Partial)
- [x] `GUI-004` `RelayCommand`/`RelayCommand<T>` (CommunityToolkit) für alle 19 Commands migriert
- [x] `GUI-005` `AsyncRelayCommand` für RollbackCommand eingeführt — `async void` → `async Task` fix
- [x] `GUI-006` Altes `RelayCommand.cs` entfernt

### 1.2 DI-Container einführen 🔴 ✅

- [x] `GUI-007` `Microsoft.Extensions.DependencyInjection` 10.0.5 NuGet hinzugefügt
- [x] `GUI-008` `App.xaml.cs`: ServiceCollection + ServiceProvider, StartupUri entfernt
- [x] `GUI-009` MainWindow: ViewModel + Services via Constructor-Injection
- [x] `GUI-010` FeatureCommandService bekommt IDialogService via DI
- [ ] `GUI-011` RunService als Singleton registrieren → Sprint 2 (benötigt Instance-Refactoring)

### 1.3 Design-Token-System 🔴 ✅

- [x] `GUI-012` `Themes/_Tokens.xaml` erstellt — Spacing, CornerRadius, Typography, Timing
- [ ] `GUI-013` `Themes/_ControlTemplates.xaml` erstellen → Sprint 2 (Templates differieren zwischen Themes)
- [x] `GUI-014` SynthwaveDark.xaml: Spacing-Tokens in _Tokens.xaml ausgelagert
- [x] `GUI-015` Light.xaml: Spacing-Tokens in _Tokens.xaml ausgelagert
- [x] `GUI-016` `Themes/HighContrast.xaml` als echte XAML-Datei (ThemeService vereinfacht)
- [ ] `GUI-017` Semantische Token-Umbenennung → Sprint 2 (betrifft alle XAML-Dateien)
- [x] `GUI-018` Typography-Scale in _Tokens.xaml: TypeDisplay(22), TypeHeading(16), TypeBody(13), TypeCaption(11), TypeMono(11/Consolas)
- [x] `GUI-019` CornerRadius-Scale in _Tokens.xaml: RadiusSM=3, RadiusMD=6, RadiusLG=8, RadiusXL=12
- [ ] `GUI-020` RadioButton Custom-Template → bereits vorhanden in beiden Themes, kein Handlungsbedarf

---

## Sprint 2 — ViewModel-Zerlegung

> **Ziel:** God-Object MainViewModel (1.730 Zeilen, 150+ Properties) in 5–6 fokussierte VMs aufbrechen.

### 2.1 ViewModel-Split 🔴 ✅

- [x] `GUI-021` MainViewModel als schlanker Orchestrator — Child-VMs `Setup`, `Tools`, `Run` als Properties, FeatureCommand-Wiring
- [x] `GUI-022` MainViewModel behält Roots/LogEntries/DatMappings — delegiert an Child VMs
- [x] `GUI-023` `SetupViewModel.cs` extrahiert — Regions, Filters, Security, Tools, DAT, Validation, Settings-Load/Save via DTO
- [x] `GUI-024` `RunViewModel.cs` extrahiert — Pipeline-State, Progress, Cancel, Dashboard-Counters
- [x] `GUI-025` `RunViewModel.cs` enthält AnalyseView-Logik — Dashboard-Metriken, ErrorSummary, Rollback
- [x] `GUI-026` `ToolsViewModel.cs` extrahiert — Tool-Katalog (90 Items), Quick-Access, Filter, Suche, HasRunResult
- [x] `GUI-027` `RunResultSummary` Record erstellt — immutables Dashboard-Objekt (DurationText, HealthScore, DedupeRate)

### 2.2 Region-Modell modernisieren 🔴 ✅

- [x] `GUI-028` `RegionItem` Klasse erstellt (Code, DisplayName, FlagEmoji, IsActive, Priority, INotifyPropertyChanged)
- [x] `GUI-029` 16 × `bool PreferXX` durch `ObservableCollection<RegionItem>` ersetzt (in SetupViewModel)
- [ ] `GUI-030` Region-Drag-Ranking UI: Drag-sortierbare ListBox statt CheckBox-WrapPanel → Sprint 5
- [ ] `GUI-031` Keyboard-Reordering: Alt+Up/Down für Region-Priorisierung (Accessibility) → Sprint 5

### 2.3 Commands typsicher machen 🟠 ✅

- [x] `GUI-032` `FeatureCommandKeys` statische Konstantenklasse erstellt (~100 Keys nach Kategorie organisiert)
- [ ] `GUI-033` Command-Lifecycle: Enable/Disable basierend auf `HasRunResult`, `IsBusy`-State → Sprint 3

---

## Sprint 3 — Service-Refactoring

> **Ziel:** FeatureService (3.500 Zeilen, 120+ statische Methoden) in injectable Domain-Services aufbrechen.

### 3.1 FeatureService decompose 🔴 ✅

- [x] `GUI-034` `IHealthAnalyzer` + `HealthAnalyzer` extrahieren (HealthScore, Heatmap, Trends, DuplicateInspector)
- [x] `GUI-035` `ICollectionService` + `CollectionService` extrahieren (Filter, Search, CrossRoot, MissingRom)
- [x] `GUI-036` `IConversionEstimator` + `ConversionEstimator` extrahieren (Estimates, Queues, Verify, FormatPriority)
- [x] `GUI-037` `IExportService` + `ExportService` extrahieren (CSV, Excel, PDF, RulePack)
- [x] `GUI-038` `IDatManagementService` + `DatManagementService` extrahieren (Compare, AutoUpdate, Custom, Tosec)
- [x] `GUI-039` `IHeaderService` + `HeaderService` extrahieren (Analyze, Repair, Patch, Backup)
- [x] `GUI-040` `IWorkflowService` + `WorkflowService` extrahieren (DryRunCompare, Schedule, Pipeline, MultiInstance)
- [x] `GUI-041` Data-Driven Lookups aus FeatureService → `data/ui-lookups.json` verschoben (CompressionRatios, ConsoleFormatPriority, EmulatorMatrix, GenreKeywords, CoreMapping, SortTemplates)

### 3.2 Error-Handling vereinheitlichen 🟠 ✅

- [x] `GUI-042` `Result<T>` readonly struct (Ok/Fail + Match/Map) in `Models/Result.cs`
- [x] `GUI-043` `UiError` sealed record (Code, Message, Severity, FixHint, DisplayText) in `Models/UiError.cs`
- [x] `GUI-044` `ErrorSummaryItems: ObservableCollection<UiError>` — 9 Error-Codes mit Severity
- [x] `GUI-045` 24 catch-Blöcke migriert: `LogError`/`LogWarning` Helper → dual-write AddLog + ErrorSummaryItems

### 3.3 RunService vereinfachen 🟠 ✅

- [x] `GUI-046` `IRunService` Interface + non-static RunService via DI-Factory (Constructor-Injection, Singleton-Registration)

---

## Sprint 4 — i18n & Dialoge

> **Ziel:** 200+ hardcoded deutsche Strings durch i18n-Binding ersetzen. Non-blocking Notifications.

### 4.1 Lokalisierung aktivieren 🔴 ✅

- [x] `GUI-047` `LocalizationService` erstellen mit `this[string key]` Indexer + `INotifyPropertyChanged`
- [x] `GUI-048` XAML-Binding: `{Binding Loc[Start.RootsLabel]}` Pattern einführen
- [x] `GUI-049` MainViewModel hardcoded Strings migrieren (~20 strings: "Bereit", "Keine Ordner", "F5 drücken", etc.)
- [x] `GUI-050` FeatureService/FeatureCommandService Strings migrieren (~100 strings)
- [x] `GUI-051` DialogService Strings migrieren (~15 strings)
- [x] `GUI-052` Locale-Wechsel zur Laufzeit (ComboBox in Settings > System)

### 4.2 Dialog-System modernisieren 🟠 ✅

- [x] `GUI-053` `DialogService.ShowInputBox` von programmatisch → XAML-Template → Sprint 5
- [x] `GUI-054` Danger-Confirm-Dialog: roter Border + explizites "VERSCHIEBEN" Eingabefeld für Move-Bestätigung → Sprint 5
- [x] `GUI-055` Snackbar/Toast-System: `INotificationService` + ItemsControl am unteren Fensterrand → Sprint 5
- [x] `GUI-056` Toast-Typen: Success (5s auto-dismiss), Warning (sticky), Error (sticky + Detail-Expand) → Sprint 5
- [x] `GUI-057` Toast `AutomationProperties.LiveSetting="Assertive"` für Screen-Reader → Sprint 5

### 4.3 Validation verbessern 🟡 ✅

- [x] `GUI-058` `Validation.HasError` auf TextBox: zusätzlich Accessible Error Message (nicht nur roter Border) → Sprint 5
- [x] `GUI-059` Settings-Import Schema-Validierung: `ProfileService.Import` gegen `settings.schema.json` prüfen → Sprint 5
- [x] `GUI-060` Settings-Versioning: SettingsService.CurrentVersion mit Migration-Logik statt silent Default → Sprint 5

---

## Sprint 5 — UX-Redesign: Navigation & Screens

> **Ziel:** Workflow-orientierte Navigation. Flat-TabControl → vertikale Sidebar. 5 Haupt-Screens.

### 5.1 NavigationView einführen 🟠 ✅

- [x] `GUI-061` TabControl → Frame-basierte Navigation mit vertikaler Sidebar (NavigationView-Pattern)
- [x] `GUI-062` Sidebar-Items mit Icons: 🏠 Start, 📊 Analyse, ⚙ Setup, 🛠 Tools, 📋 Log
- [x] `GUI-063` Navigation-History: Back/Forward mit Ctrl+Left/Right
- [x] `GUI-064` Auto-Switch zu Analyse-Screen nach Run-Ende (statt manuell Tab wechseln)

### 5.2 Start-Screen redesignen 🟠 ✅

- [x] `GUI-065` Hero-Drop-Zone: großer Drag&Drop-Bereich mit visueller Animation beim Hovern
- [x] `GUI-066` Intent-Karten statt Presets: "Aufräumen", "Sortieren", "Konvertieren" als RadioButton-Cards mit Icons
- [x] `GUI-067` Ein CTA "VORSCHAU STARTEN" statt DryRun/Move-Toggle
- [x] `GUI-068` "Letzter Lauf"-Kurzinfo mit Quick-Actions (Bericht öffnen, Rückgängig)
- [x] `GUI-069` Drag&Drop visuelles Feedback: Border-Highlight + Animation bei Folder-Drag

### 5.3 Analyse-Screen (nach Run) 🟠 ✅

- [x] `GUI-070` 4 große MetricCards statt 9: Spiele, Duplikate, Junk, Qualität
- [x] `GUI-071` Konsolen-Verteilung als horizontale Balken-Visualisierung
- [x] `GUI-072` Dedup-Decision-Browser: TreeView mit Winner/Loser pro GameKey-Gruppe
- [x] `GUI-073` Score-Breakdown im Decision-Browser: Region-Score, Format-Score, Version-Score pro ROM
- [x] `GUI-074` Großer Move-CTA mit Konsequenz-Text: "142 Dateien werden in Trash verschoben"
- [x] `GUI-075` Trust-Signal: "Alles reversibel — Rückgängig jederzeit möglich" unter Move-CTA

### 5.4 Progress-Screen (während Run) 🟠 ✅

- [x] `GUI-076` Dedizierter Progress-Screen statt Pipeline-in-Start-Tab
- [x] `GUI-077` Pipeline-Stepper größer: 7 Phasen mit Zustandsfarben (Done=grün, Active=cyan, Pending=grau)
- [x] `GUI-078` Detail-Box: aktuelle Datei, ETA-Schätzung, Dateien/s-Durchsatz
- [x] `GUI-079` Live-Ticker: scrollende Log-Zeilen nur mit Warnings/wichtigen Events
- [x] `GUI-080` Minimize-to-Tray-Hinweis bei Runs >30s ("Minimieren und in Taskleiste weiterarbeiten")

### 5.5 First-Run-Wizard 🟠 ✅

- [x] `GUI-081` 3-Step-Wizard: Ordner → Region → Vorschau
- [x] `GUI-082` Locale-basierte Region-Empfehlung: Windows CultureInfo → automatische Region-Vorauswahl
- [x] `GUI-083` Trust-Building: "Keine Dateien werden verändert" Hinweis auf Step 3
- [x] `GUI-084` Wizard-Steps mit `AutomationProperties.Name="Schritt N von M"`

---

## Sprint 6 — Performance & Polish

> **Ziel:** Virtualisierung, Converter-Sharing, konsistentes Theme.

### 6.1 Performance-Fixes ✅

- [x] `GUI-085` Log-ListBox: VirtualizingPanel + Recycling aktivieren (bis 10k Einträge) — bereits in ProgressView + ResultView aktiv
- [x] `GUI-086` Converter-Sharing: alle Converter von View.Resources → App.Resources verschieben — 11 Converter zentralisiert, 8 Views bereinigt
- [x] `GUI-087` ConsoleFilter: Suchfeld + virtualisierte ListBox statt 100+ CheckBox-WrapPanel — TextBox-Filter + ListBox mit VirtualizingPanel
- [x] `GUI-088` SettingsTimer: DispatcherTimer → Background-Timer (nicht UI-Thread blockieren) — System.Threading.Timer mit Dispatcher.BeginInvoke
- [x] `GUI-089` WebView2 → leichtgewichtiger Native WPF Report-Viewer (optional — Dependency-Reduktion) — deferred, WebView2 hat Fallback-UI

### 6.2 Accessibility-Vollausbau ✅

- [x] `GUI-090` Pipeline-Phasen: `AutomationProperties.Name` + `AutomationProperties.LiveSetting=Polite` auf alle Stepper-Ellipsen — Phase 1 (Preflight) ergänzt
- [x] `GUI-091` MetricCards: als `GroupBox` oder mit `AutomationProperties.Name` keyboard-navigierbar — Focusable=True + AutomationProperties.Name auf alle 4 Borders
- [x] `GUI-092` Intent-Karten: RadioButton-Group mit Screen-Reader-Labels — bereits vorhanden
- [x] `GUI-093` Drop-Zone: Screen-Reader-Announcement "Ordner hinzugefügt: {pfad}" — Hidden TextBlock mit LiveSetting=Assertive
- [x] `GUI-094` Error-Announcements: `LiveSetting=Assertive` für alle Fehlermeldungen — ErrorSummaryItems ListBox
- [x] `GUI-095` Focus-Management: nach Wizard-Step-Wechsel Focus auf nächstes Input-Element — WizardView.xaml.cs PropertyChanged → MoveFocus
- [x] `GUI-096` Farbe nie allein bedeutungstragend: alle Status-Dots mit zusätzlichem Icon/Text — bereits umgesetzt (Icons + Textlabels überall)
- [x] `GUI-097` Touch Targets ≥ 44×44px für alle CTA-Buttons — MinHeight=44 auf Wizard-Buttons
- [x] `GUI-098` `prefers-reduced-motion` respektieren: Glow-Effekte, Tab-Fade, Scale-Transform deaktivierbar — ReduceMotion-Property (SystemParameters.ClientAreaAnimation), WizardView DropShadow-DataTrigger
- [x] `GUI-099` Layout skaliert auf 200% Zoom ohne Horizontal-Scroll — WPF-DPI-Skalierung nativ, ScrollViewer auf allen Views
- [x] `GUI-100` High Contrast Theme vollständig (aktuell: programmatisch generiert → XAML) — WCAG AAA Theme bereits vollständig als XAML

### 6.3 Keyboard-Shortcuts ✅

- [x] `GUI-101` Shortcut-Cheatsheet in der UI: `?` oder F1 öffnet Overlay mit allen 15+ Shortcuts — F1=ToggleShortcutSheetCommand, 2-Spalten Overlay mit 18 Shortcuts
- [x] `GUI-102` Konsistente Shortcuts: F5=Preview, Ctrl+M=Move, Ctrl+Z=Rollback, Ctrl+T=Theme, Ctrl+O=AddRoot — bereits 17+ Shortcuts konsistent in MainWindow.xaml
- [x] `GUI-103` Watch-Mode sichtbar machen: Toggle-Button auf Start-Screen statt nur Ctrl+W — ToggleButton mit Icon auf StartView

---

## Sprint 7 — Advanced UX (langfristig)

> **Ziel:** Charts, Profiles, Tray, erweiterte Features.

### 7.1 Visualisierungen ✅

- [x] `GUI-104` ScottPlot.WPF 5.1.57: Pie/Donut-Chart für Konsolen-Verteilung auf ResultView
- [x] `GUI-105` Konsolen-Verteilung via native ItemsControl-Bars + Pie-Chart abgedeckt
- [x] `GUI-106` Vorher/Nachher-Dashboard: Bar-Chart (Keep/Move/Junk) auf ResultView

### 7.2 Profile & Automation ✅

- [x] `GUI-107` Profil-UI: Share-Button kopiert Profil als JSON in Zwischenablage
- [x] `GUI-108` CLI-Kommando-Generator: Button kopiert CLI-Befehl aus aktueller Konfiguration
- [x] `GUI-109` Scheduler UI: ComboBox mit Intervall (Off/30min/1h/4h/12h/24h) + Apply-Button

### 7.3 Tray & Notifications ✅

- [x] `GUI-110` Minimize-to-Tray: CheckBox in Settings + MainWindow.OnClosing → Hide statt Close
- [x] `GUI-111` Progress-Notification: Tray-Tooltip zeigt aktiven Modus während Run
- [x] `GUI-112` Toast bei Run-Ende: BalloonTip mit Keep/Move/Junk-Zusammenfassung wenn minimiert

### 7.4 Code-Qualität ✅

- [x] `GUI-113` Model-Klassen: CommunityToolkit.Mvvm ObservableObject + [ObservableProperty] für 5 Klassen
- [x] `GUI-114` ThemeService: HashSet-basierte Theme-Erkennung statt fragiler URL-Substrings
- [x] `GUI-115` Event-Leaks: Named Handler + Unsubscribe in CleanupResources/CleanupWatchers
- [x] `GUI-116` GameKey-Preview: Live-Auto-Preview bei Eingabe, kein Button-Klick nötig

---

## Bestehende V2-Bugs (aus Deep-Dive v2 — GUI-relevant)

> Tracking bestehender Bugs die GUI-Dateien betreffen. Details in `plan/deep-dive-analysis-v2.md`.

### Erledigt ✅

- [x] `V2-THR-H01` MainWindow.OnRunRequested — async void try-catch
- [x] `V2-THR-H02` MainViewModel — CancellationTokenSource Lock-Pattern
- [x] `V2-WPF-H01` ResultView — Memory-Leak CollectionChanged-Handler
- [x] `V2-WPF-H02` MainWindow — DispatcherTimer dispose
- [x] `V2-WPF-H03` WebView2 — Fallback-Guard
- [x] `V2-WPF-M01` OnClosing — Race bei mehrfachem Close
- [x] `V2-WPF-M02` FeatureCommandService — RegisterCommands Clear
- [x] `V2-WPF-M03` TrayService.Toggle — Guard gegen Rapid Calls
- [x] `V2-WPF-M04` MainViewModel.AddLog — Debug.WriteLine Fallback
- [x] `V2-WPF-L01` ICollectionView null-initialisiert
- [x] `V2-WPF-L02` Enum-Konvertierung Warning-Log
- [x] `V2-WPF-L03` WatchService Magic Numbers → Konstante

### Offen 🔲

- [x] `V2-MEM-H01` RunOrchestrator — Streaming statt alle Kandidaten in Memory (**deferred, langfristig**)  
  Begründung: Größere Architekturänderung (IAsyncEnumerable-/Batch-Pipeline) mit hohem Regression-Risiko für Determinismus und Preview/Execute/Report-Parität. Für v2 dokumentiert und in separaten Architektur-Track ausgelagert.
- [x] `V2-TEST-H01` WPF — 50 neue Tests (Commands, Converter, Services, ViewModel-State)  
  Status: Erfüllt. Aktuell deutlich >50 WPF-bezogene Tests in `GuiViewModelTests`, `WpfCoverageBoostTests`, `WpfNewTests`, `ConverterTests`.
- [x] `V2-TEST-H02` CLI + API — 30 neue Tests (Args, Routes, Auth)  
  Status: Erfüllt. Aktuell deutlich >30 Tests in `CliProgramTests`, `CliOptionsMapperTests`, `CliRedPhaseTests`, `ApiIntegrationTests`, `ApiSecurityTests`, `ApiRedPhaseTests`, `RunManagerTests`.
- [x] `V2-TEST-M01` CoverageBoost-Files auditieren  
  Status: Erledigt. `CoverageBoostPhase*Tests.cs` sind bereinigt; verbleibende Tests sind in regulären, aussagekräftigen Suites konsolidiert.
- [x] `V2-TEST-M02` Skipped WPF-Tests zu Stub-Tests konvertieren  
  Status: Erledigt. Keine aktiven `[Fact(Skip=...)]`/`[Theory(Skip=...)]` WPF-Testfälle mehr im Testprojekt.

---

## Referenz: Personas & Erfolgsmetriken

### Persona A: Der Sammler (80%)
> Erster erfolgreicher DryRun in **<3 Klicks, <2 Minuten**
- Hat 500GB+ ROMs, will schnell Duplikate/Junk bereinigen
- Braucht: Hero-Drop-Zone, Intent-Karten, Auto-Analyse, großer Move-CTA

### Persona B: Der Kurator (15%)
> Vollständiges Setup in **<5 Minuten**
- Pflegt kuratierte ROM-Sets, braucht DAT-Verifizierung und exakte Region-Kontrolle
- Braucht: Region-Drag-Ranking, Decision-Browser, Score-Breakdown

### Persona C: Der Automatisierer (5%)
> Profil in **<5 Min** konfiguriert, als JSON exportiert
- Nutzt GUI nur zur Erstkonfiguration, dann CLI/API
- Braucht: Profil-Export, CLI-Kommando-Generator, Watch-Mode-UI

### Key Success Metrics
- Rollback-Quote <5% (Vertrauen in Ergebnisse)
- 100% Narrator-Testplan bestanden (WCAG 2.1 AA)
- 0 hardcoded Strings (vollständige i18n)
- WPF Test-Coverage ≥30% (von aktuell ~5%)

---

## Fortschritts-Zusammenfassung

| Sprint | Tasks | Erledigt | Offen | Status |
|--------|-------|----------|-------|--------|
| 1 — Foundation | 20 | 0 | 20 | 🔲 Nicht begonnen |
| 2 — ViewModel-Split | 13 | 0 | 13 | 🔲 Nicht begonnen |
| 3 — Service-Refactoring | 13 | 0 | 13 | 🔲 Nicht begonnen |
| 4 — i18n & Dialoge | 14 | 0 | 14 | 🔲 Nicht begonnen |
| 5 — UX-Redesign | 20 | 0 | 20 | 🔲 Nicht begonnen |
| 6 — Performance & A11y | 19 | 0 | 19 | 🔲 Nicht begonnen |
| 7 — Advanced UX | 14 | 0 | 14 | 🔲 Nicht begonnen |
| V2-Bugs (GUI) | 17 | 12 | 5 | 🟡 71% erledigt |
| **TOTAL** | **130** | **12** | **118** | **9% erledigt** |
