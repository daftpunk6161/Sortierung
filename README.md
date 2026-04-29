# Romulus

<p align="center">
  <img src="assets/logo/romulus-banner.svg" alt="Romulus Banner" width="100%"/>
</p>

<p align="center">
  <strong>Deterministisches Aufräumen grosser ROM-Sammlungen — mit Audit-Trail und Rollback.</strong><br/>
  C# .NET 10 Werkzeug für Sammler und Archivare, die ihren Bestand verifizierbar
  verkleinern und sortieren wollen.
</p>

<p align="center">
  <strong>WPF-GUI</strong> · <strong>CLI</strong> (headless / CI) · <strong>REST API</strong> (lokal, ASP.NET Core Minimal API)
</p>

---

## Wofür Romulus gedacht ist

**Persona:** Sammler oder Archivare mit grossen ROM-Beständen (mehrere zehntausend
Dateien, gemischte Quellen, Jahre an gewachsener Unordnung), die ihre Sammlung
einmal sauber durchputzen und danach in einem definierten Zustand halten wollen.

Romulus ist **kein Frontend, kein Scraper, kein Patch-Tool, kein Spiele-Launcher.**
Es ist ein Aufräum- und Verifikations-Werkzeug, das jeden Schritt
nachvollziehbar macht und rückgängig machen kann.

## Drei USPs

1. **Signierter Audit-Trail** — jede Move/Convert-Aktion wird mit SHA-256
   protokolliert und ist nachträglich gegen Manipulation prüfbar.
2. **Rollback** — jeder Lauf kann vollständig rückgängig gemacht werden, solange
   die Quell-Dateien noch im Audit-Sidecar referenziert sind.
3. **Deterministisches Cleanup** — gleiche Eingaben erzeugen die gleichen
   Entscheidungen. Preview, Execute und Report verwenden dieselbe fachliche
   Wahrheit, ohne dass GUI/CLI/API auseinanderlaufen.

## Sechs Hauptaktionen

Die GUI und die CLI bieten genau diese sechs Aktionen — keine versteckten Modi,
keine Stub-Features:

| Aktion | Zweck |
|---|---|
| **Scan** | Roots einlesen, Kandidaten klassifizieren, Konsolen erkennen. |
| **Verify** | Hashes berechnen und gegen DAT-Quellen (No-Intro, Redump, …) prüfen. |
| **Plan** | Winner-Selection, Junk- und Doublette-Erkennung — ohne etwas anzufassen (DryRun). |
| **Move** | Plan ausführen: in Trash, in BIOS-Ordner, in Konsolen-Sortierung verschieben. Audit + Sidecar werden geschrieben. |
| **Convert** | Optional: CUE/BIN → CHD, ISO → CHD/RVZ. Lossy-Pfade sind explizit als Warnung markiert und brauchen Zustimmung. |
| **Rollback** | Jeden vergangenen Lauf vollständig rückgängig machen. |

---

## Systemanforderungen

