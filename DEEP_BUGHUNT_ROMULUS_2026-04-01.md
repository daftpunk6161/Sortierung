# Deep Bughunt – Romulus

## 1. Executive Verdict

- Gesamtzustand: nicht release-tauglich.
- Wichtigste Beobachtung: die Testsuite ist grün (`7197/7197`), deckt aber mehrere reale Release-Risiken nicht ab. Das ist ein False-Green-Zustand.
- Groesste Release-Blocker:
- `CONVERT`-Rollback ist fachlich falsch und kann den Originalzustand nicht wiederherstellen.
- API-Idempotenz kollidiert fuer fachlich unterschiedliche Requests.
- Conversion von Multi-File-Sets ist nicht atomisch genug; partielle Restartefakte und partielle Source-Entfernung sind moeglich.
- Kurzfazit: Core-Selektion und viele Safety-Basics wirken inzwischen deutlich haerter abgesichert als Orchestration/API/UI-Paritaet. Die kritischsten offenen Risiken liegen in Conversion/Audit/Rollback, API-Idempotenz und GUI-/Report-Wahrheit.

## 2. Top Release-Blocker

1. `QA-01` `CONVERT`-Rollback verschiebt das konvertierte Ziel zurueck auf den Originalpfad und laesst die echte Quelldatei im `_TRASH_CONVERTED` liegen.
2. `QA-04` API-Idempotenz-Fingerprint kollidiert fuer unterschiedliche `PreferRegions`-Reihenfolgen und fuer unterschiedliche `convertFormat`-Werte.
3. `QA-02` Erfolgreiche Conversion von Multi-File-Sets bleibt erfolgreich, selbst wenn das Wegraeumen einzelner Set-Member fehlschlaegt.
4. `QA-03` Multi-Disc-/Multi-CUE-Outputs in `AdditionalTargetPaths` werden bei Verify-/Error-Cleanup nicht aufgeraeumt.
5. `QA-06` Report/API/GUI bleiben nach erfolgreicher Conversion auf den alten `MainPath`-Quellartefakten statt die reale Ergebnisdatei abzubilden.

## 3. Findings nach Bereichen

### Core / Recognition / Classification / Sorting

- Keine belastbare P0/P1-Fehlentscheidung in `GameKeyNormalizer`, Region-Scoring, `SelectWinner`, `FileClassifier`, `ConsoleDetector`, `HypothesisResolver`, `DiscHeaderDetector` oder `CandidateFactory` nachgewiesen.
- Rest-Risiko bleibt, aber die aktuell haertesten Defekte sitzen nicht im Core, sondern in Conversion/Orchestration/API/UI-Paritaet.

### Infrastructure / Orchestration

#### QA-01 `CONVERT`-Rollback stellt den Originalzustand nicht wieder her
- Schweregrad: P0
- Impact: Rollback einer erfolgreichen Conversion restauriert nicht die Originalquelle. Stattdessen wird das konvertierte Ziel auf den alten Quellpfad zurueckverschoben. Originalbits bleiben im Trash. Das verletzt Audit-/Rollback-Integritaet und kann Dateiendungen fachlich verfälschen.
- Betroffene Dateien:
- `src/RomCleanup.Infrastructure/Orchestration/PipelinePhaseHelpers.cs:32-40`
- `src/RomCleanup.Infrastructure/Orchestration/PipelinePhaseHelpers.cs:62-103`
- `src/RomCleanup.Infrastructure/Audit/AuditSigningService.cs:321-442`
- Reproduktion:
- Erfolgreiche Conversion `foo.zip -> foo.chd` mit Audit.
- Audit enthaelt `oldPath=foo.zip`, `newPath=foo.chd`, `action=CONVERT`.
- Originalquelle wird danach physisch nach `_TRASH_CONVERTED\\foo.zip` verschoben, aber ohne passenden Audit-Eintrag.
- Rollback verarbeitet `CONVERT` wie einen normalen Move und fuehrt `foo.chd -> foo.zip` aus.
- Erwartetes Verhalten:
- Rollback muss die echte Originalquelle aus `_TRASH_CONVERTED` restaurieren oder Conversion-Rollback explizit als Zweiphasen-Operation modellieren.
- Tatsaechliches Verhalten:
- Das konvertierte Artefakt wird auf den alten Quellpfad zurueckverschoben; die echte Quelle bleibt im Trash.
- Ursache:
- `AppendConversionAudit` auditiert nur `sourcePath -> convertedPath`.
- `MoveConvertedSourceToTrash` fuehrt den physischen Source-Move separat aus, aber ohne korrespondierenden Audit-Eintrag.
- `AuditSigningService.Rollback` behandelt `CONVERT` generisch als `newPath -> oldPath`.
- Fix:
- `CONVERT` darf nicht wie `MOVE` gerollbackt werden.
- Conversion braucht entweder:
- einen expliziten Audit-Eintrag fuer `source -> _TRASH_CONVERTED\\...` plus eigenes Rollback-Verhalten, oder
- ein dediziertes Conversion-Rollback-Modell mit `OriginalPath`, `TrashSourcePath`, `ConvertedPath`.
- Testabsicherung:
- Regressionstest: erfolgreiche Conversion mit Audit, danach Rollback; erwartet wird Wiederherstellung der Originalquelle und Entfernung des konvertierten Outputs.

