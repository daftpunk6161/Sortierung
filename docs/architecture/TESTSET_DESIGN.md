# Real-World Testset Design – RomCleanup

---

## 1. Executive Verdict

### Warum ein realistisches Testset unverzichtbar ist

RomCleanup trifft **destruktive Entscheidungen** auf Basis von Erkennung: Dateien werden verschoben, sortiert, dedupliziert, umbenannt. Jede falsche Erkennung ist ein potenzieller Datenverlust. Die aktuellen 2.700+ Unit-Tests prüfen Codepfade, aber **nicht die Erkennungsqualität in realistischen Szenarien**. Es gibt:

- **Keine Confusion Matrices**, die zeigen, welche Systeme verwechselt werden
- **Keinen Benchmark-Datensatz**, der realistische ROM-Sammlungen abbildet
- **Keine Ground Truth**, gegen die neue Builds automatisch gemessen werden
- **Keine Regressionserkennung** auf Erkennungsebene (nur auf Code-Ebene)

### Hauptproblem schlechter Testsets

Ein Testset, das überwiegend saubere, korrekt benannte Referenz-ROMs enthält, misst nicht die reale Erkennungsqualität, sondern den Happy Path. Echte ROM-Sammlungen sind chaotisch: falsch benannte Dateien, fehlende Header, gemischte Ordner, Unicode, kaputte Archive, BIOS-Dateien neben Spielen, Homebrew neben kommerziellen ROMs.

Ein Testset, das dieses Chaos nicht abbildet, erzeugt **falsche Sicherheit**.

### Kurzfazit

Das Testset-System muss drei Eigenschaften gleichzeitig erfüllen:

1. **Repräsentativ** – bildet die Verteilung und Probleme realer Sammlungen ab
2. **Deterministisch** – liefert reproduzierbare, maschinenauswertbare Ergebnisse
3. **Schützend** – verhindert Regressions-Blindheit und Benchmark-Overfitting

---

## 2. Ziele des Testset-Systems

### Was das Testset leisten muss

| Ziel | Beschreibung |
|------|-------------|
| Erkennungsqualität messen | Precision, Recall, F1 pro System, pro Kategorie, pro Schwierigkeitsgrad |
| Regressionserkennung | Jeder Build wird automatisch gegen Ground Truth geprüft |
| Schwächen sichtbar machen | Confusion Matrices zeigen systematische Verwechslungen |
| Fortschritte nachweisen | Trend-Vergleich zwischen Builds, nicht nur Pass/Fail |
| Freigabeentscheidungen stützen | Sorting/Repair nur freigeben, wenn messbare Schwellen erreicht |
| Praxisnähe garantieren | Chaos-Fälle, Edge-Cases und Negativ-Kontrollen als Pflicht |

### Was es ausdrücklich NICHT sein darf

| Anti-Ziel | Warum |
|-----------|-------|
| Kein Demo-Fixture-Set | 20 handverlesene Dateien testen nichts Reales |
| Kein Repository für echte ROMs | Copyright-Verletzung; nicht committbar |
| Kein statischer Snapshot | Muss wachsen, aber kontrolliert |
| Kein Selbstbedienungs-Ablageordner | Jeder Testfall braucht Review und Ground Truth |
| Kein Overfitting-Target | Benchmark-Set darf nicht zur Optimierungsgrundlage werden |

---

## 3. Dataset-Klassen

### 3.1 `golden-core` – Deterministische Referenz-Fixtures

| Feld | Wert |
|------|------|
| **Zweck** | Schnelle, deterministische Unit- und Integrationstests für jede Erkennungsmethode |
| **Zielgrösse** | 200–300 Samples |
| **Testart** | Unit, Integration, CI (jeder Build) |
| **Laufzeit** | < 10 Sekunden |

**Inhalte:**

| Unterklasse | Anzahl | Beschreibung |
|-------------|--------|-------------|
| Cartridge-Header-Stubs | 30 | Je 2-3 pro Header-System: NES (iNES), N64 (3 Endian-Varianten), GBA, GB, GBC (CGB-Flag 0x00/0x80/0xC0), SNES (LoROM + HiROM + Copier-Header), MD, 32X, Lynx, Atari 7800 |
| Disc-Header-Stubs | 30 | PS1/PS2/PSP (PVD), GC/Wii (Magic), Saturn/DC/SegaCD (IP.BIN-Text), 3DO (Opera FS), Xbox |
| Unique-Extension-Samples | 40 | Je 1 pro uniqueExt aus consoles.json (mindestens 40 der 69 Systeme haben unique Extensions) |
| Folder-Name-Samples | 30 | Je 1-2 pro FolderAlias-Cluster (NES/famicom, Genesis/megadrive, etc.) |
| BIOS-Referenz | 15 | Korrekt getaggte BIOS-Dateien verschiedener Systeme |
| Junk-Tags | 20 | Demo, Beta, Proto, Hack, Homebrew, Pirate, Bootleg, [b], [h], [t], [p] |
| Keyword/Serial-Filename | 20 | SLUS-xxxxx, BCUS-xxxxx, NTR-XXXX-XXX, [PS1], [GBA] etc. |
| DAT-Hash-Stubs | 15 | Dateien mit bekannten SHA1-Hashes aus Test-DATs |
| Archive-Stubs | 15 | ZIP mit korrekter innerer Extension, ZIP mit falscher, korruptes ZIP |
| Negative Controls | 15 | .txt, .jpg, .exe, .pdf, leere Dateien, 0-Byte, nur Nullen |

**Welche Kernfunktionen getestet werden:**
- CartridgeHeaderDetector, DiscHeaderDetector
- ConsoleDetector.DetectByExtension, DetectByFolder
- FileClassifier.Classify
- FilenameConsoleAnalyzer.DetectBySerial, DetectByKeyword
- DatIndex.Lookup
- HypothesisResolver.Resolve

**Risiken / Biases:**
- ⚠️ Zu sauber: Nur korrekt benannte, korrekt strukturierte Dateien → kein Chaos-Stress
- ⚠️ Lösung: Diese Klasse prüft nur Basis-Erkennung; Chaos wird in anderen Klassen getestet
- ⚠️ Bias: Header-detectable Systeme überrepräsentiert → bewusst so, weil diese die höchsten Qualitätsziele haben

---

### 3.2 `golden-realworld` – Realistisches Referenz-Set

| Feld | Wert |
|------|------|
| **Zweck** | Benchmarking der End-to-End-Erkennung unter realistischen Bedingungen |
| **Zielgrösse** | 500–800 Samples |
| **Testart** | Integration, Benchmark, Regression (nightly oder per Release) |
| **Laufzeit** | < 60 Sekunden |

**Inhalte:**

| Unterklasse | Anzahl | Beschreibung |
|-------------|--------|-------------|
| No-Intro-Benennungskonvention | 150 | Korrekte No-Intro-Dateinamen aus DAT-Katalogen (synthetische Stubs mit korrektem Namen, keinem echten ROM-Inhalt) |
| Redump-Benennungskonvention | 80 | Korrekte Redump-Dateinamen für Disc-Systeme |
| Folder-sortierte Sammlung | 80 | Dateien in benannten System-Ordnern, simuliert typische sortierte Sammlung |
| Gemischte Sammlung (flach) | 60 | Alle ROMs in einem Ordner, keine Folder-Hints |
| Multi-Disc-Sets | 40 | CUE+BIN (2-4 Tracks), GDI+Track-Dateien, M3U+CHD-Referenzen, CCD+IMG+SUB, MDS+MDF |
| Regions-Varianten | 40 | Gleicher Titel in EU/US/JP/WORLD, korrekte Tags |
| Revisions | 30 | (Rev A), (Rev B), (v1.0), (v1.1), [!] verified |
| BIOS im Kontext | 20 | BIOS-Dateien neben Spiel-ROMs im gleichen System-Ordner |

**Welche Kernfunktionen getestet werden:**
- Vollständige EnrichmentPipelinePhase (alle Detektoren + Scoring + DAT)
- ConsoleSorter Sort-Entscheidungen
- Confidence-Gating im RunOrchestrator
- Region/Version/Format-Scoring

