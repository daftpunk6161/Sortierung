# ADR-017: Real-World Testset System — Comprehensive Architecture Review

## Status
**Proposed** — Konsolidiert und erweitert ADR-015, ADR-016, TESTSET_DESIGN.md, GROUND_TRUTH_SCHEMA.md, EVALUATION_STRATEGY.md

## Datum
2026-03-22

## Bezug
- [ADR-015](ADR-015-recognition-quality-benchmark-framework.md) — Benchmark Framework
- [ADR-016](ADR-016-real-world-testset-architecture.md) — Testset Architektur (Zones, Governance)
- [TESTSET_DESIGN.md](../TESTSET_DESIGN.md) — Dataset-Klassen, Pflichtfälle, Ordnerstruktur
- [GROUND_TRUTH_SCHEMA.md](../GROUND_TRUTH_SCHEMA.md) — Datenmodell (Full Spec)
- [EVALUATION_STRATEGY.md](../EVALUATION_STRATEGY.md) — Metriken, Quality Gates, Pipeline

---

## 1. Executive Verdict

### Warum ein realistisches Testset für RomCleanup unverzichtbar ist

RomCleanup trifft **destruktive Entscheidungen** über Dateien: verschieben, sortieren, deduplizieren, umbenennen. Eine falsche Konsolenzuordnung bedeutet nicht „Test rot" sondern **Datenverlust beim User**. Die gesamte Kette — von der Header-Erkennung über DAT-Abgleich bis zur Sort-Entscheidung — muss gegen ein Testset geprüft werden, das die reale Welt abbildet, nicht den Happy Path.

**Was RomCleanup von typischer Software unterscheidet:**
- 69 verschiedene Erkennungsziele (Konsolen), nicht 5-10 Klassen
- 8 kaskadierende Erkennungsmethoden (Folder → Extension → Header → Serial → DAT) mit Konfliktauflösung
- Erkennungsfehler sind oft **leise** — kein Crash, sondern falsch sortierte Datei
- Die gefährlichste Fehlerklasse (falsch + sicher → Sorting → Datenverlust) ist statistisch selten, aber katastrophal
- Synthetische Testdaten (Stubs) können nur die Parser testen, nicht die Erkennungsqualität unter Realwelt-Varianz

### Hauptproblem schlechter Testsets

| Anti-Pattern | Symptom | Konsequenz im Produkt |
|-------------|---------|----------------------|
| Nur saubere Referenzfälle | 98 % Accuracy im Test | 85 % auf echten Sammlungen; 15 % stille Fehler |
| Keine Chaos-Daten | Parser funktioniert perfekt | Wildsammlungen aus dem Internet werden falsch sortiert |
| Keine Negativ-Kontrollen | .exe wird als ROM erkannt | User verliert System-Dateien |
| Kein Anti-Overfitting | Detection auf Testset optimiert | Performance auf neuen Daten deutlich schlechter |
| Keine Cross-System-Tests | PS1/PS2 einzeln korrekt | PS1-ROM wird als PS2 sortiert bei Serial-Overlap |

### Kurzfazit

> **Das Testset-System muss drei Eigenschaften gleichzeitig erfüllen:**
> 1. **Repräsentativ** — bildet die Verteilung und Probleme realer Sammlungen ab
> 2. **Deterministisch** — liefert reproduzierbare, maschinenauswertbare Ergebnisse
> 3. **Schützend** — verhindert Regressions-Blindheit, Benchmark-Overfitting und Perfekt-Bias

### Ist-/Soll-Bewertung

| Dimension | Ist-Zustand | Bewertung | Handlungsbedarf |
|-----------|-------------|-----------|-----------------|
| Grundarchitektur (Klassen, Schema, Pipeline) | Solide, durchdacht, implementiert | ✅ Gut | Pflege, kein Umbau |
| Dataset-Größe (1.170 von 1.530 Ziel) | 77 % des Ziels | ⚠️ Knapp | +360 Entries gezielt |
| Chaos-Quote (328/1170 = 28 %) | Knapp unter 30 % Pflicht | ⚠️ Grenzwertig | chaos-mixed + edge-cases erweitern |
| System-Coverage (65/65 = 100 %) | Alle Systeme aus consoles.json abgedeckt | ✅ Erreicht | Tiefe in T2-T4 erhöhen |
| Schema-Implementierung vs. Design | ground-truth.schema.json deutlich einfacher als GROUND_TRUTH_SCHEMA.md | ⚠️ Divergenz | Schrittweise angleichen |
| Anti-Overfitting (Holdout) | Nicht implementiert | ❌ Fehlt | ADR-016 Option umsetzen |
| Performance-Scale | 0 Entries | ❌ Leer | ScaleDatasetGenerator aktivieren |
| Stub-Realismus (L2/L3) | Nicht implementiert | ⚠️ Geplant | Priorisierung klären |
| Directory-based Games | Nicht abgedeckt | ❌ Fehlt | Wii U, 3DS CIA, DOS |
| Multi-File-Sets (CUE+BIN, GDI) | Nur Metadaten, keine echten Set-Stubs | ⚠️ Schwach | Generatoren erweitern |

---

## 2. Ziele des Testset-Systems

### Was das Testset leisten muss

| # | Ziel | Begründung | Metrik |
|---|------|------------|--------|
| Z1 | **Erkennungsqualität messen** | Precision, Recall, F1 pro System, pro Kategorie, pro Schwierigkeit | M1–M3 pro System |
| Z2 | **Regressionserkennung** | Jeder Build automatisch gegen Ground Truth + Baseline geprüft | M15, Regression Report |
| Z3 | **Schwächen sichtbar machen** | Confusion Matrices zeigen systematische Verwechslungen | Console×Console CSV |
| Z4 | **Fortschritte nachweisen** | Trend-Vergleich zwischen Builds, nicht nur Pass/Fail | trend-comparison.json |
| Z5 | **Freigabeentscheidungen stützen** | Sorting/Repair nur freigeben, wenn messbare Schwellen erreicht | M4, M6, M7, M9a Gates |
| Z6 | **Praxisnähe garantieren** | Chaos-Fälle, Edge-Cases und Negativ-Kontrollen als Pflicht | Chaos-Quote ≥ 30 % |
| Z7 | **Overfitting verhindern** | Detection darf nicht gegen Benchmark-Samples optimiert werden | Holdout-Zone |
| Z8 | **Reproduzierbar sein** | Gleiche Ground Truth + gleicher Code = gleiche Ergebnisse | Deterministic Ordering |
| Z9 | **Skalierbar sein** | Von 1.170 auf 3.000+ ohne Strukturbruch | SemVer, Schema-Migration |
| Z10 | **Wartbar bleiben** | Formaler Prozess verhindert Chaos und Ground-Truth-Drift | Governance-Regeln |

### Was es ausdrücklich NICHT sein darf

| Anti-Ziel | Begründung |
|-----------|------------|
| Kein Demo-Fixture-Set | 20 handverlesene Dateien testen nichts Reales |
| Kein Repository für echte ROMs | Copyright; nicht committbar |
| Kein statischer Snapshot | Muss wachsen, aber kontrolliert |
| Kein Ablageordner | Jeder Testfall braucht Review und Ground Truth |
| Kein Overfitting-Target | Benchmark-Set darf nicht Optimierungsgrundlage werden |
| Kein Alibi-Testset | Tests, die nie einen echten Fehler finden, sind wertlos |

---

## 3. Dataset-Klassen

### Übersicht

