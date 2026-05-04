using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.FileSystem;
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

        var response = await client.PostAsync("/v1-experimental/policies/validate", Json(new
        {
            policyText = "id: all-zip\nname: Alle ZIP\nallowedExtensions: [.zip]",
            roots = new[] { _tempDir }
        }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ExperimentalPrefix_HealthAndPolicyValidate_WorkAtRuntime()
    {
        var root = Path.Combine(_tempDir, "prefixed-roms");
        Directory.CreateDirectory(root);
        using var factory = CreateFactory([]);
        using var client = CreateAuthClient(factory);

        var health = await client.GetAsync("/v1-experimental/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("experimental", health.Headers.GetValues("X-Romulus-API-Status").Single());

        var policy = await client.PostAsync("/v1-experimental/policies/validate", Json(new
        {
            policyText = "id: all-zip\nname: Alle ZIP\nallowedExtensions: [.zip]",
            roots = new[] { root }
        }));

        Assert.Equal(HttpStatusCode.OK, policy.StatusCode);
        using var doc = JsonDocument.Parse(await policy.Content.ReadAsStringAsync());
        Assert.False(doc.RootElement.GetProperty("signature").GetProperty("isPresent").GetBoolean());
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
    public async Task AuditViewerEndpoints_ListRowsSidecarAndVerification()
    {
        var auditRoot = Path.Combine(_tempDir, "audit");
        Directory.CreateDirectory(auditRoot);
        var auditPath = Path.Combine(auditRoot, "audit-run-1.csv");
        await File.WriteAllTextAsync(auditPath,
            "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp\r\n"
            + $"{_tempDir},{Path.Combine(_tempDir, "old.zip")},{Path.Combine(_tempDir, "new.zip")},MOVE,Game,abcdef0123456789,dedup,2026-05-03T10:00:00Z\r\n",
            Encoding.UTF8);
        var auditKeyPath = Path.Combine(_tempDir, "audit.key");
        new AuditSigningService(new FileSystemAdapter(), keyFilePath: auditKeyPath)
            .WriteMetadataSidecar(auditPath, rowCount: 1);

        using var factory = CreateFactory([], auditKeyPath);
        using var client = CreateAuthClient(factory);
        var encodedRoot = Uri.EscapeDataString(auditRoot);

        var runsResponse = await client.GetAsync($"/v1-experimental/audit/runs?auditRoot={encodedRoot}");
        Assert.Equal(HttpStatusCode.OK, runsResponse.StatusCode);
        using var runsDoc = JsonDocument.Parse(await runsResponse.Content.ReadAsStringAsync());
        Assert.Equal(1, runsDoc.RootElement.GetProperty("total").GetInt32());
        var runId = runsDoc.RootElement.GetProperty("runs")[0].GetProperty("runId").GetString();
        Assert.Equal("run-1", runId);

        var rowsResponse = await client.GetAsync($"/v1-experimental/audit/runs/{runId}/rows?auditRoot={encodedRoot}");
        Assert.Equal(HttpStatusCode.OK, rowsResponse.StatusCode);
        using var rowsDoc = JsonDocument.Parse(await rowsResponse.Content.ReadAsStringAsync());
        Assert.Equal("MOVE", rowsDoc.RootElement.GetProperty("rows").GetProperty("rows")[0].GetProperty("action").GetString());

        var sidecarResponse = await client.GetAsync($"/v1-experimental/audit/runs/{runId}/sidecar?auditRoot={encodedRoot}");
        Assert.Equal(HttpStatusCode.OK, sidecarResponse.StatusCode);
        using var sidecarDoc = JsonDocument.Parse(await sidecarResponse.Content.ReadAsStringAsync());
        Assert.True(sidecarDoc.RootElement.GetProperty("hasSidecar").GetBoolean());

        var verificationResponse = await client.GetAsync($"/v1-experimental/audit/runs/{runId}/verification?auditRoot={encodedRoot}");
        Assert.Equal(HttpStatusCode.OK, verificationResponse.StatusCode);
        using var verificationDoc = JsonDocument.Parse(await verificationResponse.Content.ReadAsStringAsync());
        Assert.Equal("valid", verificationDoc.RootElement.GetProperty("status").GetString());
        Assert.Equal(1, verificationDoc.RootElement.GetProperty("actualRowCount").GetInt32());
    }

    [Fact]
    public async Task CollectionsRootHealthEndpoint_ReturnsScopedReport()
    {
        var root = Path.Combine(_tempDir, "health-roms");
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

        var response = await client.GetAsync($"/v1-experimental/collections/{Uri.EscapeDataString(root)}/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, doc.RootElement.GetProperty("breakdown").GetProperty("totalFiles").GetInt32());
    }

    [Fact]
    public async Task OpenApi_DeclaresPolicyHealthAndProvenance_AndNoPluginMarketplaceSurface()
    {
        using var spec = JsonDocument.Parse(await OpenApiTestHelper.FetchOpenApiJsonAsync());
        var paths = spec.RootElement.GetProperty("paths");
        var schemas = spec.RootElement.GetProperty("components").GetProperty("schemas");

        Assert.Equal("experimental", spec.RootElement.GetProperty("x-stability").GetString());
        Assert.True(paths.TryGetProperty("/policies/validate", out var policyPath), "Missing policy validation path.");
        Assert.True(policyPath.TryGetProperty("post", out var policyPost), "Policy validation must declare POST.");
        Assert.True(policyPost.TryGetProperty("requestBody", out _), "Policy validation must declare request body.");
        Assert.Equal("experimental", policyPost.GetProperty("x-stability").GetString());
        Assert.True(paths.TryGetProperty("/v1-experimental/policies/validate", out _), "Missing experimental policy validation path.");
        Assert.True(paths.TryGetProperty("/v1-experimental/policies/sign", out _), "Missing experimental policy signing path.");
        Assert.True(paths.TryGetProperty("/health/collection", out _), "Missing collection health endpoint.");
        Assert.True(paths.TryGetProperty("/v1-experimental/health/collection", out _), "Missing experimental collection health endpoint.");
        Assert.True(paths.TryGetProperty("/v1-experimental/collections/{root}/health", out _), "Missing experimental scoped collection health endpoint.");
        Assert.True(paths.TryGetProperty("/roms/{fingerprint}/provenance", out _), "Missing provenance endpoint.");
        Assert.True(paths.TryGetProperty("/v1-experimental/roms/{fingerprint}/provenance", out _), "Missing experimental provenance endpoint.");
        Assert.True(paths.TryGetProperty("/v1-experimental/audit/runs", out _), "Missing experimental audit run listing endpoint.");
        Assert.True(paths.TryGetProperty("/v1-experimental/audit/runs/{id}/rows", out _), "Missing experimental audit row endpoint.");
        Assert.True(paths.TryGetProperty("/v1-experimental/audit/runs/{id}/sidecar", out _), "Missing experimental audit sidecar endpoint.");
        Assert.True(paths.TryGetProperty("/v1-experimental/audit/runs/{id}/verification", out _), "Missing experimental audit verification endpoint.");
        Assert.True(schemas.TryGetProperty("PolicyValidationRequest", out _), "Missing PolicyValidationRequest schema.");
        Assert.True(schemas.TryGetProperty("PolicyValidationReport", out _), "Missing PolicyValidationReport schema.");

        foreach (var path in paths.EnumerateObject())
        {
            Assert.DoesNotContain("plugin", path.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("marketplace", path.Name, StringComparison.OrdinalIgnoreCase);
        }
    }

    private WebApplicationFactory<Program> CreateFactory(
        IReadOnlyList<CollectionIndexEntry> entries,
        string? auditSigningKeyPath = null)
        => ApiTestFactory.Create(
            new Dictionary<string, string?>
            {
                ["ApiKey"] = ApiKey,
                ["RateLimitRequests"] = "120",
                ["RateLimitWindowSeconds"] = "60"
            },
            collectionIndex: new FakeCollectionIndex(entries),
            auditSigningKeyPath: auditSigningKeyPath);

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
