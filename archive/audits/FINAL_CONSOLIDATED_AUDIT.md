# Final Consolidated Audit — Reports / Dashboard / Logs / Metrics

> **Datum:** 2026-03-16
> **Scope:** Reports, Dashboard, Kennzahlen, Aggregationen, Statusmodelle, Logs, Audit
> **Methode:** 5 Audit-Runden + Meta-Audit des Refactoring-Plans
> **Basis:** 54 Einzelfindings (F-001–F-054) + 10 Meta-Findings (M-001–M-010)

---

## 1. Executive Verdict

| Kriterium | Bewertung |
|---|---|
| **Ist-Zustand freigabefähig** | **NEIN** |
| **Vertrauenswürdigkeit der aktuellen Zahlen** | **niedrig** |
| **Vertrauenswürdigkeit der aktuellen Reports** | **niedrig** |
| **Vertrauenswürdigkeit der aktuellen Logs** | **mittel** (Struktur vorhanden, aber Lücken) |
| **Tragfähigkeit des Zielmodells** | **mittel-hoch** (tragfähig mit Bedingungen) |

### Kurzfazit

1. Die Konvertierungs-Pipeline zählt eine erfolgreiche Konvertierung UND den Konvertierungsfehler für dieselbe Datei — und löscht dabei die Quelldatei. Das ist **Datenverlust** (F-032).
2. Drei verschiedene HealthScore-Formeln liefern für denselben Run verschiedene Werte je nach Ansicht.
3. Überlappende Roots erzeugen Duplikat-Candidates — alle Zahlen (TotalFiles, Winners, Dupes, HealthScore) sind potenziell inflationiert.
4. `Status = "ok"` wird gesetzt unabhängig davon ob Move- oder Convert-Fehler aufgetreten sind. Ein Run mit 50 fehlgeschlagenen Moves gilt als "erfolgreich".
5. GUI, CLI, API und Report verwenden verschiedene Feldnamen, verschiedene Zähler und verschiedene Definitionen für scheinbar gleiche Metriken.
6. ~75% der Testsuite sind Coverage-Padding ohne Invarianten-Prüfung. Kein Test erkennt die genannten Probleme.
7. MovePhaseResult vermischt Junk- und Dedupe-Moves — nirgends im System trennbar.
8. Das Logging hat Struktur (JSONL + CorrelationId), aber Audit-Lücken (ConsoleSorter, Rollback-Details, Cancel-Phaseninfo) verhindern forensische Rekonstruktion.

**In diesem Zustand kann kein Report, kein Dashboard-Wert und kein Status als belastbar gelten.**

---

## 2. Konsolidierte Hauptprobleme

Nach Bereinigung von 54 Findings + 10 Meta-Findings bleiben **9 strukturelle Hauptprobleme**:

| # | Titel | P | Bereich | Symptom/Ursache |
|---|---|---|---|---|
| HP-1 | Convert-Pipeline: Doppelzählung + Datenverlust | P0 | Orchestrator | **Ursache** |
| HP-2 | Keine Single Source of Truth für Kennzahlen | P0 | Systemweit | **Ursache** |
| HP-3 | Überlappende Roots: inflationierte Basiszahlen | P0 | Scanner | **Ursache** |
| HP-4 | Statusmodell: ok/cancelled/failed/partial unsauber | P1 | Orchestrator+UI | **Ursache** |
| HP-5 | MovePhaseResult vermischt Junk und Dedupe | P1 | Orchestrator | **Ursache** |
| HP-6 | Kein Shared Contract zwischen Output-Layern | P1 | GUI/CLI/API | **Ursache** |
| HP-7 | Audit-Architektur hat blinde Flecken | P1 | Audit | **Ursache** |
| HP-8 | Dashboard-State stale nach Re-Run/Cancel/Rollback | P1 | UI | Symptom von HP-2/HP-4 |
| HP-9 | Testsuite schützt nicht gegen Regressionen | P1 | Tests | **Ursache** |

---

## 3. Kernursachen

### KU-1: Keine Single Source of Truth für Kennzahlen

**Symptome:** F-001, F-002/F-011, F-003, F-005, F-007, F-012, F-016, F-022, F-033, F-037
**Warum zentral:** Jeder Output-Layer (GUI, CLI, API, Report) berechnet Kennzahlen eigenständig aus verschiedenen Feldern mit verschiedenen Formeln. Es existiert kein gemeinsames Projektionsmodell. Drei HealthScore-Formeln, zwei "Games"-Definitionen, ErrorCount aus zwei verschiedenen Quellen.
**Gegenmassnahme:** Ein einziges `RunProjection` Record das vom Orchestrator nach Pipeline-Ende erzeugt wird. Alle Consumer lesen nur davon.

### KU-2: Mutable RunResult als Pipeline-Akkumulator

**Symptome:** F-014, F-044, F-048, F-036, F-047, M-002, M-005
**Warum zentral:** `RunResult` hat 20+ mutable `{ get; set; }` Properties die über 6 Phasen inkrementell befüllt werden. Keine Invarianten-Prüfung, kein Freeze, kein Build-Seal-Pattern. Jeder Consumer kann den Zustand jederzeit mutieren oder in einem Zwischenzustand lesen.
**Gegenmassnahme:** `RunResultBuilder` (mutable, intern) → `.Build()` → `RunResult` (immutable record). Nur das fertige Objekt verlässt den Orchestrator.

