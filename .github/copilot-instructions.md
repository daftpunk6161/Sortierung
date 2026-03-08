# RomCleanup – Copilot Instructions

## Projektübersicht

PowerShell 5.1+ Tool zur ROM-Sammlungsverwaltung: regionsbasierte Deduplizierung, Entrümpelung (Demos/Betas/Hacks), Formatkonvertierung (CUE/BIN→CHD, ISO→RVZ etc.), DAT-basierte Verifizierung und Konsolen-Sortierung. Drei unabhängige Entry Points: **GUI** (WPF/XAML), **CLI** (headless/CI), **REST API** (loopback).

**Plattform:** Windows 10/11, PowerShell 5.1+ (Windows PowerShell), .NET Framework 4.5+. Dual-Runtime: PS7 wird erkannt via `Test-PowerShell7Available` in `Compatibility.ps1` und erlaubt PS7-exklusive Konstrukte (paralleles `ForEach-Object`, ternäre Operatoren). Kein Cross-Platform-Support in v1.x.

---

## Befehle

### Linting (PSScriptAnalyzer)
```powershell
Invoke-ScriptAnalyzer -Path ./dev/modules/*.ps1 -Settings ./PSScriptAnalyzerSettings.psd1
```

**Aktive Regeln:**
- `PSUseDeclaredVarsMoreThanAssignments`
- `PSUseApprovedVerbs`
- `PSAvoidUsingInvokeExpression`
- `PSAvoidGlobalVars`

**Explizit deaktiviert** (bewusste Ausnahmen):
- `PSAvoidUsingPlainTextForPassword` — API-Key-Handling
- `PSAvoidUsingWriteHost` — CLI-Ausgaben
- `PSUseShouldProcessForStateChangingFunctions` — interne State-Mutations

### Tests (Pester v5)
```powershell
# Vollständige Pipeline (unit + integration + e2e + coverage + governance + mutation + benchmark)
pwsh -NoProfile -File ./dev/tools/pipeline/Invoke-TestPipeline.ps1 -Stage all

# Einzelne Stufe
pwsh -NoProfile -File ./dev/tools/pipeline/Invoke-TestPipeline.ps1 -Stage unit
pwsh -NoProfile -File ./dev/tools/pipeline/Invoke-TestPipeline.ps1 -Stage integration
pwsh -NoProfile -File ./dev/tools/pipeline/Invoke-TestPipeline.ps1 -Stage e2e

# Mit Coverage-Gate (Minimum 50%)
pwsh -NoProfile -File ./dev/tools/pipeline/Invoke-TestPipeline.ps1 -Stage unit -Coverage -CoverageTarget 50

# Einzelne Testdatei direkt (schnellster Weg)
Invoke-Pester -Path ./dev/tests/unit/Core.Tests.ps1 -Output Detailed

# Mit Flaky-Retry-Support
pwsh -NoProfile -File ./dev/tools/pipeline/Invoke-TestPipeline.ps1 -Stage unit -FlakyRetries 2
```

### Entry Points starten
```powershell
# GUI (WPF, STA-Thread erforderlich)
pwsh -NoProfile -File ./simple_sort.ps1 -GUI

# CLI – DryRun (gibt JSON-Summary aus, verschiebt nichts)
pwsh -NoProfile -File ./Invoke-RomCleanup.ps1 -Roots "D:\Roms" -Mode DryRun

# CLI – Move-Modus
pwsh -NoProfile -File ./Invoke-RomCleanup.ps1 -Roots "D:\Roms" -Mode Move -PreferRegions EU,US

# REST API (127.0.0.1 only, API-Key via Header)
pwsh -NoProfile -File ./Invoke-RomCleanupApi.ps1 -ApiKey "MeinKey" -Port 8080
```

---

## Architektur

Geschichtete Clean Architecture (Ports & Adapters). Schichten dürfen **nur in Pfeilrichtung** kommunizieren – nie umgekehrt:

```
Entry Points
  simple_sort.ps1 | Invoke-RomCleanup.ps1 | Invoke-RomCleanupApi.ps1
        ↓
UI Layer          WpfHost, WpfMainViewModel, WpfEventHandlers, WpfSlice.*.ps1 (5 Slices)
        ↓
App/Orchestration ApplicationServices.ps1, OperationAdapters.ps1, RunHelpers*.ps1 (3 Dateien)
        ↓
Core Engine       Core.ps1, Dedupe.ps1, Classification.ps1, Sets.ps1, FormatScoring.ps1
        ↓
Port Interfaces   PortInterfaces.ps1  ← Abstraktion für alle I/O-Grenzen
        ↓
Infrastructure    FileOps.ps1, Tools.ps1, Dat.ps1, Report.ps1, Logging.ps1, DatSources.ps1
```

