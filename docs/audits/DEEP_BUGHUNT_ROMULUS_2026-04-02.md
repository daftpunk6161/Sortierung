# Deep Bughunt Remediation – Romulus

Datum: `2026-04-02`  
Scope: `src/` only  
Status: `remediated and regression-covered`

## 1. Executive Verdict

Der Deep-Bughunt hat mehrere echte Release-Blocker und hohe Risiken bestaetigt. Die kritischen Defekte lagen nicht im fachlichen Score-Kern, sondern in Orchestrierung, Recovery-Wahrheit, Dateisystem-Mutationsprojektion, Audit-Finalisierung, Set-Integritaet bei `ConsoleSort` und Safety-Validierung.

Diese Befunde sind in diesem Remediation-Lauf end-to-end behoben worden:

- `ConvertOnly` nutzt jetzt dieselbe Abschlusslogik wie der Standardpfad, inklusive finalem Audit-Sealing.
- Angeforderte Reports sind jetzt verpflichtende Artefakte; doppelter Report-Schreibfehler degradiert den Run auf `completed_with_errors`.
- Rollback gilt nur noch bei verifizierter Audit-CSV plus `.meta.json`.
- `DatRename` rebased sofort alle spaeteren Pipeline-Eingaben.
- `ConsoleSort` bewegt Review- und Junk-Sets jetzt atomisch.
- Post-Execute-Wahrheit fuer GUI, CLI, API und Reports wird aus derselben Mutationskette projiziert.
- Erfolgreiche Conversion-Audits werden atomisch als Batch geschrieben.
- Rooted NTFS ADS-Quellen werden explizit blockiert.

## 2. Top Release-Blocker

### F-01 `ConvertOnly` ohne finale Audit-Versiegelung
- Schweregrad: `P0`
- Impact: Erfolgreiche Convert-only-Runs konnten rollback-relevante Dateien mutieren, ohne finale `.meta.json`.
- Status: `fixed`
- Kernfix:
  - `RunOrchestrator` fuehrt `ConvertOnly` nicht mehr vorzeitig aus dem gemeinsamen Abschluss heraus.
  - Gemeinsame Finalisierung schreibt Status, ExitCode, Report und finalen Audit-Sidecar fuer alle Execute-Pfade.
- Regression:
  - `RunOrchestratorTests.Execute_ConvertOnly_Success_WritesVerifiedAuditSidecar`

### F-02 `DatRename` erzeugt stale paths fuer Move/Sort/Convert
- Schweregrad: `P0`
- Impact: Nach physischem Rename arbeiteten Folgephasen mit veralteten Pfaden weiter.
- Status: `fixed`
- Kernfix:
  - `DatRenamePipelinePhase` liefert `PathMutation`-Eintraege.
  - `PipelineState.ApplyPathMutations(...)` rebased `AllCandidates`, `ProcessingCandidates`, `AllGroups`, `GameGroups`, `JunkRemovedPaths` und `DatAuditResult.Entries`.
  - `RunDatRenameStep` wendet diese Mutationen sofort an.
- Regression:
  - `RunOrchestratorTests.RunDatRenameStep_RebasesLoserPathsBeforeMove`
  - `RunOrchestratorTests.RunDatRenameStep_RebasesCandidatePathsBeforeConsoleSort`
  - `RunOrchestratorTests.RunDatRenameStep_RebasesWinnerPathsBeforeWinnerConversion`

### F-03 `ConsoleSort` zerlegt Multi-File-Sets im Review-/Junk-Pfad
- Schweregrad: `P1`
- Impact: Descriptor-Dateien konnten ohne ihre Member nach `_REVIEW` oder `_TRASH_JUNK` verschoben werden.
- Status: `fixed`
- Kernfix:
  - Review- und Blocked+Junk-Routen benutzen jetzt denselben atomischen Set-Move wie der Standardpfad.
  - Tatsaechliche Zielpfade aller Primary-/Member-Dateien werden als `PathMutations` erfasst.
