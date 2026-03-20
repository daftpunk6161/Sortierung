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
        state.SetDedupeOutput(output.Groups, output.GameGroups);
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
        state.SetJunkPaths(removed);
        return PhaseStepResult.Ok(removed.Count, removed);
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

    private PhaseStepResult RunConsoleSortStep(
        PipelineState state,
        RunOptions options,
        RunResultBuilder result,
        PhaseMetricsCollector metrics,
        CancellationToken cancellationToken)
    {
        _ = state;
        if (!options.SortConsole || options.Mode != "Move" || _consoleDetector is null)
            return PhaseStepResult.Skipped();

        metrics.StartPhase("ConsoleSort");
        _onProgress?.Invoke("[Sort] Sortiere Dateien nach Konsole…");

        var sorter = new ConsoleSorter(_fs, _consoleDetector, _audit, options.AuditPath);
        result.ConsoleSortResult = sorter.Sort(
            options.Roots, options.Extensions, dryRun: false, cancellationToken);

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
