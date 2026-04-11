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
  Evidence: src/Romulus.UI.Wpf/Services/FeatureCommandService.Dat.cs:19, src/Romulus.Infrastructure/Analysis/DatAnalysisService.cs:233
  Target: Introduce one central threshold policy and consume it in UI/API/Infrastructure paths.
  Acceptance Criteria: Same DAT stale result for identical inputs in WPF and infrastructure helpers.

- [x] DONE TD-002 - Remove duplicate junk-rule definitions in WPF export path
  Date: 2026-04-09
  Note: Removed dead duplicate junk pattern constants from WPF export surface; FeatureService delegates to CollectionExportService as single source of truth.
  Problem: WPF contains local junk pattern tables while the logic is delegated to infrastructure, creating divergence risk.
  Evidence: src/Romulus.UI.Wpf/Services/FeatureService.Export.cs:23, src/Romulus.UI.Wpf/Services/FeatureService.Export.cs:56
  Target: Remove dead duplicate pattern definitions from WPF and keep only delegated central logic.
  Acceptance Criteria: Junk classification output is unchanged in regression tests.

- [x] DONE TD-003 - Sync-over-async reduction in CLI hot path
  Date: 2026-04-09
  Note: Converted CLI Main dispatch and async-capable subcommands to true async flow. Blocking hotspots in Program.cs reduced from 21 to 4 (remaining are test-compat wrappers and one snapshot persist bridge).
  Problem: High density of GetAwaiter().GetResult()/Wait() calls in CLI path can increase blocking/hang risk.
  Evidence: src/Romulus.CLI/Program.cs (20 matches via ripgrep pattern scan)
  Target: Refactor in small increments toward async flow boundaries.
  Acceptance Criteria: Existing CLI tests stay green; no behavior change in run/report outputs.

- [x] DONE TD-004 - Prevent internal exception detail leakage in API responses
  Date: 2026-04-09
  Note: DAT catalog load failure in API now logs full exception server-side and returns a client-safe generic error message.
  Problem: Some API error responses include raw exception messages.
  Evidence: src/Romulus.Api/Program.cs:1490
  Target: Return stable error codes and generic client-safe messages; keep detailed text only in internal logs.
  Acceptance Criteria: API responses contain no raw internal exception messages in covered error scenarios.

## P2 - Stability / Maintainability

- [x] DONE TD-005 - Narrow broad catch(Exception) blocks in top hotspots
  Date: 2026-04-09
  Note: Replaced broad catches in RunOrchestrator report generation and WPF security command flows with typed exception filters for expected I/O/config/runtime failures, while preserving fallback/reporting behavior. Full test suite remains green (including negative/error paths).
  Problem: Broad catches reduce diagnosability and can hide important failures.
  Evidence: src/Romulus.Infrastructure/Orchestration/RunOrchestrator.PreviewAndPipelineHelpers.cs (9), src/Romulus.UI.Wpf/Services/FeatureCommandService.Security.cs (8), src/Romulus.Infrastructure/Audit/AuditSigningService.cs (7)
  Target: Replace with typed exceptions where possible, add structured logs, preserve deterministic fallback behavior.
  Acceptance Criteria: Negative path tests confirm expected status/log behavior.

- [x] DONE TD-006 - Break up oversized entry-point files (god-file reduction)
  Date: 2026-04-09
  Note: Extracted cohesive entry-point logic into dedicated files: CLI analysis/DAT subcommands into Program.Subcommands.AnalysisAndDat.cs, WPF watch/progress logic into MainViewModel.RunPipeline.WatchAndProgress.cs, and API run completeness/fixdat handlers into ProgramHelpers methods.
  Problem: Very large entry files increase regression risk and review complexity.
  Evidence: src/Romulus.Api/Program.cs (2179 lines), src/Romulus.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs (1741 lines), src/Romulus.CLI/Program.cs (1336 lines)
  Target: Extract cohesive handlers/services without duplicating business logic.
  Acceptance Criteria: No public behavior drift; existing integration tests remain green.

- [x] DONE TD-007 - Revisit warning suppressions (NU1701 and pragma)
  Date: 2026-04-09
  Note: Replaced CA2255 pragma suppression with a scoped SuppressMessage attribute on the module initializer and retained explicit project-scoped NU1701 suppression in the two affected projects because the upstream SkiaSharp.Views.WPF package currently restores via .NET Framework compatibility assets only.
  Problem: Broad suppression can hide compatibility issues.
  Evidence: src/Romulus.UI.Wpf/Romulus.UI.Wpf.csproj:12, src/Romulus.Infrastructure/Orchestration/GameKeyNormalizationModuleInit.cs:10
  Target: Document rationale and reduce suppression scope where feasible.
  Acceptance Criteria: Build remains green with equal or fewer suppressions.

## P1 - High Risk / Release Relevant (new findings)

- [ ] TD-008 - Competing status-string constant sources (3 parallel definitions)
  Problem: Run status strings defined in three independent locations creating divergence risk and naming inconsistency ("ok" vs "completed").
  Evidence: src/Romulus.Contracts/RunConstants.cs:67-79 (StatusOk="ok", StatusBlocked, StatusCancelled, StatusFailed), src/Romulus.Api/ApiRunStatus.cs:4-11 (Running="running", Completed="completed", CompletedWithErrors, Blocked, Cancelled, Failed), src/Romulus.Contracts/Models/OperationResult.cs:55-58 (StatusOk="ok", StatusBlocked="blocked")
  Target: Consolidate into one canonical set of status constants in Contracts. Remove ApiRunStatus and OperationResult duplicates.
  Acceptance Criteria: Single source of truth for all status strings; API/CLI/GUI reference only one set.

