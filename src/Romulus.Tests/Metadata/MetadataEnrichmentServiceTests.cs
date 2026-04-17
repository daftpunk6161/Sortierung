using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Metadata;
using Xunit;

namespace Romulus.Tests.Metadata;

public sealed class MetadataEnrichmentServiceTests
{
    // ── Contracts ─────────────────────────────────────────────

    [Fact]
    public void GameMetadata_DefaultValues_AreEmpty()
    {
        var m = new GameMetadata();
        Assert.Equal("", m.Title);
        Assert.Null(m.Description);
        Assert.Empty(m.Genres);
        Assert.Equal("", m.Source);
    }

    [Fact]
    public void MetadataEnrichmentResult_NotFound_HasFailureReason()
    {
        var result = new MetadataEnrichmentResult
        {
            Found = false,
            FailureReason = "NotFound",
            Source = "Test"
        };

        Assert.False(result.Found);
        Assert.Null(result.Metadata);
        Assert.Equal("NotFound", result.FailureReason);
    }

    // ── Enrichment Service: Provider Cascade ──────────────────

    [Fact]
    public async Task EnrichAsync_ReturnsCachedMetadata_WhenCacheHit()
    {
        var cached = new GameMetadata { Title = "Cached Game", Source = "ScreenScraper", FetchedUtc = DateTime.UtcNow };
        var cache = new StubMetadataCache(cached);
        var provider = new StubMetadataProvider("ScreenScraper", found: true, metadata: new GameMetadata { Title = "Fresh" });
        var service = new MetadataEnrichmentService(cache, [provider]);

        var result = await service.EnrichAsync(
            new MetadataEnrichmentRequest { ConsoleKey = "SNES", GameKey = "Super Mario World" });

        Assert.True(result.Found);
        Assert.True(result.FromCache);
        Assert.Equal("Cached Game", result.Metadata!.Title);
    }

    [Fact]
    public async Task EnrichAsync_QueriesProvider_WhenCacheMiss()
    {
        var metadata = new GameMetadata { Title = "Super Mario World", Source = "ScreenScraper" };
        var cache = new StubMetadataCache(null);
        var provider = new StubMetadataProvider("ScreenScraper", found: true, metadata: metadata);
        var service = new MetadataEnrichmentService(cache, [provider]);

        var result = await service.EnrichAsync(
            new MetadataEnrichmentRequest { ConsoleKey = "SNES", GameKey = "Super Mario World" });

        Assert.True(result.Found);
        Assert.False(result.FromCache);
        Assert.Equal("Super Mario World", result.Metadata!.Title);
        Assert.Equal("ScreenScraper", result.Source);
    }

    [Fact]
    public async Task EnrichAsync_StoresInCache_AfterProviderHit()
    {
        var metadata = new GameMetadata { Title = "Zelda", Source = "ScreenScraper" };
        var cache = new StubMetadataCache(null);
        var provider = new StubMetadataProvider("ScreenScraper", found: true, metadata: metadata);
        var service = new MetadataEnrichmentService(cache, [provider]);

        await service.EnrichAsync(
            new MetadataEnrichmentRequest { ConsoleKey = "SNES", GameKey = "Zelda" });

        Assert.Equal("Zelda", cache.LastStoredMetadata?.Title);
    }

    [Fact]
    public async Task EnrichAsync_FallsToSecondProvider_WhenFirstFails()
    {
        var metadata = new GameMetadata { Title = "From LibRetro", Source = "LibRetro-DB" };
        var cache = new StubMetadataCache(null);
        var primary = new StubMetadataProvider("ScreenScraper", found: false, metadata: null);
        var fallback = new StubMetadataProvider("LibRetro-DB", found: true, metadata: metadata);
        var service = new MetadataEnrichmentService(cache, [primary, fallback]);

        var result = await service.EnrichAsync(
            new MetadataEnrichmentRequest { ConsoleKey = "SNES", GameKey = "Some Game" });

        Assert.True(result.Found);
        Assert.Equal("LibRetro-DB", result.Source);
    }