### KU-3: Convert-Pipeline ignoriert Verify-Failure

**Symptome:** F-026, F-027, F-032
**Warum zentral:** `converted++` VOR Verify. Bei Verify-Fehler: `convertErrors++` (= Doppelzählung) UND Source wird trotzdem in Trash verschoben (= Datenverlust). Audit schreibt "CONVERT" auch für fehlgeschlagene Verifikation.
**Gegenmassnahme:** Increment-Logik umstrukturieren: Verify zuerst, dann genau einen Counter erhöhen. Source nur löschen wenn Verify bestanden.

### KU-4: Kein Shared Contract zwischen Output-Channels

**Symptome:** F-005, F-007, F-012, F-016, F-031/F-040, M-005
**Warum zentral:** Jeder Layer mapped `RunResult` manuell in eigene Typen (`DashWinners`, JSON-Felder, `ApiRunResult`, `ReportSummary`). Keine Compile-Time-Prüfung ob neue Felder propagiert werden. API droppt 6+ Felder still. CLI schreibt `LoserCount` statt `MoveResult.MoveCount`.
**Gegenmassnahme:** Shared `IRunResultProjection` Interface oder exhaustiver Parity-Test.

### KU-5: Statusmodell trennt Zustände nicht sauber

**Symptome:** F-014, F-015, F-020, F-025, F-048, F-054
**Warum zentral:** `Status = "ok"` wird unabhängig von FailCount gesetzt. SSE sendet "completed" für cancelled. State-Machine crasht bei Cancel→Failed Transition. Cancelled/Failed Runs verstecken das Dashboard. Kein "partial-success" Zustand.
**Gegenmassnahme:** Enum-basiertes Statusmodell: `Completed | PartialFailure | Failed | Cancelled | Blocked`. Status-Ableitung aus FailCount/ConvertErrorCount.

### KU-6: MovePhaseResult vermischt Phase-Ergebnisse

**Symptome:** F-017/F-041, F-022, F-024/F-043, F-031/F-040
**Warum zentral:** Junk-MoveResult wird mit Dedupe-MoveResult addiert. MoveCount, FailCount, SavedBytes — alles untrennbar. PhaseMetrics erbt den Merged-Wert. CLI-Sidecar nutzt LoserCount statt tatsächlichen MoveCount.
**Gegenmassnahme:** Separate `JunkMoveResult` und `DedupeMoveResult` in RunResult. Merge nur für Display, nie für Wahrheit.

### KU-7: Audit-Architektur hat strukturelle Lücken

**Symptome:** F-028, F-029, F-050, F-052, F-053, F-054
**Warum zentral:** ConsoleSorter hat keinen Audit-Trail. Rollback-Ergebnis wird auf 1 von 7 Feldern reduziert. Cancel-Sidecar enthält keine Phase-Info. Zwei divergierende Rollback-Implementierungen. ConsoleSorter-Moves bei Cancel: physisch passiert, kein Audit, nicht rollbackbar.
**Gegenmassnahme:** Audit als Pipeline-Querschnittskonzern: jede Phase loggt eigenen Trail. Rollback-Output vollständig propagieren.

### KU-8: Test-Suite verifiziert keine Invarianten

**Symptome:** M-004, M-008, alle unentdeckten F-Findings
**Warum zentral:** ~75% der Tests sind "no crash = success" oder "dialog was shown". ReportParityTests prüft 4 von ~15 Feldern. Kein Test für Overlapping Roots, HealthScore-Parity, Cancel-Partial-State, MoveCount-vs-LoserCount. Refactoring wird nicht durch Regressions-Tests abgesichert.
**Gegenmassnahme:** Invarianten-Test-Suite die Summen, Parity und Zähllogik mathematisch verifiziert.

---

## 4. Konsolidierte Findings nach Priorität

### P0 — Release-Blocker

#### P0-A | Convert Verify-Failure: Doppelzählung + Datenverlust

- **Original-IDs:** F-026, F-027, F-032
- **Komponenten:** `RunOrchestrator.cs:185–204, 331–373`
- **Problem:** `converted++` vor Verify. Bei Verify-Fehler: `convertErrors++` zusätzlich (doppelt). Audit schreibt "CONVERT" trotz Fehler. Source-Datei wird in Trash verschoben obwohl Target korrupt.
- **Kernursache:** KU-3
- **Lösung:** If-Else statt sequentiell: Verify → Success-Counter ODER Error-Counter. Source nur bei Verify-Success entfernen. Audit-Action "CONVERT_FAILED" für Fehlerfall.
- **Tests:** Unit-Test mit absichtlich fehlerhafter Konvertierung. Assertion: `converted + convertErrors == attemptedConversions` (Summeninvariante). Assertion: Source-Datei existiert noch nach Verify-Failure.

#### P0-B | Drei divergente HealthScore-Formeln

