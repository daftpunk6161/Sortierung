# Romulus Product Roadmap Execution

Stand: 2026-04-01

Dieses Dokument ist der operative Einstiegspunkt fuer die Ausfuehrung der Produkt-Roadmap.
Die Detailarbeit wird in den verlinkten Release-Plaenen verfolgt.

## Release-Reihenfolge

- [ ] R1 Foundation
- [ ] R2 Productization
- [ ] R3 Reach

## Abhaengigkeiten

- [ ] R1 ist Grundlage fuer R2
- [ ] R1 ist Grundlage fuer R3
- [ ] R2 kann nur teilweise parallel zu R3 laufen
- [ ] Conversion-Erweiterungen laufen erst nach gesichertem Review-, Verify- und Rollback-Pfad

## Release-Plaene

- [ ] [R1 Foundation Execution](r1-foundation-execution.md)
- [ ] [R2 Productization Execution](r2-productization-execution.md)
- [ ] [R3 Reach Execution](r3-reach-execution.md)

## Aktuelle Prioritaet

- [x] R1-T01 Index-Vertrag und Datenmodell festziehen
- [x] R1-T02 Index-Adapter, Migration und Recovery umsetzen
- [ ] R1-T03 Delta-Scan-Integration in Scanner und Orchestrierung anbinden
  Persistenter Hash-Cache wurde auf den Collection-Index umgestellt; Delta-Erkennung im Scanner ist noch offen.
- [ ] R1-T04 Run-Snapshot-Queries und Verlaufsabfragen abschliessen
  API-Endpoint `/runs/history` und CLI-Subcommand `history` sind geliefert; weitere Reports/Trend-Konsumenten sind noch offen.

## Parked

- [ ] Community-Kataloge erst nach stabiler Profil- und Indexbasis neu bewerten
- [ ] Erweiterte Collection Intelligence erst nach belastbarer Run-Historie planen

## Bewusst nicht verfolgen

- Kein Cross-Platform-Desktop-Rewrite vor Web-Frontend
- Keine neue Schattenlogik fuer KPIs, Status oder Export
- Keine aggressive Conversion-Ausweitung ohne Verifikation und Rollback
