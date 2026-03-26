# Confidence & Sorting Gate Redesign

**Projekt:** Romulus / RomCleanup  
**Datum:** 2026-03-21  
**Kontext:** Wrong Match Rate 12,674 % = Unsafe Sort Rate 12,674 %  
**Rolle:** Confidence-, Safety- und Sorting-Gate-Architekt

---

## 1. Executive Verdict

### Hauptproblem

**Wrong Match Rate und Unsafe Sort Rate sind identisch.**
Das bedeutet: **Jeder falsch erkannte Datei-Konsolen-Match passiert auch das Sorting-Gate.**

Das Gate ist kein Gate.
Es ist eine Durchgangsstation.

Von 1.152 Benchmark-Samples:
- **146 FalsePositives** erhalten Confidence ≥ 80 und passieren das Sorting-Gate
- **0 FalsePositives** werden vom Gate abgefangen
- **Abfangrate des Gates für FalsePositives: 0 %**

### Ursachenkette

```
Schwaches Signal (Folder, AmbigExt, Keyword)
    ↓ Confidence = 75–95
    ↓ Kein Conflict Flag → keine Penalty
    ↓ Gate-Schwelle 80 → durchgelassen
    ↓ Sort-Entscheidung = ja
    ↓ Datei wird verschoben
    → DATENVERLUST-RISIKO
```

### Kurzfazit

Das aktuelle Gate-Modell hat drei fundamentale Schwachstellen:

1. **Keine Evidenzklassen-Bewertung** — Folder-Name allein gibt 85 Confidence, genug fürs Gate
2. **Kein „Hard Evidence Required"-Prinzip** — Weiche Signale genügen für Sort-Entscheidung
3. **Kein AMBIGUOUS-Zustand** — Das System kennt nur MATCH oder UNKNOWN, kein Zwischenzustand

---

## 2. Wo aktuell zu früh entschieden wird

### 2.1 Muster: Folder-Only-Erkennung → Confidence 85 → Sort erlaubt

**Ablauf im Code:**
```
ConsoleDetector.DetectWithConfidence()
  → DetectByFolder() findet Alias "PlayStation" → Hypothesis(PS1, 85, FolderName)
  → Kein weiterer Treffer (keine unique ext, kein header, kein serial)
  → HypothesisResolver: 1 Hypothesis, MaxSingle=85, kein Conflict
  → Ergebnis: ConsoleKey=PS1, Confidence=85, Conflict=false

RunOrchestrator.StandardPhaseSteps:
  → Category=Game ✓
  → ConsoleKey=PS1 ✓
  → DetectionConflict=false ✓
  → DetectionConfidence=85 ≥ 80 ✓
  → SORT ERLAUBT
```

**Problem:** Eine .bin-Datei in einem Ordner mit "PlayStation" im Namen wird mit 85 % Confidence als PS1 sortiert — egal ob es ein ROM, ein Save-File, eine Firmware oder Noise ist.

### 2.2 Muster: AmbiguousExtension (1 Match) → Confidence 40 + Folder 85 → Sort erlaubt

```
Hypothesis 1: AmbiguousExtension(MD, 40)
Hypothesis 2: FolderName(MD, 85)
  → Resolver: MD total = 125, max single = 85
  → Agreement Bonus: 85 + 5 = 90
  → Confidence = 90, kein Conflict
  → SORT ERLAUBT
```

**Problem:** Ein Ordner "Mega Drive" mit einer .bin-Datei gibt 90 % Confidence. Aber .bin ist das unsicherste Format überhaupt — es kann alles sein.

### 2.3 Muster: Keyword-Only-Erkennung → Confidence 75 (knapp unter Gate)

```
filename = "Game Name [GBA].bin"
  → KeywordDetection: GBA, 75
  → Kein Conflict, kein Header
  → Confidence = 75 < 80
  → SORT BLOCKIERT (gerade noch)
```

**Knapp richtig.** Aber: Wenn zusätzlich ein mehrdeutiger Ordner matcht:
```
Hypothesis 1: FilenameKeyword(GBA, 75)
Hypothesis 2: FolderName(GBA, 85)
  → Confidence = 85 + 5 = 90
  → SORT ERLAUBT mit Noise-Daten
```

