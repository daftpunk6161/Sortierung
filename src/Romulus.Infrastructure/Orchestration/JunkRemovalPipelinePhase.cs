using Romulus.Contracts;
using Romulus.Contracts.Models;

namespace Romulus.Infrastructure.Orchestration;

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

        var protectedSetMembers = CollectReferencedSetMemberPaths(input.Groups);

        var junkToRemove = input.Groups
            .Where(g => g.Losers.Count == 0 && g.Winner.Category == FileCategory.Junk)
            .Select(g => g.Winner)
            .Where(winner => !protectedSetMembers.Contains(NormalizePathSafe(winner.MainPath)))
            .ToList();

        var isDryRun = string.Equals(input.Options.Mode, RunConstants.ModeDryRun, StringComparison.OrdinalIgnoreCase);

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
            var fileName = Path.GetFileName(junk.MainPath);
            var destPath = context.FileSystem.ResolveChildPathWithinRoot(trashBase, Path.Combine(RunConstants.WellKnownFolders.TrashJunk, fileName));
            if (destPath is null)
            {
                failCount++;
                continue;
            }

            if (isDryRun)
            {
                if (!string.IsNullOrEmpty(input.Options.AuditPath))
                {
                    context.AuditStore.AppendAuditRow(
                        input.Options.AuditPath,
                        root,
                        junk.MainPath,
                        destPath,
                        RunConstants.AuditActions.JunkPreview,
                        "JUNK",
                        "",
                        "junk-preview");
                }

                continue;
            }

            var trashDir = Path.Combine(trashBase, RunConstants.WellKnownFolders.TrashJunk);
            context.FileSystem.EnsureDirectory(trashDir);

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

    private static HashSet<string> CollectReferencedSetMemberPaths(IReadOnlyList<DedupeGroup> groups)
    {
        var referencedMembers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            AddReferencedSetMembers(group.Winner.MainPath, referencedMembers);
            foreach (var loser in group.Losers)
                AddReferencedSetMembers(loser.MainPath, referencedMembers);
        }

        return referencedMembers;
    }

    private static void AddReferencedSetMembers(string path, HashSet<string> referencedMembers)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var extension = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(extension))
            return;

        IReadOnlyList<string> members;
        try
        {
            members = PipelinePhaseHelpers.GetSetMembers(path, extension, includeM3uMembers: true);
        }
        catch
        {
            return;
        }

        foreach (var member in members)
        {
            if (string.IsNullOrWhiteSpace(member))
                continue;

            referencedMembers.Add(NormalizePathSafe(member));
        }
    }

    private static string NormalizePathSafe(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }


}

public sealed record JunkRemovalPhaseInput(
    IReadOnlyList<DedupeGroup> Groups,
    RunOptions Options);

public sealed record JunkRemovalPhaseOutput(
    MovePhaseResult MoveResult,
    HashSet<string> RemovedPaths);