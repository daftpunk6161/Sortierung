# Romulus – DAT-first Recognition Redesign

**Datum**: 2026-03-30
**Status**: Abgeschlossen / Umgesetzt / Validiert
**Scope**: Recognition, Classification, DAT-Matching, Sorting, Evidence-Pipeline

**Validierung (Stand 2026-03-30):**
- `dotnet build src/Romulus.sln` erfolgreich
- `dotnet test src/Romulus.Tests/Romulus.Tests.csproj` erfolgreich (6996/6996)

---

## 1. Executive Summary

### Kernerkenntnisse

Romulus hat bereits eine solide Grundarchitektur mit:
- Multi-Source Detection (8 Quellen, gewichtet)
- HypothesisResolver mit Confidence-Scoring und Conflict-Detection
- SortDecision-Gates (Sort / Review / Blocked / DatVerified)
- MatchEvidence-Modell mit Level und Reasoning
- Deterministische Winner-Selection

**Das Problem ist nicht die Architektur, sondern die Gewichtung und Reihenfolge.**

### Warum Romulus heute schwächer erkennt als DAT-first Tools

| Problem | Ursache | Impact |
|---------|---------|--------|
| **Detection-first statt DAT-first** | DAT-Lookup passiert NACH Detection in `EnrichmentPipelinePhase.MapToCandidate()`. ConsoleDetector liefert zuerst eine Hypothese, DAT korrigiert nur nachträglich. | Wenn Detection falsch rät, kann DAT nur noch reparieren statt autoritativ zu entscheiden |
| **Kein Family-Routing** | Alle Plattformen durchlaufen dieselbe Pipeline. Ein PS1-Disc-Image und eine NES-ROM werden identisch behandelt. | Disc-basierte Systeme brauchen andere Matching-Strategien als Cartridge-basierte |
| **MatchLevel zu grob** | `MatchLevel` hat 6 Stufen, aber keine Unterscheidung zwischen "ExactDatHash" und "StructuralHeaderMatch" | Ein Folder-Name-Match und ein Disc-Header-Match werden beide als "Strong" gewertet |
| **GameKey-Collision über Plattformgrenzen** | `GameKeyNormalizer` entfernt Plattform-Tags. "Final Fantasy VII" auf PS1 und PS2 erzeugt denselben Key | Cross-Platform-Dedup wenn ConsoleKey-Detection fehlschlägt |
| **Heuristik nicht abgestuft** | `AmbiguousExtension` (Confidence 40) und `FilenameKeyword` (Confidence 75) können beide zu Sort führen, wenn genug soft sources agrieren | False Positives bei Dateien mit irreführenden Ordnernamen/Tags |
| **UNKNOWN wird als Defekt behandelt** | UNKNOWN-Dateien werden blocked, aber es gibt kein bewusstes "unsicher, braucht menschliche Entscheidung"-Routing | User sieht "Blocked" ohne klares "warum" und was zu tun ist |

### Strategischer Hebel

**DAT-first Exact Matching als primäre Erkennungsquelle, Detection-Heuristik nur als Fallback.**

RomVault erreicht hohe Erkennungsquoten, weil es _ausschließlich_ DAT-driven arbeitet. Romulus soll nicht RomVault kopieren, aber:
1. DAT-Hash als absolute Autorität behandeln (Tier 0)
2. Strukturelle Evidenz (Header, Serial) als zweite Linie (Tier 1)
3. Heuristik nur noch als dritte Linie (Tier 2)
4. Alles andere bewusst als UNKNOWN/REVIEW führen (Tier 3)

---

## 2. Analyse des Ist-Zustands

> **Hinweis:** Dieser Abschnitt dokumentiert den **Ausgangszustand vor dem Redesign** (Stand vor 2026-03-30).
> Er dient als historische Referenz, um die Motivation und den Kontext der Architekturänderungen nachvollziehbar zu halten.
> Der aktuelle Stand ist in §3–§5 beschrieben und vollständig umgesetzt.

### 2.1 Aktuelle Pipeline-Reihenfolge

```
EnrichmentPipelinePhase.MapToCandidate():
  1. GameKeyNormalizer.Normalize()         ← Plattform-agnostisch
  2. FileClassifier.Analyze()              ← Filename-basiert
  3. ConsoleDetector.DetectWithConfidence() ← Multi-Source Heuristik
  4. RegionDetector + FormatScorer + VersionScorer
  5. LookupDat()                           ← DAT-Match als Nachkorrektur
  6. ResolveBios()
  7. ApplyDatAuthority()                   ← Überschreibt Detection nur bei Match
```

**Problem**: Schritt 3 (Detection) läuft VOR Schritt 5 (DAT). Die Detection bildet die Basis-Hypothese, DAT korrigiert nur.

### 2.2 Aktuelle Detection-Sources (DetectionSource enum)

| Source | Confidence | Cap | Hard? | Problem |
|--------|-----------|-----|-------|---------|
| DatHash | 100 | 100 | ✅ | Nur als Nachkorrektur, nicht als primäre Quelle |
| UniqueExtension | 95 | 95 | ✅ | Gut, aber wenige Extensions sind wirklich unique |
| DiscHeader | 92 | 92 | ✅ | Gut für erkannte Formate, deckt nicht alle ab |
| CartridgeHeader | 90 | 90 | ✅ | Solide für bekannte Header-Formate |
| SerialNumber | 88 | 75 | ❌ | Cap bei 75 als Single-Source — korrekt, aber Serial allein ist oft ausreichend |
| FolderName | 85 | 80 | ❌ | **Zu hoch**. Ordner können falsch benannt sein |
| ArchiveContent | 80 | 70 | ❌ | Gut, aber abhängig von Inner-Extension |
| FilenameKeyword | 75 | 60 | ❌ | **Gefährlich**. "[PS1]" im Filename kann falsch sein |
| AmbiguousExtension | 40 | 55 | ❌ | Korrekt niedrig |

### 2.3 Hauptschwächen

#### A. DAT-Match ist Nachkorrektur statt Primärquelle

In `EnrichmentPipelinePhase.LookupDat()`:
- Wenn `consoleKey` bereits gesetzt ist, wird nur in diesem einen Console-DAT gesucht
- Wenn Detection "PS2" sagt, aber die Datei im PS1-DAT steht → **Miss**
- `ResolveUnknownDatMatch()` wird nur bei `UNKNOWN`/`AMBIGUOUS` aufgerufen
- Bei falschem Detection-Ergebnis findet kein Cross-Console-DAT-Lookup statt

#### B. Kein Family-basiertes Routing

Aktuelle Pipeline:
```
Alle Dateien → dieselbe Enrichment-Logik → dieselbe Detection → dieselbe Scoring
```