- **Original-IDs:** F-002, F-011
- **Komponenten:** `MainViewModel.RunPipeline.cs:825` (`FeatureService.CalculateHealthScore`), `RunViewModel.cs:352` (`WinnerCount/Total`), `RunResultSummary.cs:16` (`Winners/TotalFiles`)
- **Problem:** Gleicher Run → verschiedene HealthScores je nach View.
- **Kernursache:** KU-1
- **Lösung:** Eine Formel in `FeatureService.CalculateHealthScore`, alle Consumer referenzieren diese.
- **Tests:** Parity-Test: `MainViewModel.HealthScore == RunViewModel.HealthScore` für identischen RunResult.

#### P0-C | "Games"-Definition: Gruppen vs. GAME-Dateien

- **Original-IDs:** F-012
- **Komponenten:** `MainViewModel.RunPipeline.cs:829` (`DedupeGroups.Count`), `CLI Program.cs:221` (`AllCandidates.Count(c => c.Category == "GAME")`)
- **Problem:** GUI zeigt alle Gruppen (inkl. BIOS, JUNK) als "Spiele". CLI zählt nur GAME-Category-Dateien (nicht Gruppen).
- **Kernursache:** KU-4
- **Lösung:** Einheitliche Definition in RunProjection. "Games" = Gruppen mit mindestens einem GAME-Candidate.
- **Tests:** Parity-Test: CLI.Games == GUI.DashGames == API.Games für identischen Input.

#### P0-D | Überlappende Roots: Duplikat-Candidates

- **Original-IDs:** F-045
- **Komponenten:** `RunOrchestrator.cs:468–569` (ScanFiles)
- **Problem:** Kein cross-root Path-Dedup. `C:\Roms` + `C:\Roms\SNES` → gleiche Datei zweimal in AllCandidates. Inflationiert TotalFilesScanned, WinnerCount, LoserCount. Potentieller Doppel-Move.
- **Kernursache:** KU-2
- **Lösung:** `HashSet<string>(StringComparer.OrdinalIgnoreCase)` in ScanFiles ODER Overlap-Prüfung in Preflight.
- **Tests:** Integration-Test mit `Roots = [parent, child]`. Assertion: keine Duplikat-Pfade in AllCandidates.

---

### P1 — Schwere Risiken

#### P1-01 | Status="ok" trotz Move/Convert-Fehler

- **IDs:** F-014
- **Stelle:** `RunOrchestrator.cs:395` — `result.Status = "ok"` unconditional.
- **KU:** KU-5
- **Fix:** `Status = FailCount > 0 || ConvertErrorCount > 0 ? "partial" : "ok"`.

#### P1-02 | MovePhaseResult: Junk+Dedupe vermischt

- **IDs:** F-017/F-041, F-022, F-024/F-043
- **Stelle:** `RunOrchestrator.cs:274–281` — Junk-MoveResult + Dedupe-MoveResult addiert.
- **KU:** KU-6
- **Fix:** Separate Results. ErrorCount trennen: MoveFailCount + ConvertErrorCount.

#### P1-03 | API droppt 6+ Fields silent

- **IDs:** F-016
- **Stelle:** `RunManager.cs:281–292` — ApiRunResult fehlen ConvertedCount, ConvertErrorCount, JunkRemovedCount, MoveResult.FailCount, SavedBytes, PhaseMetrics.
- **KU:** KU-4
- **Fix:** Fields hinzufügen oder Shared Projection nutzen.

#### P1-04 | CLI Sidecar: LoserCount statt MoveCount

- **IDs:** F-031/F-040
- **Stelle:** `CLI Program.cs:250` — `["move"] = result.LoserCount` statt `MoveResult.MoveCount`.
- **KU:** KU-4/KU-6
- **Fix:** `result.MoveResult?.MoveCount ?? 0`.

#### P1-05 | ConflictPolicy Skip: stiller Zähler-Gap

- **IDs:** F-013
- **Stelle:** `RunOrchestrator.cs:675–681` — `continue` ohne Counter.
- **KU:** KU-2
- **Fix:** `skippedCount++` und in RunResult propagieren.

#### P1-06 | ConsoleSorter: kein Audit, kein FailCount

- **IDs:** F-028, F-030
- **Stelle:** `ConsoleSorter.cs` — null AppendAuditRow-Calls. `ConsoleSortResult` hat kein `Failed`-Feld.
- **KU:** KU-7
- **Fix:** IAuditStore-Injection + "CONSOLE_SORT" Action + Failed-Feld.

#### P1-07 | Cancel→Failed State-Crash

- **IDs:** F-020
- **Stelle:** `MainViewModel.RunPipeline.cs:499` — `CompleteRun(false)` setzt `RunState.Failed` nach `RunState.Cancelled`.
- **KU:** KU-5
- **Fix:** State-Machine Transitions validieren.

#### P1-08 | ApplyRunResult vor Cancel-Check

- **IDs:** F-048
- **Stelle:** `MainViewModel.RunPipeline.cs:647 vs 656` — partielle Zahlen ins Dashboard geschrieben.
- **KU:** KU-2/KU-5
- **Fix:** Cancel-Check VOR ApplyRunResult.

#### P1-09 | Stale Dashboard: Re-Run / Rollback

- **IDs:** F-036, F-047
- **Stelle:** Dashboard-Werte nicht zurückgesetzt bei Run-Start oder nach Rollback.
- **KU:** KU-2
- **Fix:** Reset auf "–" in `OnRun()` und `OnRollbackAsync()`.