#### QA-02 Conversion von Multi-File-Sets ist nicht atomisch, wenn Set-Member-Trash fehlschlaegt
- Schweregrad: P1
- Impact: Bei `.cue/.gdi/.ccd`-Sets kann die eigentliche Conversion als erfolgreich gelten, obwohl einzelne Member-Dateien nicht in den Trash verschoben wurden. Ergebnis: teilweiser Execute, inkonsistenter Source-Bestand, fragiler Rollback.
- Betroffene Dateien:
- `src/RomCleanup.Infrastructure/Orchestration/ConversionPhaseHelper.cs:234-239`
- `src/RomCleanup.Infrastructure/Orchestration/ConversionPhaseHelper.cs:289-336`
- Reproduktion:
- Erfolgreiche Descriptor-Conversion eines CUE/BIN-Sets.
- Einer der BIN/TRACK-Moves in `MoveSetMembersToTrash` scheitert wegen Lock/IO-Fehler.
- Descriptor-Quelle wird danach trotzdem nach `_TRASH_CONVERTED` verschoben und die Conversion bleibt erfolgreich gezaehlt.
- Erwartetes Verhalten:
- Multi-File-Conversion muss atomisch sein: entweder alle relevanten Source-Artefakte werden sauber in den post-conversion state ueberfuehrt, oder der Lauf wird als Fehler/partial failure behandelt.
- Tatsaechliches Verhalten:
- Set-Member-Fehler werden nur geloggt; der Lauf bleibt success.
- Ursache:
- `MoveSetMembersToTrash` swallowed Fehler pro Member als Warning.
- Der Rueckgabestatus der Conversion wird dadurch nicht mehr beeinflusst.
- Fix:
- Vor dem Descriptor-Move Set-Member-Preflight einziehen.
- Jeden Set-Member-Move als harten Failure in den Conversion-Ausgang ueberfuehren oder sauberes Rollback der bereits bewegten Member erzwingen.
- Testabsicherung:
- Regressionstest mit gesperrtem BIN-File; erwartet wird `ConversionOutcome.Error` oder mindestens `completed_with_errors` plus keine Entfernung der Descriptor-Quelle.

