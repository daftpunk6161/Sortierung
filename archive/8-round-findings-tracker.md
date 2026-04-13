# Romulus – 8-Runden Audit Findings Tracker

> Erstellt: 2026-04-12  
> Quelle: 8 Audit-Runden (Entry Points, Safety, Data/Schema, DI, Error Handling, Hashing/Tools, Orchestration, Final Sweep + Sorting)  
> **Gesamt: 120 Findings**  
> Status-Legende: ✅ Fixed | ⚠️ Partial | ❌ Open

---

## Zusammenfassung

| Kategorie                | Gesamt | ✅ Fixed | ⚠️ Partial | ❌ Open |
|--------------------------|--------|---------|------------|--------|
| Entry Points / MVVM      | 15     | 15      | 0          | 0      |
| Safety / Security         | 10     | 10      | 0          | 0      |
| Data / Schema / Config    | 12     | 12      | 0          | 0      |
| DI / Startup              | 10     | 10      | 0          | 0      |
| Error Handling             | 9      | 9       | 0          | 0      |
| Hashing / Tools            | 13     | 13      | 0          | 0      |
| Orchestration (R7)         | 10     | 10      | 0          | 0      |
| Final Sweep (R8)           | 6      | 6       | 0          | 0      |
| Sorting / Move Pipeline    | 5      | 5       | 0          | 0      |
| **Dedup / Core Logic**     | 8      | 8       | 0          | 0      |
| **API Hardening**          | 8      | 8       | 0          | 0      |
| **Test Hygiene**           | 7      | 7       | 0          | 0      |
| **i18n / UX**              | 7      | 7       | 0          | 0      |
| **Summe**                  | **120**| **120** | **0**      | **0**  |

---

## 1. Entry Points / MVVM (15)

- [x] **EP-01** ✅ MVVM-Verstoß behoben: Safety-Filterlogik aus Code-Behind in ViewModel-Projektion verschoben  
  📁 `src/Romulus.UI.Wpf/Views/LibrarySafetyView.xaml.cs`, `src/Romulus.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs`

- [x] **EP-02** ✅ MVVM-Verstoß behoben: Report-Error-Mapping in ViewModel-Servicepfad zentralisiert  
  📁 `src/Romulus.UI.Wpf/Views/LibraryReportView.xaml.cs`, `src/Romulus.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs`

- [x] **EP-03** ✅ Hidden Coupling behoben: PropertyChanged-Watcher aus Code-Behind entfernt  
  📁 `src/Romulus.UI.Wpf/Views/LibrarySafetyView.xaml.cs`

- [x] **EP-04** ✅ Run-Result KPI Projection: `RunProjectionFactory` ist Single Source of Truth  
  📁 CLI(2x), API(1x), WPF(1x), Reporting(1x), Index(1x) nutzen alle RunProjectionFactory

- [x] **EP-05** ✅ DAT-Katalog-Scan vereinheitlicht: gemeinsame Infrastruktur-Scanlogik in DatCatalogStateService  
  📁 `src/Romulus.UI.Wpf/ViewModels/DatCatalogViewModel.cs:137` vs `src/Romulus.Api/DashboardDataBuilder.cs:110-170`

- [x] **EP-06** ✅ Error-Handling in Report-Preview gehärtet: typisierte Fallback-Logik und konsistente Warnpfade  
  📁 `src/Romulus.UI.Wpf/Views/LibraryReportView.xaml.cs:87-93` vs `src/Romulus.UI.Wpf/Services/RunService.cs:143`

- [x] **EP-07** ✅ Settings-Sync MainVM↔SetupVM: Reentrancy-Guard auf verschachtelungssichere Scope-Logik umgestellt  
  📁 `src/Romulus.UI.Wpf/ViewModels/MainViewModel.Settings.cs` (SetupSyncScope / Depth-Guard)

- [x] **EP-08** ✅ API Profile PUT: route id≠body.Id wird mit 400 validiert abgewiesen  
  📁 `src/Romulus.Api/Program.ProfileWorkflowEndpoints.cs`

- [x] **EP-09** ✅ API Rate-Limiting gehärtet: TrustForwardedFor nur bei loopback-only BindAddress aktiv  
  📁 `src/Romulus.Api/Program.cs`, `src/Romulus.Api/ProgramHelpers.cs`

