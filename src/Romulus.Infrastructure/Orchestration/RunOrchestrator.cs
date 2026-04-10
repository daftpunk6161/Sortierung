using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Classification;
using Romulus.Core.Deduplication;
using Romulus.Infrastructure.Hashing;
using Romulus.Infrastructure.Index;
using Romulus.Infrastructure.Deduplication;
using Romulus.Infrastructure.Linking;
using Romulus.Infrastructure.Metrics;
using Romulus.Infrastructure.Quarantine;
using Romulus.Infrastructure.Review;
using Romulus.Infrastructure.Reporting;
using Romulus.Infrastructure.Sorting;

namespace Romulus.Infrastructure.Orchestration;

/// <summary>
/// Central pipeline orchestrator for ROM cleanup operations.
/// Port of Invoke-CliRunAdapter / RunHelpers.Execution.ps1.
/// Encapsulates: Preflight → Scan → Dedupe → JunkRemoval → Move → Sort → Convert → Report.
/// Streaming seam: scan/enrichment use IAsyncEnumerable in orchestrator partial steps.
/// Used by both CLI and API entry points.
/// </summary>
public sealed partial class RunOrchestrator : IDisposable
{
    private readonly IFileSystem _fs;
    private readonly IAuditStore _audit;
    private readonly ConsoleDetector? _consoleDetector;
    private readonly FileHashService? _hashService;
    private readonly Contracts.Ports.ICollectionIndex? _collectionIndex;
    private readonly ArchiveHashService? _archiveHashService;
    private readonly IFormatConverter? _converter;
    private readonly DatIndex? _datIndex;
    private readonly Action<string>? _onProgress;
    private readonly IPhasePlanBuilder _phasePlanBuilder;
    private readonly Contracts.Ports.IHeaderlessHasher? _headerlessHasher;
    private readonly IReadOnlySet<string>? _knownBiosHashes;
    private readonly string? _enrichmentFingerprint;
    private readonly PersistedReviewDecisionService? _reviewDecisionService;
    private readonly IFamilyDatStrategyResolver? _familyDatStrategyResolver;
    private readonly IFamilyPipelineSelector? _familyPipelineSelector;
    private bool _disposed;

    public RunOrchestrator(
        IFileSystem fs,
        IAuditStore audit,
        ConsoleDetector? consoleDetector = null,
        FileHashService? hashService = null,
        IFormatConverter? converter = null,
        DatIndex? datIndex = null,
        Action<string>? onProgress = null,
        IPhasePlanBuilder? phasePlanBuilder = null,
        ArchiveHashService? archiveHashService = null,
        Contracts.Ports.IHeaderlessHasher? headerlessHasher = null,
        IReadOnlySet<string>? knownBiosHashes = null,
        Contracts.Ports.ICollectionIndex? collectionIndex = null,
        string? enrichmentFingerprint = null,
        PersistedReviewDecisionService? reviewDecisionService = null,
        IFamilyDatStrategyResolver? familyDatStrategyResolver = null,
        IFamilyPipelineSelector? familyPipelineSelector = null)
    {
        _fs = fs;
        _audit = audit;
        _consoleDetector = consoleDetector;
        _hashService = hashService;
        _collectionIndex = collectionIndex ?? hashService?.CollectionIndex;
        _archiveHashService = archiveHashService;
        _converter = converter;
        _datIndex = datIndex;
        _onProgress = onProgress;
        _phasePlanBuilder = phasePlanBuilder ?? new PhasePlanBuilder();
        _headerlessHasher = headerlessHasher;
        _knownBiosHashes = knownBiosHashes;
        _enrichmentFingerprint = string.IsNullOrWhiteSpace(enrichmentFingerprint) ? null : enrichmentFingerprint;
        _reviewDecisionService = reviewDecisionService;
        _familyDatStrategyResolver = familyDatStrategyResolver;
        _familyPipelineSelector = familyPipelineSelector;
    }

