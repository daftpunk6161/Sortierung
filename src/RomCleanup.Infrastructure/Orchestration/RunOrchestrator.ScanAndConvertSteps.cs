using RomCleanup.Contracts.Models;
using RomCleanup.Core.Classification;
using RomCleanup.Infrastructure.Metrics;

namespace RomCleanup.Infrastructure.Orchestration;

public sealed partial class RunOrchestrator
{
    private ScanPhaseResult RunScanAndPrepareState(
        RunOptions options,
        RunResultBuilder result,
        PhaseMetricsCollector metrics,
        PipelineState pipelineState,
        CancellationToken cancellationToken)
    {
        metrics.StartPhase("Scan");
        _onProgress?.Invoke($"[Scan] Scanne {options.Roots.Count} Root-Ordner…");
        foreach (var root in options.Roots)
            _onProgress?.Invoke($"[Scan] Root: {root}");

        var scanSw = System.Diagnostics.Stopwatch.StartNew();
        var scanContext = new PipelineContext
        {
            Options = options,
            FileSystem = _fs,
            AuditStore = _audit,
            Metrics = metrics,
            OnProgress = _onProgress
        };

        IAsyncEnumerable<ScannedFileEntry> scannedFiles = new StreamingScanPipelinePhase(scanContext)
            .EnumerateFilesAsync(options.Roots, options.Extensions, cancellationToken);

        var candidates = MaterializeEnrichedCandidates(scannedFiles, scanContext, cancellationToken);

        var unknownReasonCounts = candidates
            .Where(c => c.Category == FileCategory.Unknown)
            .GroupBy(c => string.IsNullOrWhiteSpace(c.ClassificationReasonCode) ? "unknown" : c.ClassificationReasonCode,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        result.UnknownCount = candidates.Count(c => c.Category == FileCategory.Unknown);
        result.UnknownReasonCounts = unknownReasonCounts;

        List<RomCandidate> processingCandidates = candidates;
        var filteredNonGameCount = 0;
        if (options.OnlyGames)
        {
            var keepUnknown = options.KeepUnknownWhenOnlyGames;
            processingCandidates = candidates
                .Where(c => c.Category == FileCategory.Game || (keepUnknown && c.Category == FileCategory.Unknown))
                .ToList();

            filteredNonGameCount = candidates.Count - processingCandidates.Count;
            result.FilteredNonGameCount = filteredNonGameCount;
            _onProgress?.Invoke($"[Filter] OnlyGames aktiv: {filteredNonGameCount} Nicht-Spiel-Dateien ausgeschlossen (KeepUnknown={keepUnknown})");
        }

        scanSw.Stop();
        result.TotalFilesScanned = candidates.Count;
        _onProgress?.Invoke($"[Scan] Abgeschlossen: {candidates.Count} Dateien in {scanSw.ElapsedMilliseconds}ms");
        metrics.CompletePhase(candidates.Count);

        if (candidates.Count > 100_000)
            _onProgress?.Invoke($"WARNING: {candidates.Count:N0} files scanned — high memory usage. Consider scanning fewer roots.");

        pipelineState.SetScanOutput(candidates, processingCandidates);
        return new ScanPhaseResult(
            candidates,
            processingCandidates,
            result.UnknownCount,
            unknownReasonCounts,
            filteredNonGameCount);
    }

    private bool TryExecuteConvertOnlyPath(
        RunOptions options,
        IReadOnlyList<RomCandidate> candidates,
        IReadOnlyList<RomCandidate> processingCandidates,
        RunResultBuilder result,
        PhaseMetricsCollector metrics,
        PipelineState pipelineState,
        System.Diagnostics.Stopwatch sw,
        CancellationToken cancellationToken,
        out RunResult finalResult)
    {
        finalResult = default!;

        if (!options.ConvertOnly || _converter is null)
            return false;

        ExecuteConvertOnlyPhase(processingCandidates, options, result, metrics, cancellationToken);
        result.AllCandidates = candidates;
        result.TotalFilesScanned = candidates.Count;

        sw.Stop();
        result.DurationMs = sw.ElapsedMilliseconds;
        result.PhaseMetrics = metrics.GetMetrics();
        result.ReportPath = new ReportPhaseStep(() => GenerateReport(result, options))
            .Execute(pipelineState, cancellationToken).TypedResult as string;

        var convertOnlyHasErrors = result.ConvertErrorCount > 0;
        var convertOnlyOutcome = convertOnlyHasErrors ? RunOutcome.CompletedWithErrors : RunOutcome.Ok;
        result.Status = convertOnlyOutcome.ToStatusString();
        result.ExitCode = convertOnlyHasErrors ? 1 : 0;
        finalResult = result.Build();
        return true;
    }
}