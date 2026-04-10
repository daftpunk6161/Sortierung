# Romulus Product Roadmap Tracker

Stand: 2026-04-01

Dieses Dokument uebersetzt die Produktanalyse in eine trackbare Roadmap mit Releases, Epics und Exit-Kriterien.
Die Reihenfolge folgt den Projektprioritaeten: Korrektheit, Determinismus, Sicherheit, Testbarkeit, Wartbarkeit.

## Umsetzungsplaene

- [x] [Master Execution Plan](../../plan/product-roadmap-execution.md)
- [x] [R1 Foundation Execution](../../plan/r1-foundation-execution.md)
- [x] [R2 Productization Execution](../../plan/r2-productization-execution.md)
- [x] [R3 Reach Execution](../../plan/r3-reach-execution.md)
- [x] [R4 Stabilization Execution](../../plan/r4-stabilization-execution.md)
- [x] [R5 Collection Diff & Merge Execution](../../plan/r5-collection-diff-merge-execution.md)

## Priorisierung

- R1 Foundation
- R2 Productization
- R3 Reach
- R4 Stabilization
- R5 Collection Diff & Merge

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
Status: abgeschlossen am `2026-04-01`

### Letztes Update (2026-04-01)

- CLI-RunOptions-Mapping wurde fuer direkte `CliRunOptions`-Aufrufer rueckwaertskompatibel stabilisiert (kein Verlust expliziter Optionen in Tests/Legacy-Pfaden)
- API-Fehlerabbildung fuer profil-/workflowbasierte Sicherheitsvalidierung liefert wieder `SEC-*`-Codes statt generischem `RUN-INVALID-CONFIG`
- Collection-Index-Erzeugung im Run-Environment hat einen robusten Fallback auf temp-basierte DB-Pfade bei Lock-/IO-Konflikten
- GUI, CLI und API sind jetzt ueber gemeinsame Workflow-/Profil-Materialisierung, Frontend-Export und Trend-/Compare-Services verdrahtet
- Neue Regressionen decken `/profiles`, `/workflows`, workflow-/profilbasierte Runs, Watch-Automation, Frontend-Export, WPF-Auswahl-/Materialisierungslogik und OpenAPI-Vertraege ab
- Vollstaendiger `dotnet test`-Lauf ist gruen: `7133/7133` erfolgreich

### Release-Track

- [x] C3 Guided Workflows auf Basis von [`C3-guided-workflows.md`](../epics/C3-guided-workflows.md)
- [x] C5 Frontend Metadata Export auf Basis von [`C5-frontend-metadata-export.md`](../epics/C5-frontend-metadata-export.md)
- [x] C6 Community Profiles / Rule Packs auf Basis von [`C6-community-profiles.md`](../epics/C6-community-profiles.md)
- [x] Run-Diff, Trend-Reports und Storage-Insights ergaenzen

### Exit-Kriterien

- [x] Einfache Standardszenarien sind ohne Expertenwissen fuehrbar
- [x] Export nach RetroArch, LaunchBox, EmulationStation und Playnite ist reproduzierbar abbildbar
- [x] Profile sind validierbar, versionierbar und zwischen Kanaelen wiederverwendbar
- [x] Historische Runs sind fuer Nutzer und Operatoren vergleichbar

### Arbeitspakete

- [x] Wizard-Szenarien gegen bestehende RunOptions und Review-Flows mappen
- [x] Guided Workflows gegen dieselbe RunProjection anbinden
- [x] Exportmodell aus Run-Ergebnissen oder Collection Index ableiten
- [x] Exporter pro Frontend mit formatgerechter Validierung bereitstellen
- [x] Profilformat, Validierung und Built-in-Profile definieren
- [x] Profile in GUI, CLI und API ohne Schattenlogik verdrahten
- [x] Run-Vergleich und Trendmodell auf Run-Historie aufsetzen
- [x] Regressionstests fuer Wizard-Output, Export-Paritaet und Profil-Import ergaenzen

## R3 Reach

Ziel: Die bestehende lokale Plattform auf Headless-, NAS- und Non-Windows-Szenarien ausdehnen, ohne die Kerninvarianten aufzuweichen.
Status: abgeschlossen am `2026-04-01`

### Release-Track

- [x] C7 Web Dashboard auf Basis von [`C7-web-dashboard.md`](../epics/C7-web-dashboard.md)
- [x] NAS-/Server-Paketierung und dokumentierter Headless-Betrieb
- [x] C4 ECM / NKit Support auf Basis von [`C4-ecm-nkit-format-support.md`](../epics/C4-ecm-nkit-format-support.md)

### Exit-Kriterien

- [x] Web-Dashboard nutzt ausschliesslich die bestehende API als fachliche Quelle
- [x] Headless-Betrieb ist dokumentiert und sicher deploybar
- [x] Erweiterte Conversion bleibt review-pflichtig, verifizierbar und rollback-sicher

### Arbeitspakete

- [x] Dashboard-MVP fuer Run-Start, Progress, Review, DAT-Status und Completeness schneiden
- [x] Deployment-Modell fuer Reverse Proxy, API-Key und sichere Bindings dokumentieren
- [x] Paketierung fuer Server-/NAS-Szenarien definieren
- [x] ECM/NKit nur mit expliziter Verifikation, Fehler-Cleanup und Review-Flow integrieren
- [x] Tooling, Timeout-, Hash- und Exit-Code-Absicherung fuer neue Converter erweitern
- [x] Integrations- und Negativtests fuer Headless-Betrieb und Conversion-Fehlfaelle ergaenzen