#### QA-03 Verify-/Error-Cleanup ignoriert `AdditionalTargetPaths`
- Schweregrad: P1
- Impact: Multi-Disc-Conversion kann bei Verify-Failure oder spaeterem Error orphaned Outputs hinterlassen.
- Betroffene Dateien:
- `src/RomCleanup.Infrastructure/Conversion/ChdmanToolConverter.cs:214-249`
- `src/RomCleanup.Infrastructure/Orchestration/ConversionPhaseHelper.cs:241-257`
- `src/RomCleanup.Infrastructure/Orchestration/ConversionPhaseHelper.cs:269-278`
- Reproduktion:
- Multi-CUE-Archive erzeugen `TargetPath=disc1.chd` und `AdditionalTargetPaths=[disc2.chd,...]`.
- Bei nachgelagertem Verify-Failure oder Error-Cleanup loescht die Orchestration nur `convResult.TargetPath`.
- Erwartetes Verhalten:
- Alle erzeugten Primaerartefakte muessen aufgeraeumt werden.
- Tatsaechliches Verhalten:
- Nur `TargetPath` wird best-effort geloescht; `AdditionalTargetPaths` bleiben liegen.
- Ursache:
- Cleanup-Pfade kennen nur `TargetPath`, nicht `AdditionalTargetPaths`.
- Fix:
- Zentralen Cleanup-Helper fuer `TargetPath + AdditionalTargetPaths` einfuehren.
- Testabsicherung:
- Regressionstest fuer Multi-Disc-Conversion mit erzwungenem Verify-Failure; erwartet wird, dass kein `.chd`-Output liegenbleibt.

### Conversion

#### QA-06 Executed conversion wird in Report/API/GUI nicht als Ergebnisdatei projiziert
- Schweregrad: P1
- Impact: Nach erfolgreicher Winner-Conversion bleiben Detailansichten auf dem alten `MainPath` der Quelle. Report, API-DedupeGroups und GUI-Dedupe-Browser zeigen damit nicht die reale fachliche Wahrheit des Execute-Zustands.
- Betroffene Dateien:
- `src/RomCleanup.Infrastructure/Reporting/RunReportWriter.cs:19-109`
- `src/RomCleanup.Api/ApiRunResultMapper.cs:92-121`
- `src/RomCleanup.UI.Wpf/Models/DashboardProjection.cs:158-193`
- Reproduktion:
- Move-Run mit erfolgreicher Winner-Conversion.
- `RunReportWriter` schreibt Winner-/Loser-Pfade ausschliesslich aus `group.Winner.MainPath` bzw. `candidate.MainPath`.
- API-DedupeGroups serialisieren dieselben Kandidatenobjekte.
- GUI-Dedupe-Browser liest `Path.GetFileName(grp.Winner.MainPath)`.
- Erwartetes Verhalten:
- Execute/Report/API/GUI muessen dieselbe fachliche Wahrheit abbilden, also das reale Ergebnisartefakt oder eine explizite Zielpfad-Projektion.
- Tatsaechliches Verhalten:
- Die Detailsicht bleibt auf dem Pre-Conversion-Sourcepfad stehen.
- Ursache:
- Es gibt keine zentrale Post-Conversion-Projection, die Kandidatenpfade auf das ausgefuehrte Ziel materialisiert.
- Fix:
- Conversion-Ergebnisse in eine kanonische Ergebnisprojektion ueberfuehren und diese in Report/API/GUI verwenden.
- Testabsicherung:
- Paritaetstest: nach erfolgreicher Winner-Conversion muessen Report-Entries und API-/GUI-Details den Zielpfad oder eine explizite `ExecutedTargetPath`-Spalte tragen.

### Reports / Audit / Rollback / Metrics

#### QA-05 `completed_with_errors` landet im API-Recovery-Status als `unknown`
- Schweregrad: P1
- Impact: API-Clients erhalten bei abgeschlossenen Runs mit Fehlern keinen belastbaren Recovery-Status. `CanRollback` kann `true` sein, waehrend `RecoveryState` auf `unknown` steht.
- Betroffene Dateien:
- `src/RomCleanup.Api/RunLifecycleManager.cs:278-315`
- `src/RomCleanup.Api/RunLifecycleManager.cs:345-357`
- `src/RomCleanup.Api/RunManager.cs:370-375`
- Reproduktion:
- Run endet mit `ApiRunStatus.CompletedWithErrors`.
- `UpdateRecoveryState` behandelt nur `Completed`, `Cancelled`, `Failed`, aber nicht `CompletedWithErrors`.
- Erwartetes Verhalten:
- `completed_with_errors` muss recovery-seitig wie ein abgeschlossener Lauf mit moeglichem Rollback behandelt werden.
- Tatsaechliches Verhalten:
- `RecoveryState` faellt auf `_ => "unknown"`.
- Ursache:
- Switch-Mapping in `UpdateRecoveryState` ist unvollstaendig.
- Fix:
- `CompletedWithErrors` explizit mappen, inklusive audit/no-audit-Faellen.
- Testabsicherung:
- API-Regressionstest: `completed_with_errors` mit Audit muss `RecoveryState=rollback-available` oder fachlich aequivalent liefern.