### Modullade-Reihenfolge (RomCleanupLoader.ps1 – nicht manuell überspringen)

```
 1. Settings        – Konfiguration, kein Dep
 2. Tools           – Externe Tool-Wrapper (braucht Settings)
 3. FileOps         – FS-Operationen (braucht Settings)
 4. Report          – HTML/CSV-Ausgabe (kein Runtime-Dep)
 5. FormatScoring   – Format/Region-Scoring (kein Dep)
 6. SetParsing      – CUE/GDI/CCD/M3U-Parsing (kein Dep)
 7. Ps3Dedupe       – [DEAD STUB – nur Kompatibilität]
 8. ZipSort         – ZIP-Sort-Helpers (braucht Tools)
 9. Sets            – SetItem/FileItem-Builder (braucht SetParsing)
10. Core            – Region, GameKey, Winner (kein Dep)
11. Classification  – Datei-/Konsolen-Klassifikation (braucht Core, Tools)
12. Convert         – Formatkonvertierung (braucht Tools, Settings)
13. Dedupe          – Region-Dedupe-Engine (braucht Core, Sets, Classification, FormatScoring)
14. Dat             – DAT-Index/Hash (braucht Tools, Core)
15. DatSources      – DAT-Download/Install (braucht Dat)
16. RunHelpers      – Orchestrierung (braucht viele Module)
17. ConsoleSort     – Konsolen-Sort (braucht Classification, FileOps)
18. Logging         – JSON/Text-Logging (braucht Settings, FileOps)
19–22. WPF-Module   – 14 Dateien, nur im GUI-Modus
```

### Port-Interfaces (PortInterfaces.ps1) – exakte Contracts

| Port | Pflicht-Member |
|------|----------------|
| `FileSystem` | `TestPath`, `EnsureDirectory`, `GetFilesSafe`, `MoveItemSafely`, `ResolveChildPathWithinRoot` |
| `ToolRunner` | `FindTool`, `InvokeProcess`, `Invoke7z` |
| `DatRepository` | `GetDatIndex`, `GetDatGameKey`, `GetDatParentCloneIndex`, `ResolveParentName` |
| `AuditStore` | `WriteMetadataSidecar`, `TestMetadataSidecar`, `Rollback` |
| `AppState` | `Get`, `Set`, `Watch`, `Undo`, `Redo`, `GetValue`, `SetValue`, `TestCancel` |
| `RegionDedupe` | `Invoke` (Hashtable-Parameter) |

Neue Operationen **müssen** über Port-Interfaces laufen, nicht direkt über Modul-Funktionen. Ports mit `Test-PortContract` vor der Nutzung validieren.

### Zentrale Datendateien (`data/`)

| Datei | Rolle |
|-------|-------|
| `consoles.json` | Single Source of Truth für 100+ Konsolen: Key, DisplayName, `discBased`, `uniqueExts`, `folderAliases` |
| `rules.json` | Regions-Patterns, Junk-Tags, Version/Revision-Erkennung |
| `dat-catalog.json` | DAT-Quellen (Redump, No-Intro, FBNEO) mit URLs |
| `defaults.json` | Standard-Einstellungen: Mode=DryRun, Extensions, Theme, Locale |
| `tool-hashes.json` | SHA256-Allowlist für externe Tools (chdman, 7z, dolphintool) |
| `schemas/*.json` | JSON-Schema v7 für Plugin-Manifests, Settings, Rules |

User-Settings: `%APPDATA%\RomCleanupRegionDedupe\settings.json`

```jsonc
{
  "general": {
    "logLevel": "Info",            // Debug|Info|Warning|Error
    "preferredRegions": ["EU","US","JP"],
    "aggressiveJunk": false,
    "aliasEditionKeying": false
  },
  "toolPaths": { "chdman": "", "7z": "", "dolphintool": "" },
  "dat": {
    "useDat": true,
    "datRoot": "",
    "hashType": "SHA1",            // SHA1|SHA256|MD5
    "datFallback": true
  }
}
```

