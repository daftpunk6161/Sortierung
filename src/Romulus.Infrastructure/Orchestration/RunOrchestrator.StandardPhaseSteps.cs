using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Metrics;
using Romulus.Infrastructure.Sorting;

namespace Romulus.Infrastructure.Orchestration;

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
        if (options.Mode != RunConstants.ModeMove)
            return PhaseStepResult.Skipped();

        metrics.StartPhase("Move");
        var groups = state.GameGroups ?? Array.Empty<DedupeGroup>();
        var totalLosers = groups.Sum(g => g.Losers.Count);
        _onProgress?.Invoke(RunProgressLocalization.Format(
            "Move.Start",
            "[Move] Verschiebe {0} Duplikate in Trash…",
            totalLosers));

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
        _onProgress?.Invoke(RunProgressLocalization.Format(
            "Move.Completed",
            "[Move] Abgeschlossen: {0} verschoben, {1} Fehler",
            moveResult.MoveCount,
            moveResult.FailCount));
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
        if (!options.EnableDatRename || options.Mode != RunConstants.ModeMove)
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
        if (renameResult.PathMutations.Count > 0)
            state.ApplyPathMutations(renameResult.PathMutations);

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
        if (!options.SortConsole || _consoleDetector is null)
            return PhaseStepResult.Skipped();

        metrics.StartPhase("ConsoleSort");
        _onProgress?.Invoke(RunProgressLocalization.Format(
            "Sort.Start",
            "[Sort] Sortiere Dateien nach Konsole…"));

        // Build sort-decision map from enrichment phase.
        // We use the SortDecision computed by HypothesisResolver to route files.
        Dictionary<string, string>? enrichedConsoleKeys = null;
        Dictionary<string, string>? enrichedSortDecisions = null;
        Dictionary<string, string>? enrichedSortReasons = null;
        Dictionary<string, string>? enrichedCategories = null;
        if (state.AllCandidates is not null)
        {
            enrichedConsoleKeys = new Dictionary<string, string>(
                state.AllCandidates.Count, StringComparer.OrdinalIgnoreCase);
            enrichedSortDecisions = new Dictionary<string, string>(
                state.AllCandidates.Count, StringComparer.OrdinalIgnoreCase);
            enrichedSortReasons = new Dictionary<string, string>(
                state.AllCandidates.Count, StringComparer.OrdinalIgnoreCase);
            enrichedCategories = new Dictionary<string, string>(
                state.AllCandidates.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var c in state.AllCandidates)
            {
                enrichedCategories[c.MainPath] = c.Category.ToString();
                enrichedSortDecisions[c.MainPath] = c.SortDecision.ToString();
                enrichedSortReasons[c.MainPath] = ConsoleSorter.BuildSortReasonTag(c);

                if (string.IsNullOrEmpty(c.ConsoleKey) ||
                    c.ConsoleKey is "UNKNOWN" or "AMBIGUOUS")
                {
                    enrichedConsoleKeys[c.MainPath] = "UNKNOWN";
                    continue;
                }

                // Pass through the actual console key for all decisions
                enrichedConsoleKeys[c.MainPath] = c.ConsoleKey;

                if (options.ApproveReviews && c.SortDecision == SortDecision.Review)
                {
                    enrichedSortDecisions[c.MainPath] = SortDecision.Sort.ToString();
                    enrichedSortReasons[c.MainPath] = "review-approved";
                }

                // Non-game categories are blocked
                if (c.Category != FileCategory.Game)
                {
                    enrichedSortDecisions[c.MainPath] = SortDecision.Blocked.ToString();
                    enrichedSortReasons[c.MainPath] = c.Category == FileCategory.Junk
                        ? "junk-category"
                        : "non-game-category";
                }
            }
        }

        var candidatePaths = BuildConsoleSortCandidatePaths(state, options, result);
        var dryRunSort = !string.Equals(options.Mode, RunConstants.ModeMove, StringComparison.OrdinalIgnoreCase);

        var sorter = new ConsoleSorter(_fs, _consoleDetector, _audit, options.AuditPath);
        result.ConsoleSortResult = sorter.Sort(
            options.Roots, options.Extensions, dryRun: dryRunSort, cancellationToken,
            enrichedConsoleKeys: enrichedConsoleKeys,
            enrichedSortDecisions: enrichedSortDecisions,
            enrichedSortReasons: enrichedSortReasons,
            enrichedCategories: enrichedCategories,
            candidatePaths: candidatePaths,
            conflictPolicy: options.ConflictPolicy);

        if (!dryRunSort && result.ConsoleSortResult?.PathMutations is { Count: > 0 } pathMutations)
            state.ApplyPathMutations(pathMutations);

        _onProgress?.Invoke(RunProgressLocalization.Format(
            "Sort.Completed",
            "[Sort] Konsolen-Sortierung abgeschlossen"));
        metrics.CompletePhase();
        return PhaseStepResult.Ok(result.ConsoleSortResult?.Moved ?? 0, result.ConsoleSortResult);
    }

    private static IReadOnlyList<string> BuildConsoleSortCandidatePaths(
        PipelineState state,
        RunOptions options,
        RunResultBuilder result)
    {
        if (state.AllCandidates is null || state.AllCandidates.Count == 0)
            return Array.Empty<string>();

        var remainingPaths = new HashSet<string>(
            state.AllCandidates.Select(static c => c.MainPath),
            StringComparer.OrdinalIgnoreCase);

        if (string.Equals(options.Mode, RunConstants.ModeMove, StringComparison.OrdinalIgnoreCase))
        {
            if (result.MoveResult?.MovedSourcePaths is { Count: > 0 } movedPaths)
                remainingPaths.ExceptWith(movedPaths);

            if (state.JunkRemovedPaths is { Count: > 0 } removedJunkPaths)
                remainingPaths.ExceptWith(removedJunkPaths);
        }
        else
        {
            if (state.GameGroups is not null)
            {
                foreach (var loserPath in state.GameGroups.SelectMany(static g => g.Losers).Select(static c => c.MainPath))
                    remainingPaths.Remove(loserPath);
            }

            if (options.RemoveJunk && state.AllGroups is not null)
            {
                foreach (var junkWinnerPath in state.AllGroups
                    .Where(static g => g.Losers.Count == 0 && g.Winner.Category == FileCategory.Junk)
                    .Select(static g => g.Winner.MainPath))
                {
                    remainingPaths.Remove(junkWinnerPath);
                }
            }
        }

        return remainingPaths
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private PhaseStepResult RunWinnerConversionStep(
        PipelineState state,
        RunOptions options,
        RunResultBuilder result,
        PhaseMetricsCollector metrics,
        CancellationToken cancellationToken)
    {
        if (options.ConvertFormat is null || options.Mode != RunConstants.ModeMove || _converter is null)
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
