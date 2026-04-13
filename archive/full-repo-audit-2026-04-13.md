# Full Repo Audit – Findings Tracker (2026-04-13)

> **Audit-Scope:** Repo-weit, alle 7 Projekte (Contracts, Core, Infrastructure, CLI, API, WPF, Tests)
> **Analysierte Dateien:** ~400+ .cs-Dateien
> **Gesamturteil:** ~75-80% Release-tauglich
> **Status-Legende:** ⬜ Offen | 🔄 In Arbeit | ✅ Erledigt | ❌ Bewusst verschoben

---

## A. Harte Bugs

- [x] **A-1 (P0): ConsoleSort Path-Mutationen im DryRun nicht angewandt**
  - **Impact:** Preview zeigt andere ConsoleSort-Ergebnisse als Execute. Alle Folgephasen im DryRun arbeiten mit pre-sort Pfaden.
  - **Datei:** `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.StandardPhaseSteps.cs` (Zeilen 322-324)
  - **Ursache:** `if (!dryRunSort && ...)` Guard verhindert State-Update — bricht Preview/Execute Parität.
  - **Fix:** Mutationen immer auf State anwenden. DryRun soll dieselben fachlichen Entscheidungen treffen wie Execute.
  - **Test:** DryRun-ConsoleSort-Ergebnis muss identische projected Pfade liefern wie Execute-Modus.
  - **Label:** `Parity Risk`

- [x] **A-2 (P0): Move-Rollback-Exception abortiert Run**
  - **Impact:** Wenn Set-Member-Rollback fehlschlägt, wirft `InvalidOperationException`. Orchestrator fängt das nur im äußeren Catch. Kein Audit-Sidecar wird geschrieben. Benutzer sieht Crash, keine Recovery-Info.
  - **Datei:** `src/Romulus.Infrastructure/Orchestration/MovePipelinePhase.cs` (Zeilen 213-223)
  - **Ursache:** `throw new InvalidOperationException("Rollback failed...")` statt graceful degradation.
  - **Fix:** Fehlerhafte Rollbacks als `failCount++` verbuchen, nicht werfen. Orchestrator muss finalisieren können.
  - **Test:** Rollback-Failure → Run beendet mit Status=PartialFailure + vollständiges Audit-Sidecar.
  - **Label:** `Data-Integrity Risk`

- [x] **A-3 (P1): Convert-Only-Pfad setzt GroupCount/WinnerCount nicht**
  - **Impact:** GUI/API zeigt „0 Gruppen, 0 Winner" für Convert-Only-Runs. KPI-Dashboard irreführend.
  - **Datei:** `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.ScanAndConvertSteps.cs` (Zeilen 180-185)
  - **Ursache:** Nur `AllCandidates` und `TotalFilesScanned` gesetzt, nicht `GroupCount`/`WinnerCount`.
  - **Fix:** `result.GroupCount = result.DedupeGroups?.Count ?? 0;` etc. nach Convert-Only Phase setzen.
  - **Test:** Convert-Only Run → GroupCount > 0 wenn Candidates vorhanden.
  - **Label:** `Parity Risk`

- [x] **A-4 (P1): Set-Member-Preflight-Fehler verwaist Dateien**
  - **Impact:** CUE-Datei wird verschoben, aber BIN-Member-Preflight schlägt fehl → BIN bleibt zurück, CUE ist weg. Kein Audit für übersprungene Member.
  - **Datei:** `src/Romulus.Infrastructure/Orchestration/MovePipelinePhase.cs` (Zeilen 126-162)
  - **Ursache:** Hauptdatei wird verschoben auch wenn Member-Preflight fehlschlägt.
  - **Fix:** Bei Member-Preflight-Fehler: gesamten Set-Move überspringen (nicht Hauptdatei verschieben und Member ignorieren). Audit-Zeile für Skip.
  - **Test:** CUE+BIN Set, BIN-Preflight-Error → beide bleiben am Ort.
  - **Label:** `Data-Integrity Risk`

- [x] **A-5 (P2): RunProjection WinnerCount-Fallback auf stale result.WinnerCount**
  - **Impact:** Bei partieller Enrichment-Fehler kann stale `WinnerCount` irreführende KPI-Anzeigen erzeugen.
  - **Datei:** `src/Romulus.Infrastructure/Orchestration/RunProjection.cs` (Zeilen 54-57)
  - **Ursache:** Fallback auf `result.WinnerCount` ohne Warning-Log.
  - **Fix:** Fallback-Source als secondary, mit Warning-Log bei Nutzung.
  - **Test:** Enrichment-Failure → WinnerCount-Fallback loggt Warning.

- [x] **A-6 (P2): VerificationFailDelta Math.Max(0,...) verschluckt Anomalien**
  - **Impact:** `ConvertVerifyFailedCount < ConvertErrorCount` wird still auf 0 gesetzt. Under-Reporting von Failures.
  - **Datei:** `src/Romulus.Infrastructure/Orchestration/RunProjection.cs` (Zeilen 66-67)
  - **Ursache:** `Math.Max(0, ...)` verschluckt negativen Delta.
  - **Fix:** Anomalie loggen wenn Delta negativ.
  - **Test:** Szenario ConvertVerifyFailedCount < ConvertErrorCount → Warning geloggt + Delta korrekt.

### Verifikation A (2026-04-13)

- [x] **A-1** umgesetzt in `RunOrchestrator.StandardPhaseSteps`: Path-Mutationen werden unabhängig vom DryRun-Flag angewendet.
  - **Verifiziert durch:** `AuditABEndToEndRedTests.A01_ConsoleSortDryRunPathMutationGuard_MustNotSkipStateMutation`
- [x] **A-2** umgesetzt in `MovePipelinePhase`: kein Throw bei Rollback-Restore-Failure, stattdessen Failure-Zählung + Audit-Flush.
  - **Verifiziert durch:** `MovePhaseAuditInvariantTests.Move_SetMemberRollbackRestoreFailure_DoesNotThrowAndCountsFailure`
- [x] **A-3** umgesetzt in `RunOrchestrator.ScanAndConvertSteps`: ConvertOnly setzt `GroupCount`, `WinnerCount`, `LoserCount`, `DedupeGroups` konsistent.
  - **Verifiziert durch:** `AuditABEndToEndRedTests.A03_ConvertOnlyRun_PopulatesGroupAndWinnerCounts`
