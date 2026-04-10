# ADR-015: Recognition Quality Benchmark Framework

## Status
Proposed

## Datum
2026-03-21

## Kontext

Romulus trifft **destruktive Entscheidungen** (Move, Sort, Dedupe, Rename) auf Basis seiner Erkennungspipeline. Jede Fehlentscheidung ist ein potenzieller Datenverlust. Bisher existiert kein formal validiertes, durchgängig messbares Qualitätsmodell, das alle Erkennungsebenen isoliert bewertet und objektive Regressionserkennung ermöglicht.

Die Infrastruktur (8-stufige Detection Cascade gemäß ADR-0014, HypothesisResolver, 2.073 Ground-Truth-Einträge, Stub-Generatoren, CoverageGateTests, MetricsAggregator) ist bereits vorhanden. Was fehlt, ist die **verbindliche architektonische Entscheidung**, wie diese Infrastruktur als Release-Gate, Regressions-Schutz und Qualitätsnachweis verbindlich eingesetzt wird.

### Treiber

1. Die Frage „Ist 95 % Erkennung erreichbar?" ist ohne formales Qualitätsmodell nicht seriös beantwortbar.
2. Overall Accuracy als einzelner Prozentwert ist irreführend (Metric Dilution, verschleierte Wrong Matches).
3. Sorting/Repair-Freigabe muss an messbare Schwellen gebunden sein, nicht an subjektive Einschätzung.
4. Regressions in der Erkennung müssen automatisch erkannt und blockiert werden.
5. Anti-Gaming: Aggressive Matching darf UNKNOWN-Rate nicht auf Kosten von Wrong Match Rate senken.

## Entscheidung

### 1. Kein einzelner „95 % Accuracy"-Wert als Ziel

**95 % als Einzelzahl ist abgelehnt.** Stattdessen: differenzierte Zielwerte pro Erkennungsebene und pro Datenklasse.

**Begründung:**
- 200 NES-Testfälle mit 99 % Precision + 10 Arcade-Testfälle mit 50 % Precision = 97 % Global Precision. Der Nutzer sieht „97 %", Arcade ist kaputt. Das ist Metric Dilution.
- UNKNOWN ≠ WRONG. Ein konservatives System (10 % UNKNOWN, 0,1 % WRONG) ist sicherer als ein aggressives (2 % UNKNOWN, 3 % WRONG), obwohl das aggressive „besser" aussieht.
- Header-basierte Systeme (NES, GBA, N64) erreichen >99 % deterministisch. Extension-only Systeme (Atari 2600, Amiga) können prinzipiell nicht über ~90 % kommen. Eine Gesamtzahl verwischt das.

**Was „95 %" legitimerweise bedeuten kann:**
> „95 % der ROMs in einer typischen, korrekt benannten Sammlung werden dem richtigen System zugeordnet und sicher sortiert – bei weniger als 0,5 % Fehlzuordnungen. Die restlichen 5 % sind UNKNOWN (korrekt blockiert), nicht WRONG."

Das entspricht: Safe Sort Coverage (M8) ≥ 95 % im Referenz-Set bei Wrong Match Rate (M4) ≤ 0,5 %.

### 2. Sieben-Ebenen-Qualitätsmodell

Jede Erkennungsentscheidung wird auf sieben isolierten Ebenen bewertet:

| Ebene | Frage | Ergebnis-Taxonomie | Release-Relevanz |
|-------|-------|--------------------|------------------|
| A. Container | Dateityp korrekt erkannt? | CORRECT / WRONG-TYPE / WRONG-CONTAINER | Blocking bei >2 % WRONG |
| B. System | Richtiges System erkannt? | CORRECT / UNKNOWN / WRONG-CONSOLE / AMBIGUOUS-* | Blocking bei M4 > 0,5 % |
| C. Kategorie | GAME/BIOS/JUNK korrekt? | CORRECT / GAME-AS-JUNK / BIOS-AS-GAME / ... | Blocking bei GAME-AS-JUNK > 0,1 % |
| D. Spielidentität | Korrektes Spiel/Variante? | CORRECT / WRONG-GAME / WRONG-VARIANT / UNKNOWN | Informativ (kein Gate) |
| E. DAT-Match | Korrekter Hash-Match? | EXACT / WRONG-SYSTEM / NONE-MISSED / FALSE-MATCH | Blocking bei FALSE-MATCH > 0,5 % |
| F. Sorting | Korrekt sortiert/blockiert? | CORRECT-SORT / WRONG-SORT / CORRECT-BLOCK / UNSAFE-SORT | Blocking bei M7 > 0,3 % |
| G. Repair | Sicher genug für Rename/Rebuild? | REPAIR-SAFE / REPAIR-RISKY / REPAIR-BLOCKED | Informativ (Gate für Repair-Feature) |

