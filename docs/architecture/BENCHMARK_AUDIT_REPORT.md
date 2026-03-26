# Benchmark & Evaluation – Documentation Audit Report

> **Stand:** Generiert am 2026-03-22  
> **Scope:** Alle Benchmark-/Evaluation-relevanten Docs, Plans, ADRs — ohne GUI/WPF  
> **Methode:** Jede Spezifikation wurde gegen tatsächliche C#-Implementierung (`src/RomCleanup.Tests/Benchmark/`) verifiziert

---

## Executive Summary

Die Benchmark-Infrastruktur ist zu **~90–95 %** implementiert. Kernkomponenten (Evaluation Runner, Ground-Truth-Loader, MetricsAggregator, Coverage Gates, Quality Gates, HTML-Report, CI-Workflow, TrendAnalyzer, Baseline-System, AntiGamingGateTests, ConfidenceCalibrationTests) existieren und sind funktional.

**Hauptlücken:**
1. ~~Manifest/Realität-Diskrepanz bei Ground-Truth-Zahlen~~ → **BEHOBEN** (manifest.json auf 2.073 korrigiert)
2. `performance-scale.jsonl` = 0 Einträge (leer)
3. ~~`AntiGamingGateTests.cs` nicht implementiert~~ → **BEHOBEN** (2 Tests: M15 + M16)
4. ~~`ConfidenceCalibrationTests.cs` als eigenständige Test-Klasse fehlt~~ → **BEHOBEN** (3 Tests: HighConf, LowConf, PerSet ECE)
5. Holdout-Set nicht als separate Datei vorhanden (kein `holdout-blind.jsonl`)
6. Quality Gates nur informativ (nicht enforced in PR-Gate)
7. Repair-Feature selbst nicht implementiert (nur Evaluator-Infrastruktur)
8. ~~Plan-Dateien veraltet (Status "Planned" obwohl Infrastruktur existiert)~~ → **BEHOBEN** (alle auf "Mostly Complete" aktualisiert)

---

## Offene Items nach Dokument

### 1. `plan/feature-benchmark-evaluation-pipeline-1.md`

Dies ist das maßgebliche Plandokument mit 4 Phasen / 66 Tasks.

