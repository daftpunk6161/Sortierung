# Real-World Testset System – Architektonisches Gesamtkonzept

> **Version:** 1.0.0  
> **Status:** Proposed  
> **Datum:** 2026-03-21  
> **ADR-Referenz:** [ADR-016](architecture/ADR-016-real-world-testset-architecture.md)  
> **Voraussetzung:** [ADR-015](architecture/ADR-015-recognition-quality-benchmark-framework.md), [TESTSET_DESIGN.md](TESTSET_DESIGN.md), [GROUND_TRUTH_SCHEMA.md](GROUND_TRUTH_SCHEMA.md)

---

## 1. Executive Verdict

### Warum ein realistisches Testset für dieses Tool unverzichtbar ist

RomCleanup trifft **destruktive, nicht-trivial-umkehrbare Entscheidungen**: Dateien werden verschoben, sortiert, dedupliziert, konvertiert und potenziell umbenannt. Die Erkennungspipeline durchläuft 8 Stufen (FolderName → UniqueExt → AmbiguousExt → DiscHeader → ArchiveContent → CartridgeHeader → SerialNumber → FilenameKeyword), und jede falsche Entscheidung auf einer dieser Stufen kann zu Datenverlust führen.

**Das aktuelle Problem:** Das Projekt hat 5.200+ Unit-Tests (136 Dateien) und 2.073 Ground-Truth-Einträge – aber:

| Lücke | Auswirkung |
|-------|------------|
| Synthetische Stubs testen nur exakte Header-Pfade | 100 % Pass auf Stubs, potenziell 85 % auf echten Sammlungen |
| Kein Holdout-Set gegen Overfitting | Detection wird unbewusst gegen bekannte Testdaten optimiert |
| performance-scale ist leer (0 Entries) | Keine Performance-Regression-Erkennung |
| PC/Computer-Systeme unterrepräsentiert | ~15 % der realen Sammlungen blind |
| Keine formale Governance für Ground-Truth-Änderungen | Stille Ground-Truth-Drift nach Codeänderungen |
| Kein Stub-Realismus jenseits minimaler Header | Lücke zwischen Unit-Test-Welt und Real-World-Verhalten |

### Hauptproblem schlechter Testsets

Ein Testset, das überwiegend saubere, korrekt benannte Referenz-ROMs mit minimalen Header-Stubs enthält, misst den Parser-Happy-Path, nicht die reale Erkennungsqualität. Echte ROM-Sammlungen sind chaotisch: falsch benannte Dateien, fehlende Header, gemischte Ordner, Unicode-Namen, kaputte Archive, BIOS-Dateien neben Spielen, Homebrew neben kommerziellen ROMs, Dateien in falschen Ordnern, Archive mit gemischtem Inhalt.

### Kurzfazit

Das Testset-System muss fünf Eigenschaften gleichzeitig erfüllen:

1. **Repräsentativ** – bildet die Verteilung und Probleme realer Sammlungen ab (nicht den Happy Path)
2. **Deterministisch** – liefert reproduzierbare, maschinenauswertbare Ergebnisse bei jedem Build
3. **Schützend** – verhindert Regressions-Blindheit und Benchmark-Overfitting
4. **Skalierbar** – ist bereits von 1.152 auf 2.073 Entries skaliert; Zielrichtung 3.000+ Entries
5. **Honest** – macht Schwächen sichtbar statt sie zu verschleiern

---

## 2. Ziele des Testset-Systems

### Was das Testset leisten muss

| Ziel | Messbar durch |
|------|---------------|
| Erkennungsqualität pro System messen | Precision, Recall, F1 pro ConsoleKey, pro Dataset-Klasse |
| Regressionserkennung | Jeder Build gegen Baseline; CI-Gate bei Verschlechterung |
| Schwächen sichtbar machen | Confusion Matrix: welche Systeme werden verwechselt? |
| Fortschritt nachweisen | Trend-Vergleich zwischen Baselines (nicht nur Pass/Fail) |
| Overfitting erkennen | Holdout-Zone: Eval vs. Holdout-Divergenz |
| Freigabeentscheidungen stützen | Sorting/Repair nur bei gemessenen Schwellenwerten (ADR-015 M1-M16) |
| Praxisnähe garantieren | Pflicht-Chaos-Quote ≥ 30 %; Stub-Realismus-Level L2/L3 |
| Performance-Regression erkennen | performance-scale mit Throughput-Messung |
| Bug-Reproduktion ermöglichen | Jeder Bug-Report wird zu dauerhaftem Testfall |

### Was es ausdrücklich NICHT sein darf

| Anti-Ziel | Warum |
|-----------|-------|
| Kein Demo-Fixture-Set | 20 handverlesene Dateien testen nichts Reales |
| Kein Repository für echte ROMs | Copyright-Verletzung; nicht committbar |
| Kein statischer Snapshot | Muss kontrolliert wachsen |
| Kein Selbstbedienungs-Ablageordner | Jeder Testfall braucht Review und Ground Truth |
| Kein Overfitting-Target | Benchmark-Set darf nicht zur Optimierungsgrundlage werden |
| Kein All-Clean-Set | Testset ohne Chaos und Edge-Cases ist wertlos |
| Kein Single-Platform-Set | Nur NES + SNES zu testen ist Augenwischerei |

---

## 3. Dataset-Klassen

### 3.1 `golden-core` – Deterministische Referenz-Fixtures (Dev Zone)

| Feld | Wert |
|------|------|
| **Zone** | Dev (Entwickler dürfen gegen diese Samples optimieren) |
| **Zweck** | Schnelle, deterministische Unit-/Integrationstests für jede Erkennungsmethode und jeden Detection-Pfad |
| **Zielgröße** | 300–400 Entries |
| **Stub-Realismus** | L1-minimal (nur Header-Bytes, minimale Dateigröße) |
| **Testart** | Unit, Integration, CI (jeder Build) |
| **Laufzeit-Budget** | < 10 Sekunden |
| **Aktueller Stand** | 370 Entries |

**Inhalte:**

| Unterklasse | Ziel | Beschreibung |
|-------------|------|-------------|
| Cartridge-Header-Stubs | 35 | Je 2-3 pro Header-System (NES iNES, N64 3 Endian-Varianten, GBA, GB/GBC CGB-Flag, SNES LoROM/HiROM, MD, 32X, Lynx, 7800) |
| Disc-Header-Stubs | 35 | PS1/PS2/PSP PVD, GC/Wii Magic, Saturn/DC/SCD IP.BIN, 3DO Opera, Xbox |
| Unique-Extension-Samples | 45 | Je 1 pro uniqueExt aus consoles.json |
| Folder-Name-Samples | 35 | Je 1-2 pro FolderAlias-Cluster |
| BIOS-Referenz | 20 | Korrekt getaggte BIOS-Dateien verschiedener Systeme |
| Junk-Tags | 20 | Demo, Beta, Proto, Hack, Homebrew, Pirate, Bootleg, [b], [h], [t], [p] |
| Keyword/Serial-Filename | 20 | SLUS-xxxxx, BCUS-xxxxx, NTR-XXXX, [PS1], [GBA] |
| DAT-Hash-Stubs | 20 | Dateien mit bekannten SHA1-Hashes aus Test-DATs |
| Archive-Stubs | 20 | ZIP/7z mit korrekter innerer Extension, falscher, korrupt |
| Negative Controls | 20 | .txt, .jpg, .exe, .pdf, leere Dateien, 0-Byte, Null-Bytes |
| Multi-Disc-Sets | 15 | CUE+BIN, GDI+Track, M3U+CHD, CCD+IMG |
| Arcade-Sets | 15 | MAME Parent, Clone, BIOS, Split/Merged/Non-Merged |