- Regression:
  - `SortingTests.Sort_ReviewDecision_MovesCueSetAtomically`
  - `SortingTests.Sort_BlockedJunk_MovesCueSetAtomicallyToTrashJunk`

### F-04 CSV-only Recovery wurde faelschlich als rollback-faehig behandelt
- Schweregrad: `P1`
- Impact: GUI und API konnten Rollback freigeben, obwohl die verifizierende `.meta.json` fehlte.
- Status: `fixed`
- Kernfix:
  - `AuditRecoveryStateResolver` fuehrt die verifizierte Recovery-Wahrheit zentral.
  - `RunLifecycleManager`, `RunManager`, `RunService` und `MainViewModel` verwenden dieselbe Semantik.
  - `CanRollback` basiert jetzt auf verifizierter Sidecar-Integritaet statt reiner CSV-Existenz.
- Regression:
  - `RunManagerTests.CancelledRun_WithCsvOnlyAudit_RequiresManualCleanupRecoveryState`
  - `RunManagerTests.FailedRun_WithCsvOnlyAudit_RequiresManualCleanupRecoveryState`
  - `RunManagerTests.CompletedWithErrorsRun_WithCsvOnlyAudit_RequiresManualCleanupRecoveryState`
  - `RunManagerTests.CompletedWithErrorsRun_WithVerifiedSidecar_ExposesRollbackRecoveryState`
  - `GuiViewModelTests.ExecuteRunAsync_ShouldExecuteAndEnableRollback_WhenDatRenamePreviewConfirmed_Issue9`

### F-05 Sort/Rename/Conversion fuehrten zu unterschiedlichen Wahrheiten in GUI/CLI/API/Report
- Schweregrad: `P1`
- Impact: Execute mutierte Dateien, aber Kanaele projizierten weiter alte Pfade.
- Status: `fixed`
- Kernfix:
  - `PathMutation` als kanalneutrales Mutation-Model eingefuehrt.
  - `RunArtifactProjection` komponiert jetzt `DatRename -> ConsoleSort -> Conversion`.
  - API nutzt die projection-basierte Kandidatenwahrheit fuer Review-/Export-Pfade.
- Regression:
  - `RunArtifactProjectionTests.Project_ComposesDatRenameConsoleSortAndConversionMutations`
  - `RunOrchestratorTests.RunConsoleSortStep_RebasesWinnerPathsBeforeWinnerConversion`

### F-06 Erfolgreiche Conversion-Audits waren nicht atomisch
- Schweregrad: `P1`
- Impact: Teilweise geschriebene `CONVERT`/`CONVERT_SOURCE`-Zeilen konnten einen schon rollbackten Zustand falsch dokumentieren.
- Status: `fixed`
- Kernfix:
  - `IAuditStore.AppendAuditRows(...)` eingefuehrt.
  - `AuditCsvStore.AppendAuditRows(...)` schreibt Batch-Audits unter einem Lock und ersetzt die Datei atomisch ueber eine Temp-Datei.
  - `ConversionPhaseHelper` schreibt Erfolgs-Audits jetzt als Batch-Commit.
- Regression:
  - `AuditCsvStoreTests.AppendAuditRows_WhenReplaceFails_LeavesExistingAuditUnchanged`

### F-07 Angeforderter Report konnte fehlschlagen, ohne den Run zu degradieren
- Schweregrad: `P1`
- Impact: Optimistic success bei fehlendem Pflichtartefakt.
- Status: `fixed`
- Kernfix:
  - `TryGenerateRequestedReport(...)` liefert jetzt hart `false`, wenn weder Zielpfad noch Fallback funktionieren.
  - `FinalizeCompletedRun(...)` degradiert in diesem Fall von `ok` auf `completed_with_errors`, ExitCode `1`.