- [x] **EP-10** ✅ DAT Sidecar Validation: FeatureCommandService.Dat nutzt jetzt ebenfalls strictSidecarValidation-Wiring  
  📁 `src/Romulus.UI.Wpf/Services/FeatureCommandService.Dat.cs:157,210,222`

- [x] **EP-11** ✅ FeatureCommandService nutzt DI für IFileSystem/IAuditStore; MainWindow erstellt Service nicht mehr manuell  
  📁 `src/Romulus.UI.Wpf/Services/FeatureCommandService.Collection.cs:78,99`

- [x] **EP-12** ✅ API Run-/Watch-Endpoints aus Program.cs extrahiert (Partial-Registrierung)  
  📁 `src/Romulus.Api/Program.cs:597+`

- [x] **EP-13** ✅ WebView2-Fallback: Control wird beim Fallback deterministisch disposed, Leak-Risiko entschärft  
  📁 `src/Romulus.UI.Wpf/Views/LibraryReportView.xaml.cs:98-120`

- [x] **EP-14** ✅ CLI Entry Point ist sauber: korrekte Delegation, Exit-Codes, Error-Handling  
  📁 `src/Romulus.CLI/Program.cs`

- [x] **EP-15** ✅ RunViewModel State Machine: `RunStateMachine.IsValidTransition()` korrekt enforce  
  📁 `src/Romulus.UI.Wpf/ViewModels/RunViewModel.cs:28-39`

---

## 2. Safety / Security (10)

- [x] **SEC-01** ✅ Path Traversal: SEC-PATH-01/02/03 – ADS, trailing dots, device names abgesichert  
  📁 `src/Romulus.Infrastructure/Safety/SafetyValidator.cs`

- [x] **SEC-02** ✅ Reparse Points: Blockierung auf File/Directory-Level mit Ancestry-Check  
  📁 `src/Romulus.Infrastructure/FileSystem/FileSystemAdapter.cs`

- [x] **SEC-03** ✅ ZIP-Slip: Per-Entry-Validierung mit `destPath.StartsWith()` vor Extraktion  
  📁 `src/Romulus.Infrastructure/Conversion/ChdmanToolConverter.cs`

- [x] **SEC-04** ✅ ZIP-Bomb: Entry-Count (10k), Total-Size (10GB), Compression-Ratio (50x) Limits  
  📁 `src/Romulus.Infrastructure/Conversion/ChdmanToolConverter.cs`

- [x] **SEC-05** ✅ Root Containment: `ResolveChildPathWithinRoot()` mit NFC-Normalisierung  
  📁 `src/Romulus.Infrastructure/FileSystem/FileSystemAdapter.cs`

- [x] **SEC-06** ✅ Collision Handling: `__DUP{n}` Suffix mit try/catch TOCTOU-Mitigation  
  📁 `src/Romulus.Infrastructure/FileSystem/FileSystemAdapter.cs:327`

- [x] **SEC-07** ✅ Trash-Operationen: Gehen via MoveItemSafely + ResolveChildPathWithinRoot  
  📁 `src/Romulus.Infrastructure/Orchestration/PipelinePhaseHelpers.cs`

- [x] **SEC-08** ✅ Profile-IDs: Regex-enforced `^[A-Za-z0-9._-]{1,64}$` zentral  
  📁 `src/Romulus.Infrastructure/Profiles/RunProfileValidator.cs`

- [x] **SEC-09** ✅ File-Level TOCTOU gehärtet: Source-File-Identity wird direkt vor Move erneut validiert + IOException-Fallback bleibt aktiv  
  📁 `src/Romulus.Infrastructure/FileSystem/FileSystemAdapter.cs`

- [x] **SEC-10** ✅ Preflight Probe nutzt jetzt I/O-Port statt bare File API (`_fs.WriteAllText`/`_fs.DeleteFile`)  
  📁 `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.cs:173`

---

## 3. Data / Schema / Config (12)

- [x] **DATA-01** ✅ Schemas ergänzt: format-scores.json, tool-hashes.json, ui-lookups.json  
  📁 `data/schemas/format-scores.schema.json`, `data/schemas/tool-hashes.schema.json`, `data/schemas/ui-lookups.schema.json`