**Kernfunktionen unter Test:**
- CartridgeHeaderDetector, DiscHeaderDetector
- ConsoleDetector.DetectByExtension, DetectByFolder
- FileClassifier.Classify (Game/Bios/Junk/NonGame/Unknown)
- FilenameConsoleAnalyzer.DetectBySerial, DetectByKeyword
- DatIndex.Lookup
- HypothesisResolver.Resolve
- ArchiveHandler (ZIP/7z Entpackung und Content-Analysis)

**Risiken / Biases:**
- ⚠️ Zu sauber → deshalb nur Dev Zone, nicht für Qualitätsaussagen nutzbar
- ⚠️ L1-Stubs testen nur den exakten Parser-Pfad → L2/L3 in anderen Klassen
- ⚠️ Header-detectable Systeme überrepräsentiert → bewusst, weil höchste Qualitätsziele

---

### 3.2 `golden-realworld` – Realistisches Referenz-Set (Eval Zone)

| Feld | Wert |
|------|------|
| **Zone** | Eval (Read-Only für Detection-Tuning) |
| **Zweck** | Benchmarking der End-to-End-Erkennung unter realistischen Bedingungen |
| **Zielgröße** | 500–800 Entries |
| **Stub-Realismus** | L2-realistic (Header + realistisches Padding + korrekte Größenklasse) |
| **Testart** | Integration, Benchmark, Regression (Nightly / Release) |
| **Laufzeit-Budget** | < 60 Sekunden |
| **Aktueller Stand** | 232 Entries |

**Inhalte:**

| Unterklasse | Ziel | Beschreibung |
|-------------|------|-------------|
| No-Intro-Benennungskonvention | 150 | Korrekte No-Intro-Dateinamen mit L2-Stubs |
| Redump-Benennungskonvention | 80 | Korrekte Redump-Dateinamen für Disc-Systeme |
| Folder-sortierte Sammlung | 80 | Dateien in benannten System-Ordnern (typische sortierte Sammlung) |
| Gemischte Sammlung (flach) | 60 | Alle ROMs in einem Ordner, keine Folder-Hints |
| Multi-Disc-Sets | 40 | CUE+BIN (2-4 Tracks), GDI+Track, M3U+CHD, CCD+IMG+SUB, MDS+MDF |
| Regionsvarianten | 40 | Gleicher Titel in EU/US/JP/WORLD, korrekte Tags |
| Revisions | 30 | (Rev A), (Rev B), (v1.0), (v1.1), [!] verified |
| BIOS im Kontext | 20 | BIOS-Dateien neben Spiel-ROMs im gleichen Ordner |
| Arcade Real-World | 30 | Realistische MAME-Folder-Strukturen |
| Computer-Systeme | 30 | DOS/Amiga/C64/ZX/MSX mit realistischen Disk-Image-Größen |
| Konvertierte Formate | 20 | CHD v5, RVZ, CSO, WBFS → reale Ausgabeformate |

**Konsolen-Verteilung (verbindlich):**

| Tier | Systeme | Min. pro System | Begründung |
|------|---------|-----------------|------------|
| T1 | NES, SNES, N64, GBA, GB, GBC, MD, PS1, PS2 | 20 | Häufigste Systeme in echten Sammlungen |
| T2 | PSP, SAT, DC, GC, Wii, 32X, SMS, GG, PCE, LYNX, A78, A26, NDS, 3DS, SWITCH, AMIGA, ARCADE | 10 | Mittelgroße Sammlungen, diverse Detection |
| T3 | 2600, 5200, MSX, Coleco, Vectrex, NGP, WS, Jaguar, C64, ZX | 5 | Kleine Sammlungen, Extension-only Detection |
| T4 | 3DO, CD32, PCFX, JAGCD, NEOCD, FMTOWNS, CDI, SCD | 3 | Seltene Systeme, Vollständigkeit |

**Risiken / Biases:**
- ⚠️ Tier-1 dominiert → bewusst, weil dort 80 % der echten Sammlungen liegen
- ⚠️ L2-Stubs können nicht alle realen Varianten abbilden → Holdout-Zone mit gespendeten Stubs
- ⚠️ Synthetische DAT-Hashes ≠ echte DAT-Hashes → dat-coverage als isolierte Klasse

---

### 3.3 `chaos-mixed` – Robustheit unter Realbedingungen (Eval Zone)

| Feld | Wert |
|------|------|
| **Zone** | Eval |
| **Zweck** | Prüfung der Robustheit bei chaotischen, fehlerhaften und irreführenden Inputs |
| **Zielgröße** | 300–500 Entries |
| **Stub-Realismus** | L2/L3 gemischt |
| **Testart** | Integration, Benchmark, Regression |
| **Laufzeit-Budget** | < 45 Sekunden |
| **Aktueller Stand** | 101 Entries |

**Inhalte:**

| Unterklasse | Ziel | Beschreibung | Konkretes Beispiel |
|-------------|------|-------------|-------------------|
| Falsch benannt | 50 | ROM mit falschem Systemnamen im Dateinamen | `Super Mario Bros (SNES).nes` (ist NES) |
| Gekürzte Namen | 40 | Abgekürzte, verstümmelte Dateinamen | `FF7_D1.iso`, `Pkmn_R.gba` |
| Falsche Extension | 40 | Korrekte ROM, falsche Dateiendung | NES-ROM als `.bin`, GBA als `.rom` |
| Unicode-Namen | 30 | Japanisch, Koreanisch, Arabisch, Emoji | `ファイナルファンタジー.iso` |
| Sonderzeichen | 20 | Klammern, Quotes, Ampersand | `Tom & Jerry (v1.0) [!].nes` |
| Inkonsistente Tags | 30 | Gemischte No-Intro + Redump + Custom | `Mario (U) [!] (Rev A) [hM04].nes` |
| Ohne Ordner-Kontext | 40 | Alle Dateien in einem flachen Ordner | Kein Folder-Detection möglich |
| Gemischte Archive | 20 | ZIP mit ROMs verschiedener Systeme | ZIP mit `.nes` + `.gba` + `.smd` |
| Kaputte Archive | 20 | Truncated ZIP, 0-Byte-ZIP, Passwort-geschützt | ZIP mit nur Verzeichniseinträgen |
| Doppelte Dateien | 20 | Identische ROMs mit verschiedenen Namen | `Mario.nes` + `Super Mario Bros.nes` |
| Headerless ROMs | 15 | NES ohne iNES, SNES ohne Header | Extension als einziger Hinweis |
| Teilweise beschädigt | 15 | Abgeschnitten, Null-Padding | Header korrekt, Datei nach 1 KB abgeschnitten |

**Kernfunktionen unter Test:**
- ConsoleDetector: Fallback-Kette bei widersprüchlichen Signalen
- HypothesisResolver: Konflikt-Erkennung und -Bestrafung
- FileClassifier: Robustheit gegen ungewöhnliche Namensmuster
- Confidence-Kalibrierung: Stimmt Confidence mit tatsächlicher Korrektheit überein?

