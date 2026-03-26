# Romulus — Architektur-Map & Modul-Verantwortlichkeiten

Stand: 2026-03-15
Plattform: C# .NET 10 (net10.0, LangVersion 14)
Referenzen: ADR 0002 (Ports/Services), ADR 0004 (Clean Architecture)

---

## 1 — Architektur-Übersicht (Clean Architecture, Ports & Adapters)

```
┌─────────────────────────────────────────────────────────────────────┐
│                        ENTRY POINTS                                │
│  RomCleanup.CLI         RomCleanup.Api         RomCleanup.UI.Wpf   │
│  (headless)             (REST + SSE)           (WPF/MVVM)          │
└──────────┬──────────────────┬────────────────┬──────────────────────┘
           │                  │                │
           ▼                  ▼                ▼
┌─────────────────────────────────────────────────────────────────────┐
│                     INFRASTRUCTURE                                 │
│                                                                     │
│  Orchestration/     RunOrchestrator, ExecutionHelpers               │
│  FileSystem/        FileSystemAdapter (Path-Traversal, Reparse)    │
│  Audit/             AuditCsvStore, AuditSigningService             │
│  Dat/               DatRepositoryAdapter, DatSourceService         │
│  Hashing/           FileHashService, Crc32, ArchiveHash, Parallel  │
│  Conversion/        FormatConverterAdapter                         │
│  Tools/             ToolRunnerAdapter (Hash-Verify, Process)       │
│  Logging/           JsonlLogWriter (JSONL, Rotation)               │
│  Reporting/         ReportGenerator (HTML, CSV)                    │
│  Configuration/     SettingsLoader                                 │
│  Safety/            SafetyValidator                                │
│  Sorting/           ConsoleSorter, ZipSorter                       │
│  Deduplication/     CrossRootDeduplicator, FolderDeduplicator      │
│  Events/            EventBus (Pub/Sub, Wildcard-Topics)            │
│  Pipeline/          PipelineEngine (Conditional Steps)             │
│  Quarantine/        QuarantineService                              │
│  History/           RunHistoryService, ScanIndexService             │
│  Metrics/           PhaseMetricsCollector                          │
│  Linking/           HardlinkService                                │
│  Analytics/         InsightsEngine                                 │
│  Diagnostics/       CatchGuardService                              │
│  State/             AppStateStore (Undo/Redo, Watch)               │
│  Services/          ApplicationServiceFacade                       │
│  Version/           VersionHelper                                  │
└──────────────────────────┬──────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────────────┐
│                       CORE (Pure Domain Logic)                     │
│                                                                     │
│  GameKeys/          GameKeyNormalizer (LRU-Cache, Tag-Patterns)     │
│  Regions/           RegionDetector                                 │
│  Scoring/           FormatScorer, VersionScorer                    │
│  Classification/    ConsoleDetector, FileClassifier,               │
│                     ExtensionNormalizer, DiscHeaderDetector         │
│  Deduplication/     DeduplicationEngine (Winner-Selection)         │
│  SetParsing/        CueSetParser, GdiSetParser,                    │
│                     CcdSetParser, M3uPlaylistParser                │
│  Rules/             RuleEngine                                     │
│  Caching/           LruCache<TKey,TValue>                          │
└──────────────────────────┬──────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────────────┐
│                       CONTRACTS                                    │
│                                                                     │
│  Ports/             IFileSystem, IAuditStore, IDatRepository,      │
│                     IToolRunner, IFormatConverter, IAppState        │
│  Models/            RomCandidate, RomCleanupSettings, DatIndex,    │
│                     ConversionModels, SortingModels, RuleModels,   │
│                     EventBusModels, PipelineModels,                 │
│                     QuarantineModels, AdvancedModels, ...          │
│  Errors/            OperationError, ErrorClassifier                │
└─────────────────────────────────────────────────────────────────────┘
```

### Dependency-Richtung

```
Entry Points → Infrastructure → Core → Contracts (nie umgekehrt)
```

---

## 2 — Projekt-Struktur

```
src/
├── RomCleanup.Contracts/      ← Port-Interfaces, Models, Error-Contracts
├── RomCleanup.Core/           ← Pure Domain Logic (keine I/O-Deps)
├── RomCleanup.Infrastructure/ ← I/O-Adapter & Services
├── RomCleanup.CLI/            ← Headless Entry Point
├── RomCleanup.Api/            ← ASP.NET Core Minimal API
├── RomCleanup.UI.Wpf/         ← WPF GUI (MVVM, net10.0-windows)
└── RomCleanup.Tests/          ← xUnit Tests (5200+ Tests, 136 Testdateien)
```

---

## 3 — Modul-Verantwortlichkeiten