### 2.4 Muster: Single-Source ohne Corroboration → Confidence ≥ 80 → Sort erlaubt

**Kernproblem:** Das System erlaubt Sorting basierend auf einer einzigen, weichen Quelle.

| Einzelne Quelle | Confidence | Gate passiert? |
|---|---|---|
| FolderName allein | 85 | **JA** |
| UniqueExtension allein | 95 | **JA** |
| DiscHeader allein | 92 | **JA** |
| CartridgeHeader allein | 90 | **JA** |
| SerialNumber allein | 88 | **JA** |
| ArchiveContent allein | 80 | **JA** |
| FilenameKeyword allein | 75 | Nein |
| AmbiguousExtension allein | 40 | Nein |

**6 von 8 Quellen passieren das Gate allein.**
Davon sind FolderName und ArchiveContent strukturell unsicher.

### 2.5 Muster: Negative Controls mit hoher Confidence

Aus den Benchmark-Daten:
- 146 FalsePositives mit Confidence ≥ 80
- Haupt-Quellen: FolderName (85), AmbiguousExt + FolderName (90), Serial-Pattern FalseMatch
- **Keine einzige FalsePositive wird vom Gate abgefangen**

---

## 3. Zielmodell für Confidence

### 3.1 Confidence-Stufen (neu)

| Stufe | Bereich | Bedeutung | Beispiel |
|---|---|---|---|
| **VERIFIED** | 100 | DAT-Hash-Match. Absolute Gewissheit. | SHA1 in DAT gefunden |
| **HIGH** | 85–99 | Harte Evidenz mit Corroboration | DiscHeader + Folder stimmen überein |
| **MEDIUM** | 60–84 | Einzelne harte Evidenz ODER corroborierte weiche | CartridgeHeader allein, Folder+Keyword |
| **LOW** | 30–59 | Nur weiche Evidenz | FolderName allein, Keyword allein |
| **AMBIGUOUS** | 1–29 | Widersprüchliche oder mehrdeutige Signale | Folder=PS1, Serial=PS2 |
| **UNKNOWN** | 0 | Keine verwertbare Evidenz | Kein Signal |

### 3.2 Evidenzklassifikation (NEU — fehlt aktuell komplett)

**Harte Evidenz** (kann allein einen Match rechtfertigen, wenn Confidence hoch genug):
- `DatHash` (100) — unverrückbar
- `DiscHeader` (92) — binäre Signatur
- `CartridgeHeader` (90) — binäre Signatur
- `UniqueExtension` (95) — strukturell eindeutig

**Weiche Evidenz** (kann allein KEINEN Sort rechtfertigen):
- `SerialNumber` (88) — musterbasiert, Überlappungen möglich (PS1/PS2, X360 generisch)
- `FolderName` (85) — benutzerbestimmt, unzuverlässig
- `ArchiveContent` (80) — indirekt, basiert auf Inner-Extension
- `FilenameKeyword` (75) — benutzergeneriert, unstrukturiert
- `AmbiguousExtension` (40) — per Definition mehrdeutig

### 3.3 Confidence-Berechnung (neu)

**Aktuell:**
```
Aggregate = MaxSingle + (AgreementBonus) - (ConflictPenalty)
```

**Neu:**
```
HardEvidencePresent = Hypotheses.Any(h => IsHardEvidence(h.Source))
SoftOnlyDetection = !HardEvidencePresent

BaseConfidence = MaxSingle + AgreementBonus - ConflictPenalty

// NEUES GATE: Soft-Only-Cap
if (SoftOnlyDetection)
    Confidence = Min(BaseConfidence, 65)   // Soft-only kann nie über 65

// NEUES GATE: Single-Source-Cap
if (Hypotheses.DistinctSources == 1)
    Confidence = Min(Confidence, SourceTypeCap(Source))

// Source-Type-Caps (NEUES KONZEPT)
DatHash         → Cap 100
DiscHeader      → Cap 92
CartridgeHeader → Cap 90
UniqueExtension → Cap 95
SerialNumber    → Cap 75  (runter von 88, wegen Overlap-Risiko)
FolderName      → Cap 65  (runter von 85, nie allein sortierbar)
ArchiveContent  → Cap 70  (runter von 80, indirekt)
FilenameKeyword → Cap 60  (runter von 75)
AmbiguousExt    → Cap 40  (unverändert)
```