    /// <summary>
    /// Validate all prerequisites before a run.
    /// Port of Invoke-RunPreflight from RunHelpers.Execution.ps1.
    /// Checks: roots exist, audit dir writable (F-06), tools available.
    /// </summary>
    public OperationResult Preflight(RunOptions options)
    {
        var warnings = new List<string>();
        var validationErrors = RunOptionsBuilder.Validate(options);

        if (validationErrors.Count > 0)
            return OperationResult.Blocked(string.Join(" ", validationErrors));

        if (options.Roots.Count == 0)
            return OperationResult.Blocked("No roots specified");

        foreach (var root in options.Roots)
        {
            if (!_fs.TestPath(root, "Container"))
                return OperationResult.Blocked($"Root does not exist: {root}");
        }

        if (!TryValidateWritablePath(options.AuditPath, treatAsDirectory: false, out var auditWriteError))
            return OperationResult.Blocked($"Audit directory not writable: {auditWriteError}");

        if (!TryValidateWritablePath(options.TrashRoot, treatAsDirectory: true, out var trashWriteError))
            return OperationResult.Blocked($"trashRoot not writable: {trashWriteError}");

        if (!TryValidateWritablePath(options.ReportPath, treatAsDirectory: false, out var reportWriteError))
            warnings.Add($"reportPath not writable: {reportWriteError}. A fallback report path may be used.");

        if (!string.IsNullOrWhiteSpace(options.ConvertFormat))
        {
            if (_converter is null)
            {
                warnings.Add("ConvertFormat is set but no converter is configured.");
            }
            else
            {
                var missingTools = _converter.GetMissingToolsForFormat(options.ConvertFormat);
                if (missingTools.Count > 0)
                {
                    return OperationResult.Blocked(
                        $"Conversion tools unavailable for format '{options.ConvertFormat}': {string.Join(", ", missingTools)}");
                }
            }
        }

        if (options.EnableDat && _datIndex is null)
            warnings.Add("DAT enabled but no DatIndex loaded");

        if (options.Extensions.Count == 0)
            warnings.Add("No file extensions specified — scan will find nothing");

        // TASK-163: DryRun + Move-only feature warnings
        warnings.AddRange(RunOptionsBuilder.GetDryRunFeatureWarnings(options));

        var result = OperationResult.Ok("preflight-passed");
        result.Warnings.AddRange(warnings);
        result.Meta["RootCount"] = options.Roots.Count;
        result.Meta["ExtensionCount"] = options.Extensions.Count;
        return result;
    }

