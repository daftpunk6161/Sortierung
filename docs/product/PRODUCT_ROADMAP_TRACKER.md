# Romulus Product Roadmap Tracker

Stand: 2026-04-01

Dieses Dokument uebersetzt die Produktanalyse in eine trackbare Roadmap mit Releases, Epics und Exit-Kriterien.
Die Reihenfolge folgt den Projektprioritaeten: Korrektheit, Determinismus, Sicherheit, Testbarkeit, Wartbarkeit.

## Umsetzungsplaene

- [ ] [Master Execution Plan](../../plan/product-roadmap-execution.md)
- [ ] [R1 Foundation Execution](../../plan/r1-foundation-execution.md)
- [ ] [R2 Productization Execution](../../plan/r2-productization-execution.md)
- [ ] [R3 Reach Execution](../../plan/r3-reach-execution.md)

## Priorisierung

- R1 Foundation
- R2 Productization
- R3 Reach

## R1 Foundation

Ziel: Eine dauerhafte fachliche Wahrheit fuer Sammlung, Delta-Runs, Reviews und Automation herstellen.
Status: abgeschlossen am `2026-04-01`

### Release-Track

- [x] C1 Persistenter Collection Index auf Basis von [`C1-persistent-collection-index.md`](../epics/C1-persistent-collection-index.md)
- [x] Analyse-, Completeness- und Export-Pfade auf dieselbe Datenbasis ziehen
- [x] C2 Watch Folder / Scheduled Runs auf Basis von [`C2-watch-folder-scheduled-runs.md`](../epics/C2-watch-folder-scheduled-runs.md)
- [x] Gemeinsames Review Center fuer Recognition, Sorting und Conversion

### Exit-Kriterien

- [x] Delta-Scans und Hash-Cache sind produktiv nutzbar
- [x] Analyse, Completeness und Export erzeugen keine konkurrierenden Wahrheiten mehr
- [x] Watch und Schedule sind nicht mehr GUI-lokal, sondern ueber gemeinsame Services nutzbar
- [x] GUI, CLI und API bilden denselben Review- und Run-Status ab
- [x] Preview, Execute und Report bleiben konsistent

### Arbeitspakete

- [x] Index-Datenmodell, Persistenzpfad und Migrationsstrategie festziehen
- [x] Persistenten Hash-Cache auf den Collection-Index umstellen
- [x] Scanner- und Orchestrierungsintegration gegen den persistenten Index anbinden
- [x] Persistierte Run-Historie fuer API und CLI verfuegbar machen
- [x] Delta-Erkennung fuer Re-Runs verfuegbar machen
- [x] Stale-Entry-Cleanup fuer nicht mehr vorhandene Collection-Index-Pfade ergaenzen
- [x] Completeness auf `RunCandidates -> CollectionIndex -> Filesystem-Fallback` umstellen
- [x] Console-Aufloesung in Analyse- und ersten Export-Ausgaben auf den zentralen Candidate-Zustand umstellen
- [x] CLI-Export auf einen zentralen index-first Candidate-Read-Path mit hartem Scope-/Fingerprint-Fallback umstellen
- [x] Standalone-Konvertierung bei fehlender expliziter Konsole auf Collection-Index-first umstellen
- [x] Analyse- und Exportpfade weiter von heuristischen Nebenwahrheiten loesen
- [x] Watch/Schedule aus WPF in gemeinsame Infrastructure ueberfuehren
- [x] API- und CLI-Steuerung fuer Watch/Schedule definieren
- [x] Review-Entscheidungen kanaluebergreifend speichern und wiederverwenden
- [x] Invarianten- und Regressionstests fuer Index, Delta, Review und Paritaet ergaenzen

## R2 Productization

Ziel: Die starke Kernlogik in gefuehrte, wiederholbare Produktablaeufe mit geringerer Bedienkomplexitaet ueberfuehren.

### Release-Track