- [x] **DATA-02** ✅ console-maps.json ist in der Startup-Validierung enthalten  
  📁 `src/Romulus.Infrastructure/Configuration/StartupDataSchemaValidator.cs`

- [x] **DATA-03** ✅ builtin-profiles.json Schema-Mismatch behoben (Schema auf reales Array-Modell angepasst)  
  📁 `data/builtin-profiles.json`, `data/schemas/profiles.schema.json`

- [x] **DATA-04** ✅ NKit-Widerspruch behoben (`lossless=false` bei lossy NKit-Capabilities)  
  📁 `data/conversion-registry.json:145-175`

- [x] **DATA-05** ✅ `.rom` Extension in format-scores.json ergänzt  
  📁 `data/format-scores.json`, `data/defaults.json:3`

- [x] **DATA-06** ✅ SCAN-Region: Korrekt normalisiert → EU in RegionDetector  
  📁 `src/Romulus.Core/Regions/RegionDetector.cs:192`

- [x] **DATA-07** ✅ FR-i18n Branding korrigiert: "Romulus" statt "ROM Cleanup"  
  📁 `data/i18n/fr.json`

- [x] **DATA-08** ✅ Lazy-Singleton-Fallbacks gehärtet: kein stilles Verschlucken mehr, deterministisches Logging + Fallback  
  📁 `src/Romulus.Infrastructure/Orchestration/FormatScoringProfile.cs`, `src/Romulus.UI.Wpf/Services/UiLookupData.cs`

- [x] **DATA-09** ✅ ValidateSettingsStructure erlaubt unbekannte Top-Level-Keys (Extensibility)  
  📁 `src/Romulus.Infrastructure/Configuration/SettingsLoader.cs:191-192`

- [x] **DATA-10** ✅ RomulusSettings enthält `Rules`; User-Rules-Overrides werden gemerged  
  📁 `src/Romulus.Contracts/Models/RomulusSettings.cs:9-18`, `src/Romulus.Infrastructure/Configuration/SettingsLoader.cs`

- [x] **DATA-11** ✅ StartupDataSchemaValidator erweitert (inkl. console-maps/format-scores/tool-hashes/ui-lookups)  
  📁 `src/Romulus.Infrastructure/Configuration/StartupDataSchemaValidator.cs`

- [x] **DATA-12** ✅ i18n-Fallback auf Englisch als Base umgestellt  
  📁 `src/Romulus.UI.Wpf/Services/LocalizationService.cs:63-69`, `src/Romulus.UI.Wpf/Services/FeatureService.Infra.cs`

---

## 4. DI / Startup (10)

- [x] **DI-01** ✅ CLI: DI-Composition-Root via `ServiceCollection`/`BuildServiceProvider` eingeführt und im Run/Subcommand-Pfad verdrahtet  
  📁 `src/Romulus.CLI/Program.cs:246,368-723`

- [x] **DI-02** ✅ API Rollback-Endpoints nutzen injizierten `AuditSigningService`  
  📁 `src/Romulus.Api/Program.cs:488`, `src/Romulus.Api/Program.RunWatchEndpoints.cs`

- [x] **DI-03** ✅ ProgramHelpers nutzt injizierte `IRunEnvironmentFactory` (kein `new RunEnvironmentFactory()` mehr)  
  📁 `src/Romulus.Api/ProgramHelpers.cs`, `src/Romulus.Api/Program.RunWatchEndpoints.cs`

- [x] **DI-04** ✅ WPF RunService nutzt injizierte `IRunEnvironmentFactory` ohne Constructor-Nullcoalescing-Fallback  
  📁 `src/Romulus.UI.Wpf/Services/RunService.cs`

- [x] **DI-05** ✅ FeatureCommandService nutzt explizit injizierte Infrastruktur-Ports (`IFileSystem`, `IAuditStore`) statt Constructor-Fallbacks  
  📁 `src/Romulus.UI.Wpf/Services/FeatureCommandService.cs`, `src/Romulus.UI.Wpf/Services/FeatureCommandService.Collection.cs`

