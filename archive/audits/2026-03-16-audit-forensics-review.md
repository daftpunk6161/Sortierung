# Code Review: Forensic Audit & Recovery Paths
**Ready for Production**: Yes (with follow-up recommendations)
**Critical Issues**: 0 (all 8 resolved)
**Date**: 2026-03-16
**Scope**: Forensische Nachvollziehbarkeit, Audit-Vollstandigkeit, sichere Zustande, Recovery-Pfade
**Reviewer**: SE: Security Mode
**Test Suite**: 3141 tests (3135 passed, 6 skipped/UI, 0 failed)

---

## Zusammenfassung

8 Audit-/Sicherheitslucken (A-01 bis A-08) wurden identifiziert und behoben.
Alle 15 `HardAuditInvariantTests` bestehen. Die vollstandige Regression (3135 Tests) ist grun.

---

## Befunde (geloest)

### A-01 ConsoleSorter: Fehlender Audit-Trail (P2 -> behoben)

**VORHER:** `ConsoleSorter.Sort()` bewegte Dateien ohne jegliche Audit-Spur.
Bei einem `Move`-Lauf liess sich nicht nachvollziehen, welche Dateien durch
Console-Sorting wohin verschoben wurden. Rollback war nicht moglich.

**NACHHER:** Konstruktor akzeptiert `IAuditStore? audit` und `string? auditPath`
(optional, backward-kompatibel). Jede physische Dateibewegung schreibt eine
`CONSOLE_SORT`-Audit-Zeile. Atomic Set-Moves (CUE+BIN, GDI, CCD, M3U, MDS)
erhalten separate Audit-Eintraege fuer Primary und jedes Member.

**Dateien:**
- `src/RomCleanup.Infrastructure/Sorting/ConsoleSorter.cs` (4 Aenderungen)
- `src/RomCleanup.Infrastructure/Orchestration/RunOrchestrator.cs` (1 Aenderung)

---

### A-02 ConvertOnly: Doppelzaehlung bei Verify-Fehler (P0 -> behoben)

**VORHER:** Im `ConvertOnly`-Branch wurde `converted++` VOR der Verify-Pruefung
inkrementiert. Bei fehlgeschlagener Verifizierung wurde die Quelldatei trotzdem
in den Trash verschoben. Ergebnis: ueberhohte Konvertierungszahlen und
Datenverlust bei fehlerhaften Konvertierungen.

**NACHHER:** Sequenz korrigiert:
1. Konvertierung ausfuehren
2. Verify pruefen
3. NUR bei Verify-Erfolg: `converted++` und Source -> Trash
4. Bei Verify-Fehler: `CONVERT_FAILED` Audit-Zeile, kein Trash

**Datei:** `src/RomCleanup.Infrastructure/Orchestration/RunOrchestrator.cs`

---

### A-03 Source-Trash nach Konvertierung: Kein Audit (P1 -> behoben)

**VORHER:** Wenn eine Quelldatei nach erfolgreicher Konvertierung in den Trash
verschoben wurde, wurde keine Audit-Zeile geschrieben. Die Dateibewegung
war forensisch nicht nachvollziehbar.

**NACHHER:** Source-to-Trash erfolgt nur nach Verify-Erfolg. Der Audit-Trail
dokumentiert den gesamten Vorgang (CONVERT_OK oder CONVERT_FAILED).

---

### A-04 Konvertierungsfehler: Kein Audit-Eintrag (P1 -> behoben)

**VORHER:** `ConversionOutcome.Error` erzeugte keinen Audit-Eintrag.
Fehlerhafte Konvertierungen verschwanden im Log-Rauschen.

**NACHHER:** Jeder Konvertierungsfehler schreibt eine `CONVERT_ERROR`
Audit-Zeile mit Grund (`convert-error:{reason}`).

**Datei:** `src/RomCleanup.Infrastructure/Orchestration/RunOrchestrator.cs`

---

### A-05 Cancel-Sidecar: Unzureichende Informationen (P1 -> behoben)

