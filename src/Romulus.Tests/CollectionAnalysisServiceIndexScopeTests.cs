using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Analysis;
using Romulus.Infrastructure.FileSystem;
using Xunit;

namespace Romulus.Tests;

public sealed class CollectionAnalysisServiceIndexScopeTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSystemAdapter _fileSystem = new();

    public CollectionAnalysisServiceIndexScopeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AnalysisIndexScope_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task TryLoadScopedCandidatesFromCollectionIndexAsync_ReturnsCandidates_WhenScopeAndFingerprintMatch()
    {
        var root = Path.Combine(_tempDir, "root");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "game.sfc");
        File.WriteAllText(path, "data");

        var result = await CollectionAnalysisService.TryLoadScopedCandidatesFromCollectionIndexAsync(
            new FakeCollectionIndex(
            [
                new CollectionIndexEntry
                {
                    Path = path,
                    Root = root,
                    FileName = "game.sfc",
                    Extension = ".sfc",
                    SizeBytes = 4,
                    LastWriteUtc = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
                    LastScannedUtc = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
                    EnrichmentFingerprint = "fp-1",
                    PrimaryHashType = "SHA1",
                    ConsoleKey = "SNES",
                    GameKey = "game",
                    Region = "EU",
                    Category = FileCategory.Game
                }
            ]),
            _fileSystem,
            [root],
            [".sfc"],
            "fp-1");

        Assert.True(result.CanUse);
        Assert.Equal(ScopedCandidateSources.CollectionIndex, result.Source);
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(path, candidate.MainPath);
        Assert.Equal("SNES", candidate.ConsoleKey);
    }

    [Fact]
    public async Task TryLoadScopedCandidatesFromCollectionIndexAsync_ReturnsUnavailable_OnFingerprintMismatch()
    {
        var root = Path.Combine(_tempDir, "fp-root");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "game.sfc");
        File.WriteAllText(path, "data");

        var result = await CollectionAnalysisService.TryLoadScopedCandidatesFromCollectionIndexAsync(
            new FakeCollectionIndex(
            [
                new CollectionIndexEntry
                {
                    Path = path,
                    Root = root,
                    FileName = "game.sfc",
                    Extension = ".sfc",
                    EnrichmentFingerprint = "fp-old",
                    PrimaryHashType = "SHA1"
                }
            ]),
            _fileSystem,
            [root],
            [".sfc"],
            "fp-new");

        Assert.False(result.CanUse);
        Assert.Equal(ScopedCandidateSources.FallbackRun, result.Source);
        Assert.Contains("fingerprint mismatch", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TryLoadScopedCandidatesFromCollectionIndexAsync_ReturnsUnavailable_WhenFilesystemScopeDiffers()
    {
        var root = Path.Combine(_tempDir, "scope-root");
        Directory.CreateDirectory(root);
        var indexedPath = Path.Combine(root, "indexed.sfc");
        var extraPath = Path.Combine(root, "extra.sfc");
        File.WriteAllText(indexedPath, "indexed");
        File.WriteAllText(extraPath, "extra");

        var result = await CollectionAnalysisService.TryLoadScopedCandidatesFromCollectionIndexAsync(
            new FakeCollectionIndex(
            [
                new CollectionIndexEntry
                {
                    Path = indexedPath,
                    Root = root,
                    FileName = "indexed.sfc",
                    Extension = ".sfc",
                    EnrichmentFingerprint = "fp-1",
                    PrimaryHashType = "SHA1"
                }
            ]),
            _fileSystem,
            [root],
            [".sfc"],
            "fp-1");

        Assert.False(result.CanUse);
        Assert.Contains("scope does not match filesystem", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TryLoadScopedCandidatesFromCollectionIndexAsync_ReturnsEmptyScope_WhenFilesystemAndIndexAreEmpty()
    {
        var root = Path.Combine(_tempDir, "empty-root");
        Directory.CreateDirectory(root);

        var result = await CollectionAnalysisService.TryLoadScopedCandidatesFromCollectionIndexAsync(
            new FakeCollectionIndex(),
            _fileSystem,
            [root],
            [".sfc"],
            "fp-1");

        Assert.True(result.CanUse);
        Assert.Equal(ScopedCandidateSources.EmptyScope, result.Source);
        Assert.Empty(result.Candidates);
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
                        ? trimmed
                        : "." + trimmed;
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
