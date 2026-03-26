---
goal: "Real-World Benchmark Testset – Vollständige Implementierung von Ground Truth, Stub-Generatoren, Evaluation-Runner und CI-Integration"
version: "1.0"
date_created: 2026-03-20
last_updated: 2026-03-22
owner: "Romulus Team"
status: "Mostly Complete"
tags:
  - feature
  - testing
  - architecture
  - benchmark
---

# Introduction

![Status: Mostly Complete](https://img.shields.io/badge/status-Mostly%20Complete-yellow)

Dieser Plan setzt das in `docs/architecture/TESTSET_DESIGN.md` definierte Real-World-Testset-System in konkrete, sequenzielle Implementierungsschritte um. Ziel ist ein vollständig reproduzierbares Benchmark-System mit synthetischen Stub-Dateien, maschinenlesbarer Ground Truth (JSONL), automatischer Stub-Generierung, einem xUnit-basierten Evaluation-Runner und CI-integriertem Regressions-Gate.

**Referenzdokumente:**
- `docs/architecture/TESTSET_DESIGN.md` – Testset-Architektur, Dataset-Klassen, Ground-Truth-Schema
- `docs/architecture/RECOGNITION_QUALITY_BENCHMARK.md` – Metriken M1–M16, Qualitätsmodell, Freigabe-Gates

**Aktueller Stand:** Benchmark-/Evaluation-Infrastruktur ist zu ~90 % implementiert. 136 xUnit-Testdateien (5.200+ Tests), Ground-Truth-System mit 2.073 Einträgen, StubGenerator, EvaluationRunner, BaselineComparator, MetricsAggregator, HTML-Report und CI-integriertes Regressions-Gate vorhanden.

---

## 1. Requirements & Constraints

### Funktionale Anforderungen

- **REQ-001**: Ground-Truth-JSONL-Dateien müssen maschinenlesbar, Git-diff-freundlich und per JSON-Schema validierbar sein.
- **REQ-002**: Stub-Generator muss aus Ground-Truth-JSONL deterministisch alle Sample-Dateien erzeugen können (gleicher Input → identische Bytes).
- **REQ-003**: Evaluation-Runner muss jedes Sample gegen die Production-Detection-Pipeline ausführen und Actual vs. Expected vergleichen.
- **REQ-004**: CI-Gate muss bei jedem PR automatisch prüfen: Wrong-Match-Rate ≤ Baseline + 0.1 %, Unsafe-Sort-Rate ≤ Baseline + 0.1 %.
- **REQ-005**: Samples dürfen keine echten ROM-Inhalte enthalten – nur synthetische Header-Stubs (Copyright-Compliance).
- **REQ-006**: `acceptableAlternatives` im Ground-Truth-Schema müssen vom Evaluator als CORRECT gewertet werden.

### Sicherheitsanforderungen

- **SEC-001**: Stub-Generator darf keinen Pfad außerhalb des `benchmark/samples/`-Verzeichnisses erzeugen (Path-Traversal-Schutz).
- **SEC-002**: Test-DAT-Dateien dürfen nur synthetische SHA1-Hashes enthalten, keine echten Dump-Hashes.

### Architektur-Constraints

- **CON-001**: Benchmark-Code lebt als xUnit-Tests in `src/RomCleanup.Tests/` – kein separates Projekt.
- **CON-002**: Ground Truth und Generator-Code liegen in `benchmark/` im Repository-Root – nicht in `src/`.
- **CON-003**: Generierte Samples (`benchmark/samples/`) werden per `.gitignore` ausgeschlossen, nur Ground Truth + Generatoren sind committed.
- **CON-004**: Alle neuen Klassen müssen die bestehende Dependency-Richtung einhalten: Tests → Infrastructure → Core → Contracts.
- **CON-005**: Keine neuen NuGet-Pakete erforderlich – `System.Text.Json` und `xUnit` reichen aus.

### Richtlinien

- **GUD-001**: ID-Format für Ground-Truth-Einträge: `{set-prefix}-{system}-{subclass}-{laufnummer}` (z.B. `gc-nes-header-001`).
- **GUD-002**: JSONL-Dateien eine Zeile pro Sample, sortiert nach ID.
- **GUD-003**: Jeder Stub-Generator-Typ hat eine eigene statische Methode in `StubGenerator.cs` mit expliziten Byte-Arrays.

### Patterns

- **PAT-001**: Bestehende Test-Fixture-Patterns wiederverwenden: `IDisposable`-Cleanup, Guid-basierte Temp-Verzeichnisse, `File.WriteAllBytes`.
- **PAT-002**: xUnit `[Theory]` + Custom `[BenchmarkData]`-Attribut für datengetriebene Benchmark-Tests.
- **PAT-003**: Ground-Truth-Lader als shared Fixture (`IClassFixture<BenchmarkFixture>`) – einmal laden, alle Tests nutzen.

---

## 2. Implementation Steps

### Phase 1 – Fundament: Ordnerstruktur, Schema, Manifest

- GOAL-001: Repository-Grundstruktur für das Benchmark-System anlegen. Danach existieren alle Verzeichnisse, das JSON-Schema, das Manifest und die .gitignore-Regeln.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-001 | Verzeichnis `benchmark/` anlegen mit Unterordnern: `ground-truth/`, `samples/`, `dats/`, `generators/`, `baselines/`, `reports/` | | |
| TASK-002 | `benchmark/samples/`-Unterordner anlegen gemäß TESTSET_DESIGN.md Abschnitt 6.1: `golden-core/` (cartridge-headers, disc-headers, unique-extensions, folder-names, bios, junk-tags, serial-keywords, dat-hashes, archives, negative), `edge-cases/` (cross-system-names, ps1-ps2-serial, gb-gbc-cgb, genesis-32x, bios-like-games, headerless-with-ext, wrong-folder), `negative-controls/` (non-rom-types, misleading-names, junk-demo-beta, empty-corrupt, fake-extension, homebrew-hack) | | |
| TASK-003 | `benchmark/.gitignore` erstellen: `samples/` und `reports/` ignorieren, `ground-truth/`, `generators/`, `dats/`, `baselines/` tracked | | |
| TASK-004 | `benchmark/ground-truth/ground-truth.schema.json` erstellen: JSON-Schema Draft 2020-12 für die in TESTSET_DESIGN.md Abschnitt 5.3 definierten Felder (id, path, set, subclass, container, stub, expected, acceptableAlternatives, detectionSources, difficulty, tags, addedInVersion, lastVerified, notes) | | |
| TASK-005 | `benchmark/manifest.json` erstellen: `{ "version": "0.1.0", "groundTruthVersion": "1.0.0", "sampleCount": 0, "lastModified": "2026-03-20", "datasetClasses": ["golden-core", "edge-cases", "negative-controls"] }` | | |
| TASK-006 | `benchmark/README.md` erstellen: Zweck, Quickstart (`dotnet test --filter Category=Benchmark`), Verweis auf TESTSET_DESIGN.md und RECOGNITION_QUALITY_BENCHMARK.md | | |

**Exit-Kriterium Phase 1:** `benchmark/` existiert mit allen Unterordnern, Schema ist valides JSON-Schema, Manifest ist valides JSON, `.gitignore` blockt `samples/` und `reports/`.

---

### Phase 2 – Ground Truth: golden-core (erste 70 Einträge)

- GOAL-002: Erste Ground-Truth-JSONL-Datei mit 70 Einträgen für `golden-core` schreiben. Abdeckung: alle 10 Cartridge-Header-Systeme, alle 6 wichtigsten Disc-Header-Systeme, Unique-Extensions, Negative-Controls.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-007 | `benchmark/ground-truth/golden-core.jsonl` erstellen: 30 Cartridge-Header-Einträge. Je System gemäß Anhang B in TESTSET_DESIGN.md: NES (iNES: `4E45531A`, 3 Samples), N64 (BE/BS/LE: `80371240`/`37804012`/`40123780`, 3 Samples), GBA (`24FFAE51` @ 0x04, 2 Samples), GB (`CEED6666` @ 0x104 + CGB=0x00, 2 Samples), GBC-Dual (CGB=0x80, 2 Samples), GBC-Only (CGB=0xC0, 2 Samples), SNES-Lo (@ 0x7FC0, 2 Samples), SNES-Hi (@ 0xFFC0, 2 Samples), MD (`SEGA MEGA DRIVE` @ 0x100, 3 Samples), 32X (`SEGA 32X` @ 0x100, 2 Samples), Lynx (`4C594E58`, 2 Samples), 7800 (`ATARI7800` @ 0x01, 2 Samples). Alle mit `expected.consoleKey`, `expected.category: "Game"`, `expected.confidenceClass: "high"`, `expected.sortingDecision: "sort"`. | | |
| TASK-008 | Weitere 15 Disc-Header-Einträge in `golden-core.jsonl`: PS1 (PVD + `PLAYSTATION` @ 0x8000, 3 Samples), PS2 (PVD + `BOOT2=` @ 0x8000, 3 Samples), GC (Magic `C2339F3D` @ 0x1C, 2 Samples), Wii (Magic `5D1C9EA3` @ 0x18, 2 Samples), Saturn (`SEGA SATURN` @ 0x00, 2 Samples), Dreamcast (`SEGA SEGAKATANA` @ 0x00, 2 Samples), SegaCD (`SEGADISCSYSTEM`, 1 Sample) | | |
| TASK-009 | Weitere 15 Unique-Extension-Einträge: `.nes` (NES), `.sfc` (SNES), `.z64` (N64), `.gba` (GBA), `.gb` (GB), `.gbc` (GBC), `.smd` (MD), `.32x` (32X), `.pce` (PCE), `.ngp` (NGP), `.ws` (WS), `.col` (COLECO), `.a26` (2600), `.jag` (JAGUAR), `.vb` (VB). Leere Dateien, nur Extension zählt. `expected.confidenceClass: "high"` für unique Extensions. | | |
| TASK-010 | Weitere 10 Negative-Control-Einträge: `.txt` (0 Bytes), `.jpg` (JFIF-Header), `.pdf` (`%PDF-`), `.exe` (MZ-Header), leere `.bin` (0 Bytes), Random-Bytes `.nes` (kein iNES-Header), Random-Bytes `.iso` (kein PVD), `.mp3`, `.dll`, `.xml`. Alle mit `expected.consoleKey: null`, `expected.category: "Unknown"`, `expected.sortingDecision: "block"`. | | |
| TASK-011 | JSONL-Validierung: Jede Zeile muss gegen `ground-truth.schema.json` valide sein. IDs müssen dem Pattern `gc-{system}-{subclass}-{nr}` folgen. Keine Duplikat-IDs. | | |

**Exit-Kriterium Phase 2:** `golden-core.jsonl` hat exakt 70 Zeilen. Jede Zeile ist valid gegen Schema. Alle IDs sind unique. Alle 10 Cartridge-Systeme + 6 Disc-Systeme + 15 Extension-Systeme + 10 Negative Controls abgedeckt.

---

### Phase 3 – Stub-Generator (Kern)

- GOAL-003: C#-basierten StubGenerator implementieren, der aus `golden-core.jsonl` deterministisch alle 70 Sample-Dateien erzeugt. Der Generator ist eine Konsolen-Utility-Klasse, die sowohl standalone als auch aus xUnit-Setup heraus aufgerufen werden kann.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-012 | Datei `benchmark/generators/StubGenerator.cs` erstellen. Klasse `BenchmarkStubGenerator` mit statischer Methode `GenerateAll(string groundTruthDir, string outputDir)`. Liest alle `.jsonl`-Dateien aus `groundTruthDir`, parsed jede Zeile als `GroundTruthEntry`, erzeugt die Stub-Datei am Pfad `outputDir/{entry.Path}`. | | |
| TASK-013 | Generator-Methoden für Cartridge-Header-Stubs: `GenerateNesInesStub(int sizeBytes, string? headerBytesOverride)` → schreibt `4E 45 53 1A` + Padding. `GenerateN64Stub(string endian)` → BE/BS/LE Magic + Padding. `GenerateGbaStub()` → Logo @ 0x04. `GenerateGbStub(byte cgbFlag)` → Nintendo-Logo @ 0x104 + CGB-Flag @ 0x143. `GenerateSnesStub(string mode)` → LoROM @ 0x7FC0 / HiROM @ 0xFFC0. `GenerateMdStub(string headerText)` → `SEGA MEGA DRIVE` oder `SEGA 32X` @ 0x100. `GenerateLynxStub()` → `4C594E58`. `Generate7800Stub()` → `ATARI7800` @ 0x01. | | |
| TASK-014 | Generator-Methoden für Disc-Header-Stubs: `GeneratePvdStub(string systemId, string bootId)` → ISO9660 PVD @ 0x8000 (`01 CD001`). `GenerateGcStub()` → Magic @ 0x1C. `GenerateWiiStub()` → Magic @ 0x18. `GenerateIpBinStub(string text)` → Saturn/DC/SCD Text-Header. `Generate3doStub()` → Opera FS @ 0x00. | | |
| TASK-015 | Generator-Methoden für Hilfs-Stubs: `GenerateEmptyFile(string path)`, `GenerateRandomBytes(string path, int size)`, `GenerateFileWithExtensionOnly(string path)` (1-Byte-Datei), `GenerateNonRomWithRomExtension(string path, byte[] content)` (z.B. JFIF-Header in `.nes`-Datei). | | |
| TASK-016 | Dispatcher-Logik in `GenerateAll()`: Liest `stub.generator`-Feld aus Ground-Truth-Entry und dispatcht an die entsprechende Generator-Methode. `switch(entry.Stub.Generator)` mit Cases: `nes-ines`, `n64-be`, `n64-bs`, `n64-le`, `gba-logo`, `gb-dmg`, `gbc-dual`, `gbc-only`, `snes-lorom`, `snes-hirom`, `md-genesis`, `32x-sega`, `lynx-header`, `atari7800`, `ps1-pvd`, `ps2-pvd`, `psp-pvd`, `gc-magic`, `wii-magic`, `sat-ipbin`, `dc-ipbin`, `scd-ipbin`, `3do-opera`, `empty-file`, `random-bytes`, `ext-only`, `non-rom-content`. | | |
| TASK-017 | Path-Traversal-Schutz: Vor jeder Dateierzeugung prüfen, dass der normalisierte Zielpfad innerhalb von `outputDir` liegt. Exception bei Verletzung. Verzeichnisse automatisch anlegen (`Directory.CreateDirectory`). | | |
| TASK-018 | Determinismus-Garantie: `random-bytes`-Generator verwendet `new Random(entry.Id.GetHashCode())` als Seed → gleiche ID = gleiche Bytes. Kein `DateTime.Now` oder `Guid.NewGuid()` im Generator. | | |

**Exit-Kriterium Phase 3:** `StubGenerator.GenerateAll("benchmark/ground-truth", "benchmark/samples")` erzeugt exakt 70 Dateien. Zweimaliger Aufruf erzeugt byte-identische Dateien. Kein Pfad verlässt `benchmark/samples/`.

---

### Phase 4 – Ground-Truth-Lader und Datenmodelle

- GOAL-004: C#-Datenmodelle und JSONL-Lader implementieren, die in xUnit-Tests als shared Fixture verwendet werden.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-019 | Datei `src/RomCleanup.Tests/Benchmark/GroundTruthEntry.cs` erstellen. Record-Typ mit allen Feldern aus TESTSET_DESIGN.md Abschnitt 5.3: `Id`, `Path`, `Set`, `Subclass`, `Container`, `Stub` (nested record: `Generator`, `SizeBytes`, `HeaderBytes`), `Expected` (nested record: `ConsoleKey`, `Category`, `GameIdentity`, `Region`, `Version`, `DiscNumber`, `DiscSetSize`, `DatMatchLevel`, `DatGameName`, `ConfidenceClass`, `SortingDecision`, `SortTarget`, `RepairSafe`), `AcceptableAlternatives` (list), `DetectionSources`, `Difficulty`, `Tags`, `AddedInVersion`, `LastVerified`, `Notes`. | | |
| TASK-020 | Datei `src/RomCleanup.Tests/Benchmark/GroundTruthLoader.cs` erstellen. Statische Methode `Load(string jsonlPath)` → `IReadOnlyList<GroundTruthEntry>`. Liest Datei zeilenweise, deserialisiert mit `System.Text.Json`, überspringt Leerzeilen. Methode `LoadAll(string groundTruthDir)` → lädt alle `.jsonl`-Dateien. Methode `LoadBySet(string groundTruthDir, string setName)` → lädt nur die JSONL für ein bestimmtes Set. | | |
| TASK-021 | Datei `src/RomCleanup.Tests/Benchmark/BenchmarkFixture.cs` erstellen. Implementiert `IAsyncLifetime`. In `InitializeAsync()`: Prüft ob `benchmark/samples/golden-core/` existiert. Wenn nicht: ruft `BenchmarkStubGenerator.GenerateAll()` auf (Lazy Generation). Lädt alle Ground-Truth-Entries. Setzt `SamplesRoot`-Property auf absoluten Pfad zu `benchmark/samples/`. | | |
| TASK-022 | Datei `src/RomCleanup.Tests/Benchmark/BenchmarkDataAttribute.cs` erstellen. Custom xUnit `[DataAttribute]` das `[Theory]`-kompatibel ist. Nimmt `setName` als Parameter, lädt Ground Truth, liefert jede Entry als `[MemberData]`-Zeile. Optional: `difficulty`-Filter, `tags`-Filter. | | |

**Exit-Kriterium Phase 4:** `GroundTruthLoader.Load("benchmark/ground-truth/golden-core.jsonl")` gibt 70 Entries zurück. Alle Felder sind korrekt deserialisiert. `BenchmarkFixture` generiert Stubs bei Bedarf. `BenchmarkDataAttribute` liefert Theory-Data.

---

### Phase 5 – Evaluation-Runner (golden-core)

- GOAL-005: xUnit-basierte Benchmark-Tests für `golden-core` implementieren. Jedes Sample wird durch die Production-Detection-Pipeline geschickt und das Ergebnis gegen Ground Truth verglichen.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-023 | Datei `src/RomCleanup.Tests/Benchmark/BenchmarkVerdict.cs` erstellen. Enum `Verdict { Correct, Acceptable, Wrong, Unknown }`. Record `SampleResult(string Id, Verdict Verdict, string ExpectedConsoleKey, string ActualConsoleKey, string ExpectedCategory, string ActualCategory, int ActualConfidence, string Details)`. | | |
| TASK-024 | Datei `src/RomCleanup.Tests/Benchmark/GroundTruthComparator.cs` erstellen. Statische Methode `Compare(GroundTruthEntry entry, DetectionResult actual)` → `SampleResult`. Logik: (1) Prüfe `expected.consoleKey` vs `actual.ConsoleKey` – bei Match → Correct. (2) Prüfe `acceptableAlternatives` – bei Match → Acceptable. (3) Sonst → Wrong. Sonderfälle: `expected.consoleKey == null` → actual muss `UNKNOWN` sein. `expected.category` wird separat geprüft. | | |
| TASK-025 | Datei `src/RomCleanup.Tests/Benchmark/GoldenCoreBenchmarkTests.cs` erstellen. Klasse `GoldenCoreBenchmarkTests : IClassFixture<BenchmarkFixture>`. Test-Methode `[Theory] [BenchmarkData("golden-core")] ConsoleDetection_MatchesGroundTruth(GroundTruthEntry entry)`. Erstellt `ConsoleDetector` mit Production-`consoles.json`, ruft `DetectWithConfidence(samplePath, samplesRoot)`, vergleicht via `GroundTruthComparator.Compare()`. Assert: `verdict != Verdict.Wrong`. | | |
| TASK-026 | Test-Methode `[Theory] [BenchmarkData("golden-core", tags: "negative")] NegativeControls_MustBeBlocked(GroundTruthEntry entry)`. Assert: `actual.ConsoleKey == null || actual.ConsoleKey == "UNKNOWN"`. Assert: `actual.Confidence < 80 || actual.HasConflict == true` (Sort-Gate muss blockieren). | | |
| TASK-027 | xUnit Trait-Annotation: Alle Benchmark-Tests mit `[Trait("Category", "Benchmark")]` taggen. Dadurch selektiv ausführbar: `dotnet test --filter Category=Benchmark`. | | |
| TASK-028 | Runner-Integration-Test: `[Fact] AllGoldenCoreSamples_ExistOnDisk()`. Prüft, dass `BenchmarkFixture` alle 70 Samples erzeugt hat und die Dateien am erwarteten Pfad existieren. Prüft Byte-Länge gegen `stub.sizeBytes`. | | |

**Exit-Kriterium Phase 5:** `dotnet test --filter Category=Benchmark` führt 70+ Tests aus. Alle Cartridge-Header-Samples werden korrekt erkannt. Alle Negative-Controls werden korrekt blockiert. Test-Laufzeit < 10 Sekunden.

---

### Phase 6 – Baseline und Regressions-Gate

- GOAL-006: Erste Baseline aus dem Benchmark-Run erzeugen und CI-kompatibles Regressions-Gate implementieren.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-029 | Datei `src/RomCleanup.Tests/Benchmark/BenchmarkReportWriter.cs` erstellen. Nimmt `IReadOnlyList<SampleResult>` und erzeugt `benchmark-results.json`: `{ "timestamp", "groundTruthVersion", "totalSamples", "correct", "acceptable", "wrong", "unknown", "wrongMatchRate", "perSystem": { "NES": { "correct": N, "wrong": N, ... }, ... } }`. | | |
| TASK-030 | Datei `src/RomCleanup.Tests/Benchmark/BaselineComparator.cs` erstellen. Lädt `benchmark/baselines/latest-baseline.json` (falls vorhanden). Vergleicht aktuelle Ergebnisse gegen Baseline: `wrongMatchRateDelta`, `perSystemRegressions` (Systeme, bei denen Wrong-Count gestiegen ist). Gibt `RegressionReport` zurück. | | |
| TASK-031 | Test-Methode `[Fact] [Trait("Category", "BenchmarkRegression")] BenchmarkResults_NoRegressionVsBaseline()`. Führt vollen Benchmark-Run aus, vergleicht gegen `latest-baseline.json`. Assert: `wrongMatchRateDelta <= 0.001` (0.1 %). Assert: Kein System hat mehr Wrong-Matches als in Baseline. Wenn keine Baseline existiert: Test ist SKIP (nicht FAIL). | | |
| TASK-032 | Erste Baseline `benchmark/baselines/v0.1.0-baseline.json` erzeugen: `dotnet test --filter Category=Benchmark` lokal ausführen, `BenchmarkReportWriter` schreibt Ergebnis, manuell als Baseline commiten. `benchmark/baselines/latest-baseline.json` als Kopie. | | |

**Exit-Kriterium Phase 6:** `latest-baseline.json` existiert mit validen Zahlen. Regressions-Test läuft und vergleicht gegen Baseline. Absichtliche Regression (z.B. einen Expected-Wert ändern) löst Test-Failure aus.

---

### Phase 7 – Ground Truth erweitern: edge-cases + negative-controls

- GOAL-007: Ground Truth für `edge-cases` (50 Einträge) und `negative-controls` (30 Einträge) erstellen. Generator-Unterstützung erweitern.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-033 | `benchmark/ground-truth/edge-cases.jsonl` erstellen (50 Einträge): Cross-System-Namenskollision (10): Tetris/Mario/Sonic für NES/SNES/GB/MD. GB↔GBC CGB-Flag (5): CGB=0x00, 0x80, 0xC0 mit `acceptableAlternatives`. Genesis↔32X (5): `SEGA MEGA DRIVE` vs `SEGA 32X` Header. PS1↔PS2 Serial (5): SLUS-Serials mit verschiedener Digit-Anzahl. BIOS vs Spiel (5): `PlayStation BIOS (v3.0).bin` vs `BioShock.iso`. Headerless mit Extension (5): `.nes` ohne iNES, `.sfc` ohne SNES-Header. Wrong-Folder (5): NES-ROM in `SNES/`-Ordner, GBA-ROM in `NES/`-Ordner. SNES-Bimodal (5): LoROM/HiROM/Copier-Header. Folder-vs-Header-Conflict (5): Header sagt X, Folder sagt Y → Header gewinnt. | | |
| TASK-034 | `benchmark/ground-truth/negative-controls.jsonl` erstellen (30 Einträge): Nicht-ROM-Typen (10): .txt, .jpg, .png, .pdf, .exe, .dll, .mp3, .avi, .xml, .html. Junk-Tags (10): Demo, Beta, Proto, Hack, Homebrew, Pirate, Bootleg, [b], [h], Sample. Fake-Extension (5): JFIF in `.nes`, PDF in `.iso`, MZ in `.gba`, MP3 in `.sfc`, Random in `.z64`. Empty/Corrupt (5): 0-Byte `.nes`, 0-Byte `.iso`, 1-Byte `.gba`, All-Zeros `.smd`, Truncated-Header `.gb`. | | |
| TASK-035 | StubGenerator erweitern: Unterstützung für `folder-structure`-generator (erzeugt Unterordner + Datei darin), `wrong-folder-stub` (platziert Stub in falschem System-Ordner), `junk-tag-stub` (fügt Junk-Tag in Dateinamen ein). | | |
| TASK-036 | `src/RomCleanup.Tests/Benchmark/EdgeCaseBenchmarkTests.cs` erstellen. `[Theory] [BenchmarkData("edge-cases")] EdgeCase_ConsoleDetection_MatchesGroundTruth(GroundTruthEntry entry)` – analog zu golden-core, aber mit `acceptableAlternatives`-Support. Separate Assert-Methode für Conflict-Flag-Erwartung bei Wrong-Folder-Fällen. | | |
| TASK-037 | `src/RomCleanup.Tests/Benchmark/NegativeControlBenchmarkTests.cs` erstellen. `[Theory] [BenchmarkData("negative-controls")] NegativeControl_MustBeBlockedOrUnknown(GroundTruthEntry entry)` – Assert: `actual.ConsoleKey == null` oder Category in {Junk, NonGame, Unknown}. Für Junk-Tags: Assert `actual.Category == "Junk"`. | | |

**Exit-Kriterium Phase 7:** 80 neue Einträge in 2 JSONL-Dateien. Alle werden korrekt generiert und getestet. Edge-Cases mit `acceptableAlternatives` werden korrekt evaluiert. Neue Baseline erzeugt (v0.2.0).

---

### Phase 8 – Test-DATs und DAT-Coverage

- GOAL-008: Mini-DAT-Dateien für Benchmark erstellen und `dat-coverage`-Ground-Truth aufbauen.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-038 | `benchmark/dats/test-nointro-nes.xml` erstellen: No-Intro-XML-Format, 10 Entries mit synthetischen SHA1-Hashes (z.B. `da39a3ee5e6b4b0d3255bfef95601890afd80709` = SHA1 von leerem String, plus 9 weitere deterministisch erzeugte). Jeder Entry hat `<game name="...">` und `<rom ... sha1="..." />`. | | |
| TASK-039 | Weitere Test-DATs: `test-nointro-snes.xml` (5 Entries), `test-nointro-gba.xml` (5 Entries), `test-redump-ps1.xml` (5 Entries in Redump-Format), `test-redump-ps2.xml` (5 Entries), `test-collision.xml` (3 Entries mit gleichen SHA1-Hashes in verschiedenen Systemen). | | |
| TASK-040 | `benchmark/ground-truth/dat-coverage.jsonl` erstellen (30 Einträge): Hash-Match (10): Stubs deren SHA1 exakt einer Test-DAT-Entry entspricht. Hash-Miss (10): Stubs ohne DAT-Match. Hash-Collision (5): Stubs deren SHA1 in 2 System-DATs existiert. Container-vs-Content (5): ZIP mit innerem ROM-Hash. | | |
| TASK-041 | StubGenerator erweitern: `GenerateStubWithKnownHash(string path, byte[] content)` – erzeugt Datei mit exakt dem Inhalt, dessen SHA1 einem Test-DAT-Entry entspricht. Der Inhalt wird so gewählt, dass der SHA1-Hash deterministisch zum DAT passt. | | |
| TASK-042 | `src/RomCleanup.Tests/Benchmark/DatCoverageBenchmarkTests.cs` erstellen. Tests die DatIndex mit Test-DATs laden und Hash-Matching prüfen: exact-match, cross-system-collision, hash-miss. | | |

**Exit-Kriterium Phase 8:** 6 Test-DAT-Dateien im Repo. 30 DAT-Coverage-Samples mit Ground Truth. Hash-Match-Tests bestehen. Collision-Detection funktioniert.

---

### Phase 9 – Metriken-Aggregation und Reporting

- GOAL-009: Aggregierte Metriken (Precision, Recall, F1, Confusion-Matrix-Daten) berechnen und als Benchmark-Report ausgeben.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-043 | Datei `src/RomCleanup.Tests/Benchmark/MetricsAggregator.cs` erstellen. Nimmt `IReadOnlyList<SampleResult>` und berechnet: `Precision(systemKey)`, `Recall(systemKey)`, `F1(systemKey)`, `WrongMatchRate()`, `UnknownRate()`, `UnsafeSortRate()`, `SafeSortCoverage()`. Format: Dictionary<string, SystemMetrics>. | | |
| TASK-044 | Confusion-Matrix-Daten: `ConfusionEntry(string ExpectedSystem, string ActualSystem, int Count)`. Methode `BuildConfusionMatrix(IReadOnlyList<SampleResult>)` → `IReadOnlyList<ConfusionEntry>`. Nur Entries mit Verdict.Wrong werden erfasst. | | |
| TASK-045 | `BenchmarkReportWriter` erweitern: `benchmark-results.json` enthält zusätzlich `perSystem`-Metriken (Precision, Recall, F1) und `confusionMatrix`-Array. Format kompatibel mit `RECOGNITION_QUALITY_BENCHMARK.md` Metriken M1–M10. | | |
| TASK-046 | Test `[Fact] MetricsAggregator_CalculatesCorrectly()`: Definierte Sample-Ergebnisse → erwartete Precision/Recall/F1-Werte. Edge-Case: System mit 0 Samples → Division-by-Zero-Schutz. | | |

**Exit-Kriterium Phase 9:** `benchmark-results.json` enthält per-System-Metriken und Confusion-Matrix. Metriken sind mathematisch korrekt verifiziert durch Unit-Tests.

---

### Phase 10 – golden-realworld + chaos-mixed (Expansion)

- GOAL-010: Dataset auf ~350 Samples erweitern durch Aufbau von `golden-realworld` (150 Einträge) und `chaos-mixed` (60 Einträge). Nur die wichtigsten Unterklassen in dieser Phase.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-047 | `benchmark/ground-truth/golden-realworld.jsonl` erstellen (150 Einträge): No-Intro-Style (50): Korrekte Dateinamen für Tier-1-Systeme (NES, SNES, N64, GBA, GB, GBC, MD, PS1, PS2), je 5-6 pro System. Folder-sortiert (40): Dateien in korrekten System-Ordnern, Ordnernamen aus `consoles.json` `folderAliases`. Regions-Varianten (30): Gleicher Titel in USA/Europe/Japan/World. Revisions (20): (Rev A), (v1.0), (v1.1), [!]. BIOS-in-Kontext (10): BIOS neben Spielen im gleichen Ordner. | | |
| TASK-048 | `benchmark/ground-truth/chaos-mixed.jsonl` erstellen (60 Einträge): Falsch benannt (15): NES-ROMs mit SNES-Namenszusatz etc. Falsche Extension (15): NES-Header in `.bin`, GBA-Header in `.rom`. Unicode-Namen (10): Japanische Zeichen, Akzente. Headerless (10): ROMs ohne Header, nur Extension. Truncated (10): Header korrekt, Datei nach 1 KB abgeschnitten. | | |
| TASK-049 | StubGenerator erweitern für neue Generatoren: `nointro-named-stub` (korrekter No-Intro-Dateiname + Header), `folder-sorted-stub` (erzeugt System-Ordner + Datei), `region-variant-stub` (gleiche Bytes, verschiedene Region-Tags im Namen), `revision-stub`, `bios-stub`, `wrong-name-stub`, `wrong-ext-stub`, `unicode-name-stub`, `truncated-stub`. | | |
| TASK-050 | Benchmark-Tests erweitern: `GoldenRealworldBenchmarkTests.cs` und `ChaosMixedBenchmarkTests.cs` anlegen. Analog zu bestehenden Tests, aber mit set-spezifischen Assertions. Chaos-Tests: UNKNOWN ist für viele Samples eine korrekte Antwort → Ground Truth muss das abbilden. | | |
| TASK-051 | manifest.json aktualisieren: `sampleCount: ~350`, `version: "0.3.0"`, neue datasetClasses hinzufügen. Neue Baseline `v0.3.0-baseline.json` erzeugen. | | |

**Exit-Kriterium Phase 10:** ~350 Samples total. Alle 4 Dataset-Klassen (golden-core, edge-cases, negative-controls, golden-realworld + chaos-mixed) haben Ground Truth und Tests. Neue Baseline erfasst. CI-Gate funktioniert mit erweitertem Datensatz.

---

### Phase 11 – repair-safety + Performance-Scale-Generator

- GOAL-011: `repair-safety`-Ground-Truth aufbauen (30 Einträge) und `ScaleDatasetGenerator` für Performance-Tests implementieren.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-052 | `benchmark/ground-truth/repair-safety.jsonl` erstellen (30 Einträge): DAT-Exact + High-Conf (8), DAT-None + High-Conf (5), DAT-None + Low-Conf (5), Conflict + DAT (4), Folder-Only (4), Single-Source-Low (4). Jeder Eintrag hat `expected.repairSafe` und `expected.sortingDecision` explizit gesetzt. | | |
| TASK-053 | `src/RomCleanup.Tests/Benchmark/RepairSafetyBenchmarkTests.cs` erstellen. Tests prüfen: (1) DAT-Exact + Confidence ≥ 95 → repairSafe=true, (2) Confidence < 80 → sortingDecision=block, (3) HasConflict → sortingDecision=block. | | |
| TASK-054 | `benchmark/generators/ScaleDatasetGenerator.cs` erstellen. Generiert 5.000 Dateien basierend auf Verteilungsparametern: 20 Systeme, 70 % unique-ext / 20 % ambig-ext / 10 % headerless, 50 % sortiert / 30 % flach / 20 % falsch, 2 % BIOS, 5 % Junk, 8 % Duplikate. Deterministic via `Random(42)`. | | |
| TASK-055 | `src/RomCleanup.Tests/Benchmark/PerformanceBenchmarkTests.cs` erstellen. `[Fact] [Trait("Category", "BenchmarkPerformance")] FullPipeline_5000Files_CompletesWithinBudget()`. Erzeugt Scale-Dataset, misst Pipeline-Throughput. Assert: > 100 Dateien/Sekunde. Gibt Timing-Report aus. | | |

**Exit-Kriterium Phase 11:** repair-safety hat 30 Ground-Truth-Entries mit Tests. ScaleDatasetGenerator erzeugt 5.000 Dateien in < 30 Sekunden. Performance-Benchmark hat Baseline-Timing.

---

## 3. Alternatives

- **ALT-001**: Benchmark als separates .NET-Projekt (`RomCleanup.Benchmark.csproj`) statt in `RomCleanup.Tests`. **Abgelehnt**, weil: separates Projekt erhöht Build-Komplexität, CI-Konfiguration und Maintenance. xUnit-Tests mit `[Trait]`-Filterung sind einfacher und nutzen bestehende Test-Infrastruktur.

- **ALT-002**: Echte ROM-Dumps statt synthetischer Stubs. **Abgelehnt**, weil: Copyright-Verletzung, nicht committbar, nicht reproduzierbar auf Clean-Checkout.

- **ALT-003**: BenchmarkDotNet statt xUnit-basierter Runner. **Abgelehnt für Phase 1**, weil: BenchmarkDotNet misst Mikro-Performance, nicht Erkennungsqualität. Kann in Phase 11 für `performance-scale` ergänzt werden, aber ist nicht die primäre Evaluation-Engine.

- **ALT-004**: Ground Truth als JSON statt JSONL. **Abgelehnt**, weil: Ein einzelnes JSON-Array ist nicht zeilenweise Git-diff-fähig, nicht partiell ladbar, und ein Syntaxfehler macht den ganzen Datensatz unlesbar.

- **ALT-005**: Ground Truth in C# als Embedded Testdata (static arrays). **Abgelehnt**, weil: Nicht von Tooling lesbar, nicht maschinenvalidierbar, nicht von der Detection-Logik entkoppelt.

---

## 4. Dependencies

- **DEP-001**: `data/consoles.json` – Konsolendefinitionen (uniqueExts, ambigExts, folderAliases). Wird vom `StubGenerator` und `ConsoleDetector` gelesen. Aktuell 69 Konsolen.
- **DEP-002**: `data/rules.json` – Junk-Tag-Patterns, Region-Regeln. Wird von `FileClassifier` und `RegionDetector` gelesen.
- **DEP-003**: `System.Text.Json` – Bereits im Projekt vorhanden. Für JSONL-Parsing und Report-Erzeugung.
- **DEP-004**: Production-Detection-Pipeline: `ConsoleDetector`, `CartridgeHeaderDetector`, `DiscHeaderDetector`, `FilenameConsoleAnalyzer`, `HypothesisResolver`, `FileClassifier`, `DatIndex`, `EnrichmentPipelinePhase`. Alle in `src/RomCleanup.Core/Classification/` bzw. `src/RomCleanup.Infrastructure/`.
- **DEP-005**: xUnit + xUnit.DataAttributes – Bereits im Projekt vorhanden. Für `[Theory]`, `[Trait]`, `[DataAttribute]`.
- **DEP-006**: `data/dat-catalog.json` – DAT-Katalog-Definitionen für Test-DAT-Format-Kompatibilität.

---

## 5. Files

### Neue Dateien

| ID | Pfad | Zweck |
|----|------|-------|
| FILE-001 | `benchmark/README.md` | Überblick, Quickstart-Anleitung |
| FILE-002 | `benchmark/manifest.json` | Metadaten: Version, Sample-Count |
| FILE-003 | `benchmark/.gitignore` | Ignoriert `samples/`, `reports/` |
| FILE-004 | `benchmark/ground-truth/ground-truth.schema.json` | JSON-Schema für Ground-Truth-Validierung |
| FILE-005 | `benchmark/ground-truth/golden-core.jsonl` | Ground Truth: 70 Einträge |
| FILE-006 | `benchmark/ground-truth/edge-cases.jsonl` | Ground Truth: 50 Einträge |
| FILE-007 | `benchmark/ground-truth/negative-controls.jsonl` | Ground Truth: 30 Einträge |
| FILE-008 | `benchmark/ground-truth/golden-realworld.jsonl` | Ground Truth: 150 Einträge |
| FILE-009 | `benchmark/ground-truth/chaos-mixed.jsonl` | Ground Truth: 60 Einträge |
| FILE-010 | `benchmark/ground-truth/repair-safety.jsonl` | Ground Truth: 30 Einträge |
| FILE-011 | `benchmark/ground-truth/dat-coverage.jsonl` | Ground Truth: 30 Einträge |
| FILE-012 | `benchmark/generators/StubGenerator.cs` | Erzeugt alle Samples aus JSONL |
| FILE-013 | `benchmark/generators/ScaleDatasetGenerator.cs` | Erzeugt 5k–20k Performance-Samples |
| FILE-014 | `benchmark/dats/test-nointro-nes.xml` | Mini-DAT für NES |
| FILE-015 | `benchmark/dats/test-nointro-snes.xml` | Mini-DAT für SNES |
| FILE-016 | `benchmark/dats/test-nointro-gba.xml` | Mini-DAT für GBA |
| FILE-017 | `benchmark/dats/test-redump-ps1.xml` | Mini-DAT für PS1 |
| FILE-018 | `benchmark/dats/test-redump-ps2.xml` | Mini-DAT für PS2 |
| FILE-019 | `benchmark/dats/test-collision.xml` | DAT mit Cross-System-Hash-Kollisionen |
| FILE-020 | `benchmark/baselines/v0.1.0-baseline.json` | Erste Benchmark-Baseline |
| FILE-021 | `benchmark/baselines/latest-baseline.json` | Aktuelle Baseline (Kopie) |
| FILE-022 | `src/RomCleanup.Tests/Benchmark/GroundTruthEntry.cs` | C#-Datenmodell für Ground Truth |
| FILE-023 | `src/RomCleanup.Tests/Benchmark/GroundTruthLoader.cs` | JSONL-Lader |
| FILE-024 | `src/RomCleanup.Tests/Benchmark/BenchmarkFixture.cs` | xUnit Shared Fixture |
| FILE-025 | `src/RomCleanup.Tests/Benchmark/BenchmarkDataAttribute.cs` | Custom Theory-Data-Attribut |
| FILE-026 | `src/RomCleanup.Tests/Benchmark/BenchmarkVerdict.cs` | Verdict-Enum + SampleResult |
| FILE-027 | `src/RomCleanup.Tests/Benchmark/GroundTruthComparator.cs` | Actual vs Expected Vergleich |
| FILE-028 | `src/RomCleanup.Tests/Benchmark/GoldenCoreBenchmarkTests.cs` | golden-core Benchmark-Tests |
| FILE-029 | `src/RomCleanup.Tests/Benchmark/EdgeCaseBenchmarkTests.cs` | edge-cases Benchmark-Tests |
| FILE-030 | `src/RomCleanup.Tests/Benchmark/NegativeControlBenchmarkTests.cs` | negative-controls Tests |
| FILE-031 | `src/RomCleanup.Tests/Benchmark/DatCoverageBenchmarkTests.cs` | DAT-Coverage-Tests |
| FILE-032 | `src/RomCleanup.Tests/Benchmark/GoldenRealworldBenchmarkTests.cs` | golden-realworld Tests |
| FILE-033 | `src/RomCleanup.Tests/Benchmark/ChaosMixedBenchmarkTests.cs` | chaos-mixed Tests |
| FILE-034 | `src/RomCleanup.Tests/Benchmark/RepairSafetyBenchmarkTests.cs` | repair-safety Tests |
| FILE-035 | `src/RomCleanup.Tests/Benchmark/PerformanceBenchmarkTests.cs` | Performance-Tests |
| FILE-036 | `src/RomCleanup.Tests/Benchmark/BenchmarkReportWriter.cs` | JSON-Report-Erzeugung |
| FILE-037 | `src/RomCleanup.Tests/Benchmark/BaselineComparator.cs` | Regressions-Vergleich |
| FILE-038 | `src/RomCleanup.Tests/Benchmark/MetricsAggregator.cs` | Precision/Recall/F1/Confusion |

### Modifizierte Dateien

| ID | Pfad | Änderung |
|----|------|----------|
| FILE-039 | `.gitignore` (Root) | Eintrag `benchmark/samples/` und `benchmark/reports/` hinzufügen |

---

## 6. Testing

| ID | Test | Prüft was | Phase |
|----|------|-----------|-------|
| TEST-001 | `AllGoldenCoreSamples_ExistOnDisk` | StubGenerator erzeugt alle 70 Dateien korrekt | 5 |
| TEST-002 | `ConsoleDetection_MatchesGroundTruth` (golden-core, 70 Theories) | Production-Detection vs Ground Truth für jeden Cartridge/Disc/Extension-Stub | 5 |
| TEST-003 | `NegativeControls_MustBeBlocked` (golden-core negatives, 10 Theories) | Nicht-ROMs werden korrekt blockiert | 5 |
| TEST-004 | `BenchmarkResults_NoRegressionVsBaseline` | Wrong-Match-Rate nicht schlechter als Baseline | 6 |
| TEST-005 | `EdgeCase_ConsoleDetection_MatchesGroundTruth` (50 Theories) | Verwechslungspaare korrekt aufgelöst | 7 |
| TEST-006 | `NegativeControl_MustBeBlockedOrUnknown` (30 Theories) | Junk/NonROM korrekt erkannt | 7 |
| TEST-007 | `DatCoverage_HashMatch` (30 Theories) | DAT-Matching korrekt für Hash-Match/Miss/Collision | 8 |
| TEST-008 | `MetricsAggregator_CalculatesCorrectly` | Precision/Recall/F1 mathematisch korrekt | 9 |
| TEST-009 | `GoldenRealworld_ConsoleDetection` (150 Theories) | Realistische Sammlung End-to-End | 10 |
| TEST-010 | `ChaosMixed_Robustness` (60 Theories) | Robustheit bei chaotischen Inputs | 10 |
| TEST-011 | `RepairSafety_CorrectDecision` (30 Theories) | Confidence-Gating und Repair-Safe-Flag | 11 |
| TEST-012 | `FullPipeline_5000Files_CompletesWithinBudget` | Pipeline-Throughput > 100 Dateien/s | 11 |
| TEST-013 | `StubGenerator_IsDeterministic` | Zweimaliger Run erzeugt byte-identische Dateien | 3 |
| TEST-014 | `StubGenerator_RejectsPathTraversal` | Pfad-Traversal-Versuch wird mit Exception blockiert | 3 |
| TEST-015 | `GroundTruthLoader_LoadsValidJsonl` | JSONL-Parsing + Schema-Konformität | 4 |

---

## 7. Risks & Assumptions

### Risiken

- **RISK-001**: **Detection-Pipeline-Änderungen brechen Baselines**. Wenn sich `ConsoleDetector` oder `HypothesisResolver` ändern, können Baselines invalid werden. **Mitigation**: Baselines werden nie überschrieben, nur neue erzeugt. Regressions-Gate vergleicht nur innerhalb gleicher Ground-Truth-Version.

- **RISK-002**: **Synthetische Stubs bilden Edge-Cases nicht vollständig ab**. Minimale Header-Stubs triggern möglicherweise nicht alle Code-Pfade in `CartridgeHeaderDetector`. **Mitigation**: `golden-realworld` und `chaos-mixed` verwenden komplexere Stubs mit mehr Payload. Phase 10 adressiert dies explizit.

- **RISK-003**: **JSONL-Ground-Truth wird inkonsistent**. Manuelle Pflege von >400 JSONL-Zeilen ist fehleranfällig. **Mitigation**: JSON-Schema-Validierung als Test (TASK-011). ID-Uniqueness-Check. CI-Gate prüft Schema-Konformität.

- **RISK-004**: **Performance-Impact auf CI**. 500+ Benchmark-Tests bei jedem PR könnten CI verlangsamen. **Mitigation**: `[Trait("Category", "Benchmark")]` erlaubt selektive Ausführung. golden-core (< 10s) bei jedem PR, Full-Benchmark nur nightly.

- **RISK-005**: **Stub-Generator wird komplex und fehleranfällig**. 20+ Generator-Methoden mit exakten Byte-Offsets. **Mitigation**: StubGenerator_IsDeterministic-Test + Byte-Count-Assertions für jeden Generator.

- **RISK-006**: **Confidence-Schwellen ändern sich**. Aktuell hardcoded bei 80. Wenn sich der Schwellwert ändert, müssen repair-safety Ground-Truth-Werte angepasst werden. **Mitigation**: `confidenceClass` statt exakter Zahlen in Ground Truth. `high` = ≥ threshold, unabhängig vom konkreten Wert.

### Annahmen

- **ASSUMPTION-001**: Die Detection-Pipeline (`ConsoleDetector`, `CartridgeHeaderDetector`, etc.) hat stabile öffentliche APIs, die sich während der Benchmark-Implementierung nicht grundlegend ändern.
- **ASSUMPTION-002**: `System.Text.Json` kann JSONL zeilenweise parsen (kein Full-Document-Modus nötig) – `JsonSerializer.Deserialize<T>(line)` pro Zeile.
- **ASSUMPTION-003**: 70 Samples reichen für eine aussagekräftige erste Baseline. Vollständige Aussagekraft erfordert Phase 10 (~350 Samples).
- **ASSUMPTION-004**: `benchmark/` als Top-Level-Verzeichnis widerspricht nicht bestehenden Build-/CI-Konfigurationen.
- **ASSUMPTION-005**: Stub-Dateien mit minimalen Header-Bytes (z.B. 16 Bytes für NES) werden von den Detektoren korrekt erkannt – die Detektoren prüfen nur Header-Offsets, nicht Datenlänge.

---

## 8. Related Specifications / Further Reading

- [docs/architecture/TESTSET_DESIGN.md](../docs/architecture/TESTSET_DESIGN.md) – Vollständige Testset-Architektur mit 8 Dataset-Klassen, Ground-Truth-Schema, Pflichtfällen, Anti-Bias-Quoten
- [docs/architecture/RECOGNITION_QUALITY_BENCHMARK.md](../docs/architecture/RECOGNITION_QUALITY_BENCHMARK.md) – Metriken M1–16, 7-Ebenen-Qualitätsmodell, Freigabe-Gates, Evaluations-Pipeline-Design
- [src/RomCleanup.Core/Classification/ConsoleDetector.cs](src/RomCleanup.Core/Classification/ConsoleDetector.cs) – Zentrale Detection-Klasse
- [src/RomCleanup.Core/Classification/CartridgeHeaderDetector.cs](src/RomCleanup.Core/Classification/CartridgeHeaderDetector.cs) – Cartridge-Header-Erkennung (10 Systeme)
- [src/RomCleanup.Core/Classification/DiscHeaderDetector.cs](src/RomCleanup.Core/Classification/DiscHeaderDetector.cs) – Disc-Header-Erkennung (16+ Systeme)
- [src/RomCleanup.Core/Classification/HypothesisResolver.cs](src/RomCleanup.Core/Classification/HypothesisResolver.cs) – Multi-Source-Aggregation
- [src/RomCleanup.Core/Classification/FileClassifier.cs](src/RomCleanup.Core/Classification/FileClassifier.cs) – Game/Bios/Junk/NonGame-Klassifikation
- [src/RomCleanup.Contracts/Models/DatIndex.cs](src/RomCleanup.Contracts/Models/DatIndex.cs) – DAT-Matching
- [data/consoles.json](data/consoles.json) – 69 Konsolendefinitionen
- [data/rules.json](data/rules.json) – Erkennungsregeln, Junk-Patterns, Regions

