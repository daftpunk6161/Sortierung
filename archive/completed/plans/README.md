# Romulus — Archivierte Implementierungsplaene

Dieses Verzeichnis enthält **abgeschlossene historische Implementierungsplaene**.
Nichts hier ist mehr aktiv zum Abarbeiten gedacht.

Aktive Plaene liegen in [`plan/`](../../../plan/README.md).
Permanente Referenzdoku liegt in [`docs/`](../../../docs/README.md).

---

## Archivierte Plaene

| Plan | Feature | Status | Beschreibung |
|------|---------|--------|--------------|
| [feature-conversion-engine-1.md](feature-conversion-engine-1.md) | Conversion Engine | **Completed** | Graphbasierte Konvertierung: 65 Systeme, 76 Pfade, 5 Tools. Alle 6 Phasen abgeschlossen (Tasks 001–055 ✅) |
| [feature-avalonia-migration-spike-1.md](feature-avalonia-migration-spike-1.md) | Avalonia Migration Spike | **Completed** | Technischer Spike inkl. Umsetzungsstand: Avalonia Basis (Start/Progress/Result), Exportpfade, Single-Instance-Guard und grüne Verifikation (Tests/Build) |
| [feature-benchmark-coverage-expansion-1.md](feature-benchmark-coverage-expansion-1.md) | Benchmark: Testset Expansion | **Mostly Complete** | 2.073 Einträge (Ziel 1.200+ erreicht). Offen: performance-scale.jsonl leer |
| [feature-benchmark-coverage-matrix-impl-1.md](feature-benchmark-coverage-matrix-impl-1.md) | Benchmark: Coverage Matrix | **Mostly Complete** | CoverageValidator + gates.json + CoverageGateTests implementiert |
| [feature-benchmark-evaluation-pipeline-1.md](feature-benchmark-evaluation-pipeline-1.md) | Benchmark: Eval Pipeline | **Mostly Complete** | Quality Gates M4/M6/M7/M9a, HTML-Reports, Regressions-Gate implementiert |
| [feature-benchmark-testset-1.md](feature-benchmark-testset-1.md) | Benchmark: Testset System | **Mostly Complete** | JSONL-Testset (8 Dateien), GroundTruthLoader, EvaluationRunner, CI-Gate implementiert |
| [dat-first-recognition-redesign-1.md](dat-first-recognition-redesign-1.md) | DAT-first Recognition Redesign | **Completed** | Vollständige DAT-first Zielarchitektur mit Evidence-Tiers, MatchKind, Family-Routing, Decision-Resolver und Quality-Gates |
| [r1-t01-index-contract-technical-plan.md](r1-t01-index-contract-technical-plan.md) | R1: Collection Index Contract | **Completed** | Historischer Detailplan fuer den Collection-Index-Vertrag und die Modellinvarianten |

---

## Konventionen

- **Dateiname:** `feature-<bereich>-<name>-<version>.md`
- **Status im Plan-Header spiegelt den historischen Endstand**
- Neue aktive Plaene gehoeren nach [`plan/`](../../../plan/README.md), nicht in dieses Archiv
- Jeder Plan enthält: Ziel, Phasen/Tasks, Abhängigkeiten, Testanforderungen
