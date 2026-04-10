using Romulus.Core.Caching;
using Romulus.Infrastructure.Hashing;
using Romulus.Infrastructure.State;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// TEST-CONC: Concurrency tests for thread-safe components.
/// Covers LruCache, AppStateStore, FileHashService.
/// </summary>
public sealed class ConcurrencyTests : IDisposable
{
    private readonly string _tempDir;

    public ConcurrencyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_Conc_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // ── TEST-CONC-01: LruCache concurrent reads/writes ──

    [Fact]
    public void LruCache_ConcurrentSetAndGet_NoCrash()
    {
        var cache = new LruCache<string, int>(100);

        Parallel.For(0, 1000, i =>
        {
            cache.Set($"key-{i % 50}", i);
            cache.TryGet($"key-{i % 50}", out _);
        });

        Assert.InRange(cache.Count, 1, 100);
        // With 50 unique keys and capacity 100, most should survive
        Assert.True(cache.Count >= 10, $"Expected at least 10 cached items, got {cache.Count}");
    }

    [Fact]
    public void LruCache_ConcurrentEviction_ConsistentState()
    {
        var cache = new LruCache<int, int>(10);

        Parallel.For(0, 500, i =>
        {
            cache.Set(i, i * 2);
        });

        Assert.Equal(10, cache.Count);
        var snapshot = cache.GetSnapshot();
        Assert.Equal(10, snapshot.Count);
    }

    // ── TEST-CONC-02: AppStateStore concurrent SetValue/GetValue ──

    [Fact]
    public void AppState_ConcurrentSetGet_NoCrash()
    {
        var store = new AppStateStore();

        Parallel.For(0, 500, i =>
        {
            store.SetValue($"key-{i % 20}", i);
            store.GetValue<int>($"key-{i % 20}");
        });

        // With 20 unique keys, all should be stored
        var state = store.Get();
        Assert.InRange(state.Count, 1, 20);
    }

    [Fact]
    public void AppState_ConcurrentUndoRedo_NoCrash()
    {
        var store = new AppStateStore();

        // Pre-fill
        for (int i = 0; i < 50; i++)
            store.SetValue("x", i);

        // Concurrent undo/redo
        Parallel.For(0, 100, i =>
        {
            if (i % 2 == 0)
                store.Undo();
            else
                store.Redo();
        });

        // After undo/redo, value should be within the original range
        var val = store.GetValue<int>("x");
        Assert.InRange(val, 0, 49);
    }

    [Fact]
    public void AppState_ConcurrentWatchNotify_NoCrash()
    {
        var store = new AppStateStore();
        int notifications = 0;

        var watchers = Enumerable.Range(0, 10)
            .Select(_ => store.Watch(_ => Interlocked.Increment(ref notifications)))
            .ToList();

        Parallel.For(0, 100, i =>
        {
            store.SetValue("x", i);
        });

        // Dispose watchers
        foreach (var w in watchers)
            w.Dispose();

        // 10 watchers x 100 sets = up to 1000 notifications; at least some must have fired
        Assert.True(notifications >= 10, $"Expected at least 10 notifications, got {notifications}");
    }

    // ── TEST-CONC-03: FileHashService concurrent hashing ──

    [Fact]
    public void FileHashService_ConcurrentHash_ConsistentResults()
    {
        var svc = new FileHashService(1000);
        var file = Path.Combine(_tempDir, "concurrent.bin");
        File.WriteAllBytes(file, new byte[] { 1, 2, 3, 4, 5 });

        var hashes = new System.Collections.Concurrent.ConcurrentBag<string?>();

        Parallel.For(0, 50, _ =>
        {
            hashes.Add(svc.GetHash(file, "SHA1"));
        });

        var distinct = hashes.Where(h => h != null).Distinct().ToList();
        Assert.Single(distinct); // all threads see the same hash
    }

    // ── TEST-CONC-05: Cancel flag is thread-safe ──

    [Fact]
    public async Task AppState_CancelFlag_ThreadSafe()
    {
        var store = new AppStateStore();

        var tasks = new List<Task>();

        // Writer thread
        tasks.Add(Task.Run(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                store.RequestCancel();
                store.ResetCancel();
            }
        }));

        // Reader threads
        for (int t = 0; t < 4; t++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    _ = store.TestCancel();
                }
            }));
        }

        await Task.WhenAll(tasks);
        // No crash = pass
    }
}
