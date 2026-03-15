# RomCleanup – Copilot Instructions

## Projektübersicht

C# .NET 10 Tool zur ROM-Sammlungsverwaltung: regionsbasierte Deduplizierung, Entrümpelung (Demos/Betas/Hacks), Formatkonvertierung (CUE/BIN→CHD, ISO→RVZ etc.), DAT-basierte Verifizierung und Konsolen-Sortierung. Drei unabhängige Entry Points: **GUI** (WPF/XAML), **CLI** (headless/CI), **REST API** (ASP.NET Core Minimal API).

**Plattform:** Windows 10/11, .NET 10 (net10.0 / net10.0-windows für WPF), LangVersion 14.

> **Migration abgeschlossen:** Die ursprüngliche PowerShell-Version liegt archiviert in `archive/powershell/` als Referenz. Aktive Entwicklung ausschließlich in `src/`.

---

## Befehle

### Build
```bash
dotnet build src/RomCleanup.sln
```

### Tests (xUnit, 3090+ Tests)
```bash
# Alle Tests
dotnet test src/RomCleanup.sln

# Einzelnes Testprojekt
dotnet test src/RomCleanup.Tests/RomCleanup.Tests.csproj

# Mit Filter
dotnet test src/RomCleanup.sln --filter "FullyQualifiedName~GameKey"
```

### Entry Points starten
```bash
# CLI – DryRun
dotnet run --project src/RomCleanup.CLI -- --roots "D:\Roms" --mode DryRun

# CLI – Move-Modus
dotnet run --project src/RomCleanup.CLI -- --roots "D:\Roms" --mode Move --regions EU,US

# REST API (127.0.0.1 only)
dotnet run --project src/RomCleanup.Api

# GUI (WPF)
dotnet run --project src/RomCleanup.UI.Wpf
```

---

## Architektur

Clean Architecture (Ports & Adapters) als .NET Solution mit 7 Projekten:

```
src/
├── RomCleanup.Contracts/      ← Port-Interfaces, Models, Error-Contracts
├── RomCleanup.Core/           ← Pure Domain Logic (keine I/O-Deps)
│   ├── Caching/               LruCache<TKey,TValue>
│   ├── Classification/        ConsoleDetector, FileClassifier, ExtensionNormalizer
│   ├── Deduplication/         DeduplicationEngine (Winner-Selection)
│   ├── GameKeys/              GameKeyNormalizer (Tag-Parsing, Region-Scoring)
│   ├── Regions/               RegionDetector
│   ├── Rules/                 RuleEngine
│   ├── Scoring/               FormatScorer, VersionScorer
│   └── SetParsing/            CueSetParser, GdiSetParser, CcdSetParser, M3uPlaylistParser
├── RomCleanup.Infrastructure/ ← I/O-Adapter & Services
│   ├── Audit/                 AuditCsvStore, AuditSigningService
│   ├── Configuration/         SettingsLoader
│   ├── Conversion/            FormatConverterAdapter
│   ├── Dat/                   DatRepositoryAdapter, DatSourceService
│   ├── Deduplication/         CrossRootDeduplicator, FolderDeduplicator
│   ├── FileSystem/            FileSystemAdapter (Path-Traversal-Schutz, Reparse-Blocking)
│   ├── Hashing/               FileHashService, Crc32, ArchiveHashService, ParallelHasher
│   ├── Logging/               JsonlLogWriter (strukturiertes JSONL)
│   ├── Orchestration/         RunOrchestrator (Full Pipeline)
│   ├── Reporting/             ReportGenerator (HTML, CSV)
│   ├── Safety/                SafetyValidator
│   ├── Tools/                 ToolRunnerAdapter (Hash-Verifizierung, Process-Execution)
│   └── ...                    (Analytics, Events, History, Metrics, Pipeline, Quarantine, Sorting, State, Version)
├── RomCleanup.CLI/            ← Headless Entry Point (Program.cs)
├── RomCleanup.Api/            ← ASP.NET Core Minimal API (REST + SSE)
├── RomCleanup.UI.Wpf/         ← WPF GUI (MVVM, net10.0-windows)
│   ├── ViewModels/            MainViewModel (INotifyPropertyChanged, Commands)
│   ├── Services/              ThemeService, DialogService, SettingsService
│   ├── Converters/            WPF Value Converters
│   ├── Themes/                ResourceDictionary (Dark + Neon Accent)
│   └── MainWindow.xaml(.cs)   RunOrchestrator-Wiring, Rollback
└── RomCleanup.Tests/          ← xUnit Tests (3090+ Tests, 72 Testdateien)
```

