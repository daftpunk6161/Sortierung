# Feature Implementation Guide — RomCleanup v2.0

> **Dokument 3/3 — Developer Handbook**  
> Übergeordnetes Strategie-Dokument: → [01 — Produktstrategie & Roadmap](01-PRODUCT-STRATEGY-ROADMAP.md)  
> Product Requirements: → [02 — Product Requirements (PRD)](02-PRODUCT-REQUIREMENTS.md)

**Stand:** 2026-03-09  
**Quelle:** PRD v1.0 + PRODUCT_STRATEGY_ROADMAP.md  
**Zweck:** Konkrete Implementierungsanleitung pro Feature — Dateien, Funktionen, Dependencies, Tests, Reihenfolge.

---

## Legende

| Symbol | Bedeutung |
| ------ | --------- |
| 📁 | Neue Datei anlegen |
| ✏️ | Bestehende Datei anpassen |
| 🧪 | Testdatei |
| ⚠️ | Risiko / Achtung |
| 🔗 | Abhängigkeit zu anderem Feature |

**Aufwand:** S = 1–2 Tage, M = 3–5 Tage, L = 1–2 Wochen, XL = 2+ Wochen  
**Priorität:** P0 = Must-Have, P1 = Should-Have, P2 = Nice-to-Have

---

## Phase 1 — Quick Wins (S)

---

### QW-01: Datei-Rename nach DAT-Standard

**ID:** QW-01 | **Aufwand:** S | **Priorität:** P0 | **PRD-Ref:** US-003, FR-001

**Beschreibung:** ROM-Dateien nach No-Intro/Redump-Nomenklatur umbenennen, basierend auf Hash-Match im DAT-Index.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/Dat.ps1` | ✏️ | Neue Funktion `Rename-RomToDatName` |
| `dev/modules/ApplicationServices.ps1` | ✏️ | Neuer UseCase `Invoke-RunRenameService` |
| `dev/modules/OperationAdapters.ps1` | ✏️ | Rename-Adapter für CLI/GUI |
| `dev/modules/WpfSlice.DatMapping.ps1` | ✏️ | Rename-Button + Preview in DAT-Grid |
| `dev/tests/unit/Dat.Rename.Tests.ps1` | 📁 | Tests für Rename-Logik |

**Funktions-Signatur:**
```powershell
function Rename-RomToDatName {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][hashtable]$DatIndex,
        [ValidateSet('DryRun','Move')][string]$Mode = 'DryRun',
        [scriptblock]$Log
    )
    # Returns: @{ OldName; NewName; Status; Hash }
}
```

**Akzeptanzkriterien:**
- ROM mit SHA1-Match → Preview zeigt alt→neu
- Kein Match → Skip mit Warnung
- Zielname existiert → Konflikt-Handling (Skip + Log)
- Pfad > 260 Zeichen → Warnung, kein Rename
- Audit-CSV Eintrag mit Action=Rename
- DryRun verändert keine Datei

**Tests (Failure-First):**
- Rename bei Match → neuer Name korrekt
- Kein Match → Status = "NoMatch"
- Dateiname-Konflikt → Status = "Conflict"
- Ungültige Zeichen im DAT-Namen → Sanitize
- Audit-Eintrag vorhanden

**Abhängigkeiten:** Dat.ps1 (Hash-Lookup), FileOps.ps1 (Move), AuditStore Port

---

### QW-02: ECM-Dekompression

**ID:** QW-02 | **Aufwand:** S | **Priorität:** P0 | **PRD-Ref:** FR-002

**Beschreibung:** `.ecm`-Dateien automatisch zu `.bin` entpacken via `ecm2bin`-Tool.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/Convert.ps1` | ✏️ | Neuer Eintrag in Strategy-Map: `ecm → bin` |
| `dev/modules/Tools.ps1` | ✏️ | `ecm2bin`-Wrapper in Tool-Registry |
| `data/tool-hashes.json` | ✏️ | SHA256-Hash für `ecm2bin.exe` |
| `dev/tests/unit/Convert.Ecm.Tests.ps1` | 📁 | ECM-Konvertierung Tests |

**Implementierung:**
1. `tool-hashes.json` um `ecm2bin` erweitern
2. `Tools.ps1`: `Find-Ecm2Bin` + `Invoke-Ecm2Bin` analog zu bestehenden Tool-Wrappern
3. `Convert.ps1`: Strategy-Map um `@{ Source = '.ecm'; Target = '.bin'; Tool = 'ecm2bin' }` erweitern
4. Hash-Verifikation via bestehenden `Test-ToolHash` Mechanismus

**Tests:**
- ecm2bin gefunden → Konvertierung Mock erfolgreich
- ecm2bin nicht gefunden → klare Fehlermeldung
- Exit-Code ≠ 0 → Fehler geloggt, Quelldatei unverändert
- Hash-Mismatch für ecm2bin → Tool blockiert

**Abhängigkeiten:** Tools.ps1 (Tool-Framework), Convert.ps1 (Strategy-Map)

---

### QW-03: Archiv-Repack

**ID:** QW-03 | **Aufwand:** S | **Priorität:** P0 | **PRD-Ref:** FR-003

**Beschreibung:** ZIP↔7z Repack mit konfigurierbarer Kompression (RAR→ZIP, 7z→ZIP, ZIP→7z).

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/Convert.ps1` | ✏️ | Repack-Funktionen: `Invoke-ArchiveRepack` |
| `dev/modules/Tools.ps1` | ✏️ | `Invoke-7zRepack` Wrapper |
| `dev/modules/WpfSlice.RunControl.ps1` | ✏️ | Repack-Option in Konvertierungs-UI |
| `dev/tests/unit/Convert.Repack.Tests.ps1` | 📁 | Repack-Tests |

**Funktions-Signatur:**
```powershell
function Invoke-ArchiveRepack {
    param(
        [Parameter(Mandatory)][string]$ArchivePath,
        [ValidateSet('zip','7z')][string]$TargetFormat,
        [ValidateRange(1,9)][int]$CompressionLevel = 5,
        [ValidateSet('DryRun','Move')][string]$Mode = 'DryRun',
        [scriptblock]$Log
    )
    # Returns: @{ SourcePath; TargetPath; SourceSize; TargetSize; Status }
}
```

**Ablauf:** Entpacken in Temp → Neu packen → Verify (Dateiliste gleich) → Original in Trash → Audit-Log

**Tests:**
- ZIP→7z → korrekte Kompression
- 7z→ZIP → korrekte Kompression
- RAR→ZIP → Entpacken+Neupacken
- Beschädigtes Archiv → Fehler, kein Repack
- DryRun → keine Änderung

**Abhängigkeiten:** Tools.ps1 (7z), FileOps.ps1 (Temp/Trash)

---

### QW-04: Speicherplatz-Prognose

**ID:** QW-04 | **Aufwand:** S | **Priorität:** P1 | **PRD-Ref:** US-008, FR-004

**Beschreibung:** DryRun-Report zeigt geschätzte Speicherersparnis bei Konvertierung.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/Convert.ps1` | ✏️ | Neue Funktion `Get-ConversionSavingsEstimate` |
| `dev/modules/Report.ps1` | ✏️ | Speicherprognose-Sektion im HTML/JSON-Report |
| `dev/modules/WpfSlice.ReportPreview.ps1` | ✏️ | Anzeige im Dashboard |
| `dev/tests/unit/Convert.Estimate.Tests.ps1` | 📁 | Tests |

**Funktions-Signatur:**
```powershell
function Get-ConversionSavingsEstimate {
    param(
        [Parameter(Mandatory)][object[]]$Files,
        [Parameter(Mandatory)][string]$TargetFormat
    )
    # Returns: @{ TotalSourceSizeBytes; EstimatedTargetSizeBytes; EstimatedSavingsBytes; Ratio }
}
```

**Kompressions-Ratios (Lookup-Tabelle):**
- BIN/CUE → CHD: ~40–60% Reduktion
- ISO → CHD: ~30–50% Reduktion
- ISO → RVZ: ~50–70% Reduktion
- ZIP → 7z: ~5–15% Reduktion

**Tests:**
- 10 BIN/CUE-Dateien → korrekte Schätzung
- 0 Dateien → leeres Ergebnis
- Unbekanntes Format → Skip mit Warnung

**Abhängigkeiten:** FormatScoring.ps1 (Format-Erkennung)

---

### QW-05: Detaillierter Junk-Report

**ID:** QW-05 | **Aufwand:** S | **Priorität:** P0 | **PRD-Ref:** FR-005

**Beschreibung:** Pro Datei den Grund der Junk-Klassifikation anzeigen (welche Regel, welcher Tag).

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/Classification.ps1` | ✏️ | `JunkReason`-Property im Klassifikations-Ergebnis |
| `dev/modules/Report.ps1` | ✏️ | Junk-Reason-Spalte in HTML/CSV-Report |
| `dev/modules/ReportBuilder.ps1` | ✏️ | Junk-Reason-Rendering |
| `dev/tests/unit/Classification.JunkReason.Tests.ps1` | 📁 | Tests |

**Implementierung:**
1. `Classification.ps1`: Wenn eine Datei als Junk klassifiziert wird, das Match-Pattern und die Regel-ID (aus `rules.json`) im Ergebnisobjekt speichern
2. Ergebnis-Shape: `@{ Category = 'JUNK'; JunkReason = 'Tag: (Beta)'; JunkRule = 'JUNK-003' }`
3. Report: Neue Spalte "Junk-Grund" im HTML/CSV

**Tests:**
- Beta-Tag → Reason = "Tag: (Beta)", Rule = Pattern aus rules.json
- Demo-Tag → korrekte Zuordnung
- Mehrere Tags → alle Gründe aufgelistet
- Nicht-Junk → JunkReason = $null

**Abhängigkeiten:** rules.json (Junk-Patterns)

---

### QW-06: Keyboard-Shortcuts

**ID:** QW-06 | **Aufwand:** S | **Priorität:** P1 | **PRD-Ref:** US-007, FR-006

**Beschreibung:** Globale Tastenkürzel: Ctrl+R=Run, Ctrl+Z=Undo, F5=Refresh, Ctrl+Shift+D=DryRun, Escape=Cancel.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/wpf/MainWindow.xaml` | ✏️ | `<Window.InputBindings>` Block |
| `dev/modules/WpfEventHandlers.ps1` | ✏️ | Command-Handler für Shortcuts |
| `dev/modules/WpfSlice.RunControl.ps1` | ✏️ | Shortcut-Anbindung an Run/Cancel |
| `dev/tests/unit/WpfShortcuts.Tests.ps1` | 📁 | Shortcut-Binding-Tests (Mock) |

