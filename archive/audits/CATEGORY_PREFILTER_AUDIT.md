# Category Recognition & Pre-Filter Audit

> **Rolle:** Kompromissloser Classification- und Pre-Filter-Analyst  
> **Baseline:** `benchmark/baselines/latest-baseline.json` — 1152 Samples  
> **Metrik:** Kategorie-Erkennung (Proxy: NonRom/Junk/Unknown korrekt blockiert) = **23,158 %**  
> **Stand:** 2026-03-21

---

## 1. Executive Verdict

Die Kategorie-Erkennung ist mit 23,158 % ein **Release-Blocker**.

Von 190 Samples, die als Junk (108) oder Unknown (82) hätten blockiert werden müssen, wurden nur 44 korrekt blockiert. 146 Samples (76,8 %) leckten als FalsePositive durch die gesamte Pipeline und würden im produktiven Betrieb falsch sortiert, dedupliziert oder in falsche Konsolen-Ordner verschoben.

### Ursachenkette

| # | Ursache | Severity |
|---|---------|----------|
| U1 | FileClassifier ist extension-blind — Default Game(90) für alles | **Kritisch** |
| U2 | FileClassifier ist size-blind — 0-Byte-Dateien = Game(90) | **Kritisch** |
| U3 | IsLikelyJunkName blockiert Konsolen-Erkennung statt nur Kategorie zu setzen | **Hoch** |
| U4 | Kein Scan-Level-Vorfilter für Non-ROM-Extensions | **Hoch** |
| U5 | FileClassifier läuft mid-pipeline, nicht als Pre-Filter | **Mittel** |
| U6 | JunkRemovalPipelinePhase nur für Standalone-Junk | **Mittel** |
| U7 | Benchmark-Ground-Truth hat nur 9 NonGame-Entries (Unterabdeckung) | **Niedrig** |

### Erwartetes Ergebnis nach Umsetzung

| Metrik | Ist | Soll |
|--------|-----|------|
| Category Recognition | 23,158 % | > 85 % |
| FalsePositive gesamt | 146 (12,67 %) | < 20 (< 1,7 %) |
| TrueNegative | 44 | > 160 |
| JunkClassified (neu) | 0 (nicht getrackt) | > 80 |

---

## 2. Ist-Zustand

### 2.1 Kategorie-Verteilung (Ground Truth)

```
Game:    911  (79,08 %)   — reguläre ROMs, sollen erkannt + sortiert werden
Junk:    108  ( 9,38 %)   — Demo/Beta/Hack/Homebrew, sollen blockiert oder separiert werden
Unknown:  82  ( 7,12 %)   — Non-ROM, Müll, Leerdateien, sollen NICHT sortiert werden
Bios:     42  ( 3,65 %)   — BIOS-Dateien, eigene Sortierkategorie
NonGame:   9  ( 0,78 %)   — Tools/Utilities, eigene Sortierkategorie
──────────────────────────
Total:  1152
```

Ziel-Population für die 23,158%-Metrik: **Junk (108) + Unknown (82) = 190 Samples**.

### 2.2 FileClassifier — Aktueller Zustand

**Datei:** `src/RomCleanup.Core/Classification/FileClassifier.cs`

```
Eingabe:   baseName (ohne Extension)
Ausgabe:   ClassificationDecision(Category, Confidence, ReasonCode)
I/O:       Keine (pure function)
```

**Entscheidungskaskade:**

| Priorität | Regex | Ergebnis | Confidence |
|-----------|-------|----------|------------|
| 1 | `RxBios` — `\((bios\|firmware)\)` etc. | Bios | 98 |
| 2 | `RxJunkTags` — `\((alpha\|beta\|demo\|hack…)\)` | Junk | 95 |
| 3 | `RxJunkWords` — `\b(demo\|trial…)\b` | Junk | 90 |
| 4 | `RxNonGameTags` — `\((tool\|driver\|editor…)\)` | NonGame | 85 |
| 5 | `RxNonGameWords` — `\b(utility\|driver…)\b` | NonGame | 75 |
| 6 | `RxJunkTagsAggressive` (opt) — `\((wip\|dev build…)\)` | Junk | 88 |
| 7 | `RxJunkWordsAggressive` (opt) — `\b(wip\|playtest…)\b` | Junk | 82 |
| **Fallback** | **Keine Regex matched** | **Game** | **90** |