Dependency-Richtung: Entry Points → Infrastructure → Core → Contracts (nie umgekehrt).

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

Validierung via JSON Schema. Settings werden über `SettingsLoader` in Infrastructure geladen.

---

## Schlüssel-Konventionen

### Code-Struktur

- **C# Naming:** PascalCase für Methoden/Properties, camelCase für lokale Variablen/Parameter
- **Core-Logik muss pure sein:** Keine I/O-Abhängigkeiten in `RomCleanup.Core` — Grundvoraussetzung für Unit-Tests
- **Dependency Injection:** Alle Services über Konstruktor-Injection, Interfaces aus `Contracts/Ports/`
- **Records/DTOs:** Alle Datenstrukturen als C# records oder Modelle in `Contracts/Models/`

### Fehlerbehandlung

- **Drei Fehlerklassen** (`Contracts/Errors/`):
  - `Transient` → automatischer Retry
  - `Recoverable` → loggen und fortfahren
  - `Critical` → sofort abbrechen, Benutzer informieren
- Strukturierte Fehlerobjekte via `OperationError` — niemals rohe Strings werfen
- **Fehler-Code-Namespaces:** `GUI-*`, `DAT-*`, `IO-*`, `SEC-*`, `RUN-*`

### Sicherheitsregeln (nicht verhandelbar)

- **Kein direktes Löschen** — Standard ist Move in Trash + Audit-Log. Echtes Delete braucht explizite Bestätigung + Danger-Zone-UI
- **Path-Traversal-Schutz** — `FileSystemAdapter.ResolveChildPathWithinRoot` vor jedem Move/Copy/Delete aufrufen; schlägt fehl wenn außerhalb der Root
- **Zip-Slip** — Archiv-Entry-Pfade vor Extraktion gegen Root validieren
- **Reparse Points** (Symlinks/Junctions) — explizit blockieren oder definiert behandeln, niemals transparent folgen
- **CSV-Injection** verhindern: keine führenden `=`, `+`, `-`, `@` in Felder
- **HTML-Encoding** in allen Report-Outputs konsequent
- **Tool-Hash-Verifizierung:** SHA256-Checksums aus `data/tool-hashes.json` vor jedem externen Tool-Aufruf prüfen; Bypass nur via `AllowInsecureToolHashBypass`

### GameKey & Scoring-System

**GameKeyNormalizer** (LRU-Cache: 50k Einträge):
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

**Winner-Selection** (`DeduplicationEngine.SelectWinner`) – deterministisch, gleiche Inputs = gleicher Output:
1. Kategorie filtern (GAME vs. JUNK vs. BIOS)
2. Regions-Score: bevorzugte Region aus `preferredRegions` = 1000−N
3. Format-Score
4. Versions-Score: Verified `[!]` = +500; Revision a-z = 10×Ordinalwert; Version v1.0 = numerisch
5. Größen-Tiebreak: Disc-Spiele → größer bevorzugt; Cartridge → kleiner bevorzugt

### DAT-Verifizierung

- LRU-Cache für Datei-Hashes (20k Einträge), Archiv-Hash-Extraktion via 7z (Skip bei >500 MB)
- Parent/Clone-Mapping-Auflösung
- DAT-Index-Fingerprinting per `consoleKey + hashType + datRoot`
- XXE-Schutz beim XML-Parsing aktiv
- DAT-Download mit SHA256-Sidecar-Verifizierung (`*.sha256`-Datei von Download-URL)
- Quellen: No-Intro, Redump, FBNEO

### Konvertierungs-Pipeline

| Konsole | Zielformat | Tool |
|---------|-----------|------|
| PS1, Saturn, Dreamcast | CHD | `chdman createcd` |
| PS2 | CHD | `chdman createdvd` |
| GameCube, Wii | RVZ | `dolphintool` |
| PSP (PBP) | CHD | `psxtract` |
| NES, SNES etc. | ZIP | `7z` |

**`Invoke-ExternalToolProcess`:** Process-Start, Exit-Code-Prüfung, automatisches Cleanup. Timeout und Retry konfigurierbar. Alle Tool-Pfade aus `toolPaths` in Settings, niemals hardcoded.

### REST API