Validierung via `Get-RomCleanupSchema -Name 'settings-v1'` + `Test-JsonPayloadSchema`. Settings-Migration bei Version-Upgrades: `Invoke-SettingsMigration` in `Settings.ps1`.

---

## Schlüssel-Konventionen

### Code-Struktur

- **Funktionen:** `Verb-Noun` (`Invoke-RegionDedupe`, `Get-FilesInRoot`, `New-OperationError`, `Select-Winner`)
- **Modul-State:** Ausschließlich `$script:` Scope (`$script:AppState`, `$script:RuleData`, `$script:LRUCache`)
- **Parameter:** PascalCase; Mode-Parameter immer mit `[ValidateSet('DryRun','Move')]`
- **Core-Logik muss pure sein:** Keine UI-Aufrufe, keine `$script:`-Globals, keine Side-Effects in `Core.ps1`, `Dedupe.ps1`, `Classification.ps1`, `FormatScoring.ps1` — Grundvoraussetzung für Unit-Tests
- **Inline-C#:** Nur für Performance-kritische Einzel-Typen erlaubt (z.B. CRC32 in `Dat.ps1`, INotifyPropertyChanged in `WpfMainViewModel.ps1`, WPF-Shims in `WpfShims.ps1`). Kein vollständiger C#-Code außerhalb dieser Ausnahmen.

### Fehlerbehandlung

- `$ErrorActionPreference = 'Stop'` global; alle Ausnahmen sind Terminierungsfehler
- **Drei Fehlerklassen** (`ErrorContracts.ps1`):
  - `Transient` → automatischer Retry
  - `Recoverable` → loggen und fortfahren
  - `Critical` → sofort abbrechen, Benutzer informieren
- Strukturierte Fehlerobjekte via `New-OperationError` / `ConvertTo-OperationError` — niemals rohe Strings werfen
- **Fehler-Code-Namespaces:** `GUI-*`, `DAT-*`, `IO-*`, `SEC-*`, `RUN-*`
- **TD-002 CatchGuard-Compliance:** `Invoke-CatchGuard` / `Invoke-SafeCatch` verwenden. Silent-Catch (leerer catch-Block) ist **nur** in WPF-Event-Handlern erlaubt (verhindert UI-Thread-Absturz). Alle anderen Catch-Blöcke müssen loggen oder re-throwen. CI-Gate in `test-pipeline.yml` prüft dies automatisch.

### Sicherheitsregeln (nicht verhandelbar)

- **Kein direktes Löschen** — Standard ist `Move-ItemSafely` in Trash + Audit-Log. Echtes Delete braucht explizite Bestätigung + Danger-Zone-UI
- **Path-Traversal-Schutz** — `Resolve-ChildPathWithinRoot` vor jedem Move/Copy/Delete aufrufen; schlägt fehl wenn außerhalb der Root
- **Zip-Slip** — Archiv-Entry-Pfade vor Extraktion gegen Root validieren
- **Reparse Points** (Symlinks/Junctions) — explizit blockieren oder definiert behandeln, niemals transparent folgen
- **CSV-Injection** verhindern: keine führenden `=`, `+`, `-`, `@` in Felder
- **HTML-Encoding** in allen Report-Outputs konsequent (`[System.Web.HttpUtility]::HtmlEncode`)
- **Tool-Hash-Verifizierung:** SHA256-Checksums aus `data/tool-hashes.json` vor jedem externen Tool-Aufruf prüfen; Bypass nur via `AllowInsecureToolHashBypass`

### GameKey & Scoring-System

**`ConvertTo-GameKey`-Algorithmus** (LRU-Cache: 50k Einträge):
1. ASCII-Fold (Diakritika: ß→ss, é→e etc.)
2. 25 Regions-Tag-Patterns anwenden (aus `rules.json`)
3. Version/Revision-Tags entfernen
4. Junk-Tags entfernen (Alpha, Beta, Demo, Homebrew, Aftermarket …)
5. Whitespace normalisieren
6. ALWAYS-Alias-Map anwenden (immer)
7. Optional: Edition-Keying-Alias-Map (`aliasEditionKeying`)
8. Ergebnis in LRU-Cache schreiben

**`Get-FormatScore`-Werte** (höher = besser):
| Format | Score |
|--------|-------|
| CHD | 850 |
| ISO | 700 |
| ZIP | 500 |
| 7Z | 480 |
| RAR | 400 |

