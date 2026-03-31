# Romulus — Dokumentation

Dieses Verzeichnis enthält die **permanente Referenzdokumentation** des Projekts.
Actionable Pläne (Implementation Plans) liegen in [`plan/`](../plan/README.md).
Abgeschlossene und historische Dokumente liegen in [`archive/`](../archive/README.md).

---

## Ordnerstruktur

| Ordner | Inhalt | Beschreibung |
|--------|--------|--------------|
| [`architecture/`](architecture/) | Technische Specs & Systemarchitektur | Architektur-Map, API-Contracts, Teststrategien, OpenAPI-Spec, Benchmark-Design, Konvertierungs-Matrix |
| [`adrs/`](adrs/) | Architecture Decision Records | Nummerierte ADRs (0001–0019) — historische und aktive Architekturentscheidungen |
| [`guides/`](guides/) | Benutzer- & Entwicklerhandbücher | User Handbook, FAQ, Review Checklist |
| [`product/`](product/) | Produkt-Analyse & -Entscheidungen | Conversion Product Model, Roadmap Tracker |
| [`ux/`](ux/) | UX/GUI-Design & Accessibility | GUI-Redesign-Specs, Redesign-Analyse, Narrator/A11y-Testplan |
| [`screenshots/`](screenshots/) | UI-Screenshots | Referenz-Screenshots der Oberfläche |

---

## Wichtige Einstiegspunkte

| Dokument | Zweck |
|----------|-------|
| [ARCHITECTURE_MAP.md](architecture/ARCHITECTURE_MAP.md) | Systemarchitektur-Übersicht (Clean Architecture, Dependency Flow) |
| [USER_HANDBOOK.md](guides/USER_HANDBOOK.md) | Benutzerhandbuch für GUI / CLI / API |
| [TEST_STRATEGY.md](architecture/TEST_STRATEGY.md) | Teststrategie (6900+ xUnit Tests, Test-Pyramide) |
| [openapi.yaml](architecture/openapi.yaml) | OpenAPI-Spezifikation der REST API |
| [PRODUCT_ROADMAP_TRACKER.md](product/PRODUCT_ROADMAP_TRACKER.md) | Trackbare Produkt-Roadmap mit Releases, Epics und Exit-Kriterien |
| [REVIEW_CHECKLIST.md](guides/REVIEW_CHECKLIST.md) | PR-Review-Checkliste |

---

## Konventionen

- **Keine losen Dateien im docs/-Root** — jede Datei gehört in einen der Unterordner.
- **Permanente Referenz** gehört hierher. Snapshots/Audits, die veralten, gehören nach `archive/audits/`.
- **Pläne zum Abarbeiten** gehören nach `plan/`, nicht nach `docs/`.
- ADRs werden fortlaufend nummeriert (0001–NNNN) und nie gelöscht, nur als "Superseded" markiert.