Was fehlt:
- Disc-Images brauchen Redump-Track-SHA1, nicht Container-SHA1
- CHD braucht RAW-SHA1-Extraktion aus Header
- Arcade-ROMs brauchen Set-basierte Validierung (Parent/Clone)
- Computer-ROMs (TOSEC) brauchen Disk-Image-Handling
- No-Intro Cartridge braucht Headerless-Hashing

#### C. GameKey-Collision

`GameKeyNormalizer` erzeugt identische Keys über Plattformgrenzen:
```
"Final Fantasy VII (USA).iso"  → "final fantasy vii"  (PS1)
"Final Fantasy VII (USA).iso"  → "final fantasy vii"  (PS2)
```

Die Dedup-Engine gruppiert nach `GameKey` allein. Wenn beide Dateien im selben Root liegen und ConsoleKey falsch erkannt wird → Cross-Platform-Dedup.

**Aktueller Schutz**: `DeduplicationEngine` nutzt `ConsoleKey` nicht als Gruppierungskriterium. Es gruppiert nur nach `GameKey`. Der Schutz kommt ausschließlich davon, dass Detection den ConsoleKey korrekt setzt und die Dateien dann in verschiedene Console-Ordner sortiert werden.

#### D. Zu aggressive Soft-Evidence

- `FolderName` bei Confidence 85 (Cap 80) kann allein zu `SortDecision.Review` führen
- Zwei Soft-Sources zusammen (Folder 85 + Keyword 75) ergeben nach `MultiSourceAgreementBonus` (+15) → 100 → `Sort`
- Das ist **zu aggressiv**: Ordner + Keyword-Tag zusammen sind kein sicherer Beweis

#### E. UNKNOWN/REVIEW ohne Handlungsanleitung

- `SortDecision.Blocked` bedeutet "nicht sortieren", aber der User weiß nicht warum
- `SortDecision.Review` bedeutet "manuell prüfen", aber es gibt keine konkrete Empfehlung
- Kein `DecisionClass.Unknown` als explizite Kategorie "wir wissen es nicht"

### 2.4 Hauptursachen für False Positives

| Szenario | Root Cause |
|----------|-----------|
| PS1-Disc als PS2 klassifiziert | Detection über Folder/Keyword statt DAT. Beide teilen `.bin`/`.cue`/`.iso` |
| Dreamcast-GDI als Saturn erkannt | `.bin`-Extension ambiguous, GDI-Header-Scan nicht immer erfolgreich |
| Arcade-ROM als NES erkannt | Dateiname enthält "[NES]" als Tag, aber ist eigentlich MAME-Set |
| Computer-Disk-Image als Konsole erkannt | `.dsk`/`.img` geteilt zwischen Amiga, MSX, CPC, Atari ST |
| Multi-Region-ROM als falsche Region | GameKeyNormalizer entfernt Region-Tags, Dedup-Scoring wählt dann falsche Region |
| BIOS als Game klassifiziert | `FileClassifier` erkennt nicht alle BIOS-Patterns |

### 2.5 Hauptursachen für Misses

| Szenario | Root Cause |
|----------|-----------|
| DAT vorhanden, aber kein Match | Detection setzt falschen ConsoleKey → DAT-Lookup in falschem Index |
| CHD-Datei ohne DAT-Match | CHD-Container-SHA1 ≠ Redump-Track-SHA1. Nur Name-Fallback |
| Headerless Hash nicht versucht | `headerlessHasher` nur bei bekanntem ConsoleKey, nicht bei UNKNOWN |
| Compressed ROM nicht erkannt | ZIP-Container-Hash ≠ Inner-ROM-Hash, ArchiveHashService nicht immer verfügbar |
| No-Intro Daily Pack nicht geladen | PackMatch-Pattern in dat-catalog.json fehlt oder DatRoot-Layout nicht erkannt |

---

## 3. Zielarchitektur

### 3.1 Evidence Tiers

Neue hierarchische Evidenz-Einstufung als Ersatz für die flache `DetectionSource`-Confidence:

```csharp
/// <summary>
/// Hierarchical evidence tier — determines trust level for sort decisions.
/// Higher tier = higher trust. Only Tier0 and Tier1 allow auto-sort.
/// </summary>
public enum EvidenceTier
{
    /// <summary>DAT hash exact match. Absolute authority. Always auto-sortable.</summary>
    Tier0_ExactDat = 0,

    /// <summary>Structural binary evidence (header magic, serial, disc signature).
    /// Auto-sortable when unambiguous.</summary>
    Tier1_Structural = 1,

    /// <summary>Strong heuristic (unique extension, archive content analysis).
    /// Review-gate required.</summary>
    Tier2_StrongHeuristic = 2,

    /// <summary>Weak heuristic (folder name, filename keyword, ambiguous extension).
    /// Never auto-sortable. Always Review or Blocked.</summary>
    Tier3_WeakHeuristic = 3,

    /// <summary>No evidence. Unknown. Blocked.</summary>
    Tier4_Unknown = 4,
}
```

**Mapping der bestehenden DetectionSources zu Tiers:**

| DetectionSource | Neuer Tier | Begründung |
|-----------------|-----------|------------|
| DatHash | Tier0_ExactDat | Hash-verifiziert, absolute Autorität |
| UniqueExtension | Tier2_StrongHeuristic | Gut, aber nicht binary-verifiziert |
| DiscHeader | Tier1_Structural | Binary Signatur, vertrauenswürdig |
| CartridgeHeader | Tier1_Structural | Binary Signatur, vertrauenswürdig |
| SerialNumber | Tier1_Structural | Strukturell aus Filename/Header |
| FolderName | Tier3_WeakHeuristic | Kontextabhängig, nicht vertrauenswürdig |
| ArchiveContent | Tier2_StrongHeuristic | Inner-Extension-Analyse |
| FilenameKeyword | Tier3_WeakHeuristic | Rein heuristisch |
| AmbiguousExtension | Tier3_WeakHeuristic | Mehrdeutig |

### 3.2 Match Kinds

Neues `MatchKind`-Enum zur präzisen Beschreibung WIE ein Match zustande kam:

