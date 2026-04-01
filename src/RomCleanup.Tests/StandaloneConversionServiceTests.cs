using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Conversion;
using Xunit;

namespace RomCleanup.Tests;

public sealed class StandaloneConversionServiceTests : IDisposable
{
    private readonly string _tempDir;

    public StandaloneConversionServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "StandaloneConvert_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void ConvertFile_UsesCollectionIndexConsoleKey_WhenExplicitConsoleMissing()
    {
        var filePath = CreateFile(Path.Combine(_tempDir, "Misc", "disc.iso"));
        var converter = new FakeFormatConverter(("PS2", ".iso"), new ConversionTarget(".chd", "chdman", "createdvd"));
        var service = new StandaloneConversionService(
            converter,
            new FakeCollectionIndex(
            [
                new CollectionIndexEntry
                {
                    Path = filePath,
                    ConsoleKey = "PS2"
                }
            ]));

        var result = service.ConvertFile(filePath);

        Assert.Equal("PS2", converter.LastConsoleKey);
        Assert.Equal(ConversionOutcome.Success, result.Outcome);
    }

    [Fact]
    public void ConvertFile_FallsBackToPath_WhenIndexConsoleIsUnknown()
    {
        var filePath = CreateFile(Path.Combine(_tempDir, "PS1", "disc.cue"));
        var converter = new FakeFormatConverter(("PS1", ".cue"), new ConversionTarget(".chd", "chdman", "createcd"));
        var service = new StandaloneConversionService(
            converter,
            new FakeCollectionIndex(
            [
                new CollectionIndexEntry
                {
                    Path = filePath,
                    ConsoleKey = "UNKNOWN"
                }
            ]));

        var result = service.ConvertFile(filePath);

        Assert.Equal("PS1", converter.LastConsoleKey);
        Assert.Equal(ConversionOutcome.Success, result.Outcome);
    }

    [Fact]
    public void ConvertFile_ExplicitConsoleKey_OverridesCollectionIndex()
    {
        var filePath = CreateFile(Path.Combine(_tempDir, "PS1", "disc.iso"));
        var converter = new FakeFormatConverter(("GC", ".iso"), new ConversionTarget(".rvz", "dolphintool", "convert"));
        var service = new StandaloneConversionService(
            converter,
            new FakeCollectionIndex(
            [
                new CollectionIndexEntry
                {
                    Path = filePath,
                    ConsoleKey = "PS2"
                }
            ]));

        var result = service.ConvertFile(filePath, consoleKey: "GC");

        Assert.Equal("GC", converter.LastConsoleKey);
        Assert.Equal(ConversionOutcome.Success, result.Outcome);
    }

    private string CreateFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "data");
        return path;
    }

    private sealed class FakeFormatConverter : IFormatConverter
    {
        private readonly (string ConsoleKey, string Extension) _supported;
        private readonly ConversionTarget _target;

        public FakeFormatConverter((string ConsoleKey, string Extension) supported, ConversionTarget target)
        {
            _supported = supported;
            _target = target;
        }

        public string? LastConsoleKey { get; private set; }

        public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension)
        {
            LastConsoleKey = consoleKey;
            return string.Equals(consoleKey, _supported.ConsoleKey, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(sourceExtension, _supported.Extension, StringComparison.OrdinalIgnoreCase)
                ? _target
                : null;
        }

        public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken cancellationToken = default)
            => new(sourcePath, Path.ChangeExtension(sourcePath, target.Extension), ConversionOutcome.Success, null);

        public bool Verify(string targetPath, ConversionTarget target)
            => true;
    }

    private sealed class FakeCollectionIndex : ICollectionIndex
    {
        private readonly IReadOnlyList<CollectionIndexEntry> _entries;

        public FakeCollectionIndex(IReadOnlyList<CollectionIndexEntry> entries)
        {
            _entries = entries;
        }

        public ValueTask<CollectionIndexMetadata> GetMetadataAsync(CancellationToken ct = default)
            => ValueTask.FromResult(new CollectionIndexMetadata());

        public ValueTask<int> CountEntriesAsync(CancellationToken ct = default)
            => ValueTask.FromResult(_entries.Count);

        public ValueTask<CollectionIndexEntry?> TryGetByPathAsync(string path, CancellationToken ct = default)
            => ValueTask.FromResult(_entries.FirstOrDefault(entry => string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase)));

        public ValueTask<IReadOnlyList<CollectionIndexEntry>> GetByPathsAsync(IReadOnlyList<string> paths, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<CollectionIndexEntry>>(Array.Empty<CollectionIndexEntry>());

        public ValueTask<IReadOnlyList<CollectionIndexEntry>> ListByConsoleAsync(string consoleKey, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<CollectionIndexEntry>>(Array.Empty<CollectionIndexEntry>());

        public ValueTask<IReadOnlyList<CollectionIndexEntry>> ListEntriesInScopeAsync(IReadOnlyList<string> roots, IReadOnlyCollection<string> extensions, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<CollectionIndexEntry>>(Array.Empty<CollectionIndexEntry>());

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