| # | Sektion | Was fehlt | Priorität | Status |
|---|---------|-----------|-----------|--------|
| 1 | Phase 1 / TASK-001 | `ExtendedMetrics` als typisiertes Record fehlt — stattdessen `Dictionary<string, double>` via `CalculateExtendedAggregate()` | **Low** | Funktional gelöst, aber nicht exakt wie spezifiziert |
| 2 | Phase 1 / TASK-004 | **M9 False Confidence Rate** — im Dict als `falseConfidenceRate` vorhanden (`CalculateAggregate` in Basis-Dict) | — | **Erledigt** ✅ |
| 3 | Phase 1 / TASK-005 | **M10 Console Confusion Index** — implementiert als `maxConsoleConfusionRate` | — | **Erledigt** ✅ |
| 4 | Phase 1 / TASK-009 | **M14 Category Accuracy** — implementiert als `categoryConfusionRate` | — | **Erledigt** ✅ |
| 5 | Phase 1 / TASK-010 | **M15 UNKNOWN→WRONG Migration** — `BaselineComparator.CalculateUnknownToWrongMigrationRate()` existiert mit Gate (≤ 2 %) | — | **Erledigt** ✅ |
| 6 | Phase 1 / TASK-011 | **M16 Confidence Calibration Error** — `CalculateConfidenceCalibration()` existiert mit ECE-Berechnung | — | **Erledigt** ✅ |
| 7 | Phase 1 / TASK-014–019 | QualityGateTests mit M4/M6/M7/M8/M9/M9a/M9b/M10/M11/M13/M14 — **nur informativ**, kein harter Fail bei PR-Gate | **High** | Teilweise — Gates existieren, aber nur über Env-Var `ROMCLEANUP_ENFORCE_QUALITY_GATES=true` enforced |
| 8 | Phase 1 / TASK-020–021 | Baseline-Snapshot — `latest-baseline.json` + `v0.1.0-baseline.json` existieren | — | **Erledigt** ✅ |
| 9 | Phase 2 / TASK-027–030 | **DatasetExpander ausführen** — manueller Schritt, Realzahlen weichen stark von Manifest ab (s. Diskrepanz-Tabelle unten) | **High** | Offen |
| 10 | Phase 2 / TASK-036–037 | **performance-scale.jsonl** = 0 Einträge, Ziel ≥ 5.000 | **Medium** | Offen |
| 11 | Phase 3 / TASK-040–044 | BenchmarkHtmlReportWriter — **existiert** mit HTML-Escape, Confusion Matrix, Calibration, Trend-Sektion | — | **Erledigt** ✅ |
| 12 | Phase 3 / TASK-045–046 | CSV-Export — `BenchmarkArtifactWriter.WriteConfusionCsv()` + `WriteCategoryConfusionCsv()` existieren | — | **Erledigt** ✅ |
| 13 | Phase 3 / TASK-047–050 | TrendAnalyzer — **existiert** mit `TrendAnalyzerTests` | — | **Erledigt** ✅ |
| 14 | Phase 3 / TASK-051–054 | CI-Pipeline — `.github/workflows/benchmark.yml` existiert (PR-Gate + Nightly + Baseline-Publish) | — | **Erledigt** ✅ |
| 15 | Phase 4 / TASK-056–060 | ~~**`AntiGamingGateTests.cs` fehlt komplett**~~ — **BEHOBEN**: `AntiGamingGateTests.cs` existiert mit 2 Tests (M15 UnknownToWrongMigration + M16 ECE), `ConfidenceCalibrationTests.cs` existiert mit 3 Tests | **Medium** | **Erledigt** ✅ |
| 16 | Phase 4 / TASK-061–062 | Repair Gate Feature-Flag — `RepairGateEvaluator.IsRepairFeatureReady()` existiert, in QualityGateTests ist M14 nur info | **Low** | Teilweise |
| 17 | Phase 4 / TASK-063–066 | ~~**Plan-Dateien Status-Update**~~ — **BEHOBEN**: Alle 4 Plans auf "Mostly Complete" aktualisiert (2026-03-22) | **Low** | **Erledigt** ✅ |

---

### 2. `docs/architecture/EVALUATION_STRATEGY.md`

| # | Sektion | Was fehlt | Priorität |
|---|---------|-----------|-----------|
| 1 | §10, P0 #2 | **Baseline-Metriken messen und speichern** — `latest-baseline.json` existiert mit 2.073 Samples, aber es gibt keine systematische M1–M16-Baseline-Referenz als Named-Record | **Low** |
| 2 | §10, P1 #5 | **UNKNOWN→WRONG Regression Gate** — implementiert in `BaselineRegressionGateTests` (≤ 2 % Schwelle) | **Erledigt** ✅ |
| 3 | §10, P2 #11 | **`ConfidenceCalibrationTests.cs`** — als dedizierte Test-Klasse mit `[Trait("Category", "AntiGaming")]` nicht vorhanden. Funktionalität existiert in `MetricsAggregator.CalculateConfidenceCalibration()` und wird in `BaselineRegressionGateTests` aufgerufen, aber kein eigenständiger Test | **Medium** |
| 4 | §10, P2 #12 | **HTML Benchmark Report** — `BenchmarkHtmlReportWriter` existiert | **Erledigt** ✅ |
| 5 | §10, P2 #13 | **Trend-Comparison Report** — `TrendAnalyzer` existiert | **Erledigt** ✅ |
| 6 | §10, P2 #15 | **Repair-Freigabe-Gate als Feature-Flag** — Evaluator existiert, Flag-Logik vorhanden, aber Repair-Feature selbst nicht implementiert | **Medium** (blocked by Repair-Feature) |
| 7 | §9.2 | **Repair-Freigabe-Regeln** (DatMatch exact, Confidence ≥ 95, Console-Verifizierung) — alle als "Geplant" markiert, Repair-Feature existiert nicht | **Medium** (Feature-Dependency) |

---

### 3. `docs/architecture/ADR-015-recognition-quality-benchmark-framework.md`