**Winner-Selection** (`Select-Winner` in `Dedupe.ps1`) – deterministisch, gleiche Inputs = gleicher Output:
1. Kategorie filtern (GAME vs. JUNK vs. BIOS)
2. Regions-Score: bevorzugte Region aus `preferredRegions` = 1000−N
3. Format-Score
4. Versions-Score: Verified `[!]` = +500; Revision a-z = 10×Ordinalwert; Version v1.0 = numerisch
5. Größen-Tiebreak: Disc-Spiele → größer bevorzugt; Cartridge → kleiner bevorzugt

### DAT-Verifizierung (Dat.ps1, DatSources.ps1)

- LRU-Cache für Datei-Hashes (20k Einträge), Archiv-Hash-Extraktion via 7z (Skip bei >500 MB)
- Parent/Clone-Mapping-Auflösung
- DAT-Index-Fingerprinting per `consoleKey + hashType + datRoot`
- XXE-Schutz beim XML-Parsing aktiv (`Dat.ps1`)
- DAT-Download mit SHA256-Sidecar-Verifizierung (`*.sha256`-Datei von Download-URL)
- Quellen: No-Intro, Redump, FBNEO (plugin-basiert via `plugins/dat-sources/*.json`)

### Konvertierungs-Pipeline (Convert.ps1, Tools.ps1)

| Konsole | Zielformat | Tool |
|---------|-----------|------|
| PS1, Saturn, Dreamcast | CHD | `chdman createcd` |
| PS2 | CHD | `chdman createdvd` |
| GameCube, Wii | RVZ | `dolphintool` |
| PSP (PBP) | CHD | `psxtract` |
| NES, SNES etc. | ZIP | `7z` |

**`Invoke-ExternalToolProcess`:** Start-Process in Temp-File, Wait-Process, Exit-Code-Prüfung, automatisches Cleanup. Timeout und Retry konfigurierbar. Alle Tool-Pfade aus `toolPaths` in Settings, niemals hardcoded.

### REST API (Invoke-RomCleanupApi.ps1)

**Authentifizierung:** HTTP-Header `ROM_CLEANUP_API_KEY` — Wert aus Env-Variable oder `-ApiKey`-Parameter.

**Endpunkte:**
| Methode | Pfad | Zweck |
|---------|------|-------|
| `GET` | `/health` | Health-Check |
| `GET` | `/openapi` | OpenAPI-Spec |
| `POST` | `/runs` | Run erstellen (`{roots:[], mode:"DryRun"\|"Move"}`) |
| `GET` | `/runs/{id}` | Run-Status abfragen |
| `GET` | `/runs/{id}/result` | Vollständiges Ergebnis |
| `POST` | `/runs/{id}/cancel` | Run abbrechen |
| `GET` | `/runs/{id}/stream` | SSE-Fortschrittsstream |

**CORS-Modi:** `custom` / `local-dev` / `strict-local` via `-CorsAllowOrigin` (Default `*`).
**Rate Limiting:** 120 Requests/Minute.
**HTTPS/TLS:** Konfigurierbar; API ist ausschließlich auf 127.0.0.1 gebunden.
**WebSocket:** `Start-ApiWebSocketSession` in `ApiServer.ps1` für Live-Progress-Streaming.

**Response-Shape (Run-Status):**
```json
{ "Status": "ok|blocked|skipped|failed", "ExitCode": 0, "Preflight": {"valid": true, "warnings": []}, "ReportPath": "…", "AuditPath": "…" }
```

**CLI Exit Codes:** `0` = Erfolg, `1` = Fehler, `2` = Abgebrochen, `3` = Preflight fehlgeschlagen.

### Logging (Logging.ps1)

- Format: strukturiertes **JSONL** (eine JSON-Zeile pro Eintrag)
- Felder: `module`, `level`, `correlationId` (128-bit GUID, im AppState gespeichert), `phase`, `metrics`
- Log-Levels (numerisch): `Debug=10`, `Info=20`, `Warning=30`, `Error=40`
- Rotation via `Invoke-JsonlLogRotation` (max. Bytes, Anzahl Keep-Files, optional GZIP)
- Phase-Metriken: `Write-PhaseMetricsToOperationLog` am Ende jeder Run-Phase aufrufen
- LRU-Cache-Statistiken werden automatisch in den Log-Output integriert

### Plugins (`plugins/`)

**Drei Typen:**

1. **Operation-Plugins** (`*.operation-plugin.ps1`):
   ```powershell
   function Invoke-RomCleanupOperationPlugin {
       param([string]$Phase, [hashtable]$Context)
       # Phase: pre-scan | post-dedupe | post-convert | post-run
   }
   ```