**XAML-Ergänzung:**
```xml
<Window.InputBindings>
    <KeyBinding Key="R" Modifiers="Ctrl" Command="{Binding RunCommand}" />
    <KeyBinding Key="Z" Modifiers="Ctrl" Command="{Binding UndoCommand}" />
    <KeyBinding Key="F5" Command="{Binding RefreshCommand}" />
    <KeyBinding Key="D" Modifiers="Ctrl+Shift" Command="{Binding DryRunCommand}" />
    <KeyBinding Key="Escape" Command="{Binding CancelCommand}" />
    <KeyBinding Key="F1" Command="{Binding ShowShortcutsCommand}" />
</Window.InputBindings>
```

**Tests:**
- InputBinding für Ctrl+R existiert
- Modal-Dialog offen → Shortcuts deaktiviert (außer Escape)
- Shortcut-Overlay (F1/?) zeigt alle Bindings

**Abhängigkeiten:** WpfMainViewModel.ps1 (Commands), WpfEventHandlers.ps1

---

### QW-07: Dark/Light-Theme-Toggle

**ID:** QW-07 | **Aufwand:** S | **Priorität:** P1 | **PRD-Ref:** US-006, FR-007

**Beschreibung:** Umschaltbarer Theme-Modus (Dark/Light/Auto) mit System-Auto-Detect.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/wpf/Themes/DarkTheme.xaml` | 📁 | Dark-ResourceDictionary |
| `dev/modules/wpf/Themes/LightTheme.xaml` | 📁 | Light-ResourceDictionary |
| `dev/modules/WpfApp.ps1` | ✏️ | Theme-Loading + System-Detect |
| `dev/modules/WpfSlice.Settings.ps1` | ✏️ | Theme-Toggle-UI |
| `dev/modules/Settings.ps1` | ✏️ | `theme`-Feld persistieren (dark/light/auto) |
| `data/defaults.json` | ✏️ | Default: `"theme": "dark"` (bereits vorhanden) |
| `dev/tests/unit/Theme.Tests.ps1` | 📁 | Theme-Wechsel-Tests |

**Implementierung:**
1. ResourceDictionaries für Dark + Light erstellen (Farbpalette aus PRD Sektion 9.3)
2. `WpfApp.ps1`: `Set-WpfTheme` Funktion — `Application.Current.Resources.MergedDictionaries` swappen
3. Auto-Detect: `SystemParameters.HighContrast` + Registry-Key `AppsUseLightTheme`
4. Settings-Persistierung: `theme` Feld in settings.json

**Tests:**
- Dark→Light → ResourceDictionary gewechselt
- Auto + System=Dark → Dark angewendet
- Neustart → Theme persistiert
- High-Contrast → Fallback

**Abhängigkeiten:** Settings.ps1, WpfApp.ps1

---

### QW-08: ROM-Suche/Filter in Ergebnisliste

**ID:** QW-08 | **Aufwand:** S | **Priorität:** P1 | **PRD-Ref:** FR-008

**Beschreibung:** Live-Filter-Textbox über klassifizierte ROMs im Report-Tab.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/WpfSlice.ReportPreview.ps1` | ✏️ | Filter-TextBox + CollectionViewSource |
| `dev/modules/wpf/MainWindow.xaml` | ✏️ | TextBox im Report-Bereich |
| `dev/tests/unit/WpfFilter.Tests.ps1` | 📁 | Filter-Logik-Tests |

**Implementierung:**
1. XAML: `<TextBox x:Name="txtFilter" />` oberhalb der Report-Liste
2. Code-Behind: `CollectionViewSource.Filter` mit TextChanged-Event (300ms Debounce)
3. Filter auf: Dateiname, Konsole, Region, Kategorie

**Tests:**
- Suchtext "Mario" → nur Mario-Einträge sichtbar
- Leerer Suchtext → alle sichtbar
- Groß/Kleinschreibung ignoriert
- 10.000 Einträge → Filter < 200ms

**Abhängigkeiten:** WpfSlice.ReportPreview.ps1

---

### QW-09: Duplikat-Heatmap

**ID:** QW-09 | **Aufwand:** S | **Priorität:** P1 | **PRD-Ref:** FR-009

**Beschreibung:** Horizontales Balkendiagramm der Duplikat-Verteilung nach Konsole im Dashboard.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/WpfSlice.ReportPreview.ps1` | ✏️ | Heatmap-Rendering |
| `dev/modules/wpf/MainWindow.xaml` | ✏️ | Balkendiagramm-Container |
| `dev/modules/ReportBuilder.ps1` | ✏️ | Duplikat-Aggregation pro Konsole |
| `dev/tests/unit/Report.Heatmap.Tests.ps1` | 📁 | Aggregations-Tests |

**Implementierung:**
1. `ReportBuilder.ps1`: `Get-DuplicateHeatmapData` — zählt Duplikate pro Konsole
2. WPF: `ItemsControl` mit `Rectangle`-Template, Breite proportional zur Anzahl
3. Farbe: Neon-Gradient (wenige = grün, viele = rot)

**Tests:**
- 5 Konsolen mit Duplikaten → korrekte Sortierung (meiste oben)
- 0 Duplikate → "Keine Duplikate gefunden" Hinweis
- Daten-Shape korrekt (Konsole, Anzahl, Prozent)

**Abhängigkeiten:** ReportBuilder.ps1, Dedupe-Ergebnis

---

### QW-10: PowerShell-Script-Generator

**ID:** QW-10 | **Aufwand:** S | **Priorität:** P0 | **PRD-Ref:** FR-014

**Beschreibung:** Aktuelle GUI-Konfiguration als reproduzierbares CLI-Kommando exportieren.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/ApplicationServices.ps1` | ✏️ | Neue Funktion `Export-CliCommand` |
| `dev/modules/WpfSlice.Settings.ps1` | ✏️ | "CLI-Befehl kopieren"-Button |
| `dev/tests/unit/CliExport.Tests.ps1` | 📁 | Round-Trip-Tests |

**Funktions-Signatur:**
```powershell
function Export-CliCommand {
    param(
        [Parameter(Mandatory)][hashtable]$Settings
    )
    # Returns: [string] z.B. 'pwsh -NoProfile -File ./Invoke-RomCleanup.ps1 -Roots "D:\Roms" -Mode DryRun -PreferRegions EU,US'
}
```

**Tests:**
- Settings mit Roots + Mode → korrektes CLI-Kommando
- Sonderzeichen in Pfaden → korrekt gequotet
- Alle relevanten Settings abgebildet
- Round-Trip: CLI-Kommando parsen → gleiche Settings

**Abhängigkeiten:** Settings.ps1 (aktuelles Schema)

---

### QW-11: Webhook-Benachrichtigung

**ID:** QW-11 | **Aufwand:** S | **Priorität:** P1 | **PRD-Ref:** FR-015

**Beschreibung:** Discord/Slack Webhook-Benachrichtigung bei Run-Ende.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/Notifications.ps1` | ✏️ | Neue Funktion `Invoke-WebhookNotification` |
| `dev/modules/Settings.ps1` | ✏️ | `webhookUrl` Feld in Settings-Schema |
| `dev/modules/ApplicationServices.ps1` | ✏️ | Webhook nach Run-Ende aufrufen |
| `dev/modules/WpfSlice.Settings.ps1` | ✏️ | Webhook-URL-Eingabefeld |
| `data/defaults.json` | ✏️ | `"webhookUrl": ""` |
| `dev/tests/unit/Notification.Webhook.Tests.ps1` | 📁 | Tests |

**Funktions-Signatur:**
```powershell
function Invoke-WebhookNotification {
    param(
        [Parameter(Mandatory)][string]$WebhookUrl,
        [Parameter(Mandatory)][hashtable]$Summary,
        [int]$TimeoutSeconds = 10,
        [int]$MaxRetries = 3
    )
    # POST JSON an URL. Returns: @{ Success; StatusCode; Error }
}
```

**⚠️ Security:** URL muss validiert werden (nur HTTPS, keine internen/private IPs → SSRF-Schutz).

**Tests:**
- Gültiger Webhook → Mock HTTP → Success
- URL nicht erreichbar → Retry 3× → Failed + Warnung
- Leere URL → Skip (kein Fehler)
- SSRF-Schutz: `http://127.0.0.1` → ablehnen
- Summary-JSON korrekt formatiert

**Abhängigkeiten:** Notifications.ps1

---

### QW-12: Portable-Modus

**ID:** QW-12 | **Aufwand:** S | **Priorität:** P1 | **PRD-Ref:** FR-016

**Beschreibung:** `--Portable` Flag: Settings/Logs/Caches relativ zum Programmordner.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/Settings.ps1` | ✏️ | Portable-Pfad-Auflösung |
| `dev/modules/Logging.ps1` | ✏️ | Log-Pfad relativ |
| `Invoke-RomCleanup.ps1` | ✏️ | `-Portable` Parameter |
| `simple_sort.ps1` | ✏️ | `-Portable` Parameter |
| `dev/tests/unit/Settings.Portable.Tests.ps1` | 📁 | Tests |

**Implementierung:**
1. `Settings.ps1`: `Get-SettingsRoot` prüft ob `$Portable` → `$PSScriptRoot\.romcleanup\` statt `%APPDATA%`
2. Alle Pfade (Settings, Logs, Cache, DAT-Index) über `Get-SettingsRoot` aufgelöst
3. Marker-Datei: `.portable` im Programmordner → Auto-Detect

**Tests:**
- Portable=true → Settings in Script-Ordner
- Portable=false → Settings in %APPDATA%
- .portable Datei existiert → Auto-Detect
- Kein Schreibrecht auf Script-Ordner → Fehler

**Abhängigkeiten:** Settings.ps1, Logging.ps1

---

### QW-13: Export nach Excel-CSV

**ID:** QW-13 | **Aufwand:** S | **Priorität:** P0 | **PRD-Ref:** FR-022

**Beschreibung:** Sammlung als strukturiertes CSV mit allen Metadaten exportieren.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/Report.ps1` | ✏️ | Neue Funktion `Export-CollectionCsv` |
| `dev/modules/WpfSlice.ReportPreview.ps1` | ✏️ | "CSV exportieren"-Button |
| `dev/tests/unit/Report.CsvExport.Tests.ps1` | 📁 | Tests inkl. CSV-Injection-Schutz |

