# C2: Watch-Folder / Scheduled Runs

## Problem

Benutzer muessen Runs manuell starten. Keine automatische Erkennung neuer Dateien,
kein inkrementelles Processing, kein Schedule.

## Loesungsansatz

FileSystemWatcher + Timer + bestehende Pipeline mit inkrementeller Verarbeitung.

### Architektur

```
WatchFolderService
  ├── FileSystemWatcher (pro Root)
  ├── ChangeBuffer (Debounce: 5s)
  ├── ScheduleTimer (optional: Cron-like)
  └── IncrementalRunTrigger
        └── RunOrchestrator (mit Delta-Index aus C1)
```

### Konfiguration (settings.json)

```json
{
  "watchFolder": {
    "enabled": false,
    "roots": ["D:\\Roms"],
    "debounceSeconds": 5,
    "schedule": null,
    "mode": "DryRun",
    "notifyOnComplete": true
  }
}
```

### Komponenten

1. **WatchFolderService** (`Infrastructure/Watch/WatchFolderService.cs`)
   - Startet FileSystemWatcher pro root
   - Buffert Changes (Created/Renamed/Deleted)
   - Triggert nach Debounce einen inkrementellen Run

2. **IncrementalRunTrigger** (`Infrastructure/Watch/IncrementalRunTrigger.cs`)
   - Liest Delta aus CollectionIndex (C1)
   - Fuehrt Run nur fuer neue/geaenderte Dateien aus
   - Schreibt Ergebnisse zurueck in Index

3. **ScheduleService** (`Infrastructure/Watch/ScheduleService.cs`)
   - Cron-aehnlicher Timer fuer periodische Full-Scans
   - Alternativ: Interval-basiert (alle N Stunden)

### Integration Points

- API: `POST /watch/start`, `POST /watch/stop`, `GET /watch/status`
- GUI: Toggle in Settings, Status-Anzeige, Log-Viewer
- CLI: `romulus watch --roots <path>` (foreground daemon)

## Abhaengigkeiten

- **C1 (Persistenter Index)**: Pflicht fuer Delta-Erkennung
- Bestehende RunOrchestrator-Pipeline

## Risiken

- FileSystemWatcher Limitierungen (Buffer Overflow bei vielen Aenderungen)
- Netzlaufwerke: FileSystemWatcher oft unzuverlaessig → Fallback auf Polling
- Concurrent Runs: Singleton-Schutz noetig

## Testplan

- Unit: Debounce-Logik, Schedule-Trigger
- Integration: File-Create → Watch → Run → Index-Update
- Edge: Schnelle Burst-Aenderungen, Netzwerk-Disconnect
- Performance: 10k+ gleichzeitige File-Events
