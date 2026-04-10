# Romulus – Test-Strategie

**Stand:** 2026-04-01  
**Framework:** xUnit (.NET 10, `src/Romulus.Tests/`)  
**Grundsatz:** Kein Alibi-Test. Jeder Test hat eine **Failure-First-Anforderung** – er muss ohne den zu testenden Code rot werden.

---

## 1. Test-Pyramide

```
        ┌──────────────────┐
        │   Integration    │  RunOrchestrator, API-RunManager, FileSystem-Ops
        ├──────────────────┤
        │   Unit           │  200+ Testdateien, aktuell 7197 Tests
        └──────────────────┘
        Gesamt: aktuell 7197 Tests (xUnit, Stand 2026-04-01 grün)
```

---

## 2. Testdateien-Übersicht

Alle Tests liegen in `src/Romulus.Tests/` (xUnit, 200+ Testdateien, inkl. Unterordner `Benchmark/` und `Conversion/`).
Die folgende Übersicht ist thematisch gruppiert; Datei- und Fallzahlen ändern sich regelmäßig.

### 2.0 Foundation-, Productization-, Reach- und Diff/Merge-Matrix 2026-04-01

Die Foundation-, Productization-, Reach- und Diff/Merge-Erweiterungen fuer Collection-Index, Guided Workflows, Frontend-Export, Profile, Trend-/Diff-Pfade, Headless-Betrieb, Reach-Conversion und Multi-Collection-Merge werden zusaetzlich gezielt ueber folgende Schwerpunktdateien abgesichert:

| Datei | Zweck |
|---|---|
| `CollectionIndexContractTests.cs` | Persistenter Index-Vertrag, Scope-Reads, Stale-Cleanup, deterministische CRUD-Semantik |
| `LiteDbCollectionIndexTests.cs` | LiteDB-Adapter, Recovery, Migration, Snapshot-Lesen/Schreiben |
| `CollectionRunSnapshotWriterTests.cs` | Persistierte Run-Snapshots inkl. `CollectionSizeBytes` und KPI-Projektion |
| `PersistedReviewDecisionServiceTests.cs` | Persistierte Review-Approvals, Reapply, idempotentes Schreiben |
| `AutomationAndTrendServiceTests.cs` | Watch-/Schedule-Services, Trend-Berechnung aus Snapshot-Historie |
| `RunConfigurationResolverRegressionTests.cs` | Workflow-/Profil-Aufloesung, Override-Prioritaet, Wizard-/Expert-Paritaet |
| `FrontendExportRegressionTests.cs` | Frontend-Export-Haertung, XML-/CSV-Schutz, Root-Containment |
| `ApiProductizationIntegrationTests.cs` | `/profiles`, `/workflows`, workflow-/profilbasierte Runs, Watch-Automation, `/export/frontend` |
| `WpfProductizationTests.cs` | WPF-Kataloge, gemeinsame Materialisierung, Profil Save/Load, Wizard-Bindings |
| `CliProductizationTests.cs` | CLI-Usage und Produktisierungs-Subcommands |
| `OpenApiProductizationTests.cs` | OpenAPI-Vertrag fuer Profile, Workflows, Compare/Trends und Frontend-Export |
| `ApiReachIntegrationTests.cs` | Dashboard-Bootstrap, AllowedRoots, Remote-Startup-Regeln und Headless-API-Flows |
| `OpenApiReachTests.cs` | OpenAPI-Vertrag fuer Dashboard-/Reach-Endpunkte |
| `ReachConversionTests.cs` | Compound-NKit-Planung und Review-Gating in der Conversion-Pipeline |
| `ReachInvokerTests.cs` | `EcmInvoker`/`NkitInvoker` Tool-Aufrufe und Output-Verifikation |
| `CollectionCompareServiceTests.cs` | Index-first Scope-Materialisierung, Fingerprint-Guards, Diff-Klassifikation und Compare-Paging |
| `CollectionMergeServiceTests.cs` | Merge-Plan-Regeln, Konflikt-/Duplicate-Faelle, Apply-/Rollback-Fehlerpfade, FS-/Index-Revert |
| `CliCollectionDiffMergeTests.cs` | `romulus diff`/`merge` Parser, Preview-/Apply-JSON und Audit-Ausgabe |
| `ApiCollectionDiffMergeTests.cs` | `/collections/compare|merge|merge/apply|merge/rollback`, Allowlist-Negativfaelle, API-Rollback |

