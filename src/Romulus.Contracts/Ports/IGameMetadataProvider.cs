using Romulus.Contracts.Models;

namespace Romulus.Contracts.Ports;

/// <summary>
/// Port for external game metadata providers (ScreenScraper, IGDB, LibRetro-DB, etc.).
/// Implementations handle API calls, rate-limiting, and response mapping.
/// This port is consumed by infrastructure-level enrichment orchestration.
/// </summary>
public interface IGameMetadataProvider
{
    /// <summary>Stable provider name for cache keying and result attribution.</summary>
    string ProviderName { get; }

    /// <summary>
    /// Attempts to resolve metadata for a game.
    /// Returns a result indicating success/failure and the metadata when found.
    /// Implementations must respect external rate limits and not throw on transient failures.
    /// </summary>
    Task<MetadataEnrichmentResult> TryGetMetadataAsync(
        MetadataEnrichmentRequest request,
        CancellationToken ct = default);
}

/// <summary>
/// Port for caching resolved game metadata locally.
/// Implementations back this with LiteDB or other local stores.
/// </summary>
public interface IGameMetadataCache
{
    /// <summary>
    /// Looks up cached metadata by console key and game key.
    /// Returns null when no cached entry exists or when the cache entry is stale.
    /// </summary>
    Task<GameMetadata?> TryGetAsync(string consoleKey, string gameKey, CancellationToken ct = default);

    /// <summary>
    /// Stores metadata in the cache for a console key and game key combination.
    /// </summary>
    Task SetAsync(string consoleKey, string gameKey, GameMetadata metadata, CancellationToken ct = default);

    /// <summary>
    /// Stores a negative-cache entry (game was looked up but not found) to avoid repeated API calls.
    /// </summary>
    Task SetNotFoundAsync(string consoleKey, string gameKey, string providerName, CancellationToken ct = default);

    /// <summary>
    /// Checks whether a negative-cache entry exists for this game.
    /// </summary>
    Task<bool> IsNotFoundCachedAsync(string consoleKey, string gameKey, CancellationToken ct = default);
}