**Konsolen-Verteilung (Pflicht-Minimum pro System):**

| Tier | Systeme | Min. Samples pro System | Begründung |
|------|---------|------------------------|------------|
| Tier-1 (Header + DAT) | NES, SNES, N64, GBA, GB, GBC, MD, PS1, PS2 | 20 | Häufigste Systeme, vollste Detection |
| Tier-2 (Header oder DAT) | PSP, SAT, DC, GC, Wii, 32X, Lynx, 7800 | 10 | Mittelgrosse Sammlungen, gute Detection |
| Tier-3 (Ext/Folder only) | 2600, 5200, Amiga, MSX, PCE, TG16, Coleco, Vectrex | 5 | Kleine Sammlungen, limitierte Detection |
| Tier-4 (Selten) | 3DO, CD32, PCFX, JAGCD, NEOCD, FMTOWNS | 3 | Seltene Systeme, vollständigkeitshalber |

**Risiken / Biases:**
- ⚠️ Tier-1 dominiert → bewusst, weil dort 80 % der echten Sammlungen liegen
- ⚠️ Alle Dateinamen sind „sauber" → Chaos-Fälle in separater Klasse
- ⚠️ Synthetische Stubs können DAT-specifische Hashing nicht testen → DAT-Coverage als eigene Klasse

---

### 3.3 `chaos-mixed` – Robustheit unter Realbedingungen

| Feld | Wert |
|------|------|
| **Zweck** | Prüfung der Robustheit bei chaotischen, fehlerhaften und irreführenden Inputs |
| **Zielgrösse** | 300–500 Samples |
| **Testart** | Integration, Benchmark, Regression |
| **Laufzeit** | < 45 Sekunden |

**Inhalte:**

| Unterklasse | Anzahl | Beschreibung | Konkretes Beispiel |
|-------------|--------|-------------|-------------------|
| Falsch benannt | 50 | ROM mit falschem Systemnamen im Dateinamen | `Super Mario Bros (SNES).nes` (ist NES, nicht SNES) |
| Gekürzte Namen | 40 | Abgekürzte, verstümmelte Dateinamen | `FF7_D1.iso`, `Pkmn_R.gba`, `SMB3.nes` |
| Falsche Extension | 40 | Korrekte ROM, aber falsche Dateiendung | NES-ROM als `.bin`, GBA-ROM als `.rom`, PS1-ISO als `.img` |
| Unicode-Namen | 30 | Japanisch, Koreanisch, Arabisch, Akzente, Emoji | `ファイナルファンタジー.iso`, `Pokémon – Édition Rouge.gba` |
| Sonderzeichen | 20 | Klammern, Quotes, Ampersand, Leerzeichen-Varianten | `Tom & Jerry (v1.0) [!].nes`, `Pac-Man™.z64` |
| Inkonsistente Tags | 30 | Gemischte No-Intro + Redump + Custom-Tags | `Mario (U) [!] (Rev A) [hM04].nes` |
| Ohne Ordner-Kontext | 40 | Alle Dateien in einem flachen Ordner, nur Dateiname | Kein Folder-Detection möglich |
| Gemischte Archive | 20 | ZIP mit ROMs verschiedener Systeme | ZIP enthält `.nes` + `.gba` + `.smd` |
| Kaputte Archive | 20 | Halb-korrupte ZIPs, 0-Byte-ZIPs, passwortgeschützt | Truncated ZIP, ZIP mit nur Verzeichniseinträgen |
| Doppelte Dateien | 20 | Identische ROMs mit verschiedenen Namen | `Mario.nes` + `Super Mario Bros.nes` (gleicher Inhalt) |
| Headerless ROMs | 15 | NES ohne iNES, SNES ohne Header | Nur Extension als Hinweis |
| Teilweise beschädigt | 15 | Abgeschnittene Dateien, Null-Padding | Header korrekt, aber Datei nach 1 KB abgeschnitten |

**Welche Kernfunktionen getestet werden:**
- ConsoleDetector: Fallback-Kette bei widersprüchlichen Signalen
- HypothesisResolver: Konflikt-Erkennung und -Bestrafung
- FileClassifier: Robustheit gegen ungewöhnliche Namen
- Confidence-Kalibrierung: Stimmt Confidence mit tatsächlicher Korrektheit überein?

**Risiken / Biases:**
- ⚠️ Chaos-Fälle sind per Definition nicht representativ für saubere Sammlungen → separate Auswertung nötig
- ⚠️ Zu konstruiert? Realistische Chaos-Quellen: Goodtools-Benennungsstil, Internet-Archiv-Downloads, Community-Shares
- ⚠️ UNKNOWN ist hier oft die korrekte Antwort → Ground Truth muss das abbilden

---

### 3.4 `edge-cases` – Gezielte Grenzfälle

| Feld | Wert |
|------|------|
| **Zweck** | Prüfung bekannter Verwechslungspaare und ambiger Situationen |
| **Zielgrösse** | 150–250 Samples |
| **Testart** | Unit, Integration, Regression |
| **Laufzeit** | < 20 Sekunden |

**Inhalte:**

| Unterklasse | Anzahl | Beschreibung | Konkretes Beispiel |
|-------------|--------|-------------|-------------------|
| Cross-System-Namenskollision | 30 | Gleicher Spielname, verschiedene Systeme | `Tetris.gb`, `Tetris.nes`, `Tetris.smd` – alle korrekt |
| PS1 ↔ PS2 Serial-Ambiguität | 15 | SLUS-Serials mit 3-5 Digits | `SLUS-00123` (PS1?), `SLUS-12345` (PS2?) |
| GB ↔ GBC CGB-Flag | 10 | CGB-Flag 0x00, 0x80, 0xC0 | 0x80 = Dual-Mode → GBC oder GB? |
| Genesis ↔ 32X | 10 | Beide nutzen „SEGA" @ 0x100 | `SEGA MEGA DRIVE` vs `SEGA 32X` |
| BIOS-ähnliche Spielnamen | 15 | BIOS-Dateien mit spielähnlichen Namen, oder Spiele mit BIOS-ähnlichen Namen | `PlayStation BIOS (v3.0).bin` vs `BioShock.iso` |
| Multi-Disc-Zuordnung | 20 | Disc 1-4, korrekte Set-Zuordnung | `Final Fantasy VII (Disc 1).iso` + `(Disc 2).iso` + `(Disc 3).iso` |
| Headerless mit Extension | 15 | ROM ohne Header, aber korrekte Extension | `.nes`-Datei ohne iNES-Header → nur Extension gibt Hinweis |
| Archive mit gemischtem Inhalt | 10 | ZIP mit ROMs verschiedener Systeme | ZIP mit `.nes` und `.gba` → welches System? |
| DAT-Kollision | 10 | Gleicher Hash in verschiedenen System-DATs | SHA1 existiert in PS1-DAT und PS2-DAT |
| Ohne DAT vs. mit DAT | 15 | Gleiche Datei: einmal mit geladenem DAT, einmal ohne | Erkennung muss ohne DAT funktionieren, nur schwächer |
| SNES Bimodal | 10 | LoROM/HiROM/Copier-Header-Varianten | LoROM-Header @ 0x7FC0 vs HiROM @ 0xFFC0 |
| Falsche Folder-Zuordnung | 10 | ROM im falschen System-Ordner | NES-ROM in `SNES/`-Ordner → Folder sagt SNES, Header sagt NES |
| Region-Ambiguität | 10 | Mehrere Regionen, widersprüchliche Tags | `(USA, Europe)` vs `(World)` |

**Welche Kernfunktionen getestet werden:**
- HypothesisResolver: Konflikt-Handling
- ConsoleDetector: Multi-Source-Aggregation bei Widersprüchen
- DatIndex.LookupAny: Cross-System-Auflösung
- Confidence-Gating: Wann wird blockiert?