### 2.1 Core-/Engine-Tests (17 Dateien)

| Datei | Zweck |
|---|---|
| `GameKeyNormalizerTests.cs` | GameKey-Generierung, ASCII-Fold, Tag-Parsing, Alias-Map |
| `RegionDetectorTests.cs` | Region-Erkennung aus Dateinamen |
| `DeduplicationEngineTests.cs` | Winner-Selection (deterministisch), 1G1R, BIOS/Junk |
| `FormatScorerTests.cs` | Format-Score-Berechnung (CHD, ISO, ZIP etc.) |
| `VersionScorerTests.cs` | Versions-/Revisions-Scoring |
| `VersionHelperTests.cs` | Versions-Parsing-Hilfsfunktionen |
| `FileClassifierTests.cs` | BIOS/Junk/Game-Klassifikation |
| `ConsoleDetectorTests.cs` | Konsolen-Erkennung (Ordner, Extension, DAT) |
| `ExtensionNormalizerTests.cs` | Dateiendungs-Normalisierung |
| `SetParsingTests.cs` | CUE/GDI/CCD/M3U-Parser |
| `RuleEngineTests.cs` | Regelanwendung auf ROM-Kandidaten |
| `InsightsEngineTests.cs` | Analyse-/Statistik-Engine |
| `CandidateFactoryTests.cs` | ROM-Kandidaten-Erzeugung |
| `ContentSignatureClassifierTests.cs` | Content-Signatur-Klassifizierung |
| `CompletenessAndHealthScorerTests.cs` | Vollständigkeits-/Health-Scoring |
| `DetectionPipelineTests.cs` | Erkennungs-Pipeline |
| `DiscHeaderDetectorTests.cs` | Disc-Header-Erkennung (PS1/PS2/Saturn etc.) |

### 2.2 Infrastructure-Tests (26 Dateien)

| Datei | Zweck |
|---|---|
| `FileSystemAdapterTests.cs` | Path-Traversal-Schutz, Reparse-Point-Blocking, Move/Scan |
| `AuditCsvStoreTests.cs` | Audit-CSV-Erzeugung, CSV-Injection-Schutz, Rollback |
| `AuditSigningServiceTests.cs` | SHA256-Signierung von Audit-CSVs |
| `JsonlLogWriterTests.cs` | JSONL-Logging, Rotation, Felder |
| `SettingsLoaderTests.cs` | Settings-Laden/Validierung/Defaults |
| `FileHashServiceTests.cs` | SHA1/SHA256/MD5-Hashing, LRU-Cache |
| `Crc32Tests.cs` | CRC32-Berechnung |
| `ArchiveHashServiceTests.cs` | Hash-Extraktion aus Archiven |
| `ParallelHasherTests.cs` | Paralleles Hashing |
| `DatRepositoryAdapterTests.cs` | DAT-XML-Parsing, XXE-Schutz, Parent/Clone |
| `DatSourceServiceTests.cs` | DAT-Download, SHA256-Sidecar-Verifizierung |
| `ReportGeneratorTests.cs` | HTML/CSV-Reports, CSP-Header, HTML-Encoding |
| `RunReportWriterTests.cs` | Report-Datei-Schreiben |
| `ReportPathResolverTests.cs` | Report-Pfad-Auflösung |
| `FormatConverterAdapterTests.cs` | Format-Konvertierung (CHD/RVZ/ZIP) |
| `ConverterTests.cs` | Allgemeine Konverter-Tests |
| `SortingTests.cs` | Konsolen-Sortierung |
| `LruCacheTests.cs` | LRU-Cache (Thread-Safety, Eviction) |
| `AppStateStoreTests.cs` | App-State, Undo/Redo, Watch-Pattern |
| `ArtifactPathResolverTests.cs` | Artefakt-Pfad-Auflösung |
| `ToolRunnerAdapterTests.cs` | Tool-Runner-Adapter |
| `ErrorClassifierTests.cs` | Fehlerklassen (Transient/Recoverable/Critical) |
| `QuarantineServiceTests.cs` | ROM-Quarantäne |
| `PhaseMetricsCollectorTests.cs` | Phasen-Zeitmessung |
| `HistoryAndIndexTests.cs` | Run-History, Scan-Index |
| `CatchGuardServiceTests.cs` | Silent-Catch-Governance |