#### QA-07 `ConvertSavedBytes` wird im WPF-Dashboard mit invertiertem Vorzeichen angezeigt
- Schweregrad: P2
- Impact: KPI-Kanal GUI widerspricht API/CLI/Projection. Positive Einsparung wird als negatives Vorzeichen angezeigt, Speicherwachstum als positiv.
- Betroffene Dateien:
- `src/RomCleanup.UI.Wpf/Models/DashboardProjection.cs:75-82`
- `src/RomCleanup.UI.Wpf/Models/DashboardProjection.cs:125-135`
- Reproduktion:
- `projection.ConvertSavedBytes = 1024`.
- `FormatBytes` gibt `-1.0 KB` statt `+1.0 KB` zurueck.
- Erwartetes Verhalten:
- Positive `ConvertSavedBytes` muessen als Gewinn dargestellt werden.
- Tatsaechliches Verhalten:
- Vorzeichenlogik ist invertiert.
- Ursache:
- `return bytes < 0 ? $"+{formatted}" : $"-{formatted}";`
- Fix:
- Vorzeichenlogik umdrehen.
- Testabsicherung:
- UI-Projektionstest mit exakter Erwartung fuer positive und negative `ConvertSavedBytes`.

### GUI / WPF

#### QA-08 Preview-Execute-Gate ignoriert `ApproveReviews`
- Schweregrad: P1
- Impact: Die GUI kann einen Move als durch Preview abgesichert freigeben, obwohl sich die fachliche Wahrheit geaendert hat. `ApproveReviews` beeinflusst die Kandidatenlage vor Dedupe, wird aber im Preview-Fingerprint nicht beruecksichtigt.
- Betroffene Dateien:
- `src/RomCleanup.UI.Wpf/ViewModels/MainViewModel.Productization.cs:137-158`
- `src/RomCleanup.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs:18-56`
- `src/RomCleanup.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs:1261-1298`
- `src/RomCleanup.Infrastructure/Orchestration/RunOrchestrator.ReviewApprovals.cs:7-39`
- Reproduktion:
- DryRun ohne `ApproveReviews`.
- Danach `ApproveReviews` aktivieren.
- `BuildCurrentRunConfigurationDraft` schreibt `ApproveReviews` in den Run-Request.
- Preview-Gate-Fingerprint kennt `ApproveReviews` nicht; `CanStartMoveWithCurrentPreview` bleibt true.
- Execute kann dadurch andere genehmigte Review-Kandidaten nutzen als die Preview.
- Erwartetes Verhalten:
- Jede Option, die Kandidaten/Dedupe/Execute fachlich veraendert, muss das Move-Gate invalidieren.
- Tatsaechliches Verhalten:
- `ApproveReviews` wirkt zur Laufzeit, invalidiert den Preview-Gate aber nicht.
- Ursache:
- `PreviewRelevantPropertyNames` und `BuildPreviewConfigurationFingerprint` bilden `ApproveReviews` nicht ab.
- Fix:
- `ApproveReviews` in Property-Menge und Fingerprint aufnehmen.
- Testabsicherung:
- GUI-Paritaetstest: Preview mit `ApproveReviews=false`, danach `ApproveReviews=true`; `CanStartMoveWithCurrentPreview` muss auf `false` fallen.

