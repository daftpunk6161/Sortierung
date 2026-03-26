# Datensatz-Audit-Prozess (Phase D2)

> **Version:** 1.0.0  
> **Status:** Verbindlich  
> **Erstellt:** 2026-03-23  
> **Bezug:** ADR-017, RECOGNITION_QUALITY_BENCHMARK.md, COVERAGE_GAP_AUDIT.md

---

## 1. Zweck

Formalisierter Review-Zyklus für den Benchmark-Datensatz, um:

- Ground-Truth-Drift zu erkennen und zu korrigieren
- Erkennungslücken systematisch zu schließen
- Qualität und Repräsentativität des Testsets langfristig zu sichern
- Overfitting an stabile Testdaten zu verhindern

---

## 2. Frequenz und Auslöser

### Regulärer Audit (jährlich)

| # | Schritt | Verantwortlich | Output |
|---|---------|----------------|--------|
| A1 | Coverage-Gap-Analyse gegen `gates.json` | Reviewer | Aktualisierte COVERAGE_GAP_AUDIT.md |
| A2 | Confusion-Matrix-Review (Top-10 Paare) | Reviewer | Issue pro Paar mit > 2% Rate |
| A3 | Fallklassen-Vollständigkeitsprüfung | Reviewer | Fehlende Klassen → Dataset-Erweiterung |
| A4 | Holdout-Drift-Prüfung (via HoldoutEvaluator) | CI/Reviewer | Drift-Report |
| A5 | Baseline-Archiv-Prüfung | Reviewer | Bestätigung: Archiv vollständig |
| A6 | Schema-Versions-Prüfung | Reviewer | Schema vs. tatsächliche Entries |
| A7 | Zusammenfassung → COVERAGE_GAP_AUDIT.md | Reviewer | Commit + PR |

### Ereignisgesteuerte Audits

| Auslöser | Pflichtaktion |
|----------|--------------|
| Neues System in `consoles.json` | ≥ 3 Ground-Truth-Entries erstellen |
| Neue Detection-Methode | ≥ 10 Entries pro betroffener Methode |
| M4 oder M7 Regression im CI | Root-Cause-Analyse + Edge-Case-Entry |
| Bug-Report mit Fehlsortierung | Reproduktionsfall → `edge-cases.jsonl` |
| Großes Refactoring (Score/Key/Region) | Holdout-Drift-Prüfung vor und nach Merge |
| Schema-Migration (neue Pflichtfelder) | Alle bestehenden Entries aktualisieren |

---

## 3. Checkliste für den jährlichen Audit

```markdown
### Jährlicher Datensatz-Audit – Checkliste

- [ ] **Datum:** ___________
- [ ] **Reviewer:** ___________
- [ ] **Dataset-Version:** ___________

#### Coverage
- [ ] `gates.json` Schwellen: Alle Pass?
- [ ] Systeme: 69/69 abgedeckt?
- [ ] Fallklassen: 20/20 besetzt?
- [ ] Chaos-Quote: ≥ 30%?
- [ ] Tier-1-Tiefe: ≥ 20 pro System?
- [ ] BIOS-Systeme: ≥ 15?

#### Qualität
- [ ] Confusion-Matrix: Kein Paar > 2%?
- [ ] M4 (Wrong Match Rate): Trend stabil oder sinkend?
- [ ] M7 (Unsafe Sort Rate): Trend stabil oder sinkend?
- [ ] M9a (Game-as-Junk): ≤ 0.1%?
- [ ] M14 (Repair-Safe Rate): Trend steigend?
- [ ] M16 (ECE): ≤ 10%?

#### Anti-Overfitting
- [ ] Holdout-Drift: Keine signifikante Abweichung?
- [ ] Neue Entries seit letztem Audit: ≥ 50?
- [ ] Mindestens 10% der neuen Entries sind adversarial/chaos?

#### Infrastruktur
- [ ] Schema aktuell und konsistent?
- [ ] Baselines archiviert und vollständig?
- [ ] CI-Pipeline: Quality Gates aktiv?
- [ ] HTML-Dashboard generiert und lesbar?

#### Ergebnis
- [ ] COVERAGE_GAP_AUDIT.md aktualisiert
- [ ] Issues für offene Lücken erstellt
- [ ] PR mit Audit-Zusammenfassung eingereicht
```

---

## 4. Audit-Ergebnis-Format

Jeder abgeschlossene Audit wird in `COVERAGE_GAP_AUDIT.md` dokumentiert:

```markdown
### Audit 2026-XX-XX

**Reviewer:** Name
**Dataset-Version:** X.Y.Z (N Entries)
**Ergebnis:** PASS / CONDITIONAL / FAIL

#### Metriken
| Metrik | Wert | Ziel | Status |
|--------|------|------|--------|
| M4 Wrong Match Rate | X.XX% | ≤ 0.5% | ✅/❌ |
| ...    | ...  | ...  | ...    |

#### Offene Lücken
1. Beschreibung → Issue #NNN
2. ...

#### Nächste Schritte
- ...
```

---

## 5. Governance-Regeln

1. **Ground-Truth-Änderungen erfordern PR + Review** — keine direkten Commits auf main.
2. **Jeder neue Entry braucht:** korrektes Schema, Fallklassen-Tags, Review-Status.
3. **Baselines werden nie überschrieben** — immer archivieren, dann neue erstellen.
4. **Holdout-Zone ist read-only** für Detection-Tuning — nur Audit darf prüfen.
5. **Audit-Ergebnisse sind verbindlich** — offene Lücken müssen als Issues getrackt werden.