| Klasse | Zone | Zweck | Ist | Ziel | Testart | Laufzeit |
|--------|------|-------|-----|------|---------|----------|
| `golden-core` | Dev | Deterministische Referenz-Fixtures | 370 | 400 | Unit, Integration, CI | <10s |
| `golden-realworld` | Eval | End-to-End unter realistischen Bedingungen | 250 | 350 | Integration, Benchmark, Regression | <60s |
| `chaos-mixed` | Eval | Robustheit bei chaotischen Inputs | 101 | 200 | Integration, Benchmark | <45s |
| `edge-cases` | Eval | Gezielte Grenzfälle und Verwechslungspaare | 142 | 200 | Unit, Integration, Regression | <20s |
| `negative-controls` | Eval | Korrekte Zurückweisung von Nicht-ROMs | 62 | 80 | Unit, Integration | <10s |
| `repair-safety` | Eval | Confidence-Gating, Sort-/Repair-Sicherheit | 65 | 100 | Integration, Benchmark | <15s |
| `dat-coverage` | Eval | DAT-Matching-Qualität isoliert | 180 | 200 | Integration, Benchmark | <20s |
| `performance-scale` | Perf | Skalierung und Throughput (generiert) | 0 | 5.000+ | Performance, Nightly | <5min |
| `holdout-blind` | Holdout | Anti-Overfitting-Kontrolle | 0 | 200 | Release-Gate, Nightly | <30s |
| **Gesamt (manuell)** | | | **1.170** | **1.530** | | |

### 3.1 `golden-core` — Deterministische Referenz-Fixtures

| Dimension | Wert |
|-----------|------|
| **Zweck** | Schnelle, deterministische Tests für jede Erkennungsmethode. Basis-Vertrauen, dass Parser funktionieren. |
| **Zone** | Dev — Entwickler dürfen Detection gegen diese Samples entwickeln |
| **Zielgröße** | 300–400 Samples |
| **Testart** | Unit, Integration, CI (jeder Build) |
| **Laufzeit** | < 10 Sekunden |

**Inhalte (Pflicht-Unterklassen):**

| Unterklasse | Min. | Beschreibung | Erkennung |
|-------------|------|-------------|-----------|
| Cartridge-Header-Stubs | 30 | NES (iNES), N64 (3 Endian), GBA, GB, GBC (CGB 0x00/0x80/0xC0), SNES (LoROM/HiROM), MD, 32X, Lynx, 7800 | CartridgeHeader |
| Disc-Header-Stubs | 30 | PS1/PS2/PSP (PVD+Boot-Marker), GC/Wii (Magic), SAT/DC/SCD (IP.BIN), 3DO (Opera FS) | DiscHeader |
| Unique-Extension-Samples | 40 | Je 1 pro uniqueExt — .nes, .sfc, .z64, .gba, .gb, .gbc, .smd, .pce, .a26, etc. | UniqueExtension |
| Folder-Name-Samples | 30 | Je 1-2 pro FolderAlias-Cluster (NES/famicom, Genesis/megadrive, etc.) | FolderName |
| BIOS-Referenz | 15 | Korrekt getaggte BIOS-Dateien diverser Systeme | FileClassifier |
| Junk-Tags | 20 | Demo, Beta, Proto, Hack, Homebrew, Pirate, Bootleg, [b], [h], [t] | FileClassifier |
| Serial/Keyword-Filename | 20 | SLUS-xxxxx, BCUS-xxxxx, NTR-XXXX-XXX, [PS1], [GBA] | SerialNumber, Keyword |
| DAT-Hash-Stubs | 15 | Dateien mit bekannten SHA1-Hashes aus Test-DATs | DatLookup |
| Archive-Stubs | 15 | ZIP/7z mit korrekter/falscher/fehlender Innendatei | ArchiveContent |
| Negative Controls | 15 | .txt, .jpg, .exe, .pdf, 0-Byte, Random-Bytes | — (muss UNKNOWN bleiben) |

**Welche Kernfunktionen getestet werden:**
- `CartridgeHeaderDetector`, `DiscHeaderDetector` — Header-Parsing
- `ConsoleDetector.DetectByExtension`, `DetectByFolder` — Fallback-Methoden
- `FileClassifier.Classify` — Kategorie-Erkennung
- `FilenameConsoleAnalyzer.DetectBySerial`, `DetectByKeyword` — Namensanalyse
- `DatIndex.Lookup` — Hash-Matching
- `HypothesisResolver.Resolve` — Multi-Source-Aggregation

**Risiken / Biases:**
- ⚠️ **Perfekt-Bias:** Nur saubere, korrekt strukturierte Dateien → kein Chaos-Stress. **Mitigation:** Diese Klasse prüft nur Parser-Korrektheit; Chaos wird in anderen Klassen getestet.
- ⚠️ **Header-Dominanz:** Header-detectable Systeme überrepräsentiert → bewusst, weil dort die höchsten Qualitätsziele gelten.
- ⚠️ **Minimale Stubs (L1):** Nur Header-Bytes ohne realistisches Padding → tauglich für Parser, nicht für Robustheit.

---

### 3.2 `golden-realworld` — Realistisches Referenz-Set

| Dimension | Wert |
|-----------|------|
| **Zweck** | Benchmarking der End-to-End-Erkennung unter realistischen Bedingungen |
| **Zone** | Eval — Read-Only für Detection-Tuning |
| **Zielgröße** | 350–800 Samples |
| **Testart** | Integration, Benchmark, Regression (Nightly / Release) |
| **Laufzeit** | < 60 Sekunden |

**Inhalte (Pflicht-Unterklassen):**

| Unterklasse | Min. | Beschreibung |
|-------------|------|-------------|
| No-Intro-Benennungskonvention | 100 | Korrekte No-Intro-Dateinamen (synthetische Stubs) |
| Redump-Benennungskonvention | 50 | Korrekte Redump-Dateinamen für Disc-Systeme |
| Folder-sortierte Sammlung | 50 | Dateien in benannten System-Ordnern |
| Gemischte Sammlung (flach) | 40 | Alle ROMs in einem Ordner, keine Folder-Hints |
| Multi-Disc-Sets | 30 | CUE+BIN, GDI+Track, M3U+CHD, CCD+IMG+SUB |
| Regions-Varianten | 30 | Gleicher Titel in EU/US/JP/WORLD |
| Revisions | 20 | (Rev A), (Rev B), (v1.0), (v1.1) |
| BIOS im Kontext | 20 | BIOS neben Spielen im gleichen System-Ordner |
| Arcade-Realworld | 40 | Parent/Clone-Sets, Split/Merged/Non-Merged, Shared BIOS |
| Computer-Realworld | 30 | DOS, Amiga, C64, ZX, MSX — typische Sammlungsstrukturen |

**Konsolen-Verteilung (Pflicht-Minimum pro Tier):**

| Tier | Systeme | Min. pro System |
|------|---------|----------------|
| T1 | NES, SNES, N64, GBA, GB, GBC, MD, PS1, PS2 | 20 |
| T2 | PSP, SAT, DC, GC, WII, 32X, SMS, GG, PCE, LYNX, A78, A26, NDS, 3DS, SWITCH, AMIGA, ARCADE | 10 |
| T3 | Alle mit UniqueExtension | 5 |
| T4 | Alle verbleibenden | 3 |

**Risiken / Biases:**
- ⚠️ **Tier-1-Dominanz:** NES/SNES/PS1 machen ~60 % aus → korrekt, bildet echte Sammlungen ab.
- ⚠️ **Alle Namen sauber:** Chaos in separater Klasse → hier nur Referenz-Qualität.
- ⚠️ **Stub-Limitation:** Synthetische Stubs fangen keine reale Header-Padding-Varianz → L2-Stubs nötig.

---

### 3.3 `chaos-mixed` — Robustheit unter Realbedingungen

| Dimension | Wert |
|-----------|------|
| **Zweck** | Prüfung der Robustheit bei chaotischen, fehlerhaften und irreführenden Inputs |
| **Zone** | Eval |
| **Zielgröße** | 200–500 Samples |
| **Testart** | Integration, Benchmark, Regression |
| **Laufzeit** | < 45 Sekunden |

**Inhalte (Pflicht-Unterklassen):**

