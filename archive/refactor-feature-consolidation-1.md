---
goal: Feature-Landschaft konsolidieren – von 87 auf ~50 release-taugliche Tools bereinigen
version: 1.0
date_created: 2026-03-26
last_updated: 2026-03-26
owner: Romulus Team
status: 'Completed'
tags: [refactor, chore, architecture, cleanup, ui, release-readiness]
---

# Introduction

![Status: In Progress](https://img.shields.io/badge/status-In%20Progress-orange)

Vollständige Konsolidierung der sichtbaren Feature-Landschaft von Romulus. Ein systematischer Feature-Audit hat ergeben, dass von 87 registrierten Tool-Karten 16 sofort entfernt, 5 deaktiviert, 10 konsolidiert und 5 repariert/umbenannt werden müssen. Der Kern (Pipeline, Dedupe, Sort, Convert, Audit, Rollback) ist produktionsreif mit 5.400+ Tests. Die Tool-Karten-Landschaft ist zu breit, enthält Stubs, Blendwerk, Code-Duplikate und irreführende Benennungen.

Zielzustand: ~50 ehrliche, vollständig implementierte, getestete und korrekt benannte Features.

## 1. Requirements & Constraints

- **REQ-001**: Jedes sichtbare Feature muss echte Funktionalität bieten — keine Dialoge mit "nicht implementiert" oder "Coming Soon"
- **REQ-002**: Keine Code-Duplikate zwischen Features (CollectionDiff≡DryRunCompare, PluginMarketplace≡PluginManager etc.)
- **REQ-003**: Feature-Namen müssen korrekt beschreiben was das Feature tut (kein "PDF-Report" für HTML-Output)
- **REQ-004**: Alle `IsPlanned=true` Features die nicht implementiert werden sollen, müssen aus dem Tool-Katalog entfernt werden — nicht nur versteckt
- **REQ-005**: Entfernte Features dürfen keine verwaisten i18n-Keys, toten Handler-Code oder ungenutzte FeatureService-Methoden hinterlassen
- **SEC-001**: Path-Traversal-Schutz in verbleibenden Features beibehalten (ToolImport, CustomDatEditor)
- **SEC-002**: CSV-Injection-Schutz in Export-Features beibehalten
- **CON-001**: Kern-Pipeline (RunOrchestrator) wird NICHT verändert — nur die UI-Tool-Karten-Schicht
- **CON-002**: Bestehende 5.400+ Tests müssen nach jeder Phase grün bleiben
- **CON-003**: API-Endpoints und CLI bleiben unverändert
- **CON-004**: Alle Änderungen in `src/RomCleanup.UI.Wpf/` — keine Core/Infrastructure/Contracts-Änderungen
- **GUD-001**: Nach Konsolidierung soll jede Tool-Kategorie maximal 8 Karten enthalten
- **GUD-002**: DefaultPinnedKeys aktualisieren — keine entfernten Tools in Schnellzugriff
- **PAT-001**: Entfernung = Tool-Registration in ToolsViewModel + Command-Registration in FeatureCommandService + Handler-Methode + FeatureService-Methode + i18n-Keys (alle 3 Sprachen) + MainViewModel.Filters-Registrierung

## 2. Implementation Steps

### Phase 1: Stubs und Blendwerk entfernen (6 Features)

- GOAL-001: Alle Features entfernen, die nur Platzhalter-Dialoge ("nicht implementiert", "Coming Soon", "in Planung") anzeigen

- [x] **TASK-001** — **FtpSource entfernen**: ToolItem-Registration in `src/RomCleanup.UI.Wpf/ViewModels/ToolsViewModel.cs` (Zeile ~213), IsPlanned-Zuweisung (Zeile ~229), Duplikat in `src/RomCleanup.UI.Wpf/ViewModels/MainViewModel.Filters.cs` (Zeile ~258, ~274). Command-Registration `cmds["FtpSource"]` in `src/RomCleanup.UI.Wpf/Services/FeatureCommandService.cs`. Handler-Methode `FtpSource()` in `src/RomCleanup.UI.Wpf/Services/FeatureCommandService.Infra.cs` (Zeile ~32). FeatureService-Methode `BuildFtpSourceReport()` in `src/RomCleanup.UI.Wpf/Services/FeatureService.Infra.cs`. i18n-Keys `Tool.FtpSource*` aus `data/i18n/de.json`, `en.json`, `fr.json` entfernen.
- [x] **TASK-002** — **CloudSync entfernen**: ToolItem (ToolsViewModel ~214, ~229), MainViewModel.Filters (~259, ~274). Command `cmds["CloudSync"]`, Handler `CloudSync()` in FeatureCommandService.Infra.cs (~63). FeatureService `BuildCloudSyncReport()` in FeatureService.Infra.cs. i18n-Keys `Tool.CloudSync*` aus allen 3 Sprachen.
- [x] **TASK-003** — **PluginMarketplaceFeature entfernen**: ToolItem (ToolsViewModel ~215, ~229), MainViewModel.Filters (~260, ~274). Command `cmds["PluginMarketplaceFeature"]`, Handler `PluginMarketplace()` in FeatureCommandService.Infra.cs (~75). FeatureService `GetPluginMarketplaceStatus()` in FeatureService.Infra.cs. i18n-Keys `Tool.PluginMarketplaceFeature*` aus allen 3 Sprachen.
- [x] **TASK-004** — **PluginManager entfernen**: ToolItem (ToolsViewModel ~216, ~229), MainViewModel.Filters (~261, ~274). Command `cmds["PluginManager"]` in FeatureCommandService.cs (~83). Handler `PluginManager()` in FeatureCommandService.cs (~461). FeatureService `GetInstalledPlugins()` in FeatureService.Infra.cs. i18n-Keys `Tool.PluginManager*` aus allen 3 Sprachen.
- [x] **TASK-005** — **GpuHashing entfernen**: ToolItem (ToolsViewModel ~161, ~230), MainViewModel.Filters (~206, ~275). Command `cmds["GpuHashing"]`, Handler `GpuHashing()` in FeatureCommandService.Conversion.cs (~110). FeatureService `BuildGpuHashingStatus()` + `ToggleGpuHashing()` in FeatureService.Conversion.cs. i18n-Keys `Tool.GpuHashing*`.
- [x] **TASK-006** — **ParallelHashing entfernen**: ToolItem (ToolsViewModel ~160, ~230), MainViewModel.Filters (~205, ~275). Command `cmds["ParallelHashing"]`, Handler `ParallelHashing()` in FeatureCommandService.Conversion.cs (~92). FeatureService `BuildParallelHashingReport()` in FeatureService.Conversion.cs. i18n-Keys `Tool.ParallelHashing*`. Referenz in MainViewModel.cs (~395) entfernen.
- [x] **TASK-007** — Build ausführen: `dotnet build src/RomCleanup.sln` — muss fehlerfrei kompilieren
- [x] **TASK-008** — Tests ausführen: `dotnet test src/RomCleanup.Tests/RomCleanup.Tests.csproj --nologo` — alle Tests müssen grün sein

> **Phase 1 abgeschlossen (2026-03-26):** 6 Stub-Features vollständig entfernt. 18 Dateien editiert. Build: 0 Fehler, 0 Warnungen. Tests: 3086 bestanden, 0 fehlgeschlagen, 6 übersprungen. Netto ~36 Tests entfernt (Stub-Alibi-Tests). Zusätzlich bereinigt: `FeatureCommandKeys.cs` (6 Konstanten), `IConversionEstimator.cs` + `ConversionEstimator.cs` (3 Interface-Methoden + Implementierungen), tote `PluginManager()`-Methode in FeatureCommandService.cs Hauptteil. `FeatureService.Dat.BuildFtpSourceReport()` bewusst erhalten (DAT-Layer, nicht Tool-FtpSource).

### Phase 2: Redundante Features entfernen (6 Features)

- GOAL-002: Alle Features entfernen, die 100% Code-Duplikate oder 95%+ Überlappung mit anderen Features haben

- [x] **TASK-009** — **QuickPreview entfernen**: ToolItem (ToolsViewModel ~58, ~138), MainViewModel.Filters (~161, ~183). Command-Registration `cmds["QuickPreview"]` in FeatureCommandService.cs. Handler entfernen. **WICHTIG**: `QuickPreviewCommand` in MainViewModel.cs (~99, ~395) ist ein eigenständiger ICommand — diesen beibehalten und auf DryRun-Preset-Logik umleiten (`PresetSafeDryRunCommand` + `RunCommand`). Keyboard-Shortcut Ctrl+D in MainWindow.xaml auf `PresetSafeDryRunCommand` umverdrahten. Aus `DefaultPinnedKeys` entfernen (ToolsViewModel ~58). i18n-Keys `Tool.QuickPreview*`.
- [x] **TASK-010** — **CollectionDiff entfernen**: ToolItem (ToolsViewModel ~140), MainViewModel.Filters (~185). Command `cmds["CollectionDiff"]` in FeatureCommandService.cs (~71). Handler `CollectionDiff()` in FeatureCommandService.Infra.cs. DryRunCompare (das identische Feature) bleibt. i18n-Keys `Tool.CollectionDiff*`.
- [x] **TASK-011** — **ConvertQueue entfernen**: ToolItem (ToolsViewModel ~157, ~231), MainViewModel.Filters (~202, ~276). Command `cmds["ConvertQueue"]` in FeatureCommandService.cs (~102). Handler `ConvertQueue()` in FeatureCommandService.Conversion.cs (~51). FeatureService `BuildConvertQueueReport()` in FeatureService.Conversion.cs. i18n-Keys `Tool.ConvertQueue*`. ConversionPipeline bleibt als konsolidiertes Feature.
- [x] **TASK-012** — **ConversionEstimate entfernen** (als separates Tool): ToolItem (ToolsViewModel ~142), MainViewModel.Filters (~187). Command `cmds["ConversionEstimate"]` in FeatureCommandService.cs (~87). Handler `ConversionEstimate()` in FeatureCommandService.Analysis.cs (~18) und FeatureCommandService.Conversion.cs (~18/~22). **WICHTIG**: `FeatureService.GetConversionEstimate()` wird von ConversionPipeline weiter genutzt — NUR die Tool-Karte entfernen, nicht die FeatureService-Methode. i18n-Keys `Tool.ConversionEstimate*`.
- [x] **TASK-013** — **TosecDat entfernen**: ToolItem (ToolsViewModel ~166, ~231), MainViewModel.Filters (~211, ~276). Command `cmds["TosecDat"]` in FeatureCommandService.cs (~111). Handler `TosecDat()` in FeatureCommandService.Dat.cs (~281). ToolImport deckt denselben Use-Case ab. i18n-Keys `Tool.TosecDat*`.
- [x] **TASK-014** — **SplitPanelPreview entfernen**: ToolItem (ToolsViewModel ~192), MainViewModel.Filters (~237). Command `cmds["SplitPanelPreview"]`, Handler `SplitPanelPreview()` in FeatureCommandService.Workflow.cs. FeatureService Methoden für Split-Panel-Formatierung. i18n-Keys `Tool.SplitPanelPreview*`.
- [x] **TASK-015** — Build + Tests ausführen — alle grün

> **Phase 2 abgeschlossen (2025-07-24):** 6 redundante Features vollständig entfernt (QuickPreview, CollectionDiff, ConvertQueue, ConversionEstimate, TosecDat, SplitPanelPreview). 12 Quelldateien editiert (ToolsViewModel.cs, MainViewModel.Filters.cs, FeatureCommandService.cs + 4 Partials, FeatureCommandKeys.cs, de.json, en.json, fr.json). 24 Tests entfernt über 5 Testdateien. Build: 0 Fehler, 0 Warnungen. Tests: 3062 bestanden, 0 fehlgeschlagen, 6 übersprungen. QuickPreviewCommand (ICommand) und FeatureService-Methoden (GetConversionEstimate, BuildConvertQueueReport, BuildSplitPanelPreview) bewusst erhalten — Aufräumung in Phase 9.

### Phase 3: Qualitativ unzureichende Features entfernen (3 Features)

> **Phase 3 abgeschlossen (2025-07-25):** 3 qualitativ unzureichende Features vollständig entfernt (PlaytimeTracker, GenreClassification, EmulatorCompat). 11 Quelldateien editiert (ToolsViewModel.cs, MainViewModel.Filters.cs, FeatureCommandService.cs + 2 Partials, FeatureCommandKeys.cs, FeatureService.Conversion.cs, IConversionEstimator.cs, ConversionEstimator.cs, de.json, en.json). 19 Tests entfernt über 7 Testdateien. Emulator-Matrix als § 9 in docs/USER_HANDBOOK.md eingefügt. ClassifyGenre() und IHealthAnalyzer.ClassifyGenre bewusst erhalten (verwendet von BuildCollectionManagerReport). Build: 0 Fehler, 0 Warnungen. Tests: 3043 bestanden, 0 fehlgeschlagen, 6 übersprungen.

- GOAL-003: Features entfernen, die unzuverlässige oder keine echte Funktionalität bieten

- [x] **TASK-016** — **PlaytimeTracker entfernen**: ToolItem (ToolsViewModel ~175, ~232), MainViewModel.Filters (~220, ~277). Command `cmds["PlaytimeTracker"]`, Handler `PlaytimeTracker()` in FeatureCommandService.Collection.cs (~105). i18n-Keys `Tool.PlaytimeTracker*`. Grund: Zählt nur .lrtl-Dateien statt Spielzeiten zu parsen.
- [x] **TASK-017** — **GenreClassification entfernen**: ToolItem (ToolsViewModel ~174, ~232), MainViewModel.Filters (~219, ~277). Command `cmds["GenreClassification"]`, Handler `GenreClassification()` in FeatureCommandService.Collection.cs (~88). i18n-Keys `Tool.GenreClassification*`. Grund: Keyword-Regex auf Dateinamen ist unzuverlässig.
- [x] **TASK-018** — **EmulatorCompat entfernen**: ToolItem (ToolsViewModel ~152, ~232), MainViewModel.Filters (~197, ~277). Command `cmds["EmulatorCompat"]` in FeatureCommandService.cs (~97). Handler `EmulatorCompat()` in FeatureCommandService.Analysis.cs (~246). FeatureService `FormatEmulatorCompat()`. i18n-Keys `Tool.EmulatorCompat*`. Grund: Statische hardcodierte Matrix ohne ROM-Bezug — Inhalt in docs/USER_HANDBOOK.md verschieben.
- [x] **TASK-019** — Emulator-Kompatibilitäts-Matrix als Referenz-Tabelle in `docs/USER_HANDBOOK.md` einfügen (den Inhalt aus `FormatEmulatorCompat()` übernehmen, als Markdown-Tabelle)
- [x] **TASK-020** — Build + Tests ausführen — alle grün

### Phase 4: Features deaktivieren / ausblenden (5 Features)

- GOAL-004: Sichtbare aber unfertige Features aus dem Standard-Katalog nehmen, indem sie komplett aus der Tool-Registrierung entfernt werden (statt nur IsPlanned=true, da "geplant" immer noch sichtbar ist)

- [x] **TASK-021** — **CoverScraper aus Katalog entfernen**: ToolItem (ToolsViewModel), MainViewModel.Filters. Command + Handler in FeatureCommandService.Collection.cs (~39). FeatureService-Methoden BEIBEHALTEN (für späteres Epic). Nur die UI-Sichtbarkeit entfernen. i18n-Keys entfernen.
- [x] **TASK-022** — **CollectionSharing aus Katalog entfernen**: ToolItem, MainViewModel.Filters. Command + Handler in FeatureCommandService.Collection.cs (~124). Grund: Export-only, kein Import-Gegenstück.
- [x] **TASK-023** — **TrendAnalysis aus Katalog entfernen**: ToolItem, MainViewModel.Filters. Command + Handler in FeatureCommandService.Analysis.cs. Grund: Kein Auto-Snapshot nach Runs.
- [x] **TASK-024** — **WindowsContextMenu aus Katalog entfernen**: ToolItem, MainViewModel.Filters. Command + Handler in FeatureCommandService.Infra.cs (~155). Grund: Generiert .reg ohne Auto-Import.
- [x] **TASK-025** — **DockerContainer aus Katalog entfernen**: ToolItem, MainViewModel.Filters. Command + Handler in FeatureCommandService.Infra.cs (~136). Grund: Template-Generierung ohne Build/Deploy.
- [x] **TASK-026** — Build + Tests: 3013 bestanden, 0 Fehler, 6 übersprungen (30 Feature-Tests entfernt)

> **Phase 4 abgeschlossen (2025-07-25):** 5 sichtbare aber unfertige Features komplett aus dem Tool-Katalog entfernt (CoverScraper, CollectionSharing, TrendAnalysis, WindowsContextMenu, DockerContainer). 9 Quelldateien editiert (ToolsViewModel.cs, MainViewModel.Filters.cs, FeatureCommandService.cs + 3 Partials, FeatureCommandKeys.cs, de.json, en.json). 30 Tests entfernt über 7 Testdateien. Build: 0 Fehler, 0 Warnungen. Tests: 3013 bestanden, 0 fehlgeschlagen, 6 übersprungen. **Beibehaltene FeatureService-Methoden** (für Phase 9 Dead-Code-Cleanup oder spätere Epics): CoverScraper (BuildCoverReport), TrendAnalysis (SaveTrendSnapshot, LoadTrendHistory, FormatTrendReport), DockerContainer (GenerateDockerfile, GenerateDockerCompose), WindowsContextMenu (GetContextMenuRegistryScript). CollectionSharing hatte keine separaten FeatureService-Methoden. fr.json hatte keine Tool.*-Keys für diese 5 Features — kein Edit nötig. IsPlanned-Filter in beiden ViewModels auf 3 verbleibende Features reduziert: MultiInstanceSync, PatchEngine, NKitConvert.

### Phase 5: Irreführende Benennungen korrigieren (5 Features)

- GOAL-005: Feature-Namen und Beschreibungen korrigieren, sodass sie korrekt beschreiben was das Feature tut

- [x] **TASK-027** — **PdfReport → HtmlReport umbenennen**: ToolItem Key `PdfReport` → `HtmlReport` in ToolsViewModel. Command-Key `cmds["PdfReport"]` → `cmds["HtmlReport"]` in FeatureCommandService.Export.cs. i18n-Keys `Tool.PdfReport` → `Tool.HtmlReport` in allen 3 Sprachen. DisplayName in de.json: "HTML-Report", en.json: "HTML Report", fr.json: "Rapport HTML". Icon von 📄 auf 🌐 ändern.
- [x] **TASK-028** — **MobileWebUI → ApiServer umbenennen**: ToolItem Key `MobileWebUI` → `ApiServer` in ToolsViewModel. Command-Key in FeatureCommandService. i18n DisplayName in de.json: "API-Server starten", en.json: "Start API Server", fr.json: "Démarrer le serveur API".
- [x] **TASK-029** — **SchedulerAdvanced → CronTester umbenennen + Beschreibung korrigieren**: ToolItem Key `SchedulerAdvanced` → `CronTester` in ToolsViewModel. i18n DisplayName: "Cron-Tester" (de), "Cron Tester" (en), "Testeur Cron" (fr). Beschreibung anpassen: "Cron-Ausdrücke testen und validieren" statt "Automatische Zeitplanung".
- [x] **TASK-030** — **HardlinkMode Beschreibung korrigieren**: i18n-Beschreibung in allen Sprachen auf "Speicher-Einsparung durch Hardlinks schätzen" ändern (statt "Hardlinks erstellen"). IsPlanned-Flag NICHT setzen — bleibt als Info-Feature.
- [x] **TASK-031** — **SystemTray aus Tools-Katalog in Einstellungen verschieben**: ToolItem aus ToolsViewModel entfernen. Die `ToggleSystemTray()`-Funktionalität bleibt als Setting in der Allgemein-Sektion erhalten. i18n-Keys `Tool.SystemTray*` entfernen.
- [x] **TASK-032** — Build + Tests ausführen — alle grün

> **Phase 5 abgeschlossen (2026-03-26):** 5 irreführende Benennungen korrigiert. PdfReport→HtmlReport (Key, Command, Handler, IExportService, ExportService, FeatureService, Icon \xE774), MobileWebUI→ApiServer (Key, Command, Handler, Dialog-Titel), SchedulerAdvanced→CronTester (Key, Command, Handler, Beschreibung), HardlinkMode-Beschreibung korrigiert ("Speicher-Einsparung durch Hardlinks schätzen"), SystemTray aus ToolItem-Katalog entfernt (cmds["SystemTray"] + IWindowHost.ToggleSystemTray() beibehalten als Window-Level-Command). 14 Quelldateien editiert (FeatureCommandKeys.cs, ToolsViewModel.cs, MainViewModel.Filters.cs, FeatureCommandService.cs + 3 Partials, IExportService.cs, ExportService.cs, FeatureService.Export.cs, FeatureService.Workflow.cs, de.json, en.json, fr.json). 7 Testdateien aktualisiert (FeatureCommandServiceTests.cs, CoverageBoostPhase2Tests.cs, CoverageBoostPhase3Tests.cs, CoverageBoostPhase5Tests.cs, GuiViewModelTests.cs). Build: 0 Fehler, 0 Warnungen. Tests: 3013 bestanden, 0 fehlgeschlagen, 6 übersprungen.

### Phase 6: Features konsolidieren (3 Gruppen)

- GOAL-006: Redundante Feature-Gruppen zu je einer Karte mit Unteroptionen zusammenführen

- [x] **TASK-033** — **Duplikat-Analyse konsolidieren**: DuplicateInspector + DuplicateHeatmap + CrossRootDupe zu einer Karte "DuplicateAnalysis" zusammenführen. Neues ToolItem `DuplicateAnalysis` in ToolsViewModel mit Kategorie "Analyse". Neuer Handler `DuplicateAnalysis()` in FeatureCommandService.Analysis.cs, der einen Dialog mit 3 Tabs/Abschnitten zeigt: (1) Verzeichnis-Analyse (alter DuplicateInspector-Code), (2) Konsolen-Heatmap (alter DuplicateHeatmap-Code), (3) Cross-Root (alter CrossRootDupe-Code). Alte 3 separate Tool-Registrierungen entfernen. `DefaultPinnedKeys`: `DuplicateInspector` → `DuplicateAnalysis`.
- [x] **TASK-034** — **Export konsolidieren**: ExportCsv + ExportExcel + DuplicateExport zu einer Karte "ExportCollection" zusammenführen. Neuer Handler `ExportCollection()` mit Format-Auswahl-Dialog (CSV / Excel XML / Duplikate-CSV). Alte 3 separate Tool-Registrierungen entfernen. `DefaultPinnedKeys`: `ExportCsv` → `ExportCollection`. **WICHTIG**: Die separaten FeatureService-Methoden (`ExportCollectionCsv`, `ExportExcelXml`) beibehalten — nur die UI-Karten zusammenführen.
- [x] **TASK-035** — **DAT-Import konsolidieren**: ToolImport bleibt als einziger DAT-Import (TosecDat bereits in Phase 2 entfernt). ToolImport umbenennen zu `DatImport` — klarerer Name. ToolItem-Key `ToolImport` → `DatImport`. i18n aktualisieren.
- [x] **TASK-036** — Build + Tests ausführen — alle grün. Ergebnis: 3005 bestanden, 6 übersprungen, 0 Fehler.

### Phase 7: CommandPalette reparieren

- GOAL-007: CommandPalette von 8 auf alle verfügbaren Tool-Commands erweitern

- [x] **TASK-037** — `FeatureService.SearchCommands()` refaktoriert: Hardcodiertes 15-Einträge-Array `PaletteCommands` ersetzt durch dynamische Suche über `FeatureCommands` Dictionary + `CoreShortcuts` (7 VM-Level-Commands). Levenshtein-Matching beibehalten. Neuer optionaler Parameter `IReadOnlyDictionary<string, ICommand>? featureCommands`.
- [x] **TASK-038** — `FeatureCommandService.ExecuteCommand()` refaktoriert: Dictionary-Lookup auf `_vm.FeatureCommands` zuerst, Fallback auf CoreShortcuts-Switch (dryrun/move/cancel/rollback/theme/clear-log/settings). Default-Case loggt WARN statt INFO.
- [x] **TASK-039** — Build + Tests ausgeführt — alle grün. Ergebnis: 3007 bestanden, 6 übersprungen, 0 Fehler. 2 neue Tests: `SearchCommands_WithFeatureCommands_ReturnsAllCommands`, `SearchCommands_WithFeatureCommands_FindsFeatureKey`.

### Phase 8: DefaultPinnedKeys und Kategorien bereinigen

- GOAL-008: Schnellzugriff, Kategorien und Tool-Zähler aktualisieren

- [x] **TASK-040** — `DefaultPinnedKeys` in ToolsViewModel + MainViewModel.Filters aktualisiert: 6 Keys `HealthScore, DuplicateAnalysis, RollbackQuick, ExportCollection, DatAutoUpdate, ConversionPipeline`.
- [x] **TASK-041** — Tool-Kategorien bereinigt: HeaderRepair von "Sicherheit & Integrität" (9→8) nach "Konvertierung & Hashing" (4→5) verschoben. Accessibility von "UI & Erscheinungsbild" (1 Tool) nach "Infrastruktur" (6→7) verschoben. Kategorie "UI & Erscheinungsbild" komplett entfernt (CategoryIcons, i18n Tool.Cat.UI). Alle 8 Kategorien haben jetzt 3-8 Tools.
- [x] **TASK-042** — i18n verwaiste Keys entfernt (de.json + en.json): Tool.Cat.UI, Tool.ThemeEngine + .Desc, Tool.RollbackUndo + .Desc, Tool.RollbackRedo + .Desc. fr.json: keine verwaisten Keys (nur 7 aktive Tool-Keys vorhanden).
- [x] **TASK-043** — ui-lookups.json: `emulatorMatrix` entfernt (EmulatorCompat in Phase 3 entfernt, kein Leser im Code). EmulatorMatrix-Property aus UiLookupData.cs entfernt. Verbleibende 5 Sektionen sind aktiv genutzt.
- [x] **TASK-044** — Build: 0 Fehler, 0 Warnungen. Tests: 3007 bestanden, 6 übersprungen, 0 Fehler.

> **Phase 8 abgeschlossen (2026-03-26):** DefaultPinnedKeys um ConversionPipeline erweitert (5→6). 2 Kategorie-Verstösse behoben: UI & Erscheinungsbild (1 Tool) aufgelöst → Accessibility nach Infrastruktur; Sicherheit & Integrität (9 Tools) reduziert → HeaderRepair nach Konvertierung & Hashing. 7 verwaiste i18n-Keys entfernt (de+en). emulatorMatrix aus ui-lookups.json und UiLookupData.cs entfernt. 6 Dateien editiert. Build: 0F/0W, Tests: 3007/6/0.

### Phase 9: Toter Code in FeatureService bereinigen

- GOAL-009: Nicht mehr referenzierte Methoden aus FeatureService-Partials entfernen

- [x] **TASK-045** — **FeatureService.Infra.cs bereinigen**: `GenerateDockerfile()`, `GenerateDockerCompose()`, `GetContextMenuRegistryScript()`, `ExportRulePack()`, `ImportRulePack()` entfernt. Alive behalten: `GetConfigDiff`, `LoadLocale`, `ResolveDataDirectory`, `IsPortableMode`, `IsHighContrastActive`, `FindApiProjectPath`.
- [x] **TASK-046** — **FeatureService.Conversion.cs bereinigen**: `BuildConvertQueueReport()`, `BuildNKitConvertReport()`, `BuildConversionEstimateReport()` entfernt. Alive behalten: `GetConversionEstimate`, `GetTargetFormat`, `VerifyConversions`, `FormatFormatPriority`.
- [x] **TASK-047** — **FeatureService.Analysis.cs bereinigen**: `BuildSplitPanelPreview()`, `GenreKeywords`, `_defaultGenreKeywords`, `ClassifyGenre()`, `ParseCsvReport()`, `DetectAutoProfile()`, `BuildPlaytimeReport()`, `BuildCollectionManagerReport()` entfernt.
- [x] **TASK-048** — **FeatureService.Collection.cs**: Nicht editiert — PlaytimeTracker- und GenreClassification-Methoden lagen in Analysis.cs, dort entfernt (TASK-047).
- [x] **TASK-049** — **FeatureService.Workflow.cs bereinigen**: `CompareDryRuns()`, `BuildCsvDiff()`, `BuildPipelineReport()`, `BuildMultiInstanceReport()`, `RemoveLockFiles()`, `HasLockFiles()` entfernt. Alive behalten: `GetSortTemplates`, `TestCronMatch`, `CronFieldMatch`, `ExtractFirstCsvField`.
- [x] **TASK-050** — Compiler-Warnungen geprüft: `dotnet build src/RomCleanup.sln --nologo` — 0 Fehler, 0 Warnungen. Wrapper bereinigt: IWorkflowService (6→2 Members), WorkflowService (6→2 Delegates), IConversionEstimator (7→4), ConversionEstimator (7→4), IHealthAnalyzer (12→7), HealthAnalyzer (12→7).
- [x] **TASK-051** — Finaler Test-Lauf: 2956 bestanden, 6 übersprungen, 0 Fehler. 51 tote Tests entfernt (3007→2956).

> **Phase 9 abgeschlossen:** 22 tote Methoden + 2 tote Felder aus 4 FeatureService-Partials entfernt. 6 Wrapper-Services/Interfaces bereinigt (IWorkflowService, WorkflowService, IConversionEstimator, ConversionEstimator, IHealthAnalyzer, HealthAnalyzer). 51 tote Tests aus 4 Testdateien (FeatureServiceTests, GuiViewModelTests, WpfCoverageBoostTests, CoverageBoostPhase2Tests) entfernt. Build: 0F/0W, Tests: 2956/6/0.

### Phase 10: Tests für Konsolidierung

- GOAL-010: Sicherstellen, dass die konsolidierten Features korrekt funktionieren und keine Regressionen entstehen

- [x] **TASK-052** — DuplicateAnalysis-Command test: 3 Tests — `NoCandidates_ShowsAllThreeSections` (verifies Inspector + Heatmap + CrossRoot sections present), `WithDedupeGroups_ShowsHeatmapData` (SNES console in heatmap), `SingleRoot_CrossRootSaysNeedsTwo` (cross-root requires 2 roots message).
- [x] **TASK-053** — ExportCollection-Command test: 3 Tests — `DuplicateCsvFormat_CreatesDuplicateCsvFile` (format 3 creates CSV with loser paths), `InvalidChoice_LogsWarning` (format 99 logs WARN), `EmptyInput_DoesNothing` (empty input = no action).
- [x] **TASK-054** — CommandPalette coverage test: 4 Tests — `SearchFindsRegisteredToolKey` (exact match score=0), `FuzzySearchFindsCloseMatch` (Levenshtein ≤3 for "HelthScore"), `SearchCoversAllFeatureCommandKeys` (every key in FeatureCommands dictionary findable), `FeatureCommandsCountExceedsOldHardcodedLimit` (more than 8 results, validating Phase 7 expansion).
- [x] **TASK-055** — DefaultPinnedKeys test: 3 Tests — `AllKeysExistInToolItems` (pinned keys must reference existing ToolItems), `ContainsExpectedKeys` (6 expected keys: HealthScore, DuplicateAnalysis, RollbackQuick, ExportCollection, DatAutoUpdate, ConversionPipeline), `DoNotContainRemovedKeys` (31 removed keys must not appear in pinned).
- [x] **TASK-056** — Removed keys negative test: 2 Tests — `RemovedToolKeys_MustNotExistInFeatureCommands` (Theory with 30 InlineData cases for all removed keys from Phases 1-6), `RemovedToolKeys_MustNotExistInToolItems` (same 31 removed keys checked against ToolItems collection).
- [x] **TASK-057** — i18n consistency test: 3 Tests — `AllToolItems_HaveLocalizedDisplayNameAndDescription` (no empty DisplayName/Description), `DeAndEn_HaveMatchingToolKeys` (de.json and en.json Tool.* keys identical), `NoRemovedToolKeys_InAnyLocale` (31 removed Tool.* keys absent from all 3 locale files + Tool.Cat.UI absent).
- [x] **TASK-058** — Finaler Test-Lauf: 3003 bestanden, 6 übersprungen, 0 Fehler. 47 neue Tests hinzugefügt (2956→3003).

> **Phase 10 abgeschlossen:** 16 neue Testmethoden (47 Test-Cases inkl. Theory-Inline) in FeatureCommandServiceTests.cs. Alle konsolidierten Features (DuplicateAnalysis 3-Sektionen, ExportCollection 3-Formate, CommandPalette dynamische Suche) abgesichert. DefaultPinnedKeys-Invariante, Removed-Keys-Negativtest (30 entfernte Keys gegen FeatureCommands + ToolItems) und i18n-Konsistenz (de/en-Sync, keine verwaisten Keys) verifiziert. Build: 0F/0W, Tests: 3003/6/0.

## 3. Alternatives

- **ALT-001**: Features nur als `IsPlanned=true` markieren statt entfernen — **abgelehnt**, weil geplante Features immer noch im Katalog sichtbar sind und Nutzer verwirren. Komplette Entfernung ist sauberer.
- **ALT-002**: Alle Features behalten und nur die Stubs implementieren — **abgelehnt**, weil FTP, Cloud, Plugin-System, GPU-Hashing substantielle Eigenentwicklungen sind die nicht zum Kern-Produkt gehören (ROM-Cleanup ≠ ROM-Launcher ≠ Cloud-Platform).
- **ALT-003**: Features in ein separates "Labs"-Tab verschieben — **abgelehnt**, weil dies die Komplexität erhöht statt reduziert. Ein "Labs"-Tab legitimiert Halbfertiges.
- **ALT-004**: ConversionEstimate als Sub-Tab in ConversionPipeline statt eigene Entfernung — **abgelehnt**, ConversionPipeline zeigt bereits den Estimate als integralen Bestandteil.

## 4. Dependencies

- **DEP-001**: `dotnet build` muss nach jeder Phase erfolgreich sein (kein toter Code mit Compile-Fehlern)
- **DEP-002**: `dotnet test` mit 5.400+ Tests muss nach jeder Phase grün sein
- **DEP-003**: Phase 2 hängt von Phase 1 ab (PluginManager-Entfernung muss vor Konsolidierung geschehen)
- **DEP-004**: Phase 6 (Konsolidierung) hängt von Phase 2 (Redundante entfernen) ab — ConvertQueue muss weg sein bevor ConversionPipeline konsolidiert wird
- **DEP-005**: Phase 9 (Toter Code) hängt von Phase 1-6 ab — erst alle Aufrufer entfernen, dann Methoden
- **DEP-006**: Phase 10 (Tests) hängt von Phase 6-9 ab — konsolidierte Features müssen existieren

## 5. Files

**Primäre Dateien (werden stark verändert):**

- **FILE-001**: `src/RomCleanup.UI.Wpf/ViewModels/ToolsViewModel.cs` — Tool-Katalog-Registrierung, DefaultPinnedKeys, Kategorien (~350 Zeilen, wird auf ~250 reduziert)
- **FILE-002**: `src/RomCleanup.UI.Wpf/ViewModels/MainViewModel.Filters.cs` — Duplikat-Registrierungen und IsPlanned-Zuweisungen (wird parallel bereinigt)
- **FILE-003**: `src/RomCleanup.UI.Wpf/Services/FeatureCommandService.cs` — Haupt-Registrierung (513 Zeilen, ~15 Registrierungen entfernen)
- **FILE-004**: `src/RomCleanup.UI.Wpf/Services/FeatureCommandService.Analysis.cs` — Handler (251 Zeilen, ~3 Handler entfernen, 1 konsolidieren)
- **FILE-005**: `src/RomCleanup.UI.Wpf/Services/FeatureCommandService.Conversion.cs` — Handler (137 Zeilen, ~3 Handler entfernen)
- **FILE-006**: `src/RomCleanup.UI.Wpf/Services/FeatureCommandService.Dat.cs` — Handler (366 Zeilen, ~1 Handler entfernen)
- **FILE-007**: `src/RomCleanup.UI.Wpf/Services/FeatureCommandService.Collection.cs` — Handler (143 Zeilen, ~4 Handler entfernen)
- **FILE-008**: `src/RomCleanup.UI.Wpf/Services/FeatureCommandService.Workflow.cs` — Handler (177 Zeilen, ~2 Handler entfernen)
- **FILE-009**: `src/RomCleanup.UI.Wpf/Services/FeatureCommandService.Infra.cs` — Handler (292 Zeilen, ~5 Handler entfernen)
- **FILE-010**: `src/RomCleanup.UI.Wpf/Services/FeatureCommandService.Export.cs` — Handler (88 Zeilen, Export-Konsolidierung)

**FeatureService-Dateien (toter Code entfernen):**

- **FILE-011**: `src/RomCleanup.UI.Wpf/Services/FeatureService.Infra.cs` — 4 Methoden entfernen
- **FILE-012**: `src/RomCleanup.UI.Wpf/Services/FeatureService.Conversion.cs` — 3 Methoden entfernen
- **FILE-013**: `src/RomCleanup.UI.Wpf/Services/FeatureService.Analysis.cs` — 1 Methode entfernen
- **FILE-014**: `src/RomCleanup.UI.Wpf/Services/FeatureService.Collection.cs` — 2 Methoden entfernen
- **FILE-015**: `src/RomCleanup.UI.Wpf/Services/FeatureService.Workflow.cs` — SplitPanel-Methoden entfernen

**i18n-Dateien:**

- **FILE-016**: `data/i18n/de.json` — ~20 Tool-Keys entfernen, ~5 umbenennen
- **FILE-017**: `data/i18n/en.json` — ~20 Tool-Keys entfernen, ~5 umbenennen
- **FILE-018**: `data/i18n/fr.json` — ~20 Tool-Keys entfernen, ~5 umbenennen

**UI-Dateien:**

- **FILE-019**: `data/ui-lookups.json` — Verwaiste Einträge bereinigen
- **FILE-020**: `src/RomCleanup.UI.Wpf/MainWindow.xaml` — Ctrl+D Shortcut umverdrahten

**Dokumentation:**

- **FILE-021**: `docs/USER_HANDBOOK.md` — Emulator-Kompatibilitätsmatrix als Referenz einfügen

**Test-Dateien:**

- **FILE-022**: `src/RomCleanup.Tests/FeatureCommandServiceTests.cs` — Neue Tests für konsolidierte Features
- **FILE-023**: `src/RomCleanup.Tests/FeatureServiceTests.cs` — Bestehende Tests anpassen (entfernte Methoden)

## 6. Testing

- **TEST-001**: Nach jeder Phase: `dotnet build src/RomCleanup.sln` kompiliert fehlerfrei
- **TEST-002**: Nach jeder Phase: `dotnet test src/RomCleanup.Tests/RomCleanup.Tests.csproj --nologo` — 5.400+ Tests grün
- **TEST-003**: Neuer Test: `DuplicateAnalysis_ShowsAllThreeSections` — konsolidierter Duplikat-Dialog enthält Inspector-, Heatmap- und CrossRoot-Abschnitte
- **TEST-004**: Neuer Test: `ExportCollection_SupportsAllFormats` — CSV, Excel XML, Duplicate CSV Export alle funktional
- **TEST-005**: Neuer Test: `CommandPalette_FindsAllRegisteredTools` — Verifikation dass alle Tool-Keys auffindbar sind
- **TEST-006**: Neuer Test: `DefaultPinnedKeys_AllExistInCatalog` — kein Pinned-Key zeigt auf entferntes Tool
- **TEST-007**: Neuer Test: `RemovedTools_NotInFeatureCommands` — entfernte Keys (FtpSource, CloudSync, etc.) sind nicht im Dictionary
- **TEST-008**: Neuer Test: `I18nKeys_MatchToolItemKeys` — jedes ToolItem hat korrespondierende i18n-Einträge in de/en/fr
- **TEST-009**: Regressionstests: Alle bestehenden FeatureCommandServiceTests und FeatureServiceTests müssen weiterhin grün sein (Referenzen auf entfernte Features werden entfernt/angepasst)
- **TEST-010**: Final: `dotnet test src/RomCleanup.sln --nologo` — vollständige Suite

## 7. Risks & Assumptions

- **RISK-001**: Entfernte Features hinterlassen verwaiste Test-Referenzen — **Mitigation**: Phase 10 prüft explizit auf Compile-Fehler in Tests
- **RISK-002**: i18n-Keys könnten an unerwarteten Stellen referenziert werden (XAML Bindings, ResourceDictionary) — **Mitigation**: Grep-Suche nach jedem entfernten Key in allen .xaml/.cs/.json Dateien
- **RISK-003**: ConsolidatedFeatures (DuplicateAnalysis, ExportCollection) könnten UX-Regression verursachen — **Mitigation**: Tests für Konsolidierung in Phase 10
- **RISK-004**: DefaultPinnedKeys verweisen auf entfernte Keys → Laufzeit-NullRef — **Mitigation**: TASK-040 und TEST-006 adressieren dies explizit
- **RISK-005**: MainViewModel.Filters.cs enthält Tool-Registrierungen parallel zu ToolsViewModel — **Mitigation**: Beide Dateien werden synchron bereinigt in jeder Phase
- **ASSUMPTION-001**: Die Kern-Pipeline (RunOrchestrator, Core-Logik, Infrastructure-Services) wird durch diese Änderungen NICHT beeinflusst — nur UI-Schicht
- **ASSUMPTION-002**: FeatureService-Methoden die von entfernten Handlern aufgerufen werden, werden nicht von anderen Stellen referenziert (Compiler-Check in Phase 9 verifiziert dies)
- **ASSUMPTION-003**: Die 5.400+ bestehenden Tests decken ausreichend ab, dass keine Kernfunktionalität durch UI-Bereinigung bricht

## 8. Related Specifications / Further Reading

- Feature-Audit-Ergebnisse aus der vorherigen Chat-Konversation (2026-03-26)
- [docs/USER_HANDBOOK.md](docs/USER_HANDBOOK.md) — Ziel für migrierte Emulator-Kompatibilitätsmatrix
- [.github/copilot-instructions.md](.github/copilot-instructions.md) — Projektweite Architektur- und Sicherheitsregeln
- [.claude/rules/cleanup.instructions.md](.claude/rules/cleanup.instructions.md) — Coding Guidelines (Stabilität vor Feature-Hype)
