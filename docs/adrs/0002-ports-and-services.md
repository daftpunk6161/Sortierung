# ADR 0002: Service-/Port-Schnittstellen für Kernoperationen

## Status
Accepted — Umgesetzt in C# .NET 10 (2026-03-11)

## Kontext
Direkte Kopplung zwischen UI und Kernlogik erschwert Tests und Austauschbarkeit.

## Entscheidung
- Kernoperationen laufen über Application-Services.
- Erweiterungspunkte werden als Ports modelliert.

## Umsetzung (C#)

Port-Interfaces in `src/Romulus.Contracts/Ports/`:

| Interface | Verantwortung |
|-----------|---------------|
| `IFileSystem` | Datei-Operationen (Move, Copy, Enum, Reparse-Check) |
| `IToolRunner` | Externe Tools (chdman, 7z, dolphintool) |
| `IDatRepository` | DAT-XML-Parsing, Hash-Lookup |
| `IAuditStore` | Audit-CSV-Schreiben, Rollback |
| `IFormatConverter` | Formatkonvertierung |
| `IAppState` | Anwendungszustand (Undo/Redo, Watch) |

Implementierungen in `src/Romulus.Infrastructure/`:
- `FileSystemAdapter`, `ToolRunnerAdapter`, `DatRepositoryAdapter`
- `AuditCsvStore`, `FormatConverterAdapter`, `AppStateStore`

Orchestrierung via `RunOrchestrator` in `Infrastructure/Orchestration/`.

## Konsequenzen
- Verbesserte Testbarkeit (alle 5200+ Tests nutzen Port-basierte Abstraktion)
- Geringere UI-Kopplung (CLI, API, WPF nutzen gleiche Ports)
- Einfachere Integration externer Adapter