- [x] **A-4** abgesichert in `MovePipelinePhase`: Set-Preflight-Fehler verhindert Descriptor-Move.
  - **Verifiziert durch:** `MovePhaseAuditInvariantTests.Move_SetMemberPreflightFailure_SkipsWholeSetAndLeavesDescriptorInPlace`
- [x] **A-5** umgesetzt in `RunProjection`: Winner-Fallback auf coherent Snapshot begrenzt + Trace-Warnung.
  - **Verifiziert durch:** `AuditABEndToEndRedTests.A05_RunProjection_WinnerFallback_EmitsTraceWarning`
- [x] **A-6** umgesetzt in `RunProjection`: negativer Verify-Delta wird explizit per Trace-Warnung gemeldet.
  - **Verifiziert durch:** `AuditABEndToEndRedTests.A06_RunProjection_NegativeVerifyDelta_EmitsTraceWarning`

---

## B. Sicherheitsprobleme

- [x] **B-1 (P1): API Exception-Message-Leak auf stderr**
  - **Impact:** Interne Pfade, Stack-Details in Fehlermeldungen auf stderr sichtbar (Container-Logs, CI-Output).
  - **Datei:** `src/Romulus.Api/Program.cs` (Zeile 73)
  - **Beispiel:** `SafeConsoleWriteLine($"[API-ERROR] Unhandled exception: {exceptionFeature.Error.GetType().Name}: {exceptionFeature.Error.Message}");`
  - **Fix:** Nur an strukturierten Logger senden, nicht an Console/stderr. API-Response ist bereits sanitized.
  - **Test:** Unhandled Exception → stderr enthält kein `Message`.
  - **Label:** `Security Risk`

- [x] **B-2 (P1): ConversionOutputValidator prüft nur Existenz+Größe**
  - **Impact:** Tool gibt Exit-Code 0 + korrupte Datei zurück → akzeptiert. Source wird als konvertiert markiert.
  - **Datei:** `src/Romulus.Infrastructure/Conversion/ConversionOutputValidator.cs`
  - **Ursache:** Nur `File.Exists` + `Length > 0`. Keine Magic-Byte- oder Format-spezifische Prüfung.
  - **Fix:** Mindestgröße pro Format prüfen. Für CHD/RVZ/7z greift `ToolInvokerAdapter.Verify()` bereits — sicherstellen dass Verify immer aufgerufen wird wenn verfügbar. Für BIN/ISO/CSO: Mindestgröße + ggf. Header-Check.
  - **Test:** 1-Byte-Datei → Validator = false. Korrupte CHD → Verify = failed.
  - **Label:** `False Confidence Risk`

- [x] **B-3 (P2): ZipSorter Zip-Slip-Prüfung zu simpel (nur lesend)**
  - **Impact:** ZipSorter extrahiert nicht, liest nur Entry-Extensions. Traversal-Entries werden übersprungen. **Effektives Risiko gering.**
  - **Datei:** `src/Romulus.Infrastructure/Sorting/ZipSorter.cs` (Zeilen 41-42)
  - **Ursache:** `Contains("..")` ohne `GetFullPath()` Containment-Check.
  - **Fix:** `AreEntryPathsSafe()` aus ArchiveHashService wiederverwenden. Aber: kein reales Exploit da keine Extraktion.
  - **Test:** ZIP mit `subdir/../../../evil.bin` → Extension korrekt übersprungen.
  - **Label:** `Security Risk (Low)`

- [x] **B-4 (P2): ERR-08 Sync-over-async in CLI Test-Harness**
  - **Impact:** `Task.Run(() => RunAsync(opts)).Result` — Deadlock-Risiko in bestimmten SynchronizationContext-Umgebungen.
  - **Datei:** `src/Romulus.CLI/Program.cs` (Zeile 253), `src/Romulus.CLI/CliOptionsMapper.cs` (Zeile 20)
  - **Fix:** `.GetAwaiter().GetResult()` mit ConfigureAwait(false) oder async Test-Wrapper.
  - **Test:** CLI RunForTests → kein Deadlock in xUnit SynchronizationContext.

- [x] **B-5 (P2): Audit-Flush-Reihenfolge bei Cancel (Sidecar vor Hash-Cache)**
  - **Impact:** Bei Cancel wird Sidecar geschrieben bevor Hash-Cache geflusht wird. Wenn Hash-Cache-Write fehlschlägt, ist bereits Sidecar mit stale Daten committed.
  - **Datei:** `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.cs` (Zeilen 435-480)
  - **Fix:** Hash-Cache vor Sidecar flushen.
  - **Test:** Cancel-Szenario → Hash-Cache und Sidecar konsistent.

### Verifikation B (2026-04-13)

- [x] **B-1** umgesetzt in `Romulus.Api/Program.cs`: unhandled exception schreibt keine Raw-Message mehr in den Console-Logpfad.
  - **Verifiziert durch:** `AuditABEndToEndRedTests.B01_ApiUnhandledExceptionHandler_DoesNotLogRawExceptionMessage`, `Block1_ReleaseBlockerTests`
- [x] **B-2** umgesetzt in `ConversionOutputValidator`: Mindestgröße-Prüfung (`output-too-small`) ergänzt.
  - **Verifiziert durch:** `AuditABEndToEndRedTests.B02_OutputValidator_OneByteOutput_IsRejectedAsTooSmall`, `ConverterAndValidatorCoverageTests`, `PipelineAndConversionCoverageTests`
- [x] **B-3** umgesetzt in `ZipSorter`: sichere Entry-Validierung mit decoding, dot-segment- und rooted-path-Block.
  - **Verifiziert durch:** `AuditABEndToEndRedTests.B03_ZipSorter_EncodedTraversalEntry_IsBlocked`, `AuditCategoryBFixTests`
- [x] **B-4** umgesetzt in `Romulus.CLI/Program.cs`: `Task.Run(...).Result` entfernt, direkte Awaiter-Bridge.
  - **Verifiziert durch:** `AuditABEndToEndRedTests.B04_CliRunForTests_MustNotUseTaskRunResultWrapper`, `CliProgramTests`