**Kritische Schwäche — Game(90) Default:**
- `empty_file` → kein Regex-Match → **Game(90)**
- `document` → kein Regex-Match → **Game(90)**
- `mystery_file_000` → kein Regex-Match → **Game(90)**
- `music` → kein Regex-Match → **Game(90)**

Der Classifier kennt nur den **Basename** — keine Extension, keine Dateigröße, keinen Inhalt.

### 2.3 IsLikelyJunkName — Aktueller Zustand

**Datei:** `src/RomCleanup.Core/Classification/ConsoleDetector.cs`, Zeile 314

```csharp
private static readonly Regex JunkNamePattern = new(
    @"\((alpha\s*\d*|beta\s*\d*|proto(?:type)?\s*\d*|sample|sampler|demo|…|homebrew|aftermarket|translated|translation)\)",
    RegexOptions.IgnoreCase | RegexOptions.Compiled, …);
```

**Position in der Pipeline:**
- Wird als **erstes** in `Detect()` und `DetectWithConfidence()` geprüft (Zeile 183, 248)
- Bei Match: sofortiger Return mit `ConsoleKey = ""` / `UNKNOWN`
- **Alle** nachfolgenden Detektionsmethoden werden übersprungen

**Doppel-Effekt:**
1. ✅ Blockiert Junk-Dateien vor falscher Sortierung
2. ❌ Verhindert Konsolen-Identifikation für Junk → `UNKNOWN` statt `3DS + Junk`

**Ergebnis:** "Game (Demo) (USA).3ds" in Ordner `3ds/` wird:
- Ist: `ConsoleKey = UNKNOWN, Category = Junk` → TrueNegative im Benchmark
- Soll: `ConsoleKey = 3DS, Category = Junk` → JunkClassified im Benchmark

### 2.4 Scan-Phase — Aktueller Zustand

**Datei:** `src/RomCleanup.Infrastructure/Orchestration/StreamingScanPipelinePhase.cs`

```
Filter 1:  Extension-Whitelist (vom Caller übergeben)
Filter 2:  Pfad-Blocklist (6 Ordner: _TRASH_REGION_DEDUPE, _TRASH_JUNK, etc.)
Filter 3:  Set-Member-Pruning (M3U-referenzierte Dateien behalten)
```

**Fehlend:**
- ❌ Kein Non-ROM-Extension-Reject (Belt-and-Suspenders-Schutz)
- ❌ Keine Größen-Validierung (0-Byte-Dateien passieren)
- ❌ Keine Content-Signature-Prüfung (PDF/MP3/EXE-Header)

### 2.5 Pipeline-Architektur

```
┌─────────────────────────────────────────────────────────────┐
│  StreamingScanPipelinePhase                                 │
│  ├── Extension-Whitelist ✓                                  │
│  ├── Pfad-Blocklist ✓                                       │
│  └── Ergebnis: ScannedFileEntry                             │
│                    ↓                                        │
│  EnrichmentPipelinePhase.MapToCandidate                     │
│  ├── Zeile 71: FileClassifier.Analyze(baseName)  ← ⚠️ HIER│
│  │   └── Nur baseName, keine Extension, keine Größe         │
│  ├── Zeile 75: ConsoleDetector.DetectWithConfidence          │
│  │   └── IsLikelyJunkName blockt VOR Detektion  ← ⚠️ HIER │
│  ├── Zeile 80+: DAT-Matching, Hashing                       │
│  └── Ergebnis: RomCandidate                                 │
│                    ↓                                        │
│  DeduplicatePipelinePhase                                    │
│  ├── GetGameGroups: filtert Category == Game                 │
│  └── Winner-Selection: Category-Ranking berücksichtigt       │
│                    ↓                                        │
│  JunkRemovalPipelinePhase                                    │
│  └── NUR standalone Junk (0 Losers, Winner=Junk)  ← ⚠️     │
│                    ↓                                        │
│  ConsoleSortStep (StandardPhaseSteps.cs)                     │
│  ├── Gate 1: Category != Game → UNKNOWN           ← ⚠️     │
│  ├── Gate 2: ConsoleKey null/UNKNOWN → skip                 │
│  ├── Gate 3: DetectionConflict → skip                       │
│  ├── Gate 4: DetectionConfidence < 80 → skip                │
│  └── Alles passed → SORT                                    │
└─────────────────────────────────────────────────────────────┘
```