- [x] **DI-06** ✅ PersistedReviewDecisionService wird beim API-Shutdown explizit disposed  
  📁 `src/Romulus.Api/Program.cs`

- [x] **DI-07** ✅ ICollectionIndex wird beim API-Shutdown explizit disposed  
  📁 `src/Romulus.Api/Program.cs`

- [x] **DI-08** ✅ IReviewDecisionStore wird beim API-Shutdown explizit disposed  
  📁 `src/Romulus.Api/Program.cs`

- [x] **DI-09** ✅ ApiAutomationService wird beim API-Shutdown explizit disposed  
  📁 `src/Romulus.Api/Program.cs`, `src/Romulus.Api/ApiAutomationService.cs`

- [x] **DI-10** ✅ API RunManager ist `IDisposable` und führt best-effort Shutdown bei Dispose aus  
  📁 `src/Romulus.Api/RunManager.cs:18,42`

---

## 5. Error Handling (9)

- [x] **ERR-01** ✅ Bare `catch {}` entfernt; Watcher-Exceptions werden explizit geloggt  
  📁 `src/Romulus.Infrastructure/State/AppStateStore.cs:139`

- [x] **ERR-02** ✅ Silent Swallowing beseitigt (`Trace`-Logging pro Watcher-Fehler)  
  📁 `src/Romulus.Infrastructure/State/AppStateStore.cs:138-139`

- [x] **ERR-03** ✅ Fire-and-forget Catch-Pfad ohne stilles Verschlucken bereinigt  
  📁 `src/Romulus.UI.Wpf/ViewModels/MainViewModel.cs:230`

- [x] **ERR-04** ✅ Infrastructure nutzt `ILogger<T>` in kritischen Integrationsdiensten (DAT/Tools/Watch)  
  📁 `src/Romulus.Infrastructure/Dat/DatSourceService.cs`, `src/Romulus.Infrastructure/Tools/ToolRunnerAdapter.cs`, `src/Romulus.Infrastructure/Watch/WatchFolderService.cs`

- [x] **ERR-05** ✅ Externe HTTP-/Tool-Calls haben Retry-Pfade (`ExecuteHttpWithRetryAsync`, `RunProcessWithRetry`)  
  📁 `src/Romulus.Infrastructure/Dat/DatSourceService.cs`, `src/Romulus.Infrastructure/Tools/ToolRunnerAdapter.cs`

- [x] **ERR-06** ✅ API: Global Exception Middleware vorhanden  
  📁 `src/Romulus.Api/Program.cs:62-80` (`UseExceptionHandler`)

- [x] **ERR-07** ✅ API-Fehler sind RFC-7807-kompatibel (`application/problem+json` + Problem-Details-Felder in `OperationErrorResponse`)  
  📁 `src/Romulus.Api/ProgramHelpers.cs`, `src/Romulus.Contracts/Errors/OperationErrorResponse.cs`

- [x] **ERR-08** ✅ CLI Sync-over-Async `GetAwaiter().GetResult()` entfernt; produktiver Mapper-Pfad ist async (`MapAsync`)  
  📁 `src/Romulus.CLI/Program.cs:165`

- [x] **ERR-09** ✅ Async void: Nur in WPF Event-Handlers (akzeptabel)  
  📁 `MainWindow.xaml.cs:146,247`, `LibraryReportView.xaml.cs:20`

---

## 6. Hashing / Tools (13)

- [x] **TH-01** ✅ N64-Header-Repair auf stream-basiertes I/O umgestellt (kein `ReadAllBytes`)  
  📁 `src/Romulus.Infrastructure/Hashing/HeaderRepairService.cs`

- [x] **TH-02** ✅ Hash-Format vereinheitlicht auf `Convert.ToHexStringLower(...)`  
  📁 `src/Romulus.Infrastructure/Hashing/FileHashService.cs`, `src/Romulus.Infrastructure/Hashing/ParallelHasher.cs`

- [x] **TH-03** ✅ FixedTimeHashEquals: `CryptographicOperations.FixedTimeEquals` korrekt  
  📁 `src/Romulus.Infrastructure/Conversion/ToolInvokerSupport.cs:95-100`

