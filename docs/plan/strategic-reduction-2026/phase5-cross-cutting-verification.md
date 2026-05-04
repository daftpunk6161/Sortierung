# Phase 5 — Cross-Cutting Verification (T-W11-CROSS-CUTTING-VERIFICATION)

- **Plan:** `strategic-reduction-2026`
- **Task:** T-W11-CROSS-CUTTING-VERIFICATION
- **Audit-Owner:** gem-reviewer
- **Datum:** 2026-05-04
- **Build-Status zum Audit-Zeitpunkt:** `dotnet build src/Romulus.sln` → Exit 0, 0 Warnungen, 0 Fehler
- **Verdict:** **PASS** — keine neuen P1-Findings, Plan kann auf `completed` geschlossen werden.

## Geltungsbereich

Audit gemaess DeepDivePrompts.md Sektion 11 ueber:

1. Preview / Execute / Report-Paritaet
2. GUI / CLI / API-Paritaet (Decision Explainer, Audit-Viewer, HealthScore, Provenance, Policy)
3. Single-Source-of-Truth (RunConfigurationDraftFingerprint, SafetyLaneProjection, RunViewModel.ApplyDashboard)
4. Schattenlogik-Freiheit
5. Determinismus
6. Safety / Audit-Atomicity

## 1) Preview / Execute / Report-Paritaet

| Aspekt | Single Source | Pin / Schutz |
|--------|---------------|--------------|
| Run-Result-Modell | `RunResult` in `Romulus.Contracts.Models` (inkl. `WinnerReasons`) | `Wave2RefactorRegressionTests`, `RunReportWriterTests` |
| Report-Schreibpfad | `RunReportWriter.WriteReport` | `ReportUnificationTests` (7 Pin-Tests) |
| Preview vs Execute Fingerprint | `RunConfigurationDraftFingerprint.Compute` mit explizitem `EXCLUSIONS`-Block (`AcceptDataLossToken` ausgenommen, weil Authorization, nicht Konfiguration) | `Compute_EveryDraftPropertyChangesFingerprint`, `Compute_AcceptDataLossToken_ExcludedFromHash` |
| Lossy-Conversion-Gate | `ConversionLossyBatchGate` (Pipeline) + `ConversionLossyGuiGate` (UI) lesen denselben Token | `ConversionLossyGuiGateTests` (12+2) |

**Befund:** kein Divergenz-Pfad. `IResultExportService` nutzt ausschliesslich `RunReportWriter` (Wave-5 Dual-Truth-Fallback wurde entfernt; `CanonicalChannel`-Konstante drift-geguarded).

## 2) GUI / CLI / API-Paritaet

| Feature | Zentrale Projection / Engine | GUI | CLI | API |
|---------|-----------------------------|-----|-----|-----|
| Decision Explainer | `DecisionExplainerProjection` | DecisionsView | `explain` | `/decisions` |
| Audit-Viewer | `IAuditViewerBackingService` | AuditViewerView | (read-only Adapter) | (gleicher Adapter) |
| HealthScore | `CollectionAnalysisService.CalculateHealthScore` + `CollectionRunSnapshotWriter.ComputePerConsoleHealth` | RunViewModel/Dashboard | `--health-snapshot` | `/collections/{root}/health` |
| Provenance | `ProvenanceTrailProjection.Project` | Provenance-Drawer (Decisions) | `provenance` | `/roms/{fingerprint}/provenance` |
| Policy | `PolicyEngine` (`Romulus.Core.Policy`) | PolicyGovernanceView (in MainWindow) | `validate-policy` | `/policies/validate` |
| Multi-DAT | `MultiDatConflictResolver` | DAT-Audit / Decision-View | (im Run-Output) | (im Run-Output) |
| Lossy-Conversion | `ConversionLossyBatchGate` + Token-Echo | DangerConfirm-Dialog | `--accept-data-loss` | `acceptDataLossToken` |

**Befund:** jede der oben gelisteten Funktionen hat genau eine fachliche Engine, die von allen drei Entry Points konsumiert wird. Pin-Tests existieren je Feature (siehe `Wave4*`, `Wave7*`, `Wave9*`, `Wave2RefactorRegression`, `ReportUnification`, `ConversionLossyGuiGate`).

## 3) Single-Source-of-Truth-Wahrung