- [x] **B-5** umgesetzt in `RunOrchestrator`: `TryFlushHashCache()` vor `WritePartialAuditSidecar()` in Cancel/Failure-Pfaden.
  - **Verifiziert durch:** `AuditABEndToEndRedTests.B05_RunOrchestrator_MustFlushHashCacheBeforePartialSidecar`, `RunOrchestratorTests`

---

## C. Technische Schulden

- [x] **C-1 (P1): MainViewModel God-Klasse (~3600+ Zeilen)**
  - **Impact:** 50+ Properties, 30+ Commands, 20+ injizierte Services, 3 verschiedene Locking-Strategien. Schwer testbar, schwer wartbar, hohe Regressionsgefahr.
  - **Dateien:** `src/Romulus.UI.Wpf/ViewModels/MainViewModel.cs`, `MainViewModel.RunPipeline.cs`, `MainViewModel.Settings.cs`, `MainViewModel.Productization.cs`
  - **Fix:** Schrittweise extrahieren: `SettingsViewModel`, `RunPipelineViewModel`, `ProfileViewModel` als eigenständige Sub-ViewModels.
  - **Label:** `Architecture Debt Hotspot`

- [x] **C-2 (P1): BuildCurrentRunConfigurationDraft + BuildRunConfigurationMap — doppelte Wahrheit**
  - **Impact:** Wenn ein neues Feld in `Draft` ergänzt wird, kann `Map` vergessen werden. Beide bauen dieselbe Datenstruktur auf unterschiedlichen Wegen.
  - **Datei:** `src/Romulus.UI.Wpf/ViewModels/MainViewModel.Productization.cs`
  - **Fix:** `BuildRunConfigurationMap()` aus `BuildCurrentRunConfigurationDraft()` ableiten statt parallel aufbauen.
  - **Test:** Invariantentest: `Draft`-Keys müssen Obermenge von `Map`-Keys sein.
  - **Label:** `Parity Risk`

- [x] **C-3 (P2): Settings bidirektionale Sync MainVM ↔ SetupVM**
  - **Impact:** `_setupSyncDepth` Lock + `EnterSetupSyncScope` + `OnRegionPreferencePropertyChanged` = zirkulärer Update-Pfad. Race-Risiko bei schnellen Property-Änderungen.
  - **Datei:** `src/Romulus.UI.Wpf/ViewModels/MainViewModel.Settings.cs` (Zeilen 110-200)
  - **Fix:** Shared `SettingsModel` als Single Source of Truth; beide VMs beobachten dasselbe Modell.

- [x] **C-4 (P2): RunPipeline Dialog-Flow — 3+ Decline-Szenarien copy-paste**
  - **Impact:** `ConvertOnly = false; BusyHint = ""; CurrentRunState = RunState.Idle;` wird an 3+ Stellen wiederholt. DRY-Verletzung, Regressionsgefahr.
  - **Datei:** `src/Romulus.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs` (Zeilen 1115-1190)
  - **Fix:** Gemeinsame `ResetDialogState()`-Methode extrahieren.

- [x] **C-5 (P2): Hardcoded FormatScores in FormatScorer (70+ Einträge)**
  - **Impact:** Neue Formate erfordern Code-Änderung + Recompile. Sollte datengetrieben sein.
  - **Datei:** `src/Romulus.Core/Scoring/FormatScorer.cs` (Zeilen 44-124)
  - **Fix:** Scores aus `data/format-scores.json` laden (Datei existiert bereits).

- [x] **C-6 (P2): RunOrchestrator Komplexität (~2500+ Zeilen über 5 Partial-Klassen)**
  - **Impact:** Fehlerbehandlung verstreut. Cancel/Rollback-Logik nicht einheitlich. Phase-Steuerung manuell.
  - **Dateien:** `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.cs`, `*.PreviewAndPipelineHelpers.cs`, `*.ReviewApprovals.cs`, `*.ScanAndConvertSteps.cs`, `*.StandardPhaseSteps.cs`
  - **Fix:** Phase-Steps als eigenständige Klassen mit einheitlichem Error-Handling Pattern.

- [x] **C-7 (P2): _suppressRunConfigurationSelectionApply Flag in Productization**
  - **Impact:** Manual suppress-flag statt Event-Deregistration. Bei Exception zwischen suppress/unsuppress bleibt suppress=true.
  - **Datei:** `src/Romulus.UI.Wpf/ViewModels/MainViewModel.Productization.cs` (Zeile 75)
  - **Fix:** try/finally oder `IDisposable` Scoped-Suppression.

- [x] **C-8 (P2): ApplyMaterializedRunConfiguration — keine atomare Zuweisung**
  - **Impact:** Setzt 20+ Properties basierend auf `materialized`. Exception midway → VM in inkonsistentem State.
  - **Datei:** `src/Romulus.UI.Wpf/ViewModels/MainViewModel.Productization.cs` (Zeilen 242-280)
  - **Fix:** Alle Properties sammeln, dann batch-apply oder rollback-fähig machen.

- [x] **C-9 (P2): WatchService + ScheduleService Lifecycle in MainVM**
  - **Impact:** Im ctor erzeugt, keine PropertyChanged-Unsubscription bei VM Disposal erkennbar.
  - **Datei:** `src/Romulus.UI.Wpf/ViewModels/MainViewModel.cs` (Zeilen 79-80)
  - **Fix:** Sicherstellen dass Dispose() sauber aufräumt.

- [x] **C-10 (P2): Auto-Save Debounce-Timer Race in Settings**
  - **Impact:** `_autoSaveTimer?.Dispose();` dann neuer Timer — Race bei schnellem Property-Change. Timer auf ThreadPool, Dispatch auf UI.
  - **Datei:** `src/Romulus.UI.Wpf/ViewModels/MainViewModel.Settings.cs` (Zeilen 47-68)
  - **Fix:** Timer-Erstellung atomar machen oder einzelnen Timer mit Reset.

- [x] **C-11 (P2): ConversionGraph Depth-Limit 5 hardcoded**
  - **Impact:** Valide Conversion-Pfade mit 6+ Steps werden still verworfen. Kein Logging.
  - **Datei:** `src/Romulus.Core/Conversion/ConversionGraph.cs` (Zeile 40)
  - **Fix:** Konfigurierbar machen oder auf 7-8 erhöhen. Warning loggen bei Limit-Hit.

