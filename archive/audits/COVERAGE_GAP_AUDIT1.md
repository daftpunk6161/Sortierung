# Coverage Gap Audit & Minimum Coverage Matrix – RomCleanup

> **Version:** 2.0.0  
> **Status:** Verbindlich  
> **Aktualisiert:** 2026-03-23  
> **Erstellt:** 2026-03-20  
> **Bezug:** [GROUND_TRUTH_SCHEMA.md](GROUND_TRUTH_SCHEMA.md), [TESTSET_DESIGN.md](TESTSET_DESIGN.md), [RECOGNITION_QUALITY_BENCHMARK.md](RECOGNITION_QUALITY_BENCHMARK.md), [DATASET_AUDIT_PROCESS.md](DATASET_AUDIT_PROCESS.md)

---

## 0. Aktueller Stand (2026-03-23)

### Dataset-Größe: 2.073 Entries

| Set | Ist | Ziel (ADR-017) | Status |
|-----|-----|----------------|--------|
| golden-core | 648 | 400 | ✅ Übertroffen |
| golden-realworld | 493 | 350 | ✅ Übertroffen |
| chaos-mixed | 204 | 200 | ✅ Erreicht |
| edge-cases | 198 | 200 | ⚠️ Knapp (-2) |
| negative-controls | 80 | 80 | ✅ Erreicht |
| repair-safety | 100 | 100 | ✅ Erreicht |
| dat-coverage | 350 | 200 | ✅ Übertroffen |
| performance-scale | 0 | 5.000+ | ❌ Leer (generiert) |
| holdout | 5 | 200 | ⚠️ Seed nur |
| **Gesamt (manuell)** | **2.073** | **1.530** | ✅ **+35%** |

### Was seit v1.0.0 geschehen ist (Phase A–C)

- **+903 neue Entries** (von 1.170 → 2.073)
- **65/65 Systeme abgedeckt** (Lücke: 0, zuvor 17 fehlend)
- **26 neue Generator-Methoden** in DatasetExpander
- **Schema P1-Felder** implementiert (gameIdentity, discNumber, repairSafe, primaryMethod, expectedConfidence, archiveType)
- **Holdout-Zone** mit 5 Seed-Entries und HoldoutEvaluator
- **Stub-Realismus** L1/L2/L3 implementiert
- **M16 ECE Calibration** implementiert
- **Multi-File-Set-Generatoren** (CUE+BIN, GDI+Track)
- **Directory-based Games** (Wii U, 3DS, DOS)
- **TOSEC-DAT-Abdeckung** ≥10 Entries
- **Coverage Gates** (50 Tests, alle grün)

### Geschlossene Lücken

| # | Ehemaliger Befund (v1.0.0) | Neuer Stand | Status |
|---|---------------------------|-------------|--------|
| 5 | 17 Systeme fehlen ganz | 65/65 abgedeckt | ✅ Geschlossen |
| 6 | golden-core zu dünn (70) | 648 Entries | ✅ Geschlossen |
| 7 | golden-realworld zu klein (200) | 493 Entries | ✅ Geschlossen |
| 1 | BIOS-Fälle zu wenig (~15) | ~60+ über mehrere Sets | ✅ Geschlossen |
| 2 | Arcade zu schwach (80) | ~180+ mit Parent/Clone/Split-Merged | ✅ Geschlossen |
| 3 | Redump Multi-File (~40) | ~100+ mit CUE+BIN, GDI, M3U | ✅ Geschlossen |
| 4 | Computer/PC (100) | ~180+ mit DOS/Amiga/C64/ZX | ✅ Geschlossen |

### Verbleibende offene Lücken

| # | Bereich | Ist | Ziel | Delta | Priorität |
|---|---------|-----|------|-------|-----------|
| 1 | **edge-cases Set** | 198 | 200 | –2 | Niedrig |
| 2 | **performance-scale** | 0 | 5.000+ | –5.000 | Mittel (generiert, kein manueller Aufwand) |
| 3 | **holdout-Zone** | 5 | 200 | –195 | Mittel (schrittweise Erweiterung) |
| 4 | **Cross-System Disc** | ~50 | ≥50 | ~0 | ⚠️ Verifizieren |
| 5 | **Headerless ROMs** | ~30 | ≥40 | ~–10 | Niedrig |
| 6 | **Hybrid-Systeme** | ~60 | ≥80 | ~–20 | Niedrig |

### Metriken-Pipeline Stand

| Metrik | Implementiert | Quality Gate |
|--------|--------------|-------------|
| M1–M3 (Precision/Recall/F1) | ✅ | Informational |
| M4 (Wrong Match Rate) | ✅ | ≤ 0.5% (enforced in CI) |
| M5 (Unknown Rate) | ✅ | ≤ 15% |
| M6 (False Confidence Rate) | ✅ | ≤ 5% (enforced in CI) |
| M7 (Unsafe Sort Rate) | ✅ | ≤ 0.3% (enforced in CI) |
| M8 (Safe Sort Coverage) | ✅ | ≥ 80% |
| M9 (Category Confusion Rate) | ✅ | ≤ 5% |
| M9a (Game-as-Junk) | ✅ | ≤ 0.1% (enforced in CI) |
| M9b (BIOS-as-Game) | ✅ | ≤ 0.5% (enforced in CI) |
| M10 (Console Confusion) | ✅ | ≤ 2% pro Paar |
| M11 (DAT Exact Match) | ✅ | ≥ 90% |
| M13 (Ambiguous Match) | ✅ | ≤ 8% (enforced in CI) |
| M14 (Repair-Safe Rate) | ✅ | ≥ 70% (informational) |
| M15 (UNKNOWN→WRONG Migration) | ✅ | ≤ 2% pro Build-Diff |
| M16 (ECE Calibration) | ✅ | ≤ 10% |

### Infrastruktur-Stand

| Komponente | Status |
|-----------|--------|
| HTML Benchmark Dashboard (D1) | ✅ Implementiert |
| Jährlicher Audit-Prozess (D2) | ✅ Dokumentiert |
| Repair Gate M14 (D3) | ✅ Implementiert (informational) |
| Trend Dashboard (D4) | ✅ Implementiert |
| Cross-Validation Split (D5) | ✅ Implementiert |
| CI Benchmark Workflow | ✅ PR-Gate + Nightly + Baseline-Publish |
| Baseline-Management | ✅ Archivierung + Vergleich |

---

## 1. Executive Verdict

> **Hinweis (2026-03-23):** Dieser Abschnitt beschreibt die **Erstbewertung vom 2026-03-20** vor Phase A–C. Viele der hier beschriebenen Lücken sind inzwischen geschlossen — siehe §0 oben für den aktuellen Stand.

**Reicht die bisherige Abdeckung: BEDINGT JA (zuvor: NEIN).**

Mit 2.073 Entries, 65/65 Systemen und allen Coverage Gates grün ist das Testset jetzt **belastbar für CI-Gates** (Stufe S1 übertroffen). Für volle Production-Benchmark-Qualität (Stufe S2, ~2.500 Entries) fehlen noch ~400 Entries, insbesondere in edge-cases, holdout und performance-scale.

### Hauptprobleme (Erstbewertung, Stand 2026-03-20)

1. ~~**Die Manifest-Statistik ist ein Planungsdokument, kein Testset.**~~ ✅ **Gelöst.** 2.073 reale JSONL-Entries existieren.

2. ~~**Das Schema ist strukturell stark, aber die geplanten Mengen sind in 4 von 5 Plattformfamilien zu schwach.**~~ ⚠️ **Teilweise gelöst.** 2.073 Einträge verteilt auf 65 Systeme → 31,9 pro System statt 1,3. Cartridge und Disc sind gut, Arcade und Computer noch ausbaufähig.

