using System.Collections.Concurrent;

namespace RomCleanup.Api;

/// <summary>
/// Per-client sliding window rate limiter.
/// Port of API rate limiting from ApiServer.ps1.
/// </summary>
public sealed class RateLimiter
{
    private readonly int _maxRequests;
    private readonly TimeSpan _window;
    private readonly ConcurrentDictionary<string, ClientBucket> _buckets = new();

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
            return true;
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
