using System.Net;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Metadata;
using Xunit;

namespace Romulus.Tests.Metadata;

public sealed class ScreenScraperMetadataProviderTests : IDisposable
{
    private readonly ScreenScraperMetadataProvider _provider;
    private readonly MockHttpMessageHandler _handler;

    public ScreenScraperMetadataProviderTests()
    {
        _handler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(_handler);
        _provider = new ScreenScraperMetadataProvider(
            new MetadataProviderSettings
            {
                ProviderName = "ScreenScraper",
                DevId = "test-dev",
                DevPassword = "test-pass",
                MaxRequestsPerSecond = 100
            },
            httpClient);
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public void ProviderName_IsScreenScraper()
    {
        Assert.Equal("ScreenScraper", _provider.ProviderName);
    }

    [Fact]
    public async Task TryGetMetadata_ReturnsMissingCredentials_WhenNoDevId()
    {
        var provider = new ScreenScraperMetadataProvider(
            new MetadataProviderSettings { DevId = null, DevPassword = null });

        var result = await provider.TryGetMetadataAsync(
            new MetadataEnrichmentRequest { ConsoleKey = "SNES", GameKey = "Test" });

        Assert.False(result.Found);
        Assert.Equal("MissingCredentials", result.FailureReason);
    }

    [Fact]
    public async Task TryGetMetadata_ReturnsUnmappedSystem_WhenConsoleNotMapped()
    {
        var result = await _provider.TryGetMetadataAsync(
            new MetadataEnrichmentRequest { ConsoleKey = "UNKNOWN_CONSOLE", GameKey = "Test" });

        Assert.False(result.Found);
        Assert.Equal("UnmappedSystem", result.FailureReason);
    }

    [Fact]
    public async Task TryGetMetadata_ParsesValidResponse()
    {
        _handler.ResponseJson = BuildScreenScraperResponse(
            title: "Super Mario World",
            developer: "Nintendo",
            publisher: "Nintendo",
            genre: "Platformer",
            year: "1990",
            rating: "18",
            players: "1-2",
            coverUrl: "https://cdn.screenscraper.fr/box-2D/smw.jpg",
            screenshotUrl: "https://cdn.screenscraper.fr/ss/smw.png");

        var result = await _provider.TryGetMetadataAsync(
            new MetadataEnrichmentRequest { ConsoleKey = "SNES", GameKey = "Super Mario World", Sha1Hash = "abc123" });

        Assert.True(result.Found);
        Assert.Equal("ScreenScraper", result.Source);
        Assert.NotNull(result.Metadata);
        Assert.Equal("Super Mario World", result.Metadata!.Title);
        Assert.Equal("Nintendo", result.Metadata.Developer);
        Assert.Equal("Nintendo", result.Metadata.Publisher);
        Assert.Contains("Platformer", result.Metadata.Genres);
        Assert.Equal(1990, result.Metadata.ReleaseYear);
        Assert.NotNull(result.Metadata.Rating);
        Assert.Equal("1-2", result.Metadata.Players);
        Assert.Equal("https://cdn.screenscraper.fr/box-2D/smw.jpg", result.Metadata.CoverArtUrl);
        Assert.Equal("https://cdn.screenscraper.fr/ss/smw.png", result.Metadata.ScreenshotUrl);
    }

    [Fact]
    public async Task TryGetMetadata_ReturnsNotFound_On404()
    {
        _handler.StatusCode = HttpStatusCode.NotFound;
        _handler.ResponseJson = "";

        var result = await _provider.TryGetMetadataAsync(
            new MetadataEnrichmentRequest { ConsoleKey = "SNES", GameKey = "Unknown", Sha1Hash = "000" });

        Assert.False(result.Found);
        Assert.Equal("NotFound", result.FailureReason);
    }

    [Fact]
    public async Task TryGetMetadata_ReturnsRateLimited_On429()
    {
        _handler.StatusCode = (HttpStatusCode)429;
        _handler.ResponseJson = "";

        var result = await _provider.TryGetMetadataAsync(
            new MetadataEnrichmentRequest { ConsoleKey = "SNES", GameKey = "Game", Sha1Hash = "abc" });

        Assert.False(result.Found);
        Assert.Equal("RateLimited", result.FailureReason);
    }

    [Fact]
    public async Task TryGetMetadata_ReturnsNetworkError_OnException()
    {
        _handler.ThrowException = new HttpRequestException("Connection refused");

        var result = await _provider.TryGetMetadataAsync(
            new MetadataEnrichmentRequest { ConsoleKey = "SNES", GameKey = "Game", Sha1Hash = "abc" });

        Assert.False(result.Found);
        Assert.Equal("NetworkError", result.FailureReason);
    }

    [Fact]
    public async Task TryGetMetadata_UsesSha1_WhenAvailable()
    {
        _handler.ResponseJson = BuildScreenScraperResponse("Game");
        _handler.OnRequestCapture = true;

        await _provider.TryGetMetadataAsync(
            new MetadataEnrichmentRequest
            {
                ConsoleKey = "SNES",
                GameKey = "Game",
                Sha1Hash = "abc123",
                Crc32Hash = "def456",
                Md5Hash = "ghi789"
            });

        Assert.Contains("sha1=abc123", _handler.CapturedUrl);
        Assert.DoesNotContain("crc=", _handler.CapturedUrl);
        Assert.DoesNotContain("md5=", _handler.CapturedUrl);
    }

    [Fact]
    public async Task TryGetMetadata_FallsToMd5_WhenNoSha1()
    {
        _handler.ResponseJson = BuildScreenScraperResponse("Game");
        _handler.OnRequestCapture = true;

        await _provider.TryGetMetadataAsync(
            new MetadataEnrichmentRequest
            {
                ConsoleKey = "SNES",
                GameKey = "Game",
                Md5Hash = "md5hash"
            });

        Assert.Contains("md5=md5hash", _handler.CapturedUrl);
    }

    [Fact]
    public async Task TryGetMetadata_FallsToGameKey_WhenNoHashes()
    {
        _handler.ResponseJson = BuildScreenScraperResponse("Game");
        _handler.OnRequestCapture = true;

        await _provider.TryGetMetadataAsync(
            new MetadataEnrichmentRequest
            {
                ConsoleKey = "SNES",
                GameKey = "Super Mario World"
            });

        Assert.Contains("romnom=Super", _handler.CapturedUrl);
    }

    // ── System Map Tests ──────────────────────────────────────

    [Theory]
    [InlineData("SNES", 4)]
    [InlineData("PS1", 57)]
    [InlineData("MD", 1)]
    [InlineData("GBA", 12)]
    [InlineData("N64", 14)]
    [InlineData("DC", 23)]
    public void SystemMap_ResolvesKnownConsoles(string consoleKey, int expectedId)
    {
        var id = ScreenScraperSystemMap.TryGetSystemId(consoleKey);
        Assert.NotNull(id);
        Assert.Equal(expectedId, id.Value);
    }

    [Fact]
    public void SystemMap_ReturnsNull_ForUnknownConsole()
    {
        Assert.Null(ScreenScraperSystemMap.TryGetSystemId("NONEXISTENT"));
    }

    [Fact]
    public void SystemMap_IsCaseInsensitive()
    {
        Assert.Equal(
            ScreenScraperSystemMap.TryGetSystemId("snes"),
            ScreenScraperSystemMap.TryGetSystemId("SNES"));
    }

    // ── Helpers ───────────────────────────────────────────────

    private static string BuildScreenScraperResponse(
        string title = "Game",
        string? developer = null,
        string? publisher = null,
        string? genre = null,
        string? year = null,
        string? rating = null,
        string? players = null,
        string? coverUrl = null,
        string? screenshotUrl = null)
    {
        var genresJson = genre is not null
            ? "[{\"noms\":[{\"langue\":\"en\",\"text\":\"" + genre + "\"}]}]"
            : "[]";

        var datesJson = year is not null
            ? "[{\"region\":\"wor\",\"text\":\"" + year + "\"}]"
            : "[]";

        var medias = new List<string>();
        if (coverUrl is not null)
            medias.Add("{\"type\":\"box-2D\",\"region\":\"us\",\"url\":\"" + coverUrl + "\"}");
        if (screenshotUrl is not null)
            medias.Add("{\"type\":\"ss\",\"region\":\"us\",\"url\":\"" + screenshotUrl + "\"}");

        var mediasJson = string.Join(",", medias);
        var dev = developer ?? "";
        var pub = publisher ?? "";
        var rat = rating ?? "";
        var play = players ?? "";

        return $$"""
        {
          "response": {
            "jeu": {
              "id": "12345",
              "noms": [{"region":"wor","nom":"{{title}}"}],
              "synopsis": [{"region":"wor","synopsis":"A great game."}],
              "developpeur": {"text":"{{dev}}"},
              "editeur": {"text":"{{pub}}"},
              "genres": {{genresJson}},
              "dates": {{datesJson}},
              "note": {"text":"{{rat}}"},
              "joueurs": {"text":"{{play}}"},
              "medias": [{{mediasJson}}]
            }
          }
        }
        """;
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        public string ResponseJson { get; set; } = "{}";
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public HttpRequestException? ThrowException { get; set; }
        public bool OnRequestCapture { get; set; }
        public string CapturedUrl { get; private set; } = "";

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (OnRequestCapture)
                CapturedUrl = request.RequestUri?.ToString() ?? "";

            if (ThrowException is not null)
                throw ThrowException;

            return Task.FromResult(new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(ResponseJson, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