2. **Report-Plugins** (`*.report-plugin.ps1`): benutzerdefinierte Report-Generatoren (XML/JSON)
3. **Console-Plugins** (`*.console-plugin.json`): JSON-Konsolen-Definitionen, überschreiben `consoles.json`

**Trust-Modi** (Env-Variable `ROMCLEANUP_PLUGIN_TRUST_MODE`):
- `compat` — Rückwärtskompatibel (Standard)
- `trusted-only` — Nur Plugins mit `"trusted": true` im Manifest
- `signed-only` — Nur kryptographisch signierte Plugins

Manifests werden gegen `data/schemas/plugin-manifest.schema.json` (JSON Schema v7) validiert. Plugin-Discovery via `Get-ChildItem -Filter *.operation-plugin.ps1` in `OperationAdapters.ps1`.

### Output-Formate

- **Audit-CSV** — SHA256-signiert; Spalten: `RootPath, OldPath, NewPath, Action, Category, Hash, Reason, Timestamp`
- **HTML-Report** — mit Meta-Tags, CSP-Header, Diagrammen (Keep/Move/Junk nach Konsole)
- **JSON-Summary** — `{ Status, ExitCode, Preflight: {valid, warnings}, ReportPath, AuditPath }`
- **JSONL-Logs** — strukturiert mit Correlation-ID und Phase-Metriken

### GUI (WPF/XAML)

Alle WPF-Module in `dev/modules/Wpf*.ps1`:
- `WpfHost.ps1` — WPF-Assembly-Laden, Window-Hosting (`Show-RomCleanupGui`)
- `WpfMainViewModel.ps1` — ViewModel mit Undo/Redo, Inline-C# für `INotifyPropertyChanged`
- `WpfEventHandlers.ps1` — ~1700 Zeilen, alle Event-Bindings (`Register-WpfEventHandlers`)
- `WpfSlice.Roots.ps1`, `WpfSlice.Settings.ps1`, `WpfSlice.RunControl.ps1`, `WpfSlice.DatMapping.ps1`, `WpfSlice.AdvancedFeatures.ps1` — segmentierte UI-Logik
- `WpfShims.ps1` — C#-Helper-Typen für WPF in PS 5.1

**GUI-Pflichten:**
- Standard-Flow: **DryRun/Preview → explizite Summary-Anzeige → Bestätigung → Move/Apply**
- Lange Tasks **off-UI-thread**; UI-Updates ausschließlich via `Dispatcher.Invoke`
- Kein `DoEvents`-Pattern
- Farben/Styles zentral im `ResourceDictionary` (retro-modern Dark + Neon Accent)
- Zwei Modi: `rbModeEinfach` (4 Entscheidungen) und `rbModeExperte` (volle Kontrolle)
- UI-Elemente: Preflight-Ampel (`dotReady`), ETA, Undo-Button (Puls-Animation), Drag-Drop

---

## Tests

Tests in `dev/tests/`: ~80 Dateien aufgeteilt in `unit/` (~40), `integration/` (~2), `e2e/` (~1), plus Root-Level-Tests (~45).

**Benennung:** `<Modul>.<Bereich>.Tests.ps1` (z.B. `Dat.IndexCache.Tests.ps1`, `ConsoleDetection.DolphinGcWii.Tests.ps1`)

**Pflicht-Testarten:**
- **Unit:** Region-Parsing, GameKey-Konstruktion, FormatScore/VersionScore, Winner-Selection, Sanitizer
- **Integration:** echte TempDirs, Move→Trash, Report-Output, Archiv-Handling (ZIP/7Z), DAT-Index/Hits
- **Regression:** reale Problemfälle als Fixtures (beschädigte Archive, fehlende CUE-Tracks, Sonderzeichen)
- **Negativ/Edge:** Path-Traversal-Versuche, Reparse-Points, leere Archive, >500 MB Dateien

**Keine Alibi-Tests:** Jeder Test muss einen echten Fehler finden können. Invarianten prüfen:
- Winner-Selection ist deterministisch (gleiche Inputs = gleicher Winner)
- Kein Move außerhalb der Root
- Keine leeren Keys für Gruppierung

`BeforeAll`/`AfterAll` für TempDir-Setup/Teardown in Integration-Tests verwenden.

---

## CI/CD (`.github/workflows/`)