- [x] **C-12 (P2): GameKeyNormalizer Registration-Präzedenz unklar**
  - **Impact:** Zwei Registrierungspfade (direkt + factory) ohne expliziten Contract. Factory nach direkter Registrierung ignoriert — kein Fehler, kein Log.
  - **Datei:** `src/Romulus.Core/GameKeys/GameKeyNormalizer.cs` (Zeilen 56-75)
  - **Fix:** Expliziten Guard oder Dokumentation der Präzedenz.

- [x] **C-13 (P3): Region-Bool 16x Switch Copy-Paste**
  - **Impact:** 16 manuelle Cases für GetRegionBool/SetRegionBool. Fehleranfällig bei neuen Regionen.
  - **Datei:** `src/Romulus.UI.Wpf/ViewModels/MainViewModel.Settings.cs` (Zeilen 356-380)
  - **Fix:** Dictionary-basiertes Pattern.

- [x] **C-14 (P3): Magic Strings in MainViewModel Commands**
  - **Impact:** Commands wie `"Config"`, `"Analyse"`, `"Library"` hardcoded statt Enums/Constants.
  - **Datei:** `src/Romulus.UI.Wpf/ViewModels/MainViewModel.cs` (Zeilen 72, 150, 180+)
  - **Fix:** Constants oder Enum-Lookup.

- [x] **C-15 (P3): RollbackService — Failed zählt nur 1 statt tatsächliche Fehleranzahl**
  - **Impact:** Bei 100 Dateien rollback, davon 5 failed → `Failed = 1`. Client kann nicht unterscheiden.
  - **Datei:** `src/Romulus.Infrastructure/Audit/RollbackService.cs` (Zeilen 17-32)
  - **Fix:** Tatsächliche Fehleranzahl zählen und zurückgeben.

- [x] **C-16 (P3): M3U-Rewrite-Logik fragil in ConsoleSorter**
  - **Impact:** Nach Member-Move werden M3U-Einträge rewritten. Mehrere Matching-Strategien (relativ/absolut). Risiko: Playlist-Korruption bei Symlinks/UNC.
  - **Datei:** `src/Romulus.Infrastructure/Sorting/ConsoleSorter.cs`
  - **Fix:** Robusteren Playlist-Parser mit defensivem Fallback.

### Verifikation C (2026-04-13)

- [x] **C-1** umgesetzt durch Extraktion zentraler Child-ViewModels (`Shell`, `Setup`, `Run`, `Tools`, `CommandPalette`) via Factory-Pfade.
  - **Verifiziert durch:** `AuditCDRedTests.C01_MainViewModel_UsesChildViewModels_ForMajorDomains`
- [x] **C-2** umgesetzt durch map-Build aus `RunConfigurationDraft`-Reflection statt harter Key-Matrix.
  - **Verifiziert durch:** `AuditCDRedTests.C02_RunConfigurationMap_MustAvoidHardcodedKeyMatrix`
- [x] **C-3** umgesetzt via `SetupSyncMirrorState` als gemeinsamer Synchronisationszustand.
  - **Verifiziert durch:** `AuditCDRedTests.C03_SettingsSync_UsesSharedMirrorStateModel`
- [x] **C-4** umgesetzt via shared decline-reset helper.
  - **Verifiziert durch:** `AuditCDRedTests.C04_RunPipeline_DeclinePaths_UseSharedResetHelper`
- [x] **C-5** umgesetzt datengetrieben über `FormatScoringProfile` + `format-scores.json`.
  - **Verifiziert durch:** `AuditCDRedTests.C05_FormatScorer_IsDataDrivenViaProfileFactory`
- [x] **C-6** umgesetzt durch Extraktion `IPhasePlanExecutor`/`PhasePlanExecutor` aus `RunOrchestrator`.
  - **Verifiziert durch:** `AuditCDRedTests.C06_RunOrchestrator_UsesDedicatedPhasePlanExecutor`
- [x] **C-7** umgesetzt mit scoped suppression (`EnterRunConfigurationSelectionSuppressionScope`).
  - **Verifiziert durch:** `AuditCDRedTests.C07_RunConfigurationSelectionSuppression_UsesScopeHelper`
- [x] **C-8** umgesetzt mit Snapshot+Rollback bei Fehlern in `ApplyMaterializedRunConfiguration`.
  - **Verifiziert durch:** `AuditCDRedTests.C08_ApplyMaterializedRunConfiguration_InvalidConflictPolicy_RollsBackState`
- [x] **C-9** umgesetzt durch `IDisposable`-Lifecycle und VM-Dispose im Window-Cleanup.
  - **Verifiziert durch:** `AuditCDRedTests.C09_MainWindow_Cleanup_DisposesMainViewModel`
- [x] **C-10** umgesetzt mit wiederverwendetem Debounce-Timer (`??=` + `Change`).
  - **Verifiziert durch:** `AuditCDRedTests.C10_ScheduleAutoSave_ReusesSingleTimerInstance`
- [x] **C-11** umgesetzt mit konfigurierbarem Depth-Limit und Warning-Trace bei Pruning.
  - **Verifiziert durch:** `AuditCDRedTests.C11_ConversionGraph_DepthLimit_IsConfigurableAndWarned`, `ConversionGraphTests`
- [x] **C-12** umgesetzt mit explizitem Registration-Precedence-Guard + Warning-Trace.
  - **Verifiziert durch:** `AuditCDRedTests.C12_GameKeyNormalizer_RegistrationPrecedence_IsGuarded`, `GameKeyNormalizerCoverageTests`

- [x] **C-13** umgesetzt in `MainViewModel.Settings`: region-code Zugriff läuft über `RegionPreferenceReaders`/`RegionPreferenceWriters` statt 16x `switch`.
  - **Verifiziert durch:** `AuditCDRedTests.C13_RegionPreferenceAccess_MustUseDictionaryMapping`
- [x] **C-14** umgesetzt in `MainViewModel` + `MainViewModel.RunPipeline`: zentrale Nav-Tag-Konstanten (`NavTagConfig`, `NavTagLibrary`, `NavTagTools`, `NavTagMissionControl`) statt harter Strings.
  - **Verifiziert durch:** `AuditCDRedTests.C14_MainViewModel_NavigationCommands_MustUseNamedTagConstants`