#### P1-10 | ConvertOnly: irrelevante Dashboard-KPIs

- **IDs:** F-037
- **Stelle:** HealthScore, DashGames, DashDupes zeigen Dedupe-Metriken nach ConvertOnly.
- **KU:** KU-1
- **Fix:** Modusbezogene KPI-Auswahl.

#### P1-11 | Fingerprint fehlt RemoveJunk → Move-Gate-Bypass

- **IDs:** F-038
- **Stelle:** `MainViewModel.RunPipeline.cs:922–944` — RemoveJunk nicht im Fingerprint.
- **Fix:** Feld hinzufügen.

#### P1-12 | Rollback: restoredPaths.Add ausserhalb Guard

- **IDs:** F-029
- **Stelle:** `AuditCsvStore.cs:177` — Add ausserhalb `if (!dryRun && File.Exists)`.
- **KU:** KU-7
- **Fix:** In Guard verschieben.

#### P1-13 | FileCategory.Unknown → "GAME"

- **IDs:** F-046
- **Stelle:** `RunOrchestrator.cs:494–498` — `_ => "GAME"` verschluckt Unknown.
- **KU:** KU-1
- **Fix:** Explizites Mapping mit "UNKNOWN" oder Skip.

#### P1-14 | Inkonsistente Feldnamen GUI/CLI/API

- **IDs:** F-005, F-007
- **Stelle:** Winners/Keep, Dupes/Move, Games/Groups — verschiedene Bezeichnungen.
- **KU:** KU-4
- **Fix:** Ein Glossar, ein Projection-Type.

#### P1-15 | KeepCount vs WinnerCount Report

- **IDs:** F-001
- **Stelle:** Report zeigt "KeepCount" das nicht immer WinnerCount entspricht.
- **KU:** KU-1
- **Fix:** Aus Projection lesen, nicht re-deriven.

#### P1-16 | Fehlende ReportEntry-Validierung

- **IDs:** F-004
- **Stelle:** Keine Summation-Assertion: `Keep + Move + Junk + Bios == Total`.
- **KU:** KU-8
- **Fix:** Invarianten-Check in BuildSummary.

---

### P2 — Relevante Mängel

| ID | Titel | KU | Original-IDs |
|---|---|---|---|
| P2-01 | SSE "completed" für cancelled | KU-5 | F-015 |
| P2-02 | Cancelled/Failed Runs hide Dashboard | KU-5 | F-025 |
| P2-03 | BIOS konkurriert mit GAME in Dedupe | KU-1 | F-049 |
| P2-04 | DatMatch silently skipped bei UNKNOWN Console | KU-1 | F-023/F-051 |
| P2-05 | Rollback-Detail: 7→1 Feld | KU-7 | F-050 |
| P2-06 | ConsoleSort bei Cancel: Moves ohne Audit | KU-7 | F-052 |
| P2-07 | Cancel-Sidecar ohne Phase-Info | KU-7 | F-054 |
| P2-08 | CanRollback ohne Audit-Datei-Check | KU-5 | F-042 |
| P2-09 | OperationResult mutable Collections | KU-2 | F-044 |
| P2-10 | InsightsEngine re-deriviert anders | KU-1 | F-033 |
| P2-11 | DedupeRate: ambiger Nenner | KU-1 | F-003 |
| P2-12 | DashJunk: zählt Candidates, nicht Entfernte | KU-1 | F-018 |
| P2-13 | Progress: 0→100 binär | KU-2 | F-019 |
| P2-14 | PopulateErrorSummary: fehlender Math.Min-Guard | KU-2 | F-039 |
| P2-15 | Conversion-Counts fehlen im Report | KU-4 | F-009 |
| P2-16 | Cancel-State fehlt im Report | KU-4 | F-010 |
| P2-17 | ConsoleSorter-Results nicht im Dashboard | KU-4 | F-034 |
| P2-18 | Standalone JUNK/BIOS: eigene Report-Logik | KU-1 | F-008 |

---

### P3 — Nachrangige Punkte

| ID | Titel | KU | Original-IDs |
|---|---|---|---|
| P3-01 | Zwei divergente Rollback-Implementierungen | KU-7 | F-053 |
| P3-02 | TotalFiles/Candidates redundant | KU-2 | F-021 |
| P3-03 | PhaseMetrics ItemsPerSec=0 bei aktiver Phase | KU-2 | F-035 |
| P3-04 | PipelineEngine/ApplicationServiceFacade/RunResultSummary toter Code | KU-8 | M-006 |

---

## 5. Zielbild / Soll-Modell

### 5.1 Single Source of Truth

```
RunOrchestrator.Execute()
    └─→ RunResultBuilder (mutable, intern)
          └─→ .Build()
                └─→ RunResult (sealed record, immutable)
                      └─→ RunProjection (sealed record)
                            ├─→ Dashboard liest
                            ├─→ CLI liest
                            ├─→ API liest
                            └─→ Report liest
```

**RunResult** enthält rohe Phase-Ergebnisse. **RunProjection** enthält alle abgeleiteten KPIs (HealthScore, DedupeRate, Games, ErrorCount). Berechnung einmal, an einer Stelle.

### 5.2 Event-/Audit-Modell