**CSV-Spalten:**
```
Dateiname, Konsole, Region, Format, Größe_MB, Kategorie, DAT_Status, Hash_SHA1, Pfad
```

**⚠️ Security:** CSV-Injection-Schutz — Feldwerte die mit `=`, `+`, `-`, `@` beginnen, mit `'` prefixen.

**Tests:**
- Export mit 100 ROMs → korrekte Spalten
- Feld beginnt mit `=SUM(` → wird zu `'=SUM(`
- Sonderzeichen in Dateinamen → korrekt escaped
- UTF-8 BOM für Excel-Kompatibilität

**Abhängigkeiten:** Report.ps1, Classification-Ergebnis

---

### QW-14: Run-History-Browser

**ID:** QW-14 | **Aufwand:** S | **Priorität:** P1 | **PRD-Ref:** FR-023

**Beschreibung:** Liste aller bisherigen Runs mit Datum, Roots, Modus, Ergebnis.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/RunIndex.ps1` | ✏️ | `Get-RunHistory` + `Get-RunDetail` |
| `dev/modules/WpfSlice.ReportPreview.ps1` | ✏️ | History-Tab/List |
| `dev/modules/wpf/MainWindow.xaml` | ✏️ | History-ListView |
| `dev/tests/unit/RunIndex.History.Tests.ps1` | 📁 | Tests |

**Implementierung:**
1. `RunIndex.ps1` schreibt bereits Run-Metadaten — `Get-RunHistory` liest sie sortiert aus
2. WPF: ListView mit Spalten: Datum, Roots, Modus, Status, Dateien, Link
3. Klick auf Eintrag → zugehörigen Report/Audit-CSV öffnen
4. Max. 100 Einträge, älteste gelöscht (Rotation bereits vorhanden)

**Tests:**
- 5 Runs gespeichert → korrekt sortiert (neueste zuerst)
- 0 Runs → leere Liste + Hinweis
- Run-Eintrag → Link zu Report-Datei korrekt

**Abhängigkeiten:** RunIndex.ps1

---

### QW-15: M3U-Auto-Generierung für Multi-Disc

**ID:** QW-15 | **Aufwand:** S | **Priorität:** P0 | **PRD-Ref:** US-004

**Beschreibung:** Automatisches Erstellen von `.m3u`-Playlists für Multi-Disc-Spiele.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/Sets.ps1` | ✏️ | Neue Funktion `New-M3uPlaylist` |
| `dev/modules/SetParsing.ps1` | ✏️ | Multi-Disc-Erkennung via `(Disc X)` Pattern |
| `dev/modules/ApplicationServices.ps1` | ✏️ | M3U als Post-Dedupe-Schritt |
| `dev/tests/unit/Sets.M3u.Tests.ps1` | 📁 | Tests |

**Funktions-Signatur:**
```powershell
function New-M3uPlaylist {
    param(
        [Parameter(Mandatory)][object[]]$DiscFiles,  # Sortierte Disc-Dateien
        [Parameter(Mandatory)][string]$OutputDir,
        [ValidateSet('DryRun','Move')][string]$Mode = 'DryRun'
    )
    # Returns: @{ M3uPath; DiscCount; GameName; Status }
}
```

**Disc-Erkennung-Pattern:** `\(Disc\s*(\d+)\)` aus Dateiname

**Tests:**
- 3 Discs erkannt → M3U mit 3 Einträgen, korrekte Reihenfolge
- CHD-Dateien → `.chd` Pfade in M3U
- Disc 1, 3, 4 (ohne 2) → Warnung "Disc 2 fehlt"
- M3U existiert bereits → Skip + Warnung
- Mixed-Formate (CHD+BIN) → Warnung, M3U trotzdem erstellt
- DryRun → keine Datei geschrieben

**Abhängigkeiten:** SetParsing.ps1 (Disc-Pattern), Core.ps1 (GameKey)

---

### QW-16: RetroArch-Playlist-Export

**ID:** QW-16 | **Aufwand:** S | **Priorität:** P1 | **PRD-Ref:** US-005

**Beschreibung:** Sammlungsdaten als `.lpl`-Datei (RetroArch-Format) mit Core-Zuweisungen exportieren.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/Report.ps1` | ✏️ | Neue Funktion `Export-RetroArchPlaylist` |
| `data/retroarch-cores.json` | 📁 | Core-Mapping: Konsole→Core-Name |
| `dev/modules/WpfSlice.ReportPreview.ps1` | ✏️ | "RetroArch-Export"-Button |
| `dev/tests/unit/Report.RetroArch.Tests.ps1` | 📁 | Tests |

**RetroArch .lpl Format:**
```json
{
  "version": "1.5",
  "default_core_path": "",
  "default_core_name": "",
  "items": [
    {
      "path": "D:\\Roms\\SNES\\Super Mario World (Europe).sfc",
      "label": "Super Mario World (Europe)",
      "core_path": "DETECT",
      "core_name": "DETECT",
      "crc32": "A0DA23B0|crc",
      "db_name": "Nintendo - Super Nintendo Entertainment System.lpl"
    }
  ]
}
```

**Core-Mapping (retroarch-cores.json):**
```json
{
  "SNES": { "core": "snes9x_libretro", "db": "Nintendo - Super Nintendo Entertainment System" },
  "NES":  { "core": "mesen_libretro",  "db": "Nintendo - Nintendo Entertainment System" },
  "GBA":  { "core": "mgba_libretro",   "db": "Nintendo - Game Boy Advance" }
}
```

**Tests:**
- SNES-Sammlung → korrekte .lpl mit snes9x Core
- Unbekannte Konsole → Core = "DETECT", Warnung
- Sonderzeichen → JSON-Escaping korrekt
- 0 ROMs → leere Playlist

**Abhängigkeiten:** Report.ps1, Classification-Ergebnis

---

## Phase 2 — Medium Features (M)

---

### MF-01: Missing-ROM-Tracker

**ID:** MF-01 | **Aufwand:** M | **Priorität:** P1 | **PRD-Ref:** US-009, FR-025

**Beschreibung:** Pro Konsole zeigen welche Spiele laut DAT fehlen, filterbar nach Region.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/Dat.ps1` | ✏️ | Neue Funktion `Get-DatMissingGames` |
| `dev/modules/ApplicationServices.ps1` | ✏️ | Neuer UseCase `Invoke-RunMissingReportService` |
| `dev/modules/WpfSlice.DatMapping.ps1` | ✏️ | Missing-Tab/Sektion |
| `dev/modules/Report.ps1` | ✏️ | Missing-Report HTML/CSV |
| `dev/tests/unit/Dat.Missing.Tests.ps1` | 📁 | Tests |

**Funktions-Signatur:**
```powershell
function Get-DatMissingGames {
    param(
        [Parameter(Mandatory)][hashtable]$DatIndex,
        [Parameter(Mandatory)][hashtable]$FoundHashes,
        [string[]]$FilterRegions
    )
    # Returns: @( @{ Name; Region; Size; DatSource } )
}
```

**Tests:**
- 100 DAT-Einträge, 95 gefunden → 5 Missing korrekt
- Filter auf EU → nur EU-Missing
- 0 DAT-Einträge → leeres Ergebnis
- 50.000 Einträge → Performance < 2s

**Abhängigkeiten:** Dat.ps1 (DAT-Index), 🔗 MF-14 (Parallel-Hashing für schnellere Hash-Berechnung)

---

### MF-02: Cross-Root-Duplikat-Finder

**ID:** MF-02 | **Aufwand:** M | **Priorität:** P1 | **PRD-Ref:** US-010, FR-039

**Beschreibung:** Hash-basierte Erkennung identischer ROMs über verschiedene Root-Verzeichnisse.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/Dedupe.ps1` | ✏️ | Neue Funktion `Find-CrossRootDuplicates` |
| `dev/modules/ApplicationServices.ps1` | ✏️ | UseCase `Invoke-RunCrossRootDedupeService` |
| `dev/modules/WpfSlice.Roots.ps1` | ✏️ | Cross-Root-UI |
| `dev/tests/unit/Dedupe.CrossRoot.Tests.ps1` | 📁 | Tests |

**Funktions-Signatur:**
```powershell
function Find-CrossRootDuplicates {
    param(
        [Parameter(Mandatory)][string[]]$Roots,
        [ValidateSet('SHA1','SHA256','MD5','CRC32')][string]$HashType = 'SHA1',
        [scriptblock]$Progress
    )
    # Returns: @( @{ Hash; Files = @( @{Path; Root; Size; Format} ) } )
}
```

**Tests:**
- Gleiche Datei in 2 Roots → korrekt erkannt
- Gleicher Dateiname, anderer Hash → kein Duplikat
- 3 Roots, 1 offline → Warnung, andere 2 scannen
- Merge-Vorschlag: FormatScore-basiert

**Abhängigkeiten:** Dedupe.ps1, FormatScoring.ps1, 🔗 MF-14 (Parallel-Hashing)

---

### MF-03: ROM-Header-Analyse

**ID:** MF-03 | **Aufwand:** M | **Priorität:** P1 | **PRD-Ref:** FR-036

**Beschreibung:** Interne Header auslesen für NES, SNES, GBA, N64.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/HeaderAnalysis.ps1` | 📁 | Neues Modul für Header-Parsing |
| `dev/modules/RomCleanupLoader.ps1` | ✏️ | Modul in Lade-Reihenfolge |
| `dev/modules/Classification.ps1` | ✏️ | Header-Info als Klassifikations-Boost |
| `dev/tests/unit/HeaderAnalysis.Tests.ps1` | 📁 | Tests mit Fixtures |
| `dev/tests/fixtures/headers/` | 📁 | Binär-Fixtures (iNES, LoROM etc.) |