- Regression:
  - `RunOrchestratorTests.Execute_WithInvalidReportPath_DegradesToCompletedWithErrors`
  - `ReportParityTests.CliRun_WhenReportCreationFails_ReturnsExitCodeOneAndNoFakeReportPath`

### F-08 Rooted ADS-Quellen wurden nicht explizit blockiert
- Schweregrad: `P1/P2`
- Impact: Safety-Policy fuer NTFS Alternate Data Streams war inkonsistent und verliess sich auf spaeteres I/O-Verhalten.
- Status: `fixed`
- Kernfix:
  - `FileSystemAdapter` prueft ADS jetzt auf der finalen Dateinamen-Komponente, auch fuer rooted paths.
  - Dieselbe Policy wird fuer `MoveItemSafely` und `CopyFile` verwendet.
- Regression:
  - `FileSystemAdapterTests.MoveItemSafely_BlocksRootedAdsSource`

## 3. Findings nach Bereichen

### Core / Recognition / Classification / Sorting

#### F-09 `ConsoleSort` hatte fachlich korrekte Konsolenentscheidungen, aber inkonsistente Set-Behandlung
- Schweregrad: `P1`
- Impact: Kein Scoring-Bug, aber defekter physischer Vollzug fuer Multi-File-Sets.
- Betroffene Dateien:
  - `src/Romulus.Infrastructure/Sorting/ConsoleSorter.cs`
  - `src/Romulus.Tests/SortingTests.cs`
- Ursache:
  - Review-/Junk-Wege gingen nur ueber den Primary-Move.
- Fix:
  - Gemeinsamer atomischer Set-Move fuer alle physischen Sort-Pfade.
  - Vollstaendige `PathMutations` statt nachtraeglicher Destination-Heuristik.
- Testabsicherung:
  - neue Review-/Junk-Set-Regressionen in `SortingTests`

### Infrastructure / Orchestration

#### F-10 `ConvertOnly` hatte einen Schattenabschluss
- Schweregrad: `P0`
- Impact: Sonderpfad umging gemeinsame Semantik fuer Status, Audit und Report.
- Betroffene Dateien:
  - `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.cs`
  - `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.ScanAndConvertSteps.cs`
  - `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.PreviewAndPipelineHelpers.cs`
- Fix:
  - `TryExecuteConvertOnlyPath(...)` fuehrt nur noch die Phase aus.
  - `FinalizeCompletedRun(...)` finalisiert nun alle erfolgreichen Execute-Pfade.

#### F-11 `DatRename` lieferte keine Pipeline-konsistente Nachbearbeitung
- Schweregrad: `P0`
- Impact: `Move`, `ConsoleSort` und `WinnerConversion` konnten mit alten Pfaden laufen.
- Betroffene Dateien:
  - `src/Romulus.Infrastructure/Orchestration/DatRenamePipelinePhase.cs`
  - `src/Romulus.Infrastructure/Orchestration/PhasePlanning.cs`
  - `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.StandardPhaseSteps.cs`
- Fix:
  - `PathMutation`-Erzeugung im Rename-Phase-Output.
  - Sofortige State-Rebase unmittelbar nach Rename.

### Conversion

#### F-12 Erfolgs-Audit war semantisch zweiteilig, technisch aber nicht atomisch
- Schweregrad: `P1`
- Impact: Forensik und Rollback-Interpretation konnten auseinanderlaufen.
- Betroffene Dateien:
  - `src/Romulus.Contracts/Ports/IAuditStore.cs`
  - `src/Romulus.Contracts/Models/ServiceModels.cs`
  - `src/Romulus.Infrastructure/Audit/AuditCsvStore.cs`
  - `src/Romulus.Infrastructure/Orchestration/ConversionPhaseHelper.cs`
  - `src/Romulus.Infrastructure/Orchestration/PipelinePhaseHelpers.cs`
