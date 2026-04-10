using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Analysis;
using Romulus.Infrastructure.FileSystem;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for CollectionCompareService — targeting:
/// - TryMaterializeSourceAsync error paths (null index, empty roots, fingerprint mismatch, count mismatch)
/// - CompareAsync with unavailable sides
/// - CompareUnpagedAsync full result set
/// - MaterializeCandidates
/// - AreEntriesIdentical internal variants (headerless hash, identity signature)
/// - OnlyInLeft / OnlyInRight diff classification
/// </summary>
public sealed class CollectionCompareServiceCoverageTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSystemAdapter _fs = new();

    public CollectionCompareServiceCoverageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CollCmpCov_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
        catch { /* best-effort */ }
    }

    #region Helpers

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

    private static CollectionIndexEntry MakeEntry(
        string path, string root, string consoleKey, string gameKey,
        string hash, string fingerprint,
        int regionScore = 0, SortDecision sort = SortDecision.Sort,
        string? headerlessHash = null, long? sizeTieBreak = null)
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
            EnrichmentFingerprint = fingerprint,
            PrimaryHashType = "SHA1",
            PrimaryHash = hash,
            HeaderlessHash = headerlessHash,
            ConsoleKey = consoleKey,
            GameKey = gameKey,
            Region = "EU",
            RegionScore = regionScore,
            FormatScore = 100,
            VersionScore = 1,
            HeaderScore = 10,
            CompletenessScore = 1,
            SizeTieBreakScore = sizeTieBreak ?? info.Length,
            Category = FileCategory.Game,
            SortDecision = sort
        };
    }

    private static CollectionCompareRequest MakeRequest(string leftRoot, string rightRoot)
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

    private sealed class FakeIndex : ICollectionIndex
    {
        private readonly IReadOnlyList<CollectionIndexEntry> _entries;
        public FakeIndex(IReadOnlyList<CollectionIndexEntry>? entries = null)
            => _entries = entries ?? [];

        public ValueTask<CollectionIndexMetadata> GetMetadataAsync(CancellationToken ct = default)
            => ValueTask.FromResult(new CollectionIndexMetadata());
        public ValueTask<int> CountEntriesAsync(CancellationToken ct = default)
            => ValueTask.FromResult(_entries.Count);
        public ValueTask<CollectionIndexEntry?> TryGetByPathAsync(string path, CancellationToken ct = default)
            => ValueTask.FromResult(_entries.FirstOrDefault(e =>
                string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase)));
        public ValueTask<IReadOnlyList<CollectionIndexEntry>> GetByPathsAsync(
            IReadOnlyList<string> paths, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<CollectionIndexEntry>>(
                _entries.Where(e => paths.Contains(e.Path, StringComparer.OrdinalIgnoreCase)).ToArray());
        public ValueTask<IReadOnlyList<CollectionIndexEntry>> ListByConsoleAsync(
            string consoleKey, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<CollectionIndexEntry>>(
                _entries.Where(e => string.Equals(e.ConsoleKey, consoleKey, StringComparison.OrdinalIgnoreCase)).ToArray());
        public ValueTask<IReadOnlyList<CollectionIndexEntry>> ListEntriesInScopeAsync(
            IReadOnlyList<string> roots, IReadOnlyCollection<string> extensions, CancellationToken ct = default)
        {
            var normalizedRoots = roots
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r => Path.GetFullPath(r).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar)
                .ToArray();
            var normalizedExts = extensions
                .Select(e => e.StartsWith('.') ? e : "." + e)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var result = _entries.Where(e =>
            {
                var fullPath = Path.GetFullPath(e.Path);
                return normalizedRoots.Any(r => fullPath.StartsWith(r, StringComparison.OrdinalIgnoreCase))
                    && normalizedExts.Contains(Path.GetExtension(e.Path));
            }).ToArray();
            return ValueTask.FromResult<IReadOnlyList<CollectionIndexEntry>>(result);
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

    #endregion

    // =================================================================
    //  TryMaterializeSourceAsync edge cases
    // =================================================================

    [Fact]
    public async Task TryMaterializeSourceAsync_NullIndex_ReturnsUnavailable()
    {
        var root = CreateRoot("nullidx");
        CreateFile(root, "game.sfc", "data");

        var result = await CollectionCompareService.TryMaterializeSourceAsync(
            null,
            _fs,
            new CollectionSourceScope { SourceId = "test", Roots = [root], Extensions = [".sfc"] });

        Assert.False(result.CanUse);
        Assert.Contains("index unavailable", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TryMaterializeSourceAsync_EmptyRoots_ReturnsUnavailable()
    {
        var result = await CollectionCompareService.TryMaterializeSourceAsync(
            new FakeIndex(),
            _fs,
            new CollectionSourceScope { SourceId = "test", Roots = [], Extensions = [".sfc"] });

        Assert.False(result.CanUse);
        Assert.Contains("roots are required", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TryMaterializeSourceAsync_NoEntriesForScope_EmptyScopeSuccess()
    {
        var root = CreateRoot("empty");
        // No files on disk, no entries in index → empty scope

        var result = await CollectionCompareService.TryMaterializeSourceAsync(
            new FakeIndex(),
            _fs,
            new CollectionSourceScope { SourceId = "test", Roots = [root], Extensions = [".sfc"] });

        Assert.True(result.CanUse);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task TryMaterializeSourceAsync_IndexEntriesButNoFiles_ReturnsUnavailable()
    {
        var root = CreateRoot("nofiles");
        // Index has entry but file doesn't exist on disk
        var fakePath = Path.Combine(root, "phantom.sfc");

        var result = await CollectionCompareService.TryMaterializeSourceAsync(
            new FakeIndex([new CollectionIndexEntry
            {
                Path = fakePath,
                Root = root,
                FileName = "phantom.sfc",
                Extension = ".sfc",
                EnrichmentFingerprint = "fp",
                PrimaryHash = "abc",
                PrimaryHashType = "SHA1",
                ConsoleKey = "SNES",
                GameKey = "phantom"
            }]),
            _fs,
            new CollectionSourceScope { SourceId = "test", Roots = [root], Extensions = [".sfc"] });

        Assert.False(result.CanUse);
        Assert.Contains("does not match filesystem", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TryMaterializeSourceAsync_MissingEnrichmentFingerprint_ReturnsUnavailable()
    {
        var root = CreateRoot("nofp");
        var path = CreateFile(root, "game.sfc", "data");

        var result = await CollectionCompareService.TryMaterializeSourceAsync(
            new FakeIndex([MakeEntry(path, root, "SNES", "game", "hash1", "")]),
            _fs,
            new CollectionSourceScope { SourceId = "test", Roots = [root], Extensions = [".sfc"] });

        Assert.False(result.CanUse);
        Assert.Contains("enrichment fingerprint", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    // =================================================================
    //  CompareAsync edge cases
    // =================================================================

    [Fact]
    public async Task CompareAsync_LeftUnavailable_ReturnsUnavailable()
    {
        var rightRoot = CreateRoot("right");
        CreateFile(rightRoot, "game.sfc", "data");

        var result = await CollectionCompareService.CompareAsync(
            null, // no index → left fails
            _fs,
            MakeRequest(Path.Combine(_tempDir, "missing"), rightRoot));

        Assert.False(result.CanUse);
        Assert.Contains("left source unavailable", result.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompareAsync_OnlyInLeftAndOnlyInRight_ClassifiedCorrectly()
    {
        var leftRoot = CreateRoot("oileft");
        var rightRoot = CreateRoot("oiright");
        var leftPath = CreateFile(leftRoot, "Alpha.sfc", "alpha-data");
        var rightPath = CreateFile(rightRoot, "Beta.sfc", "beta-data");

        var result = await CollectionCompareService.CompareAsync(
            new FakeIndex([
                MakeEntry(leftPath, leftRoot, "SNES", "alpha", "hash-a", "fp-1"),
                MakeEntry(rightPath, rightRoot, "SNES", "beta", "hash-b", "fp-1")
            ]),
            _fs,
            MakeRequest(leftRoot, rightRoot));

        Assert.True(result.CanUse);
        Assert.Equal(2, result.Result!.Entries.Count);
        Assert.Contains(result.Result.Entries, e => e.State == CollectionDiffState.OnlyInLeft);
        Assert.Contains(result.Result.Entries, e => e.State == CollectionDiffState.OnlyInRight);
    }

    // =================================================================
    //  CompareUnpagedAsync
    // =================================================================

    [Fact]
    public async Task CompareUnpagedAsync_ReturnsAllEntries_IgnoringPagination()
    {
        var leftRoot = CreateRoot("unpaged-left");
        var rightRoot = CreateRoot("unpaged-right");
        var left1 = CreateFile(leftRoot, "A.sfc", "aaa");
        var left2 = CreateFile(leftRoot, "B.sfc", "bbb");

        var result = await CollectionCompareService.CompareUnpagedAsync(
            new FakeIndex([
                MakeEntry(left1, leftRoot, "SNES", "alpha", "ha", "fp-1"),
                MakeEntry(left2, leftRoot, "SNES", "beta", "hb", "fp-1")
            ]),
            _fs,
            MakeRequest(leftRoot, rightRoot) with { Offset = 0, Limit = 1 });

        Assert.True(result.CanUse);
        // Despite Limit=1, unpaged returns all entries
        Assert.Equal(2, result.Result!.Entries.Count);
    }

    // =================================================================
    //  MaterializeCandidates
    // =================================================================

    [Fact]
    public void MaterializeCandidates_EmptyEntries_ReturnsEmpty()
    {
        var result = CollectionCompareService.MaterializeCandidates([]);
        Assert.Empty(result);
    }

    [Fact]
    public void MaterializeCandidates_MapsEntriesToCandidates()
    {
        var root = CreateRoot("mat");
        var path = CreateFile(root, "rom.sfc", "test");
        var entries = new[] { MakeEntry(path, root, "SNES", "game", "h1", "fp") };

        var candidates = CollectionCompareService.MaterializeCandidates(entries);

        Assert.Single(candidates);
        Assert.Equal(path, candidates[0].MainPath);
    }

    // =================================================================
    //  AreEntriesIdentical internal (headerless + signature)
    // =================================================================

    [Fact]
    public void AreEntriesIdentical_ByHeaderlessHash_ReturnsTrue()
    {
        var root = CreateRoot("ident");
        var left = CreateFile(root, "a.sfc", "left-content");
        var right = CreateFile(root, "b.sfc", "right-content");
        var leftEntry = MakeEntry(left, root, "SNES", "game", "different-primary", "fp",
            headerlessHash: "SHARED_HEADERLESS");
        var rightEntry = MakeEntry(right, root, "SNES", "game", "different-primary2", "fp",
            headerlessHash: "SHARED_HEADERLESS");

        Assert.True(CollectionCompareService.AreEntriesIdentical(leftEntry, rightEntry));
    }

    [Fact]
    public void AreEntriesIdentical_DifferentHashes_ReturnsFalse()
    {
        var root = CreateRoot("diff");
        var left = CreateFile(root, "x.sfc", "xxx");
        var right = CreateFile(root, "y.sfc", "yyy");
        var leftEntry = MakeEntry(left, root, "SNES", "game", "hash-left", "fp");
        var rightEntry = MakeEntry(right, root, "SNES", "game", "hash-right", "fp");

        Assert.False(CollectionCompareService.AreEntriesIdentical(leftEntry, rightEntry));
    }
}
