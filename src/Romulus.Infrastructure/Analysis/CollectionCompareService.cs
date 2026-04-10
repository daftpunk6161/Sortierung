using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Deduplication;
using Romulus.Infrastructure.Index;
using Romulus.Infrastructure.Paths;

namespace Romulus.Infrastructure.Analysis;

public static class CollectionCompareService
{
    private const string RootFingerprintMismatchMarker = "__root-fingerprint-mismatch__";

    public static async ValueTask<CollectionSourceMaterializationResult> TryMaterializeSourceAsync(
        ICollectionIndex? collectionIndex,
        IFileSystem fileSystem,
        CollectionSourceScope scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(scope);

        var normalizedScope = NormalizeScope(scope);
        if (string.Equals(normalizedScope.RootFingerprint, RootFingerprintMismatchMarker, StringComparison.Ordinal))
        {
            return CollectionSourceMaterializationResult.Unavailable(normalizedScope, "source scope root fingerprint mismatch");
        }

        if (collectionIndex is null)
            return CollectionSourceMaterializationResult.Unavailable(normalizedScope, "collection index unavailable");

        if (normalizedScope.Roots.Count == 0)
            return CollectionSourceMaterializationResult.Unavailable(normalizedScope, "source scope roots are required");

        var scopedEntries = await collectionIndex.ListEntriesInScopeAsync(
            normalizedScope.Roots,
            normalizedScope.Extensions,
            ct).ConfigureAwait(false);
        var scopedPaths = EnumerateScopedPaths(fileSystem, normalizedScope.Roots, normalizedScope.Extensions);

        if (scopedEntries.Count == 0 && scopedPaths.Count == 0)
            return CollectionSourceMaterializationResult.Success(normalizedScope, [], CollectionMaterializationSources.EmptyScope);

        if (scopedEntries.Count == 0)
            return CollectionSourceMaterializationResult.Unavailable(normalizedScope, "collection index has no entries for current scope");

        if (!string.IsNullOrWhiteSpace(normalizedScope.EnrichmentFingerprint)
            && scopedEntries.Any(entry => !string.Equals(entry.EnrichmentFingerprint, normalizedScope.EnrichmentFingerprint, StringComparison.Ordinal)))
        {
            return CollectionSourceMaterializationResult.Unavailable(normalizedScope, "collection index fingerprint mismatch");
        }

        var effectiveFingerprint = ResolveEffectiveEnrichmentFingerprint(normalizedScope, scopedEntries);
        if (effectiveFingerprint is null)
            return CollectionSourceMaterializationResult.Unavailable(normalizedScope, "collection index scope contains mixed enrichment fingerprints");

        if (string.IsNullOrWhiteSpace(effectiveFingerprint))
            return CollectionSourceMaterializationResult.Unavailable(normalizedScope, "collection index entries missing enrichment fingerprint");

        if (scopedEntries.Count != scopedPaths.Count)
            return CollectionSourceMaterializationResult.Unavailable(normalizedScope, "collection index scope does not match filesystem");

        var entryPathSet = scopedEntries
            .Select(static entry => Path.GetFullPath(entry.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!scopedPaths.SetEquals(entryPathSet))
            return CollectionSourceMaterializationResult.Unavailable(normalizedScope, "collection index scope does not match filesystem");

        var finalizedScope = normalizedScope with
        {
            EnrichmentFingerprint = effectiveFingerprint
        };

        return CollectionSourceMaterializationResult.Success(
            finalizedScope,
            scopedEntries
                .OrderBy(static entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static entry => entry.Path, StringComparer.Ordinal)
                .ToArray(),
            CollectionMaterializationSources.CollectionIndex);
    }

    public static async ValueTask<CollectionCompareBuildResult> CompareAsync(
        ICollectionIndex? collectionIndex,
        IFileSystem fileSystem,
        CollectionCompareRequest request,
        CancellationToken ct = default)
        => await CompareCoreAsync(collectionIndex, fileSystem, request, includeAllEntries: false, ct).ConfigureAwait(false);

    internal static async ValueTask<CollectionCompareBuildResult> CompareUnpagedAsync(
        ICollectionIndex? collectionIndex,
        IFileSystem fileSystem,
        CollectionCompareRequest request,
        CancellationToken ct = default)
        => await CompareCoreAsync(collectionIndex, fileSystem, request, includeAllEntries: true, ct).ConfigureAwait(false);

    private static async ValueTask<CollectionCompareBuildResult> CompareCoreAsync(
        ICollectionIndex? collectionIndex,
        IFileSystem fileSystem,
        CollectionCompareRequest request,
        bool includeAllEntries,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(request);

        var left = await TryMaterializeSourceAsync(collectionIndex, fileSystem, request.Left, ct).ConfigureAwait(false);
        if (!left.CanUse)
            return CollectionCompareBuildResult.Unavailable($"left source unavailable: {left.Reason}");

        var right = await TryMaterializeSourceAsync(collectionIndex, fileSystem, request.Right, ct).ConfigureAwait(false);
        if (!right.CanUse)
            return CollectionCompareBuildResult.Unavailable($"right source unavailable: {right.Reason}");

        var normalizedRequest = request with
        {
            Left = left.Scope,
            Right = right.Scope,
            Offset = Math.Max(request.Offset, 0),
            Limit = NormalizeLimit(request.Limit)
        };

        var leftWinners = BuildWinnerMap(left.Entries);
        var rightWinners = BuildWinnerMap(right.Entries);
        var allEntries = BuildDiffEntries(leftWinners, rightWinners);

        var pagedEntries = includeAllEntries
            ? allEntries.ToArray()
            : allEntries
                .Skip(normalizedRequest.Offset)
                .Take(normalizedRequest.Limit)
                .ToArray();

        return CollectionCompareBuildResult.Success(new CollectionCompareResult
        {
            Request = normalizedRequest,
            Summary = CollectionDiffSummary.FromEntries(allEntries),
            Entries = pagedEntries
        }, CollectionMaterializationSources.CollectionIndex);
    }

    public static IReadOnlyList<RomCandidate> MaterializeCandidates(IReadOnlyList<CollectionIndexEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return entries
            .OrderBy(static entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.Path, StringComparer.Ordinal)
            .Select(CollectionIndexCandidateMapper.ToCandidate)
            .ToArray();
    }

    internal static CollectionSourceScope NormalizeScope(CollectionSourceScope scope)
    {
        var normalizedRoots = scope.Roots
            .Where(static root => !string.IsNullOrWhiteSpace(root))
            .Select(ArtifactPathResolver.NormalizeRoot)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static root => root, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static root => root, StringComparer.Ordinal)
            .ToArray();

        var normalizedExtensions = NormalizeExtensions(scope.Extensions);
        var computedRootFingerprint = ArtifactPathResolver.ComputeRootsFingerprint(normalizedRoots);
        var requestedRootFingerprint = string.IsNullOrWhiteSpace(scope.RootFingerprint)
            ? computedRootFingerprint
            : scope.RootFingerprint.Trim();

        if (!string.Equals(requestedRootFingerprint, computedRootFingerprint, StringComparison.Ordinal))
        {
            return scope with
            {
                SourceId = NormalizeSourceId(scope.SourceId),
                Label = NormalizeLabel(scope.Label, scope.SourceId),
                Roots = normalizedRoots,
                Extensions = normalizedExtensions,
                RootFingerprint = RootFingerprintMismatchMarker,
                EnrichmentFingerprint = scope.EnrichmentFingerprint?.Trim() ?? string.Empty
            };
        }

        return scope with
        {
            SourceId = NormalizeSourceId(scope.SourceId),
            Label = NormalizeLabel(scope.Label, scope.SourceId),
            Roots = normalizedRoots,
            Extensions = normalizedExtensions,
            RootFingerprint = computedRootFingerprint,
            EnrichmentFingerprint = scope.EnrichmentFingerprint?.Trim() ?? string.Empty
        };
    }