**Kritische Unterscheidung:** UNKNOWN ist kein Fehler. UNKNOWN ist eine korrekte Entscheidung bei unzureichender Evidenz. Nur WRONG ist ein destruktiver Fehler.

### 3. Metrik-Modell (16 Metriken)

#### Release-Blocker-Metriken (Regressions-Gate)

| ID | Metrik | Berechnung | Threshold | Gate-Typ |
|----|--------|------------|-----------|----------|
| M4 | Wrong Match Rate | Σ WRONG / Σ TOTAL | ≤ 0,5 % global, ≤ 1 % pro System | Hard-Fail |
| M6 | False Confidence Rate | \|{f ∈ WRONG : Conf ≥ 80}\| / \|WRONG\| | ≤ 5 % | Hard-Fail |
| M7 | Unsafe Sort Rate | \|WRONG-SORT\| / \|TOTAL-SORT\| | ≤ 0,3 % | Hard-Fail |
| M9a | GAME-AS-JUNK Rate | Confusion[Game→Junk] / Total | ≤ 0,1 % | Hard-Fail |

#### Qualitäts-Metriken (Mindest-Standard)

| ID | Metrik | Referenz-Set | Chaos-Set | Edge-Case-Set |
|----|--------|-------------|-----------|---------------|
| M1 | Console Precision (pro System) | ≥ 98 % | ≥ 93 % | ≥ 85 % |
| M2 | Console Recall (pro System) | ≥ 92 % | ≥ 75 % | ≥ 60 % |
| M3 | Console F1 (Macro, gewichtet) | ≥ 92 % | ≥ 80 % | ≥ 70 % |
| M5 | Unknown Rate | ≤ 8 % | ≤ 25 % | ≤ 35 % |
| M8 | Safe Sort Coverage | ≥ 80 % | ≥ 60 % | ≥ 45 % |

#### Anti-Gaming-Metriken

| ID | Metrik | Threshold | Zweck |
|----|--------|-----------|-------|
| M15 | UNKNOWN→WRONG Migration Rate | ≤ 2 % pro Build-Diff | Erkennt aggressive Matching |
| M16 | Confidence Calibration Error | ≤ 10 % | Prüft ob Confidence-Werte ehrlich sind |

#### Informations-Metriken (kein Gate, aber reported)

M9 (Category Confusion), M10 (Console Confusion), M11 (DAT Exact Match), M12 (DAT Weak Match), M13 (Ambiguous Match), M14 (Repair-Safe Match)

### 4. Differenzierte Zielwerte nach Systemklasse

| Systemklasse | Precision | Recall | Begründung |
|-------------|-----------|--------|------------|
| Deterministic Header (NES, N64, GBA, GB, Lynx, 7800) | ≥ 99 % | ≥ 97 % | Magic Bytes sind eindeutig |
| Complex Header (SNES, MD, GBC) | ≥ 97 % | ≥ 90 % | Varianten (LoROM/HiROM, CGB-Flag) |
| Disc-Header (PS1, PS2, GC, Wii) | ≥ 96 % | ≥ 88 % | PVD zuverlässig, .bin-only schwierig |
| Disc-Complex (Saturn, DC, SCD) | ≥ 95 % | ≥ 85 % | Sektor-Offsets variabel |
| Extension-Only (A26, A52, Amiga, MSX) | ≥ 90 % | ≥ 70 % | Kein deterministischer Header |
| Arcade (MAME, Neo Geo) | ≥ 85 % | ≥ 60 % | Stark DAT-abhängig |
| Computer (DOS, C64, ZX, PC98) | ≥ 88 % | ≥ 65 % | Kein Standard-Header |

**95 % Precision über alle Systemklassen ist mit der aktuellen Architektur erreichbar, aber nur auf gewichtetem Aggregat.** 95 % Recall ist für Extension-only-Systeme und Arcade nicht seriös erreichbar.

### 5. Benchmark-Datensatz-Architektur

**Ist-Stand:** 2.073 Ground-Truth-Einträge in 8 JSONL-Dateien + Schema + Gates.

**Bewertung:** Die Infrastruktur ist strukturell stark. Die Coverage-Gap-Audit identifiziert korrekt die Lücken:

| Bereich | Ist | Soll (Minimum) | Bewertung |
|---------|-----|----------------|-----------|
| Gesamt-Einträge | 2.073 | 1.200 | ✅ Deutlich über Target |
| Systeme abgedeckt | ~65 | 69 | ⚠️ 4 fehlende Systeme |
| BIOS-Fälle | ~15-20 | 60 | ❌ Kritisch unterdimensioniert |
| Arcade gesamt | ~80-100 | 200 | ❌ Kritisch unterdimensioniert |
| Computer/PC | ~40-60 | 150 | ❌ Unterdimensioniert |
| PS1↔PS2↔PSP Disambiguation | ~15 | 30 | ⚠️ Zu dünn |
| Multi-File-Sets | ~15-20 | 80 | ❌ Unterdimensioniert |
| Negative Controls | 62 | 40 | ✅ Über Minimum |

