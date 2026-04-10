# C1: Persistenter Sammlungsindex

## Problem

Jeder Run scannt die komplette Sammlung neu. Kein Delta-Tracking, keine historischen Trends,
kein schneller Rescan. Dashboard, Trend-Reports und inkrementelle Verarbeitung sind ohne
persistenten Index nicht moeglich.

## Loesungsansatz

Lokale Embedded-Datenbank (LiteDB) als Single-File NoSQL-Store. Kein externer Server noetig.

### Schema

```
CollectionEntry {
  Id: string (SHA256 Hash)
  Path: string
  ConsoleKey: string
  GameKey: string
  SizeBytes: long
  Category: FileCategory
  DatMatch: bool
  DatGameName: string?
  LastScannedUtc: DateTime
  LastModifiedUtc: DateTime
  Hash: string?
}

RunHistoryEntry {
  RunId: string
  StartedUtc: DateTime
  Mode: string
  TotalFiles: int
  Dupes: int
  Winners: int
  HealthScore: int
  DurationMs: long
}

HashCacheEntry {
  Path: string
  Hash: string
  Algorithm: string
  FileSize: long
  LastModifiedUtc: DateTime
}
```

### Interface

```csharp
// In Romulus.Contracts
public interface ICollectionIndex
{
    Task<CollectionEntry?> GetByPath(string path);
    Task UpsertAsync(CollectionEntry entry);
    Task<IReadOnlyList<CollectionEntry>> GetByConsole(string consoleKey);
    Task<int> CountAsync();
    Task<IReadOnlyList<RunHistoryEntry>> GetRunHistory(int limit = 50);
    Task AddRunHistory(RunHistoryEntry entry);
    Task<string?> GetCachedHash(string path, long size, DateTime lastModified);
    Task SetCachedHash(string path, string hash, string algorithm, long size, DateTime lastModified);
}
```

### Implementierung

- `src/Romulus.Infrastructure/Index/LiteDbCollectionIndex.cs`
- DB-Pfad: `%APPDATA%\Romulus\collection.db`
- Automatisches Schema-Migration bei Version-Upgrades
- Concurrency: LiteDB supports single-writer/multiple-reader

### Integration

- Scanner-Phase prüft `LastModifiedUtc` gegen Filesystem → Skip wenn identisch
- Nach Run: Index-Update mit neuen/geaenderten Entries
- Dashboard/API: Queries direkt gegen Index statt erneuten Scan

## Abhaengigkeiten

- Enabler fuer: C2 (Watch-Folder), Trend-Tracking, Delta-Reports
- Keine externen Abhaengigkeiten ausser LiteDB NuGet

## Risiken

- LiteDB Lock-Contention bei parallelem Zugriff aus API + GUI
- Schema-Migration bei Breaking Changes
- Datenbank-Korruption bei Crash → Recovery-Strategie noetig

## Testplan

- Unit: CRUD-Operationen, Hash-Cache Hit/Miss, RunHistory
- Integration: Scan → Index → Re-Scan mit Delta
- Edge: Korrupte DB → Graceful Recovery
- Performance: 100k+ Entries Scan-Performance