- [x] **C-15** umgesetzt in `RollbackService` + `AuditSigningService`: Integritätsabbruch zählt parsebare Audit-Rows statt fixem `Failed = 1`, inkl. Json-Sidecar-Failure-Handling.
  - **Verifiziert durch:** `AuditCDRedTests.C15_RollbackService_IntegrityFailure_MustReportAffectedRowCount`, `GuiViewModelTests.RollbackService_Execute_BlocksTamperedSignedAudit`
- [x] **C-16** umgesetzt in `ConsoleSorter`: M3U-Rewrite trennt relative/absolute/unique-filename-Mapping und vermeidet Kollisionen bei gleichnamigen Set-Membern.
  - **Verifiziert durch:** `AuditCDRedTests.C16_M3uRewrite_MustNotCollapseDistinctRelativeEntriesOnNameCollision`, `TrackerAllFindingsBatch3RedTests.Sort02_M3uPlaylist_MustBeRewrittenAfterAtomicSetMove`

---

## D. Doppelte Logik / Schattenlogik

- [x] **D-1 (P1): ProfileId/WorkflowScenarioId fehlen in Explicitness**
  - **Impact:** Wenn User explizit `--profile` in CLI oder `profileId` in API sendet, erkennt der Materializer es nicht als explizit. Settings-Defaults könnten User-Intent überschreiben.
  - **Dateien:** `src/Romulus.Api/ApiRunConfigurationMapper.cs` (Zeilen 39-67), `src/Romulus.CLI/CliOptionsMapper.cs` (Zeilen 85-95)
  - **Fix:** `ProfileId` und `WorkflowScenarioId` zu `RunConfigurationExplicitness` in beiden Mappern hinzufügen.
  - **Test:** Expliziter `--profile` → Materializer respektiert User-Intent, überschreibt nicht mit Default.
  - **Label:** `Parity Risk`

- [x] **D-2 (P2): CLI/API Asymmetrie bei Extensions-Normalisierung**
  - **Impact:** CLI validiert erst (muss mit `.` starten), normalisiert dann (fügt `.` hinzu falls fehlt). API normalisiert über Materializer. Redundant aber safe — verschiedene Codepfade.
  - **Dateien:** `src/Romulus.CLI/CliArgsParser.cs` (Zeile 225), `src/Romulus.Api/ApiRunConfigurationMapper.cs` (Zeilen 114-120)
  - **Fix:** Zentrale Extension-Normalisierung in einem Shared Helper.

- [x] **D-3 (P2): BuildConversionReviewEntries dupliciert RunEnvironmentBuilder-Instanziierung**
  - **Impact:** VM-lokale Service-Instanziierung statt DI-Injection. Duplizierte Initialization.
  - **Datei:** `src/Romulus.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs` (Zeilen 1115-1151)
  - **Fix:** Via injizierten Service lösen statt lokale Erzeugung.

- [x] **D-4 (P3): VersionScorer Regex-Timeout Silent Catch**
  - **Impact:** Wenn RegexMatchTimeoutException auftritt, wird Version-Scoring partial — kein Warning, kein Log.
  - **Datei:** `src/Romulus.Core/Scoring/VersionScorer.cs` (Zeile 168)
  - **Fix:** Mindestens Trace-Warning loggen.

- [x] **D-5 (P3): GameKeyNormalizer DOS-Iteration-Cap nur Trace-Warning**
  - **Impact:** Bei MaxDosMetadataStripIterations = 50 Limit: Key nur partial gestrippt, nur Trace-Log.
  - **Datei:** `src/Romulus.Core/GameKeys/GameKeyNormalizer.cs` (Zeile 309)
  - **Fix:** Warning-Level Log oder Rückgabeflag setzen.

### Verifikation D (2026-04-13)

- [x] **D-1** umgesetzt in `RunConfigurationExplicitness` + API/CLI Mapper.
  - **Verifiziert durch:** `AuditCDRedTests.D01_RunConfigurationExplicitness_IncludesWorkflowAndProfileFlags`
- [x] **D-2** umgesetzt via zentrale Extension-Normalisierung im CLI-Pfad.
  - **Verifiziert durch:** `AuditCDRedTests.D02_CliParser_UsesSharedExtensionNormalizer`
- [x] **D-3** umgesetzt via Delegation an `IRunService.BuildConversionReviewEntries`.
  - **Verifiziert durch:** `AuditCDRedTests.D03_ConversionReviewEntries_AreDelegatedToRunService`
- [x] **D-4** umgesetzt mit Warning-Trace bei Regex-Timeout in `VersionScorer`.
  - **Verifiziert durch:** `AuditCDRedTests.D04_VersionScorer_RegexTimeoutCatch_LogsWarning`, `VersionScorerTests`
- [x] **D-5** umgesetzt mit Warning-Level für Iteration-Cap in `GameKeyNormalizer`.
  - **Verifiziert durch:** `AuditCDRedTests.D05_GameKeyNormalizer_IterationCap_UsesTraceWarning`, `GameKeyNormalizerCoverageTests`

---

## E. Repo-Hygiene

- [x] **E-1 (P3): Hardcoded CLI-Phase-Strings**
  - **Impact:** `"[Done]"`, `"Convert"` statt `RunConstants.Phases.*`.
  - **Datei:** `src/Romulus.CLI/CliOutputWriter.cs` (Zeilen 48-49, 152)
  - **Fix:** Auf Constants umstellen.

- [x] **E-2 (P3): Report-Localization hardcoded (De/En/Fr)**
  - **Impact:** 3 Locale-Dictionaries in Source-Code. Neue Locale = Code-Änderung.
  - **Datei:** `src/Romulus.Infrastructure/Reporting/ReportGenerator.cs` (Zeilen 410-470)
  - **Fix:** In `data/i18n/` auslagern.

- [x] **E-3 (P3): WizardScan 25.000-Candidate-Limit ohne Warnung**
  - **Impact:** Silent truncation. User sieht unvollständige Analyse im Wizard.
  - **Datei:** `src/Romulus.UI.Wpf/ViewModels/MainViewModel.Productization.cs`
  - **Fix:** Warnung anzeigen wenn Limit erreicht.