**Risiken / Biases:**
- ⚠️ „Zu konstruiert?" → Realistische Chaos-Quellen: Goodtools-Sets, Internet-Archiv-Downloads, Community-Shares
- ⚠️ UNKNOWN ist hier oft die korrekte Antwort → Ground Truth muss das abbilden (category=Unknown, sortingDecision=block)

---

### 3.4 `edge-cases` – Gezielte Grenzfälle (Eval Zone)

| Feld | Wert |
|------|------|
| **Zone** | Eval |
| **Zweck** | Prüfung bekannter Verwechslungspaare und ambiger Situationen |
| **Zielgröße** | 150–250 Entries |
| **Stub-Realismus** | L1/L2/L3 je nach Fall |
| **Testart** | Unit, Integration, Regression |
| **Laufzeit-Budget** | < 20 Sekunden |
| **Aktueller Stand** | 142 Entries |

**Inhalte:**

| Unterklasse | Ziel | Beschreibung |
|-------------|------|-------------|
| Cross-System-Namenskollision | 30 | `Tetris.gb`, `Tetris.nes`, `Tetris.smd` – alle korrekt |
| PS1 ↔ PS2 Serial-Ambiguität | 15 | SLUS-Serials mit 3-5 Digits |
| GB ↔ GBC CGB-Flag | 10 | CGB-Flag 0x00 (GB), 0x80 (Dual), 0xC0 (GBC-only) |
| Genesis ↔ 32X | 10 | Beide „SEGA" @ 0x100 |
| BIOS-ähnliche Spielnamen | 15 | `PlayStation BIOS (v3.0).bin` vs `BioShock.iso` |
| Multi-Disc-Zuordnung | 20 | Disc 1-4, korrekte Set-Zuordnung |
| Headerless mit Extension | 15 | ROM ohne Header, korrekte Extension |
| Archive mit gemischtem Inhalt | 10 | ZIP mit ROMs verschiedener Systeme |
| DAT-Kollision Cross-System | 10 | SHA1 in PS1-DAT und PS2-DAT |
| Ohne DAT vs. mit DAT | 15 | Gleiche Datei: mit/ohne geladenes DAT |
| SNES Bimodal | 10 | LoROM/HiROM/Copier-Header |
| Falsche Folder-Zuordnung | 10 | NES-ROM in SNES-Ordner |
| Region-Ambiguität | 10 | `(USA, Europe)` vs `(World)` |
| Arcade NEOGEO ↔ ARCADE | 10 | AES/MVS vs. MAME-Arcade |
| Directory-based Games | 10 | Wii U RPX, 3DS CIA, PC-Installationen |

**Kernfunktionen unter Test:**
- HypothesisResolver: Konflikt-Handling und Tie-Breaking
- ConsoleDetector: Multi-Source-Aggregation bei Widersprüchen
- DatIndex.LookupAny: Cross-System-Auflösung
- Confidence-Gating: Wann blockiert, wann sortiert?

**Risiken / Biases:**
- ⚠️ Fokussiert auf **bekannte** Probleme → neue Verwechslungen werden nicht abgedeckt
- ⚠️ Lösung: Jeder Bug-Report mit Verwechslung wird als neuer Edge-Case aufgenommen

---

### 3.5 `negative-controls` – Korrektes Ablehnen (Eval Zone)

| Feld | Wert |
|------|------|
| **Zone** | Eval |
| **Zweck** | Sicherstellung, dass Nicht-ROM-Dateien, Junk und irreführende Inputs korrekt abgelehnt werden |
| **Zielgröße** | 100–150 Entries |
| **Stub-Realismus** | L1/L3 (minimal oder adversarial) |
| **Testart** | Unit, Integration, Regression |
| **Laufzeit-Budget** | < 10 Sekunden |
| **Aktueller Stand** | 62 Entries |

**Inhalte:**

| Unterklasse | Ziel | Erwartung |
|-------------|------|-----------|
| Nicht-ROM-Dateitypen | 25 | `.txt`, `.jpg`, `.pdf`, `.exe`, `.dll`, `.mp3` → UNKNOWN |
| Irreführende Dateinamen | 20 | `Nintendo 64 Game.exe` → UNKNOWN |
| Junk / Demo / Beta | 20 | `Mario (Demo).nes` → Category: Junk |
| Leere / kaputte Dateien | 15 | 0-Byte, nur Nullen, Random → UNKNOWN |
| Dummy mit ROM-Extension | 15 | `.nes`-Datei die JPEG ist → UNKNOWN |
| Homebrew / Hack | 15 | `MyGame (Homebrew).nes` → NonGame/Junk |
| Fast-ROMs | 10 | Zufällige Bytes, aber Header zufällig korrekt → Low Confidence |
| Malicious Filenames | 10 | Path-Traversal-Versuche, extrem lange Namen → Sichere Ablehnung |

**Risiken / Biases:**
- ⚠️ „Zu offensichtlich?" → Ja, aber 100 % Pass ist Pflicht
- ⚠️ Fehlende „Fast-ROM"-Fälle (L3-Adversarial) sind kritisch → Random Bytes + passende Extension

---

### 3.6 `repair-safety` – Sicherheitsklassifikation (Eval Zone)

| Feld | Wert |
|------|------|
| **Zone** | Eval |
| **Zweck** | Prüfung, ob Confidence und Match-Qualität korrekt die Sort-/Repair-Sicherheit steuern |
| **Zielgröße** | 100–150 Entries |
| **Stub-Realismus** | L2-realistic |
| **Testart** | Integration, Benchmark |
| **Laufzeit-Budget** | < 15 Sekunden |
| **Aktueller Stand** | 65 Entries |

**Inhalte:**

| Unterklasse | Ziel | Expected Decision |
|-------------|------|-------------------|
| DAT-Exact + High Confidence | 20 | Repair-Safe, Sort-Safe |
| DAT-Exact + Low Confidence | 10 | Sort-Safe (DAT-Trust), Repair-Risky |
| DAT-None + High Confidence | 15 | Sort-Safe, Repair-Blocked |
| DAT-None + Low Confidence | 15 | UNKNOWN → Block All |
| Conflict + DAT-Match | 10 | Sort-Safe (via DAT), Repair-Risky |
| Weak Match (LookupAny) | 10 | Flag für Review, Repair-Blocked |
| Ambiguous-Multi-DAT | 10 | Review Needed, Block Sort |
| Folder-Only-Detection | 10 | Sort-Risky, Repair-Blocked |
| Single-Source Low-Conf | 10 | UNKNOWN, Block All |

**Kernfunktionen unter Test:**
- RunOrchestrator: Confidence-Gating (≥ 80, ¬Conflict)
- Repair-Tauglichkeit: DAT-Exact ∧ Confidence ≥ 95 ∧ ¬Conflict
- Sorting: Korrekte Block/Sort-Entscheidung pro Confidence-Level

---

### 3.7 `dat-coverage` – DAT-Matching-Qualität (Eval Zone)

| Feld | Wert |
|------|------|
| **Zone** | Eval |
| **Zweck** | Isolierte Prüfung der DAT-Matching-Logik |
| **Zielgröße** | 150–200 Entries |
| **Stub-Realismus** | L1 (nur Hashes relevant, nicht Dateinhalt) |
| **Testart** | Integration, Benchmark |
| **Laufzeit-Budget** | < 20 Sekunden |
| **Aktueller Stand** | 180 Entries |

