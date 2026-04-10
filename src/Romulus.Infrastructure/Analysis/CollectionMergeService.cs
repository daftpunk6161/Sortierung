using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Index;
using Romulus.Infrastructure.Paths;

namespace Romulus.Infrastructure.Analysis;

public static class CollectionMergeService
{
    public static string CreateDefaultAuditPath(string targetRoot)
    {
        if (string.IsNullOrWhiteSpace(targetRoot))
            throw new ArgumentException("Target root is required.", nameof(targetRoot));

        var normalizedTargetRoot = ArtifactPathResolver.NormalizeRoot(targetRoot);
        var auditDirectory = ArtifactPathResolver.GetSiblingDirectory(normalizedTargetRoot, AppIdentity.ArtifactDirectories.AuditLogs);
        Directory.CreateDirectory(auditDirectory);
        return Path.Combine(auditDirectory, $"collection-merge-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");
    }

    public static async ValueTask<CollectionMergePlanBuildResult> BuildPlanAsync(
        ICollectionIndex? collectionIndex,
        IFileSystem fileSystem,
        CollectionMergeRequest request,
        CancellationToken ct = default)
        => await BuildPlanCoreAsync(collectionIndex, fileSystem, request, includeAllEntries: false, ct).ConfigureAwait(false);

    public static async ValueTask<CollectionMergeApplyResult> ApplyAsync(
        ICollectionIndex? collectionIndex,
        IFileSystem fileSystem,
        IAuditStore auditStore,
        CollectionMergeApplyRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(auditStore);
        ArgumentNullException.ThrowIfNull(request);

        var auditPath = string.IsNullOrWhiteSpace(request.AuditPath)
            ? CreateDefaultAuditPath(request.MergeRequest.TargetRoot)
            : Path.GetFullPath(request.AuditPath.Trim());

        var normalizedRequest = request with { AuditPath = auditPath };
        var planBuild = await BuildPlanCoreAsync(collectionIndex, fileSystem, request.MergeRequest, includeAllEntries: true, ct).ConfigureAwait(false);
        if (!planBuild.CanUse || planBuild.Plan is null)
        {
            return new CollectionMergeApplyResult
            {
                Request = normalizedRequest,
                AuditPath = auditPath,
                BlockedReason = planBuild.Reason
            };
        }

        var plan = planBuild.Plan;
        var results = new List<CollectionMergeApplyEntryResult>(plan.Entries.Count);
        var wroteAudit = false;

        if (plan.Summary.MutatingEntries > 0)
        {
            auditStore.Flush(auditPath);
            auditStore.WriteMetadataSidecar(auditPath, BuildAuditMetadata(plan, summary: null));
            wroteAudit = true;
        }

        foreach (var entry in plan.Entries)
        {
            ct.ThrowIfCancellationRequested();

            switch (entry.Decision)
            {
                case CollectionMergeDecision.KeepExistingTarget:
                    results.Add(ToApplyResult(entry, CollectionMergeApplyOutcome.KeptExistingTarget, entry.ReasonCode));
                    break;
                case CollectionMergeDecision.SkipAsDuplicate:
                    results.Add(ToApplyResult(entry, CollectionMergeApplyOutcome.SkippedAsDuplicate, entry.ReasonCode));
                    break;
                case CollectionMergeDecision.ReviewRequired:
                    results.Add(ToApplyResult(entry, CollectionMergeApplyOutcome.ReviewRequired, entry.ReasonCode));
                    break;
                case CollectionMergeDecision.Blocked:
                    results.Add(ToApplyResult(entry, CollectionMergeApplyOutcome.Blocked, entry.ReasonCode));
                    break;
                case CollectionMergeDecision.CopyToTarget:
                case CollectionMergeDecision.MoveToTarget:
                    results.Add(await ExecuteMutatingEntryAsync(
                        collectionIndex,
                        fileSystem,
                        auditStore,
                        auditPath,
                        entry,
                        ct).ConfigureAwait(false));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(entry.Decision), entry.Decision, "Unknown collection merge decision.");
            }
        }