#### QA-09 Blocked-only ConvertOnly-Runs werden im Dashboard nicht als ConvertOnly erkannt
- Schweregrad: P2
- Impact: GUI zeigt fuer reine ConvertOnly-Laeufe mit nur blockierten Conversions das falsche Dashboard-Regime und die falsche Consequence-Kommunikation.
- Betroffene Dateien:
- `src/RomCleanup.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs:1138-1143`
- `src/RomCleanup.UI.Wpf/Models/DashboardProjection.cs:33-70`
- Reproduktion:
- ConvertOnly-Run, bei dem alle Kandidaten `Blocked` sind und `ConvertedCount=0`, `ConvertErrorCount=0`, `ConvertSkippedCount=0`, `ConvertBlockedCount>0`.
- `CompleteRun` setzt `ConvertOnly=false`.
- `ApplyRunResult` rekonstruiert `isConvertOnlyRun` ohne `ConvertBlockedCount`.
- Erwartetes Verhalten:
- Jeder echte ConvertOnly-Run muss als ConvertOnly projiziert werden, auch wenn nur Blocked-Ergebnisse vorliegen.
- Tatsaechliches Verhalten:
- Blocked-only-Faelle kippen in das normale Dashboard-Regime.
- Ursache:
- Heuristik in `ApplyRunResult` prueft nur `Converted/Error/Skipped`, nicht `Blocked`.
- Fix:
- ConvertOnly-Kontext explizit persistieren oder die Heuristik um `ConvertBlockedCount` erweitern.
- Testabsicherung:
- GUI-Regressionstest fuer blocked-only ConvertOnly-Lauf.

### CLI / API / Paritaet

#### QA-04 API-Idempotenz kollidiert fuer unterschiedliche semantische Requests
- Schweregrad: P0
- Impact: API kann bei gleichem Idempotency-Key einen alten Run wiederverwenden, obwohl sich die fachliche Anforderung geaendert hat. Das bricht Determinismus, Idempotenz und API-Vertrauen.
- Betroffene Dateien:
- `src/RomCleanup.Api/RunLifecycleManager.cs:384-425`
- Reproduktion:
- Request A: `preferRegions=["EU","US"]`, `convertFormat="chd"`
- Request B: `preferRegions=["US","EU"]`, `convertFormat="rvz"`
- Der Fingerprint:
- sortiert `PreferRegions` alphabetisch und verliert damit die Prioritaetsreihenfolge.
- reduziert jeden nichtleeren `convertFormat`-Wert auf `"AUTO"`.
- Erwartetes Verhalten:
- Semantisch unterschiedliche Requests muessen unterschiedliche Fingerprints bekommen.
- Tatsaechliches Verhalten:
- Beide Requests koennen denselben Fingerprint erzeugen.
- Ursache:
- `OrderBy` auf `PreferRegions`.
- `string.IsNullOrWhiteSpace(request.ConvertFormat) ? \"\" : \"AUTO\"`.
- Fix:
- `PreferRegions` in kanonisch normalisierter, aber geordneter Form serialisieren.
- Realen `convertFormat`-Wert normalisiert serialisieren.
- Testabsicherung:
- Idempotenztests fuer Reihenfolgewechsel in `PreferRegions` und fuer jedes zulaessige `convertFormat`.

#### QA-11 CLI-Hilfe dokumentiert einen anderen Default als die produktive Konstante
- Schweregrad: P3
- Impact: Nutzer sehen in der CLI einen anderen Default als die fachlich verwendete Prioritaet. Das erzeugt Debugging- und Reproduktionsfehler.
- Betroffene Dateien:
- `src/RomCleanup.CLI/CliOutputWriter.cs:162`
- `src/RomCleanup.Contracts/RunConstants.cs:19-24`
- Reproduktion:
- CLI-Hilfe nennt `EU,US,WORLD,JP`.
- Produktiver Default ist `EU,US,JP,WORLD`.
- Erwartetes Verhalten:
- Doku/Hilfe und produktive Defaults muessen identisch sein.
- Tatsaechliches Verhalten:
- Reihenfolge driftet.
- Ursache:
- Harte Hilfezeichenkette statt Referenz auf `RunConstants.DefaultPreferRegions`.
- Fix:
- Hilfetext aus der Konstante ableiten.
- Testabsicherung:
- CLI-Help-Regressionstest gegen `RunConstants.DefaultPreferRegions`.