**Problem-Zusammenfassung:** FileClassifier ist kein Gate, sondern ein Label-Setter. Die eigentliche Gate-Logik (Category != Game → UNKNOWN) kommt erst im ConsoleSortStep — aber zu diesem Zeitpunkt ist die Datei bereits klassifiziert, detektiert, gehasht und dedupliziert worden. Der Default Game(90) hebelt das Gate aus.

### 2.6 Benchmark-Ergebnis — Status Quo

```
Verdict-Verteilung (1152 Samples):
  Correct:       902   (78,30 %)
  Acceptable:      0   ( 0,00 %)
  Wrong:            0   ( 0,00 %)
  Missed:          60   ( 5,21 %)
  TrueNegative:   44   ( 3,82 %)
  FalsePositive:  146   (12,67 %)

Ziel-Population (Junk + Unknown = 190):
  Korrekt blockiert (TrueNegative):  44  (23,158 %)
  Durchgeleckt (FalsePositive):     146  (76,842 %)
```

---

## 3. Fehlerbilder

### 3.1 FP-Vektor 1: Empty-ROM-Dateien (0 Bytes)

**Quelle:** `benchmark/ground-truth/negative-controls.jsonl` — ~20 Entries  
**Muster:** `empty_file.3ds`, `empty_file.gb`, `empty_file.nes`, etc.

| Stufe | Verhalten | Problem |
|-------|-----------|---------|
| FileClassifier | `"empty_file"` → kein Regex → **Game(90)** | Extension ignoriert |
| IsLikelyJunkName | Kein Junk-Tag → nicht blockiert | Size ignoriert |
| UniqueExtension | `.3ds` → "3DS", Confidence 95 | Valide Extension trotz 0 Bytes |
| Sorting Gate | Category=Game ✓, ConsoleKey="3DS" ✓, Confidence=95 ✓ | Alle Gates passed |
| **Ergebnis** | **FalsePositive** — 0-Byte-Datei wird als 3DS-Game sortiert | **Datenverlust-Risiko** |

**Impact:** ~20 FP direkt eliminierbar durch Size-Validation.

### 3.2 FP-Vektor 2: Non-ROM-Extension-Leakage

**Quelle:** `benchmark/ground-truth/negative-controls.jsonl` — 32 Entries  
**Muster:** `document.doc`, `music.mp3`, `manual.pdf`, `library.dll`, etc.

| Stufe | Verhalten | Problem |
|-------|-----------|---------|
| Scan-Whitelist | Sollte .doc/.mp3 blockieren | Benchmark umgeht Scan |
| FileClassifier | `"document"` → kein Regex → **Game(90)** | Extension ignoriert |
| ConsoleDetector | .doc nicht in Extension-Map → UNKNOWN | Keine Hypothese |
| **Ergebnis** | **TrueNegative** im Benchmark (kein Match) | Produktionsrisiko gering |

**Analyse:** Diese 32 Entries werden korrekt als UNKNOWN erkannt — NICHT weil das Kategorie-System funktioniert, sondern weil kein Detektionsverfahren greift. Der Schutz ist **zufällig**, nicht systematisch.

**Restrisiko:** Wenn eine Non-ROM-Extension (.bin, .iso) mit einem Konsolenordner zusammenfällt (z.B. `library.dll` in `gb/`), würde FolderName-Detection greifen = FP.

### 3.3 FP-Vektor 3: Ambiguous-Extension-Dateien

**Quelle:** Mystery files mit `.bin`, `.rom`, `.img`  
**Muster:** `mystery_file_010.bin`, `mystery_file_000.rom`

| Stufe | Verhalten | Problem |
|-------|-----------|---------|
| FileClassifier | `"mystery_file_010"` → **Game(90)** | Kein Marker |
| AmbiguousExtension | `.bin` → mehrere Konsolen, Confidence 40 | Niedrig |
| GroundTruthComparator | Soft-only → TrueNegative (geblockt) | Korrekt geblockt |

**Status:** Ambiguity-Einträge werden durch die Soft-Only-Blockade in GroundTruthComparator korrekt als TrueNegative gewertet. Aber: In der **Produktions-Pipeline** gibt es keinen Soft-Only-Block — dort greift nur die Confidence < 80 Gate-Bedingung. AmbiguousExtension(40) würde das Gate nicht passieren.