3. ~~**17 Systeme fehlen ganz.**~~ ✅ **Gelöst.** 65/65 Systeme abgedeckt (0 fehlend).

4. ~~**Die gefährlichsten Bereiche sind die dünnsten.**~~ ⚠️ **Verbessert.** Arcade ~180+, BIOS ~60+, Computer ~180+, Redump-Sonderfälle ~100+. Noch nicht auf S2-Niveau, aber CI-belastbar.

### Kurzfazit (aktualisiert 2026-03-23)

Das Testset hat mit Phase A–C die Stufe S1 (Belastbar, ~1.200 Minimum) deutlich übertroffen (2.073 Entries). Alle 20 Fallklassen sind besetzt, alle 65 Systeme abgedeckt, alle Coverage Gates grün. Für Stufe S2 (Production Benchmark, ~2.500) fehlen noch edge-cases (+2), holdout (+195) und performance-scale (generiert). Die Metriken-Pipeline M1–M16 ist vollständig implementiert und CI-integriert.

---

## 2. Wo die aktuelle Abdeckung zu schwach ist

### 2.1 Priorisierte Schwachstellen

| # | Bereich | Geplant | Nötig (Minimum) | Delta | Risiko |
|---|---------|---------|-----------------|-------|--------|
| 1 | **BIOS-Fälle systemübergreifend** | ~15 (in golden-core) | ≥60 | –45 | **KRITISCH** — BIOS-AS-GAME führt zu falscher Dedupe; GAME-AS-BIOS zu Datenverlust |
| 2 | **Arcade (Parent/Clone/BIOS/Split-Merged)** | 80 (global) | ≥180 | –100 | **KRITISCH** — Arcade-Sets sind komplex, 80 Einträge decken nicht einmal die Grundvarianten ab |
| 3 | **Redump Multi-File/Multi-Disc** | ~40 (in golden-realworld) | ≥100 | –60 | **HOCH** — CUE+BIN, GDI, M3U, CCD-Varianten × Disc-Systeme = viele Kombinationen |
| 4 | **Computer/PC-Systeme** | 100 (global) | ≥180 | –80 | **HOCH** — 10 Systeme ohne Header-Detection, Folder/Keyword-Only, höchste false-positive-Rate |
| 5 | **Fehlende 17 Systeme** | 0 | ≥51 (3 pro System) | –51 | **HOCH** — Regressionen an diesen Systemen sind unsichtbar |
| 6 | **golden-core vs. TESTSET_DESIGN-Ziel** | 70 | 200–300 | –130 bis –230 | **HOCH** — CI-schneller Referenztest ist zu dünn für alle Detection-Methoden |
| 7 | **golden-realworld vs. TESTSET_DESIGN-Ziel** | 200 | 500–800 | –300 bis –600 | **HOCH** — Benchmark-Kerndatensatz zu klein für System-Metriken |
| 8 | **Cross-System-Disc-Ambiguität** | ~15 (in edge-cases) | ≥50 | –35 | **HOCH** — PS1↔PS2↔PSP, GC↔Wii, SAT↔DC, SCD↔SAT sind die häufigsten Produktionsfehler |
| 9 | **Headerless ROMs** | ~15 (in chaos/edge) | ≥40 | –25 | **MITTEL** — Fallback-Kette ohne Header → nur Extension/DAT, häufig bei Community-Dumps |
| 10 | **Hybrid-Systeme (PSP/Vita/3DS/Switch/WiiU)** | 40 (global) | ≥80 | –40 | **MITTEL** — Container-artige Formate mit eigenen Erkennungslogiken |

### 2.2 Warum das gefährlich ist

Jeder der oben genannten Bereiche hat eine direkte Auswirkung auf die Metriken M4 (Wrong Match Rate) und M7 (Unsafe Sort Rate) — die beiden Release-Blocker-Metriken. Wenn Arcade, BIOS und Redump-Sonderfälle zu dünn getestet sind, werden die globalen Metriken von den gut getesteten Cartridge-Systemen dominiert. Das erzeugt **Metric Dilution**: Die Global-Rate sieht gut aus, obwohl in den Problembereichen die Fehlerrate unakzeptabel hoch sein könnte.

**Beispiel:** 200 NES-Testfälle mit 99% Precision + 10 Arcade-Testfälle mit 50% Precision = 97% Global Precision. Der User sieht "97% Precision" und denkt, Arcade funktioniert. Es funktioniert nicht.

---

## 3. Bewertung der aktuellen Zahl "69 Systeme"

### 3.1 Ist 69 als Coverage-Ziel sinnvoll?

**Ja und Nein.**

**Ja:** 69 ist die korrekte Systemanzahl, weil `data/consoles.json` genau 69 Systeme definiert. Jedes System, das RomCleanup erkennen kann, MUSS im Testset vertreten sein. Sonst existiert ein Blind Spot.

**Nein:** Die Zahl allein ist bedeutungslos. Ein Testset mit 69 Systemen × 1 Testfall pro System (= 69 Testfälle) wäre wertlos. Die relevante Metrik ist nicht "Systeme abgedeckt", sondern:

- **Fehlermodi pro System abgedeckt** (Header-Match, Extension-Match, Folder-Match, Conflict, Headerless, Archive, BIOS, DAT)
- **Mindesttiefe pro System-Tier** (Tier-1 braucht 20+, Tier-4 braucht ≥3)
- **Fallklassen pro System ausgefüllt** (Referenz, Chaos, Edge, Negative, Repair)

### 3.2 Warum `systemsCovered: 52` ein Problem ist

17 fehlende Systeme sind:

| Fehlend (geschätzt, basierend auf Systemabdeckungs-Matrix) | Plattformklasse | Risiko |
|-------------------------------------------------------------|----------------|--------|
| CHANNELF | cartridge | Niedrig |
| SUPERVISION | cartridge | Niedrig |
| NGPC | cartridge | Mittel (Verwechslung mit NGP) |
| ODYSSEY2 | cartridge | Niedrig |
| POKEMINI | cartridge | Niedrig |
| A52 | cartridge | Niedrig (Verwechslung mit A78, A26) |
| CPC | computer | Mittel (keine UniqueExt) |
| PC98 | computer | Mittel (keine UniqueExt) |
| X68K | computer | Mittel (keine UniqueExt) |
| CDI | disc | Mittel (Disc-Header-Erkennung) |
| WIIU | disc/hybrid | Hoch (directory-based) |
| X360 | disc | Mittel |
| PS3 | disc | Mittel |
| ATARIST | computer | Niedrig (UniqueExt .st/.stx) |
| VB | cartridge | Niedrig |
| WS | cartridge | Niedrig |
| WSC | cartridge | Niedrig |

**3 davon sind Hoch-Risiko** (WIIU, CPC, PC98) weil sie entweder keine UniqueExt haben oder Directory-basiert sind — genau die Fälle, die am ehesten brechen.

### 3.3 Was stattdessen gemessen werden muss

| Metrik | Schwelle | Beschreibung |
|--------|----------|-------------|
| **System Coverage** | 69/69 (100%) | Jedes System in consoles.json hat ≥1 Testfall |
| **Tier-1 Depth** | ≥20 pro System | Top-9 Systeme mit voller Detection haben genug Samples für System-Metriken |
| **Detection-Method Coverage** | 9/9 Methoden × relevante Systeme | Jede Detection-Methode hat Testfälle für jedes System, das sie unterstützt |
| **Fallklassen-Coverage** | 20/20 Klassen besetzt | Alle 20 Pflicht-Fallklassen (siehe §5) haben Mindesteinträge |
| **Ambiguity Coverage** | ≥5 pro bekanntem Verwechslungspaar | GB↔GBC, MD↔32X, PS1↔PS2↔PSP etc. |
| **BIOS System Coverage** | BIOS-Fälle für ≥15 Systeme | BIOS existiert für mehr als nur Arcade |
| **DAT Ecosystem Coverage** | 4/4 Ökosysteme × ≥10 Testfälle | No-Intro, Redump, MAME, TOSEC jeweils ausreichend |

