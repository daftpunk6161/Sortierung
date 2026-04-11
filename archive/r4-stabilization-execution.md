# R4 Stabilization Execution

Stand: 2026-04-01

Ziel: Nach R1-R3 die bestehende Produktbreite real belastbar machen, Release-Hygiene schliessen und die aktiven Kanaele gegen echte Betriebs- und Bedienfehler haerten, bevor neue groessere Produktbreite folgt.

Status: abgeschlossen am `2026-04-01`

## Letztes Update (2026-04-01)

- Reproduzierbare Release-Smokes liegen jetzt unter `deploy/smoke/` und werden auch im Release-Workflow genutzt.
- Headless-Betrieb ist ueber einen echten HTTP-Smoke gegen die gebaute API verifiziert, nicht nur ueber Testhosts.
- Conversion-/Tooling-Grenzen sind auf den aktuellen Produktstand triagiert; `ECM` bleibt fail-closed, `NKit` bleibt gepinnt und review-pflichtig.
- Benchmark-Manifest, Integrity-Gate und Governance laufen jetzt auf demselben angereicherten Manifestformat.
- GUI-/A11y-Basics sind ueber strukturelle Accessibility-Smokes und einen dokumentierten Operator-Spot-Check abgesichert.

## Nicht-Scope

- Kein neues grosses Nutzerfeature
- Kein neuer Entry Point
- Keine neue fachliche Parallelpipeline neben bestehender Orchestrierung

## Tickets

### [x] R4-T01 Release-Smoke-Matrix fuer GUI, CLI, API und Dashboard festziehen

Ziel: Eine kleine, wiederholbare Release-Matrix fuer die wichtigsten End-to-End-Flows definieren und reproduzierbar machen.

Betroffene Bereiche:
- `docs/guides`
- `docs/architecture`
- `src/Romulus.Tests`

Akzeptanz:
- Kritische Flows pro Kanal sind als Smoke-Matrix dokumentiert
- Build-, Test- und Kanal-Smokes koennen vor Release reproduzierbar gefahren werden
- Keine konkurrierenden manuellen Release-Checklisten

Abhaengigkeiten:
- R3 abgeschlossen

### [x] R4-T02 Headless-/NAS-Betriebspruefung mit echten Pfad- und Tool-Szenarien absichern

Ziel: Den Reach-Stack gegen reale Betriebsfaelle statt nur gegen Testhosts absichern.

Betroffene Bereiche:
- `deploy/docker`
- `docs/guides/HEADLESS_DEPLOYMENT.md`
- `src/Romulus.Api`

Akzeptanz:
- Headless-Smokes pruefen AllowedRoots, API-Key, PublicBaseUrl und Reverse-Proxy-Nutzung
- reale Tool-Pfade und fehlende Tools verhalten sich fail-closed
- keine stillen Unterschiede zwischen Testhost und dokumentiertem Betrieb

Abhaengigkeiten:
- R4-T01

### [x] R4-T03 Conversion- und Tooling-Haertung entlang aktiver Auditpunkte abschliessen

Ziel: Die verbleibenden produktionsrelevanten Conversion-Kanten geordnet abbauen.

Betroffene Bereiche:
- `docs/architecture/CONVERSION_DOMAIN_AUDIT.md`
- `src/Romulus.Infrastructure/Conversion`
- `src/Romulus.Tests/Conversion`

Akzeptanz:
- aktive, nicht rein historische Auditpunkte sind triagiert
- release-relevante Conversion-Risiken sind behoben oder bewusst dokumentiert
- Tool-Integration bleibt hash-/timeout-/cleanup-gesichert

Abhaengigkeiten:
- R4-T02

### [x] R4-T04 Benchmark-/Dataset-Audit und Baseline-Hygiene aktualisieren

Ziel: Die Erkennungs- und Benchmark-Basis vor weiterem Feature-Bau bewusst refreshen.

Betroffene Bereiche:
- `docs/architecture/DATASET_AUDIT_PROCESS.md`
- `docs/guides/BENCHMARK_AUDIT_GOVERNANCE.md`
- Benchmark-/Testset-Bereiche unter `src/Romulus.Tests`

Akzeptanz:
- aktueller Audit-Lauf ist dokumentiert
- offene Datensatz-/Gate-Luecken sind triagiert
- Baseline- und Governance-Stand passen wieder zum Produktstand

Abhaengigkeiten:
- R4-T01

### [x] R4-T05 Strukturelle UX-/A11y-Smokes und operatorischen Spot-Check fuer kritische GUI-Flows absichern

Ziel: Die produktisierten GUI-Flows ueber reproduzierbare Accessibility-Smokes absichern und den verbleibenden Narrator-/Keyboard-Spot-Check dokumentiert halten.

Betroffene Bereiche:
- `docs/ux/narrator-testplan.md`
- `src/Romulus.UI.Wpf`

Akzeptanz:
- strukturelle A11y-Smokes fuer kritische Flows sind automatisiert reproduzierbar
- erkannte A11y- oder Focus-Probleme sind behoben oder klar dokumentiert
- operatorischer Narrator-/Keyboard-Spot-Check ist dokumentiert statt still vorausgesetzt

Abhaengigkeiten:
- R4-T01

### [x] R4-T06 Release-Check, Changelog-Hygiene und offene Produktkanten sauber schneiden

Ziel: Einen sauberen Abschluss herstellen, auf dem R5 direkt aufsetzen kann.

Betroffene Bereiche:
- `docs/product`
- `plan`
- Release-Notes / Changelog-nahe Doku

Akzeptanz:
- R4-Ergebnis ist im Tracker nachgezogen
- verbleibende Risiken fuer den naechsten Feature-Block sind klar benannt
- R5 kann ohne weitere Roadmap-Klaerung starten

Abhaengigkeiten:
- R4-T02
- R4-T03
- R4-T04
- R4-T05

## Release-Exit

- [x] Kritische Kanal-Smokes sind dokumentiert und einmal erfolgreich durchlaufen
- [x] Headless-/NAS-Betrieb ist praktisch und nicht nur theoretisch verifiziert
- [x] Conversion-/Tooling-Risiken sind fuer den aktuellen Release-Stand sauber triagiert
- [x] Benchmark-/Dataset-Basis ist aktuell genug fuer den naechsten Produktblock
- [x] GUI-Bedien- und A11y-Basics sind fuer die Hauptfluesse geprueft
