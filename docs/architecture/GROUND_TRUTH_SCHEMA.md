# Ground-Truth & Manifest Schema — Definitive Spezifikation

> **Version:** 1.0.0  
> **Status:** Aktiv / Implementiert  
> **Erstellt:** 2026-03-22  
> **Bezug:** [TESTSET_DESIGN.md](TESTSET_DESIGN.md), [RECOGNITION_QUALITY_BENCHMARK.md](RECOGNITION_QUALITY_BENCHMARK.md), [plan/feature-benchmark-testset-1.md](../plan/feature-benchmark-testset-1.md)

---

## 1. Executive Verdict

### 1.1 Formatempfehlung: JSONL (eine Zeile pro Sample)

**JSONL ist das einzig tragfähige Format für diesen Anwendungsfall.** Alternativen wurden geprüft und verworfen:

| Format | Vorteil | Ausschlussgrund |
|--------|---------|-----------------|
| JSON (monolithisch) | Schema-Validierung einfach | Kein zeilenweiser Diff; ein Fehler korruptiert alles; merge-unfreundlich |
| YAML | Menschenlesbar | Indent-sensitiv → Merge-Konflikte; langsam bei >1000 Einträgen; C#-Parsing-Overhead |
| CSV | Einfach | Keine geschachtelten Objekte; keine Arrays; CSV-Injection-Risiko |
| Parquet/SQLite | Performant | Nicht menschenpflegbar; binär → kein Git-Diff; Overkill für <20k Einträge |