**Inhalte:**

| Unterklasse | Ziel | Beschreibung |
|-------------|------|-------------|
| No-Intro-Hash-Matches | 40 | SHA1-Hashes aus No-Intro-DATs |
| Redump-Hash-Matches | 30 | SHA1-Hashes aus Redump-DATs |
| MAME-CRC-Matches | 20 | CRC32-Matches für Arcade-Sets |
| Hash-Collision Cross-System | 15 | Hash in 2+ System-DATs |
| Hash-Miss (Homebrew/Unlicensed) | 20 | Keine DAT-Entsprechung |
| Hash-Miss (Undumped) | 15 | Spiel existiert, aber kein Dump |
| Container-vs-Content-Hash | 15 | ZIP-Hash vs. innerer ROM-Hash |
| CHD-Embedded-SHA1 | 10 | CHD v5 raw SHA1 → muss matchen |
| DAT-Format-Varianten | 10 | CLR-MAE, No-Intro XML, Redump XML |
| TOSEC-Ecosystem | 10 | TOSEC-spezifische Namenskonvention |

---

### 3.8 `performance-scale` – Skalierung (Performance Zone)

| Feld | Wert |
|------|------|
| **Zone** | Performance (isoliert) |
| **Zweck** | Performance-Messung und Skalierungsverhalten |
| **Zielgröße** | 5.000–20.000 Entries (vollständig generiert) |
| **Stub-Realismus** | L1-minimal (Throughput, nicht Qualität) |
| **Testart** | Performance, Nightly |
| **Laufzeit-Budget** | < 5 Minuten |
| **Aktueller Stand** | 0 Entries |

**Generierungs-Parameter:**

| Parameter | Wert | Begründung |
|-----------|------|------------|
| Systeme | 20+ | Verteilung nach realer Häufigkeit |
| Dateien pro System | 100–500 | Mittelgroße bis große Sammlung |
| Extension-Mix | 70 % unique, 20 % ambiguous, 10 % headerless | Realistische Verteilung |
| Folder-Struktur | 50 % sortiert, 30 % flach, 20 % falsch | Mischt organisiert und chaotisch |
| Archive | 10 % ZIP, 5 % 7z | Realistischer Archiv-Anteil |
| BIOS-Quote | 2 % | Typischer Anteil |
| Junk-Quote | 5 % | Typischer Anteil |
| Duplikate | 8 % | Typische Duplikat-Rate |

**Messwerte:**
- Pipeline-Throughput (Dateien/Sekunde)
- Memory-Footprint bei Peak
- LruCache-Hit-Rate
- DAT-Index-Lookup-Performance

---

### 3.9 `holdout-blind` – Anti-Overfitting-Kontrolle (Holdout Zone)

| Feld | Wert |
|------|------|
| **Zone** | Holdout (NICHT im Repo committed) |
| **Zweck** | Unabhängige Validierung, ob Eval-Zone-Verbesserungen generalisieren |
| **Zielgröße** | ~200 Entries |
| **Stub-Realismus** | L2-realistic |
| **Testart** | Release-Gate, Nightly CI |
| **Laufzeit-Budget** | < 30 Sekunden |
| **Aktueller Stand** | Nicht existent (neu) |

**Zusammensetzung (repräsentativer Querschnitt):**

| Anteil | Entsprechung |
|--------|-------------|
| 40 % | golden-realworld-äquivalent (saubere Referenzen) |
| 25 % | chaos-mixed-äquivalent (chaotische Inputs) |
| 20 % | edge-cases-äquivalent (Verwechslungspaare) |
| 15 % | negative-controls/repair-safety-äquivalent |

**Overfitting-Detektion:**
```
IF Eval-Verbesserung > 3 % UND Holdout-Verbesserung < 0.5 %
THEN → Overfitting-Alert (CI Warning)

IF Eval-Verbesserung > 5 % UND Holdout-Verschlechterung
THEN → Overfitting-Block (CI Fail)
```

---

## 4. Pflichtfälle

### 4.1 Saubere Referenzfälle (golden-core + golden-realworld)

| # | Falltyp | Min. | Begründung |
|---|---------|------|------------|
| 1 | NES-ROM mit iNES-Header + `.nes` | 5 | Häufigstes Cartridge-System |
| 2 | SNES-ROM LoROM + HiROM | 4 | Bimodale Header-Erkennung |
| 3 | N64 alle 3 Endian-Varianten | 3 | BE, Byte-Swap, LE |
| 4 | GBA mit Nintendo-Logo-Signatur | 3 | Offset 0x04, sehr zuverlässig |
| 5 | GB vs GBC (CGB-Flag 0x00/0x80/0xC0) | 4 | Verwechslungspaartest |
| 6 | Genesis vs 32X (Header @0x100) | 4 | Verwechslungspaar |
| 7 | PS1/PS2/PSP via ISO9660-PVD | 6 | 3 Systemvarianten |
| 8 | GC/Wii Magic-Bytes | 4 | Spezifische Signaturen |
| 9 | Saturn/DC via IP.BIN-Text | 4 | Regex-basiert |
| 10 | BIOS korrekt getaggt | 15 | Verschiedene Systeme |
| 11 | Multi-Disc CUE+BIN | 5 | Set-Erkennung |
| 12 | DAT-Exact Hash-Match | 15 | SHA1-Verifikation |
| 13 | Folder-Detection Major-Aliases | 15 | FolderName→Console |
| 14 | Unique Extension je System | 45 | Grundlage-Erkennung |
| 15 | MAME Arcade Parent/Clone/BIOS | 10 | Größte Einzelgruppe |
| 16 | Computer-Systeme (ADF, D64, TZX, ATR) | 10 | Unterrepräsentiert |
| 17 | Serial-Number-Detection (SLUS, BCUS) | 8 | PS1/PS2/PS3/PSP |
| 18 | CHD v5 + RVZ + CSO + WBFS | 8 | Komprimierte Formate |

### 4.2 Chaos-Fälle (chaos-mixed)

| # | Falltyp | Min. | Begründung |
|---|---------|------|------------|
| 1 | ROM mit falschem Systemnamen im Dateinamen | 10 | Häufig in Wildsammlungen |
| 2 | ROM mit falscher Dateiendung | 10 | `.bin` für alles, `.rom` generisch |
| 3 | Gekürzte/verstümmelte Namen | 10 | Internet-Downloads, Goodtools-Sets |
| 4 | Unicode-Dateinamen (JP/KR/AR) | 10 | Japanische Sammlungen |
| 5 | Gemischt sortierte Ordner | 10 | Alles in einem Ordner |
| 6 | Kaputte/Truncated Archives | 5 | Download-Abbrüche |
| 7 | ROMs in falschen System-Ordnern | 5 | User-Fehler |
| 8 | Doppelte Dateien verschiedener Namen | 5 | Typisch in Mischsammlungen |
| 9 | Goodtools-Namensformat | 5 | `[!]`, `[a]`, `[b]`, `(U)` Legacy |
| 10 | Gemischte Systeme in einem Archiv | 5 | ZIP mit NES + GBA + SNES |

### 4.3 Schwierige Fälle (edge-cases)