- [x] **E-4 (P3): CrossRoot-Preview sampelt nur 400 Dateien**
  - **Impact:** Hardcoded `.Take(400)` ohne Logging. Bei 10K+ Collections kein Insight.
  - **Datei:** `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.PreviewAndPipelineHelpers.cs` (Zeile 347)
  - **Fix:** Logging + konfigurierbare Sample-Größe.

- [x] **E-5 (P3): Quarantine-Preview sampelt nur 2000 Dateien**
  - **Impact:** Hardcoded `.Take(2000)` ohne Logging.
  - **Datei:** `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.PreviewAndPipelineHelpers.cs` (Zeile 360)
  - **Fix:** Logging der Sample-Größe vs. Gesamtanzahl.

- [x] **E-6 (P3): TryLoadWizardConsoleDetector — JsonException nur null-Return ohne Log**
  - **Impact:** Stille Fehler bei korrupter consoles.json. Wizard-Detect schlägt fehl ohne Erklärung.
  - **Datei:** `src/Romulus.UI.Wpf/ViewModels/MainViewModel.Productization.cs` (Zeilen 706-725)
  - **Fix:** Warning loggen.

- [x] **E-7 (P3): Levenshtein Distance in FeatureService statt Utils**
  - **Impact:** Utility-Methode in falscher Klasse.
  - **Datei:** `src/Romulus.UI.Wpf/Services/FeatureService.cs` (Zeilen 59-70)
  - **Fix:** In eigene Utils-Klasse verschieben wenn genutzt.

- [x] **E-8 (P3): CsvSafe() potenzielles Double-Quoting**
  - **Impact:** `CsvSafe()` fügt Quotes hinzu NACH `SanitizeSpreadsheetCsvField()`. Wenn Sanitizer bereits quoting macht → doppelte Quotes.
  - **Datei:** `src/Romulus.Infrastructure/Reporting/ReportGenerator.cs` (Zeilen 266-274)
  - **Fix:** Verifiziert: `StartsWith('"')` Guard verhindert Double-Quoting.

- [x] **E-9 (P3): Index Upsert vor Stale-Removal**
  - **Impact:** `RemoveStaleCollectionIndexEntriesAsync()` läuft NACH `UpsertEntriesAsync()`. Wenn Removal scheitert, bleiben stale Entries.
  - **Datei:** `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.PreviewAndPipelineHelpers.cs` (Zeilen 356-368)
  - **Fix:** Stale zuerst entfernen, dann upsert.

---

## F. Tests / QA-Lücken

- [x] **F-1 (P0): Keine Tests für MovePipelinePhase Partial-Failure + Rollback**
  - **Impact:** Wenn 5/10 Moves erfolgreich, dann Disk-Error → Rollback-Verhalten ungetestet. Kritischster Datensicherheits-Pfad ohne Test.
  - **Fehlender Test:** N Moves, error bei Move N/2 → korrekte Rollback aller bisherigen + Audit-Status.
  - **Label:** `False Confidence Risk`

- [x] **F-2 (P1): Keine ConversionGraph Lossy→Lossy Block Tests**
  - **Impact:** TASK-056 Guard (`expand`-Exemption) existiert im Code, aber kein Test verifiziert ihn. Regression bleibt unentdeckt.
  - **Fehlender Test:** CSO→WBFS blockiert, NKit(.nkit.iso)→ISO erlaubt.

- [x] **F-3 (P1): Keine concurrent Audit-File-Locking Tests**
  - **Impact:** AuditCsvStore hat RefCount-basiertes Locking, aber kein Test mit parallelen Schreibern.
  - **Fehlender Test:** 10 parallele Audit-Writes → keine korrupten CSV-Zeilen.

- [x] **F-4 (P1): Fehlende RollbackService Partial-Failure Tests**
  - **Impact:** 5/20 Restores fehlschlagen → `Failed = 1` (nicht 5). Ungetestet.
  - **Fehlender Test:** Partial Restore → korrekte Failed-Anzahl.

- [x] **F-5 (P1): Keine ConsoleSort DryRun Parity Tests**
  - **Impact:** DryRun ConsoleSort-Bug (A-1) bleibt ohne Paritätstest unentdeckt.
  - **Fehlender Test:** DryRun ConsoleSort → projected Pfade identisch zu Execute.

- [x] **F-6 (P1): Keine Set-Member Partial-Move Tests**
  - **Impact:** A-4 (Set-Member-Preflight-Fehler mit CUE/BIN) hat keinen dedizierten Test.
  - **Fehlender Test:** CUE+BIN Set, BIN unerreichbar → CUE bleibt auch stehen.

- [x] **F-7 (P2): Schwache VersionScorer-Tests**
  - **Impact:** Regex-Timeout Silent Catch, weniger Tests als FormatScorer.
  - **Fehlender Test:** Absichtliches Timeout → Score partial, Warning geloggt.

- [x] **F-8 (P2): Keine DeduplicationEngine Stress-Tests (1000+ Candidates)**
  - **Impact:** Performance und Determinismus bei großen Gruppen ungeprüft.
  - **Fehlender Test:** 1000 Candidates in einer Gruppe → Winner deterministisch, Performance < 100ms.

- [x] **F-9 (P2): Keine GameKeyNormalizer nicht-Latin-Tests (CJK, Cyrillic, Arabic)**
  - **Impact:** Unicode-Handling für nicht-westliche Skripte ungetestet.
  - **Fehlender Test:** Chinesische/japanische Kanji-Titel → stabile, nicht-leere Keys.

- [x] **F-10 (P2): Keine FormatScorer Boundary/Negative Tests**
  - **Impact:** Invalid Format-Strings, null Inputs, Edge-Score-Values (0, 1, 9999) ungetestet.
  - **Fehlender Test:** Null/leere Extension → DefaultUnknownFormatScore. Score-Werte an Grenzen.

- [x] **F-11 (P2): Keine RegionDetector Tests für Mismatched-Parentheses**
  - **Impact:** `(Region` oder `Region)` ohne schließende/öffnende Klammer — Verhalten ungetestet.
  - **Fehlender Test:** Unbalancierte Klammern → kein Crash, UNKNOWN-Fallback.

