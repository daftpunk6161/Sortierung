using Romulus.Contracts.Models;
using Romulus.Core.Classification;
using Romulus.Infrastructure.Metrics;

namespace Romulus.Infrastructure.Orchestration;

public sealed partial class RunOrchestrator
{
    private async Task<ScanPhaseResult> RunScanAndPrepareStateAsync(
        RunOptions options,
        RunResultBuilder result,
        PhaseMetricsCollector metrics,
        PipelineState pipelineState,
        CancellationToken cancellationToken)
    {
        metrics.StartPhase("Scan");
        _onProgress?.Invoke(RunProgressLocalization.Format(
            "Scan.Start",
            "[Scan] Scanne {0} Root-Ordner…",
            options.Roots.Count));
        foreach (var root in options.Roots)
            _onProgress?.Invoke(RunProgressLocalization.Format(
                "Scan.Root",
                "[Scan] Root: {0}",
                root));

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

        var candidates = await MaterializeEnrichedCandidatesAsync(scannedFiles, scanContext, cancellationToken);
        candidates = await ApplyPersistedReviewApprovalsAsync(candidates, options, cancellationToken);

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
            _onProgress?.Invoke(RunProgressLocalization.Format(
                "Filter.OnlyGames",
                "[Filter] OnlyGames aktiv: {0} Nicht-Spiel-Dateien ausgeschlossen (KeepUnknown={1})",
                filteredNonGameCount,
                keepUnknown));
        }

        scanSw.Stop();
        result.TotalFilesScanned = candidates.Count;
        _onProgress?.Invoke(RunProgressLocalization.Format(
            "Scan.Completed",
            "[Scan] Abgeschlossen: {0} Dateien in {1}ms",
            candidates.Count,
            scanSw.ElapsedMilliseconds));
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
        CancellationToken cancellationToken,
        PipelineState pipelineState)
    {
        if (!options.ConvertOnly || _converter is null)
            return false;

        ExecuteConvertOnlyPhase(processingCandidates, options, result, metrics, cancellationToken);
        result.AllCandidates = candidates;
        result.TotalFilesScanned = candidates.Count;
        return true;
    }
}