- Jede Phase schreibt eigene Audit-Rows mit Phase-Tag
- ConsoleSorter erhält IAuditStore-Injection, Action = "CONSOLE_SORT"
- Rollback versteht neue Actions
- Cancel-Sidecar enthält: abgebrochene Phase, Phase-Progress, Timestamp
- Sidecar schreibt `MoveResult.MoveCount` (tatsächlich), nicht `LoserCount` (geplant)

### 5.3 Kennzahlenmodell

| Kennzahl | Definition | Quelle |
|---|---|---|
| TotalFilesScanned | Anzahl eindeutiger Dateipfade nach Scan | RunResult |
| GroupCount | Anzahl Dedupe-Gruppen | RunResult |
| GameCount | Gruppen mit >= 1 GAME-Candidate | RunProjection |
| WinnerCount | Ausgewählte Winner | RunResult |
| LoserCount | Identifizierte Duplikate | RunResult |
| DedupeMoveCount | Tatsächlich verschobene Dupes | DedupeMoveResult.MoveCount |
| JunkMoveCount | Tatsächlich entfernter Junk | JunkMoveResult.MoveCount |
| ConvertedCount | Erfolgreich konvertiert UND verifiziert | RunResult |
| ConvertErrorCount | Konvertierung oder Verifikation fehlgeschlagen | RunResult |
| DatVerifiedCount | DAT-geprüft mit Match | RunProjection |
| DatSkippedCount | Nicht DAT-prüfbar (UNKNOWN Console) | RunProjection |
| HealthScore | `FeatureService.CalculateHealthScore(...)` | RunProjection |
| DedupeRate | `LoserCount / (WinnerCount + LoserCount)` | RunProjection |
| SkipCount (neu) | ConflictPolicy=Skip übersprungen | RunResult |

### 5.4 Statusmodell

```csharp
enum RunOutcome {
    Completed,          // 0 Fehler
    PartialFailure,     // >0 Move/Convert-Fehler, aber Run lief durch
    Failed,             // Pipeline-Exception
    Cancelled,          // User-Abbruch
    Blocked             // Preflight gescheitert
}
```

Ableitung:

```
if (ExitCode == 3) → Blocked
if (Cancelled) → Cancelled
if (ExitCode != 0) → Failed
if (MoveFailCount + ConvertErrorCount > 0) → PartialFailure
else → Completed
```

### 5.5 Aggregation / Projection

- **RunResult**: rohe Phasenzahlen, immutable nach Build
- **RunProjection**: alle abgeleiteten Werte, berechnet einmal durch `ProjectionFactory.Create(RunResult)`
- **ReportSummary**: reads from RunProjection
- **ApiRunResult**: reads from RunProjection
- **Dashboard**: reads from RunProjection
- **CLI Output**: reads from RunProjection

Keine eigenen Berechnungen in Consumern. Kein `AllCandidates.Count(c => ...)` in Views.

### 5.6 Logging-Modell

- JSONL mit CorrelationId, Phase, Module, Action (existiert bereits)
- Audit-CSV pro Phase-Move mit Action-Type-Unterscheidung
- Cancel-Sidecar erweitert um: `CancelledInPhase`, `PhaseProgress`
- Rollback-Log propagiert alle 7 AuditRollbackResult-Felder
- ConsoleSorter-Moves erhalten Audit-Trail

### 5.7 Report-/Dashboard-Modell

- Report + Dashboard lesen aus RunProjection
- Modusbezogene Sichtbarkeit: ConvertOnly → nur Convert-KPIs sichtbar
- Cancel/Failed Runs: Dashboard zeigt Status-Banner statt stale Zahlen
- Post-Rollback: Dashboard reset auf "–" oder "Rollback ausgeführt"
- Report enthält: RunOutcome, ConvertedCount, ConvertErrorCount, SkipCount

---

## 6. Umsetzungsreihenfolge

### Phase 0 — Sofortmassnahmen (1-2 Tage)

**Ziel:** Datenverlust stoppen, toten Code entfernen.

| Aufgabe | Finding |
|---|---|
| Convert-Pipeline: Verify VOR Increment, Source nur bei Success löschen | P0-A |
| `Status`-Ableitung: "partial" bei FailCount>0 | P1-01 |
| Toten Code löschen: PipelineEngine (display-only beibehalten), ApplicationServiceFacade, RunResultSummary | P3-04, M-006 |
| `RemoveJunk` in Fingerprint aufnehmen | P1-11 |

**Exit-Kriterium:** Kein Datenverlust durch Convert-Verify-Failure reproduzierbar. Status spiegelt Fehlerstand wider.

### Phase 1 — Basiszahlen stabilisieren (2-3 Tage)

**Ziel:** Alle Inputs für Kennzahlen sind korrekt.

| Aufgabe | Finding |
|---|---|
| Overlapping-Roots-Dedup in ScanFiles | P0-D |
| FileCategory.Unknown explizit behandeln | P1-13 |
| ConflictPolicy Skip-Counter einführen | P1-05 |
| MovePhaseResult splitten (Junk/Dedupe) | P1-02 |
| CLI Sidecar: MoveCount statt LoserCount | P1-04 |

**Exit-Kriterium:** `TotalFilesScanned` == `len(distinct(AllCandidates.MainPath))`. Junk und Dedupe getrennt zählbar.

