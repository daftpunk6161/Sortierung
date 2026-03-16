# Contributing

## Voraussetzungen

- **.NET 10 SDK** (net10.0, LangVersion 14)
- **Windows 10/11** (für WPF-GUI, net10.0-windows)

## Setup

1. Repository klonen
2. Build:
   ```bash
   dotnet build src/RomCleanup.sln
   ```
3. Tests ausführen (3090+ xUnit-Tests):
   ```bash
   dotnet test src/RomCleanup.sln
   ```

## Architektur

Clean Architecture (Ports & Adapters). Abhängigkeiten nur abwärts:

```
Entry Points → Infrastructure → Core → Contracts
```

Produktivcode liegt in `src/` (7 Projekte). Die PowerShell-Version ist archiviert in `archive/powershell/`.

### Projekte

| Projekt | Zweck |
|---------|-------|
| **RomCleanup.Contracts** | Port-Interfaces (`IFileSystem`, `IAuditStore`, `IDatRepository` …), Models/DTOs, Error-Contracts |
| **RomCleanup.Core** | Pure Domain Logic: GameKeys, Regions, Scoring, Deduplication, Classification, SetParsing, Rules, Caching |
| **RomCleanup.Infrastructure** | I/O-Adapter: FileSystem, Audit, Dat, Hashing, Tools, Conversion, Orchestration, Reporting, Logging, Configuration |
| **RomCleanup.CLI** | Headless Entry Point |
| **RomCleanup.Api** | ASP.NET Core Minimal API (REST + SSE, API-Key-Auth, Rate-Limiting) |
| **RomCleanup.UI.Wpf** | WPF GUI (MVVM, Dark-Theme, net10.0-windows) |
| **RomCleanup.Tests** | xUnit Tests (3090+ Tests, 72 Testdateien) |

## Coding Standards

- **Naming:** PascalCase für Methoden/Properties/Klassen, camelCase für lokale Variablen/Parameter
- **Dateien:** `<Klasse>.cs`, Tests: `<Klasse>Tests.cs`
- **Core-Logik muss pure sein:** Keine I/O-Abhängigkeiten in `RomCleanup.Core`
- **Dependency Injection:** Alle Services über Konstruktor-Injection, Interfaces aus `Contracts/Ports/`
- **DTOs:** Records oder Modelle in `Contracts/Models/`
- **Fehlerbehandlung:** Strukturierte `OperationError`-Objekte mit `ErrorKind` (Transient/Recoverable/Critical) — keine rohen Strings
- **Kein stilles `catch {}`** in Domain/Infrastructure
- **Keine hardcodierten Pfade** — Tool-Pfade aus Settings, Datendateien aus `data/`
- Kleine, fokussierte Änderungen
- Öffentliche APIs stabil halten

## Sicherheitsregeln

- **Kein direktes Löschen** — Standard ist Move in Trash + Audit-Log
- **Path-Traversal-Schutz** — `FileSystemAdapter.ResolveChildPathWithinRoot` vor jedem Move/Copy/Delete
- **Reparse Points** (Symlinks/Junctions) — blockieren, niemals transparent folgen
- **CSV-Injection** — keine führenden `=`, `+`, `-`, `@` in Audit-Felder
- **HTML-Encoding** in Reports
- **Tool-Hash-Verifizierung** — SHA256-Checksums aus `data/tool-hashes.json` vor Tool-Aufruf
- **XXE-Schutz** beim DAT-XML-Parsing

## Teststrategie

- Erst zielgerichtete Tests für den geänderten Bereich
- Danach alle Tests: `dotnet test src/RomCleanup.sln`
- Mit Filter: `dotnet test src/RomCleanup.sln --filter "FullyQualifiedName~GameKey"`
- Coverage-Minimum: 50%
- Details: `docs/TEST_STRATEGY.md`

## Pull Request Checkliste

- [ ] `dotnet build src/RomCleanup.sln` ohne Errors/Warnings
- [ ] `dotnet test src/RomCleanup.sln` — alle Tests grün
- [ ] Coverage ≥ 50%
- [ ] Keine Compiler-Warnings
- [ ] Dependency-Richtung eingehalten (Entry Points → Infrastructure → Core → Contracts)
- [ ] Neue Klassen mit Tests in `RomCleanup.Tests/`
- [ ] Sicherheitsregeln beachtet (Path-Traversal, CSV-Injection, Tool-Hash)
- [ ] `docs/ARCHITECTURE_MAP.md` aktualisiert bei neuen Modulen
- [ ] Breaking Changes dokumentiert