**Architektur-Entscheidung:** Der Benchmark-Datensatz wird auf mindestens **1.500 Einträge** erweitert, mit Priorität auf:
1. BIOS-Fälle (→ 60)
2. Arcade Parent/Clone/BIOS/Split-Merged (→ 200)
3. Computer/PC (→ 150)
4. Fehlende 4 Systeme (→ 69/69)
5. Multi-File/Multi-Disc (→ 80)

### 6. Evaluationspipeline als CI-Gate

```
PR / Build
    │
    ▼
┌─────────────────────────────────────────────────┐
│  1. Coverage Gate (CoverageGateTests)           │
│     - 1152+ Einträge ≥ hard-fail 970           │
│     - 65+ Systeme, 20 Fallklassen              │
│     - Platform-Family-Minima erfüllt            │
│     FAIL = Build blockiert                      │
└─────────────┬───────────────────────────────────┘
              │
              ▼
┌─────────────────────────────────────────────────┐
│  2. Detection Benchmark Run                     │
│     - BenchmarkEvaluationRunner                 │
│     - Pro Entry: Detect → Compare → Verdict     │
│     - Output: metrics-summary.json              │
└─────────────┬───────────────────────────────────┘
              │
              ▼
┌─────────────────────────────────────────────────┐
│  3. Quality Gate (Release-Blocker-Metriken)     │
│     - M4 Wrong Match Rate ≤ 0.5%               │
│     - M6 False Confidence Rate ≤ 5%            │
│     - M7 Unsafe Sort Rate ≤ 0.3%              │
│     - M9a GAME-AS-JUNK ≤ 0.1%                 │
│     FAIL = Build blockiert                      │
└─────────────┬───────────────────────────────────┘
              │
              ▼
┌─────────────────────────────────────────────────┐
│  4. Regression Gate (Baseline-Vergleich)        │
│     - M4 steigt > 0.1pp → FAIL                 │
│     - M7 steigt > 0.1pp → FAIL                 │
│     - M15 UNKNOWN→WRONG > 2% → FAIL            │
│     - M1 sinkt > 1pp für ein System → WARN     │
│     FAIL = Build blockiert                      │
│     WARN = Review required                      │
└─────────────┬───────────────────────────────────┘
              │
              ▼
┌─────────────────────────────────────────────────┐
│  5. Report-Artefakte                            │
│     - benchmark-results.json                    │
│     - confusion-console.csv                     │
│     - metrics-summary.json                      │
│     - trend-comparison.json                     │
│     - error-details.jsonl                       │
└─────────────────────────────────────────────────┘
```

### 7. Sorting-Freigaberegeln

Sorting ist nur erlaubt, wenn:

| Bedingung | Schwelle | Status |
|-----------|---------|--------|
| DetectionConfidence | ≥ 80 | ✅ Implementiert |
| HasConflict | false | ✅ Implementiert |
| Category | Game | ✅ Implementiert |
| ConsoleKey | ≠ UNKNOWN | ✅ Implementiert |

### 8. Repair-Freigaberegeln (zukünftig)

Repair (Rename/Rebuild/Cross-Root) ist nur erlaubt, wenn:

| Bedingung | Schwelle | Status |
|-----------|---------|--------|
| DatMatch | exact (Hash-verifiziert) | Geplant |
| DetectionConfidence | ≥ 95 | Geplant |
| HasConflict | false | Geplant |
| Console-Verifizierung | via DAT oder Hard Evidence | Geplant |

### 9. Anti-Gaming-Schutz

Um zu verhindern, dass Metriken durch aggressiveres Matching künstlich verbessert werden:

1. **M15 (UNKNOWN→WRONG Migration)**: Jede Reduktion der UNKNOWN-Rate muss beweisen, dass die Wrong Match Rate nicht steigt. Threshold: ≤ 2 % der vorher als UNKNOWN klassifizierten Dateien dürfen bei neuem Build WRONG sein.

2. **M16 (Confidence Calibration Error)**: Confidence-Werte müssen ehrlich sein. „90 % Confidence" soll ≈ 90 % Korrektheit bedeuten. Bucket-weise (0-39, 40-59, 60-79, 80-89, 90-100) gemessen.

3. **Confusion Matrix**: Systematische Verwechslungspaare (PS1↔PS2, GB↔GBC, MD↔32X) werden pro Build als CSV exportiert. Jede neue Verwechselungs-Häufung > 2 % eines Paares ist ein Regression-WARN.

