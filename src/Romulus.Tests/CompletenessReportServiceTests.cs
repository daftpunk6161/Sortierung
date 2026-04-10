using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Analysis;
using Xunit;

namespace Romulus.Tests;

public sealed class CompletenessReportServiceTests : IDisposable
{
    private readonly string _tempDir;

    public CompletenessReportServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CompletenessReport_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task BuildAsync_PrefersRunCandidates_AsPrimaryTruth()
    {
        var datIndex = new DatIndex();
        datIndex.Add("nes", "hash-1", "Super Mario Bros", "Super Mario Bros.nes");

        var root = Path.Combine(_tempDir, "library");
        Directory.CreateDirectory(Path.Combine(root, "NES"));
        File.WriteAllText(Path.Combine(root, "NES", "Different Game.nes"), "different");

        var report = await CompletenessReportService.BuildAsync(
            datIndex,
            [root],
            collectionIndex: new FakeCollectionIndex(),
            extensions: [".nes"],
            candidates:
            [
                new RomCandidate
                {
                    MainPath = Path.Combine(root, "NES", "Wrong Name.nes"),
                    ConsoleKey = "nes",
                    Hash = "hash-1"
                }
            ]);

        var entry = Assert.Single(report.Entries);
        Assert.Equal(CompletenessSources.RunCandidates, report.Source);
        Assert.Equal(1, report.SourceItemCount);
        Assert.Equal("nes", entry.ConsoleKey);
        Assert.Equal(1, entry.Verified);
        Assert.Equal(0, entry.MissingCount);
    }

    [Fact]
    public async Task BuildAsync_UsesCollectionIndexBeforeFilesystemFallback()
    {
        var datIndex = new DatIndex();
        datIndex.Add("nes", "hash-1", "Super Mario Bros", "Super Mario Bros.nes");

        var root = Path.Combine(_tempDir, "index-root");
        Directory.CreateDirectory(root);

        var report = await CompletenessReportService.BuildAsync(
            datIndex,
            [root],
            collectionIndex: new FakeCollectionIndex(
            [
                new CollectionIndexEntry
                {
                    Path = Path.Combine(root, "Wrong Name.nes"),
                    Root = root,
                    FileName = "Wrong Name.nes",
                    Extension = ".nes",
                    ConsoleKey = "nes",
                    PrimaryHash = "hash-1",
                    PrimaryHashType = "SHA1",
                    LastWriteUtc = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc)
                }
            ]),
            extensions: [".nes"]);

        var entry = Assert.Single(report.Entries);
        Assert.Equal(CompletenessSources.CollectionIndex, report.Source);
        Assert.Equal(1, report.SourceItemCount);
        Assert.Equal(1, entry.Verified);
        Assert.Contains("Source: collection-index", CompletenessReportService.FormatReport(report), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_FallsBackToFilesystem_WhenIndexScopeIsEmpty()
    {
        var datIndex = new DatIndex();
        datIndex.Add("nes", "hash-1", "Super Mario Bros", "Super Mario Bros.nes");

        var root = Path.Combine(_tempDir, "filesystem-root");
        var consoleDir = Path.Combine(root, "NES");
        Directory.CreateDirectory(consoleDir);
        File.WriteAllText(Path.Combine(consoleDir, "Super Mario Bros.nes"), "data");

        var report = await CompletenessReportService.BuildAsync(
            datIndex,
            [root],
            collectionIndex: new FakeCollectionIndex(),
            extensions: [".nes"]);

        var entry = Assert.Single(report.Entries);
        Assert.Equal(CompletenessSources.FilesystemFallback, report.Source);
        Assert.Equal(1, report.SourceItemCount);
        Assert.Equal(1, entry.Verified);
        Assert.Equal(0, entry.MissingCount);
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
