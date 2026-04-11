# R1 Foundation Execution

Stand: 2026-04-01

Ziel: Eine persistente Sammlungsbasis schaffen, kanaluebergreifende Automation einfuehren und konkurrierende Wahrheiten zwischen Run, Analyse, Review und Nebenpfaden abbauen.

## Nicht-Scope

- Kein Web-Frontend
- Keine neuen Community- oder Cloud-Funktionen
- Keine neue fachliche Parallelpipeline neben bestehender Run-Orchestrierung

## Tickets

### [x] R1-T01 Index-Vertrag und Datenmodell festziehen

Ziel: Den minimalen, stabilen Vertrag fuer Collection-Index, Hash-Cache und Run-Historie definieren.

Detailplan:
- [x] [`../archive/completed/plans/r1-t01-index-contract-technical-plan.md`](../archive/completed/plans/r1-t01-index-contract-technical-plan.md)

Betroffene Bereiche:
- `src/Romulus.Contracts`
- `src/Romulus.Contracts/Models`
- `docs/epics/C1-persistent-collection-index.md`

Akzeptanz:
- [x] Vertrag fuer Collection-Entries, Hash-Cache und Run-Historie ist dokumentiert und versionierbar
- [x] Keine I/O-spezifischen Typen leaken in Core oder Contracts
- [x] Migrationsversion ist explizit modelliert

Abhaengigkeiten:
- Keine

### [x] R1-T02 LiteDB-Adapter, Migration und Recovery umsetzen

Ziel: Einen robusten persistenten Store mit sauberem Recovery-Pfad und deterministischem Verhalten bereitstellen.

Betroffene Bereiche:
- `src/Romulus.Infrastructure/Index`
- `src/Romulus.Infrastructure`
- `%APPDATA%\\Romulus\\`

Akzeptanz:
- [x] Index kann initialisiert, gelesen, geschrieben und versioniert behandelt werden
- [x] V1-Migrations-/Recovery-Strategie ist explizit: unbekannte oder defekte Stores werden gebackupt und sauber neu aufgebaut
- [x] Single-Writer-Verhalten ist ueber den Adapter serialisiert und fuer lokale Nutzung klar abgesichert

Abhaengigkeiten:
- R1-T01

### [x] R1-T03 Delta-Scan und Hash-Cache in Scanner und Orchestrierung integrieren

Ziel: Vollstaendige Re-Scans nur noch dort ausfuehren, wo Dateien neu oder geaendert sind.

Status:
- [x] Persistenter Hash-Cache laeuft ueber den Collection-Index statt ueber eine separate JSON-Nebenwahrheit
- [x] Delta-Erkennung im Scan-Pfad nutzt Pfad, Groesse, `LastWriteUtc` und Enrichment-Fingerprint
- [x] Persistente Collection-Entries werden nach vollstaendigen Scans zentral aus denselben `RomCandidate`-Daten geschrieben
- [x] Stale-Entry-Cleanup fuer nicht mehr vorhandene Dateien wird nach vollstaendigen Scans gescopet nach Roots und Extensions ausgefuehrt

Betroffene Bereiche:
- `src/Romulus.Infrastructure/Hashing`
- `src/Romulus.Infrastructure/Orchestration`
- `src/Romulus.Infrastructure/FileSystem`
- `src/Romulus.Infrastructure/Index`
- `src/Romulus.Contracts/Models`

Akzeptanz:
- [x] Delta-Erkennung nutzt Pfad, Groesse und `LastModifiedUtc` deterministisch
- [x] Hash-Cache wird nur bei gueltigem Treffer wiederverwendet
- [x] Preview und Execute erzeugen weiterhin dieselbe fachliche Wahrheit

Abhaengigkeiten:
- R1-T02

### [x] R1-T04 Run-Historie und Snapshot-Abfragen bereitstellen

Ziel: Historische Runs, Kennzahlen und spaetere Trendanalysen auf eine dauerhafte Datenbasis stellen.