### 2.3 Orchestrierung & Entry-Point Tests (23 Dateien)

| Datei | Zweck |
|---|---|
| `RunOrchestratorTests.cs` | Full Pipeline (Preflight→Scan→Dedupe→Sort→Move) |
| `RunManagerTests.cs` | API-RunManager, Singleton-Run, Cancel |
| `RateLimiterTests.cs` | Rate-Limiting (120/min sliding window) |
| `ExecutionHelpersTests.cs` | Ausführungshelfer |
| `SafetyValidatorTests.cs` | Safety-Checks vor Operationen |
| `RunEnvironmentBuilderTests.cs` | Run-Umgebungs-Aufbau |
| `RunProjectionFactoryTests.cs` | Run-Projektion / DryRun-Ausgabe |
| `PipelinePhaseIsolationTests.cs` | Pipeline-Phasen-Isolation |
| `RunServiceAndSettingsTests.cs` | Run-Service und Settings-Integration |
| `FeatureServiceTests.cs` | Feature-Service-Logik |
| `FeatureServiceLargeFileTests.cs` | Feature-Service mit großen Dateien |
| `FeatureCommandServiceTests.cs` | Feature-Command-Service |
| `FcsExecutionAndSettingsTests.cs` | FCS-Ausführung und Settings |
| `CliProgramTests.cs` | CLI-Programmstart, Argumente, Ausgabe |
| `CliOptionsMapperTests.cs` | CLI-Options-Mapping |
| `CliRedPhaseTests.cs` | CLI Red-Phase-Absicherung |
| `ApiIntegrationTests.cs` | API-Integrationstest (Endpunkte, Lifecycle) |
| `ApiRedPhaseTests.cs` | API Red-Phase-Absicherung |
| `ApiSecurityTests.cs` | API-Sicherheitstests |
| `GuiViewModelTests.cs` | WPF ViewModel-Tests |
| `UiProjectionTests.cs` | UI-Projektion / Dashboard-Darstellung |
| `WpfCoverageBoostTests.cs` | WPF-Coverage-Ergänzung |
| `WpfNewTests.cs` | Neue WPF-Funktionalitäten |

### 2.4 Safety, Security & Parity Tests (10 Dateien)

| Datei | Zweck |
|---|---|
| `SafetyIoRecoveryTests.cs` | IO-Recovery nach Fehlern |
| `SafetyIoSecurityRedPhaseTests.cs` | IO-Security-Red-Phase |
| `SecurityTests.cs` | Allgemeine Security-Invarianten |
| `CrossRootAndHardlinkTests.cs` | Cross-Root-Deduplizierung, Hardlinks |
| `UncPathTests.cs` | UNC-Pfad-Handling |
| `FolderDeduplicatorTests.cs` | Ordner-Deduplizierung |
| `PreviewExecuteParityTests.cs` | Preview/Execute-Gleichheit |
| `ReportParityTests.cs` | Report-Zahlen-Konsistenz |
| `ConversionReportParityTests.cs` | Conversion-Report-Konsistenz |
| `KpiChannelParityBacklogTests.cs` | KPI-Kanal-Parität (CLI/API/GUI) |

### 2.5 Invariant- & Regressionstests (18 Dateien)

| Datei | Zweck |
|---|---|
| `CoreHeartbeatInvariantTests.cs` | Kern-Invarianten-Heartbeat |
| `HardAuditInvariantTests.cs` | Harte Audit-Invarianten |
| `HardCoreInvariantRegressionSuiteTests.cs` | Hardcore-Invarianten-Regression |
| `HardRegressionInvariantTests.cs` | Harte Regressionsprüfungen |
| `MovePhaseAuditInvariantTests.cs` | Move-Phase-Audit-Invarianten |
| `AuditComplianceTests.cs` | Audit-Compliance |
| `AuditFindingsFixTests.cs` | Audit-Finding-Fixes |
| `AuditFindingsRegressionTests.cs` | Audit-Finding-Regressionen |
| `AuditHardeningTests.cs` | Audit-Härtung |
| `Issue9InvariantRegressionRedPhaseTests.cs` | Issue-9-Regression |
| `TddRedPhaseHardcoreTests.cs` | TDD-Red-Phase-Hardcore |
| `V1TestGapTests.cs` | V1-Testlücken |
| `V2RemainingTests.cs` | V2-Restlücken |
| `V2MemH01StreamingRedPhaseTests.cs` | V2-Streaming-Red-Phase |
| `MutationKillTests.cs` | Mutation-Testing |
| `ConcurrencyTests.cs` | Parallelitäts-/Thread-Safety-Tests |
| `ChaosTests.cs` | Chaos-/Fault-Injection |
| `FixedAndIntegrationTests.cs` | Fixierte Integrationstests |