```csharp
/// <summary>
/// Describes exactly how a recognition match was established.
/// Used for explainability, audit trail, and trust calibration.
/// </summary>
public enum MatchKind
{
    /// <summary>No match found.</summary>
    None = 0,

    // --- Tier 0: DAT-verified ---
    /// <summary>Exact hash match against DAT index (SHA1/SHA256/MD5).</summary>
    ExactDatHash,
    /// <summary>Archive inner file hash matches DAT entry.</summary>
    ArchiveInnerExactDat,
    /// <summary>Headerless hash matches No-Intro DAT entry.</summary>
    HeaderlessDatHash,
    /// <summary>CHD raw SHA1 matches Redump DAT entry.</summary>
    ChdRawDatHash,

    // --- Tier 1: Structural ---
    /// <summary>Disc header binary signature (SEGA, PLAYSTATION, etc.).</summary>
    DiscHeaderSignature,
    /// <summary>Cartridge header magic bytes (iNES, N64, SNES, etc.).</summary>
    CartridgeHeaderMagic,
    /// <summary>Serial number extracted from filename (SLUS-xxxxx, etc.).</summary>
    SerialNumberMatch,
    /// <summary>CHD metadata tag identifies platform (CHGD = Dreamcast GD-ROM).</summary>
    ChdMetadataTag,

    // --- Tier 2: Strong Heuristic ---
    /// <summary>File extension unique to one console.</summary>
    UniqueExtensionMatch,
    /// <summary>Archive interior extension analysis.</summary>
    ArchiveContentExtension,
    /// <summary>DAT game name match (no hash verification).</summary>
    DatNameOnlyMatch,

    // --- Tier 3: Weak Heuristic ---
    /// <summary>Folder path matches a console alias.</summary>
    FolderNameMatch,
    /// <summary>Filename contains system keyword tag.</summary>
    FilenameKeywordMatch,
    /// <summary>Ambiguous extension with single-console resolution.</summary>
    AmbiguousExtensionSingle,

    // --- Tier 3+: Guesses ---
    /// <summary>Filename pattern guess (no structural backing).</summary>
    FilenameGuess,
}
```

### 3.3 Decision Classes

Erweiterte `SortDecision` → `DecisionClass` mit klarer Semantik:

```csharp
/// <summary>
/// Final classification decision for a ROM candidate.
/// Determines processing path: auto-sort, manual review, blocked, or unknown.
/// </summary>
public enum DecisionClass
{
    /// <summary>
    /// Auto-sort allowed. High-confidence match with strong evidence.
    /// Requires: Tier0 or Tier1 unambiguous + no conflict.
    /// </summary>
    Sort,

    /// <summary>
    /// DAT-verified sort. Hash-matched against authoritative DAT.
    /// Highest trust level. Subset of Sort.
    /// </summary>
    DatVerified,

    /// <summary>
    /// Manual review recommended. Plausible match but insufficient confidence.
    /// File moved to _REVIEW/{ConsoleKey}/ with evidence summary.
    /// </summary>
    Review,

    /// <summary>
    /// Sorting blocked. Conflict detected or evidence too weak.
    /// File stays in place. Requires user action.
    /// </summary>
    Blocked,

    /// <summary>
    /// No evidence at all. File is genuinely unknown.
    /// Different from Blocked: no conflicting evidence, just no evidence.
    /// </summary>
    Unknown,
}
```

**Upgrade-Pfad**: `SortDecision` → `DecisionClass` (Rename + neues `Unknown`)

### 3.4 Plattformfamilien

Separate Recognition-Strategies pro Familie:

```
┌─────────────────────────────────────────────────────┐
│              Recognition Pipeline                    │
│                                                      │
│  ┌──────────┐  ┌──────────┐  ┌─────────┐           │
│  │ DAT-first │→│ Structural│→│Heuristic │→ Unknown  │
│  │ (Tier 0)  │  │ (Tier 1) │  │(Tier 2-3)│           │
│  └──────────┘  └──────────┘  └─────────┘           │
│       │              │              │                │
│  ┌────┴────┐   ┌────┴────┐   ┌────┴────┐           │
│  │ Family   │   │ Family   │   │ Family   │           │
│  │ Router   │   │ Router   │   │ Router   │           │
│  └────┬────┘   └────┬────┘   └────┬────┘           │
│       │              │              │                │
│  ┌────┴──────────────┴──────────────┴────┐          │
│  │                                        │          │
│  │  NoIntroCartridge  │  RedumpDisc       │          │
│  │  Arcade/MAME       │  Computer/TOSEC   │          │
│  │  Hybrid            │  FolderBased      │          │
│  │                                        │          │
│  └────────────────────────────────────────┘          │
└─────────────────────────────────────────────────────┘
```

#### Familien-Definition

| Familie | Konsolen | Erkennungsstrategie | DAT-Source |
|---------|----------|---------------------|-----------|
| **NoIntroCartridge** | NES, SNES, N64, GB, GBC, GBA, MD, SMS, GG, PCE, Lynx, 7800, WSC, NGP, VB, Jaguar, 32X | Headerless Hash → No-Intro DAT, Cartridge-Header, Unique-Extension | No-Intro |
| **RedumpDisc** | PS1, PS2, PS3, PSP, Saturn, Dreamcast, GameCube, Wii, WiiU, Xbox, X360, 3DO, PCECD, PCFX, SCD, NEOCD, CDi, CD32, CDTV, Naomi | Track-SHA1 → Redump DAT, Disc-Header, CHD-Metadata, Serial | Redump / Non-Redump |
| **Arcade** | MAME, FBNeo, NAOMI, Atomiswave, CPS1/2/3 | Set-Structure (Parent/Clone), ZIP-Inner-Hash, ROM-Name-in-Set | MAME/FBNeo DAT |
| **ComputerTOSEC** | Amiga, DOS, AtariST, MSX, C64, CPC, ZXSpectrum, FMTowns, X68000, PC98, Sharp | TOSEC-Naming-Convention, Disk-Image-Type, Folder-Structure | TOSEC |
| **FolderBased** | PS3 (extracted), Xbox360 (GOD), WiiU (loadiine) | Folder-Hash (PS3_DISC.SFB + PARAM.SFO), Folder-Structure | Spezial |
| **Hybrid** | Sonderfälle, Konvertierte Formate (.pbp, .nkit, .rvz, .wbfs) | Container-spezifisch | Kombination |

#### Family-Router in `consoles.json`

Erweiterung der Console-Definition:

```json
{
  "key": "PS1",
  "displayName": "Sony PlayStation",
  "family": "RedumpDisc",
  "discBased": true,
  "hashStrategy": "track-sha1",
  "datSources": ["redump", "non-redump"],
  ...
}
```

```json
{
  "key": "NES",
  "displayName": "Nintendo Entertainment System",
  "family": "NoIntroCartridge",
  "discBased": false,
  "hashStrategy": "headerless-sha1",
  "headerSize": 16,
  "datSources": ["nointro"],
  ...
}
```

### 3.5 Single Source of Truth

| Konzept | Aktuell | Ziel |
|---------|---------|------|
| Console-Erkennung | `ConsoleDetector` + DAT-Nachkorrektur in `EnrichmentPipelinePhase` | `RecognitionPipeline` mit DAT-first, dann Structural, dann Heuristic |
| Sort-Entscheidung | `HypothesisResolver.DetermineSortDecision()` + `EnrichmentPipelinePhase.ApplyDatAuthority()` | Ein einziger `DecisionResolver` der alle Evidenz zusammenführt |
| Evidence | `MatchEvidence` (Level + Reasoning) | `RecognitionEvidence` (Tier + MatchKind + Sources + DecisionClass) |
| GameKey-Scope | Global (plattformübergreifend) | Plattform-scoped: `ConsoleKey + GameKey` als Composite-Key |