---

## 4. Mindestabdeckung pro Plattformfamilie

### 4.1 Familie A: No-Intro / Cartridge

**Warum wichtig:** Höchstes Volumen in realen Sammlungen (~60–70%). Enthält 10 Systeme mit CartridgeHeader-Detection, ~40 mit UniqueExtension. Verwechslungspaare (GB↔GBC, MD↔32X) sind hier. Headerless-Varianten existieren (NES ohne iNES, SNES ohne Header).

| Attribut | Wert |
|----------|------|
| **Systeme im Scope** | 35 |
| **Systeme Pflicht-Minimum** | 35 (alle) |
| **Tier-1 Systeme (≥20 Testfälle)** | NES, SNES, N64, GBA, GB, GBC, MD, NDS, 3DS, SWITCH |
| **Tier-2 Systeme (≥8 Testfälle)** | 32X, SMS, GG, PCE, LYNX, A78, A26, INTV, JAG, NGP |
| **Tier-3 Systeme (≥3 Testfälle)** | A52, A800, COLECO, VECTREX, SG1000, WS, WSC, VB, POKEMINI, ODYSSEY2, CHANNELF, SUPERVISION, NGPC |
| **Testfälle total (Minimum)** | **380** |

**Pflicht-Unterkategorien:**

| Unterkategorie | Min. | Systeme | Begründung |
|---------------|------|---------|-----------|
| CartridgeHeader richtig erkannt | 30 | 10 Header-Systeme × 3 | Primärer Erkennungsweg |
| CartridgeHeader fehlend (Headerless) | 15 | NES, SNES, GB, GBC, MD | Fallback-Test |
| UniqueExtension richtig | 40 | Alle mit uniqueExt | Grundlagen-Coverage |
| Extension-Conflict (falsche Ext) | 15 | 10 Systeme | .bin statt .nes, .rom statt .gba |
| Folder-Detection korrekt | 20 | 10 Tier-1+2 | FolderAlias-Mapping |
| Folder-vs-Header-Conflict | 10 | 5 Systeme | NES-ROM in SNES-Ordner etc. |
| GB↔GBC CGB-Flag-Varianten | 8 | GB, GBC | 0x00, 0x80, 0xC0, Dual-Mode mit .gb/.gbc |
| MD↔32X Header-Ambiguität | 6 | MD, 32X | "SEGA MEGA DRIVE" vs "SEGA 32X" @ 0x100 |
| SNES LoROM/HiROM/Copier | 6 | SNES | 3 Varianten × 2 |
| N64 Endian-Varianten | 6 | N64 | BE, Byte-Swap, LE × 2 |
| BIOS korrekt als BIOS erkannt | 10 | GBA, NDS, 3DS, etc. | Separate von Spielen |
| DAT-exact-Match | 20 | 10 Systeme | SHA1-Verifikation |
| Junk/Demo/Beta/Hack | 15 | Diverse | Category-Erkennung |
| Negative Controls | 10 | N/A | .txt, .jpg mit ROM-ext |

**Chaos-/Ambiguous-/Negative-Fälle:** ≥60 (in chaos-mixed + edge-cases + negative-controls)

---

### 4.2 Familie B: Redump / Disc

**Warum wichtig:** Enthält die teuersten Fehler — ein falsch sortiertes PS2-ISO kann 4 GB gross sein und enthält oft personalisierte Speicherstände in benachbarten Dateien. Multi-Disc-Sets, CUE+BIN, GDI, M3U und CHD erfordern Set-Integrität. Die PVD-Signatur-Ähnlichkeit zwischen PS1/PS2/PSP ist der häufigste Produktionsfehler.

| Attribut | Wert |
|----------|------|
| **Systeme im Scope** | 22 |
| **Systeme Pflicht-Minimum** | 22 (alle) |
| **Tier-1 Systeme (≥15 Testfälle)** | PS1, PS2, PSP, GC, WII, SAT, DC |
| **Tier-2 Systeme (≥8 Testfälle)** | PS3, SCD, 3DO, PCECD, XBOX, X360, WIIU |
| **Tier-3 Systeme (≥3 Testfälle)** | PCFX, NEOCD, JAGCD, CD32, CDI, FMTOWNS |
| **Testfälle total (Minimum)** | **310** |

**Pflicht-Unterkategorien:**

| Unterkategorie | Min. | Systeme | Begründung |
|---------------|------|---------|-----------|
| DiscHeader korrekt erkannt | 32 | 16 Header-Systeme × 2 | Primärer Erkennungsweg |
| PVD-Signatur-Disambiguation (PS1↔PS2↔PSP) | 15 | PS1, PS2, PSP | Boot-Marker-Unterscheidung, häufigster Produktionsfehler |
| Serial-Number-Detection | 15 | PS1, PS2, PS3, PSP, Vita, GC, SAT | SLUS/SCUS/BCUS/UCUS etc. |
| CUE+BIN korrekt als Set | 10 | PS1, SAT, SCD, DC | Container=multi-file, role=primary/data |
| GDI+Tracks korrekt als Set | 5 | DC | Dreamcast-spezifisch |
| M3U+CHD korrekt als Set | 8 | PS1, PS2, SAT | M3U-Playlist-Sets |
| CHD-RAW-SHA1 für DAT-Match | 8 | PS1, PS2, PSP, SAT, DC | Embedded SHA1 statt Container-Hash |
| Multi-Disc-Zuordnung | 15 | PS1, PS2, SAT, DC, GC | Disc 1/2/3/4 korrekt gruppiert |
| ISO-only (kein CUE) | 10 | PS1, PS2, PSP, XBOX | Häufig bei CSO/ISO-Dumps |
| AmbiguousExtension (.iso/.bin/.chd) | 15 | Alle Disc-Systeme | Single-Match vs. Multi-Match |
| DAT-exact-Match (Redump) | 15 | 8 Systeme | Redump-SHA1-Verifikation |
| Cross-System-Disc-Ambiguität | 20 | PS1↔PS2, PS2↔PSP, GC↔WII, SAT↔SCD, SAT↔DC | Verwechslungspaare |
| BIOS korrekt erkannt | 10 | PS1, PS2, SAT, DC, 3DO, XBOX | Disc-BIOS-Dateien |
| Junk/Demo-Discs | 8 | Diverse | Category-Erkennung |
| Folder-Only-Detection | 8 | Systeme ohne Header (PS3, X360) | Fallback-Erkennung |
| Negative Controls | 5 | N/A | .iso die kein Disc-Image ist |

**Chaos-/Ambiguous-/Negative-Fälle:** ≥70 (in chaos-mixed + edge-cases + negative-controls)

---

### 4.3 Familie C: Arcade

**Warum wichtig:** Arcade ist die komplexeste Plattformfamilie. Parent/Clone-Hierarchien, Shared BIOS (neogeo.zip), Split/Merged/Non-Merged ROM-Sets, CHD-Supplements (naomi.zip + naomi.chd), Device-ROMs — all das existiert in keinem anderen Bereich. Die aktuelle Planung von 80 Einträgen ist **grob unzureichend**. Arcade hat de facto hunderte relevanter Fehlermodi.

| Attribut | Wert |
|----------|------|
| **System-Keys im Scope** | 3 (ARCADE, NEOGEO, NEOCD) |
| **Logische Unterfamilien** | ≥8 (MAME Generic, Neo Geo MVS/AES, Neo Geo CD, CPS1/2/3, Naomi, Atomiswave, Sega System 16, PGM) |
| **Systeme Pflicht-Minimum** | 3 Keys, aber ≥8 logische Unterfamilien als Subclass |
| **Testfälle total (Minimum)** | **200** |