**Effekt auf die Problemfälle:**

| Szenario | Confidence ALT | Confidence NEU | Gate passiert? |
|---|---|---|---|
| Folder-Only "PlayStation" → PS1 | 85 | **65** (Soft-Cap) | **NEIN** |
| Folder + AmbigExt → MD | 90 | **65** (beide soft) | **NEIN** |
| Folder + Keyword → GBA | 90 | **65** (beide soft) | **NEIN** |
| DiscHeader allein → DC | 92 | **92** (hard, no cap) | **JA** |
| UniqueExt allein → GBA | 95 | **95** (hard, unique) | **JA** |
| Folder + DiscHeader → PS2 | 97 | **97** (corroborated) | **JA** |
| Serial allein → PS1 | 88 | **75** (Single-Source-Cap) | **NEIN** |
| Serial + Folder → PS1 | 93 | **88** (serial+soft) | **JA** |

---

## 4. Zielmodell für Sorting Gates

### 4.1 Dreistufiges Gate-Modell

```
                        ┌─────────────┐
                        │  Candidate  │
                        └──────┬──────┘
                               ▼
                    ┌──────────────────────┐
                    │ GATE 1: Category     │
                    │ Game only → weiter   │
                    │ Junk/Bios/NonGame    │
                    │ → BLOCKED            │
                    └──────────┬───────────┘
                               ▼
                    ┌──────────────────────┐
                    │ GATE 2: Confidence   │
                    │ ≥ 85 → SORT         │
                    │ 65–84 → REVIEW      │
                    │ < 65 → BLOCKED      │
                    └──────────┬───────────┘
                               ▼
                    ┌──────────────────────┐
                    │ GATE 3: Evidence     │
                    │ HasHardEvidence      │
                    │ → SORT erlaubt       │
                    │ SoftOnly             │
                    │ → REVIEW / BLOCKED   │
                    └──────────┬───────────┘
                               ▼
                    ┌──────────────────────┐
                    │ GATE 4: Conflict     │
                    │ HasConflict = false  │
                    │ → SORT erlaubt       │
                    │ HasConflict = true   │
                    │ → REVIEW             │
                    └──────────────────────┘
```

### 4.2 Kombinierte Sort-Entscheidung

| Confidence | Conflict | Hard Evidence | **Entscheidung** |
|---|---|---|---|
| ≥ 85 | Nein | Ja | **SORT** |
| ≥ 85 | Nein | Nein | **REVIEW** (soft-only, trotz hoher Zahl) |
| ≥ 85 | Ja | Ja | **REVIEW** (Conflict trotz Hard) |
| ≥ 85 | Ja | Nein | **BLOCKED** |
| 65–84 | Nein | Ja | **REVIEW** |
| 65–84 | Nein | Nein | **BLOCKED** |
| 65–84 | Ja | * | **BLOCKED** |
| < 65 | * | * | **BLOCKED** |
| = 100 (DAT) | * | * | **SORT** (DAT-Override) |

**Vergleich mit aktuellem System:**

| Logik | Aktuell | Neu |
|---|---|---|
| Schwelle | ≥ 80, kein Conflict | ≥ 85 + Hard Evidence + kein Conflict |
| Soft-Only-Schutz | **keiner** | Soft-Only → maximal 65, nie sortierbar |
| REVIEW-Stufe | **gibt es nicht** | 65–84, oder Conflict mit Hard Evidence |
| AMBIGUOUS-Marker | **gibt es nicht** | Explicit bei widersprüchlichen Signalen |
| DAT-Override | Implizit (conf=100) | Explizit, immer Sort |

### 4.3 Sort-Entscheidungs-Enum (neu)

