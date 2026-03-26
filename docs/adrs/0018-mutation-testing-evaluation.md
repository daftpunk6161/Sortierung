# ADR-0018: Mutation-Testing-Evaluation (Stryker.NET)

**Status**: Accepted  
**Datum**: 2025-07-10  
**TASK**: TASK-111  
**Kontext**: §25 TEST_STRATEGY — Mutation-Testing Status klären

---

## Kontext

Mutation Testing identifiziert schwache Tests, indem es kleine Codeänderungen (Mutanten) einführt und prüft, ob bestehende Tests diese erkennen. Stryker.NET ist das etablierte Mutation-Testing-Framework für .NET.

### Aktueller Stand

| Aspekt | Status |
|---|---|
| CI-Job | `mutation` in `test-pipeline.yml` vorhanden |
| Trigger | `workflow_dispatch` only (manuell) |
| Gate-Modus | `continue-on-error: true` (Reporting only) |
| Scope | `RomCleanup.Core` |
| Thresholds | high=80%, low=60%, break=40% |
| MutationKillTests.cs | Handgeschriebene Tests vorhanden |
| Reporter | HTML + JSON (Artifact-Upload) |

## Entscheidung

**Stryker.NET bleibt als optionales Reporting-Tool.** Kein Gate-Enforcement in CI.

### Begründung

1. **Laufzeit**: Stryker.NET gegen `RomCleanup.Core` (5700+ Tests) dauert ~15-30 Minuten. Als PR-Gate inakzeptabel.
2. **False Positives**: Equivalent Mutants (semantisch identische Änderungen) erzeugen Rauschen, insbesondere bei Score-Berechnungen mit Grenzwerten.
3. **Handgeschriebene Kill-Tests effektiver**: `MutationKillTests.cs` deckt gezielt die kritischen Mutation-Bereiche ab (DeduplicationEngine, GameKeyNormalizer, RegionDetector, Scoring). Diese Tests sind deterministisch und schnell.
4. **Kosten/Nutzen**: Der marginale Qualitätsgewinn eines Mutation-Score-Gates rechtfertigt nicht die CI-Zeit und Wartungskosten.

## Konsequenzen

### Positive Folgen

- CI-Laufzeit bleibt stabil (keine 15+ Min. pro PR)
- Kein Wartungsaufwand für Mutation-Score-Regressions
- Gezielte Mutation-Kill-Tests bleiben wartbar und verständlich

### Negative Folgen

- Kein automatisiertes Feedback über Mutations-Lücken
- Manuelle Auslösung nötig für periodische Evaluation

### Mitigationen

- **Vierteljährliche Mutation-Runs**: Manuell via `workflow_dispatch`, Ergebnisse reviewen
- **MutationKillTests.cs**: Bei neuer Core-Logik gezielt ergänzen
- **Threshold-Tracking**: Bei manuellen Runs den break-Threshold (40%) als Baseline beobachten
- **Scope-Erweiterung**: Falls Core-Coverage stabil >70%, Infrastructure in Scope aufnehmen

## Alternativen (verworfen)

| Alternative | Verwerfungsgrund |
|---|---|
| Stryker als PR-Gate | Laufzeit 15-30 Min. pro PR nicht tragbar |
| Stryker Nightly | Geringer Mehrwert gegenüber manuellem Dispatch |
| Anderes Tool (PITest, Infection) | .NET-Support unzureichend |
| Kein Mutation-Testing | Vorhandene Infrastruktur ist bereits aufgesetzt |

## Referenzen

- [Stryker.NET Docs](https://stryker-mutator.io/docs/stryker-net/introduction/)
- `.github/workflows/test-pipeline.yml` → Job `mutation`
- `src/RomCleanup.Tests/MutationKillTests.cs`
- `docs/architecture/TEST_STRATEGY.md` → §5 CI-Pipeline