**Risiken / Biases:**
- ⚠️ Fokussiert auf bekannte Probleme → neue, unbekannte Verwechslungen werden nicht abgedeckt
- ⚠️ Lösung: Jeder Bug-Report mit Verwechslung wird als neuer Edge-Case aufgenommen

---

### 3.5 `negative-controls` – Was NICHT erkannt werden darf

| Feld | Wert |
|------|------|
| **Zweck** | Sicherstellung, dass Nicht-ROM-Dateien, Junk und irreführende Inputs korrekt abgelehnt werden |
| **Zielgrösse** | 100–150 Samples |
| **Testart** | Unit, Integration, Regression |
| **Laufzeit** | < 10 Sekunden |

**Inhalte:**

| Unterklasse | Anzahl | Beschreibung | Konkretes Beispiel |
|-------------|--------|-------------|-------------------|
| Nicht-ROM-Dateitypen | 25 | Dateien, die niemals als ROM erkannt werden dürfen | `.txt`, `.jpg`, `.png`, `.pdf`, `.exe`, `.dll`, `.mp3`, `.avi` |
| Irreführende Dateinamen | 20 | ROM-ähnliche Namen, aber keine ROMs | `Nintendo 64 Game.exe`, `SNES Classic.pdf`, `GameBoy Color.txt` |
| Junk / Demo / Beta | 20 | Korrekte Junk-Erkennung für alle JunkTag-Pattern | `Mario (Demo).nes`, `Zelda (Beta 3).sfc`, `Sonic (Proto).bin` |
| Leere / kaputte Dateien | 15 | 0-Byte, nur Nullen, nur Header ohne Payload, zufälliger Inhalt | `empty.nes` (0 Bytes), `random.bin` (Random Bytes) |
| Dummy mit ROM-Extension | 15 | Nicht-ROM-Inhalt mit ROM-Extension | `.nes`-Datei die eigentlich eine JPEG ist |
| Homebrew / Hack | 15 | Homebrew und Hacks müssen als NonGame/Junk klassifiziert werden | `Super Mario World (Hack by X).smc`, `MyGame (Homebrew).nes` |

**Welche Kernfunktionen getestet werden:**
- FileClassifier: JUNK, NonGame, UNKNOWN-Erkennung
- ConsoleDetector: Muss UNKNOWN zurückgeben für Nicht-ROMs
- Sorting-Gate: Muss blockieren

**Risiken / Biases:**
- ⚠️ Zu offensichtlich? Ja – aber diese Fälle müssen trotzdem 100 % bestehen
- ⚠️ Fehlende „fast-ROM"-Fälle → ROM-Header gefolgt von Müll = Edge Case

---

### 3.6 `repair-safety` – Sicherheitsklassifikation

| Feld | Wert |
|------|------|
| **Zweck** | Prüfung, ob Confidence und Match-Qualität korrekt die Repair-/Rename-/Sort-Sicherheit steuern |
| **Zielgrösse** | 100–150 Samples |
| **Testart** | Integration, Benchmark |
| **Laufzeit** | < 15 Sekunden |

**Inhalte:**

| Unterklasse | Anzahl | Beschreibung | Expected Decision |
|-------------|--------|-------------|-------------------|
| DAT-Exact + High Confidence | 20 | Hash-verifiziert, eindeutige Erkennung | Repair-Safe, Sort-Safe |
| DAT-Exact + Low Confidence | 10 | Hash passt, aber Detection widersprüchlich | Sort-Safe (DAT-Trust), Repair-Risky |
| DAT-None + High Confidence | 15 | Gute Erkennung, aber kein DAT verfügbar | Sort-Safe, Repair-Blocked |
| DAT-None + Low Confidence | 15 | Schlechte Erkennung, kein DAT | UNKNOWN → Block All |
| Conflict + DAT-Match | 10 | Zwei Systeme high-confidence, DAT disambiguiert | Sort-Safe (via DAT), Repair-Risky |
| Weak Match (LookupAny) | 10 | DAT-Match nur über Cross-System-Fallback | Flag für Review, Repair-Blocked |
| Ambiguous-Multi-DAT | 10 | Hash in mehreren System-DATs | Review Needed, Block Sort |
| Folder-Only-Detection | 10 | Einziger Hinweis ist Ordnername | Sort-Risky, Repair-Blocked |
| Single-Source Low-Conf | 10 | Nur AmbiguousExtension oder FilenameKeyword | UNKNOWN, Block All |

**Welche Kernfunktionen getestet werden:**
- RunOrchestrator.StandardPhaseSteps: Confidence-Gating (≥80, ¬Conflict)
- Repair-Tauglichkeit: DAT-Exact ∧ Confidence ≥ 95 ∧ ¬Conflict
- Sorting: Korrekte Block/Sort-Entscheidung pro Confidence-Level

**Risiken / Biases:**
- ⚠️ Repair ist noch nicht implementiert → Tests definieren die Spezifikation vorab
- ⚠️ Confidence-Schwellen sind aktuell hardcoded (80) → Tests müssen bei Schwellenänderung aktualisiert werden

---

### 3.7 `performance-scale` – Skalierung und Performance

| Feld | Wert |
|------|------|
| **Zweck** | Performance-Messung und Skalierungsverhalten bei grossen Sammlungen |
| **Zielgrösse** | 5.000–20.000 Samples (generiert, nicht manuell gepflegt) |
| **Testart** | Performance, Nightly Benchmark |
| **Laufzeit** | < 5 Minuten |

**Inhalte:**

Generiert durch einen `ScaleDatasetGenerator`:

| Parameter | Wert | Beschreibung |
|-----------|------|-------------|
| Systeme | 20+ | Verteilung nach realer Häufigkeit |
| Dateien pro System | 100–500 | Simuliert mittelgrosse bis grosse Sammlung |
| Extension-Mix | 70 % unique, 20 % ambiguous, 10 % headerless | Realistische Verteilung |
| Folder-Struktur | 50 % sortiert, 30 % flach, 20 % falsch | Mischt organisiert und chaotisch |
| Archive | 10 % ZIP, 5 % 7z | Realistischer Archiv-Anteil |
| BIOS-Quote | 2 % | Typischer BIOS-Anteil |
| Junk-Quote | 5 % | Typischer Junk-Anteil |
| Duplikate | 8 % | Typische Duplikat-Rate |

**Welche Kernfunktionen getestet werden:**
- Pipeline-Throughput (Dateien/Sekunde)
- Memory-Footprint bei grossen Sammlungen
- LruCache-Effektivität
- DAT-Index-Performance bei grossen DATs
- EnrichmentPipelinePhase Streaming-Verhalten

**Risiken / Biases:**
- ⚠️ Generierte Daten ≠ echte Sammlungen → nur Performance, nicht Erkennungsqualität
- ⚠️ Ground Truth wird automatisch generiert → nicht für Qualitätsauswertung geeignet (nur Timing)

---

### 3.8 `dat-coverage` – DAT-Matching-Qualität

| Feld | Wert |
|------|------|
| **Zweck** | Isolierte Prüfung der DAT-Matching-Logik ohne Einfluss der Console-Detection |
| **Zielgrösse** | 150–200 Samples |
| **Testart** | Integration, Benchmark |
| **Laufzeit** | < 20 Sekunden |

**Inhalte:**

| Unterklasse | Anzahl | Beschreibung |
|-------------|--------|-------------|
| No-Intro-Hash-Matches | 40 | Stubs mit SHA1-Hashes, die in No-Intro-DATs existieren |
| Redump-Hash-Matches | 30 | Stubs mit SHA1-Hashes aus Redump-DATs |
| Hash-Collision Cross-System | 15 | Hash existiert in 2+ System-DATs |
| Hash-Miss (Homebrew) | 20 | Dateien, die in keinem DAT existieren |
| Hash-Miss (Undumped) | 15 | Bekannte Spiele, aber kein Dump im DAT |
| Container-vs-Content-Hash | 15 | ZIP-Hash vs innerer ROM-Hash → richtiger Match nur über inneren Hash |
| CHD-Embedded-SHA1 | 10 | CHD v5 mit embedded raw SHA1 → muss matchen, nicht Container-Hash |
| DAT-Format-Varianten | 10 | CLR-MAE, No-Intro XML, Redump XML → Parser-Robustheit |