    private bool TryValidateWritablePath(string? configuredPath, bool treatAsDirectory, out string reason)
    {
        reason = string.Empty;

        if (string.IsNullOrWhiteSpace(configuredPath))
            return true;

        string targetDirectory;
        try
        {
            var normalized = Path.GetFullPath(configuredPath);
            targetDirectory = treatAsDirectory
                ? normalized
                : (Path.GetDirectoryName(normalized) ?? string.Empty);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            reason = ex.Message;
            return false;
        }

        if (string.IsNullOrWhiteSpace(targetDirectory))
            return true;

        try
        {
            _fs.EnsureDirectory(targetDirectory);
            var probePath = Path.Combine(targetDirectory, $".write_test_{Guid.NewGuid():N}");
            File.WriteAllText(probePath, string.Empty);
            File.Delete(probePath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            reason = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Execute the full pipeline: Scan → Dedupe → JunkRemoval → Move → Sort → (optional Convert) → Report.
    /// </summary>
    public RunResult Execute(RunOptions options, CancellationToken cancellationToken = default)
        // Do not pass the caller token into Task.Run itself: if the token is already cancelled,
        // Task.Run would short-circuit before ExecuteAsync can translate the run into a
        // stable "cancelled" result with partial artifacts/sidecars.
        => Task.Run(() => ExecuteAsync(options, cancellationToken)).GetAwaiter().GetResult();

    public async Task<RunResult> ExecuteAsync(RunOptions options, CancellationToken cancellationToken = default)
    {
        var result = new RunResultBuilder();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var pipelineState = new PipelineState();

        // V2-M09: Structured phase metrics collection
        var metrics = new PhaseMetricsCollector();
        metrics.Initialize();

        try
        {

        // Phase 1: Preflight
        metrics.StartPhase("Preflight");
        _onProgress?.Invoke(RunProgressLocalization.Format(
            "Preflight.Checking",
            "[Preflight] Voraussetzungen prüfen…"));
        var preflight = Preflight(options);
        result.Preflight = preflight;
        metrics.CompletePhase();
        if (preflight.ShouldReturn)
        {
            _onProgress?.Invoke(RunProgressLocalization.Format(
                "Preflight.Blocked",
                "[Preflight] Blockiert: {0}",
                preflight.Reason ?? string.Empty));
            result.Status = RunOutcome.Blocked.ToStatusString();
            result.ExitCode = RunOutcome.Blocked.ToExitCode();
            return result.Build();
        }
        _onProgress?.Invoke(RunProgressLocalization.Format(
            "Preflight.Ok",
            "[Preflight] OK — {0} Root(s), {1} Extension(s), Modus: {2}",
            options.Roots.Count,
            options.Extensions.Count,
            options.Mode));
        if (preflight.Warnings.Count > 0)
            foreach (var w in preflight.Warnings)
                _onProgress?.Invoke(RunProgressLocalization.Format(
                    "Preflight.Warning",
                    "[Preflight] Warnung: {0}",
                    w));

        cancellationToken.ThrowIfCancellationRequested();

        var scanResult = await RunScanAndPrepareStateAsync(options, result, metrics, pipelineState, cancellationToken);
        var candidates = scanResult.AllCandidates;
        var processingCandidates = scanResult.ProcessingCandidates;

        if (processingCandidates.Count == 0)
        {
            // Ensure a mid-scan cancel that left 0 processing candidates uses the cancel path.
            cancellationToken.ThrowIfCancellationRequested();

            result.AllCandidates = candidates;
            return FinalizeCompletedRun(options, result, pipelineState, metrics, sw, cancellationToken);
        }

        // Integrate deferred services in a non-destructive analysis pass.
        new DeferredAnalysisPhaseStep((state, ct) => ExecuteDeferredServiceAnalysis(state, options, ct))
            .Execute(pipelineState, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        if (!TryExecuteConvertOnlyPath(options, candidates, processingCandidates, result, metrics, cancellationToken, pipelineState))
        {
            var phasePlan = _phasePlanBuilder.Build(options, new StandardPhaseStepActions
            {
                DatAudit = (state, ct) => RunDatAuditStep(state, options, result, metrics, ct),
                Deduplicate = (state, ct) => RunDeduplicateStep(state, options, result, metrics, ct),
                JunkRemoval = (state, ct) => RunJunkRemovalStep(state, options, result, metrics, ct),
                DatRename = (state, ct) => RunDatRenameStep(state, options, result, metrics, ct),
                Move = (state, ct) => RunMoveStep(state, options, result, metrics, ct),
                ConsoleSort = (state, ct) => RunConsoleSortStep(state, options, result, metrics, ct),
                WinnerConversion = (state, ct) => RunWinnerConversionStep(state, options, result, metrics, ct)
            });

            ExecutePhasePlan(phasePlan, pipelineState, cancellationToken);
        }

        // Propagate DatAudit / DatRename results from pipeline state to builder
        if (pipelineState.DatAuditResult is { } datAudit)
        {
            result.DatAuditResult = datAudit;
            result.DatHaveCount = datAudit.HaveCount;
            result.DatHaveWrongNameCount = datAudit.HaveWrongNameCount;
            result.DatMissCount = datAudit.MissCount;
            result.DatUnknownCount = datAudit.UnknownCount;
            result.DatAmbiguousCount = datAudit.AmbiguousCount;
        }

        if (pipelineState.DatRenameResult is { } datRename)
        {
            result.DatRenameProposedCount = datRename.ProposedCount;
            result.DatRenameExecutedCount = datRename.ExecutedCount;
            result.DatRenameSkippedCount = datRename.SkippedCount;
            result.DatRenameFailedCount = datRename.FailedCount;
            result.DatRenamePathMutations = datRename.PathMutations;
        }

        return FinalizeCompletedRun(options, result, pipelineState, metrics, sw, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            result.Status = RunOutcome.Cancelled.ToStatusString();
            result.ExitCode = RunOutcome.Cancelled.ToExitCode();
            result.DurationMs = sw.ElapsedMilliseconds;
            result.PhaseMetrics = metrics.GetMetrics();

            // Preserve partial scan/dedupe output so GUI/CLI can display progress made before cancellation.
            ApplyPartialPipelineState(pipelineState, result);

            // Issue #19: Write partial audit sidecar so rollback is possible after cancel
            try
            {
                WritePartialAuditSidecar(options, result, metrics, sw.ElapsedMilliseconds);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                _onProgress?.Invoke($"[Audit] Partial sidecar write failed after cancellation: {ex.GetType().Name}: {ex.Message}");
            }

            return result.Build();
        }
        // TASK-154: Catch non-cancel exceptions — write sidecar so rollback is possible,
        // mark run as failed, and return a result instead of crashing the host process.
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            result.Status = RunOutcome.Failed.ToStatusString();
            result.ExitCode = RunOutcome.Failed.ToExitCode();
            result.DurationMs = sw.ElapsedMilliseconds;
            result.PhaseMetrics = metrics.GetMetrics();

            // Keep best-effort partial data for diagnostics and UI continuity after failures.
            ApplyPartialPipelineState(pipelineState, result);

            _onProgress?.Invoke(RunProgressLocalization.Format(
                "Pipeline.Error.Aborted",
                "[FEHLER] Pipeline abgebrochen: {0}: {1}",
                ex.GetType().Name,
                ex.Message));

            // Write partial audit sidecar so rollback is possible even after crash
            try
            {
                WritePartialAuditSidecar(options, result, metrics, sw.ElapsedMilliseconds);
            }
            catch (Exception sidecarEx) when (sidecarEx is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                _onProgress?.Invoke($"[Audit] Partial sidecar write failed after pipeline error: {sidecarEx.GetType().Name}: {sidecarEx.Message}");
            }

            return result.Build();
        }
        finally
        {
            try
            {
                _hashService?.FlushPersistentCache();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                _onProgress?.Invoke($"[HashCache] Persist failed: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_hashService is IDisposable disposableHashService)
            disposableHashService.Dispose();
        if (_collectionIndex is IDisposable disposableCollectionIndex)
            disposableCollectionIndex.Dispose();
        _reviewDecisionService?.Dispose();
    }

    private static void ApplyPartialPipelineState(PipelineState pipelineState, RunResultBuilder result)
    {
        if (pipelineState.AllCandidates is { } allCandidates)
        {
            result.AllCandidates = allCandidates;
            if (result.TotalFilesScanned == 0)
                result.TotalFilesScanned = allCandidates.Count;
        }

        // Preserve DatAudit results from partial runs (runs before cancellation/failure)
        if (pipelineState.DatAuditResult is { } datAudit && result.DatAuditResult is null)
        {
            result.DatAuditResult = datAudit;
            result.DatHaveCount = datAudit.HaveCount;
            result.DatHaveWrongNameCount = datAudit.HaveWrongNameCount;
            result.DatMissCount = datAudit.MissCount;
            result.DatUnknownCount = datAudit.UnknownCount;
            result.DatAmbiguousCount = datAudit.AmbiguousCount;
        }

        if (pipelineState.AllGroups is not { } allGroups)
            return;

        result.DedupeGroups = allGroups;
        if (result.GroupCount == 0)
            result.GroupCount = allGroups.Count;
        if (result.WinnerCount == 0)
            result.WinnerCount = allGroups.Count;
        if (result.LoserCount != 0)
            return;

        var loserCount = 0;
        foreach (var group in allGroups)
            loserCount += group.Losers.Count;

        result.LoserCount = loserCount;
    }

    private void ExecutePhasePlan(
        IReadOnlyList<IPhaseStep> phasePlan,
        PipelineState pipelineState,
        CancellationToken cancellationToken)
    {
        foreach (var phase in phasePlan)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _onProgress?.Invoke($"[Plan] Phase: {phase.Name}");
            phase.Execute(pipelineState, cancellationToken);
        }
    }

}

