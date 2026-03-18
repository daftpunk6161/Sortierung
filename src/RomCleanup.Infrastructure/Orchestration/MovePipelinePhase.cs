using RomCleanup.Contracts.Models;

namespace RomCleanup.Infrastructure.Orchestration;

/// <summary>
/// Pipeline phase that moves deduplication losers to trash with audit tracking.
/// </summary>
public sealed class MovePipelinePhase : IPipelinePhase<MovePhaseInput, MovePhaseResult>
{
    public string Name => "Move";

    public MovePhaseResult Execute(MovePhaseInput input, PipelineContext context, CancellationToken cancellationToken)
    {
        int moveCount = 0;
        int failCount = 0;
        int skipCount = 0;
        long savedBytes = 0;
        var totalLosers = input.Groups.Sum(g => g.Losers.Count);
        var processedLosers = 0;

        foreach (var group in input.Groups)
        {
            foreach (var loser in group.Losers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                processedLosers++;

                var root = PipelinePhaseHelpers.FindRootForPath(loser.MainPath, input.Options.Roots);
                if (root is null)
                {
                    failCount++;
                    if (processedLosers % 100 == 0 || processedLosers == totalLosers)
                        context.OnProgress?.Invoke($"[Move] Fortschritt: {processedLosers}/{totalLosers} (moved={moveCount}, skipped={skipCount}, failed={failCount})");
                    continue;
                }

                var trashBase = string.IsNullOrEmpty(input.Options.TrashRoot) ? root : input.Options.TrashRoot;
                var trashDir = Path.Combine(trashBase, "_TRASH_REGION_DEDUPE");
                context.FileSystem.EnsureDirectory(trashDir);

                var fileName = Path.GetFileName(loser.MainPath);
                var destPath = context.FileSystem.ResolveChildPathWithinRoot(
                    trashBase, Path.Combine("_TRASH_REGION_DEDUPE", fileName));

                if (destPath is null)
                {
                    failCount++;
                    if (processedLosers % 100 == 0 || processedLosers == totalLosers)
                        context.OnProgress?.Invoke($"[Move] Fortschritt: {processedLosers}/{totalLosers} (moved={moveCount}, skipped={skipCount}, failed={failCount})");
                    continue;
                }

                if (string.Equals(input.Options.ConflictPolicy, "Skip", StringComparison.OrdinalIgnoreCase)
                    && File.Exists(destPath))
                {
                    context.OnProgress?.Invoke($"Skip (conflict): {Path.GetFileName(loser.MainPath)}");
                    if (!string.IsNullOrEmpty(input.Options.AuditPath))
                    {
                        context.AuditStore.AppendAuditRow(input.Options.AuditPath, root, loser.MainPath, destPath,
                            "SKIP", loser.Category.ToString().ToUpperInvariant(), "", "conflict-policy:skip");
                    }

                    skipCount++;
                    if (processedLosers % 100 == 0 || processedLosers == totalLosers)
                        context.OnProgress?.Invoke($"[Move] Fortschritt: {processedLosers}/{totalLosers} (moved={moveCount}, skipped={skipCount}, failed={failCount})");
                    continue;
                }

                var actualDest = context.FileSystem.MoveItemSafely(loser.MainPath, destPath);
                if (actualDest is not null)
                {
                    moveCount++;
                    savedBytes += loser.SizeBytes;

                    if (!string.IsNullOrEmpty(input.Options.AuditPath))
                    {
                        context.AuditStore.AppendAuditRow(input.Options.AuditPath, root, loser.MainPath, actualDest,
                            "Move", loser.Category.ToString().ToUpperInvariant(), "", "region-dedupe");
                    }

                    if (moveCount % 50 == 0 && !string.IsNullOrEmpty(input.Options.AuditPath))
                    {
                        context.AuditStore.Flush(input.Options.AuditPath);
                        context.AuditStore.WriteMetadataSidecar(input.Options.AuditPath, new Dictionary<string, object>
                        {
                            ["IncrementalFlush"] = true,
                            ["MoveCount"] = moveCount
                        });
                    }
                }
                else
                {
                    failCount++;
                }

                if (processedLosers % 100 == 0 || processedLosers == totalLosers)
                    context.OnProgress?.Invoke($"[Move] Fortschritt: {processedLosers}/{totalLosers} (moved={moveCount}, skipped={skipCount}, failed={failCount})");
            }
        }

        return new MovePhaseResult(moveCount, failCount, savedBytes, skipCount);
    }


}

public sealed record MovePhaseInput(
    IReadOnlyList<DedupeResult> Groups,
    RunOptions Options);