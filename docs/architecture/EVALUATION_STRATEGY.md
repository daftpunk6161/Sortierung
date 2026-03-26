# Recognition Quality Benchmark – Evaluationsstrategie

> **Version:** 2.0.0  
> **Status:** Proposed (ergänzt bestehende Docs)  
> **Datum:** 2026-03-21  
> **Bezug:** [ADR-015](architecture/ADR-015-recognition-quality-benchmark-framework.md), [RECOGNITION_QUALITY_BENCHMARK.md](RECOGNITION_QUALITY_BENCHMARK.md), [GROUND_TRUTH_SCHEMA.md](GROUND_TRUTH_SCHEMA.md), [COVERAGE_GAP_AUDIT.md](COVERAGE_GAP_AUDIT.md)  
> **Scope:** Architektur-Review und verbindliche Evaluationsstrategie für ROM-Erkennung, System-Detection, DAT-Matching, Sorting und Repair-Freigabe

---

## 1. Executive Verdict

### Ist „95 % Erkennung" als Ziel brauchbar?

**Nein — nicht als einzelner Gesamtwert. Ja — als differenzierte Aussage über ein Referenz-Set.**

| Fragestellung | Antwort |
|---------------|---------|
| 95 % Overall Accuracy über alle Sets? | **Nicht seriös erreichbar.** Extension-only Systeme und Chaos-Daten setzen physikalische Grenzen. |
| 95 % Console Precision im Referenz-Set? | **Erreichbar.** Header-basierte Systeme liefern >99 %, Extension-Systeme >90 %. Gewichtet >95 %. |
| 95 % Safe Sort Coverage im Referenz-Set? | **Ambitioniert, aber möglich** — setzt voraus, dass UNKNOWN korrekt blockiert wird (und nicht als Fehler zählt). |
| 95 % Recall über alle Systeme? | **Nicht erreichbar.** Arcade (DAT-abhängig, ~60 %) und Computer-Systeme (kein Header, ~65 %) drücken den Schnitt unter 85 %. |

### Was die bisherige unklare Zieldefinition verschleiert

1. **Metric Dilution** — 200 NES mit 99 % + 10 Arcade mit 50 % = „97 % gesamt". Arcade ist kaputt, Zahl sieht gut aus.
2. **UNKNOWN ≠ WRONG** — Ein System mit 10 % UNKNOWN und 0 % WRONG ist besser als eines mit 2 % UNKNOWN und 3 % WRONG. Die Gesamtzahl unterscheidet nicht.
3. **False Confidence** — Die gefährlichste Fehlerklasse (falsch + sicher → Sorting → Datenverlust) verschwindet im Durchschnitt.
4. **Heterogene Domains** — Cartridge-ROMs mit deterministischen Magic Bytes und Disc-Images ohne PVD sind fundamental verschieden. Ein einheitliches Ziel ignoriert das.

### Kurzfazit

> **RomCleanup braucht kein „95 % overall". Es braucht harte Fehlergrenzen (Wrong Match ≤ 0,5 %, Unsafe Sort ≤ 0,3 %), differenzierte Teilziele pro Systemklasse, und den Nachweis, dass UNKNOWN ehrlich statt falsch-sicher eingesetzt wird.**

Die einzige legitime kommunizierbare Aussage wäre:

> *„95 % der ROMs in einer typisch benannten Sammlung werden korrekt sortiert — mit weniger als 0,5 % Fehlzuordnungen. Die restlichen 5 % sind UNKNOWN (korrekt blockiert), nicht WRONG."*

---

## 2. Was „Erkennungsrate" überhaupt bedeuten muss

### Warum ein einzelner Prozentwert zu wenig ist

| Problem | Mechanismus | Konsequenz |
|---------|-------------|-----------|
| Klassenungleichgewicht | NES hat 50 Samples, Saturn 5 → Saturn-Fehler statistisch unsichtbar | Kaputte Sammlung bei seltenen Systemen |
| UNKNOWN/WRONG-Verwischung | Zusammenfassung → konservatives Verhalten wird bestraft | Anreiz für aggressives Matching |
| Fehlerschwere ignoriert | Falsches Sorting = Datenverlust; UNKNOWN = harmlos | Gleiche „Fehlerrate" mit fundamental verschiedenem Risiko |
| Kein Trendwert | Verbesserung in NES + Verschlechterung in PS2 → gleiche „Accuracy" | Regressions bleiben unsichtbar |

### Die sieben Ebenen der Erkennungsbewertung

Die Erkennungsleistung **muss auf sieben isolierten Ebenen separat bewertet** werden, weil jede Ebene eine andere Fehlerwirkung hat:

| Ebene | Frage | User-Impact bei Fehler | Rückwirkung |
|-------|-------|----------------------|-------------|
| **A. Container** | Dateityp korrekt? Archive vs. Einzeldatei? | Datei wird nicht analysiert → alles darunter versagt | Blocking |
| **B. System** | Richtige Konsole erkannt? | Falscher Ordner → Sammlung korrumpiert | Direkt destruktiv |
| **C. Kategorie** | GAME / BIOS / JUNK korrekt? | GAME-AS-JUNK = Datenverlust; BIOS-AS-GAME = falsches Dedupe | Direkt destruktiv |
| **D. Identität** | Korrektes Spiel / Variante? | Falsches Grouping, falsche Dedupe-Entscheidung | Indirekt destruktiv |
| **E. DAT-Match** | Richtiger DAT-Eintrag? | Falsches Rename, falscher Verify-Status | Potenziell destruktiv bei Repair |
| **F. Sorting** | Korrekt sortiert oder blockiert? | **Endgültige User-Wirkung.** Falsch = Schaden. | Primärer Qualitätsindikator |
| **G. Repair** | Sicher genug für Rename/Rebuild? | Bestimmt, ob destruktive Operationen freigegeben werden | Feature-Gate |