### 3.4 FP-Vektor 4: Junk-Dateien mit valider Extension + Ordner

**Quelle:** `benchmark/ground-truth/golden-realworld.jsonl` — 108 Entries  
**Muster:** `Game (Demo) (USA).3ds` in Ordner `3ds/`

| Stufe | Verhalten | Problem |
|-------|-----------|---------|
| FileClassifier | `"Game (Demo) (USA)"` → RxJunkTags → **Junk(95)** | ✅ Korrekt |
| IsLikelyJunkName | `(Demo)` matched → **UNKNOWN** sofort | ❌ Konsole verloren |
| ConsoleDetector | Gibt `ConsoleKey=""` zurück | Keine Hypothesen |
| GroundTruthComparator | Junk + UNKNOWN → **TrueNegative** | Metrik-Inflation |
| **Soll** | `ConsoleKey="3DS", Category=Junk` → **JunkClassified** | Console + Category |

**Architektur-Problem:** IsLikelyJunkName vermischt zwei orthogonale Achsen:
- **Achse 1:** "Welche Konsole?" (ConsoleDetector-Zuständigkeit)
- **Achse 2:** "Ist es Junk?" (FileClassifier-Zuständigkeit)

Durch die Blockade in ConsoleDetector wird Achse 1 für Junk-Dateien komplett ausgeschaltet. Das verhindert:
- Konsolen-spezifische Junk-Ordner (`_TRASH_JUNK/3DS/`)
- Korrekte JunkClassified-Metrik im Benchmark
- Review-Möglichkeit: "Diese 3DS-Demos könntest du trotzdem behalten"

### 3.5 FP-Vektor 5: Leere-ROM-Extension + Hard-Evidence-Leak

**Quelle:** UNKNOWN-System im Baseline: FP = 38

**Hypothese:** Einige der 82 Unknown-Entries (Empty-ROM-Extension-Dateien) werden nicht nur durch UniqueExtension erkannt, sondern triggern zusätzlich CartridgeHeader- oder andere Prüfungen die als Hard-Evidence gelten. Da der GroundTruthComparator für Case 1 (Negative Controls) bei Hard-Evidence **FalsePositive** vergibt statt TrueNegative, erzeugen diese 38 Entries direkt FP.

**Root Cause:** CartridgeHeader-/DiscHeader-Detection validiert keine Mindestgröße. Eine 0-Byte-Datei mit Extension `.gb` könnte theoretisch den Header-Check triggern, wenn der Byte-Zugriff nicht sauber fehlschlägt.

### 3.6 Sorting-Gate-Bypass — Zusammenfassung

```
Eingabe:        document.doc (0 Bytes, kein ROM-Inhalt)
FileClassifier: "document" → Game(90)           ← Game-Default
ConsoleDetector: .doc → keine Extension → UNKNOWN
Gate:           Category=Game ✓, ConsoleKey=UNKNOWN ✗ → BLOCKED

Eingabe:        empty_file.gb (0 Bytes)
FileClassifier: "empty_file" → Game(90)          ← Game-Default
ConsoleDetector: .gb → UniqueExtension → "GB"(95)
Gate:           Category=Game ✓, ConsoleKey="GB" ✓, Conf=95 ✓ → SORTED ← FP!

Eingabe:        Game (Demo) (USA).3ds (536 MB)
FileClassifier: "(Demo)" → Junk(95)              ← Korrekt
IsLikelyJunkName: "(Demo)" → UNKNOWN sofort
Gate:           Category=Junk ✗ → BLOCKED          ← Konsole verloren
```

---

## 4. Zielmodell

### 4.1 Architektur-Prinzip: Separation-of-Concerns

```
GETRENNTE ACHSEN:
  Achse 1: "Was ist es?"     → FileClassifier  (Game/Bios/Junk/NonGame/Unknown)
  Achse 2: "Welche Konsole?" → ConsoleDetector (GB/SNES/PS1/UNKNOWN)
  Achse 3: "Soll es sortiert werden?" → Sorting Gate (Kombination aus Achse 1 + 2)

AKTUELL VERMISCHT:
  IsLikelyJunkName beantwortet Achse 1 UND 2 gleichzeitig,
  indem es bei Junk die Konsolen-Erkennung komplett verhindert.
```

