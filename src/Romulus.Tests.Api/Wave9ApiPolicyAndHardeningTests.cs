using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Xunit;

namespace Romulus.Tests;

public sealed class Wave9ApiPolicyAndHardeningTests : IDisposable
{
    private const string ApiKey = "wave9-api-key";
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "romulus-w9-api-" + Guid.NewGuid().ToString("N"));

    public Wave9ApiPolicyAndHardeningTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task PolicyValidate_RequiresApiKey()
    {
        using var factory = CreateFactory([]);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/policies/validate", Json(new
        {
            policyText = "id: all-zip\nname: Alle ZIP\nallowedExtensions: [.zip]",
            roots = new[] { _tempDir }
        }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PolicyValidate_ReturnsReportFromReadOnlyPersistedIndex()
    {
        var root = Path.Combine(_tempDir, "roms");
        Directory.CreateDirectory(root);
        using var factory = CreateFactory(
        [
            new CollectionIndexEntry
            {
                Path = Path.Combine(root, "demo.sfc"),
                Root = root,
                FileName = "demo.sfc",
                Extension = ".sfc",
                ConsoleKey = "SNES",
                GameKey = "demo",
                Region = "US"
            }
        ]);
        using var client = CreateAuthClient(factory);

        var response = await client.PostAsync("/policies/validate", Json(new
        {
            policyText = "id: all-zip\nname: Alle ZIP\nallowedExtensions: [.zip]",
            roots = new[] { root }
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("experimental", response.Headers.GetValues("X-Romulus-API-Status").Single());
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(doc.RootElement.GetProperty("isCompliant").GetBoolean());
        Assert.Equal("allowed-extensions", doc.RootElement.GetProperty("violations")[0].GetProperty("ruleId").GetString());
    }

    [Fact]
    public async Task OpenApi_DeclaresPolicyHealthAndProvenance_AndNoPluginMarketplaceSurface()
    {
        using var spec = JsonDocument.Parse(await OpenApiTestHelper.FetchOpenApiJsonAsync());
        var paths = spec.RootElement.GetProperty("paths");
        var schemas = spec.RootElement.GetProperty("components").GetProperty("schemas");

        Assert.True(paths.TryGetProperty("/policies/validate", out var policyPath), "Missing policy validation path.");
        Assert.True(policyPath.TryGetProperty("post", out var policyPost), "Policy validation must declare POST.");
        Assert.True(policyPost.TryGetProperty("requestBody", out _), "Policy validation must declare request body.");
        Assert.True(paths.TryGetProperty("/health/collection", out _), "Missing collection health endpoint.");
        Assert.True(paths.TryGetProperty("/roms/{fingerprint}/provenance", out _), "Missing provenance endpoint.");
        Assert.True(schemas.TryGetProperty("PolicyValidationRequest", out _), "Missing PolicyValidationRequest schema.");
        Assert.True(schemas.TryGetProperty("PolicyValidationReport", out _), "Missing PolicyValidationReport schema.");

        foreach (var path in paths.EnumerateObject())
        {
            Assert.DoesNotContain("plugin", path.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("marketplace", path.Name, StringComparison.OrdinalIgnoreCase);
        }
    }

    private WebApplicationFactory<Program> CreateFactory(IReadOnlyList<CollectionIndexEntry> entries)
        => ApiTestFactory.Create(
            new Dictionary<string, string?>
            {
                ["ApiKey"] = ApiKey,
                ["RateLimitRequests"] = "120",
                ["RateLimitWindowSeconds"] = "60"
            },
            collectionIndex: new FakeCollectionIndex(entries));

    private static HttpClient CreateAuthClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        client.DefaultRequestHeaders.Add("X-Client-Id", "wave9-policy");
        return client;
    }

    private static StringContent Json(object payload)
        => new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private sealed class FakeCollectionIndex : ICollectionIndex
    {
        private readonly IReadOnlyList<CollectionIndexEntry> _entries;

        public FakeCollectionIndex(IReadOnlyList<CollectionIndexEntry> entries) => _entries = entries;
        public ValueTask<CollectionIndexMetadata> GetMetadataAsync(CancellationToken ct = default) => new(new CollectionIndexMetadata());
        public ValueTask<int> CountEntriesAsync(CancellationToken ct = default) => new(_entries.Count);
        public ValueTask<CollectionIndexEntry?> TryGetByPathAsync(string path, CancellationToken ct = default) => new(_entries.FirstOrDefault(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase)));
        public ValueTask<IReadOnlyList<CollectionIndexEntry>> GetByPathsAsync(IReadOnlyList<string> paths, CancellationToken ct = default) => new(_entries.Where(e => paths.Contains(e.Path, StringComparer.OrdinalIgnoreCase)).ToArray());
        public ValueTask<IReadOnlyList<CollectionIndexEntry>> ListByConsoleAsync(string consoleKey, CancellationToken ct = default) => new(_entries.Where(e => string.Equals(e.ConsoleKey, consoleKey, StringComparison.OrdinalIgnoreCase)).ToArray());
        public ValueTask<IReadOnlyList<CollectionIndexEntry>> ListEntriesInScopeAsync(IReadOnlyList<string> roots, IReadOnlyCollection<string> extensions, CancellationToken ct = default)
        {
            var scoped = _entries
                .Where(entry => roots.Any(root => entry.Path.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
                .Where(entry => extensions.Count == 0 || extensions.Contains(entry.Extension, StringComparer.OrdinalIgnoreCase))
                .OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return new(scoped);
        }
        public ValueTask UpsertEntriesAsync(IReadOnlyList<CollectionIndexEntry> entries, CancellationToken ct = default) => default;
        public ValueTask RemovePathsAsync(IReadOnlyList<string> paths, CancellationToken ct = default) => default;
        public ValueTask<CollectionHashCacheEntry?> TryGetHashAsync(string path, string algorithm, long sizeBytes, DateTime lastWriteUtc, CancellationToken ct = default) => new((CollectionHashCacheEntry?)null);
        public ValueTask SetHashAsync(CollectionHashCacheEntry entry, CancellationToken ct = default) => default;
        public ValueTask AppendRunSnapshotAsync(CollectionRunSnapshot snapshot, CancellationToken ct = default) => default;
        public ValueTask<int> CountRunSnapshotsAsync(CancellationToken ct = default) => new(0);
        public ValueTask<IReadOnlyList<CollectionRunSnapshot>> ListRunSnapshotsAsync(int limit = 50, CancellationToken ct = default) => new(Array.Empty<CollectionRunSnapshot>());
    }
}