**VORHER:** Das Cancel-Sidecar enthielt nur `Status`, `ExitCode`,
`CancelledAtUtc`. Keine Information darueber, welche Phase aktiv war,
wie viele Dateien bereits verarbeitet waren oder wie viele Fehler auftraten.

**NACHHER:** 11 zusaetzliche Felder:
- `CancelledAtUtc`, `LastPhase` (aktive Phase bei Abbruch)
- `TotalFilesScanned`, `GroupCount`
- `MoveCount`, `FailCount`, `SkipCount`
- `ConvertedCount`, `ConvertErrorCount`
- `DurationMs`

**Dateien:**
- `src/RomCleanup.Infrastructure/Orchestration/RunOrchestrator.cs`
- `src/RomCleanup.Infrastructure/Metrics/PhaseMetricsCollector.cs` (neues `GetCurrentPhaseName()`)

---

### A-06 Completion-Sidecar: Minimale Metriken (P2 -> behoben)

**VORHER:** Das regulaere (Nicht-Cancel) Sidecar enthielt nur `Status`
und `ExitCode`. Keine Metriken fuer Cross-Validation mit dem Audit-CSV.

**NACHHER:** Identische 11+ Felder wie Cancel-Sidecar, ermoeglichen
konsistente forensische Rekonstruktion unabhaengig vom Beendigungsgrund.

---

### A-07 Rollback-Trail: Fehlende forensische Details (P2 -> behoben)

**VORHER:** `WriteRollbackTrail()` schrieb nur `RestoredPath,Timestamp`.
Es war nicht nachvollziehbar, WOHIN die Datei verschoben worden war
(und somit WOHER die Wiederherstellung erfolgte) oder welche Aktion
der Grund fuer die urspruengliche Verschiebung war.

**NACHHER:** 4 Spalten: `RestoredPath,RestoredFrom,OriginalAction,Timestamp`.
Lookup aus Original-Audit-CSV liefert den damaligen NewPath (= RestoredFrom)
und die damalige Action (Move, JUNK_REMOVE, CONSOLE_SORT etc.).

**Datei:** `src/RomCleanup.Infrastructure/Audit/AuditCsvStore.cs`

---

### A-08 ConflictPolicy=Skip: Kein Audit (P1 -> behoben)

**VORHER:** Wenn `ConflictPolicy=Skip` galt und eine Zieldatei bereits
existierte, wurde die Datei still uebersprungen. Kein Audit, kein Zaehler.
Die Information ging verloren.

**NACHHER:** `SKIP` Audit-Zeile mit Reason `conflict-policy:skip`.
`skipCount` wird inkrementiert und in `MovePhaseResult.SkipCount`
zurueckgegeben. Der Wert fliesst in Sidecar und API-Response.

**Datei:** `src/RomCleanup.Infrastructure/Orchestration/RunOrchestrator.cs`

---

## P1-Bugs (parallel behoben)

| ID | Befund | Fix |
|------|--------|-----|
| P1-01 | `result.Status` immer `"ok"` auch bei Fehlern | Conditional: `completed_with_errors` bei >0 Fehlern |
| P1-02 | `MoveResult` mischte Junk- und Dedupe-Moves | Getrennte `JunkMoveResult` und `MoveResult` |
| P1-03 | `ApiRunResult` ohne ConvertedCount/FailCount | 5 neue Properties + korrekte Befuellung |
| P1-04 | CLI-Sidecar schrieb `LoserCount` statt `MoveCount` | Nutzt `MoveResult?.MoveCount`, LoserCount reconciled |
| P1-05 | Kein `SkipCount` in `MovePhaseResult` | `SkipCount = 0` (default-param, backward-komp.) |
| P1-07 | API-Status-Mapping ignorierte Fehler | `completed_with_errors` bei `ExitCode = 0 + errors` |

---

## Geaenderte Dateien