| Wahrheit | Datei | Drift-Guard |
|---------|-------|-------------|
| Run-Konfigurations-Fingerprint | [RunConfigurationDraftFingerprint.cs](src/Romulus.UI.Wpf/Services/RunConfigurationDraftFingerprint.cs) | `WpfFingerprintAndI18nTests.Compute_EveryDraftPropertyChangesFingerprint` (`FingerprintExcludedProperties`-Set) |
| Safety-Lane-Routing (Blocked / Review / Unknown) | [SafetyLaneProjection.cs](src/Romulus.UI.Wpf/Services/SafetyLaneProjection.cs) | `Wave2RefactorRegressionTests.F10_*` |
| Dashboard-Schreibflaeche | [RunViewModel.ApplyDashboard](src/Romulus.UI.Wpf/ViewModels/RunViewModel.cs#L149) | `Wave2RefactorRegressionTests.F01_ApplyDashboard_AndReset_MutateAllDashboardCounters` |
| Report-Channel | [IResultExportService.CanonicalChannel](src/Romulus.UI.Wpf/Services/IResultExportService.cs) | `ReportUnificationTests` |

**Befund:** keine konkurrierende parallele Wahrheit identifizierbar. ReviewInbox liest aus `SafetyLaneProjection` (kein eigener Routing-Pfad). `CollectionRunSnapshotWriter` ist der einzige Persistenz-Pfad fuer HealthScore (kein Schatten-Writer).

## 4) Schattenlogik-Freiheit

Stichproben (alle in W1–W6 / W7 saniert):

- FeatureCommandService: 9 → 4 Partials (T-W6-CONSOLIDATE-FEATURE-SERVICES, completed)
- WPF-Code-Behind: keine Businesslogik in `MainWindow.xaml.cs` (Process-Lifecycle ueber `IApiProcessHost`)
- ToolRunnerAdapter: zentraler `_findToolCache` statt verstreuter Look-ups (F-11)
- Frontend-Export: vollstaendig entfernt (T-W1-FRONTEND-CULL); kein Code-/Daten-/Test-Rest

**Befund:** kein neuer Schattenpfad seit Phase-2-Gate (44/44 Gate-Tests gruen).

## 5) Determinismus

Geschuetzt durch bestehende Pins:

- `GameKeyNormalizer` — unveraendert
- `RegionDetector` — unveraendert
- `FormatScore` / `VersionScore` — unveraendert
- `DeduplicationEngine.SelectWinner` — unveraendert, jetzt zusaetzlich mit `WinnerReasonTrace`
- `MultiDatConflictResolver` — deterministisch nach `preferredSources` → Match-Strength → lex. Tiebreak (Pin-Tests `Romulus.Tests.MultiDatConflictResolverTests`)
- `EnrichmentPipelinePhase` — Hash- und Name-Match nutzen denselben Resolver (`EnrichmentDeterminismInvariant`-Pins)
- `RunConfigurationDraftFingerprint` — `PreferredDatSources` als idempotenter Bestandteil

**Befund:** kein neuer nicht-deterministischer Pfad eingefuehrt.

## 6) Safety / Audit-Atomicity

- `AllowedRootPathPolicy` — Root-Validation vor Move/Copy/Delete unveraendert
- `AuditCsvStore` + `AuditSigningService` — Hash-Kette unveraendert; `IAuditViewerBackingService` ist read-only
- `JsonlProvenanceStore` — append-only mit HMAC-Signatur; `ProvenanceTrailProjection` ist read-only
- `ConversionLossyBatchGate` — blockt lossy-Konversion ohne `AcceptDataLossToken`; UI zwingt zur verbatim-Eingabe des Tokens
- `CollectionRunSnapshotWriter.TryPersistAsync` — try/catch + onWarning, log-only (blockiert Run-Status nicht)

**Befund:** keine atomicity-Regression; Path-Traversal- und Zip-Slip-Schutz bleiben unveraendert in `Romulus.Infrastructure/Safety` verankert.

## Acceptance Criteria

- [x] **Audit-Bericht ohne neue P1-Findings** — keine identifiziert.
- [x] **Alle Pflicht-Invarianten geprueft** — Sektionen 1–6 oben.
- [x] **Jede neue Welle-Implementierung hat Paritaets-Tests** — Wave 4/5/7/8/9 jeweils mit eigenen `Wave*`-Pin-Suiten dokumentiert in `planning_history`.

## Folge-Aktionen

1. T-W11-CROSS-PLATFORM-DECISION: schliessen als `wontfix-with-reason` (Aktivierungsbedingung — Bedarfs-Beleg via T-W3-BETA-USERS / T-W4-DISCOVERY-LOOP — nicht erfuellt; Default-Ausgang im Task selbst dokumentiert).
2. T-W11-IDENTITY-GUARDRAIL: parallel umgesetzt (siehe `AGENTS.md`, `.claude/rules/project.instructions.md`, `.github/PULL_REQUEST_TEMPLATE.md`).
3. Plan-Top-Level-Status auf `completed` setzen.