**JSONL gewinnt, weil:**
- Jede Zeile ist ein unabhängiges JSON-Objekt → ein korrupter Eintrag killt nicht den Rest
- Git-Diff zeigt exakt geänderte/hinzugefügte Testfälle
- Maschinenlesbar (C# `System.Text.Json`, `jq`, Python, PowerShell)
- Menschenpflegbar (jede Zeile im Editor validierbar)
- Schema-Validierung via JSON-Schema pro Zeile möglich
- Filterung mit Standard-Tools (`jq '.expected.consoleKey == "NES"'`, `grep`)

### 1.2 Warum breite Plattformabdeckung nicht verhandelbar ist

RomCleanup erkennt **69 Systeme** über eine 8-stufige Detection-Cascade (FolderName → UniqueExt → AmbiguousExt → DiscHeader → ArchiveContent → CartridgeHeader → Serial → Keyword). Ein Schema, das nur Cartridge-ROMs und ISO-Discs modelliert, deckt bestenfalls 40% der realen Fehlermodi ab. Die kritischsten Verwechslungen passieren an den Rändern:

- **Arcade vs. Neo Geo AES/MVS vs. Neo Geo CD** — gleiche IPs, verschiedene Plattformen
- **GB vs. GBC** — CGB-Flag Dual-Mode ist binär ambig
- **Genesis vs. 32X** — identischer Header-Offset 0x100
- **PS1 vs. PS2 vs. PSP** — gleiche PVD-Signatur, Unterscheidung nur über Boot-Marker
- **PC/Computer-Systeme** — keine Header-Signatur, nur Extension/Folder/Keyword
- **Multi-Disc/Multi-File** — CUE+BIN, GDI+Track, M3U+CHD erfordern Set-Modellierung
- **Parent/Clone/BIOS** — MAME-Sets, geteilte BIOS-Dateien, Split/Merged-ROM-Sets
- **Directory-based Games** — Wii U (RPX in Ordner), 3DS CIA-Installationen, PC-Spiele

Ein Schema, das diese Fälle nicht modellieren kann, erzeugt tote Winkel in der Evaluation. Tote Winkel in der Evaluation erzeugen unentdeckte Regressionen. Unentdeckte Regressionen erzeugen Datenverlust beim User.

---

## 2. Anforderungen

### 2.1 Funktionale Anforderungen

| ID | Anforderung | Priorität |
|----|-------------|-----------|
| REQ-01 | Schema modelliert alle 69 Systeme aus `data/consoles.json` | P0 |
| REQ-02 | Schema modelliert Arcade-Spezifika (Parent/Clone, ROM-Sets, Shared BIOS) | P0 |
| REQ-03 | Schema modelliert PC/Computer-Systeme (DOS, Amiga, C64, ZX, MSX, Atari ST, PC-98, X68000) | P0 |
| REQ-04 | Schema modelliert Multi-Disc-/Multi-File-Sets (CUE+BIN, GDI, M3U, CHD-Sets) | P0 |
| REQ-05 | Schema modelliert Directory-based Games (Wii U, entpackte ISOs, PC-Spiele) | P1 |
| REQ-06 | Schema drückt alle Verdict-Zustände der 7 Evaluationsebenen (A–G) aus | P0 |
| REQ-07 | Schema drückt Ambiguität (akzeptable Alternativen) aus | P0 |
| REQ-08 | Schema modelliert alle 8 Detection-Sources mit erwarteten Ergebnissen | P0 |
| REQ-09 | Schema modelliert DAT-Matching über mehrere DAT-Ökosysteme (No-Intro, Redump, MAME, Custom) | P0 |
| REQ-10 | Schema modelliert BIOS-Abhängigkeiten (welches BIOS für welches Spiel/System) | P1 |
| REQ-11 | Schema modelliert Archive-Level-Matching (ZIP-inner vs. outer Hash) | P1 |
| REQ-12 | Schema für Versioning, Diff, Migration ausgelegt | P0 |
| REQ-13 | Schema durch JSON-Schema validierbar | P0 |
| REQ-14 | Schema-Migration muss abwärtskompatibel erweiterbar sein | P1 |

### 2.2 Plattformbreite

| Plattformklasse | Systeme | Besonderheiten |
|-----------------|---------|----------------|
| **No-Intro Cartridge** (~35) | NES, SNES, N64, GB, GBC, GBA, MD, 32X, SMS, GG, PCE, A26, A52, A78, LYNX, JAG, COLECO, INTV, NGP, NGPC, NDS, 3DS, SWITCH, VB, VECTREX, SG1000, WS, WSC, POKEMINI, ODYSSEY2, CHANNELF, SUPERVISION, VITA (Cartridge-like) | Header-Signaturen, Unique Extensions |
| **Redump Disc** (~22) | PS1, PS2, PS3, PSP, GC, WII, WIIU, SAT, DC, 3DO, SCD, PCECD, PCFX, NEOCD, JAGCD, CD32, CDI, FMTOWNS, XBOX, X360 | Disc-Header, PVD, CUE/BIN/CHD |
| **Arcade** (~3 Keys) | ARCADE (MAME/FBNeo), NEOGEO (AES/MVS), NEOCD | Parent/Clone-Sets, Shared BIOS, Split/Merged/Non-Merged |
| **PC/Computer** (~10) | DOS, AMIGA, C64, ZX, MSX, ATARIST, A800, CPC, PC98, X68K | Keine Header, Folder/Extension/Keyword-Only, Disk-Images (ADF, D64, TZX) |
| **Handheld (Disc-artig)** (~3) | PSP (CSO/ISO), VITA (VPK), 3DS (CIA) | Hybrid: dateibasiert aber disc-ähnliche Container |

**Gesamtabdeckung:** 69 Systeme, 5 Plattformklassen, 8 Detection-Methoden, 4 DAT-Ökosysteme.

---

## 3. Datenmodell

### 3.1 Objekthierarchie

```
GroundTruthEntry (Zeile in JSONL)
├── id                          Pflicht  — Globale eindeutige ID
├── schemaVersion               Pflicht  — SemVer des Schemas
├── path                        Pflicht  — Relativer Pfad zur Stub-/Sample-Datei
├── set                         Pflicht  — Dataset-Klasse (Enum)
├── subclass                    Pflicht  — Feingranulare Unterklasse
├── platformClass               Pflicht  — Plattformklasse (Enum)
├── source                      Pflicht  — Source-Info (Herkunft)
│   ├── origin                              Stub/Donated/Synthetic
│   ├── stub                                Stub-Generator-Info (optional)
│   │   ├── generator                       Generator-Name
│   │   ├── sizeBytes                       Minimalgröße
│   │   ├── headerBytes                     Hex-String
│   │   └── additionalFiles                 Weitere Dateien im Set
│   └── license                             Lizenz-Tag
│
├── fileModel                   Pflicht  — Datei-/Container-Modell
│   ├── container                           single/zip/7z/rar/multi-file/directory
│   ├── primaryExtension                    Hauptextension
│   ├── innerFiles                          Dateien im Archiv (optional)
│   ├── setFiles                            Dateien im Multi-File-Set (optional)
│   └── directoryLayout                     Verzeichnisstruktur (optional)
│
├── expected                    Pflicht  — Erwartete Ergebnisse
│   ├── consoleKey                          Pflicht
│   ├── category                            Pflicht
│   ├── gameIdentity                        Optional
│   ├── region                              Optional
│   ├── version                             Optional
│   ├── confidenceClass                     Pflicht
│   ├── detectionConflict                   Pflicht
│   ├── sortingDecision                     Pflicht
│   ├── sortTarget                          Optional
│   ├── repairSafe                          Pflicht
│   └── dat                                 DAT-Matching-Erwartung
│       ├── matchLevel                      exact/cross-system/none/not-applicable
│       ├── ecosystem                       no-intro/redump/mame/tosec/custom/none
│       ├── gameName                        Erwarteter DAT-Name
│       └── hashType                        sha1/md5/crc32/sha1-chd-raw
│
├── relationships               Optional — Beziehungen zu anderen Entries
│   ├── parentId                            Parent-ROM (Arcade Clone)
│   ├── biosId                              Benötigtes BIOS
│   ├── setMembers                          Weitere Set-Mitglieder (Multi-Disc)
│   ├── cloneOf                             Clone-Beziehung
│   └── romSetType                          split/merged/non-merged
│
├── detectionExpectations       Pflicht  — Erwartete Detection-Source-Ergebnisse
│   ├── sources[]                           Welche Methoden feuern sollen
│   │   ├── method                          DetectionSource-Enum
│   │   ├── expectedConsoleKey              Was diese Methode finden soll
│   │   └── minConfidence                   Mindest-Confidence
│   └── conflictScenario                    Expected Conflict-Szenario (optional)
│
├── verdicts                    Optional — Erwartete Verdict-Zustände (Level A–G)
│   ├── containerVerdict                    A: CORRECT/WRONG-TYPE/WRONG-CONTAINER
│   ├── consoleVerdict                      B: CORRECT/UNKNOWN/WRONG-CONSOLE/AMBIGUOUS-*
│   ├── categoryVerdict                     C: CORRECT/GAME-AS-JUNK/BIOS-AS-GAME/…
│   ├── identityVerdict                     D: CORRECT/WRONG-GAME/WRONG-VARIANT/…
│   ├── datVerdict                          E: EXACT/WRONG-SYSTEM/NONE-EXPECTED/…
│   ├── sortingVerdict                      F: CORRECT-SORT/CORRECT-BLOCK/WRONG-SORT/…
│   └── repairVerdict                       G: REPAIR-SAFE/REPAIR-RISKY/REPAIR-BLOCKED
│
├── acceptableAlternatives      Optional — Ambiguitäts-Modellierung
│   └── []
│       ├── consoleKey                      Alternatives System
│       ├── category                        Alternative Kategorie
│       ├── confidenceClass                 Alternative Confidence
│       └── reason                          Begründung
│
├── difficulty                  Pflicht  — easy/medium/hard/adversarial
├── tags                        Pflicht  — Filterbare Tags
├── addedInVersion              Pflicht  — SemVer
├── lastVerified                Pflicht  — ISO-Datum
└── notes                       Optional — Menschenlesbare Erklärung
```

### 3.2 Feld-Definitionen (Vollständig)

#### 3.2.1 Wurzelfelder

| Feld | Typ | Pflicht | Beschreibung | Constraint |
|------|-----|---------|--------------|------------|
| `id` | string | Ja | Globale eindeutige ID | Format: `{set}-{platform}-{subclass}-{NNN}`. Regex: `^[a-z0-9]+-[a-z0-9]+-[a-z0-9-]+-\d{3,}$` |
| `schemaVersion` | string | Ja | SemVer des Schemas, das dieser Eintrag nutzt | SemVer: `^\d+\.\d+\.\d+$` |
| `path` | string | Ja | Relativer Pfad zur Datei unter `benchmark/samples/` | POSIX-Pfad, keine Backslashes, kein `..` |
| `set` | enum | Ja | Dataset-Klasse | `golden-core`, `golden-realworld`, `chaos-mixed`, `edge-cases`, `negative-controls`, `repair-safety`, `dat-coverage`, `performance-scale` |
| `subclass` | string | Ja | Unterklasse innerhalb des Sets | Freitext, snake_case |
| `platformClass` | enum | Ja | Plattformklasse des Testfalls | `cartridge`, `disc`, `arcade`, `computer`, `hybrid` |
| `difficulty` | enum | Ja | Schwierigkeitsgrad | `easy`, `medium`, `hard`, `adversarial` |
| `tags` | string[] | Ja | Filterbare Tags | Min. 1 Tag, lowercase-kebab |
| `addedInVersion` | string | Ja | Ground-Truth-Version (SemVer) | `^\d+\.\d+\.\d+$` |
| `lastVerified` | string | Ja | Letzte manuelle Verifikation | ISO-8601 Datum `YYYY-MM-DD` |
| `notes` | string | Nein | Erklärung, besonders für schwierige Fälle | Freitext |

#### 3.2.2 `source` — Herkunft

| Feld | Typ | Pflicht | Beschreibung |
|------|-----|---------|--------------|
| `source.origin` | enum | Ja | `stub` (generiert), `donated` (realer ROM-Stub), `synthetic` (maschinell erzeugt) |
| `source.stub.generator` | string | Wenn origin=stub | Name des Generators (z.B. `nes-ines-header`, `pvd-ps1`, `mame-parent-set`) |
| `source.stub.sizeBytes` | int | Wenn origin=stub | Minimale Dateigrösse |
| `source.stub.headerBytes` | string | Nein | Hex-String der Header-Bytes |
| `source.stub.additionalFiles` | string[] | Nein | Weitere Dateien, die der Generator erzeugt (CUE, GDI, M3U etc.) |
| `source.license` | string | Nein | Lizenz-Tag (`public-domain`, `synthetic`, `fair-use-header`) |

#### 3.2.3 `fileModel` — Datei-/Container-Modell

| Feld | Typ | Pflicht | Beschreibung |
|------|-----|---------|--------------|
| `fileModel.container` | enum | Ja | `single`, `zip`, `7z`, `rar`, `multi-file`, `directory` |
| `fileModel.primaryExtension` | string | Ja | Hauptextension (z.B. `.nes`, `.iso`, `.zip`) |
| `fileModel.innerFiles` | object[] | Wenn container=zip/7z/rar | Dateien im Archiv |
| `fileModel.innerFiles[].path` | string | Ja | Relativer Pfad im Archiv |
| `fileModel.innerFiles[].extension` | string | Ja | Extension der Innendatei |
| `fileModel.innerFiles[].sizeBytes` | int | Nein | Größe |
| `fileModel.setFiles` | object[] | Wenn container=multi-file | Alle Dateien im Set (CUE+BIN, GDI+Tracks, M3U+CHDs) |
| `fileModel.setFiles[].path` | string | Ja | Relativer Pfad |
| `fileModel.setFiles[].role` | enum | Ja | `primary` (CUE/GDI/M3U), `data` (BIN/Track/CHD), `metadata` (SFV, TXT) |
| `fileModel.setFiles[].extension` | string | Ja | Extension |
| `fileModel.directoryLayout` | object | Wenn container=directory | Verzeichnisstruktur |
| `fileModel.directoryLayout.rootName` | string | Ja | Name des Stammordners |
| `fileModel.directoryLayout.entries` | string[] | Ja | Relative Pfade aller Dateien im Verzeichnis |
| `fileModel.directoryLayout.entryPoint` | string | Nein | Startdatei (z.B. `code/boot.rpx`, `game.exe`) |

#### 3.2.4 `expected` — Erwartete Ergebnisse

| Feld | Typ | Pflicht | Beschreibung | Werte |
|------|-----|---------|--------------|-------|
| `expected.consoleKey` | string? | Ja | Erwartetes System | Einer der 69 Keys aus `consoles.json` oder `null` (UNKNOWN) |
| `expected.category` | enum | Ja | Erwartete Kategorie | `Game`, `Bios`, `Junk`, `NonGame`, `Unknown` |
| `expected.gameIdentity` | string? | Nein | Erwarteter Spielname | Freitext |
| `expected.region` | string? | Nein | Erwartete Region | `EU`, `US`, `JP`, `WORLD`, `KR`, `CN`, `AU`, `DE`, `FR`, `ES`, `IT`, etc. |
| `expected.version` | string? | Nein | Erwartete Version | Freitext (`Rev A`, `v1.1`, `1.0.2`) |
| `expected.discNumber` | int? | Nein | Disc-Nummer | ≥ 1 |
| `expected.discSetSize` | int? | Nein | Gesamtzahl Discs | ≥ 1 |
| `expected.confidenceClass` | enum | Ja | Erwartete Confidence-Klasse | `high` (≥80), `medium` (50–79), `low` (<50), `any` |
| `expected.detectionConflict` | bool | Ja | Detection-Conflict erwartet? | `true`/`false` |
| `expected.sortingDecision` | enum | Ja | Erwartete Sorting-Entscheidung | `sort`, `block`, `not-applicable` |
| `expected.sortTarget` | string? | Wenn sort | Zielordner | ConsoleKey oder Custom-Pfad |
| `expected.repairSafe` | bool | Ja | Repair/Rename sicher? | `true`/`false` |

#### 3.2.5 `expected.dat` — DAT-Matching-Erwartung

| Feld | Typ | Pflicht | Beschreibung | Werte |
|------|-----|---------|--------------|-------|
| `expected.dat.matchLevel` | enum | Ja | Erwarteter Match-Level | `exact`, `cross-system`, `none`, `not-applicable` |
| `expected.dat.ecosystem` | enum | Ja | DAT-Ökosystem | `no-intro`, `redump`, `mame`, `tosec`, `custom`, `none` |
| `expected.dat.gameName` | string? | Wenn match | Erwarteter DAT-Spielname | Freitext |
| `expected.dat.hashType` | enum | Nein | Hash-Typ für Match | `sha1`, `md5`, `crc32`, `sha1-chd-raw` |

#### 3.2.6 `relationships` — Beziehungen

| Feld | Typ | Pflicht | Beschreibung |
|------|-----|---------|--------------|
| `relationships.parentId` | string? | Nein | ID des Parent-ROM (Arcade Clone-Beziehung) |
| `relationships.biosId` | string? | Nein | ID des benötigten BIOS-Eintrags |
| `relationships.biosSystemKeys` | string[]? | Nein | Systeme, die dieses BIOS benötigen (wenn dieser Eintrag ein BIOS ist) |
| `relationships.setMembers` | string[]? | Nein | IDs der anderen Mitglieder im Multi-Disc/Multi-File-Set |
| `relationships.cloneOf` | string? | Nein | ID des Originals (bei Clone-Beziehung) |
| `relationships.romSetType` | enum? | Nein | `split`, `merged`, `non-merged` (Arcade ROM-Set-Typ) |

#### 3.2.7 `detectionExpectations` — Erwartete Detection-Ergebnisse

| Feld | Typ | Pflicht | Beschreibung |
|------|-----|---------|--------------|
| `detectionExpectations.sources` | object[] | Ja | Erwartete feuernde Detection-Methoden |
| `detectionExpectations.sources[].method` | enum | Ja | `FolderName`, `UniqueExtension`, `AmbiguousExtension`, `DiscHeader`, `ArchiveContent`, `CartridgeHeader`, `SerialNumber`, `FilenameKeyword`, `DatHash` |
| `detectionExpectations.sources[].expectedConsoleKey` | string | Ja | Was diese Methode finden soll |
| `detectionExpectations.sources[].minConfidence` | int | Nein | Mindest-Confidence (0–100) |
| `detectionExpectations.conflictScenario` | object? | Nein | Erwartetes Conflict-Szenario |
| `detectionExpectations.conflictScenario.conflictingMethods` | string[] | Ja | Methoden, die widersprüchliche Ergebnisse liefern |
| `detectionExpectations.conflictScenario.expectedWinner` | string | Ja | Welche Methode gewinnen soll |
| `detectionExpectations.conflictScenario.reason` | string | Ja | Warum |

#### 3.2.8 `verdicts` — Erwartete Evaluations-Verdicts

| Feld | Typ | Pflicht | Werte |
|------|-----|---------|-------|
| `verdicts.containerVerdict` | enum | Nein | `CORRECT`, `WRONG-TYPE`, `WRONG-CONTAINER` |
| `verdicts.consoleVerdict` | enum | Nein | `CORRECT`, `UNKNOWN`, `WRONG-CONSOLE`, `AMBIGUOUS-CORRECT`, `AMBIGUOUS-WRONG` |
| `verdicts.categoryVerdict` | enum | Nein | `CORRECT`, `GAME-AS-JUNK`, `GAME-AS-BIOS`, `BIOS-AS-GAME`, `JUNK-AS-GAME`, `UNKNOWN` |
| `verdicts.identityVerdict` | enum | Nein | `CORRECT`, `WRONG-GAME`, `WRONG-VARIANT`, `CORRECT-GROUP`, `UNKNOWN` |
| `verdicts.datVerdict` | enum | Nein | `EXACT`, `WRONG-SYSTEM`, `WRONG-GAME`, `NONE-EXPECTED`, `NONE-MISSED`, `FALSE-MATCH` |
| `verdicts.sortingVerdict` | enum | Nein | `CORRECT-SORT`, `CORRECT-BLOCK`, `WRONG-SORT`, `WRONG-BLOCK`, `UNSAFE-SORT` |
| `verdicts.repairVerdict` | enum | Nein | `REPAIR-SAFE`, `REPAIR-RISKY`, `REPAIR-BLOCKED` |

#### 3.2.9 `acceptableAlternatives` — Ambiguitäts-Modellierung

| Feld | Typ | Pflicht | Beschreibung |
|------|-----|---------|--------------|
| `acceptableAlternatives` | object[]? | Nein | Liste akzeptabler alternativer Ergebnisse |
| `[].consoleKey` | string? | Nein | Alternatives System |
| `[].category` | enum? | Nein | Alternative Kategorie |
| `[].confidenceClass` | enum? | Nein | Alternative Confidence-Klasse |
| `[].sortingDecision` | enum? | Nein | Alternative Sorting-Entscheidung |
| `[].reason` | string | Ja | Begründung, warum diese Alternative akzeptabel ist |

---

## 4. Plattform- & DAT-Modell

### 4.1 No-Intro Cartridge-Systeme

**Charakteristik:** Eine ROM-Datei pro Spiel, oft mit eindeutiger Extension, teilweise Header-Signatur.

| Aspekt | Modellierung |
|--------|-------------|
| Erkennung | `UniqueExtension` (primär) + `CartridgeHeader` (10 Systeme) + `FolderName` |
| DAT-Ökosystem | `no-intro` — CRC32+MD5+SHA1, No-Intro-Benennungskonvention |
| Container | `single` (unverpackt) oder `zip`/`7z` (archiviert) |
| Besonderheiten | Headered vs. Headerless ROMs (NES iNES, Lynx LNX-Header); GB/GBC Dual-Mode |
| Ambiguität | GB↔GBC (CGB-Flag), Genesis↔32X (gleicher Header-Offset 0x100) |

**Pflichtfelder für No-Intro Cartridge-Testfälle:**
- `expected.consoleKey` — einer der 35 Cartridge-Keys
- `expected.dat.ecosystem` = `"no-intro"`
- `expected.dat.matchLevel` — `exact` oder `none`
- `detectionExpectations.sources` — mindestens eine Methode

**Bekannte Ambiguitäten (müssen als `acceptableAlternatives` modelliert werden):**

| Paar | Ursache | Auflösung |
|------|---------|-----------|
| GB ↔ GBC | CGB-Flag 0x80 = Dual-Mode | Beide akzeptabel |
| MD ↔ 32X | Header "SEGA 32X" vs. "SEGA MEGA DRIVE" bei Offset 0x100 | 32X nur bei explizitem Tag |
| NES (headered) ↔ NES (headerless) | iNES-Header optional | Headerless nur via Extension/DAT |
| PCE ↔ PCECD | CD-Spiel vs. HuCard | Extension + DiscHeader trennt |

### 4.2 Redump Disc-Systeme

**Charakteristik:** Multi-File-Sets (CUE+BIN, GDI+Tracks), oft mit CHD-Kompression, Disc-Header-Signaturen.

| Aspekt | Modellierung |
|--------|-------------|
| Erkennung | `DiscHeader` (primär, 16+ Systeme) + `AmbiguousExtension` + `SerialNumber` + `FolderName` |
| DAT-Ökosystem | `redump` — SHA1 pro Track, Redump-Benennungskonvention |
| Container | `multi-file` (CUE+BIN), `single` (CHD/ISO), `directory` (GDI-Sets) |
| Besonderheiten | PS1/PS2/PSP teilen PVD-Signatur; CHD-RAW-SHA1 für DAT-Matching |
| Ambiguität | PS1↔PS2↔PSP (PVD-Signatur identisch), AmbiguousExt für alle Disc-Systeme |

**Pflichtfelder für Redump Disc-Testfälle:**
- `expected.consoleKey` — einer der 22 Disc-Keys
- `expected.dat.ecosystem` = `"redump"`
- `expected.dat.hashType` — `sha1` oder `sha1-chd-raw`
- `fileModel.container` — `single` (CHD/ISO) oder `multi-file` (CUE+BIN)
- `fileModel.setFiles` — wenn multi-file

**Multi-File-Set-Modellierung:**

```jsonl
{
  "fileModel": {
    "container": "multi-file",
    "primaryExtension": ".cue",
    "setFiles": [
      { "path": "Final Fantasy VII (Disc 1).cue", "role": "primary", "extension": ".cue" },
      { "path": "Final Fantasy VII (Disc 1).bin", "role": "data", "extension": ".bin" }
    ]
  }
}
```

**Multi-Disc-Set-Modellierung (über `relationships.setMembers`):**

```jsonl
{
  "id": "gc-ps1-multi-disc-001",
  "expected": { "discNumber": 1, "discSetSize": 3 },
  "relationships": {
    "setMembers": ["gc-ps1-multi-disc-002", "gc-ps1-multi-disc-003"]
  }
}
```

**Bekannte Ambiguitäten:**

| Paar | Ursache | Auflösung im Schema |
|------|---------|---------------------|
| PS1 ↔ PS2 | PVD "PLAYSTATION"; nur Boot-Marker unterscheiden | `DiscHeader` + Boot-Marker |
| PS2 ↔ PSP | PVD "PLAYSTATION"; nur "PSP GAME" vs. "BOOT2=" | `DiscHeader` + Boot-Marker |
| SCD ↔ SAT | Sega-Disc-Signatur ähnlich | Header-Substring-Unterschied |
| GC ↔ WII | Verschiedene Magic-Bytes, aber gleiche Extensions | Magic @ 0x1C vs. 0x18 |

### 4.3 Arcade-Systeme

**Charakteristik:** ROM-Sets (ZIP-Archive mit mehreren ROMs), Parent-Clone-Hierarchie, Shared BIOS, Split/Merged/Non-Merged Varianten.

| Aspekt | Modellierung |
|--------|-------------|
| Erkennung | `FolderName` (primär: "arcade", "mame", "fbneo") + `ArchiveContent` |
| DAT-Ökosystem | `mame` — CRC32 pro ROM-Datei, MAME-DAT |
| Container | `zip` (Standard), `7z` (alternativ) |
| Set-Typen | `split` (nur eigene ROMs), `merged` (Parent+Clones), `non-merged` (komplett eigenständig) |
| Besonderheiten | Parent/Clone-Beziehung, Shared BIOS, Device-ROMs, CHD-Supplements |

**Pflichtfelder für Arcade-Testfälle:**
- `platformClass` = `"arcade"`
- `expected.consoleKey` = `"ARCADE"`, `"NEOGEO"`, oder `"NEOCD"`
- `expected.dat.ecosystem` = `"mame"`
- `relationships.romSetType` — `split`, `merged`, oder `non-merged`
- `relationships.parentId` — wenn Clone
- `relationships.biosId` — wenn BIOS benötigt

**Parent-Clone-Modellierung:**

```jsonl
// Parent
{
  "id": "gc-arcade-parent-001",
  "path": "golden-core/arcade/sf2.zip",
  "expected": { "consoleKey": "ARCADE", "category": "Game", "gameIdentity": "Street Fighter II" },
  "relationships": { "romSetType": "split", "parentId": null }
}

// Clone
{
  "id": "gc-arcade-clone-001",
  "path": "golden-core/arcade/sf2ce.zip",
  "expected": { "consoleKey": "ARCADE", "category": "Game", "gameIdentity": "Street Fighter II Champion Edition" },
  "relationships": { "romSetType": "split", "parentId": "gc-arcade-parent-001", "cloneOf": "gc-arcade-parent-001" }
}

// BIOS
{
  "id": "gc-arcade-bios-001",
  "path": "golden-core/arcade/neogeo.zip",
  "expected": { "consoleKey": "NEOGEO", "category": "Bios" },
  "relationships": { "biosSystemKeys": ["NEOGEO", "ARCADE"] }
}
```

**Arcade-spezifische Herausforderungen:**

| Herausforderung | Schema-Lösung |
|-----------------|---------------|
| Ein ZIP enthält Parent+Clone (merged) | `relationships.romSetType = "merged"` + Archiv-Inner-Modell |
| Shared BIOS (neogeo.zip) für viele Spiele | `relationships.biosId` referenziert den BIOS-Eintrag |
| MAME-Versionsabhängigkeit | `tags` enthält MAME-Version (z.B. `mame-0.264`) |
| Device-ROMs (nicht eigenständig spielbar) | `expected.category = "NonGame"` + Tag `device-rom` |
| CHD-Supplement (HD-Spiele) | `fileModel.setFiles` mit `role: "data"` für CHD |

### 4.4 PC/Computer-Systeme

**Charakteristik:** Keine Header-Signaturen, Erkennung nur über Extension, Foldername, Keyword. Disk-Images (ADF, D64, TZX) oder Directory-based.

| Aspekt | Modellierung |
|--------|-------------|
| Erkennung | `UniqueExtension` (ADF, D64, TZX, ATR, ST) + `FolderName` + `FilenameKeyword` |
| DAT-Ökosystem | `tosec` (häufig), `no-intro` (teilweise), `custom` |
| Container | `single` (Disk-Images), `zip`, `directory` (PC-Spiele) |
| Besonderheiten | Viele Systeme ohne Header-Detection → niedrigere Confidence |
| Ambiguität | Extension-Konflikte, keine binäre Verifizierung möglich |

**Pflichtfelder für PC/Computer-Testfälle:**
- `platformClass` = `"computer"`
- `expected.confidenceClass` — häufig `medium` oder `low` (keine Header-Verifizierung)
- `expected.detectionConflict` — häufiger `true` als bei Cartridge/Disc

**Bekannte Einschränkungen (müssen im Schema abbildbar sein):**

| System | Erkennung | Max. Confidence ohne DAT |
|--------|-----------|--------------------------|
| DOS | Nur FolderName/Keyword | 85 (FolderName) |
| AMIGA | UniqueExt (.adf) | 95 |
| C64 | UniqueExt (.d64, .t64) | 95 |
| ZX | UniqueExt (.tzx) | 95 |
| MSX | UniqueExt (.mx1, .mx2) | 95 |
| ATARIST | UniqueExt (.st, .stx) | 95 |
| A800 | UniqueExt (.atr, .xex) | 95 |
| CPC | Nur FolderName/Keyword | 85 |
| PC98 | Nur FolderName/Keyword | 85 |
| X68K | Nur FolderName/Keyword | 85 |

**Directory-based Game Modellierung:**

```jsonl
{
  "id": "ec-dos-directory-001",
  "path": "edge-cases/directory-games/DOOM/",
  "fileModel": {
    "container": "directory",
    "primaryExtension": null,
    "directoryLayout": {
      "rootName": "DOOM",
      "entries": ["DOOM.EXE", "DOOM.WAD", "SETUP.EXE", "README.TXT"],
      "entryPoint": "DOOM.EXE"
    }
  },
  "expected": {
    "consoleKey": "DOS",
    "category": "Game",
    "confidenceClass": "medium"
  }
}
```

### 4.5 DAT-Ökosystem-Modell

Das Schema muss vier DAT-Ökosysteme modellieren, die sich in Namenskonvention, Hash-Methode und Organisationsstruktur unterscheiden:

| Ökosystem | Namenskonvention | Hash-Methode | Organisation | Systeme |
|-----------|-----------------|--------------|-------------|---------|
| **No-Intro** | `Title (Region) (Version)` | CRC32+MD5+SHA1 pro ROM-Datei | 1 DAT pro System | ~35 Cartridge-Systeme |
| **Redump** | `Title (Region) (Disc N)` | SHA1 pro Track/Disc | 1 DAT pro System | ~22 Disc-Systeme |
| **MAME** | Short-Name (z.B. `sf2ce`) | CRC32 pro ROM-Chip im Set | 1 DAT für alles | Arcade |
| **TOSEC** | `Title (Year)(Publisher)(Region)` | CRC32+MD5+SHA1 | 1 DAT pro System/Format | Computer-Systeme |

**Modellierung im Schema:**

```jsonl
{
  "expected": {
    "dat": {
      "matchLevel": "exact",
      "ecosystem": "no-intro",
      "gameName": "Super Mario Bros. (World)",
      "hashType": "sha1"
    }
  }
}
```

**Cross-System-DAT-Match (LookupAny):**

```jsonl
{
  "expected": {
    "dat": {
      "matchLevel": "cross-system",
      "ecosystem": "no-intro",
      "gameName": "Pokemon Red Version (USA, Europe)",
      "hashType": "sha1"
    }
  },
  "notes": "Hash-Match in GB-DAT, obwohl Datei als GBC erkannt wurde. LookupAny korrekt."
}
```

---

## 5. Klassifikations- & Detection-Modell

### 5.1 Kategorie-Modell

Die `FileCategory`-Enum des Produktionscodes definiert 5 Zustände:

| Kategorie | Beschreibung | Reason-Codes im Produktionscode | Confidence |
|-----------|-------------|--------------------------------|------------|
| `Game` | Reguläres Spiel | `game-default` | 90 |
| `Bios` | BIOS/Firmware | `bios-tag` | 98 |
| `Junk` | Demo, Beta, Proto, Hack, Sample | `junk-tag` (95), `junk-word` (90), `junk-aggressive-tag` (88), `junk-aggressive-word` (82) | 82–95 |
| `NonGame` | Nicht-Spielsoftware (Apps, Utilities) | `non-game-tag` (85), `non-game-word` (75) | 75–85 |
| `Unknown` | Nicht klassifizierbar (leerer Basename) | `empty-basename` | 5 |

**Im Schema:**
```jsonl
{
  "expected": {
    "category": "Junk",
    "gameIdentity": "Sonic the Hedgehog (Hack) (Beta 3)"
  },
  "verdicts": {
    "categoryVerdict": "CORRECT"
  }
}
```

### 5.2 Confidence-Modell

Das Produktionssystem verwendet ein numerisches Confidence-Scoring (0–100) mit Gating-Schwellen:

| Klasse | Bereich | Bedeutung | Sorting-Erlaubnis |
|--------|---------|-----------|-------------------|
| `high` | ≥ 80 | Zuverlässig erkannt | Sort erlaubt (wenn kein Conflict) |
| `medium` | 50–79 | Unsicher | Sort blockiert |
| `low` | < 50 | Spekulativ | Sort blockiert |
| `any` | 0–100 | Für Tests, bei denen Confidence irrelevant ist | N/A |

**Multi-Source-Bonus:** +5 pro zusätzliche übereinstimmende Quelle (max 100)
**Conflict-Penalty:** –20 bei starkem Widerspruch (≥80), –10 bei moderatem (≥50)

**Im Schema (Detection Expectations):**

```jsonl
{
  "detectionExpectations": {
    "sources": [
      { "method": "UniqueExtension", "expectedConsoleKey": "NES", "minConfidence": 95 },
      { "method": "CartridgeHeader", "expectedConsoleKey": "NES", "minConfidence": 90 }
    ],
    "conflictScenario": null
  },
  "expected": {
    "confidenceClass": "high",
    "detectionConflict": false
  }
}
```

### 5.3 Detection-Source-Alignment

Das Schema bildet alle 9 Detection-Sources des Produktionscodes ab:

| Source | Enum im Schema | Confidence im Code | Systeme mit dieser Methode |
|--------|---------------|-------------------|---------------------------|
| Folder Name | `FolderName` | 85 | Alle 69 (über `folderAliases`) |
| Unique Extension | `UniqueExtension` | 95 | ~40 Systeme mit eindeutigen Extensions |
| Ambiguous Extension | `AmbiguousExtension` | 40 | Disc-Systeme (.iso, .bin, .chd, .cue) |
| Disc Header | `DiscHeader` | 92 | 16+ Disc-Systeme |
| Archive Content | `ArchiveContent` | 80 | Alle (ZIP/7z-Inspektion) |
| Cartridge Header | `CartridgeHeader` | 90 | 10 Systeme (NES, SNES, N64, GBA, GB, GBC, MD, 32X, Lynx, 7800) |
| Serial Number | `SerialNumber` | 88–95 | PS1, PS2, PS3, PSP, Vita, GC, SAT, NDS, 3DS, X360 |
| Filename Keyword | `FilenameKeyword` | 75 | Alle (Tag-Erkennung in Dateiname) |
| DAT Hash | `DatHash` | 100 | Alle (wenn DAT geladen) |

### 5.4 Conflict-Szenario-Modellierung

Für Testfälle, bei denen Detection-Methoden widersprüchliche Ergebnisse liefern:

```jsonl
{
  "id": "ec-folder-vs-header-001",
  "path": "edge-cases/folder-conflict/SNES/ActuallyNES.nes",
  "expected": {
    "consoleKey": "NES",
    "confidenceClass": "high",
    "detectionConflict": true
  },
  "detectionExpectations": {
    "sources": [
      { "method": "FolderName", "expectedConsoleKey": "SNES", "minConfidence": 85 },
      { "method": "UniqueExtension", "expectedConsoleKey": "NES", "minConfidence": 95 },
      { "method": "CartridgeHeader", "expectedConsoleKey": "NES", "minConfidence": 90 }
    ],
    "conflictScenario": {
      "conflictingMethods": ["FolderName", "UniqueExtension"],
      "expectedWinner": "CartridgeHeader + UniqueExtension",
      "reason": "Header (90) + Extension (95) überwiegen Folder (85). Multi-Source-Bonus für NES."
    }
  },
  "verdicts": {
    "consoleVerdict": "CORRECT"
  },
  "notes": "NES-ROM in SNES-Ordner. Header + Extension haben Vorrang. Conflict-Flag MUSS gesetzt sein."
}
```

### 5.5 Ambiguitäts-Evaluation

Der Evaluator verarbeitet `acceptableAlternatives` in dieser Reihenfolge:

1. `actual` == `expected` → **CORRECT**
2. `actual` ∈ `acceptableAlternatives` → **ACCEPTABLE** (zählt als CORRECT für Metriken)
3. Keines von beiden → **WRONG**

Ambiguität wird nur bei echten, unvermeidbaren Mehrdeutigkeiten verwendet — nicht als Ausrede für schlechte Erkennung.

**Zulässige Ambiguitäts-Fälle:**

| Fall | Begründung |
|------|-----------|
| GB ↔ GBC (CGB Dual-Mode) | Hardware-Design ist inherent ambig |
| PCE ↔ PCECD (bestimmte Dateien) | Ohne CD-Signatur nicht trennbar |
| NEOGEO ↔ ARCADE (MVS/AES) | Gleiche Hardware, verschiedene Plattform-Keys |
| Headerless ROMs (Extension-Only) | Kein binäres Signal, nur Namenskonvention |

---

## 6. Parent/Clone/BIOS/Multi-Disc/Archive/Directory-Modell

### 6.1 Parent/Clone-Hierarchie (Arcade)

```
Parent-ROM (z.B. sf2.zip)
├── Clone 1 (sf2ce.zip)     → relationships.cloneOf = Parent-ID
├── Clone 2 (sf2hf.zip)     → relationships.cloneOf = Parent-ID
└── Clone 3 (sf2t.zip)      → relationships.cloneOf = Parent-ID
```

**Regeln:**
- Jeder Clone referenziert seinen Parent via `relationships.cloneOf`
- Parent hat `relationships.parentId = null`
- `relationships.romSetType` bestimmt, ob Split/Merged/Non-Merged-Verhalten getestet wird
- Evaluator prüft: wird das richtige Parent/Clone-Verhältnis erkannt?

### 6.2 BIOS-Abhängigkeiten

```
BIOS (neogeo.zip)
├── relationships.biosSystemKeys = ["NEOGEO", "ARCADE"]
└── expected.category = "Bios"

Spiel (mslug.zip)
├── relationships.biosId = "gc-arcade-bios-001"
└── expected.category = "Game"
```

**Regeln:**
- BIOS-Einträge haben `expected.category = "Bios"` und `relationships.biosSystemKeys`
- Spiel-Einträge referenzieren ihr BIOS via `relationships.biosId`
- Evaluator prüft: wird BIOS als Bios erkannt (nicht als Game → BIOS-AS-GAME Verdict)?

### 6.3 Multi-Disc-Sets

```
Multi-Disc Game
├── Disc 1: expected.discNumber = 1, discSetSize = 3
│   └── relationships.setMembers = [disc2-id, disc3-id]
├── Disc 2: expected.discNumber = 2, discSetSize = 3
│   └── relationships.setMembers = [disc1-id, disc3-id]
└── Disc 3: expected.discNumber = 3, discSetSize = 3
    └── relationships.setMembers = [disc1-id, disc2-id]
```

**Regeln:**
- Jede Disc ist ein eigenständiger GroundTruthEntry mit eigener ID
- `setMembers` enthält die IDs aller _anderen_ Discs (nicht sich selbst)
- `discNumber` und `discSetSize` sind konsistent über alle Mitglieder
- Evaluator prüft: werden alle Discs demselben Spiel zugeordnet?

### 6.4 Multi-File-Sets (CUE+BIN, GDI+Tracks)

```jsonl
{
  "fileModel": {
    "container": "multi-file",
    "primaryExtension": ".gdi",
    "setFiles": [
      { "path": "Shenmue (Disc 1).gdi", "role": "primary", "extension": ".gdi" },
      { "path": "track01.bin", "role": "data", "extension": ".bin" },
      { "path": "track02.raw", "role": "data", "extension": ".raw" },
      { "path": "track03.bin", "role": "data", "extension": ".bin" }
    ]
  }
}
```

**Regeln:**
- `role: "primary"` = die Steuerungsdatei (CUE, GDI, M3U)
- `role: "data"` = die Daten-Dateien (BIN, Track, CHD)
- `role: "metadata"` = optionale Dateien (SFV, TXT)
- Detection arbeitet primär auf der Primary-Datei
- DAT-Matching kann auf Data-Dateien erfolgen (SHA1 des BIN/Track)

### 6.5 M3U-Playlist-Sets (CHD Multi-Disc)

```jsonl
{
  "id": "gr-ps1-m3u-001",
  "fileModel": {
    "container": "multi-file",
    "primaryExtension": ".m3u",
    "setFiles": [
      { "path": "Final Fantasy VII.m3u", "role": "primary", "extension": ".m3u" },
      { "path": "Final Fantasy VII (Disc 1).chd", "role": "data", "extension": ".chd" },
      { "path": "Final Fantasy VII (Disc 2).chd", "role": "data", "extension": ".chd" },
      { "path": "Final Fantasy VII (Disc 3).chd", "role": "data", "extension": ".chd" }
    ]
  },
  "expected": {
    "consoleKey": "PS1",
    "discNumber": null,
    "discSetSize": 3
  },
  "notes": "M3U referenziert 3 CHD-Dateien. CHD-Targets dürfen nicht aus dem Scan entfernt werden (Scan-Phase Set-Member-Pruning-Regel)."
}
```

### 6.6 Archive-Level-Matching

```jsonl
{
  "id": "ec-zip-inner-001",
  "fileModel": {
    "container": "zip",
    "primaryExtension": ".zip",
    "innerFiles": [
      { "path": "Super Mario Bros. (World).nes", "extension": ".nes", "sizeBytes": 40976 }
    ]
  },
  "expected": {
    "consoleKey": "NES",
    "dat": {
      "matchLevel": "exact",
      "ecosystem": "no-intro",
      "hashType": "sha1"
    }
  },
  "notes": "DAT-Hash muss auf Inner-File-Hash matchen, nicht auf ZIP-Container-Hash."
}
```

**Regeln:**
- No-Intro-DATs hashen den ROM _innerhalb_ des Archivs
- MAME-DATs hashen die einzelnen ROM-Chips _innerhalb_ des ZIPs
- Der Evaluator muss unterscheiden: Container-Hash vs. Content-Hash
- `expected.dat.hashType` gibt an, welcher Hash-Typ erwartet wird

### 6.7 Directory-based Games

```jsonl
{
  "id": "ec-wiiu-dir-001",
  "path": "edge-cases/directory-games/Mario Kart 8 [00050000-1010EC00]/",
  "fileModel": {
    "container": "directory",
    "primaryExtension": null,
    "directoryLayout": {
      "rootName": "Mario Kart 8 [00050000-1010EC00]",
      "entries": [
        "code/app.rpx",
        "content/Common/mapobj/CommonBoard.szs",
        "meta/meta.xml"
      ],
      "entryPoint": "code/app.rpx"
    }
  },
  "expected": {
    "consoleKey": "WIIU",
    "category": "Game",
    "confidenceClass": "medium"
  },
  "detectionExpectations": {
    "sources": [
      { "method": "FolderName", "expectedConsoleKey": "WIIU" }
    ]
  },
  "notes": "Wii U entpacktes Format. Erkennung nur via Folder + ggf. RPX-Extension."
}
```

---

## 7. Datei- & Manifest-Format

### 7.1 JSONL-Dateien (Ground Truth)

```
benchmark/
├── ground-truth/
│   ├── ground-truth.schema.json          ← JSON-Schema für Validierung
│   ├── golden-core.jsonl                 ← ~70 Einträge (Cartridge + Disc + Arcade)
│   ├── golden-realworld.jsonl            ← ~200 Einträge (realistische Szenarien)
│   ├── edge-cases.jsonl                  ← ~100 Einträge (Ambiguität, Conflict, Grenzfälle)
│   ├── negative-controls.jsonl           ← ~50 Einträge (kein ROM, falsche Extension)
│   ├── chaos-mixed.jsonl                 ← ~150 Einträge (Chaos-Szenarien)
│   ├── repair-safety.jsonl               ← ~50 Einträge (Repair-Tauglichkeit)
│   ├── dat-coverage.jsonl                ← ~100 Einträge (DAT-Matching-Szenarien)
│   └── performance-scale.jsonl           ← Nur Metadaten (Stubs generiert)
```

**Jede JSONL-Datei:**
- Eine JSON-Zeile pro Testfall
- UTF-8 ohne BOM
- LF als Zeilenende
- Sortiert nach `id` (deterministische Reihenfolge)
- Validierbar gegen `ground-truth.schema.json`

### 7.2 Manifest (`benchmark/manifest.json`)

```json
{
  "$schema": "./ground-truth/ground-truth.schema.json",
  "schemaVersion": "1.0.0",
  "groundTruthVersion": "1.0.0",
  "created": "2026-03-22",
  "lastModified": "2026-03-22",
  "description": "Romulus Benchmark Ground Truth Manifest",
  "statistics": {
    "totalEntries": 720,
    "bySet": {
      "golden-core": 70,
      "golden-realworld": 200,
      "edge-cases": 100,
      "negative-controls": 50,
      "chaos-mixed": 150,
      "repair-safety": 50,
      "dat-coverage": 100,
      "performance-scale": 0
    },
    "byPlatformClass": {
      "cartridge": 280,
      "disc": 220,
      "arcade": 80,
      "computer": 100,
      "hybrid": 40
    },
    "byDifficulty": {
      "easy": 250,
      "medium": 220,
      "hard": 150,
      "adversarial": 100
    },
    "systemsCovered": 52,
    "datEcosystemsCovered": ["no-intro", "redump", "mame", "tosec"]
  },
  "integrity": {
    "checksumAlgorithm": "SHA256",
    "fileChecksums": {
      "golden-core.jsonl": "<sha256>",
      "golden-realworld.jsonl": "<sha256>"
    }
  }
}
```

### 7.3 JSON-Schema (`ground-truth.schema.json`)

Das JSON-Schema validiert jeden JSONL-Eintrag. Kernstruktur:

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "ground-truth-entry.schema.json",
  "title": "Romulus Ground Truth Entry",
  "type": "object",
  "required": [
    "id", "schemaVersion", "path", "set", "subclass",
    "platformClass", "source", "fileModel", "expected",
    "detectionExpectations", "difficulty", "tags",
    "addedInVersion", "lastVerified"
  ],
  "properties": {
    "id": {
      "type": "string",
      "pattern": "^[a-z0-9]+-[a-z0-9]+-[a-z0-9-]+-\\d{3,}$"
    },
    "schemaVersion": {
      "type": "string",
      "pattern": "^\\d+\\.\\d+\\.\\d+$"
    },
    "set": {
      "type": "string",
      "enum": [
        "golden-core", "golden-realworld", "chaos-mixed",
        "edge-cases", "negative-controls", "repair-safety",
        "dat-coverage", "performance-scale"
      ]
    },
    "platformClass": {
      "type": "string",
      "enum": ["cartridge", "disc", "arcade", "computer", "hybrid"]
    },
    "expected": {
      "type": "object",
      "required": [
        "consoleKey", "category", "confidenceClass",
        "detectionConflict", "sortingDecision", "repairSafe", "dat"
      ]
    }
  },
  "additionalProperties": false
}
```

*(Vollständiges Schema wird als separates Artefakt in TASK-003 implementiert.)*

### 7.4 Begründung der Struktur

| Entscheidung | Begründung |
|-------------|-----------|
| Eine JSONL-Datei **pro Set** (nicht pro System) | Sets definieren Evaluationskontext. System-Filter via `jq`/Code. Weniger Dateien = einfacheres Tooling. |
| `expected.dat` als **Sub-Objekt** (nicht flach) | DAT-Matching hat 4 Felder → eigene Struktur verhindert `expected`-Pollution. Erweiterbar um weitere DAT-Felder. |
| `relationships` als **optionales Objekt** | 80% der Einträge haben keine Beziehungen. Optional hält einfache Fälle sauber. |
| `detectionExpectations` als **Pflichtfeld** | Jeder Testfall muss dokumentieren, _welche_ Methoden feuern sollen. Sonst ist der Testfall unvollständig. |
| `verdicts` als **optionales Objekt** | Verdicts können aus `expected` + `actual` berechnet werden. Explizite Angabe nur bei Edge-Cases nötig, wo die Berechnung nicht trivial ist. |
| `source` statt nur `stub` | Erlaubt Mischung aus generierten Stubs und gespendeten Real-Samples. `license`-Feld sichert IP-Compliance. |

---

## 8. Beispiel-Schema

### 8.1 Vollständige Feld-Referenz

*(Siehe Abschnitt 3.2 für vollständige Feld-Definitionen.)*

### 8.2 Beispiel 1: No-Intro Cartridge (NES, einfach)

```jsonl
{
  "id": "gc-nes-cartridge-header-001",
  "schemaVersion": "1.0.0",
  "path": "golden-core/cartridge-headers/Super Mario Bros. (World).nes",
  "set": "golden-core",
  "subclass": "cartridge-header",
  "platformClass": "cartridge",
  "source": {
    "origin": "stub",
    "stub": {
      "generator": "nes-ines-header",
      "sizeBytes": 16,
      "headerBytes": "4E45531A"
    },
    "license": "synthetic"
  },
  "fileModel": {
    "container": "single",
    "primaryExtension": ".nes"
  },
  "expected": {
    "consoleKey": "NES",
    "category": "Game",
    "gameIdentity": "Super Mario Bros.",
    "region": "WORLD",
    "version": null,
    "discNumber": null,
    "discSetSize": null,
    "confidenceClass": "high",
    "detectionConflict": false,
    "sortingDecision": "sort",
    "sortTarget": "NES",
    "repairSafe": true,
    "dat": {
      "matchLevel": "exact",
      "ecosystem": "no-intro",
      "gameName": "Super Mario Bros. (World)",
      "hashType": "sha1"
    }
  },
  "relationships": null,
  "detectionExpectations": {
    "sources": [
      { "method": "UniqueExtension", "expectedConsoleKey": "NES", "minConfidence": 95 },
      { "method": "CartridgeHeader", "expectedConsoleKey": "NES", "minConfidence": 90 }
    ],
    "conflictScenario": null
  },
  "verdicts": {
    "containerVerdict": "CORRECT",
    "consoleVerdict": "CORRECT",
    "categoryVerdict": "CORRECT",
    "sortingVerdict": "CORRECT-SORT",
    "repairVerdict": "REPAIR-SAFE"
  },
  "acceptableAlternatives": null,
  "difficulty": "easy",
  "tags": ["header", "unique-ext", "tier-1", "no-intro", "nes"],
  "addedInVersion": "1.0.0",
  "lastVerified": "2026-03-22",
  "notes": ""
}
```

### 8.3 Beispiel 2: Redump Disc (PS1 Multi-Disc, CHD)

```jsonl
{
  "id": "gr-ps1-redump-multidisc-001",
  "schemaVersion": "1.0.0",
  "path": "golden-realworld/redump-style/Final Fantasy VII (USA) (Disc 1).chd",
  "set": "golden-realworld",
  "subclass": "redump-chd",
  "platformClass": "disc",
  "source": {
    "origin": "stub",
    "stub": {
      "generator": "pvd-ps1-chd",
      "sizeBytes": 2048,
      "headerBytes": null
    },
    "license": "synthetic"
  },
  "fileModel": {
    "container": "single",
    "primaryExtension": ".chd"
  },
  "expected": {
    "consoleKey": "PS1",
    "category": "Game",
    "gameIdentity": "Final Fantasy VII",
    "region": "US",
    "version": null,
    "discNumber": 1,
    "discSetSize": 3,
    "confidenceClass": "high",
    "detectionConflict": false,
    "sortingDecision": "sort",
    "sortTarget": "PS1",
    "repairSafe": true,
    "dat": {
      "matchLevel": "exact",
      "ecosystem": "redump",
      "gameName": "Final Fantasy VII (USA) (Disc 1)",
      "hashType": "sha1-chd-raw"
    }
  },
  "relationships": {
    "setMembers": ["gr-ps1-redump-multidisc-002", "gr-ps1-redump-multidisc-003"]
  },
  "detectionExpectations": {
    "sources": [
      { "method": "DiscHeader", "expectedConsoleKey": "PS1", "minConfidence": 92 },
      { "method": "SerialNumber", "expectedConsoleKey": "PS1", "minConfidence": 88 }
    ],
    "conflictScenario": null
  },
  "verdicts": {
    "containerVerdict": "CORRECT",
    "consoleVerdict": "CORRECT",
    "categoryVerdict": "CORRECT",
    "identityVerdict": "CORRECT",
    "datVerdict": "EXACT",
    "sortingVerdict": "CORRECT-SORT",
    "repairVerdict": "REPAIR-SAFE"
  },
  "acceptableAlternatives": null,
  "difficulty": "medium",
  "tags": ["disc-header", "serial", "multi-disc", "redump", "chd", "ps1"],
  "addedInVersion": "1.0.0",
  "lastVerified": "2026-03-22",
  "notes": "Disc 1 von 3. CHD mit eingebettetem Raw-SHA1 für DAT-Match. PS2/PSP-Verwechslung hier ausgeschlossen durch BOOT-Marker-Prüfung."
}
```

### 8.4 Beispiel 3: Arcade (Neo Geo, Parent + BIOS-Abhängigkeit)

```jsonl
{
  "id": "gc-arcade-neogeo-parent-001",
  "schemaVersion": "1.0.0",
  "path": "golden-core/arcade/mslug.zip",
  "set": "golden-core",
  "subclass": "arcade-parent",
  "platformClass": "arcade",
  "source": {
    "origin": "stub",
    "stub": {
      "generator": "mame-romset-stub",
      "sizeBytes": 512
    },
    "license": "synthetic"
  },
  "fileModel": {
    "container": "zip",
    "primaryExtension": ".zip",
    "innerFiles": [
      { "path": "201-p1.p1", "extension": ".p1", "sizeBytes": 1048576 },
      { "path": "201-c1.c1", "extension": ".c1", "sizeBytes": 2097152 },
      { "path": "201-s1.s1", "extension": ".s1", "sizeBytes": 131072 },
      { "path": "201-m1.m1", "extension": ".m1", "sizeBytes": 131072 }
    ]
  },
  "expected": {
    "consoleKey": "NEOGEO",
    "category": "Game",
    "gameIdentity": "Metal Slug - Super Vehicle-001",
    "region": null,
    "version": null,
    "discNumber": null,
    "discSetSize": null,
    "confidenceClass": "high",
    "detectionConflict": false,
    "sortingDecision": "sort",
    "sortTarget": "NEOGEO",
    "repairSafe": false,
    "dat": {
      "matchLevel": "exact",
      "ecosystem": "mame",
      "gameName": "mslug",
      "hashType": "crc32"
    }
  },
  "relationships": {
    "parentId": null,
    "biosId": "gc-arcade-neogeo-bios-001",
    "romSetType": "split",
    "cloneOf": null
  },
  "detectionExpectations": {
    "sources": [
      { "method": "FolderName", "expectedConsoleKey": "NEOGEO", "minConfidence": 85 },
      { "method": "DatHash", "expectedConsoleKey": "NEOGEO", "minConfidence": 100 }
    ],
    "conflictScenario": null
  },
  "verdicts": {
    "consoleVerdict": "CORRECT",
    "categoryVerdict": "CORRECT",
    "datVerdict": "EXACT",
    "sortingVerdict": "CORRECT-SORT",
    "repairVerdict": "REPAIR-BLOCKED"
  },
  "acceptableAlternatives": [
    {
      "consoleKey": "ARCADE",
      "reason": "NEOGEO-Spiele können auch unter ARCADE einsortiert werden (MAME-Konvention)"
    }
  ],
  "difficulty": "medium",
  "tags": ["arcade", "neogeo", "parent", "mame", "bios-dep", "split-set"],
  "addedInVersion": "1.0.0",
  "lastVerified": "2026-03-22",
  "notes": "Neo Geo Parent-ROM (Metal Slug). Benötigt neogeo.zip als BIOS. RepairSafe=false weil ROM-Set-Integrität nicht über Rename sichergestellt werden kann. ARCADE als Alternative akzeptabel."
}
```

### 8.5 Beispiel 4: Edge Case (GB↔GBC Dual-Mode, Ambiguität)

```jsonl
{
  "id": "ec-gb-gbc-dualmode-001",
  "schemaVersion": "1.0.0",
  "path": "edge-cases/gb-gbc/Pokemon Red (USA, Europe) (CGB-dual).gb",
  "set": "edge-cases",
  "subclass": "gb-gbc-dual",
  "platformClass": "cartridge",
  "source": {
    "origin": "stub",
    "stub": {
      "generator": "gb-cgb-header",
      "sizeBytes": 336,
      "headerBytes": null
    },
    "license": "synthetic"
  },
  "fileModel": {
    "container": "single",
    "primaryExtension": ".gb"
  },
  "expected": {
    "consoleKey": "GBC",
    "category": "Game",
    "gameIdentity": "Pokemon Red Version",
    "region": "US",
    "version": null,
    "discNumber": null,
    "discSetSize": null,
    "confidenceClass": "high",
    "detectionConflict": false,
    "sortingDecision": "sort",
    "sortTarget": "GBC",
    "repairSafe": true,
    "dat": {
      "matchLevel": "exact",
      "ecosystem": "no-intro",
      "gameName": "Pokemon Red Version (USA, Europe)",
      "hashType": "sha1"
    }
  },
  "relationships": null,
  "detectionExpectations": {
    "sources": [
      { "method": "UniqueExtension", "expectedConsoleKey": "GB", "minConfidence": 95 },
      { "method": "CartridgeHeader", "expectedConsoleKey": "GBC", "minConfidence": 90 }
    ],
    "conflictScenario": {
      "conflictingMethods": ["UniqueExtension", "CartridgeHeader"],
      "expectedWinner": "CartridgeHeader",
      "reason": "CGB-Flag 0x80 im Header zeigt GBC-Kompatibilität an. Header-Evidenz ist stärker als Extension-Heuristik."
    }
  },
  "verdicts": {
    "consoleVerdict": "CORRECT"
  },
  "acceptableAlternatives": [
    {
      "consoleKey": "GB",
      "confidenceClass": "high",
      "reason": "CGB-Flag 0x80 = Dual-Mode. Spiel läuft auf beiden Systemen. GB ist ebenso korrekt. Extension ist .gb."
    }
  ],
  "difficulty": "hard",
  "tags": ["ambiguous", "gb-gbc", "dual-mode", "header-vs-ext", "no-intro"],
  "addedInVersion": "1.0.0",
  "lastVerified": "2026-03-22",
  "notes": "CGB-Flag 0x80 = Dual-Mode. ROM läuft auf GB (monochrom) und GBC (Farbe). Erkennung darf beides zurückgeben. DetectionConflict ist false, weil es kein echter Fehler ist, sondern Hardware-inherente Ambiguität."
}
```

### 8.6 Beispiel 5: Negative Control (Keine ROM)

```jsonl
{
  "id": "nc-norom-txt-001",
  "schemaVersion": "1.0.0",
  "path": "negative-controls/non-roms/readme.txt",
  "set": "negative-controls",
  "subclass": "non-rom",
  "platformClass": "cartridge",
  "source": {
    "origin": "stub",
    "stub": {
      "generator": "text-file",
      "sizeBytes": 42
    },
    "license": "synthetic"
  },
  "fileModel": {
    "container": "single",
    "primaryExtension": ".txt"
  },
  "expected": {
    "consoleKey": null,
    "category": "Unknown",
    "gameIdentity": null,
    "region": null,
    "version": null,
    "discNumber": null,
    "discSetSize": null,
    "confidenceClass": "low",
    "detectionConflict": false,
    "sortingDecision": "block",
    "sortTarget": null,
    "repairSafe": false,
    "dat": {
      "matchLevel": "not-applicable",
      "ecosystem": "none",
      "gameName": null,
      "hashType": null
    }
  },
  "relationships": null,
  "detectionExpectations": {
    "sources": [],
    "conflictScenario": null
  },
  "verdicts": {
    "consoleVerdict": "UNKNOWN",
    "categoryVerdict": "CORRECT",
    "sortingVerdict": "CORRECT-BLOCK"
  },
  "acceptableAlternatives": null,
  "difficulty": "easy",
  "tags": ["negative", "non-rom", "txt", "should-block"],
  "addedInVersion": "1.0.0",
  "lastVerified": "2026-03-22",
  "notes": "Textdatei. Kein System sollte erkannt werden. UNKNOWN ist die korrekte Antwort."
}
```

---

## 9. Versionierung & Erweiterbarkeit

### 9.1 Schema-Versionierung

| Änderungstyp | Version-Impact | Beispiel |
|-------------|----------------|---------|
| Neues optionales Feld | Minor (1.x.0) | `expected.publisher` hinzufügen |
| Neuer Enum-Wert | Minor (1.x.0) | `platformClass: "vr"` hinzufügen |
| Pflichtfeld hinzufügen | Major (x.0.0) | `expected.hashVerified` als Pflicht |
| Feld entfernen | Major (x.0.0) | `difficulty` entfernen |
| Feld-Typ ändern | Major (x.0.0) | `expected.consoleKey` von string zu object |
| Neuer Testfall | Patch (1.0.x) | Neuer Eintrag in JSONL |
| Testfall korrigiert | Patch (1.0.x) | Ground-Truth-Wert korrigiert |

### 9.2 Ground-Truth-Versionierung

```
schemaVersion:        1.0.0    ← Struktur der Einträge (brechend bei Major)
groundTruthVersion:   1.3.2    ← Inhalt der Einträge (neue/korrigierte Testfälle)
addedInVersion:       1.2.0    ← Pro Eintrag: wann hinzugefügt
```

**Regeln:**
- `schemaVersion` in jedem Eintrag → Evaluator kann gemischte Schema-Versionen verarbeiten
- `groundTruthVersion` im Manifest → CI-Gate prüft gegen bekannte Baseline
- `addedInVersion` pro Eintrag → Changelog-Generierung automatisierbar
- `lastVerified` pro Eintrag → Stale-Detection (>6 Monate = Review-Flag)

### 9.3 Erweiterungs-Strategie

**Neue Systeme hinzufügen:**
1. System in `data/consoles.json` registrieren
2. Testfälle in passende JSONL-Datei(en) einfügen
3. `manifest.json`-Statistiken aktualisieren
4. Ground-Truth-Version bumpen (Patch)
5. Evaluator erkennt neue Keys automatisch

**Neues DAT-Ökosystem hinzufügen:**
1. `expected.dat.ecosystem`-Enum erweitern (Minor)
2. Testfälle für neues Ökosystem hinzufügen
3. JSON-Schema aktualisieren

**Neue Evaluationsebene hinzufügen (z.B. Level H):**
1. `verdicts`-Objekt um neues Feld erweitern (Minor, weil optional)
2. Evaluator-Code erweitern
3. Metriken-Framework erweitern (neues M-Metrik)
4. Bestehende Einträge optional mit neuem Verdict annotieren

### 9.4 Migration

**Minor-Versionen:** Abwärtskompatibel. Alte Einträge funktionieren unverändert.

**Major-Versionen:** Migration notwendig. Strategie:
1. Migrationsskript (`benchmark/tools/migrate-vN-to-vM.py`) bereitstellen
2. Skript liest alte JSONL, transformiert, schreibt neue JSONL
3. Validierung gegen neues Schema
4. Alte + neue Version temporär parallel im Repo (eine CI-Runde)
5. Alte Version entfernen nach grüner CI

### 9.5 Integrität

```
manifest.json
├── integrity.checksumAlgorithm = "SHA256"
└── integrity.fileChecksums
    ├── golden-core.jsonl → SHA256
    ├── golden-realworld.jsonl → SHA256
    └── ...