Status:
- [x] Snapshot-Schreibpfad fuer GUI, CLI und API ist zentral verdrahtet
- [x] API-Lesepfad fuer persistierte Historie ist mit Pagination verdrahtet
- [x] CLI-Subcommand `history` liest dieselben persistierten Snapshots headless
- [x] Trend-/Report-Konsumenten lesen dieselbe Snapshot-Historie statt separater Nebenfiles

Betroffene Bereiche:
- `src/Romulus.Contracts/Models`
- `src/Romulus.Infrastructure/Orchestration`
- `src/Romulus.Api`

Akzeptanz:
- [x] Run-Historie wird pro Run geschrieben
- [x] Historische Kennzahlen lassen sich ohne erneuten Full-Scan lesen
- [x] API, CLI und spaetere Reports koennen dieselben Snapshot-Daten konsumieren

Abhaengigkeiten:
- R1-T02

### [x] R1-T05 Analyse, Completeness und Export auf zentrale Datenbasis umstellen

Ziel: Heuristische Nebenwahrheiten in Analyse- und Exportpfaden abbauen.

Status:
- [x] Completeness bevorzugt jetzt `RunCandidates -> CollectionIndex -> Filesystem-Fallback` statt blindem Full-Scan
- [x] Analyse- und erste Export-Ausgaben nutzen fuer `Console` den zentralen `RomCandidate.ConsoleKey` mit kontrolliertem `UNKNOWN/AMBIGUOUS`-Fallback
- [x] CLI-Export nutzt einen zentralen index-first Candidate-Read-Path mit Scope-/Fingerprint-Pruefung und explizitem Fallback auf den Run-Pfad
- [x] Standalone-Konvertierung bevorzugt bei fehlender expliziter Konsole den persistierten Collection-Index vor der Pfadheuristik
- [x] WPF-Analyse-, Export- und Trendpfade delegieren auf dieselben zentralen Collection-/Run-Resolver statt lokale Schattenlogik
- [x] Export- und Konvertierungspfade leiten ihren Zustand jetzt kontrolliert aus Run oder Collection-Index mit explizitem Fallback ab

Betroffene Bereiche:
- `src/Romulus.Infrastructure/Analysis`
- `src/Romulus.CLI`
- `src/Romulus.Api`
- `src/Romulus.Infrastructure/Orchestration`

Akzeptanz:
- [x] Analyse und Completeness leiten ihre Daten aus Run-Ergebnissen oder Index ab, nicht aus Pfadheuristiken
- [x] Export nutzt denselben fachlichen Zustand wie Run und Reports
- [x] KPI-Divergenzen zwischen Hauptpipeline und Nebenpfaden sind eliminiert

Abhaengigkeiten:
- R1-T03
- R1-T04

### [x] R1-T06 Gemeinsamen Review-Decision-Store einfuehren

Ziel: Recognition-, Sorting- und Conversion-Entscheidungen kanaluebergreifend speichern und wiederverwenden.

Status:
- [x] Persistenter Review-Decision-Store ist als gemeinsame Infrastructure ueber dieselbe `collection.db` verfuegbar
- [x] Run-Orchestrierung kann persistierte Approvals erneut anwenden und neue Approvals idempotent schreiben
- [x] API-Review-Endpoints, CLI-Runs und WPF-Runs verwenden denselben persistierten Review-Zustand

Betroffene Bereiche:
- `src/Romulus.Contracts`
- `src/Romulus.Infrastructure/Orchestration`
- `src/Romulus.Api`
- `src/Romulus.UI.Wpf`

Akzeptanz:
- [x] Review-Entscheidungen sind persistiert und idempotent erneut anwendbar
- [x] GUI, CLI und API lesen denselben Review-Zustand
- [x] Review-Status ist in Preview, Execute und Report konsistent

Abhaengigkeiten:
- R1-T02

### [x] R1-T07 Watch- und Schedule-Services in gemeinsame Infrastructure ueberfuehren