**Risiken / Biases:**
- ⚠️ Echte DAT-Hashes sind nötig, aber nur SHA1-Strings → keine Copyright-Probleme
- ⚠️ DAT-Katalog-Abdeckung: Test-DATs müssen die gleichen Formate wie Produktions-DATs nutzen

---

## 4. Pflichtfälle

### 4.1 Saubere Referenzfälle (MUSS in `golden-core` + `golden-realworld`)

| # | Falltyp | Min. Anzahl | Begründung |
|---|---------|------------|------------|
| 1 | NES-ROM mit iNES-Header + `.nes` | 5 | Häufigstes Cartridge-System |
| 2 | SNES-ROM LoROM + HiROM | 4 | Bimodale Header-Erkennung |
| 3 | N64 alle 3 Endian-Varianten | 3 | BE, Byte-Swap, LE → gleicher Inhalt |
| 4 | GBA mit Nintendo-Logo-Signatur | 3 | Offset 0x04, sehr zuverlässig |
| 5 | GB vs GBC (CGB-Flag-Varianten) | 4 | 0x00=GB, 0x80=Dual, 0xC0=GBC-only |
| 6 | Genesis vs 32X (Header @0x100) | 4 | Verwechslungspaar |
| 7 | PS1/PS2/PSP via ISO9660-PVD | 6 | 3 PVD-Offsets × 2 Systeme |
| 8 | GameCube/Wii Magic-Bytes | 4 | Sehr spezifische Signaturen |
| 9 | Saturn/DC via IP.BIN-Text | 4 | Regex-basierte Erkennung |
| 10 | BIOS korrekt getaggt | 10 | `(BIOS)`, `[BIOS]`, `bios` |
| 11 | Multi-Disc CUE+BIN | 5 | Atomare Set-Erkennung |
| 12 | DAT-Exact-Hash-Match | 10 | SHA1-Verifikation |
| 13 | Folder-Detection (alle Major-Aliases) | 15 | FolderName→Console-Mapping |
| 14 | Unique Extension je System | 40 | Grundlage-Erkennung |

### 4.2 Chaos-Fälle (MUSS in `chaos-mixed`)

| # | Falltyp | Min. Anzahl | Begründung |
|---|---------|------------|------------|
| 1 | ROM mit falschem Systemnamen im Dateinamen | 10 | Häufig in Wildsammlungen |
| 2 | ROM mit falscher Dateiendung | 10 | `.bin` für alles, `.rom` generisch |
| 3 | Gekürzte/verstümmelte Namen | 10 | Internet-Downloads, alte Goodtools-Sets |
| 4 | Unicode-Dateinamen (JP/KR/AR) | 10 | Japanische ROM-Sammlungen |
| 5 | Gemischt sortierte Ordner | 10 | Alles in einem Ordner |
| 6 | Kaputte/Truncated Archives | 5 | Download-Abbrüche |
| 7 | ROMs in falschen System-Ordnern | 5 | User-Fehler |
| 8 | Doppelte Dateien verschiedener Namen | 5 | Typisch in gemischten Sammlungen |

### 4.3 Schwierige Fälle (MUSS in `edge-cases`)

| # | Falltyp | Min. Anzahl | Begründung |
|---|---------|------------|------------|
| 1 | Cross-System-Namenskollision (Tetris etc.) | 10 | Gleicher Name, verschiedene Systeme |
| 2 | PS1 ↔ PS2 Serial-Overlap | 8 | SLUS-Serials mit 3-5 Digits |
| 3 | GB ↔ GBC Dual-Mode (CGB=0x80) | 5 | Konventions-Frage |
| 4 | Genesis ↔ 32X Header-Ähnlichkeit | 5 | „SEGA" im Header |
| 5 | BIOS vs Spiel mit ähnlichem Namen | 8 | `PlayStation BIOS` vs `BioShock` |
| 6 | Headerless ROMs mit korrekter Extension | 5 | Fallback auf Extension |
| 7 | Folder sagt X, Header sagt Y | 5 | Widersprüchliche Signale |
| 8 | DAT-Match-Kollision Cross-System | 5 | Hash in 2 System-DATs |

### 4.4 Negative Kontrollen (MUSS in `negative-controls`)

| # | Falltyp | Min. Anzahl | Erwartung |
|---|---------|------------|-----------|
| 1 | `.txt`, `.jpg`, `.pdf`, `.exe` | 15 | UNKNOWN, kein System |
| 2 | ROM-Name, aber kein ROM-Inhalt | 10 | UNKNOWN |
| 3 | Demo / Beta / Proto / Hack | 15 | Category = Junk |
| 4 | 0-Byte-Dateien | 5 | UNKNOWN |
| 5 | Zufälliger Binär-Inhalt mit ROM-Extension | 5 | UNKNOWN oder System nur via Extension |

### 4.5 Safety-Fälle (MUSS in `repair-safety`)

| # | Falltyp | Min. Anzahl | Erwartung |
|---|---------|------------|-----------|
| 1 | DAT-Exact + Confidence ≥ 95 | 10 | Repair-Safe, Sort-Safe |
| 2 | Confidence < 80 | 10 | Sort-Blocked |
| 3 | HasConflict = true | 10 | Sort-Blocked |
| 4 | DAT-Match via LookupAny | 5 | Review Needed |
| 5 | Folder-Only-Detection | 5 | Not Repair-Safe |
| 6 | Category ≠ Game | 10 | Sort-Blocked |

---

## 5. Ground-Truth-Modell

### 5.1 Format: JSONL (eine Zeile pro Sample)

JSONL wird gewählt, weil:
- jede Zeile unabhängig parsbar (kein korruptes Gesamt-JSON bei einem Fehler)
- maschinenlesbar und menschenpflegbar
- Git-diff-freundlich (Zeilen-Diff statt Objekt-Diff)
- leicht filterbar mit `jq`, `grep`, PowerShell

### 5.2 Schema pro Eintrag

```jsonl
{
  "id": "gc-nes-header-001",
  "path": "golden-core/cartridge-headers/Super Mario Bros. (World).nes",
  "set": "golden-core",
  "subclass": "cartridge-header",
  "container": "single",
  "stub": {
    "generator": "nes-ines-header",
    "sizeBytes": 16,
    "headerBytes": "4E45531A"
  },
  "expected": {
    "consoleKey": "NES",
    "category": "Game",
    "gameIdentity": "Super Mario Bros.",
    "region": "WORLD",
    "version": null,
    "discNumber": null,
    "discSetSize": null,
    "datMatchLevel": "exact",
    "datGameName": "Super Mario Bros. (World)",
    "confidenceClass": "high",
    "sortingDecision": "sort",
    "sortTarget": "NES",
    "repairSafe": true
  },
  "acceptableAlternatives": null,
  "detectionSources": ["CartridgeHeader", "UniqueExtension"],
  "difficulty": "easy",
  "tags": ["header", "unique-ext", "tier-1", "no-intro"],
  "addedInVersion": "1.0.0",
  "lastVerified": "2026-03-20",
  "notes": ""
}
```

### 5.3 Feld-Definitionen