**Pflicht-Unterkategorien:**

| Unterkategorie | Min. | Begründung |
|---------------|------|-----------|
| Parent-Set korrekt erkannt | 20 | Basis-Erkennung |
| Clone-Set korrekt als Clone | 15 | Clone-Beziehung modelliert |
| BIOS-ZIP korrekt als BIOS | 15 | neogeo.zip, naomi.zip, pgm.zip etc. |
| BIOS-Abhängigkeit korrekt modelliert | 10 | Spiel → BIOS Referenz |
| Split-Set (nur eigene ROMs) | 10 | ROM-Set-Variante |
| Merged-Set (Parent+Clones in einem ZIP) | 10 | ROM-Set-Variante |
| Non-Merged-Set (komplett eigenständig) | 10 | ROM-Set-Variante |
| CHD-Supplement (Arcade + CHD) | 8 | Naomi, Atomiswave, MAME mit HDD |
| Device-ROM (nicht spielbar) | 5 | Category=NonGame |
| Neo Geo MVS/AES (FolderName-Varianten) | 10 | neogeo/, neo-geo/, ng/ Aliase |
| Neo Geo CD (Disc-basiert) | 8 | Andere Detection als AES/MVS |
| ARCADE↔NEOGEO Ambiguität | 10 | MAME vs. Standalone-Neo-Geo |
| Cross-MAME-Version-Namenswechsel | 5 | ROM-Name ändert sich zwischen MAME-Versionen |
| Kaputte/Unvollständige ROM-Sets | 8 | Fehlende ROMs im Split-Set |
| DAT-Match (MAME-CRC32) | 15 | CRC32-basiertes Matching |
| Folder-Only-Detection | 10 | "arcade/", "mame/", "fbneo/" |
| Negative Controls | 5 | ZIP das kein Arcade-Set ist |
| Junk / Mahjong / Quiz / Gambling | 6 | NonGame-Arcade-Klassifikation |

**Warum 80 Einträge nicht reichen:** Arcade hat 18 Pflicht-Unterkategorien. 80 / 18 = 4,4 pro Unterkategorie. Bei 3 System-Keys × 8 logischen Unterfamilien × 6 ROM-Set-Varianten × 3 Schwierigkeitsgrade ist die kombinatorische Breite enorm. 200 ist ein Kompromiss — eigentlich bräuchte Arcade 300+.

---

### 4.4 Familie D: Computer / PC / Directory-based

**Warum wichtig:** 10 Systeme ohne Header-Detection. Erkennung funktioniert ausschliesslich über UniqueExtension (Amiga=.adf, C64=.d64, ZX=.tzx) oder FolderName/Keyword (DOS, CPC, PC98, X68K). False-Positive-Rate ist hier am höchsten. 100 Einträge für 10 Systeme = 10 pro System = zu wenig, um Extension-Overlap, Folder-Only und Directory-Based-Szenarien zu testen.

| Attribut | Wert |
|----------|------|
| **Systeme im Scope** | 10 (DOS, AMIGA, C64, ZX, MSX, ATARIST, A800, CPC, PC98, X68K) |
| **Systeme Pflicht-Minimum** | 10 (alle) |
| **Systeme mit UniqueExt (≥8 Testfälle)** | AMIGA, C64, ZX, MSX, ATARIST, A800 |
| **Systeme ohne UniqueExt (≥10 Testfälle)** | DOS, CPC, PC98, X68K |
| **Testfälle total (Minimum)** | **150** |

**Pflicht-Unterkategorien:**

| Unterkategorie | Min. | Systeme | Begründung |
|---------------|------|---------|-----------|
| UniqueExtension korrekt | 18 | 6 Systeme × 3 | Basis-Erkennung |
| FolderName-Only-Detection | 12 | DOS, CPC, PC98, X68K | Einziger Erkennungsweg |
| FilenameKeyword-Detection | 8 | DOS, CPC, PC98 | [DOS], [PC-98] Tags |
| Directory-based Game | 10 | DOS (Ordner mit .EXE), Amiga (WHDLoad) | Container=directory |
| Extension-Conflict (.img, .bin, .iso) | 8 | Amiga (.adf vs .hdf), C64 (.g64 vs .d64) | Overlap-Fälle |
| TOSEC-DAT-Match | 8 | 5 Systeme | TOSEC-Ökosystem-Abdeckung |
| Folder-vs-Keyword-Conflict | 5 | Diverse | Ordner sagt X, Keyword sagt Y |
| Disk-Image-Varianten | 8 | Amiga (ADF/HDF/DMS), C64 (D64/T64/G64), ZX (TZX/TAP/Z80) | Format-Vielfalt |
| BIOS/Firmware korrekt erkannt | 5 | Amiga (Kickstart), C64 (KERNAL) | System-ROMs |
| Confidence < 80 (korrekt blockiert) | 8 | CPC, PC98, X68K | Sort-Block bei niedriger Confidence |
| Junk/Demo/PD-Software | 5 | Diverse | Public-Domain-Software als NonGame |
| Negative Controls | 5 | N/A | .exe die kein DOS-Spiel ist |

**Chaos-/Ambiguous-/Negative-Fälle:** ≥40 (in chaos-mixed + edge-cases + negative-controls)

---

### 4.5 Familie E: Hybrid / Sonderfälle

**Warum wichtig:** PSP (CSO/ISO), Vita (VPK), 3DS (CIA/3DS), Switch (NSP/XCI), Wii U (RPX in Ordner) — diese Systeme sind weder reine Cartridge noch reine Disc-Systeme. Sie haben eigene Container-Formate, teilweise directory-basierte Strukturen, und ihre Erkennung nutzt Mischformen der Detection-Methoden.

| Attribut | Wert |
|----------|------|
| **Systeme im Scope** | 5 (PSP, VITA, 3DS, SWITCH, WIIU) |
| **Systeme Pflicht-Minimum** | 5 (alle) |
| **Testfälle total (Minimum)** | **80** |

**Pflicht-Unterkategorien:**

| Unterkategorie | Min. | Systeme | Begründung |
|---------------|------|---------|-----------|
| Container-Format korrekt | 15 | Alle 5 × 3 | CSO, ISO, VPK, CIA, 3DS, NSP, XCI, RPX, WUX |
| Serial-Detection | 8 | PSP (UCUS), Vita (PCSE), 3DS (CTR) | Filename-Serial-Pattern |
| DiscHeader (PSP PVD) | 5 | PSP | PVD + "PSP GAME" |
| Directory-based (Wii U) | 8 | WIIU | RPX in Ordner-Struktur |
| DAT-Match | 8 | PSP, 3DS, Switch | No-Intro/Redump-DATs |
| FolderName-Detection | 5 | Alle 5 | "psp/", "vita/", "3ds/", "switch/", "wiiu/" |
| Extension-Conflict (.iso) | 5 | PSP (ISO=PS1? PS2? PSP?) | Disambiguierung |
| BIOS korrekt erkannt | 3 | 3DS, WIIU | System-Firmware |
| Update/DLC als NonGame | 5 | Switch (Updates), 3DS (DLC) | Category-Erkennung |
| Negative Controls | 3 | N/A | .nsp die keine Switch-Datei ist |

**Chaos-/Ambiguous-/Negative-Fälle:** ≥20 (in chaos-mixed + edge-cases + negative-controls)

---

## 5. Mindestabdeckung pro Fallklasse

### 5.1 Verbindliche Fallklassen-Matrix