## Alternativen (geprüft und verworfen)

### A) „95 % Overall Accuracy" als einzelnes Ziel
Verworfen: Verschleiert Fehlertypen, bestraft konservatives UNKNOWN-Verhalten, erlaubt Metric Dilution.

### B) Nur Unit-Tests ohne Ground-Truth-Benchmark
Verworfen: Unit-Tests prüfen Codepfade, nicht Erkennungsqualität in realistischen Szenarien. 3.000+ grüne Unit-Tests garantieren nicht, dass Saturn-ISOs korrekt erkannt werden.

### C) Manuelles Testen statt automatisierter Pipeline
Verworfen: Nicht reproduzierbar, nicht skalierbar, führt zu Regressions-Blindheit.

### D) Nur Referenz-Set ohne Chaos/Edge-Cases
Verworfen: Referenz-Set prüft Happy Path. Real-World hat 30-50 % Chaos-Anteil. Ohne Chaos-Tests = falsche Sicherheit.

## Betroffene Dateien

### Bestehend (validiert)
| Datei | Zweck | Status |
|-------|-------|--------|
| `benchmark/gates.json` | Machine-readable Coverage Gates | ✅ Vorhanden |
| `benchmark/ground-truth/*.jsonl` (8 Dateien) | 2.073 Ground-Truth-Einträge | ✅ Vorhanden |
| `benchmark/ground-truth/ground-truth.schema.json` | JSON-Schema-Validierung | ✅ Vorhanden |
| `benchmark/manifest.json` | Datensatz-Metadaten | ✅ Vorhanden |
| `src/Romulus.Tests/Benchmark/BenchmarkEvaluationRunner.cs` | Evaluation Runner | ✅ Vorhanden |
| `src/Romulus.Tests/Benchmark/GroundTruthComparator.cs` | Verdikt-Vergleich | ✅ Vorhanden |
| `src/Romulus.Tests/Benchmark/MetricsAggregator.cs` | Metrik-Berechnung | ✅ Vorhanden |
| `src/Romulus.Tests/Benchmark/CoverageGateTests.cs` | Coverage-Gate-Tests | ✅ Vorhanden |
| `src/Romulus.Tests/Benchmark/GoldenCoreBenchmarkTests.cs` | Golden-Core-Benchmark | ✅ Vorhanden |

### Erweiterungen nötig
| Datei | Zweck | Priorität |
|-------|-------|-----------|
| `src/Romulus.Tests/Benchmark/QualityGateTests.cs` | M4/M6/M7/M9a Hard-Fail-Gates | P0 |
| `src/Romulus.Tests/Benchmark/BaselineRegressionGateTests.cs` | Baseline-Vergleich M15 | P1 |
| `src/Romulus.Tests/Benchmark/ConfidenceCalibrationTests.cs` | M16 Calibration Error | P2 |
| `benchmark/baselines/baseline-metrics.json` | Gespeicherte Referenz-Metriken | P1 |
| `src/Romulus.Tests/Benchmark/BenchmarkHtmlReportWriter.cs` | HTML/JSON Report Output | P2 |

## Konsequenzen

### Positiv
- Erkennungsqualität wird **objektiv messbar** statt „gefühlt gut"
- Regressions werden **automatisch erkannt** vor Merge
- Sorting wird nur bei **bewiesener Sicherheit** freigegeben
- Anti-Gaming verhindert künstliche Metrik-Inflation
- Differenzierte Ziele verhindern, dass starke Systeme schwache verschleiern

### Negativ
- BIOS/Arcade/Computer-Lücken im Benchmark müssen zuerst gefüllt werden (Aufwand)
- Quality Gates können Builds blockieren, wenn Erkennung verschlechtert wird (gewollt, aber kann kurzfristig bremsen)
- Baseline-Management erfordert Disziplin (nur explizite Updates)

### Neutral
- Die bestehende Infrastruktur ist **strukturell solide** – keine Neuentwicklung nötig, nur Erweiterung der Gates und Befüllung der Lücken

## Risiken

| Risiko | Mitigation |
|--------|-----------|
| Overfitting auf Benchmark | Anti-Gaming-Metriken M15/M16; getrennte Hold-Out-Sets in edge-cases |
| Maintenance-Last für Ground-Truth | Code-Review-Pflicht; kein Bulk-Import ohne Verifikation |
| Falsche Gate-Kalibrierung | Erster Baseline-Run definiert initiale Thresholds; danach iterativ verschärfen |
| Build-Slowdown durch Benchmark-Runs | Golden-Core (<10s) läuft immer; Full-Benchmark nur bei Änderungen an Core/Classification |