- [x] **TH-04** ✅ VerifyToolHash gehärtet: Hashing über geöffneten Handle + Change-Check vor/nach Hash  
  📁 `src/Romulus.Infrastructure/Tools/ToolRunnerAdapter.cs:673-693`

- [x] **TH-05** ✅ ChdmanToolConverter: Alle InvokeProcess-Calls nutzen ToolRequirement  
  📁 `src/Romulus.Infrastructure/Conversion/ChdmanToolConverter.cs:64,87,121,185,239`

- [x] **TH-06** ✅ Unverifizierte Platzhalter-Hashes fuer `unecm/flips/xdelta` entfernt (Tools bleiben bis verifizierter Hash bewusst deaktiviert)  
  📁 `data/tool-hashes.json`, `data/conversion-registry.json`

- [x] **TH-07** ✅ Invoke7z nutzt explizites `ToolRequirement { ToolName = "7z" }`  
  📁 `src/Romulus.Infrastructure/Tools/ToolRunnerAdapter.cs:296-305`

- [x] **TH-08** ✅ ArchiveHashService: Cache-Invalidierung via LastWriteTimeUtc + Length  
  📁 `src/Romulus.Infrastructure/Hashing/ArchiveHashService.cs:93-95`

- [x] **TH-09** ✅ FileHashService: `_persistentEntries` unter `lock (_persistentGate)` gesichert  
  📁 `src/Romulus.Infrastructure/Hashing/FileHashService.cs:358-360`

- [x] **TH-10** ✅ SNES-Header-Repair auf stream-basiertes I/O umgestellt (kein `ReadAllBytes`)  
  📁 `src/Romulus.Infrastructure/Hashing/HeaderRepairService.cs`

- [x] **TH-11** ✅ Doppelte Hash-Prüfung im Produktionspfad entfernt (Invokers überspringen Constraint-Hash wenn ToolRunnerAdapter verifiziert)  
  📁 `src/Romulus.Infrastructure/Conversion/ToolInvokers/ToolInvokerSupport.cs`, `src/Romulus.Infrastructure/Conversion/ToolInvokers/*Invoker.cs`

- [x] **TH-12** ✅ EcmInvoker/NkitInvoker Verify gehärtet (Payload-Magic/Größen-Sanity statt nur Exists+Length)  
  📁 `src/Romulus.Infrastructure/Conversion/ToolInvokers/EcmInvoker.cs`, `src/Romulus.Infrastructure/Conversion/ToolInvokers/NkitInvoker.cs`

- [x] **TH-13** ✅ Crc32 bietet cancellation-aware Overloads (`HashFile/HashStream` mit `CancellationToken`)  
  📁 `src/Romulus.Infrastructure/Hashing/Crc32.cs:31-47`

---

## 7. Orchestration (10)

- [x] **ORC-01** ✅ Trigger-Fehlerpfad beobachtet: Faulted-Tasks werden abgefangen und statusseitig hinterlegt  
  📁 `src/Romulus.Api/ApiAutomationService.cs`

- [x] **ORC-02** ✅ WatchFolderService: Recovery-Flow für fehlende/gelöschte Watch-Roots (`_configuredRoots` + `TryRecoverWatchers`)  
  📁 `src/Romulus.Infrastructure/Watch/WatchFolderService.cs`

- [x] **ORC-03** ✅ ExecutePhasePlan: Outer catch-all fängt Phase-Exceptions  
  📁 `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.cs:279`

- [x] **ORC-04** ✅ PipelineState: Double-Assign InvalidOperationException wird vom Outer-Handler gefangen  
  📁 `src/Romulus.Infrastructure/Orchestration/PhasePlanning.cs:44`

- [x] **ORC-05** ✅ RunRecord: `_approvedReviewPaths` HashSet unter `lock (_lock)` gesichert  
  📁 `src/Romulus.Api/RunManager.cs:340,348,358`

- [x] **ORC-06** ✅ PhaseMetricsCollector markiert Auto-Complete explizit (`AutoCompleted`) und führt das Flag durch Export/Result weiter  
  📁 `src/Romulus.Infrastructure/Metrics/PhaseMetricsCollector.cs`, `src/Romulus.Contracts/Models/AdvancedModels.cs`