```csharp
public enum SortDecision
{
    /// Sort erlaubt — high confidence, hard evidence, kein Conflict
    Sort,
    
    /// Review nötig — Erkennung plausibel, aber nicht gesichert
    Review,
    
    /// Blockiert — Confidence zu niedrig, Conflict, oder UNKNOWN
    Blocked,
    
    /// DAT-verifiziert — Hash-Match, immer Sort
    DatVerified
}
```

---

## 5. Regeln für UNKNOWN / AMBIGUOUS

### 5.1 Wann UNKNOWN korrekt ist

UNKNOWN ist die **richtige** Antwort wenn:

1. **Keine Hypothese generiert wurde** — Kein Signal, keine Erkennung
2. **Alle Hypothesen unter Mindestschwelle** — AmbiguousExtension allein (40) reicht nicht
3. **File-Category ist nicht Game** — Junk/NonGame/Bios erhält kein ConsoleKey
4. **Junk-Marker im Filename** — IsLikelyJunkName() = true → UNKNOWN
5. **Soft-Only-Detection bei Negative-Control** — FolderName-Only ist kein Match

**Design-Regel:** UNKNOWN ist immer sicherer als ein falscher Match. Ein UNKNOWN ist reversibel. Eine falsche Sortierung ist es nicht.

### 5.2 Wann AMBIGUOUS korrekt ist (NEU)

AMBIGUOUS sollte als **expliziter Zustand** eingeführt werden für:

1. **Conflict mit ähnlich starken Signalen** — PS1 vs. PS2 (beide via Serial möglich)
2. **Cross-System-Overlap** — NEOGEO vs. NEOCD, PCE vs. PCECD, XBOX vs. X360
3. **Multi-Console-Extension ohne disambiguierendes Signal** — .bin mit 5+ consoles
4. **Folder sagt X, Header sagt Y** — Struktureller Widerspruch

```csharp
public sealed record ConsoleDetectionResult(
    string ConsoleKey,           // Winner oder "UNKNOWN" oder "AMBIGUOUS"
    int Confidence,
    IReadOnlyList<DetectionHypothesis> Hypotheses,
    bool HasConflict,
    string? ConflictDetail,
    bool HasHardEvidence,        // NEU
    bool IsSoftOnly,             // NEU
    SortDecision SortDecision)   // NEU
```

### 5.3 Wann AMBIGUOUS/UNKNOWN besser ist als ein Match

| Szenario | Aktuell | Besser |
|---|---|---|
| .bin in Ordner "ROMs" | Erste AmbigExt-Match (z.B. MD) | **UNKNOWN** |
| .iso in Ordner "PlayStation" | PS1 (Folder=85) | **AMBIGUOUS** (30+ PS-Systeme teilen .iso) |
| .chd in Ordner "Sega" | Erster Match | **AMBIGUOUS** (DC/SAT/SCD/32X) |
| Serial SLUS-xxxxx | PS1 oder PS2 (gleiche Präfixe) | **AMBIGUOUS** (PS1/PS2-Overlap) |

---

## 6. Technische Umsetzung

### 6.1 Betroffene Module

