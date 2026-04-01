# Benchmark Audit & Ground Truth Governance

Stand: 2026-04-01

Dieses Dokument beschreibt den produktionsnahen Governance-Rahmen fuer Benchmark-, Manifest- und Ground-Truth-Aenderungen.

## 1. Aktueller Baseline-Stand

Snapshot aus `benchmark/manifest.json`:

| Kennzahl | Wert |
|---|---:|
| `totalEntries` | 7639 |
| `holdoutEntries` | 200 |
| `performance-scale` | 5000 |
| `lastModified` | 2026-04-01 |

Die Manifest-Basis ist nur gueltig, wenn die Checksummen der JSONL-Dateien und die `bySet`-Summen konsistent bleiben.

## 2. Verbindliche Release-Gates

Vor Release oder vor einem bewusst akzeptierten Baseline-Update muessen mindestens diese Schritte grün sein:

```powershell
pwsh -NoProfile -File benchmark/tools/Test-ManifestIntegrity.ps1
pwsh -NoProfile -File benchmark/tools/Invoke-CoverageGate.ps1 -NoBuild
```

Optional fuer menschlich lesbare Auswertung:

```powershell
pwsh -NoProfile -File benchmark/tools/New-CoverageGapReport.ps1 -Format markdown
```

## 3. Aenderungsregeln fuer Ground Truth

1. Ground-Truth-Aenderungen nur per Pull Request.
2. Jeder GT-PR braucht mindestens ein Review.
3. Die Quelle fuer die Aenderung muss im PR nachvollziehbar benannt werden.
4. Manifest und Checksummen muessen nach Dataset-Aenderungen aktualisiert werden.
5. Holdout-Daten bleiben read-only fuer regulare Tuning-Arbeit.

## 4. Baseline- und Manifest-Hygiene

Pflicht bei Dataset-Aenderungen:

1. JSONL-Dateien aendern oder ergaenzen
2. Manifest regenerieren
3. Manifest-Integrity pruefen
4. Coverage-Gate pruefen
5. Aenderung mit Begruendung reviewen lassen

Die Manifest-Datei ist stale, wenn:

- `fileChecksums` nicht mehr zu den echten JSONL-Dateien passen
- `totalEntries` nicht mehr zur Summe der Datensaetze passt
- `bySet` oder `systemsCovered` nicht mehr konsistent sind

## 5. Audit-Kadenz

### Regulare Zyklen

- Q1: grosser Ground-Truth-/Coverage-Audit
- Q3: Delta-Review mit Fokus auf Holdout und neue Systeme

### Ereignisgetriebene Trigger

| Trigger | Aktion |
|---|---|
| neue Konsole in `consoles.json` | gezielte Benchmark-Erweiterung und Gate-Review |
| Recognition-/Scoring-Aenderung | Manifest-Integrity + Coverage-Gate + Holdout-Check |
| Regression in `CoverageGate`/`QualityGate` | Root-Cause-Review vor weiterem Feature-Bau |
| groessere Dataset-Erweiterung | Manifest regenerieren und Audit-Report aktualisieren |

## 6. Verantwortlichkeiten

| Rolle | Aufgabe |
|---|---|
| Maintainer | Ground-Truth-PRs reviewen, Audit-Freigaben geben |
| Contributor | Reproduktionsfaelle und neue Entries mit Quelle einreichen |
| CI | Manifest-Integrity, Coverage-Gate und Baseline-Drift pruefen |

## 7. Bezug zu R4 Stabilization

Der Stabilization-Schnitt verankert Benchmark-Governance bewusst im Release-Pfad:

- Manifest-Integrity wird als reproduzierbarer Schritt gefahren
- Coverage-Gate ist Teil der Release-Smoke-Matrix
- Dataset-/Manifest-Hygiene ist nicht mehr nur Prozesswissen, sondern release-relevanter Gate
