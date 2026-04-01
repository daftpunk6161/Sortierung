# C8: Collection Diff & Merge

Status: abgeschlossen am `2026-04-01` im Release-Block `R5`

## Problem

Romulus kann heute Dateien innerhalb einer Sammlung sehr gut erkennen, deduplizieren,
verifizieren und sortieren. Es fehlt aber ein gezielter Produktpfad fuer den Vergleich
und das kontrollierte Zusammenfuehren mehrerer Sammlungen oder Roots.

Typische reale Faelle:

- Sammlung A auf externer HDD, Sammlung B auf NAS
- altes Backup gegen aktuelle Hauptsammlung vergleichen
- "Welche Konsole ist in Quelle A besser als in Quelle B?"
- "Welche Dateien sind in beiden Sammlungen vorhanden, aber fachlich nicht gleich?"

Der heutige Workaround ist, beide Quellen gemeinsam zu scannen und auf die bestehende
Dedup-Logik zu hoffen. Das liefert keine echte Diff-Ansicht, keine klare Quellzuordnung
und keinen kontrollierten Merge-Plan.

## Loesungsansatz

Ein gemeinsamer `Collection Diff & Merge`-Pfad auf Basis der bestehenden fachlichen
Wahrheit:

- Materialisierung ueber Collection Index und bestehende Candidate-/Run-Resolver
- Bewertung ueber dieselbe Dedup-/Scoring-Logik wie im restlichen Produkt
- Merge-Ausfuehrung ueber denselben Safety-, Audit- und Rollback-Vertrag
- GUI, CLI und API nutzen dieselbe Compare-/Merge-Projektion

## Fachmodell

### Compare

Die Compare-Ansicht arbeitet mit expliziten Quellen (`A`, `B`, optional `N`) und
ordnet jeden Eintrag deterministisch einem Diff-Zustand zu:

- `only-in-left`
- `only-in-right`
- `present-in-both-identical`
- `present-in-both-different`
- `left-preferred`
- `right-preferred`
- `review-required`

Die fachliche Gleichheit darf nicht nur auf Dateiname oder Pfad beruhen, sondern muss
mindestens vorhandene Hashes, Candidate-Zustand, Console-Aufloesung, Format,
Version/Region-Praeferenzen und DAT-Status beruecksichtigen.

### Merge

Der Merge-Pfad erzeugt zunaechst immer einen expliziten Merge-Plan:

- `copy-to-target`
- `move-to-target`
- `keep-existing-target`
- `skip-as-duplicate`
- `review-required`
- `blocked`

Ohne bestaetigten Plan darf kein mutierender Merge stattfinden.

## Produktregeln

1. Kein Merge ohne explizite Quellidentitaet und Root-Scope
2. Kein Schreiben ausserhalb erlaubter Roots oder des expliziten Target-Roots
3. Kein stilles Ueberschreiben bestehender Dateien
4. Preview, Execute und Report muessen dieselbe Merge-Entscheidung tragen
5. Merge-Entscheidungen nutzen dieselbe Winner-Selection wie die Deduplizierung
6. Konfliktfaelle bleiben review-pflichtig, nicht heuristisch "automatisch geloest"
7. Audit und Rollback gelten fuer jeden mutierenden Merge-Schritt
8. Quellen werden nie vor erfolgreicher Zielverifikation entfernt

## Scope

### In Scope

- Vergleich zweier expliziter Sammlungs-Sichten
- spaetere Erweiterbarkeit auf `N` Quellen ohne neues Fachmodell
- Diff-Ansicht nach Konsole, Status, Quelle und Gewinner
- Merge in ein explizites Target-Root
- Audit, Undo, Rollback und Reports
- GUI-/CLI-/API-Paritaet

### Nicht Scope

- Cloud-Sync oder kollaborativer Merge
- Arcade merged/split/non-merged Transformationen
- Metadata- oder Artwork-Merge
- automatische Konfliktloesung ohne Review
- bidirektionale Dateisynchronisation im Stil eines Backup-Tools

## Vorgesehene Komponenten

### Contracts

- `CollectionCompareRequest`
- `CollectionCompareResult`
- `CollectionSourceScope`
- `CollectionDiffEntry`
- `CollectionDiffSummary`
- `CollectionMergeRequest`
- `CollectionMergePlan`
- `CollectionMergePlanEntry`
- `CollectionMergePlanSummary`
- `CollectionMergeDecision`

### Infrastructure / Orchestration

- gemeinsamer Diff-Query-Pfad auf Basis von Collection Index und Candidate-Resolvern
- Merge-Planer mit Nutzung der bestehenden Dedup-Scoring-Logik
- Merge-Executor mit Safety-, Audit- und Rollback-Vertrag
- aktueller Compare-Read-Pfad ueber `CollectionCompareService` mit index-first Scope-Materialisierung und deterministischer Diff-Klassifikation
- aktueller Merge-Read-/Plan-/Apply-Pfad ueber `CollectionMergeService` mit konfliktbewusstem Target-Index, signiertem Audit, Rollback-Metadaten und FS-/Index-Revert bei Fehlern

### Entry Points

- GUI: Compare-Ansicht + Merge-Review ueber WPF-Feature-Commands
- CLI: `romulus diff` und `romulus merge --plan/--apply`
- API: `/collections/compare`, `/collections/merge`, `/collections/merge/apply`, `/collections/merge/rollback`

## Abhaengigkeiten

- R1 Collection Index und Run-Historie abgeschlossen
- R2 Profil-/Workflow- und Export-Paritaet abgeschlossen
- R3 Headless- und Safety-Haertung abgeschlossen
- R4 Stabilization abgeschlossen

## Risiken

- grosse Sammlungen brauchen pagination- und index-first Reads
- gleiche Dateinamen mit unterschiedlichen Inhalten duerfen nicht als "gleich" erscheinen
- gleiches Spiel in unterschiedlicher Qualitaet braucht dieselbe Winner-Selection wie im Kern
- Merge darf keine zweite, konkurrierende Kopier-/Move-Logik aufbauen

## Teststrategie

- Unit: Diff-Klassifikation, Tie-Breaker, Merge-Plan-Regeln
- Integration: Index-/Run-basierte Materialisierung, API-/CLI-/GUI-Paritaet
- Regression: Preview/Execute/Report und Winner-Selection bleiben konsistent
- Negative: Root-Verstoss, Konflikte, gleichnamige Nicht-Gleichheit, partielle Outputs

## Ergebnis

- Compare und Merge sind produktiv ueber denselben Collection-Index-, Candidate- und Dedup-Pfad verdrahtet wie Analyse und Export.
- Merge bleibt konfliktbewusst: keine stillen Overwrites, keine automatische Konfliktloesung ausserhalb klarer Keep-/Skip-/Review-Faelle.
- Audit, signierte Sidecars, Rollback und Root-Metadaten sind fuer mutierende Merge-Schritte integriert.
- GUI, CLI, API und OpenAPI nutzen dieselben Contracts; Vollsuite grün: `7197/7197` Tests auf Stand `2026-04-01`.
