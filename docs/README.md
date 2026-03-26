# Romulus — Dokumentation

Dieses Verzeichnis enthält die **permanente Referenzdokumentation** des Projekts.
Actionable Pläne (Implementation Plans) liegen in [`plan/`](../plan/README.md).
Abgeschlossene und historische Dokumente liegen in [`archive/`](../archive/README.md).

---

## Ordnerstruktur

| Ordner | Inhalt | Beschreibung |
|--------|--------|--------------|
| [`architecture/`](architecture/) | Technische Specs & Systemarchitektur | Architektur-Map, API-Contracts, Teststrategien, OpenAPI-Spec, Benchmark-Design, Konvertierungs-Matrix |
| [`adrs/`](adrs/) | Architecture Decision Records | Nummerierte ADRs (0001–0017) — historische und aktive Architekturentscheidungen |
| [`guides/`](guides/) | Benutzer- & Entwicklerhandbücher | User Handbook, FAQ, Naming Guide, Review Checklist |
| [`product/`](product/) | Produkt-Analyse & -Entscheidungen | Conversion Product Model, Category Prefilter Audit, Confidence Gate Redesign |
| [`ux/`](ux/) | UX/GUI-Design & Accessibility | GUI-Redesign-Specs, Redesign-Analyse, Narrator/A11y-Testplan |
| [`screenshots/`](screenshots/) | UI-Screenshots | Referenz-Screenshots der Oberfläche |

---

## Wichtige Einstiegspunkte

| Dokument | Zweck |
|----------|-------|
| [ARCHITECTURE_MAP.md](architecture/ARCHITECTURE_MAP.md) | Systemarchitektur-Übersicht (Clean Architecture, Dependency Flow) |
| [USER_HANDBOOK.md](guides/USER_HANDBOOK.md) | Benutzerhandbuch für GUI / CLI / API |
| [TEST_STRATEGY.md](architecture/TEST_STRATEGY.md) | Teststrategie (5200+ xUnit Tests, Test-Pyramide) |
| [openapi.yaml](architecture/openapi.yaml) | OpenAPI-Spezifikation der REST API |
| [REVIEW_CHECKLIST.md](guides/REVIEW_CHECKLIST.md) | PR-Review-Checkliste |

---

## Konventionen

- **Keine losen Dateien im docs/-Root** — jede Datei gehört in einen der Unterordner.
- **Permanente Referenz** gehört hierher. Snapshots/Audits, die veralten, gehören nach `archive/audits/`.
- **Pläne zum Abarbeiten** gehören nach `plan/`, nicht nach `docs/`.
- ADRs werden fortlaufend nummeriert (0001–NNNN) und nie gelöscht, nur als "Superseded" markiert.