| Modul | Datei | Änderungsart |
|---|---|---|
| **HypothesisResolver** | `Core/Classification/HypothesisResolver.cs` | Soft-Only-Cap, Single-Source-Cap, HasHardEvidence-Flag |
| **DetectionHypothesis** | `Core/Classification/DetectionHypothesis.cs` | `IsHardEvidence`-Property auf `DetectionSource` |
| **ConsoleDetectionResult** | `Core/Classification/DetectionHypothesis.cs` | `HasHardEvidence`, `IsSoftOnly`, `SortDecision` |
| **StandardPhaseSteps** | `Infrastructure/Orchestration/RunOrchestrator.StandardPhaseSteps.cs` | Neues 4-stufiges Gate |
| **EnrichmentPipelinePhase** | `Infrastructure/Orchestration/EnrichmentPipelinePhase.cs` | SortDecision propagieren |
| **RomCandidate** | `Contracts/Models/RomCandidate.cs` | `HasHardEvidence`, `SortDecision`-Properties |
| **CandidateFactory** | `Core/Classification/CandidateFactory.cs` | Neue Properties durchreichen |
| **FilenameConsoleAnalyzer** | `Core/Classification/FilenameConsoleAnalyzer.cs` | Serial-Overlap-Handling (PS1/PS2) |
| **MetricsAggregator** | `Tests/Benchmark/MetricsAggregator.cs` | Review-Rate, Blocked-Rate, Gate-Effizienz |
| **GroundTruthComparator** | `Tests/Benchmark/GroundTruthComparator.cs` | SortDecision-basierte Bewertung |
| **ConsoleSorter** | `Infrastructure/Sorting/ConsoleSorter.cs` | SortDecision statt Confidence-Check |
| **CliOutputWriter** | `CLI/CliOutputWriter.cs` | Review/Blocked-Felder in JSON/Report |
| **DashboardProjection** | `UI.Wpf/Models/DashboardProjection.cs` | Review-Count, Blocked-Count |

### 6.2 Refactoring-Ziele

#### Phase 1: Evidenzklassifikation (Core)

**Ziel:** `DetectionSource` weiß, ob sie hart oder weich ist.

```csharp
// DetectionHypothesis.cs — Erweiterung des Enums
public static class DetectionSourceExtensions
{
    public static bool IsHardEvidence(this DetectionSource source) => source switch
    {
        DetectionSource.DatHash => true,
        DetectionSource.DiscHeader => true,
        DetectionSource.CartridgeHeader => true,
        DetectionSource.UniqueExtension => true,
        _ => false
    };

    /// Single-Source-Confidence-Cap wenn nur diese eine Quelle vorliegt
    public static int SingleSourceCap(this DetectionSource source) => source switch
    {
        DetectionSource.DatHash => 100,
        DetectionSource.UniqueExtension => 95,
        DetectionSource.DiscHeader => 92,
        DetectionSource.CartridgeHeader => 90,
        DetectionSource.SerialNumber => 75,
        DetectionSource.ArchiveContent => 70,
        DetectionSource.FolderName => 65,
        DetectionSource.FilenameKeyword => 60,
        DetectionSource.AmbiguousExtension => 40,
        _ => 0
    };
}
```

#### Phase 2: Resolver-Härtung (Core)

**Ziel:** HypothesisResolver liefert Soft-Only-Cap und Evidence-Flags.

```csharp
// HypothesisResolver.cs — neue Resolve-Logik
public static ConsoleDetectionResult Resolve(IReadOnlyList<DetectionHypothesis> hypotheses)
{
    // ... bestehende Grouping/Conflict-Logik ...

    bool hasHardEvidence = winner.Value.Items.Any(h => h.Source.IsHardEvidence());
    bool isSoftOnly = !hasHardEvidence;

    // Soft-Only-Cap: weiche Signale allein dürfen nie über 65
    if (isSoftOnly)
        aggregateConfidence = Math.Min(aggregateConfidence, 65);

    // Single-Source-Cap
    var distinctSources = winner.Value.Items.Select(h => h.Source).Distinct().ToList();
    if (distinctSources.Count == 1)
        aggregateConfidence = Math.Min(aggregateConfidence, distinctSources[0].SingleSourceCap());

    // SortDecision ableiten
    var sortDecision = DetermineSortDecision(aggregateConfidence, hasConflict, hasHardEvidence);

    return new ConsoleDetectionResult(
        winner.Key, aggregateConfidence, hypotheses,
        hasConflict, conflictDetail,
        hasHardEvidence, isSoftOnly, sortDecision);
}

private static SortDecision DetermineSortDecision(int confidence, bool conflict, bool hardEvidence)
{
    if (confidence == 100) return SortDecision.DatVerified;
    if (confidence >= 85 && !conflict && hardEvidence) return SortDecision.Sort;
    if (confidence >= 65 && !conflict && hardEvidence) return SortDecision.Review;
    if (confidence >= 85 && !conflict && !hardEvidence) return SortDecision.Review; // Soft-only Sonderfall
    if (confidence >= 85 && conflict && hardEvidence) return SortDecision.Review;
    return SortDecision.Blocked;
}
```