### Fehlinterpretationen, die aktiv vermieden werden müssen

1. **„93 % Accuracy = nur 7 % Fehler"** — Falsch. Bei 10.000 ROMs = 700 potenziell falsch sortierte Dateien.
2. **„Hoher Recall reicht"** — Falsch. Hoher Recall ohne Precision = aggressive Fehlzuordnung.
3. **„UNKNOWN-Rate sinkt → besser"** — Gefährlich. Sinkende UNKNOWN-Rate + steigende Wrong Rate = Verschlechterung.
4. **„DAT-Match-Rate steigt → besser"** — Kann durch Cross-System-False-Matches steigen.

---

## 3. Qualitätsmodell

### 3.1 Ergebnis-Taxonomie

Jede Bewertungsebene kennt exakt drei Grundzustände:

```
┌─────────────────────────────────────────────────────────────┐
│                    ERGEBNIS-TAXONOMIE                        │
├──────────────┬──────────────────────────────────────────────┤
│   CORRECT    │ Stimmt mit Ground Truth überein              │
│   UNKNOWN    │ System hat korrekt „weiß nicht" gesagt       │
│   WRONG      │ Weicht von Ground Truth ab                   │
└──────────────┴──────────────────────────────────────────────┘

UNKNOWN IST KEIN FEHLER. WRONG IST EIN FEHLER.
```

### 3.2 Fehlerzustände pro Ebene

#### A) Container-Erkennung

| Zustand | Beispiel | Schwere |
|---------|---------|---------|
| CORRECT | .zip → ZIP erkannt | ✓ |
| WRONG-TYPE | .bin als ISO statt raw Binary | Mittel |
| WRONG-CONTAINER | Korruptes ZIP nicht erkannt, 7z ignoriert | Hoch (kaskadiert) |

#### B) System-/Konsolenerkennung

| Zustand | Beispiel | Schwere |
|---------|---------|---------|
| CORRECT | NES-ROM → NES | ✓ |
| UNKNOWN | Unbekannte .bin → UNKNOWN | Akzeptabel |
| WRONG-CONSOLE | PS2-ISO als PS1 erkannt | **Kritisch** — Datenverlust |
| AMBIGUOUS-CORRECT | GB/GBC unsicher, aber beide akzeptabel | Akzeptabel |
| AMBIGUOUS-WRONG | Hypothesen enthalten richtiges System nicht | Fehler |

#### C) Kategorie-Erkennung

| Zustand | Beispiel | Schwere |
|---------|---------|---------|
| CORRECT | Game=Game, BIOS=BIOS | ✓ |
| GAME-AS-JUNK | Super Mario als Demo klassifiziert | **Kritisch** — Datenverlust |
| GAME-AS-BIOS | Spiel als BIOS | Hoch — falsche Dedupe |
| BIOS-AS-GAME | scph5500.bin als Spiel | Hoch — falsche Dedupe |
| JUNK-AS-GAME | Demo-ROM als Game | Mittel — Qualitätsverlust |
| UNKNOWN | Nicht klassifizierbar | Akzeptabel |

#### D) Spielidentität

| Zustand | Beispiel |
|---------|---------|
| CORRECT | „Super Mario Bros." → Super Mario Bros. |
| WRONG-GAME | „Tetris" → Zelda |
| WRONG-VARIANT | Richtige Game, falsche Region/Version/Disc |
| CORRECT-GROUP | Im richtigen Dedupe-Cluster, aber nicht exakte Variante |
| UNKNOWN | Keine Zuordnung |

#### E) DAT-Matching

| Zustand | Beispiel |
|---------|---------|
| EXACT | SHA1 matches → richtiges Spiel im richtigen System |
| WRONG-SYSTEM | Hash existiert, aber in anderem System-DAT |
| WRONG-GAME | Hash stimmt, anderes Spiel zugeordnet |
| NONE-EXPECTED | Kein Match, keiner erwartet (Homebrew) |
| NONE-MISSED | Kein Match, obwohl DAT-Eintrag existiert |
| FALSE-MATCH | Match gefunden, aber falsch |

#### F) Sorting-Entscheidung

| Zustand | Beispiel | Schwere |
|---------|---------|---------|
| CORRECT-SORT | Richtig in NES/ sortiert | ✓ |
| CORRECT-BLOCK | Unsicheres UNKNOWN korrekt blockiert | ✓ |
| WRONG-SORT | In PS1/ statt PS2/ sortiert | **Kritisch** |
| WRONG-BLOCK | Korrekt erkannt, aber fälschlich blockiert | Mittel |
| UNSAFE-SORT | Sortiert trotz niedrigem Confidence | **Kritisch** |

#### G) Repair-Tauglichkeit

| Zustand | Kriterium |
|---------|----------|
| REPAIR-SAFE | DAT-Exact ∧ Confidence ≥ 95 ∧ ¬HasConflict |
| REPAIR-RISKY | Teilweise erfüllt |
| REPAIR-BLOCKED | Nicht genug Evidenz |

---

## 4. Metriken

### 4.1 Release-Blocker (Hard Gates)

#### M4: Wrong Match Rate