| # | Sektion | Was fehlt | Priorität |
|---|---------|-----------|-----------|
| 1 | §5 Quality Gates | M4/M7/M9a Hard-Fail-Gates spezifiziert als "harter Block für Release" — aktuell nur informativ, gated über Env-Var | **High** |
| 2 | §8 Repair-Freigaberegeln | Alle 5 Regeln als "Geplant" markiert: DatMatch exact, DetectionConfidence ≥ 95, Console=HardEvidence, NoConflict, Ergebnis=Sort nicht Block | **Medium** (Feature-Dependency) |
| 3 | §8 Extensions | `ConfidenceCalibrationTests.cs` (P2) als fehlend markiert | **Medium** |
| 4 | §8 Extensions | `BenchmarkReportGenerator.cs` (P2) als fehlend — existiert als `BenchmarkHtmlReportWriter.cs` (anderer Name) | **Low** (Naming-Mismatch in Doku) |

---

### 4. `docs/architecture/RECOGNITION_QUALITY_BENCHMARK.md`

| # | Sektion | Was fehlt | Priorität |
|---|---------|-----------|-----------|
| 1 | §9.2 | **Repair-Freigabe** — komplett unimplementiert (nur Evaluator-Infrastruktur) | **Medium** |
| 2 | §6 Confusion Matrix | CSV-Export existiert, wird in CI generiert — aber **kein automatischer Upload** als PR-Kommentar | **Low** |

---

### 5. `docs/architecture/GROUND_TRUTH_SCHEMA.md`

| # | Sektion | Was fehlt | Priorität |
|---|---------|-----------|-----------|
| 1 | Header | Schema-Version "1.0.0-draft", Status "Design/Review" — sollte auf stable aktualisiert werden, da Schema aktiv genutzt wird | **Low** |
| 2 | CI-Validation | `SchemaValidator.cs` existiert und wird in `CoverageGateTests` aufgerufen — CI validiert via Benchmark-Workflow | **Erledigt** ✅ |

---

### 6. `docs/architecture/COVERAGE_GAP_AUDIT.md`

| # | Sektion | Was fehlt | Priorität |
|---|---------|-----------|-----------|
| 1 | Gesamt-Zahlen | **Manifest/Realität-Diskrepanz** (s. Tabelle unten) | **High** |
| 2 | edge-cases | Ziel 250, Ist 198 (Delta: **-52**) | **Medium** |
| 3 | negative-controls | Ziel 100, Ist 80 (Delta: **-20**) | **Medium** |
| 4 | chaos-mixed | Ziel 220, Ist 204 (Delta: **-16**) | **Low** |
| 5 | repair-safety | Ziel 130, Ist 100 (Delta: **-30**) | **Medium** |
| 6 | performance-scale | Ziel 5.000+, Ist **0** | **Medium** |
| 7 | holdout | Ziel 200, Ist 50 (Delta: **-150**) | **Medium** |

---

### 7. `TESTSET_DESIGN.md`

| # | Sektion | Was fehlt | Priorität |
|---|---------|-----------|-----------|
| 1 | §3.7 | **Performance-Scale Set** (5.000–20.000 Samples) — `ScaleDatasetGenerator.cs` existiert, aber `performance-scale.jsonl` = 0 Einträge | **Medium** |
| 2 | §3.8 | **Holdout-Zone** — 50 Einträge vorhanden, aber Ziel ist 200 für statistisch signifikanten Drift-Test | **Medium** |

---

### 8. `TEST_STRATEGY.md`

| # | Sektion | Was fehlt | Priorität |
|---|---------|-----------|-----------|
| 1 | §5 CI Pipeline | Quality-Gate im PR-Workflow als `informational` — kein Hard-Fail bei PR-Merge | **High** |
| 2 | §5 CI Pipeline | Mutation-Testing als "Reporting only" — Status unklar, kein Workflow dafür | **Low** |

---

### 9. `plan/feature-benchmark-testset-1.md`

| # | Sektion | Was fehlt | Priorität |
|---|---------|-----------|-----------|
| 1 | Header | Status "Planned" — Infrastruktur ist zu ~95 % implementiert, Status sollte "Completed" sein | **Low** |

---

### 10. `plan/feature-benchmark-coverage-expansion-1.md`

