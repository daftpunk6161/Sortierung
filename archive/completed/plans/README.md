# Romulus — Aktive Implementierungspläne

Dieses Verzeichnis enthält **actionable Implementation Plans** — konkrete Aufgabenpakete, die abzuarbeiten sind.

Fertige Pläne werden nach Abschluss nach [`archive/completed/`](../archive/completed/) verschoben.
Permanente Referenzdoku liegt in [`docs/`](../docs/README.md).

---

## Aktive Pläne

| Plan | Feature | Status | Beschreibung |
|------|---------|--------|--------------|
| [feature-conversion-engine-1.md](feature-conversion-engine-1.md) | Conversion Engine | **Completed** | Graphbasierte Konvertierung: 65 Systeme, 76 Pfade, 5 Tools. Alle 6 Phasen abgeschlossen (Tasks 001–055 ✅) |
| [feature-benchmark-coverage-expansion-1.md](feature-benchmark-coverage-expansion-1.md) | Benchmark: Testset Expansion | **Mostly Complete** | 2.073 Einträge (Ziel 1.200+ erreicht). Offen: performance-scale.jsonl leer |
| [feature-benchmark-coverage-matrix-impl-1.md](feature-benchmark-coverage-matrix-impl-1.md) | Benchmark: Coverage Matrix | **Mostly Complete** | CoverageValidator + gates.json + CoverageGateTests implementiert |
| [feature-benchmark-evaluation-pipeline-1.md](feature-benchmark-evaluation-pipeline-1.md) | Benchmark: Eval Pipeline | **Mostly Complete** | Quality Gates M4/M6/M7/M9a, HTML-Reports, Regressions-Gate implementiert |
| [feature-benchmark-testset-1.md](feature-benchmark-testset-1.md) | Benchmark: Testset System | **Mostly Complete** | JSONL-Testset (8 Dateien), GroundTruthLoader, EvaluationRunner, CI-Gate implementiert |

---

## Konventionen

- **Dateiname:** `feature-<bereich>-<name>-<version>.md`
- **Status im Plan-Header pflegen** (Planned / In Progress / Done)
- Erledigte Pläne → `archive/completed/` verschieben
- Jeder Plan enthält: Ziel, Phasen/Tasks, Abhängigkeiten, Testanforderungen
