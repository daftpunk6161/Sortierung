# Romulus Product Roadmap Execution

Stand: 2026-04-01

Dieses Dokument ist der operative Einstiegspunkt fuer die Ausfuehrung der Produkt-Roadmap.
Die Detailarbeit wird in den verlinkten Release-Plaenen verfolgt.

## Release-Reihenfolge

- [x] R1 Foundation
- [ ] R2 Productization
- [ ] R3 Reach

## Abhaengigkeiten

- [ ] R1 ist Grundlage fuer R2
- [ ] R1 ist Grundlage fuer R3
- [ ] R2 kann nur teilweise parallel zu R3 laufen
- [ ] Conversion-Erweiterungen laufen erst nach gesichertem Review-, Verify- und Rollback-Pfad

## Release-Plaene

- [x] [R1 Foundation Execution](r1-foundation-execution.md)
- [ ] [R2 Productization Execution](r2-productization-execution.md)
- [ ] [R3 Reach Execution](r3-reach-execution.md)

## Aktuelle Prioritaet

- [x] R1-T01 Index-Vertrag und Datenmodell festziehen
- [x] R1-T02 Index-Adapter, Migration und Recovery umsetzen
- [x] R1-T03 Delta-Scan-Integration in Scanner und Orchestrierung anbinden
  Persistenter Hash-Cache, scannerseitige Dateimetadaten, Delta-Rehydration, Candidate-Persistenz und Stale-Entry-Cleanup laufen jetzt ueber denselben Collection-Index.
- [x] R1-T04 Run-Snapshot-Queries und Verlaufsabfragen abschliessen
  API, CLI und Trend-/Report-Konsumenten lesen jetzt dieselbe persistierte Snapshot-Historie.
- [x] R1-T05 Analyse, Completeness und Export auf zentrale Datenbasis umstellen
  Completeness, Export, WPF-Analyse und Standalone-Conversion nutzen jetzt gemeinsame Candidate-/Index-Resolver mit explizitem Fallback.
- [x] R1-T06 Gemeinsamen Review-Decision-Store einfuehren
  Persistierte Review-Approvals werden in GUI, CLI und API ueber dieselbe Infrastructure gelesen und geschrieben.
- [x] R1-T07 Watch- und Schedule-Services in gemeinsame Infrastructure ueberfuehren
  Debounce, Pending-Flush und Busy-Schutz liegen jetzt in gemeinsamen Infrastructure-Diensten.
- [x] R1-T08 Kanalintegration fuer Watch, Review und Run-Status abschliessen
  API, CLI und GUI sind auf dieselben Watch-, Review- und Statusmodelle verdrahtet.
- [x] R1-T09 Invarianten- und Regressionstest-Matrix fuer Foundation vervollstaendigen
  Vollsuite grün: `7101/7101` Tests auf Stand `2026-04-01`.

## Parked

- [ ] Community-Kataloge erst nach stabiler Profil- und Indexbasis neu bewerten
- [ ] Erweiterte Collection Intelligence erst nach belastbarer Run-Historie planen

## Bewusst nicht verfolgen

- Kein Cross-Platform-Desktop-Rewrite vor Web-Frontend
- Keine neue Schattenlogik fuer KPIs, Status oder Export
- Keine aggressive Conversion-Ausweitung ohne Verifikation und Rollback
