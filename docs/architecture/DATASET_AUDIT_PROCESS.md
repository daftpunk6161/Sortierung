# Datensatz-Audit-Prozess

Stand: 2026-04-01

Status: verbindlich

Bezug:

- [BENCHMARK_AUDIT_GOVERNANCE.md](../guides/BENCHMARK_AUDIT_GOVERNANCE.md)
- [RELEASE_SMOKE_MATRIX.md](../guides/RELEASE_SMOKE_MATRIX.md)
- historische Audit-Snapshots unter [`archive/audits/`](../../archive/audits/)

## 1. Zweck

Der Datensatz-Audit-Prozess soll sicherstellen, dass Benchmark- und Ground-Truth-Daten:

- fachlich plausibel bleiben
- nach Aenderungen reproduzierbar validiert werden
- nicht still von Manifest, Gates oder Coverage-Schwellen entkoppeln

## 2. Pflichtschritte bei Dataset-Aenderungen

Wenn JSONL-Daten, Holdout, Gates oder systembezogene Benchmark-Faelle geaendert werden, ist die Minimalfolge:

1. Datensatz aendern
2. Manifest aktualisieren
3. Manifest-Integrity pruefen
4. Coverage-Gate pruefen
5. offene Gaps dokumentieren oder bewusst begruenden

Pflichtbefehle:

```powershell
pwsh -NoProfile -File benchmark/tools/Test-ManifestIntegrity.ps1
pwsh -NoProfile -File benchmark/tools/Invoke-CoverageGate.ps1 -NoBuild
```

Optional fuer Auswertung:

```powershell
pwsh -NoProfile -File benchmark/tools/New-CoverageGapReport.ps1 -Format markdown
```

## 3. Aktuelle Baseline

Aktueller Manifest-Stand aus `benchmark/manifest.json`:

- `totalEntries`: `7639`
- `holdoutEntries`: `200`
- `performance-scale`: `5000`
- `lastModified`: `2026-04-01`

Diese Zahlen sind nicht dekorativ, sondern Referenz fuer den naechsten Audit- und Release-Vergleich.

## 4. Audit-Zyklen

### Regulare Audits

| Zyklus | Fokus | Output |
|---|---|---|
| Q1 | grosser Coverage-/Ground-Truth-Audit | aktualisierter Audit-Snapshot im Archiv |
| Q3 | Delta-Review seit Q1 | Review-Notiz oder neuer Audit-Snapshot |

### Ereignisgetriebene Audits

| Trigger | Pflichtaktion |
|---|---|
| neue Konsole in `consoles.json` | betroffene Benchmark-Faelle und Gates pruefen |
| Recognition-/Scoring-Aenderung | Manifest-Integrity und Coverage-Gate erneut fahren |
| Regression in Qualitätsmetriken | Reproduktionsfall + Gap-Analyse dokumentieren |
| groessere Dataset-Erweiterung | Manifest und Audit-Notiz aktualisieren |

## 5. Audit-Checkliste

```markdown
### Datensatz-Audit

- [ ] Datum / Reviewer dokumentiert
- [ ] Manifest aktualisiert
- [ ] `Test-ManifestIntegrity.ps1` grün
- [ ] `Invoke-CoverageGate.ps1 -NoBuild` grün
- [ ] Coverage-Gap-Report bei Bedarf erzeugt
- [ ] neue oder offene Luecken dokumentiert
- [ ] Audit-Snapshot archiviert, wenn der Stand historisch festgehalten werden soll
```

## 6. Ergebnisformat

Ein Audit-Snapshot soll mindestens enthalten:

- Datum
- Reviewer
- Manifest-Stand
- Ergebnis `PASS` / `CONDITIONAL` / `FAIL`
- offene Luecken oder bewusste Entscheidungen
- naechste Schritte

Historische Snapshots bleiben unter `archive/audits/`.

## 7. Release-Bezug

Fuer den Stabilization-Schnitt ist wichtig:

- Dataset-Hygiene ist Teil des Release-Smoke-Pfads
- Manifest-Integrity und Coverage-Gate duerfen nicht erst nach einem Release auffallen
- offene Benchmark-Gaps muessen vor dem naechsten grossen Feature-Block sichtbar sein
