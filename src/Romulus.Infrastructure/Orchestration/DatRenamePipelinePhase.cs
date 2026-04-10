using Romulus.Contracts.Models;
using Romulus.Contracts;
using Romulus.Core.Audit;

namespace Romulus.Infrastructure.Orchestration;

/// <summary>
/// Pipeline phase for DAT-driven filename normalization.
/// Executes only policy-approved renames and writes per-file audit rows.
/// </summary>
public sealed class DatRenamePipelinePhase : IPipelinePhase<DatRenameInput, DatRenameResult>
{
    public string Name => "DatRename";

    public DatRenameResult Execute(DatRenameInput input, PipelineContext context, CancellationToken cancellationToken)
    {
        context.Metrics.StartPhase(Name);

        var proposals = new List<DatRenameProposal>(input.Entries.Count);
        var executableItems = new List<DatRenameExecutionItem>(input.Entries.Count);
        var pathMutations = new List<PathMutation>();
        var proposedCount = 0;
        var executedCount = 0;
        var skippedCount = 0;
        var failedCount = 0;
        var executeMode = string.Equals(input.Options.Mode, "Move", StringComparison.OrdinalIgnoreCase);
        var skipOnConflict = string.Equals(input.Options.ConflictPolicy, "Skip", StringComparison.OrdinalIgnoreCase);

        foreach (var entry in input.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentFileName = Path.GetFileName(entry.FilePath);
            var proposal = DatRenamePolicy.EvaluateRename(entry, currentFileName);

            if (entry.Status == DatAuditStatus.HaveWrongName)
                proposedCount++;

            proposals.Add(proposal);

            if (entry.Status != DatAuditStatus.HaveWrongName)
            {
                skippedCount++;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(proposal.ConflictReason))
            {
                skippedCount++;
                continue;
            }

            // DryRun should never mutate file system or audit trail.
            if (!executeMode)
                continue;

            var targetPath = Path.Combine(Path.GetDirectoryName(entry.FilePath) ?? string.Empty, proposal.TargetFileName);

            if (skipOnConflict)
            {
                if (context.FileSystem.TestPath(targetPath, "Leaf"))
                {
                    skippedCount++;
                    continue;
                }
            }

            executableItems.Add(new DatRenameExecutionItem(entry, proposal, targetPath));
        }

        if (executeMode && executableItems.Count > 0)
        {
            var winnersByTargetPath = new Dictionary<string, DatRenameExecutionItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in executableItems
                .OrderByDescending(static item => item.Entry.Confidence)
                .ThenBy(static item => item.Entry.FilePath, StringComparer.OrdinalIgnoreCase))
            {
                if (!winnersByTargetPath.ContainsKey(item.TargetPath))
                    winnersByTargetPath[item.TargetPath] = item;
            }

            foreach (var item in executableItems.OrderBy(static item => item.Entry.FilePath, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var winner = winnersByTargetPath[item.TargetPath];
                if (!string.Equals(winner.Entry.FilePath, item.Entry.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    skippedCount++;
                    continue;
                }

                var auditPath = input.Options.AuditPath;
                string? root = null;
                if (!string.IsNullOrWhiteSpace(auditPath))
                {
                    root = PipelinePhaseHelpers.FindRootForPath(item.Entry.FilePath, input.Options.Roots);
                    if (root is not null)
                    {
                        var pendingReason = $"dat-rename:write-ahead|game:{item.Entry.DatGameName}|confidence:{item.Entry.Confidence}";
                        context.AuditStore.AppendAuditRow(
                            auditPath,
                            root,
                            item.Entry.FilePath,
                            item.TargetPath,
                            RunConstants.AuditActions.DatRenamePending,
                            item.Entry.ConsoleKey,
                            item.Entry.Hash,
                            pendingReason);
                        context.AuditStore.Flush(auditPath);
                    }
                }

                var renamedPath = context.FileSystem.RenameItemSafely(item.Entry.FilePath, item.Proposal.TargetFileName);
                if (renamedPath is null)
                {
                    failedCount++;

                    if (!string.IsNullOrWhiteSpace(auditPath) && root is not null)
                    {
                        var failedReason = $"dat-rename:rename-failed|game:{item.Entry.DatGameName}|confidence:{item.Entry.Confidence}";
                        context.AuditStore.AppendAuditRow(
                            auditPath,
                            root,
                            item.Entry.FilePath,
                            item.TargetPath,
                            RunConstants.AuditActions.DatRenameFailed,
                            item.Entry.ConsoleKey,
                            item.Entry.Hash,
                            failedReason);
                    }

                    continue;
                }

                executedCount++;
                pathMutations.Add(new PathMutation(item.Entry.FilePath, renamedPath));

                if (!string.IsNullOrWhiteSpace(auditPath) && root is not null)
                {
                    var reason = $"dat-rename|game:{item.Entry.DatGameName}|confidence:{item.Entry.Confidence}";
                    context.AuditStore.AppendAuditRow(
                        auditPath,
                        root,
                        item.Entry.FilePath,
                        renamedPath,
                        RunConstants.AuditActions.DatRename,
                        item.Entry.ConsoleKey,
                        item.Entry.Hash,
                        reason);
                }
            }
        }

        context.Metrics.CompletePhase(executedCount);

        return new DatRenameResult(
            proposals,
            pathMutations,
            proposedCount,
            executedCount,
            skippedCount,
            failedCount);
    }

    private sealed record DatRenameExecutionItem(
        DatAuditEntry Entry,
        DatRenameProposal Proposal,
        string TargetPath);
}

/// <summary>
/// Input for DAT rename pipeline phase.
/// </summary>
/// <param name="Entries">DAT audit entries to evaluate for rename.</param>
/// <param name="Options">Current run options.</param>
public sealed record DatRenameInput(
    IReadOnlyList<DatAuditEntry> Entries,
    RunOptions Options);

/// <summary>
/// Output summary for DAT rename pipeline phase.
/// </summary>
/// <param name="Proposals">All evaluated rename proposals.</param>
/// <param name="ProposedCount">Count of rename-eligible entries.</param>
/// <param name="ExecutedCount">Count of successful rename operations.</param>
/// <param name="SkippedCount">Count of entries skipped by policy/status.</param>
/// <param name="FailedCount">Count of attempted renames that failed in I/O layer.</param>
public sealed record DatRenameResult(
    IReadOnlyList<DatRenameProposal> Proposals,
    IReadOnlyList<PathMutation> PathMutations,
    int ProposedCount,
    int ExecutedCount,
    int SkippedCount,
    int FailedCount);