| Feld | Wert |
|------|------|
| **Was wird gemessen** | Anteil der Dateien, die einem **falschen** System zugeordnet wurden |
| **Formel** | `Σ WRONG / Σ TOTAL` |
| **Warum kritisch** | Jeder Wrong Match ist potenzieller Datenverlust bei Sorting |
| **Fehlinterpretation** | Kann niedrig aussehen bei hoher UNKNOWN-Rate. Deshalb parallel M5 monitoren. |
| **Ziel** | ≤ 0,5 % global; ≤ 1 % pro System |
| **Gate** | Hard-Fail. Build wird blockiert. |

#### M6: False Confidence Rate

| Feld | Wert |
|------|------|
| **Was wird gemessen** | Anteil der WRONG-Matches mit Confidence ≥ 80 |
| **Formel** | `|{f ∈ WRONG : Confidence(f) ≥ 80}| / |WRONG|` |
| **Warum kritisch** | False Confidence = System „lügt überzeugend" → Sorting passiert → Datenverlust |
| **Fehlinterpretation** | Bezieht sich nur auf WRONG. Hohe Rate bei wenig WRONG < hohe Rate bei viel WRONG. |
| **Ziel** | ≤ 5 % |
| **Gate** | Hard-Fail |

#### M7: Unsafe Sort Rate

| Feld | Wert |
|------|------|
| **Was wird gemessen** | Anteil der Sorting-Entscheidungen, die in den falschen Ordner führen |
| **Formel** | `|WRONG-SORT| / |TOTAL-SORT-DECISIONS|` |
| **Warum kritisch** | **Direkte Messung des User-sichtbaren Schadens** |
| **Fehlinterpretation** | Nur sortierte Dateien. Blockierte nicht einbezogen. |
| **Ziel** | ≤ 0,3 % — harter Release-Blocker |
| **Gate** | Hard-Fail |

#### M9a: GAME-AS-JUNK Rate

| Feld | Wert |
|------|------|
| **Was wird gemessen** | Rate der echten Spiele, die als Junk klassifiziert werden |
| **Formel** | `Confusion[Game→Junk] / Total[Game]` |
| **Warum kritisch** | GAME-AS-JUNK = Datenverlust (Spiel wird gelöscht/ignoriert) |
| **Ziel** | ≤ 0,1 % |
| **Gate** | Hard-Fail |

### 4.2 Qualitätsmetriken (Mindest-Standard)

#### M1: Console Precision (pro System)

| Feld | Wert |
|------|------|
| **Was wird gemessen** | Von allen als System X erkannten: wie viele sind wirklich X? |
| **Formel** | `TP(X) / (TP(X) + FP(X))` |
| **Fehlinterpretation** | Hohe Precision sagt nichts über Completeness |
| **Ziel** | ≥ 98 % (Referenz), ≥ 93 % (Chaos), ≥ 85 % (Edge) [pro System mit >10 Testfällen] |

#### M2: Console Recall (pro System)

| Feld | Wert |
|------|------|
| **Was wird gemessen** | Von allen wahren System-X-Dateien: wie viele wurden erkannt? |
| **Formel** | `TP(X) / (TP(X) + FN(X))` wobei FN = WRONG + UNKNOWN |
| **Fehlinterpretation** | Trennt nicht zwischen WRONG und UNKNOWN |
| **Ziel** | ≥ 92 % (Header-Systeme), ≥ 75 % (Extension-only) |

#### M3: Console F1 (pro System, Macro-Aggregat)

| Feld | Wert |
|------|------|
| **Was wird gemessen** | Harmonisches Mittel aus M1 und M2 |
| **Formel** | `2 × (Precision × Recall) / (Precision + Recall)` |
| **Fehlinterpretation** | Kann hoch sein bei kleiner Stichprobe → Minimum N erforderlich |
| **Ziel** | ≥ 92 % gewichteter Macro-F1 |

#### M5: Unknown Rate

| Feld | Wert |
|------|------|
| **Was wird gemessen** | Anteil der Dateien mit UNKNOWN-Ergebnis |
| **Formel** | `Σ UNKNOWN / Σ TOTAL` |
| **Fehlinterpretation** | Sinkend ≠ besser (nur, wenn M4 nicht steigt). **Muss immer parallel zu M4 betrachtet werden.** |
| **Ziel** | ≤ 8 % (Referenz), ≤ 25 % (Chaos), ≤ 35 % (Edge) |

#### M8: Safe Sort Coverage

| Feld | Wert |
|------|------|
| **Was wird gemessen** | Anteil korrekt und sicher sortierter Dateien |
| **Formel** | `|CORRECT-SORT| / |TOTAL|` |
| **Fehlinterpretation** | Steigt nur bei besserer Erkennung UND passendem Confidence-Gate |
| **Ziel** | ≥ 80 % (Referenz), ≥ 60 % (Chaos) |

### 4.3 Anti-Gaming-Metriken

#### M15: UNKNOWN→WRONG Migration Rate

| Feld | Wert |
|------|------|
| **Was wird gemessen** | Bei Build-Vergleich: Dateien, die vorher UNKNOWN waren und jetzt WRONG |
| **Formel** | `|{f : prev=UNKNOWN ∧ curr=WRONG}| / |{f : prev=UNKNOWN}|` |
| **Warum kritisch** | Erkennt aggressive Matching-Strategien, die UNKNOWN senken, aber Fehler einführen |
| **Ziel** | ≤ 2 % pro Build-Diff |
| **Gate** | Hard-Fail |

#### M16: Confidence Calibration Error

