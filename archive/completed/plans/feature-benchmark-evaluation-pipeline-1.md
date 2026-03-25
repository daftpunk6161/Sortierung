---
goal: "Benchmark Evaluation Pipeline — Quality Gates, fehlende Metriken, Reports, Regressionserkennung und CI-Integration"
version: "1.0"
date_created: 2026-03-21
last_updated: 2026-03-22
owner: "Romulus Team"
status: "Mostly Complete"
tags:
  - feature
  - testing
  - benchmark
  - quality
  - ci
---

# Introduction

![Status: Mostly Complete](https://img.shields.io/badge/status-Mostly%20Complete-yellow)

Dieser Plan definiert die Implementierung der noch fehlenden Benchmark-Evaluation-Pipeline-Komponenten: harte Quality Gates (M4/M6/M7/M9a), erweiterte Metriken (M8–M16), HTML-Reports, Trend-Vergleich, Anti-Gaming-Schutz und CI-Integration.

**Ausgangslage:** Die Benchmark-Infrastruktur ist zu ~90 % vorhanden (2.073 Ground-Truth-Einträge, 83 C#-Dateien, 16 Stub-Generatoren, Evaluation-Runner, Comparator, Aggregator, JSON-Report, Baseline-Vergleich, Coverage-Gates). Die bestehenden Pläne (`feature-benchmark-testset-1.md`, `feature-benchmark-coverage-expansion-1.md`) sind als "Planned" markiert, obwohl die Infrastruktur bereits implementiert ist — sie dokumentieren den *damaligen* Plan, nicht den aktuellen Zustand.

**Dieser Plan adressiert die verbleibenden Lücken:**

| Komponente | Ist-Zustand | Ziel |
|------------|-------------|------|
| Quality Gate Tests (M4/M6/M7) | MetricsAggregator berechnet wrongMatchRate, unknownRate, unsafeSortRate — aber **kein xUnit-Gate** prüft harte Schwellenwerte | xUnit-Tests mit harten Fail-Schwellen aus ADR-015 |
| Metriken M8–M13 | Nicht berechnet (Safe Sort Coverage nur als 1-unsafeSort, kein False Confidence, kein Console Confusion, kein DAT Match) | Vollständig in MetricsAggregator |
| Anti-Gaming M15/M16 | Nicht implementiert | UNKNOWN→WRONG Migration + Confidence Calibration Error |
| HTML-Report | Nur JSON (BenchmarkReportWriter) | HTML-Report mit Confusion Matrix, per-System-Tabelle, Trend-Chart |
| Confusion Matrix Export | BuildConfusionMatrix() existiert, kein Export | CSV-Export + HTML-Visualisierung |
| Trend-Vergleich | BaselineComparator vergleicht 1:1 gegen Baseline | N-Run-History mit Trendrichtung |
| CI-Pipeline | Kein GitHub Actions Workflow | PR-Gate + Nightly + Baseline-Publish |
| Baseline Snapshot | Kein committed Baseline-Report | Initiale baseline-metrics.json |
| Dataset-Lücken | 2.073 Einträge (DatasetExpander bereits ausgeführt, ≥1.200 Gate erfüllt) | Weitere Expansion auf ≥3.000 Einträge |
| Performance-Scale | performance-scale.jsonl leer (0 Einträge) | ScaleDatasetGenerator ausführen → ≥5.000 Einträge |

**Referenzdokumente:**
- `docs/architecture/ADR-015-recognition-quality-benchmark-framework.md` — Qualitätsmodell, Gate-Schwellen
- `docs/architecture/EVALUATION_STRATEGY.md` — Evaluationsstrategie, Metriken M1–M16
- `docs/architecture/RECOGNITION_QUALITY_BENCHMARK.md` — Benchmark-Design
- `plan/feature-benchmark-testset-1.md` — Basis-Infrastruktur-Plan (bereits implementiert)
- `plan/feature-benchmark-coverage-expansion-1.md` — Coverage-Expansion-Plan (DatasetExpander implementiert, noch nicht ausgeführt)

---

## 1. Requirements & Constraints

### Funktionale Anforderungen

- **REQ-001**: Quality Gate Tests müssen folgende harte Schwellenwerte als xUnit-`[Fact]` prüfen:
  - M4 Wrong Match Rate ≤ 0.5 % (`wrongMatchRate` aus `MetricsAggregator.CalculateAggregate`)
  - M6 Unknown Rate (nur informativ, kein Hard-Fail — Wert soll ≤ 15 % sein, aber nicht blockieren)
  - M7 Unsafe Sort Rate ≤ 0.3 % (`unsafeSortRate` — Wrong + High Confidence)
  - M9a False Confidence Rate ≤ 0.1 % (Wrong + Confidence ≥ 85 + kein Conflict)
- **REQ-002**: MetricsAggregator muss alle 16 Metriken aus EVALUATION_STRATEGY.md berechnen — aktuell fehlen M8 (Safe Sort Coverage), M9 (False Confidence Rate), M10 (Console Confusion Index), M11 (DAT Exact Match Rate), M12 (DAT Weak Match Rate), M13 (Repair-Safe Rate), M14 (Category Accuracy), M15 (UNKNOWN→WRONG Migration), M16 (Confidence Calibration Error).
- **REQ-003**: HTML-Report muss enthalten: Executive Summary, per-System-Tabelle, Confusion Matrix (Heatmap), Gate-Status (Pass/Fail), Trend-Vergleich zur Baseline, alle 16 Metriken.
- **REQ-004**: CI-Gate muss bei jedem PR automatisch ausführen: `dotnet test --filter "Category=QualityGate|Category=BenchmarkRegression"` — bei Fail wird PR blockiert.
- **REQ-005**: Trend-History muss die letzten N Benchmark-Runs speichern und Richtung (besser/gleich/schlechter) pro Metrik anzeigen.
- **REQ-006**: Anti-Gaming-Metrik M15 muss erkennen, wenn ein Code-Change die UNKNOWN-Rate senkt und gleichzeitig die WRONG-Rate erhöht (= aggressive Detection ohne Qualitätsgewinn).
- **REQ-007**: Anti-Gaming-Metrik M16 (Confidence Calibration Error) prüft, ob die Confidence-Werte kalibriert sind: bei Samples mit Confidence 90 sollte ~90 % korrekt sein.
- **REQ-008**: DatasetExpander muss ausgeführt und die JSONL-Dateien auf ≥1.200 Einträge gebracht werden.

### Sicherheitsanforderungen

- **SEC-001**: HTML-Report muss alle dynamischen Werte (System-Keys, Dateinamen, Details) HTML-escapen — kein ungefiltertes Einfügen von Ground-Truth-Strings.
- **SEC-002**: CSV-Export muss CSV-Injection verhindern (Formel-Präfixe `=`, `+`, `-`, `@`, `\t`, `\r` am Feldanfang escapen).
- **SEC-003**: CI-Workflow darf keine Secrets in Benchmark-Reports exponieren.

### Architektur-Constraints

- **CON-001**: Alle neuen Dateien bleiben in `src/RomCleanup.Tests/Benchmark/` — kein separates Projekt.
- **CON-002**: MetricsAggregator bleibt `internal static` — erweiterte Metriken als zusätzliche Methoden, kein Breaking Change.
- **CON-003**: HTML-Report nutzt reines String-Building (kein Razor, kein Template-Engine-NuGet) — konsistent mit bestehendem `ReportGenerator.cs` in Infrastructure.
- **CON-004**: CI-Workflow nutzt `dotnet test` mit `--filter` — keine Pester-Abhängigkeit für Benchmark-Gates.
- **CON-005**: Ground-Truth-JSONL-Änderungen durch DatasetExpander müssen vor Commit validiert werden (SchemaValidator).
- **CON-006**: Kein neues NuGet-Paket — `System.Text.Json`, `System.Text.Encodings.Web` und `xUnit` reichen aus.

### Richtlinien

- **GUD-001**: Neue Metriken folgen dem Naming-Pattern `M{nummer}_{kurzbeschreibung}` (z.B. `M09_FalseConfidenceRate`).
- **GUD-002**: Quality Gate Tests tragen `[Trait("Category", "QualityGate")]` — separat filterbar in CI.
- **GUD-003**: HTML-Report wird nach `benchmark/reports/` geschrieben (`.gitignore`), nicht committed.
- **GUD-004**: Baseline-Snapshot wird als `benchmark/baselines/baseline-latest.json` committed — nur bei manueller Akzeptanz.

### Patterns

- **PAT-001**: `MetricsAggregator` Pattern beibehalten: statische Methoden, immutable Records als Rückgabe.
- **PAT-002**: Gate-Tests folgen `BaselineRegressionGateTests`-Pattern: Evaluate all sets → aggregate → assert threshold.
- **PAT-003**: HTML-Report folgt dem Muster aus `src/RomCleanup.Infrastructure/Reporting/ReportGenerator.cs` (StringBuilder + HtmlEncode).

---

## 2. Implementation Steps

### Phase 1 — Quality Gates & fehlende Metriken (P0 – Release-Blocker)

- GOAL-001: MetricsAggregator um alle fehlenden Metriken erweitern (M8–M16). Quality Gate Tests implementieren mit harten Schwellenwerten aus ADR-015. Initialen Baseline-Snapshot erzeugen und committen.

**1.1 — MetricsAggregator: Erweiterte Metriken**

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-001 | `MetricsAggregator.cs`: Neues Record `ExtendedMetrics` mit allen 16 M-Werten als Properties (`WrongMatchRate`, `UnknownRate`, `UnsafeSortRate`, `SafeSortCoverage`, `FalseConfidenceRate`, `ConsoleConfusionIndex`, `DatExactMatchRate`, `DatWeakMatchRate`, `RepairSafeRate`, `CategoryAccuracy`, `UnknownToWrongMigration`, `ConfidenceCalibrationError`). | | |
| TASK-002 | `MetricsAggregator.cs`: Neue Methode `CalculateExtended(IReadOnlyList<BenchmarkSampleResult> results, RegressionReport? regressionReport = null) → ExtendedMetrics`. Berechnet alle 16 Metriken. M15 (UNKNOWN→WRONG Migration) benötigt den Baseline-Vergleich, daher optionaler `regressionReport`-Parameter. Bestehende `CalculateAggregate` bleibt unverändert (Backward-Compat). | | |
| TASK-003 | **M8 Safe Sort Coverage**: `1.0 - unsafeSortRate`. Bereits implizit in `CalculateAggregate` als `safeSortCoverage`, jetzt explizit im `ExtendedMetrics`-Record. | | |
| TASK-004 | **M9 False Confidence Rate**: `count(Verdict=Wrong ∧ Confidence≥85 ∧ !HasConflict) / total`. Nutzt `BenchmarkSampleResult.ActualConfidence` und `ActualHasConflict`. | | |
| TASK-005 | **M10 Console Confusion Index**: Anzahl distinct (Expected, Actual)-Paare mit Expected≠Actual und Actual≠UNKNOWN, normalisiert durch Gesamtfehler. Nutzt bestehende `BuildConfusionMatrix()`. | | |
| TASK-006 | **M11 DAT Exact Match Rate**: `count(entry.tags contains "dat-exact" ∧ Verdict=Correct) / count(entry.tags contains "dat-exact")`. Benötigt Zugriff auf `GroundTruthEntry.Tags` — Methodensignatur erweitern auf `CalculateExtended(IReadOnlyList<(BenchmarkSampleResult Result, GroundTruthEntry Entry)> pairs, ...)`. | | |
| TASK-007 | **M12 DAT Weak Match Rate**: Analog M11 mit Tag `"dat-weak"`. | | |
| TASK-008 | **M13 Repair-Safe Rate**: `count(entry.tags contains "repair" ∧ Verdict ∈ {Correct, Acceptable, TrueNegative}) / count(entry.tags contains "repair")`. | | |
| TASK-009 | **M14 Category Accuracy**: `count(expected.category = actual.category) / total`. Benötigt Category-Vergleich — `GroundTruthComparator` muss Category in `BenchmarkSampleResult` mitgeben (neues Feld `ActualCategory`). | | |
| TASK-010 | **M15 UNKNOWN→WRONG Migration**: Vergleicht Baseline-Verdicts mit aktuellen Verdicts. Zählt Samples, die von `Missed` (UNKNOWN) zu `Wrong` gewechselt haben. Formel: `count(baseline=Missed ∧ current=Wrong) / count(baseline=Missed)`. Benötigt per-Sample Baseline-Vergleich — neues Record `PerSampleRegression(string Id, BenchmarkVerdict BaselineVerdict, BenchmarkVerdict CurrentVerdict)`. | | |
| TASK-011 | **M16 Confidence Calibration Error (CCE)**: Gruppiert Samples nach Confidence-Buckets (0–20, 20–40, 40–60, 60–80, 80–100). In jedem Bucket: `|bucket_accuracy - bucket_confidence_midpoint|`. CCE = Mittelwert aller Bucket-Deltas. Nur auf Samples mit `Verdict ∈ {Correct, Wrong}` anwendbar (UNKNOWN hat keinen sinnvollen Confidence-Wert). | | |

**1.2 — BenchmarkSampleResult erweitern**

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-012 | `BenchmarkVerdict.cs`: `BenchmarkSampleResult` um optionales Feld `ActualCategory` (string?) erweitern. Default null für Backward-Compat. | | |
| TASK-013 | `GroundTruthComparator.cs`: In `Compare()` das neue `ActualCategory`-Feld befüllen — aus `ConsoleDetectionResult` extrahieren (falls verfügbar) oder aus Ground-Truth-Erwartung ableiten. Prüfen, ob `ConsoleDetectionResult` ein Category-Feld hat; falls nicht, über die Detection-Pipeline-Logik ableiten. | | |

**1.3 — Quality Gate Tests**

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-014 | Neue Datei `src/RomCleanup.Tests/Benchmark/QualityGateTests.cs`: Klasse `QualityGateTests : IClassFixture<BenchmarkFixture>`, `[Collection("BenchmarkEvaluation")]`, `[Trait("Category", "QualityGate")]`. | | |
| TASK-015 | `QualityGateTests.Gate_M4_WrongMatchRate_BelowThreshold()`: `[Fact]`. Evaluiert alle 7 JSONL-Sets, berechnet `ExtendedMetrics`, asserted `WrongMatchRate ≤ 0.005`. Failure-Message zeigt aktuellen Wert + Top-3-Confusion-Paare. | | |
| TASK-016 | `QualityGateTests.Gate_M7_UnsafeSortRate_BelowThreshold()`: `[Fact]`. Asserts `UnsafeSortRate ≤ 0.003`. | | |
| TASK-017 | `QualityGateTests.Gate_M9a_FalseConfidenceRate_BelowThreshold()`: `[Fact]`. Asserts `FalseConfidenceRate ≤ 0.001`. | | |
| TASK-018 | `QualityGateTests.Info_M6_UnknownRate_ReportedButNotBlocking()`: `[Fact]`. Berechnet und gibt UnknownRate per `ITestOutputHelper.WriteLine` aus. Kein Assert — nur Visibility. | | |
| TASK-019 | `QualityGateTests.Gate_M8_SafeSortCoverage_AboveThreshold()`: `[Fact]`. Asserts `SafeSortCoverage ≥ 0.95`. Warnt bei <0.97. | | |

**1.4 — Baseline Snapshot**

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-020 | `BaselineRegressionGateTests.cs` anpassen: nach erfolgreichem Gate-Durchlauf Report auch nach `benchmark/baselines/baseline-latest.json` schreiben (nur wenn kein Baseline existiert oder explizit überschrieben wird). | | |
| TASK-021 | Initialen Baseline-Snapshot erzeugen: `dotnet test --filter "Category=BenchmarkRegression"` ausführen → `benchmark/baselines/baseline-latest.json` committen. Dieser Snapshot wird zur Referenz für Regression-Detection. | | |

**1.5 — Unit-Tests für erweiterte Metriken**

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-022 | `MetricsAggregatorTests.cs` erweitern: Test für `CalculateExtended` mit bekannten Input-Werten. Prüft M4, M7, M8, M9 numerisch korrekt. | | |
| TASK-023 | `MetricsAggregatorTests.cs`: Test für M15 (UNKNOWN→WRONG Migration) mit Baseline-Delta. | | |
| TASK-024 | `MetricsAggregatorTests.cs`: Test für M16 (CCE) mit perfekt kalibrierten Samples (CCE=0) und schlecht kalibrierten (CCE>0.2). | | |
| TASK-025 | `MetricsAggregatorTests.cs`: Test für M10 (Console Confusion Index) mit bekannter Confusion Matrix. | | |
| TASK-026 | `MetricsAggregatorTests.cs`: Test für M11/M12/M13/M14 mit getaggten Entries. | | |

**Completion Criteria Phase 1:**
- `ExtendedMetrics`-Record enthält alle 16 M-Werte
- `CalculateExtended()` ist vollständig implementiert
- 5 Quality Gate Tests (M4/M7/M8/M9a + M6 info) existieren und laufen grün
- `baseline-latest.json` existiert in `benchmark/baselines/`
- 5+ Unit-Tests für die neuen Metriken bestehen
- `dotnet test --filter "Category=QualityGate"` gibt Pass/Fail korrekt zurück

---

### Phase 2 — Dataset-Expansion & Coverage-Lücken schliessen (P1 – Hoch)

- GOAL-002: DatasetExpander ausführen, JSONL-Dateien auf ≥1.200 Einträge bringen, Arcade/Computer/BIOS/Multi-File-Lücken schliessen, Performance-Scale generieren, alle Coverage-Gates grün.

**2.1 — DatasetExpander ausführen & validieren**

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-027 | `DatasetExpansionTests.RunExpansion_GeneratesAndWritesEntries()` ausführen: `dotnet test --filter "Category=DatasetGeneration"`. Prüfen, dass ≥65 Systeme und ≥20 Fallklassen generiert werden. Output: erweiterte JSONL-Dateien in `benchmark/ground-truth/`. | | |
| TASK-028 | Nach Expansion: `SchemaValidator` gegen alle JSONL ausführen (wird durch `CoverageGateTests.AllEntries_PassStructuralValidation` automatisch geprüft). Fix von ggf. fehlerhaften generierten Einträgen. | | |
| TASK-029 | Prüfen, ob DatasetExpander die Expansion *mergt* (bestehende + neue Einträge) oder *ersetzt*. Falls Ersetzung: Expansion-Modus auf Merge umstellen (bestehende 2.073 Einträge dürfen nicht verloren gehen). | | |
| TASK-030 | `benchmark/manifest.json` nach Expansion aktualisieren: `totalEntries`, `version`, Datum. | | |

**2.2 — Spezifische Coverage-Lücken schliessen**

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-031 | Arcade-Expansion prüfen: Nach DatasetExpander-Run zählen, ob Arcade-Familie ≥160 (hardFail) erreicht. Falls nicht: manuelle JSONL-Einträge für MAME-Parent/Clone, CPS1/2/3, Neo Geo, Naomi, Cave, PGM ergänzen. | | |
| TASK-032 | Computer-Expansion prüfen: Computer-Familie ≥120 (hardFail). Falls nicht: manuelle Einträge für Amiga (ADF/ADZ/HDF), DOS (COM/EXE), C64 (D64/T64/PRG), Atari ST (ST/STX), MSX (ROM/DSK), ZX Spectrum (.tap/.tzx/.z80). | | |
| TASK-033 | BIOS-Expansion prüfen: `biosTotal ≥ 35` (hardFail), `biosSystems ≥ 8` (hardFail). Falls nicht: Einträge für PS1-BIOS, PS2-BIOS, Saturn-BIOS, DC-BIOS, GBA-BIOS, NDS-BIOS, Lynx-BIOS, PCE-BIOS ergänzen. | | |
| TASK-034 | Multi-File-Sets prüfen: `multiFileSets ≥ 20` (hardFail). Multi-Disc: ≥15. Falls nicht: PSP-ISO+Update, PS1-MultiDisc (disc1+disc2), Dreamcast-GDI-Set (*.gdi + *.raw + *.bin) ergänzen. | | |
| TASK-035 | PS-Disambiguation prüfen: `psDisambiguation ≥ 20` (hardFail). Falls nicht: Einträge wo PS1-PVD vs PS2-PVD vs PSP korrekt differenziert werden müssen. | | |

**2.3 — Performance-Scale Dataset**

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-036 | `ScaleDatasetGenerator.cs` prüfen: existiert bereits als Datei. Verstehen, ob es ein eigenständiger Generator oder Placeholder ist. Falls Placeholder: implementieren — ≥5.000 Einträge generieren (Verteilung: 40 % Cartridge, 30 % Disc, 15 % Arcade, 10 % Computer, 5 % Hybrid). | | |
| TASK-037 | `performance-scale.jsonl` füllen: `ScaleDatasetGenerator` ausführen. Performance-Test (`PerformanceBenchmarkTests`) muss Throughput ≥100 Samples/s zeigen. | | |

**2.4 — Coverage-Gate Validierung**

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-038 | `dotnet test --filter "Category=CoverageGate"` ausführen. Alle 20+ Gate-Tests müssen grün sein. Bei Fails: Lücken identifizieren und in TASK-031 bis TASK-035 nacharbeiten. | | |
| TASK-039 | Neuen Baseline-Snapshot nach Expansion erzeugen: Quality Gate Tests ausführen → `baseline-latest.json` aktualisieren. | | |

**Completion Criteria Phase 2:**
- `benchmark/manifest.json` zeigt `totalEntries ≥ 1200`
- Alle CoverageGateTests grün (hardFail-Schwellen erreicht)
- `performance-scale.jsonl` enthält ≥5.000 Einträge
- `PerformanceBenchmarkTests` zeigen Throughput ≥100 Samples/s
- Neuer Baseline-Snapshot committed
- Kein bestehender Ground-Truth-Eintrag gelöscht oder verändert (nur Additions)

---

### Phase 3 — Reports, Trend-Analyse & CI-Integration (P2 – Mittel)

- GOAL-003: HTML-Report-Generator implementieren, Confusion-Matrix CSV-Export, Trend-Vergleich über N Runs, GitHub Actions CI-Pipeline mit PR-Gate und Nightly-Run.

**3.1 — HTML Benchmark Report**

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-040 | Neue Datei `src/RomCleanup.Tests/Benchmark/BenchmarkHtmlReportWriter.cs`: Klasse `BenchmarkHtmlReportWriter` (internal static). Methode `Write(BenchmarkReport report, ExtendedMetrics metrics, RegressionReport? regression, string outputPath)`. | | |
| TASK-041 | HTML-Template: `<!DOCTYPE html>` + inline CSS (kein externes Stylesheet). Sections: Header mit Timestamp + GT-Version, Executive Summary (Pass/Fail Badge pro Gate), Metriken-Tabelle (alle 16 M-Werte mit Farb-Indikator grün/gelb/rot), per-System-Tabelle (sortierbar nach Wrong-Count desc), Confusion-Matrix als `<table>` mit Zellfarben. | | |
| TASK-042 | **SEC-001 Enforcement**: Alle dynamischen Werte über `System.Text.Encodings.Web.HtmlEncoder.Default.Encode()` escapen. Unit-Test mit XSS-Payload in System-Key (`<script>alert(1)</script>`) → muss escaped werden. | | |
| TASK-043 | Trend-Section im HTML: Falls `RegressionReport.HasBaseline == true`, Delta-Anzeige pro Metrik (↑↓→ Symbole + prozentuale Änderung). | | |
| TASK-044 | Report-Output nach `benchmark/reports/benchmark-report-{timestamp}.html`. In `.gitignore` bestätigen, dass `benchmark/reports/` ignored ist. | | |

**3.2 — Confusion Matrix CSV Export**

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-045 | Neue Methode `BenchmarkReportWriter.WriteCsv(IReadOnlyList<ConfusionEntry> matrix, string path)`. Format: `Expected,Actual,Count` mit CSV-Injection-Schutz (SEC-002). | | |
| TASK-046 | **SEC-002 Enforcement**: `CsvSanitize(string value)` Hilfsmethode — Werte die mit `=`, `+`, `-`, `@`, `\t`, `\r` beginnen, mit führendem `'` escapen und in Quotes wrappen. Unit-Test mit `=CMD(...)` Payload. | | |

**3.3 — Trend-History (N-Run-Vergleich)**

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-047 | Neue Datei `src/RomCleanup.Tests/Benchmark/TrendAnalyzer.cs`: Klasse `TrendAnalyzer` (internal static). | | |
| TASK-048 | Methode `LoadHistory(string basePath, int maxRuns = 10) → IReadOnlyList<BenchmarkReport>`: Liest alle `benchmark-report-*.json` aus `benchmark/reports/`, sortiert nach Timestamp desc, nimmt maxRuns. | | |
| TASK-049 | Methode `AnalyzeTrend(IReadOnlyList<BenchmarkReport> history) → TrendReport`: Record `TrendReport(IReadOnlyDictionary<string, TrendDirection> MetricTrends, int TotalRuns)`. `TrendDirection` = Enum `Improving, Stable, Degrading`. Trend wird über lineare Regression der letzten N Werte bestimmt (slope < -0.001 = Improving, slope > 0.001 = Degrading, sonst Stable). | | |
| TASK-050 | Unit-Tests für TrendAnalyzer: 3 Tests (alle improving, alle stable, mixed). | | |

**3.4 — CI-Integration (GitHub Actions)**

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-051 | Neue Datei `.github/workflows/benchmark-gate.yml`: GitHub Actions Workflow. Trigger: `pull_request` (Pfad-Filter: `src/**`, `benchmark/**`, `data/**`). | | |
| TASK-052 | Job `benchmark-gate`: `runs-on: windows-latest`, Setup .NET 10, `dotnet build src/RomCleanup.sln`, `dotnet test src/RomCleanup.Tests/RomCleanup.Tests.csproj --filter "Category=QualityGate\|Category=BenchmarkRegression\|Category=CoverageGate"`. Exit-Code ≠ 0 blockiert PR-Merge. | | |
| TASK-053 | Job `benchmark-nightly`: Trigger `schedule: cron: '0 3 * * *'`. Führt vollständigen Benchmark aus (alle 7 Sets + Performance-Scale), generiert HTML-Report, speichert als GitHub Actions Artifact. | | |
| TASK-054 | SEC-003: Workflow-YAML prüfen: keine `secrets.*` in Benchmark-Report-Steps, keine `GITHUB_TOKEN` Usage außer für Artifact-Upload. | | |

**3.5 — Integration in BaselineRegressionGateTests**

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-055 | `BaselineRegressionGateTests.cs` erweitern: nach Regression-Gate auch HTML-Report und CSV-Export schreiben. Methoden-Aufruf für `BenchmarkHtmlReportWriter.Write()` und `BenchmarkReportWriter.WriteCsv()` integrieren. | | |

**Completion Criteria Phase 3:**
- HTML-Report wird bei Benchmark-Durchlauf generiert
- Confusion-Matrix CSV exportierbar
- TrendAnalyzer analysiert N Runs korrekt
- GitHub Actions Workflow existiert und ist syntaktisch valide
- PR-Gate filtert `QualityGate|BenchmarkRegression|CoverageGate`
- SEC-001 (HTML-Escape) und SEC-002 (CSV-Injection) durch Tests verifiziert
- Nightly-Job generiert HTML + Artifact

---

### Phase 4 — Anti-Gaming & Confidence-Kalibrierung (P3 – Wartbarkeit)

- GOAL-004: Anti-Gaming-Schutzmetriken (M15/M16) als eigenständige Gate-Tests implementieren. Feature-Flag für Repair-Gate. Dokumentation aktualisieren.

**4.1 — Anti-Gaming Gate Tests**

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-056 | Neue Datei `src/RomCleanup.Tests/Benchmark/AntiGamingGateTests.cs`: Klasse `AntiGamingGateTests : IClassFixture<BenchmarkFixture>`, `[Trait("Category", "AntiGaming")]`. | | |
| TASK-057 | `AntiGamingGateTests.Gate_M15_NoUnknownToWrongMigration()`: `[Fact]`. Evaluiert alle Sets, vergleicht per-Sample Verdicts mit Baseline. Asserts: `UNKNOWN→WRONG Migration Rate ≤ 2 %` (= von allen Samples die vorher UNKNOWN waren, dürfen maximal 2 % jetzt WRONG sein). | | |
| TASK-058 | `AntiGamingGateTests.Gate_M16_ConfidenceCalibrationError_BelowThreshold()`: `[Fact]`. Asserts: `CCE ≤ 0.15` (15 % mittlere absolute Abweichung über alle Confidence-Buckets). | | |
| TASK-059 | Per-Sample Baseline-Vergleich implementieren: `BaselineComparator` um `ComparePerSample(IReadOnlyList<BenchmarkSampleResult> current, string baselinePath) → IReadOnlyList<PerSampleRegression>` erweitern. Benötigt Baseline-Report mit per-Sample Verdicts — `BenchmarkReport` Record um `PerSampleVerdicts: IReadOnlyDictionary<string, BenchmarkVerdict>?` erweitern. | | |
| TASK-060 | `BenchmarkReportWriter.CreateReport()` erweitern: `PerSampleVerdicts` Dictionary befüllen (Id → Verdict). Backward-Compatible: bei Deserialisierung alter Reports ist Feld null. | | |

**4.2 — Repair Gate Feature-Flag**

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-061 | `QualityGateTests.Gate_M13_RepairSafeRate_AboveThreshold()`: `[Fact]`. Asserts `RepairSafeRate ≥ 0.90` (90 % der Repair-getaggten Samples korrekt behandelt). Mit `[Trait("Category", "RepairGate")]` — separater Trait, initial nicht im CI-Gate (erst nach Evaluation). | | |
| TASK-062 | Feature-Flag Logik: Prüfe ob `benchmark/gates.json` ein `repairGate`-Objekt enthält. Falls ja: Assert aktiv. Falls nein: Test wird mit `Skip` übersprungen (nicht Fail). | | |

**4.3 — Dokumentation aktualisieren**

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-063 | `docs/architecture/RECOGNITION_QUALITY_BENCHMARK.md` aktualisieren: Link zu QualityGateTests, aktuelle Gate-Schwellenwerte, Verweis auf HTML-Report. | | |
| TASK-064 | `plan/feature-benchmark-testset-1.md` Status auf `Completed` setzen (Infrastruktur ist implementiert). | | |
| TASK-065 | `plan/feature-benchmark-coverage-expansion-1.md` Status aktualisieren (auf `In progress` oder `Completed` je nach Phase-2-Ergebnis). | | |
| TASK-066 | `benchmark/manifest.json` finale Version setzen nach allen Phasen. | | |

**Completion Criteria Phase 4:**
- M15 und M16 als Gate-Tests implementiert
- Per-Sample Baseline-Vergleich funktioniert
- Repair-Gate als optionales Feature-Flag
- Alle Dokumentation aktuell
- Bestehende Plan-Dateien korrekt aktualisiert
- `dotnet test --filter "Category=QualityGate|Category=AntiGaming"` grün

---

## 3. Alternatives

- **ALT-001**: **Separates Benchmark-Projekt (`RomCleanup.Benchmark.csproj`)** statt Tests in `RomCleanup.Tests` — verworfen, weil die Infrastruktur bereits vollständig in `RomCleanup.Tests/Benchmark/` lebt und ein neues Projekt Migration + Duplication bedeuten würde.
- **ALT-002**: **Razor/Fluid-Template für HTML-Reports** — verworfen, weil CON-006 (kein neues NuGet) und Konsistenz mit bestehendem `ReportGenerator.cs` dagegen sprechen. StringBuilder + HtmlEncoder ist ausreichend.
- **ALT-003**: **BenchmarkDotNet für Performance-Tests** — verworfen, weil der bestehende `PerformanceBenchmarkTests` (Stopwatch + Assert) ausreicht und BenchmarkDotNet Overhead + NuGet-Dependency hinzufügen würde.
- **ALT-004**: **SQLite statt JSON für Trend-History** — verworfen, weil JSON-Files einfacher, versionierbar, und für ≤50 Reports performant genug sind.
- **ALT-005**: **SpecFlow/Gherkin für Benchmark-Tests** — verworfen. xUnit Theory + JSONL Data ist das etablierte Pattern im Projekt und erfordert keine zusätzliche Tooling-Dependency.

---

## 4. Dependencies

- **DEP-001**: `System.Text.Json` — bereits vorhanden, wird für JSON-Report-Serialization und Ground-Truth-Loading genutzt.
- **DEP-002**: `System.Text.Encodings.Web` — Teil von .NET 10 BCL, wird für `HtmlEncoder.Default.Encode()` in HTML-Reports benötigt.
- **DEP-003**: `xUnit` + `xUnit.runner.visualstudio` — bereits vorhanden.
- **DEP-004**: `BenchmarkFixture` (`IClassFixture<BenchmarkFixture>`, `IAsyncLifetime`) — existiert, generiert Stubs lazy.
- **DEP-005**: `ConsoleDetector.DetectWithConfidence()` — Production-Detection-Pipeline, wird von `BenchmarkEvaluationRunner.Evaluate()` aufgerufen.
- **DEP-006**: `benchmark/gates.json` — Gate-Konfiguration, wird von `CoverageValidator` gelesen.
- **DEP-007**: GitHub Actions Runner `windows-latest` mit .NET 10 SDK — für CI-Pipeline.

---

## 5. Files

### Neue Dateien

- **FILE-001**: `src/RomCleanup.Tests/Benchmark/QualityGateTests.cs` — Quality Gate Tests (M4/M7/M8/M9a) mit harten Schwellenwerten. Phase 1.
- **FILE-002**: `src/RomCleanup.Tests/Benchmark/BenchmarkHtmlReportWriter.cs` — HTML-Report-Generator mit Metriken-Tabelle, Confusion-Matrix, Trend-Section. Phase 3.
- **FILE-003**: `src/RomCleanup.Tests/Benchmark/TrendAnalyzer.cs` — N-Run Trend-Analyse mit linearer Regression pro Metrik. Phase 3.
- **FILE-004**: `src/RomCleanup.Tests/Benchmark/AntiGamingGateTests.cs` — Anti-Gaming Gates (M15/M16). Phase 4.
- **FILE-005**: `.github/workflows/benchmark-gate.yml` — GitHub Actions CI-Pipeline (PR-Gate + Nightly). Phase 3.
- **FILE-006**: `benchmark/baselines/baseline-latest.json` — Initialer Baseline-Snapshot (generiert, committed). Phase 1.

### Bestehende Dateien (Modifikation)

- **FILE-007**: `src/RomCleanup.Tests/Benchmark/MetricsAggregator.cs` — Um `ExtendedMetrics` Record und `CalculateExtended()` Methode erweitern. Phase 1.
- **FILE-008**: `src/RomCleanup.Tests/Benchmark/BenchmarkVerdict.cs` — `BenchmarkSampleResult` um `ActualCategory` erweitern. Phase 1.
- **FILE-009**: `src/RomCleanup.Tests/Benchmark/GroundTruthComparator.cs` — `ActualCategory` in `Compare()` befüllen. Phase 1.
- **FILE-010**: `src/RomCleanup.Tests/Benchmark/MetricsAggregatorTests.cs` — Unit-Tests für M8–M16. Phase 1.
- **FILE-011**: `src/RomCleanup.Tests/Benchmark/BenchmarkReportWriter.cs` — `WriteCsv()` Methode + `PerSampleVerdicts` in Report-Record. Phase 3/4.
- **FILE-012**: `src/RomCleanup.Tests/Benchmark/BaselineComparator.cs` — `ComparePerSample()` Methode. Phase 4.
- **FILE-013**: `src/RomCleanup.Tests/Benchmark/BaselineRegressionGateTests.cs` — HTML + CSV Export integrieren. Phase 3.
- **FILE-014**: `benchmark/manifest.json` — Version + Entry-Count aktualisieren. Phase 2/4.
- **FILE-015**: `benchmark/ground-truth/*.jsonl` — Expansion durch DatasetExpander. Phase 2.

### Unveränderte Kern-Dateien (Referenz)

- **FILE-016**: `src/RomCleanup.Tests/Benchmark/BenchmarkEvaluationRunner.cs` — Keine Änderung, wird von allen Gate-Tests genutzt.
- **FILE-017**: `src/RomCleanup.Tests/Benchmark/BenchmarkFixture.cs` — Keine Änderung, Shared Fixture.
- **FILE-018**: `src/RomCleanup.Tests/Benchmark/Infrastructure/CoverageValidator.cs` — Keine Änderung, prüft gates.json.
- **FILE-019**: `src/RomCleanup.Tests/Benchmark/CoverageGateTests.cs` — Keine Änderung, bereits 20+ Gate-Tests.

---

## 6. Testing

### Phase 1 Tests

- **TEST-001**: `MetricsAggregatorTests.CalculateExtended_ReturnsAll16Metrics` — Verifies alle Properties of `ExtendedMetrics` sind non-null und numerisch korrekt für bekannte Inputs.
- **TEST-002**: `MetricsAggregatorTests.M9_FalseConfidenceRate_CountsOnlyHighConfidenceWrongs` — 5 Samples: 1 Wrong+Conf90+NoConflict, 1 Wrong+Conf50, 1 Wrong+Conf90+Conflict, 2 Correct → M9 = 1/5 = 0.2.
- **TEST-003**: `MetricsAggregatorTests.M15_UnknownToWrongMigration_DetectsRegression` — Baseline: 3 Missed. Current: 1 Missed→Wrong, 2 Missed→Correct → Migration = 1/3 = 0.33.
- **TEST-004**: `MetricsAggregatorTests.M16_CCE_PerfectCalibration_ReturnsZero` — Alle Samples haben Confidence = Accuracy → CCE = 0.
- **TEST-005**: `MetricsAggregatorTests.M16_CCE_PoorCalibration_ReturnsHigh` — Samples mit Conf=90 aber 50 % Accuracy → CCE > 0.2.
- **TEST-006**: `MetricsAggregatorTests.M10_ConsoleConfusionIndex_CountsDistinctPairs` — 3 Wrong mit 2 distinct (Expected,Actual) Paaren → CCI = 2/3.
- **TEST-007**: `MetricsAggregatorTests.M11_DatExactMatchRate_FiltersByTag` — 5 Entries, 3 mit "dat-exact" Tag, 2 davon Correct → Rate = 2/3.
- **TEST-008**: `QualityGateTests.Gate_M4_WrongMatchRate_BelowThreshold` — Integration test, evaluiert alle 7 JSONL Sets.
- **TEST-009**: `QualityGateTests.Gate_M7_UnsafeSortRate_BelowThreshold` — Integration test.
- **TEST-010**: `QualityGateTests.Gate_M9a_FalseConfidenceRate_BelowThreshold` — Integration test.

### Phase 2 Tests

- **TEST-011**: `CoverageGateTests.Gate_TotalEntries_MeetsHardFail` — Total ≥ 970 (hardFail).
- **TEST-012**: `CoverageGateTests.Gate_PlatformFamily_MeetsHardFail("arcade")` — Arcade ≥ 160.
- **TEST-013**: `CoverageGateTests.Gate_PlatformFamily_MeetsHardFail("computer")` — Computer ≥ 120.
- **TEST-014**: `PerformanceBenchmarkTests` — Throughput ≥100 Samples/s.

### Phase 3 Tests

- **TEST-015**: `BenchmarkHtmlReportWriter_GeneratesValidHtml` — Output enthält `<!DOCTYPE html>`, alle Section-Headers, korrekte Metrik-Werte.
- **TEST-016**: `BenchmarkHtmlReportWriter_EscapesXssPayload` — System-Key `<script>alert(1)</script>` wird zu `&lt;script&gt;...` escaped.
- **TEST-017**: `CsvExport_PreventsCsvInjection` — Wert `=CMD(...)` wird mit führendem `'` escaped.
- **TEST-018**: `TrendAnalyzer_DetectsImprovement` — 5 Reports mit sinkendem WrongMatchRate → Trend = Improving.
- **TEST-019**: `TrendAnalyzer_DetectsDegradation` — 5 Reports mit steigendem WrongMatchRate → Trend = Degrading.

### Phase 4 Tests

- **TEST-020**: `AntiGamingGateTests.Gate_M15_NoUnknownToWrongMigration` — Integration test.
- **TEST-021**: `AntiGamingGateTests.Gate_M16_ConfidenceCalibrationError_BelowThreshold` — Integration test.
- **TEST-022**: `BaselineComparator_ComparePerSample_DetectsVerdictChanges` — Unit test für per-Sample Regression.

---

## 7. Risks & Assumptions

### Risiken

- **RISK-001**: **DatasetExpander überschreibt bestehende JSONL-Einträge** — Der Expander generiert Einträge "from scratch" (Zeile: `var expander = new DatasetExpander([])` in `DatasetExpansionTests`). Muss sichergestellt werden, dass bestehende 2.073 Einträge erhalten bleiben. Mitigation: TASK-029 prüft explizit den Merge-Modus.
- **RISK-002**: **Quality Gates failen initial** — Die aktuellen 2.073 Entries könnten WrongMatchRate > 0.5 % zeigen, weil Edge-Cases und Chaos-Mixed absichtlich schwierige Fälle enthalten. Mitigation: Initialen Baseline-Run durchführen und Schwellenwerte ggf. anpassen (aber ≤ 1 % als absolute Obergrenze).
- **RISK-003**: **M15/M16 sind ohne Baseline-History nicht evaluierbar** — M15 braucht einen Vorher-Nachher-Vergleich. Ohne committed `baseline-latest.json` wird der Test übersprungen. Mitigation: Phase 1 committed den ersten Baseline.
- **RISK-004**: **HTML-Report-Größe** — Bei 1.200+ Entries könnte eine vollständige Confusion-Matrix als HTML-Tabelle sehr groß werden. Mitigation: Nur Confusion-Paare mit Count ≥ 2 oder Top-20 anzeigen.
- **RISK-005**: **CI-Runtime** — Fullcale Benchmark (7 Sets + Performance Scale) kann >60s dauern. Mitigation: PR-Gate filtert nur QualityGate+BenchmarkRegression+CoverageGate (schnell). Nightly führt Performance separat aus.
- **RISK-006**: **ConsoleDetectionResult hat kein Category-Feld** — M14 (Category Accuracy) braucht Category-Vergleich. Falls `ConsoleDetectionResult` kein Category liefert, muss M14 über eine Heuristik (z.B. aus Tags) abgeleitet werden. Mitigation: TASK-013 prüft die Verfügbarkeit.

### Annahmen

- **ASSUMPTION-001**: Die bestehende `BenchmarkFixture` und `BenchmarkEvaluationRunner` Infrastruktur ist stabil und korrekt — kein Refactoring nötig.
- **ASSUMPTION-002**: `ConsoleDetector.DetectWithConfidence()` ist die einzige Detection-Methode, die evaluiert wird — kein separater DAT-Matching-Evaluator nötig (DAT wird implizit über DetectionSource-Tags geprüft).
- **ASSUMPTION-003**: GitHub Actions auf `windows-latest` hat .NET 10 SDK verfügbar oder kann es via `actions/setup-dotnet` installieren.
- **ASSUMPTION-004**: Die aktuellen 2.073 Ground-Truth-Einträge sind inhaltlich korrekt — keine systematische Überprüfung der JSONL-Qualität nötig (über SchemaValidator hinaus).
- **ASSUMPTION-005**: `DatasetExpander.GenerateExpansion()` generiert valide Einträge, die `SchemaValidator` bestehen. Falls nicht, werden fehlerhafte Einträge in Phase 2 gefixt.

---

## 8. Related Specifications / Further Reading

- [ADR-015: Recognition Quality Benchmark Framework](docs/architecture/ADR-015-recognition-quality-benchmark-framework.md)
- [Evaluationsstrategie](docs/architecture/EVALUATION_STRATEGY.md)
- [Recognition Quality Benchmark](docs/architecture/RECOGNITION_QUALITY_BENCHMARK.md)
- [Ground Truth Schema](docs/architecture/GROUND_TRUTH_SCHEMA.md)
- [Coverage Gap Audit](docs/architecture/COVERAGE_GAP_AUDIT.md)
- [Testset Design](docs/TESTSET_DESIGN.md)
- [Benchmark Testset Plan](plan/feature-benchmark-testset-1.md)
- [Coverage Expansion Plan](plan/feature-benchmark-coverage-expansion-1.md)
- [Coverage Matrix Implementation Plan](plan/feature-benchmark-coverage-matrix-impl-1.md)
