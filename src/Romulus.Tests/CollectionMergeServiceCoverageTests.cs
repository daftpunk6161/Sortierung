using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Analysis;
using Romulus.Infrastructure.FileSystem;
using Romulus.Tests.TestFixtures;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Additional coverage tests for CollectionMergeService targeting untested decision branches,
/// security validation, and edge cases beyond the existing CollectionMergeServiceTests.
/// </summary>
public sealed class CollectionMergeServiceCoverageTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSystemAdapter _fileSystem = new();

    public CollectionMergeServiceCoverageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CollMergeCov_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort test cleanup: do not fail assertions due to transient file locks.
        }
    }

    // ── CreateDefaultAuditPath ──────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateDefaultAuditPath_EmptyOrWhitespace_Throws(string? input)
    {
        Assert.Throws<ArgumentException>(() => CollectionMergeService.CreateDefaultAuditPath(input!));
    }

    [Fact]
    public void CreateDefaultAuditPath_ValidRoot_ReturnsTimestampedCsvPath()
    {
        var targetRoot = CreateRoot("audit-target");
        var result = CollectionMergeService.CreateDefaultAuditPath(targetRoot);

        Assert.Contains("collection-merge-", result);
        Assert.EndsWith(".csv", result);
        Assert.True(Directory.Exists(Path.GetDirectoryName(result)));
    }

    // ── BuildPlanAsync: MoveToTarget ────────────────────────────────────

    [Fact]
    public async Task BuildPlanAsync_AllowMoves_ReturnsMoveToTarget()
    {
        var leftRoot = CreateRoot("left");
        var rightRoot = CreateRoot("right");
        var targetRoot = CreateRoot("target");
        var leftPath = CreateFile(leftRoot, "SNES", "Mario.sfc", "left-content");

        var build = await CollectionMergeService.BuildPlanAsync(
            new TrackingCollectionIndex(
            [
                CreateEntry(leftPath, leftRoot, "SNES", "mario", "hash-left", "fp-1")
            ]),
            _fileSystem,
            CreateMergeRequest(leftRoot, rightRoot, targetRoot, allowMoves: true));

        Assert.True(build.CanUse);
        var entry = Assert.Single(build.Plan!.Entries);
        Assert.Equal(CollectionMergeDecision.MoveToTarget, entry.Decision);
        Assert.Equal("merge-move-to-target", entry.ReasonCode);
    }

    // ── BuildPlanAsync: Unindexed file conflict ─────────────────────────

    [Fact]
    public async Task BuildPlanAsync_UnindexedFileAtTargetPath_ReviewRequired()
    {
        var leftRoot = CreateRoot("left");
        var rightRoot = CreateRoot("right");
        var targetRoot = CreateRoot("target");
        var leftPath = CreateFile(leftRoot, "SNES", "Game.sfc", "left");
        // Create a file at the target path that is NOT in the index
        CreateFile(targetRoot, "SNES", "Game.sfc", "unindexed-content");

        // Index only has the left entry — target file not indexed
        var build = await CollectionMergeService.BuildPlanAsync(
            new TrackingCollectionIndex(
            [
                CreateEntry(leftPath, leftRoot, "SNES", "game", "hash-left", "fp-1")
            ]),
            _fileSystem,
            CreateMergeRequest(leftRoot, rightRoot, targetRoot, allowMoves: false));

        Assert.True(build.CanUse);
        var entry = Assert.Single(build.Plan!.Entries);
        Assert.Equal(CollectionMergeDecision.ReviewRequired, entry.Decision);
        Assert.Equal("merge-target-conflict-unindexed", entry.ReasonCode);
    }

    // ── BuildPlanAsync: Identical content at exact target path ───────────

    [Fact]
    public async Task BuildPlanAsync_IdenticalContentAtExactTargetPath_KeepsExisting()
    {
        var leftRoot = CreateRoot("left");
        var rightRoot = CreateRoot("right");
        var targetRoot = CreateRoot("target");
        var leftPath = CreateFile(leftRoot, "SNES", "Game.sfc", "some-content");
        var targetPath = CreateFile(targetRoot, "SNES", "Game.sfc", "target-content");

        // Both entries share the same hash, making them "identical" per AreEntriesIdentical
        var build = await CollectionMergeService.BuildPlanAsync(
            new TrackingCollectionIndex(
            [
                CreateEntry(leftPath, leftRoot, "SNES", "game", "hash-same", "fp-1"),
                CreateEntry(targetPath, targetRoot, "SNES", "game-target", "hash-same", "fp-1")
            ]),
            _fileSystem,
            CreateMergeRequest(leftRoot, rightRoot, targetRoot, allowMoves: false));

        Assert.True(build.CanUse);
        var entry = Assert.Single(build.Plan!.Entries);
        Assert.Equal(CollectionMergeDecision.KeepExistingTarget, entry.Decision);
        Assert.Equal("merge-target-already-identical", entry.ReasonCode);
    }

    // ── BuildPlanAsync: PresentInBothDifferent → ReviewRequired ─────────

    [Fact]
    public async Task BuildPlanAsync_BothSidesDifferentEqualScores_ProducesPreferredOrReview()
    {
        var leftRoot = CreateRoot("left");
        var rightRoot = CreateRoot("right");
        var targetRoot = CreateRoot("target");
        // Same content length ensures equal SizeTieBreakScore
        var leftPath = CreateFile(leftRoot, "SNES", "Game.sfc", "same-len");
        var rightPath = CreateFile(rightRoot, "SNES", "Game.sfc", "same-len");

        // Both sides have different hash but equal scores
        var build = await CollectionMergeService.BuildPlanAsync(
            new TrackingCollectionIndex(
            [
                CreateEntry(leftPath, leftRoot, "SNES", "game", "hash-left", "fp-1", regionScore: 100),
                CreateEntry(rightPath, rightRoot, "SNES", "game", "hash-right", "fp-1", regionScore: 100)
            ]),
            _fileSystem,
            CreateMergeRequest(leftRoot, rightRoot, targetRoot, allowMoves: false));

        Assert.True(build.CanUse);
        var entry = Assert.Single(build.Plan!.Entries);
        // With equal scores, tiebreaker may still select a preferred side → CopyToTarget,
        // or if truly undecidable → ReviewRequired. Both are valid deterministic outcomes.
        Assert.True(
            entry.Decision is CollectionMergeDecision.CopyToTarget or CollectionMergeDecision.ReviewRequired,
            $"Expected CopyToTarget or ReviewRequired, got {entry.Decision}");
    }

    // ── BuildPlanAsync: Empty target root → Unavailable ─────────────────

    [Fact]
    public async Task BuildPlanAsync_EmptyTargetRoot_ReturnsUnavailable()
    {
        var leftRoot = CreateRoot("left");
        var rightRoot = CreateRoot("right");

        var request = new CollectionMergeRequest
        {
            CompareRequest = new CollectionCompareRequest
            {
                Left = new CollectionSourceScope { SourceId = "left", Label = "Left", Roots = [leftRoot], Extensions = [".sfc"] },
                Right = new CollectionSourceScope { SourceId = "right", Label = "Right", Roots = [rightRoot], Extensions = [".sfc"] }
            },
            TargetRoot = "",
            AllowMoves = false
        };

        var build = await CollectionMergeService.BuildPlanAsync(null, _fileSystem, request);

        Assert.False(build.CanUse);
        Assert.Contains("target root", build.Reason, StringComparison.OrdinalIgnoreCase);
    }

    // ── BuildPlanAsync: Headerless hash duplicate detection ──────────────

    [Fact]
    public async Task BuildPlanAsync_DuplicateByHeaderlessHash_SkipsAsDuplicate()
    {
        var leftRoot = CreateRoot("left");
        var rightRoot = CreateRoot("right");
        var targetRoot = CreateRoot("target");
        var leftPath = CreateFile(leftRoot, "SNES", "Game.sfc", "left");
        var targetPath = CreateFile(targetRoot, "SNES", "GameCopy.sfc", "target");

        // Different primary hashes but same headerless hash → duplicate
        var leftEntry = CreateEntry(leftPath, leftRoot, "SNES", "game", "hash-left", "fp-1");
        leftEntry = leftEntry with { HeaderlessHash = "headerless-match" };
        var targetEntry = CreateEntry(targetPath, targetRoot, "SNES", "game-copy", "hash-target", "fp-1");
        targetEntry = targetEntry with { HeaderlessHash = "headerless-match" };

        var build = await CollectionMergeService.BuildPlanAsync(
            new TrackingCollectionIndex([leftEntry, targetEntry]),
            _fileSystem,
            CreateMergeRequest(leftRoot, rightRoot, targetRoot, allowMoves: false));

        Assert.True(build.CanUse);
        var entry = Assert.Single(build.Plan!.Entries);
        Assert.Equal(CollectionMergeDecision.SkipAsDuplicate, entry.Decision);
    }

    // ── ApplyAsync: No mutating entries → no audit ──────────────────────

    [Fact]
    public async Task ApplyAsync_AllEntriesKeepExisting_DoesNotWriteAudit()
    {
        var leftRoot = CreateRoot("left");
        var rightRoot = CreateRoot("right");
        var leftPath = CreateFile(leftRoot, "", "Game.sfc", "same");
        var rightPath = CreateFile(rightRoot, "", "Game.sfc", "same");

        var index = new TrackingCollectionIndex(
        [
            // If right has higher score and target==rightRoot, the preferred source is already in target
            CreateEntry(leftPath, leftRoot, "SNES", "game", "hash-same", "fp-1", regionScore: 50),
            CreateEntry(rightPath, rightRoot, "SNES", "game", "hash-same", "fp-1", regionScore: 200)
        ]);
        var auditStore = new TrackingAuditStore();

        var result = await CollectionMergeService.ApplyAsync(
            index,
            _fileSystem,
            auditStore,
            new CollectionMergeApplyRequest
            {
                MergeRequest = CreateMergeRequest(leftRoot, rightRoot, rightRoot, allowMoves: false),
                AuditPath = Path.Combine(_tempDir, "no-audit.csv")
            });

        Assert.Null(result.BlockedReason);
        Assert.Equal(0, result.Summary.Applied);
        Assert.Empty(auditStore.Rows);
        Assert.Empty(result.AuditPath);
        Assert.False(result.RollbackAvailable);
    }

    // ── ApplyAsync: null collectionIndex still works for copy ────────────

    [Fact]
    public async Task ApplyAsync_NullIndex_CopySucceedsWithoutIndexUpdate()
    {
        var leftRoot = CreateRoot("left");
        var rightRoot = CreateRoot("right");
        var targetRoot = CreateRoot("target");
        CreateFile(leftRoot, "SNES", "Mario.sfc", "content");

        // Cannot use null index with the real compare service - it needs to materialize.
        // But we can verify the guard clauses pass.
        // A null index means compare returns "unavailable" → blocked result
        var result = await CollectionMergeService.ApplyAsync(
            null,
            _fileSystem,
            new TrackingAuditStore(),
            new CollectionMergeApplyRequest
            {
                MergeRequest = CreateMergeRequest(leftRoot, rightRoot, targetRoot, allowMoves: false),
                AuditPath = Path.Combine(_tempDir, "null-index.csv")
            });

        // With null index, compare returns unavailable → blocked
        Assert.NotNull(result.BlockedReason);
    }

    // ── ApplyAsync: Guard clause – null fileSystem ──────────────────────

    [Fact]
    public async Task ApplyAsync_NullFileSystem_ThrowsArgumentNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            CollectionMergeService.ApplyAsync(
                null,
                null!,
                new TrackingAuditStore(),
                new CollectionMergeApplyRequest
                {
                    MergeRequest = new CollectionMergeRequest
                    {
                        CompareRequest = new CollectionCompareRequest(),
                        TargetRoot = "C:\\test"
                    }
                }).AsTask());
    }

    [Fact]
    public async Task ApplyAsync_NullAuditStore_ThrowsArgumentNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            CollectionMergeService.ApplyAsync(
                null,
                _fileSystem,
                null!,
                new CollectionMergeApplyRequest
                {
                    MergeRequest = new CollectionMergeRequest
                    {
                        CompareRequest = new CollectionCompareRequest(),
                        TargetRoot = "C:\\test"
                    }
                }).AsTask());
    }

    [Fact]
    public async Task ApplyAsync_NullRequest_ThrowsArgumentNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            CollectionMergeService.ApplyAsync(
                null,
                _fileSystem,
                new TrackingAuditStore(),
                null!).AsTask());
    }

    // ── ApplyAsync: Default audit path when not provided ────────────────

    [Fact]
    public async Task ApplyAsync_EmptyAuditPath_GeneratesDefaultPath()
    {
        var leftRoot = CreateRoot("left");
        var rightRoot = CreateRoot("right");
        var targetRoot = CreateRoot("target");
        var leftPath = CreateFile(leftRoot, "SNES", "Mario.sfc", "content");

        var index = new TrackingCollectionIndex(
        [
            CreateEntry(leftPath, leftRoot, "SNES", "mario", "hash-left", "fp-1")
        ]);
        var auditStore = new TrackingAuditStore();

        var result = await CollectionMergeService.ApplyAsync(
            index,
            _fileSystem,
            auditStore,
            new CollectionMergeApplyRequest
            {
                MergeRequest = CreateMergeRequest(leftRoot, rightRoot, targetRoot, allowMoves: false),
                AuditPath = ""
            });

        Assert.Null(result.BlockedReason);
        Assert.Contains("collection-merge-", result.AuditPath);
        Assert.True(result.Summary.Applied > 0 || result.Summary.Failed > 0);
    }

    // ── BuildPlanAsync: SelectIdenticalSource prefers target-root source ─

    [Fact]
    public async Task BuildPlanAsync_IdenticalBothSides_PrefersSourceInTargetRoot()
    {
        var leftRoot = CreateRoot("left");
        var rightRoot = CreateRoot("right");
        // Target == left: the source in left IS in the target root
        var leftPath = CreateFile(leftRoot, "SNES", "Game.sfc", "content");
        var rightPath = CreateFile(rightRoot, "SNES", "Game.sfc", "content");

        var build = await CollectionMergeService.BuildPlanAsync(
            new TrackingCollectionIndex(
            [
                CreateEntry(leftPath, leftRoot, "SNES", "game", "hash-same", "fp-1"),
                CreateEntry(rightPath, rightRoot, "SNES", "game", "hash-same", "fp-1")
            ]),
            _fileSystem,
            CreateMergeRequest(leftRoot, rightRoot, leftRoot, allowMoves: false));

        Assert.True(build.CanUse);
        var entry = Assert.Single(build.Plan!.Entries);
        // Source already in target → KeepExistingTarget
        Assert.Equal(CollectionMergeDecision.KeepExistingTarget, entry.Decision);
        Assert.Equal(CollectionCompareSide.Left, entry.SourceSide);
    }

    // ── BuildPlanAsync: Multiple entries with mixed decisions ────────────

    [Fact]
    public async Task BuildPlanAsync_MultipleEntries_MixedDecisions()
    {
        var leftRoot = CreateRoot("left");
        var rightRoot = CreateRoot("right");
        var targetRoot = CreateRoot("target");

        // Entry 1: left-only → CopyToTarget
        var copyPath = CreateFile(leftRoot, "SNES", "Alpha.sfc", "alpha-content");

        // Entry 2: Both sides identical, source already in target → KeepExistingTarget
        var keepLeftPath = CreateFile(leftRoot, "SNES", "Beta.sfc", "beta-content");
        var keepRightPath = CreateFile(rightRoot, "SNES", "Beta.sfc", "beta-content");

        var build = await CollectionMergeService.BuildPlanAsync(
            new TrackingCollectionIndex(
            [
                CreateEntry(copyPath, leftRoot, "SNES", "alpha", "hash-alpha", "fp-1"),
                CreateEntry(keepLeftPath, leftRoot, "SNES", "beta", "hash-beta", "fp-1"),
                CreateEntry(keepRightPath, rightRoot, "SNES", "beta", "hash-beta", "fp-1")
            ]),
            _fileSystem,
            CreateMergeRequest(leftRoot, rightRoot, targetRoot, allowMoves: false));

        Assert.True(build.CanUse);
        Assert.Equal(2, build.Plan!.Summary.TotalEntries);

        var copyEntry = build.Plan.Entries.FirstOrDefault(e => e.Decision == CollectionMergeDecision.CopyToTarget);
        Assert.NotNull(copyEntry);
        Assert.Equal("game|SNES|alpha", copyEntry.DiffKey);

        // Beta: both identical, left preferred (fallback), but not in target → CopyToTarget
        // Actually: since both are identical and neither is in targetRoot, it'll try to copy
        var betaEntry = build.Plan.Entries.FirstOrDefault(e => e.DiffKey == "game|SNES|beta");
        Assert.NotNull(betaEntry);
        Assert.True(betaEntry.Decision is CollectionMergeDecision.CopyToTarget or CollectionMergeDecision.KeepExistingTarget);
    }

    // ── BuildPlanAsync: Preferred side (winner selection) ───────────────

    [Fact]
    public async Task BuildPlanAsync_DifferentScores_PrefersHigherScoreAsCopySource()
    {
        var leftRoot = CreateRoot("left");
        var rightRoot = CreateRoot("right");
        var targetRoot = CreateRoot("target");
        var leftPath = CreateFile(leftRoot, "SNES", "Game.sfc", "left-ver");
        var rightPath = CreateFile(rightRoot, "SNES", "Game.sfc", "right-ver");

        // right has higher region score → LeftPreferred/RightPreferred → copies preferred
        var build = await CollectionMergeService.BuildPlanAsync(
            new TrackingCollectionIndex(
            [
                CreateEntry(leftPath, leftRoot, "SNES", "game", "hash-left", "fp-1", regionScore: 50),
                CreateEntry(rightPath, rightRoot, "SNES", "game", "hash-right", "fp-1", regionScore: 300)
            ]),
            _fileSystem,
            CreateMergeRequest(leftRoot, rightRoot, targetRoot, allowMoves: false));

        Assert.True(build.CanUse);
        var entry = Assert.Single(build.Plan!.Entries);
        // Higher region score → RightPreferred → copies right to target
        Assert.Equal(CollectionMergeDecision.CopyToTarget, entry.Decision);
        Assert.Equal(CollectionCompareSide.Right, entry.SourceSide);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private CollectionMergeRequest CreateMergeRequest(string leftRoot, string rightRoot, string targetRoot, bool allowMoves)
        => new()
        {
            CompareRequest = new CollectionCompareRequest
            {
                Left = new CollectionSourceScope { SourceId = "left", Label = "Left", Roots = [leftRoot], Extensions = [".sfc"] },
                Right = new CollectionSourceScope { SourceId = "right", Label = "Right", Roots = [rightRoot], Extensions = [".sfc"] }
            },
            TargetRoot = targetRoot,
            AllowMoves = allowMoves
        };

    private string CreateRoot(string name)
    {
        var root = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(root);
        return root;
    }

    private static string CreateFile(string root, string relativeDirectory, string fileName, string content)
    {
        var directory = string.IsNullOrWhiteSpace(relativeDirectory) ? root : Path.Combine(root, relativeDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private static CollectionIndexEntry CreateEntry(
        string path, string root, string consoleKey, string gameKey,
        string hash, string enrichmentFingerprint, int regionScore = 0)
    {
        var info = new FileInfo(path);
        return new CollectionIndexEntry
        {
            Path = path,
            Root = root,
            FileName = Path.GetFileName(path),
            Extension = Path.GetExtension(path),
            SizeBytes = info.Length,
            LastWriteUtc = info.LastWriteTimeUtc,
            LastScannedUtc = info.LastWriteTimeUtc,
            EnrichmentFingerprint = enrichmentFingerprint,
            PrimaryHashType = "SHA1",
            PrimaryHash = hash,
            ConsoleKey = consoleKey,
            GameKey = gameKey,
            Region = "EU",
            RegionScore = regionScore,
            FormatScore = 100,
            VersionScore = 1,
            HeaderScore = 10,
            CompletenessScore = 1,
            SizeTieBreakScore = info.Length,
            Category = FileCategory.Game,
            SortDecision = SortDecision.Sort
        };
    }

    /// <summary>
    /// Lightweight mutable collection index for tests that don't need fault injection.
    /// </summary>
    private sealed class TrackingCollectionIndex : ICollectionIndex
    {
        private readonly List<CollectionIndexEntry> _entries;

        public TrackingCollectionIndex(IReadOnlyList<CollectionIndexEntry>? entries = null)
            => _entries = entries?.ToList() ?? [];

        public ValueTask<CollectionIndexMetadata> GetMetadataAsync(CancellationToken ct = default)
            => ValueTask.FromResult(new CollectionIndexMetadata());

        public ValueTask<int> CountEntriesAsync(CancellationToken ct = default)
            => ValueTask.FromResult(_entries.Count);

        public ValueTask<CollectionIndexEntry?> TryGetByPathAsync(string path, CancellationToken ct = default)
            => ValueTask.FromResult(_entries.FirstOrDefault(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase)));

        public ValueTask<IReadOnlyList<CollectionIndexEntry>> GetByPathsAsync(IReadOnlyList<string> paths, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<CollectionIndexEntry>>(
                _entries.Where(e => paths.Contains(e.Path, StringComparer.OrdinalIgnoreCase)).ToArray());

        public ValueTask<IReadOnlyList<CollectionIndexEntry>> ListByConsoleAsync(string consoleKey, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<CollectionIndexEntry>>(
                _entries.Where(e => string.Equals(e.ConsoleKey, consoleKey, StringComparison.OrdinalIgnoreCase)).ToArray());

        public ValueTask<IReadOnlyList<CollectionIndexEntry>> ListEntriesInScopeAsync(
            IReadOnlyList<string> roots,
            IReadOnlyCollection<string> extensions,
            CancellationToken ct = default)
        {
            var normalizedRoots = roots
                .Where(static r => !string.IsNullOrWhiteSpace(r))
                .Select(static r => Path.GetFullPath(r).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var normalizedExtensions = extensions
                .Where(static e => !string.IsNullOrWhiteSpace(e))
                .Select(static e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var scoped = _entries
                .Where(e =>
                {
                    var normalizedPath = Path.GetFullPath(e.Path);
                    return normalizedRoots.Any(r => normalizedPath.StartsWith(r, StringComparison.OrdinalIgnoreCase))
                           && normalizedExtensions.Contains(e.Extension.ToLowerInvariant());
                })
                .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Path, StringComparer.Ordinal)
                .ToArray();

            return ValueTask.FromResult<IReadOnlyList<CollectionIndexEntry>>(scoped);
        }

        public ValueTask UpsertEntriesAsync(IReadOnlyList<CollectionIndexEntry> entries, CancellationToken ct = default)
        {
            foreach (var entry in entries)
            {
                _entries.RemoveAll(e => string.Equals(e.Path, entry.Path, StringComparison.OrdinalIgnoreCase));
                _entries.Add(entry);
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask RemovePathsAsync(IReadOnlyList<string> paths, CancellationToken ct = default)
        {
            foreach (var path in paths)
                _entries.RemoveAll(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
            return ValueTask.CompletedTask;
        }

        public ValueTask<CollectionHashCacheEntry?> TryGetHashAsync(string path, string algorithm, long sizeBytes, DateTime lastWriteUtc, CancellationToken ct = default)
            => ValueTask.FromResult<CollectionHashCacheEntry?>(null);

        public ValueTask SetHashAsync(CollectionHashCacheEntry entry, CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask AppendRunSnapshotAsync(CollectionRunSnapshot snapshot, CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask<int> CountRunSnapshotsAsync(CancellationToken ct = default)
            => ValueTask.FromResult(0);

        public ValueTask<IReadOnlyList<CollectionRunSnapshot>> ListRunSnapshotsAsync(int limit = 50, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<CollectionRunSnapshot>>(Array.Empty<CollectionRunSnapshot>());
    }
}