### 3.1 Contracts (`RomCleanup.Contracts`)

| Datei | Verantwortung |
|---|---|
| `Ports/IFileSystem.cs` | Datei-Operationen (Move, Copy, Enum, Reparse-Check) |
| `Ports/IAuditStore.cs` | Audit-CSV-Schreiben, Sidecar-Metadaten, Rollback |
| `Ports/IDatRepository.cs` | DAT-XML-Parsing, Hash-Lookup, Parent/Clone |
| `Ports/IToolRunner.cs` | Externe Tool-Ausführung (chdman, 7z, dolphintool) |
| `Ports/IFormatConverter.cs` | Formatkonvertierung |
| `Ports/IAppState.cs` | Anwendungszustand (Get/Set/Watch/Undo/Redo) |
| `Models/*.cs` | Records/DTOs für alle Domänenmodelle |
| `Errors/OperationError.cs` | Strukturierte Fehlerobjekte |
| `Errors/ErrorClassifier.cs` | Fehler-Kategorisierung (Transient/Recoverable/Critical) |

### 3.2 Core (`RomCleanup.Core`)

| Datei | Verantwortung | Abhängigkeiten |
|---|---|---|
| `GameKeys/GameKeyNormalizer.cs` | GameKey-Generierung (ASCII-Fold, Tag-Parsing, LRU-Cache 50k) | Contracts |
| `Regions/RegionDetector.cs` | Regions-Erkennung aus Dateinamen | Contracts |
| `Scoring/FormatScorer.cs` | Format-Scoring (CHD=850, ISO=700, ZIP=500, etc.) | Contracts |
| `Scoring/VersionScorer.cs` | Versions-Scoring (Verified, Revision, Version) | Contracts |
| `Classification/ConsoleDetector.cs` | Konsolen-Erkennung (Ordner + Extension-Heuristik) | Contracts |
| `Classification/FileClassifier.cs` | GAME/BIOS/JUNK-Klassifikation | Contracts |
| `Classification/ExtensionNormalizer.cs` | Dateiendungs-Normalisierung | Contracts |
| `Classification/DiscHeaderDetector.cs` | Disc-Header-Erkennung (PS1, PS2, Saturn, DC, 3DO, etc.) | Contracts |
| `Deduplication/DeduplicationEngine.cs` | Deterministische Winner-Selection | Contracts |
| `SetParsing/CueSetParser.cs` | CUE-Sheet-Parsing | Contracts |
| `SetParsing/GdiSetParser.cs` | GDI-Set-Parsing | Contracts |
| `SetParsing/CcdSetParser.cs` | CCD-Set-Parsing | Contracts |
| `SetParsing/M3uPlaylistParser.cs` | M3U-Playlist-Parsing | Contracts |
| `Rules/RuleEngine.cs` | Konfigurierbare Klassifikationsregeln | Contracts |
| `Caching/LruCache.cs` | Thread-safe LRU-Cache, O(1) Ops | — |

### 3.3 Infrastructure (`RomCleanup.Infrastructure`)

| Verzeichnis | Klasse(n) | Verantwortung |
|---|---|---|
| `Orchestration/` | `RunOrchestrator`, `ExecutionHelpers` | Full-Pipeline (Preflight→Scan→Dedupe→Sort→Convert→Move) |
| `FileSystem/` | `FileSystemAdapter` | Path-Traversal-Schutz, Reparse-Point-Blocking, Move/Copy |
| `Audit/` | `AuditCsvStore`, `AuditSigningService` | CSV-Audit (SHA256-signiert), Rollback, CSV-Injection-Schutz |
| `Dat/` | `DatRepositoryAdapter`, `DatSourceService` | DAT-XML-Parsing (XXE-Schutz), Download mit SHA256-Sidecar |
| `Hashing/` | `FileHashService`, `Crc32`, `ArchiveHashService`, `ParallelHasher` | Streaming-Hashing, CRC32, Archiv-Inhalts-Hashing, Multi-Thread |
| `Conversion/` | `FormatConverterAdapter` | CHD/RVZ/ZIP-Konvertierung |
| `Tools/` | `ToolRunnerAdapter` | SHA256-Hash-Verifizierung, Process-Execution, Exit-Codes |
| `Logging/` | `JsonlLogWriter` | Strukturiertes JSONL, Rotation, CorrelationId |
| `Reporting/` | `ReportGenerator` | HTML (CSP-Header), CSV (Injection-Schutz) |
| `Configuration/` | `SettingsLoader` | JSON-Settings laden/validieren |
| `Safety/` | `SafetyValidator` | Preflight-Checks, Ampel |
| `Sorting/` | `ConsoleSorter`, `ZipSorter` | Konsolen-Sortierung, ZIP-Sortierung |
| `Deduplication/` | `CrossRootDeduplicator`, `FolderDeduplicator` | Cross-Root-Duplikate, Ordner-Deduplizierung |
| `Events/` | `EventBus` | Pub/Sub mit Wildcard-Topics |
| `Pipeline/` | `PipelineEngine` | Conditional Step-Chains |
| `Quarantine/` | `QuarantineService` | ROM-Quarantäne |
| `History/` | `RunHistoryService`, `ScanIndexService` | Run-Historie, Scan-Index |
| `Metrics/` | `PhaseMetricsCollector` | Phasen-Zeitmessung |
| `Linking/` | `HardlinkService` | Hardlink-Operationen |
| `Analytics/` | `InsightsEngine` | Sammlungs-Analyse, Cross-Collection-Hints |
| `Diagnostics/` | `CatchGuardService` | Exception-Governance, strukturierte Error-Records |
| `State/` | `AppStateStore` | Undo/Redo, Watch-Pattern |
| `Services/` | `ApplicationServiceFacade` | Service-Fassade für Entry Points |
| `Version/` | `VersionHelper` | Versions-Verwaltung |

