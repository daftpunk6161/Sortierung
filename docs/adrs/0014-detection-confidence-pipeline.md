# ADR-0014: Detection Confidence Pipeline & UNKNOWN-Rate Reduction

## Status
Accepted

## Datum
2026-03-19

## Kontext

Die UNKNOWN-Rate bei der Console-Erkennung war inakzeptabel hoch. Root Causes:
1. **Nur 5-stufige Detection Cascade** — keine Cartridge-Header-Analyse, keine Filename-Pattern-Erkennung
2. **ZIP-only Archive-Analyse** — 7z-Archive wurden nicht untersucht
3. **Nondeterministic DatIndex.LookupAny** — `ConcurrentDictionary`-Iteration war order-abhängig
4. **Kein Confidence-Modell** — alle Detektionsmethoden hatten gleiches Gewicht, kein Conflict-Handling
5. **ConsoleSorter wiederholte Detection** — nach Enrichment-Phase ermittelte ConsoleKeys gingen verloren
6. **Disc-Header-Detection zu spät** — lief nach Archive-Content, obwohl zuverlässiger

## Entscheidung

### 8-stufige Detection Cascade (ConsoleDetector)
Reihenfolge nach Zuverlässigkeit:
1. Folder-Name (85)
2. Unique Extension (95)
3. Ambiguous Extension bei genau 1 Match (40)
4. **Disc-Header (92)** — vorher erst nach Archive
5. Archive Content ZIP + **7z** (80)
6. **Cartridge Header (90)** — NEU
7. **Filename Serial/Keyword (88/75)** — NEU
8. UNKNOWN

### Confidence-Modell (DetectionHypothesis + HypothesisResolver)
- Jede Methode erzeugt `DetectionHypothesis(ConsoleKey, Confidence, Source, Evidence)`
- `HypothesisResolver` aggregiert gewichtet:
  - Gruppierung nach ConsoleKey, Summe der Confidence
  - Multi-Source-Agreement-Bonus: +5 pro zusätzliche Quelle (max 100)
  - Conflict-Penalty: -20 bei starkem Widerspruch (Runner ≥80), -10 bei moderatem (Runner ≥50)
  - Deterministischer Tie-Break: alphabetisch nach ConsoleKey

### Cartridge Header Detection (CartridgeHeaderDetector)
Binäre Signatur-Erkennung für: NES (iNES), SNES (interne Header mit Checksum-Verifikation), Genesis/Mega Drive, 32X, N64 (3 Byte-Orders), GBA, GB/GBC, Atari 7800, Atari Lynx.

### Filename Serial/Keyword Analysis (FilenameConsoleAnalyzer)
- Serial-Patterns: SLUS/SCUS→PS1, BCUS/BLUS→PS3, UCUS→PSP, PCSE→VITA, NTR→NDS, CTR→3DS, T-prefix→SAT
- Keyword-Tags: [PS1], (GBA), [Genesis], [Switch] etc.

### Deterministic DatIndex.LookupAny
`_data.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)` statt unsortierter ConcurrentDictionary-Iteration.

### Enrichment-Ergebnisse in ConsoleSorter durchreichen
`enrichedConsoleKeys`-Dictionary wird aus `AllCandidates` gebaut und an `ConsoleSorter.Sort()` übergeben — keine Re-Detection.

## Betroffene Dateien

### Neue Dateien
| Datei | Zweck |
|-------|-------|
| `Core/Classification/CartridgeHeaderDetector.cs` | Binäre ROM-Header-Erkennung |
| `Core/Classification/FilenameConsoleAnalyzer.cs` | Serial/Keyword-Pattern-Erkennung |
| `Core/Classification/DetectionHypothesis.cs` | Confidence-Modell (Records + Enum) |
| `Core/Classification/HypothesisResolver.cs` | Gewichtete Hypothesen-Aggregation |
| `Tests/DetectionPipelineTests.cs` | 85 Tests für alle neuen Klassen |

### Modifizierte Dateien
| Datei | Änderung |
|-------|----------|
| `Contracts/Models/DatIndex.cs` | LookupAny deterministic |
| `Contracts/Models/RomCandidate.cs` | +DetectionConfidence, +DetectionConflict |
| `Core/Classification/CandidateFactory.cs` | Neue Parameter |
| `Core/Classification/ConsoleDetector.cs` | 8-Stufen + DetectWithConfidence |
| `Infrastructure/Hashing/ArchiveHashService.cs` | +GetArchiveEntryNames für 7z |
| `Infrastructure/Orchestration/RunEnvironmentBuilder.cs` | Wiring neuer Services |
| `Infrastructure/Orchestration/EnrichmentPipelinePhase.cs` | Confidence-Tracking |
| `Infrastructure/Sorting/ConsoleSorter.cs` | enrichedConsoleKeys param |
| `Infrastructure/Orchestration/RunOrchestrator.StandardPhaseSteps.cs` | enrichedConsoleKeys Map |

## Konsequenzen

### Positiv
- **Drastische UNKNOWN-Reduktion**: Cartridge-Header + Filename-Patterns decken Fälle ab, die vorher unerkannt waren
- **7z-Support**: Archive-Content-Detection nicht mehr auf ZIP beschränkt
- **Determinismus**: DatIndex.LookupAny und HypothesisResolver sind vollständig deterministisch
- **Transparency**: DetectionConfidence + DetectionConflict pro RomCandidate → sichtbar in Reports und Debug
- **Testabdeckung**: 85 dedizierte Tests für die neuen Klassen, 2634/2635 bestehende Tests unverändert grün

### Risiken
- CartridgeHeaderDetector benötigt File-I/O — bei sehr großen Sammlungen (~100K+ Dateien) CPU/IO-Last prüfen
- FilenameConsoleAnalyzer Serial-Patterns können bei exotischen Formaten false-positives erzeugen (z.B. GameCube 6-char serials)
- SNES-Erkennung über Checksum-Complement kann bei korrupten ROMs fehlschlagen

## Alternativen Erwogen
1. **Machine Learning Classifier** — Overkill für regelbasierte Erkennung, nicht deterministisch
2. **Nur DAT-basierte Erkennung** — setzt vollständige DATs voraus, deckt nicht alle Sammlungen ab
3. **console-maps.json Regex-Loading** — vorgesehen für Phase 2, nicht in diesem ADR
