using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.Metrics;
using RomCleanup.Infrastructure.Sorting;

namespace RomCleanup.Infrastructure.Orchestration;

public sealed partial class RunOrchestrator
{
    private PhaseStepResult RunDatAuditStep(
        PipelineState state,
        RunOptions options,
        RunResultBuilder result,
        PhaseMetricsCollector metrics,
        CancellationToken cancellationToken)
    {
        if (!options.EnableDatAudit || _datIndex is null)
            return PhaseStepResult.Skipped();

        var candidates = state.AllCandidates;
        if (candidates is null || candidates.Count == 0)
            return PhaseStepResult.Skipped();

        var phase = new DatAuditPipelinePhase();
        var context = new PipelineContext
        {
            Options = options,
            FileSystem = _fs,
            AuditStore = _audit,
            Metrics = metrics,
            OnProgress = _onProgress
        };

        var auditResult = phase.Execute(
            new DatAuditInput(candidates, _datIndex, options),
            context,
            cancellationToken);

        state.SetDatAuditOutput(auditResult);
        return PhaseStepResult.Ok(auditResult.Entries.Count, auditResult);
    }

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
        var removed = ExecuteJunkPhaseIfEnabled(state.AllGroups ?? Array.Empty<DedupeGroup>(), options, result, metrics, cancellationToken);
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
        var groups = state.GameGroups ?? Array.Empty<DedupeGroup>();
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

        var entries = state.DatAuditResult?.Entries;
        if (entries is null || entries.Count == 0)
            return PhaseStepResult.Skipped();

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
        state.SetDatRenameOutput(renameResult);
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
        // We use the SortDecision computed by HypothesisResolver to route files.
        Dictionary<string, string>? enrichedConsoleKeys = null;
        Dictionary<string, string>? enrichedSortDecisions = null;
        Dictionary<string, string>? enrichedCategories = null;
        if (state.AllCandidates is not null)
        {
            enrichedConsoleKeys = new Dictionary<string, string>(
                state.AllCandidates.Count, StringComparer.OrdinalIgnoreCase);
            enrichedSortDecisions = new Dictionary<string, string>(
                state.AllCandidates.Count, StringComparer.OrdinalIgnoreCase);
            enrichedCategories = new Dictionary<string, string>(
                state.AllCandidates.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var c in state.AllCandidates)
            {
                enrichedCategories[c.MainPath] = c.Category.ToString();

                if (string.IsNullOrEmpty(c.ConsoleKey) ||
                    c.ConsoleKey is "UNKNOWN" or "AMBIGUOUS")
                {
                    enrichedConsoleKeys[c.MainPath] = "UNKNOWN";
                    continue;
                }

                // Pass through the actual console key for all decisions
                enrichedConsoleKeys[c.MainPath] = c.ConsoleKey;
                enrichedSortDecisions[c.MainPath] = c.SortDecision.ToString();

                // Non-game categories are blocked
                if (c.Category != FileCategory.Game)
                {
                    enrichedSortDecisions[c.MainPath] = SortDecision.Blocked.ToString();
                }
            }
        }

        var sorter = new ConsoleSorter(_fs, _consoleDetector, _audit, options.AuditPath);
        result.ConsoleSortResult = sorter.Sort(
            options.Roots, options.Extensions, dryRun: false, cancellationToken,
            enrichedConsoleKeys: enrichedConsoleKeys,
            enrichedSortDecisions: enrichedSortDecisions,
            enrichedCategories: enrichedCategories);

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
            state.GameGroups ?? Array.Empty<DedupeGroup>(),
            options,
            state.JunkRemovedPaths ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            result,
            metrics,
            cancellationToken);

        return PhaseStepResult.Ok(result.ConvertedCount);
    }
}