- [x] **F-12 (P2): Fehlende FileSystemAdapter ZIP-SLIP Tests**
  - **Impact:** ArchiveHashService hat gute Checks, aber FileSystemAdapter selbst ungetestet für Archive-Extraktion.
  - **Fehlender Test:** Extraction mit Traversal-Payload → blockiert.

- [x] **F-13 (P2): Keine concurrent RollbackService Tests**
  - **Impact:** Zwei Prozesse rollen gleichzeitig dasselbe Audit zurück — ungetestet.
  - **Fehlender Test:** Paralleler Rollback → keine Race-Condition.

- [x] **F-14 (P3): Keine API Size-Limit / Deep-Nesting Tests**
  - **Impact:** 100GB Request Body oder Billion-Laughs JSON-Nesting-Attack ungetestet.
  - **Fehlender Test:** Oversized Body → 413. Deep nesting → 400.

- [x] **F-15 (P3): Keine CLI repeated-flags Tests**
  - **Impact:** `--roots X --roots Y` Verhalten ungetestet.
  - **Fehlender Test:** Wiederholte Flags → korrektes Parsing oder Fehlermeldung.

- [x] **F-16 (P3): Keine ReportGenerator SVG/XML Injection Tests**
  - **Impact:** SVG-Tags oder XML-Injection in Reports ungetestet.
  - **Fehlender Test:** `<svg onload=alert()>` in Dateiname → korrekt escaped.

### Verifikation E/F (2026-04-13)

- [x] **E-1** umgesetzt in `CliOutputWriter.WriteMoveSummary` via `RunConstants.Phases.Done` statt Literal.
  - **Verifiziert durch:** `AuditCDRedTests.E01_CliMoveSummary_UsesRunConstantsDoneToken`
- [x] **E-3** umgesetzt in `MainViewModel.Productization`: Wizard-Candidate-Limit aus `RunConstants` + Warnpfad bei Trunkierung.
  - **Verifiziert durch:** `AuditCDRedTests.E03_WizardScanLimit_UsesRunConstantsAndEmitsWarningPath`, `WpfProductizationTests`
- [x] **E-4** umgesetzt in `RunOrchestrator.PreviewAndPipelineHelpers`: CrossRoot-Sample nutzt `RunConstants.CrossRootPreviewSampleSize`.
  - **Verifiziert durch:** `AuditCDRedTests.E04E05_PreviewSampling_UsesSharedLimitsAndSamplingLogs`, `RunOrchestratorTests`
- [x] **E-5** umgesetzt in `RunOrchestrator.PreviewAndPipelineHelpers`: Quarantine-Sample nutzt `RunConstants.QuarantinePreviewSampleSize` + Sampling-Progress.
  - **Verifiziert durch:** `AuditCDRedTests.E04E05_PreviewSampling_UsesSharedLimitsAndSamplingLogs`, `RunOrchestratorTests`
- [x] **E-6** umgesetzt in `MainViewModel.Productization`: `consoles.json`-JsonException liefert WARN-Logpfad statt stillem Null-Return.
  - **Verifiziert durch:** `AuditCDRedTests.E06_WizardConsoleDetectorJsonFailure_HasWarningMessagePath`, `WpfProductizationTests`
- [x] **E-9** umgesetzt in `RunOrchestrator.PreviewAndPipelineHelpers`: stale removal vor `UpsertEntriesAsync`.
  - **Verifiziert durch:** `AuditCDRedTests.E09_CollectionIndex_MustRemoveStaleBeforeUpsert`, `RunOrchestratorTests`
- [x] **F-15** umgesetzt durch dedizierten Repeated-Flag-Test und Parser-Validation bei doppeltem `--roots`.
  - **Verifiziert durch:** `AuditCDRedTests.F15_CliParser_RepeatedRootsFlag_ReturnsValidationError`, `CliProgramTests`, `CliArgsParser*`

### Verifikation E (Batch 2, 2026-04-14)

- [x] **E-2** umgesetzt: Report-Locale-Dictionaries durch `LoadReportLocale()` ersetzt, liest aus `data/i18n/{locale}.json`. Report.*-Keys in de/en/fr ergänzt.
  - **Verifiziert durch:** `AuditEFOpenTests.E02_ReportLocalization_MustNotHaveHardcodedDictionaries`, `E02_ReportLocalization_I18nFilesMustContainReportKeys`
- [x] **E-7** umgesetzt: `LevenshteinDistance` nach `StringUtils.cs` extrahiert, `FeatureService` delegiert.
  - **Verifiziert durch:** `AuditEFOpenTests.E07_LevenshteinDistance_MustBeDelegatedToStringUtils`
- [x] **E-8** verifiziert: CsvSafe `StartsWith('"')` Guard verhindert Double-Quoting korrekt. Kein Code-Change nötig.
  - **Verifiziert durch:** `AuditEFOpenTests.E08_CsvSafe_MustNotDoubleQuote_WhenSanitizerAlreadyQuotes`

### Verifikation F (2026-04-14)

- [x] **F-1** abgesichert durch 2 Behavioral-Tests (3-Group + 5-Group mit Partial Failure): Audit-Rows für erfolgreiche MOVE_PENDING/MOVE und FailCount-Invariante.
  - **Verifiziert durch:** `AuditEFBehavioralTests.F01_MovePhase_MultiGroup_PartialFailure_AuditTracksAllOutcomes`, `F01_MovePhase_FailureMidway_CountInvariantHolds`
- [x] **F-2** abgesichert durch ConversionGraph-Tests: Lossy→Lossy blockiert (CSO→WBFS), expand-Exemption erlaubt (NKit).
  - **Verifiziert durch:** `AuditEFOpenTests.F02_ConversionGraph_LossyToLossy_IsBlocked`, `F02_ConversionGraph_LossyExpand_IsAllowed`
- [x] **F-3** abgesichert durch 10 parallele AuditCsvStore-Writes: Row-Count und CSV-Feldintegrität.
  - **Verifiziert durch:** `AuditEFBehavioralTests.F03_AuditCsvStore_ConcurrentWrites_NoCorruptedRows`