---

## 4. Konkrete Architekturänderungen

### 4.1 Neue Models (in `Romulus.Contracts/Models/`)

#### `EvidenceTier.cs` (NEU)
```csharp
public enum EvidenceTier { Tier0_ExactDat, Tier1_Structural, Tier2_StrongHeuristic, Tier3_WeakHeuristic, Tier4_Unknown }
```

#### `MatchKind.cs` (NEU)
```csharp
public enum MatchKind { None, ExactDatHash, ArchiveInnerExactDat, HeaderlessDatHash, ChdRawDatHash,
    DiscHeaderSignature, CartridgeHeaderMagic, SerialNumberMatch, ChdMetadataTag,
    UniqueExtensionMatch, ArchiveContentExtension, DatNameOnlyMatch,
    FolderNameMatch, FilenameKeywordMatch, AmbiguousExtensionSingle, FilenameGuess }
```

#### `PlatformFamily.cs` (NEU)
```csharp
public enum PlatformFamily { NoIntroCartridge, RedumpDisc, Arcade, ComputerTOSEC, FolderBased, Hybrid, Unknown }
```

#### `RecognitionResult.cs` (NEU)
Vereint alle Evidenz in einem einzigen Objekt:
```csharp
public sealed record RecognitionResult
{
    public string ConsoleKey { get; init; } = "UNKNOWN";
    public EvidenceTier Tier { get; init; } = EvidenceTier.Tier4_Unknown;
    public MatchKind PrimaryMatchKind { get; init; } = MatchKind.None;
    public DecisionClass Decision { get; init; } = DecisionClass.Unknown;
    public int Confidence { get; init; }
    public PlatformFamily Family { get; init; } = PlatformFamily.Unknown;
    public bool DatVerified { get; init; }
    public string? DatGameName { get; init; }
    public IReadOnlyList<RecognitionSignal> Signals { get; init; } = [];
    public string Reasoning { get; init; } = "";
    public bool HasConflict { get; init; }
}
```

#### `RecognitionSignal.cs` (NEU)
Ersetzt `DetectionHypothesis` mit reichhaltiger Evidenz:
```csharp
public sealed record RecognitionSignal(
    string ConsoleKey,
    EvidenceTier Tier,
    MatchKind Kind,
    int Confidence,
    string Evidence);
```

#### Erweiterung `RomCandidate.cs`
Neue Felder:
```csharp
public EvidenceTier EvidenceTier { get; init; } = EvidenceTier.Tier4_Unknown;
public MatchKind PrimaryMatchKind { get; init; } = MatchKind.None;
public PlatformFamily PlatformFamily { get; init; } = PlatformFamily.Unknown;
```

#### Erweiterung `consoles.json`
Neue Felder pro Console: `family`, `hashStrategy`, `datSources`.

### 4.2 Pipeline-Umbau: DAT-first Enrichment

**Aktuell** (in `EnrichmentPipelinePhase.MapToCandidate()`):
```
1. GameKey
2. Classification
3. ConsoleDetector.DetectWithConfidence()  ← Heuristik zuerst
4. Scoring
5. LookupDat()                             ← DAT als Nachkorrektur
6. ApplyDatAuthority()
```

**Neu**:
```
1. GameKey
2. Classification
3. DAT-first Recognition:                  ← DAT ZUERST
   a. Hash berechnen (container + archive-inner + headerless)
   b. LookupAllByHash() über ALLE DATs     ← Cross-Console-Lookup
   c. Wenn 1 Match → Tier0, DatVerified
   d. Wenn N Matches → Disambiguation via Structural Detection
   e. Wenn 0 Matches → weiter zu Structural
4. Structural Detection (nur wenn kein DAT-Match):
   a. DiscHeaderDetector
   b. CartridgeHeaderDetector
   c. SerialNumber
   d. CHD Metadata
   e. → Tier1 wenn eindeutig
5. Heuristic Detection (nur wenn kein Structural-Match):
   a. UniqueExtension
   b. ArchiveContent
   c. FolderName
   d. FilenameKeyword
   e. → Tier2 oder Tier3
6. Decision Resolution:
   a. RecognitionResult aus Signals ableiten
   b. DecisionClass bestimmen
   c. Family zuweisen
7. Scoring (wie bisher)
```

### 4.3 Betroffene Dateien und Services

#### Kern-Änderungen (MUST)

| Datei | Änderung | Risiko |
|-------|----------|--------|
| `Contracts/Models/EvidenceTier.cs` | **NEU** | Low |
| `Contracts/Models/MatchKind.cs` | **NEU** | Low |
| `Contracts/Models/PlatformFamily.cs` | **NEU** | Low |
| `Contracts/Models/RecognitionResult.cs` | **NEU** | Low |
| `Contracts/Models/RecognitionSignal.cs` | **NEU** | Low |
| `Contracts/Models/RomCandidate.cs` | Erweitert um Tier/Kind/Family | Medium – alle Consumer müssen kompatibel bleiben |
| `Contracts/Models/MatchEvidence.cs` | Erweitert um Tier/Kind | Medium |
| `Contracts/Models/SortDecision.cs` | + `Unknown` Wert hinzufügen | Medium – alle switch-Statements prüfen |
| `Infrastructure/Orchestration/EnrichmentPipelinePhase.cs` | **Hauptumbau**: DAT-first Pipeline | **HIGH** – Kernlogik |
| `Core/Classification/HypothesisResolver.cs` | Erweitert um Tier-Awareness | HIGH |
| `Core/Classification/DetectionHypothesis.cs` | Erweitert um Tier/Kind | Medium |
| `Core/Classification/ConsoleDetector.cs` | Refactored: liefert Structural-Hypothesen, nicht mehr SortDecision | HIGH |
| `data/consoles.json` | + `family`, `hashStrategy`, `datSources` pro Console | Medium |
| `Infrastructure/Sorting/ConsoleSorter.cs` | Routing erweitert um `Unknown` | Medium |

#### Nachgelagerte Änderungen (SHOULD)

| Datei | Änderung |
|-------|----------|
| `Core/Deduplication/DeduplicationEngine.cs` | Composite-Key `ConsoleKey+GameKey` statt nur `GameKey` |
| `Infrastructure/Orchestration/RunProjection.cs` | Neue Metriken: Tier-Verteilung, Family-Verteilung |
| `Infrastructure/Reporting/` | Evidence-Tier in Reports anzeigen |
| `CLI/Program.cs` | Neue Output-Spalten |
| `Api/` | Neue API-Felder |
| `UI.Wpf/` | Tier/Decision in GUI anzeigen |

### 4.4 Cross-Console DAT-Lookup (Kernumstellung)

**Aktuell**: `LookupDat()` sucht nur im DAT des erkannten ConsoleKey:
```csharp
// Aktuell: Nur wenn consoleKey bekannt ist
var byConsole = datIndex.LookupWithFilename(consoleKey, innerHash);
```