| Feld | Typ | Pflicht | Beschreibung |
|------|-----|---------|-------------|
| `id` | string | Ja | Globale eindeutige ID. Format: `{set-prefix}-{system}-{subclass}-{nr}` |
| `path` | string | Ja | Relativer Pfad zur Stub-Datei im Benchmark-Verzeichnis |
| `set` | enum | Ja | `golden-core`, `golden-realworld`, `chaos-mixed`, `edge-cases`, `negative-controls`, `repair-safety`, `performance-scale`, `dat-coverage` |
| `subclass` | string | Ja | Feingranulare Unterklasse innerhalb des Sets |
| `container` | enum | Ja | `single`, `zip`, `7z`, `multi-file-set` |
| `stub` | object? | Nein | Stub-Generierungsinfo (nur für synthetische Dateien) |
| `stub.generator` | string | Nein | Name des Generators (z.B. `nes-ines-header`, `pvd-ps1`, `empty-file`) |
| `stub.sizeBytes` | int | Nein | Minimale Dateigrösse des Stubs |
| `stub.headerBytes` | string? | Nein | Hex-String der Header-Bytes für Reproduktion |
| `expected.consoleKey` | string? | Ja | Erwartetes System oder `null` (wenn UNKNOWN) |
| `expected.category` | enum | Ja | `Game`, `Bios`, `Junk`, `NonGame`, `Unknown` |
| `expected.gameIdentity` | string? | Nein | Erwarteter Spielname |
| `expected.region` | string? | Nein | Erwartete Region (EU, US, JP, WORLD, etc.) |
| `expected.version` | string? | Nein | Erwartete Version/Revision |
| `expected.discNumber` | int? | Nein | Disc-Nummer im Set |
| `expected.discSetSize` | int? | Nein | Gesamtzahl der Discs |
| `expected.datMatchLevel` | enum | Ja | `exact`, `none`, `not-applicable` |
| `expected.datGameName` | string? | Nein | Erwarteter DAT-Spielname |
| `expected.confidenceClass` | enum | Ja | `high` (≥80), `medium` (50-79), `low` (<50), `any` |
| `expected.sortingDecision` | enum | Ja | `sort`, `block`, `not-applicable` |
| `expected.sortTarget` | string? | Wenn sort | Erwarteter Zielordner |
| `expected.repairSafe` | bool | Ja | Wäre Repair/Rename sicher? |
| `acceptableAlternatives` | object[]? | Nein | Für ambige Fälle: Liste erlaubter alternativer Erwartungen |
| `detectionSources` | string[] | Ja | Welche Detektionsmethoden ein Ergebnis liefern sollten |
| `difficulty` | enum | Ja | `easy`, `medium`, `hard`, `adversarial` |
| `tags` | string[] | Ja | Filterbare Tags |
| `addedInVersion` | string | Ja | SemVer der Ground-Truth-Version |
| `lastVerified` | string | Ja | ISO-Datum der letzten manuellen Verifikation |
| `notes` | string | Nein | Erklärung, besonders für schwierige Fälle |

### 5.4 Ambiguität modellieren

Für Fälle, bei denen mehrere Ergebnisse akzeptabel sind:

```jsonl
{
  "id": "ec-gb-gbc-dual-001",
  "path": "edge-cases/gb-gbc/Pokemon Red (CGB-dual).gb",
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

Der Evaluator prüft:
1. Stimmt `actual` mit `expected` überein? → CORRECT
2. Wenn nicht: stimmt `actual` mit einem `acceptableAlternatives`-Eintrag überein? → ACCEPTABLE (wird als CORRECT gezählt)
3. Wenn beides nicht: → WRONG

### 5.5 Sonderfälle kommentieren

```jsonl
{
  "id": "ec-folder-vs-header-001",
  "path": "edge-cases/folder-conflict/SNES/ActuallyNES.nes",
  "expected": {
    "consoleKey": "NES"
  },
  "notes": "NES-ROM in SNES-Ordner. Header (iNES @ 0x00) hat Vorrang vor Folder. Conflict-Flag MUSS gesetzt sein."
}
```

---

## 6. Ordner- und Dateistruktur

### 6.1 Empfohlene Struktur

```
benchmark/
├── README.md                           ← Überblick, Pflegeregeln, Lizenzhinweis
├── manifest.json                       ← Metadaten: Version, Datum, Statistiken
│
├── ground-truth/
│   ├── ground-truth.schema.json        ← JSON-Schema für Validierung
│   ├── golden-core.jsonl               ← Ground Truth pro Dataset-Klasse (je eigene Datei)
│   ├── golden-realworld.jsonl
│   ├── chaos-mixed.jsonl
│   ├── edge-cases.jsonl
│   ├── negative-controls.jsonl
│   ├── repair-safety.jsonl
│   ├── dat-coverage.jsonl
│   └── performance-scale.jsonl         ← Nur Metadaten; Daten generiert
│
├── samples/
│   ├── golden-core/
│   │   ├── cartridge-headers/          ← NES, SNES, N64, GBA, GB, GBC, MD, 32X, Lynx, 7800
│   │   ├── disc-headers/              ← PS1, PS2, PSP, GC, Wii, SAT, DC, 3DO, SCD
│   │   ├── unique-extensions/         ← .nes, .sfc, .z64, .gba, .gb, .gbc, .smd, .pce, etc.
│   │   ├── folder-names/             ← Ordner mit Aliases + leere Dateien darin
│   │   ├── bios/                     ← BIOS-Stubs
│   │   ├── junk-tags/                ← Demo, Beta, Proto etc.
│   │   ├── serial-keywords/          ← SLUS-xxxxx, [PS1], [GBA] etc.
│   │   ├── dat-hashes/              ← Stubs mit bekannten Hashes
│   │   ├── archives/                ← ZIP-Stubs
│   │   └── negative/               ← .txt, .jpg, leere Dateien
│   │
│   ├── golden-realworld/
│   │   ├── no-intro-style/          ← No-Intro-Benennungskonvention
│   │   ├── redump-style/            ← Redump-Benennungskonvention
│   │   ├── folder-sorted/           ← Sortierte Ordnerstruktur
│   │   ├── flat-mixed/              ← Alles in einem Ordner
│   │   ├── multi-disc/              ← CUE+BIN, GDI, M3U, CCD
│   │   ├── regions/                 ← EU/US/JP/WORLD-Varianten
│   │   ├── revisions/               ← Rev A, v1.1, [!]
│   │   └── bios-in-context/         ← BIOS neben Spielen
│   │
│   ├── chaos-mixed/
│   │   ├── renamed/                 ← Falsch benannte ROMs
│   │   ├── truncated-names/         ← Gekürzte Namen
│   │   ├── wrong-extension/         ← Falsche Extensions
│   │   ├── unicode/                 ← Unicode-Dateinamen
│   │   ├── special-chars/           ← Sonderzeichen
│   │   ├── inconsistent-tags/       ← Gemischte Tags
│   │   ├── flat-no-context/         ← Ohne Ordner-Kontext
│   │   ├── mixed-archives/          ← Gemischte Archive
│   │   ├── corrupt-archives/        ← Kaputte Archive
│   │   ├── duplicates/              ← Doppelte Dateien
│   │   ├── headerless/              ← Headerless ROMs
│   │   └── partial-corrupt/         ← Teilweise beschädigte Dateien
│   │
│   ├── edge-cases/
│   │   ├── cross-system-names/
│   │   ├── ps1-ps2-serial/
│   │   ├── gb-gbc-cgb/
│   │   ├── genesis-32x/
│   │   ├── bios-like-games/
│   │   ├── multi-disc-sets/
│   │   ├── headerless-with-ext/
│   │   ├── mixed-content-archives/
│   │   ├── dat-collision/
│   │   ├── no-dat-vs-dat/
│   │   ├── snes-bimodal/
│   │   ├── wrong-folder/
│   │   └── region-ambiguity/
│   │
│   ├── negative-controls/
│   │   ├── non-rom-types/
│   │   ├── misleading-names/
│   │   ├── junk-demo-beta/
│   │   ├── empty-corrupt/
│   │   ├── fake-extension/
│   │   └── homebrew-hack/
│   │
│   ├── repair-safety/
│   │   ├── dat-exact-high-conf/
│   │   ├── dat-exact-low-conf/
│   │   ├── no-dat-high-conf/
│   │   ├── no-dat-low-conf/
│   │   ├── conflict-with-dat/
│   │   ├── weak-lookupany/
│   │   ├── multi-dat-hit/
│   │   ├── folder-only/
│   │   └── single-source-low/
│   │
│   └── dat-coverage/
│       ├── no-intro-verified/
│       ├── redump-verified/
│       ├── hash-collision/
│       ├── hash-miss/
│       ├── container-vs-content/
│       ├── chd-embedded-sha1/
│       └── dat-format-variants/
│
├── dats/                              ← Test-DAT-Dateien (Mini-Subsets)
│   ├── test-nointro-nes.xml
│   ├── test-nointro-snes.xml
│   ├── test-nointro-gba.xml
│   ├── test-redump-ps1.xml
│   ├── test-redump-ps2.xml
│   └── test-collision.xml             ← DAT mit absichtlichen Cross-System-Hashes
│
├── generators/                        ← Stub-Generierungs-Skripte
│   ├── StubGenerator.cs               ← Erzeugt alle Samples aus Ground Truth
│   ├── ScaleDatasetGenerator.cs       ← Erzeugt performance-scale-Set
│   └── DatSubsetExtractor.cs          ← Extrahiert Mini-DATs aus echten DATs
│
├── baselines/                         ← Gespeicherte Referenz-Ergebnisse
│   ├── v1.0.0-baseline.json           ← Erste Baseline
│   └── latest-baseline.json           ← Symlink oder Kopie der aktuellsten
│
└── reports/                           ← Generierte Benchmark-Reports
    ├── .gitignore                     ← reports/ wird NICHT committed
    └── (generierte JSON/HTML/CSV)