**Header-Formate:**
- **NES:** iNES (16 Bytes: `NES\x1A`) / NES 2.0 (Byte 7 Bit 3)
- **SNES:** LoROM (Offset `0x7FC0`) / HiROM (Offset `0xFFC0`)
- **GBA:** Nintendo-Logo (Offset `0x04`, 156 Bytes) + Title (Offset `0xA0`, 12 Bytes)
- **N64:** Magic Bytes: `0x80371240` (Big-Endian) / `0x40123780` (Byte-Swapped)

**Tests:**
- gültige iNES-Datei → Mapper, Mirroring, PRG/CHR-Size korrekt
- Bad-Dump (falscher Header) → Warnung "Header-Anomalie"
- Kein bekannter Header → "Unknown"
- GBA ohne Nintendo-Logo → "Possibly modified"

**Abhängigkeiten:** Keine (neues Modul)

---

### MF-04: Sammlung-Completeness-Ziel

**ID:** MF-04 | **Aufwand:** M | **Priorität:** P1 | **PRD-Ref:** FR-037

**Beschreibung:** User-definierte Zielsets mit Fortschrittsbalken.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/Dat.ps1` | ✏️ | `Get-CompletenessReport` |
| `dev/modules/Settings.ps1` | ✏️ | `completenessGoals` Array in Settings |
| `dev/modules/WpfSlice.DatMapping.ps1` | ✏️ | Completeness-UI mit Fortschrittsbalken |
| `dev/tests/unit/Dat.Completeness.Tests.ps1` | 📁 | Tests |

**Goal-Definition (Settings):**
```json
{
  "completenessGoals": [
    { "name": "100% EU PS1 RPGs", "console": "PS1", "region": "EU", "genre": "RPG" },
    { "name": "Full SNES Collection", "console": "SNES", "region": null, "genre": null }
  ]
}
```

**Abhängigkeiten:** Dat.ps1, 🔗 MF-01 (Missing-Tracker)

---

### MF-05: Smart-Collections / Auto-Playlists

**ID:** MF-05 | **Aufwand:** M | **Priorität:** P1 | **PRD-Ref:** FR-038

**Beschreibung:** Dynamische Filter-basierte Collections aus DAT-Metadaten + User-Tags.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/CollectionManager.ps1` | 📁 | Neues Modul |
| `dev/modules/RomCleanupLoader.ps1` | ✏️ | Modul laden |
| `dev/modules/WpfSlice.ReportPreview.ps1` | ✏️ | Collections-UI |
| `dev/tests/unit/CollectionManager.Tests.ps1` | 📁 | Tests |

**Abhängigkeiten:** Classification.ps1, Dat.ps1, 🔗 MF-04

---

### MF-06: CSO/ZSO→ISO→CHD-Pipeline

**ID:** MF-06 | **Aufwand:** M | **Priorität:** P1 | **PRD-Ref:** FR-031

**Beschreibung:** Komprimierte PSP/PS1-Images in einem Schritt konvertieren.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/Convert.ps1` | ✏️ | Multi-Step-Pipeline in Strategy-Map |
| `dev/modules/Tools.ps1` | ✏️ | `ciso`-Wrapper (`Invoke-Ciso`) |
| `data/tool-hashes.json` | ✏️ | SHA256 für `ciso.exe` |
| `dev/tests/unit/Convert.Pipeline.Tests.ps1` | 📁 | Pipeline-Tests |

**Pipeline:** CSO → (ciso) → ISO → (chdman) → CHD → Cleanup Temp-ISO

**⚠️ Risiko:** Temp-ISO kann sehr groß werden (4+ GB). Disk-Space-Check vor Start.

**Tests:**
- CSO→ISO→CHD → Enddatei korrekt, Temp gelöscht
- Disk-Space nicht ausreichend → Fehler vor Start
- ciso Exit-Code ≠ 0 → Abbruch, kein Temp-Artefakt
- DryRun → nur Schätzung, kein Schreibvorgang

**Abhängigkeiten:** Convert.ps1, Tools.ps1, 🔗 QW-04 (Speicherprognose)

---

### MF-07: NKit→ISO-Rückkonvertierung

**ID:** MF-07 | **Aufwand:** M | **Priorität:** P1 | **PRD-Ref:** FR-032

**Beschreibung:** NKit-Images zurück zu vollem ISO via NKit-Tool.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/Convert.ps1` | ✏️ | NKit→ISO in Strategy-Map |
| `dev/modules/Tools.ps1` | ✏️ | NKit-Tool-Wrapper |
| `data/tool-hashes.json` | ✏️ | SHA256 für NKit-Tool |
| `dev/tests/unit/Convert.NKit.Tests.ps1` | 📁 | Tests |

**Abhängigkeiten:** Convert.ps1, Tools.ps1

---

### MF-08: Konvertierungs-Queue mit Pause/Resume

**ID:** MF-08 | **Aufwand:** M | **Priorität:** P1 | **PRD-Ref:** US-011, FR-033

**Beschreibung:** Pausierbare Konvertierungs-Queue mit persistiertem Status.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/ConvertQueue.ps1` | 📁 | Neues Modul für Queue-Management |
| `dev/modules/RomCleanupLoader.ps1` | ✏️ | Modul laden |
| `dev/modules/Convert.ps1` | ✏️ | Queue-Integration |
| `dev/modules/WpfSlice.RunControl.ps1` | ✏️ | Pause/Resume-Buttons |
| `dev/tests/unit/ConvertQueue.Tests.ps1` | 📁 | Tests |

**Persistierung:** `reports/convert-queue.json`
```json
{
  "queueId": "guid",
  "created": "2026-03-09T14:00:00",
  "status": "paused",
  "currentIndex": 47,
  "items": [
    { "source": "path", "target": "path", "status": "completed|pending|failed", "error": null }
  ]
}
```

**Tests:**
- 100 Items, Pause bei 50 → Status=paused, currentIndex=50
- Resume → weiter bei 51
- App-Crash → Queue aus Datei laden
- Item fehlgeschlagen → nächstes Item, Fehler geloggt

**Abhängigkeiten:** Convert.ps1, 🔗 MF-09 (Batch-Verify)

---

### MF-09: Batch-Verify nach Konvertierung

**ID:** MF-09 | **Aufwand:** M | **Priorität:** P1 | **PRD-Ref:** FR-034

**Beschreibung:** Automatischer Hash-Vergleich vor/nach Konvertierung.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/Convert.ps1` | ✏️ | `Test-ConversionIntegrity` Funktion |
| `dev/modules/Dat.ps1` | ✏️ | Hash-Lookup für Quelldatei |
| `dev/tests/unit/Convert.Verify.Tests.ps1` | 📁 | Tests |

**Ablauf:** Quell-Hash speichern → Konvertieren → chdman verify (oder Tool-spezifisch) → Report

**Tests:**
- Konvertierung korrekt → Verify = Pass
- Hash-Mismatch → Warnung + Quelldatei behalten
- chdman verify Exit-Code > 0 → Fehler

**Abhängigkeiten:** Convert.ps1, Dat.ps1, 🔗 MF-08

---

### MF-10: Konvertierungs-Prioritätsliste

**ID:** MF-10 | **Aufwand:** M | **Priorität:** P1 | **PRD-Ref:** FR-035

**Beschreibung:** Konfigurierbare Zielformat-Hierarchie pro Konsole.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/FormatScoring.ps1` | ✏️ | User-konfigurierbare Prioritäten |
| `dev/modules/Settings.ps1` | ✏️ | `formatPriority` in Settings |
| `dev/modules/WpfSlice.Settings.ps1` | ✏️ | Prioritäts-Editor-UI |
| `dev/tests/unit/FormatScoring.Priority.Tests.ps1` | 📁 | Tests |

**Settings-Erweiterung:**
```json
{
  "formatPriority": {
    "PS1": ["CHD", "BIN/CUE", "PBP", "CSO"],
    "GC": ["RVZ", "ISO", "NKit"]
  }
}
```

**Abhängigkeiten:** FormatScoring.ps1, Settings.ps1

---

### MF-11: DAT-Auto-Update

**ID:** MF-11 | **Aufwand:** M | **Priorität:** P1 | **PRD-Ref:** US-012, FR-026

**Beschreibung:** Automatischer Check und Download neuer DAT-Versionen.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/DatSources.ps1` | ✏️ | `Test-DatUpdateAvailable` + `Update-DatSource` |
| `dev/modules/WpfSlice.DatMapping.ps1` | ✏️ | Update-Button + Changelog-Popup |
| `dev/modules/Settings.ps1` | ✏️ | `datAutoUpdate` + `datUpdateInterval` |
| `dev/tests/unit/DatSources.Update.Tests.ps1` | 📁 | Tests |

**Tests:**
- Neue Version verfügbar → korrekt erkannt
- Download + SHA256-Prüfung → Success
- SHA256-Mismatch → Rollback auf alte Version
- Kein Internet → Warnung, letzte Version nutzen

**Abhängigkeiten:** DatSources.ps1, 🔗 MF-12 (DAT-Diff)

---

### MF-12: DAT-Diff-Viewer

**ID:** MF-12 | **Aufwand:** M | **Priorität:** P1 | **PRD-Ref:** FR-027

