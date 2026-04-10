using Romulus.Api;
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
        // Use a window long enough that two rapid calls stay within it
        var limiter = new RateLimiter(1, TimeSpan.FromSeconds(1));

        Assert.True(limiter.TryAcquire("c"));
        Assert.False(limiter.TryAcquire("c")); // still within window

        // Force window expiry by creating a new limiter with already-expired window
        var limiter2 = new RateLimiter(1, TimeSpan.FromMilliseconds(1));
        Assert.True(limiter2.TryAcquire("d"));
        Thread.Sleep(50);
        Assert.True(limiter2.TryAcquire("d")); // window expired, reset
    }
}