- Fix:
  - Batched append API.
  - Single-lock / single-flush / atomic replace fuer Erfolgs-Commits.

### Reports / Audit / Rollback / Metrics

#### F-13 Recovery- und Statusprojektion waren nicht streng genug
- Schweregrad: `P1`
- Impact: falsche Rollback-Freigaben und inkonsistente Teilstatuskommunikation.
- Betroffene Dateien:
  - `src/Romulus.Infrastructure/Audit/AuditRecoveryStateResolver.cs`
  - `src/Romulus.Api/RunLifecycleManager.cs`
  - `src/Romulus.Api/RunManager.cs`
  - `src/Romulus.UI.Wpf/Services/IRunService.cs`
  - `src/Romulus.UI.Wpf/Services/RunService.cs`
  - `src/Romulus.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs`
- Fix:
  - Zentrale Recovery-Regel fuer `CanRollback` und `RecoveryState`.

#### F-14 Reports waren optional behandelt, obwohl der Nutzer sie explizit angefordert hatte
- Schweregrad: `P1`
- Impact: fehlendes Pflichtartefakt bei nominal erfolgreich gemeldetem Run.
- Betroffene Dateien:
  - `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.PreviewAndPipelineHelpers.cs`
  - `src/Romulus.Tests/RunOrchestratorTests.cs`
  - `src/Romulus.Tests/ReportParityTests.cs`
- Fix:
  - harte Degradierung auf `completed_with_errors`, ExitCode `1`

### GUI / WPF

#### F-15 GUI-Rollback-Gates waren zu optimistisch
- Schweregrad: `P1`
- Impact: Undo konnte in einem nicht verifizierten Zustand aktiv werden.
- Betroffene Dateien:
  - `src/Romulus.UI.Wpf/Services/IRunService.cs`
  - `src/Romulus.UI.Wpf/Services/RunService.cs`
  - `src/Romulus.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs`
  - `src/Romulus.Tests/GuiViewModelTests.cs`
- Fix:
  - GUI nutzt jetzt dieselbe verifizierte Recovery-Wahrheit wie API.

### CLI / API / Paritaet

#### F-16 API und Reports brauchten dieselbe post-mutation truth wie die Execute-Pipeline
- Schweregrad: `P1`
- Impact: API/Report/GUI konnten denselben Run unterschiedlich darstellen.
- Betroffene Dateien:
  - `src/Romulus.Infrastructure/Orchestration/RunArtifactProjection.cs`
  - `src/Romulus.Api/Program.cs`
  - `src/Romulus.Tests/RunArtifactProjectionTests.cs`
- Fix:
  - zentrale Projektion statt lokaler Nachrechnungen

### Safety / Security

#### F-17 ADS-Guards waren asymmetrisch
- Schweregrad: `P1/P2`
- Impact: rooted ADS-Quellen wurden nicht explizit abgefangen.
- Betroffene Dateien:
  - `src/Romulus.Infrastructure/FileSystem/FileSystemAdapter.cs`
  - `src/Romulus.Tests/FileSystemAdapterTests.cs`
- Fix:
  - gemeinsame ADS-Erkennung fuer Source und Destination

### Tests / QA-Luecken

#### F-18 Kritische Stale-Path- und Finalisierungsfaelle waren ungetestet
- Schweregrad: `P1`
- Impact: P0/P1-Bugs konnten unbemerkt ausgerollt werden.
- Status: `fixed`
- Neue Regressionen:
  - `ConvertOnly` + Audit-Sidecar
  - `DatRename + Move`
  - `DatRename + ConsoleSort`
  - `DatRename + WinnerConversion`
  - `ConsoleSort + WinnerConversion`
  - `Projection composition`
  - `Audit batch atomicity`
  - `CSV-only rollback gating`

## 4. Wichtige geaenderte Dateien

