using Romulus.Core.Caching;
using Xunit;

namespace Romulus.Tests;

public class LruCacheTests
{
    [Fact]
    public void Set_And_TryGet_ReturnsValue()
    {
        var cache = new LruCache<string, int>(10);
        cache.Set("a", 1);

        Assert.True(cache.TryGet("a", out var val));
        Assert.Equal(1, val);
    }

    [Fact]
    public void TryGet_MissingKey_ReturnsFalse()
    {
        var cache = new LruCache<string, int>(10);
        Assert.False(cache.TryGet("missing", out _));
    }

    [Fact]
    public void Evicts_LeastRecentlyUsed_OnOverflow()
    {
        var cache = new LruCache<string, int>(3);
        cache.Set("a", 1);
        cache.Set("b", 2);
        cache.Set("c", 3);
        cache.Set("d", 4); // should evict "a"

        Assert.False(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
        Assert.True(cache.TryGet("d", out _));
        Assert.Equal(3, cache.Count);
    }

    [Fact]
    public void TryGet_PromotesToFront_PreventsEviction()
    {
        var cache = new LruCache<string, int>(3);
        cache.Set("a", 1);
        cache.Set("b", 2);
        cache.Set("c", 3);

        // Access "a" to promote it
        cache.TryGet("a", out _);

        // Add "d" — should now evict "b" (least recently used), not "a"
        cache.Set("d", 4);

        Assert.True(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));
    }

    [Fact]
    public void Set_ExistingKey_UpdatesValue()
    {
        var cache = new LruCache<string, int>(10);
        cache.Set("a", 1);
        cache.Set("a", 99);

        Assert.True(cache.TryGet("a", out var val));
        Assert.Equal(99, val);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void Clear_RemovesAll()
    {
        var cache = new LruCache<string, int>(10);
        cache.Set("a", 1);
        cache.Set("b", 2);

        cache.Clear();

        Assert.Equal(0, cache.Count);
        Assert.False(cache.TryGet("a", out _));
    }

    [Fact]
    public void ContainsKey_Works()
    {
        var cache = new LruCache<string, int>(10);
        cache.Set("x", 42);

        Assert.True(cache.ContainsKey("x"));
        Assert.False(cache.ContainsKey("y"));
    }

    [Fact]
    public void MaxEntries_CanBeReduced_EvictsExcess()
    {
        var cache = new LruCache<string, int>(10);
        for (int i = 0; i < 10; i++)
            cache.Set($"k{i}", i);

        Assert.Equal(10, cache.Count);

        cache.MaxEntries = 3;

        Assert.Equal(3, cache.Count);
    }

    [Fact]
    public void GetSnapshot_ReturnsCurrentEntries()
    {
        var cache = new LruCache<string, int>(10);
        cache.Set("a", 1);
        cache.Set("b", 2);

        var snapshot = cache.GetSnapshot();
        Assert.Equal(2, snapshot.Count);
    }

    [Fact]
    public void Constructor_CaseInsensitiveComparer()
    {
        var cache = new LruCache<string, int>(10, StringComparer.OrdinalIgnoreCase);
        cache.Set("Key", 1);

        Assert.True(cache.TryGet("key", out var val));
        Assert.Equal(1, val);
        Assert.True(cache.TryGet("KEY", out _));
    }

    [Fact]
    public void Constructor_InvalidMaxEntries_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LruCache<string, int>(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LruCache<string, int>(-1));
    }
}
