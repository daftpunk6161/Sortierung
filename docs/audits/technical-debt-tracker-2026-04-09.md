# Technical Debt Tracker - 2026-04-09

Status: active
Scope: src production code (excl. tests, excl. bin/obj)
Method: ripgrep + focused code review

## Tracking Rules

- Mark one item as in progress by changing `[ ]` to `[x] IN PROGRESS`.
- Mark done by changing to `[x] DONE` and adding date + short note.
- Keep acceptance criteria measurable.

## P1 - High Risk / Release Relevant

- [x] DONE TD-001 - DAT stale-threshold parity (single source of truth)
  Date: 2026-04-09
  Note: Unified stale-threshold usage to DatCatalogStateService.StaleThresholdDays across infrastructure analysis, API dashboard status, and WPF health/update messaging.
  Problem: Different stale thresholds are used across layers (365 vs 180 days), causing inconsistent behavior and messaging.
  Evidence: src/RomCleanup.UI.Wpf/Services/FeatureCommandService.Dat.cs:19, src/RomCleanup.Infrastructure/Analysis/DatAnalysisService.cs:233
  Target: Introduce one central threshold policy and consume it in UI/API/Infrastructure paths.
  Acceptance Criteria: Same DAT stale result for identical inputs in WPF and infrastructure helpers.

- [x] DONE TD-002 - Remove duplicate junk-rule definitions in WPF export path
  Date: 2026-04-09
  Note: Removed dead duplicate junk pattern constants from WPF export surface; FeatureService delegates to CollectionExportService as single source of truth.
  Problem: WPF contains local junk pattern tables while the logic is delegated to infrastructure, creating divergence risk.
  Evidence: src/RomCleanup.UI.Wpf/Services/FeatureService.Export.cs:23, src/RomCleanup.UI.Wpf/Services/FeatureService.Export.cs:56
  Target: Remove dead duplicate pattern definitions from WPF and keep only delegated central logic.
  Acceptance Criteria: Junk classification output is unchanged in regression tests.

- [x] DONE TD-003 - Sync-over-async reduction in CLI hot path
  Date: 2026-04-09
  Note: Converted CLI Main dispatch and async-capable subcommands to true async flow. Blocking hotspots in Program.cs reduced from 21 to 4 (remaining are test-compat wrappers and one snapshot persist bridge).
  Problem: High density of GetAwaiter().GetResult()/Wait() calls in CLI path can increase blocking/hang risk.
  Evidence: src/RomCleanup.CLI/Program.cs (20 matches via ripgrep pattern scan)
  Target: Refactor in small increments toward async flow boundaries.
  Acceptance Criteria: Existing CLI tests stay green; no behavior change in run/report outputs.

- [x] DONE TD-004 - Prevent internal exception detail leakage in API responses
  Date: 2026-04-09
  Note: DAT catalog load failure in API now logs full exception server-side and returns a client-safe generic error message.
  Problem: Some API error responses include raw exception messages.
  Evidence: src/RomCleanup.Api/Program.cs:1490
  Target: Return stable error codes and generic client-safe messages; keep detailed text only in internal logs.
  Acceptance Criteria: API responses contain no raw internal exception messages in covered error scenarios.

## P2 - Stability / Maintainability

- [ ] TD-005 - Narrow broad catch(Exception) blocks in top hotspots
  Problem: Broad catches reduce diagnosability and can hide important failures.
  Evidence: src/RomCleanup.Infrastructure/Orchestration/RunOrchestrator.PreviewAndPipelineHelpers.cs (9), src/RomCleanup.UI.Wpf/Services/FeatureCommandService.Security.cs (8), src/RomCleanup.Infrastructure/Audit/AuditSigningService.cs (7)
  Target: Replace with typed exceptions where possible, add structured logs, preserve deterministic fallback behavior.
  Acceptance Criteria: Negative path tests confirm expected status/log behavior.

- [ ] TD-006 - Break up oversized entry-point files (god-file reduction)
  Problem: Very large entry files increase regression risk and review complexity.
  Evidence: src/RomCleanup.Api/Program.cs (2179 lines), src/RomCleanup.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs (1741 lines), src/RomCleanup.CLI/Program.cs (1336 lines)
  Target: Extract cohesive handlers/services without duplicating business logic.
  Acceptance Criteria: No public behavior drift; existing integration tests remain green.

- [ ] TD-007 - Revisit warning suppressions (NU1701 and pragma)
  Problem: Broad suppression can hide compatibility issues.
  Evidence: src/RomCleanup.UI.Wpf/RomCleanup.UI.Wpf.csproj:12, src/RomCleanup.Infrastructure/Orchestration/GameKeyNormalizationModuleInit.cs:10
  Target: Document rationale and reduce suppression scope where feasible.
  Acceptance Criteria: Build remains green with equal or fewer suppressions.

## Quick Execution Plan

- [x] DONE Phase 1: TD-001 + TD-002 (single-source-of-truth cleanup)
- [x] IN PROGRESS Phase 2: TD-004 + TD-005 (error handling and security hardening)
- [ ] Phase 3: TD-003 + TD-006 + TD-007 (structural maintainability) - partially done (TD-003 complete)

## Verification Checklist (per phase)

- [x] DONE dotnet build src/RomCleanup.sln
- [x] DONE dotnet test src/RomCleanup.Tests/RomCleanup.Tests.csproj --no-build
- [ ] Preview/Execute/Report parity validated for touched areas
- [ ] No new shadow logic introduced