- `src/Romulus.Contracts/Models/RunExecutionModels.cs`
- `src/Romulus.Contracts/Models/ServiceModels.cs`
- `src/Romulus.Contracts/Ports/IAuditStore.cs`
- `src/Romulus.Infrastructure/Audit/AuditCsvStore.cs`
- `src/Romulus.Infrastructure/Audit/AuditRecoveryStateResolver.cs`
- `src/Romulus.Infrastructure/FileSystem/FileSystemAdapter.cs`
- `src/Romulus.Infrastructure/Orchestration/DatRenamePipelinePhase.cs`
- `src/Romulus.Infrastructure/Orchestration/PhasePlanning.cs`
- `src/Romulus.Infrastructure/Orchestration/PipelinePhaseHelpers.cs`
- `src/Romulus.Infrastructure/Orchestration/ConversionPhaseHelper.cs`
- `src/Romulus.Infrastructure/Orchestration/RunArtifactProjection.cs`
- `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.cs`
- `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.ScanAndConvertSteps.cs`
- `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.StandardPhaseSteps.cs`
- `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.PreviewAndPipelineHelpers.cs`
- `src/Romulus.Infrastructure/Sorting/ConsoleSorter.cs`
- `src/Romulus.Api/RunLifecycleManager.cs`
- `src/Romulus.Api/RunManager.cs`
- `src/Romulus.Api/Program.cs`
- `src/Romulus.UI.Wpf/Services/IRunService.cs`
- `src/Romulus.UI.Wpf/Services/RunService.cs`
- `src/Romulus.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs`

## 5. Neue oder aktualisierte Regressionstests

- `src/Romulus.Tests/RunOrchestratorTests.cs`
- `src/Romulus.Tests/RunArtifactProjectionTests.cs`
- `src/Romulus.Tests/RunManagerTests.cs`
- `src/Romulus.Tests/GuiViewModelTests.cs`
- `src/Romulus.Tests/ReportParityTests.cs`
- `src/Romulus.Tests/SortingTests.cs`
- `src/Romulus.Tests/FileSystemAdapterTests.cs`
- `src/Romulus.Tests/AuditCsvStoreTests.cs`
- `src/Romulus.Tests/DatRenameKpiPropagationTests.cs`

## 6. Validation

Durchgefuehrte Verifikation:

1. Gezielter Regressionslauf ueber die geaenderten Audit-, Recovery-, Sorting-, Projection- und Orchestrator-Bereiche.
2. Erzwungener Rebuild des Testprojekts, um stale Test-Binaries auszuschliessen.
3. Voller Testlauf ueber `Romulus.Tests`.

Ergebnis:

- `dotnet build src/Romulus.Tests/Romulus.Tests.csproj -t:Rebuild`
- `dotnet test src/Romulus.Tests/Romulus.Tests.csproj --no-build`
- Status: `7270 / 7270 Tests bestanden`

Hinweis:

- Waehrend der Verifikation gab es kurz zwei scheinbare Fehler in `ToolRunnerAdapterTests`, die auf einen stale Buildstand des Testhosts zurueckgingen. Nach erzwungenem Rebuild waren auch diese Faelle gruen. Es war kein offener Produktdefekt.

## 7. Rest-Risiko

Nach diesem Lauf bleibt kein bekannter offener `P0`- oder `P1`-Befund aus dem Deep-Bughunt uebrig.

Nicht Teil dieses Remediation-Laufs, aber fuer Releases weiter relevant:

- externe Tool-Hashes und reale Tool-Binaries muessen weiterhin zur ausgelieferten `data/tool-hashes.json` passen
- End-to-end-Smokes mit echten Dritt-Tools bleiben sinnvoll, obwohl die Code- und Testpfade jetzt konsistent abgesichert sind

## 8. Schlussurteil

Die zuvor identifizierten Deep-Bughunt-Befunde sind in diesem Scope behoben und regression-abgesichert. Der aktuell verifizierte Stand ist fuer den untersuchten Codepfad release-faehig.