| Unterklasse | Min. | Beschreibung | Konkretes Beispiel |
|-------------|------|-------------|-------------------|
| Falsch benannt | 30 | ROM mit falschem Systemnamen | `Super Mario Bros (SNES).nes` (ist NES) |
| Gekürzte Namen | 20 | Abgekürzte Dateinamen | `FF7_D1.iso`, `Pkmn_R.gba` |
| Falsche Extension | 20 | Korrekte ROM, falsche Endung | NES-ROM als `.bin`, GBA als `.rom` |
| Unicode-Namen | 15 | JP/KR/AR, Akzente, Emoji | `ファイナルファンタジー.iso` |
| Sonderzeichen | 10 | Klammern, Quotes, Ampersand | `Tom & Jerry (v1.0) [!].nes` |
| Inkonsistente Tags | 15 | Gemischte No-Intro + Redump + Custom Tags | `Mario (U) [!] (Rev A) [hM04].nes` |
| Ohne Ordner-Kontext | 20 | Alle in flachem Ordner | Kein FolderName-Detection |
| Gemischte Archive | 10 | ZIP mit ROMs verschiedener Systeme | `.nes` + `.gba` + `.smd` im ZIP |
| Kaputte Archive | 10 | Truncated ZIP, 0-Byte-ZIP, passwortgeschützt | Download-Abbrüche |
| Doppelte Dateien | 10 | Gleicher Inhalt, verschiedene Namen | `Mario.nes` + `Super Mario Bros.nes` |
| Headerless ROMs | 10 | NES ohne iNES, SNES ohne Header | Nur Extension als Hinweis |
| Teilweise beschädigt | 10 | Header korrekt, Datei aber nach 1 KB abgeschnitten | Truncated |
| ROMs in falschen Ordnern | 10 | NES-ROM in `SNES/` | Folder↔Header-Konflikt |

**Zentrale Prüffunktionen:**
- `ConsoleDetector`: Fallback-Kette bei widersprüchlichen Signalen
- `HypothesisResolver`: Konflikt-Erkennung und -Bestrafung
- `FileClassifier`: Robustheit gegen ungewöhnliche Namen
- Confidence-Kalibrierung: Stimmt Confidence mit tatsächlicher Korrektheit überein?

**Risiken / Biases:**
- ⚠️ **UNKNOWN ist oft die korrekte Antwort** → Ground Truth muss das abbilden (expected: `null`, sortDecision: `block`)
- ⚠️ **Konstruiert?** Quellen für realistische Chaos-Daten: Goodtools-Sets, Internet-Archiv-Downloads, Community-Shares.
- ⚠️ **Separate Auswertung nötig:** Chaos-Metriken dürfen nicht mit Referenz-Metriken gemischt werden.

---

### 3.4 `edge-cases` — Gezielte Grenzfälle

| Dimension | Wert |
|-----------|------|
| **Zweck** | Prüfung bekannter Verwechslungspaare und ambiger Situationen |
| **Zone** | Eval |
| **Zielgröße** | 150–250 Samples |
| **Testart** | Unit, Integration, Regression |
| **Laufzeit** | < 20 Sekunden |

**Inhalte (Pflicht-Unterklassen):**

| Unterklasse | Min. | Konkretes Beispiel |
|-------------|------|-------------------|
| PS1 ↔ PS2 Serial-Ambiguität | 15 | SLUS-00123 (PS1?) vs SLUS-12345 (PS2?) |
| PS2 ↔ PSP PVD-Ambiguität | 10 | Beide "PLAYSTATION" im PVD, Boot-Marker unterscheiden |
| GB ↔ GBC CGB-Flag | 10 | CGB-Flag 0x80 = Dual-Mode → GBC oder GB? |
| Genesis ↔ 32X | 8 | Beide "SEGA" @ 0x100 |
| Saturn ↔ Dreamcast | 6 | IP.BIN-Struktur ähnlich |
| Cross-System-Namenskollision | 20 | Tetris auf NES/GB/SMD/GBA — gleicher Name, anderes System |
| BIOS-ähnliche Spielnamen | 10 | `PlayStation BIOS (v3.0).bin` vs `BioShock.iso` |
| Multi-Disc-Zuordnung | 15 | Disc 1-4, korrekte Set-Zuordnung |
| Headerless mit Extension | 10 | `.nes` ohne iNES → nur Extension gibt Hinweis |
| DAT-Kollision Cross-System | 8 | Gleicher SHA1 in PS1-DAT und PS2-DAT |
| Folder sagt X, Header sagt Y | 10 | NES-ROM in `SNES/`-Ordner |
| SNES Bimodal | 6 | LoROM/HiROM/Copier-Header-Varianten |
| Region-Ambiguität | 8 | `(USA, Europe)` vs `(World)` |
| Ohne DAT vs. mit DAT | 10 | Gleiche Datei: mit/ohne geladenen DAT |
| Neo Geo AES ↔ MVS ↔ NEOCD | 6 | Gleiche IPs, verschiedene Plattformen |

**Risiken / Biases:**
- ⚠️ **Nur bekannte Probleme:** Neue Verwechslungen werden nicht abgedeckt → Jeder Bug-Report mit Verwechslung wird als neuer Edge-Case aufgenommen.
- ⚠️ **`acceptableAlternatives` essenziell:** GB/GBC Dual-Mode hat zwei korrekte Antworten → Schema muss das abbilden.

---

### 3.5 `negative-controls` — Was NICHT erkannt werden darf

| Dimension | Wert |
|-----------|------|
| **Zweck** | Sicherstellung, dass Nicht-ROM-Dateien, Junk und irreführende Inputs korrekt zurückgewiesen werden |
| **Zone** | Eval |
| **Zielgröße** | 80–150 Samples |
| **Testart** | Unit, Integration, Regression |
| **Laufzeit** | < 10 Sekunden |

**Inhalte:**

| Unterklasse | Min. | Erwartung |
|-------------|------|-----------|
| Nicht-ROM-Dateitypen | 20 | .txt, .jpg, .png, .pdf, .exe, .dll → UNKNOWN |
| Irreführende Dateinamen | 15 | `Nintendo 64 Game.exe`, `SNES Classic.pdf` → UNKNOWN |
| Junk/Demo/Beta/Hack | 20 | Demo-ROMs → Category=Junk, nicht Game |
| Leere/kaputte Dateien | 10 | 0 Bytes, nur Nullen → UNKNOWN |
| Dummy mit ROM-Extension | 10 | `.nes`-Datei die JPEG enthält → UNKNOWN oder Warnung |
| Homebrew/Hack | 10 | `(Hack by X)`, `(Homebrew)` → Category=Junk/NonGame |
| Fast-ROM-Fälle | 5 | Zufällige Bytes mit gültigem Header-Prefix → Korrekte Confidence-Abstufung |

**Kritische Invariante:** Kein einziges Negativ-Control darf als `sortDecision: "sort"` enden. 0 % False-Positive-Sorting.

---

### 3.6 `repair-safety` — Sicherheitsklassifikation

| Dimension | Wert |
|-----------|------|
| **Zweck** | Prüfung, ob Confidence und Match-Qualität korrekt Sort-/Repair-Freigabe steuern |
| **Zone** | Eval |
| **Zielgröße** | 100–150 Samples |
| **Testart** | Integration, Benchmark |
| **Laufzeit** | < 15 Sekunden |

**Inhalte:**

| Unterklasse | Min. | Erwartete Entscheidung |
|-------------|------|----------------------|
| DAT-Exact + High Confidence | 15 | Repair-Safe, Sort-Safe |
| DAT-Exact + Low Confidence | 10 | Sort-Safe (DAT-Trust), Repair-Risky |
| DAT-None + High Confidence | 15 | Sort-Safe, Repair-Blocked |
| DAT-None + Low Confidence | 10 | UNKNOWN → Block All |
| Conflict + DAT-Match | 10 | Sort-Safe (via DAT), Repair-Risky |
| Weak Match (LookupAny) | 10 | Review, Repair-Blocked |
| Ambiguous-Multi-DAT | 8 | Review Needed, Block Sort |
| Folder-Only-Detection | 8 | Sort-Risky, Repair-Blocked |
| Single-Source Low-Conf | 8 | UNKNOWN, Block All |

**Kernfunktion geprüft:**
- `RunOrchestrator.StandardPhaseSteps`: Confidence-Gating (≥80, ¬Conflict)
- Repair-Gate: DAT-Exact ∧ Confidence ≥ 95 ∧ ¬Conflict
- Sort/Block-Entscheidung pro Confidence-Level

---

### 3.7 `dat-coverage` — DAT-Matching-Qualität

