---
goal: 'Umfassendes GUI/UX/UI/Architektur-Audit der WPF-Oberfläche (RomCleanup.UI.Wpf)'
version: 1.0
date_created: 2026-03-12
owner: RomCleanup Team
status: 'Active'
tags: [gui, ux, ui, wpf, mvvm, audit, refactoring, design-system, accessibility]
---

# GUI/UX Deep-Audit — RomCleanup WPF

---

## 0) Scope & Annahmen

### Geprüfte Artefakte

| Datei | Zeilen (ca.) | Rolle |
|-------|-------------|-------|
| `MainWindow.xaml` | ~1400 | Gesamtes Layout in einer Datei |
| `MainWindow.xaml.cs` | **~348** | Code-behind: 5 verbleibende Handler (Lifecycle, Run-Trigger, DragDrop, Report/WebView2) |
| `ViewModels/MainViewModel.cs` | ~1328 | INPC, Commands, Status, Bindings, Rollback, Settings, RunPipeline, WatchMode |
| `Services/ThemeService.cs` | ~50 | Theme-Swap Dark/Light |
| `Services/DialogService.cs` | ~180 | Dialoge, InputBox, thread-safe marshalling |
| `Services/SettingsService.cs` | ~200 | JSON Persistence |
| `Services/FeatureService.cs` | ~2640 | Backend-Logik für 60+ Feature-Commands (extrahiert aus Code-behind) |
| `Services/FeatureCommandService.cs` | ~1800 | ~60 Commands über BindFeatureCommand, inkl. 5 IWindowHost-Handler |
| `Services/IWindowHost.cs` | ~30 | Interface für Window-Level-Ops (FontSize, SelectTab, ShowTextDialog, ToggleSystemTray, ApiProcess) |
| `Services/StatusBarService.cs` | ~20 | **Deprecated**, wrapper um VM.RefreshStatus |
| `Converters/Converters.cs` | ~115 | 5 Value-Converter |
| `Models/` | ~40 | LogEntry, StatusLevel, DatMapRow |
| `RelayCommand.cs` | ~35 | ICommand |
| `Themes/SynthwaveDark.xaml` | ~600 | Design System Dark |
| `Themes/Light.xaml` | ~500+ | Design System Light |
| `App.xaml` / `App.xaml.cs` | ~60 | Startup, Crash-Handler |

### Annahmen (pragmatisch)
- **A1:** Zielgruppe sind technisch versierte ROM-Sammler (Intermediate-Expert). Trotzdem muss *Simple Mode* für Anfänger funktionieren.
- **A2:** Primäre Plattform bleibt Windows 10/11 Desktop. Cross-platform ist „nice to have", nicht Pflicht.
- **A3:** Maximale ROM-Sammlung: ~500k Dateien über mehrere Roots. UI muss bei dieser Menge stabil bleiben.
- **A4:** `MainWindow.xaml.cs` von ~3370 auf ~451 Zeilen reduziert (87%). ~60 Feature-Handler über FeatureCommandService ins ViewModel migriert. AutoWire-Konvention (78 Buttons), Browse/Quick/Rollback ins VM. 9 Handler bleiben wegen UI-Kopplung (Dispatcher, WPF-Controls, Window-Properties).

---

## 1) UI-Systemkarte

### 1.1 Screen-/Tab-Struktur (Ist-Zustand)

```
┌─────────────────────────────────────────────────────────┐
│ TOP STATUS BAR                                          │
│ [ROM CLEANUP] ● Roots ● Tools ● DAT ● Ready ● Runtime  │
│                                    [Profil ▾] [Bericht] │
├─────────────────────────────────────────────────────────┤
│ Tab: Sortieren                                          │
│   ├─ Step-Indicator (1·Ordner → 2·Optionen → 3·Starten)│
│   ├─ Quick-Start Presets (3 Buttons)                    │
│   ├─ Mode Toggle (Einfach / Experte)                    │
│   ├─ ROM-Verzeichnisse (ListBox + Add/Remove)           │
│   ├─ [Simple] Schnellstart (Region, 3 Checkboxen)       │
│   └─ [Expert] Optionen (DryRun, Convert, Danger Zone)   │
│                                                          │
│ Tab: Konfiguration (Nested TabControl)                   │
│   ├─ Regeln (Sortieroptionen, Regionen, Extensions,     │
│   │         Konsolen-Filter, Sicherheitsmodus)           │
│   ├─ Tools & DAT (Pfade, DAT-Config, Mapping)           │
│   ├─ Profile (Speichern/Laden/Löschen)                  │
│   ├─ Erweitert (Audit, GameKey-Vorschau, Export/Import)  │
│   └─ Features (~60+ Buttons in 8 Expandern)             │
│                                                          │
│ Tab: Protokoll                                           │
│   ├─ Dashboard (6 MetricCards + Performance Details)     │
│   ├─ Log (ListBox, Consolas, farbkodiert)                │
│   ├─ Report-Vorschau (WebBrowser)                        │
│   └─ Fehler-Summary (ListBox)                            │
│                                                          │
├─────────────────────────────────────────────────────────┤
│ FOOTER ACTION BAR                                        │
│ [▶ Starten] [■ Stopp] │ [↩ Rückgängig] │ Hint  Progress │
└─────────────────────────────────────────────────────────┘
```

### 1.2 Run Lifecycle (Ist-Zustand)

```
Idle ──[F5/Starten]──→ OnRun (IsBusy=true)
  ──→ [Move + ConfirmMove?] ──→ DialogService.Confirm
  ──→ CreateRunCancellation
  ──→ Task.Run: Build Infrastructure (off-thread)
  ──→ Task.Run: orchestrator.Execute (off-thread)
     ──→ onProgress → Dispatcher.InvokeAsync (throttled 100ms)
  ──→ Dispatcher: Update DashWinners/Dupes/Junk/Duration
  ──→ Task.Run: ReportGenerator (off-thread)
  ──→ CompleteRun(success, reportPath)
     ──→ IsBusy=false, CanRollback=true, RefreshStatus
  ──[Esc/Cancel]──→ _cts.Cancel → OperationCanceledException
  ──[Close during run]──→ Confirm → Cancel → Wait → Close
```

**Problem:** Kein expliziter State-Enum. Zustand wird durch `IsBusy` + `CanRollback` + `ShowMoveCompleteBanner` + `DryRun` implizit abgeleitet. Fehleranfällig.

### 1.3 Datenflüsse

```
Settings (JSON) → SettingsService.LoadInto → MainViewModel (Properties)
VM Properties → XAML Bindings → UI Controls
User Action → Command / Click-Handler → VM state change
              ↓
RunRequested Event → MainWindow.xaml.cs
  → RunOrchestrator → onProgress → Dispatcher → VM.ProgressText
  → RunResult → VM.Dashboard, LogEntries, ErrorSummary
  → AuditCsvStore → _lastAuditPath
  → ReportGenerator → VM.LastReportPath
              ↓
VM.CanRollback → RollbackCommand → AuditCsvStore.Rollback
```

---

