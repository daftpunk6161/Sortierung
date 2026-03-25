---
goal: "Coverage Expansion Plan — Benchmark-Testset von 70 auf 1.200+ Ground-Truth-Einträge ausbauen gemäss Minimum Coverage Matrix"
version: "1.0"
date_created: 2026-03-20
last_updated: 2026-03-22
owner: "Romulus Team"
status: "Mostly Complete"
tags:
  - feature
  - testing
  - benchmark
  - coverage
---

# Coverage Expansion Plan

![Status: Mostly Complete](https://img.shields.io/badge/status-Mostly%20Complete-yellow)

Dieser Plan konkretisiert den Ausbau des Benchmark-Testsets von den initialen 70 Einträgen (Phase 2 des Basis-Plans `feature-benchmark-testset-1.md`) auf das S1-Gate von ≥1.200 Einträgen gemäss `docs/architecture/COVERAGE_GAP_AUDIT.md`. Der Basis-Plan deckt Infrastruktur + erste 70 Einträge ab. Dieser Plan beginnt dort, wo der Basis-Plan aufhört, und definiert die konkreten Inhalte, Generatoren und Prüfmechanismen für jede Ausbauphase.

**Referenzdokumente:**
- `docs/architecture/COVERAGE_GAP_AUDIT.md` — Minimum Coverage Matrix, Gates, Lückenanalyse
- `docs/architecture/GROUND_TRUTH_SCHEMA.md` — JSONL-Schema, Datenmodell, Felddefinitionen
- `docs/architecture/TESTSET_DESIGN.md` — Dataset-Klassen, Generator-Spec, Pflichtfälle
- `plan/feature-benchmark-testset-1.md` — Basis-Plan (Infrastruktur + erste 70 Einträge)

**Voraussetzung:** Phasen 1–5 des Basis-Plans sind abgeschlossen (Ordnerstruktur, Schema, 70 golden-core-Einträge, StubGenerator, Evaluation-Runner).

---

## 1. Requirements & Constraints

- **REQ-001**: Alle 69 Systeme aus `data/consoles.json` müssen mit ≥1 Testfall abgedeckt sein (100% System-Coverage).
- **REQ-002**: Jede der 20 Fallklassen (FC-01 bis FC-20) muss die Minimum-Einträge aus der Coverage Matrix erreichen.
- **REQ-003**: Tier-1-Systeme brauchen ≥20 Testfälle, Tier-2 ≥8, Tier-3 ≥3, Tier-4 ≥2.
- **REQ-004**: BIOS-Fälle ≥50 über ≥12 verschiedene Systeme.
- **REQ-005**: Arcade-Fälle ≥200 über 20 Subszenarien differenziert.
- **REQ-006**: PS1↔PS2↔PSP-Disambiguation ≥30 Testfälle.
- **SEC-001**: Alle neuen Stub-Generatoren müssen Path-Traversal-Schutz einhalten (Root-Validierung).
- **SEC-002**: Keine echten ROM-Inhalte — nur synthetische Header + Padding.
- **CON-001**: Jede Ausbauphase muss den Build grün halten — kein roter CI-State zwischen Phasen.
- **CON-002**: JSONL-Dateien bleiben nach ID sortiert, UTF-8 ohne BOM, LF-Zeilenende.
- **CON-003**: Manifest-Statistiken werden nach jeder Phase aktualisiert.
- **GUD-001**: ID-Format: `{set-prefix}-{system}-{subclass}-{laufnummer}`. Laufnummern pro Set+System fortlaufend.
- **GUD-002**: Jeder neue Eintrag enthält `addedInVersion` mit der Phase-Version (z.B. `"1.1.0"` für Phase E1).
- **PAT-001**: Neue Stub-Generatoren folgen dem Dispatch-Pattern aus `StubGenerator.cs` (Basis-Plan TASK-016).

---

## 2. Implementation Steps

### Expansion Phase E1 — Fundament: golden-core auf 250, fehlende Systeme, BIOS-Matrix, PS-Disambiguation

- GOAL-E01: golden-core von 70 auf 250 Einträge erweitern. Alle 69 Systeme abdecken. BIOS-Matrix B-01 bis B-12 aufbauen. PS1↔PS2↔PSP Disambiguation implementieren. Nach Phase E1: System-Coverage 69/69, golden-core 250, BIOS ≥50, PS-Disambiguation ≥30.

**E1.1 — golden-core.jsonl: Cartridge-Expansion (+60 Einträge)**

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-E001 | `golden-core.jsonl`: 10 zusätzliche Tier-1-Cartridge-Einträge. Je 1 weiterer NES (iNES2.0-Header `4E45531A 08`), SNES (ExLoROM), N64 (N64DD-Disk), GBA (Multiboot), GB (SGB-Enhanced `0x146=0x03`), GBC (CGB-Only `0xC0` mit `.gb`-Extension — Sonderfall), MD (PAL-Region `SEGA MEGA DRIVE`), 32X (mit CD-Supplement-Ref), NDS (Dual-Slot NDS+GBA), 3DS (CIA-Container). Alle `set: "golden-core"`, `difficulty: "easy"`, `expected.confidenceClass: "high"`. | | |
| TASK-E002 | `golden-core.jsonl`: 10 Tier-2-Cartridge-FolderName-Detection-Einträge. SMS (`.sms` + Ordner `sms/`), GG (`.gg` + Ordner `gamegear/`), PCE (`.pce` + Ordner `pcengine/`), A26 (`.a26` + Ordner `atari2600/`), A78 (`.a78` + Ordner `atari7800/` — Folder sagt 7800, Extension sagt 7800), NGP (`.ngp`), INTV (`.int`), COLECO (`.col`), VECTREX (`.vec`), SG1000 (`.sg`). `detectionSources: ["UniqueExtension", "FolderName"]` oder `["UniqueExtension"]`. | | |
| TASK-E003 | `golden-core.jsonl`: 13 Tier-3/4-Systeme die bisher fehlen (fehlende Systeme Gap #4). Je 1 Eintrag: CHANNELF (`.bin` + Folder `channelf/`), SUPERVISION (`.sv` + Folder), NGPC (`.ngc`), ODYSSEY2 (`.bin` + Folder `odyssey2/`), A52 (`.a52` + Folder), CPC (Folder `amstradcpc/` + `.dsk`), PC98 (Folder `pc98/` + `.hdi`), X68K (Folder `x68000/` + `.dim`), CDI (`.cdi`), WIIU (Directory-Container `code/app.rpx`), X360 (`.xex` + Folder `xbox360/`), PS3 (`.pkg` + Folder `ps3/`), ATARIST (`.st`). `difficulty: "easy"` bis `"medium"`. | | |
| TASK-E004 | `golden-core.jsonl`: 4 verbleibende fehlende Systeme: VB (`.vb`), WS (`.ws`), WSC (`.wsc`), POKEMINI (`.min`). `detectionSources: ["UniqueExtension"]`, `expected.confidenceClass: "high"`. | | |

**E1.2 — golden-core.jsonl: Disc-Expansion (+25 Einträge)**

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-E005 | `golden-core.jsonl`: 5 weitere Disc-Header-Systeme. PSP (PVD + `PSP_GAME`, 2 Samples: ISO + CSO), SAT (IP.BIN `SEGA SATURN`, 2 Samples), DC (IP.BIN `SEGA SEGAKATANA`, 1 Sample mit GDI), 3DO (Opera FS, 1 Sample), SCD (`SEGADISCSYSTEM`, 1 Sample). `detectionSources: ["DiscHeader"]`. | | |
| TASK-E006 | `golden-core.jsonl`: 5 Multi-File-Set Referenzfälle. PS1 CUE+BIN (1 Track), PS1 CUE+BIN (Multi-Track Audio+Data), SAT CUE+BIN, DC GDI+3 Tracks, PS1 M3U+2 CHDs. Alle `container: "multi-file"`, `fileModel.setFiles` korrekt ausgefüllt. | | |
| TASK-E007 | `golden-core.jsonl`: 5 Serial-Number-Detection-Referenzen. PS1 `SLUS-00123`, PS2 `SCUS-97113`, PSP `UCUS-98630`, GC `GMSE01`, SAT `MK-81005`. `detectionSources: ["SerialNumber"]` oder `["DiscHeader", "SerialNumber"]`. | | |
| TASK-E008 | `golden-core.jsonl`: 5 Archive-Inner-Hash-Referenzen. NES in ZIP (No-Intro Hash), SNES in ZIP, MD in 7z, GBA in ZIP, PS1-BIN in ZIP (Redump Hash). `container: "zip"` oder `"7z"`, `fileModel.innerFiles` ausgefüllt, `expected.dat.hashType: "sha1"`. | | |
| TASK-E009 | `golden-core.jsonl`: 5 DAT-Exact-Match-Referenzen (zusätzlich zu TASK-E008). NES No-Intro, SNES No-Intro, PS1 Redump, ARCADE MAME (CRC32), AMIGA TOSEC. Verschiedene DAT-Ökosysteme. `expected.datMatchLevel: "exact"`. | | |

**E1.3 — BIOS-Einträge verteilt über 3 JSONL-Dateien (+50 Einträge)**

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-E010 | `golden-core.jsonl`: 20 BIOS-Referenzfälle (B-01 bis B-04). Verteilung: PS1 `[BIOS]` (3), PS2 `[BIOS]` (3), GBA BIOS (2), NDS BIOS (2), DC BIOS (2), SAT BIOS (2), 3DO BIOS (2), XBOX BIOS (1), Amiga Kickstart (1), C64 KERNAL (1), 3DS Firmware (1). Alle `expected.category: "Bios"`, `tags: ["bios"]`. | | |
| TASK-E011 | `edge-cases.jsonl`: 15 BIOS-Edge-Cases (B-05 bis B-12). GAME-fälschlich-als-BIOS: "BioShock" (3), "BIOS Agent" (2) → `expected.category: "Game"`. BIOS ohne Tag (nur DAT-Kennung, 3). BIOS in Archiv (ZIP, 2). BIOS neben Spielen im Ordner (3). Korruptes BIOS (falscher Hash, 2) → `expected.datMatchLevel: "none"`. | | |
| TASK-E012 | `chaos-mixed.jsonl`: 15 Arcade-BIOS-Fälle. neogeo.zip (3 Varianten: vollständig, fehlende ROMs, Extra-ROMs). pgm.zip (2). naomi.zip (2). Shared-BIOS mit `biosSystemKeys` korrekt. Neo Geo BIOS in falschem Ordner (2). Split-BIOS vs. Merged-BIOS (3). BIOS-ZIP mit identischem Namen aber verschiedenem Inhalt (1). | | |

**E1.4 — PS1↔PS2↔PSP Disambiguation (+30 Einträge)**

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-E013 | `edge-cases.jsonl`: 10 PS1↔PS2-Fälle. PS1-ISO mit PVD (no BOOT2=), PS2-ISO mit BOOT2=, PS1-ISO in PS2-Ordner, PS2-ISO in PS1-Ordner, PS1-CUE+BIN, PS2-CUE+BIN, PS1-CHD, PS2-CHD, PS1 mit falscher `.iso`-Extension in PS2-Folder, PS2 mit `SLUS-` Serial (→ PS2, nicht PS1). Alle `detectionSources` enthalten `"DiscHeader"`, `difficulty: "hard"`. | | |
| TASK-E014 | `edge-cases.jsonl`: 10 PS2↔PSP-Fälle. PSP-ISO mit `PSP_GAME`, PS2-ISO ohne `PSP_GAME`, PSP-CSO, PSP-ISO in PS2-Ordner, PS2-ISO in PSP-Ordner, PSP mit `UCUS-` Serial, PS2 mit `SCUS-` Serial, PSP-ISO mit fehlendem PVD, PS2-ISO mit Folder-Only (kein Header), PS2 vs PSP jeweils in neutralem Ordner. | | |
| TASK-E015 | `edge-cases.jsonl`: 10 Triple-Ambiguity-Fälle (PS1↔PS2↔PSP). ISO in `playstation/`-Ordner (ambig), ISO mit `.bin`-Extension (ambig), ISO mit nur PVD (no Boot-Marker, Folder sagt PS1), ISO mit nur PVD (Folder sagt PSP), CHD ohne Header-Access → Folder-Only, CSO in flachem Ordner, ISO mit korruptem PVD-Sektor (→ Extension-/Folder-Fallback), PS1→PS2 Cross-DAT-Match, PS2→PSP Cross-DAT-Match, ISO 0-Byte → UNKNOWN. `difficulty: "hard"` bis `"adversarial"`, `tags: ["ps-disambiguation", "cross-system"]`. | | |

**E1.5 — Manifest-Update + Coverage-Validator**

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-E016 | `benchmark/manifest.json` aktualisieren: `totalEntries: 250`, `systemsCovered: 69`, neue `bySet`-Verteilung, neue `byPlatformClass`-Verteilung, `byDifficulty`, `byFallklasse` (FC-01 bis FC-20 Zählung). | | |
| TASK-E017 | Datei `benchmark/generators/CoverageValidator.cs` erstellen. Klasse `CoverageValidator` mit Methode `Validate(IReadOnlyList<GroundTruthEntry> entries)` → `CoverageReport`. Prüft gegen alle Gates aus §10 der COVERAGE_GAP_AUDIT.md: Plattformfamilien-Gate (§10.1), Fallklassen-Gate (§10.2), Tier-Tiefe-Gate (§10.3), Spezialbereich-Gate (§10.4). Gibt `List<GateViolation>` zurück; jede Violation enthält GateName, Expected, Actual, Severity. | | |
| TASK-E018 | Datei `src/RomCleanup.Tests/Benchmark/CoverageGateTests.cs` erstellen. `[Fact] AllPlatformFamilyGates_AreMet()`, `[Fact] AllCaseClassGates_AreMet()`, `[Fact] AllTierDepthGates_AreMet()`, `[Fact] AllSpecialAreaGates_AreMet()`. Jeder Test lädt alle Ground-Truth-Entries, ruft `CoverageValidator.Validate()` auf und asssertiert: keine FAIL-Violations. Trait `[Trait("Category", "CoverageGate")]`. | | |
| TASK-E019 | Neue Stub-Generator-Methoden registrieren für Phase-E1-Einträge: `GeneratePspPvdStub()` (PVD + PSP_GAME), `GenerateCsoStub()` (CSO-Header + komprimierter Block), `GenerateDirectoryGameStub()` (Wii U: erstellt Ordner + app.rpx + meta.xml), `GenerateMultiFileSetStub()` (CUE + N BINs), `GenerateGdiSetStub()` (GDI + Tracks), `GenerateM3uSetStub()` (M3U + CHDs). Path-Traversal-Schutz für Directory-Container. | | |

**Exit-Kriterium Phase E1:**
- `golden-core.jsonl`: 250 Einträge
- `edge-cases.jsonl`: ≥45 Einträge (30 PS-Disambig + 15 BIOS-Edge)
- `chaos-mixed.jsonl`: ≥15 Einträge (Arcade-BIOS)
- System-Coverage: 69/69
- BIOS-Fälle: ≥50 über ≥12 Systeme
- PS-Disambiguation: ≥30
- Alle CoverageGate-Tests grün (Phase-E1-Gates: Plattformfamilie + System-Coverage + BIOS)
- Build grün

---

### Expansion Phase E2 — Tiefe: Arcade, Multi-File, Computer, CHD

- GOAL-E02: Arcade auf 200, Multi-File/Multi-Disc auf 80, Computer/PC auf 150, CHD-RAW-SHA1 auf 25. Schwerpunkt auf den komplexesten Detection-Szenarien.

**E2.1 — Arcade-Ausbau (+120 Einträge)**

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-E020 | `golden-realworld.jsonl`: 30 Arcade-Referenzfälle. MAME Parent (10): sf2, mslug, dkong, pacman, galaga, 1943, bublbobl, xmen, tmnt, garou. MAME Clone (10): sf2ce, sf2hf, mslughw, dkongjr, pacplus, gallag, bublbob2, xmenj, tmhtj, garouo. Neo Geo Parent (5): kof98, mslug3, samsho2, fatfury3, blazstar. Neo Geo Clone (5): kof98h, mslug3s, samsho2k, fatfury3s, blazstara. Alle mit `expected.category: "Game"`, `tags: ["arcade", "parent"]` oder `["arcade", "clone"]`. Relationships modellieren (cloneOf). | | |
| TASK-E021 | `golden-realworld.jsonl`: 10 Arcade-BIOS-Referenzfälle. neogeo.zip (Universe BIOS Varianten: 1.0, 2.3, 4.0), pgm.zip, naomi.zip, atomiswave.zip, decocass.zip, skns.zip, stvbios.zip, hng64.zip. Alle `expected.category: "Bios"`, `biosSystemKeys` korrekt. | | |
| TASK-E022 | `edge-cases.jsonl`: 30 Arcade-ROM-Set-Varianten. Split-Set (10): Parent-ZIP mit nur eigenen ROMs in 5 Systemen × 2, Clone-ZIP mit nur Delta-ROMs. Merged-Set (10): Parent+alle Clones in einem ZIP, 5 verschiedene Spiele × 2 Prüfvarianten. Non-Merged-Set (10): Komplett eigenständige ZIPs, 5 verschiedene Spiele × 2. `tags: ["arcade", "split"|"merged"|"non-merged"]`. | | |
| TASK-E023 | `edge-cases.jsonl`: 10 Arcade-CHD-Supplement-Fälle. Naomi: ZIP + CHD (3), Atomiswave: ZIP + CHD (2), MAME HDD-Games: ZIP + CHD (3), Neo Geo CD: CUE+BIN (2). `container: "multi-file"` für CHD-Supplements, `container: "multi-file"` für NeoCD. | | |
| TASK-E024 | `chaos-mixed.jsonl`: 20 Arcade-Chaos-Fälle. Kaputte ROM-Sets (fehlende ROMs, 5), MAME-Versionswechsel-Namen (5), ARCADE↔NEOGEO Ambiguität (5), ZIP mit gemischtem Inhalt Arcade+NonArcade (3), Mahjong/Quiz/Gambling als NonGame (2). `difficulty: "hard"`, `tags: ["arcade", "chaos"]`. | | |
| TASK-E025 | `negative-controls.jsonl`: 5 Arcade-Negative. ZIP das kein Arcade-Set ist (3), Device-ROM nur (nicht spielbar, 2). `expected.category: "NonGame"` oder `"Unknown"`. | | |
| TASK-E026 | `dat-coverage.jsonl`: 15 Arcade-DAT-Fälle. MAME-CRC32-Match exact (5), MAME-CRC32-Match mit ROM-Rename (3), Kein DAT-Match Homebrew (2), Cross-MAME-Version-Mismatch (3), FBNeo-DAT vs MAME-DAT Unterschied (2). | | |

**E2.2 — Multi-File / Multi-Disc (+40 Einträge)**

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-E027 | `golden-realworld.jsonl`: 15 Multi-File-Referenzen. CUE+BIN Single-Track (PS1, SAT, SCD: 3), CUE+BIN Multi-Track Audio+Data (PS1, SAT, DC: 3), GDI+Tracks (DC: 2), CCD+IMG+SUB (PS1, SAT: 2), MDS+MDF (PS1, PS2: 2), M3U+CHD 2-Disc (PS1: 1), M3U+CHD 3-Disc (PS1: 1), M3U+CHD 4-Disc (PS2: 1). Alle `container: "multi-file"`, `fileModel.setFiles` vollständig. | | |
| TASK-E028 | `edge-cases.jsonl`: 15 Multi-Disc-Edge-Cases. 2-Disc korrekt gruppiert (PS1, SAT: 4), 3-Disc (PS1, PS2: 3), 4-Disc (PS1: 1), Disc 1+3 vorhanden aber Disc 2 fehlt (2), Disc mit falscher Nummer im Name (2), Multi-Disc im M3U aber ein CHD fehlt (M3U-Pruning-Test, 2), Multi-Disc verschiedene Systeme (PS1-Disc + PS2-Disc im gleichen Ordner, 1). | | |
| TASK-E029 | `repair-safety.jsonl`: 10 Multi-File-Repair-Fälle. CUE+BIN mit BIN-Mismatch (Ref fehlt, 2), GDI mit fehlenden Tracks (2), M3U mit korrupter Referenz (2), CHD mit falschem embedded-SHA1 (2), CUE-only ohne BIN (2). Alle `expected.repairSafe: false`. | | |

**E2.3 — Computer/PC-Ausbau (+50 Einträge)**

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-E030 | `golden-realworld.jsonl`: 20 Computer-Referenzen. AMIGA: ADF (3), HDF (1), DMS (1). C64: D64 (3), T64 (1), G64 (1). ZX: TZX (2), TAP (1), Z80 (1). MSX: ROM (2), DSK (1). ATARIST: ST (2), STX (1). A800: ATR (2). CPC: DSK in Folder `amstradcpc/` (2). PC98: HDI in Folder `pc98/` (1). X68K: DIM in Folder `x68000/` (1). DOS: Ordner mit GAME.EXE (1, directory-container). `tags: ["computer", "tier-2"|"tier-3"]`. | | |
| TASK-E031 | `edge-cases.jsonl`: 15 Computer-Edge-Cases. Folder-Only-Detection (CPC ohne Extension: 3, PC98 ohne Extension: 2, X68K: 2). Extension-Conflict: `.dsk` (Amiga-HDF vs CPC-DSK: 2), `.img` (Amiga vs generic: 2). Folder-vs-Keyword-Conflict: Ordner `amiga/` enthält C64-D64 (2), Ordner `msx/` enthält CPC-DSK (2). | | |
| TASK-E032 | `negative-controls.jsonl`: 5 Computer-Negatives. `.exe` das kein DOS-Spiel ist (2), `.dsk` mit unbekanntem Format (2), leerer Ordner mit Systemnamen (1). | | |
| TASK-E033 | `dat-coverage.jsonl`: 10 Computer-TOSEC-DAT-Fälle. TOSEC-exact-Match AMIGA (3), C64 (2), ZX (2), MSX (1). TOSEC-Miss (kein Eintrag, 2). `expected.dat.ecosystem: "tosec"`. | | |

**E2.4 — CHD-RAW-SHA1-Ausbau (+15 Einträge)**

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-E034 | `dat-coverage.jsonl`: 15 CHD-RAW-SHA1-Fälle. PS1-CHD exact (3), PS2-CHD exact (3), PSP-CHD exact (2), SAT-CHD exact (2), DC-CHD exact (2). CHD v5 Header mit SHA1 @ offset 0x40. Container-Hash ≠ Content-Hash prüfbar. Zusätzlich: CHD mit falschem embedded-SHA1 (DAT-Miss, 2), CHD v4 (älteres Format, 1). | | |

**E2.5 — Neue Stub-Generatoren Phase E2**

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-E035 | Neue Generator-Methoden: `GenerateArcadeZipStub(string romsetName, string[] innerRomNames, int[] innerCrc32s)` — erzeugt ZIP mit benannten inneren Dateien und vorgegebenen CRC32. `GenerateMergedArcadeZipStub(string parent, string[] clones)` — erstellt Merged-Set. `GenerateSplitArcadeZipStub(string name, string[] ownRoms, string[] sharedRoms)`. `GenerateNonMergedArcadeZipStub(string name, string[] allRoms)`. | | |
| TASK-E036 | Neue Generator-Methoden: `GenerateChdV5Stub(byte[] embeddedSha1, int dataBytes)` — erzeugt Minimal-CHD-v5-Header mit SHA1 @ 0x40 + Padding. `GenerateChdV4Stub(byte[] sha1)` — älteres Format. `GenerateCsoStub(byte[] pvdContent)` — CSO-Header + komprimierter PVD-Block. | | |
| TASK-E037 | Neue Generator-Methoden für Computer-Stubs: `GenerateAdfStub()` (Amiga 880KB Disk-Image-Header), `GenerateD64Stub()` (C64 BAM @ Track 18), `GenerateTzxStub()` (ZX TZX-Header `ZXTape!`), `GenerateDskStub(string system)` (CPC/MSX EDSK `EXTENDED`-Header vs. Standard-DSK), `GenerateStStub()` (Atari ST Disk-Image), `GenerateAtrStub()` (Atari 800 ATR-Header `0x9602`). | | |

**Exit-Kriterium Phase E2:**
- Arcade gesamt: ≥200 Einträge über alle JSONL-Dateien
- Multi-File/Multi-Disc: ≥80
- Computer/PC: ≥150
- CHD-RAW-SHA1: ≥25
- Alle CoverageGate-Tests grün (Phase-E2-Gates: Arcade ≥160, Multi-File ≥20, Computer ≥120)
- Neue Stub-Generatoren mit Path-Traversal-Schutz
- Build grün

---

### Expansion Phase E3 — Breite: golden-realworld, chaos-mixed, edge-cases, negative-controls

- GOAL-E03: golden-realworld auf 350, chaos-mixed auf 200, edge-cases auf 150, negative-controls auf 80. Alle 20 Fallklassen besetzt. Tier-1 ≥20, Tier-2 ≥8 pro System.

**E3.1 — golden-realworld System-Tiefe (+150 Einträge)**

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-E038 | `golden-realworld.jsonl`: Tier-1 Auffüllung auf ≥20 pro System. Berechnung der Lücken nach E1+E2 und gezielte Ergänzung. Pro Tier-1-System: Region-Varianten (EU, US, JP, WORLD), Revision-Varianten (Rev A, v1.1), verschiedene Container (single, zip), FolderName-Aliase. Priorität: NES (+X), SNES (+X), N64 (+X), GBA (+X), GB (+X), GBC (+X), MD (+X), PS1 (+X), PS2 (+X). Genaue Zahlen abhängig vom Stand nach E1+E2. | | |
| TASK-E039 | `golden-realworld.jsonl`: Tier-2 Auffüllung auf ≥8 pro System. Systeme: 32X, PSP, SAT, DC, GC, WII, SMS, GG, PCE, LYNX, A78, A26, NDS, 3DS, SWITCH, AMIGA. Je System: mindestens 1 Region-Variante, 1 Archive-Variante, 1 DAT-Match. | | |
| TASK-E040 | `golden-realworld.jsonl`: Hybrid-Systeme auf ≥10 pro System. PSP (ISO, CSO, DAX), Vita (VPK), 3DS (3DS, CIA, CCI), Switch (NSP, XCI), WiiU (WUX, RPX-Directory). Container-Vielfalt pro System. | | |

**E3.2 — chaos-mixed Fallklassen-Auffüllung (+50 Einträge)**

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-E041 | `chaos-mixed.jsonl`: FC-02 Falsch benannte Dateien (15). NES-ROM namens "SNES Game.nes" (3), PS2-ISO namens "PS1-Game.iso" (3), MD-ROM mit GBA-Extension (2), Arcade-ROM namens "NeoGeo-XYZ.zip" aber ist MAME-Parent (2), Unicode-Dateinamen mit CJK-Zeichen (3), Dateinamen mit Leerzeichen und Sonderzeichen (2). | | |
| TASK-E042 | `chaos-mixed.jsonl`: FC-20 Kaputte Sets (15). Truncated NES (Header OK, ROM-Data abgeschnitten, 2), Truncated ISO (PVD OK, Daten fehlen, 2), 0-Byte `.nes` (2), 0-Byte `.iso` (2), ZIP mit korruptem Central-Directory (2), 7z mit fehlerhaftem Header (2), CUE ohne zugehörige BINs (1), GDI mit fehlerhaften Track-Referenzen (1), Halb-entpacktes RAR (1). | | |
| TASK-E043 | `chaos-mixed.jsonl`: FC-12 Archive-Inner-Mixed (10). ZIP mit ROMs verschiedener Systeme (NES+SNES, 2), ZIP mit ROM + TXT + JPG (2), ZIP mit 100+ Dateien aber nur 1 ROM (1), 7z mit verschachteltem ZIP (1), ZIP mit Pfade die `../` enthalten (Path-Traversal-Sim, 1), Korrupte ZIP die trotzdem Inner-Files listet (1), ZIP mit falscher Extension `.rar` (1), Passwort-geschütztes ZIP (1). | | |
| TASK-E044 | `chaos-mixed.jsonl`: Headerless-ROMs (10). NES ohne iNES-Header (raw PRG, 2), SNES ohne Header (raw ROM, 2), MD ohne "SEGA MEGA DRIVE" Header (2), GB ohne Nintendo-Logo (2), N64 mit korruptem Magic-Byte (2). Alle `difficulty: "hard"`, `detectionSources` ohne Header-Methode. | | |

**E3.3 — edge-cases Disambiguation (+50 Einträge)**

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-E045 | `edge-cases.jsonl`: GB↔GBC CGB-Flag-Varianten (12). CGB=0x00 + `.gb` (→ GB), CGB=0x00 + `.gbc` (→ GB trotz Extension), CGB=0x80 + `.gb` (→ GBC, akzeptabel GB), CGB=0x80 + `.gbc` (→ GBC), CGB=0xC0 + `.gb` (→ GBC, Extension lügt), CGB=0xC0 + `.gbc` (→ GBC). Jeweils 2 Samples. `acceptableAlternatives` für Dual-Mode. | | |
| TASK-E046 | `edge-cases.jsonl`: MD↔32X Header (8). `SEGA MEGA DRIVE` @ 0x100 (→ MD, 2), `SEGA 32X` @ 0x100 (→ 32X, 2), `SEGA MEGA DRIVE` aber `.32x`-Extension (Conflict, 2), 32X-ROM in Genesis-Ordner (Conflict, 2). `tags: ["md-32x-conflict"]`. | | |
| TASK-E047 | `edge-cases.jsonl`: SAT↔SCD↔DC Disc-Disambiguation (12). `SEGA SATURN` IP.BIN (→ SAT, 2), `SEGADISCSYSTEM` IP.BIN (→ SCD, 2), `SEGA SEGAKATANA` IP.BIN (→ DC, 2). SAT-Disc in SCD-Ordner (2), DC-Disc in SAT-Ordner (2), IP.BIN mit korruptem Header (→ Folder-Fallback, 2). | | |
| TASK-E048 | `edge-cases.jsonl`: GC↔Wii Magic-Byte (8). GC-Magic `C2339F3D` (→ GC, 2), Wii-Magic `5D1C9EA3` (→ Wii, 2), GC-ISO in Wii-Ordner (Conflict, 2), Wii-ISO mit GC-Extension `.gcm` (Conflict, 2). | | |
| TASK-E049 | `edge-cases.jsonl`: Confidence-Gating-Edge (10). Single-Signal FolderOnly Confidence~70 (3), FolderOnly + Extension Confidence~85 (2), Header + DAT Confidence~98 (2), Extension-only ambiguous Confidence~40 (2), Zero-Signals → UNKNOWN (1). `tags: ["confidence-gating"]`. | | |

**E3.4 — negative-controls (+30 Einträge)**

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-E050 | `negative-controls.jsonl`: FC-14 UNKNOWN expected (15). Datei mit `.rom`-Extension aber zufälligem Inhalt (3), Leerer Ordner mit System-Name (2), `.bin` mit PDF-Inhalt (2), `.iso` mit RAR-Magic (2), Datei ohne Extension (2), Datei mit doppelter Extension `.nes.bak` (2), Riesige `.txt`-Datei (10 MB Platzhalter, 2). | | |
| TASK-E051 | `negative-controls.jsonl`: FC-16 Negative Controls (15). `.doc`, `.pptx`, `.zip` (nicht ROM-Archiv), `.avi`, `.mp4`, `.png`, `.bmp`, `.html`, `.css`, `.js`, `.py`, `.java`, `.swift`, `.rs`, `.sql`. Alle `expected.consoleKey: null`, `expected.category: "Unknown"`, `expected.sortingDecision: "block"`. | | |

**Exit-Kriterium Phase E3:**
- golden-realworld: ≥350 Einträge
- chaos-mixed: ≥200
- edge-cases: ≥150
- negative-controls: ≥80
- Tier-1: ≥20 pro System (9 Systeme)
- Tier-2: ≥8 pro System (16 Systeme)
- Alle 20 Fallklassen über Gate-Schwelle
- Build grün

---

### Expansion Phase E4 — Metriken: repair-safety, dat-coverage, Headerless, TOSEC

- GOAL-E04: repair-safety auf 70, dat-coverage auf 100, TOSEC-DAT ≥10, Headerless ≥20. Alle Spezialbereich-Gates der Coverage Matrix erfüllt.

**E4.1 — repair-safety (+20 Einträge)**

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-E052 | `repair-safety.jsonl`: Confidence-Varianten (20). DAT-Exact + Confidence ≥95 → Repair-Safe (4), Header+Ext Confidence ~88 → Repair-Safe (3), Folder-Only Confidence ~70 → Not-Repair-Safe (3), Extension-Only Confidence ~60 → Not-Repair-Safe (2), HasConflict=true → Sort-Blocked (3), Category=Bios → Sort-Blocked (2), Category=Junk → Sort-Blocked (2), LookupAny (DAT weak-match) → Review-Needed (1). | | |

**E4.2 — dat-coverage Auffüllung (+0, bereits durch E2 erreicht; Validierung)**

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-E053 | `dat-coverage.jsonl` Validierung: Prüfen dass nach E2 mindestens 100 Einträge vorhanden sind. Verteilung: No-Intro ≥25, Redump ≥25, MAME ≥15, TOSEC ≥10. DAT-none/miss ≥15. Falls Lücken: gezielte Ergänzung. | | |

**E4.3 — Manifest-Finalisierung + Gate-Vollprüfung**

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-E054 | `benchmark/manifest.json` finalisieren mit exakten Zählungen. Neue Manifest-Felder (siehe §5): `coverage.systemsCovered`, `coverage.byPlatformFamily`, `coverage.byFallklasse`, `coverage.byTier`, `coverage.bySpecialArea`. Alle Werte müssen die S1-Gates erfüllen. | | |
| TASK-E055 | `CoverageGateTests.cs` erweitern: `[Fact] S1_MinimumViableBenchmark_AllGatesMet()`. Aggregiert ALLE Gate-Prüfungen (Plattform + Fallklasse + Tier + Spezial) und gibt bei FAIL eine detaillierte Auflistung aller Violations aus. Wird in CI als Release-Gate verwendet. | | |
| TASK-E056 | Baseline-Datei `benchmark/baselines/s1-baseline.json` erstellen: Aktueller Metrik-Snapshot nach E4. Enthält Wrong-Match-Rate, Unsafe-Sort-Rate, Precision, Recall, F1 pro Plattformfamilie und global. Wird für Regressions-Gate verwendet (Basis-Plan TASK-042+043). | | |

**Exit-Kriterium Phase E4:**
- repair-safety: ≥70
- dat-coverage: ≥100
- TOSEC: ≥10
- Headerless: ≥20
- Alle S1-Gates der Minimum Coverage Matrix bestanden
- Manifest vollständig mit Coverage-Metriken
- Baseline-Snapshot geschrieben
- `dotnet test --filter Category=CoverageGate` grün
- Build grün
- **S1-Gate: ≥1.200 Einträge, 69/69 Systeme, 20/20 Fallklassen**

---

## 3. Mindestmengen

### 3.1 JSONL-Dateien: Soll-Zustand nach jeder Phase

| JSONL-Datei | Nach E1 | Nach E2 | Nach E3 | Nach E4 (S1) |
|-------------|---------|---------|---------|---------------|
| `golden-core.jsonl` | **250** | 250 | 250 | **250** |
| `golden-realworld.jsonl` | 0 | **105** | **350** | 350 |
| `edge-cases.jsonl` | **45** | **90** | **150** | 150 |
| `chaos-mixed.jsonl` | **15** | **35** | **85** (+chaos-Einträge aus E3) → **200** | 200 |
| `negative-controls.jsonl` | 0 | **10** | **80** | 80 |
| `repair-safety.jsonl` | 0 | **10** | 10 | **70** |
| `dat-coverage.jsonl` | 0 | **40** | 40 | **100** |
| **TOTAL** | **~310** | **~540** | **~1.060** | **≥1.200** |

### 3.2 Plattformfamilien: Soll-Zustand nach jeder Phase

| Familie | Nach E1 | Nach E2 | Nach E3 | Nach E4 (S1-Gate) |
|---------|---------|---------|---------|-------------------|
| A: Cartridge | ~170 | ~200 | ~350 | **≥380** |
| B: Disc | ~50 | ~140 | ~280 | **≥310** |
| C: Arcade | ~15 | ~195 | ~200 | **≥200** |
| D: Computer | ~13 | ~100 | ~140 | **≥150** |
| E: Hybrid | ~12 | ~30 | ~70 | **≥80** |
| Systemübergreifend | ~50 | ~75 | ~80 | **≥80** |

### 3.3 Fallklassen: Mindestmengen (S1-Gate)

| FC | Name | Minimum | Hard-Fail |
|----|------|---------|-----------|
| FC-01 | Saubere Referenz | ≥120 | <100 |
| FC-02 | Falsch benannt | ≥40 | <30 |
| FC-03 | Header-Konflikte | ≥25 | <20 |
| FC-04 | Extension-Konflikte | ≥30 | <20 |
| FC-05 | Folder-vs-Header | ≥20 | <15 |
| FC-06 | DAT exact | ≥60 | <40 |
| FC-07 | DAT weak/no/ambig | ≥40 | <30 |
| FC-08 | BIOS | ≥60 | <40 |
| FC-09 | Parent/Clone | ≥30 | <20 |
| FC-10 | Multi-Disc | ≥25 | <15 |
| FC-11 | Multi-File-Sets | ≥30 | <20 |
| FC-12 | Archive inner-file | ≥20 | <15 |
| FC-13 | Directory-based | ≥15 | <10 |
| FC-14 | UNKNOWN expected | ≥30 | <20 |
| FC-15 | Ambiguous acceptable | ≥25 | <15 |
| FC-16 | Negative controls | ≥40 | <30 |
| FC-17 | Repair-unsafe/blocked | ≥30 | <20 |
| FC-18 | Cross-system conflict | ≥25 | <15 |
| FC-19 | Junk/NonGame | ≥25 | <15 |
| FC-20 | Kaputte Sets | ≥20 | <10 |

---

## 4. Benötigte Generatoren / Tools

### 4.1 Stub-Generatoren (neu in Expansion)

| Generator-Name | Phase | Erzeugt | Besonderheiten |
|---------------|-------|---------|----------------|
| `psp-pvd` | E1 | PSP-ISO mit PVD + `PSP_GAME` | PVD @ 0x8000 + Boot-Marker |
| `cso-container` | E1 | CSO-Header + komprimierter Block | CSO-Magic `CISO` @ 0x00 |
| `directory-wiiu` | E1 | WiiU-Ordnerstruktur (code/app.rpx) | Directory-Container, kein Einzelfile |
| `multi-file-cue-bin` | E1 | CUE + N BIN-Dateien | Generiert CUE-Text + Padding-BINs |
| `multi-file-gdi` | E1 | GDI + Track-Dateien | GDI-Textformat + Track-Padding |
| `multi-file-m3u-chd` | E1 | M3U + N CHD-Stubs | M3U-Text + CHD-v5-Headers |
| `arcade-zip-parent` | E2 | MAME Parent ZIP | ZIP mit CRC-kontrollierten Inner-Files |
| `arcade-zip-clone` | E2 | MAME Clone ZIP | Wie Parent, mit Delta-ROMs |
| `arcade-zip-merged` | E2 | Merged ROM-Set ZIP | Parent + alle Clones in einem ZIP |
| `arcade-zip-split` | E2 | Split ROM-Set ZIP | Nur eigene ROMs |
| `arcade-zip-nonmerged` | E2 | Non-Merged ROM-Set ZIP | Komplett eigenständig |
| `chd-v5` | E2 | CHD v5 mit embedded SHA1 | SHA1 @ offset 0x40 |
| `chd-v4` | E2 | CHD v4 mit SHA1 | Älteres Format |
| `adf-amiga` | E2 | Amiga ADF Disk-Image | 880KB Disk-Header |
| `d64-c64` | E2 | C64 D64 Disk-Image | BAM @ Track 18 |
| `tzx-zx` | E2 | ZX Spectrum TZX | `ZXTape!` Header |
| `dsk-cpc` | E2 | Amstrad CPC DSK | EDSK `EXTENDED` Header |
| `st-atarist` | E2 | Atari ST Disk-Image | ST-Format Header |
| `atr-atari800` | E2 | Atari 800 ATR | ATR-Header `0x9602` |
| `corrupt-zip` | E3 | ZIP mit kaputtem Central-Directory | Für FC-20 Tests |
| `truncated-rom` | E3 | ROM mit korrektem Header aber abgeschnittenen Daten | Für FC-20 Tests |

### 4.2 Validierungs-Tools (neu)

| Tool | Phase | Funktion |
|------|-------|----------|
| `CoverageValidator.cs` | E1 | Prüft Ground Truth gegen alle Coverage-Matrix-Gates |
| `ManifestGenerator.cs` | E1 | Generiert `manifest.json` aus Ground-Truth-JSONL-Dateien automatisch |
| `FallklasseClassifier.cs` | E1 | Ordnet jeden GT-Entry einer Fallklasse (FC-01 bis FC-20) zu, basierend auf Tags + Feldern |
| `PlatformFamilyClassifier.cs` | E1 | Ordnet jeden GT-Entry einer Plattformfamilie (A–E) zu, basierend auf `expected.consoleKey` |

### 4.3 Automatisierungs-Skripte

| Skript | Beschreibung |
|--------|-------------|
| `benchmark/tools/validate-coverage.ps1` | PowerShell-Wrapper: lädt JSONL, ruft CoverageValidator, gibt Gate-Report aus |
| `benchmark/tools/generate-manifest.ps1` | Generiert Manifest aus JSONL-Dateien |
| `benchmark/tools/check-jsonl-schema.ps1` | Validiert alle JSONL-Zeilen gegen `ground-truth.schema.json` |

---

## 5. Manifest-Erweiterungen

### 5.1 Neue Felder im Manifest (`benchmark/manifest.json`)

Das aktuelle Manifest (aus Basis-Plan) enthält nur Basis-Felder. Für Coverage-Tracking werden folgende Felder ergänzt:

```json
{
  "version": "1.2.0",
  "groundTruthVersion": "1.0.0",
  "lastModified": "2026-XX-XX",
  "totalEntries": 1200,

  "bySet": {
    "golden-core": 250,
    "golden-realworld": 350,
    "chaos-mixed": 200,
    "edge-cases": 150,
    "negative-controls": 80,
    "repair-safety": 70,
    "dat-coverage": 100
  },

  "coverage": {
    "systemsCovered": 69,
    "systemsTotal": 69,
    "systemCoveragePercent": 100.0,

    "byPlatformFamily": {
      "cartridge": { "entries": 380, "systems": 35, "gate": 320, "status": "PASS" },
      "disc": { "entries": 310, "systems": 22, "gate": 260, "status": "PASS" },
      "arcade": { "entries": 200, "systems": 3, "subclasses": 8, "gate": 160, "status": "PASS" },
      "computer": { "entries": 150, "systems": 10, "gate": 120, "status": "PASS" },
      "hybrid": { "entries": 80, "systems": 5, "gate": 60, "status": "PASS" }
    },

    "byTier": {
      "tier1": { "systems": 9, "minPerSystem": 20, "actualMin": 20, "gate": 15, "status": "PASS" },
      "tier2": { "systems": 16, "minPerSystem": 8, "actualMin": 8, "gate": 5, "status": "PASS" },
      "tier3": { "systems": 22, "minPerSystem": 3, "actualMin": 3, "gate": 2, "status": "PASS" },
      "tier4": { "systems": 22, "minPerSystem": 2, "actualMin": 2, "gate": 1, "status": "PASS" }
    },

    "byFallklasse": {
      "FC-01": { "entries": 120, "gate": 100, "status": "PASS" },
      "FC-02": { "entries": 40, "gate": 30, "status": "PASS" },
      "...": "..."
    },

    "bySpecialArea": {
      "bios": { "entries": 50, "systems": 12, "gate": 35, "status": "PASS" },
      "arcadeParent": { "entries": 20, "gate": 15, "status": "PASS" },
      "arcadeClone": { "entries": 15, "gate": 10, "status": "PASS" },
      "arcadeSplitMergedNonMerged": { "entries": 30, "gate": 20, "status": "PASS" },
      "psDisambiguation": { "entries": 30, "gate": 20, "status": "PASS" },
      "gbGbcCgb": { "entries": 12, "gate": 8, "status": "PASS" },
      "md32x": { "entries": 8, "gate": 5, "status": "PASS" },
      "multiFileSets": { "entries": 30, "gate": 20, "status": "PASS" },
      "chdRawSha1": { "entries": 8, "gate": 5, "status": "PASS" },
      "datNoIntro": { "entries": 25, "gate": 15, "status": "PASS" },
      "datRedump": { "entries": 25, "gate": 15, "status": "PASS" },
      "datMame": { "entries": 15, "gate": 10, "status": "PASS" },
      "datTosec": { "entries": 10, "gate": 5, "status": "PASS" },
      "directoryBased": { "entries": 10, "gate": 5, "status": "PASS" },
      "headerless": { "entries": 20, "gate": 10, "status": "PASS" }
    },

    "byDifficulty": {
      "easy": 500,
      "medium": 350,
      "hard": 250,
      "adversarial": 100
    }
  },

  "gates": {
    "s1MinimumViableBenchmark": {
      "totalEntries": { "required": 1200, "actual": 1200, "status": "PASS" },
      "systemsCovered": { "required": 69, "actual": 69, "status": "PASS" },
      "fallklassenCovered": { "required": 20, "actual": 20, "status": "PASS" },
      "platformFamiliesAboveGate": { "required": 5, "actual": 5, "status": "PASS" },
      "overallStatus": "PASS"
    }
  }
}
```

### 5.2 Ground-Truth-Felder die besonders wichtig sind

Diese Felder müssen bei **jedem** Eintrag korrekt und vollständig sein, weil sie für Coverage-Berechnung, Gate-Prüfung und Evaluator-Vergleich zentral sind:

| Feld | Warum kritisch | Validierungsregel |
|------|---------------|-------------------|
| `expected.consoleKey` | System-Zuordnung, Plattformfamilie, Tier-Berechnung | Muss in `data/consoles.json` existieren oder `null` sein |
| `expected.category` | BIOS-Gate, Junk-Gate, Sort-Decision | Enum: `Game`, `Bios`, `Junk`, `NonGame`, `Unknown` |
| `expected.confidenceClass` | Confidence-Gating, Repair-Safety | Enum: `high`, `medium`, `low`, `any` |
| `expected.sortingDecision` | Unsafe-Sort-Gate, FC-17 | Enum: `sort`, `block`, `not-applicable` |
| `expected.datMatchLevel` | DAT-Coverage-Gates | Enum: `exact`, `none`, `not-applicable` |
| `expected.dat.ecosystem` | DAT-Ökosystem-Gate (No-Intro/Redump/MAME/TOSEC) | Muss bei `datMatchLevel: "exact"` gesetzt sein |
| `set` | Dataset-Klassen-Zuordnung | Enum der 8 Sets |
| `tags` | Fallklassen-Klassifikation, Spezialbereich-Zuordnung | Array, mindestens 1 Tag |
| `difficulty` | Schwierigkeitsverteilung | Enum: `easy`, `medium`, `hard`, `adversarial` |
| `detectionSources` | Detection-Method-Coverage | Array der erwarteten Methoden |
| `acceptableAlternatives` | Ambiguity-Gate (FC-15) | Array von Alternativ-Erwartungen oder `null` |
| `stub.generator` | Stub-Erzeugung (Reproduzierbarkeit) | Muss registrierten Generator-Namen enthalten |
| `fileModel.container` | Container-Typ-Klassifikation | `single`, `zip`, `7z`, `multi-file`, `directory` |
| `relationships` | Parent/Clone, Multi-Disc, BIOS-Dependency | Für Arcade und Multi-Disc Pflicht |

### 5.3 Fallklassen-Zuordnung per Tags

Jeder GT-Entry wird über seine `tags` einer oder mehreren Fallklassen zugeordnet. Die Zuordnungslogik in `FallklasseClassifier.cs`:

| Tag(s) | → Fallklasse |
|--------|-------------|
| `reference` + (`easy` difficulty) | FC-01 |
| `renamed`, `wrong-name` | FC-02 |
| `header-conflict` | FC-03 |
| `extension-conflict`, `wrong-extension` | FC-04 |
| `folder-conflict`, `folder-vs-header` | FC-05 |
| `dat-exact` | FC-06 |
| `dat-none`, `dat-weak`, `dat-ambiguous` | FC-07 |
| `bios` | FC-08 |
| `parent`, `clone` | FC-09 |
| `multi-disc` | FC-10 |
| `multi-file`, `cue-bin`, `gdi`, `m3u` | FC-11 |
| `archive-inner`, `zip-inner` | FC-12 |
| `directory-game`, `directory-container` | FC-13 |
| `unknown-expected` | FC-14 |
| `ambiguous`, `acceptable-alternative` | FC-15 |
| `negative-control` | FC-16 |
| `repair-unsafe`, `sort-blocked`, `confidence-low` | FC-17 |
| `cross-system`, `ps-disambiguation`, `gb-gbc`, `md-32x` | FC-18 |
| `junk`, `demo`, `beta`, `proto`, `hack`, `homebrew` | FC-19 |
| `corrupt`, `truncated`, `incomplete`, `zero-byte` | FC-20 |

---

## 6. Exit-Kriterien

### 6.1 Pro Phase

| Phase | Einträge gesamt | Spezifische Kriterien | CI-Gate |
|-------|----------------|----------------------|---------|
| **E1** | ≥310 | 69/69 Systeme, BIOS ≥50, PS-Disambig ≥30, golden-core=250 | `CoverageGateTests` grün (E1-Subset) |
| **E2** | ≥540 | Arcade ≥200, Multi-File ≥80, Computer ≥150, CHD ≥25 | `CoverageGateTests` grün (E2-Subset) |
| **E3** | ≥1.060 | golden-realworld ≥350, chaos ≥200, edge ≥150, neg ≥80, Tier-1 ≥20, Tier-2 ≥8 | `CoverageGateTests` grün (E3-Subset) |
| **E4** | **≥1.200** | repair ≥70, dat ≥100, TOSEC ≥10, Headerless ≥20, **alle S1-Gates PASS** | `S1_MinimumViableBenchmark_AllGatesMet` grün |

### 6.2 Gate-Prüfung: Automatische Validierung

Die Coverage-Prüfung läuft als xUnit-Test in CI:

```
dotnet test --filter Category=CoverageGate
```

Dieser Test:
1. Lädt alle `.jsonl`-Dateien aus `benchmark/ground-truth/`
2. Klassifiziert jeden Eintrag nach Plattformfamilie, Tier, Fallklasse, Spezialbereich
3. Vergleicht Ist-Zahlen gegen Gate-Schwellen aus COVERAGE_GAP_AUDIT.md §10
4. Gibt bei FAIL einen strukturierten Report aus:

```
COVERAGE GATE REPORT
====================
Plattformfamilie-Gate:
  ✅ Cartridge: 385/380 (Gate: 320) — PASS
  ✅ Disc: 312/310 (Gate: 260) — PASS
  ❌ Arcade: 155/200 (Gate: 160) — FAIL (benötigt +45)
  ...

Fallklassen-Gate:
  ✅ FC-01: 125/120 (Gate: 100) — PASS
  ❌ FC-08: 38/60 (Gate: 40) — FAIL (benötigt +22)
  ...

Tier-Gate:
  ✅ Tier-1 NES: 22/20 (Gate: 15) — PASS
  ❌ Tier-1 MD: 14/20 (Gate: 15) — FAIL (benötigt +6)
  ...
  
OVERALL: FAIL (3 Violations)
```

### 6.3 Manifest-Konsistenz-Gate

Zusätzlich zum Coverage-Gate prüft ein separater Test:

```
[Fact] Manifest_IsConsistentWithGroundTruth()
```

- Manifest-`totalEntries` == tatsächliche JSONL-Zeilenanzahl
- Manifest-`bySet`-Zahlen == tatsächliche Zeilen pro JSONL-Datei
- Manifest-`systemsCovered` == tatsächliche unique `expected.consoleKey` Werte
- Manifest-`coverage.*`-Gates == `CoverageValidator`-Ergebnis
- Alle IDs in allen JSONL-Dateien sind unique
- Alle IDs folgen dem Namens-Pattern `{set-prefix}-{system}-{subclass}-{nr}`
- Alle JSONL-Zeilen validieren gegen `ground-truth.schema.json`

---

## 7. Alternatives

- **ALT-001**: Monolithische Expansion in einer Phase statt 4 inkrementeller Phasen. Verworfen: Zu hohes Risiko für inkonsistente JSONL-Dateien und zu lange ohne CI-Feedback. Inkrementell erlaubt Korrektur nach jeder Phase.
- **ALT-002**: Generierte Ground-Truth statt handgeschriebener JSONL. Verworfen: Generierte Einträge spiegeln die Annahmen des Generators wider — sie finden keine echten Fehler. Handgeschriebene Einträge basieren auf realen Fehlermodi.
- **ALT-003**: Performance-Scale-Einträge als Teil von S1 zählen. Verworfen: Performance-Scale wird generiert und testet Laufzeit, nicht Korrektheit. Nicht vergleichbar mit handverifizierten Einträgen.
- **ALT-004**: SQL-basierte Coverage-Berechnung statt In-Memory-Validator. Verworfen: Overkill für <5.000 Einträge; JSONL + C# reicht.
- **ALT-005**: Separate JSONL-Dateien pro System statt pro Dataset-Klasse. Verworfen: 69 Dateien statt 7 → unhandlich, Merge-Konflikte bei Cross-System-Fällen, Pipeline wird fragiler.

---

## 8. Dependencies

- **DEP-001**: Basis-Plan `feature-benchmark-testset-1.md` Phasen 1–5 müssen abgeschlossen sein (Ordnerstruktur, Schema, erste 70 Einträge, StubGenerator, Evaluation-Runner).
- **DEP-002**: `data/consoles.json` muss alle 69 Systeme definieren (ist der Fall, verifiziert in COVERAGE_GAP_AUDIT.md §3).
- **DEP-003**: JSON-Schema `ground-truth.schema.json` muss alle erweiterten Felder (`fileModel`, `relationships`, `coverage`-Tags) unterstützen.
- **DEP-004**: StubGenerator muss erweiterbar sein für neue Generator-Typen (Dispatch via `stub.generator`-Feld).
- **DEP-005**: CoverageValidator benötigt Zugriff auf die Gate-Schwellen — entweder hardcodiert oder aus einer Config-Datei (`benchmark/gates.json`).

---

## 9. Files

### Neue Dateien

- **FILE-001**: `benchmark/generators/CoverageValidator.cs` — Prüft Coverage gegen Matrix-Gates
- **FILE-002**: `benchmark/generators/ManifestGenerator.cs` — Erzeugt manifest.json aus JSONL
- **FILE-003**: `benchmark/generators/FallklasseClassifier.cs` — Fallklassen-Zuordnung per Tags
- **FILE-004**: `benchmark/generators/PlatformFamilyClassifier.cs` — Plattformfamilien-Zuordnung
- **FILE-005**: `benchmark/gates.json` — Gate-Schwellen als Konfiguration (maschinenlesbar)
- **FILE-006**: `src/RomCleanup.Tests/Benchmark/CoverageGateTests.cs` — xUnit Coverage-Gate-Tests
- **FILE-007**: `benchmark/tools/validate-coverage.ps1` — PowerShell-Wrapper für Coverage-Prüfung
- **FILE-008**: `benchmark/tools/generate-manifest.ps1` — Manifest-Generator-Skript
- **FILE-009**: `benchmark/tools/check-jsonl-schema.ps1` — JSONL-Schema-Validierung
- **FILE-010**: `benchmark/baselines/s1-baseline.json` — Metrik-Snapshot nach S1

### Bestehende Dateien (erweitert)

- **FILE-011**: `benchmark/ground-truth/golden-core.jsonl` — 70 → 250 Einträge
- **FILE-012**: `benchmark/ground-truth/golden-realworld.jsonl` — 0 → 350 Einträge
- **FILE-013**: `benchmark/ground-truth/edge-cases.jsonl` — 0 → 150 Einträge
- **FILE-014**: `benchmark/ground-truth/chaos-mixed.jsonl` — 0 → 200 Einträge
- **FILE-015**: `benchmark/ground-truth/negative-controls.jsonl` — 0 → 80 Einträge
- **FILE-016**: `benchmark/ground-truth/repair-safety.jsonl` — 0 → 70 Einträge
- **FILE-017**: `benchmark/ground-truth/dat-coverage.jsonl` — 0 → 100 Einträge
- **FILE-018**: `benchmark/manifest.json` — Erweitert um Coverage-Metriken
- **FILE-019**: `benchmark/generators/StubGenerator.cs` — 20+ neue Generator-Methoden
- **FILE-020**: `benchmark/ground-truth/ground-truth.schema.json` — Erweitert um fileModel, relationships

---

## 10. Testing

- **TEST-001**: `CoverageGateTests.AllPlatformFamilyGates_AreMet()` — Prüft alle 5 Familien + systemübergreifend gegen Gate-Schwellen
- **TEST-002**: `CoverageGateTests.AllCaseClassGates_AreMet()` — Prüft alle 20 Fallklassen gegen Minimum-Einträge
- **TEST-003**: `CoverageGateTests.AllTierDepthGates_AreMet()` — Prüft alle 4 Tiers pro System gegen Mindesttiefe
- **TEST-004**: `CoverageGateTests.AllSpecialAreaGates_AreMet()` — Prüft BIOS, Arcade, PS-Disambig, GB-GBC, Multi-File, DAT-Ökosysteme
- **TEST-005**: `CoverageGateTests.S1_MinimumViableBenchmark_AllGatesMet()` — Aggregiert alle Gates; Release-Gate
- **TEST-006**: `CoverageGateTests.Manifest_IsConsistentWithGroundTruth()` — Manifest-Zahlen == tatsächliche JSONL-Counts
- **TEST-007**: `CoverageGateTests.AllIds_AreUnique()` — Keine Duplikat-IDs über alle JSONL-Dateien
- **TEST-008**: `CoverageGateTests.AllIds_FollowNamingConvention()` — ID-Format `{set}-{system}-{subclass}-{nr}`
- **TEST-009**: `CoverageGateTests.AllEntries_ValidateAgainstSchema()` — Jede JSONL-Zeile gegen ground-truth.schema.json
- **TEST-010**: `CoverageGateTests.NoEntryWithoutRequiredTags()` — Jeder Eintrag hat ≥1 klassifizierbaren Tag
- **TEST-011**: `StubGeneratorTests.AllNewGenerators_ProduceValidOutput()` — Jeder neue Generator erzeugt Dateien mit korrekter Mindestgrösse
- **TEST-012**: `StubGeneratorTests.AllNewGenerators_AreDeterministic()` — Zweimaliger Aufruf → byte-identisch
- **TEST-013**: `StubGeneratorTests.DirectoryContainerGenerator_NoPathTraversal()` — Pfad-Validierung für WiiU etc.
- **TEST-014**: `StubGeneratorTests.ArcadeZipGenerator_SetsCrc32Correctly()` — CRC32 in ZIP stimmt mit Ground-Truth überein
- **TEST-015**: `BenchmarkTests.AllGoldenCoreExpanded_MatchGroundTruth()` — Erweiterte golden-core (250) gegen Detection-Pipeline

---

## 11. Risks & Assumptions

- **RISK-001**: **Stub-Generatoren für komplexe Formate (CHD, CSO, Arcade-ZIP) sind fragil.** Minimale Header reichen möglicherweise nicht, um die Detection-Pipeline korrekt zu triggern. Mitigation: Jeder Generator wird mit einem Unit-Test gegen die reale Detection-Pipeline verifiziert. Bei Fehlschlag: Generator anpassen oder als `known-limitation` im Ground-Truth-Entry taggen.
- **RISK-002**: **Handpflege von 1.200 JSONL-Einträgen ist fehleranfällig.** Mitigation: Schema-Validierung bei jedem CI-Lauf. Duplikat-Prüfung automatisiert. Manifest-Konsistenz-Test.
- **RISK-003**: **Fallklassen-Zuordnung per Tags ist subjektiv.** Ein Eintrag könnte mehreren Fallklassen zugeordnet werden. Mitigation: Ein Eintrag zählt in alle Fallklassen, deren Tags er trägt. Double-Counting ist akzeptabel und sogar erwünscht (ein BIOS-Eintrag in edge-cases zählt sowohl für FC-08 als auch für die jeweilige Edge-Case-Klasse).
- **RISK-004**: **Gate-Schwellen sind zu streng und blockieren Releases.** Mitigation: Hard-Fail-Schwellen liegen 15–25% unter den Soll-Werten (z.B. BIOS Soll=50, Gate=35). Nur echte Untererfüllung blockiert.
- **RISK-005**: **Directory-based Stubs (WiiU) erzeugen Komplexität im Generator und im .gitignore.** Mitigation: `benchmark/samples/` ist komplett gitignored. Generator erzeugt Ordner on-the-fly. Cleanup nach Tests via `IDisposable`.

- **ASSUMPTION-001**: Alle 69 Systeme in `data/consoles.json` haben eindeutige Keys und stabile Detection-Methoden.
- **ASSUMPTION-002**: Die Detection-Pipeline akzeptiert Minimal-Stubs (wenige Bytes Header + Padding) als valide Kandidaten.
- **ASSUMPTION-003**: ZIP-Stubs mit korrekten CRC32-Werten werden von der Archive-Content-Detection korrekt verarbeitet.
- **ASSUMPTION-004**: Das Schema `ground-truth.schema.json` wird in Phase 1 des Basis-Plans so erweitert, dass alle in diesem Plan verwendeten Felder abgedeckt sind.

---

## 12. Related Specifications / Further Reading

- [docs/architecture/COVERAGE_GAP_AUDIT.md](../docs/architecture/COVERAGE_GAP_AUDIT.md) — Minimum Coverage Matrix, Gate-Definitionen
- [docs/architecture/GROUND_TRUTH_SCHEMA.md](../docs/architecture/GROUND_TRUTH_SCHEMA.md) — JSONL-Schema, Datenmodell, Felddefinitionen
- [docs/architecture/TESTSET_DESIGN.md](../docs/architecture/TESTSET_DESIGN.md) — Dataset-Klassen, Tier-System, Pflichtfälle
- [docs/architecture/RECOGNITION_QUALITY_BENCHMARK.md](../docs/architecture/RECOGNITION_QUALITY_BENCHMARK.md) — Qualitätsmodell, Metriken M1–M16
- [plan/feature-benchmark-testset-1.md](feature-benchmark-testset-1.md) — Basis-Plan (Infrastruktur + erste 70 Einträge)