| Dimension | Wert |
|-----------|------|
| **Zweck** | Isolierte Prüfung der DAT-Matching-Logik |
| **Zone** | Eval |
| **Zielgröße** | 150–200 Samples |
| **Testart** | Integration, Benchmark |
| **Laufzeit** | < 20 Sekunden |

**Inhalte:**

| Unterklasse | Min. | Beschreibung |
|-------------|------|-------------|
| No-Intro-Hash-Matches | 40 | SHA1-Hashes aus No-Intro-DATs |
| Redump-Hash-Matches | 30 | SHA1-Hashes aus Redump-DATs |
| Hash-Collision Cross-System | 15 | Hash in 2+ System-DATs |
| Hash-Miss (Homebrew) | 20 | Dateien in keinem DAT |
| Hash-Miss (Undumped) | 15 | Bekannte Spiele, kein Dump im DAT |
| Container-vs-Content-Hash | 15 | ZIP-Hash vs. innerer ROM-Hash |
| CHD-Embedded-SHA1 | 10 | CHD v5 raw SHA1 → muss matchen |
| TOSEC-Einträge | 10 | Computer-System DAT-Abdeckung |
| DAT-Format-Varianten | 8 | CLR-MAE, No-Intro XML, Redump XML |
| MAME-DAT-Entries | 15 | CRC32 pro ROM-Chip, Arcade |

---

### 3.8 `performance-scale` — Skalierung (generiert)

| Dimension | Wert |
|-----------|------|
| **Zweck** | Performance- und Skalierungsmessung; NICHT für Erkennungsqualität |
| **Zone** | Performance |
| **Zielgröße** | 5.000–20.000 Samples (generiert) |
| **Testart** | Performance, Nightly Benchmark |
| **Laufzeit** | < 5 Minuten |

**Parameter für `ScaleDatasetGenerator`:**

| Parameter | Wert |
|-----------|------|
| Systeme | 20+ nach realer Häufigkeitsverteilung |
| Dateien pro System | 100–500 |
| Extension-Mix | 70 % unique, 20 % ambiguous, 10 % headerless |
| Folder-Struktur | 50 % sortiert, 30 % flach, 20 % falsch |
| Archive | 10 % ZIP, 5 % 7z |
| BIOS-Quote | 2 % |
| Junk-Quote | 5 % |
| Duplikate | 8 % |

**Ground Truth:** Automatisch generiert → nur für Timing, nicht für Qualitätsauswertung.

---

### 3.9 `holdout-blind` — Anti-Overfitting (NEU)

| Dimension | Wert |
|-----------|------|
| **Zweck** | Erkennt, wenn Detection gegen das Eval-Set overfitted wird |
| **Zone** | Holdout — NICHT im Haupt-Repo committed |
| **Zielgröße** | ~200 Samples |
| **Testart** | Release-Gate, Nightly |
| **Laufzeit** | < 30 Sekunden |

**Mechanismus:**
1. Holdout-Entries in separatem privatem Repo (oder verschlüsseltem Blob)
2. CI clont/entschlüsselt zur Evaluationszeit
3. Eval-Metriken steigen, Holdout stagniert/sinkt → **Overfitting-Signal**
4. Schwelle: |ΔEval − ΔHoldout| > 5pp → Hard-Warn

**Implementierung (empfohlen: Option A aus ADR-016):**
- Separates privates Repository
- CI-Pipeline clont als Submodul
- Entwickler haben keinen direkten Zugriff

---

## 4. Pflichtfälle

### 4.1 Saubere Referenzfälle (golden-core + golden-realworld)

| # | Falltyp | Min. | Begründung |
|---|---------|------|------------|
| 1 | NES iNES-Header + `.nes` | 5 | Häufigstes Cartridge-System |
| 2 | SNES LoROM + HiROM | 4 | Bimodale Header-Erkennung |
| 3 | N64 alle 3 Endian-Varianten | 3 | BE, Byte-Swap, LE |
| 4 | GBA mit Nintendo-Logo-Signatur | 3 | Offset 0x04 |
| 5 | GB vs GBC (CGB-Flag 0x00/0x80/0xC0) | 4 | Ambiguitäts-Handling |
| 6 | Genesis vs 32X (Header @0x100) | 4 | Verwechslungspaar |
| 7 | PS1/PS2/PSP via ISO9660-PVD | 9 | 3 Systeme × 3 Varianten |
| 8 | GC/Wii Magic-Bytes | 4 | Eindeutige Signaturen |
| 9 | Saturn/DC/SCD via IP.BIN | 6 | Regex-basierte Erkennung |
| 10 | BIOS korrekt getaggt | 12 | für ≥12 Systeme |
| 11 | Multi-Disc CUE+BIN | 5 | Set-Integrität |
| 12 | DAT-Exact-Hash-Match | 10 | SHA1-Verifikation |
| 13 | Folder-Detection alle Major-Aliases | 15 | FolderName→Console |
| 14 | Unique Extension je System | 40 | Basis-Erkennung |
| 15 | Alle 4 DAT-Ökosysteme | 12 | No-Intro, Redump, MAME, TOSEC |

### 4.2 Chaos-Fälle (chaos-mixed)

| # | Falltyp | Min. | Begründung |
|---|---------|------|------------|
| 1 | ROM mit falschem Systemnamen im Dateinamen | 10 | Häufig in Wildsammlungen |
| 2 | ROM mit falscher Dateiendung | 10 | `.bin` für alles |
| 3 | Gekürzte/verstümmelte Namen | 10 | Goodtools-Sets, alte Downloads |
| 4 | Unicode-Dateinamen (JP/KR) | 10 | Japanische ROM-Sammlungen |
| 5 | Gemischt sortierte Ordner | 10 | Alles in einem Ordner |
| 6 | Kaputte/Truncated Archives | 5 | Download-Abbrüche |
| 7 | ROMs in falschen System-Ordnern | 5 | User-Fehler |
| 8 | Doppelte Dateien verschiedener Namen | 5 | Gemischte Sammlungen |

### 4.3 Schwierige Fälle (edge-cases)

| # | Falltyp | Min. | Begründung |
|---|---------|------|------------|
| 1 | Cross-System-Namenskollision | 10 | Tetris auf 6 Systemen |
| 2 | PS1 ↔ PS2 Serial-Overlap | 8 | SLUS-Serials 3-5 Digits |
| 3 | GB ↔ GBC Dual-Mode (CGB=0x80) | 5 | Konventionsfrage |
| 4 | Genesis ↔ 32X Header-Ähnlichkeit | 5 | "SEGA" im Header |
| 5 | BIOS vs Spiel mit ähnlichem Namen | 8 | False Classification |
| 6 | Headerless ROMs mit korrekter Extension | 5 | Fallback testen |
| 7 | Folder sagt X, Header sagt Y | 5 | Widersprüchliche Signale |
| 8 | DAT-Match-Kollision Cross-System | 5 | Hash in 2 System-DATs |
| 9 | Neo Geo AES ↔ MVS ↔ CD | 6 | Gleiche IPs, verschiedene Plattformen |
| 10 | Arcade Parent/Clone-Hierarchie | 10 | Split/Merged/Non-Merged |

### 4.4 Negative Kontrollen (negative-controls)

| # | Falltyp | Min. | Erwartung |
|---|---------|------|-----------|
| 1 | .txt, .jpg, .pdf, .exe | 15 | UNKNOWN, kein System |
| 2 | ROM-Name, kein ROM-Inhalt | 10 | UNKNOWN |
| 3 | Demo/Beta/Proto/Hack | 15 | Category = Junk |
| 4 | 0-Byte-Dateien | 5 | UNKNOWN |
| 5 | Random-Bytes mit ROM-Extension | 5 | UNKNOWN oder Extension-only mit niedriger Confidence |

### 4.5 Safety-Fälle (repair-safety)

| # | Falltyp | Min. | Erwartung |
|---|---------|------|-----------|
| 1 | DAT-Exact + Confidence ≥ 95 | 10 | Repair-Safe, Sort-Safe |
| 2 | Confidence < 80 | 10 | Sort-Blocked |
| 3 | HasConflict = true | 10 | Sort-Blocked |
| 4 | DAT-Match via LookupAny | 5 | Review Needed |
| 5 | Folder-Only-Detection | 5 | Not Repair-Safe |
| 6 | Category ≠ Game | 10 | Sort-Blocked |