**Neu**: Immer zuerst ALLE DATs durchsuchen:
```csharp
// NEU: Cross-Console-Lookup als erste Aktion
var allMatches = datIndex.LookupAllByHash(hash);

if (allMatches.Count == 1)
{
    // Eindeutig: Tier0, DatVerified
    return Tier0Result(allMatches[0]);
}
else if (allMatches.Count > 1)
{
    // Mehrdeutig: Structural Detection zur Disambiguation
    var structural = RunStructuralDetection(filePath);
    var resolved = DisambiguateWithStructural(allMatches, structural);
    return resolved ?? Tier0AmbiguousReview(allMatches);
}
else
{
    // Kein DAT-Match: weiter zu Structural → Heuristic
    return FallbackToDetection(filePath, rootPath);
}
```

**Wichtig**: `DatIndex.LookupAllByHash()` existiert bereits. Es wird aktuell nur für UNKNOWN-Dateien aufgerufen. Im neuen Design wird es für ALLE Dateien aufgerufen.

### 4.5 Dedup-Schutz: Composite GameKey

**Aktuell**:
```csharp
// DeduplicationEngine gruppiert nach GameKey allein
var groups = candidates.GroupBy(c => c.GameKey, StringComparer.OrdinalIgnoreCase);
```

**Neu**:
```csharp
// Composite-Key: ConsoleKey + GameKey
var groups = candidates.GroupBy(
    c => $"{c.ConsoleKey}||{c.GameKey}",
    StringComparer.OrdinalIgnoreCase);
```

Dies verhindert Cross-Platform-Dedup selbst bei identischen Spielnamen.

### 4.6 Decision-Resolution-Logik

Neuer zentraler `DecisionResolver` (in Core):

```csharp
public static class DecisionResolver
{
    public static DecisionClass Resolve(EvidenceTier tier, bool hasConflict, int confidence)
    {
        // Tier 0: DAT-verified → immer Sort
        if (tier == EvidenceTier.Tier0_ExactDat && !hasConflict)
            return DecisionClass.DatVerified;

        // Tier 0 mit Conflict (DAT in mehreren Konsolen) → Review
        if (tier == EvidenceTier.Tier0_ExactDat && hasConflict)
            return DecisionClass.Review;

        // Tier 1: Structural, eindeutig → Sort
        if (tier == EvidenceTier.Tier1_Structural && !hasConflict && confidence >= 85)
            return DecisionClass.Sort;

        // Tier 1 mit Conflict oder grenzwertig → Review
        if (tier == EvidenceTier.Tier1_Structural)
            return DecisionClass.Review;

        // Tier 2: Strong Heuristic → immer Review
        if (tier == EvidenceTier.Tier2_StrongHeuristic)
            return DecisionClass.Review;

        // Tier 3: Weak Heuristic → Blocked
        if (tier == EvidenceTier.Tier3_WeakHeuristic)
            return DecisionClass.Blocked;

        // Tier 4: Unknown → Unknown
        return DecisionClass.Unknown;
    }
}
```

**Kritisch**: Tier 2 (Strong Heuristic) führt IMMER zu Review, nie zu Sort. Nur Tier 0 und Tier 1 erlauben Auto-Sort.

### 4.7 Konsolidierung: Was entfernt/ersetzt wird

| Aktuell | Neuer Status | Begründung |
|---------|-------------|------------|
| `DetectionSource.IsHardEvidence()` | Compat-Layer (beibehalten) | Genutzt für abwärtskompatible Confidence-Berechnung. Tier-Gate ist autoritative Decision-Quelle. |
| `DetectionSource.SingleSourceCap()` | Compat-Layer (beibehalten) | Confidence-Caps bleiben, Tier-Gate entscheidet. |
| `HypothesisResolver.ComputeSoftOnlyCap()` | Compat-Layer (beibehalten) | Begrenzt Confidence, aber Decision kommt aus `DecisionResolver`. |
| `HypothesisResolver.MultiSourceAgreementBonus` | Compat-Layer (beibehalten) | Confidence-Aggregation bleibt, Bonus darf nicht Tier-Gate überspringen. |
| `EnrichmentPipelinePhase.ApplyDatAuthority()` | **Entfernt** ✅ | In DAT-first Pipeline integriert. DAT ist Primärquelle. |
| `MatchLevel` enum | Compat-Layer (beibehalten) | Intern aus `EvidenceTier`/`MatchKind` abgeleitet. |

**Kompatibilitätsschicht (bewusst beibehalten):**

Folgende Legacy-Konstrukte bleiben als Kompatibilitätsschicht erhalten, bis alle Consumer vollständig auf das neue Tier-System migriert sind:

| Legacy-Konstrukt | Status | Begründung |
|-----------------|--------|------------|
| `MatchLevel` enum + `MatchEvidence` | Beibehalten | Intern aus `EvidenceTier`/`MatchKind` abgeleitet. Consumer (Reports, API, GUI) nutzen beides parallel. |
| `DetectionSource.IsHardEvidence()` | Beibehalten | Genutzt in Tests und HypothesisResolver für abwärtskompatible Confidence-Berechnung. |
| `DetectionSource.SingleSourceCap()` | Beibehalten | Genutzt im HypothesisResolver für Confidence-Caps. Decision kommt aus Tier-Gate via `DecisionResolver`. |
| `HypothesisResolver.ComputeSoftOnlyCap()` | Beibehalten | Begrenzt Confidence für Soft-only-Quellen. Tier-Gate via `DecisionResolver` ist die autoritative Decision-Quelle. |
| `HypothesisResolver.MultiSourceAgreementBonus` | Beibehalten | Confidence-Aggregation bleibt, aber Decision kommt aus Tier, nicht aus Confidence allein. |
| `EnrichmentPipelinePhase.ApplyDatAuthority()` | **Entfernt** | In DAT-first Pipeline integriert. DAT ist Primärquelle, nicht Nachkorrektur. |

Die Entfernung der verbleibenden Legacy-Konstrukte erfolgt erst nach vollständiger Migration aller Consumer. Die autoritative Decision-Quelle ist immer `DecisionResolver.Resolve()` — die Legacy-Confidence-Berechnung dient nur noch der abwärtskompatiblen Confidence-Zahl.

---

## 5. Phasenweiser Umsetzungsplan

### Phase 1: Foundation Models & DAT-first Lookup (2-3 Wochen)

**Ziel**: Neue Models einführen, Cross-Console DAT-Lookup implementieren.

**Umfang**:
1. `EvidenceTier`, `MatchKind`, `PlatformFamily`, `RecognitionSignal` als neue Enums/Records in Contracts
2. `RomCandidate` um `EvidenceTier`, `PrimaryMatchKind`, `PlatformFamily` erweitern
3. `SortDecision` um `Unknown` erweitern
4. `consoles.json` um `family` Feld erweitern
5. `EnrichmentPipelinePhase.LookupDat()` umbauen: Cross-Console-Lookup IMMER, nicht nur bei UNKNOWN
6. `DatIndex.LookupAllByHash()` als primäre Lookup-Methode
7. `EvidenceTier` aus DAT-Match-Ergebnis ableiten und in Candidate setzen

