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
        var sidecarPrimed = false;
        var hasAuditPath = !string.IsNullOrEmpty(input.Options.AuditPath);

        if (hasAuditPath)
        {
            context.AuditStore.Flush(input.Options.AuditPath!);
            context.AuditStore.WriteMetadataSidecar(input.Options.AuditPath!, new Dictionary<string, object>
            {
                ["PreMoveCheckpoint"] = true,
                ["MoveCount"] = 0
            });
            sidecarPrimed = true;
        }

        // TASK-168: Build set-membership map so descriptor moves include their members.
        // This prevents orphaned BIN/TRACK files when their CUE/GDI/CCD is moved to trash.
        var alreadyMovedAsSetMember = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
                    if (hasAuditPath)
                    {
                        context.AuditStore.AppendAuditRow(input.Options.AuditPath!, root, loser.MainPath, destPath,
                            "SKIP", loser.Category.ToString().ToUpperInvariant(), "", "conflict-policy:skip");
                    }

                    skipCount++;
                    if (processedLosers % 100 == 0 || processedLosers == totalLosers)
                        context.OnProgress?.Invoke($"[Move] Fortschritt: {processedLosers}/{totalLosers} (moved={moveCount}, skipped={skipCount}, failed={failCount})");
                    continue;
                }

                // TASK-147: Write-Ahead Audit Pattern — write planned audit row BEFORE move.
                // If move fails, the row is already there with "PENDING" status.
                // On success, we append the definitive row. On failure, the pending row
                // ensures rollback discovery even if the process crashes mid-move.
                if (hasAuditPath)
                {
                    context.AuditStore.AppendAuditRow(input.Options.AuditPath!, root, loser.MainPath, destPath,
                        "MOVE_PENDING", loser.Category.ToString().ToUpperInvariant(), "", "region-dedupe:write-ahead");
                    context.AuditStore.Flush(input.Options.AuditPath!);
                }

                // TASK-168: Resolve set members BEFORE moving the descriptor,
                // because CueSetParser/GdiSetParser read the descriptor file to find members.
                // After the move, the descriptor is gone from the original path and parsing would fail.
                IReadOnlyList<string> setMembers = Array.Empty<string>();
                var ext = Path.GetExtension(loser.MainPath);
                if (!string.IsNullOrEmpty(ext))
                {
                    setMembers = PipelinePhaseHelpers.GetSetMembers(loser.MainPath, ext.ToLowerInvariant());
                }

                var actualDest = context.FileSystem.MoveItemSafely(loser.MainPath, destPath);
                if (actualDest is not null)
                {
                    moveCount++;
                    savedBytes += loser.SizeBytes;

                    if (hasAuditPath)
                    {
                        // Write definitive audit row with actual destination (may differ due to DUP suffix)
                        context.AuditStore.AppendAuditRow(input.Options.AuditPath!, root, loser.MainPath, actualDest,
                            "Move", loser.Category.ToString().ToUpperInvariant(), "", "region-dedupe");
                    }

                    // TASK-168: Co-move set members (BIN/TRACK files) when their descriptor (CUE/GDI/CCD/M3U) is moved.
                    // This prevents orphaned members from being left behind in the source directory.
                    foreach (var member in setMembers)
                        {
                            if (alreadyMovedAsSetMember.Contains(member) || !File.Exists(member))
                                continue;

                            var memberFileName = Path.GetFileName(member);
                            var memberDest = context.FileSystem.ResolveChildPathWithinRoot(
                                trashBase, Path.Combine("_TRASH_REGION_DEDUPE", memberFileName));

                            if (memberDest is null) continue;

                            if (hasAuditPath)
                            {
                                context.AuditStore.AppendAuditRow(input.Options.AuditPath!, root, member, memberDest,
                                    "MOVE_PENDING", "SET_MEMBER", "", "region-dedupe:set-member:write-ahead");
                                context.AuditStore.Flush(input.Options.AuditPath!);
                            }

                            var memberActual = context.FileSystem.MoveItemSafely(member, memberDest);
                            if (memberActual is not null)
                            {
                                alreadyMovedAsSetMember.Add(member);
                                moveCount++;

                                if (hasAuditPath)
                                {
                                    context.AuditStore.AppendAuditRow(input.Options.AuditPath!, root, member, memberActual,
                                        "Move", "SET_MEMBER", "", "region-dedupe:set-member");
                                }
                            }
                            else if (hasAuditPath)
                            {
                                context.AuditStore.AppendAuditRow(input.Options.AuditPath!, root, member, memberDest,
                                    "MOVE_FAILED", "SET_MEMBER", "", "region-dedupe:set-member:move-failed");
                            }
                        }

                    if (moveCount % 10 == 0 && hasAuditPath)
                    {
                        context.AuditStore.Flush(input.Options.AuditPath!);
                        context.AuditStore.WriteMetadataSidecar(input.Options.AuditPath!, new Dictionary<string, object>
                        {
                            ["IncrementalFlush"] = true,
                            ["MoveCount"] = moveCount
                        });
                    }
                }
                else
                {
                    failCount++;
                    // TASK-147: Record move failure so the PENDING row can be identified during rollback
                    if (hasAuditPath)
                    {
                        context.AuditStore.AppendAuditRow(input.Options.AuditPath!, root, loser.MainPath, destPath,
                            "MOVE_FAILED", loser.Category.ToString().ToUpperInvariant(), "", "region-dedupe:move-failed");
                    }
                }

                if (processedLosers % 100 == 0 || processedLosers == totalLosers)
                    context.OnProgress?.Invoke($"[Move] Fortschritt: {processedLosers}/{totalLosers} (moved={moveCount}, skipped={skipCount}, failed={failCount})");
            }
        }

        if (hasAuditPath && sidecarPrimed)
        {
            context.AuditStore.Flush(input.Options.AuditPath!);
            context.AuditStore.WriteMetadataSidecar(input.Options.AuditPath!, new Dictionary<string, object>
            {
                ["FinalCheckpoint"] = true,
                ["MoveCount"] = moveCount,
                ["SkipCount"] = skipCount,
                ["FailCount"] = failCount
            });
        }

        return new MovePhaseResult(moveCount, failCount, savedBytes, skipCount);
    }


}

public sealed record MovePhaseInput(
    IReadOnlyList<DedupeGroup> Groups,
    RunOptions Options);