- **Windows 10/11**
- **.NET 10 SDK** (`net10.0` / `net10.0-windows` für die GUI)
- **Optionale externe Tools** (für Konvertierung; Pfade in den Settings):
  - [chdman](https://www.mamedev.org/) — CHD-Konvertierung (PS1/PS2/Saturn/Dreamcast)
  - [DolphinTool](https://dolphin-emu.org/) — RVZ-Konvertierung (GameCube/Wii)
  - [7-Zip](https://www.7-zip.org/) — Archiv-Handhabung

Alle externen Tools werden gegen eine SHA-256-Allowlist geprüft, bevor sie
ausgeführt werden.

---

## Quick Start

### GUI (WPF)

```bash
dotnet run --project src/Romulus.UI.Wpf
```

Empfohlener Ablauf:

1. **Roots hinzufügen** (Drag & Drop oder Button).
2. **Scan** → **Verify** → **Plan** (alles als DryRun, kein Move).
3. **Summary** und HTML-Report prüfen.
4. **Bestätigen** und **Move** ausführen.
5. Bei Bedarf **Rollback** desselben Laufs.

### CLI (headless)

```bash
# DryRun — nur Vorschau und Report
dotnet run --project src/Romulus.CLI -- --roots "D:\Roms" --mode DryRun

# Move — Plan ausführen
dotnet run --project src/Romulus.CLI -- --roots "D:\Roms" --mode Move --regions EU,US
```

**Exit Codes:** `0` = Erfolg, `1` = Fehler, `2` = Abgebrochen, `3` = Preflight fehlgeschlagen.

### REST API (lokal)

```bash
dotnet run --project src/Romulus.Api
```

Die API bindet ausschliesslich an `127.0.0.1:7878` und ist nicht für
Multi-User- oder Remote-Betrieb gedacht. Authentifizierung über
`X-Api-Key`-Header, Wert aus der Env-Variable `ROM_CLEANUP_API_KEY`.

| Methode | Pfad | Zweck |
|---|---|---|
| `GET` | `/health` | Health-Check |
| `GET` | `/openapi` | OpenAPI-Spec |
| `POST` | `/runs` | Run erstellen |
| `GET` | `/runs/{id}` | Run-Status |
| `GET` | `/runs/{id}/result` | Vollständiges Ergebnis |
| `POST` | `/runs/{id}/cancel` | Run abbrechen |
| `GET` | `/runs/{id}/stream` | SSE-Fortschrittsstream |

API-Schutz: Rate Limit (120 Req/min, Sliding Window), Body Limit 1 MB,
CORS standardmässig auf `strict-local`.

---

## Konsolen-Abdeckung

Romulus kennt **163 Konsolen**, davon **30 als „core" markiert** (Mainstream
Heim/Handheld bis Wii/PS3). Der Onboarding-Wizard zeigt standardmässig nur die
Core-Liste und weist auf den Best-Effort-Status der übrigen 133 Konsolen hin.

Diese Trennung ist explizit, damit klar bleibt, wofür Romulus garantiert
sinnvolle Defaults liefert und wo Heuristik im Spiel ist.

---

## Projektstruktur

```
src/
├── Romulus.Contracts/        # Port-Interfaces, Models, Error-Contracts
├── Romulus.Core/             # Pure Domänenlogik (keine I/O)
│   ├── Classification/         #   ConsoleDetector, FileClassifier
│   ├── Deduplication/          #   DeduplicationEngine, Winner-Selection
│   ├── GameKeys/               #   GameKeyNormalizer (Tag-Parsing)
│   ├── Regions/                #   RegionDetector
│   ├── Scoring/                #   FormatScorer, VersionScorer
│   └── SetParsing/             #   CUE/GDI/CCD/M3U
├── Romulus.Infrastructure/   # I/O-Adapter
│   ├── Audit/                  #   AuditCsvStore, AuditSigningService (Sidecar + Ledger)
│   ├── Conversion/             #   FormatConverterAdapter
│   ├── Dat/                    #   DatRepositoryAdapter, DatSourceService
│   ├── FileSystem/             #   FileSystemAdapter (Path-Traversal-Schutz)
│   ├── Hashing/                #   FileHashService, ArchiveHashService
│   ├── Orchestration/          #   RunOrchestrator (Phasen-Pipeline)
│   ├── Reporting/              #   ReportGenerator (HTML/CSV)
│   ├── Safety/                 #   SafetyValidator
│   ├── Sorting/                #   ConsoleSorter
│   └── Tools/                  #   ToolRunnerAdapter (Hash-verifizierte Aufrufe)
├── Romulus.CLI/              # Headless Entry Point
├── Romulus.Api/              # ASP.NET Core Minimal API (lokal)
├── Romulus.UI.Wpf/           # WPF-GUI (MVVM, net10.0-windows)
└── Romulus.Tests/            # xUnit-Tests

data/                            # Datenquellen (consoles, rules, dat-catalog, …)
docs/                            # Architektur, ADRs, Pläne, Guides
archive/                         # Historische / archivierte Inhalte
```

> **Hinweis:** Eine zweite GUI in Avalonia wurde 2026 als Spike evaluiert und
> bewusst nicht weiterverfolgt (siehe ADR `docs/adrs/0022-gui-platform.md`).
> Der Spike liegt unter `archive/avalonia-spike/` und wird nicht mehr gebaut
> oder getestet.

---

## Architektur

Clean Architecture (Ports & Adapters). Abhängigkeiten nur abwärts:

```
┌────────────────────────────────────────────────────────────┐
│ Entry Points                                               │
│   Romulus.CLI  │  Romulus.Api  │  Romulus.UI.Wpf           │
├────────────────────────────────────────────────────────────┤
│ Infrastructure (I/O-Adapter)                               │
│   FileSystem │ Audit │ Dat │ Hashing │ Tools │ Conversion  │
│   Orchestration │ Reporting │ Logging │ Configuration      │
├────────────────────────────────────────────────────────────┤
│ Core (Pure Domain Logic)                                   │
│   GameKeys │ Regions │ Scoring │ Deduplication             │
│   Classification │ SetParsing │ Rules                      │
├────────────────────────────────────────────────────────────┤
│ Contracts (Ports & Models)                                 │
│   Port-Interfaces │ Models/DTOs │ Error-Contracts          │
└────────────────────────────────────────────────────────────┘
```

**Sicherheit:** root-gebundene Moves, Reparse-Point-Blocking (Quelle und Ziel),
Zip-Slip-Schutz, CSV-Injection-Neutralisierung, SHA-256-signierte Audit-Sidecars
mit Append-only-Ledger, Tool-Hash-Verifizierung, XXE-Schutz beim DAT-Parsing,
HTML-Encoding in allen Reports.

---

## Build & Tests

```bash
# Build
dotnet build src/Romulus.sln

# Alle Tests
dotnet test src/Romulus.sln

# Mit Filter
dotnet test src/Romulus.sln --filter "FullyQualifiedName~GameKey"
```

---

## Konfiguration

Settings: `%APPDATA%\Romulus\settings.json`

```jsonc
{
  "general": {
    "logLevel": "Info",
    "preferredRegions": ["EU", "US", "JP"],
    "aggressiveJunk": false
  },
  "toolPaths": { "chdman": "", "7z": "", "dolphintool": "" },
  "dat": {
    "useDat": true,
    "datRoot": "",
    "hashType": "SHA1"
  }
}
```

Datendateien unter `data/`: `consoles.json` (163 Konsolen, davon 30 „core"),
`rules.json` (Regions-Patterns, Junk-Tags), `dat-catalog.json` (DAT-Quellen),
`defaults.json`, `tool-hashes.json` (SHA-256-Allowlist für externe Tools).

---

## Was Romulus bewusst nicht macht

Damit das Versprechen schmal und einlösbar bleibt, wurden folgende Bereiche
explizit aus dem Scope genommen (siehe
`docs/plan/strategic-reduction-2026/feature-cull-list.md`):

- Frontend-Export (RetroArch, ES-DE, LaunchBox, Playnite, MiSTer, …)
- Metadaten-/Artwork-Scraping (ScreenScraper, IGDB, MobyGames, …)
- ROM-Patching (IPS/BPS/UPS)
- MAME-Set-Building (split/merge/non-merged)
- RetroAchievements-Compliance
- In-Browser-Play
- Plugin- oder Marketplace-Mechanik

Wer eines dieser Themen braucht, wird mit dedizierten Werkzeugen wie RomM, Igir
oder LaunchBox besser bedient.

---

## Lizenz

Privates Projekt — keine öffentliche Lizenz.