```

### 6.2 Begründung der Struktur

| Entscheidung | Begründung |
|-------------|------------|
| Ground Truth pro Dataset-Klasse separiert | Unabhängig pflegbar; PRs können gezielt eine Klasse ändern |
| Samples nach Klasse → Unterklasse | Klare Zuordnung; Benchmark-Runner kann Klassen selektiv laden |
| Generators im Repo | Reproduzierbare Stub-Erzeugung; `StubGenerator` liest Ground Truth und erzeugt Stubs |
| Test-DATs als Mini-Subsets | Keine echten Full-DATs im Repo; nur relevante Ausschnitte |
| Baselines versioniert | Trend-Vergleich zwischen Releases |
| Reports gitignored | Nur lokal oder als CI-Artefakt; nicht im Repo |

### 6.3 Warum Stub-Generation statt statische Dateien?

Der Benchmark-Datensatz besteht (zum Grossteil) aus **generierten** Stub-Dateien, nicht aus manuell erstellten Binärdateien. Das hat drei Gründe:

1. **Copyright:** Echte ROMs dürfen nicht committed werden. Synthetische Stubs mit korrekten Header-Bytes, aber ohne lauffähigen Inhalt, sind copyright-sicher.
2. **Reproduzierbarkeit:** `StubGenerator` kann den gesamten Datensatz aus Ground Truth + Generierungsregeln deterministisch erzeugen. Kein „funktioniert nur auf meinem Rechner".
3. **Wartbarkeit:** Statt 1.500 Binärdateien zu pflegen, pflegt man die Ground-Truth-JSONL und die Generator-Logik.

**Ablauf:**
```
ground-truth/*.jsonl → StubGenerator.cs → samples/**/*
```

Der Generator wird:
- beim ersten Checkout ausgeführt (oder als Test-Setup)
- als Teil von `dotnet test` bei Bedarf ausgeführt (lazy generation)
- als CI-Step vor dem Benchmark-Run ausgeführt

---

## 7. Versionierung und Pflegeprozess

### 7.1 Versionierung

| Element | Strategie |
|---------|-----------|
| Ground-Truth-JSONL | Git-tracked, jede Änderung = Commit mit Reason |
| manifest.json | Enthält `version` (SemVer), `sampleCount`, `lastModified` |
| Baselines | Git-tracked unter `baselines/`, benannt nach Version |
| Samples | Generiert, NICHT committed (`.gitignore`); Generator ist committed |
| Reports | Generiert, NICHT committed; CI-Artefakt |

**SemVer-Regeln für Ground Truth:**
- **MAJOR:** Strukturelle Änderung am Schema (neues Pflichtfeld, umbenanntes Feld)
- **MINOR:** Neue Samples hinzugefügt (neue Testfälle, neue Klasse)
- **PATCH:** Korrektur bestehender Ground-Truth-Werte (Bugfix im erwarteten Wert)

### 7.2 Aufnahme neuer Samples

```
Prozess: Neues Sample aufnehmen

1. Bug oder Feature identifiziert, der/das ein neues Testmuster braucht
2. Ground-Truth-Eintrag als JSONL-Zeile formulieren
   - id, path, set, subclass, expected, tags, difficulty
   - stub-Info (generator, headerBytes, sizeBytes) definieren
3. Generator-Regel hinzufügen (wenn neuer Generator-Typ nötig)
4. Lokal ausführen: StubGenerator → Sample erzeugen → Benchmark-Run
5. Ergebnis prüfen: Stimmt tatsächliches Verhalten mit Erwartung überein?
   - Wenn ja: Ground Truth ist validiert
   - Wenn nein (Test soll scheitern): Ground Truth dokumentiert gewünschtes Verhalten → Red-Test-Muster
6. Pull Request mit:
   - Geänderte JSONL-Datei
   - Ggf. neuer Generator-Code
   - Begründung im PR-Beschreibungstext
7. Code Review: Mindestens 1 Reviewer prüft Ground-Truth-Korrektheit
8. Merge → manifest.json Version-Bump (MINOR)
```

### 7.3 Änderung bestehender Ground Truth

```
WARNUNG: Bestehende Ground Truth ändern ist ein privilegierter Vorgang.

Erlaubt:
- Korrektur eines nachweislich falschen erwarteten Werts (PATCH)
- Erweiterung um acceptableAlternatives (PATCH)
- Aktualisierung von lastVerified (PATCH)

Verboten ohne Review:
- Löschen von Samples
- Ändern von expected.consoleKey
- Ändern von expected.sortingDecision
- Ändern von expected.repairSafe

Jede Ground-Truth-Änderung MUSS enthalten:
- Welcher Wert sich ändert (alt → neu)
- Warum der alte Wert falsch war
- Welche Quelle den neuen Wert belegt
```

### 7.4 Historische Vergleichbarkeit

```
Regel: Baselines werden NIE überschrieben.

Ablauf:
1. Neuer Release (z.B. v1.3.0) → Benchmark-Run gegen aktuelle Ground Truth
2. Ergebnis wird als baselines/v1.3.0-baseline.json gespeichert
3. baselines/latest-baseline.json wird aktualisiert
4. CI vergleicht jeden PR gegen latest-baseline.json
5. Trend-Dashboard zeigt Verlauf über alle gespeicherten Baselines

Wenn Ground Truth sich ändert:
- Alle Baselines werden NICHT retroaktiv angepasst
- Stattdessen: manifest.json enthält groundTruthVersion
- Vergleiche sind nur innerhalb gleicher groundTruthVersion gültig
- Bei Major-Änderung: neue Baseline-Serie beginnen
```

---

## 8. Nutzung im Projekt

### 8.1 Einbindung nach Testart

| Testart | Dataset-Klasse | Trigger | Erwartung |
|---------|---------------|---------|-----------|
| **Unit Tests** | `golden-core` (Subset) | Jeder Build | 100 % Pass, < 5s |
| **Integration Tests** | `golden-core` + `edge-cases` | Jeder Build | 100 % Pass, < 30s |
| **Regression Tests** | `golden-core` + `golden-realworld` + `edge-cases` | Jeder PR | Keine Regression vs. Baseline |
| **Benchmark Run** | Alle Klassen ausser `performance-scale` | Nightly / Release | Metriken-Report + Trend |
| **Performance Tests** | `performance-scale` | Nightly | Throughput-Regression < 10 % |
| **Manuelle QA** | `chaos-mixed` + `repair-safety` | Vor Release | Review der UNKNOWN/WRONG-Fälle |
| **Bug-Reproduktion** | Neuer Testfall in passender Klasse | Bei Bug-Report | Red→Green-Nachweis |
| **Golden-File-Tests** | Snapshot-Vergleich der Benchmark-Outputs | Jeder PR | Keine unbeabsichtigte Änderung |

### 8.2 CI/CD-Integration

```
CI Pipeline (pro PR):