Ziel: Watch und Schedule aus dem GUI-lokalen Zustand loesen und als gemeinsame Dienste verfuegbar machen.

Status:
- [x] Gemeinsame `WatchFolderService`- und `ScheduleService`-Implementierungen tragen Debounce, Busy-Schutz und Pending-Flush zentral
- [x] WPF-Watch-Service ist auf die gemeinsame Infrastructure umgestellt
- [x] API- und CLI-Automation nutzen dieselben Watch-/Schedule-Dienste statt separater Lokallogik

Betroffene Bereiche:
- `src/Romulus.Infrastructure/Watch`
- `src/Romulus.UI.Wpf/Services`
- `src/Romulus.Api`
- `src/Romulus.CLI`

Akzeptanz:
- [x] Watch-Folder und Schedule sind nicht mehr nur an WPF gebunden
- [x] Debounce, Singleton-Schutz und Cancel-Verhalten sind zentral implementiert
- [x] Delta-Runs nutzen den Collection-Index statt eigener lokaler Logik

Abhaengigkeiten:
- R1-T03

### [x] R1-T08 Kanalintegration fuer Watch, Review und Run-Status abschliessen

Ziel: Dieselben operativen Funktionen in GUI, CLI und API verfuegbar machen, ohne Logik zu duplizieren.

Status:
- [x] CLI bietet einen gemeinsamen `watch`-Daemon-Subcommand auf Basis der Infrastructure-Services
- [x] API stellt Watch-Start/Stop/Status inklusive Owner-Binding und Pending-Status bereit
- [x] GUI, CLI und API verwenden dieselben Run-, Review- und Watch-Statusmodelle ohne lokale KPI-Neuberechnung

Betroffene Bereiche:
- `src/Romulus.Api`
- `src/Romulus.CLI`
- `src/Romulus.UI.Wpf`
- `src/Romulus.Infrastructure/Orchestration`

Akzeptanz:
- [x] CLI, API und GUI nutzen denselben Service fuer Watch und Status
- [x] Run-, Review- und Watch-Status stimmen in allen Kanaelen ueberein
- [x] Keine lokale Neuberechnung von KPIs oder Entscheidungslisten

Abhaengigkeiten:
- R1-T06
- R1-T07

### [x] R1-T09 Invarianten- und Regressionstest-Matrix fuer Foundation vervollstaendigen

Ziel: Die neuen Grundlagen gegen Datenverlust, Paritaetsfehler und nichtdeterministisches Verhalten absichern.

Status:
- [x] Index-CRUD, Snapshot-Persistenz, Delta-Rehydration und Recovery sind mit Contract-/Adapter-Tests abgedeckt
- [x] Review-Store-, Watch-/Schedule- und Trend-Services sind mit gezielten Regressionstests abgedeckt
- [x] Die Vollsuite laeuft gruen mit `7101/7101` Tests auf Stand `2026-04-01`

Betroffene Bereiche:
- `src/Romulus.Tests`
- `docs/architecture/TEST_STRATEGY.md`

Akzeptanz:
- [x] Tests decken Index-CRUD, Delta-Scans, Watch-Bursts, Review-Paritaet und Recovery ab
- [x] Negative Faelle fuer Korruption, Reparse-Points und konkurrierende Runs sind enthalten
- [x] Release-Kriterien fuer Preview/Execute/Report-Paritaet bleiben gruen

Abhaengigkeiten:
- R1-T03
- R1-T06
- R1-T07

## Release-Exit

- [x] Delta-Scans, Hash-Cache und Run-Historie sind produktiv nutzbar
- [x] Analyse, Completeness und Export verwenden keine konkurrierende Wahrheit mehr
- [x] Watch und Schedule sind in GUI, CLI und API nutzbar
- [x] Review-Zustand ist persistent und kanaluebergreifend identisch
- [x] Foundation-Tests fuer Invarianten und Recovery sind vorhanden