| # | Sektion | Was fehlt | Priorität |
|---|---------|-----------|-----------|
| 1 | Header | Status "Planned" — DatasetExpander ist implementiert, Coverage-Gates laufen, Status sollte "In Progress" oder "Completed" sein | **Low** |
| 2 | Expansion-Tasks | DatasetExpander muss **ausgeführt** werden um die Coverage-Lücken zu schließen | **High** |

---

### 11. `plan/feature-benchmark-coverage-matrix-impl-1.md`

| # | Sektion | Was fehlt | Priorität |
|---|---------|-----------|-----------|
| 1 | Header | Status "Planned" — `CoverageValidator` + `gates.json` + `CoverageGateTests` existieren und laufen | **Low** |

---

### 12. `benchmark/manifest.json`

| # | Sektion | Was fehlt | Priorität |
|---|---------|-----------|-----------|
| 1 | Zahlen-Konsistenz | **Manifest zeigt 2.200 Einträge, reale JSONL-Zählung ergibt 2.073** | **High** |
| 2 | groundTruthVersion | Manifest sagt "2.0.0", Baseline sagt "1.0.0" | **Medium** |

---

### 13. `benchmark/gates.json`

| # | Sektion | Was fehlt | Priorität |
|---|---------|-----------|-----------|
| 1 | holdout Gate | `hardFail: 0` — Holdout-Gate ist effektiv deaktiviert (erlaubt 0 Einträge), obwohl 50 Holdout-Einträge existieren und Ziel 200 ist | **Medium** |

---

## Diskrepanz-Tabelle: manifest.json vs. Realität

| Set | manifest.json | Tatsächliche JSONL-Lines | Delta |
|-----|---------------|--------------------------|-------|
| golden-core | 550 | **648** | +98 |
| golden-realworld | 400 | **493** | +93 |
| edge-cases | 250 | **198** | **-52** |
| negative-controls | 100 | **80** | **-20** |
| chaos-mixed | 220 | **204** | **-16** |
| repair-safety | 130 | **100** | **-30** |
| dat-coverage | 300 | **350** | +50 |
| performance-scale | 0 | **0** | 0 |
| **Gesamt** | **2.200** | **2.073** | **-127** |
| holdout (separat) | — | **50** | — |

> **Hinweis:** `golden-core` und `golden-realworld` haben *mehr* Einträge als im Manifest angegeben — das Manifest wurde nach einer früheren Expansion nicht aktualisiert. Andere Sets haben *weniger* als deklariert.

---

## Zusammenfassung nach Priorität

### Critical (Release-Blocker)
*Keine — das Benchmark-System blockiert keine Release-Fähigkeit, da es als nicht-enforced Gate läuft.*

### High (Sollte vor nächstem Meilenstein gelöst werden)
| # | Item | Betroffene Dokumente |
|---|------|---------------------|
| H1 | Quality Gates nur informativ statt Hard-Fail im PR-Gate | ADR-015, EVALUATION_STRATEGY, TEST_STRATEGY, benchmark.yml |
| ~~H2~~ | ~~manifest.json / Realität-Diskrepanz~~ → **BEHOBEN** (auf reale 2.073 korrigiert) | COVERAGE_GAP_AUDIT, manifest.json |
| H3 | DatasetExpander ausführen um Coverage-Lücken zu schließen (edge-cases -52, negative-controls -20, repair-safety -30) | COVERAGE_GAP_AUDIT, TESTSET_DESIGN, feature-benchmark-coverage-expansion-1.md |

### Medium
| # | Item | Betroffene Dokumente |
|---|------|---------------------|
| M1 | `performance-scale.jsonl` = 0 Einträge (Ziel: ≥ 5.000 generiert) | TESTSET_DESIGN, COVERAGE_GAP_AUDIT, feature-benchmark-evaluation-pipeline-1.md |
| M2 | Holdout-Set bei 50 (Ziel: 200) + Holdout-Gate auf hardFail=0 | COVERAGE_GAP_AUDIT, gates.json |
| ~~M3~~ | ~~`AntiGamingGateTests.cs` fehlt~~ → **BEHOBEN** (2 Tests: M15_UnknownToWrongMigration + M16_ECE) | feature-benchmark-evaluation-pipeline-1.md (Phase 4) |
| ~~M4~~ | ~~`ConfidenceCalibrationTests.cs` fehlt~~ → **BEHOBEN** (3 Tests: HighConf Accuracy, LowConf Safety, PerSet ECE) | EVALUATION_STRATEGY, ADR-015 |
| M5 | Repair-Feature nicht implementiert (Evaluator-Infra vorhanden) | EVALUATION_STRATEGY, RECOGNITION_QUALITY_BENCHMARK, ADR-015 |
| M6 | Baseline `groundTruthVersion` "1.0.0" vs. Manifest "2.0.0" | manifest.json, latest-baseline.json |