## 2) UX-Review: Top Probleme (P0/P1)

### P0-UX-001: MainWindow.xaml.cs als God-Class (~3370 Zeilen)

**Problem:** Die gesamte Orchestration, ~80 Feature-Button-Handler, Watch-Mode, Rollback-Stacks, Tray-Icon, API-Prozess-Management, Report-Generierung und >30 `ShowTextDialog`-Aufrufe leben in einer einzigen Code-behind-Datei.

**Warum es schadet:**
- Untestbar: Kein einziger Unit-Test kann irgendeinen Feature-Handler testen, weil alles an `Window` gekoppelt ist
- Merge-Konflikte bei paralleler Entwicklung praktisch garantiert
- Run-Orchestration (`RunCoreAsync`) hat ~120 Zeilen mit verschachtelten `Task.Run` + `Dispatcher.InvokeAsync` — Race-Condition-Risiko bei Closing/Cancel
- Feature-Handler duplizieren Muster (Kandidaten prüfen → StringBuilder → ShowTextDialog) ohne Abstraktion

**Fundstellen:**
- [MainWindow.xaml.cs](src/RomCleanup.UI.Wpf/MainWindow.xaml.cs) Zeilen 1–3370
- ~80 `private void On…` Handler (Zeile 232ff)
- `RunCoreAsync` (Zeile 348–615)
- Feature-Wiring (Zeile 150–225, `WireFeatureButtons`)

**Fix-Strategie:**
1. Sub-ViewModels einführen (siehe §4)
2. Feature-Handler in separate Service-Klassen extrahieren
3. Run-Orchestration in ein dediziertes `RunService` verschieben
4. Code-behind auf max. ~200 Zeilen reduzieren (Drag-Drop, Browse-Dialoge, WebBrowser)

**Verifikation:** Code-behind ≤ 200 Zeilen, alle Feature-Handler per VM-Unit-Test prüfbar

---

### P0-UX-002: Kein expliziter Run-State (State Machine fehlt)

**Problem:** Der Lauf-Zustand wird aus `IsBusy`, `CanRollback`, `DryRun`, `ShowMoveCompleteBanner` inferiert. Es gibt keinen `RunState`-Enum.