**Beschreibung:** Änderungen zwischen zwei DAT-Versionen anzeigen.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/Dat.ps1` | ✏️ | `Compare-DatVersions` |
| `dev/modules/WpfSlice.DatMapping.ps1` | ✏️ | Diff-Ansicht |
| `dev/tests/unit/Dat.Diff.Tests.ps1` | 📁 | Tests |

**Diff-Output:**
```powershell
@{
    Added   = @("Game X (Europe)", "Game Y (Japan)")
    Removed = @("Game Z (Beta)")
    Renamed = @( @{ Old = "Game A (U)"; New = "Game A (USA)" } )
    Count   = @{ Added = 2; Removed = 1; Renamed = 1 }
}
```

**Abhängigkeiten:** Dat.ps1, 🔗 MF-11

---

### MF-13: TOSEC-DAT-Support

**ID:** MF-13 | **Aufwand:** M | **Priorität:** P1 | **PRD-Ref:** FR-028

**Beschreibung:** TOSEC-Kataloge als zusätzliche DAT-Quelle.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/Dat.ps1` | ✏️ | TOSEC-Parser (anderes XML-Schema als No-Intro/Redump) |
| `dev/modules/DatSources.ps1` | ✏️ | TOSEC als Quell-Typ |
| `data/dat-catalog.json` | ✏️ | TOSEC-URLs |
| `dev/tests/unit/Dat.Tosec.Tests.ps1` | 📁 | TOSEC-Parser-Tests |

**⚠️ Achtung:** TOSEC-Benennungsschema unterscheidet sich von No-Intro (`Title (Year)(Publisher)(Region)` vs. `Title (Region) (Version)`).

**Abhängigkeiten:** Dat.ps1, DatSources.ps1

---

### MF-14: Parallel-Hashing

**ID:** MF-14 | **Aufwand:** M | **Priorität:** P1 | **PRD-Ref:** FR-029

**Beschreibung:** Multi-threaded Hash-Berechnung via RunspacePool.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/Dat.ps1` | ✏️ | `Get-ParallelFileHashes` |
| `dev/modules/RunspaceLifecycle.ps1` | ✏️ | Shared RunspacePool für Hashing |
| `dev/modules/BackgroundOps.ps1` | ✏️ | Hash-Job-Definition |
| `dev/tests/unit/Dat.ParallelHash.Tests.ps1` | 📁 | Benchmark-Tests |

**Implementierung:**
1. RunspacePool mit `[Environment]::ProcessorCount` Threads (max. 8)
2. Dateien in Chunks aufteilen (100 pro Batch)
3. Progress-Callback für UI
4. Fallback auf single-threaded bei RunspacePool-Fehler

**Tests:**
- 1.000 Dateien → Hashes korrekt = single-threaded Ergebnis
- Speedup ≥ 4× bei 8 Cores (Benchmark)
- RunspacePool-Fehler → Fallback single-threaded
- Cancel während Hashing → sauberes Cleanup

**Abhängigkeiten:** RunspaceLifecycle.ps1, BackgroundOps.ps1

---

### MF-15: Command-Palette

**ID:** MF-15 | **Aufwand:** M | **Priorität:** P1 | **PRD-Ref:** US-014, FR-010

**Beschreibung:** VSCode-artiges Ctrl+Shift+P mit Fuzzy-Suche.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/CommandPalette.ps1` | 📁 | Neues Modul: Palette-Logik + Fuzzy-Search |
| `dev/modules/wpf/CommandPaletteOverlay.xaml` | 📁 | XAML-Overlay mit TextBox + Liste |
| `dev/modules/WpfEventHandlers.ps1` | ✏️ | Ctrl+Shift+P Binding |
| `dev/modules/RomCleanupLoader.ps1` | ✏️ | Modul laden (nur GUI) |
| `dev/tests/unit/CommandPalette.Tests.ps1` | 📁 | Fuzzy-Search-Tests |

**Command-Registry:**
```powershell
@(
    @{ Name = "DryRun starten"; Key = "run.dryrun"; Action = { ... }; Shortcut = "Ctrl+Shift+D" }
    @{ Name = "Konvertierung starten"; Key = "convert.start"; Action = { ... } }
    @{ Name = "Settings öffnen"; Key = "settings.open"; Action = { ... } }
)
```

**Fuzzy-Search:** Substring + Levenshtein mit Threshold ≤ 3

**Tests:**
- "conv" → "Konvertierung starten" als Treffer
- "xyz" → keine Ergebnisse
- Enter → Command ausgeführt
- Escape → Palette geschlossen

**Abhängigkeiten:** 🔗 QW-06 (Shortcuts)

---

### MF-16: Split-Panel-Vorschau

**ID:** MF-16 | **Aufwand:** M | **Priorität:** P1 | **PRD-Ref:** FR-011

**Beschreibung:** Norton-Commander-artige Quell-/Ziel-Ansicht für DryRun-Preview.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/wpf/SplitPanelView.xaml` | 📁 | Zwei-Panel-Layout |
| `dev/modules/WpfSlice.ReportPreview.ps1` | ✏️ | Split-Panel-Integration |
| `dev/tests/unit/WpfSplitPanel.Tests.ps1` | 📁 | Tests |

**Abhängigkeiten:** DryRun-Ergebnis

---

### MF-17: Filter-Builder

**ID:** MF-17 | **Aufwand:** M | **Priorität:** P1 | **PRD-Ref:** FR-012

**Beschreibung:** Visueller Query-Builder: "Zeige alle PS1 ROMs > 500MB ohne DAT-Match".

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/FilterBuilder.ps1` | 📁 | Filter-DSL + Query-Engine |
| `dev/modules/wpf/FilterBuilderControl.xaml` | 📁 | UI-Komponente |
| `dev/modules/WpfSlice.ReportPreview.ps1` | ✏️ | Filter-Builder einbinden |
| `dev/tests/unit/FilterBuilder.Tests.ps1` | 📁 | Tests |

**Filter-Model:**
```powershell
@{
    Conditions = @(
        @{ Field = "Console"; Operator = "eq"; Value = "PS1" }
        @{ Field = "SizeMB"; Operator = "gt"; Value = 500 }
        @{ Field = "DatStatus"; Operator = "eq"; Value = "NoMatch" }
    )
    Logic = "AND"
}
```

**Abhängigkeiten:** Classification-Ergebnis

---

### MF-18: Mini-Modus / System-Tray

**ID:** MF-18 | **Aufwand:** M | **Priorität:** P1 | **PRD-Ref:** FR-013

**Beschreibung:** App minimiert im System-Tray mit Status-Icon.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/WpfApp.ps1` | ✏️ | `NotifyIcon` Integration (System.Windows.Forms) |
| `dev/modules/WpfEventHandlers.ps1` | ✏️ | Minimize-to-Tray Handling |
| `dev/tests/unit/WpfTray.Tests.ps1` | 📁 | Tests |

**⚠️ Achtung:** Erfordert `System.Windows.Forms` Assembly für `NotifyIcon` — Kompatibilität mit WPF-App prüfen.

**Abhängigkeiten:** WpfApp.ps1

---

### MF-19: Rule-Engine

**ID:** MF-19 | **Aufwand:** M | **Priorität:** P1 | **PRD-Ref:** US-013, FR-017

**Beschreibung:** User-definierbare Regeln für Klassifikation.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/RuleEngine.ps1` | 📁 | Neues Modul: Regel-Parser, Evaluator, Prioritäten |
| `dev/modules/RomCleanupLoader.ps1` | ✏️ | Modul laden |
| `dev/modules/Classification.ps1` | ✏️ | RuleEngine-Integration |
| `dev/modules/WpfSlice.AdvancedFeatures.ps1` | ✏️ | Regel-Editor-UI |
| `data/schemas/rules-user.schema.json` | 📁 | JSON Schema für User-Regeln |
| `dev/tests/unit/RuleEngine.Tests.ps1` | 📁 | Tests |

**Regel-Format:**
```json
{
  "rules": [
    {
      "name": "Remove JP non-PS1",
      "priority": 10,
      "conditions": [
        { "field": "region", "op": "eq", "value": "JP" },
        { "field": "console", "op": "neq", "value": "PS1" }
      ],
      "action": "junk",
      "reason": "User-Regel: JP-ROMs außer PS1 entfernt"
    }
  ]
}
```

**Tests:**
- Regel "Region=JP AND Konsole≠PS1 → JUNK" → korrekt evaluiert
- Regel-Konflikt → höhere Priorität gewinnt
- Ungültige Regel-Syntax → Validierungsfehler
- Regel trifft 100% → Warnung

**Abhängigkeiten:** Classification.ps1, rules.json

---

### MF-20: Conditional-Pipelines

**ID:** MF-20 | **Aufwand:** M | **Priorität:** P1 | **PRD-Ref:** FR-018

**Beschreibung:** Mehrstufige Aktionsketten: Sortieren → Konvertieren → Verifizieren → Umbenennen.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/PipelineEngine.ps1` | 📁 | Pipeline-Definition + Runner |
| `dev/modules/RomCleanupLoader.ps1` | ✏️ | Modul laden |
| `dev/modules/ApplicationServices.ps1` | ✏️ | `Invoke-RunPipelineService` |
| `dev/modules/WpfSlice.AdvancedFeatures.ps1` | ✏️ | Pipeline-Builder-UI |
| `dev/tests/unit/PipelineEngine.Tests.ps1` | 📁 | Tests |

**Pipeline-Definition:**
```json
{
  "name": "Full Cleanup",
  "steps": [
    { "action": "sort", "params": {} },
    { "action": "dedupe", "params": { "mode": "DryRun" } },
    { "action": "convert", "params": { "target": "CHD" } },
    { "action": "verify", "params": {} },
    { "action": "rename", "params": {} }
  ],
  "onError": "stop"
}
```

**Tests:**
- 5-Step-Pipeline → alle Steps sequenziell ausgeführt
- Step 3 fehlschlägt → Pipeline stoppt, vorherige Steps bleiben
- DryRun → alle Steps als DryRun
- onError=continue → nächster Step trotz Fehler

**Abhängigkeiten:** ApplicationServices.ps1 (alle Use Cases), 🔗 MF-19 (Rule-Engine)

---

### MF-21: DryRun-Vergleich

**ID:** MF-21 | **Aufwand:** M | **Priorität:** P1 | **PRD-Ref:** FR-019

**Beschreibung:** Zwei DryRuns mit unterschiedlichen Settings Side-by-Side vergleichen.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/ReportBuilder.ps1` | ✏️ | `Compare-DryRunResults` |
| `dev/modules/WpfSlice.ReportPreview.ps1` | ✏️ | Diff-UI |
| `dev/tests/unit/Report.DryRunDiff.Tests.ps1` | 📁 | Tests |