- [ ] TD-009 - Hardcoded status magic strings bypass existing constants
  Problem: Multiple locations use literal status strings instead of defined constants, risking typo-induced silent failures.
  Evidence: src/Romulus.Api/Program.cs (6x "running" literal), src/Romulus.Api/RunManager.cs:375 (Status != "running"), src/Romulus.UI.Wpf/Models/DashboardProjection.cs:151-152 ("cancelled"/"failed" literals), src/Romulus.UI.Wpf/Models/ErrorSummaryProjection.cs:45 ("blocked" literal), src/Romulus.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs:1180 ("cancelled" literal)
  Target: Replace all string literals with constant references.
  Acceptance Criteria: Zero hardcoded status string literals in production code (grep-verified).

- [ ] TD-010 - Identical 76-line code duplication in API error classification
  Problem: MapRunConfigurationError() and MapWatchConfigurationError() are identical except for "RUN-"/"WATCH-" prefix.
  Evidence: src/Romulus.Api/ProgramHelpers.cs:509-585 (both methods with 11 identical message.Contains() checks each)
  Target: Extract shared method with prefix parameter.
  Acceptance Criteria: Single error mapping method; both callers delegate to it.

- [ ] TD-011 - Fragile string-based error classification in API
  Problem: Error-code mapping relies on 11x message.Contains() matching exception text. If Infrastructure changes exception wording, classification breaks silently at runtime.
  Evidence: src/Romulus.Api/ProgramHelpers.cs:510-547 (Contains("protected system path"), Contains("drive root"), Contains("UNC path"), Contains("Invalid region"), etc.)
  Target: Replace with typed exceptions or error-code enums thrown by Infrastructure/Safety layer.
  Acceptance Criteria: Error classification uses type-safe patterns; no message.Contains()-based routing.

- [ ] TD-012 - I/O dependencies in Romulus.Core (architecture violation)
  Problem: Core contains direct System.IO defaults (File.Exists, FileStream, FileInfo, ZipFile.OpenRead) violating "pure domain logic, no I/O" rule. Delegate pattern makes it testable but doesn't remove the dependency.
  Evidence: src/Romulus.Core/SetParsing/SetParserIo.cs:9-10 (File.Exists, File.ReadLines defaults), src/Romulus.Core/Classification/ClassificationIo.cs:14-19 (File.Exists, FileStream, FileInfo.Length, File.GetAttributes, ZipFile.OpenRead defaults). Consumed by: DiscHeaderDetector (reads 128KB binary), CartridgeHeaderDetector (reads 512B header), 5 set parsers (M3u, Gdi, Cue, Ccd, Mds).
  Target: Either move I/O facades + detectors to Infrastructure, or inject I/O delegates via constructor instead of static defaults.
  Acceptance Criteria: Romulus.Core has zero direct System.IO references in production code paths.

- [ ] TD-013 - Hardcoded German strings in WPF dialog handlers (i18n bypass)
  Problem: 30+ user-facing German strings hardcoded in FeatureCommandService partials, bypassing the localization system entirely.
  Evidence: src/Romulus.UI.Wpf/Services/FeatureCommandService.Infra.cs:114 ("Hardlink-Modus", "Verfügbar"/"Nicht verfügbar", "Speicherplatz"), src/Romulus.UI.Wpf/Services/FeatureCommandService.Security.cs:113 ("Integritäts-Baseline erstellen oder prüfen?"), src/Romulus.UI.Wpf/Services/FeatureService.Export.cs:141-143 ("Benutzerdefinierte Regeln", "Erstelle Regeln mit Bedingungen"), src/Romulus.UI.Wpf/Services/FeatureCommandService.Dat.cs:276,327, src/Romulus.UI.Wpf/Services/FeatureCommandService.Workflow.cs:117,138,157
  Target: Replace all hardcoded strings with _vm.Loc[key] calls; add keys to de.json/en.json.
  Acceptance Criteria: Zero hardcoded user-facing German strings in FeatureCommandService*.cs files.

## P2 - Stability / Maintainability (new findings)

- [ ] TD-014 - RVZ magic-byte verification duplicated in two converters
  Problem: Identical RVZ magic byte check (R-V-Z-0x01) implemented in two separate files.
  Evidence: src/Romulus.Infrastructure/Conversion/ToolInvokerAdapter.cs:185-198 (VerifyRvz method), src/Romulus.Infrastructure/Conversion/ToolInvokers/DolphinToolInvoker.cs:92-103 (identical magic check inline)
  Target: Extract to shared static helper; both callers delegate.
  Acceptance Criteria: Single RVZ magic check method; zero duplication.

- [ ] TD-015 - Sort decision magic strings not centralized
  Problem: Sort decision strings ("Sort", "Review", "Blocked", "DatVerified") used as raw literals in sorting logic.
  Evidence: src/Romulus.Infrastructure/Sorting/ConsoleSorter.cs:63,150,212,271
  Target: Define constants in RunConstants.SortDecisions; replace all literals.
  Acceptance Criteria: Zero hardcoded sort-decision strings in ConsoleSorter.

- [ ] TD-016 - Sync-over-async remains (7 blocking hotspots)
  Problem: Remaining .GetAwaiter().GetResult() calls risk deadlocks.
  Evidence: src/Romulus.CLI/Program.cs:277,747,855 (3x), src/Romulus.CLI/CliOptionsMapper.cs:38,60 (2x), src/Romulus.Api/Program.cs:1916 (shutdown handler), src/Romulus.Api/RunManager.cs:126 (1x)
  Target: Convert to async or document why blocking is necessary at each site.
  Acceptance Criteria: Each remaining blocking call has a documented justification or is converted to async.