### 4.2 Drei-Stufen-Vorfilter-Modell

```
┌─────────────────────────────────────────────────────────────┐
│  STUFE 1 — Extension Gate (Scan-Level)                      │
│  ├── Non-ROM-Extension-Blocklist:                           │
│  │   .doc .mp3 .pdf .exe .dll .sys .msi .dmg .apk .ipa     │
│  │   .png .gif .bmp .jpg .html .xml .csv .json .log .ini    │
│  │   .bat .ps1 .nfo .sfv .torrent .url .lnk .pptx .xlsx    │
│  │   .ogg .flac .wav .ttf .avi .tmp .bak .old               │
│  ├── Ergebnis: Rejected → Audit-Eintrag "NON_ROM_REJECT"   │
│  └── Durchsatz: nur bekannte ROM-Extensions + Archive       │
│                    ↓                                        │
│  STUFE 2 — Plausibility Gate (Pre-Enrichment)               │
│  ├── Size-Validation:                                        │
│  │   0 Bytes → Category=Unknown(98, "zero-byte-file")       │
│  │   < 64 Bytes → Category=Unknown(95, "implausible-size")  │
│  ├── Optional: Content-Signature-Prüfung (Phase 2)          │
│  │   PDF-Header (%PDF) → NonGame                            │
│  │   MP3-Header (ID3/FF FB) → NonGame                       │
│  │   ELF/PE-Header → NonGame                                │
│  └── Ergebnis: Flagged mit Reason oder passiert             │
│                    ↓                                        │
│  STUFE 3 — Enhanced FileClassifier                           │
│  ├── Eingabe: baseName + extension + sizeBytes               │
│  ├── Extension-Aware Rules (vor Regex-Kaskade):              │
│  │   .nfo/.sfv/.txt/.cue → NonGame(92, "metadata-ext")     │
│  │   .exe/.com/.bat/.cmd → NonGame(95, "executable-ext")    │
│  │   Non-ROM + kein ROM-Content → Unknown(90, "non-rom")    │
│  ├── Size-Aware Rules (vor Regex-Kaskade):                   │
│  │   0 Bytes + ROM-Extension → Unknown(98, "zero-byte")     │
│  ├── Bestehende Regex-Kaskade (BIOS → Junk → NonGame)       │
│  └── Weiterhin fallback Game(90) für ungeflagte Dateien     │
└─────────────────────────────────────────────────────────────┘
```

### 4.3 Junk-Konsolen-Identifikation (Decoupled)

```
AKTUELL:
  IsLikelyJunkName → UNKNOWN (keine Konsole)
  FileClassifier → Junk(95)
  Ergebnis: ConsoleKey="", Category=Junk

ZIEL:
  FileClassifier → Junk(95) (Kategorie bestimmt)
  ConsoleDetector → "3DS"(95) (Konsole TROTZDEM erkannt)
  Ergebnis: ConsoleKey="3DS", Category=Junk
  
  Sorting Gate entscheidet:
    Category=Junk → SortDecision=JunkSort (→ _TRASH_JUNK/3DS/)
    Category=Game → SortDecision=Sort (→ 3DS/)
    Category=Unknown → SortDecision=Blocked
```

### 4.4 Erweiterte FileClassifier-Signatur

```csharp
// Bestehend (backward-compatible):
public static ClassificationDecision Analyze(string baseName, bool aggressiveJunk = false)

// Neu — extension- und size-aware:
public static ClassificationDecision Analyze(
    string baseName, 
    string extension, 
    long sizeBytes, 
    bool aggressiveJunk = false)
```

### 4.5 JunkClassified als eigener Metrik-Track

```
Benchmark-Verdicts (erweitert):
  Correct         — richtige Konsole und Kategorie
  JunkClassified  — Junk, aber richtige Konsolen-Familie erkannt
  TrueNegative    — korrekt als UNKNOWN blockiert
  FalsePositive   — fälschlich erkannt (falsche Konsole oder nicht blockiert)
  Wrong           — falsche Konsole
  Missed          — Konsole nicht erkannt
```

Im Baseline-JSON: JunkClassified als eigenes Feld tracken, nicht unter TrueNegative verbergen.

---

## 5. Konkrete technische Fixes

### Fix 1: Extension-Aware FileClassifier

