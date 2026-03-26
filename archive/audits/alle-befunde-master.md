---
goal: Alle Befunde zentral in einem Markdown dokumentieren
author: RomCleanup Team
version: 1.0
date_created: 2026-03-12
last_updated: 2026-03-12
status: In-Progress
tags: [audit, findings, master-index, p0, p1, security, qa]
---

# Alle Befunde - Master Index

Dieses Dokument ist die zentrale Sammelstelle fuer alle bekannten Befunde inklusive Priorisierung und Umsetzungsstatus.

## Quellen (kanonisch)

- [consolidated-bug-audit.md](consolidated-bug-audit.md)
- [feature-deep-dive-bug-audit-1.md](feature-deep-dive-bug-audit-1.md)
- [feature-deep-dive-ux-ui-audit-2.md](feature-deep-dive-ux-ui-audit-2.md)
- [feature-deep-dive-remaining-audit-3.md](feature-deep-dive-remaining-audit-3.md)
- [feature-restluecke-audit-1.md](feature-restluecke-audit-1.md)

## Umfang

- Gesamtbestand konsolidierter Audit: TASK-001 bis TASK-212
- Restluecken-Umsetzung: TASK-001 bis TASK-020 in [feature-restluecke-audit-1.md](feature-restluecke-audit-1.md)

## Prioritaetsstatus (konsolidierter Audit)

- P0: 11 gesamt, 11 erledigt
- P1: 55 gesamt, Rest offen: TASK-110, TASK-111 (bewusst nachgelagerte Grossrefactorings)
- P2: 101 gesamt, mehrere bewusst nachgelagert
- P3: 56 gesamt, mehrere bewusst nachgelagert

## Task-Register (vollstaendig)

- Audit 1: TASK-001 bis TASK-083
- Audit 2: TASK-084 bis TASK-149
- Audit 3: TASK-150 bis TASK-212

Damit sind alle 212 Befunde in einem Dokument eindeutig referenziert und nachvollziehbar.

## P0/P1 - Aktueller Arbeitsfokus

### Bereits gestartet (dieser Schritt)

- [x] REST-P0/P1: Tool-Hash fail-closed begonnen und implementiert
  - Datei: [src/RomCleanup.Infrastructure/Tools/ToolRunnerAdapter.cs](../src/RomCleanup.Infrastructure/Tools/ToolRunnerAdapter.cs)
  - Tests begonnen: [src/RomCleanup.Tests/ToolRunnerAdapterTests.cs](../src/RomCleanup.Tests/ToolRunnerAdapterTests.cs)

- [x] REST-P1: DAT sync-over-async entfernt und auf async umgestellt
  - Datei: [src/RomCleanup.Infrastructure/Dat/DatSourceService.cs](../src/RomCleanup.Infrastructure/Dat/DatSourceService.cs)
  - Tests aktualisiert: [src/RomCleanup.Tests/DatSourceServiceTests.cs](../src/RomCleanup.Tests/DatSourceServiceTests.cs)

- [x] REST-P1: Quarantine-Restore Root-Allowlist gehaertet (mandatory statt optional)
  - Datei: [src/RomCleanup.Infrastructure/Quarantine/QuarantineService.cs](../src/RomCleanup.Infrastructure/Quarantine/QuarantineService.cs)
  - Tests: [src/RomCleanup.Tests/QuarantineServiceTests.cs](../src/RomCleanup.Tests/QuarantineServiceTests.cs) (Restore_NoAllowedRoots_ReturnsError)

- [x] REST-P1: API-Integrationstests erweitert (8 neue: InvalidJSON, EmptyRoots, InvalidMode, DriveRoot, RunNotFound, ConcurrentRun, NonexistentRoot, VersionHeader)
  - Datei: [src/RomCleanup.Tests/ApiIntegrationTests.cs](../src/RomCleanup.Tests/ApiIntegrationTests.cs)

