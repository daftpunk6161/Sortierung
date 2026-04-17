using LiteDB;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Metadata;
using Xunit;

namespace Romulus.Tests.Metadata;

public sealed class LiteDbGameMetadataCacheTests : IDisposable
{
    private readonly LiteDatabase _db;
    private readonly LiteDbGameMetadataCache _cache;

    public LiteDbGameMetadataCacheTests()
    {
        _db = new LiteDatabase("Filename=:memory:");
        _cache = new LiteDbGameMetadataCache(_db);
    }

    public void Dispose()
    {
        _cache.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task TryGetAsync_ReturnsNull_WhenCacheEmpty()
    {
        var result = await _cache.TryGetAsync("SNES", "Super Mario World");
        Assert.Null(result);
    }

    [Fact]
    public async Task SetAsync_ThenTryGetAsync_ReturnsCachedEntry()
    {
        var metadata = new GameMetadata
        {
            Title = "Super Mario World",
            Developer = "Nintendo",
            Publisher = "Nintendo",
            ReleaseYear = 1990,
            Genres = ["Platformer"],
            Rating = 4.8,
            CoverArtUrl = "https://example.com/smw.jpg",
            Source = "ScreenScraper",
            FetchedUtc = DateTime.UtcNow
        };

        await _cache.SetAsync("SNES", "Super Mario World", metadata);
        var result = await _cache.TryGetAsync("SNES", "Super Mario World");

        Assert.NotNull(result);
        Assert.Equal("Super Mario World", result!.Title);
        Assert.Equal("Nintendo", result.Developer);
        Assert.Equal(1990, result.ReleaseYear);
        Assert.Single(result.Genres);
        Assert.Equal("Platformer", result.Genres[0]);
        Assert.Equal(4.8, result.Rating);
    }

    [Fact]
    public async Task SetAsync_Upserts_ExistingEntry()
    {
        var original = new GameMetadata { Title = "Original", Source = "Test", FetchedUtc = DateTime.UtcNow };
        var updated = new GameMetadata { Title = "Updated", Source = "Test", FetchedUtc = DateTime.UtcNow };

        await _cache.SetAsync("SNES", "Game", original);
        await _cache.SetAsync("SNES", "Game", updated);
        var result = await _cache.TryGetAsync("SNES", "Game");

        Assert.NotNull(result);
        Assert.Equal("Updated", result!.Title);
    }

    [Fact]
    public async Task IsNotFoundCachedAsync_ReturnsFalse_WhenNotSet()
    {
        var result = await _cache.IsNotFoundCachedAsync("SNES", "Unknown Game");
        Assert.False(result);
    }

    [Fact]
    public async Task SetNotFoundAsync_ThenIsNotFoundCachedAsync_ReturnsTrue()
    {
        await _cache.SetNotFoundAsync("SNES", "Unknown Game", "ScreenScraper");
        var result = await _cache.IsNotFoundCachedAsync("SNES", "Unknown Game");
        Assert.True(result);
    }

    [Fact]
    public async Task SetAsync_ClearsNegativeCache()
    {
        await _cache.SetNotFoundAsync("SNES", "Game", "ScreenScraper");
        Assert.True(await _cache.IsNotFoundCachedAsync("SNES", "Game"));

        await _cache.SetAsync("SNES", "Game",
            new GameMetadata { Title = "Found!", Source = "Test", FetchedUtc = DateTime.UtcNow });

        Assert.False(await _cache.IsNotFoundCachedAsync("SNES", "Game"));
    }

    [Fact]
    public async Task DifferentConsoleKeys_AreIndependent()
    {
        var metadata = new GameMetadata { Title = "Sonic", Source = "Test", FetchedUtc = DateTime.UtcNow };

        await _cache.SetAsync("MD", "Sonic", metadata);
        var mdResult = await _cache.TryGetAsync("MD", "Sonic");
        var snesResult = await _cache.TryGetAsync("SNES", "Sonic");

        Assert.NotNull(mdResult);
        Assert.Null(snesResult);
    }

    [Fact]
    public async Task ConsoleKey_IsCaseInsensitive()
    {
        var metadata = new GameMetadata { Title = "Game", Source = "Test", FetchedUtc = DateTime.UtcNow };

        await _cache.SetAsync("snes", "Game", metadata);
        var result = await _cache.TryGetAsync("SNES", "Game");

        Assert.NotNull(result);
        Assert.Equal("Game", result!.Title);
    }
}
