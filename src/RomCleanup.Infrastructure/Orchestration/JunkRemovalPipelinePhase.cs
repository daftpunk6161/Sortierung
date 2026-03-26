using RomCleanup.Contracts.Models;

namespace RomCleanup.Infrastructure.Orchestration;

/// <summary>
/// Pipeline phase that removes standalone junk winners (single-member junk groups).
/// </summary>
public sealed class JunkRemovalPipelinePhase : IPipelinePhase<JunkRemovalPhaseInput, JunkRemovalPhaseOutput>
{
    public string Name => "JunkRemoval";

    public JunkRemovalPhaseOutput Execute(JunkRemovalPhaseInput input, PipelineContext context, CancellationToken cancellationToken)
    {
        context.Metrics.StartPhase(Name);
        context.OnProgress?.Invoke("[Junk] Entferne Junk-Dateien…");

        var junkToRemove = input.Groups
            .Where(g => g.Losers.Count == 0 && g.Winner.Category == FileCategory.Junk)
            .Select(g => g.Winner)
            .ToList();

        var removedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int moveCount = 0;
        int failCount = 0;
        long savedBytes = 0;

        foreach (var junk in junkToRemove)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var root = PipelinePhaseHelpers.FindRootForPath(junk.MainPath, input.Options.Roots);
            if (root is null)
            {
                failCount++;
                continue;
            }

            var trashBase = string.IsNullOrEmpty(input.Options.TrashRoot) ? root : input.Options.TrashRoot;
            var trashDir = Path.Combine(trashBase, "_TRASH_JUNK");
            context.FileSystem.EnsureDirectory(trashDir);

            var fileName = Path.GetFileName(junk.MainPath);
            var destPath = context.FileSystem.ResolveChildPathWithinRoot(trashBase, Path.Combine("_TRASH_JUNK", fileName));
            if (destPath is null)
            {
                failCount++;
                continue;
            }

            var actualDest = context.FileSystem.MoveItemSafely(junk.MainPath, destPath);
            if (actualDest is null)
            {
                failCount++;
                continue;
            }

            moveCount++;
            savedBytes += junk.SizeBytes;
            removedPaths.Add(junk.MainPath);

            if (!string.IsNullOrEmpty(input.Options.AuditPath))
            {
                context.AuditStore.AppendAuditRow(input.Options.AuditPath, root, junk.MainPath, actualDest,
                    "JUNK_REMOVE", "JUNK", "", "junk-removal");
            }
        }

        context.OnProgress?.Invoke($"[Junk] {moveCount} Junk-Dateien entfernt");
        context.Metrics.CompletePhase(moveCount);

        return new JunkRemovalPhaseOutput(new MovePhaseResult(moveCount, failCount, savedBytes), removedPaths);
    }


}

public sealed record JunkRemovalPhaseInput(
    IReadOnlyList<DedupeGroup> Groups,
    RunOptions Options);

public sealed record JunkRemovalPhaseOutput(
    MovePhaseResult MoveResult,
    HashSet<string> RemovedPaths);