**Authentifizierung:** HTTP-Header `ROM_CLEANUP_API_KEY` — Wert aus Env-Variable.

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

**Response-Shape (Run-Status):**
```json
{ "Status": "ok|blocked|skipped|failed", "ExitCode": 0, "Preflight": {"valid": true, "warnings": []}, "ReportPath": "…", "AuditPath": "…" }
```

**CLI Exit Codes:** `0` = Erfolg, `1` = Fehler, `2` = Abgebrochen, `3` = Preflight fehlgeschlagen.

### Logging

- Format: strukturiertes **JSONL** (eine JSON-Zeile pro Eintrag)
- Felder: `module`, `level`, `correlationId` (128-bit GUID), `phase`, `metrics`
- Log-Levels (numerisch): `Debug=10`, `Info=20`, `Warning=30`, `Error=40`

### Plugins (`archive/powershell/plugins/`)

> Plugins sind ein PS-Legacy-Feature. Beispiele liegen im Archiv als Referenz für eine mögliche C#-Neuimplementierung.

### Output-Formate

- **Audit-CSV** — SHA256-signiert; Spalten: `RootPath, OldPath, NewPath, Action, Category, Hash, Reason, Timestamp`
- **HTML-Report** — mit Meta-Tags, CSP-Header, Diagrammen (Keep/Move/Junk nach Konsole)
- **JSON-Summary** — `{ Status, ExitCode, Preflight: {valid, warnings}, ReportPath, AuditPath }`
- **JSONL-Logs** — strukturiert mit Correlation-ID und Phase-Metriken

### GUI (WPF/XAML)

WPF GUI in `src/RomCleanup.UI.Wpf/` (MVVM-Pattern):
- `MainWindow.xaml` — Full Layout mit TabControl, Dashboard, Progress, Timeline
- `ViewModels/MainViewModel.cs` — ~530 Zeilen, 12 Commands, INotifyPropertyChanged
- `Services/` — ThemeService, DialogService, SettingsService, StatusBarService
- `Converters/` — WPF Value Converters
- `Themes/` — ResourceDictionary (Dark + Neon Accent)

**GUI-Pflichten:**
- Standard-Flow: **DryRun/Preview → explizite Summary-Anzeige → Bestätigung → Move/Apply**
- Lange Tasks **off-UI-thread**; UI-Updates ausschließlich via `Dispatcher.Invoke`
- Kein `DoEvents`-Pattern
- Farben/Styles zentral im `ResourceDictionary` (retro-modern Dark + Neon Accent)
- Zwei Modi: Einfach (4 Entscheidungen) und Experte (volle Kontrolle)

---

## Tests

3090+ xUnit-Tests in `src/RomCleanup.Tests/` (72 Testdateien).

**Benennung:** `<Klasse>Tests.cs` (z.B. `GameKeyNormalizerTests.cs`, `DeduplicationEngineTests.cs`)

**Pflicht-Testarten:**
- **Unit:** Region-Parsing, GameKey-Konstruktion, FormatScore/VersionScore, Winner-Selection, Sanitizer
- **Integration:** echte TempDirs, Move→Trash, Report-Output, Archiv-Handling (ZIP/7Z), DAT-Index/Hits
- **Regression:** reale Problemfälle als Fixtures (beschädigte Archive, fehlende CUE-Tracks, Sonderzeichen)
- **Negativ/Edge:** Path-Traversal-Versuche, Reparse-Points, leere Archive, >500 MB Dateien

**Keine Alibi-Tests:** Jeder Test muss einen echten Fehler finden können. Invarianten prüfen:
- Winner-Selection ist deterministisch (gleiche Inputs = gleicher Winner)
- Kein Move außerhalb der Root
- Keine leeren Keys für Gruppierung

---

## CI/CD (`.github/workflows/`)

### test-pipeline.yml
Trigger: Push/PR auf `dev/**`, `.github/**`

| Job | Gate | Bei Fehler |
|-----|------|-----------|
| Unit-Tests + Coverage | 50% Minimum | Fail |
| Governance | Modul-Grenzen, Komplexität | Warn only |
| Mutation-Testing | Reporting only | continue-on-error |
| Benchmark-Gate | Regression-Erkennung | continue-on-error |

Artifacts: `test-pipeline-*.json`, `mutation-reports-*.json`

### release.yml
Trigger: `workflow_dispatch` oder Tag `v*`
Artifact: `rom-cleanup-v*.zip`