| Feld | Wert |
|------|------|
| **Was wird gemessen** | Abweichung zwischen Confidence-Wert und tatsächlicher Korrektheit |
| **Formel** | `Σ |bucket_confidence - bucket_accuracy| / num_buckets` (Buckets: 0-39, 40-59, 60-79, 80-89, 90-100) |
| **Warum kritisch** | „90 % Confidence" soll ≈ 90 % Korrektheit bedeuten |
| **Ziel** | ≤ 10 % |
| **Gate** | Warn |

### 4.4 Informationsmetriken (kein Gate)

| ID | Metrik | Berechnung | Zweck |
|----|--------|-----------|--------|
| M9 | Category Confusion Rate | Off-diagonal / total per confusion matrix | Verwechslungsmuster identifizieren |
| M10 | Console Confusion Rate | C[A][B] / Σ C[A][*] pro Paar | Systematische Verwechslungen (PS1↔PS2, GB↔GBC) |
| M11 | DAT Exact Match Rate | \|DAT-EXACT\| / \|DAT-VERIFIABLE\| | DAT-Integrations-Qualität |
| M12 | DAT Weak Match Rate | \|DAT-LOOKUPANY\| / \|DAT-TOTAL\| | Fallback-Abhängigkeit |
| M13 | Ambiguous Match Rate | \|HasConflict=true\| / \|TOTAL\| | Unsicherheits-Anteil |
| M14 | Repair-Safe Match Rate | \|DAT-Exact ∧ Conf≥95 ∧ ¬Conflict\| / \|TOTAL\| | Repair-Feature-Readiness |

---

## 5. Benchmark-Datensatz

### 5.1 Ist-Zustand

| Dataset | Einträge | Bewertung |
|---------|----------|-----------|
| golden-core.jsonl | 648 | ✅ Deutlich über 400-Ziel |
| golden-realworld.jsonl | 493 | ✅ Über 350-Ziel |
| edge-cases.jsonl | 198 | ✅ Über 150-Ziel |
| negative-controls.jsonl | 80 | ✅ Über 40-Minimum |
| chaos-mixed.jsonl | 204 | ✅ Über 150-Ziel |
| repair-safety.jsonl | 100 | ✅ Über 80-Minimum |
| dat-coverage.jsonl | 350 | ✅ Solide |
| performance-scale.jsonl | 0 | ⚠️ Noch leer |
| **Gesamt** | **2.073** | ✅ Deutlich über 1.200-Target |

### 5.2 Kritische Coverage-Lücken

| Bereich | Ist (geschätzt) | Soll (Minimum) | Delta | Priorität |
|---------|----------------|----------------|-------|-----------|
| BIOS-Fälle systemübergreifend | ~15-20 | 60 | –40 | **P0** — BIOS↔GAME-Verwechslung = Datenverlust |
| Arcade gesamt | ~80-100 | 200 | –100 | **P0** — Komplexeste Plattform |
| Computer/PC | ~40-60 | 150 | –90 | **P1** — Höchste False-Positive-Rate |
| PS1↔PS2↔PSP Disambiguation | ~15 | 30 | –15 | **P0** — Häufigster Produktionsfehler |
| Multi-File-Sets (CUE+BIN, GDI, M3U) | ~15-20 | 80 | –60 | **P1** — Disc-Set-Integrität |
| Fehlende Systeme (4 von 69) | 0 | 12 | –12 | **P0** — 100 % Systemabdeckung Pflicht |
| Redump CHD-RAW-SHA1 | ~3-5 | 8 | –5 | **P1** — Disc-DAT-Verifikation |
| TOSEC-Einträge | ~0-2 | 10 | –8 | **P1** — Computer-DAT-Abdeckung |

### 5.3 Dataset-Klassen (Pflicht)

#### Saubere Referenzdaten (golden-core + golden-realworld)

Mindestens enthalten:
- Alle 69 Systeme mit ≥1 Eintrag
- Alle 9 Detection-Methoden vertreten
- Alle 4 DAT-Ökosysteme (No-Intro, Redump, MAME, TOSEC) mit ≥3 Einträgen
- Tier-1-Systeme (9 Stk.) mit ≥20 Einträgen
- BIOS für ≥12 Systeme
- Multi-Disc für ≥5 Disc-Systeme

#### Realistische Chaos-Daten (chaos-mixed)

| Unterklasse | Min. | Beispiel |
|------------|------|---------|
| Falsch benannte Dateien | 40 | „Final Fantasy VII.bin" (PS1? PS2? PC?) |
| Gekürzte Namen | 20 | „FF7_D1.iso", „Pkmn_R.gba" |
| Unicode-Dateinamen | 15 | „ファイナルファンタジー.iso" |
| Gemischte Sammlungen | 30 | Alle ROMs in einem Ordner ohne Struktur |
| Kaputte Archive | 15 | Halb-korrupte ZIPs, truncated 7z |
| Falsche Extensions | 15 | .nes-Datei ist MD-ROM |
| Fehlende Header | 15 | NES ohne iNES, SNES ohne Copier-Header |

#### Schwierige Fälle (edge-cases)

| Unterklasse | Min. | Spezifik |
|------------|------|---------|
| Cross-System-Disambiguierung | 50 | PS1↔PS2↔PSP, GB↔GBC, MD↔32X, SAT↔DC |
| BIOS-Edge-Cases | 15 | BIOS mit spielähnlichen Namen |
| Header-Konflikte | 25 | Folder sagt NES, Header sagt SNES |
| Multi-Disc-Zuordnung | 15 | 4-Disc-Set mit gemischten Benennungen |
| Ambiguous Extensions | 15 | .bin/.rom/.img ohne weitere Evidenz |
| Repair-unsichere Fälle | 10 | Korrekt erkannt, aber nicht sicher genug für Rename |