| # | Fallklasse | Beschreibung | Warum Pflicht | Min. total | Familien |
|---|-----------|-------------|--------------|------------|----------|
| FC-01 | **Saubere Referenzfälle** | Korrekt benannte, korrekt strukturierte Dateien mit eindeutiger Erkennung | Baseline für Precision-Messung; Regression-Gate | **120** | Alle 5 |
| FC-02 | **Falsch benannte Dateien** | ROM-Name enthält falsches System, falsche Region, falschen Titel | Häufigster Fehler in Wildsammlungen; testet Filename-Unabhängigkeit | **40** | A, B, C |
| FC-03 | **Header-Konflikte** | CartridgeHeader/DiscHeader widerspricht anderem Signal | Testet HypothesisResolver-Gewichtung | **25** | A (GB↔GBC, MD↔32X, SNES-Varianten), B (PS1↔PS2↔PSP, GC↔Wii) |
| FC-04 | **Extension-Konflikte** | Korrekte ROM mit falscher Extension | Testet Fallback auf Header/DAT wenn Extension lügt | **30** | A, B, D |
| FC-05 | **Folder-vs-Header-Konflikte** | ROM im falschen System-Ordner | Testet Priorisierung: Header > Folder | **20** | A, B |
| FC-06 | **DAT exact matches** | Hash-verifizierte Dateien | Testet DAT-Integration, SHA1/CRC32/MD5-Matching | **60** | A (No-Intro), B (Redump), C (MAME), D (TOSEC) |
| FC-07 | **DAT weak/no/ambiguous matches** | Kein Match, Cross-System-Match, Hash-Collision | Testet LookupAny, Confidence-Degradation bei fehlendem DAT | **40** | A, B, C |
| FC-08 | **BIOS-Fälle** | BIOS korrekt als BIOS erkannt; Spiele nicht fälschlich als BIOS | Verhindert BIOS-AS-GAME (falsche Dedupe) und GAME-AS-BIOS (Datenverlust) | **60** | A, B, C, D, E |
| FC-09 | **Parent/Clone-Fälle** | Arcade Parent/Clone-Hierarchie korrekt modelliert | Arcade-spezifisch, aber kritisch für Set-Integrität | **30** | C |
| FC-10 | **Multi-Disc** | 2–4 Discs korrekt demselben Spiel zugeordnet | Verhindert Set-Zerstörung durch separate Sortierung | **25** | B, E |
| FC-11 | **Multi-File-Sets** | CUE+BIN, GDI+Tracks, M3U+CHD | Verhindert Datenverlust durch unvollständige Set-Verarbeitung | **30** | B |
| FC-12 | **Archive inner-file matching** | ZIP/7z: Inner-Hash statt Container-Hash für DAT | Verhindert systematischen DAT-Miss bei archivierten ROMs | **20** | A, C |
| FC-13 | **Directory-based games** | Ordner-Struktur als Spiel (Wii U, DOS, Amiga WHDLoad) | Nicht-triviale Erkennung; kein einzelner Dateipfad | **15** | D, E |
| FC-14 | **UNKNOWN expected** | Dateien, die korrekt als UNKNOWN bleiben sollen | Stellt sicher, dass UNKNOWN kein Fehler ist, sondern korrekte Unsicherheit | **30** | Alle 5 |
| FC-15 | **Ambiguous acceptable** | Mehrere Ergebnisse korrekt (GB↔GBC, NEOGEO↔ARCADE) | Testet acceptableAlternatives-Evaluation | **25** | A, C |
| FC-16 | **Negative controls** | Dateien, die nie als ROM erkannt werden dürfen | 100% Pass-Rate Pflicht; verhindert False Positives | **40** | Alle 5 |
| FC-17 | **Repair-unsafe / sort-blocked** | Confidence < 80, HasConflict, Category ≠ Game | Testet Confidence-Gating; verhindert Unsafe-Sort | **30** | Alle 5 |
| FC-18 | **Cross-system conflict** | Zwei Systeme mit hoher Confidence | Testet HypothesisResolver-Conflict-Detection + Penalty | **25** | A (GB↔GBC, MD↔32X), B (PS1↔PS2↔PSP), C (ARCADE↔NEOGEO) |
| FC-19 | **Junk / NonGame / Tooling** | Demo, Beta, Proto, Hack, Homebrew, Util, Firmware-Updater | Testet FileClassifier-Vollständigkeit | **25** | Alle 5 |
| FC-20 | **Kaputte/unvollständige Sets** | Truncated Files, fehlende BINs, Halb-Archive, 0-Byte | Stellt sicher, dass kaputte Dateien nicht crashen oder falsch positiv matchen | **20** | A, B, C |

### 5.2 Zusammenfassung Fallklassen

| Metrik | Wert |
|--------|------|
| **Fallklassen total** | 20 |
| **Mindest-Einträge über alle Klassen** | **700** |
| **Davon in golden-core** | ~200 (FC-01, FC-06 Teile, FC-08 Teile, FC-16 Teile) |
| **Davon in edge-cases** | ~180 (FC-03, FC-04, FC-05, FC-15, FC-18) |
| **Davon in chaos-mixed** | ~150 (FC-02, FC-04 Teile, FC-20) |
| **Davon in repair-safety** | ~60 (FC-17) |
| **Davon in dat-coverage** | ~100 (FC-06, FC-07, FC-12) |
| **Davon in negative-controls** | ~70 (FC-14, FC-16) |

---

## 6. BIOS-, Arcade- und Redump-Spezialmatrix

Diese drei Bereiche verdienen gesonderte Übergewichtung, weil:
- **BIOS:** Falsche BIOS-Klassifikation hat kaskadierende Folgen (Dedupe-Fehler, Sortier-Fehler, Emulator-Fehler)
- **Arcade:** Höchste Komplexität, aber typischerweise niedrigste Test-Coverage
- **Redump:** Grösste Dateien, höchster potentieller Datenverlust pro Fehler

### 6.1 BIOS-Pflichtmatrix

| # | BIOS-Szenario | Min. | Systeme | Erwartetes Ergebnis |
|---|--------------|------|---------|---------------------|
| B-01 | BIOS mit `[BIOS]` Tag im Dateinamen | 8 | PS1, PS2, GBA, NDS, DC, SAT, 3DO, XBOX | Category=Bios |
| B-02 | BIOS mit `(BIOS)` Tag | 5 | PS1, GC, Wii | Category=Bios |
| B-03 | BIOS mit Systemnamen (z.B. "PlayStation BIOS") | 5 | PS1, PS2, SAT | Category=Bios |
| B-04 | BIOS ohne explizites Tag (nur DAT-Kennung) | 5 | Diverse | Category=Bios via DAT |
| B-05 | Spiel mit BIOS-ähnlichem Namen | 5 | "BioShock", "BIOS Agent" | Category=Game (nicht fälschlich Bios) |
| B-06 | Arcade Shared BIOS (neogeo.zip, pgm.zip) | 5 | ARCADE, NEOGEO | Category=Bios, biosSystemKeys korrekt |
| B-07 | Amiga Kickstart ROM | 2 | AMIGA | Category=Bios |
| B-08 | C64 KERNAL/BASIC ROM | 2 | C64 | Category=Bios |
| B-09 | 3DS/WiiU System-Firmware | 2 | 3DS, WIIU | Category=Bios |
| B-10 | BIOS neben Spielen im gleichen Ordner | 5 | PS1, SAT, DC | BIOS korrekt klassifiziert trotz Spiel-Nachbarn |
| B-11 | BIOS in Archiv (ZIP mit nur BIOS) | 3 | PS1, NEOGEO | Archive-Inner-BIOS |
| B-12 | Falsches BIOS (korrupte Datei, falscher Hash) | 3 | Diverse | DAT-Miss oder UNKNOWN |
| **Total** | | **≥50** | **≥12 Systeme** | |

**Warum 50 und nicht 15:** RomCleanup hat 5 BIOS-relevante FileClassifier-Patterns. Jedes Pattern muss gegen mehrere Systeme, in verschiedenen Kontexten (allein, neben Spielen, in Archiven, ohne Tag) getestet werden. 15 Fälle decken bestenfalls 3 Patterns × 5 Systeme ab — ohne Negativ-Fälle, ohne Kontext-Varianten.