- [ ] TD-017 - DateTime.UtcNow used directly (30+ sites) without TimeProvider
  Problem: Direct DateTime.UtcNow calls violate "time behind testable abstraction" project rule.
  Evidence: src/Romulus.Api/RunLifecycleManager.cs (7x), src/Romulus.Api/RateLimiter.cs (2x), src/Romulus.Api/RunManager.cs (3x), src/Romulus.Api/Program.cs (10+x), src/Romulus.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs (4x)
  Target: Introduce TimeProvider (.NET 8+) abstraction; inject in API/CLI/WPF composition roots.
  Acceptance Criteria: Zero direct DateTime.UtcNow in new code; existing sites documented for migration.

- [ ] TD-018 - new HttpClient() directly instantiated (socket exhaustion risk)
  Problem: DatSourceService creates HttpClient directly instead of using IHttpClientFactory.
  Evidence: src/Romulus.Infrastructure/Dat/DatSourceService.cs:38
  Target: Accept HttpClient via constructor injection; wire via IHttpClientFactory in DI.
  Acceptance Criteria: No direct new HttpClient() in production code.

- [ ] TD-019 - Thread.Sleep in production code blocks threads
  Problem: Synchronous Thread.Sleep used in retry loops, potentially blocking UI thread in WPF.
  Evidence: src/Romulus.UI.Wpf/Services/ProfileService.cs:36,40 (2x, 25*attempt backoff), src/Romulus.Infrastructure/Configuration/SettingsFileAccess.cs:39
  Target: Replace with await Task.Delay() in async context.
  Acceptance Criteria: Zero Thread.Sleep in WPF production code.

- [ ] TD-020 - Empty catch(IOException) blocks suppress errors silently (10 sites)
  Problem: IOException caught and swallowed without any logging or diagnostics.
  Evidence: src/Romulus.Core/Classification/DiscHeaderDetector.cs:74,99 (2x), src/Romulus.Core/Classification/CartridgeHeaderDetector.cs:64,202 (2x), src/Romulus.Core/Classification/ConsoleDetector.cs:550, src/Romulus.Infrastructure/Deduplication/FolderDeduplicator.cs:67, src/Romulus.Infrastructure/Conversion/ConversionExecutor.cs:415,422 (2x), src/Romulus.UI.Wpf/ViewModels/DatCatalogViewModel.cs:210,367 (2x)
  Target: Add at minimum structured logging or comment explaining why suppression is intentional.
  Acceptance Criteria: Each empty catch has either a log statement or a documented suppression rationale.

- [ ] TD-021 - Hardcoded C:\tools\conversion fallback path
  Problem: Non-portable hardcoded Windows path as default tool root.
  Evidence: src/Romulus.Infrastructure/Tools/ToolRunnerAdapter.cs:20
  Target: Use relative path or settings-based default instead of absolute Windows path.
  Acceptance Criteria: No hardcoded absolute drive paths in Infrastructure code.

- [ ] TD-022 - Hardcoded dolphintool compression parameters
  Problem: Conversion compression arguments (zstd, level 5, block 131072) hardcoded in code instead of configuration.
  Evidence: src/Romulus.Infrastructure/Conversion/ToolInvokerAdapter.cs:133-142, src/Romulus.Infrastructure/Conversion/ToolInvokers/DolphinToolInvoker.cs:50
  Target: Move to conversion-registry.json or configurable defaults.
  Acceptance Criteria: Compression parameters configurable without code changes.

- [ ] TD-023 - CLI Help text uses magic number for stale-days default
  Problem: Help text hardcodes "365" instead of referencing DatCatalogStateService.StaleThresholdDays.
  Evidence: src/Romulus.CLI/CliOutputWriter.cs:185
  Target: Use string interpolation with the central constant.
  Acceptance Criteria: Help text default matches DatCatalogStateService.StaleThresholdDays at compile time.

## P3 - Low Risk / Hygiene (new findings)

- [ ] TD-024 - SHA1 as default hash algorithm (weak cryptographic default)
  Problem: FileHashService defaults to SHA1 when hash type is unspecified. SHA1 collision resistance is broken.
  Evidence: src/Romulus.Infrastructure/Hashing/FileHashService.cs:210 (_ => SHA1.Create())
  Target: Document SHA1 usage rationale (DAT/No-Intro compatibility requirement) or default to SHA256 for non-DAT paths.
  Acceptance Criteria: Default hash choice is documented with rationale; new non-DAT paths use SHA256.

- [ ] TD-025 - Conversion preview vs execute parity untested
  Problem: Preview/Execute parity tests exist for dedupe and sort but NOT for conversion estimates.
  Evidence: src/Romulus.Tests/PreviewExecuteParityTests.cs (covers GroupCount, WinnerCount, ConsoleSortResult but no ConvertedCount parity assertion)
  Target: Add DryRunAndMoveMode_KeepConversionEstimateParity() test.
  Acceptance Criteria: Test asserts conversion count parity between DryRun and Move modes.

- [ ] TD-026 - No cross-validation between consoles.json and conversion-registry.json
  Problem: Console keys in conversion-registry.json applicableConsoles are not validated against consoles.json at load time.
  Evidence: src/Romulus.Infrastructure/Conversion/ConversionRegistryLoader.cs:127,191
  Target: Add startup validation that all referenced console keys exist in consoles.json.
  Acceptance Criteria: Invalid console key in conversion-registry.json causes clear error at load time.