### Safety / Security

#### QA-10 `SafetyValidator.TestTools` ignoriert den uebergebenen Timeout
- Schweregrad: P2
- Impact: Tool-Selftests koennen deutlich laenger blockieren als vom Aufrufer angegeben. Das ist vor allem fuer GUI-Healthchecks und Diagnosepfade riskant.
- Betroffene Dateien:
- `src/RomCleanup.Infrastructure/Safety/SafetyValidator.cs:244-279`
- `src/RomCleanup.Contracts/Ports/IToolRunner.cs:17-31`
- `src/RomCleanup.Infrastructure/Tools/ToolRunnerAdapter.cs:123-149`
- Reproduktion:
- `TestTools(timeoutSeconds: X)` wird aufgerufen.
- `SafetyValidator` reicht `timeoutSeconds` nicht an `_tools.InvokeProcess(...)` weiter.
- Erwartetes Verhalten:
- Der uebergebene Timeout muss in den Process-Call einfliessen.
- Tatsaechliches Verhalten:
- Der Default des ToolRunners gilt weiter.
- Ursache:
- Aufruferparameter wird ignoriert.
- Fix:
- `TimeSpan.FromSeconds(timeoutSeconds)` an `InvokeProcess(..., timeout, cancellationToken)` weiterreichen.
- Testabsicherung:
- Test mit Fake-Runner, der den effektiven Timeout protokolliert.

### Tests / QA-Luecken

#### QA-T01 Idempotenztests decken die echten Kollisionsfaelle nicht ab
- Schweregrad: P1
- Impact: Der aktuelle Fingerprint-Bug konnte in einer grünen Testsuite verbleiben.
- Betroffene Dateien:
- `src/RomCleanup.Tests/IdempotencyFingerprintTests.cs:20-86`
- Ursache:
- Es gibt Tests fuer einige Flags, aber keinen Test fuer `PreferRegions`-Reihenfolge und keinen fuer konkrete `convertFormat`-Werte.
- Fix:
- Zwei neue Regressionsfaelle: Reihenfolgewechsel und Formatwechsel.

#### QA-T02 Der Multi-CUE-Atomicity-Test ist tautologisch
- Schweregrad: P2
- Impact: Ein Test mit dem Titel "rolls back all" prueft nur, ob eine Methode existiert. Verhaltensfehler wie liegenbleibende `AdditionalTargetPaths` bleiben unentdeckt.
- Betroffene Dateien:
- `src/RomCleanup.Tests/Conversion/Phase4ConversionInvariantTests.cs:166-184`
- Ursache:
- Der Test prueft `Assert.NotNull(method)` statt Dateisystemzustand.
- Fix:
- Echten Integrations-/Regressionstest fuer Multi-CUE-Error- und Verify-Failure-Pfade schreiben.

#### QA-T03 UI-Projektionstest fuer `ConvertSavedBytes` ist zu schwach
- Schweregrad: P2
- Impact: Die invertierte Vorzeichenlogik im Dashboard blieb trotz grüner UI-Tests unentdeckt.
- Betroffene Dateien:
- `src/RomCleanup.Tests/ConversionReportParityTests.cs:162-174`
- Ursache:
- Der Test prueft nur `Assert.NotEqual("–", ...)`, nicht den semantischen Wert.
- Fix:
- Exakte Erwartungswerte fuer positive und negative Bytes.

#### QA-T04 Es fehlt ein Regressionstest fuer erfolgreichen Conversion-Rollback
- Schweregrad: P0
- Impact: Der haerteste Audit-/Rollback-Bug ist aktuell ungetestet.
- Betroffene Dateien:
- keine passende Abdeckung gefunden
- Ursache:
- Es gibt Rollback-Tests fuer Move/Reparse/Sidecar, aber keinen fuer den `CONVERT`-Happy-Path mit anschliessendem Rollback.
- Fix:
- Integrationsfall: successful convert + audit + rollback + Dateisystemassertions.

