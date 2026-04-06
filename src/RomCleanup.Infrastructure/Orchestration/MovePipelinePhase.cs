using RomCleanup.Contracts;
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
        var movedSourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sidecarPrimed = false;
        var hasAuditPath = !string.IsNullOrEmpty(input.Options.AuditPath);

        if (hasAuditPath)
        {
            context.AuditStore.Flush(input.Options.AuditPath!);
            context.AuditStore.WriteMetadataSidecar(
                input.Options.AuditPath!,
                RomCleanup.Infrastructure.Audit.AuditRollbackRootMetadata.WithAllowedRoots(input.Options, new Dictionary<string, object>
                {
                ["PreMoveCheckpoint"] = true,
                ["MoveCount"] = 0
                }));
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
                var trashDir = Path.Combine(trashBase, RunConstants.WellKnownFolders.TrashRegionDedupe);
                context.FileSystem.EnsureDirectory(trashDir);

                var fileName = Path.GetFileName(loser.MainPath);
                var destPath = context.FileSystem.ResolveChildPathWithinRoot(
                    trashBase, Path.Combine(RunConstants.WellKnownFolders.TrashRegionDedupe, fileName));

                if (destPath is null)
                {
                    failCount++;
                    if (processedLosers % 100 == 0 || processedLosers == totalLosers)
                        context.OnProgress?.Invoke($"[Move] Fortschritt: {processedLosers}/{totalLosers} (moved={moveCount}, skipped={skipCount}, failed={failCount})");
                    continue;
                }

                if (string.Equals(input.Options.ConflictPolicy, "Skip", StringComparison.OrdinalIgnoreCase)
                    && context.FileSystem.FileExists(destPath))
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
                        RunConstants.AuditActions.MovePending, loser.Category.ToString().ToUpperInvariant(), "", "region-dedupe:write-ahead");
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

                var plannedMemberMoves = new List<(string SourcePath, string DestPath, long SizeBytes)>();
                var setPreflightFailed = false;
                foreach (var member in setMembers)
                {
                    if (alreadyMovedAsSetMember.Contains(member))
                        continue;

                    if (!context.FileSystem.FileExists(member))
                    {
                        setPreflightFailed = true;
                        break;
                    }

                    var memberRoot = PipelinePhaseHelpers.FindRootForPath(member, input.Options.Roots);
                    if (memberRoot is null)
                    {
                        setPreflightFailed = true;
                        break;
                    }

                    var memberFileName = Path.GetFileName(member);
                    var memberDest = context.FileSystem.ResolveChildPathWithinRoot(
                        trashBase, Path.Combine(RunConstants.WellKnownFolders.TrashRegionDedupe, memberFileName));

                    if (memberDest is null)
                    {
                        setPreflightFailed = true;
                        break;
                    }

                    if (string.Equals(input.Options.ConflictPolicy, "Skip", StringComparison.OrdinalIgnoreCase)
                        && context.FileSystem.FileExists(memberDest))
                    {
                        setPreflightFailed = true;
                        break;
                    }

                    long memberSizeBytes;
                    try
                    {
                        memberSizeBytes = new FileInfo(member).Length;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        setPreflightFailed = true;
                        break;
                    }

                    plannedMemberMoves.Add((member, memberDest, memberSizeBytes));
                }

                if (setPreflightFailed)
                {
                    failCount++;
                    if (hasAuditPath)
                    {
                        context.AuditStore.AppendAuditRow(input.Options.AuditPath!, root, loser.MainPath, destPath,
                            "MOVE_FAILED", loser.Category.ToString().ToUpperInvariant(), "", "region-dedupe:set-member-preflight-failed");
                    }

                    if (processedLosers % 100 == 0 || processedLosers == totalLosers)
                        context.OnProgress?.Invoke($"[Move] Fortschritt: {processedLosers}/{totalLosers} (moved={moveCount}, skipped={skipCount}, failed={failCount})");
                    continue;
                }

                var actualDest = context.FileSystem.MoveItemSafely(loser.MainPath, destPath);
                if (actualDest is not null)
                {
                    var movedItems = new List<(string SourcePath, string ActualDestPath, string Category)>();
                    movedItems.Add((loser.MainPath, actualDest, loser.Category.ToString().ToUpperInvariant()));

                    var memberMoveFailed = false;
                    foreach (var plannedMemberMove in plannedMemberMoves)
                    {
                        if (hasAuditPath)
                        {
                            context.AuditStore.AppendAuditRow(input.Options.AuditPath!, root, plannedMemberMove.SourcePath, plannedMemberMove.DestPath,
                                RunConstants.AuditActions.MovePending, "SET_MEMBER", "", "region-dedupe:set-member:write-ahead");
                            context.AuditStore.Flush(input.Options.AuditPath!);
                        }

                        var memberActual = context.FileSystem.MoveItemSafely(plannedMemberMove.SourcePath, plannedMemberMove.DestPath);
                        if (memberActual is null)
                        {
                            memberMoveFailed = true;
                            if (hasAuditPath)
                            {
                                context.AuditStore.AppendAuditRow(input.Options.AuditPath!, root, plannedMemberMove.SourcePath, plannedMemberMove.DestPath,
                                    "MOVE_FAILED", "SET_MEMBER", "", "region-dedupe:set-member:move-failed");
                            }
                            break;
                        }

                        movedItems.Add((plannedMemberMove.SourcePath, memberActual, "SET_MEMBER"));
                    }

                    if (memberMoveFailed)
                    {
                        var rollbackFailures = new List<string>();
                        foreach (var movedItem in movedItems.AsEnumerable().Reverse())
                        {
                            var restoredPath = context.FileSystem.MoveItemSafely(movedItem.ActualDestPath, movedItem.SourcePath);
                            if (string.IsNullOrWhiteSpace(restoredPath)
                                || !string.Equals(restoredPath, movedItem.SourcePath, StringComparison.OrdinalIgnoreCase))
                            {
                                rollbackFailures.Add($"{movedItem.ActualDestPath} -> {movedItem.SourcePath}");
                            }
                        }

                        failCount++;
                        if (hasAuditPath)
                        {
                            var reason = rollbackFailures.Count == 0
                                ? "region-dedupe:set-member-rollback"
                                : $"region-dedupe:set-member-rollback-partial:{rollbackFailures.Count}";
                            context.AuditStore.AppendAuditRow(input.Options.AuditPath!, root, loser.MainPath, destPath,
                                "MOVE_FAILED", loser.Category.ToString().ToUpperInvariant(), "", reason);
                        }

                        if (rollbackFailures.Count > 0)
                        {
                            context.OnProgress?.Invoke(
                                $"WARNING: Set-member rollback incomplete for {Path.GetFileName(loser.MainPath)} ({rollbackFailures.Count} restore failure(s)).");
                        }
                    }
                    else
                    {
                        moveCount += movedItems.Count;
                        savedBytes += loser.SizeBytes + plannedMemberMoves.Sum(static m => m.SizeBytes);
                        foreach (var movedItem in movedItems)
                            movedSourcePaths.Add(movedItem.SourcePath);

                        foreach (var movedItem in movedItems)
                        {
                            if (hasAuditPath)
                            {
                                var reason = string.Equals(movedItem.Category, "SET_MEMBER", StringComparison.OrdinalIgnoreCase)
                                    ? "region-dedupe:set-member"
                                    : "region-dedupe";
                                context.AuditStore.AppendAuditRow(input.Options.AuditPath!, root, movedItem.SourcePath, movedItem.ActualDestPath,
                                    RunConstants.AuditActions.Move, movedItem.Category, "", reason);
                            }
                        }

                        foreach (var plannedMemberMove in plannedMemberMoves)
                        {
                            alreadyMovedAsSetMember.Add(plannedMemberMove.SourcePath);
                        }

                        if (moveCount % 10 == 0 && hasAuditPath)
                        {
                            context.AuditStore.Flush(input.Options.AuditPath!);
                            context.AuditStore.WriteMetadataSidecar(
                                input.Options.AuditPath!,
                                RomCleanup.Infrastructure.Audit.AuditRollbackRootMetadata.WithAllowedRoots(input.Options, new Dictionary<string, object>
                                {
                                ["IncrementalFlush"] = true,
                                ["MoveCount"] = moveCount
                                }));
                        }
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
            context.AuditStore.WriteMetadataSidecar(
                input.Options.AuditPath!,
                RomCleanup.Infrastructure.Audit.AuditRollbackRootMetadata.WithAllowedRoots(input.Options, new Dictionary<string, object>
                {
                ["FinalCheckpoint"] = true,
                ["MoveCount"] = moveCount,
                ["SkipCount"] = skipCount,
                ["FailCount"] = failCount
                }));
        }

        return new MovePhaseResult(moveCount, failCount, savedBytes, skipCount, movedSourcePaths);
    }


}

public sealed record MovePhaseInput(
    IReadOnlyList<DedupeGroup> Groups,
    RunOptions Options);