    [Fact]
    public async Task EnrichAsync_ReturnsNotFound_WhenAllProvidersFail()
    {
        var cache = new StubMetadataCache(null);
        var provider = new StubMetadataProvider("ScreenScraper", found: false, metadata: null);
        var service = new MetadataEnrichmentService(cache, [provider]);

        var result = await service.EnrichAsync(
            new MetadataEnrichmentRequest { ConsoleKey = "SNES", GameKey = "Unknown Game" });

        Assert.False(result.Found);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public async Task EnrichAsync_StoresNotFoundInCache_WhenAllProvidersFail()
    {
        var cache = new StubMetadataCache(null);
        var provider = new StubMetadataProvider("ScreenScraper", found: false, metadata: null);
        var service = new MetadataEnrichmentService(cache, [provider]);

        await service.EnrichAsync(
            new MetadataEnrichmentRequest { ConsoleKey = "SNES", GameKey = "Unknown" });

        Assert.True(cache.NotFoundStored);
    }

    [Fact]
    public async Task EnrichAsync_SkipsProvider_WhenNegativeCacheHit()
    {
        var cache = new StubMetadataCache(null) { NegativeCacheHit = true };
        var provider = new StubMetadataProvider("ScreenScraper", found: true,
            metadata: new GameMetadata { Title = "Should Not Be Called" });
        var service = new MetadataEnrichmentService(cache, [provider]);

        var result = await service.EnrichAsync(
            new MetadataEnrichmentRequest { ConsoleKey = "SNES", GameKey = "NegCached" });

        Assert.False(result.Found);
        Assert.Equal("NegativeCache", result.FailureReason);
        Assert.False(provider.WasCalled);
    }

    [Fact]
    public async Task EnrichAsync_HandlesProviderException_Gracefully()
    {
        var cache = new StubMetadataCache(null);
        var badProvider = new ThrowingMetadataProvider("BadProvider");
        var goodProvider = new StubMetadataProvider("Fallback", found: true,
            metadata: new GameMetadata { Title = "Fallback Hit", Source = "Fallback" });
        var service = new MetadataEnrichmentService(cache, [badProvider, goodProvider]);

        var result = await service.EnrichAsync(
            new MetadataEnrichmentRequest { ConsoleKey = "SNES", GameKey = "Test" });

        Assert.True(result.Found);
        Assert.Equal("Fallback", result.Source);
    }

    // ── Batch Enrichment ──────────────────────────────────────

    [Fact]
    public async Task EnrichBatchAsync_ProcessesAllRequests()
    {
        var metadata = new GameMetadata { Title = "Game", Source = "Test" };
        var cache = new StubMetadataCache(null);
        var provider = new StubMetadataProvider("Test", found: true, metadata: metadata);
        var service = new MetadataEnrichmentService(cache, [provider]);

        var requests = new[]
        {
            new MetadataEnrichmentRequest { ConsoleKey = "SNES", GameKey = "Game1" },
            new MetadataEnrichmentRequest { ConsoleKey = "SNES", GameKey = "Game2" },
            new MetadataEnrichmentRequest { ConsoleKey = "SNES", GameKey = "Game3" }
        };

        var results = await service.EnrichBatchAsync(requests);

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.True(r.Found));
    }

    [Fact]
    public async Task EnrichBatchAsync_RespectsRateLimit()
    {
        var metadata = new GameMetadata { Title = "Game", Source = "Test" };
        var cache = new StubMetadataCache(null);
        var provider = new StubMetadataProvider("Test", found: true, metadata: metadata);
        var service = new MetadataEnrichmentService(cache, [provider]);

        var requests = Enumerable.Range(1, 5)
            .Select(i => new MetadataEnrichmentRequest { ConsoleKey = "SNES", GameKey = $"Game{i}" })
            .ToArray();

        var results = await service.EnrichBatchAsync(requests);

        Assert.Equal(5, results.Count);
        Assert.Equal(5, provider.CallCount);
    }

    [Fact]
    public async Task EnrichBatchAsync_ReportsCancellation()
    {
        var metadata = new GameMetadata { Title = "Game", Source = "Test" };
        var cache = new StubMetadataCache(null);
        var provider = new StubMetadataProvider("Test", found: true, metadata: metadata);
        var service = new MetadataEnrichmentService(cache, [provider]);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.EnrichBatchAsync(
                [new MetadataEnrichmentRequest { ConsoleKey = "SNES", GameKey = "Game" }],
                ct: cts.Token));
    }

    // ── Test Stubs ────────────────────────────────────────────

    private sealed class StubMetadataCache(GameMetadata? cachedResult) : IGameMetadataCache
    {
        public GameMetadata? LastStoredMetadata { get; private set; }
        public bool NotFoundStored { get; private set; }
        public bool NegativeCacheHit { get; set; }

        public Task<GameMetadata?> TryGetAsync(string consoleKey, string gameKey, CancellationToken ct)
            => Task.FromResult(cachedResult);

        public Task SetAsync(string consoleKey, string gameKey, GameMetadata metadata, CancellationToken ct)
        {
            LastStoredMetadata = metadata;
            return Task.CompletedTask;
        }

        public Task SetNotFoundAsync(string consoleKey, string gameKey, string providerName, CancellationToken ct)
        {
            NotFoundStored = true;
            return Task.CompletedTask;
        }

        public Task<bool> IsNotFoundCachedAsync(string consoleKey, string gameKey, CancellationToken ct)
            => Task.FromResult(NegativeCacheHit);
    }

    private sealed class StubMetadataProvider(string name, bool found, GameMetadata? metadata) : IGameMetadataProvider
    {
        public string ProviderName => name;
        public bool WasCalled { get; private set; }
        public int CallCount { get; private set; }

        public Task<MetadataEnrichmentResult> TryGetMetadataAsync(MetadataEnrichmentRequest request, CancellationToken ct)
        {
            WasCalled = true;
            CallCount++;
            return Task.FromResult(new MetadataEnrichmentResult
            {
                Found = found,
                Metadata = metadata,
                Source = name,
                FailureReason = found ? null : "NotFound"
            });
        }
    }

    private sealed class ThrowingMetadataProvider(string name) : IGameMetadataProvider
    {
        public string ProviderName => name;
        public Task<MetadataEnrichmentResult> TryGetMetadataAsync(MetadataEnrichmentRequest request, CancellationToken ct)
            => throw new HttpRequestException("API unavailable");
    }
}