### 2.6 Conversion Tests (14 Dateien)

Im Unterordner `Conversion/`:

| Datei | Zweck |
|---|---|
| `ConversionExecutorHardeningTests.cs` | Executor-Härtung (Pfad, Timeout, Cleanup) |
| `ConversionFacadeRegressionTests.cs` | Facade-Regressionstests |
| `ConversionGraphTests.cs` | Konvertierungs-Graph |
| `ConversionMetricsPipelineTests.cs` | Metriken-Pipeline (Bytes, Review, Blocked) |
| `ConversionPlannerTests.cs` | Konvertierungs-Planung |
| `ConversionPolicyEvaluatorTests.cs` | Policy-Evaluierung (Safety-Einstufung) |
| `ConversionRegistryLoaderTests.cs` | Registry-Laden, Schema-Validierung |
| `ConversionRegistrySchemaTests.cs` | Registry-Schema-Tests |
| `FormatConverterArchiveSecurityTests.cs` | Archiv-Security (Zip-Slip etc.) |
| `SourceIntegrityClassifierTests.cs` | Quell-Integritäts-Klassifizierung |
| `ToolInvokerAdapterHardeningTests.cs` | Tool-Invoker-Härtung (Hash, Version) |
| `ChdmanInvokerTests.cs` | chdman-Toolaufruf |
| `DolphinToolInvokerTests.cs` | dolphintool-Toolaufruf |
| `SevenZipInvokerTests.cs` | 7z-Toolaufruf |

### 2.7 Benchmark Tests (28 Dateien)

Im Unterordner `Benchmark/`:

| Datei | Zweck |
|---|---|
| `AntiGamingGateTests.cs` | Anti-Gaming-Gate |
| `BaselineRegressionGateTests.cs` | Baseline-Regressions-Gate |
| `ChaosMixedBenchmarkTests.cs` | Chaos-Mixed-Benchmark |
| `ConfidenceCalibrationTests.cs` | Confidence-Kalibrierung |
| `ConfusionMatrixRedTests.cs` | Confusion-Matrix-Red-Phase |
| `CoverageGateTests.cs` | Coverage-Gate |
| `DatasetExpansionTests.cs` | Dataset-Expansion |
| `DatCoverageBenchmarkTests.cs` | DAT-Coverage-Benchmark |
| `EdgeCaseBenchmarkTests.cs` | Edge-Case-Benchmark |
| `EvaluationRunnerRedTests.cs` | Evaluation-Runner-Red-Phase |
| `GoldenCoreBenchmarkTests.cs` | Golden-Core-Benchmark |
| `GoldenRealworldBenchmarkTests.cs` | Golden-Realworld-Benchmark |
| `GroundTruthComparatorTests.cs` | Ground-Truth-Vergleicher |
| `GroundTruthParsingRedTests.cs` | Ground-Truth-Parsing-Red-Phase |
| `HoldoutGateTests.cs` | Holdout-Gate |
| `MetricCalculationRedTests.cs` | Metrik-Berechnung-Red-Phase |
| `MetricsAggregatorTests.cs` | Metriken-Aggregation |
| `MetricsSafetyThresholdRedTests.cs` | Metriken-Safety-Schwellwerte |
| `NegativeControlBenchmarkTests.cs` | Negativ-Kontroll-Benchmark |
| `PerformanceBenchmarkTests.cs` | Performance-Benchmark |
| `PerformanceScaleTests.cs` | Performance-Skalierung |
| `QualityGateTests.cs` | Quality-Gate |
| `RegressionDetectionRedTests.cs` | Regressions-Erkennung |
| `RepairSafetyBenchmarkTests.cs` | Repair-Safety-Benchmark |
| `ReportGenerationRedTests.cs` | Report-Generierung-Red-Phase |
| `SystemTierGateTests.cs` | System-Tier-Gate |
| `TrendAndCrossValidationTests.cs` | Trend- und Kreuzvalidierung |
| `VerdictSeparationRedTests.cs` | Verdict-Separation-Red-Phase |