**Betroffene Dateien**:
- `Contracts/Models/` (5 neue Dateien + 3 erweiterte)
- `data/consoles.json`
- `Infrastructure/Orchestration/EnrichmentPipelinePhase.cs` (Hauptumbau LookupDat)
- `Infrastructure/Orchestration/CandidateFactory.cs`

**Risiken**:
- Cross-Console-Lookup ist teurer (alle DATs statt nur eines). Mitigation: Hash-Index ist bereits O(1), Multi-DAT-Lookup ist O(N_consoles) mit kleinem N.
- `SortDecision.Unknown` bricht alle existierenden switch-Statements. Mitigation: Compiler-Warnings als Gate.

**Abnahmekriterien**:
- [x] Neue Models kompilieren und sind in Contracts verfügbar
- [x] Cross-Console DAT-Lookup findet Matches die vorher verpasst wurden
- [x] Kein Regressions-Break bei bestehenden Tests
- [x] Benchmark: DAT-Match-Rate steigt um ≥5% bei Testset

### Phase 2: Evidence Tier Pipeline & Decision Resolution (2-3 Wochen)

**Ziel**: Tier-basiertes Decision-Gating statt Confidence-Threshold-Gating.

**Umfang**:
1. `DecisionResolver` als neue Klasse in Core
2. `HypothesisResolver` refactored: liefert `RecognitionResult` statt `ConsoleDetectionResult`
3. Enrichment-Pipeline nutzt Tier-basierte Decision statt nur Confidence
4. `ConsoleSorter` routing erweitert um `Unknown`
5. Backward-Compat: `MatchLevel` wird aus `EvidenceTier`/`MatchKind` abgeleitet

**Betroffene Dateien**:
- `Core/Classification/HypothesisResolver.cs` (Refactor)
- `Core/Classification/DecisionResolver.cs` (NEU)
- `Infrastructure/Orchestration/EnrichmentPipelinePhase.cs` (Decision-Ableitung)
- `Infrastructure/Sorting/ConsoleSorter.cs` (Unknown-Routing)

**Risiken**:
- Behavior-Change bei Sort/Review/Blocked-Verteilung. Viele Dateien die vorher `Sort` waren könnten jetzt `Review` werden.
- Mitigation: Dual-Mode (alt + neu parallel) mit Benchmark-Vergleich.

**Abnahmekriterien**:
- [x] False-Positive-Rate sinkt um ≥30%
- [x] Kein Datenverlust (keine Datei wird fälschlich in Trash verschoben)
- [x] Preview/Execute/Report zeigen dieselben Decisions
- [x] Alle bestehenden Tests grün oder bewusst angepasst

### Phase 3: Family-based Recognition (3-4 Wochen)

**Ziel**: Plattformfamilien mit spezifischen Erkennungsstrategien.

**Umfang**:
1. `PlatformFamily` Routing in Enrichment-Pipeline
2. Familie-spezifische Hash-Strategien:
   - `NoIntroCartridge`: Headerless-Hash zuerst, dann Container-Hash
   - `RedumpDisc`: Track-SHA1 wo möglich, CHD-RAW-SHA1, Name-Fallback
   - `Arcade`: Set-Structure-Validation (Parent/Clone-Matching)
   - `FolderBased`: Folder-Hash (PS3_DISC.SFB + PARAM.SFO)
3. Familie aus `consoles.json` lesen
4. Family-spezifische Scoring-Anpassungen

**Betroffene Dateien**:
- `data/consoles.json` (family-Feld für alle Konsolen)
- `Infrastructure/Orchestration/EnrichmentPipelinePhase.cs` (Family-Router)
- `Core/Classification/ConsoleDetector.cs` (Family-Awareness)
- `Infrastructure/Hashing/FileHashService.cs` (Family-spezifische Hash-Strategie)

**Risiken**:
- Große Cross-Cutting-Änderung. Alle Pipelines betroffen.
- Mitigation: Feature-Flag `UseFamilyRouting` mit Legacy-Fallback.

**Abnahmekriterien**:
- [x] Disc-Images werden mit Redump-spezifischem Matching behandelt
- [x] Cartridge-ROMs werden headerless gehasht
- [x] Arcade-Sets werden strukturell validiert
- [x] Benchmark: Erkennungsrate steigt um ≥10% für Disc-Images

### Phase 4: Conservative Heuristic Layer & Sort Hardening (2 Wochen)

**Ziel**: Heuristik-Thresholds verschärfen, UNKNOWN als legitimes Ergebnis normalisieren.

**Umfang**:
1. Soft-Evidence Caps verschärfen:
   - FolderName: max Tier3, nie Sort
   - FilenameKeyword: max Tier3, nie Sort
   - AmbiguousExtension: max Tier3, nie Sort
2. Composite GameKey (`ConsoleKey||GameKey`) in DeduplicationEngine
3. Review- und Unknown-Routing mit Handlungsempfehlungen
4. GUI/CLI/API zeigen Tier + MatchKind + DecisionClass konsistent

**Betroffene Dateien**:
- `Core/Deduplication/DeduplicationEngine.cs` (Composite GameKey)
- `Core/Classification/HypothesisResolver.cs` (verschärfte Caps)
- `Infrastructure/Sorting/ConsoleSorter.cs` (Review/Unknown-Routing)
- `Infrastructure/Reporting/` (Evidence-Tier in Reports)
- `UI.Wpf/` (Tier-Anzeige)
- `CLI/` (Output-Spalten)
- `Api/` (API-Response-Felder)

**Risiken**:
- Mehr Dateien landen in Review/Unknown → User muss mehr manuell prüfen
- Mitigation: Klare Review-Empfehlungen in GUI

**Abnahmekriterien**:
- [x] Kein Soft-Only-Detection führt zu Auto-Sort
- [x] Cross-Platform-Dedup ist ausgeschlossen
- [x] GUI/CLI/API zeigen identische Decisions
- [x] False-Positive-Rate < 1%

### Phase 5: Benchmark, Ground Truth & Quality Gates (2-3 Wochen)

**Ziel**: Automatisierte Qualitätssicherung der Erkennung.

**Umfang**:
1. Ground-Truth-Dataset erweitern:
   - Pro Familie ein Test-Set
   - Bekannte False-Positive-Fälle als Regressionstests
   - Intentionally-Ambiguous-Fälle (UNKNOWN erwartet)
2. Quality Gates:
   - DAT-Match-Rate pro Console
   - False-Positive-Rate pro Familie
   - Tier-Verteilung
   - Review-Rate (soll sinken über Zeit, nicht steigen)