---

## 5. Ground-Truth-Modell

### 5.1 Format: JSONL (eine Zeile pro Sample)

**Warum JSONL:**
- Jede Zeile ist ein unabhängiges JSON-Objekt → ein korrupter Eintrag killt nicht den Rest
- Git-Diff zeigt exakt geänderte/hinzugefügte Testfälle
- Maschinenlesbar (`System.Text.Json`, `jq`, PowerShell)
- Menschenpflegbar (jede Zeile im Editor validierbar)
- Schema-Validierung via JSON-Schema pro Zeile (`ground-truth.schema.json`)
- Filterung: `jq`, `grep`, PowerShell One-Liner

### 5.2 Schema: Ist-Zustand vs. Soll-Zustand

**Aktuell implementiert (`ground-truth.schema.json`):**

| Feld | Typ | Pflicht | Status |
|------|-----|---------|--------|
| `id` | string (Pattern) | Ja | ✅ |
| `source.fileName` | string | Ja | ✅ |
| `source.extension` | string | Ja | ✅ |
| `source.sizeBytes` | integer | Ja | ✅ |
| `source.directory` | string? | Nein | ✅ |
| `source.stub` | object? | Nein | ✅ (generator, variant, params) |
| `source.innerFiles` | array? | Nein | ✅ |
| `tags` | string[] (Enum 68 Werte) | Ja | ✅ |
| `difficulty` | enum | Ja | ✅ |
| `expected.consoleKey` | string? | Ja | ✅ |
| `expected.category` | enum | Ja | ✅ |
| `expected.confidence` | int? | Nein | ✅ |
| `expected.hasConflict` | bool | Nein | ✅ |
| `expected.datMatchLevel` | enum? | Nein | ✅ |
| `expected.datEcosystem` | enum? | Nein | ✅ |
| `expected.sortDecision` | enum? | Nein | ✅ |
| `detectionExpectations` | object | Nein | ✅ (primaryMethod, acceptableAlternatives, acceptableConsoleKeys) |
| `fileModel` | object | Nein | ✅ (type, setFiles, discCount) |
| `relationships` | object | Nein | ✅ (cloneOf, biosSystemKeys, parentSet) |
| `notes` | string | Nein | ✅ |

**Im Design-Dokument vorgesehen, aber NICHT implementiert:**

| Feld | Zweck | Priorität | Empfehlung |
|------|-------|-----------|------------|
| `schemaVersion` | SemVer des Schemas | P1 | Beim nächsten Schema-Update hinzufügen |
| `set` | Dataset-Klasse explizit | P2 | Redundant (wird aus Dateiname abgeleitet) — optional |
| `subclass` | Feinere Unterklasse | P2 | Durch Tags abgedeckt — nicht zwingend |
| `platformClass` | Plattformklasse | P2 | `PlatformFamilyClassifier` leitet ab — nicht zwingend |
| `source.origin` | stub/donated/synthetic | P2 | Nice-to-have |
| `expected.gameIdentity` | Spielname | P1 | Für DAT-Matching-Assertions nötig |
| `expected.region` | Region | P2 | Für Region-Scoring-Tests relevant |
| `expected.version` | Version/Revision | P2 | Für Version-Scoring-Tests |
| `expected.discNumber/discSetSize` | Multi-Disc | P1 | Für Set-Integrität essenziell |
| `expected.repairSafe` | Repair-Freigabe | P1 | Vorbereitung für Repair-Gate |
| `verdicts` (A–G) | Erwartete Evaluations-Ebenen | P2 | Für tiefere Evaluation nötig |
| `acceptableAlternatives` (strukturiert) | Ambiguität | P1 | Aktuell nur über `acceptableConsoleKeys` |
| `addedInVersion` | Testset-Version | P1 | Tracking |
| `lastVerified` | Verifikationsdatum | P1 | Anti-Drift |

**Empfehlung:** Schrittweise erweitern. Nächste Iteration sollte `schemaVersion`, `expected.gameIdentity`, `expected.discNumber`, `expected.repairSafe`, `addedInVersion`, `lastVerified` hinzufügen. Alles als optional, um bestehende Entries nicht zu brechen.

### 5.3 Ambiguitäts-Modellierung

Für ambige Fälle (GB/GBC Dual-Mode, Neo Geo AES/MVS):

```jsonl
{
  "id": "ec-GBC-dual-001",
  "expected": { "consoleKey": "GBC", "category": "Game" },
  "detectionExpectations": {
    "primaryMethod": "CartridgeHeader",
    "acceptableConsoleKeys": ["GB"]
  },
  "notes": "CGB=0x80 Dual-Mode; GB und GBC sind beide korrekt"
}
```

**Evaluator-Logik (implementiert in `GroundTruthComparator`):**
1. `actual == expected.consoleKey` → **CORRECT**
2. `actual ∈ acceptableConsoleKeys` → **ACCEPTABLE** (zählt als CORRECT)
3. `actual == null` → **MISSED**
4. Sonst → **WRONG**

### 5.4 Sonderfälle und Kommentare

Das `notes`-Feld dokumentiert, warum ein Testfall so erwartet wird:

```jsonl
{
  "id": "ec-NES-folder-conflict-001",
  "source": { "fileName": "ActuallyNES.nes", "directory": "snes" },
  "expected": { "consoleKey": "NES", "hasConflict": true },
  "notes": "NES-ROM in SNES-Ordner. Header (iNES @ 0x00) hat Vorrang. Conflict-Flag MUSS gesetzt sein."
}
```

---

## 6. Ordner- und Dateistruktur

### 6.1 Aktuelle Struktur (validiert)

```
benchmark/
├── README.md                           ← Überblick, Pflegregeln
├── manifest.json                       ← Version, Statistiken
├── gates.json                          ← Coverage & Quality Thresholds
│
├── ground-truth/
│   ├── ground-truth.schema.json        ← JSON-Schema
│   ├── golden-core.jsonl               ← 370 Entries
│   ├── golden-realworld.jsonl          ← 250 Entries
│   ├── chaos-mixed.jsonl               ← 101 Entries
│   ├── edge-cases.jsonl                ← 142 Entries
│   ├── negative-controls.jsonl         ← 62 Entries
│   ├── repair-safety.jsonl             ← 65 Entries
│   ├── dat-coverage.jsonl              ← 180 Entries
│   └── performance-scale.jsonl         ← 0 Entries (Metadaten)
│
├── samples/                            ← Generierte Stubs (65 System-Ordner)
│   ├── .generated                      ← Marker für Lazy Generation
│   ├── nes/                            ← NES-Stubs
│   ├── snes/                           ← SNES-Stubs
│   ├── ps1/                            ← PS1-Stubs (ISO mit PVD)
│   ├── ...                             ← (65 Systemordner + roms/, unsorted/, wrong_folder/)
│   └── wrong_folder/                   ← Folder-Konflikt-Samples
│
├── dats/                               ← Test-DAT-Dateien (Mini-Subsets)
│   ├── test-nointro-nes.xml
│   └── ...
│
├── baselines/
│   ├── latest-baseline.json            ← Aktuelle Referenz
│   └── v0.1.0-baseline.json            ← Versionierte Baseline
│
├── reports/                            ← Generierte Artefakte (gitignored)
│   ├── benchmark-results.json
│   ├── metrics-summary.json
│   ├── confusion-console.csv
│   ├── confusion-category.csv
│   ├── error-details.jsonl
│   └── trend-comparison.json
│
├── generated/                          ← Performance-Scale-Daten (gitignored)
│
├── tools/                              ← Build/CI-Hilfsmittel
└── docs/                               ← Benchmark-spezifische Docs
```

### 6.2 Fehlende / Empfohlene Ergänzungen

| Ergänzung | Begründung | Priorität |
|-----------|------------|-----------|
| `baselines/archive/` | Historische Baselines archivieren (ADR-016 §7) | P1 |
| `holdout/` (oder externes Repo) | Anti-Overfitting-Zone | P2 |
| `samples/multi-file/` | CUE+BIN, GDI+Track-Sets | P1 |
| `samples/directory-games/` | DOS/WiiU Directory-Struktur | P2 |