    internal static string[] NormalizeExtensions(IReadOnlyList<string> extensions)
    {
        var source = extensions.Count > 0
            ? extensions
            : RunOptions.DefaultExtensions;

        return source
            .Where(static extension => !string.IsNullOrWhiteSpace(extension))
            .Select(static extension =>
            {
                var trimmed = extension.Trim();
                return trimmed.StartsWith('.')
                    ? trimmed.ToLowerInvariant()
                    : "." + trimmed.ToLowerInvariant();
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static string NormalizeSourceId(string? sourceId)
        => string.IsNullOrWhiteSpace(sourceId)
            ? "source"
            : sourceId.Trim();

    internal static string NormalizeLabel(string? label, string? sourceId)
        => string.IsNullOrWhiteSpace(label)
            ? NormalizeSourceId(sourceId)
            : label.Trim();

    internal static string? ResolveEffectiveEnrichmentFingerprint(
        CollectionSourceScope scope,
        IReadOnlyList<CollectionIndexEntry> scopedEntries)
    {
        if (!string.IsNullOrWhiteSpace(scope.EnrichmentFingerprint))
        {
            return scopedEntries.All(entry => string.Equals(entry.EnrichmentFingerprint, scope.EnrichmentFingerprint, StringComparison.Ordinal))
                ? scope.EnrichmentFingerprint
                : null;
        }

        var fingerprints = scopedEntries
            .Select(static entry => entry.EnrichmentFingerprint ?? string.Empty)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return fingerprints.Length == 1 ? fingerprints[0] : null;
    }

    private static HashSet<string> EnumerateScopedPaths(
        IFileSystem fileSystem,
        IReadOnlyList<string> roots,
        IReadOnlyCollection<string> extensions)
    {
        var scopedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            foreach (var path in fileSystem.GetFilesSafe(root, extensions))
                scopedPaths.Add(Path.GetFullPath(path));
        }

        return scopedPaths;
    }

    private static Dictionary<string, CollectionIndexEntry> BuildWinnerMap(IReadOnlyList<CollectionIndexEntry> entries)
    {
        var groups = new Dictionary<string, List<CollectionIndexEntry>>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var diffKey = BuildDiffKey(entry);
            if (!groups.TryGetValue(diffKey, out var grouped))
            {
                grouped = new List<CollectionIndexEntry>();
                groups[diffKey] = grouped;
            }

            grouped.Add(entry);
        }

        var winners = new Dictionary<string, CollectionIndexEntry>(StringComparer.Ordinal);
        foreach (var pair in groups.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            winners[pair.Key] = SelectWinnerEntry(pair.Value);

        return winners;
    }

    private static CollectionIndexEntry SelectWinnerEntry(IReadOnlyList<CollectionIndexEntry> entries)
    {
        if (entries.Count == 1)
            return entries[0];

        var mapped = entries
            .OrderBy(static entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.Path, StringComparer.Ordinal)
            .Select(static entry => (Entry: entry, Candidate: CollectionIndexCandidateMapper.ToCandidate(entry)))
            .ToArray();

        var winnerCandidate = DeduplicationEngine.SelectWinner(mapped.Select(static item => item.Candidate).ToArray());
        if (winnerCandidate is null)
            throw new InvalidOperationException("Winner selection returned null for non-empty collection compare group.");

        foreach (var item in mapped)
        {
            if (ReferenceEquals(item.Candidate, winnerCandidate))
                return item.Entry;
        }

        throw new InvalidOperationException("Winner selection could not be mapped back to its collection index entry.");
    }

    private static List<CollectionDiffEntry> BuildDiffEntries(
        IReadOnlyDictionary<string, CollectionIndexEntry> leftWinners,
        IReadOnlyDictionary<string, CollectionIndexEntry> rightWinners)
    {
        var remainingLeft = new Dictionary<string, CollectionIndexEntry>(leftWinners, StringComparer.Ordinal);
        var remainingRight = new Dictionary<string, CollectionIndexEntry>(rightWinners, StringComparer.Ordinal);
        var pairs = new List<(string DiffKey, CollectionIndexEntry? Left, CollectionIndexEntry? Right)>();

        foreach (var diffKey in leftWinners.Keys.Intersect(rightWinners.Keys, StringComparer.Ordinal).OrderBy(static key => key, StringComparer.Ordinal))
        {
            pairs.Add((diffKey, leftWinners[diffKey], rightWinners[diffKey]));
            remainingLeft.Remove(diffKey);
            remainingRight.Remove(diffKey);
        }

        if (remainingLeft.Count > 0 && remainingRight.Count > 0)
        {
            var rightAliasMap = remainingRight.Values
                .Join(
                    remainingRight,
                    static entry => entry.Path,
                    static pair => pair.Value.Path,
                    static (entry, pair) => pair,
                    StringComparer.OrdinalIgnoreCase)
                .Where(static pair => !string.IsNullOrWhiteSpace(pair.Value.GameKey))
                .GroupBy(static pair => pair.Value.GameKey.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(static group => group.Count() == 1)
                .ToDictionary(static group => group.Key, static group => group.Single(), StringComparer.OrdinalIgnoreCase);

            foreach (var leftPair in remainingLeft.OrderBy(static pair => pair.Key, StringComparer.Ordinal).ToArray())
            {
                var leftEntry = leftPair.Value;
                if (string.IsNullOrWhiteSpace(leftEntry.GameKey))
                    continue;

                if (!rightAliasMap.TryGetValue(leftEntry.GameKey.Trim(), out var rightPair))
                    continue;

                var rightEntry = rightPair.Value;
                if (!CanAliasMatch(leftEntry, rightEntry))
                    continue;

                if (!remainingRight.ContainsKey(rightPair.Key))
                    continue;

                var aliasDiffKey = $"review-game|{leftEntry.GameKey.Trim()}";
                pairs.Add((aliasDiffKey, leftEntry, rightEntry));
                remainingLeft.Remove(leftPair.Key);
                remainingRight.Remove(rightPair.Key);
            }
        }

        foreach (var leftPair in remainingLeft.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            pairs.Add((leftPair.Key, leftPair.Value, null));

        foreach (var rightPair in remainingRight.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            pairs.Add((rightPair.Key, null, rightPair.Value));

        return pairs
            .OrderBy(static pair => pair.DiffKey, StringComparer.Ordinal)
            .Select(static pair => BuildDiffEntry(pair.DiffKey, pair.Left, pair.Right))
            .ToList();
    }

    private static CollectionDiffEntry BuildDiffEntry(
        string diffKey,
        CollectionIndexEntry? left,
        CollectionIndexEntry? right)
    {
        if (left is null && right is null)
            throw new InvalidOperationException("Collection diff entry requires at least one side.");

        if (left is not null && right is null)
        {
            return new CollectionDiffEntry
            {
                DiffKey = diffKey,
                State = CollectionDiffState.OnlyInLeft,
                ReasonCode = "left-only",
                Left = left
            };
        }

        if (left is null)
        {
            return new CollectionDiffEntry
            {
                DiffKey = diffKey,
                State = CollectionDiffState.OnlyInRight,
                ReasonCode = "right-only",
                Right = right
            };
        }

        var leftEntry = left ?? throw new InvalidOperationException("Collection diff entry requires a left-side value after null guards.");
        var rightEntry = right ?? throw new InvalidOperationException("Collection diff entry requires a right-side value after null guards.");

        if (EntriesAreIdentical(leftEntry, rightEntry))
        {
            return new CollectionDiffEntry
            {
                DiffKey = diffKey,
                State = CollectionDiffState.PresentInBothIdentical,
                ReasonCode = ResolveIdenticalReasonCode(leftEntry, rightEntry),
                Left = leftEntry,
                Right = rightEntry
            };
        }

        var reviewReason = ResolveReviewReason(leftEntry, rightEntry);
        if (reviewReason is not null)
        {
            return new CollectionDiffEntry
            {
                DiffKey = diffKey,
                State = CollectionDiffState.ReviewRequired,
                ReviewRequired = true,
                ReasonCode = reviewReason,
                Left = leftEntry,
                Right = rightEntry
            };
        }

        if (HaveEqualWinnerRank(leftEntry, rightEntry))
        {
            return new CollectionDiffEntry
            {
                DiffKey = diffKey,
                State = CollectionDiffState.PresentInBothDifferent,
                ReasonCode = "different-no-meaningful-preference",
                Left = leftEntry,
                Right = rightEntry
            };
        }

        var winner = DeduplicationEngine.SelectWinner(
        [
            CollectionIndexCandidateMapper.ToCandidate(leftEntry),
            CollectionIndexCandidateMapper.ToCandidate(rightEntry)
        ]);
        if (winner is null)
        {
            return new CollectionDiffEntry
            {
                DiffKey = diffKey,
                State = CollectionDiffState.ReviewRequired,
                ReviewRequired = true,
                ReasonCode = "winner-selection-failed",
                Left = leftEntry,
                Right = rightEntry
            };
        }

        var preferredSide = string.Equals(winner.MainPath, leftEntry.Path, StringComparison.OrdinalIgnoreCase)
            ? CollectionCompareSide.Left
            : CollectionCompareSide.Right;

        return new CollectionDiffEntry
        {
            DiffKey = diffKey,
            State = preferredSide == CollectionCompareSide.Left
                ? CollectionDiffState.LeftPreferred
                : CollectionDiffState.RightPreferred,
            PreferredSide = preferredSide,
            ReasonCode = preferredSide == CollectionCompareSide.Left
                ? "left-preferred"
                : "right-preferred",
            Left = leftEntry,
            Right = rightEntry
        };
    }

    internal static bool AreEntriesIdentical(CollectionIndexEntry left, CollectionIndexEntry right)
        => EntriesAreIdentical(left, right);

    internal static string GetIdentitySignature(CollectionIndexEntry entry)
        => BuildIdentitySignature(entry);

    private static bool EntriesAreIdentical(CollectionIndexEntry left, CollectionIndexEntry right)
    {
        if (!string.IsNullOrWhiteSpace(left.PrimaryHash)
            && !string.IsNullOrWhiteSpace(right.PrimaryHash)
            && string.Equals(CollectionIndexCandidateMapper.NormalizeHashType(left.PrimaryHashType), CollectionIndexCandidateMapper.NormalizeHashType(right.PrimaryHashType), StringComparison.Ordinal)
            && string.Equals(left.PrimaryHash, right.PrimaryHash, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(left.HeaderlessHash)
            && !string.IsNullOrWhiteSpace(right.HeaderlessHash)
            && string.Equals(left.HeaderlessHash, right.HeaderlessHash, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(BuildIdentitySignature(left), BuildIdentitySignature(right), StringComparison.Ordinal);
    }

    private static string ResolveIdenticalReasonCode(CollectionIndexEntry left, CollectionIndexEntry right)
    {
        if (!string.IsNullOrWhiteSpace(left.PrimaryHash)
            && !string.IsNullOrWhiteSpace(right.PrimaryHash)
            && string.Equals(CollectionIndexCandidateMapper.NormalizeHashType(left.PrimaryHashType), CollectionIndexCandidateMapper.NormalizeHashType(right.PrimaryHashType), StringComparison.Ordinal)
            && string.Equals(left.PrimaryHash, right.PrimaryHash, StringComparison.OrdinalIgnoreCase))
        {
            return "identical-primary-hash";
        }

        if (!string.IsNullOrWhiteSpace(left.HeaderlessHash)
            && !string.IsNullOrWhiteSpace(right.HeaderlessHash)
            && string.Equals(left.HeaderlessHash, right.HeaderlessHash, StringComparison.OrdinalIgnoreCase))
        {
            return "identical-headerless-hash";
        }

        return "identical-entry-state";
    }

    private static string BuildIdentitySignature(CollectionIndexEntry entry)
    {
        return string.Join("|",
            NormalizeConsoleKey(entry.ConsoleKey),
            entry.GameKey.Trim(),
            entry.Region.Trim(),
            entry.Extension.Trim().ToLowerInvariant(),
            entry.SizeBytes,
            CollectionIndexCandidateMapper.NormalizeHashType(entry.PrimaryHashType),
            entry.PrimaryHash ?? string.Empty,
            entry.HeaderlessHash ?? string.Empty,
            entry.Category,
            entry.DatMatch ? 1 : 0,
            entry.DatGameName ?? string.Empty,
            entry.DatAuditStatus,
            entry.RegionScore,
            entry.FormatScore,
            entry.VersionScore,
            entry.HeaderScore,
            entry.CompletenessScore,
            entry.SizeTieBreakScore,
            entry.SortDecision,
            entry.DecisionClass,
            entry.EvidenceTier,
            entry.PrimaryMatchKind,
            entry.DetectionConfidence,
            entry.DetectionConflict ? 1 : 0,
            entry.HasHardEvidence ? 1 : 0,
            entry.IsSoftOnly ? 1 : 0,
            entry.ClassificationReasonCode,
            entry.ClassificationConfidence,
            entry.PlatformFamily);
    }

    private static string? ResolveReviewReason(CollectionIndexEntry left, CollectionIndexEntry right)
    {
        if (!HasResolvedConsoleKey(left.ConsoleKey) || !HasResolvedConsoleKey(right.ConsoleKey))
            return "review-unresolved-console";

        if (string.IsNullOrWhiteSpace(left.GameKey) || string.IsNullOrWhiteSpace(right.GameKey))
            return "review-missing-gamekey";

        if (left.DetectionConflict || right.DetectionConflict)
            return "review-detection-conflict";

        if (left.SortDecision == SortDecision.Blocked || right.SortDecision == SortDecision.Blocked)
            return "review-sort-blocked";

        return null;
    }

    private static bool HaveEqualWinnerRank(CollectionIndexEntry left, CollectionIndexEntry right)
    {
        return left.Category == right.Category
               && left.CompletenessScore == right.CompletenessScore
               && left.DatMatch == right.DatMatch
               && left.RegionScore == right.RegionScore
               && left.HeaderScore == right.HeaderScore
               && left.VersionScore == right.VersionScore
               && left.FormatScore == right.FormatScore
               && left.SizeTieBreakScore == right.SizeTieBreakScore;
    }

    private static string BuildDiffKey(CollectionIndexEntry entry)
    {
        var consoleKey = NormalizeConsoleKey(entry.ConsoleKey);
        if (!string.IsNullOrWhiteSpace(entry.GameKey))
            return $"game|{consoleKey}|{entry.GameKey.Trim()}";

        if (!string.IsNullOrWhiteSpace(entry.PrimaryHash))
        {
            return $"hash|{consoleKey}|{CollectionIndexCandidateMapper.NormalizeHashType(entry.PrimaryHashType)}|{entry.PrimaryHash.Trim().ToLowerInvariant()}";
        }

        if (!string.IsNullOrWhiteSpace(entry.HeaderlessHash))
            return $"headerless|{consoleKey}|{entry.HeaderlessHash.Trim().ToLowerInvariant()}";

        return $"path|{consoleKey}|{Path.GetFileNameWithoutExtension(entry.FileName).Trim().ToLowerInvariant()}|{entry.Extension.Trim().ToLowerInvariant()}|{entry.SizeBytes}";
    }

    private static bool HasResolvedConsoleKey(string? consoleKey)
        => !string.IsNullOrWhiteSpace(consoleKey)
           && !string.Equals(consoleKey, "UNKNOWN", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(consoleKey, "AMBIGUOUS", StringComparison.OrdinalIgnoreCase);

    private static bool CanAliasMatch(CollectionIndexEntry left, CollectionIndexEntry right)
        => string.Equals(left.GameKey, right.GameKey, StringComparison.OrdinalIgnoreCase)
           && (!HasResolvedConsoleKey(left.ConsoleKey) || !HasResolvedConsoleKey(right.ConsoleKey));

    private static string NormalizeConsoleKey(string? consoleKey)
        => HasResolvedConsoleKey(consoleKey)
            ? consoleKey!.Trim()
            : "UNKNOWN";

    private static int NormalizeLimit(int limit)
    {
        var effectiveLimit = limit <= 0 ? 500 : limit;
        return Math.Clamp(effectiveLimit, 1, 5000);
    }
}

public sealed record CollectionSourceMaterializationResult(
    bool CanUse,
    CollectionSourceScope Scope,
    IReadOnlyList<CollectionIndexEntry> Entries,
    string Source,
    string? Reason = null)
{
    public static CollectionSourceMaterializationResult Success(
        CollectionSourceScope scope,
        IReadOnlyList<CollectionIndexEntry> entries,
        string source)
        => new(true, scope, entries, source);

    public static CollectionSourceMaterializationResult Unavailable(
        CollectionSourceScope scope,
        string reason)
        => new(false, scope, Array.Empty<CollectionIndexEntry>(), CollectionMaterializationSources.FallbackRun, reason);
}

public sealed record CollectionCompareBuildResult(
    bool CanUse,
    CollectionCompareResult? Result,
    string Source,
    string? Reason = null)
{
    public static CollectionCompareBuildResult Success(CollectionCompareResult result, string source)
        => new(true, result, source);

    public static CollectionCompareBuildResult Unavailable(string reason)
        => new(false, null, CollectionMaterializationSources.FallbackRun, reason);
}

public static class CollectionMaterializationSources
{
    public const string CollectionIndex = "collection-index";
    public const string EmptyScope = "empty-scope";
    public const string FallbackRun = "fallback-run";
}