### Phase 2 — Wahrheitsmodell (3-4 Tage)

**Ziel:** Single Source of Truth etabliert.

| Aufgabe | Finding |
|---|---|
| RunResult immutable machen (Builder-Pattern) | KU-2 |
| RunProjection einführen (zentrale KPI-Berechnung) | KU-1 |
| HealthScore konsolidieren auf FeatureService | P0-B |
| "Games"-Definition vereinheitlichen | P0-C |
| ApplyRunResult/CLI/API/Report: nur aus RunProjection lesen | KU-4 |

**Exit-Kriterium:** Alle 4 Output-Channels (GUI/CLI/API/Report) zeigen identische Werte für identischen RunResult.

### Phase 3 — Statusmodell + Dashboard (2-3 Tage)

**Ziel:** Status, Cancel und Dashboard-Lifecycle korrekt.

| Aufgabe | Finding |
|---|---|
| RunOutcome-Enum einführen | KU-5 |
| Cancel→Failed Crash fixen | P1-07 |
| ApplyRunResult erst nach Cancel-Check | P1-08 |
| Dashboard-Reset bei Run-Start und Rollback | P1-09 |
| ConvertOnly: nur relevante KPIs zeigen | P1-10 |
| SSE: korrektes Event für Cancel | P2-01 |
| Cancelled/Failed Dashboard-Handling | P2-02 |

**Exit-Kriterium:** Cancel produziert nie "completed"/"ok". Dashboard zeigt nie stale Werte.

### Phase 4 — Audit + Forensik härten (2-3 Tage)

**Ziel:** Jede Zahl ist auf Audit-Records zurückführbar.

| Aufgabe | Finding |
|---|---|
| ConsoleSorter Audit-Integration | P1-06 |
| Rollback restoredPaths.Add Bug fixen | P1-12 |
| AuditCsvStore.Rollback → delegieren an AuditSigningService | P3-01 |
| Rollback-Detail vollständig loggen (7 Felder) | P2-05 |
| Cancel-Sidecar: Phase-Info | P2-07 |
| CanRollback mit File.Exists prüfen | P2-08 |

**Exit-Kriterium:** Audit-CSV + Sidecar reichen aus, jeden Move forensisch zu rekonstruieren.

### Phase 5 — Invarianten-Tests (fortlaufend, 3-5 Tage)

**Ziel:** Regressions-Schutz für alle Fixes.

| Aufgabe | Details |
|---|---|
| Summen-Invarianten | `Keep + Move + Skip + Fail == Total` |
| Cross-Output-Parity | GUI == CLI == API == Report für alle KPIs |
| Cancel/Partial Tests | Cancel zu jedem Phase-Zeitpunkt |
| Overlapping-Roots Test | Parent+Child Roots |
| HealthScore-Parity | All consumers produce identical score |
| Convert-Verify Tests | Verify-Failure → no data loss, no double count |

**Exit-Kriterium:** Alle Invarianten-Tests sind green.

### Phase 6 — Freigabe (1-2 Tage)

**Ziel:** Release-Readiness bestätigt.

| Aufgabe |
|---|
| Full Test-Suite green |
| Manueller E2E-Test: DryRun → Move → Rollback → Re-Run |
| Audit-Trail-Reconstruction für 3 verschiedene Szenarien |
| Parity-Check: GUI/CLI/API für gleichen Input |
| Cancelled Run: korrekte Status-Propagation auf allen Channels |

**Exit-Kriterium:** Alle Freigabebedingungen (Abschnitt 9) erfüllt.

---

## 7. Sofort wichtigste 10 Massnahmen

| # | P | Titel | Warum jetzt | Effekt |
|---|---|---|---|---|
| 1 | P0 | Convert: Verify VOR Increment + kein Source-Delete bei Failure | **Datenverlust** | Eliminiert KU-3 komplett |
| 2 | P0 | Overlapping-Roots-Dedup in ScanFiles | Alle Zahlen inflationiert | Korrekte Basiszahlen |
| 3 | P0 | HealthScore auf eine Formel konsolidieren | 3 verschiedene Werte | Vertrauenswürdige KPI |
| 4 | P0 | "Games"-Definition vereinheitlichen | GUI/CLI zeigen Verschiedenes | Konsistente Darstellung |
| 5 | P1 | Status-Ableitung: "partial" bei Fehlern | "ok" trotz 50 Failures | Korrektes Statusbild |
| 6 | P1 | MovePhaseResult split (Junk/Dedupe) | Zahlen nicht trennbar | Forensische Nachvollziehbarkeit |
| 7 | P1 | Cancel-Flow: ApplyRunResult nach Check | Stale partial Daten im Dashboard | Korrekte UI nach Cancel |
| 8 | P1 | CLI Sidecar: MoveCount statt LoserCount | Audit-Integrität verletzt | Forensisch belastbar |
| 9 | P1 | Toten Code entfernen | Verwirrung bei Refactoring (M-006) | Klare Architektur |
| 10 | P1 | Invarianten-Tests schreiben | Kein Regressions-Schutz | Absicherung aller Fixes |

---

## 8. Test- und Verifikationspaket

### Unit-Tests