### 6.2 Arcade-Pflichtmatrix

| # | Arcade-Szenario | Min. | Begründung |
|---|----------------|------|-----------|
| A-01 | MAME Parent-Set (Standard) | 10 | Basis-Erkennung |
| A-02 | MAME Clone-Set (relationships.cloneOf) | 10 | Clone→Parent-Zuordnung |
| A-03 | Neo Geo MVS/AES Parent-Set | 8 | NEOGEO-Key vs. ARCADE |
| A-04 | Neo Geo Clone | 5 | Clone unter NEOGEO |
| A-05 | Neo Geo CD (Disc-basiert) | 8 | Wechsel von Cartridge-artig zu Disc |
| A-06 | Shared BIOS (neogeo.zip) | 5 | Category=Bios + biosSystemKeys |
| A-07 | Shared BIOS (pgm.zip, naomi.zip, etc.) | 5 | Weitere BIOS-Systeme |
| A-08 | Split-ROM-Set | 8 | Nur eigene ROMs im ZIP |
| A-09 | Merged-ROM-Set | 8 | Parent+Clones im ZIP |
| A-10 | Non-Merged-ROM-Set | 8 | Komplett eigenständig |
| A-11 | CHD-Supplement (Arcade+CHD) | 5 | ZIP + .chd nebeneinander |
| A-12 | Device-ROM (nicht spielbar) | 5 | Category=NonGame |
| A-13 | MAME-Versionswechsel-Name | 5 | ROM heisst in MAME 0.264 anders als in 0.260 |
| A-14 | Kaputtes/unvollständiges ROM-Set | 8 | Fehlende ROMs → Erkennung trotzdem? |
| A-15 | ARCADE↔NEOGEO Ambiguität | 8 | acceptableAlternatives |
| A-16 | FolderName-Varianten | 8 | arcade/, mame/, fbneo/, fba/, neogeo/, neo-geo/ |
| A-17 | Junk-Arcade (Mahjong, Quiz, Gambling) | 5 | NonGame oder Junk? |
| A-18 | DAT-Match MAME (CRC32 pro ROM-Chip) | 10 | MAME-spezifisches Hashing |
| A-19 | DAT-Miss (Homebrew-Arcade, Bootleg) | 5 | Kein DAT-Eintrag |
| A-20 | ZIP mit gemischtem Inhalt (Arcade + Non-Arcade) | 4 | Archiv-Robustheit |
| **Total** | | **≥140** | |

**Warum die geplanten 80 nicht reichen:** 80 / 20 Szenarien = 4 pro Szenario. Bei 3 System-Keys × mehreren Varianten pro Szenario bleiben <2 Testfälle pro Key-Szenario-Kombination. Das ist kein Benchmark, das ist ein Rauchtest.

### 6.3 Redump-Pflichtmatrix

| # | Redump-Szenario | Min. | Systeme | Begründung |
|---|----------------|------|---------|-----------|
| R-01 | Single-ISO korrekt erkannt | 14 | 7 Tier-1 × 2 | Basis-Erkennung |
| R-02 | CUE+BIN Single-Track | 8 | PS1, SAT, SCD | Einfachster Multi-File-Fall |
| R-03 | CUE+BIN Multi-Track | 8 | PS1, SAT, DC | Audio+Data-Tracks |
| R-04 | GDI+Tracks | 5 | DC | Dreamcast-spezifisch |
| R-05 | CHD (single disc) | 8 | PS1, PS2, SAT, DC | CHD-Container-Erkennung |
| R-06 | CHD-RAW-SHA1 DAT-Match | 8 | PS1, PS2, PSP, SAT | Embedded SHA1, nicht Container-Hash |
| R-07 | M3U-Playlist + CHD-Set | 6 | PS1, PS2 | M3U referenziert CHDs |
| R-08 | CCD+IMG+SUB | 4 | PS1, SAT | CloneCD-Format |
| R-09 | MDS+MDF | 3 | PS1, PS2 | Alcohol 120%-Format |
| R-10 | Multi-Disc 2 Discs | 6 | PS1, SAT | Grundfall |
| R-11 | Multi-Disc 3-4 Discs | 6 | PS1, PS2 | Grössere Sets |
| R-12 | PS1↔PS2 PVD-Disambiguation | 8 | PS1, PS2 | Boot-Marker "BOOT2=" |
| R-13 | PS2↔PSP PVD-Disambiguation | 6 | PS2, PSP | Boot-Marker "PSP GAME" |
| R-14 | GC↔Wii Magic-Byte-Disambiguation | 6 | GC, Wii | 0xC2339F3D vs 0x5D1C9EA3 |
| R-15 | SAT↔SCD↔DC Header-Disambiguation | 6 | SAT, SCD, DC | SEGASATURN vs SEGADISCSYSTEM vs SEGAKATANA |
| R-16 | CSO-Container (PSP) | 3 | PSP | Compressed ISO |
| R-17 | WIA/RVZ/WBFS (Wii-spezifisch) | 4 | Wii | Wii-Kompressionsformate |
| R-18 | Xbox/X360 ISO-Signatur | 4 | XBOX, X360 | MICROSOFT*XBOX*MEDIA |
| R-19 | DAT-Miss bei seltenen Discs | 5 | PCFX, JAGCD, CD32 | Kein DAT-Eintrag für seltene Systeme |
| R-20 | Disc-BIOS (PS1 BIOS.bin etc.) | 5 | PS1, PS2, SAT, DC | BIOS neben Disc-Spielen |
| **Total** | | **≥143** | | |

---

## 7. Empfohlene Zielgrösse des Testsets

### 7.1 Drei Stufen

| Stufe | Name | Einträge | Systeme | Zweck | Zeitrahmen |
|-------|------|----------|---------|-------|-----------|
| **S1: Belastbar** | Minimum Viable Benchmark | **~1.200** | 69/69 | CI-fähig, alle Fallklassen besetzt, Tier-1 statistisch belastbar | Sofort (Phase 1–3) |
| **S2: Realistisch** | Production Benchmark | **~2.500** | 69/69 | Alle Familien belastbar, System-Metriken für Tier-1+2, Confusion-Matrices sinnvoll | Mittelfristig (Phase 4–6) |
| **S3: Langfristig** | Full Coverage Benchmark | **~5.000+** | 69/69 | Statistisch valide Metriken pro System, Performance-Scale-Tests, Trend-Analyse | Langfristig |

### 7.2 Stufe S1 — Aufschlüsselung

| Dataset-Klasse | GROUND_TRUTH_SCHEMA.md geplant | TESTSET_DESIGN.md Ziel | **S1 Minimum** | Begründung |
|---------------|-------------------------------|----------------------|---------------|-----------|
| golden-core | 70 | 200–300 | **250** | Muss alle Detection-Methoden × relevante Systeme abdecken |
| golden-realworld | 200 | 500–800 | **350** | Muss Tier-1 Systeme mit ≥20 Samples bedienen |
| chaos-mixed | 150 | 300–500 | **200** | 20 Fallklassen × 10 = 200 Minimum |
| edge-cases | 100 | 150–250 | **150** | Alle bekannten Verwechslungspaare + Ambiguitäten |
| negative-controls | 50 | 100–150 | **80** | 40 Negative Controls + 40 UNKNOWN-expected |
| repair-safety | 50 | 100–150 | **70** | Confidence-Gating-Varianten |
| dat-coverage | 100 | 150–200 | **100** | 4 Ökosysteme × 25 |
| performance-scale | 0 (generiert) | 5.000–20.000 | 0 (generiert) | Nicht handpflegt |
| **Total** | **720** | **1.500–2.350** | **~1.200** | |

### 7.3 Verteilung S1 nach Plattformfamilie

