# Produktstrategie 2026 (Q2–Q4)

Stand: 2026-03-03
Owner: Product + Engineering

## 1) Zielsegmentierung

### Segment A — Solo Curator
- Profil: Einzelanwender mit 1–3 ROM-Sammlungen, Fokus auf Konsistenz und Sicherheit.
- Hauptnutzen: Safe DryRun, nachvollziehbare Reports, einfacher Wizard.
- Primärer KPI: Time-to-safe-run.

### Segment B — Collector Pro
- Profil: große Multi-Root-Bestände, intensive DAT-Nutzung, regelmäßige Delta-Läufe.
- Hauptnutzen: Skalierung, adaptive Concurrency, Plugin-Workflows.
- Primärer KPI: Laufzeit pro 100k Dateien.

### Segment C — Team/Archiv
- Profil: mehrere Betreiber, Audit-/Compliance-Anforderungen.
- Hauptnutzen: Signierte Policies, API-Contracts, Artefakt-Nachvollziehbarkeit.
- Primärer KPI: Audit-Replay-Erfolgsrate.

## 2) Preisexperiment (90 Tage)

- Modell: Feature-basierte Staffelung (Free / Pro / Team).
- Hypothese H1: Team-Funktionen erhöhen Conversion bei Segment C.
- Hypothese H2: Performance-/Automationsfeatures erhöhen Retention bei Segment B.
- Messung:
  - Conversion (Install -> aktive Runs > 3 pro Woche)
  - Retention (4-Wochen-Retention)
  - Rollback-Rate nach Move

## 3) Feature-Gates (Produktlinien)

### Free
- GUI DryRun, Basis-Reports, grundlegende Dedupe-Regeln.

### Pro
- Erweiterte Presets, Plugin-Trust-Policy, erweiterte Export-/Audit-Optionen.

### Team
- API-Automation, Approval-orientierte Templates, signaturbasierte Governance.

## 4) 12-Monats-Roadmap mit KPI-Zielen

### Q2 2026 — Stabilisierung & UX
- Ziele:
  - Time-to-safe-run median < 10 Minuten.
  - UI-Fehlerabbrüche pro Run < 2%.
- Deliverables:
  - Wizard v1, Action-Hints im Preflight, Safe-Run-Shortcuts.

### Q3 2026 — Skalierung & API
- Ziele:
  - P95 Laufzeitstabilität ohne Regressionen.
  - OpenAPI-Drift = 0 ungeklärte Abweichungen.
- Deliverables:
  - Adaptive Worker-Policy, API v1-Konvention, Contract-Gates.

### Q4 2026 — Teamfähigkeit & Governance
- Ziele:
  - Audit-Replay-Erfolgsrate > 99%.
  - Rollback-Erfolgsrate > 99.5%.
- Deliverables:
  - Team-Profiles, Approvals, strengere Policy-Gates.