## 4. Determinismus-Risiken

- `QA-04` API-Fingerprint macht aus semantisch unterschiedlichen Requests denselben Identitaetswert.
- `QA-08` GUI-Preview-Gate invalidiert sich nicht fuer `ApproveReviews`, obwohl sich damit dieselbe Eingabe anders auswerten kann.
- `QA-06` Execute-Ergebnis wird in Detailprojektionen nicht stabil als echte Ergebnisdatei materialisiert; verschiedene Kanaele zeigen unterschiedliche "Wahrheiten" fuer denselben Lauf.

## 5. Datenverlust- und Sicherheitsrisiken

- `QA-01` ist ein direkter Rollback-/Datenintegritaetsblocker.
- `QA-02` fuehrt zu partiellem Entfernen von Source-Artefakten bei Multi-File-Conversion.
- `QA-03` laesst partielle Outputs liegen und verletzt Clean-Failure-Verhalten.
- Kein neuer konkreter Path-Traversal-, Zip-Slip-, Reparse-, ADS- oder XXE-Bypass konnte in den inspizierten produktiven Pfaden nachgewiesen werden.
- Die derzeit kritischste Sicherheits-/Integritaetsflaeche ist nicht Root-Containment, sondern Conversion/Audit/Rollback-Semantik.

## 6. Paritaets- und KPI-Probleme

- `QA-06` Report/API/GUI-Details bleiben auf dem Sourcepfad, obwohl Execute eine neue Ergebnisdatei erzeugt.
- `QA-05` API liefert bei `completed_with_errors` einen inkonsistenten Recovery-Zustand.
- `QA-07` GUI-KPI fuer `ConvertSavedBytes` widerspricht API/CLI/Projection.
- `QA-09` ConvertOnly-Blocked-only-Laeufe werden im GUI-Dashboard falsch eingerahmt.
- `QA-11` CLI-Hilfe dokumentiert einen anderen Default als die fachliche Runtime.

## 7. Testluecken

- `QA-T01` Keine Kollisionsabdeckung fuer `PreferRegions`-Reihenfolge und `convertFormat`.
- `QA-T02` Multi-CUE-"Atomicity"-Test ist method-existence-only.
- `QA-T03` UI-Projektion testet `ConvertSavedBytesDisplay` nicht semantisch.
- `QA-T04` Kein End-to-End-Test fuer erfolgreichen `CONVERT`-Rollback.

## 8. Die 20 wichtigsten Fixes

1. `QA-01` Conversion-Rollback auf ein dediziertes Modell umstellen; `CONVERT` nicht wie `MOVE` rollbacken.
2. Audit-Zeile fuer den realen Source-Move nach `_TRASH_CONVERTED` einziehen oder Conversion-Restore explizit modellieren.
3. Regressionstest fuer successful convert + rollback + Dateisystemzustand schreiben.
4. `QA-02` Set-Member-Preflight vor Descriptor-Move einfuehren.
5. Set-Member-Move-Fehler zu einem harten Conversion-Fehler machen oder sauber rollbacken.
6. `QA-03` Cleanup fuer `AdditionalTargetPaths` zentralisieren.
7. Verify-/Error-Pfade muessen alle erzeugten Outputs entfernen.
8. `QA-04` `PreferRegions` im API-Fingerprint geordnet erhalten.
9. `QA-04` realen `convertFormat`-Wert statt pauschal `"AUTO"` serialisieren.
10. Idempotenztests fuer Regionen-Reihenfolge und alle Formate ergaenzen.
11. `QA-05` `completed_with_errors` in `UpdateRecoveryState` vollstaendig behandeln.
12. `CanRollback`/`RecoveryState` API-seitig konsistent machen.
13. `QA-06` zentrale Post-Conversion-Ergebnisprojektion fuer Report/API/GUI einfuehren.
14. Report-/API-/GUI-Paritaetstest fuer ausgefuehrte Winner-Conversion schreiben.
15. `QA-08` `ApproveReviews` in Preview-Fingerprint und Preview-Relevanzliste aufnehmen.
16. GUI-Test: Preview muss nach `ApproveReviews`-Aenderung den Move sperren.
17. `QA-09` ConvertOnly-Kontext explizit am Result festhalten oder Blocked in die Heuristik aufnehmen.
18. `QA-07` `DashboardProjection.FormatBytes` korrigieren und exakte UI-KPI-Tests ergaenzen.
19. `QA-10` `timeoutSeconds` an `InvokeProcess` durchreichen.
20. `QA-11` CLI-Hilfe gegen `RunConstants.DefaultPreferRegions` ableiten statt hart zu codieren.