**Abhängigkeiten:** ReportBuilder.ps1, DryRun-Ergebnis-Format

---

### MF-22: Ordnerstruktur-Vorlagen

**ID:** MF-22 | **Aufwand:** M | **Priorität:** P1 | **PRD-Ref:** FR-020

**Beschreibung:** Vordefinierte Layouts: RetroArch, EmulationStation, LaunchBox, Batocera, Custom.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/ConsoleSort.ps1` | ✏️ | Template-basierte Sortierung |
| `data/sort-templates.json` | 📁 | Template-Definitionen |
| `dev/modules/WpfSlice.Settings.ps1` | ✏️ | Template-Dropdown |
| `dev/tests/unit/ConsoleSort.Templates.Tests.ps1` | 📁 | Tests |

**Template-Format (sort-templates.json):**
```json
{
  "RetroArch": {
    "pattern": "{console}/{filename}",
    "consoleMappings": { "SNES": "Nintendo - Super Nintendo Entertainment System" }
  },
  "EmulationStation": {
    "pattern": "roms/{console_lower}/{filename}",
    "consoleMappings": { "SNES": "snes" }
  }
}
```

**Abhängigkeiten:** ConsoleSort.ps1, consoles.json

---

### MF-23: Run-Scheduler mit Kalender-UI

**ID:** MF-23 | **Aufwand:** M | **Priorität:** P1 | **PRD-Ref:** FR-021

**Beschreibung:** Geplante Runs: "Jeden Sonntag 03:00 → Full Scan + Cleanup".

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/Scheduler.ps1` | ✏️ | Cron-artige Schedule-Engine erweitern |
| `dev/modules/WpfSlice.AdvancedFeatures.ps1` | ✏️ | Kalender-UI |
| `dev/tests/unit/Scheduler.Tests.ps1` | 📁 | Tests |

**Schedule-Format:**
```json
{
  "schedules": [
    {
      "name": "Weekly Cleanup",
      "cron": "0 3 * * 0",
      "pipeline": "Full Cleanup",
      "enabled": true
    }
  ]
}
```

**⚠️ Achtung:** App muss laufen/im Tray sein. Alternative: Windows Task-Scheduler-Integration.

**Abhängigkeiten:** Scheduler.ps1, 🔗 MF-18 (Tray), 🔗 MF-20 (Pipelines)

---

### MF-24: Integritäts-Monitor

**ID:** MF-24 | **Aufwand:** M | **Priorität:** P1 | **PRD-Ref:** FR-041

**Beschreibung:** Periodischer Hash-Check für Bit-Rot-Erkennung.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/IntegrityMonitor.ps1` | 📁 | Neues Modul |
| `dev/modules/RomCleanupLoader.ps1` | ✏️ | Modul laden |
| `dev/modules/WpfSlice.AdvancedFeatures.ps1` | ✏️ | Integritäts-Dashboard |
| `dev/tests/unit/IntegrityMonitor.Tests.ps1` | 📁 | Tests |

**Ablauf:**
1. Hash-Baseline speichern (beim ersten Scan)
2. Periodisch: aktuelle Hashes berechnen → vergleichen
3. Mismatch → Warnung "Datei X hat sich geändert"

**Abhängigkeiten:** Dat.ps1 (Hash-Cache), 🔗 MF-14 (Parallel-Hashing)

---

### MF-25: Automatische Backup-Strategie

**ID:** MF-25 | **Aufwand:** M | **Priorität:** P1 | **PRD-Ref:** FR-042

**Beschreibung:** Inkrementelle Backups vor jeder Operation mit Retention-Policy.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/BackupManager.ps1` | 📁 | Neues Modul |
| `dev/modules/RomCleanupLoader.ps1` | ✏️ | Modul laden |
| `dev/modules/ApplicationServices.ps1` | ✏️ | Backup vor Move/Convert |
| `dev/modules/Settings.ps1` | ✏️ | `backup` Settings |
| `dev/tests/unit/BackupManager.Tests.ps1` | 📁 | Tests |

**Settings:**
```json
{
  "backup": {
    "enabled": true,
    "backupRoot": "D:\\RomBackups",
    "retentionDays": 30,
    "maxSizeGB": 50
  }
}
```

**Abhängigkeiten:** FileOps.ps1, Settings.ps1

---

### MF-26: ROM-Quarantäne

**ID:** MF-26 | **Aufwand:** M | **Priorität:** P1 | **PRD-Ref:** FR-043

**Beschreibung:** Verdächtige/unbekannte Dateien in Quarantäne-Ordner isolieren.

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/FileOps.ps1` | ✏️ | `Move-ToQuarantine` Funktion |
| `dev/modules/Classification.ps1` | ✏️ | Quarantäne-Klassifikation |
| `dev/modules/WpfSlice.AdvancedFeatures.ps1` | ✏️ | Quarantäne-Übersicht |
| `dev/tests/unit/FileOps.Quarantine.Tests.ps1` | 📁 | Tests |

**Quarantäne-Kriterien:**
- Unbekanntes Format + unbekannte Konsole
- Hash nicht in DAT + verdächtiger Dateiname
- Header-Anomalien (🔗 MF-03)

**Abhängigkeiten:** FileOps.ps1, Classification.ps1, 🔗 MF-03 (Header-Analyse)

---

## Phase 3 — Large Features (L)

---

### LF-01: ROM-Thumbnail/Cover-Scraping

**ID:** LF-01 | **Aufwand:** L | **Priorität:** P2 | **PRD-Ref:** US-016

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/CoverScraper.ps1` | 📁 | Scraping-Engine (ScreenScraper.fr API) |
| `dev/modules/RomCleanupLoader.ps1` | ✏️ | Modul laden |
| `dev/modules/WpfSlice.ReportPreview.ps1` | ✏️ | Thumbnail-Anzeige |
| `dev/modules/Settings.ps1` | ✏️ | API-Credentials, Cache-Pfad |
| `dev/tests/unit/CoverScraper.Tests.ps1` | 📁 | Tests |

**⚠️ Security:** API-Key verschlüsselt speichern (DPAPI). Rate-Limiting respektieren. Keine internen URLs aufrufen (SSRF-Schutz).

**Abhängigkeiten:** Settings.ps1, Classification-Ergebnis

---

### LF-02: Genre-/Tag-Klassifikation

**ID:** LF-02 | **Aufwand:** L | **Priorität:** P2

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/GenreClassifier.ps1` | 📁 | Neues Modul |
| `dev/modules/Dat.ps1` | ✏️ | Genre-Extraktion aus DAT-Metadaten |
| `dev/tests/unit/GenreClassifier.Tests.ps1` | 📁 | Tests |

**Abhängigkeiten:** Dat.ps1, 🔗 LF-01 (Scraping als Fallback)

---

### LF-03: Emulator-Launcher-Integration

**ID:** LF-03 | **Aufwand:** L | **Priorität:** P2 | **PRD-Ref:** US-015

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/EmulatorExport.ps1` | 📁 | Multi-Format-Export-Engine |
| `data/emulator-mappings.json` | 📁 | Konsole→Emulator/Core-Mappings |
| `dev/tests/unit/EmulatorExport.Tests.ps1` | 📁 | Tests pro Format |

**Export-Formate:**
- RetroArch `.lpl` (🔗 QW-16 erweitern)
- LaunchBox XML (`<LaunchBox><Game>`)
- EmulationStation `gamelist.xml`
- Playnite Config

**Abhängigkeiten:** 🔗 QW-16 (RetroArch-Basis), Classification

---

### LF-04: Spielzeit-Tracking-Import

**ID:** LF-04 | **Aufwand:** L | **Priorität:** P2

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/PlaytimeImport.ps1` | 📁 | Import von RetroAchievements/RetroArch |
| `dev/tests/unit/PlaytimeImport.Tests.ps1` | 📁 | Tests |

**Abhängigkeiten:** RetroArch-Config-Parsing

---

### LF-05: IPS/BPS/UPS-Patch-Engine

**ID:** LF-05 | **Aufwand:** L | **Priorität:** P2 | **PRD-Ref:** US-017

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/PatchEngine.ps1` | 📁 | IPS/BPS/UPS-Implementierung |
| `dev/modules/RomCleanupLoader.ps1` | ✏️ | Modul laden |
| `dev/tests/unit/PatchEngine.Tests.ps1` | 📁 | Tests mit Fixtures |
| `dev/tests/fixtures/patches/` | 📁 | Test-Patches |

**Patch-Formate:**
- **IPS**: Offset+Size+Data Records, EOF `0x454F46`
- **BPS**: CRC32-validiert, Source/Target/Patch checksums
- **UPS**: XOR-basiert, CRC32-validiert

**Ablauf:** Original kopieren → Patch anwenden → Hash verifizieren → Audit-Log

**Abhängigkeiten:** FileOps.ps1

---

### LF-06: ROM-Header-Reparatur

**ID:** LF-06 | **Aufwand:** L | **Priorität:** P2

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/HeaderAnalysis.ps1` | ✏️ | `Repair-RomHeader` Funktion |
| `dev/tests/unit/HeaderAnalysis.Repair.Tests.ps1` | 📁 | Tests |

**Reparaturen:**
- NES: fehlenden/fehlerhaften iNES-Header korrigieren
- SNES: Copier-Header (512 Bytes am Anfang) entfernen

**⚠️ Risiko:** Daten-Modifikation → immer Backup + Verifizierung.

**Abhängigkeiten:** 🔗 MF-03 (Header-Analyse)

---

### LF-07: Arcade ROM-Merge/Split

**ID:** LF-07 | **Aufwand:** L | **Priorität:** P2 | **PRD-Ref:** US-018

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/ArcadeSets.ps1` | 📁 | Merge/Split/Non-Merged-Konvertierung |
| `dev/modules/RomCleanupLoader.ps1` | ✏️ | Modul laden |
| `dev/modules/Dat.ps1` | ✏️ | Parent/Clone-Auflösung für Arcade |
| `dev/tests/unit/ArcadeSets.Tests.ps1` | 📁 | Tests |