**Datei:** `src/RomCleanup.Core/Classification/FileClassifier.cs`  
**Aufwand:** Mittel  
**Impact:** +30–40 % Category Recognition  
**Risiko:** Niedrig (additive Überladung, bestehende API bleibt)

**Änderung:**
- Neue `Analyze(baseName, extension, sizeBytes, aggressiveJunk)` Überladung
- Extension-Blocklist (Non-ROM-Extensions → NonGame/Unknown)
- Size-Gate: 0 Bytes → Unknown(98, "zero-byte-file")
- Size-Gate: < 64 Bytes UND ROM-Extension → Unknown(95, "implausible-size")
- Bestehende baseName-Regex-Kaskade bleibt als Fallback
- Bestehende `Analyze(baseName, aggressiveJunk)` delegiert an neue Überladung mit `extension=""`, `sizeBytes=-1`

**Betroffene Aufrufer:**
- `EnrichmentPipelinePhase.MapToCandidate` — Extension und SizeBytes verfügbar, Aufruf erweitern

### Fix 2: IsLikelyJunkName aus ConsoleDetector entfernen

**Datei:** `src/RomCleanup.Core/Classification/ConsoleDetector.cs`  
**Aufwand:** Niedrig  
**Impact:** ~108 Junk-Entries bekommen Konsolen-Erkennung  
**Risiko:** Mittel — Sortierung muss über Category-Gate geschützt bleiben

**Änderung:**
- `IsLikelyJunkName`-Aufrufe in `Detect()` (Zeile 183) und `DetectWithConfidence()` (Zeile 248) entfernen
- `JunkNamePattern`-Regex und `IsLikelyJunkName`-Methode können entfernt werden
- ConsoleDetector gibt für "Game (Demo) (USA).3ds" jetzt `ConsoleKey="3DS", Confidence=95` zurück
- Kategorie-Entscheidung liegt bei FileClassifier (der schon korrekt Junk(95) liefert)

**Schutz-Prüfung:**
- Sorting Gate in StandardPhaseSteps blockiert Category != Game → ✅ Schutz bleibt
- DeduplicationEngine-Category-Ranking: Junk(2) < Game(5) → ✅ Winner-Selection sicher
- JunkRemovalPipelinePhase greift für Standalone-Junk → ✅ Cleanup bleibt

**ACHTUNG:** Dieser Fix erfordert, dass das Sorting Gate und die Deduplication-Logik robust genug sind, um Junk-Dateien nicht als Game-Winner zu wählen. Aktuelle Prüfung zeigt: Die Gates sind ausreichend. Category != Game → UNKNOWN im SortStep. DeduplicationEngine rankt Junk(2) unter Game(5).

### Fix 3: Size-Validation in EnrichmentPipelinePhase

**Datei:** `src/RomCleanup.Infrastructure/Orchestration/EnrichmentPipelinePhase.cs`  
**Aufwand:** Niedrig  
**Impact:** ~20 FP von 0-Byte-Dateien eliminiert

**Änderung:**
- Nach `FileClassifier.Analyze()` (Zeile 71): Prüfung `sizeBytes == 0`
- Bei 0 Bytes UND Category == Game: Override `category = FileCategory.Unknown`
- Optional: ConsoleDetector-Aufruf überspringen für 0-Byte-Dateien (Performance)

**Alternative:** Diese Validierung wird auch in Fix 1 (FileClassifier v2) abgedeckt. Fix 3 ist eine Belt-and-Suspenders-Absicherung, falls Fix 1 nicht sofort implementiert wird.

### Fix 4: Non-ROM-Extension-Blocklist in Scan-Phase

**Datei:** `src/RomCleanup.Infrastructure/Orchestration/StreamingScanPipelinePhase.cs`  
**Aufwand:** Niedrig  
**Impact:** Defense-in-Depth auf Scan-Ebene

**Änderung:**
- Statische `HashSet<string>` mit bekannten Non-ROM-Extensions
- Prüfung nach Extension-Whitelist: Falls Extension in Blocklist → Skip mit Audit-Hinweis
- Blocklist komplementiert Whitelist (Whitelist = "was rein darf", Blocklist = "was definitiv nicht")

**Beziehung zu Whitelist:** Die Scan-Phase erhält bereits eine Extension-Whitelist vom Caller. Im Produktionsbetrieb sollten Non-ROM-Extensions nie in der Whitelist sein. Die Blocklist ist ein Sicherheitsnetz für den Fall, dass die Whitelist lückenhaft oder leer ist.

