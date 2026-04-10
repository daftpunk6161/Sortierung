using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Analysis;
using Romulus.Infrastructure.FileSystem;
using Xunit;

namespace Romulus.Tests;

public sealed class CollectionCompareServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSystemAdapter _fileSystem = new();

    public CollectionCompareServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CollectionCompare_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task TryMaterializeSourceAsync_InferesFingerprintsFromScope()
    {
        var root = CreateRoot("left");
        var path = CreateFile(root, "Mario.sfc", "left-data");

        var result = await CollectionCompareService.TryMaterializeSourceAsync(
            new FakeCollectionIndex(
            [
                CreateEntry(path, root, "SNES", "mario", "hash-1", "fp-1")
            ]),
            _fileSystem,
            new CollectionSourceScope
            {
                SourceId = "left",
                Label = "Left Source",
                Roots = [root],
                Extensions = [".sfc"]
            });

        Assert.True(result.CanUse);
        Assert.Equal(CollectionMaterializationSources.CollectionIndex, result.Source);
        Assert.Equal("fp-1", result.Scope.EnrichmentFingerprint);
        Assert.False(string.IsNullOrWhiteSpace(result.Scope.RootFingerprint));
        Assert.Equal(root, Assert.Single(result.Scope.Roots));
        Assert.Single(result.Entries);
    }

    [Fact]
    public async Task TryMaterializeSourceAsync_FailsWhenScopeContainsMixedFingerprints()
    {
        var root = CreateRoot("mixed");
        var firstPath = CreateFile(root, "A.sfc", "A");
        var secondPath = CreateFile(root, "B.sfc", "B");

        var result = await CollectionCompareService.TryMaterializeSourceAsync(
            new FakeCollectionIndex(
            [
                CreateEntry(firstPath, root, "SNES", "alpha", "hash-a", "fp-a"),
                CreateEntry(secondPath, root, "SNES", "beta", "hash-b", "fp-b")
            ]),
            _fileSystem,
            new CollectionSourceScope
            {
                SourceId = "mixed",
                Roots = [root],
                Extensions = [".sfc"]
            });

        Assert.False(result.CanUse);
        Assert.Equal(CollectionMaterializationSources.FallbackRun, result.Source);
        Assert.Contains("mixed enrichment fingerprints", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompareAsync_ClassifiesIdenticalByPrimaryHash()
    {
        var leftRoot = CreateRoot("identical-left");
        var rightRoot = CreateRoot("identical-right");
        var leftPath = CreateFile(leftRoot, "Mario (EU).sfc", "left");
        var rightPath = CreateFile(rightRoot, "Mario (US).sfc", "right");

        var compare = await CollectionCompareService.CompareAsync(
            new FakeCollectionIndex(
            [
                CreateEntry(leftPath, leftRoot, "SNES", "mario", "hash-1", "fp-1"),
                CreateEntry(rightPath, rightRoot, "SNES", "mario", "hash-1", "fp-1")
            ]),
            _fileSystem,
            CreateRequest(leftRoot, rightRoot));

        Assert.True(compare.CanUse);
        var entry = Assert.Single(compare.Result!.Entries);
        Assert.Equal(CollectionDiffState.PresentInBothIdentical, entry.State);
        Assert.Equal("identical-primary-hash", entry.ReasonCode);
        Assert.Equal(1, compare.Result.Summary.PresentInBothIdentical);
    }

    [Fact]
    public async Task CompareAsync_ClassifiesPreferredSideUsingWinnerSelection()
    {
        var leftRoot = CreateRoot("preferred-left");
        var rightRoot = CreateRoot("preferred-right");
        var leftPath = CreateFile(leftRoot, "Game.sfc", "left");
        var rightPath = CreateFile(rightRoot, "Game.sfc", "right");

        var compare = await CollectionCompareService.CompareAsync(
            new FakeCollectionIndex(
            [
                CreateEntry(leftPath, leftRoot, "SNES", "game", "hash-left", "fp-1", regionScore: 100, sortDecision: SortDecision.Sort),
                CreateEntry(rightPath, rightRoot, "SNES", "game", "hash-right", "fp-1", regionScore: 200, sortDecision: SortDecision.Sort)
            ]),
            _fileSystem,
            CreateRequest(leftRoot, rightRoot));

        Assert.True(compare.CanUse);
        var entry = Assert.Single(compare.Result!.Entries);
        Assert.Equal(CollectionDiffState.RightPreferred, entry.State);
        Assert.Equal(CollectionCompareSide.Right, entry.PreferredSide);
        Assert.Equal("right-preferred", entry.ReasonCode);
    }

    [Fact]
    public async Task CompareAsync_ClassifiesDifferentWithoutMeaningfulPreference_WhenScoresAreEqual()
    {
        var leftRoot = CreateRoot("different-left");
        var rightRoot = CreateRoot("different-right");
        var leftPath = CreateFile(leftRoot, "Game A.sfc", "AAAA");
        var rightPath = CreateFile(rightRoot, "Game B.sfc", "BBBB");

        var compare = await CollectionCompareService.CompareAsync(
            new FakeCollectionIndex(
            [
                CreateEntry(leftPath, leftRoot, "SNES", "game", "hash-left", "fp-1", sortDecision: SortDecision.Sort, sizeTieBreakScore: 4),
                CreateEntry(rightPath, rightRoot, "SNES", "game", "hash-right", "fp-1", sortDecision: SortDecision.Sort, sizeTieBreakScore: 4)
            ]),
            _fileSystem,
            CreateRequest(leftRoot, rightRoot));

        Assert.True(compare.CanUse);
        var entry = Assert.Single(compare.Result!.Entries);
        Assert.Equal(CollectionDiffState.PresentInBothDifferent, entry.State);
        Assert.Null(entry.PreferredSide);
        Assert.Equal("different-no-meaningful-preference", entry.ReasonCode);
    }

    [Fact]
    public async Task CompareAsync_ClassifiesReviewRequired_WhenConsoleIsUnresolved()
    {
        var leftRoot = CreateRoot("review-left");
        var rightRoot = CreateRoot("review-right");
        var leftPath = CreateFile(leftRoot, "Unknown.sfc", "left");
        var rightPath = CreateFile(rightRoot, "Unknown.sfc", "right");

        var compare = await CollectionCompareService.CompareAsync(
            new FakeCollectionIndex(
            [
                CreateEntry(leftPath, leftRoot, "UNKNOWN", "mystery", "hash-left", "fp-1", sortDecision: SortDecision.Review),
                CreateEntry(rightPath, rightRoot, "SNES", "mystery", "hash-right", "fp-1", sortDecision: SortDecision.Sort)
            ]),
            _fileSystem,
            CreateRequest(leftRoot, rightRoot));

        Assert.True(compare.CanUse);
        var entry = Assert.Single(compare.Result!.Entries);
        Assert.Equal(CollectionDiffState.ReviewRequired, entry.State);
        Assert.True(entry.ReviewRequired);
        Assert.Equal("review-unresolved-console", entry.ReasonCode);
    }

    [Fact]
    public async Task CompareAsync_AppliesOffsetAndLimit_WithoutChangingFullSummary()
    {
        var leftRoot = CreateRoot("paged-left");
        var rightRoot = CreateRoot("paged-right");
        var firstLeftPath = CreateFile(leftRoot, "Alpha.sfc", "alpha");
        var secondLeftPath = CreateFile(leftRoot, "Beta.sfc", "beta");

        var compare = await CollectionCompareService.CompareAsync(
            new FakeCollectionIndex(
            [
                CreateEntry(firstLeftPath, leftRoot, "SNES", "alpha", "hash-a", "fp-1"),
                CreateEntry(secondLeftPath, leftRoot, "SNES", "beta", "hash-b", "fp-1")
            ]),
            _fileSystem,
            CreateRequest(leftRoot, rightRoot) with
            {
                Offset = 1,
                Limit = 1
            });

        Assert.True(compare.CanUse);
        Assert.Equal(2, compare.Result!.Summary.TotalEntries);
        var entry = Assert.Single(compare.Result.Entries);
        Assert.Equal("game|SNES|beta", entry.DiffKey);
        Assert.Equal(CollectionDiffState.OnlyInLeft, entry.State);
    }

    private CollectionCompareRequest CreateRequest(string leftRoot, string rightRoot)
    {
        return new CollectionCompareRequest
        {
            Left = new CollectionSourceScope
            {
                SourceId = "left",
                Label = "Left",
                Roots = [leftRoot],
                Extensions = [".sfc"]
            },
            Right = new CollectionSourceScope
            {
                SourceId = "right",
                Label = "Right",
                Roots = [rightRoot],
                Extensions = [".sfc"]
            }
        };
    }

    private string CreateRoot(string name)
    {
        var root = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(root);
        return root;
    }

    private static string CreateFile(string root, string fileName, string content)
    {
        var path = Path.Combine(root, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private static CollectionIndexEntry CreateEntry(
        string path,
        string root,
        string consoleKey,
        string gameKey,
        string hash,
        string enrichmentFingerprint,
        int regionScore = 0,
        SortDecision sortDecision = SortDecision.Sort,
        long? sizeTieBreakScore = null)
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
            SizeTieBreakScore = sizeTieBreakScore ?? info.Length,
            Category = FileCategory.Game,
            SortDecision = sortDecision
        };
    }

    private sealed class FakeCollectionIndex : ICollectionIndex
    {
        private readonly IReadOnlyList<CollectionIndexEntry> _entries;

        public FakeCollectionIndex(IReadOnlyList<CollectionIndexEntry>? entries = null)
        {
            _entries = entries ?? Array.Empty<CollectionIndexEntry>();
        }

        public ValueTask<CollectionIndexMetadata> GetMetadataAsync(CancellationToken ct = default)
            => ValueTask.FromResult(new CollectionIndexMetadata());

        public ValueTask<int> CountEntriesAsync(CancellationToken ct = default)
            => ValueTask.FromResult(_entries.Count);

        public ValueTask<CollectionIndexEntry?> TryGetByPathAsync(string path, CancellationToken ct = default)
            => ValueTask.FromResult(_entries.FirstOrDefault(entry => string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase)));

        public ValueTask<IReadOnlyList<CollectionIndexEntry>> GetByPathsAsync(IReadOnlyList<string> paths, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<CollectionIndexEntry>>(
                _entries.Where(entry => paths.Contains(entry.Path, StringComparer.OrdinalIgnoreCase)).ToArray());

        public ValueTask<IReadOnlyList<CollectionIndexEntry>> ListByConsoleAsync(string consoleKey, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<CollectionIndexEntry>>(
                _entries.Where(entry => string.Equals(entry.ConsoleKey, consoleKey, StringComparison.OrdinalIgnoreCase)).ToArray());

        public ValueTask<IReadOnlyList<CollectionIndexEntry>> ListEntriesInScopeAsync(
            IReadOnlyList<string> roots,
            IReadOnlyCollection<string> extensions,
            CancellationToken ct = default)
        {
            var normalizedRoots = roots
                .Where(static root => !string.IsNullOrWhiteSpace(root))
                .Select(static root => Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var normalizedExtensions = extensions
                .Where(static extension => !string.IsNullOrWhiteSpace(extension))
                .Select(static extension =>
                {
                    var trimmed = extension.Trim();
                    return trimmed.StartsWith('.')
                        ? trimmed.ToLowerInvariant()
                        : "." + trimmed.ToLowerInvariant();
                })
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var scoped = _entries
                .Where(entry =>
                {
                    var normalizedPath = Path.GetFullPath(entry.Path);
                    return normalizedRoots.Any(root => normalizedPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                           && normalizedExtensions.Contains(entry.Extension);
                })
                .OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Path, StringComparer.Ordinal)
                .ToArray();

            return ValueTask.FromResult<IReadOnlyList<CollectionIndexEntry>>(scoped);
        }

        public ValueTask UpsertEntriesAsync(IReadOnlyList<CollectionIndexEntry> entries, CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask RemovePathsAsync(IReadOnlyList<string> paths, CancellationToken ct = default)
            => ValueTask.CompletedTask;

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
