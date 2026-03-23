using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.Metrics;
using RomCleanup.Infrastructure.Sorting;

namespace RomCleanup.Infrastructure.Orchestration;

public sealed partial class RunOrchestrator
{
    private PhaseStepResult RunDeduplicateStep(
        PipelineState state,
        RunOptions options,
        RunResultBuilder result,
        PhaseMetricsCollector metrics,
        CancellationToken cancellationToken)
    {
        var output = ExecuteDedupePhase(state.ProcessingCandidates!, state.AllCandidates!, options, result, metrics, cancellationToken);
        state.SetDedupeOutput(output.AllGroups, output.GameGroups);
        return PhaseStepResult.Ok(output.GameGroups.Count, output);
    }

    private PhaseStepResult RunJunkRemovalStep(
        PipelineState state,
        RunOptions options,
        RunResultBuilder result,
        PhaseMetricsCollector metrics,
        CancellationToken cancellationToken)
    {
        var removed = ExecuteJunkPhaseIfEnabled(state.AllGroups ?? Array.Empty<DedupeResult>(), options, result, metrics, cancellationToken);
        state.SetJunkPaths(removed.RemovedPaths);
        return PhaseStepResult.Ok(removed.RemovedPaths.Count, removed);
    }

    private PhaseStepResult RunMoveStep(
        PipelineState state,
        RunOptions options,
        RunResultBuilder result,
        PhaseMetricsCollector metrics,
        CancellationToken cancellationToken)
    {
        if (options.Mode != "Move")
            return PhaseStepResult.Skipped();

        metrics.StartPhase("Move");
        var groups = state.GameGroups ?? Array.Empty<DedupeResult>();
        var totalLosers = groups.Sum(g => g.Losers.Count);
        _onProgress?.Invoke($"[Move] Verschiebe {totalLosers} Duplikate in Trash…");

        var movePhase = new MovePipelinePhase();
        var moveContext = new PipelineContext
        {
            Options = options,
            FileSystem = _fs,
            AuditStore = _audit,
            Metrics = metrics,
            OnProgress = _onProgress
        };

        var moveResult = movePhase.Execute(new MovePhaseInput(groups, options), moveContext, cancellationToken);
        _onProgress?.Invoke($"[Move] Abgeschlossen: {moveResult.MoveCount} verschoben, {moveResult.FailCount} Fehler");
        result.MoveResult = moveResult;
        metrics.CompletePhase(moveResult.MoveCount);

        return PhaseStepResult.Ok(moveResult.MoveCount, moveResult);
    }

    private PhaseStepResult RunDatRenameStep(
        PipelineState state,
        RunOptions options,
        RunResultBuilder result,
        PhaseMetricsCollector metrics,
        CancellationToken cancellationToken)
    {
        if (!options.EnableDatRename || options.Mode != "Move")
            return PhaseStepResult.Skipped();

        // DatAudit-to-Rename payload wiring lands in subsequent DAT integration steps.
        var entries = Array.Empty<DatAuditEntry>();

        var phase = new DatRenamePipelinePhase();
        var context = new PipelineContext
        {
            Options = options,
            FileSystem = _fs,
            AuditStore = _audit,
            Metrics = metrics,
            OnProgress = _onProgress
        };

        var renameResult = phase.Execute(new DatRenameInput(entries, options), context, cancellationToken);
        return PhaseStepResult.Ok(renameResult.ExecutedCount, renameResult);
    }

    private PhaseStepResult RunConsoleSortStep(
        PipelineState state,
        RunOptions options,
        RunResultBuilder result,
        PhaseMetricsCollector metrics,
        CancellationToken cancellationToken)
    {
        if (!options.SortConsole || options.Mode != "Move" || _consoleDetector is null)
            return PhaseStepResult.Skipped();

        metrics.StartPhase("ConsoleSort");
        _onProgress?.Invoke("[Sort] Sortiere Dateien nach Konsole…");

        // Build sort-decision map from enrichment phase.
        // We use the SortDecision computed by HypothesisResolver instead of raw confidence.
        Dictionary<string, string>? enrichedConsoleKeys = null;
        if (state.AllCandidates is not null)
        {
            enrichedConsoleKeys = new Dictionary<string, string>(
                state.AllCandidates.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var c in state.AllCandidates)
            {
                if (c.Category != FileCategory.Game)
                {
                    enrichedConsoleKeys[c.MainPath] = "UNKNOWN";
                    continue;
                }

                if (string.IsNullOrEmpty(c.ConsoleKey) ||
                    c.ConsoleKey is "UNKNOWN" or "AMBIGUOUS")
                {
                    enrichedConsoleKeys[c.MainPath] = "UNKNOWN";
                    continue;
                }

                // SortDecision-based gate: only Sort and DatVerified pass
                if (c.SortDecision is "Sort" or "DatVerified")
                {
                    enrichedConsoleKeys[c.MainPath] = c.ConsoleKey;
                }
                else
                {
                    enrichedConsoleKeys[c.MainPath] = "UNKNOWN";
                }
            }
        }

        var sorter = new ConsoleSorter(_fs, _consoleDetector, _audit, options.AuditPath);
        result.ConsoleSortResult = sorter.Sort(
            options.Roots, options.Extensions, dryRun: false, cancellationToken,
            enrichedConsoleKeys: enrichedConsoleKeys);

        _onProgress?.Invoke("[Sort] Konsolen-Sortierung abgeschlossen");
        metrics.CompletePhase();
        return PhaseStepResult.Ok(result.ConsoleSortResult?.Moved ?? 0, result.ConsoleSortResult);
    }

    private PhaseStepResult RunWinnerConversionStep(
        PipelineState state,
        RunOptions options,
        RunResultBuilder result,
        PhaseMetricsCollector metrics,
        CancellationToken cancellationToken)
    {
        if (options.ConvertFormat is null || options.Mode != "Move" || _converter is null)
            return PhaseStepResult.Skipped();

        ExecuteWinnerConversionPhase(
            state.GameGroups ?? Array.Empty<DedupeResult>(),
            options,
            state.JunkRemovedPaths ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            result,
            metrics,
            cancellationToken);

        return PhaseStepResult.Ok(result.ConvertedCount);
    }
}