**Abhängigkeiten:** Dat.ps1 (Parent/Clone-Mapping), ZipSort.ps1

---

### LF-08: Intelligent Storage Tiering

**ID:** LF-08 | **Aufwand:** L | **Priorität:** P2

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/StorageTiering.ps1` | 📁 | SSD/HDD-Optimierung |
| `dev/tests/unit/StorageTiering.Tests.ps1` | 📁 | Tests |

**Abhängigkeiten:** 🔗 LF-04 (Spielzeit-Daten), FileOps.ps1

---

### LF-09: Custom-DAT-Editor

**ID:** LF-09 | **Aufwand:** L | **Priorität:** P2 | **PRD-Ref:** FR-030

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/DatEditor.ps1` | 📁 | DAT-Erstellung/Bearbeitung |
| `dev/modules/wpf/DatEditorView.xaml` | 📁 | Editor-UI |
| `dev/tests/unit/DatEditor.Tests.ps1` | 📁 | Tests |

**Abhängigkeiten:** Dat.ps1

---

### LF-10: Clone-List-Visualisierung

**ID:** LF-10 | **Aufwand:** L | **Priorität:** P2

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/WpfSlice.DatMapping.ps1` | ✏️ | TreeView für Parent/Clone |
| `dev/modules/Dat.ps1` | ✏️ | Clone-Tree-Datenstruktur |
| `dev/tests/unit/Dat.CloneTree.Tests.ps1` | 📁 | Tests |

**Abhängigkeiten:** Dat.ps1 (Parent/Clone-Index)

---

### LF-11: Hash-Datenbank-Export

**ID:** LF-11 | **Aufwand:** L | **Priorität:** P2

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/Dat.ps1` | ✏️ | `Export-HashDatabase` (JSON/SQLite) |
| `dev/tests/unit/Dat.Export.Tests.ps1` | 📁 | Tests |

**Abhängigkeiten:** Dat.ps1

---

### LF-12: Virtuelle Ordner-Vorschau (Treemap)

**ID:** LF-12 | **Aufwand:** L | **Priorität:** P2

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/wpf/TreemapControl.xaml` | 📁 | Treemap-Visualisierung |
| `dev/modules/WpfSlice.ReportPreview.ps1` | ✏️ | Treemap einbinden |
| `dev/tests/unit/Treemap.Tests.ps1` | 📁 | Tests |

**Abhängigkeiten:** Classification-Ergebnis, Scan-Daten

---

### LF-13: Barrierefreiheit (Accessibility)

**ID:** LF-13 | **Aufwand:** L | **Priorität:** P2 | **PRD-Ref:** NFR-031–034

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/wpf/MainWindow.xaml` | ✏️ | AutomationProperties für alle Elemente |
| `dev/modules/wpf/Themes/HighContrastTheme.xaml` | 📁 | High-Contrast-Theme |
| Alle `WpfSlice.*.ps1` | ✏️ | Accessibility-Properties setzen |
| `dev/tests/unit/Accessibility.Tests.ps1` | 📁 | Accessibility-Audit |

**Checkliste:**
- [ ] Alle Buttons/Labels haben `AutomationProperties.Name`
- [ ] Tab-Reihenfolge logisch (TabIndex)
- [ ] Focus-Indicator sichtbar (nicht nur Farbänderung)
- [ ] Alle Farben: min. 4.5:1 Kontrastverhältnis
- [ ] Skalierung bis 200% DPI ohne Layout-Bruch

**Abhängigkeiten:** Alle WPF-Module

---

### LF-14: PDF-Report-Export

**ID:** LF-14 | **Aufwand:** L | **Priorität:** P2 | **PRD-Ref:** FR-024

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/Report.ps1` | ✏️ | `Export-PdfReport` |
| `dev/tests/unit/Report.Pdf.Tests.ps1` | 📁 | Tests |

**Abhängigkeiten:** Report.ps1, HTML-Report als Basis

---

### LF-15: NAS/SMB-Optimierung

**ID:** LF-15 | **Aufwand:** L | **Priorität:** P2 | **PRD-Ref:** FR-044

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/FileOps.ps1` | ✏️ | Adaptive Batch-Größen, Retry-Logic |
| `dev/modules/Settings.ps1` | ✏️ | NAS-Profil (Throttling, Timeout, Batch-Size) |
| `dev/tests/unit/FileOps.Nas.Tests.ps1` | 📁 | Tests |

**Abhängigkeiten:** FileOps.ps1

---

### LF-16: FTP/SFTP-Source

**ID:** LF-16 | **Aufwand:** L | **Priorität:** P2 | **PRD-Ref:** FR-045

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/RemoteSource.ps1` | 📁 | FTP/SFTP-Adapter |
| `dev/modules/PortInterfaces.ps1` | ✏️ | FileSystem-Port um Remote-Support erweitern |
| `dev/tests/unit/RemoteSource.Tests.ps1` | 📁 | Tests |

**⚠️ Security:** SFTP bevorzugen, FTP nur mit Warnung. Credentials verschlüsselt (DPAPI).

**Abhängigkeiten:** PortInterfaces.ps1 (FileSystem-Port)

---

### LF-17: Cloud-Settings-Sync

**ID:** LF-17 | **Aufwand:** L | **Priorität:** P2

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/CloudSync.ps1` | 📁 | OneDrive/Dropbox-Sync für Metadaten |
| `dev/tests/unit/CloudSync.Tests.ps1` | 📁 | Tests |

**⚠️ Achtung:** Nur Metadaten/Settings synchronisieren, NIEMALS ROM-Dateien.

**Abhängigkeiten:** Settings.ps1

---

### LF-18: Plugin-Marketplace-UI

**ID:** LF-18 | **Aufwand:** L | **Priorität:** P2 | **PRD-Ref:** FR-046

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/PluginMarketplace.ps1` | 📁 | Marketplace-Client |
| `dev/modules/wpf/PluginMarketplaceView.xaml` | 📁 | Browse/Install-UI |
| `dev/modules/WpfSlice.AdvancedFeatures.ps1` | ✏️ | Marketplace einbinden |
| `dev/tests/unit/PluginMarketplace.Tests.ps1` | 📁 | Tests |

**⚠️ Security:** Plugins nur von signierter Quelle. Manifest-Validierung.

**Abhängigkeiten:** Plugin-System, Trust-Modi

---

### LF-19: Rule-Pack-Sharing

**ID:** LF-19 | **Aufwand:** L | **Priorität:** P2 | **PRD-Ref:** FR-047

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/RuleEngine.ps1` | ✏️ | Import/Export von Rule-Packs |
| `dev/tests/unit/RuleEngine.Sharing.Tests.ps1` | 📁 | Tests |

**⚠️ Security:** Signaturprüfung für importierte Rule-Packs.

**Abhängigkeiten:** 🔗 MF-19 (Rule-Engine)

---

### LF-20: Theme-Engine

**ID:** LF-20 | **Aufwand:** L | **Priorität:** P2 | **PRD-Ref:** FR-048

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/ThemeEngine.ps1` | 📁 | Theme-Laden/Validieren |
| `dev/modules/WpfApp.ps1` | ✏️ | Theme-Plugin-Support |
| `dev/tests/unit/ThemeEngine.Tests.ps1` | 📁 | Tests |

**Abhängigkeiten:** 🔗 QW-07 (Dark/Light-Basis)

---

## Phase 4 — XL / Strategische Features (v2.0)

---

### XL-01: Docker-Container

**ID:** XL-01 | **Aufwand:** XL | **Priorität:** P2 | **PRD-Ref:** US-020, FR-049

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `Dockerfile` | 📁 | Multi-Stage Build (.NET 8 SDK → Runtime) |
| `docker-compose.yml` | 📁 | Volume-Mounts, Env-Vars |
| `.dockerignore` | 📁 | Ignorierte Dateien |
| `dev/tests/e2e/Docker.Tests.ps1` | 📁 | Container-Smoke-Tests |

**Abhängigkeiten:** 🔗 XL-Migration (C# Core notwendig)

---

### XL-02: Mobile-Web-UI

**ID:** XL-02 | **Aufwand:** XL | **Priorität:** P2 | **PRD-Ref:** FR-050

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `web/` | 📁 | React/Vue Frontend |
| `dev/modules/ApiServer.ps1` | ✏️ | CORS für Web-UI |
| `dev/tests/e2e/WebUi.Tests.ps1` | 📁 | Tests |

**Abhängigkeiten:** REST-API (bereits vorhanden), 🔗 XL-01 (Docker)

---

### XL-03: Windows-Context-Menu

**ID:** XL-03 | **Aufwand:** XL | **Priorität:** P2 | **PRD-Ref:** FR-051

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `installer/ShellExtension.cs` | 📁 | COM Shell-Extension |
| `installer/register-context-menu.ps1` | 📁 | Registry-Eintrag |
| `dev/tests/unit/ShellExtension.Tests.ps1` | 📁 | Tests |

**Abhängigkeiten:** C#-Migration

---

### XL-04: PSGallery-Modul

**ID:** XL-04 | **Aufwand:** XL | **Priorität:** P2

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/RomCleanup.psd1` | ✏️ | PSGallery-Manifest |
| `dev/modules/RomCleanup.psm1` | ✏️ | Module-Export |
| `.github/workflows/publish.yml` | 📁 | Auto-Publish-Workflow |

**Abhängigkeiten:** Modul-Struktur stabil

---

### XL-05: Winget/Scoop-Paket

**ID:** XL-05 | **Aufwand:** XL | **Priorität:** P2 | **PRD-Ref:** FR-052

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `installer/winget-manifest.yaml` | 📁 | Winget-Manifest |
| `installer/scoop-manifest.json` | 📁 | Scoop-Manifest |

**Abhängigkeiten:** Release-Artefakt stabil