### test-pipeline.yml
Trigger: Push/PR auf `dev/**`, `.github/**`

| Job | Gate | Bei Fehler |
|-----|------|-----------|
| Unit-Tests + Coverage | 50% Minimum | Fail |
| PSScriptAnalyzer | Warning + Error | Fail |
| Governance | Modul-Grenzen, Komplexität | Warn only |
| Catch-Compliance (TD-002) | Keine Silent-Catches außer WPF | Fail |
| Mutation-Testing | Reporting only | continue-on-error |
| Benchmark-Gate | Regression-Erkennung | continue-on-error |

Artifacts: `test-pipeline-*.json`, `mutation-reports-*.json`

### release.yml
Trigger: `workflow_dispatch` oder Tag `v*`
Artifact: `rom-cleanup-v*.zip` mit `simple_sort.ps1` + `dev/`

---

## Migration von PowerShell nach C#/.NET 8

> Laut `ANALYSE.md` (Abschnitt 5.1 + Roadmap): PowerShell ist langfristig limitierend (keine Cross-Platform-GUI, kein natives Async). Die offizielle Migrations-Roadmap:
> - **v1.x** (aktuell): PowerShell-Architektur bereinigen und stabilisieren
> - **v2.0** (Ziel Q1 2027): Migration zu C# .NET 8 + Avalonia (Cross-Platform)

### Migrations-Strategie ("Strangler Fig")

Die bestehende Port/Adapter-Architektur ist **bewusst** auf Migration ausgelegt:

1. **Core-Engine zuerst** — `Core.ps1`, `Dedupe.ps1`, `Classification.ps1`, `FormatScoring.ps1` sind pure functions ohne UI/IO-Abhängigkeiten → direkt 1:1 nach C# portierbar
2. **Port-Interfaces** → werden zu C# `interface`-Definitionen
3. **Infrastructure** (FileOps, Tools, Dat) → C#-Implementierungen der Port-Interfaces
4. **UI zuletzt** → WPF bleibt (Windows-only) oder Avalonia für Cross-Platform

### Zielstruktur v2.0
```
RomCleanup.Core/           // C# – pure domain logic (GameKey, Scoring, Dedupe)
RomCleanup.Contracts/      // C# – Port-Interfaces, ErrorContracts, DTOs
RomCleanup.Infrastructure/ // C# – FileOps, Tools, Dat, Logging
RomCleanup.UI.Wpf/         // C# – WPF-only (Windows)
RomCleanup.UI.Avalonia/    // C# – Cross-Platform (optional)
RomCleanup.CLI/            // C# – headless entry point
RomCleanup.Api/            // C# – ASP.NET Core minimal API
```

### Was bei PS-Entwicklung jetzt beachten (Migration-Readiness)

- **Keine neuen `$script:`-Globals** — alle neuen State-Zugriffe über `PortInterfaces.ps1` (`GetValue`/`SetValue`/`TestCancel`)
- **Inline-C# nicht erweitern** — bestehende Inline-C#-Blöcke sind bereits Migrations-Kandidaten; keine neuen Inline-C#-Klassen hinzufügen
- **Core-Logik pure halten** — jede neue Funktion in Core/Dedupe/Classification ohne UI/IO-Dep implementieren
- **Kein direkter WPF-Code außerhalb der Wpf*-Module** — nie WPF-Typen in Core oder Infrastructure referenzieren
- **Alle neuen Datenstrukturen als Hashtable-Contracts** definieren (spätere Überführung in C#-Records/DTOs)
- **Tests sind die Migrations-Sicherung** — gut getestete Funktionen können sicher nach C# portiert werden

### Bekannte technische Schulden (für Migration vorbereiten)

| ID | Bereich | Problem | Migrations-Relevanz |
|----|---------|---------|---------------------|
| DEAD-001 | `Ps3Dedupe.ps1` | Leerer Stub, nur Kompatibilität | Beim Port weglassen |
| DUP-006/008 | `Tools.ps1` | 4 Process-Runner-Implementierungen | Vor Port konsolidieren |
| DUP-100 | `WpfMainViewModel` | `Set-WpfViewModelProperty` 2× definiert | Ladereihenfolge-Risiko |
| DEAD-211 | `AppState` | Start/Stop-AppStorePersistence nie aufgerufen | Auto-Save-Feature defekt |
| MISS-001 | `Report.ps1` | CSP-Header fehlt in HTML-Reports | Security-Lücke, vor Port fixen |
