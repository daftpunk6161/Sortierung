# ROM Cleanup & Region Dedupe

> PowerShell 5.1 WinForms-Tool zum Aufräumen, Deduplizieren und Konvertieren von ROM-Sammlungen.

---

## Features

| Feature | Beschreibung |
|---|---|
| **Region Dedupe** | Behält pro Spiel die beste ROM-Variante (Region, Version, Format, Größe). Priorität konfigurierbar (z.B. EU > US > JP). |
| **Junk-Entfernung** | Entfernt Demos, Betas, Protos, Software, Bad Dumps, Trainer, Hacks etc. automatisch. |
| **BIOS-Trennung** | Sortiert BIOS/Firmware-Dateien optional in separaten Ordner. |
| **Format-Konvertierung** | CUE/BIN → CHD, ISO → CHD/RVZ, CSO → ISO, PBP → CHD (via chdman, DolphinTool, psxtract, ciso). Parallele Konvertierung mit Runspace-Pool. |
| **DAT-Matching** | Verifiziert ROMs gegen No-Intro, Redump, FBNEO, MAME DATs (SHA1/MD5/CRC32). |
| **1G1R-Modus** | One Game One ROM via Parent/Clone-Listen aus DAT-Dateien. |
| **Konsolen-Sortierung** | Sortiert Dateien automatisch nach Konsolen-Ordner (Erkennung via Extension, Ordnername, Disc-Header, DAT). |
| **PS3 Dedupe** | Duplikat-Erkennung für PS3-Ordnerstrukturen via Content-Hashing. |
| **DOS → ZIP** | Packt DOS-Spieleordner einzeln als ZIP. |
| **Audit & Rollback** | Signierte Audit-CSVs mit vollständigem Rollback-Support. |
| **DryRun** | Vorschau aller Aktionen ohne Dateien zu verschieben — CSV/HTML/JSON Reports. |
| **Dark Mode** | Automatische Erkennung + manueller Toggle. |

---

## Systemanforderungen

