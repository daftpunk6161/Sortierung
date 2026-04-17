using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.Metadata;

/// <summary>
/// ScreenScraper v2 API metadata provider.
/// Looks up game metadata by hash (SHA1/CRC32/MD5) or by name.
/// Respects rate limits and never stores credentials in code.
/// </summary>
public sealed class ScreenScraperMetadataProvider : IGameMetadataProvider, IDisposable
{
    private const string BaseUrl = "https://www.screenscraper.fr/api2/";
    private const string SoftwareName = "Romulus";

    private readonly HttpClient _httpClient;
    private readonly MetadataProviderSettings _settings;
    private readonly ILogger<ScreenScraperMetadataProvider>? _logger;
    private readonly SemaphoreSlim _rateLimiter = new(1, 1);
    private DateTime _lastRequestUtc = DateTime.MinValue;

    public string ProviderName => "ScreenScraper";

    public ScreenScraperMetadataProvider(
        MetadataProviderSettings settings,
        HttpClient? httpClient = null,
        ILogger<ScreenScraperMetadataProvider>? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _httpClient = httpClient ?? new HttpClient();
        _logger = logger;
    }

    public async Task<MetadataEnrichmentResult> TryGetMetadataAsync(
        MetadataEnrichmentRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_settings.DevId) || string.IsNullOrEmpty(_settings.DevPassword))
        {
            return new MetadataEnrichmentResult
            {
                Found = false,
                Source = ProviderName,
                FailureReason = "MissingCredentials"
            };
        }

        var systemId = ScreenScraperSystemMap.TryGetSystemId(request.ConsoleKey);
        if (!systemId.HasValue)
        {
            return new MetadataEnrichmentResult
            {
                Found = false,
                Source = ProviderName,
                FailureReason = "UnmappedSystem"
            };
        }

        // Build query - prefer hash-based lookup, fall back to name
        var url = BuildLookupUrl(systemId.Value, request);

        await RespectRateLimitAsync(ct);

        try
        {
            using var response = await _httpClient.GetAsync(url, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new MetadataEnrichmentResult { Found = false, Source = ProviderName, FailureReason = "NotFound" };
            }

            if (response.StatusCode == (HttpStatusCode)429)
            {
                _logger?.LogWarning("ScreenScraper rate limit exceeded for {Console}/{GameKey}",
                    request.ConsoleKey, request.GameKey);
                return new MetadataEnrichmentResult { Found = false, Source = ProviderName, FailureReason = "RateLimited" };
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return new MetadataEnrichmentResult { Found = false, Source = ProviderName, FailureReason = "Forbidden" };
            }

            if (!response.IsSuccessStatusCode)
            {
                return new MetadataEnrichmentResult { Found = false, Source = ProviderName, FailureReason = $"HttpError_{(int)response.StatusCode}" };
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var metadata = ParseGameInfoResponse(json, request.ConsoleKey);

            if (metadata is null)
            {
                return new MetadataEnrichmentResult { Found = false, Source = ProviderName, FailureReason = "ParseError" };
            }

            return new MetadataEnrichmentResult
            {
                Found = true,
                Metadata = metadata,
                Source = ProviderName
            };
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogWarning(ex, "ScreenScraper HTTP error for {Console}/{GameKey}",
                request.ConsoleKey, request.GameKey);
            return new MetadataEnrichmentResult { Found = false, Source = ProviderName, FailureReason = "NetworkError" };
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new MetadataEnrichmentResult { Found = false, Source = ProviderName, FailureReason = "Timeout" };
        }
    }

    public void Dispose()
    {
        _rateLimiter.Dispose();
    }

    private string BuildLookupUrl(int systemId, MetadataEnrichmentRequest request)
    {
        var baseParams = $"devid={Uri.EscapeDataString(_settings.DevId!)}" +
                         $"&devpassword={Uri.EscapeDataString(_settings.DevPassword!)}" +
                         $"&softname={SoftwareName}" +
                         $"&output=json" +
                         $"&systemeid={systemId}";

        if (!string.IsNullOrEmpty(_settings.Username) && !string.IsNullOrEmpty(_settings.UserPassword))
        {
            baseParams += $"&ssid={Uri.EscapeDataString(_settings.Username)}" +
                          $"&sspassword={Uri.EscapeDataString(_settings.UserPassword)}";
        }

        // Prefer hash-based lookup (most reliable)
        if (!string.IsNullOrEmpty(request.Sha1Hash))
            return $"{BaseUrl}jeuInfos.php?{baseParams}&sha1={request.Sha1Hash}";

        if (!string.IsNullOrEmpty(request.Md5Hash))
            return $"{BaseUrl}jeuInfos.php?{baseParams}&md5={request.Md5Hash}";

        if (!string.IsNullOrEmpty(request.Crc32Hash))
            return $"{BaseUrl}jeuInfos.php?{baseParams}&crc={request.Crc32Hash}";

        // Fall back to name search
        if (!string.IsNullOrEmpty(request.FileName))
            return $"{BaseUrl}jeuInfos.php?{baseParams}&romnom={Uri.EscapeDataString(request.FileName)}";

        return $"{BaseUrl}jeuInfos.php?{baseParams}&romnom={Uri.EscapeDataString(request.GameKey)}";
    }

    private GameMetadata? ParseGameInfoResponse(string json, string consoleKey)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("response", out var response))
                return null;
            if (!response.TryGetProperty("jeu", out var jeu))
                return null;

            var title = GetLocalizedText(jeu, "noms", "nom", _settings.PreferredMediaRegion);
            var description = GetLocalizedText(jeu, "synopsis", "synopsis", _settings.PreferredMediaRegion);
            var developer = GetNestedText(jeu, "developpeur", "text");
            var publisher = GetNestedText(jeu, "editeur", "text");
            var genres = GetGenres(jeu);
            var releaseYear = GetReleaseYear(jeu);
            var rating = GetRating(jeu);
            var players = GetPlayers(jeu);

            var region = _settings.PreferredMediaRegion;
            var coverArt = GetMediaUrl(jeu, "box-2D", region)
                           ?? GetMediaUrl(jeu, "box-2D-front", region);
            var screenshot = GetMediaUrl(jeu, "ss", region);
            var titleScreen = GetMediaUrl(jeu, "sstitle", region);
            var fanArt = GetMediaUrl(jeu, "fanart", region);

            var externalId = jeu.TryGetProperty("id", out var idProp)
                ? idProp.ToString()
                : null;

            return new GameMetadata
            {
                Title = title ?? "",
                Description = description,
                Developer = developer,
                Publisher = publisher,
                ReleaseYear = releaseYear,
                Genres = genres,
                Rating = rating,
                Players = players,
                CoverArtUrl = coverArt,
                ScreenshotUrl = screenshot,
                TitleScreenUrl = titleScreen,
                FanArtUrl = fanArt,
                ExternalId = externalId,
                Source = ProviderName,
                FetchedUtc = DateTime.UtcNow
            };
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "Failed to parse ScreenScraper JSON response");
            return null;
        }
    }

    private static string? GetLocalizedText(JsonElement parent, string arrayProp, string textProp, string preferredRegion)
    {
        if (!parent.TryGetProperty(arrayProp, out var array) || array.ValueKind != JsonValueKind.Array)
            return null;

        string? fallback = null;
        foreach (var item in array.EnumerateArray())
        {
            var region = item.TryGetProperty("region", out var r) ? r.GetString() : null;
            var text = item.TryGetProperty(textProp, out var t) ? t.GetString() : null;

            if (string.IsNullOrEmpty(text)) continue;

            if (string.Equals(region, preferredRegion, StringComparison.OrdinalIgnoreCase))
                return text;

            if (string.Equals(region, "wor", StringComparison.OrdinalIgnoreCase))
                fallback = text;

            fallback ??= text;
        }

        return fallback;
    }

    private static string? GetNestedText(JsonElement parent, string propName, string textProp)
    {
        if (!parent.TryGetProperty(propName, out var obj)) return null;
        return obj.TryGetProperty(textProp, out var text) ? text.GetString() : null;
    }

    private static List<string> GetGenres(JsonElement jeu)
    {
        var genres = new List<string>();
        if (!jeu.TryGetProperty("genres", out var genresArray) || genresArray.ValueKind != JsonValueKind.Array)
            return genres;

        foreach (var genre in genresArray.EnumerateArray())
        {
            if (!genre.TryGetProperty("noms", out var noms) || noms.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var nom in noms.EnumerateArray())
            {
                var lang = nom.TryGetProperty("langue", out var l) ? l.GetString() : null;
                var text = nom.TryGetProperty("text", out var t) ? t.GetString() : null;
                if (!string.IsNullOrEmpty(text) && string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase))
                {
                    genres.Add(text);
                    break;
                }
            }
        }

        return genres;
    }

    private static int? GetReleaseYear(JsonElement jeu)
    {
        if (!jeu.TryGetProperty("dates", out var dates) || dates.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var date in dates.EnumerateArray())
        {
            var text = date.TryGetProperty("text", out var t) ? t.GetString() : null;
            if (!string.IsNullOrEmpty(text) && text.Length >= 4 && int.TryParse(text[..4], out var year))
                return year;
        }

        return null;
    }

    private static double? GetRating(JsonElement jeu)
    {
        if (!jeu.TryGetProperty("note", out var note)) return null;
        if (!note.TryGetProperty("text", out var text)) return null;
        var ratingStr = text.GetString();
        if (double.TryParse(ratingStr, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var rating))
            return Math.Round(rating / 4.0, 2); // ScreenScraper uses 0-20, normalize to 0-5
        return null;
    }

    private static string? GetPlayers(JsonElement jeu)
    {
        if (!jeu.TryGetProperty("joueurs", out var joueurs)) return null;
        return joueurs.TryGetProperty("text", out var text) ? text.GetString() : null;
    }

    private static string? GetMediaUrl(JsonElement jeu, string mediaType, string preferredRegion)
    {
        if (!jeu.TryGetProperty("medias", out var medias) || medias.ValueKind != JsonValueKind.Array)
            return null;

        string? fallback = null;
        foreach (var media in medias.EnumerateArray())
        {
            var type = media.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (!string.Equals(type, mediaType, StringComparison.OrdinalIgnoreCase))
                continue;

            var url = media.TryGetProperty("url", out var u) ? u.GetString() : null;
            if (string.IsNullOrEmpty(url))
                continue;

            var region = media.TryGetProperty("region", out var r) ? r.GetString() : null;

            if (string.Equals(region, preferredRegion, StringComparison.OrdinalIgnoreCase))
                return url;

            fallback ??= url;
        }

        return fallback;
    }

    private async Task RespectRateLimitAsync(CancellationToken ct)
    {
        if (_settings.MaxRequestsPerSecond <= 0) return;

        await _rateLimiter.WaitAsync(ct);
        try
        {
            var minInterval = TimeSpan.FromSeconds(1.0 / _settings.MaxRequestsPerSecond);
            var elapsed = DateTime.UtcNow - _lastRequestUtc;
            if (elapsed < minInterval)
            {
                var delay = minInterval - elapsed;
                await Task.Delay(delay, ct);
            }
            _lastRequestUtc = DateTime.UtcNow;
        }
        finally
        {
            _rateLimiter.Release();
        }
    }
}