- [ ] TD-027 - AppStateStore.Get() returns mutable dictionary beyond lock scope
  Problem: Get() returns a new Dictionary copy that callers can mutate, weakening immutability contract.
  Evidence: src/Romulus.Infrastructure/State/AppStateStore.cs (Get() creates shallow copy as Dictionary)
  Target: Return IReadOnlyDictionary to enforce immutability contract at type level.
  Acceptance Criteria: Get() return type is IReadOnlyDictionary; callers compile without changes.

## P1 - Critical / Security (deep dive round 3)

- [ ] TD-028 - GameKeyNormalizer non-atomic volatile dual-field registration (race condition)
  Problem: Two volatile fields (_registeredPatterns, _registeredAliasMap) assigned sequentially inside lock. Reads outside lock see partially-initialized state: thread A writes _registeredPatterns, thread B reads patterns with stale null aliases. The lock protects writes but volatile does NOT provide atomicity across two fields.
  Evidence: src/Romulus.Core/GameKeys/GameKeyNormalizer.cs:27-45 (volatile + lock pattern), Normalize() at :107 reads both fields without lock
  Target: Replace two volatile fields with single volatile tuple field: _registeredState = (patterns, aliases). Read and write atomically.
  Acceptance Criteria: Single volatile field holds both patterns and aliases; no partial-state window possible.

- [ ] TD-029 - Region detection rules hardcoded in C# instead of data-driven (shadow logic)
  Problem: 25+ region regex rules in DefaultOrderedRules AND 100+ token-to-region mappings in RegionTokenMap are hardcoded in C# source. rules.json also defines region rules separately. Both must be maintained in sync – violation of single source of truth.
  Evidence: src/Romulus.Core/Regions/RegionDetector.cs:34-65 (DefaultOrderedRules, 25 hardcoded regexes), :226-287 (RegionTokenMap, 100+ hardcoded entries)
  Target: Load region rules and token map from rules.json at startup via Infrastructure, register like GameKeyNormalizer.
  Acceptance Criteria: RegionDetector has no hardcoded rule/map arrays; all region logic driven by rules.json.

- [ ] TD-030 - FormatScorer 40+ format scores hardcoded as C# switch statement
  Problem: Business-critical format scores (CHD=850, ISO=700, etc.) hardcoded in static switch. To adjust scores, code must recompile. These are business rules, not infrastructure.
  Evidence: src/Romulus.Core/Scoring/FormatScorer.cs:9-60 (DiscExtensions + GetFormatScore switch with 40+ entries)
  Target: Load format scores from data/conversion-registry.json or new data/format-scores.json. Inject into FormatScorer.
  Acceptance Criteria: Format scores configurable without code changes; zero hardcoded switch entries.

- [ ] TD-031 - DatSourceService ReadAsByteArrayAsync loads full response before size check
  Problem: ReadAsByteArrayAsync() loads entire response into memory before checking bytes.Length > MaxDownloadBytes. If server sends no Content-Length header, a 500MB response loads fully before rejection - OOM risk with malicious DAT servers.
  Evidence: src/Romulus.Infrastructure/Dat/DatSourceService.cs:112-113 (Content-Length check + ReadAsByteArrayAsync)
  Target: Use HttpCompletionOption.ResponseHeadersRead with streaming chunk reader + cumulative size guard.
  Acceptance Criteria: Download aborts early when cumulative bytes exceed MaxDownloadBytes; no full-response buffering.

- [ ] TD-032 - AuditSigningService HMAC key file left with loose permissions on failure
  Problem: If ACL restriction fails (IOException/UnauthorizedAccessException), key file is used with open permissions. On multi-user systems, other accounts could read the HMAC key and forge audit signatures.
  Evidence: src/Romulus.Infrastructure/Audit/AuditSigningService.cs:90-93 (catch swallows permission error, logs warning, continues using key)
  Target: If permission restriction fails, treat as security error: do not persist key, throw or return failure.
  Acceptance Criteria: HMAC key is never used without owner-only permissions.

- [ ] TD-033 - AuditSigningService rollback loads entire audit CSV into memory
  Problem: File.ReadAllLines() on audit CSV during rollback. Large collections can produce multi-GB audit files - OOM risk.
  Evidence: src/Romulus.Infrastructure/Audit/AuditSigningService.cs:268 (File.ReadAllLines)
  Target: Use StreamReader with line-by-line processing.
  Acceptance Criteria: Rollback processes audit CSV without loading entire file into memory.

## P2 - Stability / Maintainability (deep dive round 3)

- [ ] TD-034 - Language codes triple-hardcoded (RegionDetector + VersionScorer + rules.json)
  Problem: Same 40+ language codes appear in three separate locations. Adding a new language requires edits in all three places.
  Evidence: src/Romulus.Core/Regions/RegionDetector.cs:242-250 (LanguageCodes + EuLanguageCodes), src/Romulus.Core/Scoring/VersionScorer.cs:32-33 (langPattern regex with all codes), data/rules.json (language definitions)
  Target: Load language codes once from rules.json at startup; build regex and sets dynamically.
  Acceptance Criteria: Language codes defined in exactly one place (rules.json); no hardcoded code lists in C#.