## 9. Schlussurteil

- Romulus ist in diesem Zustand nicht release-tauglich.
- Zwingend zuerst zu beheben:
- `QA-01` Conversion-Rollback/Audit-Modell
- `QA-04` API-Idempotenz-Kollisionen
- `QA-02` und `QA-03` Conversion-Atomicity/Cleanup
- `QA-05` Recovery-State-Drift
- Danach:
- `QA-06`, `QA-08`, `QA-09`, `QA-07` fuer Kanal-Paritaet und vertrauenswuerdige GUI/API/Reports

## 10. Tracking-Checklist

### Release-Blocker

- [x] `QA-01` `CONVERT`-Rollback restauriert den Originalzustand nicht.
- [x] `QA-02` Multi-File-Conversion bleibt erfolgreich trotz Set-Member-Trash-Fehlern.
- [x] `QA-03` Verify-/Error-Cleanup ignoriert `AdditionalTargetPaths`.
- [x] `QA-04` API-Idempotenz-Fingerprint kollidiert fuer geaenderte `PreferRegions`-Reihenfolge.
- [x] `QA-04` API-Idempotenz-Fingerprint kollidiert fuer unterschiedliche `convertFormat`-Werte.

### Hohe Risiken

- [x] `QA-05` `completed_with_errors` wird in der API als `RecoveryState=unknown` ausgeliefert.
- [x] `QA-06` Report/API/GUI projizieren nach Conversion weiterhin den alten Quellpfad.
- [x] `QA-08` GUI-Preview-Gate ignoriert `ApproveReviews`.
- [x] `QA-09` Blocked-only ConvertOnly-Laeufe werden im Dashboard falsch dargestellt.

### KPI / UX / Tooling

- [x] `QA-07` `ConvertSavedBytes` wird im WPF-Dashboard mit invertiertem Vorzeichen angezeigt.
- [x] `QA-10` `SafetyValidator.TestTools` ignoriert `timeoutSeconds`.
- [x] `QA-11` CLI-Hilfe nennt die falsche Default-Reihenfolge fuer `PreferRegions`.

### Tests / QA-Luecken

- [x] `QA-T01` Idempotenztests decken `PreferRegions`-Reihenfolge nicht ab.
- [x] `QA-T01` Idempotenztests decken `convertFormat`-Werte nicht ab.
- [x] `QA-T02` Multi-CUE-Atomicity-Test ist tautologisch.
- [x] `QA-T03` UI-Test fuer `ConvertSavedBytesDisplay` ist zu schwach.
- [x] `QA-T04` Es fehlt ein End-to-End-Regressionstest fuer erfolgreichen `CONVERT`-Rollback.

### Empfohlene Reihenfolge

- [x] Fix `QA-01` vor jedem Release.
- [x] Fix `QA-04` vor jeder weiteren API-Freigabe mit Idempotency-Key.
- [x] Fix `QA-02` und `QA-03` vor Freigabe fuer Disc-/Multi-Disc-Conversion.
- [x] Fix `QA-05` und `QA-06` vor Freigabe fuer produktive API-/Report-Nutzung.
- [x] Fix `QA-08` und `QA-09` vor Freigabe der Move-Flow-GUI.
- [x] Fix `QA-07`, `QA-10`, `QA-11` zusammen mit den Regressionstests.