- [x] **ORC-07** ✅ Eviction-Schutz ergänzt (`MinimumEvictionAge`) um aktive WaitForCompletion-Races zu vermeiden  
  📁 `src/Romulus.Api/RunLifecycleManager.cs:346-360`

- [x] **ORC-08** ✅ Preflight Probe verwendet IFileSystem-Port (`WriteAllText/DeleteFile`)  
  📁 `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.cs:173`

- [x] **ORC-09** ✅ DedupePhase: 0 Candidates → leere Groups, CompletePhase(0) sauber  
  📁 `src/Romulus.Infrastructure/Orchestration/DeduplicatePipelinePhase.cs:24-26`

- [x] **ORC-10** ✅ Disposed-CancellationSource liefert weiterhin cancelbaren/cancelled Tokenzustand  
  📁 `src/Romulus.Api/RunManager.cs`

---

## 8. Final Sweep – R8 (6)

- [x] **FIN-01** ✅ DatCatalogState: Case-Insensitive Comparer nach Deserialize explizit rehydratisiert  
  📁 `src/Romulus.Infrastructure/Dat/DatCatalogStateService.cs` (Deserialize)

- [x] **FIN-02** ✅ OperationResult bietet read-only Consumer-Sichten (`IReadOnly*`) bei intern kontrollierter Mutabilität  
  📁 `src/Romulus.Contracts/Models/OperationResult.cs`

- [x] **FIN-03** ✅ QuarantineModels auf init-only Properties gehärtet (`{ get; init; }`)  
  📁 `src/Romulus.Contracts/Models/QuarantineModels.cs`

- [x] **FIN-04** ✅ BenchmarkFixture: Init-Synchronisation auf `SemaphoreSlim`/`await` umgestellt (kein `lock`-Block)  
  📁 `src/Romulus.Tests/Benchmark/BenchmarkFixture.cs:24-49`

- [x] **FIN-05** ✅ xunit maxParallelThreads begrenzt (`1`)  
  📁 `src/Romulus.Tests/xunit.runner.json:2`

- [x] **FIN-06** ✅ JSON-Output vereinheitlicht: API/CLI nutzen zentral nicht-ASCII-freundliche Serializer-Pfade (`UnsafeRelaxedJsonEscaping`) inkl. SSE-Events  
  📁 `src/Romulus.Api/Program.cs`, `src/Romulus.Api/ProgramHelpers.cs`, `src/Romulus.CLI/CliOutputWriter.cs`

---

## 9. Sorting / Move Pipeline (5)

- [x] **SORT-01** ✅ DryRun Set-Path validiert vollständige Auflösung vor Move-Zählung (Preview≈Execute)  
  📁 `src/Romulus.Infrastructure/Sorting/ConsoleSorter.cs`

- [x] **SORT-02** ✅ M3U-Content wird nach atomarem Set-Move konsistent umgeschrieben  
  📁 `src/Romulus.Infrastructure/Sorting/ConsoleSorter.cs`

- [x] **SORT-03** ✅ Set-Membership deterministisch: überlappende Members werden genau einem Primary zugeordnet  
  📁 `src/Romulus.Infrastructure/Sorting/ConsoleSorter.cs`

- [x] **SORT-04** ✅ AuditCsvStore: statische Lock-Map über ref-counted Handles sauber freigegeben  
  📁 `src/Romulus.Infrastructure/Audit/AuditCsvStore.cs:14`

- [x] **SORT-05** ✅ ArchiveHashService: keine SHA1/CRC32-Mischung mehr im ZIP-Hash-Output  
  📁 `src/Romulus.Infrastructure/Hashing/ArchiveHashService.cs:191-199`

---

## 10. Dedup / Core Logic (8)

- [x] **CORE-01** ✅ GameKeyNormalizer: Deterministische Normalisierung mit Disc-Padding  
  📁 `src/Romulus.Core/GameKeys/GameKeyNormalizer.cs`

- [x] **CORE-02** ✅ DeduplicationEngine: ConsoleKey-Normalisierung definiert (invalid/leer → `UNKNOWN`, gültige Keys kanonisch)  
  📁 `src/Romulus.Core/Deduplication/DeduplicationEngine.cs`

- [x] **CORE-03** ✅ FormatScore: Deterministisch via FormatScorer mit registriertem Profil  
  📁 `src/Romulus.Core/Scoring/FormatScorer.cs`

