---
goal: Benchmark-Testset Coverage auf Mindestabdeckungs-Matrix ausbauen
version: 1.0
date_created: 2026-03-28
last_updated: 2026-03-28
owner: Architecture
status: 'Completed'
tags: [benchmark, coverage, testing, ground-truth]
---

# Coverage Expansion Plan – Benchmark Testset

![Status: Planned](https://img.shields.io/badge/status-Planned-blue)

Konkreter Ausbauplan fuer das Benchmark-Testset auf Basis des [Coverage Gap Audit](../docs/audits/COVERAGE_GAP_AUDIT.md).
Ziel: Von 2.073 auf ≥2.400 Eintraege (Phase S), mittelfristig auf ≥3.200 (Phase M).

## 1. Requirements & Constraints

- **REQ-001**: Alle neuen Eintraege muessen gegen `ground-truth.schema.json` validieren
- **REQ-002**: Alle neuen Eintraege muessen deterministische `id`-Werte haben (Prefix + System + Subclass + Nummer)
- **REQ-003**: Stub-Generatoren muessen deterministisch sein (keine Guid, kein DateTime)
- **REQ-004**: Bestehende Eintraege duerfen nicht geaendert werden (append-only)
- **REQ-005**: DatasetExpander muss alle neuen Fallklassen generieren koennen
- **REQ-006**: CoverageValidator muss alle neuen Gates pruefen koennen
- **REQ-007**: `analyze-gates.ps1` muss die neuen specialAreas zaehlen
- **REQ-008**: Performance-Scale (5.000 Eintraege) bleibt ausserhalb der Gate-Zaehlung
- **SEC-001**: Keine path-traversal in Stub-Pfaden (bestehende Guard in StubGeneratorDispatch)
- **CON-001**: Nur synthetische/Stub-Daten, keine echten ROMs im Repo
- **CON-002**: Generatoren erzeugen nur minimale Header-Stubs (L1/L2 Realism)
- **CON-003**: CUE/BIN/GDI Generatoren existieren bereits (MultiFileSetGenerator), muessen aber in Ground-Truth verdrahtet werden
- **PAT-001**: Neuer Generator = IStubGenerator implementieren + StubGeneratorRegistry registrieren
- **PAT-002**: Neue Gate = gates.json erweitern + CoverageValidator erweitern + CoverageGateTests erweitern
- **PAT-003**: Neue Tag-Werte zuerst in ground-truth.schema.json aufnehmen, dann FallklasseClassifier aktualisieren
- **GUD-001**: Jede Phase schliesst mit gruenen CoverageGate-Tests ab

## 2. Implementation Steps

### Phase S1 — Disc-Format-Tiefe (KRITISCH, +65 Eintraege)

- GOAL-001: CUE/BIN, GDI, CCD/IMG, MDS/MDF und M3U Testfaelle in Ground-Truth aufnehmen. Schliesst die groesste einzelne Luecke des gesamten Testsets.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-001 | Neue Tags in `ground-truth.schema.json` aufnehmen: bestehende `cue-bin`, `gdi-tracks`, `ccd-img`, `mds-mdf`, `m3u-playlist` sind bereits im Schema definiert — pruefen dass sie tatsaechlich in FallklasseClassifier fuer FC-11 (Multi-File) mitzaehlen | ✅ | 2026-03-29 |
| TASK-002 | **20 CUE/BIN Eintraege** in `golden-realworld.jsonl` ergaenzen: je 4 pro System (PS1, SAT, SCD, PCECD, DC) — davon je 2 `cue-single` und 2 `cue-multi`. Tags: `cue-bin`, `multi-file`, `redump`, `clean-reference`. Difficulty: 10 easy, 5 medium, 5 hard | ✅ | 2026-03-29 |
| TASK-003 | **8 GDI+Tracks Eintraege** in `golden-realworld.jsonl`: 5× DC `gdi-single`, 3× DC `gdi-multi`. Tags: `gdi-tracks`, `multi-file`, `redump`. Difficulty: 4 easy, 2 medium, 2 hard | ✅ | 2026-03-29 |
| TASK-004 | **5 CCD/IMG Eintraege** in `edge-cases.jsonl`: 2× PS1, 2× SAT, 1× SCD. Tags: `ccd-img`, `multi-file`. Difficulty: 3 medium, 2 hard | ✅ | 2026-03-29 |
| TASK-005 | **5 MDS/MDF Eintraege** in `edge-cases.jsonl`: 2× PS1, 2× PS2, 1× DC. Tags: `mds-mdf`, `multi-file`. Difficulty: 3 medium, 2 hard | ✅ | 2026-03-29 |
| TASK-006 | **8 M3U Playlist Eintraege** in `golden-realworld.jsonl`: Multi-Disc-Pointer fuer PS1 (3), PS2 (2), SAT (1), DC (1), PCECD (1). Tags: `m3u-playlist`, `multi-disc`. FileModel: `playlist`. Difficulty: 4 easy, 2 medium, 2 hard | ✅ | 2026-03-29 |
| TASK-007 | **10 Serial-Number Eintraege** in `golden-realworld.jsonl`: PS1 (3), PS2 (3), PSP (2), SAT (1), DC (1). Tags: `serial-number`, `disc-header`. Difficulty: 5 medium, 5 hard | ✅ | 2026-03-29 |
| TASK-008 | **9 Container-Varianten** in `edge-cases.jsonl`: CSO (2× PSP), WIA (2× GC/WII), RVZ (2× GC/WII), WBFS (2× WII), WUX (1× WIIU). Tags: `container-cso`/`container-wia`/`container-rvz`/`container-wbfs`. Difficulty: all medium | ✅ | 2026-03-29 |
| TASK-009 | DatasetExpander: neue Methode `GenerateDiscFormatEntries()` die TASK-002 bis TASK-008 programmatisch generieren kann | ✅ | 2026-03-29 |
| TASK-010 | StubGeneratorRegistry: neuen `CcdImgGenerator` registrieren (IStubGenerator, erzeugt minimale CCD+IMG Pair-Dateien) | ✅ | 2026-03-29 |
| TASK-011 | StubGeneratorRegistry: neuen `MdsMdfGenerator` registrieren (IStubGenerator, erzeugt minimale MDS+MDF Pair-Dateien) | ✅ | 2026-03-29 |
| TASK-012 | StubGeneratorRegistry: neuen `M3uPlaylistGenerator` registrieren (erzeugt M3U-Datei die auf CHD/BIN/ISO Dateien verweist) | ✅ | 2026-03-29 |
| TASK-013 | CoverageGate-Tests: neue Tests `Gate_CueBin`, `Gate_GdiTracks`, `Gate_CcdMds`, `Gate_M3uPlaylist`, `Gate_SerialNumber`, `Gate_ContainerVariants` | ✅ | 2026-03-29 |

### Phase S2 — BIOS-Fehlermodi (KRITISCH, +25 Eintraege)

- GOAL-002: BIOS-Fehlermodi einfuehren: falsch benannt, falsch zugeordnet, Game-als-BIOS, BIOS-als-Game, shared BIOS. Schliesst die zweitkritischste Luecke.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-014 | Neue Tags in `ground-truth.schema.json`: `bios-wrong-name`, `bios-wrong-folder`, `bios-false-positive`, `bios-shared` hinzufuegen (Schema-Erweiterung) | ✅ | 2026-03-29 |
| TASK-015 | FallklasseClassifier: `bios-wrong-name`, `bios-wrong-folder`, `bios-false-positive` als FC-08-Trigger aufnehmen | ✅ | 2026-03-29 |
| TASK-016 | **5 BIOS-falsch-benannt** in `chaos-mixed.jsonl`: PS1 (1), PS2 (1), DC (1), SAT (1), GBA (1). Tags: `bios`, `bios-wrong-name`, `wrong-name`. Difficulty: hard | ✅ | 2026-03-29 |
| TASK-017 | **5 BIOS-falsch-zugeordnet** in `chaos-mixed.jsonl`: BIOS in falschen Ordnern. Tags: `bios`, `bios-wrong-folder`, `folder-header-conflict`. Difficulty: hard | ✅ | 2026-03-29 |
| TASK-018 | **5 Game-als-BIOS** (false positive) in `edge-cases.jsonl`: Games die BIOS-aehnliche Namen tragen. Tags: `bios-false-positive`, `clean-reference`. Expected: category=Game. Difficulty: adversarial | ✅ | 2026-03-29 |
| TASK-019 | **3 BIOS-als-Game** (false negative) in `edge-cases.jsonl`: BIOS ohne typischen BIOS-Namensmuster. Expected: category=Bios. Difficulty: adversarial | ✅ | 2026-03-29 |
| TASK-020 | **3 BIOS-negative** erweitern in `negative-controls.jsonl`: Dateien die BIOS heissen aber keine sind (PDF mit "bios" im Namen). Difficulty: medium | ✅ | 2026-03-29 |
| TASK-021 | **5 Shared-BIOS** in `golden-core.jsonl`: neogeo.zip (ARCADE+NEOGEO), scph5500.bin (PS1 multi-region), dc_bios.bin (DC multi-region). Tags: `bios`, `bios-shared`. Relationships.biosSystemKeys befuellen. Difficulty: medium | ✅ | 2026-03-29 |
| TASK-022 | DatasetExpander: neue Methode `GenerateBiosErrorModes()` fuer TASK-016 bis TASK-021 | ✅ | 2026-03-29 |
| TASK-023 | gates.json: neues Gate `biosErrorModes: { target: 25, hardFail: 18 }` + `biosTotal` von 55→75 / 40→55 erhoehen | ✅ | 2026-03-29 |
| TASK-024 | CoverageValidator + CoverageGateTests: `biosErrorModes`-Gate auswerten | ✅ | 2026-03-29 |
| TASK-025 | analyze-gates.ps1: `biosErrorModes`-Zaehlung ergaenzen (bios-wrong-name, bios-wrong-folder, bios-false-positive, bios-shared Tags zaehlen) | ✅ | 2026-03-29 |

### Phase S3 — Arcade-Konfusion (HOCH, +25 Eintraege)

- GOAL-003: Echte Arcade-Konfusionsfaelle zwischen Split/Merged/Non-Merged Settypen und CHD-basierte Arcade-Spiele einfuehren.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-026 | Neue Tags in Schema: `arcade-confusion-split-merged`, `arcade-confusion-merged-nonmerged`, `arcade-game-chd` | ✅ | 2026-03-29 |
| TASK-027 | **8 Split-als-Merged misklassifiziert** in `edge-cases.jsonl`: Spiel in Merged-Set-Ordner hat nur eigene ROMs (Split). Tags: `arcade-confusion-split-merged`, `arcade-split`. Difficulty: hard. Expected: hasConflict=true | ✅ | 2026-03-29 |
| TASK-028 | **8 Merged-als-Non-Merged misklassifiziert** in `edge-cases.jsonl`: Merged ZIP als Non-Merged behandelt. Tags: `arcade-confusion-merged-nonmerged`, `arcade-merged`. Difficulty: hard | ✅ | 2026-03-29 |
| TASK-029 | **8 CHD-basierte Arcade-Spiele** in `golden-realworld.jsonl`: Spiele die primaer als CHD existieren (area51.chd, kinst.chd, etc.). Tags: `arcade-chd`, `arcade-game-chd`. Difficulty: medium | ✅ | 2026-03-29 |
| TASK-030 | **5 Naomi/Atomiswave Disc-Arcade** in `edge-cases.jsonl`: System=ARCADE, disc-basierte Arcade-Boards. Difficulty: hard | ✅ | 2026-03-29 |
| TASK-031 | DatasetExpander: `GenerateArcadeConfusion()` Methode | ✅ | 2026-03-29 |
| TASK-032 | gates.json: neues Gate `arcadeConfusion: { target: 25, hardFail: 16 }` + `arcadeChdSupplement` → `arcadeChd: { target: 18, hardFail: 12 }` | ✅ | 2026-03-29 |
| TASK-033 | CoverageValidator + analyze-gates.ps1: `arcadeConfusion` und `arcadeChd` Gates | ✅ | 2026-03-29 |

### Phase S4 — Header-vs-Headerless Paare + Cross-System (HOCH, +35 Eintraege)

- GOAL-004: Echte Vergleichspaare Header/Headerless und fehlende Cross-System-Disambiguierungen ergaenzen.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-034 | Neues Tag in Schema: `header-vs-headerless-pair` | ✅ | 2026-03-29 |
| TASK-035 | **15 Header-vs-Headerless Paare** in `edge-cases.jsonl`. Pro System 2 Eintraege (headed + headerless): NES (2), SNES (2), MD (2), A78 (2), LYNX (2), GB (2), GBA (1), N64 (2). Tags: `headerless`, `header-vs-headerless-pair` oder `cartridge-header`, `header-vs-headerless-pair`. Difficulty: medium/hard abwechselnd | ✅ | 2026-03-29 |
| TASK-036 | **8 SAT/DC ISO Cross-System** in `edge-cases.jsonl`: ISO-Dateien die sowohl SAT als auch DC sein koennten. Tags: `cross-system-ambiguity`, `disc-header`. Expected: consoleKey basierend auf IP.BIN-Header. Difficulty: hard | ✅ | 2026-03-29 |
| TASK-037 | **5 PCE/PCECD Disambiguation** in `edge-cases.jsonl`: Dateien die sowohl PCE-HuCard als auch PCECD sein koennten. Difficulty: hard | ✅ | 2026-03-29 |
| TASK-038 | **8 NES iNES/NES2.0/raw** in `edge-cases.jsonl`: 3× iNES v1 Standard, 3× NES 2.0 Header, 2× headerless NES. NesInesGenerator Variants nutzen. Difficulty: medium/hard | ✅ | 2026-03-29 |
| TASK-039 | DatasetExpander: `GenerateHeaderVsHeaderlessPairs()` und `GenerateNewCrossSystemPairs()` | ✅ | 2026-03-29 |
| TASK-040 | gates.json: `headerVsHeaderlessPairs: { target: 25, hardFail: 16 }`, `satDcDisambiguation: { target: 8, hardFail: 5 }`, `pcePcecdDisambiguation: { target: 5, hardFail: 3 }` | ✅ | 2026-03-29 |
| TASK-041 | CoverageValidator + CoverageGateTests + analyze-gates.ps1: alle drei neuen Gates | ✅ | 2026-03-29 |

### Phase S5 — Golden-Core Schwierigkeitsbalance (HOCH, +80 Eintraege)

- GOAL-005: Golden-Core von 0% hard/adversarial auf 15% hard / 5% adversarial anheben.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-042 | **60 hard-Eintraege** in `golden-core.jsonl`: Pro Tier-1-System (9 Systeme) je 5 harte Faelle + 15 verteilt auf Tier-2. Mischung aus: wrong-name mit unique-ext (rettet Header), folder-header-conflict, headerless mit DAT, extension-conflict. Difficulty: hard | ✅ | 2026-03-29 |
| TASK-043 | **20 adversarial-Eintraege** in `golden-core.jsonl`: Pro Tier-1-System 1-2 adversarial Faelle. Mischung aus: cross-system + wrong-extension + wrong-folder Triple-Threat, headerless + no-DAT + ambiguous-extension. Difficulty: adversarial | ✅ | 2026-03-29 |
| TASK-044 | DatasetExpander: `GenerateGoldenCoreHardEntries()` und `GenerateGoldenCoreAdversarialEntries()` | ✅ | 2026-03-29 |
| TASK-045 | gates.json: neuer Gate-Typ `difficultyDistribution` mit: `easyMax: { target: 0.55, hardFail: 0.60 }`, `mediumMin: { target: 0.25, hardFail: 0.20 }`, `hardMin: { target: 0.12, hardFail: 0.10 }`, `adversarialMin: { target: 0.01, hardFail: 0.005 }` | ✅ | 2026-03-29 |
| TASK-046 | GateConfiguration Model: neue Property `DifficultyDistribution` mit Threshold-Records | ✅ | 2026-03-29 |
| TASK-047 | CoverageValidator: Difficulty-Ratio-Pruefung implementieren | ✅ | 2026-03-29 |
| TASK-048 | CoverageGateTests: `Gate_DifficultyDistribution_MeetsHardFail` (Theory×4), `Gate_DifficultyDistribution_EasyNotDominant`, `Gate_DifficultyDistribution_HardAndAdversarialPresent` | ✅ | 2026-03-29 |

### Phase S6 — Negative Controls + NonGame Upgrade (MITTEL, +30 Eintraege)

- GOAL-006: Negative Controls von generisch auf spezifisch umbauen, NonGame-Tiefe ergaenzen.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-049 | **15 reale Nicht-ROM-Dateitypen** in `negative-controls.jsonl`: PDF (2), JPEG/JFIF (2), PNG (2), TXT/NFO (3), SFV/CRC (2), EXE/DLL (2), MP3 (2). Tags: `negative-control`, `non-game`. NonRomContentGenerator Varianten nutzen/erweitern. Expected: category=Unknown oder Junk. Difficulty: easy/medium | ✅ | 2026-03-29 |
| TASK-050 | NonRomContentGenerator: Varianten `mp3`, `sfv`, `nfo`, `txt` ergaenzen | ✅ | 2026-03-29 |
| TASK-051 | **8 Demo-Eintraege** in `golden-realworld.jsonl`: NES Demo (2), SNES Demo (2), PS1 Demo (2), GBA Demo (2). Tags: `demo`, `non-game`. Expected: category=NonGame. Difficulty: easy | ✅ | 2026-03-29 |
| TASK-052 | **4 Hack-Eintraege** in `golden-realworld.jsonl`: NES Hack (2), SNES Hack (2). Tags: `hack`, `non-game`. Expected: category=NonGame. Difficulty: medium | ✅ | 2026-03-29 |
| TASK-053 | **3 Utility-Eintraege** in `negative-controls.jsonl`: Cheat-Device ROMs (Action Replay, Game Genie). Tags: `non-game`, `negative-control`. Expected: category=NonGame. Difficulty: medium | ✅ | 2026-03-29 |
| TASK-054 | gates.json: FC-16 (Negative Control) von 55/40 → 70/50, FC-19 (Junk/NonGame) von 170/133 → 200/155 | ✅ | 2026-03-29 |
| TASK-055 | CoverageGateTests: Gate-Werte fuer FC-16 und FC-19 aktualisieren (91/91 pass) | ✅ | 2026-03-29 |

### Phase M1 — Computer-Format-Tiefe (MITTEL, +35 Eintraege)

- GOAL-007: Computer-Systeme mit fehlender Format-Vielfalt und Directory-Erkennung ergaenzen.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-056 | **8 Amiga WHDLoad-Directories** in `golden-realworld.jsonl`: WHDLoad Games mit Verzeichnisstruktur (game.slave + data). Tags: `directory-based`, `folder-only-detection`. FileModel: `directory`. Difficulty: 4 medium, 4 hard | ✅ | 2026-03-29 |
| TASK-057 | **5 C64 Format-Vielfalt** in `edge-cases.jsonl`: .d64 (1), .t64 (1), .crt (1), .prg (1), .tap (1). Pro Format ein Eintrag. Difficulty: medium | ✅ | 2026-03-29 |
| TASK-058 | **4 ZX Spectrum Formate** in `edge-cases.jsonl`: .tzx (1), .tap (1), .z80 (1), .sna (1). Difficulty: medium | ✅ | 2026-03-29 |
| TASK-059 | **3 MSX Formate** in `edge-cases.jsonl`: .rom (1), .dsk (1), .cas (1). Difficulty: medium | ✅ | 2026-03-29 |
| TASK-060 | **3 Atari ST Formate** in `edge-cases.jsonl`: .st (1), .stx (1), .msa (1). Difficulty: medium | ✅ | 2026-03-29 |
| TASK-061 | **3 PC-98 Formate** in `edge-cases.jsonl`: .fdi (1), .hdi (1), .d88 (1). Difficulty: medium | ✅ | 2026-03-29 |
| TASK-062 | **3 X68K Formate** in `edge-cases.jsonl`: .dim (1), .xdf (1), .2hd (1). Difficulty: medium | ✅ | 2026-03-29 |
| TASK-063 | **12 Keyword-only Detection** in `golden-realworld.jsonl`: Computer-Systeme wo nur Keyword im Dateipfad das System identifiziert. Tags: `keyword-detection`. Pro System 1-2 Eintraege (AMIGA, C64, ZX, MSX, CPC, DOS, A800, ATARIST). Difficulty: hard | ✅ | 2026-03-29 |
| TASK-064 | gates.json: `directoryBased` von 33/24 → 45/32, `keywordOnly: { target: 20, hardFail: 12 }` | ✅ | 2026-03-29 |
| TASK-065 | DatasetExpander: `GenerateComputerFormatDepth()` und `GenerateKeywordOnlyDetection()` | ✅ | 2026-03-29 |

### Phase M2 — Manifest & Gate Konsolidierung

- GOAL-008: manifest.json, gates.json, CoverageValidator, analyze-gates.ps1 und CoverageGateTests vollstaendig auf die neue Coverage-Matrix aktualisieren.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-066 | `benchmark/manifest.json`: `totalEntries`, `bySet`-Zaehler aktualisieren | ✅ | 2026-03-29 |
| TASK-067 | `benchmark/gates.json`: alle erhoeungten FC-Werte konsolidieren (FC-03: 35→45/24→32, FC-08: 55→75/40→55, FC-11: 25→35/16→25, FC-13: 35→45/24→32, FC-18: 100→120/76→90, FC-20: 85→100/64→75) | ✅ | 2026-03-29 |
| TASK-068 | `benchmark/gates.json`: `totalEntries` target auf 2400 / hardFail auf 2000 erhoehen | ✅ | 2026-03-29 |
| TASK-069 | GateConfiguration Model: alle neuen Gate-Properties aufnehmen (`DifficultyDistribution`, `BiosErrorModes`, `ArcadeConfusion`, `SatDcDisambiguation`, `PcePcecdDisambiguation`, `CueBin`, `GdiTracks`, `CcdMds`, `M3uPlaylist`, `SerialNumber`, `HeaderVsHeaderlessPairs`, `ContainerVariants`, `KeywordOnly`) | ✅ | 2026-03-29 |
| TASK-070 | CoverageValidator: alle 12 neuen specialArea-Zaehler implementieren | ✅ | 2026-03-29 |
| TASK-071 | analyze-gates.ps1: alle neuen specialArea-Tags zaehlen und ausgeben | ✅ | 2026-03-29 |
| TASK-072 | CoverageGateTests: alle neuen Gate-Tests sicherstellen dass sie gegen aktuellen Datensatz validieren | ✅ | 2026-03-29 |
| TASK-073 | `Invoke-CoverageGate.ps1` ausfuehren — alle Gates muessen gruen sein | ✅ | 2026-03-29 |

### Phase M3 — Region-/Revisions-Tiefe + Corrupt/Truncated (MITTEL, +55 Eintraege)

- GOAL-009: Region- und Revisionsvarianten sowie kaputte Dateien vertiefen.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-074 | **30 Region-Varianten-Gruppen** in `golden-realworld.jsonl`: 10 Spiele × 3 Regionen (USA/EUR/JPN). Tier-1-Systeme bevorzugt. Tags: `region-variant`, `clean-reference`. Difficulty: easy/medium | ✅ | 2026-03-29 |
| TASK-075 | **15 Revision-Varianten** in `golden-realworld.jsonl`: 5 Spiele × 3 Revisionen (Rev0/Rev1/Rev2). Tags: `revision-variant`, `clean-reference`. Difficulty: easy/medium | ✅ | 2026-03-29 |
| TASK-076 | **10 Corrupt-Varianten** in `chaos-mixed.jsonl`: verschiedene Korruptionsarten (Header-corrupt, Truncated-at-50%, Zero-filled, Random-corrupt). Tags: `corrupt`, `truncated`, `broken-set`. Difficulty: adversarial | ✅ | 2026-03-29 |
| TASK-077 | gates.json: FC-20 (Kaputte Sets) von 85→100 / 64→75 | ✅ | 2026-03-29 |

### Phase M4 — SNES ROM-Types + Copier-Header (NIEDRIG, +14 Eintraege)

- GOAL-010: SNES LoROM/HiROM/ExHiROM und Copier-Header fuer Cartridge-Systeme ergaenzen.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-078 | SnesHeaderGenerator: Varianten `lorom`, `hirom`, `exhirom` sicherstellen (oder ergaenzen) | ✅ | 2026-03-29 |
| TASK-079 | **6 SNES ROM-Type Eintraege** in `edge-cases.jsonl`: 2× LoROM, 2× HiROM, 2× ExHiROM. Tags: `cartridge-header`. Difficulty: medium | ✅ | 2026-03-29 |
| TASK-080 | **8 Copier-Header ROMs** in `edge-cases.jsonl`: NES (2), SNES (2), MD (2), SFC (2) mit 512-byte Copier-Header-Prefix. Tags: `headerless`, `cartridge-header`. Difficulty: hard. Expected: System korrekt trotz Prefix | ✅ | 2026-03-29 |
| TASK-081 | NesInesGenerator: Variante `copier-header` (512 Byte Prefix + iNES Header) | ✅ | 2026-03-29 |

## 3. Alternatives

- **ALT-001**: Statt DatasetExpander programmatisch zu erweitern, koennten JSONL-Dateien manuell editiert werden. Abgelehnt: zu fehleranfaellig bei 300+ neuen Eintraegen, nicht reproduzierbar.
- **ALT-002**: Alle neuen Formate (CCD, MDS, M3U) als reine Tags ohne echte Generatoren. Abgelehnt: Tags allein testen kein Parsing; Stub-Generatoren sind noetig fuer End-to-End-Benchmark-Runs.
- **ALT-003**: Disc-Format-Tests nur als Unit-Tests statt Ground-Truth-Eintraege. Abgelehnt: die Benchmark-Pipeline testet den vollen Pfad (Detection → Classification → Sorting Decision), Unit-Tests decken nur Erkennung ab.
- **ALT-004**: Golden-Core Difficulty-Balance durch Verschieben bestehender Eintraege statt neue hinzuzufuegen. Abgelehnt: CON REQ-004 (append-only), und bestehende Eintraege haben validierte expected-Werte.

## 4. Dependencies

- **DEP-001**: `ground-truth.schema.json` Tag-Enum muss vor jeder JSONL-Erweiterung aktualisiert werden
- **DEP-002**: `FallklasseClassifier.cs` muss neue Tags zu FC-Klassen mappen bevor CoverageGateTests laufen
- **DEP-003**: `GateConfiguration.cs` muss neue Gate-Properties haben bevor CoverageValidator kompiliert
- **DEP-004**: `StubGeneratorRegistry.cs` muss neue Generatoren registrieren bevor DatasetExpansionTests laufen
- **DEP-005**: `MultiFileSetGenerator.cs` existiert bereits mit CUE/BIN und GDI Varianten — keine neue Implementierung noetig, nur JSONL-Verdrahtung
- **DEP-006**: `NonRomContentGenerator.cs` hat bereits jfif, png, pdf, mz, elf — muss um mp3, sfv, nfo, txt erweitert werden
- **DEP-007**: `SegaIpBinGenerator.cs` hat saturn, dreamcast, segacd Varianten — direkt nutzbar fuer SAT/DC Cross-System
- **DEP-008**: Phase M2 (Gate-Konsolidierung) haengt von S1-S6 ab — muss nach allen Phase-S-Tasks ausgefuehrt werden

## 5. Files

### Schema & Konfiguration
- **FILE-001**: `benchmark/ground-truth/ground-truth.schema.json` — Tag-Enum erweitern (Phasen S1-S4)
- **FILE-002**: `benchmark/gates.json` — Neue Gates + erhoehte Schwellen (Phasen S1-S6, M2)
- **FILE-003**: `benchmark/manifest.json` — Zaehler aktualisieren (Phase M2)

### Ground-Truth JSONL (Erweiterung, append-only)
- **FILE-004**: `benchmark/ground-truth/golden-core.jsonl` — +80 hard/adversarial + 5 shared-BIOS (Phasen S2, S5)
- **FILE-005**: `benchmark/ground-truth/golden-realworld.jsonl` — +68 (CUE/BIN, GDI, M3U, Serial, Arcade-CHD, Demos, Hacks, WHDLoad, Keyword, Regions, Revisions) (Phasen S1, S3, S6, M1, M3)
- **FILE-006**: `benchmark/ground-truth/edge-cases.jsonl` — +85 (CCD, MDS, Container, Arcade-Konfusion, Header-Paare, Cross-System, Computer-Formate, SNES-Types) (Phasen S1, S3, S4, M1, M4)
- **FILE-007**: `benchmark/ground-truth/chaos-mixed.jsonl` — +20 (BIOS-Fehlermodi, Corrupt) (Phasen S2, M3)
- **FILE-008**: `benchmark/ground-truth/negative-controls.jsonl` — +21 (Nicht-ROM-Typen, BIOS-negative, Utilities) (Phasen S2, S6)
- **FILE-009**: `benchmark/ground-truth/repair-safety.jsonl` — keine Aenderungen in Phase S/M

### Generatoren (neu)
- **FILE-010**: `src/RomCleanup.Tests/Benchmark/Generators/Disc/CcdImgGenerator.cs` — NEU (Phase S1)
- **FILE-011**: `src/RomCleanup.Tests/Benchmark/Generators/Disc/MdsMdfGenerator.cs` — NEU (Phase S1)
- **FILE-012**: `src/RomCleanup.Tests/Benchmark/Generators/Disc/M3uPlaylistGenerator.cs` — NEU (Phase S1)

### Generatoren (erweitern)
- **FILE-013**: `src/RomCleanup.Tests/Benchmark/Generators/NonRomContentGenerator.cs` — +4 Varianten (Phase S6)
- **FILE-014**: `src/RomCleanup.Tests/Benchmark/Generators/StubGeneratorRegistry.cs` — 3 neue Registrierungen (Phase S1)
- **FILE-015**: `src/RomCleanup.Tests/Benchmark/Generators/Cartridge/NesInesGenerator.cs` — +1 Variante `copier-header` (Phase M4)
- **FILE-016**: `src/RomCleanup.Tests/Benchmark/Generators/Cartridge/SnesHeaderGenerator.cs` — Varianten pruefen/ergaenzen (Phase M4)

### Infrastructure (erweitern)
- **FILE-017**: `src/RomCleanup.Tests/Benchmark/Infrastructure/DatasetExpander.cs` — 8 neue Methoden (Phasen S1-S6, M1)
- **FILE-018**: `src/RomCleanup.Tests/Benchmark/Infrastructure/CoverageValidator.cs` — 12 neue specialArea-Zaehler + Difficulty-Ratio (Phasen S1-S5, M2)
- **FILE-019**: `src/RomCleanup.Tests/Benchmark/Infrastructure/FallklasseClassifier.cs` — neue Tag-Mappings (Phasen S1-S4)
- **FILE-020**: `src/RomCleanup.Tests/Benchmark/Models/GateConfiguration.cs` — neue Gate-Properties (Phasen S1-S5, M2)

### Tests (erweitern)
- **FILE-021**: `src/RomCleanup.Tests/Benchmark/CoverageGateTests.cs` — 15+ neue Gate-Tests (Phasen S1-S6, M2)
- **FILE-022**: `src/RomCleanup.Tests/Benchmark/DatasetExpansionTests.cs` — neue Assertions fuer erweiterte Gates (Phase M2)

### Tools (erweitern)
- **FILE-023**: `benchmark/tools/analyze-gates.ps1` — neue specialArea-Zaehler (Phase M2)

## 6. Testing

### Automatisierte Gate-Tests (pro Phase)
- **TEST-001**: Nach jeder Phase: `dotnet test --filter Category=CoverageGate` muss gruen sein
- **TEST-002**: Nach jeder Phase: `pwsh benchmark/tools/analyze-gates.ps1` muss alle neuen Gates mit IST >= hardFail zeigen
- **TEST-003**: Schema-Validierung: alle neuen JSONL-Eintraege muessen gegen `ground-truth.schema.json` validieren (SchemaValidator)
- **TEST-004**: ID-Einmaligkeitspruefung: keine doppelten IDs ueber alle JSONL-Dateien hinweg

### Generator-Tests
- **TEST-005**: CcdImgGenerator: erzeugt valide CCD+IMG Dateipaar-Struktur
- **TEST-006**: MdsMdfGenerator: erzeugt valide MDS+MDF Dateipaar-Struktur
- **TEST-007**: M3uPlaylistGenerator: erzeugt M3U Datei mit korrekten Pfaden
- **TEST-008**: NonRomContentGenerator neue Varianten: mp3/sfv/nfo/txt erzeugen korrekte Magic Bytes
- **TEST-009**: NesInesGenerator `copier-header`: 512-Byte Prefix + iNES Header

### Regressions-Tests
- **TEST-010**: Alle bestehenden CoverageGate-Tests bleiben gruen (bestehende Eintraege unveraendert)
- **TEST-011**: DatasetExpansionTests: erweiterte Assertions fuer ≥2.400 Eintraege, ≥65 Systeme, ≥20 Fallklassen
- **TEST-012**: Difficulty-Distribution-Gate: easy ≤55%, medium ≥20%, hard ≥12%, adversarial ≥5%

### Manueller Gate-Run (nach Phase M2)
- **TEST-013**: `pwsh benchmark/tools/Invoke-CoverageGate.ps1` — vollstaendiger Gate-Durchlauf, 0 Failures

## 7. Risks & Assumptions

- **RISK-001**: Neue JSONL-Eintraege koennten Schema-Validierung brechen wenn Tags nicht zuerst im Schema stehen → **Mitigation**: Immer zuerst Schema erweitern (TASK-001, TASK-014, TASK-026, TASK-034)
- **RISK-002**: DatasetExpander Aenderungen koennten bestehende Entry-IDs verschieben → **Mitigation**: `_existingIds`-Guard verhindert Duplikate, append-only Logik
- **RISK-003**: Neue Generatoren koennten nicht-deterministische Ausgabe erzeugen → **Mitigation**: Keine `Random()` ohne fixen Seed, keine `DateTime.Now`, keine `Guid.NewGuid()`
- **RISK-004**: Gate-Erhoehungen koennten bestehende CI brechen bevor neue Eintraege vorhanden sind → **Mitigation**: Gates erst in Phase M2 final hochziehen, nachdem alle Eintraege ergaenzt sind
- **RISK-005**: MultiFileSetGenerator erzeugt bereits CUE/BIN und GDI, aber nur als Stub-Dateien — echtes Format-Parsing der Detection-Pipeline ist damit nicht End-to-End getestet → **Mitigation**: Akzeptabel fuer Coverage-Gate, echte Format-Tests brauchen zusaetzlich Integrations-Tests (out of scope)
- **ASSUMPTION-001**: Die Detection-Pipeline kann stub-generierte Dateien erkennen (L1/L2 Realism reicht)
- **ASSUMPTION-002**: Alle 65 Systeme aus consoles.json bleiben stabil — keine Umbenennungen oder Loeschungen waehrend des Ausbaus
- **ASSUMPTION-003**: Performance-Scale (5.000 Eintraege) bleibt von Gate-Zaehlung ausgeschlossen

## 8. Related Specifications / Further Reading

- [Coverage Gap Audit](../docs/audits/COVERAGE_GAP_AUDIT.md)
- [Testset Design](../docs/architecture/TESTSET_DESIGN.md)
- [Recognition Quality Benchmark](../docs/architecture/RECOGNITION_QUALITY_BENCHMARK.md)
- [Ground-Truth Schema](../benchmark/ground-truth/ground-truth.schema.json)
- [Original Benchmark Plan](../archive/completed/plans/feature-benchmark-testset-1.md)
- [Detection Limits](../benchmark/docs/detection-limits.md)

---

## Anhang A: Generatoren-Uebersicht

### Bestehende Generatoren (23)

| ID | Typ | Systeme | Varianten |
|----|-----|---------|-----------|
| `nes-ines` | Cartridge | NES | standard, headerless, truncated |
| `snes-header` | Cartridge | SNES | standard, lorom(?), hirom(?) |
| `n64-header` | Cartridge | N64 | standard |
| `gba-header` | Cartridge | GBA | standard |
| `gb-header` | Cartridge | GB, GBC | standard |
| `md-header` | Cartridge | MD, 32X | standard |
| `lynx-header` | Cartridge | LYNX | standard |
| `atari7800-header` | Cartridge | A78 | standard |
| `ps1-pvd` | Disc | PS1 | standard, ps2-header, no-system-id |
| `ps2-pvd` | Disc | PS2 | standard, psp, no-system-id |
| `ps3-pvd` | Disc | PS3 | standard |
| `sega-ipbin` | Disc | SAT, DC, SCD | saturn, dreamcast, segacd |
| `nintendo-disc` | Disc | GC, WII | standard |
| `multi-file-set` | Disc | Alle Disc | cue-single, cue-multi, gdi-single, gdi-multi |
| `opera-fs` | Disc | 3DO | standard |
| `boot-sector-text` | Disc | diverse | standard |
| `xdvdfs` | Disc | XBOX, X360 | standard |
| `fmtowns-pvd` | Disc | FMTOWNS | standard |
| `cdi-disc` | Disc | CDI | standard |
| `ext-only` | Utility | Alle | default, empty |
| `random-bytes` | Utility | Alle | (seedbar) |
| `non-rom-content` | Utility | Negative | jfif, png, pdf, mz, elf |

### Neue Generatoren (3 + Erweiterungen)

| ID | Typ | Datei | Varianten | Phase |
|----|-----|-------|-----------|-------|
| `ccd-img` | Disc | CcdImgGenerator.cs | standard (CCD+IMG Paar) | S1 |
| `mds-mdf` | Disc | MdsMdfGenerator.cs | standard (MDS+MDF Paar) | S1 |
| `m3u-playlist` | Disc | M3uPlaylistGenerator.cs | standard (M3U mit CHD/ISO/BIN Referenzen) | S1 |
| `non-rom-content` +4 | Utility | NonRomContentGenerator.cs | +mp3, +sfv, +nfo, +txt | S6 |
| `nes-ines` +1 | Cartridge | NesInesGenerator.cs | +copier-header | M4 |

---

## Anhang B: Mindestmengen pro JSONL-Datei nach Ausbau

| Datei | IST | Phase S Ziel | Phase M Ziel | Beschreibung |
|-------|-----|-------------|-------------|--------------|
| `golden-core.jsonl` | 648 | 753 (+105) | 753 | Referenz-Kern + hard/adversarial + shared-BIOS |
| `golden-realworld.jsonl` | 493 | 579 (+86) | 624 (+131) | Disc-Formate, Arcade-CHD, Demos, WHDLoad, Keyword, Regions, Revisions |
| `edge-cases.jsonl` | 198 | 279 (+81) | 320 (+122) | CCD/MDS, Container, Arcade-Konfusion, Header-Paare, Cross-System, Computer-Formate, SNES-Types |
| `chaos-mixed.jsonl` | 204 | 214 (+10) | 224 (+20) | BIOS-Fehlermodi, Corrupt |
| `negative-controls.jsonl` | 80 | 101 (+21) | 101 | Nicht-ROM-Typen, BIOS-negative, Utilities |
| `dat-coverage.jsonl` | 350 | 350 (±0) | 350 | Unveraendert in Phase S+M |
| `repair-safety.jsonl` | 100 | 100 (±0) | 100 | Unveraendert in Phase S+M |
| `performance-scale.jsonl` | 5000 | 5000 (±0) | 5000 | Unveraendert (exkl. Gate-Zaehlung) |
| **GESAMT (exkl. perf)** | **2073** | **2376 (+303)** | **2472 (+399)** |

---

## Anhang C: Exit-Kriterien pro Phase

### Phase S1 (Disc-Format-Tiefe)
- [ ] ≥20 CUE/BIN Eintraege in Ground-Truth
- [ ] ≥8 GDI Eintraege
- [ ] ≥5 CCD/IMG Eintraege
- [ ] ≥5 MDS/MDF Eintraege
- [ ] ≥8 M3U Eintraege
- [ ] ≥10 Serial-Number Eintraege (gesamt ≥16)
- [ ] ≥9 Container-Varianten (gesamt ≥16)
- [ ] CcdImgGenerator, MdsMdfGenerator, M3uPlaylistGenerator existieren und sind registriert
- [ ] `dotnet test --filter Category=CoverageGate` gruen
- [ ] Schema-Validierung aller neuen Eintraege bestanden

### Phase S2 (BIOS-Fehlermodi)
- [ ] ≥25 BIOS-Fehlermodus-Eintraege (bios-wrong-name + bios-wrong-folder + bios-false-positive + bios-shared)
- [ ] biosTotal ≥75
- [ ] biosErrorModes Gate gruen
- [ ] `dotnet test --filter Category=CoverageGate` gruen

### Phase S3 (Arcade-Konfusion)
- [ ] ≥16 Arcade-Konfusions-Eintraege
- [ ] ≥8 CHD-basierte Arcade-Spiele (gesamt arcadeChd ≥16)
- [ ] arcadeConfusion und arcadeChd Gates gruen
- [ ] `dotnet test --filter Category=CoverageGate` gruen

### Phase S4 (Header/Headerless + Cross-System)
- [ ] ≥15 Header-vs-Headerless Paare
- [ ] ≥8 SAT/DC Disambiguation
- [ ] ≥5 PCE/PCECD Disambiguation
- [ ] ≥8 NES iNES/NES2.0 Eintraege
- [ ] headerVsHeaderlessPairs, satDcDisambiguation, pcePcecdDisambiguation Gates gruen
- [ ] `dotnet test --filter Category=CoverageGate` gruen

### Phase S5 (Golden-Core Schwierigkeit)
- [ ] Golden-Core easy ≤60%
- [ ] Golden-Core hard ≥15% (≥110 Eintraege)
- [ ] Golden-Core adversarial ≥5% (≥38 Eintraege)
- [ ] DifficultyDistribution Gate gruen
- [ ] `dotnet test --filter Category=CoverageGate` gruen

### Phase S6 (Negative Controls + NonGame)
- [ ] ≥15 reale Nicht-ROM Negative Controls
- [ ] ≥8 Demo Eintraege
- [ ] FC-16 ≥50, FC-19 ≥155
- [ ] `dotnet test --filter Category=CoverageGate` gruen

### Phase M1 (Computer-Format-Tiefe)
- [ ] ≥8 WHDLoad-Directory Eintraege
- [ ] ≥21 Computer-Format-Varianten (C64+ZX+MSX+ST+PC98+X68K)
- [ ] ≥12 Keyword-only Eintraege
- [ ] directoryBased ≥32, keywordOnly ≥12
- [ ] `dotnet test --filter Category=CoverageGate` gruen

### Phase M2 (Gate Konsolidierung)
- [ ] `benchmark/manifest.json` aktualisiert
- [ ] `benchmark/gates.json` alle 12 neuen + 8 erhoehte Gates
- [ ] GateConfiguration Model komplett
- [ ] CoverageValidator alle neuen Zaehler
- [ ] analyze-gates.ps1 alle neuen Zaehler
- [ ] `pwsh benchmark/tools/Invoke-CoverageGate.ps1` exit code 0
- [ ] `dotnet test --filter Category=CoverageGate` gruen — **ALLE Gates**

### Phase M3 (Region/Revision/Corrupt)
- [ ] ≥30 neue Region-Varianten
- [ ] ≥15 neue Revision-Varianten
- [ ] ≥10 neue Corrupt-Varianten
- [ ] `dotnet test --filter Category=CoverageGate` gruen

### Phase M4 (SNES/Copier)
- [ ] ≥6 SNES ROM-Type Eintraege
- [ ] ≥8 Copier-Header Eintraege
- [ ] `dotnet test --filter Category=CoverageGate` gruen

---

## Anhang D: Coverage nach vollstaendigem Ausbau (Zielwerte)

| Gate | IST | Phase S | Phase M | Target |
|------|-----|---------|---------|--------|
| totalEntries | 2073 | 2376 | 2472 | 2400 |
| systemsCovered | 65 | 65 | 65 | 65 |
| fallklassenCovered | 20 | 20 | 20 | 20 |
| biosTotal | 60 | 85 | 85 | 75 |
| biosErrorModes | 0 | 25 | 25 | 25 |
| arcadeConfusion | 0 | 21 | 21 | 25 |
| arcadeChd | 18 | 26 | 26 | 18 |
| cueBin | 0 | 20 | 20 | 20 |
| gdiTracks | 0 | 8 | 8 | 8 |
| ccdMds | 0 | 10 | 10 | 10 |
| m3uPlaylist | 0 | 8 | 8 | 8 |
| serialNumber | 6 | 16 | 16 | 15 |
| headerVsHeaderlessPairs | 0 | 15 | 15 | 25 |
| satDcDisambiguation | 0 | 8 | 8 | 8 |
| pcePcecdDisambiguation | 0 | 5 | 5 | 5 |
| containerVariants | 7 | 16 | 16 | 20 |
| keywordOnly | 8 | 8 | 20 | 20 |
| directoryBased | 30 | 30 | 38 | 45 |
| easy% | 60% | ~50% | ~48% | ≤55% |
| hard% | 12% | ~18% | ~19% | ≥12% |
| adversarial% | 5% | ~7% | ~8% | ≥5% |