- [ ] TD-035 - DeduplicationEngine BuildGroupKey uses "||" separator (collision risk)
  Problem: If a GameKey or ConsoleKey contains "||", the composite key becomes ambiguous. Example: ConsoleKey="PS1", GameKey="A||B" → "PS1||A||B" → wrong grouping.
  Evidence: src/Romulus.Core/Deduplication/DeduplicationEngine.cs:95-98 (BuildGroupKey with "||" separator)
  Target: Use tuple-based grouping or a separator guaranteed absent from keys (null char).
  Acceptance Criteria: Group key construction cannot produce ambiguous keys regardless of input content.

- [ ] TD-036 - WPF PropertyChanged event handler leaks on collection items
  Problem: 13 PropertyChanged subscriptions found but only 5 unsubscribes. Collection items (ExtensionFilterItem, ConsoleFilterItem) subscribe via += but are never unsubscribed before .Clear(). Memory leak accumulates over collection resets.
  Evidence: src/Romulus.UI.Wpf/ViewModels/MainViewModel.cs:379,489 (+= OnExtensionCheckedChanged, += OnConsoleCheckedChanged), src/Romulus.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs:1417 (+= OnExtensionFilterChanged). No corresponding -= before Clear().
  Target: Unsubscribe all item handlers before clearing collections.
  Acceptance Criteria: Every PropertyChanged += has corresponding -= before collection Clear/Replace.

- [ ] TD-037 - HealthScorer magic numbers not configurable (business KPI weights in code)
  Problem: Health score formula uses 8 hardcoded magic numbers (30.0, 0.3, 90.0, 70.0, 10.0, 0.15, 20.0, 2.0) without documentation of rationale. KPI weights are business logic, not code logic.
  Evidence: src/Romulus.Core/Scoring/HealthScorer.cs:11-16 (all weights hardcoded)
  Target: Move scoring weights to data/defaults.json; document formula rationale.
  Acceptance Criteria: Health score weights configurable from data file; formula documented with rationale.

- [ ] TD-038 - DeduplicationEngine category rank hardcoded (business rule in code)
  Problem: FileCategory priority ranks (Game=5, Bios=4, NonGame=3, Junk=2, Unknown=1) are hardcoded magic numbers. Priority order is a business rule.
  Evidence: src/Romulus.Core/Deduplication/DeduplicationEngine.cs:51-59 (GetCategoryRank switch)
  Target: Move to data/defaults.json or inject as dependency.
  Acceptance Criteria: Category priority configurable without code changes.

- [ ] TD-039 - SafetyValidator IsProtectedSystemPath allocates array on every call
  Problem: IsProtectedSystemPath creates a new string[] of SpecialFolder paths on every invocation. Called frequently during move operations, creating unnecessary GC pressure.
  Evidence: src/Romulus.Infrastructure/Safety/SafetyValidator.cs:340-351 (new[] { ... }.Where().ToArray() on every call)
  Target: Cache protected roots as static readonly field, computed once.
  Acceptance Criteria: Protected system roots computed once; zero per-call allocations.

- [ ] TD-040 - SafetyValidator EnsureNoReparsePointInExistingAncestry no max depth guard
  Problem: Walk-up loop from file to drive root has no max-depth limit. Deeply nested malformed paths could cause excessive iterations.
  Evidence: src/Romulus.Infrastructure/Safety/SafetyValidator.cs:418-459 (while loop without max iterations)
  Target: Add max depth counter (e.g., 256 levels) and break with error.
  Acceptance Criteria: Ancestry walk terminates after reasonable max depth.

- [ ] TD-041 - Child ViewModels created inline without DI container
  Problem: MainViewModel constructor creates 6+ child ViewModels directly instead of resolving from container. Tight coupling; child VMs can't receive injected services without changing constructor.
  Evidence: src/Romulus.UI.Wpf/ViewModels/MainViewModel.cs:87-98 (new ShellViewModel, new SetupViewModel, new ToolsViewModel, etc.)
  Target: Register child ViewModels in DI container; inject into MainViewModel.
  Acceptance Criteria: Child ViewModels resolved from DI; testable independently.

- [ ] TD-042 - MovePipelinePhase rollback failure only logged as WARNING
  Problem: If a rollback of a set member move fails, only a WARNING is logged. User has partially-moved files with no clear error. Run continues as if OK.
  Evidence: src/Romulus.Infrastructure/Orchestration/MovePipelinePhase.cs:230-280 (set member rollback loop, warning-only on failure)
  Target: Escalate rollback failure to FAILURE status; abort run if rollback cannot restore consistent state.
  Acceptance Criteria: Failed rollback produces FAILURE, not WARNING.