| Datei | Aenderungen | Risiko |
|-------|-------------|--------|
| `ConsoleSorter.cs` | +IAuditStore, +WriteAuditRow, Set-Audit | Niedrig (opt. Params) |
| `RunOrchestrator.cs` | ConvertOnly-Fix, Cancel/Completion-Sidecar, Skip-Audit, MoveResult-Split, Status-Derivation, LoserCount-Reconciliation | Mittel |
| `PhaseMetricsCollector.cs` | +GetCurrentPhaseName() | Niedrig |
| `AuditCsvStore.cs` | Rollback-Trail 4-Spalten, Audit-Lookup | Niedrig |
| `RunManager.cs` | ApiRunResult +5 Props, Status-Mapping | Niedrig |
| `Program.cs` (CLI) | Sidecar `["move"]` Fix | Niedrig |

---

## Offene Empfehlungen (Follow-up)

### E-01 Security-Tests fuer neue Audit-Verhalten (empfohlen)
- ConsoleSorter-Test: Pruefe `CONSOLE_SORT` Audit-Zeilen nach echtem Move
- Cancel-Sidecar-Test: Pruefe `LastPhase`-Feld
- Rollback-Trail-Test: Pruefe `RestoredFrom` und `OriginalAction`
- Skip-Audit-Test: Pruefe `SKIP`-Zeile bei Zielkonflikt

### E-02 Audit-Trail-Signierung fuer CONSOLE_SORT
Der bestehende `AuditSigningService` erzeugt SHA256-Sidecar-Dateien fuer
Audit-CSVs. CONSOLE_SORT-Moves werden jetzt in dasselbe Audit-CSV geschrieben.
Sicherstellung, dass die Signierung NACH allen Phasen (inkl. Sorting) erfolgt.

### E-03 Rollback fuer CONSOLE_SORT-Moves
Aktuell kann `RollbackLastRun()` nur Move/JUNK_REMOVE rueckgaengig machen.
CONSOLE_SORT-Zeilen sollten ebenfalls rueckgaengig gemacht werden koennen.

---

## Regression

```
dotnet test src/RomCleanup.Tests/RomCleanup.Tests.csproj
Bestanden: 3135, Uebersprungen: 6, Fehler: 0
```

Alle 15 `HardAuditInvariantTests` bestehen (8 P0 + 7 P1).

---

## Status-Nachtrag (2026-03-18)

Die folgenden Punkte aus dem konsolidierten Audit wurden nachtraeglich umgesetzt
und per zielgerichteter Regression verifiziert:

| ID | Status | Umsetzung |
|----|--------|-----------|
| P1-12 | Behoben | Dry-Run-Rollback meldet nur wirklich restorable Eintraege (existierende aktuelle Quelle); False-Positives entfernt |
| P2-04 | Behoben | Explizites Progress-Logging fuer UNKNOWN-Console DAT-No-Match in der Enrichment-Phase |
| P1-16 | Behoben | Report-Accounting-Invariante auf strikte Gleichheit gehaertet (Dedup-Runs) |
| P3-04 | Behoben | Tote Fassade entfernt: `ApplicationServiceFacade` geloescht, zugehoerige Legacy-Tests bereinigt |
| P1-14 | Behoben | KPI-Namensparitaet ueber CLI/API/OpenAPI via nicht-brechende Alias-Felder (`Winners`, `Losers`, `Duplicates`) |
| P2-13 | Behoben | Feingranulare Fortschrittsupdates in Move- und Conversion-Phasen (periodische Zwischenstaende) |
| P3-01 | Behoben | Rollback-Pfad konsolidiert: `AuditCsvStore` delegiert an `AuditSigningService`; WPF-Rollback nutzt den Store-Pfad |

### Verifikation (2026-03-18)

- Zielgerichtete Regression fuer Rollback- und CLI-Pfade: **112 bestanden, 0 fehlgeschlagen**
- Diagnostik in den geaenderten Rollback-Dateien: keine offenen Fehler

### Hinweise

- Die oben genannten Aenderungen sind bewusst rueckwaertskompatibel umgesetzt (Alias-Felder statt Breaking Renames).
- Ein optionaler Voll-Lauf aller Tests wird weiterhin empfohlen, falls ein Release-Snapshot erzeugt wird.