- [x] **CORE-04** ✅ DeduplicatePipelinePhase lässt Unknown/Non-Game Gruppen durch; kein erzwungener Game-Filter mehr  
  📁 `src/Romulus.Infrastructure/Orchestration/DeduplicatePipelinePhase.cs`

- [x] **CORE-05** ✅ Winner-Selection: Deterministisch durch Score-Kette + Tiebreaker  
  📁 `src/Romulus.Core/Deduplication/DeduplicationEngine.cs`

- [x] **CORE-06** ✅ ClassificationIoResolver bleibt Interface-basiert ohne Infrastructure-Reflection und erzwingt deterministische Einmal-Konfiguration (kein Runtime-Reconfigure)  
  📁 `src/Romulus.Core/Classification/ClassificationIoResolver.cs`, `src/Romulus.Tests/TrackerAllFindingsBatch4RedTests.cs`

- [x] **CORE-07** ✅ SetParserIoResolver bleibt Interface-basiert ohne Infrastructure-Reflection und erzwingt deterministische Einmal-Konfiguration (kein Runtime-Reconfigure)  
  📁 `src/Romulus.Core/SetParsing/SetParserIoResolver.cs`, `src/Romulus.Tests/TrackerAllFindingsBatch4RedTests.cs`

- [x] **CORE-08** ✅ Expliziter Regressionstest für "disc 001" vs "disc 1" vorhanden  
  📁 `src/Romulus.Tests/TrackerAllFindingsBatch2RedTests.cs`

---

## 11. API Hardening (8)

- [x] **API-01** ✅ Global Exception Handler: `UseExceptionHandler` vorhanden  
  📁 `src/Romulus.Api/Program.cs:62-80`

- [x] **API-02** ✅ Endpoint-Fehlerpfade liefern sanitizte API-Messages; technische Exception-Details nur noch im Server-Log  
  📁 `src/Romulus.Api/Program.cs` (catch-Blöcke mit `ex.Message` in Response)

- [x] **API-03** ✅ Path Security: `ValidatePathSecurity()` mit AllowedRoots für jeden Input  
  📁 `src/Romulus.Api/ProgramHelpers.cs`

- [x] **API-04** ✅ API-Key Timing-Safe: FixedTimeEquals für Auth  
  📁 `src/Romulus.Api/Program.cs`

- [x] **API-05** ✅ Static Files nicht mehr standardmäßig ausgeliefert (Angriffsfläche reduziert)  
  📁 `src/Romulus.Api/Program.cs`

- [x] **API-06** ✅ RunRequest-Explicitness zentralisiert: precomputed Presence-Matrix (`BuildPresence`) ersetzt verteilte `HasProperty`-Abfragen  
  📁 `src/Romulus.Api/ApiRunConfigurationMapper.cs`

- [x] **API-07** ✅ DashboardDataBuilder nutzt DatCatalogStateService-basierte DAT-Status-Logik  
  📁 `src/Romulus.Api/DashboardDataBuilder.cs:110-170`

- [x] **API-08** ✅ Run-Lifecycle Tokenhandling bleibt cancelbar auch nach Disposal-Pfad  
  📁 `src/Romulus.Api/RunManager.cs`

---

## 12. Test Hygiene (7)

- [x] **TEST-01** ✅ PhaseMetricsCollectorTests: Echte Assertions vorhanden  
  📁 `src/Romulus.Tests/PhaseMetricsCollectorTests.cs`

- [x] **TEST-02** ✅ Sicherheits-Tests: SafetyIoRecoveryTests, ApiSecurityTests umfangreich  
  📁 `src/Romulus.Tests/`

- [x] **TEST-03** ✅ BenchmarkFixture ohne `lock`-Block in `InitializeAsync` (SemaphoreSlim-gated Init)  
  📁 `src/Romulus.Tests/Benchmark/BenchmarkFixture.cs:24-49`

- [x] **TEST-04** ✅ xunit Parallelisierung begrenzt (`maxParallelThreads=1`)  
  📁 `src/Romulus.Tests/xunit.runner.json`