3. CI-Integration: Benchmark-Gates als Pipeline-Step

**Betroffene Dateien**:
- `benchmark/` (Ground-Truth-Erweiterung)
- `src/Romulus.Tests/Benchmark/` (neue Quality-Gate-Tests)
- `benchmark/gates.json` (neue Thresholds)

**Risiken**:
- Ground-Truth-Pflege ist aufwändig
- Mitigation: Automatische Ground-Truth-Ableitung aus DAT-Matches

**Abnahmekriterien**:
- [x] Benchmark-Suite mit ≥500 Testfällen
- [x] Quality Gates in CI enforced
- [x] Regression-Detection für False-Positives
- [x] Tier-Verteilungsreport pro Run

---

## 6. Test- und Benchmark-Strategie

### 6.1 Neue Tests (MUST)

#### Unit-Tests

| Testklasse | Prüft | Datei |
|-----------|-------|-------|
| `DecisionResolverTests` | Tier → DecisionClass Mapping für alle Kombinationen | `Tests/DecisionResolverTests.cs` |
| `EvidenceTierMappingTests` | DetectionSource → EvidenceTier korrekt | `Tests/EvidenceTierMappingTests.cs` |
| `CrossConsoleDatLookupTests` | DAT-Lookup über alle Konsolen findet korrekten Match | `Tests/CrossConsoleDatLookupTests.cs` |
| `CompositeGameKeyTests` | ConsoleKey+GameKey als Dedup-Schlüssel | `Tests/CompositeGameKeyTests.cs` |
| `PlatformFamilyRoutingTests` | Console → Family korrekt zugeordnet | `Tests/PlatformFamilyRoutingTests.cs` |
| `MatchKindTests` | Jeder MatchKind wird korrekt aus Evidenz abgeleitet | `Tests/MatchKindTests.cs` |

#### Integration-Tests

| Testklasse | Prüft |
|-----------|-------|
| `DatFirstPipelineTests` | Volle Pipeline: DAT-first → Structural → Heuristic |
| `FamilyBasedEnrichmentTests` | Family-spezifische Hash-Strategien |
| `ReviewRoutingIntegrationTests` | Review-Dateien korrekt geroutet |
| `UnknownHandlingIntegrationTests` | Unknown-Dateien nicht verschoben |

#### Regressions-Tests

| Testklasse | Prüft |
|-----------|-------|
| `FalsePositiveRegressionTests` | Bekannte False-Positive-Fälle bleiben korrekt |
| `CrossPlatformDedupRegressionTests` | PS1/PS2 gleichnamige Spiele nicht cross-deduped |
| `SoftOnlyNeverSortTests` | Soft-Only-Detection führt NIE zu Sort |
| `DatOverrideDetectionTests` | DAT-Match überschreibt falsche Detection |

### 6.2 Neue Benchmark-Gates

| Gate | Threshold | Metrik |
|------|----------|--------|
| `dat-match-rate` | ≥ 60% (bei geladenem DAT-Set) | Anteil Dateien mit Tier0_ExactDat |
| `false-positive-rate` | < 1% | Falsch erkannte Konsolen (laut Ground-Truth) |
| `unknown-rate` | < 30% | Dateien ohne jede Erkennung |
| `review-rate` | < 40% | Dateien in Review (soll sinken über Zeit) |
| `cross-platform-dedup` | = 0 | Deduplications über Plattformgrenzen |
| `sort-without-evidence` | = 0 | Auto-Sort ohne mindestens Tier1-Evidenz |

### 6.3 Invarianten (MUST in jedem Test-Run)

```
INV-1: DatVerified → Tier0_ExactDat (immer)
INV-2: Sort → Tier0 OR (Tier1 AND !Conflict AND Confidence ≥ 85)
INV-3: Tier3 oder Tier4 → NIEMALS Sort oder DatVerified
INV-4: ConsoleKey == "UNKNOWN" → DecisionClass == Unknown oder Blocked
INV-5: Composite GameKey = ConsoleKey + "||" + GameKey (keine Cross-Platform-Gruppen)
INV-6: Preview Decision == Execute Decision (für identische Inputs)
INV-7: GUI Decision == CLI Decision == API Decision (für identische Inputs)
INV-8: Keine Datei gleichzeitig Winner UND Loser (SEC-DEDUP)
```

---

## 7. Top 15 Konkrete Maßnahmen

| # | Maßnahme | Phase | Erwarteter Nutzen |
|---|----------|-------|-------------------|
| **1** | **Cross-Console DAT-Lookup immer (nicht nur bei UNKNOWN)** | 1 | Eliminiert ~80% der DAT-Misses die durch falsche Detection entstehen |
| **2** | **EvidenceTier + MatchKind Models einführen** | 1 | Präzise Evidenz-Klassifikation statt flacher Confidence |
| **3** | **DecisionResolver: Tier-basiertes Gating** | 2 | Tier3/Tier4 können nie zu Sort führen → eliminiert False Positives |
| **4** | **Composite GameKey (ConsoleKey + GameKey)** | 4 | Verhindert Cross-Platform-Dedup komplett |
| **5** | **SortDecision.Unknown einführen** | 1 | Klare Unterscheidung "wir wissen es nicht" vs "Konflikt" |
| **6** | **consoles.json: family-Feld** | 1 | Foundation für Family-basiertes Routing |
| **7** | **Soft-Evidence Caps verschärfen (Folder/Keyword nie Sort)** | 4 | Eliminiert aggressive Heuristik-Sortierungen |
| **8** | **DAT-first Pipeline-Reihenfolge** | 2 | DAT als Primärquelle statt Nachkorrektur |
| **9** | **Family-spezifische Hash-Strategien** | 3 | Redump-Track-SHA1, No-Intro-Headerless, Arcade-Set-Structure |
| **10** | **False-Positive-Regressionstests** | 2 | Automatische Absicherung gegen bekannte Fehlerkategorien |
| **11** | **Benchmark Quality Gates** | 5 | CI-enforced Erkennungsqualität |
| **12** | **Review-Routing mit Handlungsempfehlung** | 4 | User weiß was zu tun ist statt nur "Review" |
| **13** | **Tier-Verteilung in Reports/KPIs** | 4 | Sichtbarkeit der Erkennungsqualität |
| **14** | **Ground-Truth-Dataset pro Familie** | 5 | Regression-Detection für Erkennungslogik |
| **15** | **MatchLevel → EvidenceTier+MatchKind Migration** | 2 | Eliminiert ungenaue MatchLevel-Abstufung |

---

## 8. Risiken und offene Punkte

### 8.1 Was schwierig wird