| Familie | golden-core | golden-realworld | chaos+edge+neg | repair+dat | **Total** |
|---------|-------------|-----------------|---------------|-----------|-----------|
| A: Cartridge | 100 | 140 | 90 | 50 | **380** |
| B: Disc | 70 | 100 | 80 | 60 | **310** |
| C: Arcade | 40 | 50 | 60 | 50 | **200** |
| D: Computer | 25 | 40 | 50 | 35 | **150** |
| E: Hybrid | 15 | 20 | 20 | 25 | **80** |
| **Systemübergreifend** | — | — | — | 80 | **80** |
| **Total** | **250** | **350** | **300** | **300** | **~1.200** |

---

## 8. Die 20 wichtigsten Coverage-Lücken

| Rang | Lücke | Ist-Stand | Soll | Warum kritisch |
|------|-------|-----------|------|----------------|
| 1 | **Arcade Parent/Clone/BIOS/Split-Merged Tiefe** | 80 pauschal | ≥200 differenziert | Komplexeste Plattform, 20 Subszenarien, systematische Blind Spots bei ROM-Set-Varianten |
| 2 | **BIOS systemübergreifend** | ~15 in golden-core | ≥50 über alle Sets | BIOS-AS-GAME = falsche Dedupe = Datenverlust; nur 5 Patterns im Classifier, alle brauchen negative Tests |
| 3 | **PS1↔PS2↔PSP PVD-Disambiguation** | ~6 in edge-cases | ≥30 (10 pro Paar) | Häufigster Produktionsfehler; PVD-Signatur identisch, nur Boot-Marker unterscheidet |
| 4 | **17 fehlende Systeme** | 0 Testfälle | ≥51 (3 pro System) | 25% des Scopes unsichtbar bei Refactors; 3 davon Hoch-Risiko (WIIU, CPC, PC98) |
| 5 | **Multi-File-Sets (CUE+BIN, GDI, M3U)** | ~40 in golden-realworld | ≥80 über alle Sets | Set-Integrität ist kritisch; einzelne fehlende BIN→ Disc unbrauchbar |
| 6 | **Computer/PC Folder-Only-Detection** | ~10 in Computer-Familie | ≥40 | DOS, CPC, PC98, X68K haben NUR Folder/Keyword → höchste false-positive-Rate |
| 7 | **CHD-RAW-SHA1 DAT-Matching** | ~10 in dat-coverage | ≥25 | CHD-embedded-SHA1 ist die einzige DAT-Matching-Methode für komprimierte Discs |
| 8 | **Headerless ROMs** | ~15 in chaos+edge | ≥40 | NES ohne iNES, SNES ohne Header, MD headerless → Fallback-Kette bis Extension/DAT |
| 9 | **Cross-System-Disc-Ambiguität (SAT↔SCD↔DC)** | ~5 in edge-cases | ≥18 (6 pro Paar) | Sega-Disc-Systeme teilen ähnliche IP.BIN-Signaturen |
| 10 | **Arcade ROM-Set-Varianten (Split/Merged/Non-Merged)** | Nicht differenziert | ≥30 (10 pro Variante) | Jede Variante hat andere ZIP-Inhalte → andere Detection |
| 11 | **golden-core Untererfüllung** | 70 geplant | 250 minimum | TESTSET_DESIGN.md fordert 200–300; CI-Test zu dünn für 9 Detection-Methoden × relevante Systeme |
| 12 | **Junk/NonGame/Tooling-Artefakte** | ~20 in chaos | ≥40 | Demo, Beta, Proto, Hack, Homebrew, Firmware-Updater, Mahjong/Quiz-Arcade |
| 13 | **Archive mit gemischtem Inhalt** | ~10 in chaos | ≥25 | ZIP mit ROMs verschiedener Systeme, ZIP mit ROM+Nicht-ROM, korrupte Archive |
| 14 | **Directory-based Games** | ~5 im Schema | ≥15 | Wii U, DOS, Amiga WHDLoad, entpackte ISOs |
| 15 | **DAT-Ökosystem TOSEC** | 0 explizite Fälle | ≥20 | Computer-Systeme nutzen TOSEC; kein einziger expliziter Test |
| 16 | **GB↔GBC CGB-Flag-Varianten** | ~5 in edge-cases | ≥12 | 0x00/0x80/0xC0 × .gb/.gbc Extension × mit/ohne DAT = 12 Kombinationen |
| 17 | **golden-realworld Untererfüllung** | 200 geplant | 350 minimum | TESTSET_DESIGN.md fordert 500–800; Tier-1 braucht ≥20 pro System |
| 18 | **Repair-Safety Confidence-Varianten** | 50 geplant | ≥70 | 9 Confidence-Szenarien × mindestens 7-8 Fälle pro Szenario |
| 19 | **Neo Geo CD (Disc-basiert, aber NEOGEO-adressiert)** | ~3 in Arcade | ≥8 | Wechsel von Zip-basiert (AES/MVS) zu Disc-basiert (CD); andere Detection-Methode |
| 20 | **Wii-spezifische Formate (WIA/RVZ/WBFS/NKIT)** | ~2 in Disc | ≥8 | 4 Container-Formate, alle in AmbiguousExt → Disambiguierung nötig |

---

## 9. Empfohlene Reihenfolge zum Ausbau

### Phase 1: Fundament (sofort, vor erstem Evaluator-Lauf)

| Schritt | Einträge | Begründung |
|---------|----------|-----------|
| golden-core auf 250 bringen | +180 | CI-Basis-Test; alle Detection-Methoden × Tier-1/2 Systeme |
| Alle 69 Systeme mit ≥1 Testfall | +51 (17 fehlende × 3) | 100% System-Coverage |
| BIOS-Matrix (B-01 bis B-12) auf 50 | +35 | Kritischstes Klassifikationsrisiko |
| PS1↔PS2↔PSP PVD-Disambiguation | +24 | Häufigster Produktionsfehler |
| **Phase-1-Total:** | **~290 neue Einträge** | |

### Phase 2: Tiefe (nach Phase 1, vor erstem Release-Gate)

| Schritt | Einträge | Begründung |
|---------|----------|-----------|
| Arcade auf 200 differenzierte Fälle | +120 | Komplexeste Plattform, 20 Subszenarien |
| Multi-File/Multi-Disc auf 80 | +40 | Set-Integrität |
| Computer/PC auf 150 | +50 | Folder-Only-Detection |
| CHD-RAW-SHA1 DAT-Matching | +15 | DAT-Matching für komprimierte Discs |
| **Phase-2-Total:** | **~225 neue Einträge** | |

### Phase 3: Breite (nach Phase 2, vor Metriken-Dashboard)

| Schritt | Einträge | Begründung |
|---------|----------|-----------|
| golden-realworld auf 350 | +150 | Tier-1 ≥20, Tier-2 ≥8 |
| chaos-mixed auf 200 | +50 | Alle 20 Fallklassen besetzt |
| edge-cases auf 150 | +50 | Alle Verwechslungspaare |
| negative-controls auf 80 | +30 | UNKNOWN-expected + Nicht-ROM |
| **Phase-3-Total:** | **~280 neue Einträge** | |

### Phase 4: Metriken-Validierung (nach Phase 3)

| Schritt | Einträge | Begründung |
|---------|----------|-----------|
| repair-safety auf 70 | +20 | Confidence-Gating-Varianten |
| dat-coverage auf 100 | +0 (bereits geplant) | 4 Ökosysteme ausgefüllt |
| TOSEC-DAT-Fälle hinzufügen | +20 | Bisher komplett fehlend |
| Headerless-Varianten ausbauen | +25 | Fallback-Kette testen |
| **Phase-4-Total:** | **~65 neue Einträge** | |

### Phase 5: Long-Tail (laufend)