#### Negative Kontrollfälle (negative-controls)

| Unterklasse | Min. | Zweck |
|------------|------|-------|
| Non-ROM-Dateien | 20 | .txt, .jpg, .pdf, .exe → müssen UNKNOWN bleiben |
| Irreführende Dateinamen | 10 | „Nintendo 64 Game.exe" → darf nicht als N64 erkannt werden |
| Junk/Demo/Beta/Hack | 20 | Demo-ROMs → müssen als Junk, nicht als Game klassifiziert werden |
| Leere/korrupte Dateien | 10 | 0 Bytes, nur Nullen, nur Header → UNKNOWN |

### 5.4 Keine echten ROMs

Der Benchmark-Datensatz besteht aus:
- **Synthetische Header-Stubs** (80-512 Bytes mit korrekten Magic Bytes)
- **DAT-basierte Metadaten** (öffentliche No-Intro/Redump-Dateinamen + Hashes)
- **Verzeichnisstruktur-Fixtures** (leere Dateien in benannten Ordnern)
- **Archiv-Stubs** (ZIP/7z mit korrekten inneren Namen, Minimal-Inhalt)

### 5.5 Skalierungs-Übersicht

| Dataset | Ist | Urspr. Ziel | Status |
|---------|-----|-------------|--------|
| golden-core | 648 | 400 | ✅ Übertroffen |
| golden-realworld | 493 | 350 | ✅ Übertroffen |
| edge-cases | 198 | 200 | ✅ Erreicht |
| negative-controls | 80 | 80 | ✅ Erreicht |
| chaos-mixed | 204 | 200 | ✅ Erreicht |
| repair-safety | 100 | 100 | ✅ Erreicht |
| dat-coverage | 350 | 200 | ✅ Übertroffen |
| **Gesamt** | **2.073** | **1.530** | ✅ **Ziel übertroffen** |

---

## 6. Ground Truth Modell

### 6.1 Format