1. [Build] dotnet build
2. [Generate] StubGenerator erzeugt Samples aus Ground Truth
3. [Unit+Integration] dotnet test --filter "Category=BenchmarkUnit|BenchmarkIntegration"
   → golden-core + edge-cases, < 30s
4. [Regression] EvaluationRunner gegen golden-core + golden-realworld + edge-cases
   → Vergleich gegen latest-baseline.json
   → FAIL wenn: Wrong Match Rate ↑ > 0.1%, Unsafe Sort Rate ↑ > 0.1%
   → WARN wenn: Safe Sort Coverage ↓ > 2%, Unknown Rate ↑ > 3%
5. [Report] benchmark-results.json als CI-Artefakt speichern

Nightly:

6. [Full Benchmark] Alle Dataset-Klassen
7. [Performance] performance-scale mit Timing
8. [Trend] Vergleich gegen alle gespeicherten Baselines
9. [HTML Report] Menschenlesbarer Bericht
```

### 8.3 xUnit-Integration

```csharp
// Beispiel: Golden-Core als Theory-Data

public class BenchmarkTests
{
    [Theory]
    [BenchmarkData("golden-core")]
    public void GoldenCore_ConsoleDetection_MatchesGroundTruth(BenchmarkSample sample)
    {
        // Arrange
        var detector = CreateDetector();
        
        // Act
        var result = detector.DetectWithConfidence(sample.FullPath, sample.RootPath);
        
        // Assert
        if (sample.Expected.ConsoleKey is null)
            Assert.Equal("UNKNOWN", result.ConsoleKey);
        else
            Assert.Equal(sample.Expected.ConsoleKey, result.ConsoleKey);
    }
    
    [Theory]
    [BenchmarkData("edge-cases", acceptAlternatives: true)]
    public void EdgeCases_ConsoleDetection_MatchesGroundTruthOrAlternative(BenchmarkSample sample)
    {
        var result = detector.DetectWithConfidence(sample.FullPath, sample.RootPath);
        Assert.True(sample.IsAcceptable(result.ConsoleKey),
            $"Expected {sample.Expected.ConsoleKey} or alternatives, got {result.ConsoleKey}");
    }
}
```

### 8.4 Bug-Reproduktion

```
Wenn ein Bug gemeldet wird (z.B. "PS2-ROM wird als PS1 erkannt"):

1. Neuen Ground-Truth-Eintrag erstellen:
   set: edge-cases, subclass: ps1-ps2-serial
   expected.consoleKey: "PS2"
   difficulty: hard
   notes: "Bug #42: SLUS-20001 wurde als PS1 erkannt"

2. Stub erzeugen (manuell oder Generator)

3. Test lokal ausführen → muss FAIL (Red Phase)

4. Fix implementieren

5. Test lokal ausführen → muss PASS (Green Phase)

6. PR mit: Ground-Truth-Eintrag + Stub-Generator + Fix

