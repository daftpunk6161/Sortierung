using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Analysis;
using Romulus.Infrastructure.Watch;
using Xunit;

namespace Romulus.Tests;

public sealed class AutomationAndTrendServiceTests : IDisposable
{
    private readonly string _tempDir;

    public AutomationAndTrendServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_Automation_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void ScheduleService_QueuesPendingWhileBusy_AndFlushesDeterministically()
    {
        var now = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Local);
        using var scheduleService = new ScheduleService(() => now, TimeSpan.FromMilliseconds(25));
        using var triggered = new ManualResetEventSlim(false);
        var busy = true;
        var triggerCount = 0;

        scheduleService.IsBusyCheck = () => busy;
        scheduleService.Triggered += () =>
        {
            Interlocked.Increment(ref triggerCount);
            triggered.Set();
        };

        Assert.True(scheduleService.Start(intervalMinutes: 1));

        now = now.AddMinutes(1);
        Assert.True(SpinWait.SpinUntil(() => scheduleService.HasPending, TimeSpan.FromSeconds(2)));
        Assert.Equal(0, triggerCount);

        busy = false;
        scheduleService.FlushPendingIfNeeded();

        Assert.True(triggered.Wait(TimeSpan.FromSeconds(2)));
        Assert.Equal(1, triggerCount);
    }

    [Fact]
    public void WatchFolderService_FileChangeWhileBusy_QueuesPendingAndFlushes()
    {
        var nowUtc = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc);
        using var watchService = new WatchFolderService(() => nowUtc);
        using var triggered = new ManualResetEventSlim(false);
        var busy = true;
        var triggerCount = 0;
        var root = Path.Combine(_tempDir, "watch-root");
        Directory.CreateDirectory(root);

        watchService.IsBusyCheck = () => busy;
        watchService.RunTriggered += () =>
        {
            Interlocked.Increment(ref triggerCount);
            triggered.Set();
        };

        Assert.Equal(1, watchService.Start([root], debounceSeconds: 1, maxWaitSeconds: 1));

        File.WriteAllText(Path.Combine(root, "game.bin"), "data");

        Assert.True(SpinWait.SpinUntil(() => watchService.HasPending, TimeSpan.FromSeconds(4)));
        Assert.Equal(0, triggerCount);

        busy = false;
        nowUtc = nowUtc.AddSeconds(31);
        watchService.FlushPendingIfNeeded();

        Assert.True(triggered.Wait(TimeSpan.FromSeconds(4)));
        Assert.Equal(1, triggerCount);
    }

    [Fact]
    public async Task RunHistoryTrendService_UsesPersistedCollectionSizeBytes()
    {
        var snapshots = new[]
        {
            new CollectionRunSnapshot
            {
                RunId = "run-1",
                StartedUtc = new DateTime(2026, 4, 1, 8, 0, 0, DateTimeKind.Utc),
                CompletedUtc = new DateTime(2026, 4, 1, 8, 1, 0, DateTimeKind.Utc),
                TotalFiles = 10,
                CollectionSizeBytes = 1024,
                DatMatches = 8,
                Dupes = 2,
                Junk = 1,
                HealthScore = 90,
                SavedBytes = 999999,
                ConvertSavedBytes = 555555
            }
        };

        var history = await RunHistoryTrendService.LoadTrendHistoryAsync(new SnapshotOnlyCollectionIndex(snapshots));

        Assert.Single(history);
        Assert.Equal(1024, history[0].SizeBytes);
    }

    private sealed class SnapshotOnlyCollectionIndex : ICollectionIndex
    {
        private readonly IReadOnlyList<CollectionRunSnapshot> _snapshots;

        public SnapshotOnlyCollectionIndex(IReadOnlyList<CollectionRunSnapshot> snapshots)
        {
            _snapshots = snapshots;
        }

        public ValueTask<CollectionIndexMetadata> GetMetadataAsync(CancellationToken ct = default)
            => ValueTask.FromResult(new CollectionIndexMetadata());

        public ValueTask<int> CountEntriesAsync(CancellationToken ct = default)
            => ValueTask.FromResult(0);

        public ValueTask<CollectionIndexEntry?> TryGetByPathAsync(string path, CancellationToken ct = default)
            => ValueTask.FromResult<CollectionIndexEntry?>(null);

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
            => ValueTask.FromResult(_snapshots.Count);

        public ValueTask<IReadOnlyList<CollectionRunSnapshot>> ListRunSnapshotsAsync(int limit = 50, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<CollectionRunSnapshot>>(_snapshots.Take(limit).ToArray());
    }
}