```

**CI-Prüfung:**
1. `manifest.json`-Checksums gegen tatsächliche JSONL-Dateien prüfen
2. Wenn Abweichung: Build fehlschlägt (Ground Truth wurde ohne Manifest-Update geändert)
3. Garantiert, dass Manifest und Ground Truth synchron bleiben

---

## 10. Konkrete nächste Schritte

| # | Schritt | Abhängigkeit | Priorität |
|---|---------|-------------|-----------|
| 1 | **JSON-Schema erstellen** (`ground-truth.schema.json`) — vollständige Validierung aller Felder, Enums, Constraints | Dieses Dokument | P0 |
| 2 | **Manifest-Template erstellen** (`manifest.json`) — Initialer Inhalt mit Statistik-Platzhaltern | Schritt 1 | P0 |
| 3 | **StubGenerator-Architektur finalisieren** — Generatoren für alle 5 Plattformklassen (Cartridge, Disc, Arcade, Computer, Hybrid) | TESTSET_DESIGN.md §4 | P0 |
| 4 | **golden-core.jsonl befüllen** — 70 Einträge: ~30 Cartridge, ~20 Disc, ~10 Arcade, ~10 Computer | Schritte 1–3 | P0 |
| 5 | **C# Loader implementieren** — `GroundTruthEntry`-Record, JSONL-Deserialisierung, Schema-Validation | Schritt 1 | P0 |
| 6 | **edge-cases.jsonl befüllen** — alle bekannten Ambiguitäten (GB↔GBC, MD↔32X, PS1↔PS2↔PSP, Folder-vs-Header-Conflicts) | Schritt 4 | P1 |
| 7 | **Evaluator-Core implementieren** — Verdict-Berechnung für Level A–G, Metrik-Aggregation | Schritte 5–6, BENCHMARK.md §4 | P1 |
| 8 | **DAT-Coverage-Tests entwerfen** — Testfälle für alle 4 DAT-Ökosysteme (No-Intro, Redump, MAME, TOSEC) | Schritt 4 | P1 |
| 9 | **CI-Gate integrieren** — Manifest-Integritätsprüfung, Schema-Validierung aller JSONL-Dateien, Regressions-Gate | Schritte 1–7 | P2 |
| 10 | **Stale-Detection einrichten** — Automatische Warnung bei `lastVerified` > 6 Monate | Schritt 9 | P2 |

---

## Anhang A: Enum-Referenz

### A.1 `set`
`golden-core`, `golden-realworld`, `chaos-mixed`, `edge-cases`, `negative-controls`, `repair-safety`, `dat-coverage`, `performance-scale`

### A.2 `platformClass`
`cartridge`, `disc`, `arcade`, `computer`, `hybrid`

### A.3 `fileModel.container`
`single`, `zip`, `7z`, `rar`, `multi-file`, `directory`

### A.4 `fileModel.setFiles[].role`
`primary`, `data`, `metadata`

### A.5 `expected.category`
`Game`, `Bios`, `Junk`, `NonGame`, `Unknown`

### A.6 `expected.confidenceClass`
`high` (≥80), `medium` (50–79), `low` (<50), `any`

### A.7 `expected.sortingDecision`
`sort`, `block`, `not-applicable`

### A.8 `expected.dat.matchLevel`
`exact`, `cross-system`, `none`, `not-applicable`

### A.9 `expected.dat.ecosystem`
`no-intro`, `redump`, `mame`, `tosec`, `custom`, `none`

### A.10 `expected.dat.hashType`
`sha1`, `md5`, `crc32`, `sha1-chd-raw`

### A.11 `detectionExpectations.sources[].method`
`FolderName`, `UniqueExtension`, `AmbiguousExtension`, `DiscHeader`, `ArchiveContent`, `CartridgeHeader`, `SerialNumber`, `FilenameKeyword`, `DatHash`

### A.12 `relationships.romSetType`
`split`, `merged`, `non-merged`

### A.13 `source.origin`
`stub`, `donated`, `synthetic`

### A.14 `verdicts.consoleVerdict`
`CORRECT`, `UNKNOWN`, `WRONG-CONSOLE`, `AMBIGUOUS-CORRECT`, `AMBIGUOUS-WRONG`

### A.15 `verdicts.categoryVerdict`
`CORRECT`, `GAME-AS-JUNK`, `GAME-AS-BIOS`, `BIOS-AS-GAME`, `JUNK-AS-GAME`, `UNKNOWN`

### A.16 `verdicts.containerVerdict`
`CORRECT`, `WRONG-TYPE`, `WRONG-CONTAINER`

### A.17 `verdicts.identityVerdict`
`CORRECT`, `WRONG-GAME`, `WRONG-VARIANT`, `CORRECT-GROUP`, `UNKNOWN`

### A.18 `verdicts.datVerdict`
`EXACT`, `WRONG-SYSTEM`, `WRONG-GAME`, `NONE-EXPECTED`, `NONE-MISSED`, `FALSE-MATCH`

### A.19 `verdicts.sortingVerdict`
`CORRECT-SORT`, `CORRECT-BLOCK`, `WRONG-SORT`, `WRONG-BLOCK`, `UNSAFE-SORT`

### A.20 `verdicts.repairVerdict`
`REPAIR-SAFE`, `REPAIR-RISKY`, `REPAIR-BLOCKED`

### A.21 `difficulty`
`easy`, `medium`, `hard`, `adversarial`

---

## Anhang B: Systemabdeckungs-Matrix

| ConsoleKey | PlatformClass | UniqueExt | CartridgeHeader | DiscHeader | SerialNumber | DAT-Ökosystem |
|------------|--------------|-----------|-----------------|------------|-------------|---------------|
| NES | cartridge | ✓ (.nes) | ✓ (iNES) | — | — | no-intro |
| SNES | cartridge | ✓ (.sfc/.smc) | ✓ (LoROM/HiROM) | — | — | no-intro |
| N64 | cartridge | ✓ (.n64/.z64/.v64) | ✓ (Magic) | — | — | no-intro |
| GB | cartridge | ✓ (.gb) | ✓ (Nintendo Logo) | — | — | no-intro |
| GBC | cartridge | ✓ (.gbc) | ✓ (CGB-Flag) | — | — | no-intro |
| GBA | cartridge | ✓ (.gba) | ✓ (Nintendo Logo) | — | — | no-intro |
| MD | cartridge | ✓ (.md/.gen) | ✓ (SEGA Header) | — | — | no-intro |
| 32X | cartridge | ✓ (.32x) | ✓ (SEGA 32X) | — | — | no-intro |
| SMS | cartridge | ✓ (.sms) | — | — | — | no-intro |
| GG | cartridge | ✓ (.gg) | — | — | — | no-intro |
| PCE | cartridge | ✓ (.pce) | — | — | — | no-intro |
| LYNX | cartridge | ✓ (.lnx) | ✓ (LYNX Magic) | — | — | no-intro |
| A78 | cartridge | ✓ (.a78) | ✓ (ATARI7800) | — | — | no-intro |
| NDS | cartridge | ✓ (.nds) | — | — | ✓ (NTR-) | no-intro |
| 3DS | cartridge | ✓ (.3ds/.cia) | — | — | ✓ (CTR-) | no-intro |
| SWITCH | cartridge | ✓ (.nsp/.xci) | — | — | — | no-intro |
| PS1 | disc | — | — | ✓ (PVD+BOOT) | ✓ (SLUS etc.) | redump |
| PS2 | disc | — | — | ✓ (PVD+BOOT2) | ✓ (SLUS etc.) | redump |
| PS3 | disc | — | — | — | ✓ (BCUS etc.) | redump |
| PSP | disc | — | — | ✓ (PVD+PSPGAME) | ✓ (UCUS etc.) | redump |
| GC | disc | — | — | ✓ (Magic@0x1C) | ✓ (3+1+2) | redump |
| WII | disc | ✓ (.wbfs/.wad) | — | ✓ (Magic@0x18) | — | redump |
| SAT | disc | — | — | ✓ (SEGASATURN) | ✓ (T-prefix) | redump |
| DC | disc | ✓ (.gdi) | — | ✓ (SEGAKATANA) | — | redump |
| 3DO | disc | — | — | ✓ (Opera FS) | — | redump |
| SCD | disc | — | — | ✓ (SEGADISCSYSTEM) | — | redump |
| ARCADE | arcade | — | — | — | — | mame |
| NEOGEO | arcade | — | — | — | — | mame |
| NEOCD | disc | — | — | ✓ (NEOGEO CD) | — | redump/mame |
| AMIGA | computer | ✓ (.adf) | — | — | — | tosec |
| C64 | computer | ✓ (.d64/.t64) | — | — | — | tosec |
| ZX | computer | ✓ (.tzx) | — | — | — | tosec |
| MSX | computer | ✓ (.mx1/.mx2) | — | — | — | tosec |
| ATARIST | computer | ✓ (.st/.stx) | — | — | — | tosec |
| DOS | computer | — | — | — | — | tosec/custom |
| A800 | computer | ✓ (.atr/.xex) | — | — | — | tosec |
| CPC | computer | — | — | — | — | tosec |
| PC98 | computer | — | — | — | — | tosec |
| X68K | computer | — | — | — | — | tosec |

*(Tabelle zeigt 38 der 69 Systeme. Restliche Systeme folgen demselben Muster.)*