### 6.3 Warum Stub-Generation statt statische Dateien?

1. **Copyright:** Echte ROMs unzulässig. Stubs mit korrekten Headers, aber ohne lauffähigen Inhalt, sind copyright-sicher.
2. **Reproduzierbarkeit:** `StubGeneratorDispatch.GenerateAll()` erzeugt alles deterministisch aus Ground Truth.
3. **Wartbarkeit:** Ground-Truth-JSONL + Generatoren pflegen statt 1.500 Binärdateien.

**Ablauf:**
```
ground-truth/*.jsonl → StubGeneratorDispatch → samples/**/*
```

Generator wird:
- Beim ersten Test-Run als Teil von `BenchmarkFixture.InitializeAsync()` ausgeführt
- Lazy: nur wenn `.generated`-Marker fehlt
- Deterministic: PRNG mit fester Seed für Padding-Bytes

---

## 7. Versionierung und Pflegeprozess

### 7.1 Versionierung

| Element | Strategie |
|---------|-----------|
| Ground-Truth-JSONL | Git-tracked, jede Änderung = Commit mit Reason |
| `manifest.json` | `version` (SemVer), `totalEntries`, `lastModified` |
| Baselines | Git-tracked unter `baselines/`, benannt nach Version |
| Samples | Generiert, NICHT committed; Generator ist committed |
| Reports | Generiert, NICHT committed; CI-Artefakt |
| `ground-truth.schema.json` | Git-tracked, SemVer in `$id` |

**SemVer-Regeln:**
- **MAJOR:** Schema-Strukturänderung (neues Pflichtfeld, umbenanntes Feld)
- **MINOR:** Neue Samples hinzugefügt
- **PATCH:** Korrektur bestehender Ground-Truth-Werte

### 7.2 Aufnahme neuer Samples

```
1. Bug oder Coverage-Lücke identifiziert
2. Ground-Truth-Eintrag als JSONL-Zeile formulieren
   → id, source, tags, difficulty, expected, detectionExpectations
   → stub-Info (generator, variant, params) definieren
3. Generator-Regel prüfen (existiert Generator für diesen Typ?)
4. Lokal ausführen: StubGenerator → Sample → Benchmark-Run
5. Ergebnis prüfen:
   → Stimmt actual mit expected überein? → Ground Truth validiert
   → Test soll scheitern? → Red-Test (documenting desired behavior)
6. Pull Request mit:
   → Geänderte JSONL-Datei
   → Ggf. neuer Generator-Code
   → Begründung
7. Review: ≥1 Reviewer prüft Ground-Truth-Korrektheit
8. Merge → manifest.json Version-Bump (MINOR)
```

### 7.3 Änderung bestehender Ground Truth

| Aktion | Erlaubt? | Bedingung |
|--------|----------|-----------|
| Neues Sample hinzufügen | Ja | PR + 1 Reviewer + Schema-Validierung |
| `expected.consoleKey` ändern | Eingeschränkt | PR + 2 Reviewer + Quellennachweis |
| Sample löschen | Eingeschränkt | PR + 2 Reviewer + Begründung |
| `acceptableAlternatives` erweitern | Ja | PR + 1 Reviewer |
| `notes` aktualisieren | Ja | Kein Review nötig |
| Neues Pflichtfeld zum Schema | MAJOR Version | ADR-Amendment + Migration Script |

### 7.4 Anti-Drift-Maßnahmen

CI prüft bei jedem Build:
1. ✅ Alle JSONL-Einträge sind schema-valide (implementiert in `CoverageGateTests`)
2. ✅ Alle IDs sind global eindeutig (implementiert in `GroundTruthLoader`)
3. ⚠️ Alle referenzierten Stubs können generiert werden (teilweise — BenchmarkFixture)
4. ❌ Keine veralteten `lastVerified`-Daten (nicht implementiert — Feld fehlt noch)

### 7.5 Historische Vergleichbarkeit

```
Regel: Baselines werden NIE überschrieben.

Workflow:
1. Neuer Release → Benchmark-Run → Ergebnis als baselines/v{X}.json
2. baselines/latest-baseline.json wird aktualisiert
3. CI vergleicht jeden PR gegen latest-baseline.json
4. Trend-Reports vergleichen über alle gespeicherten Baselines

Bei Ground-Truth-Änderung:
→ Baselines NICHT retroaktiv angepasst
→ manifest.json enthält groundTruthVersion
→ Vergleiche nur innerhalb gleicher MAJOR gültig
→ Bei MAJOR-Bump: neue Baseline-Serie
```

---

## 8. Nutzung im Projekt

### 8.1 Einbindung nach Testart

| Testart | Dataset-Klasse | Trigger | Erwartung | Implementiert? |
|---------|---------------|---------|-----------|----------------|
| **Unit Tests** | golden-core (Subset) | Jeder Build | 100 % Pass, <5s | ✅ `GoldenCoreBenchmarkTests` |
| **Integration** | golden-core + edge-cases | Jeder Build | 100 % Pass, <30s | ✅ Mehrere Testklassen |
| **Regression** | Alle 7 aktiven Sets | Jeder PR | Keine Regression vs. Baseline | ✅ `BaselineRegressionGateTests` |
| **Quality Gate** | Alle 7 aktiven Sets | Jeder PR | M4/M6/M7/M9a ≤ Schwellen | ✅ `QualityGateTests` |
| **Coverage Gate** | Ground-Truth-Struktur | Jeder Build | Alle strukturellen Mindest-Coverage-Schwellen | ✅ `CoverageGateTests` |
| **Benchmark** | Alle Klassen | Nightly / Release | Metriken-Report + Trend | ✅ Pipeline vorhanden |
| **Performance** | performance-scale | Nightly | Throughput-Regression <10 % | ⚠️ Dataset leer |
| **Manuelle QA** | chaos-mixed + repair-safety | Vor Release | Review der UNKNOWN/WRONG-Fälle | Manuell |
| **Bug-Reproduktion** | Neuer Testfall in passender Klasse | Bei Bug-Report | Red→Green-Nachweis | ✅ Prozess dokumentiert |
| **Golden-File** | Snapshot der Benchmark-Outputs | Jeder PR | Keine unbeabsichtigte Änderung | ⚠️ Nicht formalisiert |
| **Holdout** | holdout-blind | Release-Gate, Nightly | Overfitting-Erkennung | ❌ Nicht implementiert |

### 8.2 CI/CD-Integration (Soll)

```
CI Pipeline (pro PR):

1. [Build]      dotnet build
2. [Generate]   BenchmarkFixture erzeugt Stubs (lazy, <5s)
3. [Unit+Int]   dotnet test --filter "Category=Benchmark"
                → golden-core + edge-cases + alle Sets, <60s
4. [Coverage]   CoverageGateTests: Strukturelle Mindest-Coverage
                → FAIL = Datensatz zu klein für belastbare Messung
5. [Quality]    QualityGateTests: M4 ≤0.5%, M6 ≤5%, M7 ≤0.3%, M9a ≤0.1%
                → Informational (ROMCLEANUP_ENFORCE_QUALITY_GATES=true → Hard Fail)
6. [Regression] BaselineRegressionGateTests: vs latest-baseline.json
                → FAIL wenn: Wrong Match Rate ↑ >0.1pp, Unsafe Sort Rate ↑ >0.1pp
                → WARN wenn: M15 UNKNOWN→WRONG >2%
7. [Artifacts]  Upload: benchmark-results.json, metrics-summary.json,
                confusion-*.csv, error-details.jsonl als CI-Artefakte

Nightly:

8. [Full]       Alle Tests + Performance-Scale
9. [Holdout]    holdout-blind Evaluation (wenn verfügbar)
10. [HTML]      HTML-Benchmark-Report generieren
11. [Trend]     Vergleich gegen alle Baselines
```

### 8.3 xUnit-Integration (implementiert)

Die Benchmark-Tests nutzen `IClassFixture<BenchmarkFixture>` für Shared State:

