using Microsoft.Extensions.Logging;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.Metadata;

/// <summary>
/// Orchestrates game metadata enrichment through a cascade of providers with caching.
/// Tries cache first, then each provider in priority order, stores results.
/// </summary>
public sealed class MetadataEnrichmentService
{
    private readonly IGameMetadataCache _cache;
    private readonly IReadOnlyList<IGameMetadataProvider> _providers;
    private readonly ILogger<MetadataEnrichmentService>? _logger;

    public MetadataEnrichmentService(
        IGameMetadataCache cache,
        IEnumerable<IGameMetadataProvider> providers,
        ILogger<MetadataEnrichmentService>? logger = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _providers = (providers ?? throw new ArgumentNullException(nameof(providers))).ToList();
        _logger = logger;
    }

    /// <summary>
    /// Enriches a single game with metadata.
    /// </summary>
    public async Task<MetadataEnrichmentResult> EnrichAsync(
        MetadataEnrichmentRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // 1. Check positive cache
        var cached = await _cache.TryGetAsync(request.ConsoleKey, request.GameKey, ct);
        if (cached is not null)
        {
            return new MetadataEnrichmentResult
            {
                Found = true,
                Metadata = cached,
                Source = cached.Source,
                FromCache = true
            };
        }

        // 2. Check negative cache (avoid repeated lookups for known-missing games)
        if (await _cache.IsNotFoundCachedAsync(request.ConsoleKey, request.GameKey, ct))
        {
            return new MetadataEnrichmentResult
            {
                Found = false,
                Source = "NegativeCache",
                FailureReason = "NegativeCache"
            };
        }

        // 3. Query providers in priority order
        foreach (var provider in _providers)
        {
            try
            {
                var result = await provider.TryGetMetadataAsync(request, ct);
                if (result.Found && result.Metadata is not null)
                {
                    await _cache.SetAsync(request.ConsoleKey, request.GameKey, result.Metadata, ct);
                    return new MetadataEnrichmentResult
                    {
                        Found = true,
                        Metadata = result.Metadata,
                        Source = provider.ProviderName,
                        FromCache = false
                    };
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex,
                    "Metadata provider {Provider} failed for {Console}/{GameKey}",
                    provider.ProviderName, request.ConsoleKey, request.GameKey);
            }
        }

        // 4. All providers failed - store negative cache entry
        var lastProvider = _providers.Count > 0 ? _providers[^1].ProviderName : "none";
        await _cache.SetNotFoundAsync(request.ConsoleKey, request.GameKey, lastProvider, ct);

        return new MetadataEnrichmentResult
        {
            Found = false,
            FailureReason = "NotFound"
        };
    }

    /// <summary>
    /// Enriches a batch of games with metadata.
    /// Processes sequentially to respect external API rate limits.
    /// </summary>
    public async Task<IReadOnlyList<MetadataEnrichmentResult>> EnrichBatchAsync(
        IReadOnlyList<MetadataEnrichmentRequest> requests,
        Action<int, int>? onProgress = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var results = new List<MetadataEnrichmentResult>(requests.Count);
        for (var i = 0; i < requests.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var result = await EnrichAsync(requests[i], ct);
            results.Add(result);
            onProgress?.Invoke(i + 1, requests.Count);
        }

        return results;
    }
}
