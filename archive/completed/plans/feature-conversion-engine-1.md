---
goal: "Conversion Engine – Graphbasierte, konfigurationsgesteuerte Format-Konvertierung für 65 Systeme, 76 Pfade, 5 Tools"
version: "1.0"
date_created: 2026-03-21
last_updated: 2026-03-21
owner: "Romulus Team"
status: "In Progress"
tags:
  - feature
  - architecture
  - conversion
  - refactoring
---

# Conversion Implementation Plan – Romulus

![Status: In Progress](https://img.shields.io/badge/status-In%20Progress-yellow)

## Aktueller Status (nur offene Punkte sichtbar)

Stand: 2026-03-21

### Bereits umgesetzt

- Phase 1 (P1) vollständig: TASK-001 bis TASK-009
- Phase 2 (P2) vollständig: TASK-010 bis TASK-019
- Phase 3 weitgehend: TASK-020, TASK-021, TASK-022, TASK-023, TASK-024, TASK-025
- Phase 4 teilweise:
  - umgesetzt: TASK-026, TASK-027, TASK-028, TASK-029, TASK-030, TASK-031, TASK-032, TASK-033, TASK-034, TASK-035, TASK-036, TASK-037
  - zusätzlich gehärtet: Zip-Slip-Regression, strikte JSON-Property-Validierung, Tool-Constraint-Enforcement (ExpectedHash/MinVersion)
- Phase 5 teilweise:
  - umgesetzt: TASK-040, TASK-041, TASK-042, TASK-043, TASK-044, TASK-045, TASK-046, TASK-047, TASK-048
  - bewusst verworfen: TASK-038, TASK-039 (inline-Integration in bestehende Pipeline-Phasen ist funktional äquivalent)

### Wirklich offene Punkte (priorisiert)

1. Phase 7+ (Erweiterte Features)

### Aktuelle Test-Baseline

- `dotnet test src/RomCleanup.Tests/RomCleanup.Tests.csproj --nologo`
- Ergebnis: 4196 passed, 0 failed

### Umsetzungsdetails Block 2

- TASK-035: ✅ umgesetzt – 13 ConversionExecutor-Tests (SingleStep, MultiStep, VerifyFail, Cancel, OutputExists, InvokerNotFound, OnStepComplete, PlanBlocked, SourcePreserved)
- TASK-038/039: **bewusst verworfen** – Separate ConversionPlanningPhase/ConversionExecutionPhase nicht nötig, da WinnerConversionPipelinePhase und ConvertOnlyPipelinePhase bereits FormatConverterAdapter.ConvertForConsole delegieren, das intern Planner+Executor orchestriert
- TASK-044: ✅ umgesetzt – ConvertReviewCount + ConvertSavedBytes in RunResult, RunResultBuilder, RunProjection, CLI, API und OpenAPI-Schema eingefügt
- TASK-045: ✅ verifiziert – DI-Konsistenz über RunEnvironmentBuilder.Build() sichergestellt (alle Entry Points nutzen gleichen Pfad)
- TASK-046: ✅ umgesetzt – 10 Facade-Regressionstests (Planner/Executor-Delegation, Legacy-Fallback, BlockedSystem, Registry-Vorrang, SingleStepPlan)
- TASK-047: ✅ umgesetzt – 6 Pipeline-Integrationstests (Builder→RunResult→Projection→CLI→API Durchreichung ConvertReviewCount/ConvertSavedBytes)

### Umsetzungsdetails Block 3 (Phase 6)

- TASK-049: ✅ umgesetzt – CLI DryRun JSON enthält `conversionPlans[]` (Success+Skipped) und `conversionBlocked[]` (Blocked+Error) mit Feldern SourcePath, TargetExtension, Safety, Outcome, Verification/Reason
- TASK-050: ✅ umgesetzt – API `ApiRunResult` enthält `ConversionPlans` + `ConversionBlocked` Arrays, OpenAPI-Schema mit `ApiConversionPlan` und `ApiConversionBlocked` Schemas erweitert
- TASK-051: ✅ umgesetzt – `DashboardProjection` erweitert um `ConvertedDisplay`, `ConvertBlockedDisplay`, `ConvertReviewDisplay`, `ConvertSavedBytesDisplay` mit FormatBytes-Helfer
- TASK-052: ✅ umgesetzt – ManualOnly-Review-Dialog mit eigenem `ConversionReviewViewModel` + `ConversionReviewDialog` und expliziter Confirm/Cancel-Gate vor Move-Run
- TASK-053: ✅ umgesetzt – `ReportSummary` um `ConvertReviewCount`/`ConvertSavedBytes` erweitert, HTML-Report zeigt Convert-Review/Convert-Gespart Karten
- TASK-054: ✅ umgesetzt – 8 ConversionReportParityTests (RunProjection, CLI JSON, API, GUI Dashboard, Reports, OpenAPI Schema, Null/Empty-Cases)
- TASK-055: ✅ validiert – 4196 Tests, 0 Fehler

### Architekturänderungen Block 3

- **Pipeline-Phasen**: `WinnerConversionPipelinePhase` und `ConvertOnlyPipelinePhase` sammeln jetzt per-file `ConversionResult`-Objekte in einer Liste und geben sie über erweiterte Phase-Output-Records zurück
- **Orchestrator**: Neue `ApplyConversionReport()`-Methode baut `ConversionReport` aus gesammelten Ergebnissen, berechnet `ConvertReviewCount` (Safety=Risky) und `ConvertSavedBytes` (source-target size delta)
- **RunResult/RunResultBuilder**: Neues `ConversionReport?`-Feld für per-file Ergebnisse
- **Datenfluss**: Pipeline → Phase Output (mit Results) → Orchestrator (ApplyConversionReport) → RunResult → RunProjection → CLI/API/GUI/Report

### Entscheidung für den nächsten Schritt

- Phase 5 ist bis auf die bewusst verworfenen TASKs abgeschlossen
- Nächster Schritt: Phase 6 (CLI/API/GUI/Reports Entry-Point-Anbindung)

Dieser Plan überführt die monolithische Conversion-Implementierung (`FormatConverterAdapter` mit 22 hardcodierten Einträgen) in eine **graphbasierte, konfigurationsgesteuerte Conversion-Engine** gemäss der Zielarchitektur (`docs/architecture/CONVERSION_ENGINE_ARCHITECTURE.md`).

**Referenzdokumente:**
- `docs/architecture/CONVERSION_ENGINE_ARCHITECTURE.md` — Technische Zielarchitektur
- `docs/architecture/CONVERSION_MATRIX.md` — Vollständige Matrix (76 Pfade, 65 Systeme, 5 Plattformfamilien)
- `docs/product/CONVERSION_PRODUCT_MODEL.md` — Fachmodell (Policies, Safety, Integrity, Regeln R-01..R-10)
- `docs/architecture/CONVERSION_DOMAIN_AUDIT.md` — Domänenanalyse (12 Lücken L1-L12, aktiver Bug)

**IST-Zustand:**
- `FormatConverterAdapter` (523 Zeilen) mit `DefaultBestFormats` Dictionary (22 Einträge)
- Tool-Wahl per switch-Statement, keine Policy-Prüfung, kein ConversionPlan, kein Graph
- 43 von 65 Systemen stillschweigend ignoriert
- ARCADE/NEOGEO als ZIP-Target eingetragen (Bug)
- Keine mehrstufigen Ketten (CSO→ISO→CHD fehlt)
- Verification nur einmal am Ende, nicht per Step

**SOLL-Zustand:**
- `conversion-registry.json` als Single Source of Truth für alle Conversion-Capabilities
- `consoles.json` mit explizitem `conversionPolicy`-Feld pro System
- ConversionGraph mit Dijkstra-Pathfinding für optimale Pfade
- ConversionPlan als Preview-fähiges Zwischenprodukt
- Schritt-für-Schritt-Execution mit Per-Step-Verification
- Dedizierte ToolInvoker statt switch-Statement
- `IFormatConverter` bleibt als rückwärtskompatible Facade

---

## 1. Executive Plan

### Phasenübersicht

| Phase | Scope | Layer | Risiko | Kompilier-Impact |
|-------|-------|-------|--------|------------------|
| **P1** | Contracts: Enums + Records + Ports | Contracts | Null | Additiv, kein Breaking Change |
| **P2** | Core: Graph + Planner + Evaluator | Core | Niedrig | Additiv, kein Breaking Change |
| **P3** | Data: conversion-registry.json + consoles.json-Erweiterung | Data | Mittel | Schema-Erweiterung mit Defaults |
| **P4** | Infrastructure: Registry-Loader + Tool-Invoker + Executor | Infrastructure | Mittel | Internes Refactoring |
| **P5** | Integration: Pipeline-Phasen + FormatConverterAdapter-Facade + Bug-Fix | Infrastructure | Hoch | Verhaltensänderung (Bug-Fix) |
| **P6** | Entry Points: CLI/GUI/API-Anbindung + Reports | Entry Points | Mittel | Additive Erweiterung |

### Dependency-Graph

```
P1 (Contracts) ─┬─→ P2 (Core) ─┬─→ P4 (Infrastructure) ─→ P5 (Integration) ─→ P6 (Entry Points)
                 │               │
                 └─→ P3 (Data) ──┘
```

P1 ist Voraussetzung für alles. P2 und P3 können parallel starten. P4 braucht P2+P3. P5 braucht P4. P6 braucht P5.

---

## 2. Requirements & Constraints

### Funktionale Anforderungen

- **REQ-001**: Alle 65 Systeme in `consoles.json` müssen eine explizite `ConversionPolicy` haben (Auto/ArchiveOnly/ManualOnly/None). Kein System darf ohne Policy sein.
- **REQ-002**: Alle 76 Conversion-Pfade aus der CONVERSION_MATRIX müssen als `ConversionCapability` in `conversion-registry.json` deklariert sein.
- **REQ-003**: ConversionPlan muss VOR der Ausführung berechnet und im DryRun/Preview angezeigt werden können.
- **REQ-004**: Mehrstufige Ketten (CSO→ISO→CHD, PBP→CUE→CHD) müssen als Multi-Step-ConversionPlan modelliert werden.
- **REQ-005**: Jeder ConversionStep muss einzeln verifiziert werden (Verify-or-Die).
- **REQ-006**: SourceIntegrity (Lossless/Lossy/Unknown) muss pro Datei klassifiziert werden.
- **REQ-007**: ConversionSafety (Safe/Acceptable/Risky/Blocked) muss pro Plan berechnet werden.
- **REQ-008**: ManualOnly-Conversions dürfen nur mit expliziter Nutzerbestätigung ausgeführt werden.
- **REQ-009**: ARCADE/NEOGEO dürfen NIEMALS konvertiert werden (Set-Protection, hardcoded).
- **REQ-010**: Preview/Execute/Report-Parität muss erhalten bleiben.
- **REQ-011**: CLI/API/GUI müssen identische ConversionPlans und ConversionReports erzeugen.

### Sicherheitsanforderungen

- **SEC-001**: Zip-Slip-Schutz bei Archiv-Extraktion innerhalb ConversionExecutor (Root-validierte Pfadauflösung).
- **SEC-002**: Tool-Hash-Verifizierung (SHA256) vor jeder Tool-Invocation, existierende Logik in ToolRunnerAdapter bleibt.
- **SEC-003**: Argument-Quoting bei Tool-Invocation (keine Shell-Injection), existierende ArgumentList-Nutzung bleibt.
- **SEC-004**: Intermediate-Dateien in deterministische Temp-Pfade mit Guid, Cleanup im finally-Block.
- **SEC-005**: Conversion-Registry-JSON muss Schema-validiert werden beim Laden (keine fremden Felder, keine fehlenden Pflichtfelder).

### Constraints

- **CON-001**: Alle 5200+ bestehenden Tests müssen nach jeder Phase grün sein. Kein roter CI-State zwischen Phasen.
- **CON-002**: `IFormatConverter` bleibt als Public-Interface erhalten. Bestehende Aufrufer (WinnerConversionPipelinePhase, ConvertOnlyPipelinePhase, GUI, CLI) müssen weiter funktionieren.
- **CON-003**: Dependency-Richtung `Entry Points → Infrastructure → Core → Contracts` darf nicht verletzt werden.
- **CON-004**: Keine neuen NuGet-Dependencies. Dijkstra ist trivial genug für eigene Implementierung (<100 Zeilen).
- **CON-005**: .NET 10, C# LangVersion 14, `net10.0` / `net10.0-windows` Targets.
- **CON-006**: consoles.json-Schema-Erweiterung muss rückwärtskompatibel sein (optionale Felder mit Defaults).

### Patterns

- **PAT-001**: Alle neuen Typen sind `sealed record` oder `enum` (immutable).
- **PAT-002**: Core-Services sind pure (kein I/O, kein Dateisystem, keine Tool-Aufrufe).
- **PAT-003**: Infrastructure-Services per Constructor Injection.
- **PAT-004**: Tool-Invoker implementieren `IToolInvoker` mit `CanHandle`, `Invoke`, `Verify`.
- **PAT-005**: Pipeline-Phasen folgen dem bestehenden `IPipelinePhase<TInput, TOutput>` Pattern.

---

## 3. Implementation Steps

### Phase 1 – Contracts: Enums, Records, Ports

- GOAL-001: Alle Domänentypen der Conversion-Engine als immutable Contracts definieren. Kein bestehendes Verhalten wird geändert.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-001 | Erstelle `src/RomCleanup.Contracts/Models/ConversionPolicyModels.cs` mit `ConversionPolicy` enum (Auto, ArchiveOnly, ManualOnly, None), `SourceIntegrity` enum (Lossless, Lossy, Unknown), `ConversionSafety` enum (Safe, Acceptable, Risky, Blocked), `ConversionCondition` enum (None, FileSizeLessThan700MB, FileSizeGreaterEqual700MB, IsNKitSource, IsWadFile, IsCdiSource, IsEncryptedPbp). | | |
| TASK-002 | Erstelle `src/RomCleanup.Contracts/Models/ConversionGraphModels.cs` mit `ConversionCapability` sealed record (SourceExtension, TargetExtension, Tool, Command, ApplicableConsoles, RequiredSourceIntegrity, ResultIntegrity, Lossless, Cost, Verification, Description, Condition) und `ToolRequirement` sealed record (ToolName, ExpectedHash, MinVersion). | | |
| TASK-003 | Erstelle `src/RomCleanup.Contracts/Models/ConversionPlanModels.cs` mit `ConversionStep` sealed record (Order, InputExtension, OutputExtension, Capability, IsIntermediate, ExpectedOutputPath) und `ConversionPlan` sealed record (SourcePath, ConsoleKey, Policy, SourceIntegrity, Safety, Steps, FinalTargetExtension, SkipReason, IsExecutable, RequiresReview). | | |
| TASK-004 | Erweitere `src/RomCleanup.Contracts/Models/ConversionModels.cs`: `ConversionOutcome` um `Blocked` erweitern. `ConversionResult` um `Plan`, `SourceIntegrity`, `Safety`, `VerificationResult`, `DurationMs` Felder erweitern. Neue Typen `VerificationStatus` enum (Verified, VerifyFailed, VerifyNotAvailable, NotAttempted), `VerificationMethod` enum (ChdmanVerify, RvzMagicByte, SevenZipTest, FileExistenceCheck, None), `ConversionReport` sealed record, `ConversionStepResult` sealed record, `ToolInvocationResult` sealed record. | | |
| TASK-005 | Erstelle `src/RomCleanup.Contracts/Ports/IConversionRegistry.cs` mit Methoden `GetCapabilities()`, `GetPolicy(consoleKey)`, `GetPreferredTarget(consoleKey)`, `GetAlternativeTargets(consoleKey)`. | | |
| TASK-006 | Erstelle `src/RomCleanup.Contracts/Ports/IConversionPlanner.cs` mit Methoden `Plan(sourcePath, consoleKey, sourceExtension)`, `PlanBatch(candidates)`. | | |
| TASK-007 | Erstelle `src/RomCleanup.Contracts/Ports/IConversionExecutor.cs` mit Methode `Execute(plan, onStepComplete, cancellationToken)`. | | |
| TASK-008 | Erstelle `src/RomCleanup.Contracts/Ports/IToolInvoker.cs` (internes Interface) mit Methoden `CanHandle(ConversionCapability)`, `Invoke(sourcePath, targetPath, capability, ct)`, `Verify(targetPath)`. | | |
| TASK-009 | Build-Validierung: `dotnet build src/RomCleanup.sln` muss 0 Errors haben. Alle bestehenden Tests müssen grün sein. | | |

### Phase 2 – Core: Graph, Planner, Evaluator (pure Logik)

- GOAL-002: Reine Domänenlogik ohne I/O implementieren: Conversion-Graph aufbauen, Pathfinding (Dijkstra), SourceIntegrity-Klassifikation, Policy-Evaluation, Plan-Berechnung.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-010 | Erstelle `src/RomCleanup.Core/Conversion/SourceIntegrityClassifier.cs`. Statische Methode `Classify(string extension, string? fileName) → SourceIntegrity`. Mapping: Lossless = {.cue, .bin, .iso, .img, .gdi, .gcm, .wbfs, .gcz, .wia, .wud}; Lossy = {.cso, .pbp, .cdi} + fileName.Contains(".nkit."); Unknown = alles andere. | | |
| TASK-011 | Erstelle `src/RomCleanup.Core/Conversion/ConversionPolicyEvaluator.cs`. Methoden: `EvaluateSafety(policy, integrity, pathCapabilities, allToolsAvailable) → ConversionSafety` gemäss Safety-Matrix in Architektur-Dokument Abschnitt 6.1. `GetEffectivePolicy(consoleKey, configuredPolicy) → ConversionPolicy` mit hardcodiertem Set-Schutz für ARCADE/NEOGEO. | | |
| TASK-012 | Erstelle `src/RomCleanup.Core/Conversion/ConversionConditionEvaluator.cs`. Methode `Evaluate(ConversionCondition condition, string sourcePath) → bool`. Implementiert: FileSizeLessThan700MB (FileInfo.Length < 700*1024*1024), FileSizeGreaterEqual700MB, IsNKitSource (fileName.Contains(".nkit.")), IsWadFile (.wad ext), IsCdiSource (.cdi ext), IsEncryptedPbp (prüft PBP header magic + encryption flag). Merke: Diese Klasse braucht Dateizugriff via `Func<string, long>` für Dateigrösse statt direktem I/O. | | |
| TASK-013 | Erstelle `src/RomCleanup.Core/Conversion/ConversionGraph.cs`. Klasse baut aus `IReadOnlyList<ConversionCapability>` einen gewichteten gerichteten Graphen auf. Methode `FindPath(sourceExtension, targetExtension, consoleKey, Func<ConversionCondition, bool> conditionEvaluator) → IReadOnlyList<ConversionCapability>?`. Dijkstra-Implementierung mit Kostenfunktion aus Architektur Abschnitt 3.3. Max Pfadlänge = 5 (Zyklen-Schutz). Visited-Set zur Vermeidung doppelter Besuche. | | |
| TASK-014 | Erstelle `src/RomCleanup.Core/Conversion/ConversionPlanner.cs` implementiert `IConversionPlanner`. Constructor: `(IConversionRegistry registry, Func<string, string?> toolFinder, Func<string, long> fileSizeProvider)`. Methode `Plan()` implementiert Pathfinding-Algorithmus aus Architektur Abschnitt 3.2 (Policy-Check → PreferredTarget → Already-Target-Check → Integrity → Graph-Pathfinding → Safety-Evaluation → Plan-Konstruktion). `PlanBatch()` ruft `Plan()` pro Kandidat. | | |
| TASK-015 | Unit-Tests für SourceIntegrityClassifier: alle Extensions getestet, .nkit.iso = Lossy, unbekannte Extension = Unknown. Min. 15 Testfälle. Datei: `src/RomCleanup.Tests/Conversion/SourceIntegrityClassifierTests.cs`. | | |
| TASK-016 | Unit-Tests für ConversionPolicyEvaluator: alle 9 Kombinationen aus Safety-Matrix testen. ARCADE/NEOGEO hardcoded → None. Min. 12 Testfälle. Datei: `src/RomCleanup.Tests/Conversion/ConversionPolicyEvaluatorTests.cs`. | | |
| TASK-017 | Unit-Tests für ConversionGraph: Dijkstra findet kürzesten Pfad. Kein Pfad wenn disconnected. Max Pfadlänge 5 wird respektiert. Conditions filtern Kanten. ConsoleKey-Filter funktioniert. Min. 12 Testfälle. Datei: `src/RomCleanup.Tests/Conversion/ConversionGraphTests.cs`. | | |
| TASK-018 | Unit-Tests für ConversionPlanner: Policy=None → Blocked. Already-target → Skip. Lossless Direct → Safe. Lossy Source → Acceptable. Multi-Step CSO→ISO→CHD. Tool-nicht-verfügbar → Skip. Min. 15 Testfälle. Datei: `src/RomCleanup.Tests/Conversion/ConversionPlannerTests.cs`. | | |
| TASK-019 | Build-Validierung: `dotnet build src/RomCleanup.sln` 0 Errors + alle neuen und bestehenden Tests grün. | | |

### Phase 3 – Data: conversion-registry.json + consoles.json-Erweiterung

- GOAL-003: Single Source of Truth für alle Conversion-Capabilities und ConversionPolicies in JSON-Dateien etablieren. consoles.json bekommt `conversionPolicy` und `preferredConversionTarget` Felder.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-020 | Erstelle `data/schemas/conversion-registry.schema.json` mit JSON Schema für conversion-registry.json: `schemaVersion` (string, erforderlich), `capabilities` (Array von ConversionCapability-Objekten mit allen Pflichtfeldern aus ConversionGraphModels.cs). | | |
| TASK-021 | Erstelle `data/conversion-registry.json` mit allen Capabilities aus der CONVERSION_MATRIX. Pro Plattformfamilie: **CD-Systeme** (12 Systeme): cue→chd, iso→chd, gdi→chd (createcd). **DVD-Systeme**: iso→chd (createdvd) für PS2 DVD + iso→chd (createcd) für PS2 CD mit Condition. PSP: iso→chd (createcd), cso→iso (ciso), pbp→cue (psxtract). **GC/Wii**: iso→rvz, gcm→rvz, wbfs→rvz, gcz→rvz, wia→rvz (dolphintool). **Cartridge/Archive**: *→zip (7z), 7z→zip (7z extract+zip), rar→zip (7z extract+zip). **Gesamt: ~25-30 Capability-Einträge.** | | |
| TASK-022 | Erweitere `data/schemas/consoles.schema.json` (oder erstelle falls fehlend): neues optionales Feld `conversionPolicy` (enum: "Auto", "ArchiveOnly", "ManualOnly", "None", default "None") und `preferredConversionTarget` (string, nullable, z.B. ".chd", ".rvz", ".zip"). `alternativeTargets` (string array, optional). | | |
| TASK-023 | Erweitere `data/consoles.json` für alle 65 Systeme mit `conversionPolicy` und `preferredConversionTarget` gemäss CONVERSION_MATRIX Anhang A. Auto(14): PS1→.chd, PS2→.chd, PSP→.chd, SAT→.chd, DC→.chd, SCD→.chd, PCECD→.chd, NEOCD→.chd, 3DO→.chd, JAGCD→.chd, PCFX→.chd, CD32→.chd, GC→.rvz, WII→.rvz. ArchiveOnly(36): Alle Cartridge+Computer→.zip. ManualOnly(5): XBOX→.chd, X360→.chd, WIIU→null, PC98→.zip, X68K→.zip. None(7): ARCADE, NEOGEO, SWITCH, PS3, 3DS, VITA, DOS. CDI_SYS→.chd/Auto, FMTOWNS→.chd/Auto. | | |
| TASK-024 | Validierungstest: Erstelle `src/RomCleanup.Tests/Conversion/ConversionRegistrySchemaTests.cs`. Testet: Jeder ConsoleKey in capabilities muss in consoles.json existieren. Jede ConversionPolicy in consoles.json muss gültig sein. Kein ARCADE/NEOGEO in capabilities. Alle Pflichtfelder vorhanden. Min. 8 Testfälle. | | |
| TASK-025 | Build-Validierung: `dotnet build src/RomCleanup.sln` 0 Errors + alle Tests grün. | | |

### Phase 4 – Infrastructure: Registry-Loader, Tool-Invoker, Executor

- GOAL-004: JSON-basierte Konfiguration laden, Tool-Logik aus FormatConverterAdapter in dedizierte Invoker extrahieren, Schritt-für-Schritt-Executor implementieren.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-026 | Erstelle `src/RomCleanup.Infrastructure/Conversion/ConversionRegistryLoader.cs` implementiert `IConversionRegistry`. Constructor: `(string conversionRegistryPath, string consolesJsonPath)`. Lädt `conversion-registry.json` → `IReadOnlyList<ConversionCapability>`. Lädt `consoles.json` → `ConversionPolicy` + `preferredConversionTarget` pro ConsoleKey. Schema-Validierung beim Laden: fehlende Pflichtfelder → `InvalidOperationException` mit konkreter Fehlermeldung. Cross-Validierung: ConsoleKeys in capabilities müssen in consoles.json existieren. | | |
| TASK-027 | Erstelle `src/RomCleanup.Infrastructure/Conversion/ToolInvokers/ChdmanInvoker.cs` implementiert `IToolInvoker`. `CanHandle`: target = ".chd". `Invoke`: chdman {command} -i {source} -o {target}. Archiv-Extraktion: wenn source ist .zip/.7z → in Temp extrahieren, cue/bin finden, Zip-Slip-Guard. `Verify`: chdman verify -i {target}, Return VerificationStatus. Extrahiert aus FormatConverterAdapter.ConvertWithChdman (Zeilen ~200-350). | | |
| TASK-028 | Erstelle `src/RomCleanup.Infrastructure/Conversion/ToolInvokers/DolphinToolInvoker.cs` implementiert `IToolInvoker`. `CanHandle`: target = ".rvz". `Invoke`: dolphintool convert -i {source} -o {target} -f rvz -c zstd -l 5 -b 131072. `Verify`: Magic-Byte-Check (RVZ\x01, Size > 4B). Extrahiert aus FormatConverterAdapter.ConvertWithDolphinTool. | | |
| TASK-029 | Erstelle `src/RomCleanup.Infrastructure/Conversion/ToolInvokers/SevenZipInvoker.cs` implementiert `IToolInvoker`. `CanHandle`: target = ".zip". `Invoke`: 7z a -tzip -y {target} {source}. `Verify`: 7z t -y {target}. Auch für Extraktion (7z → Loose). Extrahiert aus FormatConverterAdapter.ConvertWithSevenZip. | | |
| TASK-030 | Erstelle `src/RomCleanup.Infrastructure/Conversion/ToolInvokers/PsxtractInvoker.cs` implementiert `IToolInvoker`. `CanHandle`: tool = "psxtract". `Invoke`: psxtract pbp2chd. `Verify`: Nur FileExistence (nachgelagert chdman verify). Encrypted-PBP-Erkennung: PBP Header Byte prüfen → Error wenn encrypted. Extrahiert aus FormatConverterAdapter.ConvertWithPsxtract. | | |
| TASK-031 | Erstelle `src/RomCleanup.Infrastructure/Conversion/ConversionExecutor.cs` implementiert `IConversionExecutor`. Constructor: `(IReadOnlyList<IToolInvoker> invokers, IToolRunner toolRunner, IFileSystem? fileSystem)`. Methode `Execute(plan, onStepComplete, ct)`: Iteriert plan.Steps. Pro Step: Invoker resolven via `CanHandle`. Input/Output-Pfad berechnen (Intermediate → Temp-Pfad mit Guid). Tool ausführen. Per-Step-Verify (3-Stufen: Existenz → Format-Verify → Plausibilität). Bei Fehler: Partial-Output löschen, Error-Result. Am Ende: Alle Intermediates löschen bei Erfolg. `onStepComplete` Callback pro Step. | | |
| TASK-032 | Unit-Tests für ChdmanInvoker: `CanHandle` für .chd true, .rvz false. Invoke ruft IToolRunner korrekt auf. Verify parst chdman verify Exit-Code. Zip-Slip in Archiv-Extraktion wird blockiert. Min. 8 Testfälle. Datei: `src/RomCleanup.Tests/Conversion/ChdmanInvokerTests.cs`. | | |
| TASK-033 | Unit-Tests für DolphinToolInvoker: CanHandle, Invoke, Verify Magic-Byte. Min. 6 Testfälle. Datei: `src/RomCleanup.Tests/Conversion/DolphinToolInvokerTests.cs`. | | |
| TASK-034 | Unit-Tests für SevenZipInvoker: CanHandle, Invoke, Verify mit 7z t. Min. 6 Testfälle. Datei: `src/RomCleanup.Tests/Conversion/SevenZipInvokerTests.cs`. | | |
| TASK-035 | Unit-Tests für ConversionExecutor: Single-Step-Success. Single-Step mit Verify-Fail → Cleanup. Multi-Step CSO→ISO→CHD → Intermediate-Cleanup. Cancellation. Min. 10 Testfälle. Datei: `src/RomCleanup.Tests/Conversion/ConversionExecutorTests.cs`. | | |
| TASK-036 | Unit-Tests für ConversionRegistryLoader: Gültige JSON → korrekte Capabilities. Fehlende Pflichtfelder → Exception. Cross-Validation ConsoleKey. Min. 8 Testfälle. Datei: `src/RomCleanup.Tests/Conversion/ConversionRegistryLoaderTests.cs`. | | |
| TASK-037 | Build-Validierung: `dotnet build src/RomCleanup.sln` 0 Errors + alle Tests grün. | | |

### Phase 5 – Integration: Pipeline-Phasen, Facade, Bug-Fix

- GOAL-005: Neue Conversion-Engine in bestehende Pipeline integrieren. FormatConverterAdapter wird zur Facade. ARCADE/NEOGEO-Bug fixen. Bestehende 5200+ Tests plus neue Tests müssen grün sein.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-038 | Erstelle `src/RomCleanup.Infrastructure/Orchestration/ConversionPlanningPhase.cs` implementiert `IPipelinePhase<ConversionPlanningInput, ConversionPlanningOutput>`. Input: Candidates + IConversionPlanner. Output: Executable/RequiresReview/Blocked Listen. Gemäss Architektur Abschnitt 7.3. | | |
| TASK-039 | Erstelle `src/RomCleanup.Infrastructure/Orchestration/ConversionExecutionPhase.cs` implementiert `IPipelinePhase<ConversionExecutionInput, ConversionExecutionOutput>`. Input: Plans + IConversionExecutor + RunOptions. Output: ConversionReport. Iteriert Plans, ruft Executor auf, auditiert via PipelinePhaseHelpers, Progress alle 25 Items. Gemäss Architektur Abschnitt 7.3. | | |
| TASK-040 | Refactore `src/RomCleanup.Infrastructure/Conversion/FormatConverterAdapter.cs` zur Facade: Constructor nimmt `IConversionPlanner` + `IConversionExecutor`. `GetTargetFormat()` delegiert an Planner. `Convert()` erstellt Plan + führt via Executor aus. `Verify()` delegiert an passenden Invoker. `DefaultBestFormats` wird intern durch Registry ersetzt. Bestehende Signatur `IFormatConverter` bleibt unverändert. | | |
| TASK-041 | **BUG-FIX:** Entferne ARCADE und NEOGEO aus `DefaultBestFormats` in `FormatConverterAdapter.cs` (oder äquivalent: ConversionPolicy=None in consoles.json verhindert Konvertierung). Sicherstellen, dass Set-Protection in ConversionPolicyEvaluator hardcoded ist. | | |
| TASK-042 | Update `src/RomCleanup.Infrastructure/Orchestration/WinnerConversionPipelinePhase.cs`: Intern ConversionPlanningPhase + ConversionExecutionPhase nutzen ODER bestehende Logik beibehalten und FormatConverterAdapter-Facade delegiert transparent. Entscheidung: Facade-Ansatz (minimal-invasiv) bevorzugt. | | |
| TASK-043 | Update `src/RomCleanup.Infrastructure/Orchestration/ConvertOnlyPipelinePhase.cs`: Gleiche Anpassung wie TASK-042 — Facade-Ansatz, da ConvertOnly dieselbe IFormatConverter-Schnittstelle nutzt. | | |
| TASK-044 | Erweitere `src/RomCleanup.Infrastructure/Orchestration/RunProjection.cs` und `RunProjectionFactory.cs`: Neue Felder `ConvertBlockedCount`, `ConvertReviewCount`, `ConvertSavedBytes`. Factory berechnet aus ConversionReport. | | |
| TASK-045 | DI-Registration: In `src/RomCleanup.CLI/Program.cs`, `src/RomCleanup.Api/Program.cs`, `src/RomCleanup.UI.Wpf/App.xaml.cs` (oder ServiceRegistration): `IConversionRegistry → ConversionRegistryLoader`, `IConversionPlanner → ConversionPlanner`, `IConversionExecutor → ConversionExecutor`, `IToolInvoker → [ChdmanInvoker, DolphinToolInvoker, SevenZipInvoker, PsxtractInvoker]`. | | |
| TASK-046 | Regressionstests: Alle bestehenden FormatConverterAdapter-Tests müssen mit refactored Facade identische Ergebnisse liefern. Neue Tests: ARCADE-Candidate → Blocked. NEOGEO-Candidate → Blocked. Datei: `src/RomCleanup.Tests/Conversion/ConversionFacadeRegressionTests.cs`. Min. 10 Testfälle. | | |
| TASK-047 | Pipeline-Integrationstests: Full-Pipeline DryRun mit Conversion zeigt ConversionPlans. Full-Pipeline Execute mit Mock-Tools konvertiert korrekt. ConversionReport-Zahlen stimmen mit RunProjection überein. Min. 8 Testfälle. Datei: `src/RomCleanup.Tests/Conversion/ConversionPipelineIntegrationTests.cs`. | | |
| TASK-048 | Build-Validierung: `dotnet build src/RomCleanup.sln` 0 Errors + `dotnet test src/RomCleanup.Tests/RomCleanup.Tests.csproj --nologo` ALLE Tests grün (bestehende 5200+ plus neue). | | |

### Phase 6 – Entry Points: CLI, GUI, API + Reports

- GOAL-006: CLI, GUI und API nutzen die neue Conversion-Transparenz (ConversionPlans, Safety-Badges, Block-Kommunikation, ConversionReport).

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-049 | CLI: `CliOutputWriter` erweitern. DryRun-JSON enthält `conversionPlans[]` mit Feldern: sourcePath, consoleKey, targetExtension, safety, steps[], skipReason. Blocked-Plans in separatem `conversionBlocked[]` Array. Datei: `src/RomCleanup.CLI/CliOutputWriter.cs`. | ✅ | 2025-07 |
| TASK-050 | API: `ApiRunResultMapper` erweitern. `ApiRunResult` bekommt `ConvertBlockedCount`, `ConvertReviewCount`, `ConvertSavedBytes`. Neuer optionaler Endpoint `GET /runs/{id}/conversion-plans` liefert ConversionPlans (nur im DryRun-Modus). Datei: `src/RomCleanup.Api/ApiRunResultMapper.cs`, `src/RomCleanup.Api/Program.cs`. | ✅ | 2025-07 |
| TASK-051 | GUI: `DashboardProjection.From()` erweitern um ConvertBlockedCount, ConvertReviewCount. Dashboard zeigt Conversion-Ergebnis differenziert (Converted / Errors / Blocked / Review). Datei: `src/RomCleanup.UI.Wpf/Models/DashboardProjection.cs`, `src/RomCleanup.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs`. | ✅ | 2025-07 |
| TASK-052 | GUI: ManualOnly-Confirmation-Dialog. Wenn ConversionPlans mit RequiresReview=true existieren → Modal-Dialog mit Liste der betroffenen Dateien + Safety-Reason + Confirm/Cancel. MVVM: eigenes ViewModel + eigene View. Kein Code-Behind. Datei: `src/RomCleanup.UI.Wpf/ViewModels/ConversionReviewViewModel.cs`, `src/RomCleanup.UI.Wpf/Views/ConversionReviewDialog.xaml`. | ✅ | 2025-07 |
| TASK-053 | Reports: HTML-Report bekommt Conversion-Sektion mit Tabelle (Source, Target, Safety-Badge, VerificationStatus, Duration). CSV-Report bekommt Spalten `ConvertedTo`, `ConversionSafety`, `VerificationStatus`. Datei: `src/RomCleanup.Infrastructure/Reporting/HtmlReportBuilder.cs`, `src/RomCleanup.Infrastructure/Reporting/CsvReportBuilder.cs`. HTML-Output muss HTML-escaped sein (SEC). | ✅ | 2025-07 |
| TASK-054 | Parity-Tests: CLI DryRun JSON, API DryRun Response, GUI DashboardProjection müssen identische ConvertedCount/ErrorCount/BlockedCount liefern für identische Inputs. Min. 5 Testfälle. Datei: `src/RomCleanup.Tests/ConversionReportParityTests.cs`. | ✅ | 2025-07 |
| TASK-055 | Build-Validierung: Vollständiger `dotnet test` ALLE Tests grün. 4196 passed, 0 failed. | ✅ | 2025-07 |

---

## 4. Priorisierte Plattformen / Formate

### Implementierungsreihenfolge innerhalb der Conversion-Registry

| Priorität | Plattformfamilie | Systeme | Pfade | Begründung |
|-----------|-----------------|---------|-------|------------|
| **P1-DISC-CD** | CD-basierte Disc-Systeme | PS1, SAT, DC, SCD, PCECD, NEOCD, 3DO, JAGCD, PCFX, CD32, CDI_SYS, FMTOWNS | cue→chd, iso→chd, gdi→chd | Grösster Nutzer-Impact. chdman ist battle-tested. Alle 12 Systeme nutzen identisches createcd. |
| **P1-DISC-DVD** | DVD-basierte Disc-Systeme | PS2 | iso→chd (createdvd + createcd mit CD-Heuristik) | PS2 grösste Einzelsammlung. CD/DVD-Heuristik ist kritisch. |
| **P1-DISC-UMD** | UMD-basiertes System | PSP | iso→chd (createcd) | Zweitgrösste Sammlung. UMD=CD-kompatibel. |
| **P1-GC-WII** | GameCube/Wii | GC, WII | iso→rvz, wbfs→rvz, gcz→rvz, wia→rvz, gcm→rvz | Riesige ISOs (4-8 GB). RVZ spart ~60%. dolphintool ist stabil. |
| **P2-CART** | Cartridge-Systeme (alle) | NES, SNES, N64, GB, GBC, GBA, NDS, MD, SMS, GG, 32X, SG1000, PCE, A26-A78, JAG, LYNX, COLECO, INTV, VB, WS, WSC, NGP, NGPC, POKEMINI, VECTREX, CHANNELF, ODYSSEY2, SUPERVISION | rom→zip, 7z→zip, rar→zip | Trivial, null Risiko, hohe Dateizahlen. Alle identischer 7z-Pfad. |
| **P2-COMPUTER** | Computer-Systeme | AMIGA, C64, A800, ATARIST, ZX, MSX, CPC | disk-images→zip | ZIP-Wrapping, kein Inhaltseingriff. |
| **P3-MULTI-STEP** | Mehrstufige Ketten | PSP (CSO→ISO→CHD, PBP→CUE→CHD) | cso→iso, pbp→cue, dann regulärer Pfad | Braucht ciso + psxtract als Zwischenschritt-Tools. |
| **P3-MANUAL** | ManualOnly-Systeme | XBOX, X360, WIIU, PC98, X68K | iso→chd (ManualOnly), disk→zip (ManualOnly) | Niedriger Impact, erfordern Review-Dialogflow. |
| **P4-LOSSY** | Lossy-Quellen | GC/WII (NKit), DC (CDI) | nkit→rvz, cdi→chd | Risky-Pfade, erfordern Nutzer-Warnung. |
| **NEVER** | Blockierte Systeme | ARCADE, NEOGEO, SWITCH, PS3, 3DS, VITA, DOS | – | Policy=None. Keine Implementation nötig, nur Block-Enforcement. |

---

## 5. Benötigte Tools

| Tool | Version | Lizenz | SHA256-Hash | In tool-hashes.json | Benutzt für | Verfügbarkeit |
|------|---------|--------|-------------|---------------------|-------------|---------------|
| **chdman** | ≥0.262 | BSD-3 (MAME) | Ja | ✅ Bereits eingetragen | CD/DVD/UMD → CHD, verify | Bereits integriert |
| **dolphintool** | ≥5.0-18xxx | GPL-2 (Dolphin) | Ja | ✅ Bereits eingetragen | ISO/WBFS/GCZ/WIA → RVZ | Bereits integriert |
| **7z** (7-Zip) | ≥23.01 | LGPL-2.1 | Ja | ✅ Bereits eingetragen | ROM → ZIP, ZIP/7z/RAR test/extract | Bereits integriert |
| **psxtract** | ≥1.x | Open Source | Ja | ✅ Bereits eingetragen | PBP → CUE/BIN | Bereits integriert |
| **ciso/maxcso** | ≥1.x | MIT/GPL | **Nein** | ❌ **Fehlt** | CSO → ISO (Decompression) | **NEU: Muss in tool-hashes.json eingetragen werden** |

### Aktion für ciso

| Task | Detail |
|------|--------|
| Tool beschaffen | ciso oder maxcso Binary für Windows beschaffen (MIT-lizenziert) |
| Hash registrieren | SHA256-Hash in `data/tool-hashes.json` eintragen |
| ToolRunnerAdapter erweitern | Tool-Discovery-Pfad für ciso hinzufügen |
| Integrationstests | CSO→ISO Zwischenschritt mit echtem Tool testen |

---

## 6. Benötigte Datenstrukturen

### Neue Dateien (erstellt in Phasen 1-4)

| # | Datei | Layer | Beschreibung |
|---|-------|-------|--------------|
| **DS-01** | `src/RomCleanup.Contracts/Models/ConversionPolicyModels.cs` | Contracts | ConversionPolicy, SourceIntegrity, ConversionSafety, ConversionCondition Enums |
| **DS-02** | `src/RomCleanup.Contracts/Models/ConversionGraphModels.cs` | Contracts | ConversionCapability, ToolRequirement Records |
| **DS-03** | `src/RomCleanup.Contracts/Models/ConversionPlanModels.cs` | Contracts | ConversionStep, ConversionPlan Records |
| **DS-04** | `src/RomCleanup.Contracts/Ports/IConversionRegistry.cs` | Contracts | Port-Interface für Registry |
| **DS-05** | `src/RomCleanup.Contracts/Ports/IConversionPlanner.cs` | Contracts | Port-Interface für Planner |
| **DS-06** | `src/RomCleanup.Contracts/Ports/IConversionExecutor.cs` | Contracts | Port-Interface für Executor |
| **DS-07** | `src/RomCleanup.Contracts/Ports/IToolInvoker.cs` | Contracts | Internes Interface für Tool-Dispatch |
| **DS-08** | `src/RomCleanup.Core/Conversion/ConversionGraph.cs` | Core | Graph-Aufbau + Dijkstra |
| **DS-09** | `src/RomCleanup.Core/Conversion/ConversionPlanner.cs` | Core | Plan-Berechnung |
| **DS-10** | `src/RomCleanup.Core/Conversion/SourceIntegrityClassifier.cs` | Core | Extension → Lossless/Lossy/Unknown |
| **DS-11** | `src/RomCleanup.Core/Conversion/ConversionPolicyEvaluator.cs` | Core | Policy → Safety |
| **DS-12** | `src/RomCleanup.Core/Conversion/ConversionConditionEvaluator.cs` | Core | Condition-Enum-Evaluator |
| **DS-13** | `data/conversion-registry.json` | Data | Alle ConversionCapabilities als JSON |
| **DS-14** | `data/schemas/conversion-registry.schema.json` | Data | JSON Schema |
| **DS-15** | `src/RomCleanup.Infrastructure/Conversion/ConversionRegistryLoader.cs` | Infrastructure | JSON → IConversionRegistry |
| **DS-16** | `src/RomCleanup.Infrastructure/Conversion/ConversionExecutor.cs` | Infrastructure | Step-by-Step Execution |
| **DS-17** | `src/RomCleanup.Infrastructure/Conversion/ToolInvokers/ChdmanInvoker.cs` | Infrastructure | chdman-Wrapper |
| **DS-18** | `src/RomCleanup.Infrastructure/Conversion/ToolInvokers/DolphinToolInvoker.cs` | Infrastructure | dolphintool-Wrapper |
| **DS-19** | `src/RomCleanup.Infrastructure/Conversion/ToolInvokers/SevenZipInvoker.cs` | Infrastructure | 7z-Wrapper |
| **DS-20** | `src/RomCleanup.Infrastructure/Conversion/ToolInvokers/PsxtractInvoker.cs` | Infrastructure | psxtract-Wrapper |
| **DS-21** | `src/RomCleanup.Infrastructure/Orchestration/ConversionPlanningPhase.cs` | Infrastructure | Pipeline-Phase Planning |
| **DS-22** | `src/RomCleanup.Infrastructure/Orchestration/ConversionExecutionPhase.cs` | Infrastructure | Pipeline-Phase Execution |

### Geänderte Dateien

| # | Datei | Änderung |
|---|-------|---------|
| **MOD-01** | `src/RomCleanup.Contracts/Models/ConversionModels.cs` | ConversionOutcome um Blocked erweitern. ConversionResult um Plan, Safety, VerificationResult, DurationMs erweitern. VerificationStatus, VerificationMethod, ConversionReport, ConversionStepResult, ToolInvocationResult hinzufügen. |
| **MOD-02** | `src/RomCleanup.Infrastructure/Conversion/FormatConverterAdapter.cs` | Refactoring zur Facade: delegiert an IConversionPlanner + IConversionExecutor. DefaultBestFormats entfernen. ARCADE/NEOGEO-Bug fix. |
| **MOD-03** | `data/consoles.json` | Alle 65 Systeme: `conversionPolicy` + `preferredConversionTarget` + `alternativeTargets` Felder hinzufügen. |
| **MOD-04** | `src/RomCleanup.Infrastructure/Orchestration/RunProjection.cs` | ConvertBlockedCount, ConvertReviewCount, ConvertSavedBytes Felder. |
| **MOD-05** | `src/RomCleanup.Infrastructure/Orchestration/RunProjectionFactory.cs` | Factory berechnet neue Felder aus ConversionReport. |
| **MOD-06** | `src/RomCleanup.CLI/CliOutputWriter.cs` | DryRun-JSON mit conversionPlans[], conversionBlocked[]. |
| **MOD-07** | `src/RomCleanup.Api/ApiRunResultMapper.cs` | ConvertBlockedCount, ConvertReviewCount, ConvertSavedBytes. |
| **MOD-08** | `src/RomCleanup.UI.Wpf/Models/DashboardProjection.cs` | ConvertBlockedCount, ConvertReviewCount. |
| **MOD-09** | `src/RomCleanup.Infrastructure/Reporting/HtmlReportBuilder.cs` | Conversion-Sektion im HTML-Report. |
| **MOD-10** | `src/RomCleanup.Infrastructure/Reporting/CsvReportBuilder.cs` | ConvertedTo, ConversionSafety, VerificationStatus Spalten. |
| **MOD-11** | `data/tool-hashes.json` | ciso/maxcso Hash-Eintrag hinzufügen. |

---

## 7. Teststrategie

### Pflicht-Tests pro Phase

| Phase | Testtyp | Ziel | Min. Testfälle | Datei |
|-------|---------|------|-----------------|-------|
| **P1** | Kompilierung | Alle neuen Typen kompilieren, kein Breaking Change | Build + bestehende Tests grün | – |
| **P2** | Unit | SourceIntegrityClassifier: alle Extensions + .nkit | 15 | `Conversion/SourceIntegrityClassifierTests.cs` |
| **P2** | Unit | ConversionPolicyEvaluator: Safety-Matrix + Set-Protection | 12 | `Conversion/ConversionPolicyEvaluatorTests.cs` |
| **P2** | Unit | ConversionGraph: Dijkstra Pathfinding, Conditions, Max-Depth | 12 | `Conversion/ConversionGraphTests.cs` |
| **P2** | Unit | ConversionPlanner: Policy→Block, Already-Target→Skip, Multi-Step | 15 | `Conversion/ConversionPlannerTests.cs` |
| **P3** | Schema | Conversion-Registry Schema valide, Cross-Validation ConsoleKeys | 8 | `Conversion/ConversionRegistrySchemaTests.cs` |
| **P4** | Unit | ChdmanInvoker: CanHandle, Invoke, Verify, Zip-Slip | 8 | `Conversion/ChdmanInvokerTests.cs` |
| **P4** | Unit | DolphinToolInvoker: CanHandle, Invoke, Magic-Byte Verify | 6 | `Conversion/DolphinToolInvokerTests.cs` |
| **P4** | Unit | SevenZipInvoker: CanHandle, Invoke, 7z-test Verify | 6 | `Conversion/SevenZipInvokerTests.cs` |
| **P4** | Unit | ConversionExecutor: Single/Multi-Step, Verify-Fail Cleanup, Cancel | 10 | `Conversion/ConversionExecutorTests.cs` |
| **P4** | Unit | ConversionRegistryLoader: JSON-Parsing, Validation, Cross-Check | 8 | `Conversion/ConversionRegistryLoaderTests.cs` |
| **P5** | Regression | FormatConverterAdapter-Facade liefert identische Ergebnisse | 10 | `Conversion/ConversionFacadeRegressionTests.cs` |
| **P5** | Integration | Full-Pipeline DryRun + Execute + Report | 8 | `Conversion/ConversionPipelineIntegrationTests.cs` |
| **P6** | Parity | CLI / API / GUI identische Counts | 5 | `Conversion/ConversionParityTests.cs` |

**Gesamt:** ~123 neue Testfälle + 5200 bestehende = ~5323 Tests

### Kritische Invarianten

| ID | Invariante | Test-Methode |
|----|-----------|--------------|
| **INV-01** | ARCADE/NEOGEO → ConversionOutcome.Blocked, NIEMALS Conversion | `[Theory] ConsoleKey=ARCADE → Plan.Safety=Blocked` |
| **INV-02** | Preview zeigt identischen Plan wie Execute ausführt | `DryRun.ConversionPlans.Count == Execute.ConvertedCount + Execute.ErrorCount + Execute.BlockedCount` |
| **INV-03** | ConversionGraph hat keine Zyklen (Visited-Set + Max-Depth=5) | Graph mit Zyklus-Input → findet trotzdem kürzesten Pfad oder gibt null |
| **INV-04** | Intermediate-Dateien werden bei Erfolg gelöscht | `ConversionExecutor Multi-Step → Assert.False(File.Exists(intermediatePath))` |
| **INV-05** | Source-Datei wird bei Verify-Fail NICHT gelöscht | `Verify-Fail → Assert.True(File.Exists(sourcePath))` |
| **INV-06** | Tool-Verfügbarkeit wird in Plan-Phase geprüft, nicht erst bei Execution | `toolFinder returns null → Plan.SkipReason = "tool-not-found:chdman"` |
| **INV-07** | consoles.json hat conversionPolicy für alle 65 Systeme | `Assert.All(consoles, c => Assert.NotNull(c.ConversionPolicy))` |
| **INV-08** | Jeder ConsoleKey in conversion-registry.json existiert in consoles.json | Cross-Validation Test |

### Negative Tests (Edge Cases)

| # | Szenario | Erwartung |
|---|----------|-----------|
| N-1 | Datei ist bereits im Zielformat (.chd → .chd für PS1) | ConversionOutcome.Skipped, SkipReason="already-target" |
| N-2 | ConsoleKey = UNKNOWN | ConversionOutcome.Blocked |
| N-3 | Conversion-Registry JSON ist leer | ConversionRegistryLoader → leere Capabilities, alle Plans = Skip |
| N-4 | Tool-Hash stimmt nicht | ToolRunner → ToolResult.Success=false vor Conversion |
| N-5 | Zieldatei existiert bereits | ConversionOutcome.Skipped |
| N-6 | Source-Datei existiert nicht | ConversionOutcome.Error |
| N-7 | Cancelled CancellationToken | OperationCanceledException, Temp-Cleanup |
| N-8 | Graph hat keinen Pfad Source → Target | ConversionPlan.Steps=leer, SkipReason="no-conversion-path" |
| N-9 | Multi-Step: Step 2 Verify-Fail | Step-1 Intermediate bleibt, Source unverändert, Partial-Output gelöscht |
| N-10 | consoles.json fehlt conversionPolicy für ein System | Default=None (sicher) |

---

## 8. Risiken

### Architekturrisiken

- **RISK-001**: **Graph-Zyklen** – Dijkstra mit Visited-Set + Max-Pfadlänge 5 mitigiert. Ohne diesen Schutz Endlosschleife möglich. *Mitigation: TASK-013 erzwingt Max-Depth.*
- **RISK-002**: **Rückwärtskompatibilitäts-Bruch** – FormatConverterAdapter-Facade muss identische Ergebnisse liefern. Bestehende 5200+ Tests sind die Sicherung. *Mitigation: TASK-046 Regressionstests.*
- **RISK-003**: **Registry-Drift** – conversion-registry.json und Code können divergieren. *Mitigation: TASK-024 Schema-Validierungstests + TASK-036 Loader-Tests.*
- **RISK-004**: **Intermediate-Datei-Leaks** – Temp-ISOs bleiben liegen bei Crash/OOM. *Mitigation: Deterministische Temp-Pfade mit Guid + finally-Block-Cleanup + Startup-Scan für Orphans.*
- **RISK-005**: **RVZ-Verify zu schwach** – Magic-Byte-Check erkennt korrumpierte RVZ nicht. *Mitigation: Akzeptiert als Known-Weakness. Mittelfristig dolphintool convert -f iso → /dev/null als Dry-Verify.*
- **RISK-006**: **PS2 CD/DVD-Fehlklassifikation** – 700MB-Heuristik ist approximativ. ~15% PS2-Spiele sind CD-basiert. *Mitigation: Grössenbasierte Heuristik in Phase 1. Robustere SYSTEM.CNF-Analyse als eigenes Epic.*

### Implementierungsrisiken

- **RISK-007**: **consoles.json-Schema-Erweiterung** – Parser in Core/Infrastructure, SettingsService, GUI müssen neue optionale Felder tolerieren. *Mitigation: Neue Felder sind optional mit Default=None.*
- **RISK-008**: **ciso/maxcso nicht verfügbar** – CSO→ISO→CHD Kette blockiert ohne Tool. *Mitigation: Phase P3-MULTI-STEP als separate Priorität. CSO→CHD direkt nicht möglich → Converted=Skipped wenn kein ciso.*
- **RISK-009**: **FormatConverterAdapter-Refactoring-Umfang** – 523 Zeilen, viele Security-Guards (Zip-Slip etc.). *Mitigation: Guards werden in ToolInvoker verschoben, nicht gelöscht. Facade-Ansatz minimiert Änderungen am Public-Interface.*

### Annahmen

- **ASSUMPTION-001**: Bestehende IToolRunner-Schnittstelle bleibt unverändert. Neue ToolInvoker-Schicht arbeitet ÜBER IToolRunner.
- **ASSUMPTION-002**: consoles.json wird von allen Entry Points (CLI, API, GUI) über denselben Loader gelesen. Kein Parallelzugriff-Problem.
- **ASSUMPTION-003**: Conversion-Funktionalität in Run-Pipeline ist sequenziell (kein paralleles Konvertieren mehrerer Dateien gleichzeitig).
- **ASSUMPTION-004**: Alle 5 bestehenden Tools (chdman, dolphintool, 7z, psxtract, ciso) sind als Windows-Binaries verfügbar und SHA256-hashbar.

---

## 9. Exit-Kriterien

### Phase 1 – Contracts

| # | Kriterium | Prüfmethode |
|---|-----------|-------------|
| E-01 | Alle Enums, Records, Port-Interfaces kompilieren | `dotnet build src/RomCleanup.sln` = 0 Errors |
| E-02 | Kein bestehender Test bricht | `dotnet test` = 5200+ passed, 0 failed |
| E-03 | Keine Dependency-Verletzung | Contracts-Projekt hat keine Referenz auf Core/Infrastructure/EntryPoints |

### Phase 2 – Core

| # | Kriterium | Prüfmethode |
|---|-----------|-------------|
| E-04 | SourceIntegrityClassifier: 100% Extension-Coverage getestet | 15+ Unit-Tests grün |
| E-05 | ConversionPolicyEvaluator: Safety-Matrix 100% abgedeckt | 12+ Unit-Tests grün |
| E-06 | ConversionGraph: Dijkstra findet optimalen Pfad für alle Matrix-Familien | 12+ Unit-Tests grün |
| E-07 | ConversionPlanner: Plan-Berechnung für PS1/PS2/GC/Wii/Cartridge/None | 15+ Unit-Tests grün |
| E-08 | Core-Projekt hat keine I/O-Abhängigkeit | StaticAnalysis oder manuelle Prüfung |

### Phase 3 – Data

| # | Kriterium | Prüfmethode |
|---|-----------|-------------|
| E-09 | conversion-registry.json enthält alle 76 Pfade aus CONVERSION_MATRIX | Schema-Validierungstest |
| E-10 | consoles.json hat conversionPolicy für alle 65 Systeme | Unit-Test iteriert alle Keys |
| E-11 | Kein ARCADE/NEOGEO in conversion-registry capabilities | Validierungstest |
| E-12 | JSON Schema existiert und ist korrekt | Schema-Test |

### Phase 4 – Infrastructure

| # | Kriterium | Prüfmethode |
|---|-----------|-------------|
| E-13 | ConversionRegistryLoader lädt und validiert JSON korrekt | 8+ Unit-Tests grün |
| E-14 | Alle 4 ToolInvoker: CanHandle/Invoke/Verify korrekt | 26+ Unit-Tests grün |
| E-15 | ConversionExecutor: Single-Step, Multi-Step, Cleanup, Cancel | 10+ Unit-Tests grün |
| E-16 | Zip-Slip-Guard in ChdmanInvoker verifiziert | Security-Test |

### Phase 5 – Integration

| # | Kriterium | Prüfmethode |
|---|-----------|-------------|
| E-17 | FormatConverterAdapter-Facade: identische Ergebnisse zu Vor-Refactoring | 10+ Regressionstests grün |
| E-18 | ARCADE/NEOGEO werden NICHT konvertiert (Bug-Fix verifiziert) | Explizite Tests für beide Keys |
| E-19 | Pipeline-DryRun zeigt ConversionPlans | Integrationstest |
| E-20 | RunProjection enthält neue Conversion-Metriken | Unit-Test auf Factory |
| E-21 | Alle bestehenden 5200+ Tests grün | `dotnet test` = 0 failed |

### Phase 6 – Entry Points

| # | Kriterium | Prüfmethode |
|---|-----------|-------------|
| E-22 | CLI DryRun-JSON enthält conversionPlans[] | Integrationstest |
| E-23 | API Response enthält ConvertBlockedCount, ConvertReviewCount | Integrationstest |
| E-24 | GUI DashboardProjection zeigt differenzierte Conversion-Counts | Unit-Test |
| E-25 | ManualOnly-Dialog wird korrekt angezeigt (RequiresReview=true) | WPF-Integrationstest oder manuell |
| E-26 | HTML-Report Conversion-Sektion mit HTML-Escaping | Unit-Test + manuelles Review |
| E-27 | CLI / API / GUI Parity für Conversion-Counts | 5+ Parity-Tests grün |
| E-28 | Gesamt-Testsuite ~3500+ Tests, 0 failures | `dotnet test` final |

---

## 10. Alternatives

- **ALT-001**: **Dictionary-Approach beibehalten** (DefaultBestFormats erweitern statt Graph). Abgelehnt: Kann keine Multi-Step-Ketten, keine Policy-Checks, keine Data-Driven-Konfiguration. Skaliert nicht auf 76 Pfade.
- **ALT-002**: **consoles.json als alleinige Quelle** (Capabilities inline pro System statt separater conversion-registry.json). Abgelehnt: consoles.json wird bereits zu gross (65 Einträge). Capabilities sind format→format-Beziehungen, nicht system-zentrisch. Separation of Concerns.
- **ALT-003**: **NuGet-Graphbibliothek** (QuikGraph o.ä.). Abgelehnt: Dijkstra auf <100 Knoten ist trivial (<100 Zeilen Code). Keine externe Dependency nötig (CON-004).
- **ALT-004**: **Plugin-basierte Tool-Invoker** (separate Assemblies pro Tool). Abgelehnt: Over-Engineering für 5 Tools. Interne IToolInvoker-Implementierungen in Infrastructure reichen.
- **ALT-005**: **Parallele Conversion** (mehrere Dateien gleichzeitig konvertieren). Abgelehnt für v1.0: Komplexitätsrisiko. chdman/dolphintool sind CPU-intensiv. Sequenzielle Verarbeitung ist deterministischer. Parallelisierung als separates Epic (ASSUMPTION-003).

---

## 11. Dependencies

- **DEP-001**: `data/consoles.json` — Wird um 3 Felder erweitert (conversionPolicy, preferredConversionTarget, alternativeTargets). Alle bestehenden Parser müssen optionale Felder tolerieren.
- **DEP-002**: `data/tool-hashes.json` — ciso/maxcso Hash-Eintrag fehlt. Blockiert CSO→ISO→CHD Kette bis eingetragen.
- **DEP-003**: `IFormatConverter` (bestehend) — Public-Interface bleibt. FormatConverterAdapter wird zur Facade.
- **DEP-004**: `IToolRunner` (bestehend) — Alle Tool-Invocations laufen weiterhin über dieses Interface. ToolInvoker-Schicht arbeitet darüber.
- **DEP-005**: `PipelinePhaseHelpers` (bestehend) — Audit-Methoden (AppendConversionAudit etc.) werden von ConversionExecutionPhase genutzt.
- **DEP-006**: `RunProjection` / `RunProjectionFactory` (bestehend) — Werden um 3 Felder erweitert. Alle Entry-Point-Mappings (CLI/API/GUI) müssen angepasst werden.
- **DEP-007**: `docs/architecture/CONVERSION_ENGINE_ARCHITECTURE.md` — Primäre Architektur-Referenz. Alle Code-Typen und Service-Signaturen sind dort definiert.

---

## 12. Eigene Epics (Out of Scope)

Die folgenden Themen sind bewusst NICHT Teil dieses Plans und gehören in separate Epics:

| # | Epic | Begründung | Abhängigkeit |
|---|------|------------|-------------|
| **EPIC-01** | **PS2 SYSTEM.CNF-Analyse** (robuste CD/DVD-Erkennung) | Erfordert ISO-Mounting/Parsing, eigene Testdaten, eigenes Risikoprofil. 700MB-Heuristik reicht für v1.0. | Phase 5 abgeschlossen |
| **EPIC-02** | **RVZ-Verify-Verbesserung** (dolphintool dry-convert als Integrity-Check) | Erfordert Dolphin-Tool-Analyse, Performance-Benchmark, eigenes ADR. Magic-Byte-Check reicht für v1.0. | Phase 4 abgeschlossen |
| **EPIC-03** | **Parallele Conversion** (Thread-Pool für Batch-Conversion) | Erfordert Concurrency-Architektur, Resource-Management, eigene Tests. Sequenziell reicht für v1.0. | Phase 5 abgeschlossen |
| **EPIC-04** | **MDF/MDS/NRG-Support** (nicht integrierte Tools) | mdf2iso/nrg2iso müssten integriert, gehasht, getestet werden. Nische. | Phase 4 abgeschlossen |
| **EPIC-05** | **Conversion-Preview-UI** (dediziertes Conversion-Tab in WPF mit Filter/Sort/Detail) | Erfordert eigenes UI-Design, DataGrid-Bindings, Filter-ViewModel. Dashboard-Integration reicht für v1.0. | Phase 6 abgeschlossen |
| **EPIC-06** | **ciso-Tool-Integration** (CSO→ISO Decompression) | Tool muss beschafft, validiert, in tool-hashes.json aufgenommen werden. Bis dahin: CSO=Skipped. | Phase 3 abgeschlossen |

---

## 13. Files

### Neue Dateien (22 Stück)

- **FILE-001**: `src/RomCleanup.Contracts/Models/ConversionPolicyModels.cs` — Enums: ConversionPolicy, SourceIntegrity, ConversionSafety, ConversionCondition
- **FILE-002**: `src/RomCleanup.Contracts/Models/ConversionGraphModels.cs` — Records: ConversionCapability, ToolRequirement
- **FILE-003**: `src/RomCleanup.Contracts/Models/ConversionPlanModels.cs` — Records: ConversionStep, ConversionPlan
- **FILE-004**: `src/RomCleanup.Contracts/Ports/IConversionRegistry.cs` — Port-Interface
- **FILE-005**: `src/RomCleanup.Contracts/Ports/IConversionPlanner.cs` — Port-Interface
- **FILE-006**: `src/RomCleanup.Contracts/Ports/IConversionExecutor.cs` — Port-Interface
- **FILE-007**: `src/RomCleanup.Contracts/Ports/IToolInvoker.cs` — Internes Interface
- **FILE-008**: `src/RomCleanup.Core/Conversion/SourceIntegrityClassifier.cs` — Pure Logik
- **FILE-009**: `src/RomCleanup.Core/Conversion/ConversionPolicyEvaluator.cs` — Pure Logik
- **FILE-010**: `src/RomCleanup.Core/Conversion/ConversionConditionEvaluator.cs` — Condition-Dispatch
- **FILE-011**: `src/RomCleanup.Core/Conversion/ConversionGraph.cs` — Graph + Dijkstra
- **FILE-012**: `src/RomCleanup.Core/Conversion/ConversionPlanner.cs` — Plan-Berechnung
- **FILE-013**: `data/conversion-registry.json` — Alle ConversionCapabilities
- **FILE-014**: `data/schemas/conversion-registry.schema.json` — JSON Schema
- **FILE-015**: `src/RomCleanup.Infrastructure/Conversion/ConversionRegistryLoader.cs` — JSON-Loader
- **FILE-016**: `src/RomCleanup.Infrastructure/Conversion/ConversionExecutor.cs` — Step-Executor
- **FILE-017**: `src/RomCleanup.Infrastructure/Conversion/ToolInvokers/ChdmanInvoker.cs` — chdman-Wrapper
- **FILE-018**: `src/RomCleanup.Infrastructure/Conversion/ToolInvokers/DolphinToolInvoker.cs` — dolphintool-Wrapper
- **FILE-019**: `src/RomCleanup.Infrastructure/Conversion/ToolInvokers/SevenZipInvoker.cs` — 7z-Wrapper
- **FILE-020**: `src/RomCleanup.Infrastructure/Conversion/ToolInvokers/PsxtractInvoker.cs` — psxtract-Wrapper
- **FILE-021**: `src/RomCleanup.Infrastructure/Orchestration/ConversionPlanningPhase.cs` — Pipeline-Phase
- **FILE-022**: `src/RomCleanup.Infrastructure/Orchestration/ConversionExecutionPhase.cs` — Pipeline-Phase

### Geänderte Dateien (11 Stück)

- **FILE-023**: `src/RomCleanup.Contracts/Models/ConversionModels.cs` — ConversionOutcome+Blocked, ConversionResult erweitert
- **FILE-024**: `src/RomCleanup.Infrastructure/Conversion/FormatConverterAdapter.cs` — Facade-Refactoring, DefaultBestFormats entfernt
- **FILE-025**: `data/consoles.json` — conversionPolicy, preferredConversionTarget, alternativeTargets für alle 65 Systeme
- **FILE-026**: `src/RomCleanup.Infrastructure/Orchestration/RunProjection.cs` — Neue Felder
- **FILE-027**: `src/RomCleanup.Infrastructure/Orchestration/RunProjectionFactory.cs` — Factory-Erweiterung
- **FILE-028**: `src/RomCleanup.CLI/CliOutputWriter.cs` — conversionPlans[], conversionBlocked[]
- **FILE-029**: `src/RomCleanup.Api/ApiRunResultMapper.cs` — Neue Conversion-Felder
- **FILE-030**: `src/RomCleanup.UI.Wpf/Models/DashboardProjection.cs` — Neue Conversion-Felder
- **FILE-031**: `src/RomCleanup.Infrastructure/Reporting/HtmlReportBuilder.cs` — Conversion-Sektion
- **FILE-032**: `src/RomCleanup.Infrastructure/Reporting/CsvReportBuilder.cs` — Conversion-Spalten
- **FILE-033**: `data/tool-hashes.json` — ciso/maxcso Eintrag

### Neue Testdateien (12 Stück)

- **FILE-034**: `src/RomCleanup.Tests/Conversion/SourceIntegrityClassifierTests.cs`
- **FILE-035**: `src/RomCleanup.Tests/Conversion/ConversionPolicyEvaluatorTests.cs`
- **FILE-036**: `src/RomCleanup.Tests/Conversion/ConversionGraphTests.cs`
- **FILE-037**: `src/RomCleanup.Tests/Conversion/ConversionPlannerTests.cs`
- **FILE-038**: `src/RomCleanup.Tests/Conversion/ConversionRegistrySchemaTests.cs`
- **FILE-039**: `src/RomCleanup.Tests/Conversion/ChdmanInvokerTests.cs`
- **FILE-040**: `src/RomCleanup.Tests/Conversion/DolphinToolInvokerTests.cs`
- **FILE-041**: `src/RomCleanup.Tests/Conversion/SevenZipInvokerTests.cs`
- **FILE-042**: `src/RomCleanup.Tests/Conversion/ConversionExecutorTests.cs`
- **FILE-043**: `src/RomCleanup.Tests/Conversion/ConversionRegistryLoaderTests.cs`
- **FILE-044**: `src/RomCleanup.Tests/Conversion/ConversionFacadeRegressionTests.cs`
- **FILE-045**: `src/RomCleanup.Tests/Conversion/ConversionPipelineIntegrationTests.cs`
- **FILE-046**: `src/RomCleanup.Tests/Conversion/ConversionParityTests.cs`

---

## 14. Related Specifications / Further Reading

- [CONVERSION_ENGINE_ARCHITECTURE.md](../docs/architecture/CONVERSION_ENGINE_ARCHITECTURE.md) — Technische Zielarchitektur
- [CONVERSION_MATRIX.md](../docs/architecture/CONVERSION_MATRIX.md) — Vollständige Conversion-Matrix (76 Pfade, 65 Systeme)
- [CONVERSION_PRODUCT_MODEL.md](../docs/product/CONVERSION_PRODUCT_MODEL.md) — Fachliches Produktmodell
- [CONVERSION_DOMAIN_AUDIT.md](../docs/architecture/CONVERSION_DOMAIN_AUDIT.md) — Domänenanalyse (12 Lücken)
- [ARCHITECTURE_MAP.md](../docs/architecture/ARCHITECTURE_MAP.md) — Clean Architecture Übersicht
- [ADR-0007](../docs/adrs/0007-final-core-functions-review.md) — Architektur-Review Verdict
