using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Core.Classification;
using RomCleanup.Core.Deduplication;
using RomCleanup.Infrastructure.Hashing;
using RomCleanup.Infrastructure.Deduplication;
using RomCleanup.Infrastructure.Linking;
using RomCleanup.Infrastructure.Metrics;
using RomCleanup.Infrastructure.Quarantine;
using RomCleanup.Infrastructure.Reporting;
using RomCleanup.Infrastructure.Sorting;

namespace RomCleanup.Infrastructure.Orchestration;

/// <summary>
/// Central pipeline orchestrator for ROM cleanup operations.
/// Port of Invoke-CliRunAdapter / RunHelpers.Execution.ps1.
/// Encapsulates: Preflight → Scan → Dedupe → JunkRemoval → Move → Sort → Convert → Report.
/// Streaming seam: scan/enrichment use IAsyncEnumerable in orchestrator partial steps.
/// Used by both CLI and API entry points.
/// </summary>
public sealed partial class RunOrchestrator
{
    private readonly IFileSystem _fs;
    private readonly IAuditStore _audit;
    private readonly ConsoleDetector? _consoleDetector;
    private readonly FileHashService? _hashService;
    private readonly ArchiveHashService? _archiveHashService;
    private readonly IFormatConverter? _converter;
    private readonly DatIndex? _datIndex;
    private readonly Action<string>? _onProgress;
    private readonly IPhasePlanBuilder _phasePlanBuilder;
    private readonly Contracts.Ports.IHeaderlessHasher? _headerlessHasher;

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
        Contracts.Ports.IHeaderlessHasher? headerlessHasher = null)
    {
        _fs = fs;
        _audit = audit;
        _consoleDetector = consoleDetector;
        _hashService = hashService;
        _archiveHashService = archiveHashService;
        _converter = converter;
        _datIndex = datIndex;
        _onProgress = onProgress;
        _phasePlanBuilder = phasePlanBuilder ?? new PhasePlanBuilder();
        _headerlessHasher = headerlessHasher;
    }

    /// <summary>
    /// Validate all prerequisites before a run.
    /// Port of Invoke-RunPreflight from RunHelpers.Execution.ps1.
    /// Checks: roots exist, audit dir writable (F-06), tools available.
    /// </summary>
    public OperationResult Preflight(RunOptions options)
    {
        var warnings = new List<string>();

        if (options.Roots.Count == 0)
            return OperationResult.Blocked("No roots specified");

        foreach (var root in options.Roots)
        {
            if (!_fs.TestPath(root, "Container"))
                return OperationResult.Blocked($"Root does not exist: {root}");
        }

        // F-06: Audit directory write test
        if (!string.IsNullOrEmpty(options.AuditPath))
        {
            var auditDir = Path.GetDirectoryName(options.AuditPath);
            if (!string.IsNullOrEmpty(auditDir))
            {
                try
                {
                    _fs.EnsureDirectory(auditDir);
                    var testFile = Path.Combine(auditDir, $".write_test_{Guid.NewGuid():N}");
                    File.WriteAllText(testFile, "");
                    File.Delete(testFile);
                }
                catch (Exception ex)
                {
                    return OperationResult.Blocked($"Audit directory not writable: {ex.Message}");
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

    /// <summary>
    /// Execute the full pipeline: Scan → Dedupe → JunkRemoval → Move → Sort → (optional Convert) → Report.
    /// </summary>
    public RunResult Execute(RunOptions options, CancellationToken cancellationToken = default)
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
        _onProgress?.Invoke("[Preflight] Voraussetzungen prüfen…");
        var preflight = Preflight(options);
        result.Preflight = preflight;
        metrics.CompletePhase();
        if (preflight.ShouldReturn)
        {
            _onProgress?.Invoke($"[Preflight] Blockiert: {preflight.Reason}");
            result.Status = RunOutcome.Blocked.ToStatusString();
            result.ExitCode = 3;
            return result.Build();
        }
        _onProgress?.Invoke($"[Preflight] OK — {options.Roots.Count} Root(s), {options.Extensions.Count} Extension(s), Modus: {options.Mode}");
        if (preflight.Warnings.Count > 0)
            foreach (var w in preflight.Warnings)
                _onProgress?.Invoke($"[Preflight] Warnung: {w}");

        cancellationToken.ThrowIfCancellationRequested();

        var scanResult = RunScanAndPrepareState(options, result, metrics, pipelineState, cancellationToken);
        var candidates = scanResult.AllCandidates;
        var processingCandidates = scanResult.ProcessingCandidates;

        if (processingCandidates.Count == 0)
        {
            result.Status = RunOutcome.Ok.ToStatusString();
            result.ExitCode = 0;
            result.AllCandidates = candidates;
            sw.Stop();
            result.DurationMs = sw.ElapsedMilliseconds;
            result.PhaseMetrics = metrics.GetMetrics();
            if (!string.IsNullOrEmpty(options.ReportPath))
            {
                var reportStep = new ReportPhaseStep(() =>
                {
                    _onProgress?.Invoke("[Report] Generiere HTML-Report…");
                    return GenerateReport(result, options);
                });
                var reportOutcome = reportStep.Execute(pipelineState, cancellationToken);
                result.ReportPath = reportOutcome.TypedResult as string;
                if (!string.IsNullOrEmpty(result.ReportPath))
                    _onProgress?.Invoke($"[Report] Report erstellt: {result.ReportPath}");
            }
            return result.Build();
        }

        // Integrate deferred services in a non-destructive analysis pass.
        new DeferredAnalysisPhaseStep((state, ct) => ExecuteDeferredServiceAnalysis(state, options, ct))
            .Execute(pipelineState, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        if (TryExecuteConvertOnlyPath(options, candidates, processingCandidates, result, metrics, pipelineState, sw, cancellationToken, out var convertOnlyResult))
            return convertOnlyResult;

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

        // Propagate DatAudit / DatRename results from pipeline state to builder
        if (pipelineState.DatAuditResult is { } datAudit)
        {
            result.DatAuditResult = datAudit;
            result.DatHaveCount = datAudit.Entries.Count(e => e.Status == DatAuditStatus.Have);
            result.DatHaveWrongNameCount = datAudit.Entries.Count(e => e.Status == DatAuditStatus.HaveWrongName);
            result.DatMissCount = datAudit.Entries.Count(e => e.Status == DatAuditStatus.Miss);
            result.DatUnknownCount = datAudit.Entries.Count(e => e.Status == DatAuditStatus.Unknown);
            result.DatAmbiguousCount = datAudit.Entries.Count(e => e.Status == DatAuditStatus.Ambiguous);
        }

        if (pipelineState.DatRenameResult is { } datRename)
        {
            result.DatRenameProposedCount = datRename.ProposedCount;
            result.DatRenameExecutedCount = datRename.ExecutedCount;
            result.DatRenameSkippedCount = datRename.SkippedCount;
            result.DatRenameFailedCount = datRename.FailedCount;
        }

        sw.Stop();
        // TASK-151: Derive status based on ALL phase errors (incl. DatRename + ConsoleSort)
        var hasErrors = result.ConvertErrorCount > 0
                     || (result.MoveResult is { FailCount: > 0 })
                     || (result.JunkMoveResult is { FailCount: > 0 })
                     || result.DatRenameFailedCount > 0
                     || (result.ConsoleSortResult is { Failed: > 0 });
        var runOutcome = hasErrors ? RunOutcome.CompletedWithErrors : RunOutcome.Ok;
        result.Status = runOutcome.ToStatusString();
        result.ExitCode = hasErrors ? 1 : 0;
        result.DurationMs = sw.ElapsedMilliseconds;
        result.PhaseMetrics = metrics.GetMetrics();

        // FEAT-02: Generate report at end of pipeline
        if (!string.IsNullOrEmpty(options.ReportPath))
        {
            var reportStep = new ReportPhaseStep(() =>
            {
                _onProgress?.Invoke("[Report] Generiere HTML-Report…");
                return GenerateReport(result, options);
            });
            result.ReportPath = reportStep.Execute(pipelineState, cancellationToken).TypedResult as string;
            if (!string.IsNullOrEmpty(result.ReportPath))
                _onProgress?.Invoke($"[Report] Report erstellt: {result.ReportPath}");
        }

        // FEAT-03: Write final audit sidecar with HMAC signature after all phases.
        // TASK-145: Pass actual RunOutcome so sidecar status reflects errors.
        new AuditSealPhaseStep(() => WriteCompletedAuditSidecar(options, result, sw.ElapsedMilliseconds, runOutcome))
            .Execute(pipelineState, cancellationToken);

        _onProgress?.Invoke($"[Fertig] Pipeline abgeschlossen in {sw.ElapsedMilliseconds}ms — {result.TotalFilesScanned} Dateien, {result.GroupCount} Gruppen");
        return result.Build();
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            result.Status = RunOutcome.Cancelled.ToStatusString();
            result.ExitCode = 2;
            result.DurationMs = sw.ElapsedMilliseconds;
            result.PhaseMetrics = metrics.GetMetrics();

            // Issue #19: Write partial audit sidecar so rollback is possible after cancel
            WritePartialAuditSidecar(options, result, metrics, sw.ElapsedMilliseconds);

            return result.Build();
        }
        // TASK-154: Catch non-cancel exceptions — write sidecar so rollback is possible,
        // mark run as failed, and return a result instead of crashing the host process.
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            result.Status = RunOutcome.Failed.ToStatusString();
            result.ExitCode = 4;
            result.DurationMs = sw.ElapsedMilliseconds;
            result.PhaseMetrics = metrics.GetMetrics();

            _onProgress?.Invoke($"[FEHLER] Pipeline abgebrochen: {ex.GetType().Name}: {ex.Message}");

            // Write partial audit sidecar so rollback is possible even after crash
            WritePartialAuditSidecar(options, result, metrics, sw.ElapsedMilliseconds);

            return result.Build();
        }
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

