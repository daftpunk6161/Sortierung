# Romulus — Architektur-Map & Modul-Verantwortlichkeiten

Stand: 2026-04-01
Plattform: C# .NET 10 (net10.0, LangVersion 14)
Referenzen: ADR 0002 (Ports/Services), ADR 0004 (Clean Architecture)

---

## 1 — Architektur-Übersicht (Clean Architecture, Ports & Adapters)

```
┌─────────────────────────────────────────────────────────────────────┐
│                        ENTRY POINTS                                │
│  Romulus.CLI         Romulus.Api         Romulus.UI.Wpf   │
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
│  Analysis/          CollectionAnalysisService, DatAnalysisService  │
│  Quarantine/        QuarantineService                              │
│  Metrics/           PhaseMetricsCollector                          │
│  Linking/           HardlinkService                                │
│  Paths/             ArtifactPathResolver, ToolPathValidator         │
│  State/             AppStateStore (Undo/Redo, Watch)               │
│  Time/              SystemTimeProvider                             │
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
│  Models/            RomCandidate, RomulusSettings, DatIndex,    │
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
├── Romulus.Contracts/      ← Port-Interfaces, Models, Error-Contracts
├── Romulus.Core/           ← Pure Domain Logic (keine I/O-Deps)
├── Romulus.Infrastructure/ ← I/O-Adapter & Services
├── Romulus.CLI/            ← Headless Entry Point
├── Romulus.Api/            ← ASP.NET Core Minimal API
├── Romulus.UI.Wpf/         ← WPF GUI (MVVM, net10.0-windows)
└── Romulus.Tests/          ← xUnit Tests (aktuell 7047 Tests, 200+ Testdateien)
```

---

## 3 — Modul-Verantwortlichkeiten

### 3.1 Contracts (`Romulus.Contracts`)

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

### 3.2 Core (`Romulus.Core`)

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

### 3.3 Infrastructure (`Romulus.Infrastructure`)

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
| `Analysis/` | `CollectionAnalysisService`, `DatAnalysisService` | Sammlungs-, DAT- und Integritätsanalysen |
| `Quarantine/` | `QuarantineService` | ROM-Quarantäne |
| `Metrics/` | `PhaseMetricsCollector` | Phasen-Zeitmessung |
| `Linking/` | `HardlinkService` | Hardlink-Operationen |
| `Paths/` | `ArtifactPathResolver`, `ToolPathValidator` | Artefakt- und Tool-Pfadvalidierung |
| `State/` | `AppStateStore` | Undo/Redo, Watch-Pattern |
| `Time/` | `SystemTimeProvider` | Zeitquelle für Laufzeit-/Auditlogik |
| `Version/` | `VersionHelper` | Versions-Verwaltung |

### 3.4 Entry Points

| Projekt | Verantwortung |
|---|---|
| `Romulus.CLI/Program.cs` | Headless CLI (Args-Parsing, RunOrchestrator-Wiring, Exit-Codes 0/1/2/3) |
| `Romulus.Api/Program.cs` | ASP.NET Core Minimal API (127.0.0.1, API-Key, CORS, Rate-Limiting) |
| `Romulus.Api/RunManager.cs` | Run-Lifecycle (Create, Execute, Cancel, SSE-Stream) |
| `Romulus.Api/RateLimiter.cs` | Sliding-Window Rate-Limiting (120 req/min) |
| `Romulus.Api/OpenApiSpec.cs` | OpenAPI-Konfiguration und Transformer fuer die generierte Laufzeit-Spec |
| `Romulus.UI.Wpf/MainWindow.xaml` | WPF-Hauptfenster (TabControl, Dashboard, Timeline) |
| `Romulus.UI.Wpf/ViewModels/MainViewModel.cs` | MVVM-ViewModel (INotifyPropertyChanged, Commands) |
| `Romulus.UI.Wpf/Services/ThemeService.cs` | Dark/Light-Theme-Verwaltung |
| `Romulus.UI.Wpf/Services/DialogService.cs` | Dialoge |
| `Romulus.UI.Wpf/Services/SettingsService.cs` | Settings-Verwaltung |
| `Romulus.UI.Wpf/Services/RunService.cs` | WPF-Run-Ausführung und Orchestrator-Integration |

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
| **NO-IO-IN-CORE** | `Romulus.Core` darf keine I/O-Operationen enthalten (kein System.IO, kein Process) |
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

Aktuell 7047 xUnit-Tests in 200+ Testdateien (`src/Romulus.Tests/`).

Detaillierte Teststrategie: siehe `TEST_STRATEGY.md`.