- [x] Audit-P1: TASK-102 Feature-Button AutomationProperties.Name (80+ Buttons ergaenzt)
  - Datei: [src/RomCleanup.UI.Wpf/MainWindow.xaml](../src/RomCleanup.UI.Wpf/MainWindow.xaml)

- [x] Bugfix: SanitizeCsvField "-2+3" CSV-Injection nicht blockiert
  - Datei: [src/RomCleanup.Infrastructure/Audit/AuditSigningService.cs](../src/RomCleanup.Infrastructure/Audit/AuditSigningService.cs)
  - IsPlainNegativeNumber() prueft ob der gesamte String eine echte negative Zahl ist

- [x] Bugfix: InsightsEngine GameKey mit Extension statt ohne → falsches Grouping
  - Datei: [src/RomCleanup.Infrastructure/Analytics/InsightsEngine.cs](../src/RomCleanup.Infrastructure/Analytics/InsightsEngine.cs)
  - Path.GetFileNameWithoutExtension statt Path.GetFileName

### Naechste P0/P1-Pakete

- [x] REST-P1: API-Integrationstests fuer Auth/CORS/RateLimit/SSE
- [x] REST-P1: Quarantine-Restore Root-Allowlist haerten
- [x] Audit-P1: TASK-102 (AutomationProperties Name)
- [ ] Audit-P1: TASK-110 (MainWindow Refactor) — bewusst nachgelagert
- [ ] Audit-P1: TASK-111 (Click to Command) — bewusst nachgelagert

### Erledigte P2/P3-Aufgaben (Session 6)

- [x] TASK-206: ConversionPipelineDef.Status Mutation → separate ConversionPipelineResult-Klasse
  - Dateien: ServiceModels.cs, ConversionPipeline.cs, ConversionPipelineTests.cs
- [x] TASK-207: PipelineStep.Status Mutation → PipelineStepOutcome als separates Result-Objekt
  - Dateien: PipelineModels.cs, PipelineEngine.cs, PipelineEngineTests.cs (12 Tests, 2 neue)
- [x] TASK-210: RomCandidate.Type → ConsoleKey umbenannt (7+ Dateien)
  - Dateien: RomCandidate.cs, RunOrchestrator.cs, InsightsEngine.cs, CLI/Program.cs, MainWindow.xaml.cs, InsightsEngineTests.cs, DatSourceServiceTests.cs

### Fixes (Session 7)

- [x] Race-Condition in RunCoreAsync behoben: Dispatcher.InvokeAsync fire-and-forget im zweiten Task.Run → svcResult direkt via await zurückgeben, alle UI-Updates synchron auf dem UI-Thread nach await
  - Bug: _lastRunResult/PopulateErrorSummary konnte null sein, weil Dispatcher.InvokeAsync nicht abgewartet wurde
  - Datei: MainWindow.xaml.cs (RunCoreAsync)
- [x] String-Interpolation-Bug: OnParallelHashing zeigte literal "{cores}" statt die Zahl → `$` prefix ergänzt
  - Datei: MainWindow.xaml.cs (OnParallelHashing)
- [x] RF-008 abgehakt: ProfileService.cs existiert bereits (Delete, Import, Export, LoadSavedConfigFlat)
  - Datei: gui-ux-deep-audit.md

### Fixes (Session 8)

- [x] GDI-Ressourcen-Leak: TrayService.Toggle() erstellte Bitmap ohne Dispose → `using`-Block ergänzt
  - Datei: Services/TrayService.cs (Toggle)
- [x] Prozess-Handle-Leak: OnMobileWebUI disposte alten _apiProcess nicht vor Neuzuweisung → `.Dispose()` ergänzt
  - Datei: MainWindow.xaml.cs (OnMobileWebUI)

## Abnahme-Kriterien

- Jede P0/P1-Aenderung hat mindestens einen Negativtest und einen Regressionstest.
- Keine Placebo-Assertion fuer Security-kritische Themen.
- Alle Aenderungen sind im Master-Index und in der Quell-Auditdatei rueckverfolgbar.