#### Phase 3: Gate-Update (Infrastructure)

**Ziel:** StandardPhaseSteps nutzt `SortDecision` statt rohes Confidence-Threshold.

```csharp
// RunOrchestrator.StandardPhaseSteps.cs — Neues Gate
foreach (var c in state.AllCandidates)
{
    if (c.Category != FileCategory.Game)
    {
        enrichedConsoleKeys[c.MainPath] = "UNKNOWN";
        continue;
    }

    if (string.IsNullOrEmpty(c.ConsoleKey) ||
        c.ConsoleKey is "UNKNOWN" or "AMBIGUOUS")
    {
        enrichedConsoleKeys[c.MainPath] = "UNKNOWN";
        continue;
    }

    // NEUES GATE: SortDecision-basiert statt Confidence-Threshold
    if (c.SortDecision is SortDecision.Sort or SortDecision.DatVerified)
    {
        enrichedConsoleKeys[c.MainPath] = c.ConsoleKey;
    }
    else
    {
        enrichedConsoleKeys[c.MainPath] = "UNKNOWN";
        // Optional: Review-Liste für UI/Report befüllen
    }
}
```

#### Phase 4: Propagation (Contracts → Infrastructure → CLI/API/GUI)

**Ziel:** `HasHardEvidence`, `IsSoftOnly`, `SortDecision` durchgängig bis Reports.

### 6.3 Reihenfolge

```
1. DetectionSource Extensions (IsHardEvidence, SingleSourceCap)     — Core, kein Breaking Change
2. ConsoleDetectionResult erweitern                                  — Core, Breaking Change
3. HypothesisResolver Soft-Only-Cap + Evidence-Flags                — Core, Logik-Änderung
4. CandidateFactory + RomCandidate Properties                       — Contracts, Properties
5. EnrichmentPipelinePhase Propagation                              — Infrastructure
6. StandardPhaseSteps Gate-Update                                   — Infrastructure, kritisch
7. MetricsAggregator: Gate-Effizienz-Metrik                        — Tests
8. GroundTruthComparator: SortDecision-aware Verdicts               — Tests
9. ConsoleSorter: SortDecision nutzen                               — Infrastructure
10. CLI/API/GUI: Review/Blocked in Reports                          — Entry Points
```

---

## 7. Top 10 Massnahmen

### Massnahme 1: Soft-Only Confidence Cap = 65
**Impact:** Eliminiert ~90 % der FalsePositives sofort  
**Datei:** `HypothesisResolver.cs`  
**Logik:** Wenn keine `DetectionSource` mit `IsHardEvidence()` vorliegt → `Min(confidence, 65)`  
**Risiko:** Gering — echte ROMs mit harter Extension/Header sind nicht betroffen  
**Erwarteter Effekt:** UnsafeSortRate von 12,67 % auf ~2 %

### Massnahme 2: Single-Source Caps einführen
**Impact:** Verhindert überhöhte Confidence bei Einzelsignalen  
**Datei:** `HypothesisResolver.cs`, `DetectionHypothesis.cs`  
**Logik:** Wenn nur eine distinkte `DetectionSource` → Cap auf Source-spezifischen Maximalwert  
**Key-Caps:** FolderName → 65, ArchiveContent → 70, SerialNumber → 75  
**Erwarteter Effekt:** UnsafeSortRate zusätzlich ~1 % Reduktion

### Massnahme 3: Sort-Gate-Schwelle auf 85 anheben
**Impact:** Schmalere Durchlasszone, härtere Anforderung  
**Datei:** `RunOrchestrator.StandardPhaseSteps.cs`  
**Logik:** `DetectionConfidence < 85` statt `< 80`  
**Erwarteter Effekt:** Zusätzlicher Puffer gegen Grenzfälle