### Low
| # | Item | Betroffene Dokumente |
|---|------|---------------------|
| ~~L1~~ | ~~Plan-Dateien Status "Planned" statt "Completed"/"In Progress"~~ → **BEHOBEN** (2026-03-22) | feature-benchmark-testset-1.md, feature-benchmark-coverage-expansion-1.md, feature-benchmark-coverage-matrix-impl-1.md |
| ~~L2~~ | ~~Ground-Truth Schema "1.0.0-draft" / "Design/Review" — sollte stable sein~~ → **BEHOBEN** (2026-03-22) | GROUND_TRUTH_SCHEMA.md |
| ~~L3~~ | ~~ADR-015 referenziert `BenchmarkReportGenerator.cs` — existiert als `BenchmarkHtmlReportWriter.cs`~~ → **BEHOBEN** (2026-03-22) | ADR-015 |
| L4 | `ExtendedMetrics` als Dictionary statt typisiertem Record | feature-benchmark-evaluation-pipeline-1.md |
| L5 | Mutation-Testing Status unklar | TEST_STRATEGY |

---

## Bereits erledigt (Verifiziert im Code)

Diese Items aus den Dokumenten sind **implementiert** und benötigen keine weitere Arbeit:

- ✅ `QualityLevelEvaluator` (7 Stufen A–G)
- ✅ `RepairGateEvaluator` (Safe/Risky/Blocked)
- ✅ `ContentSignatureClassifier` (Magic-Byte-Klassifikator, 28 Tests)
- ✅ `HoldoutEvaluator` mit `HoldoutGateTests`
- ✅ `ScaleDatasetGenerator` (Klasse existiert)
- ✅ `PerformanceScaleTests` / `PerformanceBenchmarkTests`
- ✅ `SystemTierGateTests`
- ✅ `MetricsAggregator` mit `CalculateExtendedAggregate()` (M4–M14)
- ✅ `CalculateConfidenceCalibration()` (M16 ECE)
- ✅ `UnknownToWrongMigrationRate` (M15) in `BaselineComparator`
- ✅ `BuildConfusionMatrix()` + `WriteConfusionCsv()` + `WriteCategoryConfusionCsv()`
- ✅ `BenchmarkHtmlReportWriter` mit HTML-Escape, Confusion, Calibration, Trend
- ✅ `TrendAnalyzer` + `TrendAnalyzerTests`
- ✅ `BaselineRegressionGateTests` (Regression + M15 Gate)
- ✅ `CoverageGateTests` (20+ Gates)
- ✅ `.github/workflows/benchmark.yml` (PR-Gate + Nightly + Baseline-Publish)
- ✅ `latest-baseline.json` + `v0.1.0-baseline.json`
- ✅ `SchemaValidator` + CI-Integration
- ✅ `CrossValidationSplitter`
- ✅ `BenchmarkArtifactWriter` (JSON + CSV Artifacts)
- ✅ `ground-truth.schema.json` mit Validation

---

## Empfohlene nächste Schritte

1. **manifest.json korrigieren** — Reale Zahlen eintragen (2.073 statt 2.200)
2. **DatasetExpander ausführen** — edge-cases, negative-controls, repair-safety auffüllen
3. **Quality Gates enforced setzen** im PR-Gate (Env-Var `ROMCLEANUP_ENFORCE_QUALITY_GATES=true` im Workflow)
4. **Holdout expandieren** (50 → 200) und `gates.json` holdout `hardFail` anheben
5. **performance-scale generieren** — `ScaleDatasetGenerator` ausführen
6. **Plan-Dateien updaten** — Status auf "Completed" / "In Progress" setzen