### Fix 5: Konsolen-Aware Junk-Sortierung

**Datei:** `src/RomCleanup.Infrastructure/Orchestration/JunkRemovalPipelinePhase.cs`  
**Aufwand:** Niedrig  
**Impact:** Bessere Junk-Organisation, User-Transparenz

**Änderung:**
- Statt `_TRASH_JUNK/{fileName}`: Verschiebe nach `_TRASH_JUNK/{ConsoleKey}/{fileName}`
- Nur wenn `ConsoleKey` bekannt (nicht UNKNOWN)
- Fallback: `_TRASH_JUNK/UNSORTED/{fileName}` für unbekannte Konsolen
- Audit-Eintrag enthält Konsolen-Key

**Voraussetzung:** Fix 2 (IsLikelyJunkName entfernen) liefert Konsolen-Key für Junk-Dateien.

### Fix 6: GroundTruthComparator und Baseline-Tracking

**Datei:** `src/RomCleanup.Tests/Benchmark/GroundTruthComparator.cs`  
**Datei:** `src/RomCleanup.Tests/Benchmark/MetricsAggregator.cs`

**Änderung GroundTruthComparator:**
- Bestehende JunkClassified-Logik ist korrekt (Case 0, Junk + correct console = JunkClassified)
- Nach Fix 2 werden die 108 Junk-Entries ConsoleKeys bekommen
- JunkClassified-Verdict wird AKTIV statt theoretisch

**Änderung MetricsAggregator:**
- JunkClassified separat zählen und im Baseline-JSON als eigenes Feld ausgeben
- `categoryRecognitionRate` hinzufügen: `(TrueNegative + JunkClassified) / (Junk + Unknown + NonGame)`
- `categoryLeakageRate` hinzufügen: `FalsePositive / (Junk + Unknown + NonGame)`

**Änderung Baseline-Schema:**
- Neues Feld `JunkClassified` in PerSystem und Global
- Neues Aggregate-Feld `categoryRecognitionRate`

---

## 6. Top 10 Maßnahmen (priorisiert)

| # | Maßnahme | Fix | Impact auf Category Recognition | Aufwand | Risiko |
|---|----------|-----|--------------------------------|---------|--------|
| **1** | **Extension-Aware FileClassifier** | Fix 1 | +30–40 % (Non-ROM + 0-Byte) | Mittel | Niedrig |
| **2** | **IsLikelyJunkName aus ConsoleDetector entfernen** | Fix 2 | +56 % (108 Junk → JunkClassified statt TN/FP) | Niedrig | Mittel |
| **3** | **Size-Validation (0-Byte → Unknown)** | Fix 3 | +10 % (~20 FP eliminiert) | Niedrig | Niedrig |
| **4** | **JunkClassified-Metrik nachtracken** | Fix 6 | Metrik-Korrektur, kein direkter FP-Impact | Niedrig | Niedrig |
| **5** | **Non-ROM-Extension-Blocklist im Scan** | Fix 4 | Defense-in-Depth | Niedrig | Niedrig |
| **6** | **Konsolen-Aware Junk-Sortierung** | Fix 5 | UX-Verbesserung, kein Metrik-Impact | Niedrig | Niedrig |
| **7** | **CartridgeHeader/DiscHeader Size-Minimum** | — | Verhindert Hard-Evidence-Leak bei 0-Byte | Niedrig | Niedrig |
| **8** | **NonGame Ground-Truth erweitern** | — | Nur 9 Entries → mindestens 30 | Niedrig | Keine |
| **9** | **Content-Signature-Prüfung (Phase 2)** | — | Fängt umbenannte Non-ROMs | Mittel | Niedrig |
| **10** | **categoryRecognitionRate als Benchmark-Metrik** | Fix 6 | Tracking und Regression-Gate | Niedrig | Keine |

### Empfohlene Reihenfolge

```
Phase 1 (sofort):  Fix 2 → Fix 1 → Fix 3 → Fix 6
Phase 2 (danach):  Fix 4 → Fix 5 → Maßnahme 7 → Maßnahme 8
Phase 3 (optional): Maßnahme 9 → Maßnahme 10
```