- **Windows** 10/11 (oder Windows Server 2016+)
- **PowerShell 5.1** (vorinstalliert auf Windows 10+)
- **.NET Framework 4.5+** (für WinForms)
- **Optionale Tools** (für Konvertierung):
  - [chdman](https://www.mamedev.org/) — CHD-Konvertierung
  - [DolphinTool](https://dolphin-emu.org/) — RVZ-Konvertierung
  - [7-Zip](https://www.7-zip.org/) — Archiv-Handling
  - [psxtract](https://github.com/Starter-01/psxtract) — PBP → ISO
  - [ciso (Source)](https://github.com/jamie/ciso) — CSO → ISO (GitHub-Releases liefern i.d.R. nur Quellcode; für Windows eigenes ciso.exe bereitstellen/kompilieren)

---

## Quick Start

```powershell
# Rechtsklick → "Mit PowerShell ausführen" oder:
powershell -ExecutionPolicy Bypass -File .\simple_sort.ps1
```

1. **ROM-Ordner hinzufügen** — Drag & Drop, `Ordner hinzufügen`-Button oder `Ctrl+V`.
2. **Modus wählen** — `Nur prüfen` (DryRun) für Vorschau, `Verschieben` für echte Änderungen.
3. **Dedupe starten** — Erster Lauf immer als DryRun empfohlen.
4. **Report prüfen** — HTML/CSV-Report zeigt alle geplanten/durchgeführten Aktionen.

> **Tipp:** Konvertierung, Sortierung, DAT-Matching und weitere Features sind in separaten Tabs konfigurierbar.

---

## CLI fuer Automation (JSON Output)

Das Headless-Skript `Invoke-RomCleanup.ps1` unterstuetzt jetzt maschinenlesbare Ergebnisse fuer externe Toolchains:

```powershell
pwsh -NoProfile -File .\Invoke-RomCleanup.ps1 `
  -Roots 'D:\ROMs\SNES' `
  -Mode DryRun `
  -EmitJsonSummary `
  -SummaryJsonPath .\reports\cli-summary.json
```

- `-EmitJsonSummary`: schreibt ein JSON-Ergebnis auf stdout.
- `-SummaryJsonPath`: schreibt dasselbe JSON in eine Datei.
- JSON-Schema: `romcleanup-cli-result-v1` mit Status, ExitCode, Preflight, RunErrors und Report-Pfaden.

---

## REST API (lokal, API-Key)

Der API-Server ist lokal auf Loopback begrenzt und nutzt Header-Auth via `X-Api-Key`.

```powershell
$env:ROM_CLEANUP_API_KEY = 'change-me'
pwsh -NoProfile -File .\Invoke-RomCleanupApi.ps1 -Port 7878 -CorsMode strict-local
```

### Endpunkte (MVP)

- `GET /health`
- `POST /runs`
- `GET /runs/{runId}`
- `GET /runs/{runId}/result`
- `POST /runs/{runId}/cancel`

### API-Sicherheitsoptionen

- `-CorsMode custom|local-dev|strict-local`
  - `custom`: verwendet `-CorsAllowOrigin`
  - `local-dev`: erlaubt `*`
  - `strict-local`: erzwingt `http://127.0.0.1`

### Plugin-Trust-Modus (Operation-Plugins)

- `ROMCLEANUP_PLUGIN_TRUST_MODE=compat|trusted-only|signed-only`
  - `compat`: rückwärtskompatibel (Standard)
  - `trusted-only`: nur Plugins mit `manifest.trusted=true`
  - `signed-only`: nur gültig signierte Plugins

### Beispiel: Run starten

```powershell
$headers = @{ 'X-Api-Key' = 'change-me' }
$body = @{
  mode = 'DryRun'
  roots = @('D:\ROMs\SNES')
  useDat = $false
  notifyAfterRun = $true
} | ConvertTo-Json

Invoke-RestMethod -Uri 'http://127.0.0.1:7878/runs' -Method Post -Headers $headers -Body $body -ContentType 'application/json'
```

### Beispiel: synchron warten

```powershell
Invoke-RestMethod -Uri 'http://127.0.0.1:7878/runs?wait=true' -Method Post -Headers $headers -Body $body -ContentType 'application/json'
```

---

## Projektstruktur

```
simple_sort.ps1              # Hauptskript (Entry Point + GUI)
dev/
  modules/
    Core.ps1                 # Regelwerk: Region, GameKey, Scoring, Winner
    Dedupe.ps1               # Dedupe-Pipeline
    Convert.ps1              # Format-Konvertierung (CHD, RVZ, CSO, PBP)
    Dat.ps1                  # DAT-Parsing, XML, Hash-Matching
    DatSources.ps1           # DAT-Katalog & Download (Redump, No-Intro)
    WpfShims.ps1             # WPF-Typen/Binding-Hilfen
    WpfSelectionConfig.ps1   # Statische Auswahl-Maps für Advanced-Optionen
    WpfXaml.ps1              # WPF-XAML-Definition
    WpfHost.ps1              # WPF-Window-Host/Context-Aufbau
    WpfEventHandlers.ps1     # WPF Event-Wiring + Settings/Profile
    SimpleSort.WpfMain.ps1   # WPF-Startpunkt (Start-WpfGui)
    RunHelpers.ps1           # Reports, Move-Phase, Audit, Error-Tracking
    Tools.ps1                # Externe Tools, Archive, Wait-ProcessResponsive
    Report.ps1               # CSV/HTML Report-Generierung
    Sets.ps1                 # Set-Item-Konstruktoren (CUE, M3U, GDI, CCD)
    Settings.ps1             # User-Settings Persistenz (JSON)
  tests/
    unit/                    # Unit-Tests (Pester)
    integration/             # Integrations-Tests
    e2e/                     # End-to-End-Tests
  tools/
    pipeline/                # Test-Pipeline (Invoke-TestPipeline.ps1)
```

---

## Architektur

```
┌──────────────────────────────────────┐
│  UI Boundary                         │
│  simple_sort.ps1, Wpf*.ps1            │
│  → User-Interaktion, Controls, Log   │
├──────────────────────────────────────┤
│  Engine Boundary                     │
│  Core.ps1, Dedupe.ps1, Convert.ps1   │
│  → Region-Parsing, Scoring, Dedupe   │
│  → Deterministische Logik, kein GUI  │
├──────────────────────────────────────┤
│  IO / Security Boundary              │
│  Tools.ps1, Report.ps1, RunHelpers   │
│  → Dateisystem, Prozesse, Safety     │
│  → Root-bound, Reparse-Schutz        │
└──────────────────────────────────────┘
```

**Sicherheitsfeatures:** Root-bound Moves, Reparse-Point-Blocking, Zip-Slip Pre/Post-Check, CSV-Injection-Neutralisierung, Verify-before-Delete, Audit-Signatur.

---

## Tests ausführen

```powershell
# Alle Tests
pwsh -NoProfile -File dev/tools/pipeline/Invoke-TestPipeline.ps1 -Stage all

# Nur Unit-Tests
pwsh -NoProfile -File dev/tools/pipeline/Invoke-TestPipeline.ps1 -Stage unit

# Nur Integration
pwsh -NoProfile -File dev/tools/pipeline/Invoke-TestPipeline.ps1 -Stage integration

# Nur E2E
pwsh -NoProfile -File dev/tools/pipeline/Invoke-TestPipeline.ps1 -Stage e2e
```

---

## Konfiguration

Settings werden automatisch gespeichert unter:  
`%APPDATA%\RomCleanupRegionDedupe\settings.json`

Portable Configs können über **Tab 7 → Config Export/Import** als JSON-Datei geteilt werden.

---

## Lizenz

Privates Projekt — keine öffentliche Lizenz.