### 3.4 Entry Points

| Projekt | Verantwortung |
|---|---|
| `RomCleanup.CLI/Program.cs` | Headless CLI (Args-Parsing, RunOrchestrator-Wiring, Exit-Codes 0/1/2/3) |
| `RomCleanup.Api/Program.cs` | ASP.NET Core Minimal API (127.0.0.1, API-Key, CORS, Rate-Limiting) |
| `RomCleanup.Api/RunManager.cs` | Run-Lifecycle (Create, Execute, Cancel, SSE-Stream) |
| `RomCleanup.Api/RateLimiter.cs` | Sliding-Window Rate-Limiting (120 req/min) |
| `RomCleanup.Api/OpenApiSpec.cs` | Eingebettete OpenAPI 3.0.3 Spec |
| `RomCleanup.UI.Wpf/MainWindow.xaml` | WPF-Hauptfenster (TabControl, Dashboard, Timeline) |
| `RomCleanup.UI.Wpf/ViewModels/MainViewModel.cs` | MVVM-ViewModel (INotifyPropertyChanged, Commands) |
| `RomCleanup.UI.Wpf/Services/ThemeService.cs` | Dark/Light-Theme-Verwaltung |
| `RomCleanup.UI.Wpf/Services/DialogService.cs` | Dialoge |
| `RomCleanup.UI.Wpf/Services/SettingsService.cs` | Settings-Verwaltung |
| `RomCleanup.UI.Wpf/Services/StatusBarService.cs` | Statusleiste |

---

## 4 — Dependency-Grenzen (verbindliche Regeln)

### 4.1 Layer-Regeln

| Von (Aufrufer) | Darf referenzieren | Darf NICHT referenzieren |
|---|---|---|
| Entry Points (CLI, Api, UI.Wpf) | Infrastructure, Core, Contracts | — |
| Infrastructure | Core, Contracts | Entry Points |
| Core | Contracts | Infrastructure, Entry Points |
| Contracts | — | Core, Infrastructure, Entry Points |

### 4.2 Verbotene Abhängigkeiten

| Regel | Beschreibung |
|---|---|
| **NO-IO-IN-CORE** | `RomCleanup.Core` darf keine I/O-Operationen enthalten (kein System.IO, kein Process) |
| **NO-REVERSE-DEPS** | Core darf Infrastructure nicht referenzieren |
| **NO-CROSSENTRY** | Entry Points dürfen sich nicht gegenseitig referenzieren |

---

## 5 — Zentrale Datendateien (`data/`)

| Datei | Rolle |
|---|---|
| `consoles.json` | Single Source of Truth: 100+ Konsolen (Key, DisplayName, discBased, uniqueExts, folderAliases) |
| `rules.json` | Regions-Patterns, Junk-Tags, Version/Revision-Erkennung |
| `dat-catalog.json` | DAT-Quellen (Redump, No-Intro, FBNEO) mit URLs |
| `defaults.json` | Standard-Einstellungen (Mode=DryRun, Extensions, Theme, Locale) |
| `tool-hashes.json` | SHA256-Allowlist für externe Tools (chdman, 7z, dolphintool) |
| `schemas/*.json` | JSON-Schema v7 für Settings, Rules, Profiles, Plugins |
| `i18n/*.json` | Lokalisierung (de, en, fr) |

---

## 6 — Tests

5200+ xUnit-Tests in 136 Testdateien (`src/RomCleanup.Tests/`).

Detaillierte Teststrategie: siehe `TEST_STRATEGY.md`.