- Performance-Scale-Generatoren implementieren
- Trend-Analyse zwischen Builds etablieren
- Community-contributed Edge Cases aufnehmen
- Jeder Bug-Report mit Verwechslung → neuer Testfall

---

## 10. Verbindliche Minimum Coverage Matrix

### 10.1 Plattformfamilien-Gate

| Familie | Systeme (Pflicht) | Minimum Testfälle | Davon BIOS | Davon Chaos/Edge/Neg | Davon DAT | Gate-Kriterium |
|---------|-------------------|-------------------|------------|---------------------|-----------|----------------|
| **A: Cartridge** | 35/35 | **380** | ≥15 | ≥90 | ≥30 | FAIL wenn <320 |
| **B: Disc** | 22/22 | **310** | ≥15 | ≥80 | ≥35 | FAIL wenn <260 |
| **C: Arcade** | 3/3 + 8 Subclasses | **200** | ≥15 | ≥70 | ≥25 | FAIL wenn <160 |
| **D: Computer** | 10/10 | **150** | ≥5 | ≥45 | ≥15 | FAIL wenn <120 |
| **E: Hybrid** | 5/5 | **80** | ≥3 | ≥25 | ≥10 | FAIL wenn <60 |
| **Systemübergreifend** | — | **80** | ≥7 | ≥40 | ≥15 | FAIL wenn <50 |
| **TOTAL** | **69/69 + 8 Arcade-Sub** | **≥1.200** | **≥60** | **≥350** | **≥130** | **FAIL wenn <970** |

### 10.2 Fallklassen-Gate

| Fallklasse | Min. Einträge | Min. betroffene Familien | Gate |
|-----------|--------------|------------------------|------|
| FC-01: Saubere Referenz | ≥120 | 5/5 | FAIL wenn <100 |
| FC-02: Falsch benannt | ≥40 | ≥3 | FAIL wenn <30 |
| FC-03: Header-Konflikte | ≥25 | ≥2 | FAIL wenn <20 |
| FC-04: Extension-Konflikte | ≥30 | ≥3 | FAIL wenn <20 |
| FC-05: Folder-vs-Header | ≥20 | ≥2 | FAIL wenn <15 |
| FC-06: DAT exact | ≥60 | ≥4 | FAIL wenn <40 |
| FC-07: DAT weak/no/ambig | ≥40 | ≥3 | FAIL wenn <30 |
| FC-08: BIOS | ≥60 | ≥4 | FAIL wenn <40 |
| FC-09: Parent/Clone | ≥30 | ≥1 (C) | FAIL wenn <20 |
| FC-10: Multi-Disc | ≥25 | ≥2 | FAIL wenn <15 |
| FC-11: Multi-File-Sets | ≥30 | ≥1 (B) | FAIL wenn <20 |
| FC-12: Archive inner-file | ≥20 | ≥2 | FAIL wenn <15 |
| FC-13: Directory-based | ≥15 | ≥2 | FAIL wenn <10 |
| FC-14: UNKNOWN expected | ≥30 | ≥4 | FAIL wenn <20 |
| FC-15: Ambiguous acceptable | ≥25 | ≥2 | FAIL wenn <15 |
| FC-16: Negative controls | ≥40 | 5/5 | FAIL wenn <30 |
| FC-17: Repair-unsafe/blocked | ≥30 | ≥3 | FAIL wenn <20 |
| FC-18: Cross-system conflict | ≥25 | ≥3 | FAIL wenn <15 |
| FC-19: Junk/NonGame | ≥25 | ≥3 | FAIL wenn <15 |
| FC-20: Kaputte Sets | ≥20 | ≥3 | FAIL wenn <10 |

### 10.3 Tier-Tiefe-Gate

| Tier | Systeme | Min. Testfälle pro System | Gate |
|------|---------|--------------------------|------|
| **Tier-1** (9 Systeme) | NES, SNES, N64, GBA, GB, GBC, MD, PS1, PS2 | ≥20 | FAIL wenn <15 bei einem System |
| **Tier-2** (16 Systeme) | 32X, PSP, SAT, DC, GC, WII, SMS, GG, PCE, LYNX, A78, A26, NDS, 3DS, SWITCH, AMIGA | ≥8 | FAIL wenn <5 bei einem System |
| **Tier-3** (22 Systeme) | Rest mit UniqueExt oder Header | ≥3 | FAIL wenn <2 bei einem System |
| **Tier-4** (22 Systeme) | Rest (selten, keine Header) | ≥2 | FAIL wenn <1 bei einem System |

### 10.4 Spezialbereich-Gate

| Bereich | Min. Einträge | Gate |
|---------|--------------|------|
| BIOS gesamt | ≥50 | FAIL wenn <35 |
| BIOS-Systeme abgedeckt | ≥12 verschiedene | FAIL wenn <8 |
| Arcade Parent | ≥20 | FAIL wenn <15 |
| Arcade Clone | ≥15 | FAIL wenn <10 |
| Arcade Split/Merged/Non-Merged | ≥30 (10 pro Typ) | FAIL wenn <20 |
| Arcade BIOS | ≥15 | FAIL wenn <10 |
| PS1↔PS2↔PSP Disambiguation | ≥30 | FAIL wenn <20 |
| GB↔GBC CGB-Varianten | ≥12 | FAIL wenn <8 |
| MD↔32X Header-Ambiguität | ≥8 | FAIL wenn <5 |
| Multi-File-Sets (CUE+BIN/GDI/M3U) | ≥30 | FAIL wenn <20 |
| CHD-RAW-SHA1-Matching | ≥8 | FAIL wenn <5 |
| DAT-Ökosystem No-Intro | ≥25 | FAIL wenn <15 |
| DAT-Ökosystem Redump | ≥25 | FAIL wenn <15 |
| DAT-Ökosystem MAME | ≥15 | FAIL wenn <10 |
| DAT-Ökosystem TOSEC | ≥10 | FAIL wenn <5 |
| Directory-based Games | ≥10 | FAIL wenn <5 |
| Headerless ROMs | ≥20 | FAIL wenn <10 |

### 10.5 Zusammenfassung: Minimales belastbares Testset

```
╔══════════════════════════════════════════════════════════════════╗
║  MINIMUM COVERAGE MATRIX — BENCHMARK GATE                       ║
╠══════════════════════════════════════════════════════════════════╣
║                                                                  ║
║  Systemabdeckung:          69/69 Systeme           (= 100%)     ║
║  Gesamteinträge:           ≥ 1.200                              ║
║  Fallklassen besetzt:      20/20                   (= 100%)     ║
║  Plattformfamilien:        5/5 über Gate-Schwelle               ║
║  BIOS-Fälle:               ≥ 50 über ≥ 12 Systeme              ║
║  Arcade-Fälle:             ≥ 200 über 20 Subszenarien           ║
║  Disc-Disambiguation:      ≥ 30 für PS1↔PS2↔PSP                ║
║  DAT-Ökosysteme:           4/4 (No-Intro, Redump, MAME, TOSEC) ║
║  Tier-1 Mindesttiefe:      ≥ 20 pro System (9 Systeme)         ║
║  Tier-2 Mindesttiefe:      ≥ 8 pro System (16 Systeme)         ║
║  Tier-3 Mindesttiefe:      ≥ 3 pro System (22 Systeme)         ║
║  Tier-4 Mindesttiefe:      ≥ 2 pro System (22 Systeme)         ║
║                                                                  ║
║  Gate: FAIL wenn einer dieser Werte unterschritten wird.        ║
║                                                                  ║
╚══════════════════════════════════════════════════════════════════╝
```

Dieses Gate ist als CI-prüfbare Metrik implementierbar: Ein Skript zählt Einträge pro Familie, Fallklasse, Tier und Spezialbereich und vergleicht gegen die hier definierten Schwellen. FAIL = Build blockiert, bis Ground Truth ergänzt wird.
