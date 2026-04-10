using System.Collections.Concurrent;

namespace Romulus.Api;

/// <summary>
/// Per-client fixed-window rate limiter with automatic bucket eviction.
/// Port of API rate limiting from ApiServer.ps1.
/// </summary>
public sealed class RateLimiter
{
    private readonly int _maxRequests;
    private readonly TimeSpan _window;
    private readonly ConcurrentDictionary<string, ClientBucket> _buckets = new();
    private long _lastEvictionTicks = DateTime.UtcNow.Ticks;

    public RateLimiter(int maxRequestsPerWindow, TimeSpan window)
    {
        _maxRequests = maxRequestsPerWindow;
        _window = window;
    }

    public bool TryAcquire(string clientId)
    {
        if (_maxRequests <= 0) return true; // disabled

        var now = DateTime.UtcNow;
        var bucket = _buckets.GetOrAdd(clientId, _ => new ClientBucket(now));

        lock (bucket)
        {
            // Reset window if expired
            if (now - bucket.WindowStart > _window)
            {
                bucket.WindowStart = now;
                bucket.Count = 0;
            }

            if (bucket.Count >= _maxRequests)
                return false;

            bucket.Count++;
        }

        // Periodic eviction of stale buckets (every 5 minutes)
        // BUG-FIX: Use Interlocked for thread-safe access to _lastEvictionTicks
        var lastTicks = Interlocked.Read(ref _lastEvictionTicks);
        if (now.Ticks - lastTicks > TimeSpan.FromMinutes(5).Ticks)
        {
            // Only one thread should evict — CAS to prevent duplicate evictions
            if (Interlocked.CompareExchange(ref _lastEvictionTicks, now.Ticks, lastTicks) == lastTicks)
            {
                EvictStaleBuckets(now);
            }
        }

        return true;
    }

    private void EvictStaleBuckets(DateTime now)
    {
        foreach (var (key, bucket) in _buckets)
        {
            // V2-THR-M01: Evict after window + 5s instead of 2x window to prevent unbounded growth
            if (now - bucket.WindowStart > _window + TimeSpan.FromSeconds(5))
                _buckets.TryRemove(key, out _);
        }
    }

    private sealed class ClientBucket
    {
        public DateTime WindowStart;
        public int Count;

        public ClientBucket(DateTime start)
        {
            WindowStart = start;
            Count = 0;
        }
    }
}