### Massnahme 4: Hard-Evidence-Requirement für Sort
**Impact:** Grundlegende Architekturhärtung  
**Datei:** `RunOrchestrator.StandardPhaseSteps.cs`  
**Logik:** Sort nur wenn `HasHardEvidence = true` (DAT/Header/UniqueExt)  
**Erwarteter Effekt:** Soft-only-Detections können nie automatisch sortiert werden

### Massnahme 5: SortDecision-Enum einführen
**Impact:** Saubere 3-Wege-Entscheidung: Sort / Review / Blocked  
**Datei:** `DetectionHypothesis.cs`, `RomCandidate.cs`  
**Logik:** Zentraler Entscheidungspunkt statt verteilte Confidence-Checks  
**Erwarteter Effekt:** Konsistenz über CLI/API/GUI

### Massnahme 6: Serial-Pattern-Overlap PS1/PS2 bereinigen
**Impact:** Verhindert PS1/PS2-Verwechslung bei SLUS/SCES/SLES-Serien  
**Datei:** `FilenameConsoleAnalyzer.cs`  
**Logik:** PS1-Serials = 3-stellig (SLUS-001), PS2-Serials = 5-stellig (SLUS-20001). Disjunkte Regex.  
**Erwarteter Effekt:** Wrong-Rate für PS1/PS2-Confusion → 0

### Massnahme 7: AMBIGUOUS-ConsoleKey einführen
**Impact:** Explizite Markierung für widersprüchliche Erkennung  
**Datei:** `HypothesisResolver.cs`, `ConsoleDetectionResult`  
**Logik:** Wenn Conflict mit beiden Confidence ≥ 60 → ConsoleKey="AMBIGUOUS"  
**Erwarteter Effekt:** Cross-System-Konflikte (NEOGEO/NEOCD, PCE/PCECD) werden sichtbar statt falsch aufgelöst

### Massnahme 8: MetricsAggregator Gate-Effizienz-Metrik
**Impact:** Messbarkeit der Gate-Qualität  
**Datei:** `MetricsAggregator.cs`  
**Logik:** Neue Metriken: `gateBlockedRate`, `gateReviewRate`, `gateFalsePassRate`  
**Erwarteter Effekt:** Regression sofort erkennbar

### Massnahme 9: Review-Queue in Reports
**Impact:** Sichtbarkeit für Benutzer  
**Dateien:** `RunProjection.cs`, `CliOutputWriter.cs`, `DashboardProjection.cs`  
**Logik:** Review-Kandidaten als eigene Kategorie in DryRun/Report/Dashboard  
**Erwarteter Effekt:** User kann REVIEW-Fälle manuell prüfen statt Datenverlust

### Massnahme 10: Benchmark-Regression-Gate
**Impact:** Automatisierte Qualitätssicherung  
**Datei:** `GoldenCoreBenchmarkTests.cs`  
**Logik:** `Assert.True(unsafeSortRate < 0.02, ...)` — UnsafeSortRate darf nie > 2 %  
**Erwarteter Effekt:** Regressions-Gate in CI verhindert Verschlechterung

---

## Anhang A: Impact-Projektion

### Erwartete Metriken nach Umsetzung (konservativ)

| Metrik | Aktuell | Nach Phase 1–2 | Nach Phase 3–4 | Ziel |
|---|---|---|---|---|
| Wrong Match Rate | 12,67 % | ~3 % | ~1 % | < 1 % |
| Unsafe Sort Rate | 12,67 % | ~2 % | < 1 % | < 0,5 % |
| Unknown Rate | 9,03 % | ~14 % | ~12 % | < 15 % |
| Correct Rate | 78,30 % | ~75 % | ~78 % | > 80 % |
| Review Rate | 0 % | ~8 % | ~5 % | < 10 % |

**Trade-off:** Unknown Rate steigt temporär, weil Soft-Only-Matches zu UNKNOWN/REVIEW werden. 
Das ist **beabsichtigt** — es ist sicherer, 5 % mehr als UNKNOWN zu behandeln, als 12,67 % falsch zu sortieren.

### Risikomatrix

