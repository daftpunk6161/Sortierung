<!-- markdownlint-disable MD041 -->
<!--
PR-Template Romulus.
Bitte alle Abschnitte ausfuellen oder begruendet streichen.
-->

## Was aendert sich?

<!-- Eine bis drei Saetze, was diese PR fachlich aendert. -->

## Warum?

<!-- Bezug zu Issue, Plan-Task (z. B. T-W2-XXX) oder Bugfix. -->

## Audit-Moratorium-Check

> Im Geltungszeitraum **2026-04-28 bis 2026-05-28** (Wellen 1-3 der strategischen Reduktion) gilt ein hartes Moratorium gegen neue Audit-Dokumente, Findings-Tracker und Repo-wide Deep-Dives. Details siehe Abschnitt "Audit-Moratorium" in [AGENTS.md](../AGENTS.md).

- [ ] Diese PR fuegt **keine** neuen Audit-/Findings-/Tracker-/Deep-Dive-Dokumente hinzu.
- [ ] Falls doch: Die PR adressiert einen konkreten **P1-Sicherheits- oder Datenintegritaets-Befund** und liefert direkt den Fix (kein Sammeldokument). Begruendung:

<!-- Falls die zweite Box angekreuzt ist, hier kurz begruenden. Sonst leer lassen. -->

## Architektur-Check

- [ ] Aenderung respektiert die Schichten-Richtung Entry Points -> Infrastructure -> Core -> Contracts.
- [ ] Keine Businesslogik im WPF Code-Behind, keine I/O in `Romulus.Core`.
- [ ] Keine konkurrierenden Wahrheiten (Status / KPIs / Results / Reports) eingefuehrt.

## Identitaets-Check

> Romulus ist ein **Safe ROM Library Cleanup & Verification Tool mit Audit Trail**. Details siehe Abschnitt "Identitaets-Guardrail" in [AGENTS.md](../AGENTS.md).

Pflichtfrage:

> Schaerft diese Aenderung die Sichere-Cleanup-Identitaet (Cleanup, Verifikation, Audit, Determinismus, Sicherheit) — oder verschiebt sie Romulus in fremdes Revier?

- [ ] Diese PR schaerft die Sichere-Cleanup-Identitaet (klar Ja).
- [ ] Diese PR beruehrt **keinen** Punkt der harten Streichliste (Frontend-Builder, Scraper, Patching als Hauptfunktion, MAME-Set-Builder, Plugin/Marketplace, Telemetrie ohne Opt-in, "Coming Soon"-Stubs, nicht-aktivierte BONUS-Tasks).

Begruendung (eine bis drei Saetze, Pflicht):

<!-- Hier konkret begruenden, warum die Antwort auf die Identitaets-Frage Ja ist. -->

## Test-Nachweis

<!-- Welche Tests sichern die Aenderung ab? Pin-Tests, Regressionstests, Invarianten? -->

## Risiken / Hinweise

<!-- Bekannte Risiken, Folgearbeiten, Migrationsschritte. -->
