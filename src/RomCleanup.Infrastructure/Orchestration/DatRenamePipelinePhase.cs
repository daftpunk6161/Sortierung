using RomCleanup.Contracts.Models;
using RomCleanup.Core.Audit;

namespace RomCleanup.Infrastructure.Orchestration;

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
        var proposedCount = 0;
        var executedCount = 0;
        var skippedCount = 0;
        var failedCount = 0;
        var executeMode = string.Equals(input.Options.Mode, "Move", StringComparison.OrdinalIgnoreCase);

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

            var renamedPath = context.FileSystem.RenameItemSafely(entry.FilePath, proposal.TargetFileName);
            if (renamedPath is null)
            {
                failedCount++;
                continue;
            }

            executedCount++;

            if (!string.IsNullOrWhiteSpace(input.Options.AuditPath))
            {
                var root = PipelinePhaseHelpers.FindRootForPath(entry.FilePath, input.Options.Roots);
                if (root is not null)
                {
                    var reason = $"dat-rename|game:{entry.DatGameName}|confidence:{entry.Confidence}";
                    context.AuditStore.AppendAuditRow(
                        input.Options.AuditPath,
                        root,
                        entry.FilePath,
                        renamedPath,
                        "DAT_RENAME",
                        entry.ConsoleKey,
                        entry.Hash,
                        reason);
                }
            }
        }

        context.Metrics.CompletePhase(executedCount);

        return new DatRenameResult(
            proposals,
            proposedCount,
            executedCount,
            skippedCount,
            failedCount);
    }
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
    int ProposedCount,
    int ExecutedCount,
    int SkippedCount,
    int FailedCount);