**Begründung Phase 1:**
1. Fix 2 (IsLikelyJunkName entfernen) ist die größte Einzel-Verbesserung — 108 Junk-Entries bekommen Konsolen-Erkennung
2. Fix 1 (Extension-Aware FileClassifier) fängt alle Non-ROM-Leakage systematisch
3. Fix 3 (Size-Validation) ist schnell und eliminiert die offensichtlichsten FPs
4. Fix 6 (Metrik-Tracking) stellt sicher, dass der Fortschritt messbar ist

### Erwartete Metrik-Entwicklung nach Phase 1

```
Ist-Zustand:
  TrueNegative:   44 / 190 = 23,158 %
  FalsePositive: 146 / 190 = 76,842 %

Nach Fix 2 (IsLikelyJunkName entfernen):
  JunkClassified: ~100 (Junk mit korrekter Konsole)
  TrueNegative:    ~44 (Unknown-Entries bleiben)
  FalsePositive:   ~46 (Unknown-Entries mit Hard-Evidence-Leak)
  Category Recognition: (44 + 100) / 190 ≈ 75,8 %

Nach Fix 1 + 3 (Extension + Size):
  JunkClassified: ~100
  TrueNegative:    ~80 (Unknown-Entries jetzt korrekt blockiert)
  FalsePositive:    ~10 (Restfälle mit Hard-Evidence + plausible Size)
  Category Recognition: (80 + 100) / 190 ≈ 94,7 %
```

---

## Anhang A: FP-Verteilung nach System

Systeme mit den meisten FalsePositives (Baseline):

| System | FP | Haupt-Ursache |
|--------|----|---------------|
| UNKNOWN | 38 | Empty-ROM + Ambig-Extension Leakage |
| GB | 4 | Empty .gb + Edge Cases |
| GBA | 4 | Empty .gba + Edge Cases |
| GBC | 4 | Empty .gbc + Edge Cases |
| MD | 4 | Empty .md + Edge Cases |
| N64 | 4 | Empty .n64 + Edge Cases |
| NES | 4 | Empty .nes + Edge Cases |
| PS1 | 4 | Empty .iso + Edge Cases |
| PS2 | 4 | Empty .iso + Edge Cases |
| SNES | 4 | Empty .sfc + Edge Cases |
| Alle anderen | 1–2 | Je 1 Empty-ROM + 0–1 Edge Case |

## Anhang B: Beziehung zu CONFIDENCE_SORTING_GATE_REDESIGN.md

Dieses Audit ergänzt das Confidence & Sorting Gate Redesign:

| Aspekt | Gate-Redesign | Category-Audit |
|--------|---------------|----------------|
| **Fokus** | Wann darf sortiert werden? | Was darf in die Pipeline? |
| **Metrik** | UnsafeSortRate (12,67 %) | Category Recognition (23,16 %) |
| **Lösung** | Härtere Gate-Bedingungen | Bessere Vorfilterung |
| **Overlap** | Soft-Only-Cap verhindert 90 % der FPs | Extension/Size-Filter verhindert 70 % der FPs |
| **Synergie** | Gate fängt auf, was Filter durchlässt | Filter reduziert Last auf das Gate |

**Implementierungs-Empfehlung:** Category Pre-Filter ZUERST implementieren, dann Gate-Hardening. Die Pre-Filter reduzieren die FP-Last drastisch, sodass das Gate nur noch Restfälle abfangen muss.

## Anhang C: Betroffene Dateien (Gesamtübersicht)

| Datei | Fixes |
|-------|-------|
| `src/RomCleanup.Core/Classification/FileClassifier.cs` | Fix 1 |
| `src/RomCleanup.Core/Classification/ConsoleDetector.cs` | Fix 2 |
| `src/RomCleanup.Infrastructure/Orchestration/EnrichmentPipelinePhase.cs` | Fix 1, Fix 3 |
| `src/RomCleanup.Infrastructure/Orchestration/StreamingScanPipelinePhase.cs` | Fix 4 |
| `src/RomCleanup.Infrastructure/Orchestration/JunkRemovalPipelinePhase.cs` | Fix 5 |
| `src/RomCleanup.Tests/Benchmark/GroundTruthComparator.cs` | Fix 6 |
| `src/RomCleanup.Tests/Benchmark/MetricsAggregator.cs` | Fix 6 |
| `benchmark/baselines/latest-baseline.json` | Fix 6 (Schema-Erweiterung) |
