# Romulus — Archiv

Dieses Verzeichnis enthält **abgeschlossene, veraltete** und **historische** Dokumente.
Nichts hier ist aktiv zum Abarbeiten gedacht.

Aktive Referenzdoku → [`docs/`](../docs/README.md)
Aktive Pläne → [`plan/`](../plan/README.md)

---

## Ordnerstruktur

| Ordner | Inhalt | Beschreibung |
|--------|--------|--------------|
| [`audits/`](audits/) | Abgeschlossene Audits & Reviews | Code-Reviews, Security-Audits, Bug-Audits, Deep-Dive-Analysen |
| [`completed/`](completed/) | Erledigte Tracker & Checklisten | Abgehakte Migrations-/Redesign-/Release-Checklisten |
| [`legacy/`](legacy/) | PowerShell-Ära Dokumente | Alte Strategie-/Requirements-Docs aus der archivierten PS-Version |
| [`powershell/`](powershell/) | Legacy PowerShell-Codebase | Archivierter PowerShell-Quellcode (nicht mehr aktiv) |

---

## audits/ — 16 Dokumente

| Datei | Datum | Beschreibung |
|-------|-------|--------------|
| `FULL_TOOL_AUDIT_2026-03-18.md` | 2026-03-18 | Kompletter Tool-Audit (production-ready, 3273 Tests grün) |
| `2026-03-18-api-security-review.md` | 2026-03-18 | API Security Review (3 CRITs + 4 HIGHs — alle gefixt) |
| `FINAL_CONSOLIDATED_AUDIT.md` | 2026-03-16 | Finaler konsolidierter Audit (9 Hauptprobleme) |
| `2026-03-16-audit-forensics-review.md` | 2026-03-16 | Forensics Review: Audit/Undo/Rollback |
| `deep-dive-analysis-v2.md` | 2026-03-14 | Full-scope Code Review (0 Critical, 14 High, 18 Med, 11 Low) |
| `deep-dive-analysis-v2-criteria-9-22.md` | 2026-03-14 | Deep-Dive Kriterien 9–22 |
| `feature-deep-dive-remaining-audit-3.md` | 2026-03-13 | Deep-Dive Runde 3: Restliche Befunde |
| `feature-deep-dive-ux-ui-audit-2.md` | 2026-03-13 | Deep-Dive Runde 2: UX/UI-Befunde |
| `feature-deep-dive-bug-audit-1.md` | 2026-03-12 | Deep-Dive Runde 1: Bug-Audit |
| `consolidated-bug-audit.md` | 2026-03-12 | Konsolidierter Bug-Audit (212 Findings) |
| `alle-befunde-master.md` | 2026-03-12 | Master-Index aller 212 Audit-Findings |
| `BUG_AUDIT_TRACKER.md` | 2026-03-11 | Bug-Tracker (P0–P3 Kategorisierung) |
| `feature-restluecke-audit-1.md` | — | Restlücken-Audit |
| `refactor-wpf-gui-ux-audit-1.md` | — | WPF GUI/UX Refactor-Audit |
| `gui-ux-deep-audit.md` | — | GUI/UX Deep-Audit |
| `deep-dive-analysis-v1.md` | — | Deep-Dive v1 (superseded by v2) |

## completed/ — 4 Dokumente

| Datei | Beschreibung |
|-------|--------------|
| `MIGRATION_TRACKING_CHECKLIST.md` | C#-Migration Checklist (alle ✅) |
| `TRACKING_CHECKLIST.md` | Release-Blocker Checklist (alle ✅) |
| `GUI-REDESIGN-TRACKER.md` | GUI-Redesign Tracker (7 Sprints, abgeschlossen) |
| `AUDIT_LOG_FORMAT.md` | Audit-Log Format Spezifikation (implementiert) |

## legacy/ — 7 Dokumente

| Datei | Beschreibung |
|-------|--------------|
| `01-PRODUCT-STRATEGY-ROADMAP.md` | PS-Ära Produkt-Roadmap |
| `02-PRODUCT-REQUIREMENTS.md` | PS-Ära Product Requirements |
| `03-FEATURE-IMPLEMENTATION-GUIDE.md` | PS-Ära Feature-Guide |
| `PLUGIN_DEVELOPER.md` | Plugin-Developer Guide (PS-Ära, C#-Plugin-System geplant) |
| `PRODUCT_STRATEGY_2026.md` | Redirects zu .instructions.md (superseded) |
| `split-feature-command-service.ps1` | PS Migrations-Script |
| `split-feature-service.ps1` | PS Migrations-Script |