| Test | Prüft | Finding |
|---|---|---|
| Convert: Verify-Failure → kein Increment, Source bleibt | Summeninvariante | P0-A |
| HealthScore: alle 3 Stellen → gleicher Wert | Formula-Parity | P0-B |
| FileCategory.Unknown → nicht "GAME" | Kategorisierungsinvariante | P1-13 |
| ConflictPolicy Skip → SkipCount++ | Zähler-Vollständigkeit | P1-05 |
| Status = "partial" wenn FailCount > 0 | Status-Ableitung | P1-01 |
| MovePhaseResult: Junk und Dedupe separat | Trennungsinvariante | P1-02 |
| DedupeEngine: gleicher MainPath → kein Loser | Idempotenz | P0-D |

### Integrationstests

| Test | Prüft |
|---|---|
| Overlapping Roots: parent + child → dedup | P0-D |
| Full Pipeline: Scan→Dedupe→Junk→Move→Convert→Report | End-to-End Invarianten |
| ConvertOnly-Run → nur Convert-KPIs im Dashboard | P1-10 |
| Cancel bei jeder Phase → korrekter Status + keine stale Werte | P1-07/P1-08 |
| Move → Rollback → Dashboard reset | P1-09 |

### Cross-Output-Konsistenztests

| Test | Prüft |
|---|---|
| CLI == WPF == API: **alle** numerischen Felder | P1-14, M-005 |
| ReportSummary == RunProjection (erweiterte ReportParityTests) | P1-15/P1-16 |
| API: alle RunResult-Felder vorhanden in ApiRunResult | P1-03 |

### Retry-/Resume-/Cancel-Tests

| Test | Prüft |
|---|---|
| Cancel vor Scan → GroupCount=0, Status=Cancelled | P1-08 |
| Cancel nach Dedupe, vor Move → Groups populated, MoveResult=null | P1-08 |
| Cancel während Move → partial MoveResult, Status=Cancelled | P2-06 |
| Cancel während ConsoleSort → Audit-Trail für bisherige Sort-Moves | P2-06 |
| Re-Run via WatchService → Dashboard-Reset | P1-09 |

### Snapshot-/Golden-File-Tests

| Test | Prüft |
|---|---|
| HTML-Report: bekannter Input → deterministischer Output | Report-Determinismus |
| Audit-CSV: bekannte Moves → exakte Rows | Audit-Vollständigkeit |
| Audit-Sidecar: MoveCount == Audit-Row-Count | Konsistenz |

---

## 9. Freigabebedingungen

Messbare, nicht verhandelbare Kriterien:

1. **Jede finale Summary-Zahl ist auf Audit-Records oder AllCandidates zurückführbar.** Summe aller Phase-Counts == TotalFilesScanned.
2. **GUI, CLI, API und Report verwenden dieselbe Berechnung** für HealthScore, Games, ErrorCount, DedupeRate.
3. **`converted + convertErrors + convertSkipped == conversion-attempts`.** Keine Doppelzählung.
4. **Verify-Failure löscht nie die Quelldatei.** Assertion: Source exists after verify failure.
5. **Status "ok" nur wenn MoveFailCount == 0 UND ConvertErrorCount == 0.**
6. **Cancelled Runs produzieren nie Status "ok", "completed", oder ExitCode 0.**
7. **Dashboard zeigt nie Werte eines vorherigen Runs** während oder nach einem neuen Run/Cancel/Rollback.
8. **`MoveResult.MoveCount + MoveResult.FailCount == attempted moves`.** Kein stiller Skip.
9. **Junk-Moves und Dedupe-Moves sind in RunResult separat abfragbar.**
10. **Audit-CSV enthält Rows für alle physischen Dateibewegungen** — inklusive ConsoleSorter.
11. **Keine zwei Candidates in AllCandidates mit identischem MainPath.**
12. **Alle numerischen Felder in ReportParityTests verglichen** — nicht nur 4 von 15.

---

## 10. Offene Risiken / Annahmen / Restunsicherheit

| # | Risiko | Einstufung | Bemerkung |
|---|---|---|---|
| R-01 | **BIOS + GAME gleicher GameKey**: Dedupe entfernt GAME zugunsten BIOS | mittel | Tritt nur bei fehlerhafter GameKey-Normalisierung auf. Keine Produktivdaten zur Häufigkeitsbewertung. |
| R-02 | **RuleEngine static Regex-Cache**: stale Regeln nach Profilwechsel in WPF | niedrig-mittel | Nur relevant bei identischen Rule-Keys mit geändertem Pattern. Kein Test für dieses Szenario. |
| R-03 | **API hat bauartbedingt keine DAT-Verification** | mittel | RunManager injiziert weder hashService noch datIndex. Alle DAT-bezogenen Fixes wirken nicht auf API. Parity-Test würde fehlschlagen oder DAT-Felder explizit excluden. |
| R-04 | **RunResult-Immutability: Migration-Aufwand** | hoch | Builder-Pattern ändert Orchestrator-Interna und alle 6 Phasen. Muss atomar mit Phase 2 implementiert werden, da inkrementelle Migration inkonsistente Zustände erzeugt. |
| R-05 | **ConsoleSorter Audit ist kein trivialer Fix** | mittel | Erfordert neuen Audit-Action-Type, Rollback-Erweiterung, Konstruktor-Änderung in 3+ Stellen. |
| R-06 | **Unquantifiziertes Produktionsrisiko bei Overlapping Roots** | niedrig | Unklar wie häufig User überlappende Roots konfigurieren. Preflight-Warnung könnte als Sofortmassnahme reichen. |
| R-07 | **Toter Code PipelineEngine wird als UI-Feature-Command genutzt** | niedrig | Display-only (zeigt Pipeline-Steps). Löschung muss den Display-Path beibehalten. |