- [ ] C3 Guided Workflows auf Basis von [`C3-guided-workflows.md`](../epics/C3-guided-workflows.md)
- [ ] C5 Frontend Metadata Export auf Basis von [`C5-frontend-metadata-export.md`](../epics/C5-frontend-metadata-export.md)
- [ ] C6 Community Profiles / Rule Packs auf Basis von [`C6-community-profiles.md`](../epics/C6-community-profiles.md)
- [ ] Run-Diff, Trend-Reports und Storage-Insights ergaenzen

### Exit-Kriterien

- [ ] Einfache Standardszenarien sind ohne Expertenwissen fuehrbar
- [ ] Export nach RetroArch, LaunchBox, EmulationStation und Playnite ist reproduzierbar abbildbar
- [ ] Profile sind validierbar, versionierbar und zwischen Kanaelen wiederverwendbar
- [ ] Historische Runs sind fuer Nutzer und Operatoren vergleichbar

### Arbeitspakete

- [ ] Wizard-Szenarien gegen bestehende RunOptions und Review-Flows mappen
- [ ] Guided Workflows gegen dieselbe RunProjection anbinden
- [ ] Exportmodell aus Run-Ergebnissen oder Collection Index ableiten
- [ ] Exporter pro Frontend mit formatgerechter Validierung bereitstellen
- [ ] Profilformat, Validierung und Built-in-Profile definieren
- [ ] Profile in GUI, CLI und API ohne Schattenlogik verdrahten
- [ ] Run-Vergleich und Trendmodell auf Run-Historie aufsetzen
- [ ] Regressionstests fuer Wizard-Output, Export-Paritaet und Profil-Import ergaenzen

## R3 Reach

Ziel: Die bestehende lokale Plattform auf Headless-, NAS- und Non-Windows-Szenarien ausdehnen, ohne die Kerninvarianten aufzuweichen.

### Release-Track

- [ ] C7 Web Dashboard auf Basis von [`C7-web-dashboard.md`](../epics/C7-web-dashboard.md)
- [ ] NAS-/Server-Paketierung und dokumentierter Headless-Betrieb
- [ ] C4 ECM / NKit Support auf Basis von [`C4-ecm-nkit-format-support.md`](../epics/C4-ecm-nkit-format-support.md)

### Exit-Kriterien

- [ ] Web-Dashboard nutzt ausschliesslich die bestehende API als fachliche Quelle
- [ ] Headless-Betrieb ist dokumentiert und sicher deploybar
- [ ] Erweiterte Conversion bleibt review-pflichtig, verifizierbar und rollback-sicher

### Arbeitspakete

- [ ] Dashboard-MVP fuer Run-Start, Progress, Review, DAT-Status und Completeness schneiden
- [ ] Deployment-Modell fuer Reverse Proxy, API-Key und sichere Bindings dokumentieren
- [ ] Paketierung fuer Server-/NAS-Szenarien definieren
- [ ] ECM/NKit nur mit expliziter Verifikation, Fehler-Cleanup und Review-Flow integrieren
- [ ] Tooling, Timeout-, Hash- und Exit-Code-Absicherung fuer neue Converter erweitern
- [ ] Integrations- und Negativtests fuer Headless-Betrieb und Conversion-Fehlfaelle ergaenzen

## Parked / Bewusst spaeter

- [ ] Community-Kataloge oder Sharing erst nach stabiler lokaler Profil- und Indexbasis bewerten
- [ ] Erweiterte Collection Intelligence erst nach belastbarer Run-Historie ausbauen

## Bewusst nicht verfolgen

- Kein Cross-Platform-Desktop-Rewrite vor einem Web-Frontend
- Keine neuen Schattenlogiken fuer Analyse, Export, Dashboard oder KPIs
- Keine aggressive "convert everything"-Strategie ohne Verifikation und Rollback
- Keine Ausweitung in unsichere oder rechtlich problematische Decrypt-/Key-Bereiche
- Kein Cloud-Backend vor stabiler lokaler Daten-, Review- und Automationsbasis
