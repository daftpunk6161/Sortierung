# R5 Collection Diff & Merge Execution

Stand: 2026-04-01

Ziel: Einen kontrollierten Produktpfad fuer den Vergleich und das sichere Zusammenfuehren mehrerer Sammlungen schaffen, ohne neue Schattenlogik neben Collection Index, Dedup-Engine, Audit und Rollback aufzubauen.

## Letztes Update (2026-04-01)

- `R5-T01` ist abgeschlossen: kanalneutrale Compare-/Merge-Contracts liegen jetzt in `Romulus.Contracts`.
- Der Modellraum baut direkt auf `CollectionIndexEntry` auf statt auf einem zweiten Compare-Candidate-Modell.
- Diff- und Merge-Summaries sind zentral ableitbar, damit GUI/CLI/API spaeter nicht jeweils eigene Zaehlwege bauen.
- `R5-T02` ist abgeschlossen: `CollectionCompareService` materialisiert explizite Quellen index-first mit Root-/Fingerprint-Guards und speist damit bestehende Analysis-/Export-Pfade.
- `R5-T03` ist abgeschlossen: Compare klassifiziert `only-in-*`, `identical`, `different`, `preferred` und `review-required` deterministisch auf Basis von `CollectionIndexEntry`, `CollectionIndexCandidateMapper` und `DeduplicationEngine.SelectWinner`.
- `R5-T04` bis `R5-T08` sind abgeschlossen: Merge-Planung, Apply, Audit, Rollback sowie GUI-/CLI-/API-Projektionen laufen jetzt ueber denselben `CollectionIndex`-, Compare-, Audit- und Rollback-Vertrag.
- Neue Regressionen decken Materialisierung, Compare-Paging, Merge-Conflict-Regeln, Apply-/Rollback-Fehlerpfade, CLI-/API-/OpenAPI-/WPF-Paritaet sowie Root-/Allowlist-Negativfaelle ab.
- Vollsuite grün: `7197/7197` Tests erfolgreich auf Stand `2026-04-01`.

## Nicht-Scope

- Kein Cloud- oder Sync-Produkt
- Keine Arcade merged/split/non-merged Transformation
- Kein Metadata- oder Artwork-Merge
- Kein stilles Ueberschreiben oder heuristisches Auto-Merge ausserhalb klarer Review-Regeln

## Tickets

### [x] R5-T01 Compare-Vertrag, Diff-Zustaende und Merge-Plan-Modell festziehen

Ziel: Das gemeinsame Fachmodell definieren, bevor GUI/CLI/API eigene Sichtmodelle aufbauen.

Betroffene Bereiche:
- `docs/epics/C8-collection-diff-merge.md`
- `src/Romulus.Contracts`
- `src/Romulus.Contracts/Models`

Akzeptanz:
- Compare- und Merge-Modelle sind versionierbar und kanalneutral
- Diff-Zustaende sind deterministisch und fachlich klar abgegrenzt
- keine I/O-Details leaken in Contracts oder Core

Abhaengigkeiten:
- R4 abgeschlossen

### [x] R5-T02 Gemeinsame Source-Scope-Materialisierung auf Collection Index und Candidate-Resolver setzen

Ziel: Den Compare-Pfad aus derselben Sammlungswahrheit speisen wie Analyse, Export und Completeness.

Betroffene Bereiche:
- `src/Romulus.Infrastructure/Index`
- `src/Romulus.Infrastructure/Analysis`
- `src/Romulus.Infrastructure/Orchestration`

Akzeptanz:
- Compare liest index-first und nur kontrolliert fallback-basiert
- Quellen/Sammlungen sind explizit identifizierbar
- keine neue Scanner- oder Candidate-Schattenlogik entsteht

Abhaengigkeiten:
- R5-T01

### [x] R5-T03 Diff-Engine auf Basis vorhandener Hash-, Candidate- und Winner-Regeln bauen

Ziel: Unterschiede fachlich korrekt klassifizieren, statt nur Dateilisten zu vergleichen.

Betroffene Bereiche:
- `src/Romulus.Core`
- `src/Romulus.Infrastructure/Analysis`
- `src/Romulus.Tests`

