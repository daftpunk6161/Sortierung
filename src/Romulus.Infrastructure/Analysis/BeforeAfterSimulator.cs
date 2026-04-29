using System.Diagnostics.CodeAnalysis;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.Analysis;

/// <summary>
/// Wave 5 — T-W5-BEFORE-AFTER-SIMULATOR. Default implementation of
/// <see cref="IBeforeAfterSimulator"/>. Wraps the canonical
/// <c>RunOrchestrator.Execute</c> in DryRun mode and projects the resulting
/// <see cref="RunResult"/> into a flat before/after item list. NEVER computes
/// its own plan; all decisions come from the underlying RunResult so GUI / CLI
/// / API and Reports share one fachliche Wahrheit.
/// </summary>
public sealed class BeforeAfterSimulator : IBeforeAfterSimulator
{
    private readonly Func<RunOptions, CancellationToken, RunResult> _planExecutor;

    /// <param name="planExecutor">
    /// Delegate that runs the canonical pipeline (typically
    /// <c>(opts, ct) =&gt; new RunOrchestrator(...).Execute(opts, ct)</c>).
    /// Injected so the simulator stays testable without instantiating a full
    /// orchestrator graph.
    /// </param>
    public BeforeAfterSimulator(Func<RunOptions, CancellationToken, RunResult> planExecutor)
    {
        ArgumentNullException.ThrowIfNull(planExecutor);
        _planExecutor = planExecutor;
    }

    public BeforeAfterSimulationResult Simulate(RunOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var dryRunOptions = ForceDryRun(options);
        var plan = _planExecutor(dryRunOptions, cancellationToken);

        var items = ProjectEntries(plan);
        var summary = SummarizeFromItems(items);

        return new BeforeAfterSimulationResult(items, summary, plan);
    }

    private static IReadOnlyList<BeforeAfterEntry> ProjectEntries(RunResult plan)
    {
        var entries = new List<BeforeAfterEntry>();

        // Track winners that received a rename so we don't double-emit a Keep entry.
        var renamedSourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rename in plan.DatRenamePathMutations)
        {
            entries.Add(new BeforeAfterEntry(
                SourcePath: rename.SourcePath,
                TargetPath: rename.TargetPath,
                Action: BeforeAfterAction.Rename,
                SizeBytes: 0,
                Reason: "dat-rename"));
            renamedSourcePaths.Add(rename.SourcePath);
        }

        // Track winners targeted by a planned conversion (override Keep with Convert).
        var convertedSourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (plan.ConversionReport is { Results: { } results })
        {
            foreach (var step in results)
            {
                if (string.IsNullOrWhiteSpace(step.SourcePath))
                    continue;
                convertedSourcePaths.Add(step.SourcePath);
                var targetFmt = step.Plan?.FinalTargetExtension ?? "?";
                entries.Add(new BeforeAfterEntry(
                    SourcePath: step.SourcePath,
                    TargetPath: string.IsNullOrWhiteSpace(step.TargetPath) ? null : step.TargetPath,
                    Action: BeforeAfterAction.Convert,
                    SizeBytes: step.SourceBytes ?? 0,
                    Reason: $"convert -> {targetFmt}"));
            }
        }

        foreach (var group in plan.DedupeGroups)
        {
            var winnerPath = group.Winner.MainPath;
            var alreadyEmitted = renamedSourcePaths.Contains(winnerPath) || convertedSourcePaths.Contains(winnerPath);
            if (!alreadyEmitted)
            {
                entries.Add(new BeforeAfterEntry(
                    SourcePath: winnerPath,
                    TargetPath: winnerPath,
                    Action: BeforeAfterAction.Keep,
                    SizeBytes: group.Winner.SizeBytes,
                    Reason: $"winner gameKey={group.GameKey}"));
            }

            foreach (var loser in group.Losers)
            {
                entries.Add(new BeforeAfterEntry(
                    SourcePath: loser.MainPath,
                    TargetPath: null,
                    Action: BeforeAfterAction.Remove,
                    SizeBytes: loser.SizeBytes,
                    Reason: $"loser gameKey={group.GameKey}"));
            }
        }

        return entries;
    }

    private static BeforeAfterSummary SummarizeFromItems(IReadOnlyList<BeforeAfterEntry> items)
    {
        int kept = 0, removed = 0, converted = 0, renamed = 0;
        long savedBytes = 0;
        foreach (var item in items)
        {
            switch (item.Action)
            {
                case BeforeAfterAction.Keep: kept++; break;
                case BeforeAfterAction.Remove: removed++; savedBytes += item.SizeBytes; break;
                case BeforeAfterAction.Convert: converted++; break;
                case BeforeAfterAction.Rename: renamed++; break;
            }
        }

        // After-state file count: every removed item is gone, everything else still exists.
        // Convert and Rename do not add or remove files (1:1 transformation).
        var totalBefore = kept + removed + converted + renamed;
        var totalAfter = totalBefore - removed;

        return new BeforeAfterSummary(
            TotalBefore: totalBefore,
            TotalAfter: totalAfter,
            Kept: kept,
            Removed: removed,
            Converted: converted,
            Renamed: renamed,
            PotentialSavedBytes: savedBytes);
    }

    /// <summary>
    /// Returns a copy of <paramref name="source"/> with <c>Mode</c> forced to
    /// <see cref="RunConstants.ModeDryRun"/>. The simulator MUST never invoke
    /// the pipeline in Move mode; this helper is the single chokepoint.
    /// </summary>
    [SuppressMessage("Maintainability", "CA1502:AvoidExcessiveComplexity",
        Justification = "Property fan-out mirrors RunOptions; intentional 1:1 copy.")]
    internal static RunOptions ForceDryRun(RunOptions source)
    {
        return new RunOptions
        {
            Roots = source.Roots,
            Mode = RunConstants.ModeDryRun,
            PreferRegions = source.PreferRegions,
            Extensions = source.Extensions,
            RemoveJunk = source.RemoveJunk,
            OnlyGames = source.OnlyGames,
            KeepUnknownWhenOnlyGames = source.KeepUnknownWhenOnlyGames,
            AggressiveJunk = source.AggressiveJunk,
            SortConsole = source.SortConsole,
            EnableDat = source.EnableDat,
            EnableDatAudit = source.EnableDatAudit,
            EnableDatRename = source.EnableDatRename,
            DatRoot = source.DatRoot,
            HashType = source.HashType,
            ConvertFormat = source.ConvertFormat,
            ConvertOnly = source.ConvertOnly,
            ApproveReviews = source.ApproveReviews,
            ApproveConversionReview = source.ApproveConversionReview,
            TrashRoot = source.TrashRoot,
            AuditPath = source.AuditPath,
            ReportPath = source.ReportPath,
            ConflictPolicy = source.ConflictPolicy,
            AllowHeuristicFallback = source.AllowHeuristicFallback,
            AcceptDataLossToken = source.AcceptDataLossToken
        };
    }
}
