# ADR 0004: Zielarchitektur Clean Architecture (Ports & Adapters)

## Status
Accepted — Vollständig umgesetzt in C# .NET 10 (2026-03-11)

**Reviewed by:** Core Team
**Approval date:** 2026-03-02
**Last updated:** 2026-03-11 (C#-Migration abgeschlossen)

## Kontext
Die ursprüngliche PowerShell-Codebasis litt unter zentralisierten Orchestrierungsdateien mit hoher Änderungs- und Regressionsgefahr. Die Entscheidung fiel auf eine vollständige Migration nach C# .NET 10 mit Clean Architecture.

## Entscheidung
Clean Architecture (Ports & Adapters) als .NET Solution mit 7 Projekten:

1. **Contracts** (`Romulus.Contracts`)
   - Port-Interfaces (`IFileSystem`, `IAuditStore`, `IDatRepository`, `IToolRunner`, `IFormatConverter`, `IAppState`)
   - Models (Records/DTOs)
   - Error-Contracts (`OperationError`, `ErrorClassifier`)

2. **Core** (`Romulus.Core`)
   - Reine, deterministische Domain-Logik — keine I/O-Abhängigkeiten
   - `GameKeys/` — GameKeyNormalizer (LRU-Cache, Tag-Parsing, Region-Scoring)
   - `Regions/` — RegionDetector
   - `Scoring/` — FormatScorer, VersionScorer
   - `Classification/` — ConsoleDetector, FileClassifier, ExtensionNormalizer, DiscHeaderDetector
   - `Deduplication/` — DeduplicationEngine (deterministische Winner-Selection)
   - `SetParsing/` — CueSetParser, GdiSetParser, CcdSetParser, M3uPlaylistParser
   - `Rules/` — RuleEngine
   - `Caching/` — LruCache<TKey,TValue>

3. **Infrastructure** (`Romulus.Infrastructure`)
   - `Orchestration/` — RunOrchestrator, ExecutionHelpers
   - `FileSystem/` — FileSystemAdapter (Path-Traversal-Schutz, Reparse-Blocking)
   - `Audit/` — AuditCsvStore, AuditSigningService
   - `Dat/` — DatRepositoryAdapter, DatSourceService
   - `Hashing/` — FileHashService, Crc32, ArchiveHashService, ParallelHasher
   - `Conversion/` — FormatConverterAdapter
   - `Tools/` — ToolRunnerAdapter
   - `Logging/` — JsonlLogWriter
   - `Reporting/` — ReportGenerator (HTML, CSV)
   - `Configuration/` — SettingsLoader
   - `Safety/` — SafetyValidator
   - `Sorting/` — ConsoleSorter, ZipSorter
   - `Deduplication/` — CrossRootDeduplicator, FolderDeduplicator
   - `Events/` — EventBus
   - `Pipeline/` — PipelineEngine
   - `Quarantine/` — QuarantineService
   - `History/` — RunHistoryService, ScanIndexService
   - `Metrics/` — PhaseMetricsCollector
   - `Linking/` — HardlinkService
   - `Analytics/` — InsightsEngine
   - `Diagnostics/` — CatchGuardService
   - `State/` — AppStateStore
   - `Services/` — ApplicationServiceFacade
   - `Version/` — VersionHelper

4. **Entry Points**
   - `Romulus.CLI` — Headless (Exit-Codes: 0/1/2/3)
   - `Romulus.Api` — ASP.NET Core Minimal API (127.0.0.1, API-Key, Rate-Limiting, SSE)
   - `Romulus.UI.Wpf` — WPF GUI (MVVM, Dark-Theme, net10.0-windows)

5. **Tests** (`Romulus.Tests`)
   - 5200+ xUnit-Tests in 136 Testdateien

## Dependency-Richtung

```
Entry Points (CLI, Api, UI.Wpf) → Infrastructure → Core → Contracts
                                                          (nie umgekehrt)
```

## Technische Leitplanken
- Core-Logik muss pure sein — keine I/O-Abhängigkeiten
- Alle Services über Konstruktor-Injection, Interfaces aus `Contracts/Ports/`
- Kein stilles `catch {}` in Domain/Application/IO
- Drei Fehlerklassen: `Transient`, `Recoverable`, `Critical`

## Migrationsfortschritt

| Phase | Status | Beschreibung |
|-------|--------|-------------|
| PowerShell → C# Core | ✅ Done | Alle Domain-Algorithmen portiert |
| PowerShell → C# Infrastructure | ✅ Done | Alle I/O-Adapter portiert |
| PowerShell → C# CLI | ✅ Done | Vollständiger CLI Entry Point |
| PowerShell → C# API | ✅ Done | REST-API mit Auth, Rate-Limiting, SSE |
| PowerShell → C# GUI | ✅ Done | WPF-GUI (MVVM, Dark-Theme) |
| PowerShell → C# Tests | ✅ Done | 5200+ xUnit-Tests |
| Plugin-System (C#) | ⏳ Backlog | Neuimplementierung ausstehend |

## Konsequenzen

### Positiv
- Klar getrennte Projekte mit expliziten Abhängigkeiten
- 5200+ Tests sichern Regressionssicherheit
- Drei unabhängige Entry Points (CLI, API, GUI) teilen einen Kern
- .NET 10 bietet native Async, starke Typisierung, Cross-Platform-Readiness

### Negativ
- PowerShell-Plugins nicht direkt übertragbar (Neuimplementierung nötig)

## Verknüpfungen
- ADR 0002 (Ports/Services) — aktiv, in C# umgesetzt
- ADR 0001 (Module-Loader) — superseded durch .NET DI
- ADR 0003 (Externalized XAML) — superseded durch native WPF-Projektstruktur