Akzeptanz:
- `only-in-left/right`, `identical`, `different`, `preferred`, `review-required` sind abgedeckt
- gleiche Inputs erzeugen gleiche Diff-Ergebnisse
- Winner-Selection entspricht bestehenden Dedup-Regeln

Abhaengigkeiten:
- R5-T02

### [x] R5-T04 Merge-Planer mit Safety-, Conflict- und Review-Regeln integrieren

Ziel: Aus dem Diff einen sicheren, nachvollziehbaren Merge-Plan ableiten.

Betroffene Bereiche:
- `src/Romulus.Infrastructure/Sorting`
- `src/Romulus.Infrastructure/Orchestration`
- `src/Romulus.Infrastructure/Safety`

Akzeptanz:
- kein stilles Ueberschreiben
- Konflikte und fachlich unklare Faelle bleiben review-pflichtig
- Target-Root und erlaubte Pfade werden vor jeder Operation geprueft

Abhaengigkeiten:
- R5-T03

### [x] R5-T05 Merge-Execute, Audit und Rollback ueber bestehende mutierende Infrastruktur anbinden

Ziel: Den Merge nicht als Sonderweg, sondern ueber denselben Schutzvertrag wie andere mutierende Flows ausfuehren.

Betroffene Bereiche:
- `src/Romulus.Infrastructure/Audit`
- `src/Romulus.Infrastructure/FileSystem`
- `src/Romulus.Infrastructure/Orchestration`

Akzeptanz:
- Merge-Apply erzeugt Audit-Trail und Rollback-Daten
- Quellen werden nie vor erfolgreicher Zielverifikation entfernt
- partielle Fehler fuehren zu sauberem Ergebnis- und Cleanup-Verhalten

Abhaengigkeiten:
- R5-T04

### [x] R5-T06 GUI-, CLI- und API-Projektionen fuer Compare und Merge ohne Schattenlogik verdrahten

Ziel: Die neue Funktion in allen Kanaelen ueber dieselben Modelle sichtbar machen.

Betroffene Bereiche:
- `src/Romulus.UI.Wpf`
- `src/Romulus.CLI`
- `src/Romulus.Api`

Akzeptanz:
- GUI, CLI und API zeigen dieselben Diff-/Merge-Zahlen
- Preview, Execute und Report divergieren fachlich nicht
- Paging, Filter und Review-Zustaende sind konsistent modelliert

Abhaengigkeiten:
- R5-T03
- R5-T05

### [x] R5-T07 Performance- und Scope-Haertung fuer grosse Sammlungen ergaenzen

Ziel: Diff & Merge fuer reale NAS-/Backup-Sammlungen belastbar machen.

Betroffene Bereiche:
- `src/Romulus.Infrastructure/Index`
- `src/Romulus.Api`
- `src/Romulus.Tests`

Akzeptanz:
- Compare-/Merge-Abfragen sind paginierbar
- grosse Sammlungen erzwingen keinen unkontrollierten Vollmaterialisierungs-Pfad
- Scope-/Fingerprint-Guards verhindern fachlich falsche Vergleiche

Abhaengigkeiten:
- R5-T06

### [x] R5-T08 Invarianten-, Negativ- und Paritaetstests vervollstaendigen

Ziel: Den neuen Produktpfad release-faehig absichern.

Betroffene Bereiche:
- `src/Romulus.Tests`
- `docs/architecture/TEST_STRATEGY.md`

Akzeptanz:
- Root-Containment, Konflikte, gleichnamige Nicht-Gleichheit und Rollback sind getestet
- Preview/Execute/Report-Paritaet ist fuer Diff & Merge abgesichert
- GUI/CLI/API teilen dieselbe fachliche Wahrheit

Abhaengigkeiten:
- R5-T05
- R5-T06
- R5-T07

## Release-Exit

- [x] Compare und Merge laufen auf derselben Sammlungswahrheit wie Analyse und Export
- [x] Merge nutzt bestehende Safety-, Audit- und Rollback-Infrastruktur
- [x] GUI, CLI und API bilden denselben Diff-/Merge-Zustand ab
- [x] Konflikt- und Risk-Faelle bleiben review-pflichtig statt heuristisch automatisiert
- [x] Invarianten und Negativfaelle sind fuer reale Multi-Collection-Szenarien abgesichert
