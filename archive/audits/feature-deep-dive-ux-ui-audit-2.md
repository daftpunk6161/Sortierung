---
goal: "Deep-Dive UX/UI Bug-Audit: Theme-Konsistenz, Data-Binding-Gaps, Accessibility, MVVM-Violations, Feature-Completeness"
version: 1.0
date_created: 2026-03-12
last_updated: 2026-03-12
owner: RomCleanup Team
status: 'Planned'
tags: [bug-audit, ux, ui, wpf, accessibility, theme, data-binding, mvvm, feature-completeness]
---

# Deep-Dive UX/UI Bug-Audit — WPF GUI

![Status: Planned](https://img.shields.io/badge/status-Planned-blue)

Ergänzender Deep-Dive Bug-Audit fokussiert auf UX/UI-Probleme der WPF-GUI (`src/RomCleanup.UI.Wpf/`). Erweitert den initialen Audit (55 Bugs) und den Feature-Deep-Dive (83 Bugs, `plan/feature-deep-dive-bug-audit-1.md`) um **62 neue UX/UI-Findings** in 8 Runden: (1) Data-Binding-Gaps / Dead UI, (2) Theme-Konsistenz, (3) Accessibility & Keyboard, (4) MVVM-Violations & Code-Behind, (5) Feature-Completeness / Phantom-Features, (6) Layout & Responsiveness, (7) Input-Validierung & Fehler-UX, (8) Zusammenfassung.

Priorisierung: **3× P0** (Release-Blocker), **14× P1** (Hoch), **28× P2** (Mittel), **17× P3** (Niedrig).

---

## 1. Requirements & Constraints

- **REQ-001**: Alle Findings enthalten konkrete Dateipfade, Zeilennummern (XAML/CS) und Fix-Strategien
- **REQ-002**: Priorisierung nach P0 (Release-Blocker) → P1 (Hoch) → P2 (Mittel) → P3 (Niedrig)
- **REQ-003**: Jedes Finding braucht eine Testabsicherungsstrategie
- **REQ-004**: Nur Findings, die im vorherigen Audit (TASK-001–TASK-083) noch nicht erfasst sind
- **UX-001**: GUI-Pflicht laut copilot-instructions: DryRun/Preview → Summary → Bestätigung → Move
- **UX-002**: Lange Tasks off-UI-thread; kein DoEvents-Pattern
- **UX-003**: Farben/Styles zentral im ResourceDictionary — keine hardcodierten Farben
- **UX-004**: Zwei Modi: Einfach (4 Entscheidungen) und Experte (volle Kontrolle)
- **ACC-001**: WCAG 2.1 AA Konformität (bereits als GUI-22 im Code kommentiert)
- **ACC-002**: Keyboard-Navigation durchgängig funktionsfähig (GUI-23)
- **CON-001**: Keine Code-Änderungen in diesem Plan — nur Analyse und Maßnahmen
- **CON-002**: Alle Findings basieren auf tatsächlich gelesenem Code

---

## 2. Implementation Steps

### Runde 13 — Data-Binding-Gaps / Dead UI

- GOAL-013: Alle XAML-Controls identifizieren, die visuell funktional erscheinen aber mangels Binding wirkungslos sind

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-084 | **P0 – ~30 Konsolen-Filter-Checkboxen ohne Binding**: `MainWindow.xaml` Zeilen ~460–540 definiert Checkboxen wie `chkConsPS1`, `chkConsPS2`, `chkConsSNES`, `chkConsSaturn`, `chkConsDC`, `chkConsN64`, `chkConsGBA`, `chkConsNDS`, `chkConsMD`, `chkConsPSP`, `chkConsArcade` usw. mit `x:Name` aber **ohne** `IsChecked="{Binding ...}"`. Der Benutzer setzt Haken, aber kein Code liest `.IsChecked` — die Filter sind komplett wirkungslos. `RunOrchestrator.Execute()` erhält keine Konsolen-Filterinformation. **Fix**: (a) ViewModel-Properties `FilterPS1`, `FilterPS2` etc. als `bool` mit INPC ergänzen, (b) XAML `IsChecked="{Binding FilterPS1}"` binden, (c) in `RunCoreAsync()` an `RunOptions` übergeben, oder (d) wenn Filter nicht implementiert werden soll: Controls entfernen/deaktivieren mit „Coming Soon"-Label. **Test**: UI-Automation: Checkbox setzen → `_vm.FilterPS1 == true` prüfen → RunOptions enthält Filter. | | |
| TASK-085 | **P0 – ~18 Dateityp-Filter-Checkboxen ohne Binding**: `MainWindow.xaml` Zeilen ~430–460 definiert `chkExtChd`, `chkExtIso`, `chkExtZip`, `chkExt7z`, `chkExtRar`, `chkExtBin`, `chkExtCue`, `chkExtGdi`, `chkExtNes`, `chkExtSfc`, `chkExtGba`, `chkExtZ64`, `chkExtNds`, `chkExtGb`, `chkExtGbc`, `chkExtMd`, `chkExtPbp`, `chkExtRvz`. Alle ohne Binding. `RunOptions.Extensions` verwendet stattdessen `DefaultExtensions` (hardcoded). Benutzer-Selektion wird ignoriert. **Fix**: Analog TASK-084 — Properties oder Sammlung im ViewModel, XAML-Binding, Weiterleitung an RunOptions. **Test**: Checkbox deselektieren → Extension nicht in RunOptions.Extensions. | | |
| TASK-086 | **P1 – cmbDatHash nicht an VM.DatHashType gebunden**: `MainWindow.xaml` Tools&DAT-Tab enthält `<ComboBox x:Name="cmbDatHash">` mit Items `sha1` (IsSelected), `sha256`, `md5`. Kein `SelectedValue="{Binding DatHashType}"`. Die VM-Property `DatHashType` wird nur aus Settings geladen, nie aus der UI aktualisiert. **Fix**: `SelectedValue="{Binding DatHashType, Mode=TwoWay}"` + `SelectedValuePath="Content"`. **Test**: ComboBox auf SHA256 stellen → VM.DatHashType == "SHA256" → RunOptions.HashType == "SHA256". | | |
| TASK-087 | **P1 – cmbLogLevel nicht an VM.LogLevel gebunden**: Analog zu TASK-086. ComboBox hat Items Debug/Info/Warning/Error aber kein Binding. Log-Level-Änderung durch den User hat keine Wirkung. **Fix**: `SelectedValue="{Binding LogLevel, Mode=TwoWay}"`. **Test**: ComboBox ändern → VM.LogLevel ändert sich. | | |
| TASK-088 | **P2 – cmbQuickProfile komplett leer**: Status-Bar enthält `<ComboBox x:Name="cmbQuickProfile" Width="160">` ohne `ItemsSource` und ohne `SelectedItem`-Binding. Die ComboBox ist immer leer — kein Dropdown-Inhalt. **Fix**: Entweder Profil-Liste aus SettingsService laden und binden, oder Control entfernen. **Test**: ComboBox enthält mindestens „Standard"-Eintrag nach dem Laden. | | |
| TASK-089 | **P2 – cmbLocale (Features-Tab) nicht gebunden**: `<ComboBox x:Name="cmbLocale">` hat hardcoded Items `de`, `en`, `fr` aber kein `SelectedValue="{Binding Locale}"`. `OnApplyLocale` liest `_vm.Locale` (Standard "de"), nicht den ComboBox-Wert. **Fix**: `SelectedValue` binden oder in `OnApplyLocale` via `cmbLocale.SelectedItem` lesen. **Test**: Locale auf "en" setzen → OnApplyLocale verwendet "en". | | |
| TASK-090 | **P2 – chkWatchMode (Features-Tab) nicht gebunden**: `<CheckBox x:Name="chkWatchMode" Content="Watch-Mode">` ohne `IsChecked`-Binding. `OnWatchApply` prüft `_watchers.Count`, nicht den Checkbox-Zustand. Checkbox und tatsächlicher Watcher-Zustand können divergieren (Checkbox an, Watcher aus oder umgekehrt). **Fix**: VM-Property `IsWatchModeActive` einführen, bidirektional binden, in `OnWatchApply` und `DisposeWatchers` synchronisieren. **Test**: Watch aktivieren → chkWatchMode.IsChecked == true; deaktivieren → false. | | |
| TASK-091 | **P2 – Step-Indicator-Dots (stepDot1/2/3) nie aktualisiert**: `MainWindow.xaml` Zeilen ~170–195 definiert drei Ellipsen mit `Fill="Transparent"`. Kein Binding, kein Code-Behind setzt `Fill`. Die Dots zeigen immer „leer" — sie visualisieren nie den aktuellen Workflow-Schritt. **Fix**: VM-Properties `Step1Status`, `Step2Status`, `Step3Status` (StatusLevel) einführen. In `RefreshStatus()` setzen. XAML: `Fill="{Binding Step1Status, Converter={StaticResource StatusToBrush}}"`. **Test**: Roots hinzufügen → stepDot1 wird grün. Optionen setzen → stepDot2 wird grün. | | |
| TASK-092 | **P3 – listErrorSummary Items.Add statt Binding**: `MainWindow.xaml.cs` `PopulateErrorSummary()` (Zeile ~620) manipuliert `listErrorSummary.Items` direkt, statt eine `ObservableCollection<string>` im ViewModel zu binden. Funktioniert, aber verletzt MVVM und erschwert Testbarkeit. **Fix**: VM-Property `ErrorSummaryEntries` als `ObservableCollection<string>`, XAML-Binding. **Test**: Nach Lauf → VM.ErrorSummaryEntries enthält erwartete Einträge. | | |
| TASK-093 | **P3 – Performance-Labels (lblPerf*) via x:Name statt Binding**: 6 TextBlocks (`lblPerfProgress`, `lblPerfThroughput`, `lblPerfEta`, `lblPerfCache`, `lblPerfPhase`, `lblPerfFile`) im Protokoll-Tab verwenden `x:Name`. Kein Code setzt deren `Text`-Property — sie zeigen dauerhaft ihre XAML-Defaults ("0%", "– grp/s", "ETA –", etc.). Entweder binden oder entfernen. **Fix**: VM-Properties für Performance-Metriken, `Text="{Binding PerfProgress}"`. **Test**: Während Lauf → lblPerfProgress zeigt echten Fortschritt. | | |

### Runde 14 — Theme-Konsistenz & Visual Issues

- GOAL-014: Visuelle Inkonsistenzen zwischen Themes identifizieren und Stellen mit hardcodierten Farben finden

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-094 | **P1 – ShowTextDialog ignoriert Theme**: `MainWindow.xaml.cs` `ShowTextDialog()` (Zeile ~3100) erstellt ein Window mit hardcodierten Farben: `Background = Color.FromRgb(0x1A, 0x1A, 0x2E)`, TextBox: `Background = Color.FromRgb(0x16, 0x21, 0x3E)`, `Foreground = Color.FromRgb(0xEA, 0xEA, 0xEA)`. Im Light-Theme erscheint ein dunkles Fenster. 40+ Feature-Buttons rufen `ShowTextDialog` auf — das betrifft die Mehrheit aller Feature-Dialoge. **Fix**: Window-Background/Foreground via `DynamicResource` aus dem aktuellen Theme laden: `Background = (Brush)Application.Current.FindResource("BrushSurface")`. **Test**: Theme auf Light wechseln → ShowTextDialog hat hellen Hintergrund. | | |
| TASK-095 | **P1 – MessageBox.Show() in 5+ Stellen bricht Theme**: `OnConflictPolicy`, `OnRulePackSharing`, `OnThemeEngine`, `OnIntegrityMonitor`, `OnClosing` verwenden `MessageBox.Show()` — zeigt immer Standard-Windows-Chrome, nicht das Synthwave/Light-Theme. **Fix**: Eigenes Theme-konformes Dialog-Fenster erstellen oder `DialogService` um themed MessageBox erweitern. **Test**: Im Dark-Theme → Dialog hat dunklen Hintergrund mit Accent-Farben. | | |
| TASK-096 | **P2 – SynthwaveDark ProgressBar hat Neon-Glow, Light nicht**: `SynthwaveDark.xaml` ProgressBar-Style hat `DropShadowEffect BlurRadius="10" Color="#00F5FF"` (Neon-Glow). `Light.xaml` ProgressBar hat keinen Effect. Visuell inkonsistent — Dark hat Glow, Light nicht. Kein Bug, aber Design-Inkonsistenz. **Fix**: Light-Theme ProgressBar einen subtilen Schatten geben oder Dark-Theme-Glow dokumentieren als bewusste Entscheidung. **Test**: Visueller Vergleich beider Themes. | | |
| TASK-097 | **P2 – Expander-Header nicht custom-gestylt**: Beide Themes setzen `Foreground`, `BorderBrush`, `Background` für Expander, aber **kein ControlTemplate** für den Header mit Pfeil/Toggle. Der Default-WPF-Expander-Pfeil verwendet SystemColors, die im Dark-Theme nahezu unsichtbar sind (dunkelgrauer Pfeil auf dunklem Hintergrund). **Fix**: Custom ControlTemplate für Expander mit Theme-konformem Toggle-Pfeil (BrushAccentCyan). Die Features-Tab enthält 8 Expander — alle betroffen. **Test**: Dark-Theme → Expander-Pfeil sichtbar und theme-konform. | | |
| TASK-098 | **P2 – ScrollBar nur Width/Background gestylt**: Beide Themes setzen `Width="8"` und `Background` für ScrollBar, aber keinen Custom Thumb/Track. Die Standard-WPF-ScrollBar-Pfeile und der Thumb verwenden SystemColors — im Dark-Theme helle Pfeile auf dunklem Track. **Fix**: Minimales ScrollBar-ControlTemplate mit Theme-Brushes für Thumb und Track. **Test**: Dark-Theme → ScrollBar-Thumb in BrushSurfaceLight, Track in BrushSurface. | | |
| TASK-099 | **P3 – ToolTip-Style fehlt in beiden Themes**: Kein `<Style TargetType="ToolTip">` definiert. Standard-WPF-ToolTips verwenden SystemColors (hellgelb/weiß). Im Dark-Theme erscheinen weiße ToolTips — visueller Bruch. 50+ Controls haben `ToolTip`-Attribute. **Fix**: ToolTip-Style mit `BrushSurface`/`BrushTextPrimary`/`BrushBorder`. **Test**: Hover über Button mit ToolTip → Theme-konformer ToolTip. | | |
| TASK-100 | **P3 – SynthwaveDark SectionCard vs. Light SectionCard Shadow-Unterschied**: Dark: `DropShadowEffect Opacity="0.25"`. Light: `Opacity="0.08"`. Akzeptabel, aber nicht dokumentiert. Kein Fix nötig — nur Dokumentation. | | |
| TASK-101 | **P3 – Keine Transition/Animation beim Theme-Wechsel**: `ThemeService.Toggle()` swappt ResourceDictionaries synchron. Alle Farben ändern sich abrupt. **Fix**: Optional — Crossfade-Animation oder bewusst beibehalten als Snap-Switch. Niedrige Priorität. | | |

### Runde 15 — Accessibility & Keyboard-Navigation

- GOAL-015: Accessibility-Lücken für Screenreader, Keyboard-Navigation und High-Contrast identifizieren

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-102 | **P1 – 80+ Feature-Buttons ohne AutomationProperties.Name**: `MainWindow.xaml` Features-Tab enthält ~80 Buttons wie `<Button x:Name="btnDatAutoUpdate" Content="DAT Auto-Update" Width="145">`. Keiner hat `AutomationProperties.Name`. Screenreader lesen nur `Content`-Text — fehlt bei Icon-Buttons oder ist nicht deskriptiv genug. Dashboard-Metriken und Footer-Buttons haben korrekte `AutomationProperties.Name`-Attribute (vorbildlich). **Fix**: `AutomationProperties.Name` auf allen Feature-Buttons setzen. Kann automatisiert werden: `AutomationProperties.Name="{Binding RelativeSource={RelativeSource Self}, Path=Content}"` als implizites Setting im Theme. **Test**: Accessibility Insights → alle Buttons haben Name. | | |
| TASK-103 | **P1 – Keyboard-Shortcuts nur 4 von 14 definiert**: `MainWindow.xaml` `<Window.InputBindings>` deklariert nur F5 (Run), Esc (Cancel), Ctrl+R (Report), Ctrl+Z (Rollback). Die Command-Palette (`FeatureService.PaletteCommands`) listet 14 Shortcuts inkl. Ctrl+D (DryRun), Ctrl+M (Move), Ctrl+K (Convert), Ctrl+E (Export), Ctrl+H (History), Ctrl+F (Filter), Ctrl+T (Theme), Ctrl+L (Clear Log), Ctrl+G (GameKey). 10 Shortcuts sind dokumentiert aber nicht als InputBindings verdrahtet. **Fix**: Alle 14 Palette-Commands als `<KeyBinding>` in `Window.InputBindings` deklarieren. Für Commands ohne ICommand: neue Commands im ViewModel oder statische Actions. **Test**: Ctrl+D drücken → DryRun startet. Ctrl+T → Theme wechselt. | | |
| TASK-104 | **P2 – Kein TabIndex auf Feature-Buttons**: Nur 3 Controls haben expliziten `TabIndex` (Start=1, Cancel=2, Rollback=3). 80+ Feature-Buttons und alle Config-Controls haben keinen `TabIndex`. Tab-Reihenfolge folgt der XAML-Deklarationsreihenfolge — funktioniert, aber bei verschachtelten TabControls/ScrollViewern kann die Reihenfolge nicht-intuitiv sein. **Fix**: Logische TabIndex-Gruppen definieren: 1-10 = Action-Bar, 11-20 = Sortieren-Tab, 21-30 = Konfiguration, 31+ = Features. **Test**: Tab-Key drücken → Focus wandert in logischer Reihenfolge. | | |
| TASK-105 | **P2 – Dashboard-Metriken LiveSetting="Polite" gut, aber keine LiveRegion auf Progress**: Dashboard-MetricCards haben korrekt `AutomationProperties.LiveSetting="Polite"` (**vorbildlich**). Aber `ProgressBar` und `ProgressText` in der Footer-Bar haben kein LiveSetting. Screenreader werden Fortschritts-Änderungen nicht ankündigen. **Fix**: `AutomationProperties.LiveSetting="Polite"` auf ProgressBar und ProgressText setzen. **Test**: Screenreader kündigt Fortschritt an. | | |
| TASK-106 | **P2 – High-Contrast-Modus nicht getestet/unterstützt**: `FeatureService.IsHighContrastActive()` prüft den Registry-Key, aber das Ergebnis wird nie genutzt um ein HC-Theme zu laden. `OnAccessibility` zeigt nur den Status an. `OnThemeEngine` bietet „High-Contrast" als Option, macht aber nur `_theme.Toggle()` (normalen Theme-Wechsel). **Fix**: Minimales HC-Theme-Dictionary erstellen das SystemColors verwendet, oder bei HC-Modus die Standard-WPF-Themes durchschlagen lassen (keine MergedDictionaries). **Test**: Windows-HC aktivieren → App respektiert HC-Farben. | | |
| TASK-107 | **P2 – WebBrowser-Control nicht accessible**: `<WebBrowser x:Name="webReportPreview">` (IE-basiert) hat keine `AutomationProperties`-Attribute. Kein Alternativtext, kein `IsAccessibilityFocusable`. Screenreader können den Inhalt nicht lesen. **Fix**: `AutomationProperties.Name="Report-Vorschau"` setzen. Langfristig: WebView2 statt IE-WebBrowser. **Test**: Accessibility Insights findet WebBrowser mit Name. | | |
| TASK-108 | **P3 – Keine Focus-Rückkehr nach ShowTextDialog**: `ShowTextDialog()` zeigt ein modales Fenster via `ShowDialog()`. Nach dem Schließen geht der Focus auf das übergeordnete Window, aber nicht auf den auslösenden Button. Bei 40+ Feature-Buttons verliert der User den Kontext. **Fix**: Focus nach `ShowDialog()` return auf `(sender as Button)?.Focus()`. **Test**: Button klicken → Dialog schließen → Focus wieder auf dem gleichen Button. | | |
| TASK-109 | **P3 – Kein Tooltip/Hilfetext auf Simple-Mode-Controls**: Simple-Mode-Panel (Zeilen ~350–400) hat RadioButtons für Quick-Presets und einen Region-Slider ohne Tooltips. Novice-User haben keine Erklärung was die Optionen bewirken. **Fix**: Descriptive ToolTips auf alle Simple-Mode-Controls. **Test**: Hover über Simple-Mode-Radio → informativer ToolTip. | | |

### Runde 16 — MVVM-Violations & Code-Behind

- GOAL-016: MVVM-Pattern-Verletzungen katalogisieren, die Testbarkeit und Wartbarkeit beeinträchtigen

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-110 | **P1 – MainWindow.xaml.cs ~3200 Zeilen Code-Behind**: Die Code-Behind-Datei enthält 81 Feature-Handler, Run-Orchestration, Rollback, Drag-Drop, 40+ Event-Handler — alles in einer Datei. Die Klasse hat >50 private Felder und ist nicht unit-testbar. **Fix**: Refactoring-Plan: (a) Feature-Handler in ViewModel-Commands migrieren, (b) Run-Orchestration in eigenen Service extrahieren, (c) Tray/Watch-Logik in eigene Services. Stufenweises Refactoring über mehrere PRs. **Test**: Nach Refactoring: ViewModel-Commands unit-testbar ohne Window-Instanz. | | |
| TASK-111 | **P1 – 81 Feature-Buttons via Click-Event statt Command**: Alle Feature-Tab-Buttons verwenden `btnXyz.Click += OnXyz` im Konstruktor (Zeilen 100–210). MVVM-Pattern verlangt `Command="{Binding XyzCommand}"`. Die wenigen Ausnahmen (ThemeToggle, Run, Cancel, Rollback, OpenReport, ClearLog) verwenden korrekt Commands. **Fix**: FeatureCommands-Klasse oder Dictionary<string, ICommand> im ViewModel. Feature-Buttons in XAML: `Command="{Binding FeatureCommand}" CommandParameter="ConversionEstimate"`. **Test**: XAML-Grep: kein `x:Name="btn"` ohne zugehöriges `Command`-Binding. | | |
| TASK-112 | **P2 – webReportPreview.Navigate() direkte UI-Manipulation**: `RefreshReportPreview()` (Zeile ~580) ruft `webReportPreview.Navigate(new Uri(fullPath))` direkt auf. **Fix**: VM-Property `ReportPreviewUri` binden (wenn WebBrowser Binding unterstützt) oder bewusst als Code-Behind-Ausnahme dokumentieren (WebBrowser hat kein Binding-API). **Test**: Dokumentation in Code-Kommentar. | | |
| TASK-113 | **P2 – Drag-Drop auf listRoots in Code-Behind**: `OnRootsDragEnter` und `OnRootsDrop` (Zeile ~270) sind Event-Handler in Code-Behind. In WPF ist DragDrop-Binding komplex — hier ist Code-Behind akzeptabel, aber sollte dokumentiert sein. **Fix**: XML-Kommentar „Code-Behind: WPF DragDrop hat kein natives Binding". **Test**: Doku vorhanden. | | |
| TASK-114 | **P2 – _conflictPolicy Feld nie verwendet**: `MainWindow.xaml.cs` Zeile ~45 deklariert `private string _conflictPolicy = "Rename"`. `OnConflictPolicy()` setzt den Wert. Aber `RunCoreAsync()` reicht `_conflictPolicy` nie an `RunOrchestrator` oder `RunOptions` weiter. Die Conflict-Policy ist komplett wirkungslos. **Fix**: `RunOptions.ConflictPolicy` Property; im Orchestrator bei File-Move berücksichtigen. **Test**: ConflictPolicy="Skip" → bei Namenkonflikt wird Datei übersprungen. | | |
| TASK-115 | **P3 – StatusBarService ist überflüssiger Wrapper**: `StatusBarService.cs` hat eine einzige Methode `Refresh(MainViewModel vm)` die nur `vm.RefreshStatus()` aufruft. Kein Mehrwert. **Fix**: Klasse entfernen, Aufrufer direkt `_vm.RefreshStatus()` verwenden. **Test**: Build erfolgreich nach Entfernung. | | |
| TASK-116 | **P3 – _rollbackUndoStack/_rollbackRedoStack nie befüllt**: `MainWindow.xaml.cs` Zeilen ~50–51 deklarieren Undo/Redo-Stacks. `OnRollbackUndo` poppt vom Stack, `OnRollbackRedo` poppt vom Redo-Stack. Aber **kein Code pusht jemals auf `_rollbackUndoStack`**. Die Buttons „Rollback Undo" und „Rollback Redo" sind immer wirkungslos (Stack ist leer → sofortige Warnung). **Fix**: In `OnRollbackRequested()` nach erfolgreichem Rollback den `_lastAuditPath` auf den Undo-Stack pushen. **Test**: Rollback durchführen → Undo-Stack nicht leer → Undo-Button funktioniert. | | |

### Runde 17 — Feature-Completeness / Phantom-Features

- GOAL-017: Features identifizieren, die in der UI sichtbar sind aber keine echte Funktionalität haben

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-117 | **P1 – Locale-Wechsel ändert nur Window.Title**: `OnApplyLocale()` (Zeile ~1090) lädt Locale-Strings via `FeatureService.LoadLocale()`, setzt aber nur `Title = title`. Keine andere UI-Beschriftung wird aktualisiert. Die i18n-Dateien (`data/i18n/de.json`, `en.json`, `fr.json`) haben viele Keys, aber die XAML verwendet hardcodierte deutsche Strings. **Fix**: Entweder vollständige Lokalisierung via `x:Static`/`DynamicResource`-Keys (aufwändig) oder ComboBox/Button entfernen und mit „Nur Deutsch"-Label ersetzen. **Test**: Locale auf "en" → alle sichtbaren Labels auf Englisch. | | |
| TASK-118 | **P1 – Scheduler zeigt nur Cron-Match, plant nichts**: `OnSchedulerAdvanced()` (Zeile ~2500) akzeptiert Cron-Expression und testet ob „jetzt" matcht. Kein Timer, kein wiederkehrender Job, keine Persistierung. Feature suggeriert zeitgesteuerte Läufe, tut aber nichts dergleichen. **Fix**: Entweder DispatcherTimer + Persistierung implementieren, oder Button-Label ändern zu „Cron-Tester" und Feature als experimentell markieren. **Test**: Cron-Schedule setzen → nach Match-Zeitpunkt wird DryRun automatisch gestartet. | | |
| TASK-119 | **P2 – GPU-Hashing setzt Env-Variable die niemand liest**: `OnGpuHashing()` setzt `Environment.SetEnvironmentVariable("ROMCLEANUP_GPU_HASHING", "on/off")`. Kein Code in `FileHashService` oder anderswo liest diese Variable. Feature ist rein kosmetisch. **Fix**: Entweder in FileHashService einbauen (OpenCL-basiertes SHA256) oder Button als „Experimentell / Nicht implementiert" markieren. **Test**: Env-Var gesetzt → Hash-Berechnung nutzt GPU (wenn implementiert). | | |
| TASK-120 | **P2 – Parallel-Hashing setzt Env-Variable die niemand liest**: `OnParallelHashing()` setzt `ROMCLEANUP_HASH_THREADS`. `FileHashService` liest diese Variable nicht — verwendet stets sequentielles Hashing. **Fix**: `FileHashService` um Thread-Konfiguration erweitern oder den Button als „Nicht implementiert" markieren. **Test**: Thread-Anzahl setzen → Hash-Berechnung läuft parallel. | | |
| TASK-121 | **P2 – Cloud-Sync ist reines Status-Display**: `OnCloudSync()` prüft nur ob OneDrive/Dropbox-Ordner existieren und zeigt das an. Keine Sync-Logik. **Fix**: Button-Label zu „Cloud-Status prüfen" ändern oder echte Sync implementieren (Settings-JSON kopieren). **Test**: Keim funktionaler Test möglich — nur Label-Prüfung. | | |
| TASK-122 | **P2 – FTP-Source parst nur URL, verbindet nicht**: `OnFtpSource()` akzeptiert FTP-URL, zeigt Info an, verbindet aber nie zum Server. **Fix**: Entweder FTP-Client implementieren (FtpWebRequest/FluentFTP) oder als „Geplant" markieren. **Test**: Label-Prüfung. | | |
| TASK-123 | **P2 – Plugin-System lädt keine Plugins**: `OnPluginManager()` und `OnPluginMarketplace()` listen nur Dateien im plugins/-Ordner. Kein Plugin-Loading, keine Assembly-Reflexion, keine Manifest-Validierung gegen Schema. **Fix**: Mindestens Manifest-Validierung gegen `data/schemas/plugin-manifest.schema.json` implementieren, oder als „Geplant (v3.0)" markieren. **Test**: Plugin mit gültigem Manifest → wird als geladen angezeigt. | | |
| TASK-124 | **P3 – Docker-Button generiert nur Text**: `OnDockerContainer()` zeigt einen statischen Dockerfile und docker-compose.yml als Text. Kein File-Export, kein Docker-Build. **Fix**: SaveFile-Dialog anbieten zum Exportieren. Akzeptabel als Info-Feature — Prio niedrig. **Test**: Button zeigt Text → optional Datei speichern. | | |
| TASK-125 | **P3 – Spielzeit-Tracker liest nur .lrtl Dateien**: `OnPlaytimeTracker()` listet .lrtl-Dateien und zeigt Zeilenanzahl. Keine Zeitauswertung, keine Formatierung, keine Integration mit ROM-Sammlung. **Fix**: Akzeptabel als MVP — optimieren wenn gewünscht. **Test**: .lrtl-Dateien vorhanden → werden aufgelistet mit Zeilenanzahl. | | |
| TASK-126 | **P3 – Cover-Scraper matcht nur nach exaktem Dateinamen**: `OnCoverScraper()` vergleicht `Path.GetFileNameWithoutExtension(cover)` mit GameKey per `HashSet.Contains`. Kein Fuzzy-Match, keine No-Intro/Redump-Normalisation. Viele Cover werden nicht zugeordnet weil Cover-Dateinamen oft anders benannt sind. **Fix**: `GameKeyNormalizer.Normalize()` auf Cover-Dateinamen anwenden vor Vergleich. **Test**: Cover „Super Mario World (U).jpg" → matcht ROM „Super Mario World (USA) [!].sfc". | | |

### Runde 18 — Layout & Responsiveness

- GOAL-018: Layout-Probleme identifizieren, die bei verschiedenen DPI-Settings, Fenstergrößen oder Inhalten auftreten

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-127 | **P2 – Feature-Buttons haben fixe Width-Werte**: Alle 80+ Feature-Buttons verwenden `Width="110"` bis `Width="165"`. Bei höherer DPI, größerer Schrift (Accessibility) oder anderer Sprache wird der Text abgeschnitten oder die Buttons zu breit. **Fix**: `Width` entfernen, stattdessen `MinWidth` + `Padding` verwenden. WrapPanel sorgt für automatischen Umbruch. **Test**: Schriftgröße auf 18 setzen → kein Text abgeschnitten. | | |
| TASK-128 | **P2 – Kein Auto-Scroll im Log-ListBox**: Log-ListBox (Protokoll-Tab) hat Virtualisierung und ScrollViewer, aber keinen Auto-Scroll zum neuesten Eintrag. Benutzer muss manuell nach unten scrollen um den neuesten Log-Eintrag zu sehen. **Fix**: `ScrollIntoView` auf letztes Item bei `CollectionChanged`, oder Attached Behavior `AutoScrollToBottom`. **Test**: Log-Einträge hinzufügen → ListBox scrollt automatisch zum letzten Eintrag. | | |
| TASK-129 | **P3 – MainWindow MinWidth/MinHeight könnten knapp sein**: `MinWidth="720" MinHeight="520"`. Bei der Menge an Content (5 Tabs, Dashboard, 80+ Buttons) könnte 720px breite UI-Elemente abschneiden. Status-Bar ist bereits WrapPanel, aber DockPanel-Dock="Right" für QuickProfile-ComboBox + Report-Button könnte bei 720px übereinander fallen. **Fix**: Testen mit MinWidth → ggf. auf 800 erhöhen. **Test**: Window auf 720×520 verkleinern → kein Content abgeschnitten. | | |
| TASK-130 | **P3 – WebBrowser (IE) veraltet und problematisch**: `<WebBrowser x:Name="webReportPreview">` verwendet den eingebetteten Internet Explorer. IE11 ist seit 2022 deprecated. Rendering-Probleme bei modernem HTML/CSS (Flexbox, Grid). **Fix**: Migration zu Microsoft.Web.WebView2 (Edge/Chromium). NuGet: `Microsoft.Web.WebView2`. **Test**: Report mit CSS Grid → korrekt gerendert. | | |

### Runde 19 — Input-Validierung & Fehler-UX

- GOAL-019: Fehlende Eingabevalidierung und Fehler-Feedback identifizieren

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-131 | **P1 – Tool-Pfade TextBox ohne Existenz-Validierung**: Tool-Pfade (chdman, 7z, dolphintool, psxtract, ciso) werden als Strings akzeptiert ohne Prüfung ob die Datei existiert. Benutzer tippt falschen Pfad → Fehler erst bei Konvertierung zur Laufzeit. **Fix**: INotifyDataErrorInfo im ViewModel mit `File.Exists()`-Prüfung. XAML: `Validation.ErrorTemplate` mit rotem Rahmen. **Test**: Ungültigen Pfad eingeben → rotes TextBox-Border + Fehlertext. | | |
| TASK-132 | **P1 – DatRoot/TrashRoot/AuditRoot ohne Pfad-Validierung**: Analog zu TASK-131. Pfadfelder akzeptieren beliebigen Text. `Directory.Exists()`-Prüfung fehlt. **Fix**: INotifyDataErrorInfo mit Directory-Existenz-Check (lazy, nach 500ms Debounce). **Test**: Ungültigen Ordner eingeben → Warnung angezeigt. | | |
| TASK-133 | **P2 – Interaction.InputBox() komplett unthematisiert**: 8+ Stellen verwenden `Microsoft.VisualBasic.Interaction.InputBox()` (OnRomFilter, OnCommandPalette, OnFilterBuilder, OnSchedulerAdvanced, OnParallelHashing, OnAccessibility, OnCustomDatEditor ×4). `InputBox` ist ein VB6-Relikt mit eigener Fenster-Chrome — ignoriert Theme, Schriftart, DPI-Scaling. **Fix**: Eigene modalen Input-Dialog erstellen (`InputDialog.xaml`) mit Theme-Support. **Test**: InputBox erscheint im aktuellen Theme-Stil. | | |
| TASK-134 | **P2 – Keine Fehler-Templates auf TextBox-Inputs**: Kein `<Style TargetType="TextBox">` in den Themes definiert `Validation.ErrorTemplate`. Bei zukünftiger INotifyDataErrorInfo-Nutzung würde kein visuelles Feedback erscheinen. **Fix**: ErrorTemplate mit rotem Border und Tooltip in beide Themes einfügen. **Test**: Validation-Error → roter Rahmen + Fehler-Tooltip. | | |
| TASK-135 | **P2 – OnCustomDatEditor keine Hash-Validierung**: `OnCustomDatEditor()` (Zeile ~1740) akzeptiert CRC32 und SHA1 als freie Strings via InputBox ohne Format-Validierung. Benutzer kann "abc" als CRC32 eingeben. **Fix**: Regex-Prüfung: CRC32 = 8 Hex-Zeichen, SHA1 = 40 Hex-Zeichen. Bei Fehler → Meldung und Re-Prompt. **Test**: "xyz" als CRC32 → Fehlermeldung. | | |
| TASK-136 | **P2 – Data-Directory-Resolution fragil**: `RunCoreAsync()` (Zeile ~320) und 5+ Feature-Handler verwenden `Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data")` gefolgt von Fallback auf `Directory.GetCurrentDirectory()`. Im publizierten/installierten Szenario ist `BaseDirectory` ein anderer Ort. Wiederholt sich in `OnDatAutoUpdate`, `OnRuleEngine`, `OnRulePackSharing`, `OnApplyLocale`. **Fix**: Zentraler `DataDirectoryResolver` Service der einmal den data/-Pfad bestimmt und cached. **Test**: Im publizierten Build → data/ gefunden. | | |
| TASK-137 | **P3 – OnClosing async-Pattern mit re-entrantem Close()**: `OnClosing()` (Zeile ~230) ist `async void`, canceled den aktuellen Close mit `e.Cancel = true`, wartet auf Task, dann ruft `Close()` erneut auf. Der zweite `Close()`-Aufruf trigger `OnClosing` erneut — aber `_vm.IsBusy` ist jetzt false, also geht er durch. Theoretisch korrekt, aber fragil bei Timing-Edge-Cases (z.B. wenn zwischen `await` und `Close()` ein neuer Run gestartet wird). **Fix**: Boolean `_isClosing` Guard um Re-Entrance zu verhindern. **Test**: Während Lauf Window schließen → Clean-Shutdown ohne Exception. | | |

### Runde 20 — System-Tray, Watch-Mode & Spezial-Features

- GOAL-020: Verbleibende UX-Bugs in Sonder-Features (Tray, Watch, Backup, Integrity)

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-138 | **P1 – System-Tray Icon-Handle nicht freigegeben**: `OnSystemTray()` (Zeile ~2340) erstellt ein `Bitmap`, ruft `GetHicon()` auf, aber der native HICON-Handle wird nie freigegeben (`DestroyIcon` nicht aufgerufen). GDI-Leak bei wiederholtem Tray-Toggle. **Fix**: `[DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle)` nach Icon-Erstellung, oder .ico-Datei aus Embedded Resource verwenden. **Test**: Tray 10× togglen → kein GDI-Handle-Leak (Perfmon „GDI Objects" stabil). | | |
| TASK-139 | **P1 – Watch-Mode FileSystemWatcher-Throttling unzureichend**: `OnWatcherFileChanged` setzt Debounce-Timer auf 5 Sekunden. Aber bei einer großen Batch-Copy-Operation (tausende Dateien) wird der Timer ständig resettet und der DryRun startet erst 5s nach dem letzten Event. Wenn der Kopiervorgang 30+ Sekunden dauert, startet kein DryRun während des Kopiervorgangs, aber sofort 5s danach — potenziell während die Dateien noch geschrieben werden. **Fix**: Maximale Wartezeit (z.B. 30s) einführen. Nach 30s wird der DryRun unabhängig vom Debounce gestartet. **Test**: 10.000 Dateien kopieren → DryRun startet maximal 30s nach erster Änderung. | | |
| TASK-140 | **P2 – Backup-Manager kopiert nur Winner ohne Verzeichnisstruktur**: `FeatureService.CreateBackup()` (Zeile ~560) kopiert Dateien flach in einen Ordner: `Path.Combine(sessionDir, Path.GetFileName(path))`. Wenn zwei Winner denselben Dateinamen haben (verschiedene Konsolen), überschreibt `File.Copy(path, dest, overwrite: false)` zwar nicht (overwrite=false), aber wirft eine IOException. **Fix**: Relative Verzeichnisstruktur beibehalten: `Path.Combine(sessionDir, relativePath)`. **Test**: Zwei Dateien gleichen Namens aus verschiedenen Ordnern → beide im Backup erhalten. | | |
| TASK-141 | **P2 – Backup CleanupOldBackups löscht rekursiv ohne Bestätigung**: `FeatureService.CleanupOldBackups()` ruft `Directory.Delete(dir, recursive: true)` auf Backup-Verzeichnisse. Der Aufrufer (`OnBackupManager`) ruft dies nie auf — die Methode existiert aber als API und könnte versehentlich genutzt werden ohne User-Confirm. **Fix**: Methode sollte Confirm-Callback enthalten oder mindestens ein Dry-Run-flag. **Test**: CleanupOldBackups → mindestens Logging vor Delete. | | |
| TASK-142 | **P2 – Integrity-Baseline speichert absolute Pfade**: `FeatureService.CreateBaseline()` speichert Dateipfade als `Dictionary<string, IntegrityEntry>` mit absoluten Pfaden als Keys. Wenn der ROM-Ordner verschoben wird, matcht die Baseline nicht mehr. **Fix**: Pfade relativ zum Root speichern. **Test**: Ordner umbenennen → Baseline funktioniert mit neuem Pfad. | | |
| TASK-143 | **P3 – System-Tray StateChanged-Handler wird mehrfach registriert**: `OnSystemTray()` registriert `StateChanged += OnWindowStateChanged` bei jedem Aufruf. Wenn der Button mehrfach gedrückt wird (ohne Toggle), werden mehrere Handler registriert. **Fix**: Handler nur einmal registrieren (z.B. im Konstruktor oder mit Guard). **Test**: Button 5× drücken → `OnWindowStateChanged` feuert nur 1× pro StateChange. | | |
| TASK-144 | **P3 – PresetFullSort setzt DryRun=true trotz Name "Volle Sortierung"**: `MainViewModel.OnPresetFullSort()` setzt `DryRun = true`. Benutzer erwartet bei „Volle Sortierung" einen Move-Lauf, bekommt aber DryRun. **Fix**: `DryRun = false` oder Preset-Label ändern zu „Volle Sortierung (Vorschau)". **Test**: Quick-Start „Volle Sortierung" → DryRun Flag korrekt gesetzt. | | |
| TASK-145 | **P3 – App.xaml.cs hat keinen globalen Exception-Handler**: `App.xaml.cs` ist leer. Kein `DispatcherUnhandledException`-Handler. Unbehandelte Exceptions (z.B. aus `async void` Event-Handlers) crashen die App ohne Fehlermeldung. **Fix**: `DispatcherUnhandledException` Handler in App.xaml.cs der Fehler loggt und anzeigt. `AppDomain.CurrentDomain.UnhandledException` für Background-Thread-Crashes. **Test**: Exception simulieren → Fehlerdialog statt Crash. | | |

### Runde 21 — Zusammenfassung & Priorisierung

- GOAL-021: Alle UX/UI-Findings konsolidieren und nach Priorität/Kategorie sortieren

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-146 | **Konsolidierung P0 (3 Findings)**: TASK-084 (Konsolen-Filter unwirksam), TASK-085 (Dateityp-Filter unwirksam), ergänzt die P0s aus Audit 1 (TASK-031, TASK-032). Zusammen mit SimpleRegionIndex (TASK-032 aus Audit 1) sind 3 zentrale UI-Controls komplett wirkungslos — der Benutzer hat keine Kontrolle über Konsolen-Filter, Dateityp-Filter, und Simple-Mode-Region. Alle P0s müssen vor Release gefixt werden. | | |
| TASK-147 | **Konsolidierung P1 (14 Findings)**: TASK-086 (DatHash-Binding), TASK-087 (LogLevel-Binding), TASK-094 (ShowTextDialog Theme), TASK-095 (MessageBox Theme), TASK-102 (Accessibility Names), TASK-103 (Keyboard-Shortcuts), TASK-110 (Code-Behind 3200 Zeilen), TASK-111 (81 Buttons Click statt Command), TASK-117 (Locale nur Title), TASK-118 (Scheduler plant nichts), TASK-131 (Tool-Pfad-Validierung), TASK-132 (Ordner-Validierung), TASK-138 (GDI-Leak Tray), TASK-139 (Watch-Mode Throttling). Fix-Reihenfolge: Data-Binding (086,087) → Security/Leak (138) → Theme (094,095) → Validation (131,132) → Accessibility (102,103) → Refactoring (110,111). | | |
| TASK-148 | **Konsolidierung P2 (28 Findings)**: TASK-088, TASK-089, TASK-090, TASK-091, TASK-096, TASK-097, TASK-098, TASK-104, TASK-105, TASK-106, TASK-107, TASK-112, TASK-113, TASK-114, TASK-119, TASK-120, TASK-121, TASK-122, TASK-123, TASK-127, TASK-128, TASK-133, TASK-134, TASK-135, TASK-136, TASK-140, TASK-141, TASK-142. Gruppierbar: Binding-Gaps (5), Theme (3), Accessibility (4), MVVM (3), Phantom-Features (5), Layout (2), Validation (4), System (2). | | |
| TASK-149 | **Konsolidierung P3 (17 Findings)**: TASK-092, TASK-093, TASK-099, TASK-100, TASK-101, TASK-108, TASK-109, TASK-115, TASK-116, TASK-124, TASK-125, TASK-126, TASK-129, TASK-130, TASK-137, TASK-143, TASK-144, TASK-145. Niedrige Priorität, fortlaufend bei Gelegenheit beheben. | | |

---

## 3. Alternatives

- **ALT-001**: Alle Dead-UI-Controls (CheckBoxen, ComboBoxen ohne Binding) sofort entfernen. Abgelehnt: Besser Bindings implementieren, da die Controls User-Erwartungen setzen und die Features geplant sind.
- **ALT-002**: Statt einzelne Feature-Buttons zu ICommands zu migrieren, einen generischen Feature-Dispatcher verwenden (`Command="{Binding FeatureCommand}" CommandParameter="xyz"`). Bevorzugt: Reduziert 81 einzelne Commands auf 1 generischen Command + Dictionary-Dispatch. Genau dieser Pattern ist in `ExecuteCommand()` bereits begonnen.
- **ALT-003**: Statt `Microsoft.VisualBasic.Interaction.InputBox` eine Dependency auf ein WPF-Toolkit (MahApps.Metro, HandyControl) nehmen. Abgelehnt: Externe Dependency für simple Dialoge zu schwer. Eigenen InputDialog als UserControl in 50 Zeilen.
- **ALT-004**: WebBrowser durch CefSharp statt WebView2 ersetzen. Abgelehnt: CefSharp ist viel größer (~200MB). WebView2 nutzt den bereits installierten Edge-Browser.
- **ALT-005**: High-Contrast komplett ignorieren und nur Dark/Light anbieten. Akzeptabel als kurzfristiger Kompromiss — Windows HC-Mode wird von WPF automatisch erkannt wenn keine ResourceDictionaries die SystemColors überschreiben.

---

## 4. Dependencies

- **DEP-001**: `Microsoft.Web.WebView2` NuGet-Paket für TASK-130 (WebBrowser-Ersatz)
- **DEP-002**: INotifyDataErrorInfo Support in MainViewModel für TASK-131/TASK-132 (Input-Validierung)
- **DEP-003**: Feature-Deep-Dive Audit 1 TASK-031/TASK-032 (Konsolen-Filter & SimpleMode) — überschneidet sich mit TASK-084/TASK-085

---

## 5. Files

- **FILE-001**: `src/RomCleanup.UI.Wpf/MainWindow.xaml` — XAML-Layout (~1150 Zeilen), betroffen von TASK-084–093, 097, 099, 102–109, 127–129
- **FILE-002**: `src/RomCleanup.UI.Wpf/MainWindow.xaml.cs` — Code-Behind (~3200 Zeilen), betroffen von TASK-094, 095, 110–116, 131–145
- **FILE-003**: `src/RomCleanup.UI.Wpf/ViewModels/MainViewModel.cs` — ViewModel (~560 Zeilen), betroffen von TASK-084–093, 111, 131–132
- **FILE-004**: `src/RomCleanup.UI.Wpf/Themes/SynthwaveDark.xaml` — Dark Theme (~600 Zeilen), betroffen von TASK-096–099, 134
- **FILE-005**: `src/RomCleanup.UI.Wpf/Themes/Light.xaml` — Light Theme (~600 Zeilen), betroffen von TASK-096–099, 134
- **FILE-006**: `src/RomCleanup.UI.Wpf/Services/FeatureService.cs` — Feature-Backend (~1200 Zeilen), betroffen von TASK-119–126, 140–142
- **FILE-007**: `src/RomCleanup.UI.Wpf/Services/DialogService.cs` — Dialog-Wrapper (~130 Zeilen), betroffen von TASK-095, 133
- **FILE-008**: `src/RomCleanup.UI.Wpf/Services/ThemeService.cs` — Theme-Switching (~50 Zeilen), betroffen von TASK-094, 101, 106
- **FILE-009**: `src/RomCleanup.UI.Wpf/Services/StatusBarService.cs` — Status-Wrapper (~15 Zeilen), betroffen von TASK-115
- **FILE-010**: `src/RomCleanup.UI.Wpf/App.xaml.cs` — App-Einstieg (~8 Zeilen), betroffen von TASK-145
- **FILE-011**: `src/RomCleanup.UI.Wpf/Converters/Converters.cs` — Value Converters (~90 Zeilen), potenziell betroffen von nötigen neuen Convertern für Step-Dots
- **FILE-012**: `src/RomCleanup.UI.Wpf/RelayCommand.cs` — ICommand-Impl (~35 Zeilen), betroffen von TASK-111

---

## 6. Testing

- **TEST-001**: UI-Automation-Test: Checkbox `chkConsPS1` setzen → ViewModel-Property reflektiert Zustand (TASK-084)
- **TEST-002**: UI-Automation-Test: Extension-Checkboxen → RunOptions.Extensions enthält nur selektierte (TASK-085)
- **TEST-003**: Unit-Test: `MainViewModel.GetPreferredRegions()` nach SimpleRegionIndex-Änderung (TASK-032 aus Audit 1)
- **TEST-004**: UI-Automation-Test: cmbDatHash ändern → VM.DatHashType aktualisiert (TASK-086)
- **TEST-005**: Visueller Regressions-Test: ShowTextDialog im Light-Theme hat hellen Hintergrund (TASK-094)
- **TEST-006**: Accessibility Insights Scan: alle Controls haben Name + Role (TASK-102)
- **TEST-007**: Keyboard-Navigation-Test: Tab durch alle Controls → logische Reihenfolge (TASK-104)
- **TEST-008**: Unit-Test: MainViewModel Commands statt Code-Behind Handlers (TASK-111)
- **TEST-009**: Integration-Test: ConflictPolicy="Skip" → bei Namenskollision wird übersprungen (TASK-114)
- **TEST-010**: GDI-Handle-Leak-Test: Tray 10× togglen → GDI-Count stabil (TASK-138)
- **TEST-011**: Input-Validierung: ungültiger Pfad → TextBox zeigt Fehlerindikator (TASK-131)
- **TEST-012**: Smoke-Test: DispatcherUnhandledException → Fehlerdialog statt Crash (TASK-145)

---

## 7. Risks & Assumptions

- **RISK-001**: Refactoring von 81 Code-Behind-Handlers zu Commands (TASK-110/111) ist ein großes Refactoring mit hohem Regressions-Risiko. Stufenweise Migration empfohlen (5-10 Handlers pro PR).
- **RISK-002**: WebView2-Migration (TASK-130) erfordert dass Edge Runtime auf Zielsystemen installiert ist. .NET 10 SDK enthält Evergreen-Runtime, aber ältere Systeme könnten betroffen sein.
- **RISK-003**: Lokalisierung (TASK-117) ist ein umfangreicher Cross-Cutting-Concern. Vollständige i18n-Migration betrifft jede XAML-Datei und erfordert Resource-Key-Architektur.
- **RISK-004**: Custom InputDialog (TASK-133) als Ersatz für VB6 InputBox muss in allen Szenarien (multi-monitor, DPI-scaling, theme-switch) funktionieren.
- **ASSUMPTION-001**: Alle Feature-Buttons bleiben in der UI (werden nicht entfernt), auch wenn die Funktion noch nicht vollständig implementiert ist. Phantom-Features werden mit „Experimentell"-Label versehen.
- **ASSUMPTION-002**: WCAG 2.1 AA ist das Ziel-Level für Barrierefreiheit (wie in GUI-22 Kommentar im Code).
- **ASSUMPTION-003**: Die 3 P0-Findings (TASK-084, TASK-085 + TASK-032 aus Audit 1) werden als Release-Blocker behandelt.

---

## 8. Related Specifications / Further Reading

- [plan/feature-deep-dive-bug-audit-1.md](plan/feature-deep-dive-bug-audit-1.md) — Initialer Deep-Dive (83 Findings, TASK-001–TASK-083)
- [docs/REVIEW_CHECKLIST.md](docs/REVIEW_CHECKLIST.md) — Review-Checkliste
- [docs/ARCHITECTURE_MAP.md](docs/ARCHITECTURE_MAP.md) — Architekturübersicht
- [.github/copilot-instructions.md](.github/copilot-instructions.md) — GUI-Pflichten, Security-Regeln, Theme-Vorgaben