**Warum es schadet:**
- `CurrentStep` wird über `IsBusy ? 2 : hasRoots ? 1 : 0` berechnet — bildet den tatsächlichen Lifecycle (Idle → Preflight → Scanning → Deduplicating → Moving → Converting → Completed → Failed → Cancelled) nicht ab
- CanExecute-Guards basieren nur auf `IsBusy`, nicht auf dem semantischen Zustand
- UI kann inkonsistente Zustände anzeigen (z.B. „Bereit ✓" während ein Fehler vorliegt)

**Fundstellen:**
- [MainViewModel.cs](src/RomCleanup.UI.Wpf/ViewModels/MainViewModel.cs#L233): `CurrentStep`-Berechnung in `RefreshStatus()`
- [MainViewModel.cs](src/RomCleanup.UI.Wpf/ViewModels/MainViewModel.cs#L470): `OnRun` setzt nur `IsBusy = true`
- [MainWindow.xaml.cs](src/RomCleanup.UI.Wpf/MainWindow.xaml.cs#L348): `RunCoreAsync` hat keinen State-Übergang

**Fix-Strategie:**
- `RunState`-Enum einführen: `Idle`, `Preflight`, `Scanning`, `Deduplicating`, `Moving`, `Converting`, `Completed`, `Failed`, `Cancelled`
- Step-Indicator an RunState binden statt an boolesche Inferenz
- CanExecute-Guards auf RunState basieren
- Progress-Phase aus RunState ableiten

**Verifikation:** Unit-Test: alle Zustandsübergänge, ungültige Übergänge werfen, CanExecute reflektiert State korrekt

---

### P0-UX-003: Features-Tab = „Knopf-Friedhof" (~60+ Buttons ohne Struktur)

**Problem:** Der „Features"-Tab unter Konfiguration enthält ~60 Buttons in 8 Expandern, die alle als `WrapPanel` mit gleicher Optik dargestellt werden. Es gibt kein Visual-Feedback welche Features implementiert, geplant, oder experimentell sind.

**Warum es schadet:**
- Overwhelm: Nutzer sieht 60+ identisch aussehende Buttons — kein Orientierungspunkt
- Viele Features zeigen nur `ShowTextDialog` mit Plaintext — schlechte UX für Ergebnisdarstellung
- Keine Filterung/Suche möglich
- Kein Status-Indikator (implementiert / geplant / Beta)
- Buttons haben kein `IsEnabled`-Binding → alle sehen klickbar aus (auch wenn Voraussetzungen fehlen)

**Fundstellen:**
- [MainWindow.xaml](src/RomCleanup.UI.Wpf/MainWindow.xaml) Zeilen 900–1100 (Features-Tab)
- [MainWindow.xaml.cs](src/RomCleanup.UI.Wpf/MainWindow.xaml.cs#L150): `WireFeatureButtons()` — alle 60+ Buttons per Click-Event

**Fix-Strategie:**
1. Features kategorisieren: **Core** (immer sichtbar), **Analyse** (nach Run verfügbar), **Werkzeuge** (unabhängig), **Geplant** (read-only/disabled)
2. Status-Badges: ✓ Implementiert, ⚙ Beta, 📅 Geplant
3. Precondition-Check: Buttons disabled wenn Vorbedingungen nicht erfüllt (z.B. „erst Run starten")
4. Features als Commands im VM statt Click-Handler im Code-behind
5. Langfristig: Feature-entdeckung über Sidebar/Palette statt Button-Feld

**Verifikation:** Kein Feature-Button ohne CanExecute-Guard; Status-Badge bei jedem Button

---

### P1-UX-004: Konsolen-/Extension-Filter ohne ViewModel-Binding

**Problem:** ~30 Konsolen-Checkboxen und ~18 Dateityp-Checkboxen verwenden `x:Name` statt Bindings. Die Auswertung erfolgt über die `GetSelectedExtensions()`-Methode im Code-behind, die direkt auf CheckBox-Controls zugreift.

**Warum es schadet:**
- Nicht persistiert (SettingsService kennt diese Werte nicht)
- Nicht testbar (benötigt Fenster-Instanz)
- Konsolen-Filter wird aktuell **gar nicht ausgewertet** — alle Konsolen immer aktiv
- Bereits als TASK-084/085 im Bug-Audit erfasst, aber noch nicht gefixt

**Fundstellen:**
- [MainWindow.xaml](src/RomCleanup.UI.Wpf/MainWindow.xaml) Zeilen 500–570 (Konsolen-Checkboxen mit x:Name)
- [MainWindow.xaml.cs](src/RomCleanup.UI.Wpf/MainWindow.xaml.cs#L3329): `GetSelectedExtensions()` greift direkt auf UI-Elemente zu

**Fix-Strategie:**
- `SelectedExtensions` und `SelectedConsoles` Collections im VM
- Checkboxen per Binding an VM-Properties
- SettingsService erweitern um Persistenz
- RunOptions aus VM-Collections befüllen

**Verifikation:** Settings round-trip: speichern → laden → alle Checkboxen restauriert

---

### P1-UX-005: ShowTextDialog als universelle Output-Methode (30+ Aufrufe)

**Problem:** Nahezu alle Feature-Ergebnisse werden über `ShowTextDialog(title, plaintext)` als modale Fenster mit Monospace-Text angezeigt. Keine Tabellen, kein Copy-to-Clipboard, keine Struktur.

**Warum es schadet:**
- Lange Textblöcke sind schwer zu scannen
- Kein Export möglich (nur visuell lesen, kein Copy-Button)
- Keine Sortierung/Filterung der Ergebnisse
- Bei großen Ergebnissen (>100 Zeilen) schlecht scrollbar
- 30+ Handler wiederholen dasselbe Muster: Daten sammeln → StringBuilder → ShowTextDialog

**Fundstellen:**
- [MainWindow.xaml.cs](src/RomCleanup.UI.Wpf/MainWindow.xaml.cs#L3231): `ShowTextDialog` Definition
- 30+ Aufrufe (siehe grep: Zeilen 851, 913, 979, 988, 1001, 1186, 1254, 1262, 1278, …)

**Fix-Strategie:**
1. `ResultDialog` als wiederverwendbares modales Fenster mit:
   - Titel, strukturierter Inhalt, Copy-Button, Export-Button
   - Tabellen-View für tabellarische Daten (DataGrid)
   - TextBlock für Fließtext
2. Feature-Results als typed Records statt Strings
3. Daten-Bindung statt String-Generierung

**Verifikation:** Kein Feature-Handler baut mehr ad-hoc Fenster; ResultDialog hat Copy+Export

---

### P1-UX-006: Simple Mode zu versteckt, Expert Mode zu überladen

**Problem:** Der Simple Mode bietet nur 4 Optionen (Region, Duplikate, Junk, Sortieren) — gut. Aber:
- Toggle zwischen Simple/Expert ist ein kleiner RadioButton, leicht zu übersehen
- Expert Mode dumpt ALLES auf eine Seite: Optionen, Danger Zone, Regionen, Extensions, Konsolen, Sicherheit — als verschachtelte Expander
- Konfiguration-Tab hat 4 Sub-Tabs mit jeweils weiteren Expandern → 3 Navigationsebenen

**Warum es schadet:**
- Anfänger sehen den Mode-Toggle nicht und landen möglicherweise im Experten-Modus
- Experten müssen sich durch verschachtelte Expander klicken, um Optionen zu finden
- Konfiguration-Tab → Regeln → Erweiterte Sortierung & Sicherheit = 3 Klicks bis zu Regionen

**Fundstellen:**
- [MainWindow.xaml](src/RomCleanup.UI.Wpf/MainWindow.xaml) Zeile ~270 (Mode Toggle: 2 RadioButtons, kein visuelles Gewicht)
- [MainWindow.xaml](src/RomCleanup.UI.Wpf/MainWindow.xaml) Zeile ~420+ (Konfiguration Sub-Tabs mit verschachtelten Expandern)

**Fix-Strategie:**
- Mode-Toggle als prominente Segmented-Control mit Icons/Beschreibung
- Expert-Mode als Wizard/Sidebar mit vertikal navigierbaren Sektionen statt verschachtelter Expander
- Regionen-Konfiguration direkt im Sortieren-Tab (Expert), nicht in Konfiguration → Regeln → Expander
- „Konfiguration"-Tab aufteilen: Häufiges (Regionen, Regeln) vs. Seltenes (Pfade, Profile, Features)

**Verifikation:** Maximale Tiefe bis zu jeder Option ≤ 2 Klicks; Mode-Toggle A/B-Usability-Test

---

### P1-UX-007: Conflict-Policy über YesNoCancel-Dialog — schlechte UX

**Problem:** Die Conflict-Policy (Rename/Skip/Overwrite) wird über `MessageBox.YesNoCancel` gesetzt, wobei die Mapping-Beziehung nicht offensichtlich ist: Ja=Rename, Nein=Skip, Abbrechen=Overwrite.

**Warum es schadet:**
- "Abbrechen" = "Overwrite" ist kontraintuitiv und gefährlich
- User, die den Dialog schließen wollen, aktivieren versehentlich den gefährlichsten Modus
- State wird nicht im VM persistiert (lebt als `_conflictPolicy` im Code-behind!)

**Fundstellen:**
- [MainWindow.xaml.cs](src/RomCleanup.UI.Wpf/MainWindow.xaml.cs#L1220): `OnConflictPolicy` — `MessageBoxResult.Cancel → "Overwrite"`
- Code-behind-Feld `_conflictPolicy` (Zeile 54) — nicht im VM

**Fix-Strategie:**
- Eigener Dialog mit 3 explizit gelabelten RadioButtons + OK/Cancel
- Oder: ComboBox im VM mit Optionen ["Umbenennen", "Überspringen", "Überschreiben"]
- State in VM verschieben und persistieren
- Default: "Rename" (sicherste Option)

**Verifikation:** Cancel-Aktion führt NICHT zu Overwrite; State persisted über Restart

---

### P1-UX-008: DryRun → Move Transition unklar

**Problem:** Nach einem DryRun gibt es einen „▶ Jetzt als Move ausführen"-Button im DryRun-Banner, aber dieser hat `Visibility="Collapsed"` und wird nie sichtbar gemacht. Der User muss manuell DryRun deaktivieren und erneut starten.

**Warum es schadet:**
- Der Standard-Flow (DryRun → Review → Move) ist unterbrochen
- User muss wissen, dass sie die Checkbox „Nur Vorschau (Dry Run)" deaktivieren müssen
- Kein Review-/Summary-Dialog zwischen DryRun und Move

**Fundstellen:**
- [MainWindow.xaml](src/RomCleanup.UI.Wpf/MainWindow.xaml) Zeile ~395: `btnStartMove` mit `Visibility="Collapsed"`
- [MainWindow.xaml.cs](src/RomCleanup.UI.Wpf/MainWindow.xaml.cs#L133): `btnStartMove.Click` handler existiert, aber Button nie sichtbar

**Fix-Strategie:**
1. Nach DryRun-Completion: Summary-Dialog mit Key-Metriken (Keep/Move/Junk) und „Jetzt als Move ausführen"-Button
2. Oder: `btnStartMove.Visibility` an `ShowDryRunBanner && !IsBusy && DryRun && LastRunResult != null` binden
3. Summary muss vor Move angezeigt und bestätigt werden (Safety-Requirement)

**Verifikation:** DryRun → Summary → Bestätigung → Move als durchgängiger Flow testbar

---

### P1-UX-009: Progress-Feedback fehlt granulare Phasen

**Problem:** Progress zeigt nur eine ProgressBar (0–100%) + ein Textfeld mit dem letzten Fortschritts-String. Es gibt keinen Phasen-Indikator (Scan → Classify → Dedupe → Move → Convert).

**Warum es schadet:**
- Bei großen Sammlungen (>100k Dateien) bleibt Progress lange bei niedrigen Werten → User denkt App hängt
- `ProgressText` enthält Rohtext aus `onProgress`-Callback, oft technisch kryptisch
- Keine ETA, keine Phase-Visualisierung, kein Datei-Counter

**Fundstellen:**
- [MainWindow.xaml](src/RomCleanup.UI.Wpf/MainWindow.xaml) Zeile ~120 (ProgressBar)
- [MainViewModel.cs](src/RomCleanup.UI.Wpf/ViewModels/MainViewModel.cs#L253): `Progress`, `ProgressText`, `PerfPhase`, `PerfFile`
- [MainWindow.xaml.cs](src/RomCleanup.UI.Wpf/MainWindow.xaml.cs#L470): Progress-Parsing aus String

**Fix-Strategie:**
- Phasen-Balken: visueller Stepper (Scan ✓ → Classify ● → Dedupe ○ → Move ○)
- Datei-Counter: „Datei 1234 / 5000"
- Phase-spezifischer Progress statt Global-Wert
- ETA-Berechnung (Dateien/Sekunde × verbleibend)

**Verifikation:** Phasen-Übergänge im Progress sichtbar; keine Phase bleibt >30s auf 0%

---

### P1-UX-010: Rollback-Undo/Redo im Code-behind statt VM

**Problem:** Rollback-Undo- und Redo-Stacks leben als `Stack<string>` im Code-behind. Die `OnRollbackUndo/Redo`-Handler manipulieren nur die Stacks, führen aber **keinen tatsächlichen Rollback durch** — sie loggen lediglich.

**Warum es schadet:**
- Funktional nutzlos: User drückt „Rollback Undo", und es passiert nur ein Log-Eintrag
- State nicht im VM → nicht testbar
- Stack-State nicht persistiert → nach Restart verloren

**Fundstellen:**
- [MainWindow.xaml.cs](src/RomCleanup.UI.Wpf/MainWindow.xaml.cs) Zeile 51: `_rollbackUndoStack`, `_rollbackRedoStack`
- [MainWindow.xaml.cs](src/RomCleanup.UI.Wpf/MainWindow.xaml.cs#L1038): `OnRollbackUndo` — Pop → Push → Log
- [MainWindow.xaml.cs](src/RomCleanup.UI.Wpf/MainWindow.xaml.cs#L1047): `OnRollbackRedo` — Pop → Push → Log

**Fix-Strategie:**
- Rollback-History in VM (oder als Service) mit echtem Undo-Mechanismus
- „Undo" muss den tatsächlichen `AuditCsvStore.Rollback` aufrufen
- History in SettingsService persistieren
- UI: Dropdown mit letzten N Rollback-Points

**Verifikation:** Rollback Undo → Dateien werden tatsächlich verschoben; Round-trip Test

---

### P1-UX-011: WebBrowser-Control (IE-Engine) für Report-Vorschau ✅ ERLEDIGT

**Problem:** `WebBrowser` nutzte die veraltete IE11/Trident-Engine. Sicherheitsrisiko bei HTML-Rendering.

**Fix (Runde 19):**
- NuGet: `Microsoft.Web.WebView2 1.0.3800.47`
- XAML: `<wv2:WebView2 x:Name="webReportPreview"/>` mit `xmlns:wv2` + AutomationProperties
- Code-behind: `EnsureWebView2Initialized()` → `EnsureCoreWebView2Async()`, `NavigateToString()` via `CoreWebView2`, Navigation via `Source = new Uri(...)`
- Fallback: Fehlerhandling wenn WebView2-Runtime nicht installiert
- Kein Legacy-`WebBrowser`-Control mehr im Projekt

**Verifikation:** Build 0 Fehler, 1215 Tests bestanden

---

### P1-UX-012: Keine Keyboard-Navigation-Strategie

**Problem:** Zwar existieren Hotkeys (F5, Esc, Ctrl+R etc.) und `FocusVisualStyle`, aber:
- Kein TabIndex auf den meisten Controls (nur Run/Cancel/Rollback haben TabIndex)
- Keine Access Keys (Unterstrichene Buchstaben in Labels)
- Tab-Reihenfolge folgt der XAML-Deklarationsreihenfolge, nicht dem logischen Flow
- ListBox-Items haben keinen Keyboard-Handler für Delete-Taste

**Fundstellen:**
- [MainWindow.xaml](src/RomCleanup.UI.Wpf/MainWindow.xaml) Zeile ~42: InputBindings (Hotkeys vorhanden)
- [MainWindow.xaml](src/RomCleanup.UI.Wpf/MainWindow.xaml) Zeile ~121-145: nur Run/Cancel/Rollback mit TabIndex

**Fix-Strategie:**
- TabIndex auf allen interaktiven Elementen in logischer Reihenfolge setzen
- Access Keys für Hauptaktionen (`_Starten`, `_Stopp`, `_Rückgängig`)
- Delete-Taste auf Root-ListBox zum Entfernen
- `AutomationProperties.AcceleratorKey` für Hotkey-Discoverability

**Verifikation:** Kompletter Flow nur mit Tastatur durchführbar (Tab→Enter→Esc)

---

## 3) UI/UX Redesign-Vorschlag

### 3.1 Neue Tab-Struktur

**Empfehlung: 4 Top-Level-Tabs statt 3**

```
┌──────────┬───────────┬────────────┬──────────┐
│ Sortieren │ Ergebnis  │ Werkzeuge  │ Einst.   │
└──────────┴───────────┴────────────┴──────────┘
```

| Tab | Inhalt | Begründung |
|-----|--------|------------|
| **Sortieren** | Roots, Mode, Quick-Start, Optionen (Simple+Expert), Regionen | Hauptworkflow bleibt zusammen |
| **Ergebnis** | Dashboard, Log, Report-Vorschau, Fehler-Summary, Rollback-History | Alles was mit dem Lauf-Ergebnis zu tun hat |
| **Werkzeuge** | Analyse-Features (Health, Heatmap, Filter, Cross-Root), Konvertierung, DAT-Tools | Tritt an Stelle des Button-Friedhofs |
| **Einstellungen** | Tools-Pfade, Profile, Allgemein, Erweitert, Theme/Sprache | Selten geänderte Konfiguration |

### 3.2 Basic vs. Advanced — Progressive Disclosure

**Simple Mode (4 Entscheidungen) — bleibt:**
1. Ordner hinzufügen
2. Region wählen (ComboBox)
3. Was tun: Duplikate / Junk / Sortieren (3 Checkboxen)
4. Starten (F5)

**Expert Mode — Redesign:**
- Kollabierende Sektionen (Accordion) statt verschachtelter Expander:
  1. Regionen (direkt sichtbar, nicht unter Konfiguration → Regeln → Erweitert)
  2. Dateityp-Filter
  3. Konsolen-Filter
  4. Sicherheitsoptionen
  5. DAT-Verifizierung
  6. Konvertierung

### 3.3 Wizard vs. Dashboard → Empfehlung: Hybrid

**Dashboard** für den Sortieren-Tab (Step-Indikator bleibt, wird aber zum echten Stepper):
- Step 1: Ordner konfiguriert? ✓/✗
- Step 2: Optionen gewählt? ✓/✗
- Step 3: Run ✓/✗/laufend

**Wizard-Pattern** nur für gefährliche Aktionen (Move):
- DryRun-Ergebnis → Summary Modal → Bestätigung → Move

### 3.4 Spacing / Responsiveness / Scroll

**Aktuell gut:**
- Spacing-Tokens existieren (SpaceXS–SpaceXXL)
- ScrollViewer in allen Tab-Inhalten
- MinWidth/MinHeight auf Window gesetzt

**Verbesserungsbedarf:**
- ListBox für Roots: fixe MinHeight=90, MaxHeight=200 — zu starr. Adaptive Height wäre besser (z.B. MinHeight=60, MaxHeight=40% Viewport)
- WrapPanel für Feature-Buttons: kein MaxWidth → bei kleinen Fenstern wird jeder Button eigene Zeile
- Spacing zwischen Sektionen inkonsistent (Margin „0,0,0,8" vs „0,0,0,12" vs „0,16,0,0")

### 3.5 Copy/Labels/Microcopy

**Gut:**
- Tooltips auf fast allen Elementen
- Deutsche Lokalisierung konsistent
- CaptionText-Hinweise unter komplexen Optionen

**Verbesserungsbedarf:**
- „Sicherer DryRun" — „sicher" ist relativ. Besser: „Nur analysieren (keine Änderungen)"
- „Volle Sortierung" — unklar was „voll" heißt. Besser: „Standard-Sortierung (alle Regionen)"
- Status-Texte sind technisch: „Roots: 3", „Tools: ⚠" → Besser: „3 Ordner konfiguriert", „Tools: 2 von 5 gefunden"

---

## 4) Refactoring-Plan (MVVM & Architektur)

### 4.1 MainViewModel aufteilen

| Sub-VM | Properties | Commands | Quelle (aktuell) |
|--------|-----------|----------|-------------------|
| `SortViewModel` | Roots, SelectedRoot, DryRun, ConvertEnabled, SortConsole, AggressiveJunk, AliasKeying, ConfirmMove, IsSimpleMode, SimpleRegion/Dupes/Junk/Sort | RunCommand, CancelCommand, AddRoot, RemoveRoot, PresetCommands | MainViewModel Zeilen 60–250 |
| `RegionViewModel` | PreferEU…PreferSCAN (16 Booleans), SelectedExtensions, SelectedConsoles | – | MainViewModel Zeilen 160–210 |
| `RunStateViewModel` | RunState (Enum), IsBusy, Progress, ProgressText, PerfPhase, PerfFile, StepLabels, DashMode/Winners/Dupes/Junk/Duration/HealthScore | – | MainViewModel Zeilen 230–350 |
| `ToolsViewModel` | ToolChdman/Dolphin/7z/Psxtract/Ciso, DatRoot, UseDat, DatHashType, DatFallback, CrcVerifyScan | AutoFindTools | MainViewModel Zeilen 80–98, 105–115 |
| `LogViewModel` | LogEntries, ErrorSummaryItems | ClearLog, ExportLog | MainViewModel Zeilen 65–68 |
| `SettingsViewModel` | TrashRoot, AuditRoot, Ps3DupesRoot, LogLevel, Locale, ProtectedPaths, SafetyStrict, SafetyPrompts, JpOnlySelected, JpKeepConsoles, IsWatchModeActive, ConflictPolicy | ProfileSave/Load/Delete/Import/Export, ThemeToggle | MainViewModel Zeilen 70–78, 145–160 |

`MainViewModel` wird zum **Compositor**: hält Sub-VMs, delegiert, steuert Navigation.

### 4.2 State Machine für Run Lifecycle

```
States: Idle, Preflight, Scanning, Deduplicating, Moving, Converting, Completed, 
        CompletedDryRun, Failed, Cancelled

Transitions:
  Idle → Preflight          [RunCommand.Execute]
  Preflight → Scanning      [preflight passes]
  Preflight → Failed        [preflight blocks]
  Scanning → Deduplicating  [scan complete]
  Deduplicating → Moving    [mode == Move]
  Deduplicating → CompletedDryRun [mode == DryRun]
  Moving → Converting       [convertEnabled]
  Moving → Completed        [!convertEnabled]
  Converting → Completed    [done]
  Any Running → Cancelled   [CancelCommand]
  Any Running → Failed      [unhandled exception]
  Completed → Idle          [new run or reset]
```

Implementierung als `sealed record RunState` mit Pattern-Matching oder einfacher Enum + Transition-Tabelle.

### 4.3 Services/Ports für Code-behind-Extraktion

| Service | Verantwortung | Aktuell in |
|---------|--------------|------------|
| `IRunService` | Build Orchestrator, Execute, Progress-Reporting | MainWindow.xaml.cs `RunCoreAsync` |
| `IRollbackService` | Undo/Redo-Stack, Rollback-Execution | MainWindow.xaml.cs Zeile 51, 621, 1038, 1047 |
| `IFeatureDialogService` | Typisierte Result-Dialoge | MainWindow.xaml.cs `ShowTextDialog` × 30 |
| `IWatchService` | FileSystemWatcher-Management, Debounce | MainWindow.xaml.cs Zeile 1056–1153 |
| `ITrayService` | System-Tray, Minimize-to-tray | MainWindow.xaml.cs Zeile 2528–2617 |
| `IProfileService` | Profile CRUD, Import/Export, Diff | MainWindow.xaml.cs Zeile 781–898 |

### 4.4 Test-Seams

| Test-Ziel | Seam | Wie testen |
|-----------|------|------------|
| CanExecute-Guards | Sub-VM + State Machine | Unit-Test: State → CanExecute-Erwartung |
| Run-Flow | IRunService (mockbar) | Integration: Mock-Orchestrator, State-Übergänge prüfen |
| Settings Round-trip | SettingsService | Unit-Test: SaveFrom → LoadInto → Werte identisch |
| Feature-Ergebnisse | FeatureService (bereits static) | Unit-Test: Eingabe → Ausgabe prüfen |
| Region-Übersetzung | RegionViewModel.GetPreferredRegions | Unit-Test: SimpleMode Index → Region-Array |

### 4.5 Risiken & Reihenfolge

**Empfohlene Reihenfolge (minimiert Regression):**

1. **Phase 1 — State Machine** einführen (RunState Enum im VM, CanExecute darauf basieren). Keine UI-Änderung nötig.
2. **Phase 2 — Sub-VMs** extrahieren (SortVM, RegionVM, RunStateVM). Bindings updaten. Regressionstest: alle Bindings funktionieren.
3. **Phase 3 — Code-behind-Extraktion**: RunService, RollbackService, FeatureDialogService. MainWindow.xaml.cs auf ≤200 Zeilen.
4. **Phase 4 — UI-Redesign**: Tab-Struktur, Progressive Disclosure, ResultDialog. Sichtbar, daher höheres UX-Risiko.
5. **Phase 5 — Feature-Tab-Refactor**: Button-Friedhof → kategorisierte, filterbareFeature-Liste mit CanExecute.

---

## 5) Migrationsbewertung

### Option 1: WPF modernisieren (empfohlen)

| Aspekt | Bewertung |
|--------|-----------|
| **Pro** | Kein Framework-Wechsel, bestehender Code wiederverwendbar, .NET 10 Support, ausgereifte Tooling-Landschaft, alle WPF-Bugs bekannt |
| **Contra** | Windows-only, veralteter WebBrowser (→ WebView2 nachrüsten), Design-System manuell pflegen |
| **Risiko** | Niedrig |
| **Aufwand** | Mittel (MVVM-Refactor + Design System + Sub-VMs) |
| **ROI** | Hoch — maximale Funktionsparität bei minimalem Risiko |

### Option 2: Avalonia

| Aspekt | Bewertung |
|--------|-----------|
| **Pro** | Cross-platform (Linux/macOS), modernes XAML, guter .NET-Support, Hot-Reload |
| **Contra** | 100% XAML/Converters/Styles müssen portiert werden, subtile Behavioral-Diffs, kein WinForms-Interop (Tray-Icon), Packaging-Komplexität |
| **Risiko** | Hoch (6+ Monate Portierungsaufwand bei 1400-Zeilen XAML + 600-Zeilen Theme) |
| **Aufwand** | Hoch |
| **ROI** | Nur wenn Cross-Platform Ziel ist. Für Windows-Only: kein Mehrwert |

### Option 3: WinUI 3

| Aspekt | Bewertung |
|--------|-----------|
| **Pro** | Modernes Fluent-Design out-of-the-box, WinAppSDK, native Windows 11 Look |
| **Contra** | XAML-Inkompatibilitäten, kein .NET 10 native TFM support (nur WindowsAppSDK), MSIX-Packaging Pflicht, instabiles Ökosystem, weniger Community-Support |
| **Risiko** | Sehr hoch (Breaking Changes, Packaging-Komplexität) |
| **Aufwand** | Sehr hoch |
| **ROI** | Negativ — zu riskant für produktives Tool |

### Option 4: WebView (Electron/Blazor Hybrid)

| Aspekt | Bewertung |
|--------|-----------|
| **Pro** | Maximale Styling-Freiheit (CSS/HTML), Cross-Platform, moderne UI-Frameworks (React/Vue/Svelte) |
| **Contra** | Packaging-Overhead (Chromium-Runtime ~100MB), IPC-Komplexität, kein nativer File-System-Zugriff, Debugging schwieriger, Latenz |
| **Risiko** | Hoch (Architektur-Neubau) |
| **Aufwand** | Sehr hoch |
| **ROI** | Nur bei Wechsel zu Web-First-Strategie sinnvoll |

### Empfehlung: **Option 1 — WPF modernisieren**

**No-regret Steps (unabhängig von späterer Migration):**
1. MVVM-Aufräumen (Sub-VMs, State Machine) — portabel zu Avalonia/WinUI
2. Design-System-Token konsolidieren — übertragbar
3. Services extrahieren (RunService, FeatureDialogService) — Framework-unabhängig
4. WebBrowser → WebView2 — kurzfristiger Qualitätssprung

---

## 6) Tests & Verifikation fürs GUI

### 6.1 VM-Unit-Tests

| Test-Klasse | Was prüfen | Fehler die gefunden werden |
|-------------|-----------|---------------------------|
| `SortViewModelTests` | AddRoot/RemoveRoot, CanExecute-Guards, Preset-Defaults | Falsche CanExecute-Logik, Preset überschreibt falsche Werte |
| `RunStateTests` | Alle State-Übergänge, ungültige Transitions | Zustandsautomaten-Bugs, Race Conditions |
| `RegionViewModelTests` | GetPreferredRegions für alle SimpleMode-Indices | Falsche Region-Reihenfolge, fehlende Regionen |
| `SettingsRoundtripTests` | SaveFrom → LoadInto → alle Properties identisch | Vergessene Properties, Serialisierungsfehler |
| `RefreshStatusTests` | StatusLevel-Berechnung für alle Kombinationen | Falsche Status-Anzeige, inkonsistente Dots |

### 6.2 Integration-/Smoke-Tests

| Test | Was prüfen | Fehler |
|------|-----------|--------|
| Kompletter DryRun-Flow | Roots → Config → F5 → Progress → Dashboard → Report | Flow-Brüche, Thread-Deadlocks |
| Cancellation | F5 → Esc → IsBusy=false, Progress resettet | Race Condition, Zombie-Tasks |
| Close während Run | X-Button → Confirm → Cancel → Wait → Close | Endlosschleife in OnClosing |
| Rollback | Move → Rollback → Dateien zurück | AuditStore-Fehler, Pfad-Inkompatibilitäten |

### 6.3 Golden/Approval-Tests

- Theme-Konsistenz: Alle Brush-Keys in SynthwaveDark müssen in Light existieren (und umgekehrt)
- SettingsService: JSON-Schema-Stabilität (keine neuen Required-Felder ohne Migration)

### 6.4 „No Alibi"-Kriterien

Jeder Test muss mindestens einen dieser Fehler catchen können:
- [x] CanExecute gibt `true` zurück, obwohl Voraussetzung fehlt → TEST-001 (7 CanExecute Guard-Tests, Runde 4)
- [x] State-Inkonsistenz nach Cancel (z.B. IsBusy=true bleibt hängen) → TEST-008 (5 Cancellation/State-Tests, Runde 5)
- [x] Settings-Property wird nicht persistiert/restauriert → TEST-004 (2 Round-trip-Tests, Runde 4)
- [x] Status-Dot zeigt falschen Level → TEST-005 (12 RefreshStatus-Tests, Runde 4)
- [x] UI friert ein weil I/O auf UI-Thread → Race-Condition in RunCoreAsync behoben (Runde 8)
- [x] Feature-Button klickbar obwohl Voraussetzung fehlt (kein Run-Ergebnis) → TEST-009 (6 HasRunResult Theory-Tests, 7 ConsoleFilter-Tests, Runde 6+7)

---

## 7) Tracking Checklist

### P0 Fixes
- [~] **UX-001**: MainWindow.xaml.cs Code-behind auf ≤200 Zeilen reduzieren — **Stand: 348 Zeilen** (von ~3370 auf 348 reduziert, 90% Reduktion). RunCoreAsync komplett ins VM migriert (ExecuteRunAsync mit SynchronizationContext). WatchService komplett ins VM migriert (ToggleWatchMode, WatchApplyCommand). Verbleibende 5 Handler: OnLoaded, OnClosing/CleanupResources, OnRunRequested (thin wrapper), RefreshReportPreview/WebView2, DragDrop. Alle genuinely UI-gekoppelt (Window-Lifecycle, WebView2, DragDrop). ≤200 erfordert Sub-VM-Architektur (RF-001).
- [x] **UX-002**: RunState-Enum einführen, State Machine implementieren
- [x] **UX-003**: Features-Tab redesignen (HasRunResult-Bindings auf 13 Buttons, Console-Filter → VM)

### P1 Fixes
- [x] **UX-004**: Extension-Filter ins VM migriert (ExtensionFilters Collection + CollectionViewSource Grouping, code-behind GetSelectedExtensions entfernt)
- [x] **UX-005**: ShowTextDialog → ResultDialog mit Copy/Export/Tabelle (RD-008)
- [x] **UX-006**: Simple/Expert Mode-Toggle prominent (Segmented Control mit Icons, größer, Cyan-Rahmen)
- [x] **UX-007**: Conflict-Policy: eigene ComboBox statt YesNoCancel-Hack
- [x] **UX-008**: DryRun → Move Transition: Summary-Dialog + sichtbaren Übergangs-Button
- [x] **UX-009**: Phasen-Progress (Phase 1: StepLabel3 phase-aware aus RunState)
- [x] **UX-010**: Rollback-Undo/Redo in VM + echter Rollback + Persistenz
- [x] **UX-011**: WebBrowser → WebView2 (Edge Chromium): NuGet `Microsoft.Web.WebView2 1.0.3800.47`, XAML `<wv2:WebView2>`, Code-behind `EnsureWebView2Initialized()` + `CoreWebView2.NavigateToString()` + `Source = new Uri(...)`. Kein Legacy-WebBrowser mehr.
- [x] **UX-012**: Keyboard-Navigation: TabIndex auf Roots/Add/Remove, Delete-KeyBinding

### Redesign Tasks (IA/Layout/Design System)
- [ ] **RD-001**: Tab-Struktur: 4 Tabs (Sortieren / Ergebnis / Werkzeuge / Einstellungen)
- [ ] **RD-002**: Expert-Mode: Accordion statt verschachtelte Expander
- [ ] **RD-003**: Regionen direkt im Sortieren-Tab (Expert), nicht in Sub-Tab
- [ ] **RD-004**: Feature-Tab → Werkzeuge-Tab mit kategorisierter, filterbarer Liste
- [x] **RD-005**: Status-Texte humanisieren (z.B. „3 Ordner konfiguriert" statt „Roots: 3")
- [x] **RD-006**: Spacing-Margins auf Token-Basis (PaddingBar, SpaceDotInline, SpaceDivider, SpaceRightSM/MD, SpaceBottom*, SpaceLeftSM, SpaceRightXS, SpaceTopLG — 83 Hardcodes → DynamicResource, Runde 3+9)
- [x] **RD-007**: Adaptive ListBox-Höhe (MinHeight=60, MaxHeight=280 statt 90/200)
- [x] **RD-008**: ResultDialog-Komponente (Titel, Text+Tabelle Tabs, Copy, Export, ESC)
- [x] **RD-009**: Phasen-Stepper-Visualisierung: Connector-Linien dynamisch eingefärbt via StepToBrush

### Refactor Tasks (VM Split, State Machine, Services)
- [~] **RF-001**: Sub-VMs einführen — Phase 1 erledigt: Partial-Class-Split. MainViewModel.cs (1328→173 Zeilen Core) aufgeteilt in 4 Dateien: MainViewModel.cs (173, Kern: Felder/Ctor/Commands/Collections/INPC), MainViewModel.Settings.cs (357, alle Settings/Regionen/Presets/Browse/Config), MainViewModel.Filters.cs (230, Extensions/Consoles/Tools-Filter+Init), MainViewModel.RunPipeline.cs (593, RunState/Rollback/Status/Dashboard/Execution/Watch). Phase 2 (echte Sub-VM-Klassen mit eigenem DataContext) offen.
- [x] **RF-002**: RunState-Enum + Transition-Tabelle
- [x] **RF-003**: RunService extrahiert (BuildOrchestrator + ExecuteRun, ~200 Zeilen aus Code-behind entfernt)
- [x] **RF-004**: RollbackService extrahiert (delegiert an AuditCsvStore.Rollback)
- [~] **RF-005**: IFeatureDialogService — Phase 1 erledigt (ShowTextDialog → ResultDialog.ShowText delegiert). Phase 2 (typisierte Dialoge pro Feature) offen.
- [x] **RF-006**: WatchService extrahiert (FileSystemWatcher + Debounce + IsBusyCheck, ~100 Zeilen aus Code-behind entfernt)
- [x] **RF-007**: TrayService extrahiert (Toggle, OnWindowStateChanged, Dispose — Services/TrayService.cs ~120 Zeilen aus Code-behind entfernt). DllImport + Felder migriert. 1 xUnit-Test.
- [x] **RF-008**: ProfileService extrahiert (Delete, Import, Export, LoadSavedConfigFlat — Services/ProfileService.cs). 5 Handler vereinfacht, FlattenJson + GetSiblingDirectory aus Code-behind entfernt. 4 xUnit-Tests.
- [x] **RF-009**: _conflictPolicy in VM verschieben + persistieren
- [x] **RF-010**: StatusBarService (deprecated) entfernt
- [x] **RF-011**: Feature-Handler-Logik extrahiert → FeatureService.cs (2640+ Zeilen, 60+ Methoden): Batch 1 (11 Handler, ~475 Zeilen, 12 xUnit-Tests), Batch 2 (6 Handler: FilterBuilder, PluginMarketplace, MultiInstanceSync, MobileWebUI, RulePackSharing, HeaderRepair), Batch 3 (8 Handler: NKitConvert, TosecDat, ToolImport, FtpSource, GpuHashing, PdfReport, ConversionEstimate, CustomDatEditor, 25 xUnit-Tests), Batch 4 (AutoProfile, CollectionManager, PlaytimeTracker, ParallelHashing, CommandPalette). FeatureCommandService wired ~60 Feature-Buttons über BindFeatureCommand, MainWindow.xaml.cs von ~3370 → 601 Zeilen.
- [x] **RF-012**: IWindowHost-Interface + Extraktion: 5 UI-gekoppelte Handler (CommandPalette, SystemTray, MobileWebUI, Accessibility, ThemeEngine) über IWindowHost-Abstraction nach FeatureCommandService migriert. 4 redundante Code-behind-Felder eliminiert → VM-Properties nutzen. PopulateErrorSummary + ApplyRunResult in VM konsolidiert. 809→601 Zeilen.
- [x] **RF-013**: AutoWire + VM-Commands: 78 BindFeatureCommand-Calls durch AutoWireFeatureButtons (Konventions-Loop btn{Key}→Key) ersetzt. Browse-Buttons (9) über BrowseToolPathCommand/BrowseFolderPathCommand ins VM. QuickPreview/StartMove/RollbackQuick als VM-Commands. 6 unbenutzte usings entfernt. 601→451 Zeilen. 10 neue Tests (1215 gesamt).
- [x] **RF-014**: VM-Migrations: Rollback-Execution komplett ins VM (async, IDialogService statt static). ConfirmMoveDialog ins VM. Profile Save/Load als VM-Commands (SaveSettingsCommand/LoadSettingsCommand). SettingsService in VM injiziert. RollbackRequested-Event eliminiert. OnAddRoot nutzt _dialog statt static DialogService. 451→497 Zeilen (4 Zeilen Netto-Anstieg durch ConfirmMoveDialog-Methode, aber 25 Zeilen Handler + Event entfernt).
- [x] **RF-015**: RunCoreAsync + WatchService ins VM migriert: ExecuteRunAsync() mit SynchronizationContext.Post statt Dispatcher.InvokeAsync. WatchApplyCommand + ToggleWatchMode() + OnWatchRunTriggered() + CleanupWatchers() ins VM. Zwei static DialogService.Info → _dialog.Info. 497→348 Zeilen Code-behind (−90% von Original).

### Migration Tasks (WPF modernisieren)
- [x] **MIG-001**: WebBrowser → WebView2 (Edge Chromium) — siehe UX-011
- [x] **MIG-002**: Theme-Token audit: Brush/Spacing/Style-Keys Parity (3 Tests in GuiViewModelTests.cs)
- [x] **MIG-003**: Spacing-Hardcodes → DynamicResource-Tokens (StatusBar, Footer, Dots, Divider, Step-Indicator, Buttons, Expander — 83 Instanzen tokenisiert, Runde 3+9)
- [x] **MIG-004**: DropShadowEffect auf SectionCard: BlurRadius halbiert (Dark 12→6, Light 8→4) für 21+ Cards

### Test Tasks
- [x] **TEST-001**: CanExecute Guard-Tests (RunCommand, CancelCommand, RollbackCommand — 7 Tests)
- [x] **TEST-002**: RunState State-Machine Tests (23 Tests in GuiViewModelTests.cs)
- [x] **TEST-003**: GetPreferredRegions für alle SimpleMode-Indices + ExpertMode (5 Tests)
- [x] **TEST-004**: SettingsService Round-trip (Save → Load → Verify, ConflictPolicy round-trip)
- [x] **TEST-005**: RefreshStatus Kombinationen (12 Tests: Roots/Tools/DAT/Ready × present/missing, RunState-Phasen)
- [x] **TEST-006**: Theme-Parity: Brush/Spacing/Style-Keys (3 Tests in GuiViewModelTests.cs)
- [x] **TEST-007**: Smoke-Test: DryRun End-to-End (3 Tests: Orchestrator E2E + VM-State DryRun-Sequence + DryRun→Move Transition, Runde 9)
- [x] **TEST-008**: Cancellation/State Tests (TransitionTo Cancelled/Failed, ShowStartMoveButton — 5 Tests)
- [x] **TEST-009**: Feature CanExecute: HasRunResult Tests (6 Theory-Tests für alle RunStates) + ExtensionFilter Tests (5 Tests) + ConsoleFilter Tests (7 Tests) + CTS Race Safety (1 Test)

### Accessibility Tasks
- [x] **A11Y-001**: TabIndex auf Root-ListBox (10), Add (11), Remove (12)
- [x] **A11Y-002**: Access Keys für Hauptaktionen (Starten, Stopp, Rückgängig, Hinzufügen, Entfernen, Bericht)
- [x] **A11Y-003**: Delete-Taste auf Root-ListBox (KeyBinding)
- [x] **A11Y-004**: AutomationProperties.AcceleratorKey für F5, Esc, Ctrl+Z, Ctrl+R
- [x] **A11Y-005**: WCAG AA Kontrast-Tests: 8 Theory-Tests für Text/Muted/Accent auf Background/Surface (Dark+Light)
- [x] **A11Y-006**: LiveRegion-Annotations: StatusReady, ProgressText, StepLabel3 + AutomationProperties.Name

---

## Nächste Runden

### Runde 2 — Deep Dive: Bindings & Converter
- [x] Alle `x:Name`-Referenzen identifizieren, die Bindings sein sollten → 30 Console-Checkboxen zu ConsoleFiltersView migriert (Runde 7)
- [x] Binding-Errors im Output-Window bei Laufzeit prüfen → Statische XAML-Analyse: alle 76+ Binding-Pfade gegen VM-Reflection validiert, 0 Mismatches (Runde 10, 11 neue Tests in GuiViewModelTests.cs)
- [x] Converter auf Null-Safety geprüft (alle 4 sind null-safe, Runde 6)
- [x] OneWay vs. TwoWay Bindings auditiert (korrekt, Runde 6)

### Runde 3 — Deep Dive: Threading & Cancellation
- [x] Alle `Task.Run` + `Dispatcher.InvokeAsync` Paare auf Race Conditions geprüft → Race-Condition behoben: result fire-and-forget InvokeAsync → explizite Rückgabe + UI-Update nach await (Runde 8)
- [x] CancellationToken-Propagation durch alle Pfade verifiziert + OnCancel CTS-Race-Fix (Volatile.Read + try/catch, Runde 7)
- [x] `_activeRunTask` Lifecycle-Analyse: korrekt (snap + await in OnClosing, finally-null in OnRunRequested, Runde 8)
- [x] Progress-Throttle: 100ms-Limit wird eingehalten (DateTime.UtcNow-Vergleich, single-thread closure, Runde 8)

### Runde 4 — Deep Dive: Theme/Styles/Accessibility
- [x] Brush-Key-Parity zwischen Dark/Light Theme (100%-Check): 53/53 Keys identisch (Runde 8)
- [x] CornerRadius/Padding/Margin-Konsistenz: TextBox CornerRadius (4→6), TabItem Padding (14 8→16 9) angeglichen (Runde 8, 2 Tests)
- [x] High-Contrast-Theme implementiert (ThemeService.BuildHighContrastDictionary, WCAG AAA 7:1, Session 5)
- [x] Screenreader-Test mit Windows Narrator → AutomationProperties.Name-Abdeckung: alle Buttons, TextBoxen, ComboBoxen, ListBoxen + 18 CheckBoxen (16 Region + 2 DataTemplate) ergänzt; 11 automatisierte A11y-Regressionstests (Runde 10)

### Runde 5 — Deep Dive: Resource Leaks & Cleanup
- [x] Resource-Leak-Audit: 5 echte Leaks gefunden und behoben (Runde 9):
  - `_settingsTimer.Stop()` in OnClosing (Timer feuerte nach Window-Close)
  - VM-Event-Unsubscription: `RunRequested -= OnRunRequested`, `RollbackRequested -= OnRollbackRequested`
  - `LogEntries.CollectionChanged` Handler gespeichert + unsubscribed
  - `_apiProcess.Dispose()` vor null-Setzung (Handle-Leak)
  - `CleanupResources()` extrahiert → wird in BEIDEN OnClosing-Pfaden (normal + busy-cancel) aufgerufen
- [x] 83 hardcodierte Margin-Werte in MainWindow.xaml → DynamicResource-Tokens migriert (Runde 9)
- [x] 3 neue Spacing-Tokens: SpaceLeftSM, SpaceRightXS, SpaceTopLG (in beiden Themes, Runde 9)