- [ ] TD-043 - No JSON schema validation for data/*.json files at startup
  Problem: Data files loaded without schema validation. Malformed or incomplete data files (missing fields, wrong types) cause hard-to-debug runtime errors instead of clear startup failures.
  Evidence: data/consoles.json, data/rules.json, data/defaults.json, data/conversion-registry.json, data/dat-catalog.json – all loaded without structural validation
  Target: Create JSON schemas (data/schemas/*.json); validate on startup.
  Acceptance Criteria: Malformed data files produce clear startup error with file + field identification.

- [ ] TD-044 - SettingsFileAccess exponential backoff can block up to ~3s without global timeout
  Problem: 8 attempts with exponential backoff (25, 50, 100, ..., 3200 ms total) without absolute timeout. In WPF, if called on UI thread, blocks for seconds.
  Evidence: src/Romulus.Infrastructure/Configuration/SettingsFileAccess.cs:10-30 (DefaultMaxAttempts=8, InitialDelayMs=25, doubling)
  Target: Add totalTimeoutMs parameter; abort early if exceeded.
  Acceptance Criteria: Settings read never blocks longer than specified timeout.

- [ ] TD-045 - Test stubs with empty no-op implementations (alibi mocks)
  Problem: Test mocks with empty method bodies provide no behavior tracking. Tests pass even when ViewModel forgets to call required services.
  Evidence: src/Romulus.Tests/GuiViewModelTests.cs:2632-2700 (StubDialogService with empty Info(), ShowText(), etc.)
  Target: Add call tracking (CallCount, LastArgs) to test stubs; assert interactions.
  Acceptance Criteria: Test stubs record invocations; tests verify expected service calls.

- [ ] TD-046 - StreamingScanPipelinePhase swallows async iterator exceptions
  Problem: If EnumerateFilesAsync throws (e.g., UnauthorizedAccessException on a directory), the iterator may stop silently. Scan is incomplete but no error propagated.
  Evidence: src/Romulus.Infrastructure/Orchestration/StreamingScanPipelinePhase.cs:90-110
  Target: Collect and surface iterator exceptions; report incomplete scan.
  Acceptance Criteria: Scan reports errors from inaccessible directories; user sees incomplete-scan warning.

- [ ] TD-047 - UkPattern duplicates DefaultOrderedRules UK detection
  Problem: UK is detected twice – once via standalone UkPattern and once as part of DefaultOrderedRules EU pattern. Creates implicit priority not documented.
  Evidence: src/Romulus.Core/Regions/RegionDetector.cs:152-156 (UkPattern), :57 (UK in DefaultOrderedRules EU entry)
  Target: Remove separate UkPattern; handle UK as first-priority entry in DefaultOrderedRules.
  Acceptance Criteria: UK detection via single code path; no implicit priority.

## P3 - Low Risk / Hygiene (deep dive round 3)

- [ ] TD-048 - GameKeyNormalizer DOS metadata loop hardcoded limit of 20
  Problem: MsDosTrailingParen stripping loop limited to 20 iterations. Filenames with >20 nested paren groups silently stop normalizing. No logging when limit hit.
  Evidence: src/Romulus.Core/GameKeys/GameKeyNormalizer.cs:310-316 (for i < 20)
  Target: Increase limit or use comprehensive regex; add warning log when limit approached.
  Acceptance Criteria: Documented limit with logging when approached.

- [ ] TD-049 - VersionScorer truncates at 6 version segments without logging
  Problem: Version numbers with >6 segments silently truncated. Score penalty for overflow is minuscule (+1 per extra segment).
  Evidence: src/Romulus.Core/Scoring/VersionScorer.cs:101-117 (const maxSegments = 6, wasTruncated)
  Target: Make maxSegments configurable; log warning when truncation occurs.
  Acceptance Criteria: Version truncation logged; configurable max.

- [ ] TD-050 - FormatScorer GetRegionScore O(n) linear search
  Problem: Linear search through preferOrder array per candidate comparison. Low-latency path for large collections.
  Evidence: src/Romulus.Core/Scoring/FormatScorer.cs:68-79 (for loop through preferOrder)
  Target: Convert preferOrder to Dictionary<string,int> for O(1) lookup.
  Acceptance Criteria: Region score lookup is O(1).

- [ ] TD-051 - AllowedRootPathPolicy catch(Exception) too broad
  Problem: Catches all Exceptions including OutOfMemoryException silently.
  Evidence: src/Romulus.Infrastructure/Safety/AllowedRootPathPolicy.cs:29-33
  Target: Narrow to ArgumentException, PathTooLongException, NotSupportedException.
  Acceptance Criteria: Only expected path exceptions caught.

- [ ] TD-052 - SetupViewModel/MainViewModel property duplication with manual sync
  Problem: TrashRoot, DatRoot, ToolChdman etc. exist in both ViewModels with fragile SyncToSetup() calls.
  Evidence: src/Romulus.UI.Wpf/ViewModels/MainViewModel.Settings.cs:68+ (property setters call SyncToSetup), src/Romulus.UI.Wpf/ViewModels/SetupViewModel.cs:32+
  Target: SetupViewModel should project from MainViewModel state; not duplicate properties.
  Acceptance Criteria: Settings properties exist in one ViewModel only.

- [ ] TD-053 - DatAuditPipelinePhase ToConfidence default case returns 60 (no exhaustive match)
  Problem: New DatAuditStatus enum values fall silently into default case with score 60. No compiler warning for non-exhaustive match.
  Evidence: src/Romulus.Infrastructure/Orchestration/DatAuditPipelinePhase.cs:70-88 (_ => 60)
  Target: Replace default with throw new InvalidOperationException for unexpected status values.
  Acceptance Criteria: Unknown status values throw at runtime instead of silently scoring 60.

- [ ] TD-054 - SettingsLoader MergeFromDefaults swallows JsonException silently
  Problem: If defaults.json has malformed structure, catch(JsonException) uses hardcoded defaults without any warning.
  Evidence: src/Romulus.Infrastructure/Configuration/SettingsLoader.cs:160-200
  Target: Log warning when defaults.json parsing fails.
  Acceptance Criteria: Malformed defaults.json produces visible warning.

## P1 - High Risk (final hygiene scan round 4)

- [ ] TD-055 - Diverging disc extension definitions between Core and Infrastructure
  Problem: Two incompatible DiscExtensions HashSets define different format sets. Core (FormatScorer) has 26 extensions including Nintendo formats (.nsp, .xci, .nsz, .xcz, .wud, .wux) and playlist (.m3u). Infrastructure (ExecutionHelpers) has 18 extensions including archives (.zip, .7z) and .sub/.ecm. 13 formats differ between the sets. This means Core scores files that Infrastructure doesn't recognize as disc formats, and vice versa.
  Evidence: src/Romulus.Core/Scoring/FormatScorer.cs:15-19 (26 extensions), src/Romulus.Infrastructure/Orchestration/ExecutionHelpers.cs:14-20 (18 extensions)
  Target: Consolidate into one canonical disc extension set in Contracts; both Core and Infrastructure reference the same source.
  Acceptance Criteria: Single disc extension definition; Core and Infrastructure agree on all disc formats.

- [ ] TD-056 - Phase name magic strings not centralized (9 prefixes across 5+ files)
  Problem: Pipeline phase prefixes ([Preflight], [Scan], [Filter], [Dedupe], [Junk], [Move], [Sort], [Convert], [Report], [Fertig]) are hardcoded as string literals across orchestration and WPF layers. PipelinePhaseWeights.cs defines tuples but values are not shared as reusable string constants. Changing a phase name requires edits in 5+ files.
  Evidence: src/Romulus.UI.Wpf/ViewModels/MainViewModel.RunPipeline.WatchAndProgress.cs:440-461, src/Romulus.Infrastructure/Orchestration/RunOrchestrator.StandardPhaseSteps.cs:82-221, src/Romulus.Infrastructure/Orchestration/RunOrchestrator.ScanAndConvertSteps.cs:19-74, src/Romulus.Infrastructure/Orchestration/WinnerConversionPipelinePhase.cs:19-46, src/Romulus.Infrastructure/Orchestration/StreamingScanPipelinePhase.cs:34-65, src/Romulus.Contracts/PipelinePhaseWeights.cs:13-21
  Target: Extract phase prefix constants to Contracts (e.g., RunConstants.Phases); all sites reference constants.
  Acceptance Criteria: Zero hardcoded phase prefix literals in production code; all reference central constants.

## P2 - Stability / Maintainability (final hygiene scan round 4)

- [ ] TD-057 - Inline disc extension checks scattered across 11+ files (not using centralized set)
  Problem: Hardcoded `is ".chd" or ".iso" or ...` patterns in 11+ production files, each checking a different subset of disc formats. None reference FormatScorer.IsDiscExtension() or ExecutionHelpers.GetDiscExtensions(). Each site independently decides which formats count as "disc" - a maintenance nightmare and inconsistency source.
  Evidence: src/Romulus.UI.Wpf/ViewModels/MainViewModel.Productization.cs:607 (9 formats), src/Romulus.UI.Wpf/Services/FeatureCommandService.cs:654 (5 formats), src/Romulus.UI.Wpf/Services/FeatureCommandService.Conversion.cs:71 (3 formats), src/Romulus.Core/Classification/ConsoleDetector.cs:353,420,479 (4 formats x3), src/Romulus.Core/Classification/DiscHeaderDetector.cs:140 (4 formats), src/Romulus.Infrastructure/Dat/FamilyDatStrategyResolver.cs:88 (6 formats), src/Romulus.Infrastructure/Dat/DiscDatStrategy.cs:17 (7 formats), src/Romulus.Infrastructure/Conversion/FormatConverterAdapter.cs:553 (6 formats), src/Romulus.Infrastructure/Conversion/ChdmanToolConverter.cs:45 (3 formats), src/Romulus.Infrastructure/Orchestration/EnrichmentPipelinePhase.cs:541 (6 formats)
  Target: Replace inline checks with IsDiscExtension() or subset helper from canonical set. Document intentional subset restrictions where needed.
  Acceptance Criteria: No inline disc extension patterns; all checks reference central method or documented subset.

- [ ] TD-058 - Hardcoded German strings in Infrastructure orchestration/reporting bypass i18n (30+ instances)
  Problem: 30+ German progress/log messages hardcoded inline across RunOrchestrator partials, pipeline phases, WatchFolderService, and ReportGenerator. These bypass RunProgressLocalization and the i18n system entirely. Examples: "Dateien sammeln", "Fortschritt", "Verschiebe", "Analyse übersprungen", "Duplikate", "Konvertierung", "abgeschlossen", "FileSystemWatcher-Fehler".
  Evidence: src/Romulus.Infrastructure/Orchestration/RunOrchestrator.StandardPhaseSteps.cs:82-221, src/Romulus.Infrastructure/Orchestration/RunOrchestrator.ScanAndConvertSteps.cs:19-74, src/Romulus.Infrastructure/Orchestration/RunOrchestrator.PreviewAndPipelineHelpers.cs:33-332, src/Romulus.Infrastructure/Orchestration/MovePipelinePhase.cs:34-331, src/Romulus.Infrastructure/Orchestration/StreamingScanPipelinePhase.cs:34-65, src/Romulus.Infrastructure/Orchestration/WinnerConversionPipelinePhase.cs:19-46, src/Romulus.Infrastructure/Watch/WatchFolderService.cs:96-169, src/Romulus.Infrastructure/Reporting/ReportGenerator.cs:300-350
  Target: Route all user-/operator-visible messages through RunProgressLocalization or i18n keys.
  Acceptance Criteria: Infrastructure progress messages localized; no hardcoded German in user-facing output.

## P3 - Low Risk / Hygiene (final hygiene scan round 4)

- [ ] TD-059 - FormatFixDatReport() in DatAnalysisService unused in production
  Problem: Public method FormatFixDatReport(FixDatResult) exists in DatAnalysisService but is only called from test code, never from any production path (CLI, API, GUI).
  Evidence: src/Romulus.Infrastructure/Analysis/DatAnalysisService.cs:161, src/Romulus.Tests/DatAnalysisServiceCoverageTests.cs:313 (only caller)
  Target: Either integrate into a production report path or mark internal/remove.
  Acceptance Criteria: Method is either production-integrated or removed.

- [ ] TD-060 - German text in ReportGenerator HTML output hardcoded
  Problem: HTML reports contain hardcoded German text ("Convert-Fehler", "Fehler", "Datei(en)", "UNKNOWN bedeutet...", "Mögliche Ursachen...") instead of localized strings.
  Evidence: src/Romulus.Infrastructure/Reporting/ReportGenerator.cs:300,308,348-350
  Target: Replace with locale-aware report strings from i18n data or a report localization provider.
  Acceptance Criteria: HTML report text uses localized strings; no hardcoded German.

## Positive Findings (verified safe)

- HTML reports: All user data properly escaped via WebUtility.HtmlEncode (ReportGenerator.cs)
- CSV injection: Formula prefixes (=,+,-,@) sanitized via AuditCsvParser.SanitizeCsvField()
- Path traversal: SafetyValidator blocks extended-length paths, NTFS ADS, reparse points, trailing dots/spaces
- Hash comparison: CryptographicOperations.FixedTimeEquals used correctly (no timing attacks)
- Cancellation: CancellationToken properly propagated through all orchestrator phases
- Partial audit: Sidecar written on mid-run cancellation for rollback recovery
- Determinism: .OrderBy(StringComparer.OrdinalIgnoreCase) used consistently to prevent Dictionary iteration issues
- Undo stack: Properly bounded to 100 entries in AppStateStore
- TOCTOU: No race conditions found in path validation – paths normalized once then reused
- Zip-Slip: DatSourceService validates extracted paths against normalizedExtractRoot before extraction
- SEC-DEDUP: Cross-group winner/loser collision correctly filtered with OrdinalIgnoreCase
- DeduplicationEngine: Deterministic alphabetical MainPath tiebreaker (BUG-011 fix verified)
- AuditCsvStore: FileLocks ConcurrentDictionary correctly uses OrdinalIgnoreCase comparer
- SafetyValidator: NormalizePath correctly rejects device paths (\\?\, \\.\), ADS, trailing dots/spaces
- EnsureNoReparsePointInExistingAncestry: Walk-up loop terminates at drive root with proper equality checks

## Quick Execution Plan

- [x] DONE Phase 1: TD-001 + TD-002 (single-source-of-truth cleanup)
- [x] DONE Phase 2: TD-004 + TD-005 (error handling and security hardening)
- [x] DONE Phase 3: TD-003 + TD-006 + TD-007 (structural maintainability)
- [ ] Phase 4: TD-008 + TD-009 + TD-015 (status string consolidation)
- [ ] Phase 5: TD-010 + TD-011 (API error classification refactor)
- [ ] Phase 6: TD-012 + TD-013 (Core I/O violation + i18n hygiene)
- [ ] Phase 7: TD-014 + TD-022 (conversion pipeline cleanup)
- [ ] Phase 8: TD-016 + TD-017 + TD-018 + TD-019 (async/time/http modernization)
- [ ] Phase 9: TD-020 + TD-021 + TD-023 (minor hardening)
- [ ] Phase 10: TD-024 + TD-025 + TD-026 + TD-027 (P3 hygiene round 2)
- [ ] Phase 11: TD-028 + TD-032 + TD-033 (critical security + race condition)
- [ ] Phase 12: TD-029 + TD-030 + TD-034 (data-driven migration: regions, formats, languages)
- [ ] Phase 13: TD-031 + TD-044 + TD-046 (streaming + timeout hardening)
- [ ] Phase 14: TD-035 + TD-036 + TD-041 + TD-042 (dedup safety + WPF lifecycle)
- [ ] Phase 15: TD-037 + TD-038 + TD-039 + TD-043 (config-driven scoring + schema validation)
- [ ] Phase 16: TD-045 + TD-047 + TD-048-TD-054 (test quality + P3 hygiene round 3)
- [ ] Phase 17: TD-055 + TD-057 (disc extension consolidation - single source of truth)
- [ ] Phase 18: TD-056 (pipeline phase name constants)
- [ ] Phase 19: TD-058 + TD-060 (Infrastructure/Report i18n remaining German strings)
- [ ] Phase 20: TD-059 (dead production method cleanup)

## Verified Clean Areas (round 4 hygiene scan)

- DI registrations: all services in SharedServiceRegistration and App.xaml.cs are actively injected
- Contracts interfaces: all defined interfaces in Contracts/Ports have implementations and callers
- XAML resources: all x:Key styles and templates actively referenced via StaticResource/DynamicResource
- data/defaults.json: all setting keys actively consumed by CLI, API, and WPF code paths
- data/tool-hashes.json: actively used in ToolRunnerAdapter, RunEnvironmentBuilder, and tests (14 references)
- WatchService.Dispose: properly implemented with event unsubscribe + inner disposal
- ResolveCorsOrigin: actively used in both ProgramHelpers and HeadlessApiOptions (12 callers)
- i18n keys: Tools.CountFormat present in all 3 language files (de, en, fr)

## Verification Checklist (per phase)

- [x] DONE dotnet build src/Romulus.sln
- [x] DONE dotnet test src/Romulus.Tests/Romulus.Tests.csproj --no-build
- [x] DONE Preview/Execute/Report parity validated for touched areas
- [x] DONE No new shadow logic introduced