```csharp
[Collection("BenchmarkEvaluation")]
public sealed class GoldenCoreBenchmarkTests : IClassFixture<BenchmarkFixture>
{
    [Fact]
    [Trait("Category", "Benchmark")]
    public void GoldenCore_AllEntries_MatchGroundTruth()
    {
        var results = BenchmarkEvaluationRunner.EvaluateSet(fixture, "golden-core.jsonl");
        // Assert per-entry verdicts
    }
}
```

### 8.4 Bug-Reproduktion (Workflow)

```
Bei Bug "PS2-ROM wird als PS1 erkannt":

1. Neuen Ground-Truth-Eintrag erstellen:
   → edge-cases.jsonl, id: ec-PS2-serial-disambig-NNN
   → expected.consoleKey: "PS2", difficulty: "hard"
   → notes: "Bug #42: SLUS-20001 wurde als PS1 erkannt"

2. Stub erzeugen (Generator: ps2-pvd mit spezifischem Serial)

3. Test lokal → muss FAIL (Red Phase)

4. Fix implementieren

5. Test lokal → muss PASS (Green Phase)

6. PR mit: JSONL-Eintrag + Fix

Testfall bleibt dauerhaft → Regression verhindert.
```

---

## 9. Risiken und Anti-Patterns

### 9.1 Identifizierte Risiken

| # | Anti-Pattern | Symptom | Aktueller Status | Gegenmaßnahme |
|---|-------------|---------|-------------------|----------------|
| R1 | **Perfekt-Bias** | >70 % saubere Referenzfälle | 53 % in golden-core+golden-realworld (620/1170) | Pflicht-Chaos-Quote: ≥30 % in chaos/edge/negative/repair |
| R2 | **Happy-Path-Mono** | Nur Header-Systeme getestet | 65 Systeme abgedeckt, aber Computer/Arcade unterrepräsentiert | Tier-Quoten in gates.json enforced |
| R3 | **Plattform-Mono** | >50 % NES+SNES | Tier-1 bei ~55 % | Max 60 % Tier-1 in golden-realworld |
| R4 | **Benchmark-Overfitting** | Detection gegen bekannte Stubs getuned | Kein Holdout | Holdout-Zone (ADR-016 §1) |
| R5 | **Tote Samples** | Stubs mit veralteten Header-Formaten | Kein lastVerified-Feld | Schema erweitern + jährliche Revalidierung |
| R6 | **Ground-Truth-Drift** | Expected-Werte nach Codeänderung falsch | Kein formaler Drift-Detection | CI: Schema-Validierung + Regression-Gate |
| R7 | **Unkontrolliertes Wachstum** | Samples ohne Review | Governance nicht CI-enforced | PR-Review-Pflicht + CI-Gate |
| R8 | **Alibi-Negatives** | Nur .txt/.jpg als Negative | 62 Entries, aber wenige "Fast-ROM"-Fälle | Random-Bytes mit ROM-Extension aufnehmen |
| R9 | **Schema-Divergenz** | Implementierung weicht vom Design ab | ground-truth.schema.json ≠ GROUND_TRUTH_SCHEMA.md | Schrittweise Schema-Erweiterung |
| R10 | **Performance-Blindheit** | Keine Skalierungstests | performance-scale = 0 | ScaleDatasetGenerator aktivieren |
| R11 | **Stub-Unrealismus** | Minimale Stubs ≠ echte ROM-Varianz | Nur L1-Stubs | L2/L3 Realism-Level einführen (ADR-016 §3) |
| R12 | **DAT-Lückenhaftigkeit** | TOSEC/MAME-DATs unterrepräsentiert | ~0 TOSEC, ~15 MAME | Gezielt erweitern |

### 9.2 Quantitative Bias-Analyse (Ist-Zustand)

```
Schwierigkeitsverteilung (Ist → Soll):
├── easy:        ~60 %  → 40 %  ⚠️ zu viele einfache Fälle
├── medium:      ~25 %  → 30 %
├── hard:        ~12 %  → 20 %  ⚠️ zu wenige
└── adversarial: ~3 %   → 10 %  ⚠️ deutlich zu wenige

Chaos-Quote (Ist → Soll):
├── chaos-mixed + edge-cases + negative-controls + repair-safety: 370 / 1170 = 31.6 %
└── Soll: ≥ 30 %  ✅ Knapp erreicht, aber fragil

Platform-Verteilung:
├── Cartridge:  ~55 %  ✅ OK
├── Disc:       ~25 %  ✅ OK
├── Arcade:     ~8 %   ⚠️ Unter 13 % Ziel
├── Computer:   ~7 %   ⚠️ Unter 10 % Ziel
└── Hybrid:     ~5 %   ✅ OK
```

### 9.3 Datensatz-Audit (jährlich empfohlen)

```
Prüfpunkte:
□ Anteil perfect vs chaos vs edge vs negative → Zielverhältnis 40:25:20:15
□ Konsolen-Abdeckung → alle Tiers mit Min-Samples?
□ Keine Samples ohne Ground Truth?
□ Keine Stubs mit veralteten Header-Formaten?
□ Performance-Scale-Generator aktuell?
□ Holdout-Zone aktuell?
□ Neue Verwechslungspaare aus Bug-Reports aufgenommen?
□ Schema-Version aktuell?
□ Bias-Metriken innerhalb der Toleranz?
```

---

## 10. Konkrete nächste Schritte

### Phase A: Sofortige Lücken schließen (Größtes Delta zuerst)

| # | Schritt | Delta | Output | Abhängigkeit |
|---|---------|-------|--------|-------------|
| A1 | **chaos-mixed auf 200 erweitern** (+99) | 101 → 200 | +99 JSONL-Entries mit Falsch-Benannt, Unicode, Headerless, Wrong-Folder | Stub-Generatoren vorhanden |
| A2 | **golden-realworld auf 350 erweitern** (+100) | 250 → 350 | +100 Entries: Arcade-Depth, Computer-Systeme, Folder-sortiert | Neue Entries für ARCADE, DOS, AMIGA, C64 |
| A3 | **edge-cases auf 200 erweitern** (+58) | 142 → 200 | +58 Entries: PS-Disambiguation, Cross-System, BIOS-Edge | PVD-Generator vorhanden |
| A4 | **negative-controls auf 80 erweitern** (+18) | 62 → 80 | +18 Entries: Fast-ROM-Fälle, Homebrew, irreführende Namen | RandomBytesGenerator vorhanden |
| A5 | **repair-safety auf 100 erweitern** (+35) | 65 → 100 | +35 Entries: Disc-Repair, Multi-DAT, Folder-Only | Confidence-Gating-Tests definieren |

**Ergebnis Phase A:** 1.170 → 1.480 Entries (97 % des 1.530-Ziels)

### Phase B: Infrastruktur-Härtung

| # | Schritt | Output | Abhängigkeit |
|---|---------|--------|-------------|
| B1 | **Schema um P1-Felder erweitern** | `schemaVersion`, `expected.gameIdentity`, `expected.discNumber`, `expected.repairSafe`, `addedInVersion`, `lastVerified` (alle optional) | Schema-Migration-Script |
| B2 | **Performance-Scale Generator aktivieren** | 5.000–10.000 generierte Entries | `ScaleDatasetGenerator` existiert, muss verdrahtet werden |
| B3 | **Difficulty-Bias korrigieren** | Adversarial von 3 % auf 10 %, Hard von 12 % auf 20 % | Neue hard/adversarial Entries in chaos-mixed und edge-cases |
| B4 | **Multi-File-Set Stub-Generator** | CUE+BIN, GDI+Track, M3U+CHD Generator | Neuer `MultiFileSetGenerator` |
| B5 | **Arcade-Depth: Parent/Clone/BIOS/Split-Merged** | ≥200 Arcade-Entries | Arcade-spezifischer Generator oder MAME-DAT-Extraktor |

### Phase C: Anti-Overfitting und Reife

