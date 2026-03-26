# Benchmark Audit & Ground Truth Governance

## Jährlicher Audit-Prozess

### Geplante Audits
- **Jahresaudit (Q1)**: Vollständige GT-Überprüfung, Baseline-Archivierung, Gate-Threshold-Review.
- **Halbjahres-Review (Q3)**: Delta-Check neuer Einträge seit Q1. Holdout-Stichprobe prüfen.

### Ereignisgesteuerte Trigger
| Trigger | Aktion |
|---------|--------|
| Neue Konsole in `consoles.json` | GT-Expansion für die Konsole + Gate-Schwellenwert setzen |
| Recognition-Algorithmus-Änderung (GameKey, Region, Scoring) | Baseline-Snapshot + Holdout-Vergleich |
| M4/M6/M7/M9a-Schwellenwert-Verletzung | Sofortiger Root-Cause-Review |
| Holdout-Divergenz >3% bei Eval-Verbesserung <0.5% | Overfitting-Warning → GT-Bias-Audit |
| Neue DAT-Quelle (TOSEC, Redump Update) | dat-coverage Re-Evaluation |

## Ground-Truth-Änderungsrichtlinie

### Verpflichtende Regeln
1. **Alle GT-Änderungen nur per Pull Request** — kein Direct Push auf `main`.
2. **Jeder GT-PR braucht mindestens 1 Review** durch einen Zweit-Maintainer.
3. **GT-Änderungen brauchen eine Begründung** im PR-Body (z.B. "Redump 2026-03 Update bestätigt SHA1").
4. **`addedInVersion` und `lastVerified`** Felder im GT-Entry pflegen.
5. **Keine rückwirkenden Verdikt-Änderungen** ohne dokumentierten Grund.

### PR-Template für GT-Änderungen
```
## Ground Truth Änderung

**Art:** [ ] Neuer Eintrag [ ] Korrektur [ ] Entfernung
**Konsole:** ___
**Begründung:** ___
**Quelle:** [ ] Redump [ ] No-Intro [ ] TOSEC [ ] Manuelle Prüfung
**Holdout betroffen:** [ ] Ja [ ] Nein
```

## Baseline-Archivierung

### Archivierungs-Strategie
- Jeder Baseline-Snapshot wird unter `benchmark/baselines/` mit Datums-Suffix gespeichert.
- `baseline-latest.json` ist immer ein Symlink/Kopie der aktuellen Baseline.
- Alte Baselines werden archiviert (nicht gelöscht) unter `benchmark/baselines/archive/`.
- Beim Jahresaudit werden Baselines >12 Monate in ein separates Archiv verschoben.

### CI-Integration
- **PR-Gate** (`benchmark.yml` → `benchmark-gate` Job): Prüft CoverageGate + QualityGate auf jedem PR.
- **Nightly** (`benchmark.yml` → `benchmark-full` Job): Vollständiger Benchmark-Lauf mit HTML-Dashboard.
- **Baseline-Update** (`benchmark.yml` → `baseline-publish` Job): Nur manuell nach Review.

## Verantwortlichkeiten

| Rolle | Aufgabe |
|-------|---------|
| Maintainer | GT-PRs reviewen, Jahresaudit durchführen |
| CI Bot | Nightly Benchmarks, Overfitting-Detection |
| Contributor | GT-Ergänzungen nur per PR mit Template |