        var summary = CollectionMergeApplySummary.FromEntries(results);
        if (wroteAudit)
        {
            auditStore.Flush(auditPath);
            auditStore.WriteMetadataSidecar(auditPath, BuildAuditMetadata(plan, summary));
        }

        return new CollectionMergeApplyResult
        {
            Request = normalizedRequest,
            Plan = plan,
            Summary = summary,
            Entries = results,
            AuditPath = wroteAudit ? auditPath : string.Empty,
            RollbackAvailable = wroteAudit && summary.Applied > 0
        };
    }

    private static async ValueTask<CollectionMergePlanBuildResult> BuildPlanCoreAsync(
        ICollectionIndex? collectionIndex,
        IFileSystem fileSystem,
        CollectionMergeRequest request,
        bool includeAllEntries,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.TargetRoot))
            return CollectionMergePlanBuildResult.Unavailable("target root is required");

        var normalizedTargetRoot = ArtifactPathResolver.NormalizeRoot(request.TargetRoot);
        var fullCompareRequest = request.CompareRequest with
        {
            Offset = 0,
            Limit = int.MaxValue
        };

        var compareBuild = await CollectionCompareService.CompareUnpagedAsync(
            collectionIndex,
            fileSystem,
            fullCompareRequest,
            ct).ConfigureAwait(false);
        if (!compareBuild.CanUse || compareBuild.Result is null)
            return CollectionMergePlanBuildResult.Unavailable(compareBuild.Reason ?? "collection compare unavailable");

        var compareResult = compareBuild.Result;
        var targetScope = BuildTargetScope(compareResult.Request, normalizedTargetRoot);
        var targetMaterialization = await CollectionCompareService.TryMaterializeSourceAsync(
            collectionIndex,
            fileSystem,
            targetScope,
            ct).ConfigureAwait(false);
        var targetIndex = targetMaterialization.CanUse
            ? TargetEntryIndex.Create(targetMaterialization.Entries)
            : TargetEntryIndex.Empty;

        var allEntries = compareResult.Entries
            .OrderBy(static entry => entry.DiffKey, StringComparer.Ordinal)
            .Select(entry => BuildPlanEntry(entry, normalizedTargetRoot, request.AllowMoves, targetIndex, fileSystem))
            .ToArray();

        var requestedOffset = Math.Max(request.CompareRequest.Offset, 0);
        var requestedLimit = request.CompareRequest.Limit <= 0 ? 500 : request.CompareRequest.Limit;
        var normalizedPreviewRequest = compareResult.Request with
        {
            Offset = requestedOffset,
            Limit = requestedLimit
        };

        var returnedEntries = includeAllEntries
            ? allEntries
            : allEntries
                .Skip(requestedOffset)
                .Take(requestedLimit)
                .ToArray();

        return CollectionMergePlanBuildResult.Success(new CollectionMergePlan
        {
            Request = request with
            {
                TargetRoot = normalizedTargetRoot,
                CompareRequest = normalizedPreviewRequest
            },
            Summary = CollectionMergePlanSummary.FromEntries(allEntries),
            Entries = returnedEntries
        }, compareBuild.Source);
    }

    private static CollectionSourceScope BuildTargetScope(CollectionCompareRequest compareRequest, string targetRoot)
    {
        var extensions = compareRequest.Left.Extensions
            .Concat(compareRequest.Right.Extensions)
            .Where(static extension => !string.IsNullOrWhiteSpace(extension))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var leftFingerprint = compareRequest.Left.EnrichmentFingerprint?.Trim() ?? string.Empty;
        var rightFingerprint = compareRequest.Right.EnrichmentFingerprint?.Trim() ?? string.Empty;
        var effectiveFingerprint = string.Equals(leftFingerprint, rightFingerprint, StringComparison.Ordinal)
            ? leftFingerprint
            : string.Empty;

        return new CollectionSourceScope
        {
            SourceId = "target",
            Label = "Target",
            Roots = [targetRoot],
            Extensions = extensions,
            RootFingerprint = ArtifactPathResolver.ComputeRootsFingerprint([targetRoot]),
            EnrichmentFingerprint = effectiveFingerprint
        };
    }

    private static CollectionMergePlanEntry BuildPlanEntry(
        CollectionDiffEntry diffEntry,
        string targetRoot,
        bool allowMoves,
        TargetEntryIndex targetIndex,
        IFileSystem fileSystem)
    {
        var sourceSelection = SelectSource(diffEntry, targetRoot);
        if (sourceSelection.ReviewReason is not null)
        {
            return new CollectionMergePlanEntry
            {
                PlanEntryId = diffEntry.DiffKey,
                DiffKey = diffEntry.DiffKey,
                Decision = CollectionMergeDecision.ReviewRequired,
                ReviewRequired = true,
                ReasonCode = sourceSelection.ReviewReason,
                TargetRoot = targetRoot,
                SourceSide = sourceSelection.SourceSide,
                Source = sourceSelection.Source,
                ExistingTarget = sourceSelection.SourceSide == CollectionCompareSide.Left ? diffEntry.Right : diffEntry.Left
            };
        }

        var source = sourceSelection.Source;
        if (source is null)
        {
            return new CollectionMergePlanEntry
            {
                PlanEntryId = diffEntry.DiffKey,
                DiffKey = diffEntry.DiffKey,
                Decision = CollectionMergeDecision.Blocked,
                TargetRoot = targetRoot,
                ReasonCode = "merge-missing-source"
            };
        }

        var targetPath = TryResolveTargetPath(fileSystem, targetRoot, source);
        if (targetPath is null)
        {
            return new CollectionMergePlanEntry
            {
                PlanEntryId = diffEntry.DiffKey,
                DiffKey = diffEntry.DiffKey,
                Decision = CollectionMergeDecision.Blocked,
                TargetRoot = targetRoot,
                SourceSide = sourceSelection.SourceSide,
                Source = source,
                ReasonCode = "merge-target-path-blocked"
            };
        }

        var normalizedTargetPath = Path.GetFullPath(targetPath);
        if (string.Equals(Path.GetFullPath(source.Path), normalizedTargetPath, StringComparison.OrdinalIgnoreCase))
        {
            return new CollectionMergePlanEntry
            {
                PlanEntryId = diffEntry.DiffKey,
                DiffKey = diffEntry.DiffKey,
                Decision = CollectionMergeDecision.KeepExistingTarget,
                SourceSide = sourceSelection.SourceSide,
                ReasonCode = "merge-source-already-in-target",
                TargetRoot = targetRoot,
                TargetPath = normalizedTargetPath,
                Source = source,
                ExistingTarget = source
            };
        }

        if (targetIndex.TryGetExactPath(normalizedTargetPath, out var exactTarget))
        {
            if (CollectionCompareService.AreEntriesIdentical(source, exactTarget))
            {
                return new CollectionMergePlanEntry
                {
                    PlanEntryId = diffEntry.DiffKey,
                    DiffKey = diffEntry.DiffKey,
                    Decision = CollectionMergeDecision.KeepExistingTarget,
                    SourceSide = sourceSelection.SourceSide,
                    ReasonCode = "merge-target-already-identical",
                    TargetRoot = targetRoot,
                    TargetPath = normalizedTargetPath,
                    Source = source,
                    ExistingTarget = exactTarget
                };
            }

            return new CollectionMergePlanEntry
            {
                PlanEntryId = diffEntry.DiffKey,
                DiffKey = diffEntry.DiffKey,
                Decision = CollectionMergeDecision.ReviewRequired,
                SourceSide = sourceSelection.SourceSide,
                ReviewRequired = true,
                ReasonCode = "merge-target-conflict-existing",
                TargetRoot = targetRoot,
                TargetPath = normalizedTargetPath,
                Source = source,
                ExistingTarget = exactTarget
            };
        }

        if (fileSystem.TestPath(normalizedTargetPath))
        {
            return new CollectionMergePlanEntry
            {
                PlanEntryId = diffEntry.DiffKey,
                DiffKey = diffEntry.DiffKey,
                Decision = CollectionMergeDecision.ReviewRequired,
                SourceSide = sourceSelection.SourceSide,
                ReviewRequired = true,
                ReasonCode = "merge-target-conflict-unindexed",
                TargetRoot = targetRoot,
                TargetPath = normalizedTargetPath,
                Source = source
            };
        }

        if (targetIndex.TryFindDuplicate(source, out var duplicateTarget))
        {
            return new CollectionMergePlanEntry
            {
                PlanEntryId = diffEntry.DiffKey,
                DiffKey = diffEntry.DiffKey,
                Decision = CollectionMergeDecision.SkipAsDuplicate,
                SourceSide = sourceSelection.SourceSide,
                ReasonCode = "merge-target-has-duplicate",
                TargetRoot = targetRoot,
                TargetPath = normalizedTargetPath,
                Source = source,
                ExistingTarget = duplicateTarget
            };
        }

        return new CollectionMergePlanEntry
        {
            PlanEntryId = diffEntry.DiffKey,
            DiffKey = diffEntry.DiffKey,
            Decision = allowMoves ? CollectionMergeDecision.MoveToTarget : CollectionMergeDecision.CopyToTarget,
            SourceSide = sourceSelection.SourceSide,
            ReasonCode = allowMoves ? "merge-move-to-target" : "merge-copy-to-target",
            TargetRoot = targetRoot,
            TargetPath = normalizedTargetPath,
            Source = source
        };
    }

    private static string? TryResolveTargetPath(IFileSystem fileSystem, string targetRoot, CollectionIndexEntry source)
    {
        if (string.IsNullOrWhiteSpace(source.Path) || string.IsNullOrWhiteSpace(source.Root))
            return null;

        var relativePath = Path.GetRelativePath(source.Root, source.Path);
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            return null;

        var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Any(static segment => segment == ".."))
            return null;

        return fileSystem.ResolveChildPathWithinRoot(targetRoot, relativePath);
    }

    private static SourceSelection SelectSource(CollectionDiffEntry diffEntry, string targetRoot)
    {
        ArgumentNullException.ThrowIfNull(diffEntry);

        return diffEntry.State switch
        {
            CollectionDiffState.OnlyInLeft => new SourceSelection(diffEntry.Left, CollectionCompareSide.Left),
            CollectionDiffState.OnlyInRight => new SourceSelection(diffEntry.Right, CollectionCompareSide.Right),
            CollectionDiffState.LeftPreferred => new SourceSelection(diffEntry.Left, CollectionCompareSide.Left),
            CollectionDiffState.RightPreferred => new SourceSelection(diffEntry.Right, CollectionCompareSide.Right),
            CollectionDiffState.PresentInBothIdentical => SelectIdenticalSource(diffEntry, targetRoot),
            CollectionDiffState.PresentInBothDifferent => new SourceSelection(null, null, "merge-review-different-no-preference"),
            CollectionDiffState.ReviewRequired => new SourceSelection(null, diffEntry.PreferredSide, diffEntry.ReasonCode),
            _ => new SourceSelection(null, null, "merge-blocked-unsupported-state")
        };
    }

    private static SourceSelection SelectIdenticalSource(CollectionDiffEntry diffEntry, string targetRoot)
    {
        if (diffEntry.Left is not null
            && string.Equals(ArtifactPathResolver.NormalizeRoot(diffEntry.Left.Root), targetRoot, StringComparison.OrdinalIgnoreCase))
        {
            return new SourceSelection(diffEntry.Left, CollectionCompareSide.Left);
        }

        if (diffEntry.Right is not null
            && string.Equals(ArtifactPathResolver.NormalizeRoot(diffEntry.Right.Root), targetRoot, StringComparison.OrdinalIgnoreCase))
        {
            return new SourceSelection(diffEntry.Right, CollectionCompareSide.Right);
        }

        return diffEntry.Left is not null
            ? new SourceSelection(diffEntry.Left, CollectionCompareSide.Left)
            : new SourceSelection(diffEntry.Right, CollectionCompareSide.Right);
    }

    private static CollectionMergeApplyEntryResult ToApplyResult(
        CollectionMergePlanEntry entry,
        CollectionMergeApplyOutcome outcome,
        string reasonCode)
        => new()
        {
            PlanEntryId = entry.PlanEntryId,
            DiffKey = entry.DiffKey,
            Decision = entry.Decision,
            Outcome = outcome,
            SourceSide = entry.SourceSide,
            ReasonCode = reasonCode,
            SourcePath = entry.Source?.Path,
            TargetPath = entry.TargetPath
        };

    private static async ValueTask<CollectionMergeApplyEntryResult> ExecuteMutatingEntryAsync(
        ICollectionIndex? collectionIndex,
        IFileSystem fileSystem,
        IAuditStore auditStore,
        string auditPath,
        CollectionMergePlanEntry entry,
        CancellationToken ct)
    {
        var source = entry.Source;
        if (source is null || string.IsNullOrWhiteSpace(entry.TargetPath))
            return ToApplyResult(entry, CollectionMergeApplyOutcome.Failed, "merge-apply-missing-source-or-target");

        var targetPath = entry.TargetPath;
        var targetRoot = entry.TargetRoot;
        if (string.IsNullOrWhiteSpace(targetRoot))
            return ToApplyResult(entry, CollectionMergeApplyOutcome.Failed, "merge-apply-missing-target-root");
        var rootPath = Path.GetFullPath(targetRoot);
        var category = source.Category.ToString().ToUpperInvariant();
        var hash = source.PrimaryHash ?? string.Empty;

        auditStore.AppendAuditRow(
            auditPath,
            rootPath,
            source.Path,
            targetPath,
            entry.Decision == CollectionMergeDecision.CopyToTarget ? RunConstants.AuditActions.CopyPending : RunConstants.AuditActions.MovePending,
            category,
            hash,
            entry.ReasonCode + ":pending");
        auditStore.Flush(auditPath);

        try
        {
            if (entry.Decision == CollectionMergeDecision.CopyToTarget)
            {
                fileSystem.CopyFile(source.Path, targetPath, overwrite: false);
                if (!fileSystem.TestPath(targetPath, "Leaf"))
                    throw new IOException("Copy completed without a target artifact.");
            }
            else
            {
                var actualTarget = fileSystem.MoveItemSafely(source.Path, targetPath, targetRoot);
                if (actualTarget is null || !fileSystem.TestPath(actualTarget, "Leaf"))
                    throw new IOException("Move did not produce a verified target artifact.");
                targetPath = actualTarget;
            }

            await PersistIndexMutationAsync(collectionIndex, source, targetPath, targetRoot, entry.Decision, ct).ConfigureAwait(false);

            auditStore.AppendAuditRow(
                auditPath,
                rootPath,
                source.Path,
                targetPath,
                entry.Decision == CollectionMergeDecision.CopyToTarget ? RunConstants.AuditActions.Copy : RunConstants.AuditActions.Move,
                category,
                hash,
                entry.ReasonCode);

            return new CollectionMergeApplyEntryResult
            {
                PlanEntryId = entry.PlanEntryId,
                DiffKey = entry.DiffKey,
                Decision = entry.Decision,
                Outcome = CollectionMergeApplyOutcome.Applied,
                SourceSide = entry.SourceSide,
                ReasonCode = entry.ReasonCode,
                SourcePath = source.Path,
                TargetPath = targetPath
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or FileNotFoundException)
        {
            TryRevertFailedMutation(fileSystem, source.Path, targetPath, entry.Decision);
            if (collectionIndex is not null)
                await TryRevertIndexMutationAsync(collectionIndex, source, targetPath, entry.Decision, ct).ConfigureAwait(false);
            return ToApplyResult(entry, CollectionMergeApplyOutcome.Failed, $"{entry.ReasonCode}:apply-failed");
        }
    }

    private static async ValueTask PersistIndexMutationAsync(
        ICollectionIndex? collectionIndex,
        CollectionIndexEntry source,
        string targetPath,
        string targetRoot,
        CollectionMergeDecision decision,
        CancellationToken ct)
    {
        if (collectionIndex is null)
            return;

        var normalizedTargetPath = Path.GetFullPath(targetPath);
        var normalizedTargetRoot = ArtifactPathResolver.NormalizeRoot(targetRoot);
        var targetEntry = source with
        {
            Path = normalizedTargetPath,
            Root = normalizedTargetRoot,
            FileName = Path.GetFileName(normalizedTargetPath)
        };

        await collectionIndex.UpsertEntriesAsync([targetEntry], ct).ConfigureAwait(false);
        if (decision == CollectionMergeDecision.MoveToTarget)
            await collectionIndex.RemovePathsAsync([source.Path], ct).ConfigureAwait(false);
    }

    private static async ValueTask TryRevertIndexMutationAsync(
        ICollectionIndex? collectionIndex,
        CollectionIndexEntry source,
        string targetPath,
        CollectionMergeDecision decision,
        CancellationToken ct)
    {
        if (collectionIndex is null)
            return;

        try
        {
            await collectionIndex.RemovePathsAsync([targetPath], ct).ConfigureAwait(false);
            if (decision == CollectionMergeDecision.MoveToTarget)
                await collectionIndex.UpsertEntriesAsync([source], ct).ConfigureAwait(false);
        }
        catch
        {
            // Best effort only. The failed apply result already signals that operator review is required.
        }
    }

    private static void TryRevertFailedMutation(
        IFileSystem fileSystem,
        string sourcePath,
        string targetPath,
        CollectionMergeDecision decision)
    {
        try
        {
            if (decision == CollectionMergeDecision.CopyToTarget)
            {
                if (fileSystem.TestPath(targetPath, "Leaf"))
                    fileSystem.DeleteFile(targetPath);
                return;
            }

            if (fileSystem.TestPath(targetPath, "Leaf") && !fileSystem.TestPath(sourcePath, "Leaf"))
                _ = fileSystem.MoveItemSafely(targetPath, sourcePath);
        }
        catch
        {
            // Best effort cleanup only; the caller already reports a failed apply result.
        }
    }

    private static IDictionary<string, object> BuildAuditMetadata(
        CollectionMergePlan plan,
        CollectionMergeApplySummary? summary)
    {
        var restoreRoots = plan.Request.CompareRequest.Left.Roots
            .Concat(plan.Request.CompareRequest.Right.Roots)
            .Append(plan.Request.TargetRoot)
            .Where(static root => !string.IsNullOrWhiteSpace(root))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static root => root, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var metadata = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["Mode"] = "CollectionMerge",
            ["TargetRoot"] = plan.Request.TargetRoot,
            ["AllowMoves"] = plan.Request.AllowMoves,
            ["AllowedRestoreRoots"] = restoreRoots,
            ["AllowedCurrentRoots"] = restoreRoots,
            ["PlanTotalEntries"] = plan.Summary.TotalEntries,
            ["PlanMutatingEntries"] = plan.Summary.MutatingEntries
        };

        if (summary is not null)
        {
            metadata["Applied"] = summary.Applied;
            metadata["Copied"] = summary.Copied;
            metadata["Moved"] = summary.Moved;
            metadata["Failed"] = summary.Failed;
        }

        return metadata;
    }

    private sealed record SourceSelection(
        CollectionIndexEntry? Source,
        CollectionCompareSide? SourceSide,
        string? ReviewReason = null);

    private sealed class TargetEntryIndex
    {
        private readonly Dictionary<string, CollectionIndexEntry> _byPath;
        private readonly Dictionary<string, CollectionIndexEntry> _byPrimaryHash;
        private readonly Dictionary<string, CollectionIndexEntry> _byHeaderlessHash;
        private readonly Dictionary<string, CollectionIndexEntry> _byIdentitySignature;

        public static TargetEntryIndex Empty { get; } = new([], [], [], []);

        private TargetEntryIndex(
            Dictionary<string, CollectionIndexEntry> byPath,
            Dictionary<string, CollectionIndexEntry> byPrimaryHash,
            Dictionary<string, CollectionIndexEntry> byHeaderlessHash,
            Dictionary<string, CollectionIndexEntry> byIdentitySignature)
        {
            _byPath = byPath;
            _byPrimaryHash = byPrimaryHash;
            _byHeaderlessHash = byHeaderlessHash;
            _byIdentitySignature = byIdentitySignature;
        }

        public static TargetEntryIndex Create(IReadOnlyList<CollectionIndexEntry> entries)
        {
            var byPath = new Dictionary<string, CollectionIndexEntry>(StringComparer.OrdinalIgnoreCase);
            var byPrimaryHash = new Dictionary<string, CollectionIndexEntry>(StringComparer.Ordinal);
            var byHeaderlessHash = new Dictionary<string, CollectionIndexEntry>(StringComparer.OrdinalIgnoreCase);
            var byIdentitySignature = new Dictionary<string, CollectionIndexEntry>(StringComparer.Ordinal);

            foreach (var entry in entries
                         .OrderBy(static item => item.Path, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(static item => item.Path, StringComparer.Ordinal))
            {
                byPath[Path.GetFullPath(entry.Path)] = entry;

                if (!string.IsNullOrWhiteSpace(entry.PrimaryHash))
                {
                    var primaryHashKey = $"{CollectionIndexCandidateMapper.NormalizeHashType(entry.PrimaryHashType)}|{entry.PrimaryHash.Trim().ToLowerInvariant()}";
                    byPrimaryHash.TryAdd(primaryHashKey, entry);
                }

                if (!string.IsNullOrWhiteSpace(entry.HeaderlessHash))
                    byHeaderlessHash.TryAdd(entry.HeaderlessHash.Trim(), entry);

                byIdentitySignature.TryAdd(CollectionCompareService.GetIdentitySignature(entry), entry);
            }

            return new TargetEntryIndex(byPath, byPrimaryHash, byHeaderlessHash, byIdentitySignature);
        }

        public bool TryGetExactPath(string path, out CollectionIndexEntry entry)
            => _byPath.TryGetValue(Path.GetFullPath(path), out entry!);

        public bool TryFindDuplicate(CollectionIndexEntry source, out CollectionIndexEntry entry)
        {
            if (!string.IsNullOrWhiteSpace(source.PrimaryHash))
            {
                var primaryHashKey = $"{CollectionIndexCandidateMapper.NormalizeHashType(source.PrimaryHashType)}|{source.PrimaryHash.Trim().ToLowerInvariant()}";
                if (_byPrimaryHash.TryGetValue(primaryHashKey, out entry!))
                    return true;
            }

            if (!string.IsNullOrWhiteSpace(source.HeaderlessHash)
                && _byHeaderlessHash.TryGetValue(source.HeaderlessHash.Trim(), out entry!))
            {
                return true;
            }

            return _byIdentitySignature.TryGetValue(CollectionCompareService.GetIdentitySignature(source), out entry!);
        }
    }
}

public sealed record CollectionMergePlanBuildResult(
    bool CanUse,
    CollectionMergePlan? Plan,
    string Source,
    string? Reason = null)
{
    public static CollectionMergePlanBuildResult Success(CollectionMergePlan plan, string source)
        => new(true, plan, source);

    public static CollectionMergePlanBuildResult Unavailable(string reason)
        => new(false, null, CollectionMaterializationSources.FallbackRun, reason);
}