---

## 3. Test-Konventionen

### 3.1 Naming

```
<Klasse>Tests.cs    # z.B. GameKeyNormalizerTests.cs, DeduplicationEngineTests.cs
```

### 3.2 Test-Methoden-Naming

```csharp
[Fact]
public void SelectWinner_SameInputs_ReturnsSameWinner()  // Determinismus

[Theory]
[InlineData("EU", 1000)]
[InlineData("US", 999)]
public void GetRegionScore_PreferredRegion_ReturnsExpectedScore(string region, int expected)
```

### 3.3 Fixture-Strategie

| Situation | Empfohlen |
|---|---|
| Dateisystem | `Path.GetTempPath()` + `Directory.CreateTempSubdirectory()` + cleanup in `Dispose` |
| Externe Tools (chdman, 7z) | Mock via Interface (`IToolRunner`) |
| Port-Interfaces | Eigene Test-Implementierungen oder Mocks |
| Große Dateien (>500 MB) | Überspringen mit `Skip` oder künstliche Stubs |

### 3.4 Pflicht-Invarianten

Jeder Test muss einen echten Fehler finden können. Typische Invarianten:

- **Winner-Selection ist deterministisch** — gleiche Inputs = gleicher Winner
- **Kein Move außerhalb der Root** — Path-Traversal-Versuche müssen scheitern
- **Keine leeren Keys** — GameKey-Normalisierung darf keine leeren Strings liefern
- **CSV-Injection-Schutz** — führende `=`, `+`, `-`, `@` werden escaped
- **Run-Snapshots sind wiederverwendbar** — Historie und Trends duerfen keinen neuen Full-Scan benoetigen
- **Review-Zustand ist kanaluebergreifend identisch** — GUI, CLI und API duerfen keine abweichenden Approval-Ergebnisse liefern
- **Watch-/Schedule-Bursts bleiben deterministisch** — Debounce, Busy-Schutz und Pending-Flush duerfen keine Doppel-Runs erzeugen
- **Compare-/Merge-Pfade bleiben kanalgleich** — GUI, CLI, API und OpenAPI duerfen keine abweichenden Diff-/Merge-Entscheidungen erzeugen
- **Merge-Rollback bleibt verifizierbar** — signierte Audits, Root-Metadaten und COPY/MOVE-Rollback muessen denselben Schutzvertrag einhalten

### 3.5 Verboten

- `Assert.True(true)` (Alibi-Test)
- `try/catch` das Tests immer grün macht
- Tests ohne Assertions

---

## 4. Coverage-Ziel

CI-Pipeline prüft einen globalen **Minimum-Schwellwert von 50%**.

| Bereich | Minimal-Coverage |
|---|---|
| Core-Domain-Logik | 70% |
| Infrastructure-Adapter | 50% |
| API/CLI Entry Points | 40% |

---

## 5. CI-Pipeline (`.github/workflows/test-pipeline.yml`)

| Job | Gate | Bei Fehler |
|---|---|---|
| Unit-Tests + Coverage | 50% Minimum | Fail |
| Governance | Modul-Grenzen, Komplexität | Warn only |
| Mutation-Testing | Reporting only | continue-on-error |
| Benchmark-Gate | Regression-Erkennung | continue-on-error |

Trigger: Push/PR auf `dev/**`, `.github/**`

### Testausführung

```bash
# Alle Tests
dotnet test src/Romulus.sln

# Einzelnes Testprojekt
dotnet test src/Romulus.Tests/Romulus.Tests.csproj

# Mit Filter
dotnet test src/Romulus.sln --filter "FullyQualifiedName~GameKey"

# Mit Coverage
dotnet test src/Romulus.sln --collect:"XPlat Code Coverage"
```

### Release-Smokes

```powershell
pwsh -NoProfile -File deploy/smoke/Invoke-ReleaseSmoke.ps1 -Configuration Release
pwsh -NoProfile -File deploy/smoke/Invoke-HeadlessSmoke.ps1 -Configuration Release -SkipBuild
```