---

### XL-06: Historische Trendanalyse

**ID:** XL-06 | **Aufwand:** XL | **Priorität:** P2

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/TrendAnalysis.ps1` | 📁 | Run-History-Aggregation + Graph-Daten |
| `dev/modules/WpfSlice.ReportPreview.ps1` | ✏️ | Trend-Chart |
| `dev/tests/unit/TrendAnalysis.Tests.ps1` | 📁 | Tests |

**Abhängigkeiten:** RunIndex.ps1, 🔗 QW-14 (Run-History)

---

### XL-07: Emulator-Kompatibilitäts-Report

**ID:** XL-07 | **Aufwand:** XL | **Priorität:** P2

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/CompatReport.ps1` | 📁 | Kompatibilitätsmatrix |
| `data/compat-lists.json` | 📁 | Community-Kompatibilitätslisten |
| `dev/tests/unit/CompatReport.Tests.ps1` | 📁 | Tests |

**Abhängigkeiten:** Classification, 🔗 LF-03 (Emulator-Integration)

---

### XL-08: Sammlungs-Sharing

**ID:** XL-08 | **Aufwand:** XL | **Priorität:** P2

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/CollectionShare.ps1` | 📁 | Export als HTML/JSON (ohne ROMs!) |
| `dev/tests/unit/CollectionShare.Tests.ps1` | 📁 | Tests |

**⚠️ Achtung:** Nur Metadaten exportieren, NIEMALS Dateipfade oder Hashes die Downloads ermöglichen.

**Abhängigkeiten:** Report.ps1

---

### XL-09: GPU-beschleunigtes Hashing

**ID:** XL-09 | **Aufwand:** XL | **Priorität:** P2

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `src/RomCleanup.Hashing/GpuHasher.cs` | 📁 | OpenCL/CUDA SHA1/SHA256 |
| `dev/tests/unit/GpuHasher.Tests.cs` | 📁 | Benchmark + Korrektheitstests |

**Abhängigkeiten:** C#-Migration, GPU-fähige Hardware

---

### XL-10: USN-Journal Differential-Scan

**ID:** XL-10 | **Aufwand:** XL | **Priorität:** P2

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `src/RomCleanup.Infrastructure/UsnJournalScanner.cs` | 📁 | NTFS USN Journal Reader |
| `dev/tests/unit/UsnJournal.Tests.cs` | 📁 | Tests |

**⚠️ Achtung:** Erfordert Admin-Rechte für USN-Journal-Zugriff.

**Abhängigkeiten:** C#-Migration, Windows-only

---

### XL-11: Hardlink/Symlink-Modus

**ID:** XL-11 | **Aufwand:** XL | **Priorität:** P2

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/FileOps.ps1` | ✏️ | `New-HardLink` / `New-SymLink` Funktionen |
| `dev/modules/ConsoleSort.ps1` | ✏️ | Hardlink-Option für Sortierung |
| `dev/tests/unit/FileOps.Links.Tests.ps1` | 📁 | Tests |

**⚠️ Security:** Reparse-Point-Schutz bleibt aktiv! Nur explizit vom User erstellte Links.

**Abhängigkeiten:** FileOps.ps1

---

### XL-12: clrmamepro/RomVault-Import

**ID:** XL-12 | **Aufwand:** XL | **Priorität:** P2

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/ToolImport.ps1` | 📁 | Parser für clrmamepro/RomVault Datenbanken |
| `dev/tests/unit/ToolImport.Tests.ps1` | 📁 | Tests |

**Abhängigkeiten:** Dat.ps1

---

### XL-13: Multi-Instance-Koordination

**ID:** XL-13 | **Aufwand:** XL | **Priorität:** P2

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/InstanceSync.ps1` | 📁 | Lock-Management, State-Sync |
| `dev/tests/unit/InstanceSync.Tests.ps1` | 📁 | Tests |

**Abhängigkeiten:** Settings-Sync, File-Locking

---

### XL-14: Telemetrie (Opt-in)

**ID:** XL-14 | **Aufwand:** XL | **Priorität:** P2

**Betroffene Dateien:**

| Datei | Aktion | Änderung |
| ----- | ------ | -------- |
| `dev/modules/UiTelemetry.ps1` | ✏️ | Opt-in Upload-Mechanismus |
| `dev/modules/Settings.ps1` | ✏️ | `telemetryOptIn` Setting |
| `dev/tests/unit/Telemetry.Tests.ps1` | 📁 | Tests |

**⚠️ Privacy:** Strikt opt-in, anonymisiert, DSGVO-konform. Keine PII (Pfade, Dateinamen).

**Abhängigkeiten:** UiTelemetry.ps1, EventBus.ps1

---

## C#-Migration (Strangler-Fig)

---

### MIG-01: Core-Engine nach C#

**ID:** MIG-01 | **Aufwand:** XL | **Priorität:** P0 | **PRD-Ref:** US-019

**C#-Projekt:** `src/RomCleanup.Core/`

**Zu portierende Module:**

| PS-Modul | C#-Klasse | Test-Klasse |
| -------- | --------- | ----------- |
| `Core.ps1` | `GameKeyGenerator.cs`, `RegionScorer.cs` | `GameKeyGeneratorTests.cs` |
| `Dedupe.ps1` | `RegionDeduplicator.cs`, `WinnerSelector.cs` | `WinnerSelectorTests.cs` |
| `Classification.ps1` | `FileClassifier.cs`, `ConsoleDetector.cs` | `FileClassifierTests.cs` |
| `FormatScoring.ps1` | `FormatScorer.cs`, `VersionScorer.cs` | `FormatScorerTests.cs` |

**Vorgehen:**
1. Unit-Tests von PS nach C# portieren (1:1)
2. C#-Implementierung schreiben
3. Alle Tests grün → Feature-Parität nachgewiesen
4. PS-Entry-Points rufen C#-DLLs auf (Hybrid-Modus via `Add-Type`/Assembly-Load)

---

### MIG-02: Contracts nach C#

**ID:** MIG-02 | **Aufwand:** L | **Priorität:** P0

**C#-Projekt:** `src/RomCleanup.Contracts/`

**Zu portierende Module:**

| PS-Modul | C#-Artefakt |
| -------- | ----------- |
| `PortInterfaces.ps1` | `IFileSystem.cs`, `IToolRunner.cs`, `IDatRepository.cs`, `IAuditStore.cs`, `IAppState.cs` |
| `ErrorContracts.ps1` | `OperationError.cs`, `ErrorKind.cs` |
| `DataContracts.ps1` | `RunDedupeInput.cs`, `RunDedupeOutput.cs` etc. (Records) |
| `UseCaseContracts.ps1` | `IRunDedupeService.cs` etc. |

---

### MIG-03: Infrastructure nach C#

**ID:** MIG-03 | **Aufwand:** XL | **Priorität:** P1

**C#-Projekt:** `src/RomCleanup.Infrastructure/`

| PS-Modul | C#-Klasse |
| -------- | --------- |
| `FileOps.ps1` | `FileSystemService.cs` |
| `Tools.ps1` | `ToolRunnerService.cs` |
| `Dat.ps1` | `DatRepositoryService.cs` |
| `Logging.ps1` | `JsonlLogger.cs` |
| `LruCache.ps1` | `LruCache<TKey, TValue>.cs` |

---

### MIG-04: UI nach C#/WPF

**ID:** MIG-04 | **Aufwand:** XL | **Priorität:** P2

**C#-Projekt:** `src/RomCleanup.UI.Wpf/`

Letzter Migrationsschritt — WPF bleibt Windows-only, Avalonia optional.

---

### MIG-05: API nach ASP.NET Core

**ID:** MIG-05 | **Aufwand:** L | **Priorität:** P2

**C#-Projekt:** `src/RomCleanup.Api/`

ASP.NET Core Minimal API als Ersatz für `ApiServer.ps1`.

---

## Dependency-Graph (Feature-Reihenfolge)

```
Phase 1 (keine inter-Feature-Dependencies):
  QW-01 bis QW-16 → alle unabhängig voneinander

Phase 2 (Dependencies markiert):
  MF-14 (Parallel-Hashing) → blockt MF-01, MF-02, MF-24
  MF-03 (Header-Analyse) → enablet MF-26
  MF-08 (Queue) ↔ MF-09 (Batch-Verify)
  MF-11 (DAT-Update) → MF-12 (Diff-Viewer)
  MF-18 (Tray) → enablet MF-23 (Scheduler)
  MF-19 (Rule-Engine) → MF-20 (Pipelines)
  QW-07 (Theme) → LF-20 (Theme-Engine)
  QW-16 (RetroArch) → LF-03 (Multi-Emulator-Export)

Phase 3:
  MF-03 → LF-06 (Header-Reparatur)
  LF-01 (Covers) → LF-02 (Genre-Klassifikation) Fallback
  MF-19 → LF-19 (Rule-Sharing)

Phase 4:
  C#-Migration (MIG-01–05) → XL-01 (Docker), XL-09 (GPU-Hashing)
  QW-14 → XL-06 (Trends)
```

---

## Zusammenfassung

| Phase | Features | Neue Module | Neue Tests | Geschätzter Aufwand |
| ----- | -------- | ----------- | ---------- | ------------------- |
| Phase 1 (Quick Wins) | 16 | 2 (retroarch-cores.json, Themes) | 16 | ~20 Tage |
| Phase 2 (Medium) | 26 | 10 (HeaderAnalysis, ConvertQueue, RuleEngine, etc.) | 26 | ~100 Tage |
| Phase 3 (Large) | 20 | 12 (CoverScraper, PatchEngine, ArcadeSets, etc.) | 20 | ~200 Tage |
| Phase 4 (XL) | 14 | 6 (Docker, Web, ShellExt, etc.) | 14 | ~300+ Tage |
| C#-Migration | 5 | 5 C#-Projekte | 5 C#-Test-Projekte | ~200 Tage |
| **Gesamt** | **81** | **~35** | **~81** | — |

---

*Erstellt: 2026-03-09 | Basierend auf PRD v1.0 und FEATURE_ROADMAP.md*