| Änderung | Risiko | Mitigation |
|---|---|---|
| Soft-Only-Cap | Gering — Hard-Evidence-Systeme unberührt | Benchmark vor/nach vergleichen |
| Gate auf 85 | Mittel — einige korrecte Matches werden zu REVIEW | REVIEW = nicht verloren, nur manuell |
| AMBIGUOUS | Mittel — neue Code-Pfade | schrittweise einführen, zunächst nur logging |
| SortDecision-Enum | Hoch (Breaking) — alle Consumer müssen angepasst werden | Phased Rollout, alte Logik als Fallback |

---

## Anhang B: Entscheidungsbaum (Ziel-Algorithmus)

```
Datei wird gescannt
│
├─ Hypothesen sammeln (alle 8 Methoden)
│
├─ HypothesisResolver
│   ├─ Gruppieren nach ConsoleKey
│   ├─ Winner bestimmen (Summe, MaxSingle)
│   ├─ Conflict-Penalty anwenden
│   ├─ Agreement-Bonus anwenden
│   ├─ ★ Soft-Only-Cap (max 65) anwenden
│   ├─ ★ Single-Source-Cap anwenden
│   ├─ ★ HasHardEvidence + IsSoftOnly setzen
│   ├─ ★ SortDecision ableiten
│   └─ ConsoleDetectionResult zurückgeben
│
├─ Enrichment
│   ├─ DAT-Match prüfen
│   │   ├─ Match → Confidence=100, SortDecision=DatVerified
│   │   └─ Kein Match → Confidence aus Resolver
│   ├─ RomCandidate erstellen mit allen Flags
│   └─ weiter
│
├─ Sorting Gate (NEU: 4 Stufen)
│   ├─ Gate 1: Category = Game?
│   │   └─ Nein → BLOCKED
│   ├─ Gate 2: ConsoleKey != UNKNOWN && != AMBIGUOUS?
│   │   └─ Nein → BLOCKED
│   ├─ Gate 3: SortDecision = Sort oder DatVerified?
│   │   ├─ Ja → SORT ERLAUBT
│   │   ├─ SortDecision = Review → REVIEW-QUEUE
│   │   └─ SortDecision = Blocked → BLOCKED
│   └─ Gate 4: (Optional) Zusätzliche System-spezifische Regeln
│
└─ Report: Sort/Review/Blocked counts
```

---

## Anhang C: PS1/PS2 Serial-Overlap-Fix

### Problem

Aktuelle Serial-Patterns:

```csharp
// PS1: \b(SLUS|SCUS|SLPS|SCPS|SLPM|SIPS|SCES|SLES|SLKA|PAPX)-\d{3,5}\b
// PS2: \b(SLUS|SCUS|SLPS|SCPS|SLPM|SCES|SLES|SLKA|PBPX)-\d{5}\b
```

Überlappung bei 5-stelligen Nummern: SLUS-12345 matcht **beide** Patterns.
SerialPatterns-Array iteriert sequentiell → PS1 gewinnt immer (First-Match).

### Lösung

```csharp
// PS1: 3-4 stellige Nummern (SLUS-001 bis SLUS-9999)
(Rx(@"\b(SLUS|SCUS|SLPS|SCPS|SLPM|SIPS|SCES|SLES|SLKA|PAPX)-\d{3,4}\b"), "PS1"),

// PS2: 5-stellige Nummern ab 20000 (SLUS-20001 bis SLUS-29999)
(Rx(@"\b(SLUS|SCUS|SLPS|SCPS|SLPM|SCES|SLES|SLKA|PBPX)-[2-9]\d{4}\b"), "PS2"),
```

### Alternative: Confidence-Split

```csharp
// PS1 Serial (3-4 digits → high confidence)
(Rx(@"\b(SLUS|SCUS|...)-\d{3,4}\b"), "PS1", 95),

// PS2 Serial (5 digits, 20000+ → high confidence)
(Rx(@"\b(SLUS|SCUS|...)-[2-9]\d{4}\b"), "PS2", 95),

// Ambiguous range (5 digits, 10000-19999 → niedrige Confidence)
(Rx(@"\b(SLUS|SCUS|...)-1\d{4}\b"), "PS1", 60),  // wird durch Cap nicht sortiert
```