- [x] **TEST-05** ✅ Parity-Suite in nicht-parallele Collection verschoben (`SerialExecution`)  
  📁 `src/Romulus.Tests/HardCoreInvariantRegressionSuiteTests.cs:1067`

- [x] **TEST-06** ✅ Große GUI-Testdatei gesplittet: `GuiViewModelTests.cs` < 3000 Zeilen (partial class in zweite Datei ausgelagert)  
  📁 `src/Romulus.Tests/GuiViewModelTests.cs`, `src/Romulus.Tests/GuiViewModelTests.AccessibilityAndRedTests.cs`

- [x] **TEST-07** ✅ Disc-Padding Regressionstest vorhanden  
  📁 `src/Romulus.Tests/TrackerAllFindingsBatch2RedTests.cs`

---

## 13. i18n / UX (7)

- [x] **I18N-01** ✅ FR: Produktname auf "Romulus" korrigiert  
  📁 `data/i18n/fr.json`

- [x] **I18N-02** ✅ i18n-Fallback nutzt Englisch als Base-Locale  
  📁 `src/Romulus.UI.Wpf/Services/LocalizationService.cs:63`

- [x] **I18N-03** ✅ FR-Set strukturell vollständig (Key-Parität de/fr), verbleibende identische Werte deutlich reduziert  
  📁 `data/i18n/fr.json`, `data/i18n/de.json`

- [x] **I18N-04** ✅ Hardcodierte Converter-Strings ersetzt durch i18n-Keys (`Run.PhaseDetail.*`, `Run.PhaseStatus.*`)  
  📁 `src/Romulus.UI.Wpf/Converters/Converters.cs`

- [x] **I18N-05** ✅ Locale-Switch Runtime: `INotifyPropertyChanged` + Item[]-Refresh korrekt  
  📁 `src/Romulus.UI.Wpf/Services/LocalizationService.cs:44-46`

- [x] **I18N-06** ✅ Theme/Locale-Defaults nutzen System-Detection (`auto` + Auflösung über SettingsLoader)  
  📁 `data/defaults.json`, `src/Romulus.Contracts/Models/RomulusSettings.cs`, `src/Romulus.Infrastructure/Configuration/SettingsLoader.cs`

- [x] **I18N-07** ✅ Non-ASCII JSON-Output in user-facing API/CLI-Pfaden vereinheitlicht (CLI + API HTTP + API SSE)  
  📁 `src/Romulus.Api/Program.cs`, `src/Romulus.Api/ProgramHelpers.cs`, `src/Romulus.CLI/CliOutputWriter.cs`

---

## Nächste Schritte (Prioritätsreihenfolge)

### Priorität 1 – Release-Blocker
- [x] **1. EP-01/02/03**: MVVM Code-Behind Business-Logik → ViewModel verschieben
- [x] **2. SORT-01**: DryRun/Execute Set-Divergenz → Preview-Parität sichern
- [x] **3. ORC-10/API-08**: Disposed CancellationToken → sauberes Lifecycle-Management
- [x] **4. EP-08**: API Profile PUT route≠body ID → explizite Validierung
- [x] **5. API-02**: Exception Leak in API Responses → Messages sanitizen

### Priorität 2 – Hohe Risiken
- [x] **6. DI-06/07/08/09**: IDisposable Singletons → Explicit Dispose at Shutdown
- [x] **7. ERR-08**: Sync-over-Async → async-Kette durchziehen
- [x] **8. TH-01/10**: File.ReadAllBytes OOM → Stream-basiertes Lesen
- [x] **9. TH-06**: tool-hashes.json PENDING → echte Hashes eintragen oder Tools entfernen
- [x] **10. ORC-01**: Fire-and-Forget → Exceptions loggen

### Priorität 3 – Wartbarkeit
- [x] **11. DATA-01/02/11**: Fehlende Schemas und Startup-Validierung erweitern
- [x] **12. I18N-01/03/04**: FR-Übersetzungen vervollständigen, Converter-Strings lokalisieren
- [x] **13. DI-01**: CLI DI-Container einführen (größeres Refactoring)
- [x] **14. EP-12**: API Program.cs Endpoints extrahieren
- [x] **15. TEST-04/05**: xunit Parallelisierung tunen, Parity-Flake stabilisieren