---

## 11. Schlussentscheidung

### Was ist heute kaputt oder unzuverlässig?

**Kaputt:**
- Convert-Pipeline: Doppelzählung + Datenverlust (P0-A)
- Status "ok" trotz n Fehler (P1-01)
- Cancel→Failed State-Crash (P1-07)

**Unzuverlässig:**
- Jede Zahl die von TotalFilesScanned oder WinnerCount abgeleitet ist (P0-D: Overlapping Roots)
- HealthScore (P0-B: drei verschiedene Werte)
- "Games" (P0-C: zwei verschiedene Definitionen)
- MoveCount/FailCount/SavedBytes (P1-02: Junk+Dedupe vermischt)
- Dashboard nach Cancel/Rollback/Re-Run (P1-08/P1-09)
- Gesamter ErrorCount (P1-02: Move + Convert zusammengeworfen)

### Was muss zuerst stabilisiert werden?

**Phase 0** (Datenverlust stoppen): Convert-Verify-Fix, Status-Ableitung, Fingerprint-Fix, toter Code weg.
**Phase 1** (Basiszahlen): Overlapping-Roots-Dedup, MovePhaseResult-Split, CLI-Sidecar.

### Ist das Zielmodell überzeugend?

**Ja, mit Bedingungen.** Das RunResult→RunProjection-Pattern ist solide. Die Risiken liegen in der Migration (R-04: atomarer Umbau nötig) und in der bauartbedingten API-Lücke (R-03: keine DAT-Verification). Das Zielbild löst alle 8 Kernursachen.

### Ist der Umbau realistisch?

**Ja.** Phase 0+1 (Sofort + Basiszahlen) sind in 3-5 Tagen machbar und eliminieren alle P0s. Phase 2 (Wahrheitsmodell) ist der grösste Einzelaufwand (3-4 Tage), aber architekturell klar. Gesamtumfang: **~15-20 Arbeitstage** für Phase 0-5.

### Ab wann kann man Zahlen, Reports und Logs wieder vertrauen?

- **Nach Phase 0:** Convert-Pipeline vertrauenswürdig. Status belastbar.
- **Nach Phase 1:** Basiszahlen korrekt. Junk/Dedupe trennbar.
- **Nach Phase 2:** Alle Zahlen auf allen Channels konsistent.
- **Nach Phase 5:** Durch Invarianten-Tests bewiesen.

### Die 5 wichtigsten Punkte jetzt

1. **Convert-Verify-Fix** — Datenverlust ist ein Release-Blocker. Sofort.
2. **Overlapping-Roots-Dedup** — Alle Folge-Fixes arbeiten sonst auf falschen Basiszahlen.
3. **HealthScore konsolidieren** — Sichtbarstes Symptom für den User.
4. **MovePhaseResult splitten** — Voraussetzung für korrekte ErrorCounts, PhaseMetrics, Sidecar.
5. **Invarianten-Tests** — Ohne Tests ist jeder Fix ein Glücksspiel.

---

## Anhang: Finding-Kreuzreferenz

| Konsolidiert | Original-IDs |
|---|---|
| P0-A | F-026, F-027, F-032 |
| P0-B | F-002, F-011 |
| P0-C | F-012 |
| P0-D | F-045 |
| P1-01 | F-014 |
| P1-02 | F-017, F-041, F-022, F-024, F-043 |
| P1-03 | F-016 |
| P1-04 | F-031, F-040 |
| P1-05 | F-013 |
| P1-06 | F-028, F-030 |
| P1-07 | F-020 |
| P1-08 | F-048 |
| P1-09 | F-036, F-047 |
| P1-10 | F-037 |
| P1-11 | F-038 |
| P1-12 | F-029 |
| P1-13 | F-046 |
| P1-14 | F-005, F-007 |
| P1-15 | F-001 |
| P1-16 | F-004 |
| P2-01 | F-015 |
| P2-02 | F-025 |
| P2-03 | F-049 |
| P2-04 | F-023, F-051 |
| P2-05 | F-050 |
| P2-06 | F-052 |
| P2-07 | F-054 |
| P2-08 | F-042 |
| P2-09 | F-044 |
| P2-10 | F-033 |
| P2-11 | F-003 |
| P2-12 | F-018 |
| P2-13 | F-019 |
| P2-14 | F-039 |
| P2-15 | F-009 |
| P2-16 | F-010 |
| P2-17 | F-034 |
| P2-18 | F-008 |
| P3-01 | F-053 |
| P3-02 | F-021 |
| P3-03 | F-035 |
| P3-04 | M-006 |

> **54 Original-Findings → 42 konsolidierte Findings** (12 Dubletten entfernt)
> **4 P0 | 16 P1 | 18 P2 | 4 P3**
> **8 Kernursachen | 6 Umsetzungsphasen | 12 Freigabebedingungen | 7 offene Risiken**
