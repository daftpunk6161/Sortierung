using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Conversion;
using Xunit;

namespace Romulus.Tests;

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

    // ──────────────────────────────────────────
    // ConvertFile error paths
    // ──────────────────────────────────────────

    [Fact]
    public void ConvertFile_SourceNotFound_ReturnsError()
    {
        var converter = new FakeFormatConverter(("PSX", ".bin"), new ConversionTarget(".chd", "chdman", "createcd"));
        using var service = new StandaloneConversionService(converter);

        var result = service.ConvertFile(Path.Combine(_tempDir, "nonexistent.bin"));

        Assert.Equal(ConversionOutcome.Error, result.Outcome);
        Assert.Contains("not found", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConvertFile_NoTargetForExtension_ReturnsSkipped()
    {
        var filePath = CreateFile(Path.Combine(_tempDir, "PSX", "game.txt"));
        var converter = new FakeFormatConverter(("PSX", ".bin"), new ConversionTarget(".chd", "chdman", "createcd"));
        using var service = new StandaloneConversionService(converter);

        var result = service.ConvertFile(filePath, consoleKey: "PSX");

        Assert.Equal(ConversionOutcome.Skipped, result.Outcome);
    }

    [Fact]
    public void ConvertFile_RequestedFormatMismatch_ReturnsSkipped()
    {
        var filePath = CreateFile(Path.Combine(_tempDir, "PSX", "game.bin"));
        var converter = new FakeFormatConverter(("PSX", ".bin"), new ConversionTarget(".chd", "chdman", "createcd"));
        using var service = new StandaloneConversionService(converter);

        // Requesting .rvz but conversion would produce .chd
        var result = service.ConvertFile(filePath, consoleKey: "PSX", targetFormat: ".rvz");

        Assert.Equal(ConversionOutcome.Skipped, result.Outcome);
        Assert.Contains("does not match", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConvertFile_NullConverter_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new StandaloneConversionService(null!));
    }

    [Fact]
    public void ConvertFile_NoIndex_UsesPathDetection()
    {
        var filePath = CreateFile(Path.Combine(_tempDir, "PSX", "game.bin"));
        var converter = new FakeFormatConverter(("PSX", ".bin"), new ConversionTarget(".chd", "chdman", "createcd"));
        using var service = new StandaloneConversionService(converter, collectionIndex: null);

        var result = service.ConvertFile(filePath);

        // PSX detected from path
        Assert.Equal(ConversionOutcome.Success, result.Outcome);
    }

    // ──────────────────────────────────────────
    // ConvertDirectory
    // ──────────────────────────────────────────

    [Fact]
    public void ConvertDirectory_NonExistentDir_ReturnsEmptyReport()
    {
        var converter = new FakeFormatConverter(("PSX", ".bin"), new ConversionTarget(".chd", "chdman", "createcd"));
        using var service = new StandaloneConversionService(converter);

        var report = service.ConvertDirectory(Path.Combine(_tempDir, "nonexistent"));

        Assert.Empty(report.Results);
        Assert.Equal(0, report.Converted);
        Assert.Equal(0, report.Skipped);
        Assert.Equal(0, report.Errors);
    }

    [Fact]
    public void ConvertDirectory_EmptyDir_ReturnsEmptyReport()
    {
        var emptyDir = Path.Combine(_tempDir, "empty");
        Directory.CreateDirectory(emptyDir);
        var converter = new FakeFormatConverter(("PSX", ".bin"), new ConversionTarget(".chd", "chdman", "createcd"));
        using var service = new StandaloneConversionService(converter);

        var report = service.ConvertDirectory(emptyDir, consoleKey: "PSX");

        Assert.Empty(report.Results);
        Assert.Equal(0, report.Converted);
    }

    [Fact]
    public void ConvertDirectory_WithMatchingFiles_ReportsConvertedCount()
    {
        var dir = Path.Combine(_tempDir, "PSX");
        CreateFile(Path.Combine(dir, "game1.bin"));
        CreateFile(Path.Combine(dir, "game2.bin"));
        CreateFile(Path.Combine(dir, "readme.txt")); // won't match converter

        var converter = new FakeFormatConverter(("PSX", ".bin"), new ConversionTarget(".chd", "chdman", "createcd"));
        using var service = new StandaloneConversionService(converter);

        var report = service.ConvertDirectory(dir, consoleKey: "PSX");

        Assert.Equal(3, report.Results.Count);
        Assert.Equal(2, report.Converted); // 2 bin files match
        Assert.Equal(1, report.Skipped);   // 1 txt file skipped
        Assert.Equal(0, report.Errors);
    }

    [Fact]
    public void ConvertDirectory_Recursive_IncludesSubdirs()
    {
        var dir = Path.Combine(_tempDir, "PSX");
        CreateFile(Path.Combine(dir, "game1.bin"));
        CreateFile(Path.Combine(dir, "sub", "game2.bin"));

        var converter = new FakeFormatConverter(("PSX", ".bin"), new ConversionTarget(".chd", "chdman", "createcd"));
        using var service = new StandaloneConversionService(converter);

        var report = service.ConvertDirectory(dir, consoleKey: "PSX", recursive: true);

        Assert.Equal(2, report.Converted);
    }

    [Fact]
    public void ConvertDirectory_NonRecursive_ExcludesSubdirs()
    {
        var dir = Path.Combine(_tempDir, "PSX");
        CreateFile(Path.Combine(dir, "game1.bin"));
        CreateFile(Path.Combine(dir, "sub", "game2.bin"));

        var converter = new FakeFormatConverter(("PSX", ".bin"), new ConversionTarget(".chd", "chdman", "createcd"));
        using var service = new StandaloneConversionService(converter);

        var report = service.ConvertDirectory(dir, consoleKey: "PSX", recursive: false);

        Assert.Equal(1, report.Converted);
    }

    [Fact]
    public void ConvertDirectory_Cancellation_ThrowsOperationCanceled()
    {
        var dir = Path.Combine(_tempDir, "PSX");
        CreateFile(Path.Combine(dir, "game1.bin"));
        CreateFile(Path.Combine(dir, "game2.bin"));
        CreateFile(Path.Combine(dir, "game3.bin"));

        var cts = new CancellationTokenSource();
        var converter = new CancellingFakeConverter(cts);
        using var service = new StandaloneConversionService(converter);

        Assert.Throws<OperationCanceledException>(() =>
            service.ConvertDirectory(dir, consoleKey: "PSX", cancellationToken: cts.Token));
    }

    [Fact]
    public void ConvertDirectory_ErrorOutcome_CountedInErrors()
    {
        var dir = Path.Combine(_tempDir, "PSX");
        CreateFile(Path.Combine(dir, "game.bin"));

        var converter = new ErrorFakeConverter();
        using var service = new StandaloneConversionService(converter);

        var report = service.ConvertDirectory(dir, consoleKey: "PSX");

        Assert.Equal(0, report.Converted);
        Assert.Equal(0, report.Skipped);
        Assert.Equal(1, report.Errors);
    }

    // ──────────────────────────────────────────
    // Dispose
    // ──────────────────────────────────────────

    [Fact]
    public void Dispose_DisposesLifetime()
    {
        var lifetime = new TrackingDisposable();
        var converter = new FakeFormatConverter(("PSX", ".bin"), new ConversionTarget(".chd", "chdman", "createcd"));
        var service = new StandaloneConversionService(converter, lifetime: lifetime);

        Assert.False(lifetime.Disposed);
        service.Dispose();
        Assert.True(lifetime.Disposed);
    }

    [Fact]
    public void Dispose_NullLifetime_DoesNotThrow()
    {
        var converter = new FakeFormatConverter(("PSX", ".bin"), new ConversionTarget(".chd", "chdman", "createcd"));
        var service = new StandaloneConversionService(converter, lifetime: null);
        service.Dispose(); // should not throw
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

    private sealed class CancellingFakeConverter : IFormatConverter
    {
        private readonly CancellationTokenSource _cts;

        public CancellingFakeConverter(CancellationTokenSource cts) => _cts = cts;

        public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension)
            => new(".chd", "chdman", "createcd");

        public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken cancellationToken = default)
        {
            _cts.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return new(sourcePath, null, ConversionOutcome.Error, "Should not reach here");
        }

        public bool Verify(string targetPath, ConversionTarget target) => true;
    }

    private sealed class ErrorFakeConverter : IFormatConverter
    {
        public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension)
            => new(".chd", "chdman", "createcd");

        public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken cancellationToken = default)
            => new(sourcePath, null, ConversionOutcome.Error, "Simulated error");

        public bool Verify(string targetPath, ConversionTarget target) => false;
    }

    private sealed class TrackingDisposable : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