## R4 Stabilization

Ziel: Die bestehende Produktbreite aus R1-R3 vor dem naechsten groesseren Feature-Block real belastbar machen.
Status: abgeschlossen am `2026-04-01`

### Release-Track

- [x] Release-Smoke-Matrix fuer GUI, CLI, API und Dashboard
- [x] Headless-/NAS-Betriebspruefung mit echten Pfad- und Tool-Szenarien
- [x] Conversion- und Tooling-Haertung entlang aktiver Auditpunkte
- [x] Benchmark-/Dataset-Audit und Baseline-Hygiene
- [x] Strukturelle UX-/A11y-Smokes plus dokumentierter Operator-Spot-Check fuer kritische GUI-Flows

### Exit-Kriterien

- [x] Kritische Kanal-Smokes sind dokumentiert und erfolgreich durchlaufen
- [x] Headless-/NAS-Betrieb ist praktisch verifiziert
- [x] Conversion-/Tooling-Risiken sind fuer den Release-Stand triagiert
- [x] Benchmark-/Dataset-Basis ist fuer den naechsten Produktblock aktuell genug
- [x] GUI-Bedien- und A11y-Basics sind fuer Hauptfluesse geprueft

### Arbeitspakete

- [x] Kleine Release-Smoke-Matrix definieren und verankern
- [x] Reale Headless-/Proxy-/AllowedRoots-Smokes fahren
- [x] Aktive Conversion-Auditkanten abbauen oder bewusst dokumentieren
- [x] Datensatz-/Benchmark-Audit refreshen
- [x] Kritische GUI-/A11y-Basics ueber Struktur-Smokes und Operator-Spot-Check absichern
- [x] Ergebnis sauber in Plan, Tracker und Changelog ueberfuehren

## R5 Collection Diff & Merge

Ziel: Einen sicheren, auditierbaren Produktpfad fuer den Vergleich und das Zusammenfuehren mehrerer Sammlungen schaffen.
Status: abgeschlossen am `2026-04-01`

### Letztes Update (2026-04-01)

- Compare-/Merge-Contracts liegen zentral in `Romulus.Contracts`.
- `CollectionCompareService` und `CollectionMergeService` materialisieren linke/rechte/Target-Sichten index-first mit Root-/Fingerprint-Guards statt neuer Scanner-Schattenlogik.
- Merge-Plan, Apply, Audit und Rollback laufen jetzt ueber denselben Safety-, Audit- und Rollback-Vertrag wie andere mutierende Produktpfade.
- GUI, CLI und API sind auf dieselben Compare-/Merge-Modelle verdrahtet; OpenAPI und WPF-/CLI-/API-Regressionen sind ergaenzt.
- Vollsuite grün: `7197/7197` Tests erfolgreich auf Stand `2026-04-01`.

### Release-Track

- [x] C8 Collection Diff & Merge auf Basis von [`C8-collection-diff-merge.md`](../epics/C8-collection-diff-merge.md)
- [x] Compare- und Merge-Modelle ueber denselben Collection-Index und Candidate-Resolver anbinden
- [x] Merge ueber bestehende Safety-, Audit- und Rollback-Infrastruktur fuehren
- [x] GUI-, CLI- und API-Paritaet fuer Compare und Merge sicherstellen

### Exit-Kriterien

- [x] Compare und Merge nutzen dieselbe Sammlungswahrheit wie Analyse und Export
- [x] Kein Merge schreibt ausserhalb erlaubter Roots oder ueberschreibt still
- [x] Konflikt- und Risiko-Faelle bleiben review-pflichtig
- [x] Preview, Execute und Report bleiben fuer Merge konsistent
- [x] Diff & Merge ist gegen reale Multi-Collection-Negativfaelle abgesichert

### Arbeitspakete

- [x] Compare-Vertrag, Diff-Zustaende und Merge-Plan-Modell definieren
- [x] Source-Scope-Materialisierung index-first verdrahten
- [x] Diff-Engine auf bestehende Winner-Selection setzen
- [x] Merge-Planer mit Safety- und Conflict-Regeln anbinden
- [x] Merge-Execute, Audit und Rollback ueber bestehende Infrastruktur verdrahten
- [x] GUI-, CLI- und API-Oberflaechen ohne Schattenlogik anbinden
- [x] Performance- und Scope-Haertung fuer grosse Sammlungen ergaenzen
- [x] Invarianten-, Negativ- und Paritaetstests vervollstaendigen

## Parked / Bewusst spaeter

- [ ] Community-Kataloge oder Sharing erst nach stabiler lokaler Profil- und Indexbasis bewerten
- [ ] Erweiterte Collection Intelligence erst nach belastbarer Run-Historie ausbauen

## Bewusst nicht verfolgen

- Kein Cross-Platform-Desktop-Rewrite vor einem Web-Frontend
- Keine neuen Schattenlogiken fuer Analyse, Export, Dashboard oder KPIs
- Keine aggressive "convert everything"-Strategie ohne Verifikation und Rollback
- Keine Ausweitung in unsichere oder rechtlich problematische Decrypt-/Key-Bereiche
- Kein Cloud-Backend vor stabiler lokaler Daten-, Review- und Automationsbasis