| # | Falltyp | Min. | Begründung |
|---|---------|------|------------|
| 1 | Cross-System-Namenskollision | 10 | Tetris, Pac-Man, etc. |
| 2 | PS1 ↔ PS2 Serial-Overlap | 8 | SLUS-xxxxx Digits |
| 3 | GB ↔ GBC Dual-Mode | 5 | CGB=0x80 |
| 4 | Genesis ↔ 32X | 5 | Gleicher Header-Offset |
| 5 | BIOS vs Spiel mit ähnlichem Namen | 8 | PlayStation BIOS vs. BioShock |
| 6 | Headerless ROMs + korrekte Extension | 5 | Fallback auf Extension |
| 7 | Folder sagt X, Header sagt Y | 5 | Widersprüchliche Signale |
| 8 | DAT-Match-Kollision Cross-System | 5 | Hash in 2 DATs |
| 9 | NEOGEO AES/MVS vs. ARCADE/MAME | 5 | Plattformklassen-Ambiguität |
| 10 | Directory-based Games | 5 | Wii U RPX, DOS, PC |

### 4.4 Negative Kontrollfälle (negative-controls)

| # | Falltyp | Min. | Erwartung |
|---|---------|------|-----------|
| 1 | Nicht-ROM-Dateitypen (.txt, .jpg, .pdf) | 15 | UNKNOWN |
| 2 | ROM-Name, aber kein ROM-Inhalt | 10 | UNKNOWN |
| 3 | Demo / Beta / Proto / Hack | 15 | Junk |
| 4 | 0-Byte-Dateien | 5 | UNKNOWN |
| 5 | Random Bytes + ROM-Extension | 5 | UNKNOWN oder Low-Confidence |
| 6 | Fast-ROMs (zufällig korrekter Header) | 5 | Low Confidence, Sort-Block |
| 7 | Malicious Filenames (Path-Traversal) | 5 | Sichere Ablehnung |

### 4.5 Safety-Fälle (repair-safety)

| # | Falltyp | Min. | Erwartung |
|---|---------|------|-----------|
| 1 | DAT-Exact + Confidence ≥ 95 | 10 | Repair-Safe, Sort-Safe |
| 2 | Confidence < 80 | 10 | Sort-Blocked |
| 3 | HasConflict = true | 10 | Sort-Blocked |
| 4 | DAT-Match via LookupAny | 5 | Review Needed |
| 5 | Folder-Only-Detection | 5 | Not Repair-Safe |
| 6 | Category ≠ Game | 10 | Sort-Blocked |
| 7 | Ambiguous-Multi-DAT | 5 | Review Needed |

---

## 5. Ground-Truth-Modell

### 5.1 Formatempfehlung: JSONL

JSONL (JSON Lines, eine Zeile pro Entry) ist die einzig tragfähige Wahl:

| Eigenschaft | JSONL | JSON | YAML | CSV | SQLite |
|-------------|-------|------|------|-----|--------|
| Zeilenweiser Diff | ✓ | ✗ | ✗ | ✓ | ✗ |
| Merge-freundlich | ✓ | ✗ | ✗ | ✓ | ✗ |
| Geschachtelte Objekte | ✓ | ✓ | ✓ | ✗ | ✗ |
| Ein korrupter Eintrag killt Rest | ✗ | ✓ | ✓ | ✗ | ✗ |
| Maschinenlesbar (C#, jq, Python) | ✓ | ✓ | ✓ | ✓ | ✓ |
| Menschenpflegbar | ✓ | ✓ | ✓ | ✓ | ✗ |
| Schema-Validierung | ✓ (per Zeile) | ✓ | ✓ | ✗ | ✗ |

### 5.2 Schema-Kernfelder

Das vollständige Schema ist in [GROUND_TRUTH_SCHEMA.md](GROUND_TRUTH_SCHEMA.md) definiert. Hier die architektonisch relevanten Felder:

#### Identifikation
| Feld | Typ | Pflicht | Constraint |
|------|-----|---------|------------|
| `id` | string | Ja | `^[a-z0-9]+-[a-z0-9]+-[a-z0-9-]+-\d{3,}$` |
| `schemaVersion` | string | Ja | SemVer |
| `path` | string | Ja | POSIX, relativ zu `benchmark/samples/`, kein `..` |
| `set` | enum | Ja | 9 Dataset-Klassen |
| `platformClass` | enum | Ja | cartridge/disc/arcade/computer/hybrid |

#### Erwartete Ergebnisse
| Feld | Typ | Pflicht | Beschreibung |
|------|-----|---------|-------------|
| `expected.consoleKey` | string? | Ja | Einer der 69 Keys oder `null` (UNKNOWN) |
| `expected.category` | enum | Ja | Game/Bios/Junk/NonGame/Unknown |
| `expected.confidenceClass` | enum | Ja | high (≥80) / medium (50-79) / low (<50) / any |
| `expected.detectionConflict` | bool | Ja | Erwartet Conflict? |
| `expected.sortingDecision` | enum | Ja | sort/block/not-applicable |
| `expected.repairSafe` | bool | Ja | Repair/Rename sicher? |

#### DAT-Matching
| Feld | Typ | Pflicht | Beschreibung |
|------|-----|---------|-------------|
| `expected.dat.matchLevel` | enum | Ja | exact/cross-system/none/not-applicable |
| `expected.dat.ecosystem` | enum | Ja | no-intro/redump/mame/tosec/custom/none |
| `expected.dat.hashType` | enum | Nein | sha1/md5/crc32/sha1-chd-raw |

#### Detection-Erwartungen
| Feld | Typ | Pflicht | Beschreibung |
|------|-----|---------|-------------|
| `detectionExpectations.sources[].method` | enum | Ja | 9 Detection-Methoden |
| `detectionExpectations.sources[].expectedConsoleKey` | string | Ja | Was diese Methode finden soll |
| `detectionExpectations.conflictScenario` | object? | Nein | Erwarteter Conflict + Winner |

#### Verdicts (7 Ebenen gemäß ADR-015)
| Feld | Typ | Werte |
|------|-----|-------|
| `verdicts.containerVerdict` | enum | CORRECT/WRONG-TYPE/WRONG-CONTAINER |
| `verdicts.consoleVerdict` | enum | CORRECT/UNKNOWN/WRONG-CONSOLE/AMBIGUOUS-* |
| `verdicts.categoryVerdict` | enum | CORRECT/GAME-AS-JUNK/BIOS-AS-GAME/... |
| `verdicts.identityVerdict` | enum | CORRECT/WRONG-GAME/WRONG-VARIANT/... |
| `verdicts.datVerdict` | enum | EXACT/WRONG-SYSTEM/NONE-MISSED/... |
| `verdicts.sortingVerdict` | enum | CORRECT-SORT/WRONG-SORT/UNSAFE-SORT/... |
| `verdicts.repairVerdict` | enum | REPAIR-SAFE/REPAIR-RISKY/REPAIR-BLOCKED |

### 5.3 Ambiguität modellieren

Für Fälle mit mehreren akzeptablen Outcomes:

```json
{
  "id": "ec-gb-gbc-dual-001",
  "expected": {
    "consoleKey": "GBC",
    "confidenceClass": "high"
  },
  "acceptableAlternatives": [
    {
      "consoleKey": "GB",
      "confidenceClass": "high",
      "reason": "CGB=0x80 ist Dual-Mode; GB ist ebenfalls korrekt"
    }
  ]
}
```

**Evaluationslogik:**
1. `actual == expected` → **CORRECT**
2. `actual ∈ acceptableAlternatives` → **ACCEPTABLE** (zählt als CORRECT in Metriken)
3. Sonst → **WRONG**

### 5.4 Unsicherheit explizit modellieren

Für Fälle, bei denen die Ground Truth selbst unsicher ist:

```json
{
  "id": "cm-unknown-ambig-001",
  "expected": {
    "consoleKey": null,
    "category": "Unknown",
    "sortingDecision": "block"
  },
  "difficulty": "adversarial",
  "notes": "Absichtlich nicht erkennbar (Random Bytes + .bin Extension). UNKNOWN ist die einzig korrekte Antwort."
}
```

**Regel:** Wenn `expected.consoleKey == null`, dann ist jeder spezifische ConsoleKey-Output ein Fehler. UNKNOWN ist die einzig korrekte Antwort.

### 5.5 Mehrere gültige Sort-Ziele

Für Systeme mit Alias-Beziehungen (z.B. MEGADRIVE → MD):

```json
{
  "expected": {
    "consoleKey": "MD",
    "sortTarget": "MD"
  },
  "acceptableAlternatives": [
    {
      "consoleKey": "MD",
      "sortTarget": "Mega Drive",
      "reason": "Display-Name-Variante des gleichen Systems"
    }
  ]
}
```

---

## 6. Ordner- und Dateistruktur

### 6.1 Empfohlene Struktur

```
benchmark/
├── README.md                              ← Überblick, Pflegeregeln
├── manifest.json                          ← Version, Statistiken, Ground-Truth-Hash
│
├── ground-truth/                          ← ALLE Ground-Truth-Dateien (Git-tracked)
│   ├── ground-truth.schema.json           ← JSON-Schema (Draft 2020-12)
│   ├── golden-core.jsonl                  ← Pro Dataset-Klasse eigene Datei
│   ├── golden-realworld.jsonl
│   ├── chaos-mixed.jsonl
│   ├── edge-cases.jsonl
│   ├── negative-controls.jsonl
│   ├── repair-safety.jsonl
│   ├── dat-coverage.jsonl
│   ├── performance-scale.jsonl            ← Nur Metadaten; Daten generiert
│   └── .generated                         ← Fingerprint Marker
│
├── samples/                               ← Generierte Stub-Dateien (Git-IGNORED)
│   ├── .generated                         ← Generierungs-Fingerprint
│   ├── {consoleKey}/                      ← Pro System (nes/, ps1/, arcade/, etc.)
│   ├── unsorted/                          ← Für flat-mixed Tests
│   └── wrong_folder/                      ← Für Folder-Conflict Tests
│
├── dats/                                  ← Synthetische Test-DATs (Git-tracked)
│   ├── test-nointro-nes.xml
│   ├── test-nointro-snes.xml
│   ├── test-redump-ps1.xml
│   ├── test-mame-subset.xml
│   └── test-collision.xml                 ← Absichtliche Cross-System-Hashes
│
├── baselines/                             ← Referenz-Ergebnisse (Git-tracked)
│   ├── v1.0.0-baseline.json
│   ├── v2.0.0-baseline.json
│   └── latest-baseline.json               ← Aktuellste Baseline
│
├── gates.json                             ← Coverage-Gate-Schwellen
├── reports/                               ← Generierte Reports (Git-IGNORED)
│   └── .gitignore
│
└── docs/                                  ← Benchmark-spezifische Dokumentation
    ├── ADDING_SAMPLES.md                  ← Anleitung für neue Samples
    ├── STUB_GENERATOR_REFERENCE.md        ← Generator-Dokumentation
    └── HOLDOUT_MANAGEMENT.md              ← Holdout-Zone-Prozess
```

### 6.2 Begründung

| Entscheidung | Begründung |
|-------------|------------|
| Ground Truth pro Dataset-Klasse separiert | Unabhängig pflegbar; PRs gezielt auf eine Klasse |
| Samples Git-ignored, nur Generator committed | Copyright-sicher; reproduzierbar; kein Binary-Bloat |
| Baselines Git-tracked und versioniert | Trend-Vergleich zwischen Releases; nie überschrieben |
| Reports Git-ignored | Groß, generiert, als CI-Artefakt verwaltbar |
| `benchmark/docs/` als separate Doku-Zone | Trennung von Projekt-Docs und Benchmark-Doku |
| Samples nach ConsoleKey statt nach Dataset-Klasse | ConsoleDetector arbeitet per Pfad, nicht per Klasse |

### 6.3 Warum Stub-Generation statt statische Dateien

```
ground-truth/*.jsonl  ──→  StubGeneratorDispatch  ──→  samples/**/*
     (JSONL Metadaten)       (deterministische            (Binäre Stubs)
                              Byte-Generierung)
```

| Vorteil | Beschreibung |
|---------|-------------|
| Copyright-sicher | Nur Header-Bytes und Padding, kein lauffähiger ROM-Inhalt |
| Reproduzierbar | Deterministischer PRNG-Seed → gleiche Bytes bei jedem Run |
| Wartbar | Ground Truth pflegen statt 2.000 Binärdateien |
| Versionierbar | Git trackt nur JSONL (Text), nicht Binärdateien |
| Erweiterbar | Neues System = neuer Generator + JSONL-Entries |

---

## 7. Versionierung und Pflegeprozess

### 7.1 SemVer-Regeln

| Änderung | Version-Bump | Beispiel |
|----------|-------------|---------|
| Schema-Strukturänderung (neues Pflichtfeld) | MAJOR | `schemaVersion: 2.0.0` |
| Neue Entries hinzugefügt | MINOR | `groundTruthVersion: 1.3.0` |
| Korrektur bestehender Expected-Werte | PATCH | `groundTruthVersion: 1.2.1` |

### 7.2 Aufnahme neuer Samples

```
1. Bug oder Feature identifiziert → neues Testmuster nötig
2. Ground-Truth-Eintrag als JSONL-Zeile formulieren
   → id, path, set, expected, tags, difficulty, stub-Info
3. Generator-Regel hinzufügen (wenn neuer Generator-Typ nötig)
4. Lokal testen:
   → dotnet test --filter "Category=BenchmarkUnit"
   → Prüfen: Stub wird generiert, Evaluation läuft
5. PR erstellen mit:
   → Geänderte JSONL-Datei
   → Ggf. neuer Generator-Code
   → Begründung im PR-Text
6. Review: Min. 1 Reviewer prüft Ground-Truth-Korrektheit
7. Merge → manifest.json Version-Bump
```

### 7.3 Änderung bestehender Ground Truth

```
WARNUNG: Ground Truth ändern ist ein privilegierter Vorgang.

Erlaubt (1 Reviewer):
  → Korrektur nachweislich falscher Werte (PATCH)
  → Erweiterung acceptableAlternatives (PATCH)
  → Aktualisierung lastVerified (kein Review)

Eingeschränkt (2 Reviewer):
  → Ändern von expected.consoleKey
  → Ändern von expected.sortingDecision
  → Ändern von expected.repairSafe
  → Löschen von Samples

Verboten:
  → Löschen von Samples weil "Test stört"
  → Ändern Expected-Werte um CI grün zu machen
```

### 7.4 Historische Vergleichbarkeit

```
Regel: Baselines werden NIE überschrieben.

Ablauf:
1. Release v1.3.0 → Benchmark-Run → baselines/v1.3.0-baseline.json
2. latest-baseline.json wird aktualisiert
3. CI vergleicht jeden PR gegen latest-baseline.json
4. Trend-Dashboard zeigt Verlauf über alle Baselines

Bei Ground-Truth-MAJOR-Änderung:
  → Neue Baseline-Serie starten
  → Alte Baselines archivieren (nicht löschen)
  → Vergleiche nur innerhalb gleicher MAJOR-Version
```

### 7.5 Anti-Drift-Automatisierung

CI prüft bei jedem Build:

| Prüfung | Aktion bei Verstoß |
|---------|-------------------|
| Alle JSONL-Entries schema-valide | Build Fail |
| Alle IDs global eindeutig | Build Fail |
| Alle referenzierten Stubs generierbar | Build Fail |
| Pflicht-Chaos-Quote ≥ 30 % | Warning (v1.x), Fail (v2.0+) |
| Tier-Coverage-Minima eingehalten | Warning |
| lastVerified < 18 Monate | Warning pro Entry |
| manifest.json Version passt zu tatsächlicher Entry-Anzahl | Build Fail |

---

## 8. Nutzung im Projekt

### 8.1 Einbindung nach Testart

| Testart | Dataset-Klasse | Trigger | Erwartung | Laufzeit |
|---------|---------------|---------|-----------|----------|
| **Unit Tests** | golden-core (Subset) | Jeder Build | 100 % Pass | < 5s |
| **Integration Tests** | golden-core + edge-cases | Jeder Build | 100 % Pass | < 30s |
| **Regression Tests** | golden-core + golden-realworld + edge-cases | Jeder PR | Keine Regression vs. Baseline | < 90s |
| **Full Benchmark** | Alle Klassen (außer performance-scale + holdout) | Nightly / Release | Metriken + Trend | < 3min |
| **Performance Tests** | performance-scale | Nightly | Throughput ≥ Baseline - 10 % | < 5min |
| **Holdout Validation** | holdout-blind | Release / Nightly | Overfitting-Check | < 30s |
| **Manuelle QA** | chaos-mixed + repair-safety | Vor Release | Review UNKNOWN/WRONG | Manuell |
| **Bug-Reproduktion** | Neuer Entry in passender Klasse | Bei Bug-Report | Red→Green-Nachweis | - |
| **Golden-File-Tests** | Snapshot-Vergleich der Outputs | Jeder PR | Keine unbeabsichtigte Änderung | < 10s |

### 8.2 CI/CD-Pipeline

```
CI Pipeline (pro PR):

1. [Build]      dotnet build
2. [Generate]   StubGeneratorDispatch erzeugt Samples
3. [Schema]     SchemaValidator prüft alle JSONL-Entries
4. [Coverage]   CoverageValidator prüft Tier-Minima + Chaos-Quote
5. [Unit+Int]   dotnet test --filter "Category=BenchmarkUnit|BenchmarkIntegration"
                → golden-core + edge-cases, < 30s
6. [Regression] EvaluationRunner gegen golden-core + golden-realworld + edge-cases
                → Vergleich gegen latest-baseline.json
                → FAIL wenn: Wrong Match Rate ↑ > 0.1 %, Unsafe Sort Rate ↑ > 0.1 %
                → WARN wenn: Safe Sort Coverage ↓ > 2 %, Unknown Rate ↑ > 3 %
7. [Report]     benchmark-results.json als CI-Artefakt

Nightly:

8. [Full Bench] Alle Dataset-Klassen (ohne Holdout)
9. [Holdout]    holdout-blind auswerten → Overfitting-Check
10. [Perf]      performance-scale mit Timing
11. [Trend]     Vergleich gegen alle Baselines
12. [HTML]      Menschenlesbarer Benchmark-Report

Release:

13. [Holdout]   Pflicht-Prüfung: Holdout ≥ Eval - 5 %
14. [Baseline]  Neue Baseline speichern
15. [Manifest]  manifest.json aktualisieren
```

### 8.3 xUnit-Integration (Architektur)

```csharp
// Dataset-Klassen als xUnit Theory Data
[Theory]
[BenchmarkData("golden-core")]
[Trait("Category", "BenchmarkUnit")]
public void GoldenCore_Detection_MatchesGroundTruth(GroundTruthEntry entry)
{
    var result = fixture.Evaluate(entry);
    Assert.True(result.Verdict is BenchmarkVerdict.Correct or BenchmarkVerdict.AcceptableAlt,
        $"Entry {entry.Id}: Expected {entry.Expected.ConsoleKey}, " +
        $"got {result.ActualConsoleKey} (Verdict: {result.Verdict})");
}

// Regression-Gate als Fact
[Fact]
[Trait("Category", "BenchmarkRegression")]
public void Regression_WrongMatchRate_WithinThreshold()
{
    var results = fixture.EvaluateAll("golden-realworld");
    var baseline = BaselineComparator.LoadLatest();
    var comparison = BaselineComparator.Compare(results, baseline);
    Assert.True(comparison.WrongMatchRateDelta <= 0.001,
        $"Wrong Match Rate regression: {comparison.WrongMatchRateDelta:P3}");
}
```

### 8.4 Bug-Reproduktion

```
Wenn Bug gemeldet wird (z.B. "PS2-ROM wird als PS1 erkannt"):

1. Neuen Ground-Truth-Eintrag erstellen:
   → set: edge-cases, subclass: ps1-ps2-serial
   → expected.consoleKey: "PS2"
   → difficulty: hard
   → notes: "Bug #42: SLUS-20001 wurde als PS1 erkannt"

2. Stub erzeugen (Generator oder manuell)

3. dotnet test --filter "Bug42" → muss FAIL (Red Phase)

4. Fix implementieren

5. dotnet test --filter "Bug42" → muss PASS (Green Phase)

6. PR mit: JSONL-Eintrag + ggf. Generator + Fix

→ Testfall bleibt dauerhaft → Regression permanent verhindert
```

---

## 9. Risiken und Anti-Patterns

### 9.1 Identifizierte Risiken und Gegenmaßnahmen

| # | Anti-Pattern | Symptom | Gegenmaßnahme |
|---|-------------|---------|----------------|
| 1 | **Perfekt-Bias** | >70 % saubere Referenz-Samples | **Pflicht-Chaos-Quote ≥ 30 %; CI prüft** |
| 2 | **Happy-Path-Mono** | Nur Header-detectable Systeme | **Tier-Coverage: T3/T4 (Extension-only) Pflicht** |
| 3 | **Plattform-Mono** | >50 % NES + SNES | **Tier-1 max 60 % der golden-realworld** |
| 4 | **Overfitting** | Detection gegen bekannte Stubs getuned | **Holdout-Zone (ADR-016); Dev/Eval Split** |
| 5 | **Stub-Unrealismus** | Minimale 16-Byte-Header ohne Padding | **L2/L3 Stub-Realismus für Eval Zone** |
| 6 | **Tote Samples** | Header-Format stimmt nicht mehr | **lastVerified < 18 Monate; CI Warning** |
| 7 | **Ground-Truth-Drift** | Expected-Werte veralten nach Codeänderung | **Governance mit 2-Reviewer-Pflicht für Kernfelder** |
| 8 | **Unkontrolliertes Wachstum** | Samples ohne Review | **Gate: Jedes Sample braucht PR + Review** |
| 9 | **Alibi-Negatives** | Nur .txt und .jpg als Negativ-Kontrolle | **Fast-ROM-Pflicht: Random Bytes + ROM Extension** |
| 10 | **BIOS-Unterrepräsentanz** | <5 BIOS-Entries | **Min. 15 BIOS pro System-Tier** |
| 11 | **Archive-Blindheit** | Kaum ZIP/7z-Tests | **Min. 30 Archive-Entries über alle Klassen** |
| 12 | **Datenleakage** | Eval-Entries zum Tuning genutzt | **Code-Review-Pflicht + Holdout-Divergenz-Check** |

### 9.2 Datensatz-Audit (halbjährlich)

```
Prüfpunkte:
□ Anteil sauber : chaos : edge : negativ → Zielverhältnis ~40:25:20:15
□ Konsolen-Coverage → alle Tiers mit Min-Samples?
□ lastVerified < 18 Monate für alle Entries?
□ Keine Entries ohne Ground Truth?
□ performance-scale Generator aktuell?
□ Holdout-Zone aktuell und repräsentativ?
□ Neue Verwechslungspaare identifiziert? → edge-cases ergänzen
□ Stub-Generatoren konsistent mit aktueller Detection-Logik?
□ Kein signifikanter Eval vs. Holdout-Drift?
□ Baseline-Historie lückenlos?
```

---

## 10. Konkrete nächste Schritte

### Phase 1: Lücken schließen (Priorität 1 – blockt Qualitätsaussagen)

| # | Schritt | Deliverable | Status |
|---|---------|-------------|--------|
| 1 | **performance-scale Generator implementieren** | `ScaleDatasetGenerator` mit 5.000 L1-Stubs; JSONL-Metadaten | Offen |
| 2 | **Stub-Realismus L2 einführen** | `StubRealismLevel` Enum + L2-Logik in StubGeneratorDispatch | Offen |
| 3 | **PC/Computer-Coverage erweitern** | 30 neue Entries (DOS, AMIGA, C64, ZX, MSX, ATARIST) in golden-realworld + chaos-mixed | Offen |
| 4 | **Arcade-Tiefe erweitern** | 20 neue Entries (MAME Parent/Clone/Split/Merged/Non-Merged) | Offen |
| 5 | **Directory-based-Games-Coverage** | 10 neue edge-case Entries (Wii U RPX, 3DS CIA, DOS Directory) | Offen |

### Phase 2: Governance & Anti-Overfitting (Priorität 2)

| # | Schritt | Deliverable |
|---|---------|-------------|
| 6 | **Holdout-Zone einrichten** | Separates privates Repo oder verschlüsseltes Blob; 200 Entries; CI-Integration |
| 7 | **Pflicht-Chaos-Quote in CI** | CoverageValidator prüft ≥ 30 % Chaos-Quote |
| 8 | **Ground-Truth-Governance formalisieren** | ADDING_SAMPLES.md Anleitung; GitHub CODEOWNERS für ground-truth/ |
| 9 | **Anti-Drift CI-Checks** | Schema-Validierung + ID-Uniqueness + lastVerified-Prüfung bei jedem Build |
| 10 | **Baseline-Management** | Automatisches Speichern bei Release; Trend-Vergleich über Baselines |

### Phase 3: Breite und Tiefe (Priorität 3 – iterativ)

| # | Schritt | Zielgröße |
|---|---------|-----------|
| 11 | golden-realworld auf 500+ erweitern | +270 Entries (v.a. T2/T3/T4 Systeme) |
| 12 | chaos-mixed auf 300+ erweitern | +200 Entries (Unicode, Archive, Headerless) |
| 13 | negative-controls auf 100+ erweitern | +40 Entries (Fast-ROMs, Malicious Names) |
| 14 | repair-safety auf 100+ erweitern | +35 Entries (Multi-DAT, Weak Match) |
| 15 | Stub-Realismus L3 (Adversarial) einführen | L3-Logik für edge-cases und chaos-mixed |

### Phase 4: Reife (Priorität 4)

| # | Schritt |
|---|---------|
| 16 | Confusion-Matrix-Dashboard (HTML Report) |
| 17 | Trend-Dashboard über alle Baselines |
| 18 | Overfitting-Detector (Eval vs. Holdout Divergenz-Alert) |
| 19 | Jährlicher Datensatz-Audit-Prozess formalisiert |
| 20 | Dokumentation: HOLDOUT_MANAGEMENT.md, STUB_GENERATOR_REFERENCE.md |

---

## Anhang A: Konsolen-Coverage-Matrix

| Tier | Systeme | golden-core | golden-realworld | chaos-mixed | edge-cases | negative | repair-safety | dat-coverage |
|------|---------|:-----------:|:----------------:|:-----------:|:----------:|:--------:|:-------------:|:------------:|
| **T1** | NES, SNES, N64, GBA, GB, GBC, MD, PS1, PS2 | ✓ 20+ | ✓ 20+ | ✓ 5+ | ✓ 3+ | ✓ 2+ | ✓ 3+ | ✓ 5+ |
| **T2** | PSP, SAT, DC, GC, WII, 32X, SMS, GG, PCE, LYNX, A78, A26, NDS, 3DS, SWITCH, AMIGA, ARCADE | ✓ 5+ | ✓ 10+ | ✓ 2+ | ✓ 2+ | – | ✓ 2+ | ✓ 3+ |
| **T3** | C64, ZX, MSX, ATARIST, A800, COLECO, VECTREX, NGP, WS, JAG, INTV | ✓ 2+ | ✓ 5+ | – | – | – | – | ✓ 1+ |
| **T4** | 3DO, CD32, PCFX, JAGCD, NEOCD, FMTOWNS, CDI, SCD, DOS, PC98, X68K, CPC | ✓ 1+ | ✓ 3+ | – | – | – | – | – |

## Anhang B: Stub-Realismus-Level

| Level | Bytes | Einsatz | Repro-Seed |
|-------|-------|---------|------------|
| L1-minimal | Nur Header-Bytes, minimale Größe | golden-core, dat-coverage | Nicht nötig (deterministisch) |
| L2-realistic | Header + PRNG-Padding, realistische Größenklasse | golden-realworld, repair-safety | `SHA256(entry.id)` als Seed |
| L3-adversarial | Header + Varianz (Alignment-Fehler, Trailing-Junk, Padding-Anomalien) | edge-cases, chaos-mixed | `SHA256(entry.id + "adversarial")` als Seed |

## Anhang C: Detection-Methoden-Abdeckungsziel

| Detection-Methode | Min. Entries | Systeme |
|-------------------|-------------|---------|
| CartridgeHeader | 80 | NES, SNES, N64, GBA, GB, GBC, MD, 32X, LYNX, A78 |
| DiscHeader | 60 | PS1, PS2, PSP, GC, WII, SAT, DC, SCD, 3DO |
| UniqueExtension | 100 | Alle 45+ Systeme mit unique Extensions |
| FolderName | 40 | Alle 69 Systeme (über Aliases) |
| ArchiveContent | 30 | ZIP/7z-basierte Detection |
| SerialNumber | 20 | PS1, PS2, PS3, PSP, NDS |
| FilenameKeyword | 20 | [PS1], [GBA], MAME-Keywords |
| AmbiguousExtension | 25 | .bin, .iso, .img (geteilte Extensions) |
| DatHash | 30 | Alle DAT-Ecosysteme |