**JSONL** (eine Zeile pro Testfall). Begründung: zeilenweiser Git-Diff, unabhängige Validierung pro Zeile, maschinenlesbar (C# `System.Text.Json`), filterbar (`jq`, `grep`).

### 6.2 Gespeicherte Wahrheit pro Eintrag

| Feld | Typ | Beispiel | Pflicht |
|------|-----|---------|---------|
| `expected.consoleKey` | string | `"NES"` | Ja |
| `expected.category` | enum | `"Game"` / `"Bios"` / `"Junk"` / `"Unknown"` | Ja |
| `expected.confidence` | int | `95` | Ja |
| `expected.hasConflict` | bool | `false` | Ja |
| `expected.datMatchLevel` | enum | `"exact"` / `"weak"` / `"none"` | Ja |
| `expected.datEcosystem` | enum | `"no-intro"` / `"redump"` / `"mame"` / `"tosec"` | Wenn DAT |
| `expected.sortDecision` | enum | `"sort"` / `"review"` / `"blocked"` | Ja |
| `detectionExpectations.primaryMethod` | enum | `"CartridgeHeader"` | Ja |
| `detectionExpectations.acceptableAlternatives` | string[] | `["UniqueExtension"]` | Ja |
| `difficulty` | enum | `"easy"` / `"medium"` / `"hard"` / `"adversarial"` | Ja |

### 6.3 Versionierung

- Git-tracked in `benchmark/ground-truth/*.jsonl`
- Jede Änderung = Commit mit Begründung
- Kein Bulk-Import ohne Verifikation
- JSON-Schema-Validierung per CI (`ground-truth.schema.json`)
- Referenzierte Stub-Dateien müssen existieren (CI-Prüfung)

### 6.4 Erweiterungsprozess

1. Neuen Testfall als JSONL-Zeile formulieren
2. Stub-Generator wählen oder Stub manuell erstellen
3. Erwartung manuell verifizieren (Quelle: DAT-File, Header-Spec, manuelle Prüfung)
4. Tags und Fallklasse korrekt setzen
5. Pull Request mit Begründung und Verifikationsnachweis

### 6.5 Maschinenlesbare Auswertung von Abweichungen

```json
{
  "id": "gc-PS1-ref-005",
  "expected": {"consoleKey": "PS1", "category": "Game", "sortDecision": "sort"},
  "actual":   {"consoleKey": "PS2", "confidence": 88, "sortDecision": "sort"},
  "verdicts": {
    "console": "WRONG-CONSOLE",
    "category": "CORRECT",
    "sorting": "WRONG-SORT",
    "repair": "REPAIR-BLOCKED"
  },
  "errorClass": "cross-system-disambiguation",
  "severity": "critical"
}
```

---

## 7. Evaluationspipeline

### 7.1 Technischer Ablauf

```
┌──────────────────────────────────────────────────────────────────────┐
│                     EVALUATION PIPELINE                              │
│                                                                      │
│  INPUT                                                               │
│  ├── benchmark/samples/         (generierte Stubs)                   │
│  ├── benchmark/ground-truth/    (7 JSONL-Dateien, 1152+ Einträge)   │
│  ├── data/consoles.json         (69 Systeme)                        │
│  └── benchmark/gates.json       (Coverage + Quality Thresholds)     │
│                                                                      │
│  STAGE 1: Coverage Gate                                              │
│  └── CoverageValidator prüft gates.json gegen Manifest              │
│      → FAIL = Build blockiert (Benchmark-Datensatz zu klein)        │
│                                                                      │
│  STAGE 2: Detection Run                                              │
│  └── BenchmarkEvaluationRunner pro Entry:                           │
│      ├── ConsoleDetector.DetectWithConfidence(samplePath, root)     │
│      ├── FileClassifier.Analyze(fileName)                           │
│      ├── DatIndex.Lookup(hash) [wenn DAT geladen]                   │
│      └── → BenchmarkSampleResult(Verdict, Actual, Expected)        │
│                                                                      │
│  STAGE 3: Verdikt-Vergleich                                         │
│  └── GroundTruthComparator:                                         │
│      ├── Pro Ebene (A–G): actual vs. expected                       │
│      ├── Confusion Matrices (Console×Console, Category×Category)    │
│      └── → ComparisonResult mit allen 16 Metriken                   │
│                                                                      │
│  STAGE 4: Quality Gates                                              │
│  └── M4 ≤ 0.5%, M6 ≤ 5%, M7 ≤ 0.3%, M9a ≤ 0.1%                  │
│      → FAIL = Build blockiert (Erkennungsqualität unzureichend)     │
│                                                                      │
│  STAGE 5: Regression Gate                                            │
│  └── Vergleich gegen benchmark/baselines/baseline-metrics.json      │
│      ├── M4 steigt > 0.1pp → FAIL                                  │
│      ├── M7 steigt > 0.1pp → FAIL                                  │
│      ├── M15 UNKNOWN→WRONG > 2% → FAIL                             │
│      └── M1 sinkt > 1pp pro System → WARN                          │
│                                                                      │
│  OUTPUT                                                              │
│  ├── benchmark-results.json       (Maschinen-lesbar)                │
│  ├── confusion-console.csv        (Console Confusion Matrix)        │
│  ├── confusion-category.csv       (Category Confusion Matrix)       │
│  ├── metrics-summary.json         (M1–M16 Werte)                   │
│  ├── error-details.jsonl          (Jeder Fehler mit Context)        │
│  └── trend-comparison.json        (Diff gegen Baseline)             │
└──────────────────────────────────────────────────────────────────────┘
```

### 7.2 Regressionserkennung

| Regel | Schwelle | Gate-Typ | Begründung |
|-------|---------|----------|-----------|
| M4 (Wrong Match) steigt | > +0,1 pp | Hard-Fail | Neue Fehler eingeführt |
| M7 (Unsafe Sort) steigt | > +0,1 pp | Hard-Fail | Mehr falsche Sortierungen |
| M15 (UNKNOWN→WRONG) | > 2 % Migrations | Hard-Fail | Aggressive-Matching-Detection |
| M1 (Precision) sinkt pro System | > –1 pp | Warn + Review | Systemspezifische Regression |
| M8 (Safe Sort Coverage) sinkt | > –2 pp | Warn | Funktionsverlust |
| M6 (False Confidence) steigt | > +1 pp | Warn | Confidence-Kalibrierung driftet |

### 7.3 Baseline-Management

- `benchmark/baselines/baseline-metrics.json` wird nur bei explizitem, bewusstem Update überschrieben
- Kein automatisches Baseline-Rebase bei Verbesserungen
- Baseline-Update erfordert Commit-Message mit Begründung
- Historische Baselines werden archiviert: `benchmark/baselines/archive/baseline-v{version}.json`

### 7.4 Fehlerklassen-Taxonomie für Reports

| Fehlerklasse | Beschreibung | Beispiel |
|-------------|-------------|---------|
| `cross-system-disambiguation` | Verwechslung zwischen ähnlichen Systemen | PS1↔PS2, GB↔GBC |
| `bios-game-confusion` | BIOS↔Game-Verwechslung | scph5500.bin als Game |
| `category-misclassification` | Falsche Kategorie trotz richtigem System | Game als Junk |
| `false-confidence` | Falsches Ergebnis mit hoher Sicherheit | Wrong + Confidence 92 |
| `container-failure` | Archiv/Container nicht korrekt verarbeitet | 7z ignoriert |
| `extension-collision` | Ambiguous Extension falsch aufgelöst | .bin → falsches System |
| `folder-mismatch` | Folder-Kontext widerspricht Header | NES-ROM in SNES-Ordner |
| `headerless-fallback` | Ohne Header auf schwächere Methode angewiesen | SNES ohne Copier-Header |
| `dat-false-match` | DAT-Hash in falschem System gefunden | Cross-System-Collision |
| `negative-false-positive` | Non-ROM fälschlich als ROM erkannt | .exe als Game erkannt |

---

## 8. Qualitätsziele

### 8.1 Differenzierte Zielwerte

#### System-Erkennung nach Detektionsklasse

| Systemklasse | Precision | Recall | Wrong Rate | Begründung |
|-------------|-----------|--------|-----------|-----------|
| **Deterministic Header** (NES, N64, GBA, GB, Lynx, 7800) | ≥ 99 % | ≥ 97 % | ≤ 0,2 % | Magic Bytes eindeutig |
| **Complex Header** (SNES, MD, GBC) | ≥ 97 % | ≥ 90 % | ≤ 0,5 % | Varianten existieren |
| **Disc-Header** (PS1, PS2, GC, Wii) | ≥ 96 % | ≥ 88 % | ≤ 0,5 % | PVD zuverlässig |
| **Disc-Complex** (Saturn, DC, SCD) | ≥ 95 % | ≥ 85 % | ≤ 1 % | Sektor-Offsets variabel |
| **Extension-Only** (A26, A52, Amiga, MSX) | ≥ 90 % | ≥ 70 % | ≤ 2 % | Kein Header |
| **Arcade** (MAME, Neo Geo) | ≥ 85 % | ≥ 60 % | ≤ 3 % | DAT-abhängig |
| **Computer** (DOS, C64, ZX, PC98) | ≥ 88 % | ≥ 65 % | ≤ 2 % | Kein Standard-Header |

#### Kategorie-Erkennung

| Metrik | Schwelle | Gate-Typ |
|--------|---------|----------|
| GAME-AS-JUNK | ≤ 0,1 % | **Hard-Fail** (Datenverlust) |
| BIOS-AS-GAME | ≤ 0,5 % | Hard-Fail |
| JUNK-AS-GAME | ≤ 3 % | Warn |
| Overall Category Accuracy | ≥ 95 % | Warn |

#### DAT-Matching

| Metrik | Schwelle | Gate-Typ |
|--------|---------|----------|
| DAT Exact Match Rate | ≥ 90 % | Warn |
| DAT False Match Rate | ≤ 0,5 % | Hard-Fail |
| DAT None-Missed Rate | ≤ 10 % | Warn |

#### Sorting

| Metrik | Schwelle | Gate-Typ |
|--------|---------|----------|
| **Unsafe Sort Rate** | **≤ 0,3 %** | **Hard-Fail — Release-Blocker** |
| Safe Sort Coverage (Referenz) | ≥ 80 % | Warn |
| Safe Sort Coverage (Chaos) | ≥ 55 % | Info |
| Correct Block Rate | ≥ 95 % | Warn |

### 8.2 Was 95 % konkret bedeuten könnte

Wenn das Projekt „95 %" kommunizieren will, dann **nur** so:

> **„In einer typischen, korrekt benannten ROM-Sammlung:**
> - **95 % aller Dateien werden dem richtigen System zugeordnet und sicher sortiert**
> - **Weniger als 0,5 % werden falsch zugeordnet**  
> - **Die restlichen ~4,5 % sind UNKNOWN (korrekt blockiert zur manuellen Prüfung)"**

Das impliziert:
- Referenz-Set als Maßstab (nicht Chaos)
- M8 (Safe Sort Coverage) ≥ 95 % im golden-core + golden-realworld
- M4 (Wrong Match Rate) ≤ 0,5 %
- M5 (UNKNOWN Rate) ≤ 5 % im Referenz-Set

**Für Chaos-Daten gelten realistischere Ziele:**
- Safe Sort Coverage ≥ 60 %
- Wrong Match Rate ≤ 2 %
- UNKNOWN Rate ≤ 25 %

### 8.3 Bereiche, die strengere Regeln brauchen

| Bereich | Warum strenger | Schwelle |
|---------|---------------|---------|
| BIOS↔Game-Verwechslung | Direkt destruktiv für Dedupe | Nahe 0 % |
| Cross-System (PS1↔PS2, GB↔GBC) | Häufigster Produktionsfehler | ≤ 1 % pro Paar |
| False Confidence + Sorting | System lügt und sortiert falsch | ≤ 0,3 % |
| Negative Controls | Non-ROMs dürfen nie als Game sortiert werden | 0 % False Positive Sorting |

---

## 9. Freigaberegeln

### 9.1 Sorting-Freigabe (implementiert)

| Bedingung | Schwelle | Implementiert |
|-----------|---------|--------------|
| DetectionConfidence | ≥ 80 | ✅ RunOrchestrator |
| HasConflict | false | ✅ |
| Category | Game | ✅ |
| ConsoleKey | ≠ UNKNOWN | ✅ |

### 9.2 Repair-Freigabe (zukünftig, Feature-Gate)

| Bedingung | Schwelle | Status |
|-----------|---------|--------|
| DatMatch | exact (Hash-verifiziert) | Geplant |
| DetectionConfidence | ≥ 95 | Geplant |
| HasConflict | false | Geplant |
| Console-Verifizierung | Hard Evidence (Header/DAT, nicht nur Folder) | Geplant |
| Benchmark M14 | ≥ 70 % Repair-Safe im Referenz-Set | Geplant |

### 9.3 Wann UNKNOWN zwingend ist

| Situation | Grund |
|-----------|-------|
| Confidence < 80 | Nicht genug Evidenz |
| HasConflict = true | Widersprüchliche Erkennung |
| Nur AmbiguousExtension (40) | .bin ohne weitere Signale |
| Nur FilenameKeyword (75) | User-Tag, keine harte Evidenz |
| Keine Hypothese | Unbekannte Datei |

### 9.4 Wann Ambiguous Review nötig ist

| Situation | Aktion |
|-----------|--------|
| Zwei Systeme mit Confidence ≥ 80 | User-Review erforderlich |
| DAT-Match in anderem System als Detection | User-Review |
| DAT-Match exact, Confidence < 80 | Automatisch DAT akzeptieren, aber markieren |
| Mehrere DAT-Matches für gleichen Hash | User-Review |

---

## 10. Konkrete nächste Schritte

### Priorität P0 — Ohne diese ist keine belastbare Messung möglich

| # | Schritt | Voraussetzung | Output |
|---|---------|---------------|--------|
| 1 | **QualityGateTests.cs implementieren** — M4/M6/M7/M9a als xUnit-Tests gegen berechnete Metriken | MetricsAggregator vorhanden | Hard-Fail-Gate in CI |
| 2 | **Baseline-Metriken messen und speichern** — Erster vollständiger Benchmark-Run, Ergebnis als `baseline-metrics.json` | Evaluation Runner vorhanden | Referenzpunkt für alle Regressionen |
| 3 | **BIOS-Einträge auf ≥60 erweitern** — BIOS für ≥12 Systeme in golden-core + golden-realworld | Stub-Generator vorhanden | M9a-Gate belastbar |
| 4 | **PS1↔PS2↔PSP Disambiguation auf ≥30 erweitern** — Gezielt PVD-Boot-Marker-Varianten | Disc-Stub-Generator vorhanden | Cross-System-Confusion messbar |
| 5 | **Fehlende 4 Systeme ergänzen** — 69/69 System-Coverage | consoles.json als Referenz | Kein Blind Spot |

### Priorität P1 — Essentiell für statistisch belastbare Aussagen

| # | Schritt | Output |
|---|---------|--------|
| 6 | **BaselineRegressionGateTests.cs implementieren** — M15 UNKNOWN→WRONG-Migration gegen Baseline | Automatische Regressionserkennung |
| 7 | **Arcade auf ≥200 erweitern** — Parent/Clone/BIOS/Split-Merged/CHD | Arcade-Metriken belastbar |
| 8 | **Computer/PC auf ≥150 erweitern** — 10 Systeme ohne Header | Computer-Metriken belastbar |
| 9 | **Multi-File-Sets auf ≥80 erweitern** — CUE+BIN, GDI, M3U-Varianten | Disc-Set-Integrität messbar |
| 10 | **Confusion-Matrix-Export als CSV** — Console×Console und Category×Category | Verwechslungsmuster visuell |

### Priorität P2 — Reife und Langfrist-Qualität

| # | Schritt | Output |
|---|---------|--------|
| 11 | ConfidenceCalibrationTests.cs — M16 Calibration Error berechnen | Ehrlichkeit der Confidence-Werte geprüft |
| 12 | HTML-Benchmark-Report mit Pro-System-Dashboard | Menschenlesbare Qualitätsübersicht |
| 13 | Trend-Comparison-Report (JSON Diff gegen Baseline) | Fortschritt über Zeit sichtbar |
| 14 | Datensatz auf 1.500+ erweitern (alle Lücken schließen) | Volle Statistik-Belastbarkeit |
| 15 | Repair-Freigabe-Gate als Feature-Flag vorbereiten | M14 als Voraussetzung für Repair-Feature |

### Reihenfolge der ersten 10 Schritte

```
  #1  QualityGateTests.cs          ← Ohne Gates kein Release-Schutz
  #2  Baseline messen              ← Ohne Baseline kein Regressions-Vergleich
  #3  BIOS auf ≥60                 ← Gefährlichste Lücke zuerst
  #4  PS-Disambiguation auf ≥30    ← Häufigster Produktionsfehler
  #5  69/69 Systeme                ← Keine Blind Spots
  #6  BaselineRegressionGateTests.cs ← Anti-Gaming-Schutz
  #7  Arcade auf ≥200              ← Komplexeste Plattform
  #8  Computer/PC auf ≥150         ← Höchste False-Positive-Rate
  #9  Multi-File auf ≥80           ← Disc-Integrität
  #10 Confusion-Matrix-Export      ← Diagnostik-Werkzeug
```

---

## Anhang A: Warum bestimmte Bereiche 95 % nicht erreichen können

| Bereich | Physikalische Grenze | Maximale realistische Precision |
|---------|---------------------|-------------------------------|
| Atari 2600 | Kein Header, nur .a26 Extension | ~90 % (falsch benannte .a26 = Fehler) |
| Amiga | ADF-Format ohne System-ID im Header | ~88 % (nur Extension + Folder) |
| Arcade (MAME) | Parent/Clone nur via DAT auflösbar | ~85 % (ohne DAT = UNKNOWN) |
| .bin-Dateien ohne Header | 15+ Systeme teilen .bin | ~55 % (ohne Kontext nicht lösbar) |
| Disc-Images ohne PVD | Raw .bin ohne CUE | ~60 % (keine Sektordaten = kein PVD) |
| PC-Spiele | Kein Standard-Format | ~65 % (Folder + Keyword einzige Signale) |

## Anhang B: Bekannte Cross-System-Verwechslungspaare

| Paar | Ursache | Gegenmaßnahme | Test-Minimum |
|------|---------|---------------|-------------|
| PS1 ↔ PS2 | Serial-Overlap (SLUS) | Digit-Count-Disambiguierung | 15 |
| PS2 ↔ PSP | PVD-Signatur ähnlich | Boot-Marker (BOOT2 vs PBP) | 10 |
| GB ↔ GBC | CGB-Flag 0x80 (Dual-Mode) | Konvention: 0x80=GBC, 0xC0=GBC-only | 8 |
| Genesis ↔ 32X | Beide „SEGA" @ 0x100 | „SEGA 32X" vs „SEGA MEGA DRIVE" | 6 |
| SNES ↔ SFC | Gleiche Hardware | Region-Tag / DAT | 4 |
| Saturn ↔ DC | Ähnliche IP.BIN-Struktur | Sektor-Offset + Signatur-Text | 6 |
| DC ↔ Naomi | Gleiche GD-ROM-Basis | Sector-Content | 4 |
| Neo Geo AES ↔ MVS ↔ CD | Gleiche IPs | Plattform-Flag + DAT | 8 |