Der Testfall bleibt dauerhaft im Set → Regression verhindert.
```

---

## 9. Risiken und Anti-Patterns

### 9.1 Wie das Testset unbrauchbar werden kann

| Anti-Pattern | Symptom | Konsequenz |
|-------------|---------|-----------|
| **Perfekt-Bias** | >80 % der Samples sind saubere, korrekt benannte ROMs | Chaos-Schwächen unsichtbar |
| **Happy-Path-Mono** | Nur Systeme mit Header-Detection getestet | Extension-only-Systeme ungeprüft |
| **Plattform-Mono** | >50 % NES + SNES | Saturn/DC/3DO-Fehler unsichtbar |
| **Leaky Benchmark** | Erkennungslogik wird gegen das Benchmark-Set optimiert | Overfitting – funktioniert nur für Testdaten |
| **Tote Samples** | Samples, deren Header-Format nicht mehr der aktuellen Detection-Logik entspricht | Falsche Confidence in Ergebnissen |
| **Ground-Truth-Drift** | Expected-Werte veralten nach Codeänderungen | Falsch-positive Tests |
| **Unkontrolliertes Wachstum** | Samples ohne Review, ohne Ground Truth, „schnell noch dazu" | Rauschen statt Signal |
| **Alibi-Negatives** | Nur offensichtliche Negativ-Fälle (.txt, .jpg) | „Fast-ROM"-Fälle (z.B. zufällige Bytes mit .nes-Extension) fehlen |

### 9.2 Gegenmaßnahmen

| Risiko | Gegenmaßnahme |
|--------|---------------|
| Perfekt-Bias | **Pflicht-Quoten:** Min. 25 % der Samples in chaos-mixed + edge-cases + negative-controls. CI prüft Verteilung. |
| Happy-Path-Mono | **Konsolen-Coverage-Metrik:** Jedes System mit >3 Samples muss in mindestens 2 Dataset-Klassen vorkommen |
| Plattform-Mono | **Tier-Quoten:** Tier-1 max 60 %, Tier-2 min 20 %, Tier-3+4 min 10 % der golden-realworld-Samples |
| Leaky Benchmark | **Split-Prinzip:** Benchmark-Set ist READ-ONLY für Entwickler. Neue Detektionsregeln dürfen nicht gegen Benchmark-Samples getunt werden. Stattdessen: separate Dev-Fixtures für Entwicklung, Benchmark nur für Messung. |
| Tote Samples | **Jährliche Revalidierung:** lastVerified aktualisieren; Samples ohne Aktualisierung nach 12 Monaten werden markiert |
| Ground-Truth-Drift | **CI-Validierung:** Jeder Build prüft, dass Ground Truth Schema-valide ist und alle referenzierten Stubs existieren |
| Unkontrolliertes Wachstum | **Gate:** Jedes neue Sample braucht PR + Review + Ground-Truth-Eintrag |
| Alibi-Negatives | **Pflicht-Negatives:** Min. 10 % der Samples müssen Negativ-Kontrollen sein |

### 9.3 Datensatz-Audit (jährlich)

```
Prüfpunkte:
□ Anteil perfect vs chaos vs edge vs negative → Zielverhältnis 40:25:20:15
□ Konsolen-Abdeckung → alle Tier-1/2 mit min. Samples?
□ lastVerified < 12 Monate für alle Samples?
□ Keine Samples ohne Ground Truth?
□ Keine Stubs mit veralteten Header-Formaten?
□ Performance-Scale-Generator aktuell?
□ Threat-Modell: neue Verwechslungspaare identifiziert?
```

---

## 10. Konkrete nächste Schritte

### Phase 1: Foundation (Priorität 1 – blockt alles Weitere)

| # | Schritt | Deliverable | Aufwand |
|---|---------|-------------|---------|
| 1 | **Ground-Truth-Schema definieren** | `benchmark/ground-truth/ground-truth.schema.json` | Klein |
| 2 | **StubGenerator-Grundgerüst** | `benchmark/generators/StubGenerator.cs` – erzeugt Cartridge-Header-Stubs aus JSONL | Mittel |
| 3 | **golden-core Cartridge-Stubs** | 30 Samples für NES, SNES, N64, GBA, GB, GBC, MD, 32X, Lynx, 7800 | Mittel |
| 4 | **golden-core Unique-Extension-Stubs** | 40 Samples (leere Dateien mit korrekter Extension) | Klein |
| 5 | **Erste Ground Truth** | `benchmark/ground-truth/golden-core.jsonl` mit 70 Einträgen | Mittel |

### Phase 2: Runner & Messung (Priorität 2)

| # | Schritt | Deliverable | Aufwand |
|---|---------|-------------|---------|
| 6 | **EvaluationRunner** | xUnit-basierter Runner, der Ground Truth lädt und DetectWithConfidence ausführt | Mittel |
| 7 | **GroundTruthComparator** | Vergleicht Actual vs Expected, erzeugt Verdict pro Sample | Mittel |
| 8 | **Erste Baseline messen** | `benchmark/baselines/v1.0.0-baseline.json` | Klein |
| 9 | **CI-Gate einbauen** | Regression-Gate: Wrong Match Rate ≤ Baseline + 0.1 % | Klein |
| 10 | **golden-core auf 200 erweitern** | + Disc-Header, BIOS, Junk, Serial, Archive, Negative | Mittel |

### Phase 3: Breite (Priorität 3 – iterativ)

| # | Schritt | Aufwand |
|---|---------|---------|
| 11 | golden-realworld aufbauen (500 Samples) | Groß |
| 12 | chaos-mixed aufbauen (300 Samples) | Groß |
| 13 | edge-cases aufbauen (150 Samples) | Mittel |
| 14 | Confusion-Matrix-Generator | Mittel |
| 15 | HTML-Benchmark-Report | Mittel |

### Phase 4: Reife (Priorität 4)

| # | Schritt | Aufwand |
|---|---------|---------|
| 16 | repair-safety aufbauen (100 Samples) | Mittel |
| 17 | dat-coverage aufbauen (150 Samples) | Mittel |
| 18 | performance-scale Generator | Mittel |
| 19 | Trend-Dashboard über Baselines | Mittel |
| 20 | Datensatz-Audit-Prozess formalisieren | Klein |

---

## Anhang A: Konsolen-Abdeckungsmatrix (69 Systeme)

Die folgende Matrix definiert, welche Systeme in welcher Dataset-Klasse mindestens vertreten sein müssen:

| Tier | Systeme | golden-core | golden-realworld | chaos-mixed | edge-cases |
|------|---------|-------------|-----------------|-------------|------------|
| **1** | NES, SNES, N64, GBA, GB, GBC, MD, PS1, PS2 | ✓ (20+ je) | ✓ (20+ je) | ✓ (5+ je) | ✓ (3+ je) |
| **2** | PSP, SAT, DC, GC, Wii, 32X, Lynx, 7800, PSVita, PS3 | ✓ (5+ je) | ✓ (10+ je) | ✓ (2+ je) | ✓ (2+ je) |
| **3** | 2600, 5200, Amiga, MSX, PCE, TG16, Coleco, Vectrex, NGP, WS, Jaguar | ✓ (2+ je) | ✓ (5+ je) | – | – |
| **4** | 3DO, CD32, PCFX, JAGCD, NEOCD, FMTOWNS, CDI, SCD | ✓ (1+ je) | ✓ (3+ je) | – | – |

## Anhang B: Header-Stub-Generator-Referenz

Für `StubGenerator.cs` – welche Bytes pro System:

| System | Generator-ID | Bytes | Offset | Extra |
|--------|-------------|-------|--------|-------|
| NES | `nes-ines` | `4E 45 53 1A` | 0x00 | Min 16 Bytes |
| N64-BE | `n64-be` | `80 37 12 40` | 0x00 | Min 64 Bytes |
| N64-BS | `n64-bs` | `37 80 40 12` | 0x00 | Min 64 Bytes |
| N64-LE | `n64-le` | `40 12 37 80` | 0x00 | Min 64 Bytes |
| GBA | `gba-logo` | `24 FF AE 51` | 0x04 | Min 64 Bytes |
| GB | `gb-dmg` | `CE ED 66 66` @ 0x104, `00` @ 0x143 | 0x104 | Min 0x150 Bytes |
| GBC-Dual | `gbc-dual` | `CE ED 66 66` @ 0x104, `80` @ 0x143 | 0x104 | Min 0x150 Bytes |
| GBC-Only | `gbc-only` | `CE ED 66 66` @ 0x104, `C0` @ 0x143 | 0x104 | Min 0x150 Bytes |
| MD | `md-genesis` | `"SEGA MEGA DRIVE"` (16B padded) | 0x100 | Min 0x120 Bytes |
| 32X | `32x-sega` | `"SEGA 32X"` (8B padded to 16) | 0x100 | Min 0x120 Bytes |
| SNES-Lo | `snes-lorom` | 21-byte title + valid checksum @ 0x7FDC | 0x7FC0 | Min 0x8000 Bytes |
| SNES-Hi | `snes-hirom` | 21-byte title + valid checksum @ 0xFFDC | 0xFFC0 | Min 0x10000 Bytes |
| Lynx | `lynx-header` | `4C 59 4E 58` | 0x00 | Min 4 Bytes |
| 7800 | `atari7800` | `"ATARI7800"` (9B) | 0x01 | Min 10 Bytes |
| GC | `gc-magic` | `C2 33 9F 3D` | 0x1C | Min 80 Bytes |
| Wii | `wii-magic` | `5D 1C 9E A3` | 0x18 | Min 28 Bytes |
| 3DO | `3do-opera` | `01 5A 5A 5A 5A 5A 01` | 0x00 | Min 50 Bytes |
| PS1-PVD | `ps1-pvd` | `01 "CD001"` + SysID "PLAYSTATION" | 0x8000 | Min 0x8030 Bytes |
| PS2-PVD | `ps2-pvd` | `01 "CD001"` + Boot "BOOT2=" | 0x8000 | Min 0x8030 Bytes |
| PSP-PVD | `psp-pvd` | `01 "CD001"` + "PSP_GAME" | 0x8000 | Min 0x8030 Bytes |
| SAT-IPBIN | `sat-ipbin` | `"SEGA SATURN"` text | 0x00 | Min 48 Bytes |
| DC-IPBIN | `dc-ipbin` | `"SEGA SEGAKATANA"` text | 0x00 | Min 48 Bytes |
| SCD-IPBIN | `scd-ipbin` | `"SEGADISCSYSTEM"` text | 0x00 | Min 48 Bytes |
| Empty | `empty-file` | (keine Bytes) | – | 0 Bytes |
| Random | `random-bytes` | Zufällig | – | Konfigurierbar |

## Anhang C: Verteilungsziele (Anti-Bias)

```
Gesamtes Benchmark-Set (ohne performance-scale):

Minimal-Verteilung:
├── golden-core:        200 Samples  (15 %)
├── golden-realworld:   500 Samples  (37 %)
├── chaos-mixed:        300 Samples  (22 %)
├── edge-cases:         150 Samples  (11 %)
├── negative-controls:  100 Samples  (7 %)
├── repair-safety:      100 Samples  (7 %)
└── dat-coverage:       150 Samples  (11 %)
                        ──────────────
                        ~1.500 Total

Schwierigkeitsverteilung:
├── easy:        40 %  (Basis-Erkennung muss funktionieren)
├── medium:      30 %  (realistische Praxis-Schwierigkeit)
├── hard:        20 %  (bekannte Problemfälle)
└── adversarial: 10 %  (absichtlich böse Inputs)

Konsolen-Verteilung:
├── Tier-1 (NES..PS2):  max 55 % der non-golden-core Samples
├── Tier-2 (PSP..7800): min 20 %
├── Tier-3 (2600..WS):  min 10 %
└── Tier-4 (3DO..CDI):  min 5 %
```

## Anhang D: Beziehung zum Metrik-Dokument

Dieses Testset-Design ist die **Datengrundlage** für die in [RECOGNITION_QUALITY_BENCHMARK.md](RECOGNITION_QUALITY_BENCHMARK.md) definierten Metriken:

| Metrik | Benötigtes Dataset |
|--------|-------------------|
| Console Precision/Recall/F1 (M1-M3) | golden-realworld + chaos-mixed |
| Wrong Match Rate (M4) | Alle Klassen |
| Unknown Rate (M5) | Alle Klassen |
| False Confidence Rate (M6) | edge-cases + chaos-mixed |
| Unsafe Sort Rate (M7) | repair-safety + golden-realworld |
| Safe Sort Coverage (M8) | golden-realworld |
| Category Confusion Rate (M9) | golden-core + negative-controls |
| Console Confusion Rate (M10) | edge-cases (cross-system) |
| DAT Exact Match Rate (M11) | dat-coverage |
| DAT Weak Match Rate (M12) | dat-coverage (hash-collision) |
| Ambiguous Match Rate (M13) | edge-cases + chaos-mixed |
| Repair-Safe Match Rate (M14) | repair-safety |
| UNKNOWN→WRONG Migration (M15) | Trend-Vergleich alle Klassen |
| Confidence Calibration Error (M16) | Alle Klassen (pro Confidence-Bucket) |
