# ADR-016: Real-World Testset Architecture

## Status
Proposed

## Datum
2026-03-21

## Kontext

Romulus besitzt bereits ein Benchmark-Framework (ADR-015) mit 2.073 Ground-Truth-Einträgen, 16 Stub-Generatoren, einem 7-Ebenen-Qualitätsmodell und CI-fähiger Evaluationspipeline. Die bisherigen Designdokumente (TESTSET_DESIGN.md, GROUND_TRUTH_SCHEMA.md) definieren Schema, Dataset-Klassen und Pflichtfälle auf konzeptioneller Ebene.

Was fehlt, ist die **architektonische Entscheidung**, wie das Testset als **langfristig belastbares, Anti-Overfitting-gesichertes, versioniertes Real-World-Testsystem** betrieben wird – insbesondere:

1. **Daten-Isolation** zwischen Entwicklungs-Fixtures und Benchmark-Evaluation
2. **Testset-Governance** als formaler Prozess (nicht als Gentleman's Agreement)
3. **Skalierungsstrategie** von 2.073 auf 3.000+ Entries ohne Qualitätsverlust
4. **Härtungsstrategie** gegen die 12 identifizierten Bias-Risiken
5. **Architektonische Verbindung** zwischen Testset-Struktur und Produktions-Detection-Cascade

### Treiber

| # | Treiber | Risiko ohne Entscheidung |
|---|---------|--------------------------|
| T1 | Stub-Dateien sind synthetisch; echte ROMs haben Varianz in Header-Padding, Alignment, Sektorgrößen | False Confidence: 100 % Pass auf Stubs, 85 % auf echten Sammlungen |
| T2 | Alle 2.073 Entries sind für Entwickler sichtbar und als Optimierungsziel nutzbar | Benchmark-Overfitting: Detection wird gegen bekannte Stubs getuned |
| T3 | performance-scale.jsonl ist leer (0 Entries) | Keine Performance-Regression-Erkennung |
| T4 | PC/Computer-Systeme (DOS, AMIGA, C64, ZX, MSX) sind unterrepräsentiert | Blinde Spots bei ~15 % der realen Sammlungen |
| T5 | Arcade-Parent/Clone und Split/Merged-Varianten haben minimale Tiefe | MAME-Nutzer (größte Einzelgruppe) unzureichend getestet |
| T6 | Kein formaler Prozess verhindert Ground-Truth-Drift nach Codeänderungen | Stille Regression: Code ändert sich, Ground Truth nicht |
| T7 | Directory-based Games (Wii U RPX, 3DS CIA, PC) praktisch nicht abgedeckt | Wachsender Anteil moderner Sammlungen ungetestet |

## Entscheidung

### 1. Drei-Zonen-Architektur (Dev / Eval / Holdout)

```
┌─────────────────────────────────────────────────────────────────┐
│                     TESTSET ARCHITECTURE                        │
├──────────────────┬──────────────────┬───────────────────────────┤
│   DEV ZONE       │   EVAL ZONE      │   HOLDOUT ZONE            │
│   (Entwicklung)  │   (Messung)      │   (Anti-Overfitting)      │
├──────────────────┼──────────────────┼───────────────────────────┤
│ golden-core      │ golden-realworld │ holdout-blind             │
│ Fixtures für     │ chaos-mixed      │ (Entries nie öffentlich    │
│ Unit/Integration │ edge-cases       │  im Repo; nur CI-Runner   │
│                  │ negative-controls│  hat Zugriff)             │
│ Sichtbar für     │ repair-safety    │                           │
│ alle Entwickler  │ dat-coverage     │ Wird nur bei Release-     │
│                  │                  │ Gates und Nightly          │
│ Optimierung      │ READ-ONLY für    │ ausgewertet               │
│ erlaubt          │ Detection-Tuning │                           │
├──────────────────┼──────────────────┼───────────────────────────┤
│ ~370 Entries     │ ~780 Entries     │ ~200 Entries (Ziel)       │
│ Laufzeit <10s    │ Laufzeit <60s    │ Laufzeit <30s             │
└──────────────────┴──────────────────┴───────────────────────────┘
```

**Rationale:**
- **Dev Zone** (`golden-core`): Darf als Entwicklungshilfe genutzt werden. Stubs sind bekannt, Header-Bytes dokumentiert. Schnelle Feedback-Schleife.
- **Eval Zone** (alle anderen aktiven Sets): Read-Only für Detection-Tuning. Entwickler dürfen Ergebnisse sehen, aber Detection-Regeln NICHT gegen diese Samples optimieren. Verstöße werden durch Holdout-Divergenz erkannt.
- **Holdout Zone** (neues Set): 200 Entries, die NICHT im Repo committed sind. Als verschlüsseltes Artefakt oder CI-Secret verwaltet. Nur der CI-Runner wertet sie aus. Wenn Eval-Zone-Metriken steigen, Holdout aber stagniert → Overfitting-Signal.

**Implementierung der Holdout-Zone:**
```
Option A (empfohlen): Separates Git-Repository (privat)
  → CI clont es als Submodul in geschützter Pipeline
  → Entwickler haben keinen Direct-Access

Option B (fallback): Encrypted blob im Repo
  → benchmark/holdout/holdout.jsonl.enc (AES-256, Key als CI-Secret)
  → Nur CI-Step kann entschlüsseln und auswerten

Option C (minimal): Kein Holdout, stattdessen statistischer Overfitting-Detector
  → Vergleicht Verbesserungsrate pro Commit auf Eval vs. vorherigen Baseline
  → Alert wenn Verbesserung >5× stärker auf Eval als erwartet
```

### 2. Dataset-Klassen-Architektur (8 + 1)

| Klasse | Zone | Zweck | Zielgröße | Laufzeit-Budget |
|--------|------|-------|-----------|-----------------|
| `golden-core` | Dev | Deterministische Unit-/Integrationstests | 300–400 | <10s |
| `golden-realworld` | Eval | End-to-End-Erkennung unter realistischen Bedingungen | 500–800 | <60s |
| `chaos-mixed` | Eval | Robustheit bei chaotischen Inputs | 300–500 | <45s |
| `edge-cases` | Eval | Gezielte Grenzfälle und Verwechslungspaare | 150–250 | <20s |
| `negative-controls` | Eval | Sicherstellung korrekter Zurückweisung | 100–150 | <10s |
| `repair-safety` | Eval | Confidence-Gating und Sort-/Repair-Sicherheit | 100–150 | <15s |
| `dat-coverage` | Eval | DAT-Matching-Qualität isoliert | 150–200 | <20s |
| `performance-scale` | Perf | Skalierung und Throughput (generiert) | 5.000–20.000 | <5min |
| `holdout-blind` | Holdout | Anti-Overfitting-Kontrolle | ~200 | <30s |

**Gesamtziel:** 1.900–2.500 manuell kuratierte Entries + 5.000–20.000 generierte Perf-Entries.

### 3. Stub-Realismus-Härtung

**Problem:** Synthetische Stubs mit minimalen Header-Bytes (z.B. 16 Bytes für NES iNES) testen nur den exakten Header-Parser-Pfad. Echte ROMs haben:
- Variables Padding nach dem Header
- Alignment auf Sektorgrenzen (2048 Bytes bei Disc-Images)
- Zusätzliche Metadaten (CHD v5 Hunks, ZIP Local File Headers)
- Ranges in Dateigröße (256 KB – 4 GB)

**Entscheidung:** Drei Stub-Realismus-Level einführen:

| Level | Bezeichnung | Dateigröße | Einsatz |
|-------|-------------|------------|---------|
| L1-minimal | Nur Header-Bytes | 16 B – 64 KB | golden-core Unit Tests |
| L2-realistic | Header + realistisches Padding + korrekte Dateigröße-Klasse | 64 KB – 16 MB | golden-realworld, chaos-mixed |
| L3-adversarial | Header mit absichtlichen Abweichungen (Padding-Varianz, Alignment-Fehler, Trailing-Junk) | variabel | edge-cases, chaos-mixed |

**Implementierung:** `StubGeneratorDispatch` erhält einen `RealismLevel`-Parameter. L2 und L3 verwenden randomisierten Padding-Inhalt (deterministische Seed-basierte PRNG für Reproduzierbarkeit).

```csharp
public enum StubRealismLevel
{
    Minimal,      // L1: Nur Header-Bytes, minimale Dateigröße
    Realistic,    // L2: Header + Padding, realistische Dateigröße-Klasse
    Adversarial   // L3: Header + absichtliche Abweichungen
}
```

### 4. Konsolen-Coverage-Tiering (verbindlich)

| Tier | Min. Entries | Systeme | Coverage-Pflicht |
|------|-------------|---------|------------------|
| T1 (20+) | 20 pro System | NES, SNES, N64, GBA, GB, GBC, MD, PS1, PS2 | In allen 7 aktiven Dataset-Klassen vertreten |
| T2 (10+) | 10 pro System | PSP, SAT, DC, GC, WII, 32X, SMS, GG, PCE, LYNX, A78, A26, NDS, 3DS, SWITCH, AMIGA, ARCADE | In mindestens 4 Dataset-Klassen |
| T3 (5+) | 5 pro System | Alle anderen mit UniqueExtension | In mindestens 2 Dataset-Klassen |
| T4 (3+) | 3 pro System | Alle verbleibenden | In mindestens 1 Dataset-Klasse |

**CI-Gate:** `CoverageValidator` prüft Tier-Compliance bei jedem Build. Verstoß ist Warning (nicht Blocker), wird erst ab v2.0.0 zum Hard Fail.

### 5. Pflicht-Chaos-Quote

**Entscheidung:** Mindestens 30 % aller manuell kuratierten Entries müssen in den Klassen `chaos-mixed`, `edge-cases`, `negative-controls` oder `repair-safety` liegen.

**Rationale:** Testsets degenerieren natürlich zu „mostly clean" – Entwickler fügen bevorzugt einfache Referenzfälle hinzu. Die Pflicht-Chaos-Quote verhindert den bekannten Perfekt-Bias.

**CI-Enforcement:**
```
Total manuell kuratierte Entries: N
Chaos-Entries (chaos-mixed + edge-cases + negative-controls + repair-safety): C
Quote: C / N ≥ 0.30

Verstoß → CI Warning (v1.x), CI Fail (v2.0+)
```

### 6. Ground-Truth-Governance

| Aktion | Erlaubt? | Bedingung |
|--------|----------|-----------|
| Neues Sample hinzufügen | Ja | PR + 1 Reviewer + Schema-Validierung + Stub-Generation-Test |
| `expected.consoleKey` ändern | Eingeschränkt | PR + 2 Reviewer + Begründung mit Quellennachweis |
| Sample löschen | Eingeschränkt | PR + 2 Reviewer + Begründung (Bug in Ground Truth, nicht „Test stört") |
| `acceptableAlternatives` erweitern | Ja | PR + 1 Reviewer |
| `lastVerified` aktualisieren | Ja | Kein Review nötig |
| Neues Pflichtfeld zum Schema hinzufügen | MAJOR Version | ADR-Amendment + Migration Script |

**Anti-Drift-Maßnahme:** CI prüft bei jedem Build:
1. Alle JSONL-Einträge sind schema-valide
2. Alle referenzierten Stub-Dateien können generiert werden
3. Alle IDs sind global eindeutig
4. Keine `lastVerified`-Daten älter als 18 Monate

### 7. Versionierungs-Strategie

```
Ground Truth Version: SemVer (MAJOR.MINOR.PATCH)
  MAJOR: Schema-Strukturänderung (neues Pflichtfeld, Enum-Änderung)
  MINOR: Neue Entries hinzugefügt
  PATCH: Korrektur bestehender Expected-Werte

Baseline-Kompatibilität:
  Baselines sind nur innerhalb derselben MAJOR-Version vergleichbar.
  Bei MAJOR-Bump: Neue Baseline-Serie starten, alte archivieren.

Manifest-Schema:
  manifest.json enthält:
    - version (Testset-Version)
    - groundTruthSchemaVersion (Schema-Version)
    - entryCount (pro Klasse)
    - lastModified (ISO-8601)
    - minimumDetectorVersion (älteste kompatible Detection-Version)
```

## Konsequenzen

### Positive Konsequenzen
- Holdout-Zone macht Overfitting messbar und blockierbar
- Stub-Realismus-Level schließt die Lücke zwischen synthetischen Tests und realen Sammlungen
- Pflicht-Chaos-Quote verhindert Perfekt-Bias-Degeneration
- Formale Governance verhindert unkontrolliertes Wachstum und Ground-Truth-Drift
- Tier-basierte Coverage-Pflicht stellt Breite sicher

### Negative Konsequenzen
- Holdout-Zone erhöht Infrastruktur-Komplexität (CI-Secrets, separates Repo)
- L2/L3-Stubs erhöhen Generierungszeit und Festplattenverbrauch
- Strikte Governance verlangsamt das Hinzufügen neuer Samples

### Risiken
- Holdout-Zone kann veralten, wenn sie nicht aktiv gepflegt wird
- L3-Adversarial-Stubs können falsche Erwartungen testen, wenn die Adversarial-Varianten nicht realistisch sind
- Governance-Overhead könnte dazu führen, dass Entwickler Samples gar nicht erst einreichen

## Betroffene Dokumente
- [TESTSET_DESIGN.md](../TESTSET_DESIGN.md)
- [GROUND_TRUTH_SCHEMA.md](../GROUND_TRUTH_SCHEMA.md)
- [RECOGNITION_QUALITY_BENCHMARK.md](../RECOGNITION_QUALITY_BENCHMARK.md)
- [ADR-015](ADR-015-recognition-quality-benchmark-framework.md)
- `benchmark/manifest.json`
- `benchmark/gates.json`