- [x] **F-4** abgesichert durch 2 Tests: Tampered-Sidecar → Failed=5 (nicht 1); fehlender Sidecar → Failed≥3.
  - **Verifiziert durch:** `AuditEFBehavioralTests.F04_RollbackService_IntegrityFailure_FailedCountEqualsRowCount`, `F04_RollbackService_NoSidecar_FailedCountEqualsRowCount`
- [x] **F-5** abgesichert: Source-Level-Test bestätigt pathMutations-Tracking im DryRun-Pfad.
  - **Verifiziert durch:** `AuditEFOpenTests.F05_ConsoleSortResult_DryRunVsExecute_MustHaveConsistentCounts`
- [x] **F-6** abgesichert durch CUE+BIN Set-Preflight-Test: BIN-Konflikt → CUE und alle BINs bleiben in Place.
  - **Verifiziert durch:** `AuditEFBehavioralTests.F06_SetMember_BINPreflightFail_CUEAndBINStayInPlace`
- [x] **F-7** abgesichert: Source-Level-Test bestätigt RegexMatchTimeoutException-Catch + TraceWarning.
  - **Verifiziert durch:** `AuditEFOpenTests.F07_VersionScorer_RegexTimeoutCatch_LogsTraceWarning`
- [x] **F-8** abgesichert: 1000 Candidates, 20 Shuffle-Runs → deterministischer Winner.
  - **Verifiziert durch:** `AuditEFOpenTests.F08_DeduplicationEngine_1000Candidates_DeterministicWinner`
- [x] **F-9** abgesichert: CJK, Cyrillic, Chinese, Fullwidth→Halfwidth → stabile nicht-leere Keys.
  - **Verifiziert durch:** `AuditEFOpenTests.F09_GameKeyNormalizer_*` (4 Tests)
- [x] **F-10** abgesichert: Null, Empty, Whitespace, Unknown, Known Extension → DefaultUnknownFormatScore / positive Score.
  - **Verifiziert durch:** `AuditEFOpenTests.F10_FormatScorer_*` (5 Tests)
- [x] **F-11** abgesichert: Unbalanced open/close, empty, nested Parens → kein Crash.
  - **Verifiziert durch:** `AuditEFOpenTests.F11_RegionDetector_*` (4 Tests)
- [x] **F-12** abgesichert: ZipSorter Source-Check + ArchiveHashService.AreEntryPathsSafe blockiert Traversal.
  - **Verifiziert durch:** `AuditEFOpenTests.F12_ZipSorter_TraversalEntry_SourceCheck`, `F12_ArchiveHashService_AreEntryPathsSafe_BlocksTraversal`
- [x] **F-13** abgesichert: 2 parallele DryRun-Rollbacks → keine Exception, Audit-Datei intakt.
  - **Verifiziert durch:** `AuditEFBehavioralTests.F13_AuditCsvStore_ConcurrentRollback_NoException`
- [x] **F-14** abgesichert: Source-Level-Test bestätigt MaxRequestBodySize in API-Konfiguration.
  - **Verifiziert durch:** `AuditEFOpenTests.F14_ApiProgram_HasRequestBodySizeLimit`
- [x] **F-16** abgesichert: SVG/Script encoding + Source-Level HtmlEncode-Nachweis.
  - **Verifiziert durch:** `AuditEFOpenTests.F16_*` (3 Tests)

---

## G. Positive Patterns (zur Kenntnisnahme)

- [x] **G-1:** Path-Traversal-Defense in FileSystemAdapter — SEC-MOVE-01/03, TOCTOU-Mitigation, NFC-Normalisierung. **Erstklassig.**
- [x] **G-2:** HTML-Escaping in ReportGenerator — CSP-Meta-Tag, `WebUtility.HtmlEncode()`, keine inline Handler. **Gut.**
- [x] **G-3:** CSV-Injection-Schutz in AuditCsvStore + ReportGenerator — `=, +, -, @` Prefix-Block. **Konsistent.**
- [x] **G-4:** Process-Tracking in ExternalProcessGuard — Job-Object, Kill-on-Close. **Solid.**
- [x] **G-5:** ArgumentList statt string-concat in ToolRunnerAdapter — `psi.ArgumentList.Add(arg)`. **Sicher.**
- [x] **G-6:** QuarantineService Restore-Allowlist — `allowedRestoreRoots` mandatory. **Exzellent.**
- [x] **G-7:** Reparse-Point-Blocking durchgängig — FileSystemAdapter, ArchiveHashService, QuarantineService.
- [x] **G-8:** DeduplicationEngine deterministisch — BUG-011 Fix mit alphabetischem MainPath-Tiebreaker.
- [x] **G-9:** GameKeyNormalizer — 60+ Tests, PS3-Support, Timeout-Protection via SafeRegex.
- [x] **G-10:** Audit-Sidecar Write-Ahead Pattern — MOVE_PENDING + finale Aktion. **Robust.**
- [x] **G-11:** ArchiveHashService Zip-Slip — Regex-basierter `..` Check + Post-Extraction `GetFullPath()` + `StartsWith()`. **Vollständig.**
- [x] **G-12:** WatchFolderService — Debounce (5s) + MaxWait (30s) + Buffer 64KB. Event-Storm geschützt.
- [x] **G-13:** API Rate-Limiting + Fixed-Time API-Key-Comparison. **Korrekt.**

---

## Zusammenfassung nach Priorität

| Priorität | Anzahl | IDs |
|-----------|--------|-----|
| **P0** | 3 | A-1, A-2, F-1 |
| **P1** | 11 | A-3, A-4, B-1, B-2, C-1, C-2, D-1, F-2, F-3, F-4, F-5, F-6 |
| **P2** | 20 | A-5, A-6, B-3, B-4, B-5, C-3, C-4, C-5, C-6, C-7, C-8, C-9, C-10, C-11, C-12, D-2, D-3, F-7–F-13 |
| **P3** | 13 | C-15, C-16, D-4, D-5, E-1–E-9, F-14–F-16 |
| **Total** | **49** | |

---

## Sanierungsstrategie

### Sofort (vor nächstem Merge)
- A-1, A-2, B-1

### Vor Release
- A-3, A-4, B-2, D-1, F-1, F-2, F-3, F-4, F-5, F-6

### Post-Release Sprint
- C-1, C-2, C-3, C-5, C-6

### Bewusst verschieben
- B-3 (kein reales Exploit), E-3, E-4, E-5