| # | Schritt | Output | Abhängigkeit |
|---|---------|--------|-------------|
| C1 | **Holdout-Zone implementieren** (ADR-016 Option A) | Separates privates Repo mit 200 Entries | CI-Pipeline-Erweiterung |
| C2 | **Stub-Realismus L2/L3 einführen** | `StubRealismLevel`-Parameter in Generatoren | Generator-Refactoring |
| C3 | **Directory-based Game Samples** | DOS, Wii U, 3DS CIA Verzeichnis-Stubs | Neuer `DirectoryGameGenerator` |
| C4 | **Confidence Calibration Test (M16)** | Bucket-basierte Kalibrierung | MetricsAggregator erweitern |
| C5 | **TOSEC DAT-Abdeckung** | ≥10 TOSEC-Entries mit Test-DATs | TOSEC-DAT-Subset-Extraktor |
| C6 | **Datensatz auf 2.000+ erweitern** | Alle Lücken schließen | Phase A + B Ergebnisse |

### Phase D: Langfrist-Qualität

| # | Schritt | Output |
|---|---------|--------|
| D1 | **HTML Benchmark Dashboard** mit Pro-System-Metriken | Menschenlesbarer Report |
| D2 | **Jährlicher Datensatz-Audit-Prozess** | Formalisierter Review-Zyklus |
| D3 | **Repair-Freigabe-Gate (M14)** als Feature-Flag | Repair-Feature-Readiness |
| D4 | **Trend-Dashboard** über alle historischen Baselines | Langzeit-Qualitätsverlauf |
| D5 | **Cross-Validation Split** | Train/Test-Split für ML-basierte Erkennung (Zukunft) |

### Prioritätsreihenfolge der ersten 10 Aktionen

```
A1  chaos-mixed +99          ← Größtes Dataset-Delta, lowest hanging fruit
A2  golden-realworld +100    ← Arcade/Computer-Tiefe
A3  edge-cases +58           ← Cross-System und BIOS-Edge
B1  Schema P1-Felder         ← Unlocks gameIdentity, discNumber, repairSafe
A4  negative-controls +18    ← Fast-ROM und Homebrew
A5  repair-safety +35        ← Sort-Confidence-Gating
B2  performance-scale        ← Performance-Regression endlich messbar
B3  Difficulty-Bias          ← Adversarial-Quote anheben
B4  Multi-File-Set Generator ← CUE+BIN, GDI, M3U
B5  Arcade-Depth             ← MAME-Nutzer abdecken
```

---

## Entscheidungen (zusammengefasst)

| # | Entscheidung | Rationale |
|---|-------------|-----------|
| E1 | **JSONL bleibt das Format** | Zeilenweiser Git-Diff, unabhängige Zeilen, breites Tooling |
| E2 | **8+1 Dataset-Klassen** (8 aktive + holdout) | Klare Verantwortlichkeit, separate Auswertung |
| E3 | **Drei-Zonen-Architektur** (Dev/Eval/Holdout) | Anti-Overfitting, klare Nutzungsregeln |
| E4 | **Stub-Generation > statische Dateien** | Copyright, Reproduzierbarkeit, Wartbarkeit |
| E5 | **Schema-Evolution: optional-first** | Bestehende Entries nicht brechen |
| E6 | **Baselines NIE überschreiben** | Historische Vergleichbarkeit |
| E7 | **Chaos-Quote ≥ 30 %** | Schutz gegen Perfekt-Bias |
| E8 | **Ground-Truth-Governance: PR + Review** | Qualitätssicherung des Testsets selbst |
| E9 | **Coverage-Gates CI-enforced** | Verhindert stille Dataset-Erosion |
| E10 | **Bug-Reports → Edge-Case-Entries** | Dauerhafter Regressionsschutz |

---

## Betroffene Dateien

### Bestehend (validiert)
- `benchmark/ground-truth/*.jsonl` — Ground Truth (8 Dateien)
- `benchmark/ground-truth/ground-truth.schema.json` — Schema
- `benchmark/gates.json` — Coverage-Schwellen
- `benchmark/manifest.json` — Metadaten
- `src/RomCleanup.Tests/Benchmark/**/*.cs` — 63 Testdateien

### Zu erstellen/erweitern
- `benchmark/ground-truth/holdout-blind.jsonl` (oder externes Repo)
- `src/RomCleanup.Tests/Benchmark/Generators/MultiFileSetGenerator.cs`
- `src/RomCleanup.Tests/Benchmark/Generators/DirectoryGameGenerator.cs`
- Schema-Erweiterung: `ground-truth.schema.json` v2.0

---

## Anhang A: Matrik Dataset-Klasse × Kernfunktion

| Kernfunktion | golden-core | golden-realworld | chaos-mixed | edge-cases | negative-controls | repair-safety | dat-coverage |
|-------------|:-----------:|:----------------:|:-----------:|:----------:|:-----------------:|:-------------:|:------------:|
| Console Detection | ✅ | ✅ | ✅ | ✅ | ✅ | ○ | ○ |
| Category Classification | ✅ | ✅ | ✅ | ○ | ✅ | ○ | ○ |
| DAT-Matching | ○ | ✅ | ○ | ✅ | ○ | ✅ | ✅ |
| Sorting Decision | ○ | ✅ | ✅ | ✅ | ✅ | ✅ | ○ |
| Repair Safety | ○ | ○ | ○ | ○ | ○ | ✅ | ✅ |
| Confidence Calibration | ○ | ✅ | ✅ | ✅ | ○ | ✅ | ○ |
| Header Parsing | ✅ | ✅ | ○ | ✅ | ○ | ○ | ○ |
| Folder/Extension/Keyword | ✅ | ✅ | ✅ | ✅ | ✅ | ○ | ○ |
| Conflict Resolution | ○ | ○ | ✅ | ✅ | ○ | ✅ | ○ |
| Archive Handling | ✅ | ○ | ✅ | ✅ | ○ | ○ | ✅ |

✅ = Primäres Testfeld | ○ = Sekundär/Nicht abgedeckt

## Anhang B: Bekannte Verwechslungspaare (Pflicht-Edge-Cases)

| Paar | Ursache | Min. Testfälle | Aktuell |
|------|---------|---------------|---------|
| PS1 ↔ PS2 | Serial-Overlap (SLUS Digit-Count) | 15 | ~12 |
| PS2 ↔ PSP | PVD "PLAYSTATION", Boot-Marker-Differenz | 10 | ~8 |
| GB ↔ GBC | CGB-Flag 0x80 Dual-Mode | 8 | ~6 |
| Genesis ↔ 32X | "SEGA" @ 0x100 | 6 | ~4 |
| Saturn ↔ DC | IP.BIN-Struktur | 6 | ~4 |
| SNES ↔ SFC | Gleiche Hardware, Region-Tag | 4 | ~3 |
| NES (headered) ↔ NES (headerless) | iNES optional | 4 | ~3 |
| Neo Geo (AES/MVS/CD) | Plattform-Flag + DAT | 8 | ~5 |
| Arcade ↔ Neo Geo | Genre-/Set-Overlap | 6 | ~3 |
| PCE ↔ PCECD | HuCard vs CD | 4 | ~2 |

## Anhang C: Metrik-zu-Dataset Zuordnung

| Metrik | Primäres Dataset | Sekundär |
|--------|-----------------|----------|
| M1-M3 (Precision/Recall/F1) | golden-realworld | chaos-mixed |
| M4 (Wrong Match Rate) | Alle | — |
| M5 (Unknown Rate) | Alle | — |
| M6 (False Confidence) | edge-cases, chaos-mixed | — |
| M7 (Unsafe Sort) | repair-safety, golden-realworld | — |
| M8 (Safe Sort Coverage) | golden-realworld | — |
| M9 (Category Confusion) | golden-core, negative-controls | — |
| M9a (Game-as-Junk) | golden-core, negative-controls | chaos-mixed |
| M10 (Console Confusion) | edge-cases | — |
| M11 (DAT Exact Match) | dat-coverage | — |
| M12 (DAT Weak Match) | dat-coverage | — |
| M13 (Ambiguous Match) | edge-cases, chaos-mixed | — |
| M14 (Repair-Safe Match) | repair-safety | — |
| M15 (UNKNOWN→WRONG Migration) | Alle (Trend-Vergleich) | — |
| M16 (Confidence Calibration) | Alle (Bucket-Analyse) | — |
