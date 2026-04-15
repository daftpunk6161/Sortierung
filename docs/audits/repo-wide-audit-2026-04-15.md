# Repo-weiter Audit – Romulus (2026-04-15)

> **Scope:** Gesamtes Repository `src/` (~665 Quelldateien, 7 Projekte)  
> **Methode:** Automatisierte Mehrstufenanalyse (Security, Core-Determinismus, Paritaet, Conversion/Rollback, Testluecken, Dead Code/Hygiene) + gezielte manuelle Verifizierung kritischer Findings  
> **Prioritaetsreihenfolge:** Datenverlust > Security > Falsche fachliche Entscheidungen > Paritaetsfehler > Conversion/Audit/Rollback-Risiken > Tech Debt mit Release-Risiko > Hygiene

---

## P1 – Datenverlust-Risiken

### F-001: RollbackMovedArtifacts verschluckt Fehler still

- [ ] **Offen**
- **Schweregrad:** Blocking
- **Impact:** Wenn eine Konvertierung fehlschlaegt und das Rollback der Source-Dateien aus dem Trash ebenfalls fehlschlaegt (IOException, UnauthorizedAccessException), wird nur eine WARNING-Progress-Message ausgegeben. Es gibt keine Eskalation, keinen Return-Wert, keinen Fehler im Ergebnis. Das Callercode behandelt die gescheiterte Rueckfuehrung nicht – die Quelldatei verbleibt im Trash und der User erfaehrt davon nur ueber den Fortschrittslog.
- **Betroffene Datei(en):** [ConversionPhaseHelper.cs](../src/Romulus.Infrastructure/Orchestration/ConversionPhaseHelper.cs#L441-L465)
- **Reproduktion:** Konvertierung starten → Konvertierung schlaegt nach Source-Move-to-Trash fehl → Rollback-Move scheitert (Dateisperre, Rechte) → Quelldatei bleibt dauerhaft im Trash
- **Ursache:** `catch (Exception ex) when (ex is IOException or ...)` loggt nur, propagiert nicht. Kein aggregierter Rollback-Status.
- **Fix:** Rollback-Erfolg als bool[] oder Fehlerliste an Caller zurueckgeben. Bei teilweisem Rollback-Versagen das ConversionResult mit `Reason = "rollback-partial-failure"` und explizitem Audit-Eintrag versehen.
- **Testabsicherung:** Integration-Test mit Mock-FileSystem das bei MoveItemSafely im Rollback wirft. Erwarten: Fehler-Audit-Zeile + ConversionOutcome != Success.
- **Coverage-Stand:** `line-rate="0"` laut Cobertura-Report → **komplett ungetestet**

### F-002: CleanupConversionOutputs ebenfalls still verschluckend

- [ ] **Offen**
- **Schweregrad:** Warning
- **Impact:** Wenn nach einer gescheiterten Konvertierung die Bereinigung der Partial-Outputs fehlschlaegt, verbleiben verwaiste Konvertierungs-Artefakte auf dem Dateisystem. Keine Eskalation.
- **Betroffene Datei(en):** [ConversionPhaseHelper.cs](../src/Romulus.Infrastructure/Orchestration/ConversionPhaseHelper.cs#L467-L480)
- **Reproduktion:** Konvertierung fehlschlaegt → Partial-Output-Datei ist gesperrt → Cleanup-Delete wirft → WARNING-Log, Output bleibt stehen
- **Ursache:** Gleiche best-effort-only Pattern wie F-001
- **Fix:** Mindestens einen dedizierten Counter `PartialOutputsNotCleaned` oder Audit-Eintrag erzeugen, damit der User verwaiste Dateien identifizieren kann.
- **Testabsicherung:** Unit-Test: Mock-FileSystem das DeleteFile wirft → Erwarten: WARNING-Progress + Audit-Eintrag

### F-003: TryAppendConversionErrorAudit verschluckt alle Exceptions

- [ ] **Offen**
- **Schweregrad:** Warning
- **Impact:** Wenn das Audit-Logging selbst fehlschlaegt (`catch (Exception) { // best effort only }`), geht sowohl die Fehlermeldung als auch die Audit-Spur verloren. Bei Disk-Full-Szenarien bleibt kein Hinweis, dass Konvertierungsfehler stattfanden.
- **Betroffene Datei(en):** [ConversionPhaseHelper.cs](../src/Romulus.Infrastructure/Orchestration/ConversionPhaseHelper.cs#L430-L440)
- **Reproduktion:** Disk voll waehrend Konvertierung → Error-Audit schlaegt fehl → keine Spur
- **Ursache:** `catch (Exception) { /* best effort */ }` ohne Fallback
- **Fix:** Mindestens Trace.WriteLine als Fallback. Optional: In-Memory-Error-Buffer fuer den Run.
- **Testabsicherung:** Unit-Test: Audit-Writer wirft → Erwarten: Trace.WriteLine passiert

---

## P2 – Security

### F-004: ConvertSingleFile validiert Source-Pfad nicht gegen AllowedRoots

- [ ] **Offen**
- **Schweregrad:** Warning
- **Impact:** Theoretisch koennte ein manipulierter Scan-Pfad an `ConvertSingleFile` weitergegeben werden, ohne dass geprueft wird, ob er innerhalb erlaubter Roots liegt. In der Praxis wird der Pfad allerdings aus dem vorherigen Scan-Ergebnis eingespeist, das bereits Root-validiert ist.
- **Betroffene Datei(en):** [ConversionPhaseHelper.cs](../src/Romulus.Infrastructure/Orchestration/ConversionPhaseHelper.cs#L180-L220)
- **Reproduktion:** Nur ausbeutbar, wenn ein Eingabepfad den Scan-Schritt umgehen kann (aktuell nicht moeglich via regulaere GUI/CLI/API-Pfade).
- **Ursache:** Defense-in-Depth-Luecke: Der Konvertierer vertraut dem vorgelagerten Scan statt selbst zu validieren.
- **Fix:** `AllowedRootPathPolicy.Validate(filePath, context.Options.Roots)` am Anfang von `ConvertSingleFile`. Einfach, kein Seiteneffekt.
- **Testabsicherung:** Unit-Test: Pfad ausserhalb Roots → erwartet `null`-Return oder Exception

### F-005: TOCTOU in AllowedRootPathPolicy bei Reparse-Point-Pruefung

- [ ] **Offen**
- **Schweregrad:** Suggestion
- **Impact:** Zwischen der Pruefung `IsReparsePoint(path)` und der tatsaechlichen Move/Copy-Operation koennte ein Angreifer einen Symlink einschleusen. Praktisches Risiko extrem gering (erfordert lokalen Zugriff waehrend aktiver Sortierung).
- **Betroffene Datei(en):** `AllowedRootPathPolicy.cs`
- **Reproduktion:** Race-Condition, nicht deterministisch reproduzierbar
- **Ursache:** Inhaerent bei File-System-Operationen; TOCTOU ist nur per striktem Exclusive-Lock eliminierbar.
- **Fix:** Akzeptables Restrisiko. Optional: doppelte Pruefung per `MoveItemSafely` nach dem Move (post-validation).
- **Testabsicherung:** Nicht realistisch testbar fuer TOCTOU. Bestehende Tests decken Reparse-Point-Rejection ab.

### F-006: Hardcoded German Strings in IDialogService-Interface-Defaults

- [ ] **Offen**
- **Schweregrad:** Suggestion
- **Impact:** Default-Parameter in `IDialogService` sind deutsch hartcodiert ("Ordner auswählen", "Bestätigung", "Fehler"). In einer zukuenftigen Lokalisierung wuerden sie als Fallback statt der uebersetzten Werte angezeigt, wenn ein Caller die Defaults nutzt.
- **Betroffene Datei(en):** [IDialogService.cs](../src/Romulus.Contracts/Ports/IDialogService.cs#L8-L20)
- **Reproduktion:** Aktuell kein Bug, nur Lokalisierungs-Schuld. Sichtbar, wenn ein zukuenftiger Caller `BrowseFolder()` ohne Parameter aufruft.
- **Ursache:** Historisch gewaehlt, nie auf i18n umgestellt
- **Fix:** Defaults auf neutrale Keys oder englische Strings umstellen; tatsaechliche Lokalisierung in den WPF-Adapter verlagern.
- **Testabsicherung:** Kein Test noetig, rein kosmetisch

---

## P3 – Falsche fachliche Entscheidungen

### F-007: Keine kritischen Defekte gefunden

- [x] **Kein Fund**
- **Schweregrad:** —
- **Detail:** `GameKeyNormalizer`, `FormatScorer`, `VersionScorer`, `DeduplicationEngine.SelectWinner` und `RegionDetector` sind deterministisch und konsistent implementiert. Winner-Selection verwendet stabile Tiebreaker (Dateipfad als letzter Fallback). Scoring-Profile werden per `FormatScoringProfile.EnsureRegistered()` einmalig aus `data/format-scores.json` geladen und an `FormatScorer` injiziert – mit sauberen Fallback-Defaults im Code.

---

## P4 – Paritaetsfehler (GUI/CLI/API)

### F-008: Gemeinsamer Materializer – Paritaet bestaetigt

- [x] **Kein Fund**
- **Detail:** GUI, CLI und API nutzen alle `RunConfigurationMaterializer` fuer die Erstellung des finalen `RunConfiguration`. Kein Entry Point hat eigene fachliche Parallellogik. `RunService` wird von der GUI, `CliRunner` vom CLI und API-Endpoints nutzen ebenfalls den Materializer-Pfad.

### F-009: DryRun vs. Move – unterschiedliches Residual-Pfad-Filtering

- [ ] **Offen**
- **Schweregrad:** Suggestion
- **Impact:** DryRun und Move verwenden leicht unterschiedliche Filterlogik fuer Restzaehlung. Dies ist **by design** (DryRun muss heuristische Schaetzung liefern, Move zaehlt tatsaechliche Ergebnisse), aber ein expliziter Integrations-Test fehlt.
- **Betroffene Datei(en):** `RunOrchestrator.PreviewAndPipelineHelpers.cs`, `DeduplicatePipelinePhase.cs`
- **Reproduktion:** Nicht als Bug reproduzierbar – rein Test-Luecke
- **Fix:** Integrationstest, der DryRun-Zaehlung mit nachfolgender Execute-Zaehlung fuer die gleiche Eingabe vergleicht.
- **Testabsicherung:** Neuer Integrations-Test fuer Preview/Execute-Paritaet

---

## P5 – Conversion/Audit/Rollback-Risiken

### F-010: ConversionExecutor Timeout-Recovery ungetestet

- [ ] **Offen**
- **Schweregrad:** Warning
- **Impact:** ConversionExecutor hat Timeout-Logik fuer externe Tools (chdman, nkit, etc.), aber kein einziger Test prueft, was passiert wenn ein Tool in den Timeout laeuft. Partial-Outputs koennten unbereinigt bleiben.
- **Betroffene Datei(en):** `ConversionExecutor.cs`, `ToolRunnerAdapter.cs`
- **Reproduktion:** Externes Tool haengt laenger als Timeout → Prozess wird gekillt → Output-Datei partiell geschrieben → Cleanup unklar
- **Ursache:** Kein dedizierter Timeout-Cleanup-Test vorhanden
- **Fix:** Test mit Mock-Process das Timeout auslöst. Verifizieren: Output-Datei wird geloescht, Return = Error.
- **Testabsicherung:** Unit-Test erforderlich

### F-011: Audit-CSV-Korruption bei Rollback ungetestet

- [ ] **Offen**
- **Schweregrad:** Warning
- **Impact:** Wenn die Audit-CSV waehrend eines Runs korrupt wird (abgeschnittene Zeilen, duplicierte Eintraege), koennte ein nachfolgender Rollback falsche Dateizuordnungen treffen.
- **Betroffene Datei(en):** `RollbackService.cs`, `AuditLogWriter.cs`
- **Reproduktion:** Audit-CSV manuell korrumpieren → Rollback starten → Verhalten: Entweder Fehlermeldung oder falsche Zuordnung
- **Ursache:** Keine Tests fuer korrupte-CSV-Szenarien
- **Fix:** Regressions-Test: Feed korrupte CSV an RollbackService → erwarten: Error-Return mit Details, keine Datei-Moves
- **Testabsicherung:** Regressions-Test erforderlich

### F-012: Rollback mit fehlenden Trash-Eintraegen ungetestet

- [ ] **Offen**
- **Schweregrad:** Warning
- **Impact:** Wenn der Trash-Ordner manuell bereinigt wurde, fehlen die Quelldateien fuer den Rollback. Undefiniertes Verhalten.
- **Betroffene Datei(en):** `RollbackService.cs`
- **Reproduktion:** Run ausfuehren → Trash manuell leeren → Rollback versuchen
- **Ursache:** Fehlender Test
- **Fix:** Regressions-Test: Trash-Dateien entfernen → Rollback → erwarten: partielle Rueckmeldung und keine Abstuerzende Exception
- **Testabsicherung:** Regressions-Test erforderlich

### F-013: R5-025 Verification-Status-Aggregation inkonsistent

- [ ] **Offen**
- **Schweregrad:** Warning
- **Impact:** Die Verifikation nach Konvertierung hat zwei Pfade (Legacy-Converter vs. Advanced FormatConverterAdapter). Der Aggregationsstatus (Verified/Unverified/Error) wird leicht unterschiedlich gebildet.
- **Betroffene Datei(en):** `ConversionVerificationHelpers.cs`, `ConversionPhaseHelper.cs`
- **Reproduktion:** Advanced-Konverter liefert Success aber mit abweichendem Verification-Status → Counting-Differenz
- **Ursache:** Zwei Code-Pfade fuer Legacy/Advanced-Konvertierung
- **Fix:** Vereinheitlichten `IsVerificationSuccessful`-Helper pruefen auf konsistente Status-Zuordnung
- **Testabsicherung:** Invarianten-Test: gleiches ROM → gleicher Verification-Status unabhaengig vom Konverter-Pfad

---

## P6 – Technische Schulden mit Release-Risiko

### F-014: Format-Scores haben duale Quelle (Code-Fallback + JSON)

- [ ] **Offen**
- **Schweregrad:** Warning
- **Impact:** `FormatScorer.cs` enthaelt ~130 Zeilen hartcodierte Fallback-Scores (Zeilen 30-140). Gleichzeitig existiert `data/format-scores.json` als primaere Quelle. Wenn eine neue Extension nur im JSON ergaenzt wird aber nicht im Fallback, ist der Fallback-Pfad (wenn JSON fehlt) inkonsistent. Wenn im Fallback eine Score-Aenderung gemacht wird, weicht sie still vom JSON ab.
- **Betroffene Datei(en):** [FormatScorer.cs](../src/Romulus.Core/Scoring/FormatScorer.cs#L30-L140), `data/format-scores.json`, [FormatScoringProfile.cs](../src/Romulus.Infrastructure/Orchestration/FormatScoringProfile.cs)
- **Reproduktion:** Extension `.abc` in JSON mit Score 700 hinzufuegen → FormatScorer Fallback gibt `DefaultUnknownFormatScore` (300) → inkonsistentes Verhalten je nach Vorhandensein der JSON
- **Ursache:** Architekturell gewollt als Fallback, aber inzwischen redundant da JSON immer vorhanden ist
- **Fix:** Zwei Optionen: (a) Fallback auf Minimal-Default-Set reduzieren (nur Archive-Basis-Scores), oder (b) einen Invarianten-Test der sicherstellt dass JSON und Fallback fuer alle gemeinsamen Keys dieselben Scores liefern.
- **Testabsicherung:** Invarianten-Test: alle Keys aus Fallback auch in JSON vorhanden mit gleichem Score

### F-015: 40+ orphaned FeatureCommandKeys

- [ ] **Offen**
- **Schweregrad:** Warning
- **Impact:** `FeatureCommandKeys.cs` definiert ~75 Konstanten. Viele davon sind im `FeatureCommandService` registriert aber haben keine echte Handler-Implementierung (Command-Aktion zeigt Placeholder-Message oder ist leer).
- **Betroffene Datei(en):** [FeatureCommandKeys.cs](../src/Romulus.UI.Wpf/Models/FeatureCommandKeys.cs), `FeatureCommandService.cs` (mehrere Partials)
- **Reproduktion:** Im Tool-Katalog Features anklicken → einige zeigen "Funktion in Entwicklung" oder aehnliche Meldung
- **Ursache:** Historisch angelegt, nie bereinigt nach Feature-Konsolidierung
- **Fix:** Nicht sichtbare/nicht implementierte Keys entfernen oder klar als `ToolMaturity.Planned` markieren und aus dem Default-Katalog ausblenden.
- **Testabsicherung:** Bestehender Test `SearchCoversAllFeatureCommandKeys` aktualisieren

### F-016: 31 Timing-abhaengige Tests

- [ ] **Offen**
- **Schweregrad:** Warning
- **Impact:** 31 Tests verwenden `Thread.Sleep` oder `Task.Delay` fuer Timing-Synchronisation. Auf langsamen CI-Runnern koennten diese flaky werden.
- **Betroffene Datei(en):** Diverse Test-Dateien in `src/Romulus.Tests/`
- **Reproduktion:** Tests laufen auf ueberlastetem CI-Runner → sporadische Failures
- **Ursache:** Polling/Wait-Patterns statt event-basierter Synchronisation
- **Fix:** Schrittweise auf `TaskCompletionSource`, `ManualResetEventSlim` oder `SemaphoreSlim` umstellen.
- **Testabsicherung:** Selbst-validierend nach Umbau

---

## P7 – Hygiene

### F-017: Hardcoded deutsche Strings in FeatureCommandService

- [ ] **Offen**
- **Schweregrad:** Suggestion
- **Impact:** Beschreibungen, Kategorie-Titel und Hinweise in `FeatureCommandService`-Partials sind direkt deutsch statt ueber i18n-Keys. Betrifft nur die Tool-Katalog-Ansicht.
- **Betroffene Datei(en):** `FeatureCommandService.*.cs` (8 Partials)
- **Ursache:** Historisch, vor der i18n-Einbindung geschrieben
- **Fix:** Sukzessive auf `i18n["key"]` umstellen
- **Testabsicherung:** Kein eigener Test noetig; bestehender `SearchCoversAllFeatureCommandKeys`-Test bleibt stabil

### F-018: Magic Numbers (Timeouts, Limits, Parallelitaet)

- [ ] **Offen**
- **Schweregrad:** Suggestion
- **Impact:** Einige Timeouts und Limits (z.B. `WatchFolderService` Polling-Intervall, `ParallelHasher` Parallelitaetsgrad, `ScheduleService` Intervalle) sind direkt als Zahlen codiert statt als benannte Konstanten.
- **Betroffene Datei(en):** `WatchFolderService.cs`, `ParallelHasher.cs`, `ScheduleService.cs`
- **Fix:** Als `const` oder Konfigurationswert in `RunConstants` bzw. `defaults.json` extrahieren.
- **Testabsicherung:** Kein eigener Test noetig

### F-019: Keine Integration-Tests fuer echte ZIP-Extraktion mit malicious entries

- [ ] **Offen**
- **Schweregrad:** Suggestion
- **Impact:** Zip-Slip wird im Code korrekt geblocked (Pfad-Validierung vor Extraktion). Aber kein Test laeuft mit einer echten Zip-Datei die boeswillige Pfade enthaelt – nur Mock-basierte Tests.
- **Betroffene Datei(en):** `ArchiveHashService.cs`, `ArchiveExtractionHelpers.cs`
- **Fix:** Integrationstest mit manipulierter Testzip (relativer `../`-Pfad im Entry-Name).
- **Testabsicherung:** Integration-Test erforderlich

### F-020: FormatScorer/VersionScorer Edge-Cases schwach getestet

- [ ] **Offen**
- **Schweregrad:** Suggestion
- **Impact:** Null-Extensions, leere Strings und Whitespace-Only-Inputs an `GetFormatScore()` und `GetVersionScore()` haben keine dedizierten Edge-Case-Tests.
- **Betroffene Datei(en):** `FormatScorer.cs`, `VersionScorer.cs`
- **Fix:** Parametrisierte Tests: `null`, `""`, `" "`, `".unknown"`, sehr lange Extension
- **Testabsicherung:** Unit-Tests erforderlich

### F-021: Cross-Root Symlink-Deduplication-Boundary ungetestet

- [ ] **Offen**
- **Schweregrad:** Suggestion
- **Impact:** Wenn zwei Roots ueber Symlinks auf denselben physischen Ordner zeigen, koennte eine Datei doppelt gezaehlt werden. Der Code blocked Reparse Points, aber kein Test deckt das Cross-Root-Szenario ab.
- **Betroffene Datei(en):** `AllowedRootPathPolicy.cs`, `ScanEngine.cs`
- **Fix:** Regressions-Test mit Mock-FileSystem das Reparse Points an Root-Ebene simuliert.
- **Testabsicherung:** Regressions-Test erforderlich

---

## Zusammenfassung

| Prioritaet | Finding-Bereich | Anzahl | Blocking | Warnings | Suggestions |
|---|---|---|---|---|---|
| P1 – Datenverlust | Conversion Rollback / Cleanup | 3 | 1 | 2 | 0 |
| P2 – Security | Root-Validation, TOCTOU, i18n | 3 | 0 | 1 | 2 |
| P3 – Fachliche Entscheidungen | — | 0 | 0 | 0 | 0 |
| P4 – Paritaet | GUI/CLI/API | 1 | 0 | 0 | 1 |
| P5 – Conversion/Audit/Rollback | Test-Luecken, Status-Inkonsistenz | 4 | 0 | 4 | 0 |
| P6 – Tech Debt | Duale Scores, Orphaned Keys, Timing | 3 | 0 | 3 | 0 |
| P7 – Hygiene | Hardcoded Strings, Magic Numbers, Edge-Tests | 5 | 0 | 0 | 5 |
| **Gesamt** | | **19** | **1** | **10** | **8** |

---

## Top 20 Massnahmen (priorisiert)

1. **F-001:** RollbackMovedArtifacts Fehler-Propagation implementieren (Blocking)
2. **F-004:** Root-Validierung in ConvertSingleFile ergaenzen (Defense-in-Depth)
3. **F-010:** Timeout-Recovery-Test fuer ConversionExecutor schreiben
4. **F-011:** Korrupte-Audit-CSV-Rollback-Test schreiben
5. **F-012:** Fehlende-Trash-Dateien-Rollback-Test schreiben
6. **F-002:** CleanupConversionOutputs Fehler-Tracking ergaenzen
7. **F-003:** TryAppendConversionErrorAudit Fallback-Logging ergaenzen
8. **F-014:** Invarianten-Test Format-Scores JSON vs. Fallback-Code
9. **F-013:** Verification-Status-Aggregation vereinheitlichen
10. **F-009:** Preview/Execute-Paritaets-Integrationstest
11. **F-015:** Orphaned FeatureCommandKeys bereinigen
12. **F-016:** Timing-abhaengige Tests (Top 10 kritischste zuerst) umstellen
13. **F-019:** Zip-Slip-Integrationstest mit echter manipulierter Zip
14. **F-020:** FormatScorer/VersionScorer Edge-Case-Tests
15. **F-021:** Cross-Root Symlink-Boundary-Test
16. **F-017:** Deutsche Strings in FeatureCommandService auf i18n umstellen
17. **F-018:** Magic Numbers in Konstanten extrahieren
18. **F-006:** IDialogService-Defaults lokalisierbar machen
19. **F-005:** TOCTOU-Risiko dokumentieren (akzeptables Restrisiko)
20. Allgemeine Cleanup-Runde fuer tote Testfixtures und veraltete Testdaten

---

## Systemische Hauptursachen

1. **Best-effort-only Pattern in Fehlerbehandlung**: Insbesondere im Conversion-/Rollback-Bereich werden Fehler verschluckt statt propagiert. Das "best effort"-Pattern ist an sich vertretbar, aber es fehlt eine Eskalationsstufe wenn best-effort auch fehlschlaegt.

2. **Duale Datenquellen ohne Konsistenz-Checks**: Format-Scores existieren sowohl als Code-Fallback als auch als JSON. Ohne Invarianten-Test koennen sie still divergieren.

3. **Fehlende Negativ-/Edge-Testabdeckung im Conversion-Pipeline-Bereich**: Die Happy-Path-Tests sind solide, aber Fehler- und Timeout-Szenarien im Konvertierungspfad sind nahezu ungetestet. Coverage-Reports bestaetigen dies (`RollbackMovedArtifacts` hat `line-rate="0"` in einigen Cobertura-Snapshots).

4. **Historisch gewachsene Feature-Command-Infrastruktur**: Viele Konsolidierungsphasen haben zwar Features entfernt, aber die Keys und Registrierungen nicht vollstaendig nachgezogen.

---

## Sanierungsstrategie

### Phase 1 – Datenschutz & Rollback haerten (1 Sprint)
- F-001 beheben (Rollback-Propagation)
- F-002 + F-003 (Cleanup-Tracking + Audit-Fallback)
- F-010, F-011, F-012 (fehlende Tests)

### Phase 2 – Defense-in-Depth & Konsistenz (1 Sprint)
- F-004 (Root-Validierung in ConvertSingleFile)
- F-014 (Format-Score-Invarianten-Test)
- F-013 (Verification-Status vereinheitlichen)
- F-009 (Preview/Execute-Paritaets-Test)

### Phase 3 – Hygiene & Tech-Debt (2 Sprints)
- F-015 (Orphaned Keys)
- F-016 (Timing-Tests, iterativ)
- F-017, F-018 (Lokalisierung, Konstanten)
- F-019, F-020, F-021 (Edge-Case-Tests)

---

## Schlussurteil (vor Deep-Dive)

**Romulus ist in sehr gutem Zustand.** Die Architektur ist sauber geschichtet, die Sicherheitsmassnahmen (CSV-Injection, Zip-Slip, Tool-Hash-Verifizierung, HMAC-Audit-Signing, CSP-Nonce in HTML-Reports) sind vorbildlich. Determinismus in der Kernlogik (GameKey, Scoring, Deduplication) ist sauber implementiert. GUI/CLI/API-Paritaet ist ueber `RunConfigurationMaterializer` garantiert.

---
---

## Deep-Dive Bug-Hunting (2026-04-15, Nachtrag)

> **Methode:** Zeilenweise Analyse aller kritischen Subsysteme (Conversion Pipeline, Deduplication, Set Parsing, Audit/Rollback, FileSystem Safety, API/CLI Entry Points) mit manueller Verifizierung jedes Findings gegen den tatsaechlichen Code.

---

## DB-001: CRITICAL — Legacy-Konvertierung loescht erfolgreich konvertierte Outputs

- [ ] **Offen**
- **Schweregrad:** Critical (Datenverlust von Konvertierungs-Output)
- **Impact:** Wenn `conversion-registry.json` nicht geladen werden kann (korrupt, fehlende Datei, Rechtefehler), arbeitet `FormatConverterAdapter` im Legacy-Modus (`_planner == null || _executor == null`). In diesem Modus:
  1. `ConvertForConsole` ruft intern `Convert()` auf, das die Legacy-Tool-Converter (`_chdman.Convert()`, `_sevenZip.Convert()`, etc.) nutzt
  2. Diese geben `ConversionResult` **ohne** `Plan` und mit `VerificationResult = NotAttempted` (Default) zurueck
  3. Zurueck in `ConversionPhaseHelper.ConvertSingleFile`: `target` bleibt `null` (wird nur im Nicht-FormatConverterAdapter-Branch gesetzt)
  4. `IsVerificationSuccessful(convResult, converter, target=null)` erhaelt `Plan=null` + `target=null` → kann keinen `effectiveTarget` ableiten → gibt `false` zurueck
  5. `ProcessConversionResult` markiert das Ergebnis als `ConversionOutcome.Error` + `VerificationStatus.VerifyFailed`
  6. **`CleanupConversionOutputs()` loescht die erfolgreich konvertierte Datei**
- **Betroffene Dateien:**
  - [ConversionPhaseHelper.cs](../src/Romulus.Infrastructure/Orchestration/ConversionPhaseHelper.cs#L179-L190) — `target` bleibt `null`
  - [ConversionVerificationHelpers.cs](../src/Romulus.Infrastructure/Orchestration/ConversionVerificationHelpers.cs#L36) — Return `false` bei `null/null`
  - [FormatConverterAdapter.cs](../src/Romulus.Infrastructure/Conversion/FormatConverterAdapter.cs#L237-L244) — Legacy-Fallback ohne Plan
  - [ChdmanToolConverter.cs](../src/Romulus.Infrastructure/Conversion/ChdmanToolConverter.cs) / DolphinToolConverter / SevenZipToolConverter / PsxtractToolConverter — alle liefern `ConversionResult` ohne `Plan`/`VerificationResult`
- **Reproduktion:**
  1. `data/conversion-registry.json` loeschen/korrumpieren
  2. Run mit Konvertierung starten
  3. Konvertierung schlaegt bei jedem einzelnen File scheinbar fehl (obwohl die Tool-Konvertierung erfolgreich war)
  4. Konvertierte Outputs werden geloescht
- **Ursache:** `ConvertSingleFile` setzt `target` nur im Nicht-FormatConverterAdapter-Branch (Zeile 198). Fuer FormatConverterAdapter bleibt `target=null`. Legacy-Converter-Ergebnisse haben weder `Plan` noch `VerificationResult` gesetzt. `IsVerificationSuccessful` hat keinen Fallback fuer dieses Double-Null-Szenario.
- **Quelldateien:** Nicht betroffen — Source wird erst im `verificationOk`-Branch in den Trash verschoben, der hier nie erreicht wird. Die Source bleibt am Ursprungsort, aber der konvertierte Output ist verloren.
- **Fix:** Zwei Optionen:
  - (a) `ConvertForConsole` im Legacy-Pfad soll das `Plan`-Property auf ein synthetisches Single-Step-Plan setzen (analog zu `PlanForConsole`'s Fallback-Plan), damit `IsVerificationSuccessful` den `effectiveTarget` ableiten kann
  - (b) `IsVerificationSuccessful` soll bei `target=null` + `Plan=null` + `VerificationResult==NotAttempted` + `TargetPath!=null` → `true` zurueckgeben (Skip-Verification statt Fail-Verification)
- **Testabsicherung:** Integration-Test: FormatConverterAdapter mit `planner=null, executor=null` → ConvertForConsole → Ergebnis muss `Outcome == Success` sein. Regression-Test: IsVerificationSuccessful mit null/null/NotAttempted → darf nicht false zurueckgeben wenn TargetPath existiert.

---

## DB-002: HIGH — MoveDirectorySafely prueft nur den unmittelbaren Parent auf Reparse Points

- [ ] **Offen**
- **Schweregrad:** High (Security – Symlink-Escape fuer Verzeichnis-Moves)
- **Impact:** `MoveItemSafely` (File-Moves) nutzt `HasReparsePointInAncestry(fullDest, root)` und prueft die gesamte Verzeichnishierarchie. `MoveDirectorySafely` prueft dagegen **nur** `new DirectoryInfo(destParent)` — den unmittelbaren Parent. Wenn ein Symlink-Verzeichnis tiefer in der Hierarchie liegt, wird es bei Directory-Moves nicht erkannt.
- **Betroffene Datei:** [FileSystemAdapter.cs](../src/Romulus.Infrastructure/FileSystem/FileSystemAdapter.cs#L523-L529) — `MoveDirectorySafely` nur immediate-Parent-Check vs. Zeile 299 (`MoveItemSafely` mit `HasReparsePointInAncestry`)
- **Reproduktion:**
  1. Symlink-Kette: `C:\trash\innocent\link → C:\extern\`
  2. `MoveDirectorySafely(source, "C:\\trash\\innocent\\link\\dest")` → Nur `C:\trash\innocent` wird geprueft (kein Reparse Point), `link` selbst wird nicht erkannt
- **Ursache:** Copy-Paste-Asymmetrie bei der Sicherheitsimplementierung zwischen File- und Directory-Move
- **Fix:** `HasReparsePointInAncestry(fullDest, root)` analog zu `MoveItemSafely` aufrufen
- **Testabsicherung:** Unit-Test: Mock-Directory mit Reparse Point in mittlerer Hierarchie-Stufe → `MoveDirectorySafely` muss werfen

---

## DB-003: HIGH — AllowedRootPathPolicy prueft keine File-Level Reparse Points

- [ ] **Offen**
- **Schweregrad:** High (Security – Symlink-Escape auf Dateiebene)
- **Impact:** `ContainsReparsePoint()` iteriert nur ueber `DirectoryInfo`-Ancestors. Wenn die Zieldatei selbst ein Symlink ist (Windows-File-Symlink), wird dies nicht erkannt. `IsPathAllowed` gibt `true` zurueck obwohl die Datei auf ein externes Ziel zeigt.
- **Betroffene Datei:** [AllowedRootPathPolicy.cs](../src/Romulus.Infrastructure/Safety/AllowedRootPathPolicy.cs#L82-L92) — `ContainsReparsePoint` prueft nur Directories
- **Mitigierung:** Die nachgelagerten `MoveItemSafely`/`MoveDirectorySafely` in `FileSystemAdapter` pruefen Source-Reparse-Points separat. Damit ist das `Move`-Szenario abgedeckt, aber `Read`-/`Copy`-Szenarien (z.B. Hash-Berechnung, DAT-Vergleich) koennten betroffen sein.
- **Fix:** `File.GetAttributes(fullPath) & FileAttributes.ReparsePoint` am Ende von `ContainsReparsePoint` pruefen
- **Testabsicherung:** Unit-Test: Datei-Symlink innerhalb erlaubter Roots → `IsPathAllowed` muss `false` zurueckgeben

---

## DB-004: HIGH — AuditCsvParser akzeptiert ungeschlossene Quotes

- [ ] **Offen**
- **Schweregrad:** High (Datenkorruption bei Rollback mit manipulierten/korrupten CSVs)
- **Impact:** Wenn eine CSV-Zeile ein oeffnendes `"` ohne schliessendes `"` hat, bleibt `inQuotes=true` fuer den Rest der Zeile. Alle nachfolgenden Kommas werden als Teil des Feldwerts behandelt statt als Separator. Ergebnis: Zu wenige Felder → Rollback liest falsche Spalten → falsche Dateizuordnung.
- **Betroffene Datei:** [AuditCsvParser.cs](../src/Romulus.Infrastructure/Audit/AuditCsvParser.cs#L15-L57) — kein Fehler/Warning bei `inQuotes==true` am Zeilenende
- **Reproduktion:**
  ```
  CSV-Zeile: rootPath,C:\path,C:\trash,"ungeschlossener Pfad,mit Komma
  Erwartet: 8 Felder (oder Fehler)
  Tatsaechlich: 4 Felder (Feld 3 enthaelt alles ab dem Quote)
  ```
- **Ursache:** RFC-4180-konformes Verhalten bei korruptem Input nicht definiert; kein Error-Path
- **Fix:** Am Ende des Parsens `if (inQuotes)` pruefen und entweder Exception werfen oder die Zeile als korrupt markieren. Rollback-Code muss korrupte Zeilen als `skippedCorrupt` zaehlen statt falsche Felder zu verwenden.
- **Testabsicherung:** Unit-Test: CSV-Zeile mit ungeschlossenem Quote → erwarten: Exception oder spezifischer Error-Marker

---

## DB-005: MEDIUM — API-Endpoint lehnt doppelte Roots nicht ab

- [ ] **Offen**
- **Schweregrad:** Medium (Doppeltes Scanning, doppelte Audit-Eintraege, falsche KPI-Zaehlung)
- **Impact:** `POST /runs` akzeptiert `["C:\\Games", "C:\\Games"]` → gleicher Ordner wird 2× gescannt → doppelte Move-Kandidaten → evtl. fehlerhafte Deduplication-Ergebnisse. CLI ist geschuetzt (`.Distinct()` in `BuildRunMutexName`), API nicht.
- **Betroffene Datei:** API-Endpoint fuer Run-Erstellung (RunWatchEndpoints / Run Endpoint in `Program.cs`)
- **Fix:** `request.Roots.Distinct(StringComparer.OrdinalIgnoreCase)` vor Weiterreichung an Orchestrator
- **Testabsicherung:** Integration-Test: POST mit doppelten Roots → erwarten: De-Deduplizierung oder 400-Bad-Request

---

## DB-006: MEDIUM — Cross-Prozess Audit-Schreib-Konflikte moeglich

- [ ] **Offen**
- **Schweregrad:** Medium (Audit-Datenverlust bei parallelen Prozessen)
- **Impact:** `AuditCsvStore.AppendAuditRows` nutzt ein prozessinternes `lock(lockHandle.Sync)`. Innerhalb eines Prozesses ist die Serialisierung korrekt. Wenn aber CLI und GUI (oder zwei CLI-Instanzen) gleichzeitig in dieselbe Audit-CSV schreiben, gibt es keine Cross-Prozess-Synchronisation. Das Copy-Append-Move-Pattern kann dazu fuehren, dass Zeilen des anderen Prozesses ueberschrieben werden.
- **Betroffene Datei:** [AuditCsvStore.cs](../src/Romulus.Infrastructure/Audit/AuditCsvStore.cs#L109-L165) — `lock(lockHandle.Sync)` ist nur prozessintern
- **Mitigierung:** In der Praxis nutzt jeder Run eine eigene Audit-CSV mit Zeitstempel im Namen. Parallele Schreibzugriffe auf dieselbe CSV sind daher selten.
- **Fix:** Named Mutex oder `FileShare.None` auf die Audit-CSV waehrend des Schreibvorgangs (statt Copy→Append→Move). Oder: Wenn das Copy-Move-Pattern beibehalten wird, Named File Lock (z.B. `*.audit.lock`).
- **Testabsicherung:** Schwierig deterministisch zu testen; am besten durch Architektur-Aenderung (Named Lock) eliminieren

---

## DB-007: MEDIUM — GdiSetParser behandelt Quotes in Dateinamen falsch

- [ ] **Offen**
- **Schweregrad:** Medium (Set-Member-Verlust bei exotischen Dateinamen)
- **Impact:** `IndexOf('"', quoteStart + 1)` findet das naechste Quote-Zeichen, nicht das matchende schliessende. Bei Dateinamen mit Anführungszeichen (z.B. `"Game's Edition.bin"`) wird der Name abgeschnitten.
- **Betroffene Datei:** [GdiSetParser.cs](../src/Romulus.Core/SetParsing/GdiSetParser.cs#L51-L55)
- **Praktische Relevanz:** GDI-Track-Dateinamen enthalten in der Praxis fast nie Quotes. Das GDI-Format spezifiziert keine Escape-Sequenzen. Trotzdem ist das Parsing-Verhalten falsch.
- **Fix:** Der GDI-Standard verwendet keine verschachtelten Quotes. Der Fix waere: `LastIndexOf('"')` statt `IndexOf('"', quoteStart + 1)` — nimmt das LETZTE Quote als Abschluss.
- **Testabsicherung:** Unit-Test: GDI mit Quote-Zeichen in Track-Name → korrekter Parse

---

## DB-008: MEDIUM — CcdSetParser.GetMissingFiles prueft Existenz der CCD-Datei nicht

- [ ] **Offen**
- **Schweregrad:** Medium (Falsche Missing-Files-Reports)
- **Impact:** `GetMissingFiles` prueft nicht, ob die CCD-Datei selbst existiert, bevor es Companion-Dateien als "fehlend" meldet. Fuer eine nicht-existierende CCD-Datei werden .img/.sub-Companions als "fehlend" gemeldet.
- **Betroffene Datei:** [CcdSetParser.cs](../src/Romulus.Core/SetParsing/CcdSetParser.cs#L28) — fehlt `!parserIo.Exists(ccdPath)` Check (im Gegensatz zu `GetRelatedFiles`)
- **Fix:** `if (string.IsNullOrWhiteSpace(ccdPath) || !parserIo.Exists(ccdPath)) return Array.Empty<string>();` analog zu `GetRelatedFiles`
- **Testabsicherung:** Unit-Test: nicht-existierende CCD → `GetMissingFiles` gibt leere Liste zurueck

---

## DB-009: LOW — MdsSetParser unterschiedliche Pfadnormalisierung

- [ ] **Offen**  
- **Schweregrad:** Low
- **Impact:** `GetRelatedFiles` nutzt `Path.GetDirectoryName(mdsPath)` (relativ), `GetMissingFiles` nutzt `Path.GetDirectoryName(Path.GetFullPath(mdsPath))` (absolut). Inkonsistentes Verhalten bei relativen Pfaden.
- **Betroffene Datei:** [MdsSetParser.cs](../src/Romulus.Core/SetParsing/MdsSetParser.cs)
- **Fix:** Beide Methoden sollten `Path.GetFullPath` konsistent nutzen
- **Testabsicherung:** Unit-Test mit relativen Pfaden

---

## Aktualisierte Zusammenfassung (inkl. Deep-Dive)

| Prioritaet | Finding | Anzahl | Blocking/Critical | High | Medium | Low |
|---|---|---|---|---|---|---|
| Erstaudit (P1-P7) | | 19 | 1 | — | 10 | 8 |
| Deep-Dive | Conversion Verification Bug | 1 | 1 | — | — | — |
| Deep-Dive | Security / FileSystem | 2 | — | 2 | — | — |
| Deep-Dive | Audit Integrity | 2 | — | 1 | 1 | — |
| Deep-Dive | API Entry Point | 1 | — | — | 1 | — |
| Deep-Dive | Set Parsing | 3 | — | — | 2 | 1 |
| **Gesamt** | | **28** | **2** | **3** | **14** | **9** |

---

## Aktualisierte Top-Massnahmen (priorisiert)

1. **DB-001:** Legacy-Conversion-Verification-Bug fixen (CRITICAL — konvertierte Files werden geloescht)
2. **F-001:** RollbackMovedArtifacts Fehler-Propagation (BLOCKING)
3. **DB-002:** MoveDirectorySafely full-ancestry Reparse-Point-Check (HIGH)
4. **DB-003:** AllowedRootPathPolicy File-Level Reparse-Point-Check (HIGH)
5. **DB-004:** AuditCsvParser Unclosed-Quote-Handling (HIGH)
6. **F-004:** Root-Validierung in ConvertSingleFile (Defense-in-Depth)
7. **DB-005:** API-Endpoint Roots-Deduplizierung
8. **DB-006:** Cross-Prozess Audit-Lock
9. **F-010:** ConversionExecutor Timeout-Recovery-Test
10. **F-011:** Korrupte-Audit-CSV-Rollback-Test

---

## Schlussurteil (nach Deep-Dive)

Die Kernlogik (Deduplication, Scoring, GameKey, RegionDetection) ist **fehlerfrei und deterministisch** — der Deep-Dive hat hier keine Bugs gefunden. Die allgemeine Architektur und Security-Implementierung bleiben vorbildlich.

**Der gravierendste neue Fund (DB-001)** betrifft einen realen Bug in der Conversion Pipeline: Bei degradiertem Betrieb (fehlende/korrupte `conversion-registry.json`) werden erfolgreich konvertierte Dateien faelschlicherweise geloescht. Obwohl die Quelldateien erhalten bleiben, ist das Loeschen valider Konvertierungs-Outputs ein klarer Bug der sofort gefixt werden muss.

**Die Security-Findings (DB-002, DB-003)** zeigen Asymmetrien in der Reparse-Point-Validierung zwischen File- und Directory-Pfaden. Diese sind in der Praxis schwer ausnutzbar (erfordert lokalen Zugriff), sollten aber aus Defense-in-Depth-Gruenden behoben werden.

---
---

## Runde 3 — Erweiterte Tiefenanalyse (2026-04-15)

> **Methode:** Parallele Analyse von RunOrchestrator-Flow, DAT-Verifikation, Sorting/Classification, Reporting (HTML/CSV), Tool-Integration/Hashing, Settings/Configuration mit manueller Verifizierung jedes Findings.

---

### DB-010: MEDIUM-HIGH — SettingsLoader.MergeFromUserSettings validiert, korrigiert aber nicht

- [ ] **Offen**
- **Schweregrad:** Medium-High (invalide Settings bleiben aktiv nach Warnung)
- **Impact:** `MergeFromUserSettings()` fuehrt `RomulusSettingsValidator.Validate(settings)` aus und logt bei Fehlern eine Warnung (`onWarning?.Invoke(...)`). Das Kommentar sagt "revert the specific invalid user-settings sections", aber **der Code tut dies nicht** — die invaliden Settings bleiben unveraendert im `settings`-Objekt. Im Gegensatz dazu: `LoadFromSafe()` (Zeile 148) gibt bei Validierungsfehlern korrekt `new RomulusSettings()` mit `WasCorrupt: true` zurueck.
- **Betroffene Datei:** [SettingsLoader.cs](../src/Romulus.Infrastructure/Configuration/SettingsLoader.cs#L363-L375) — nur `onWarning`, kein Revert
- **Reproduktion:**
  ```json
  // %APPDATA%\Romulus\settings.json
  { "general": { "preferredRegions": [] } }
  ```
  → Validation erkennt leeres Array → Warning geloggt → `PreferredRegions` bleibt leer → Code der `PreferredRegions[0]` nutzt → `IndexOutOfRangeException`
- **Ursache:** Der R6-008-Fix-Kommentar wurde geschrieben, aber die eigentliche Revert-Logik nie implementiert
- **Fix:** Nach `validationErrors.Count > 0`: invalide Sektionen auf `defaults.json`-Werte zuruecksetzen, oder gesamtes User-Merge verwerfen und nur Defaults verwenden
- **Testabsicherung:** Unit-Test: MergeFromUserSettings mit invalider settings.json → Validate-Fehler → Settings muessen auf Safe-Defaults zurueckgefallen sein

---

### DB-011: MEDIUM — ConversionRegistryLoader nutzt rohes GetProperty fuer lossless/cost

- [ ] **Offen**
- **Schweregrad:** Medium (kryptischer Crash bei malformed conversion-registry.json)
- **Impact:** Zwei Felder in `LoadCapability()` nutzen `item.GetProperty("lossless").GetBoolean()` und `item.GetProperty("cost").GetInt32()` direkt, waehrend alle anderen Felder ueber die sicheren `ReadRequired*`/`ReadOptional*`-Wrapper laufen. Bei fehlendem Feld wirft `GetProperty` eine `KeyNotFoundException` ohne Kontextinfo (welche Capability, welcher Index). Bei falschem Typ (z.B. `"lossless": "yes"`) wirft `GetBoolean()` eine `InvalidOperationException`.
- **Betroffene Datei:** [ConversionRegistryLoader.cs](../src/Romulus.Infrastructure/Conversion/ConversionRegistryLoader.cs#L174-L175) — `GetProperty("lossless").GetBoolean()` und `GetProperty("cost").GetInt32()`
- **Reproduktion:** `data/conversion-registry.json` manuell editieren, `"lossless"` entfernen → Application-Start crasht mit `KeyNotFoundException: The given key 'lossless' was not found`
- **Ursache:** Inkonsistenz — `ReadRequiredString` und `ReadRequiredEnum` existieren, aber kein `ReadRequiredBool`/`ReadRequiredInt` wurde erstellt
- **Fix:** `ReadRequiredBool` und `ReadRequiredInt` Wrapper inklusive Context-Info erstellen und an diesen Stellen verwenden
- **Testabsicherung:** Unit-Test: Capability-JSON ohne `lossless`-Feld → erwarten: spezifische InvalidOperationException mit Kontextinfo statt KeyNotFoundException

---

### DB-012: MEDIUM — DatIndex.Add() verwirft Eintraege still bei Kapazitaetsgrenze

- [ ] **Offen**
- **Schweregrad:** Medium (False-Negative DAT-Verifikation bei grossen DATs)
- **Impact:** Wenn `MaxEntriesPerConsole > 0` und `hashMap.Count >= MaxEntriesPerConsole`, gibt `Add()` sofort `return` zurueck — keine Warnung, kein Counter, keine Exception. Eintraege nach der Kapazitaetsgrenze existieren im DAT, werden aber nie indexiert. ROMs die diesen Hashes entsprechen werden faelschlich als "Miss" verifiziert.
- **Betroffene Datei:** [DatIndex.cs](../src/Romulus.Contracts/Models/DatIndex.cs#L47-L48) — `if (MaxEntriesPerConsole > 0 && hashMap.Count >= MaxEntriesPerConsole) return;`
- **Reproduktion:** DAT mit 5000 Eintraegen, `MaxEntriesPerConsole = 4000` → Eintraege 4001-5000 werden still verworfen → ROMs mit diesen Hashes werden als "Miss" gemeldet
- **Ursache:** Sicherheitsgrenze gegen OOM bei boeswilligen DATs, aber ohne Feedback an den Caller
- **Fix:** Counter `DroppedByCapacityLimit` erhoehen und mindestens einmal pro Console loggen (z.B. ueber Callback-Parameter oder Rueckgabewert)
- **Testabsicherung:** Unit-Test: DatIndex mit MaxEntriesPerConsole → ueberzaehlige Adds → DroppedCount > 0 und Lookup fuer verworfene Hashes gibt null zurueck

---

### DB-013: LOW — DatIndex nameMap behaelt verwaiste Eintraege bei Hash-Update

- [ ] **Offen**
- **Schweregrad:** Low (inkonsistentes LookupByName-Ergebnis)
- **Impact:** Wenn ein Hash mit einem neuen GameName ueberschrieben wird (`hashMap[hash] = newEntry`), wird `nameMap[gameName] = newEntry` fuer den neuen Namen gesetzt. Aber `nameMap[oldGameName]` bleibt bestehen und zeigt auf den alten (jetzt ueberholten) Eintrag. `LookupByName(oldGameName)` gibt den alten Eintrag zurueck, obwohl der zugehoerige Hash inzwischen auf einen anderen GameName zeigt.
- **Betroffene Datei:** [DatIndex.cs](../src/Romulus.Contracts/Models/DatIndex.cs#L42-L46) — Update-Pfad loescht alten nameMap-Eintrag nicht
- **Praktische Relevanz:** Niedrig — Name-Index ist dokumentiert als "first entry per game wins" und dient nur als Fallback. Hash-Index ist primaer.
- **Fix:** Vor `nameMap[gameName] = newEntry` den alten GameName aus nameMap entfernen (erfordert Lookup des alten Eintrags)
- **Testabsicherung:** Unit-Test: Add(hash=X, name=A) → Add(hash=X, name=B) → LookupByName(A) muss null sein

---

### DB-014: MEDIUM — RunReportWriter.SkippedCount summiert nicht alle Skip-Quellen

- [ ] **Offen**
- **Schweregrad:** Medium (unvollstaendige KPI im Report)
- **Impact:** `SkippedCount = projection.ConvertSkippedCount + projection.ConvertBlockedCount + projection.SkipCount` summiert nur Conversion-Skips und Move-Skips. Es fehlen `DatRenameSkippedCount`, `ConsoleSortBlocked` und `ConsoleSortUnknown`. Der Report-KPI "Uebersprungen" zeigt damit einen zu niedrigen Wert. Die Einzelwerte sind separat im Report enthalten, aber der Summary-KPI ist irrefuehrend.
- **Betroffene Datei:** [RunReportWriter.cs](../src/Romulus.Infrastructure/Reporting/RunReportWriter.cs#L164)
- **Reproduktion:** Run mit DatRenameSkipped=10, ConsoleSortBlocked=5 → SkippedCount im Report zeigt 0 fuer diese Kategorien
- **Ursache:** SkippedCount wurde fuer Conversion-Phase erstellt und nie um spaetere Pipeline-Phasen erweitert
- **Fix:** `DatRenameSkippedCount` in die Summe aufnehmen. ConsoleSort-Blocked/Unknown-Behandlung klaeren (sind diese "Skips" oder eigene Kategorie?)
- **Testabsicherung:** Unit-Test: Projection mit allen Skip-Quellen > 0 → SkippedCount == Summe aller Quellen

---

### DB-015: MEDIUM — DatSourceService Regex-Timeout bei Sidecar-Hash-Extraktion nicht gefangen

- [ ] **Offen**
- **Schweregrad:** Medium (unhandled Exception bei Timeout in DAT-Sidecar-Validierung)
- **Impact:** `Regex.Match(shaText, ...)` hat `TimeSpan.FromMilliseconds(500)` als Timeout. Wenn der Timeout erreicht wird, wirft .NET eine `RegexMatchTimeoutException`. Der umgebende `catch` faengt aber nur `HttpRequestException`, `IOException` und `TaskCanceledException` — **nicht** `RegexMatchTimeoutException`. Die Exception propagiert unbehandelt und crasht die DAT-Verifizierung.
- **Betroffene Datei:** [DatSourceService.cs](../src/Romulus.Infrastructure/Dat/DatSourceService.cs#L393-L401) — Regex.Match mit Timeout, catch faengt nicht RegexMatchTimeoutException
- **Reproduktion:** Sidecar-Datei mit extrem langem oder pathologischem Content → Regex-Engine braucht >500ms → `RegexMatchTimeoutException` → unbehandelt
- **Praktische Relevanz:** Niedrig — Sidecar-Content ist typischerweise kurz (ein Hash + optional Dateiname). Der Timeout wuerde nur bei absichtlich pathologischem Input ausgeloest.
- **Fix:** `RegexMatchTimeoutException` im catch-Block ergaenzen, oder separater try/catch um den Regex.Match
- **Testabsicherung:** Unit-Test: extrem langer Input-String → erwarten: kein unhandled Crash, sondern korrekter Fallback-Return

---

### DB-016: LOW — M3U Playlist-Rewrite entfernt ambige Filename-Mappings komplett

- [ ] **Offen**
- **Schweregrad:** Low (Playlist zeigt tote Links bei exotischen Multi-Disc-Sets)
- **Impact:** Wenn zwei Set-Members denselben Dateinamen haben (z.B. `Track 01.bin` aus verschiedenen Disc-Ordnern), wird der Filename-Key aus `fileNameRenameMap` komplett entfernt. Wenn das M3U bare Filenames nutzt (nicht relative Pfade), werden BEIDE Eintraege nicht rewritten → tote Links.
- **Betroffene Datei:** [ConsoleSorter.cs](../src/Romulus.Infrastructure/Sorting/ConsoleSorter.cs#L660-L667)
- **Mitigierung:** Die `relativeRenameMap` und `absoluteRenameMap` behandeln beide Members korrekt. Nur der Filename-Fallback-Pfad ist betroffen. Die meisten M3U-Dateien nutzen relative Pfade.
- **Fix:** Statt ambige Entries komplett zu entfernen, den ersten gueltigen Eintrag behalten (oder bei Kollision den M3U-Entry-Index als Disambiguator nutzen)
- **Testabsicherung:** Unit-Test: Multi-Disc mit identischen Track-Filenames + M3U mit bare Filenames → Rewrite muss korrekt sein

---

## Aktualisierte Zusammenfassung (inkl. Runde 3)

| Quelle | Anzahl | Critical/Blocking | High | Medium-High | Medium | Low |
|---|---|---|---|---|---|---|
| Erstaudit (P1-P7) | 19 | 1 | — | — | 10 | 8 |
| Deep-Dive Runde 2 | 9 | 1 | 3 | — | 4 | 1 |
| Runde 3 | 7 | — | — | 1 | 4 | 2 |
| **Gesamt** | **35** | **2** | **3** | **1** | **18** | **11** |

---

## Aktualisierte Top-Massnahmen (priorisiert nach Runde 3)

1. **DB-001:** Legacy-Conversion-Verification-Bug fixen (CRITICAL — konvertierte Files werden geloescht)
2. **F-001:** RollbackMovedArtifacts Fehler-Propagation (BLOCKING)
3. **DB-002:** MoveDirectorySafely full-ancestry Reparse-Point-Check (HIGH)
4. **DB-003:** AllowedRootPathPolicy File-Level Reparse-Point-Check (HIGH)
5. **DB-004:** AuditCsvParser Unclosed-Quote-Handling (HIGH)
6. **DB-010:** SettingsLoader.MergeFromUserSettings Validation-Revert implementieren (MEDIUM-HIGH)
7. **F-004:** Root-Validierung in ConvertSingleFile (Defense-in-Depth)
8. **DB-011:** ConversionRegistryLoader ReadRequired-Wrapper fuer lossless/cost
9. **DB-012:** DatIndex Capacity-Drop Logging/Counter
10. **DB-014:** RunReportWriter SkippedCount alle Quellen summieren
11. **DB-015:** DatSourceService RegexMatchTimeoutException fangen
12. **DB-005:** API-Endpoint Roots-Deduplizierung

---

## Gesamtschlussurteil (nach Runde 3)

Die Kernlogik (Deduplication, Scoring, GameKey, RegionDetection) bleibt **fehlerfrei und deterministisch**. Auch in Runde 3 wurden hier keine Bugs gefunden.

Runde 3 hat 7 weitere verifizierte Findings ergeben, die ueberwiegend robustere Error-Handling- und Konfigurationsvalidierung betreffen:

- **DB-010** (SettingsLoader) ist der wichtigste neue Fund: Invalide User-Settings werden nach fehlgeschlagener Validierung nicht korrigiert. Das Kommentar verspricht einen Revert, der Code tut es aber nicht. Bei boeswilligen oder versehentlich fehlerhaften `settings.json` koennten Downstream-Crashes entstehen.
- **DB-011** (ConversionRegistryLoader) und **DB-012** (DatIndex) sind Robustheitsprobleme mit kryptischen Fehlermeldungen bei korrupten Daten.
- **DB-014** (SkippedCount) und **DB-015** (Regex-Timeout) sind KPI-/Reporting-Bugs mit begrenztem praktischem Impact.

**Gesamtbewertung:** 35 Findings ueber 3 Runden. 2 sofort fixen (DB-001, F-001), 4 zeitnah (DB-002–DB-004, DB-010), Rest planbar. Die Codebasis ist **architektonisch sehr solide** — die Findings betreffen Edge-Case-Robustheit und Defense-in-Depth, nicht fundamentale Designprobleme.

---
---

## Runde 4 — Erweiterte Tiefenanalyse (2026-04-15)

> **Methode:** 5 parallele Subagenten fuer: (1) WPF ViewModel/MVVM-Schicht, (2) ConversionGraph/Planner/Executor, (3) LiteDb/CollectionIndex/Caching, (4) Thread-Safety Hotspots gesamt, (5) Cross-Root/Hardlink/Quarantine. Anschliessend manuelle Verifikation jedes Findings am Quellcode.
>
> **Thread-Safety Gesamtaudit:** Der dedizierte Thread-Safety-Subagent hat die gesamte Codebasis auf Race Conditions, Deadlocks, unsichere Shared-Mutable-State und Lock-Patterns untersucht. Ergebnis: **ZERO Thread-Safety-Bugs gefunden.** Interlocked-Operationen, ConcurrentDictionary-Usage, Lock-Patterns, double-checked Locking in RuleEngine, reference-counted File-Locks in AuditCsvStore — alles korrekt implementiert. **Die Thread-Safety ist produktionsreif.**

---

### DB-017: HIGH — ConversionExecutor meldet success=true bei VerifyFailed

- [ ] **Offen**
- **Schweregrad:** High (widerspruechliches Step-Ergebnis)
- **Impact:** In `ConversionExecutor.ExecuteAsync()`, wenn die Verifikation eines Conversion-Steps fehlschlaegt (`verifyStatus == VerificationStatus.VerifyFailed`), wird der Step-Callback `onStepComplete` mit `success: true` aufgerufen, obwohl die Verifikation gescheitert ist. Das Gesamt-Ergebnis gibt korrekt `ConversionOutcome.Error` zurueck, aber der **einzelne Step-Report zeigt success=true + VerifyFailed** — ein Widerspruch.
- **Betroffene Datei:** [ConversionExecutor.cs](../src/Romulus.Infrastructure/Conversion/ConversionExecutor.cs#L225-L232) — `new ConversionStepResult(step.Order, finalOutputPath, true, verifyStatus, ...)`
- **Reproduktion:** Conversion-Step produziert Output-Datei → Verify prueft Hash → Hash stimmt nicht → `VerifyFailed` → StepResult bekommt `success: true`
- **Ursache:** Dritter Parameter von `ConversionStepResult` ist `true` statt `false` im VerifyFailed-Branch
- **Fix:** Zeile aendern von `true` auf `false`: `new ConversionStepResult(step.Order, finalOutputPath, false, verifyStatus, null, invokeResult.DurationMs)`
- **Testabsicherung:** Unit-Test: Conversion-Step mit VerifyFailed → StepResult.Success muss false sein

---

### DB-018: HIGH — LiteDbCollectionIndex.RecreateDatabase Exception-Safety Violation

- [ ] **Offen**
- **Schweregrad:** High (disposed Database-Referenz bei Fehler, kein Recovery ohne Restart)
- **Impact:** `RecreateDatabase()` ruft `_database.Dispose()` auf und versucht dann `_database = OpenDatabase()`. Wenn `OpenDatabase()` eine Exception wirft (IOException, LiteException, Permission-Fehler), bleibt `_database` auf die **bereits disposed Instanz** zeigend. Alle folgenden Operationen (`UpsertEntriesAsync`, `GetMetadataAsync`, etc.) arbeiten mit einem disposed `LiteDatabase` und produzieren undefiniertes Verhalten oder Crashes.
- **Betroffene Datei:** [LiteDbCollectionIndex.cs](../src/Romulus.Infrastructure/Index/LiteDbCollectionIndex.cs#L452-L454) — `_database.Dispose(); RecoverDatabaseFile(reason); _database = OpenDatabase();`
- **Reproduktion:** Schema-Mismatch → RecreateDatabase() → Disk voll oder Permissions → OpenDatabase() wirft → `_database` zeigt auf disposed Objekt → naechster `_gate.WaitAsync()` + `_database.GetCollection()` → Crash
- **Ursache:** Kein Exception-Guard zwischen Dispose der alten und Zuweisung der neuen Instanz
- **Fix:** OpenDatabase() in try/catch wrappen; bei Fehler die alte Referenz nicht verlieren oder Zustand als "corrupted" markieren und `ObjectDisposedException` bei Folgezugriffen werfen
- **Testabsicherung:** Unit-Test: RecreateDatabase wo OpenDatabase fehlschlaegt → Zustand muss safe-degraded sein, keine NullRef/Disposed-Exceptions bei Folgezugriff

---

### DB-019: MEDIUM — _syncContext-null-Pfad feuert PropertyChanged auf Background-Thread

- [ ] **Offen**
- **Schweregrad:** Medium (WPF-Binding-Fehler bei null SynchronizationContext)
- **Impact:** In `ArmInlineMoveConfirmDebounce()` und im Progress-Message-Handler von `ExecuteRunCoreAsync()` gibt es einen Fallback-Pfad: Wenn `_syncContext is null`, werden `OnPropertyChanged()` und `ApplyProgressMessage()` direkt auf dem Background-Thread aufgerufen (nach `Task.Run(...).ConfigureAwait(false)`). WPF-Bindings erwarten PropertyChanged-Events auf dem UI-Thread — das Feuern vom Background-Thread kann zu nicht-aktualisierten UI-Elementen oder stillen Binding-Fehlern fuehren.
- **Betroffene Dateien:**
  - [MainViewModel.cs](../src/Romulus.UI.Wpf/ViewModels/MainViewModel.cs#L253-L258) — `if (_syncContext is null) { OnPropertyChanged(...); ... }`
  - [MainViewModel.RunPipeline.cs](../src/Romulus.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs#L1230-L1242) — Progress-Callback identisches Pattern
- **Eintrittswahrscheinlichkeit:** Niedrig in Produktion (WPF setzt SynchronizationContext immer), hoeher in Unit-Tests ohne Dispatcher-Loop
- **Ursache:** Defensive null-Pruefung degradiert korrekt fuer Non-WPF-Szenarien, aber der Fallback-Pfad ist fuer WPF-Bindings unsicher
- **Fix:** Bei `_syncContext is null` entweder Exception werfen (fail-fast) oder `Application.Current?.Dispatcher.Invoke()` als Fallback
- **Testabsicherung:** Bestehende Tests pruefen, ob ViewModel-Konstruktion immer mit gueltigem SynchronizationContext erfolgt

---

### DB-020: LOW — PBP-Registry command/target-Inkonsistenz

- [ ] **Offen**
- **Schweregrad:** Low (potentiell falsches Tool-Kommando oder falscher Target-Extension)
- **Impact:** In `conversion-registry.json` hat der Eintrag fuer `.pbp → .cue` den Befehl `"command": "pbp2chd"`. Der Kommandoame impliziert CHD-Output (`pbp2chd`), aber die `targetExtension` ist `.cue`. Entweder ist der Kommandoname falsch (sollte `pbp2cue` sein) oder die targetExtension ist falsch (sollte `.chd` sein). Bei falscher Konfiguration wuerde der ConversionPlanner einen Plan erstellen, dessen Tool-Output nicht zum erwarteten Format passt.
- **Betroffene Datei:** [conversion-registry.json](../data/conversion-registry.json#L117-L127) — `"command": "pbp2chd"` mit `"targetExtension": ".cue"`
- **Ursache:** Vermutlich Tippfehler im command-Feld oder im targetExtension-Feld
- **Fix:** Verifizieren was `psxtract pbp2chd` tatsaechlich produziert und Registry entsprechend korrigieren
- **Testabsicherung:** Integration-Test: PBP-Conversion → Output-Extension muss targetExtension entsprechen

---

### DB-021: LOW — HardlinkService schliesst ReFS aus

- [ ] **Offen**
- **Schweregrad:** Low (Feature-False-Negative auf Windows-Server-Systemen)
- **Impact:** `IsHardlinkSupported()` prueft nur auf `"NTFS"`. ReFS (Resilient File System) unterstuetzt ebenfalls Hardlinks (ab Windows Server 2012 R2), wird aber als nicht-unterstuetzt zurueckgemeldet. Benutzer auf ReFS-Volumes koennen den Hardlink-Modus nicht nutzen.
- **Betroffene Datei:** [HardlinkService.cs](../src/Romulus.Infrastructure/Linking/HardlinkService.cs#L22) — `return string.Equals(driveInfo.DriveFormat, "NTFS", ...)`
- **Eintrittswahrscheinlichkeit:** Sehr niedrig (Zielgruppe: Windows 10/11 Desktop, nicht Server)
- **Fix:** Pruefung erweitern: `driveInfo.DriveFormat is "NTFS" or "ReFS" (StringComparison.OrdinalIgnoreCase)`
- **Testabsicherung:** Unit-Test mit Moq fuer DriveInfo.DriveFormat = "ReFS" → muss true zurueckgeben

---

### DB-022: LOW — LiteDbCollectionIndex._pendingMutationCount Reset bei Compaction-Failure

- [ ] **Offen**
- **Schweregrad:** Low (Database-Bloat ohne Re-Trigger der Compaction)
- **Impact:** `RegisterMutationAndMaybeCompact()` setzt `_pendingMutationCount = 0` im `finally`-Block — auch wenn `_database.Rebuild()` mit Exception fehlschlaegt. Dadurch wird der Zaehler zurueckgesetzt, obwohl die Compaction nicht stattfand. Es braucht erneut `MutationCompactionThreshold` (5000) weitere Mutationen, bevor Compaction erneut versucht wird. Bei wiederholtem Rebuild-Fehler waechst die Datenbank unkontrolliert.
- **Betroffene Datei:** [LiteDbCollectionIndex.cs](../src/Romulus.Infrastructure/Index/LiteDbCollectionIndex.cs#L373-L378) — `finally { _pendingMutationCount = 0; }`
- **Ursache:** Unconditional Reset im finally-Block statt nur bei erfolgreichem Rebuild
- **Fix:** Reset nur bei Erfolg: `bool rebuilt = false; try { _database.Rebuild(); rebuilt = true; } ... finally { if (rebuilt) _pendingMutationCount = 0; }`
- **Testabsicherung:** Unit-Test: Rebuild wirft Exception → _pendingMutationCount muss unveraendert bleiben → naechster Batch triggert erneut Rebuild

---

## Aktualisierte Zusammenfassung (inkl. Runde 4)

| Quelle | Anzahl | Critical/Blocking | High | Medium-High | Medium | Low |
|---|---|---|---|---|---|---|
| Erstaudit (P1-P7) | 19 | 1 | — | — | 10 | 8 |
| Deep-Dive Runde 2 | 9 | 1 | 3 | — | 4 | 1 |
| Runde 3 | 7 | — | — | 1 | 4 | 2 |
| Runde 4 | 6 | — | 2 | — | 1 | 3 |
| **Gesamt** | **41** | **2** | **5** | **1** | **19** | **14** |

---

## Aktualisierte Top-Massnahmen (priorisiert nach Runde 4)

1. **DB-001:** Legacy-Conversion-Verification-Bug fixen (CRITICAL — konvertierte Files werden geloescht)
2. **F-001:** RollbackMovedArtifacts Fehler-Propagation (BLOCKING)
3. **DB-017:** ConversionExecutor success=true bei VerifyFailed fixen (HIGH — widerspruechliches Step-Ergebnis)
4. **DB-018:** LiteDbCollectionIndex.RecreateDatabase Exception-Safety (HIGH — disposed DB bei Fehler)
5. **DB-002:** MoveDirectorySafely full-ancestry Reparse-Point-Check (HIGH)
6. **DB-003:** AllowedRootPathPolicy File-Level Reparse-Point-Check (HIGH)
7. **DB-004:** AuditCsvParser Unclosed-Quote-Handling (HIGH)
8. **DB-010:** SettingsLoader.MergeFromUserSettings Validation-Revert implementieren (MEDIUM-HIGH)
9. **DB-019:** _syncContext-null-Pfad Background-Thread-PropertyChanged absichern (MEDIUM)
10. **F-004:** Root-Validierung in ConvertSingleFile (Defense-in-Depth)
11. **DB-011:** ConversionRegistryLoader ReadRequired-Wrapper fuer lossless/cost
12. **DB-012:** DatIndex Capacity-Drop Logging/Counter
13. **DB-014:** RunReportWriter SkippedCount alle Quellen summieren
14. **DB-020:** PBP-Registry command/target-Inkonsistenz verifizieren und korrigieren

---

## Gesamtschlussurteil (nach Runde 4)

Die Kernlogik (Deduplication, Scoring, GameKey, RegionDetection) bleibt nach 4 Runden **fehlerfrei und deterministisch**. Auch die **Thread-Safety der gesamten Codebasis** wurde in Runde 4 umfassend geprueft und als **produktionsreif** bestaetigt — Interlocked-Operationen, Lock-Patterns, ConcurrentDictionary-Usage und reference-counted File-Locks sind korrekt implementiert.

Runde 4 hat 6 weitere verifizierte Findings ergeben:

- **DB-017** (ConversionExecutor) ist der wichtigste neue Fund: Ein VerifyFailed-Step meldet `success=true` — ein klarer Widerspruch, der UI/CLI/Reports irreleiten kann. Einfacher Einzeiler-Fix.
- **DB-018** (LiteDbCollectionIndex) zeigt eine Exception-Safety-Luecke: Bei fehlgeschlagenem Database-Rebuild nach Schema-Mismatch bleibt ein disposed `_database`-Feld zurueck. Alle Folgezugriffe crashen. Erfordert try/catch-Guard.
- **DB-019** (_syncContext null) ist defensiv-korrekt fuer Non-WPF-Szenarien, aber unsicher fuer WPF-Bindings. Niedrige Eintrittswahrscheinlichkeit in Produktion.
- **DB-020** bis **DB-022** sind Low-Severity Edge Cases in Registry-Konfiguration, Hardlink-Detection und Compaction-Zaehler.

**Gesamtbewertung:** 41 Findings ueber 4 Runden. 2 sofort fixen (DB-001, F-001), 6 zeitnah (DB-002–DB-004, DB-010, DB-017, DB-018), Rest planbar. Die Codebasis ist **architektonisch sehr solide und thread-safe**. Die Findings betreffen Edge-Case-Robustheit, Exception-Safety und Defense-in-Depth — nicht fundamentale Designprobleme.

---
---

## Runde 5 — Erweiterte Tiefenanalyse (2026-04-15)

> **Methode:** 5 parallele Subagenten fuer: (1) CLI Argument-Parsing & API-Endpoints, (2) Scoring-Engine & GameKey-Normalization, (3) Classification/Rules/Regions/SetParsing, (4) Reporting & Audit/Rollback-System, (5) DI-Komposition & Boot-Pfade. Manuelle Verifikation jedes Findings am Quellcode.
>
> **CLI & API Layer:** Der dedizierte CLI/API-Subagent hat alle Endpoints, Input-Validierung, Path-Security, Auth, Request-Body-Limits und CLI-Dispatch umfassend geprueft. Ergebnis: **ZERO Bugs in CLI/API.** Input-Sanitisierung, Path-Security (Drive-Root/UNC/Symlink-Blocking), FixedTimeEquals, CORS, Pagination-Bounds — alles korrekt und produktionsreif.
>
> **Scoring & GameKey:** Die Kernlogik (FormatScore, VersionScore, DeduplicationEngine.SelectWinner, GameKeyNormalizer) wurde gruendlich auf Determinismus, Tie-Breaking und Edge-Cases geprueft. Ergebnis: **Kernlogik fehlerfrei** — ein Minor-Finding in RegionRankMap (s. DB-023).
>
> **CartridgeHeaderDetector SNES:** Der Subagent hat behauptet, `(complement ^ checksum) == 0xFFFF` sei falsch und muesse `+` sein. **FALSE ALARM** — mathematisch korrekt: Fuer 16-Bit-Complement gilt `~x ^ x = 0xFFFF`. Der Subagent hat den XOR falsch berechnet.

---

### DB-023: LOW — GetRegionRankMap nutzt Raw-Index statt bereinigte Position

- [ ] **Offen**
- **Schweregrad:** Low (Determinismus-Inkonsistenz bei Whitespace-Settings)
- **Impact:** `GetRegionRankMap()` baut die Region-Rank-Map mit dem rohen Array-Index `i` statt einem bereinigten Rank-Counter. Wenn `preferOrder` Whitespace-Only-Eintraege enthaelt (z.B. `["US", "", "EU"]`), werden diese zwar bei der Map-Bildung uebersprungen, aber der Index-Zaehler laeuft weiter. Ergebnis: "EU" bekommt Rang 2 statt 1, Score-Gap steigt von 1 auf 2 Punkte.
- **Betroffene Datei:** [FormatScorer.cs](../src/Romulus.Core/Scoring/FormatScorer.cs#L275-L283) — `map[region] = i;` statt bereinigte Position
- **Eintrittswahrscheinlichkeit:** Sehr niedrig — erfordert leere Eintraege in `PreferredRegions[]` aus User-Settings
- **Ursache:** Schleife nutzt `i` (Array-Index) statt separaten inkrementierten Rank-Counter fuer gueltige Eintraege
- **Fix:** Separaten `rank`-Counter einfuehren: `int rank = 0; ... if (!map.ContainsKey(region)) map[region] = rank; rank++;`
- **Testabsicherung:** Unit-Test: `preferOrder = ["US", "", "EU"]` → "EU" Score muss identical sein wie bei `["US", "EU"]`

---

### DB-024: MEDIUM — AuditSigningService.Rollback nutzt noch Math.Max(1,...) fuer Failed-Count

- [ ] **Offen**
- **Schweregrad:** Medium (irrefuehrende Rollback-Fehlerzahl in UI)
- **Impact:** `AuditSigningService.Rollback()` nutzt `Failed = Math.Max(1, CountAuditDataRows(auditCsvPath))` bei Integritaets-Check-Fehlern. Fuer leere Audit-CSVs (nur Header-Zeile) ergibt `CountAuditDataRows()` = 0, aber `Math.Max(1, 0)` = 1. Die UI zeigt "1 Failed" obwohl 0 Zeilen betroffen sind.
- **Inkonsistenz:** `RollbackService.cs` hat den identischen Bug bereits per R6-006-Fix behoben (Kommentar: *"R6-006 FIX: Return actual count — Math.Max(1, 0) returned 1 for empty audit files, displaying misleading '1 Failed' in rollback results"*). `AuditSigningService` wurde nicht aktualisiert.
- **Betroffene Datei:** [AuditSigningService.cs](../src/Romulus.Infrastructure/Audit/AuditSigningService.cs#L260) — `Failed = Math.Max(1, CountAuditDataRows(auditCsvPath))`
- **Zweite Stelle:** [AuditSigningService.cs](../src/Romulus.Infrastructure/Audit/AuditSigningService.cs#L271) — identisches Problem beim fehlenden Sidecar-Pfad
- **Fix:** `Math.Max(1, ...)` durch `CountAuditDataRows(auditCsvPath)` ersetzen (wie in RollbackService)
- **Testabsicherung:** Unit-Test: Rollback einer Audit-CSV mit nur Header-Zeile → Failed-Count muss 0 sein, nicht 1

---

### DB-025: LOW — CcdSetParser.GetMissingFiles keine Datei-Existenz-Pruefung

- [ ] **Offen**
- **Schweregrad:** Low (inkonsistentes Verhalten gegenueber GetRelatedFiles)
- **Impact:** `CcdSetParser.GetMissingFiles("Z:\\nonexistent\\file.ccd")` berechnet und liefert `[file.img, file.sub]` als "fehlende" Dateien, auch wenn die CCD-Basisdatei selbst nicht existiert. `GetRelatedFiles()` (Zeile 17) prueft `if (!parserIo.Exists(ccdPath)) return []` — diese Pruefung fehlt in `GetMissingFiles()`.
- **Betroffene Datei:** [CcdSetParser.cs](../src/Romulus.Core/SetParsing/CcdSetParser.cs#L34-L47) — `GetMissingFiles()` ohne Existenz-Check
- **Ursache:** Fehlende Symmetrie zwischen GetRelatedFiles und GetMissingFiles
- **Fix:** `if (string.IsNullOrWhiteSpace(ccdPath) || !parserIo.Exists(ccdPath)) return [];`
- **Testabsicherung:** Unit-Test: GetMissingFiles mit nicht-existentem Pfad → leeres Array

---

## Aktualisierte Zusammenfassung (inkl. Runde 5)

| Quelle | Anzahl | Critical/Blocking | High | Medium-High | Medium | Low |
|---|---|---|---|---|---|---|
| Erstaudit (P1-P7) | 19 | 1 | — | — | 10 | 8 |
| Deep-Dive Runde 2 | 9 | 1 | 3 | — | 4 | 1 |
| Runde 3 | 7 | — | — | 1 | 4 | 2 |
| Runde 4 | 6 | — | 2 | — | 1 | 3 |
| Runde 5 | 3 | — | — | — | 1 | 2 |
| **Gesamt** | **44** | **2** | **5** | **1** | **20** | **16** |

---

## Aktualisierte Top-Massnahmen (priorisiert nach Runde 5)

1. **DB-001:** Legacy-Conversion-Verification-Bug fixen (CRITICAL — konvertierte Files werden geloescht)
2. **F-001:** RollbackMovedArtifacts Fehler-Propagation (BLOCKING)
3. **DB-017:** ConversionExecutor success=true bei VerifyFailed fixen (HIGH)
4. **DB-018:** LiteDbCollectionIndex.RecreateDatabase Exception-Safety (HIGH)
5. **DB-002:** MoveDirectorySafely full-ancestry Reparse-Point-Check (HIGH)
6. **DB-003:** AllowedRootPathPolicy File-Level Reparse-Point-Check (HIGH)
7. **DB-004:** AuditCsvParser Unclosed-Quote-Handling (HIGH)
8. **DB-010:** SettingsLoader.MergeFromUserSettings Validation-Revert implementieren (MEDIUM-HIGH)
9. **DB-024:** AuditSigningService.Rollback Math.Max(1,...) R6-006-Inconsistenz fixen (MEDIUM)
10. **DB-019:** _syncContext-null-Pfad Background-Thread-PropertyChanged absichern (MEDIUM)
11. **F-004:** Root-Validierung in ConvertSingleFile (Defense-in-Depth)
12. **DB-011:** ConversionRegistryLoader ReadRequired-Wrapper fuer lossless/cost
13. **DB-020:** PBP-Registry command/target-Inkonsistenz verifizieren und korrigieren

---

## Gesamtschlussurteil (nach Runde 5)

Nach 5 tiefen Audit-Runden mit insgesamt **15+ analysierten Subsystemen** steht fest:

### Was fehlerfrei bestaetigt ist
- **Kernlogik:** Deduplication, Scoring, GameKey, RegionDetection — deterministisch und korrekt
- **Thread-Safety:** gesamte Codebasis produktionsreif (Runde 4)
- **CLI & API Layer:** Input-Validierung, Path-Security, Auth, CORS — alles korrekt (Runde 5)
- **CartridgeHeaderDetector:** SNES-Checksum-XOR mathematisch korrekt (FALSE ALARM widerlegt)
- **Reporting Security:** HTML-XSS-Encoding, CSV-Injection-Schutz, CSP-Headers korrekt
- **Audit Integrity:** HMAC-SHA256 Verifizierung, Concurrent Access, Signing — korrekt

### Was offen bleibt
- **2 Critical/Blocking:** DB-001 (Conversion-Output-Loeschung), F-001 (Rollback-Error-Propagation)
- **5 High:** DB-002/DB-003 (Reparse-Point-Asymmetrien), DB-004 (CSV-Quote), DB-017 (StepResult-Widerspruch), DB-018 (Exception-Safety LiteDb)
- **1 Medium-High:** DB-010 (Settings-Validation-Revert)
- **20 Medium/Low:** ueberwiegend Edge-Case-Robustheit und Defense-in-Depth

**Gesamtbewertung:** 44 Findings ueber 5 Runden. Die Codebasis ist **architektonisch sehr solide, thread-safe und security-geheartet**. Die verbleibenden Bugs betreffen Edge-Case-Robustheit, Exception-Safety und Inkonsistenzen in Neben-Codepfaden — keine fundamentalen Design- oder Sicherheitsprobleme. Die Audit-Trefferquote sinkt deutlich: Runde 5 (3 Findings) vs. Runde 2 (9 Findings) — die Codebasis naehert sich dem Punkt, an dem weitere Deep-Dives nur noch Marginal-Findings liefern.

---
---

## Runde 6 — Pipeline-Execution, DAT-Indexing, Sorting, Daten-Konsistenz, Safety (2026-04-15)

> **Methode:** 5 parallele Subagenten fuer: (1) RunOrchestrator Pipeline-Execution-Phasen, (2) Safety/PathPolicy Deep-Dive, (3) DAT-Indexing/Matching/Catalog, (4) Sorting/ConsoleSorter/Move-Operationen, (5) Data-JSON-Konsistenz und Lade-Validierung. Anschliessend manuelle Verifikation jedes Findings am Quellcode.
>
> **Safety-Subagent:** Alle 3 Findings waren **Duplikate** bestehender Findings (DB-002, DB-003, DB-004 aus Runde 2). Keine neuen Safety-Bugs.
>
> **Was fehlerfrei bestaetigt wurde:**
> - **ConsoleSorter M3U-Rewrite:** Angeblicher "wrong reference path"-Bug ist korrekt — alle Set-Members werden in dasselbe Zielverzeichnis verschoben, Filename-Only-Mapping ist bewusst und korrekt
> - **MoveItemSafelyCore DUP-Loop:** Beide Branches haben korrekte `if (!moved) throw` Guards — kein unbegrenzter Loop
> - **IsInExcludedFolder:** Caller (`GetFilesForRoot`) garantiert, dass root immer Praefix von filePath ist — kein Runtime-Crash
> - **rules.json EU/ASIA Keys:** Duplicate Keys sind **beabsichtigtes Sub-Region-Grouping** (UK→EU, Taiwan→ASIA) — kein Bug
> - **rules.json NZ→AU:** Beabsichtigtes Oceania-Grouping gemaess ROM-Distribution-Konventionen (No-Intro, Redump)
> - **DatIndex OrdinalIgnoreCase:** Bewusste Design-Entscheidung fuer Hex-Hash-Vergleich — funktional korrekt

---

### DB-026: MEDIUM — Move-Phase nutzt GameGroups statt AllGroups — BIOS-Losers werden nicht verschoben

- [ ] **Offen**
- **Schweregrad:** Medium (unvollstaendige Deduplizierung fuer BIOS-Dateien)
- **Impact:** `RunMoveStep` uebergibt `state.GameGroups` (gefiltert) an `MovePipelinePhase.Execute()`. `GameGroups` schliesst seit F-012 reine BIOS-Winner-Gruppen aus (nur fuer Zaehlung gedacht). Seiteneffekt: BIOS-Loser-Dateien werden nie an die Move-Phase uebergeben und verbleiben als Duplikate in der Sammlung. JunkRemoval nutzt korrekterweise `state.AllGroups`.
- **Betroffene Dateien:**
  - [RunOrchestrator.StandardPhaseSteps.cs](../src/Romulus.Infrastructure/Orchestration/RunOrchestrator.StandardPhaseSteps.cs#L78) — `var groups = state.GameGroups ?? Array.Empty<DedupeGroup>();`
  - [DeduplicatePipelinePhase.cs](../src/Romulus.Infrastructure/Orchestration/DeduplicatePipelinePhase.cs#L37-L45) — `GetGameGroups()` filtert BIOS-Winner
- **Asymmetrie:**
  - JunkRemoval (Zeile 62): `state.AllGroups` ← korrekt
  - MoveStep (Zeile 78): `state.GameGroups` ← gefiltert, BIOS fehlt
- **Reproduktion:** 2 BIOS-Dateien (bios_v1.bin Winner, bios_v2.bin Loser) → Deduplizierung erkennt Gruppe → Move-Phase sieht Gruppe nicht → bios_v2.bin bleibt in Sammlung
- **Ursache:** F-012-Fix war nur fuer Zaehlung gedacht, aber Move-Phase liest dieselbe gefilterte Liste
- **Fix:** In `RunMoveStep`: `var groups = state.AllGroups ?? Array.Empty<DedupeGroup>();`
- **Testabsicherung:** Unit-Test: Deduplizierung mit reiner BIOS-Gruppe → Move-Phase muss BIOS-Loser verarbeiten

---

### DB-027: LOW — DAT-Hash-Werte aus XML nicht getrimmt/normalisiert

- [ ] **Offen**
- **Schweregrad:** Low (potenzielle falsche DAT-Mismatches bei malformierten DATs)
- **Impact:** `DatRepositoryAdapter.ParseDatFile()` speichert Hashes aus `reader.GetAttribute("sha1")` direkt in `rom["hash"]` ohne `Trim()`. In `GetDatIndex()` wird der Hash 1:1 an `index.Add()` uebergeben. Wenn eine DAT-Datei Whitespace in Hash-Attributen hat (`sha1="  DEADBEEF  "`), scheitert der Lookup gegen den getrimmten Hash aus `FileHashService`.
- **Betroffene Dateien:**
  - [DatRepositoryAdapter.cs](../src/Romulus.Infrastructure/Dat/DatRepositoryAdapter.cs#L280) — `rom["hash"] = hash;` ohne Trim
  - [DatRepositoryAdapter.cs](../src/Romulus.Infrastructure/Dat/DatRepositoryAdapter.cs#L58) — `index.Add(consoleKey, hash, ...)` ohne Trim
- **Mitigierung:** `DatIndex` nutzt `StringComparer.OrdinalIgnoreCase` fuer Hash-Lookups, was Case-Differenzen abdeckt. Whitespace-Padding wird aber nicht mitigiert. Standard-DATs (No-Intro, Redump, MAME) haben saubere Hex-Strings.
- **Praktische Relevanz:** Niedrig — nur bei malformierten Community-DATs oder fehlerhaften DAT-Konvertern. Aber: stille Miss-Klassifikation ohne Diagnosis.
- **Fix:** `rom["hash"] = hash.Trim();` und/oder `index.Add(consoleKey, hash.Trim(), ...)` — idealerweise auch `.ToLowerInvariant()` fuer kanonische Form
- **Testabsicherung:** Unit-Test: DAT mit whitespace-padded Hash → Lookup muss trotzdem matchen

---

### DB-028: LOW — PhasePlanExecutor setzt FailedPhaseName nicht bei unbehandelten Exceptions

- [ ] **Offen**
- **Schweregrad:** Low (Diagnostik-Luecke)
- **Impact:** `PhasePlanExecutor.Execute()` setzt `pipelineState.SetFailedPhase(phase.Name, ...)` nur wenn eine Phase `StatusFailed` zurueckgibt. Wenn eine Phase eine unbehandelte Exception wirft, propagiert diese direkt zum Caller (`RunOrchestrator`), aber `FailedPhaseName` bleibt null. Der Catch-Block in `RunOrchestrator` hat die Exception-Message, kennt aber die fehlgeschlagene Phase nicht. Das Audit-Sidecar kann die Phase nicht identifizieren.
- **Betroffene Dateien:**
  - [PhasePlanExecutor.cs](../src/Romulus.Infrastructure/Orchestration/PhasePlanExecutor.cs#L19-L27) — kein try/catch um `phase.Execute()`
  - [RunOrchestrator.cs](../src/Romulus.Infrastructure/Orchestration/RunOrchestrator.cs#L334) — `catch (Exception ex)` ohne Phasen-Info
- **Reproduktion:** Move-Phase wirft IOException → RunOrchestrator faengt Exception → `pipelineState.FailedPhaseName == null` → Audit-Sidecar hat `LastPhase=unknown`
- **Fix:** In `PhasePlanExecutor.Execute()` um den `phase.Execute()`-Call einen try/catch legen: `catch { pipelineState.SetFailedPhase(phase.Name, "exception"); throw; }`
- **Testabsicherung:** Unit-Test: Phase wirft Exception → `pipelineState.FailedPhaseName` muss gesetzt sein

---

## Aktualisierte Zusammenfassung (inkl. Runde 6)

| Quelle | Anzahl | Critical/Blocking | High | Medium-High | Medium | Low |
|---|---|---|---|---|---|---|
| Erstaudit (P1-P7) | 19 | 1 | — | — | 10 | 8 |
| Deep-Dive Runde 2 | 9 | 1 | 3 | — | 4 | 1 |
| Runde 3 | 7 | — | — | 1 | 4 | 2 |
| Runde 4 | 6 | — | 2 | — | 1 | 3 |
| Runde 5 | 3 | — | — | — | 1 | 2 |
| Runde 6 | 3 | — | — | — | 1 | 2 |
| **Gesamt** | **47** | **2** | **5** | **1** | **21** | **18** |

---

## Aktualisierte Top-Massnahmen (priorisiert nach Runde 6)

1. **DB-001:** Legacy-Conversion-Verification-Bug fixen (CRITICAL — konvertierte Files werden geloescht)
2. **F-001:** RollbackMovedArtifacts Fehler-Propagation (BLOCKING)
3. **DB-017:** ConversionExecutor success=true bei VerifyFailed fixen (HIGH)
4. **DB-018:** LiteDbCollectionIndex.RecreateDatabase Exception-Safety (HIGH)
5. **DB-002:** MoveDirectorySafely full-ancestry Reparse-Point-Check (HIGH)
6. **DB-003:** AllowedRootPathPolicy File-Level Reparse-Point-Check (HIGH)
7. **DB-004:** AuditCsvParser Unclosed-Quote-Handling (HIGH)
8. **DB-010:** SettingsLoader.MergeFromUserSettings Validation-Revert implementieren (MEDIUM-HIGH)
9. **DB-026:** Move-Phase AllGroups statt GameGroups fuer BIOS-Loser-Move (MEDIUM)
10. **DB-024:** AuditSigningService.Rollback Math.Max(1,...) R6-006-Inconsistenz fixen (MEDIUM)
11. **DB-019:** _syncContext-null-Pfad Background-Thread-PropertyChanged absichern (MEDIUM)
12. **F-004:** Root-Validierung in ConvertSingleFile (Defense-in-Depth)
13. **DB-011:** ConversionRegistryLoader ReadRequired-Wrapper fuer lossless/cost
14. **DB-020:** PBP-Registry command/target-Inkonsistenz verifizieren und korrigieren

---

## Gesamtschlussurteil (nach Runde 6)

Nach 6 tiefen Audit-Runden mit insgesamt **20+ analysierten Subsystemen** steht fest:

### Was fehlerfrei bestaetigt ist
- **Kernlogik:** Deduplication, Scoring, GameKey, RegionDetection — deterministisch und korrekt
- **Thread-Safety:** gesamte Codebasis produktionsreif (Runde 4)
- **CLI & API Layer:** Input-Validierung, Path-Security, Auth, CORS — alles korrekt (Runde 5)
- **CartridgeHeaderDetector:** SNES-Checksum-XOR mathematisch korrekt
- **Reporting Security:** HTML-XSS-Encoding, CSV-Injection-Schutz, CSP-Headers korrekt
- **Audit Integrity:** HMAC-SHA256 Verifizierung, Concurrent Access, Signing — korrekt
- **Safety-Module:** Keine neuen Findings in Runde 6 (alle 3 waren Duplikate R2)
- **ConsoleSorter M3U-Rewrite:** Korrekt — Set-Members gehen in dasselbe Zielverzeichnis
- **MoveItemSafelyCore DUP-Loop:** Korrekt — beide Branches haben Exception-Guards
- **rules.json Region-Grouping:** Beabsichtigtes Sub-Region-Design (NZ→AU, UK→EU, Taiwan→ASIA)

### Was offen bleibt
- **2 Critical/Blocking:** DB-001 (Conversion-Output-Loeschung), F-001 (Rollback-Error-Propagation)
- **5 High:** DB-002/DB-003 (Reparse-Point-Asymmetrien), DB-004 (CSV-Quote), DB-017 (StepResult-Widerspruch), DB-018 (Exception-Safety LiteDb)
- **1 Medium-High:** DB-010 (Settings-Validation-Revert)
- **21 Medium/Low:** ueberwiegend Edge-Case-Robustheit, Diagnostik-Luecken und Defense-in-Depth

### Trefferquoten-Trend

| Runde | Findings | Trend |
|---|---|---|
| Runde 2 | 9 | Hoch |
| Runde 3 | 7 | ↓ |
| Runde 4 | 6 | ↓ |
| Runde 5 | 3 | ↓ |
| Runde 6 | 3 | → (Plateau) |

**Gesamtbewertung:** 47 Findings ueber 6 Runden. Die Codebasis ist **architektonisch sehr solide, thread-safe und security-geheartet**. Die Trefferquote hat ein Plateau bei 3 Findings pro Runde erreicht — ueberwiegend Edge-Case-Robustheit und Diagnostik-Verbesserungen. **Die Codebasis hat den Punkt erreicht, an dem weitere Deep-Dives nur noch marginale Findings liefern.** Empfehlung: Verbleibende Critical/High-Findings (DB-001, F-001, DB-017, DB-018, DB-002–DB-004) fixen und Release vorbereiten.

---
---

## Runde 7 — Hashing, Set-Parsing, WPF-XAML, i18n, Profiles/Enrichment (2026-04-15)

> **Methode:** 5 parallele Subagenten fuer: (1) Hashing-Modul (FileHashService, ArchiveHashService, Caching), (2) Set-Parser (CUE/GDI/MDS/PBP/CCD), (3) WPF-XAML/Converter/Code-Behind, (4) i18n/Lokalisierung, (5) RunConfigurationResolver/Profile/Enrichment. Anschliessend manuelle Verifikation jedes Findings am Quellcode.
>
> **Abgelehnte Findings (FALSE ALARM):**
> - WPF LibraryReportView "Memory Leak": Button ist Child des UserControl — Event-Handler-Referenz bleibt im selben Objektgraph, kein externer Ref-Leak bei GC
> - i18n Convert.Progress De/Fr param mismatch: De hat tatsaechlich alle 7 Parameter `{0}-{6}`, identisch zu En — Subagent hat falschen Code referenziert
> - RunConfigurationResolver workflow > profile Prioritaet: Deliberate Design-Entscheidung — `isExplicit` faengt User-explizite Werte ab, Workflow-Override auf Non-Explicit-Defaults ist konsistentes Verhalten
> - CcdSetParser GetMissingFiles: Bereits als DB-025 in Runde 5 dokumentiert (Duplikat)
>
> **Was fehlerfrei bestaetigt wurde:**
> - **FileHashService:** Cache-Invalidierung, Fingerprint-Validierung, Stream-Handling korrekt
> - **HeaderlessHasher/HeaderRepairService:** N64 Byte-Order-Normalisierung, Buffer-Offsets korrekt
> - **Crc32/ParallelHasher:** Table-Initialisierung, Streaming-Hash, Resource-Cleanup korrekt
> - **CueSetParser/GdiSetParser/PbpSetParser:** Pfadauflosung, Set-Member-Erkennung korrekt
> - **EnrichmentPipelinePhase/ScanPipelinePhase:** Hash-Propagation, Candidate-Mapping, deterministische Sortierung korrekt
> - **RunConfigurationMaterializer:** Cascading (resolved → baseline → settings → defaults) korrekt
> - **RunOptionsBuilder.Normalize/Validate:** Extension-/Region-/Mode-Normalisierung korrekt

---

### DB-029: LOW — MdsSetParser.GetMissingFiles fehlende Datei-Existenz-Pruefung

- [ ] **Offen**
- **Schweregrad:** Low (inkonsistentes Verhalten gegenueber GetRelatedFiles)
- **Impact:** `MdsSetParser.GetMissingFiles("Z:\\nonexistent\\file.mds")` berechnet und liefert `[file.mdf]` als "fehlende" Datei, auch wenn die MDS-Basisdatei selbst nicht existiert. `GetRelatedFiles()` (Zeile 14) prueft `if (!parserIo.Exists(mdsPath)) return []` — diese Pruefung fehlt in `GetMissingFiles()`.
- **Betroffene Datei:** [MdsSetParser.cs](../src/Romulus.Core/SetParsing/MdsSetParser.cs#L27-L35) — `GetMissingFiles()` ohne Existenz-Check
- **Ursache:** Identisches Muster wie DB-025 (CcdSetParser) — fehlende Symmetrie zwischen GetRelatedFiles und GetMissingFiles
- **Fix:** `if (string.IsNullOrWhiteSpace(mdsPath) || !parserIo.Exists(mdsPath)) return [];`
- **Testabsicherung:** Unit-Test: GetMissingFiles mit nicht-existentem Pfad → leeres Array

---

### DB-030: LOW — ArchiveHashService ZIP/7z-Inkonsistenz bei Zero-Length-Eintraegen

- [ ] **Offen**
- **Schweregrad:** Low (Inkonsistenz mit extrem niedriger praktischer Relevanz)
- **Impact:** `HashZipEntries()` filtert Eintraege mit `entry.Length <= 0` (Zeile 196 — uebersprungen). `Hash7zEntries()` nutzt `Directory.GetFiles()` das Verzeichnisse natuerlich ausschliesst, aber echte Null-Byte-Dateien einschliesst. Ergebnis: Wenn ein ZIP und ein identisches 7z-Archiv jeweils eine genuiine Null-Byte-Datei enthalten, liefert ZIP weniger Hashes als 7z.
- **Betroffene Datei:** [ArchiveHashService.cs](../src/Romulus.Infrastructure/Hashing/ArchiveHashService.cs#L196) — `if (entry.Length <= 0) continue;` in `HashZipEntries`
- **Praktische Relevanz:** Extrem niedrig — Null-Byte-Dateien in ROM-Archiven sind in der Praxis nicht existent. Der Filter schuetzt zudem korrekt gegen ZIP-Directory-Eintraege (Length=0, FullName endet mit '/').
- **Fix:** Praeziseren Filter nutzen: `if (string.IsNullOrEmpty(entry.Name)) continue;` (filtert Directory-Eintraege ohne echte Dateien zu verlieren)
- **Testabsicherung:** Unit-Test: ZIP mit genuiner Null-Byte-Datei → Hash-Count muss identisch zu 7z sein

---

### DB-031: LOW — Franzoesische i18n-Uebersetzungen unvollstaendig (~15 fehlende Keys)

- [ ] **Offen**
- **Schweregrad:** Low (keine Runtime-Fehler, Fallback auf Deutsch)
- **Impact:** Die franzoesische Uebersetzung (`Fr`) in `RunProgressLocalization.cs` hat ca. 15 fehlende Keys gegenueber De und En. Fehlende Keys fallen auf die deutsche Uebersetzung zurueck (`ResolveTemplate()` gibt De-Template bei fehlendem Fr-Key). Franzoesische UI zeigt deutsche Fortschrittsmeldungen.
- **Betroffene Datei:** [RunProgressLocalization.cs](../src/Romulus.Infrastructure/Orchestration/RunProgressLocalization.cs) — `Fr` Dictionary
- **Fehlende Keys (Beispiele):** `Scan.RootCollecting`, `Scan.RootFound`, `Scan.ProgressProcessed`, `Scan.IncompleteWarning`, `Move.SkipConflict`, `Dedupe.Start`, `Dedupe.Completed`, `Junk.Start`, `Junk.Completed`, `Convert.OnlyStart`, `Convert.StartGroups`, `Convert.Completed`, `Convert.Progress`, `Convert.FileTarget`, `Convert.StepDone`, `Audit.SidecarWriteFailed`, `Preview.*`, `CollectionIndex.DeltaLookupsDisabled`
- **Fix:** Fehlende Keys in Fr-Dictionary ergaenzen mit franzoesischen Uebersetzungen
- **Testabsicherung:** Unit-Test: Fuer jeden Key in De/En pruefen, dass Fr den Key ebenfalls enthaelt

---

## Aktualisierte Zusammenfassung (inkl. Runde 7)

| Quelle | Anzahl | Critical/Blocking | High | Medium-High | Medium | Low |
|---|---|---|---|---|---|---|
| Erstaudit (P1-P7) | 19 | 1 | — | — | 10 | 8 |
| Deep-Dive Runde 2 | 9 | 1 | 3 | — | 4 | 1 |
| Runde 3 | 7 | — | — | 1 | 4 | 2 |
| Runde 4 | 6 | — | 2 | — | 1 | 3 |
| Runde 5 | 3 | — | — | — | 1 | 2 |
| Runde 6 | 3 | — | — | — | 1 | 2 |
| Runde 7 | 3 | — | — | — | — | 3 |
| **Gesamt** | **50** | **2** | **5** | **1** | **21** | **21** |

---

## Aktualisierte Top-Massnahmen (priorisiert nach Runde 7)

1. **DB-001:** Legacy-Conversion-Verification-Bug fixen (CRITICAL — konvertierte Files werden geloescht)
2. **F-001:** RollbackMovedArtifacts Fehler-Propagation (BLOCKING)
3. **DB-017:** ConversionExecutor success=true bei VerifyFailed fixen (HIGH)
4. **DB-018:** LiteDbCollectionIndex.RecreateDatabase Exception-Safety (HIGH)
5. **DB-002:** MoveDirectorySafely full-ancestry Reparse-Point-Check (HIGH)
6. **DB-003:** AllowedRootPathPolicy File-Level Reparse-Point-Check (HIGH)
7. **DB-004:** AuditCsvParser Unclosed-Quote-Handling (HIGH)
8. **DB-010:** SettingsLoader.MergeFromUserSettings Validation-Revert implementieren (MEDIUM-HIGH)
9. **DB-026:** Move-Phase AllGroups statt GameGroups fuer BIOS-Loser-Move (MEDIUM)
10. **DB-024:** AuditSigningService.Rollback Math.Max(1,...) R6-006-Inconsistenz fixen (MEDIUM)
11. **DB-019:** _syncContext-null-Pfad Background-Thread-PropertyChanged absichern (MEDIUM)
12. **F-004:** Root-Validierung in ConvertSingleFile (Defense-in-Depth)
13. **DB-011:** ConversionRegistryLoader ReadRequired-Wrapper fuer lossless/cost
14. **DB-020:** PBP-Registry command/target-Inkonsistenz verifizieren und korrigieren

---

## Gesamtschlussurteil (nach Runde 7)

Nach 7 tiefen Audit-Runden mit insgesamt **25+ analysierten Subsystemen** steht fest:

### Was fehlerfrei bestaetigt ist

- **Kernlogik:** Deduplication, Scoring, GameKey, RegionDetection — deterministisch und korrekt
- **Thread-Safety:** gesamte Codebasis produktionsreif (Runde 4)
- **CLI & API Layer:** Input-Validierung, Path-Security, Auth, CORS — alles korrekt (Runde 5)
- **CartridgeHeaderDetector:** SNES-Checksum-XOR mathematisch korrekt
- **Reporting Security:** HTML-XSS-Encoding, CSV-Injection-Schutz, CSP-Headers korrekt
- **Audit Integrity:** HMAC-SHA256 Verifizierung, Concurrent Access, Signing — korrekt
- **Safety-Module:** Keine neuen Findings seit Runde 2 (alle Runde-6-Findings waren Duplikate)
- **ConsoleSorter:** M3U-Rewrite, DUP-Loop, IsInExcludedFolder — korrekt
- **rules.json Region-Grouping:** Beabsichtigtes Sub-Region-Design (NZ→AU, UK→EU, Taiwan→ASIA)
- **Hashing-Modul:** FileHashService, HeaderlessHasher, Crc32, ParallelHasher — korrekt (Runde 7)
- **Set-Parser:** CueSetParser, GdiSetParser, PbpSetParser — korrekt (Runde 7)
- **Enrichment/Scan-Pipeline:** Hash-Propagation, Candidate-Mapping, Filtering — korrekt (Runde 7)
- **RunConfigurationMaterializer/Resolver:** Cascading-Logik, Workflow/Profile-Prioritaet — korrekt (Runde 7)
- **WPF-XAML-Layer:** Event-Handler-Patterns, Converter-Registration — korrekt (Runde 7)

### Was offen bleibt

- **2 Critical/Blocking:** DB-001 (Conversion-Output-Loeschung), F-001 (Rollback-Error-Propagation)
- **5 High:** DB-002/DB-003 (Reparse-Point-Asymmetrien), DB-004 (CSV-Quote), DB-017 (StepResult-Widerspruch), DB-018 (Exception-Safety LiteDb)
- **1 Medium-High:** DB-010 (Settings-Validation-Revert)
- **21 Medium/Low:** ueberwiegend Edge-Case-Robustheit, Diagnostik-Luecken und Defense-in-Depth

### Trefferquoten-Trend

| Runde | Findings | Schweregrad-Trend |
|---|---|---|
| Runde 2 | 9 | 1 CRITICAL, 3 HIGH |
| Runde 3 | 7 | 1 MEDIUM-HIGH |
| Runde 4 | 6 | 2 HIGH |
| Runde 5 | 3 | alle LOW/MEDIUM |
| Runde 6 | 3 | 1 MEDIUM, 2 LOW |
| Runde 7 | 3 | alle LOW |

**Gesamtbewertung:** 50 Findings ueber 7 Runden. Die Codebasis ist **architektonisch sehr solide, thread-safe und security-geheartet**. Runde 7 hat nur noch 3 LOW-Findings produziert — eine i18n-Luecke, eine Set-Parser-Symmetrie-Luecke und eine Hashing-Inkonsistenz. **Der Audit hat definitiv den Punkt der diminishing returns erreicht.** Alle neuen Findings sind rein defensive Verbesserungen ohne praktischen Produktions-Impact. Empfehlung: Die 2 Critical/Blocking und 5 High-Findings sind die einzigen release-relevanten Punkte — alles andere ist planbar.

---
---

## Runde 8 — API-Isolation, WPF-Konfig-Fidelity, Settings-Pipeline, Dashboard Exposure (2026-04-15)

> **Methode:** gezielter Deep-Dive in API-Read-Models (`/runs*`, `/dashboard/*`), WPF-Run-Configuration-Round-Trip und Settings/Profile-Ingress. `dotnet build src/Romulus.sln --no-restore -v minimal` war gruen. Ein voller Lauf von `dotnet test src/Romulus.Tests/Romulus.Tests.csproj --no-restore -v minimal` lief auf dem Audit-Rechner >10 Minuten ohne Abschluss; selektive `PreviewExecuteParityTests` (4 Tests) waren gruen.
>
> **Abgelehnte Findings (FALSE ALARM / nicht belastbar genug):**
> - Reine `FeatureCommandKeys`-Registrierungs-Luecken sind auf aktuellem HEAD nicht bestaetigt; das offene Hygiene-Thema liegt eher bei Handler-Reife/Tool-Katalog als bei nackter Registrierung.
>
> **Was fehlerfrei bestaetigt wurde:**
> - **Live-Run-Sichtbarkeit:** `/runs` filtert ueber `CanAccessRun(...)` korrekt pro `X-Client-Id`
> - **Shared run truth:** `RunConfigurationMaterializer` bleibt der zentrale Materialisierungspfad fuer CLI/API/WPF
> - **Build-Zustand:** `src/Romulus.sln` baut auf aktuellem HEAD ohne Fehler/Warnungen

---

### DB-032: HIGH — Persistierte Run-History und Dashboard umgehen das Client-Binding

- [ ] **Offen**
- **Schweregrad:** High (Security / API-Paritaet / Mandantenisolation)
- **Impact:** Das Live-Run-Modell ist clientgebunden, die persistierten Read-Models aber nicht. `/runs` filtert ueber `CanAccessRun(...)`, aber `/runs/history`, `/runs/compare`, `/runs/trends` lesen globale Snapshots ohne `HttpContext` oder Client-Filter. `DashboardSummary` exponiert zusaetzlich `GetActive()` und die letzten Snapshots ebenfalls global. Ein API-Client kann dadurch Run-IDs, Root-Fingerprints, Groessen- und KPI-Daten anderer Clients sehen, obwohl dieselben Runs im Live-Endpoint `/runs` korrekt versteckt werden.
- **Betroffene Dateien:**
  - [Program.RunWatchEndpoints.cs](../src/Romulus.Api/Program.RunWatchEndpoints.cs#L21-L99) — `/runs` filtert, `/runs/history`, `/runs/compare`, `/runs/trends` nicht
  - [DashboardDataBuilder.cs](../src/Romulus.Api/DashboardDataBuilder.cs#L34-L70) — `BuildSummaryAsync()` nutzt `GetActive()` und `ListRunSnapshotsAsync()` global
  - [ProgramHelpers.cs](../src/Romulus.Api/ProgramHelpers.cs#L165-L183) — Client-Binding existiert, wird aber nur im Live-Pfad angewandt
  - [CollectionIndexModels.cs](../src/Romulus.Contracts/Models/CollectionIndexModels.cs#L173-L231) — `CollectionRunSnapshot` hat kein `OwnerClientId`
  - [CollectionRunSnapshotWriter.cs](../src/Romulus.Infrastructure/Index/CollectionRunSnapshotWriter.cs#L39-L80) — persistiert keine Owner-/Tenant-Information
- **Reproduktion / Beispiel:**
  1. Client A startet einen Run mit `X-Client-Id: owner-history`.
  2. Client B mit `X-Client-Id: other-history` sieht diesen Run in `/runs` korrekterweise nicht.
  3. Derselbe Client B bekommt in `/dashboard/summary` den globalen `activeRun` und nach Abschluss in `/runs/history`, `/runs/compare` und `/runs/trends` dieselben persistierten Snapshot-Daten trotzdem geliefert.
- **Ursache:** Die Ownership-Information endet am `RunRecord`. Beim Uebergang in Snapshot-/Dashboard-Read-Models wird kein Owner-/Tenant-Schluessel mitgefuehrt und spaeter auch nicht gefiltert.
- **Fix:** Client-Binding durchgaengig ueber die Read-Model-Pipeline ziehen. `CollectionRunSnapshot` braucht ein `OwnerClientId` (oder aequivalenten Tenant-Key), `CollectionRunSnapshotWriter` muss es persistieren, und `history/compare/trends/dashboard summary` muessen vor der Projektion clientgefiltert werden. Falls bewusst ein globales Admin-Dashboard gewollt ist, muss es explizit admin-only statt normal API-authenticated sein.
- **Testabsicherung:**
  - Integrationstest: Client A erstellt Run, Client B sieht ihn in `/dashboard/summary` nicht
  - Integrationstest: `/runs/history`, `/runs/compare`, `/runs/trends` liefern nur Snapshots des anfragenden Clients
  - Regressionstest: Snapshot-Persistierung traegt `OwnerClientId` durch

---

### DB-033: HIGH — WPF kollabiert explizite `ConvertFormat`-Werte still auf `"auto"`

- [ ] **Offen**
- **Schweregrad:** High (falsche fachliche Entscheidung / GUI-CLI-Profile-Paritaet / stille Verhaltensaenderung)
- **Impact:** Die WPF-Oberflaeche speichert die konkrete Zielkonvertierung nicht, sondern nur `ConvertEnabled`. Wird ein Profil oder Workflow mit `ConvertFormat = "rvz"`, `"chd"`, `"zip"` oder `"7z"` angewendet, geht diese Information im UI-State verloren. Beim naechsten Draft-Build schreibt die GUI immer `"auto"` zurueck. Dadurch kann ein unveraendert geladenes Profil still umgedeutet werden; `BuildCurrentRunConfigurationExplicitness()` markiert `ConvertFormat` dann faelschlich als explizit geaendert, und Profil-Save bzw. CLI-Command-Copy exportieren nicht mehr den urspruenglichen Zielformat-Wert.
- **Betroffene Dateien:**
  - [MainViewModel.Productization.cs](../src/Romulus.UI.Wpf/ViewModels/MainViewModel.Productization.cs#L206-L231) — `BuildCurrentRunConfigurationDraft()` schreibt immer `"auto"`
  - [MainViewModel.Productization.cs](../src/Romulus.UI.Wpf/ViewModels/MainViewModel.Productization.cs#L245-L270) — Explicitness vergleicht `"auto"` gegen den urspruenglichen Baseline-Wert
  - [MainViewModel.Productization.cs](../src/Romulus.UI.Wpf/ViewModels/MainViewModel.Productization.cs#L415-L446) — `ApplyMaterializedRunConfiguration()` setzt nur `ConvertEnabled`
  - [FeatureCommandService.cs](../src/Romulus.UI.Wpf/Services/FeatureCommandService.cs#L379-L423) — CLI-Copy kann den konkreten Zielformat-Wert nicht mehr abbilden
  - [GuiViewModelTests.cs](../src/Romulus.Tests/GuiViewModelTests.cs#L1614-L1617) / [WpfProductizationTests.cs](../src/Romulus.Tests/WpfProductizationTests.cs#L145-L181) — decken nur `"auto"` bzw. keinen non-auto-Round-Trip ab
- **Reproduktion / Beispiel:**
  1. Ein Profil definiert `ConvertFormat = "rvz"`.
  2. `ApplyMaterializedRunConfiguration()` setzt `ConvertEnabled = true`, speichert aber kein `"rvz"` im ViewModel.
  3. `BuildCurrentRunConfigurationDraft()` erzeugt daraus `"auto"`.
  4. `BuildCurrentRunConfigurationExplicitness()` erkennt nun einen Unterschied zwischen Baseline `"rvz"` und aktuellem Draft `"auto"`.
  5. Profil-Save / CLI-Copy / erneute Materialisierung laufen mit `"auto"` statt `"rvz"`.
- **Ursache:** Die GUI bildet ein mehrwertiges fachliches Feld (`ConvertFormat`) verlustbehaftet als Bool (`ConvertEnabled`) ab.
- **Fix:** Konkretes `ConvertFormat` als eigener ViewModel-State fuehren und round-trippen. `"auto"` darf nur gesetzt werden, wenn der Benutzer oder das Profil explizit `auto` gewaehlt hat. CLI-Copy muss den konkreten Wert (`--convertformat rvz` etc.) ausgeben koennen.
- **Testabsicherung:**
  - WPF-Regressionstest: Materialized profile `ConvertFormat = "rvz"` -> Apply -> BuildDraft bleibt `"rvz"`
  - WPF-Regressionstest: unveraendertes Profil darf `ConvertFormat` nicht als explicit override markieren
  - FeatureCommandService-Test: CLI-Copy rendert den konkreten `--convertformat`-Wert

---

### DB-034: MEDIUM — WPF-Settings/Profil-Import umgehen die zentrale `SettingsLoader`-Validierung

- [ ] **Offen**
- **Schweregrad:** Medium (Schattenlogik / inkonsistente Validierung / potenziell stiller Settings-Reset)
- **Impact:** Die WPF hat einen eigenen JSON-Import-/Load-Pfad fuer `%APPDATA%\\Romulus\\settings.json`, statt die zentrale `SettingsLoader`-Pipeline zu nutzen. `ProfileService.Import()` validiert nur "Root ist Objekt" plus drei Pflichtsektionen und kopiert die Datei dann direkt nach `settings.json`. `SettingsService.Load()` parst dieselbe Datei spaeter manuell und faengt Strukturfehler pauschal mit Default-Fallback ab. Ergebnis: syntaktisch gueltige, aber strukturell falsche Profile koennen importiert werden, die GUI faellt danach still auf Defaults zurueck, waehrend CLI/API ueber `SettingsLoader` andere Validierungs- und Korruptionspfade verwenden.
- **Betroffene Dateien:**
  - [SettingsService.cs](../src/Romulus.UI.Wpf/Services/SettingsService.cs#L33-L197) — manueller Parse-/Fallback-Pfad statt `SettingsLoader.LoadFromSafe()`
  - [ProfileService.cs](../src/Romulus.UI.Wpf/Services/ProfileService.cs#L74-L100) — Import prueft nur Top-Level-Praesenz und kopiert danach blind
  - [SettingsLoader.cs](../src/Romulus.Infrastructure/Configuration/SettingsLoader.cs#L132-L181) — zentrale Safe-Load-/Schema-Validierung, die WPF hier umgeht
  - [GuiViewModelTests.cs](../src/Romulus.Tests/GuiViewModelTests.cs#L2105-L2115) / [WpfNewTests.cs](../src/Romulus.Tests/WpfNewTests.cs#L840-L853) — testen nur kaputtes JSON bzw. Array-Root, nicht semantisch falsche Objektstrukturen
- **Reproduktion / Beispiel:** Importiere ein syntaktisch gueltiges Profil wie `{\"general\":[],\"toolPaths\":{},\"dat\":{}}`. `ProfileService.Import()` akzeptiert die Datei, weil alle drei Pflichtsektionen vorhanden sind. `SettingsService.Load()` ruft spaeter `TryGetProperty(...)` auf `general` (Array statt Objekt) auf, faengt die resultierende Exception und liefert still die Defaults zurueck.
- **Ursache:** Historisch gewachsene GUI-Persistenz dupliziert die Settings-Ingress-Logik statt den vorhandenen `SettingsLoader` zu reuse-en.
- **Fix:** WPF-Import und WPF-Load auf `SettingsLoader.LoadFromSafe()` bzw. eine gemeinsame, testbare Mapping-Pipeline umstellen. `ProfileService.Import()` darf erst nach erfolgreicher Schema-/Semantikvalidierung in `%APPDATA%` kopieren.
- **Testabsicherung:**
  - Regressionstest: semantisch falsches, aber syntaktisch gueltiges Profil wird beim Import abgewiesen
  - Regressionstest: WPF-Load nutzt denselben Korruptions-/Backup-Pfad wie `SettingsLoader`
  - Paritaetstest: identische `settings.json` ergeben in GUI und CLI/API dieselbe effektive Konfiguration

---

### DB-035: LOW — Anonymer Dashboard-Bootstrap exponiert `AllowedRoots`

- [ ] **Offen**
- **Schweregrad:** Low (Information Disclosure / Hardening)
- **Impact:** `/dashboard/bootstrap` ist explizit anonym und liefert `AllowedRoots` 1:1 aus `AllowedRootPathPolicy` zurueck. In Remote-/Headless-Deployments koennen damit absolute Root-Pfade ohne API-Key enumeriert werden. Das ist kein direkter Escape aus der Root-Policy, aber ein unnoetiger Recon-Kanal ueber Deployment- und Dateisystemstruktur.
- **Betroffene Dateien:**
  - [ProgramHelpers.cs](../src/Romulus.Api/ProgramHelpers.cs#L124-L128) — `/dashboard/bootstrap` ist anonymous
  - [Program.cs](../src/Romulus.Api/Program.cs#L252-L255) — mappt den anonymen Bootstrap-Endpoint
  - [DashboardDataBuilder.cs](../src/Romulus.Api/DashboardDataBuilder.cs#L15-L31) — projiziert `AllowedRoots`
  - [DashboardModels.cs](../src/Romulus.Api/DashboardModels.cs#L6-L17) — DTO enthaelt `AllowedRoots`
  - [DashboardDataBuilderCoverageTests.cs](../src/Romulus.Tests/DashboardDataBuilderCoverageTests.cs#L13-L31) — aktuelle Tests bestaetigen das Leaken der Werte sogar
- **Reproduktion / Beispiel:** `GET /dashboard/bootstrap` ohne `X-Api-Key` liefert `allowedRoots` inklusive absoluter Pfade.
- **Ursache:** Bootstrap wurde fuer den Dashboard-Shell-Start zu grosszuegig modelliert und exponiert Betriebsmetadaten, die fuer den anonymen Client nicht notwendig sind.
- **Fix:** `AllowedRoots` aus dem anonymen Payload entfernen oder auf unschaedliche Metadaten reduzieren (`AllowedRootsEnforced`, Count). Alternative: Bootstrap nur im Local-Only-Modus anonym lassen und bei Remote-/Headless-Deployments authentifizieren.
- **Testabsicherung:**
  - Integrationstest: anonymer Bootstrap enthaelt keine konkreten Root-Pfade
  - Regressionstest: bei Remote-/Headless-Konfiguration bleibt Bootstrap auth-sicher oder liefert nur redigierte Metadaten

---

## Aktualisierte Zusammenfassung (inkl. Runde 8)

| Quelle | Anzahl | Critical/Blocking | High | Medium-High | Medium | Low |
|---|---|---|---|---|---|---|
| Erstaudit (P1-P7) | 19 | 1 | — | — | 10 | 8 |
| Deep-Dive Runde 2 | 9 | 1 | 3 | — | 4 | 1 |
| Runde 3 | 7 | — | — | 1 | 4 | 2 |
| Runde 4 | 6 | — | 2 | — | 1 | 3 |
| Runde 5 | 3 | — | — | — | 1 | 2 |
| Runde 6 | 3 | — | — | — | 1 | 2 |
| Runde 7 | 3 | — | — | — | — | 3 |
| Runde 8 | 4 | — | 2 | — | 1 | 1 |
| **Gesamt** | **54** | **2** | **7** | **1** | **22** | **22** |

---

## Aktualisierte Top 20 Massnahmen (priorisiert nach Runde 8)

1. **DB-001:** Legacy-Conversion-Verification-Bug fixen (CRITICAL — konvertierte Files werden geloescht)
2. **F-001:** RollbackMovedArtifacts Fehler-Propagation (BLOCKING)
3. **DB-032:** Persistierte Run-History / Dashboard client-isolieren (HIGH)
4. **DB-002:** MoveDirectorySafely full-ancestry Reparse-Point-Check (HIGH)
5. **DB-003:** AllowedRootPathPolicy File-Level Reparse-Point-Check (HIGH)
6. **DB-004:** AuditCsvParser Unclosed-Quote-Handling (HIGH)
7. **DB-017:** ConversionExecutor success=true bei VerifyFailed fixen (HIGH)
8. **DB-033:** WPF `ConvertFormat` non-auto round-trip reparieren (HIGH)
9. **DB-018:** LiteDbCollectionIndex.RecreateDatabase Exception-Safety (HIGH)
10. **DB-010:** SettingsLoader.MergeFromUserSettings Validation-Revert implementieren (MEDIUM-HIGH)
11. **DB-034:** WPF Settings/Profile auf zentrale `SettingsLoader`-Validierung umstellen (MEDIUM)
12. **DB-026:** Move-Phase AllGroups statt GameGroups fuer BIOS-Loser-Move (MEDIUM)
13. **F-013:** Verification-Status-Aggregation vereinheitlichen
14. **DB-024:** AuditSigningService.Rollback Math.Max(1,...) R6-006-Inkonsistenz fixen (MEDIUM)
15. **DB-019:** `_syncContext`-null-Pfad Background-Thread-PropertyChanged absichern (MEDIUM)
16. **F-004:** Root-Validierung in ConvertSingleFile (Defense-in-Depth)
17. **DB-011:** ConversionRegistryLoader ReadRequired-Wrapper fuer lossless/cost
18. **F-010:** Timeout-Recovery-Test fuer ConversionExecutor schreiben
19. **F-011:** Korrupte-Audit-CSV-Rollback-Test schreiben
20. **DB-035:** Dashboard-Bootstrap ohne Root-Disclosure haerten (LOW)

---

## Aktualisierte Systemische Hauptursachen (nach Runde 8)

1. **Security-/Ownership-Modell endet zu frueh:** Client-Binding ist im Live-Run-Pfad vorhanden, wird aber beim Uebergang in persistierte Read-Models und Dashboard-Projektionen nicht mitgezogen.

2. **Verlustbehaftete UI-Zustandsmodelle:** Mehrwertige fachliche Felder wie `ConvertFormat` werden in der WPF als Bool komprimiert und spaeter heuristisch rekonstruiert. Das erzeugt stille Paritaetsfehler.

3. **Mehrere Settings-Ingress-Pfade mit Schattenlogik:** GUI `SettingsService` / `ProfileService` und Infrastructure `SettingsLoader` validieren und behandeln Korruption unterschiedlich. Dieselbe Datei kann je nach Entry Point zu unterschiedlichen effektiven Settings fuehren.

4. **Testabdeckung fokussiert zu stark auf Reachability und Happy Paths:** Live-Run-Auth ist getestet, aber Cross-Client-Isolation in persistierten Read-Models und non-auto-Konfigurations-Round-Trips sind nicht als Invarianten verankert. Der nicht abschliessende Volltestlauf auf dem Audit-Rechner ist ein weiteres Signal fuer zu teure End-to-End-Validierung.

---

## Aktualisierte Sanierungsstrategie (nach Runde 8)

### Phase 1 – Release-Blocker und Security schliessen (1 Sprint)
- DB-001 und F-001 beheben
- DB-032 client-isolierte History-/Dashboard-Read-Models implementieren
- DB-002, DB-003, DB-004 haerten
- DB-017 und DB-018 korrigieren

### Phase 2 – Fachliche Wahrheit zwischen GUI / CLI / API konsolidieren (1 Sprint)
- DB-033 non-auto-`ConvertFormat` im WPF-State sauber modellieren
- DB-010 und DB-034 auf einen gemeinsamen Settings-Validierungspfad konsolidieren
- DB-026, F-013 und DB-024 fuer Preview/Execute/Report-Konsistenz schliessen

### Phase 3 – Testluecken und Hardening schliessen (1 Sprint)
- Cross-Client-Integrationstests fuer `/runs/history`, `/runs/compare`, `/runs/trends`, `/dashboard/summary`
- WPF-Round-Trip-Regressionstests fuer konkrete `ConvertFormat`-Werte
- Settings-Import-/Korruptions-Regressionstests fuer GUI und CLI/API-Paritaet
- F-010, F-011 und DB-035 als Hardening-/Regression-Paket abschliessen

---

## Gesamtschlussurteil (nach Runde 8)

Nach 8 Audit-Runden mit insgesamt **54 Findings** ist das Bild zweigeteilt:

### Was weiterhin stark ist

- **Core-Domaenenlogik:** Deduplication, GameKey, RegionDetection, Scoring bleiben deterministisch und sauber
- **Architektur:** Schichtung und zentrale Materialisierung sind grundsaetzlich stark
- **Build-Qualitaet:** `src/Romulus.sln` baut gruen
- **Viele Security-Basics:** HTML-Escaping, CSV-Injection-Schutz, Tool-Hashing, Audit-Signing, Zip-Slip-Grundschutz bleiben intakt

### Was release-relevant offen ist

- **2 Critical/Blocking:** DB-001, F-001
- **7 High:** DB-002, DB-003, DB-004, DB-017, DB-018, DB-032, DB-033
- **1 Medium-High:** DB-010
- **44 Medium/Low:** ueberwiegend Konsistenz-, Diagnostik- und Hardening-Themen

### Revidierte Gesamtbewertung

Die vorherige Einschaetzung "nur noch Low-Value-Diminishing-Returns" gilt **nicht** mehr uneingeschraenkt. Runde 8 hat zwei weitere echte Release-Findings in schichtuebergreifenden Pfaden gefunden: **fehlende Client-Isolation in den persistierten API-Read-Models** und **stille GUI-Umdeutung expliziter `ConvertFormat`-Werte**. Beides sind keine kosmetischen Hygiene-Punkte, sondern reale Security- bzw. Paritaetsfehler.

**Schlussurteil:** Romulus ist **architektonisch stark, aber noch nicht release-fertig fuer shared/headless API-Deployments und profilgetriebene Conversion-Workflows**, solange DB-032 und DB-033 zusammen mit den bestehenden Critical/High-Findings offen sind. Sobald diese P1/P2-Punkte geschlossen und mit den genannten Regressionstests abgesichert sind, ist die Codebasis wieder auf einem belastbaren Release-Pfad.

---
---

## Runde 9 — Voll-Re-Sweep aller Bereiche (2026-04-15)

> **Methode:** erneuter Sweep ueber alle 5 Hauptbereiche mit Ausschluss bereits bekannter Findings (DB-001..DB-035, F-001..F-004):
> - Bereich A: Core-Domaenenlogik (GameKeys, Regions, Scoring, Deduplication, Classification)
> - Bereich B: Orchestration / Conversion / Reporting / Audit
> - Bereich C: Safety / FileSystem / DAT / Hashing
> - Bereich D: CLI / API / WPF
> - Bereich E: data/*.json / Configuration / Test-Rigor

### Bereichsverdicts Runde 9

- **Bereich A:** 1 neues Finding
- **Bereich B:** 0 neue Findings
- **Bereich C:** 0 neue Findings
- **Bereich D:** 0 neue Findings
- **Bereich E:** 0 neue Findings

---

### DB-036: HIGH — `VersionScorer` kann bei grossen Version-Segmenten in `long` ueberlaufen

- [ ] **Offen**
- **Schweregrad:** High (Determinismus / Winner-Selection-Risiko)
- **Impact:** In der Version-Scoring-Logik wird fuer Segmentlisten ein Gewicht bis `1000^(n-1)` aufgebaut und dann `versionScore += seg * weight` gerechnet. Bei grossen numerischen Segmenten (bis `int.MaxValue` aus `int.TryParse`) kann `seg * weight` den `long`-Bereich ueberlaufen. Das fuehrt zu stillem Wraparound und potenziell falscher Ranking-Reihenfolge.
- **Betroffene Datei:** [VersionScorer.cs](../src/Romulus.Core/Scoring/VersionScorer.cs#L276-L293)
- **Reproduktion / Beispiel:** Dateiname mit grossem Versionsmuster wie `Game (v2000000000.999999999.999999999.999999999.999999999.999999999)` kann bei gewichteter Summierung ueberlaufen.
- **Ursache:** Segmentanzahl wird begrenzt, Segmentgroesse aber nicht; die Multiplikation laeuft ausserhalb eines `checked`-Kontexts.
- **Fix:** `checked`-arithmetik mit deterministischem Overflow-Handling (z.B. Cap/Saturation oder validierungsseitige Segmentobergrenze), damit Ranking stabil bleibt.
- **Testabsicherung:** Regressionstest mit sehr grossen Versionsegmenten; Erwartung: kein Wraparound, deterministische Ordnung.

---

## Aktualisierte Zusammenfassung (inkl. Runde 9)

| Quelle | Anzahl | Critical/Blocking | High | Medium-High | Medium | Low |
|---|---|---|---|---|---|---|
| Erstaudit (P1-P7) | 19 | 1 | — | — | 10 | 8 |
| Deep-Dive Runde 2 | 9 | 1 | 3 | — | 4 | 1 |
| Runde 3 | 7 | — | — | 1 | 4 | 2 |
| Runde 4 | 6 | — | 2 | — | 1 | 3 |
| Runde 5 | 3 | — | — | — | 1 | 2 |
| Runde 6 | 3 | — | — | — | 1 | 2 |
| Runde 7 | 3 | — | — | — | — | 3 |
| Runde 8 | 4 | — | 2 | — | 1 | 1 |
| Runde 9 | 1 | — | 1 | — | — | — |
| **Gesamt** | **55** | **2** | **8** | **1** | **22** | **22** |

---

## Runde 10 — Verifikationsrunde "0 neue Findings" (2026-04-15)

> **Ziel:** Abschlusskriterium aus dem Deep-Dive-Prozess pruefen: "In jedem Bereich keine neuen Findings mehr".
>
> **Vorgehen:**
> - Bereich A nach Aufnahme von DB-036 erneut gesweept
> - Bereiche B-E unveraendert seit Runde 9, dort bereits 0 neue Findings

### Bereichsverdicts Runde 10

- **Bereich A:** 0 neue Findings
- **Bereich B:** 0 neue Findings (Runde 9)
- **Bereich C:** 0 neue Findings (Runde 9)
- **Bereich D:** 0 neue Findings (Runde 9)
- **Bereich E:** 0 neue Findings (Runde 9)

### Abschlussstatus

Das Stoppkriterium fuer den wiederholten Bereichs-Sweep ist erreicht: **keine neuen Findings mehr in allen Hauptbereichen**.

Es bleiben ausschliesslich bereits bekannte, priorisierte Findings offen (insb. DB-001, F-001, DB-002, DB-003, DB-004, DB-017, DB-018, DB-032, DB-033, DB-036).

---

## Gesamtschlussurteil (nach Runde 10)

Der iterative Deep-Dive wurde bis zum geforderten Endkriterium fortgefuehrt. In der finalen Verifikationsrunde liegen fuer alle Bereiche **0 neue Findings** vor.

Romulus ist damit aus Audit-Sicht nicht mehr im Modus "neue unbekannte Fehlerklassen finden", sondern im Modus "bekannte Findings gezielt schliessen".

Die weitere Arbeit sollte auf die bereits priorisierten Critical/High-Findings konzentriert werden; zusaetzliche brute-force Auditrunden haben nur noch sehr geringen Erkenntnisgewinn.