| Risiko | Impact | Mitigation |
|--------|--------|-----------|
| **Cross-Console-Lookup Performance** | Bei 50+ geladenen DATs und 100k+ Dateien könnte Lookup langsamer werden | Hash-Index ist O(1), actual lookup scales mit Anzahl DATs. Benchmark Gate. |
| **Review-Explosion** | Strikte Tier-Gates verschieben viele Dateien von Sort → Review | Feature-Flag für graduelles Verschärfen. User-Settings für Tier-Thresholds. |
| **Backward-Compat** | `SortDecision.Unknown` bricht switch-Statements. Neue Fields auf `RomCandidate` | Compiler-Warnings als Gate. Default-Werte. |
| **DAT-Coverage** | Ohne passende DATs bleibt Tier0 leer | DAT-Download-Automatisierung (bereits vorhanden via DatSourceService) |
| **Disc-Image-Hashing** | Redump-Track-SHA1 ≠ Container-SHA1. CHD-RAW-SHA1 ist Approximation | Name-Fallback bleibt als Tier2, nie als Tier0 |
| **Arcade-Set-Validation** | MAME-Sets sind komplex (Parent/Clone/BIOS/Device). Volle Validierung ist MAME-Versionabhängig | Phase 3 Arcade-Support minimal: ZIP-Inner-Hash + DAT-Match. Volle Set-Validation als spätere Erweiterung. |

### 8.2 Was bewusst konservativ bleiben sollte

1. **Disc-Images ohne Hash-Match**: Name-only-Match bleibt Review, nicht Sort. Track-SHA1-Berechnung ist komplex und fehleranfällig.
2. **CHD-Container-SHA1**: CHD-Header-SHA1 ist manipulierbar. Nur als Tier2 werten, nicht als Tier0.
3. **TOSEC/Computer**: Naming-Konventionen sind inkonsistent. Immer Review oder Blocked.
4. **Arcade-Sets**: Set-Vollständigkeit ist MAME-Versionsabhängig. Review statt Sort, außer bei exaktem DAT-Match.
5. **Multi-Region-Dateien**: Region-Erkennung bleibt konservativ. Lieber WORLD als falsches US/EU/JP.

### 8.3 Fälle die besser Review/Unknown bleiben sollten

| Fall | Warum Review/Unknown statt Sort |
|------|-------------------------------|
| `.bin` ohne Header-Match und ohne DAT | Ambiguous Extension + keine Strukturevidenz |
| Ordner heißt "PS1" aber Dateien haben keine PS1-Header | Ordnername ist nicht vertrauenswürdig |
| CHD-Datei, DAT-Name-Match aber kein Hash-Match | Track-SHA1-Diskrepanz ist häufig bei Redump |
| ZIP mit gemischten Extensions innen | Könnte Compilation / Mixed-Set sein |
| Dateiname enthält "[MAME]" aber ist kein MAME-ZIP | Keyword-Tags sind fälschbar |
| Disc-Image in falschem Ordner | Ordner-Detection widerspricht Disc-Header |
| Homebrew / Hack / Unlicensed | Oft nicht in DATs enthalten, Erkennung unsicher |
| BIOS-Datei ohne DAT-Match | BIOS-Katalog ist unvollständig |

### 8.4 Architektur-Entscheidungen (ADR-Bedarf)

Folgende Entscheidungen sollten als ADR dokumentiert werden:

1. **ADR: DAT-first vs Detection-first Pipeline** — Warum DAT als Primärquelle
2. **ADR: Evidence Tier System** — Warum 5 Tiers statt flache Confidence
3. **ADR: Composite GameKey** — Warum ConsoleKey+GameKey statt nur GameKey
4. **ADR: Family-based Recognition** — Warum plattformfamilien-spezifische Strategien
5. **ADR: Conservative Heuristic Policy** — Warum Tier3 nie zu Sort führen darf

---

## Appendix A: Mapping Ist → Soll

```
IST:                                    SOLL:
                                        
DetectionSource.DatHash        (100) →  EvidenceTier.Tier0_ExactDat
  + MatchKind.ExactDatHash             
                                        
DetectionSource.UniqueExt      (95)  →  EvidenceTier.Tier2_StrongHeuristic
  + MatchKind.UniqueExtensionMatch     (Downgrade: nicht binary-verifiziert)
                                        
DetectionSource.DiscHeader     (92)  →  EvidenceTier.Tier1_Structural
  + MatchKind.DiscHeaderSignature      
                                        
DetectionSource.CartridgeHeader(90)  →  EvidenceTier.Tier1_Structural
  + MatchKind.CartridgeHeaderMagic     
                                        
DetectionSource.SerialNumber   (88)  →  EvidenceTier.Tier1_Structural
  + MatchKind.SerialNumberMatch        
                                        
DetectionSource.FolderName     (85)  →  EvidenceTier.Tier3_WeakHeuristic
  + MatchKind.FolderNameMatch          (Downgrade: nicht vertrauenswürdig)
                                        
DetectionSource.ArchiveContent (80)  →  EvidenceTier.Tier2_StrongHeuristic
  + MatchKind.ArchiveContentExtension  
                                        
DetectionSource.FilenameKeyword(75)  →  EvidenceTier.Tier3_WeakHeuristic
  + MatchKind.FilenameKeywordMatch     (Downgrade: fälschbar)
                                        
DetectionSource.AmbiguousExt   (40)  →  EvidenceTier.Tier3_WeakHeuristic
  + MatchKind.AmbiguousExtensionSingle 
```

## Appendix B: Entscheidungsmatrix

```
Tier0 + !Conflict        → DatVerified  (Auto-Sort)
Tier0 + Conflict          → Review       (DAT in 2+ Konsolen)
Tier1 + !Conflict + ≥85   → Sort         (Auto-Sort)
Tier1 + Conflict           → Review
Tier1 + <85                → Review
Tier2 + any                → Review       (Nie Sort)
Tier3 + any                → Blocked      (Nie Sort, nie Review)
Tier4                      → Unknown      (Keine Evidenz)
```

## Appendix C: Pipeline-Vergleich Alt vs Neu

```
ALT:                                NEU:
                                    
1. GameKey                          1. GameKey
2. FileClassifier                   2. FileClassifier
3. ConsoleDetector (Heuristik)      3. Hash berechnen (alle Strategien)
4. Scoring                          4. Cross-Console DAT-Lookup (Tier0)
5. DAT-Lookup (Nachkorrektur)       5. Structural Detection (Tier1)
6. ApplyDatAuthority                6. Heuristic Detection (Tier2/3)
7. → RomCandidate                   7. DecisionResolver (Tier→Decision)
                                    8. Scoring
                                    9. → RomCandidate
                                    
Reihenfolge:                        Reihenfolge:
Heuristik → DAT-Korrektur          DAT → Structural → Heuristik
                                    
False Positives:                    False Positives:
Hoch (Heuristik kann Sort)         Niedrig (nur Tier0/1 können Sort)
                                    
DAT-Misses:                         DAT-Misses:
Hoch (nur im erkannten DAT)        Niedrig (Cross-Console-Lookup)
```
