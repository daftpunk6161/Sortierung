using Romulus.Api;
using Romulus.Contracts.Ports;
using Xunit;

namespace Romulus.Tests;

public class RateLimiterTests
{
    [Fact]
    public void TryAcquire_AllowsUpToLimit()
    {
        var limiter = new RateLimiter(3, TimeSpan.FromMinutes(1));

        Assert.True(limiter.TryAcquire("client1"));
        Assert.True(limiter.TryAcquire("client1"));
        Assert.True(limiter.TryAcquire("client1"));
        Assert.False(limiter.TryAcquire("client1"));
    }

    [Fact]
    public void TryAcquire_DifferentClients_IndependentBuckets()
    {
        var limiter = new RateLimiter(2, TimeSpan.FromMinutes(1));

        Assert.True(limiter.TryAcquire("a"));
        Assert.True(limiter.TryAcquire("a"));
        Assert.False(limiter.TryAcquire("a"));

        // Client b has its own bucket
        Assert.True(limiter.TryAcquire("b"));
        Assert.True(limiter.TryAcquire("b"));
        Assert.False(limiter.TryAcquire("b"));
    }

    [Fact]
    public void TryAcquire_Disabled_AlwaysAllows()
    {
        var limiter = new RateLimiter(0, TimeSpan.FromMinutes(1));

        for (int i = 0; i < 1000; i++)
            Assert.True(limiter.TryAcquire("client"));
    }

    [Fact]
    public void TryAcquire_WindowExpires_Resets()
    {
        var clock = new TestTimeProvider(new DateTimeOffset(2026, 4, 10, 12, 0, 0, TimeSpan.Zero));
        var limiter = new RateLimiter(1, TimeSpan.FromSeconds(1), clock);

        Assert.True(limiter.TryAcquire("c"));
        Assert.False(limiter.TryAcquire("c"));

        clock.Advance(TimeSpan.FromSeconds(2));

        Assert.True(limiter.TryAcquire("c"));
    }

    private sealed class TestTimeProvider(DateTimeOffset initialUtcNow) : ITimeProvider
    {
        private DateTimeOffset _utcNow = initialUtcNow;

        public DateTimeOffset UtcNow => _utcNow;

        public void Advance(TimeSpan delta)
        {
            _utcNow = _utcNow.Add(delta);
        }
    }
}
