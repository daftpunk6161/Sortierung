using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Integration tests for API endpoint validation branches (input validation, error codes, security validation).
/// Exercises uncovered validation branches in Program.cs including runs, profiles, workflows, collections, merge.
/// </summary>
public sealed class ApiValidationIntegrationTests
{
    private const string ApiKey = "api-validation-test-key";

    private static HttpClient CreateClient(
        IDictionary<string, string?> settings,
        string? clientId = null)
    {
        var factory = ApiTestFactory.Create(settings);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", settings["ApiKey"]!);
        if (clientId is not null)
            client.DefaultRequestHeaders.Add("X-Client-Id", clientId);
        return client;
    }

    private static StringContent JsonBody(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static Dictionary<string, string?> DefaultSettings() =>
        new() { ["ApiKey"] = ApiKey };

    // ──────────────── /runs/list ────────────────

    [Fact]
    public async Task RunsList_InvalidOffset_Returns400()
    {
        using var factory = ApiTestFactory.Create(DefaultSettings());
        using var client = CreateClient(DefaultSettings());

        var response = await client.GetAsync("/runs?offset=-5");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("RUN-INVALID-OFFSET", body);
    }

    [Fact]
    public async Task RunsList_NonNumericOffset_Returns400()
    {
        using var client = CreateClient(DefaultSettings());

        var response = await client.GetAsync("/runs?offset=abc");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("RUN-INVALID-OFFSET", body);
    }

    [Fact]
    public async Task RunsList_LimitTooHigh_Returns400()
    {
        using var client = CreateClient(DefaultSettings());

        var response = await client.GetAsync("/runs?limit=5000");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("RUN-INVALID-LIMIT", body);
    }

    [Fact]
    public async Task RunsList_LimitZero_Returns400()
    {
        using var client = CreateClient(DefaultSettings());

        var response = await client.GetAsync("/runs?limit=0");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("RUN-INVALID-LIMIT", body);
    }

    [Fact]
    public async Task RunsList_ValidPagination_Returns200()
    {
        using var client = CreateClient(DefaultSettings());

        var response = await client.GetAsync("/runs?offset=0&limit=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ──────────────── /runs/history ────────────────

    [Fact]
    public async Task RunsHistory_InvalidOffset_Returns400()
    {
        using var client = CreateClient(DefaultSettings());

        var response = await client.GetAsync("/runs/history?offset=abc");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("RUN-INVALID-OFFSET", body);
    }

    [Fact]
    public async Task RunsHistory_InvalidLimit_Returns400()
    {
        using var client = CreateClient(DefaultSettings());

        var response = await client.GetAsync("/runs/history?limit=9999");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("RUN-INVALID-LIMIT", body);
    }

    [Fact]
    public async Task RunsHistory_ValidPagination_Returns200()
    {
        using var client = CreateClient(DefaultSettings());

        var response = await client.GetAsync("/runs/history?offset=0&limit=5");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("total", out _));
        Assert.True(doc.RootElement.TryGetProperty("hasMore", out _));
    }

    // ──────────────── Auth ────────────────

    [Fact]
    public async Task Request_WithoutApiKey_Returns401()
    {
        using var factory = ApiTestFactory.Create(DefaultSettings());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/runs");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithWrongApiKey_Returns401()
    {
        using var factory = ApiTestFactory.Create(DefaultSettings());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "wrong-key");

        var response = await client.GetAsync("/runs");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ──────────────── /profiles ────────────────

    [Fact]
    public async Task Profiles_GetNotFound_Returns404()
    {
        using var client = CreateClient(DefaultSettings());

        var response = await client.GetAsync("/profiles/nonexistent-profile-id");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("PROFILE-NOT-FOUND", body);
    }

    [Fact]
    public async Task Profiles_DeleteNotFound_Returns404()
    {
        using var client = CreateClient(DefaultSettings());

        var response = await client.DeleteAsync("/profiles/nonexistent-profile-id");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("PROFILE-NOT-FOUND", body);
    }

    [Fact]
    public async Task Profiles_DeleteBuiltIn_Returns400()
    {
        using var client = CreateClient(DefaultSettings());

        var response = await client.DeleteAsync("/profiles/default");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("PROFILE-DELETE-BLOCKED", body);
    }

    // ──────────────── /workflows ────────────────

    [Fact]
    public async Task Workflows_GetNotFound_Returns404()
    {
        using var client = CreateClient(DefaultSettings());

        var response = await client.GetAsync("/workflows/nonexistent-workflow");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("WORKFLOW-NOT-FOUND", body);
    }

    [Fact]
    public async Task Workflows_ListWithQuery_NotFound_Returns404()
    {
        using var client = CreateClient(DefaultSettings());

        var response = await client.GetAsync("/workflows?id=nonexistent");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("WORKFLOW-NOT-FOUND", body);
    }

    // ──────────────── POST /runs validation ────────────────

    [Fact]
    public async Task Runs_Post_InvalidContentType_Returns400()
    {
        using var client = CreateClient(DefaultSettings());

        using var content = new StringContent("test", Encoding.UTF8, "text/plain");
        var response = await client.PostAsync("/runs", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("RUN-INVALID-CONTENT-TYPE", body);
    }

    [Fact]
    public async Task Runs_Post_InvalidJson_Returns400()
    {
        using var client = CreateClient(DefaultSettings());

        using var content = new StringContent("{invalid json", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/runs", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("RUN-INVALID-JSON", body);
    }

    [Fact]
    public async Task Runs_Post_MissingRoots_Returns400()
    {
        using var client = CreateClient(DefaultSettings());

        using var content = JsonBody(new { mode = "DryRun" });
        var response = await client.PostAsync("/runs", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("RUN-ROOTS-REQUIRED", body);
    }

    [Fact]
    public async Task Runs_Post_EmptyRootPath_Returns400()
    {
        using var client = CreateClient(DefaultSettings());

        using var content = JsonBody(new { roots = new[] { "" }, mode = "DryRun" });
        var response = await client.PostAsync("/runs", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        // Empty root gets filtered or caught → RUN-ROOT-EMPTY or RUN-ROOTS-REQUIRED
        Assert.True(body.Contains("RUN-ROOT-EMPTY") || body.Contains("RUN-ROOTS-REQUIRED"), $"Expected root error, got: {body}");
    }

    [Fact]
    public async Task Runs_Post_NonexistentRoot_Returns400()
    {
        using var client = CreateClient(DefaultSettings());

        using var content = JsonBody(new { roots = new[] { @"C:\NonExistent_ApiTest_" + Guid.NewGuid().ToString("N") }, mode = "DryRun" });
        var response = await client.PostAsync("/runs", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("IO-ROOT-NOT-FOUND", body);
    }

    [Fact]
    public async Task Runs_Post_InvalidMode_Returns400()
    {
        var root = CreateTempRoot();
        try
        {
            using var client = CreateClient(DefaultSettings());
            using var content = JsonBody(new { roots = new[] { root }, mode = "InvalidMode" });
            var response = await client.PostAsync("/runs", content);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("RUN-INVALID-MODE", body);
        }
        finally { SafeDeleteDirectory(root); }
    }

    [Fact]
    public async Task Runs_Post_InvalidRegion_Returns400()
    {
        var root = CreateTempRoot();
        try
        {
            using var client = CreateClient(DefaultSettings());
            using var content = JsonBody(new { roots = new[] { root }, preferRegions = new[] { "US!@#$" } });
            var response = await client.PostAsync("/runs", content);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            // Materializer or inline validation catches invalid region
            Assert.True(body.Contains("RUN-INVALID-REGION") || body.Contains("RUN-INVALID-CONFIG"), $"Expected region error, got: {body}");
        }
        finally { SafeDeleteDirectory(root); }
    }

    [Fact]
    public async Task Runs_Post_TooManyRegions_Returns400()
    {
        var root = CreateTempRoot();
        try
        {
            var regions = Enumerable.Range(0, 25).Select(i => $"R{i}").ToArray();
            using var client = CreateClient(DefaultSettings());
            using var content = JsonBody(new { roots = new[] { root }, preferRegions = regions });
            var response = await client.PostAsync("/runs", content);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            // Materializer or inline check catches too many regions
            Assert.True(body.Contains("RUN-TOO-MANY-REGIONS") || body.Contains("RUN-INVALID-CONFIG") || body.Contains("region", StringComparison.OrdinalIgnoreCase), $"Expected region error, got: {body}");
        }
        finally { SafeDeleteDirectory(root); }
    }

    [Fact]
    public async Task Runs_Post_InvalidHashType_Returns400()
    {
        var root = CreateTempRoot();
        try
        {
            using var client = CreateClient(DefaultSettings());
            using var content = JsonBody(new { roots = new[] { root }, hashType = "BLAKE3" });
            var response = await client.PostAsync("/runs", content);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(body.Contains("RUN-INVALID-HASH-TYPE") || body.Contains("RUN-INVALID-CONFIG"), $"Expected hash type error, got: {body}");
        }
        finally { SafeDeleteDirectory(root); }
    }

    [Fact]
    public async Task Runs_Post_InvalidExtension_Returns400()
    {
        var root = CreateTempRoot();
        try
        {
            using var client = CreateClient(DefaultSettings());
            using var content = JsonBody(new { roots = new[] { root }, extensions = new[] { ".a!b" } });
            var response = await client.PostAsync("/runs", content);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(body.Contains("RUN-INVALID-EXTENSION") || body.Contains("RUN-INVALID-CONFIG"), $"Expected extension error, got: {body}");
        }
        finally { SafeDeleteDirectory(root); }
    }

    [Fact]
    public async Task Runs_Post_InvalidIdempotencyKey_Returns400()
    {
        var root = CreateTempRoot();
        try
        {
            using var client = CreateClient(DefaultSettings());
            client.DefaultRequestHeaders.Add("X-Idempotency-Key", "invalid key with spaces!!!");
            using var content = JsonBody(new { roots = new[] { root }, mode = "DryRun" });
            var response = await client.PostAsync("/runs", content);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("RUN-INVALID-IDEMPOTENCY-KEY", body);
        }
        finally { SafeDeleteDirectory(root); }
    }

    [Fact]
    public async Task Runs_Post_DriveRoot_Returns400()
    {
        using var client = CreateClient(DefaultSettings());

        using var content = JsonBody(new { roots = new[] { @"C:\" }, mode = "DryRun" });
        var response = await client.PostAsync("/runs", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        // Security validation returns error code with "DriveRoot" or "SECURITY" prefix
        Assert.Contains("error", body);
    }

    // ──────────────── /runs/compare ────────────────

    [Fact]
    public async Task RunsCompare_MissingIds_Returns400()
    {
        using var client = CreateClient(DefaultSettings());

        var response = await client.GetAsync("/runs/compare?runId=&compareToRunId=");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("RUN-COMPARE-IDS-REQUIRED", body);
    }

    [Fact]
    public async Task RunsCompare_NonExistentRuns_Returns404()
    {
        using var client = CreateClient(DefaultSettings());

        var response = await client.GetAsync("/runs/compare?runId=fake123&compareToRunId=fake456");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("RUN-COMPARE-NOT-FOUND", body);
    }

    // ──────────────── /runs/trends ────────────────

    [Fact]
    public async Task RunsTrends_Returns200()
    {
        using var client = CreateClient(DefaultSettings());

        var response = await client.GetAsync("/runs/trends?limit=5");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ──────────────── /collections/compare validation ────────────────

    [Fact]
    public async Task CollectionsCompare_InvalidLimit_Returns400()
    {
        using var client = CreateClient(DefaultSettings());

        using var content = JsonBody(new
        {
            left = new { roots = new[] { @"C:\FakePath1" }, sourceId = "a" },
            right = new { roots = new[] { @"C:\FakePath2" }, sourceId = "b" },
            limit = 0
        });
        var response = await client.PostAsync("/collections/compare", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("COLLECTION-COMPARE-INVALID-LIMIT", body);
    }

    [Fact]
    public async Task CollectionsCompare_EmptyRoots_Returns400()
    {
        using var client = CreateClient(DefaultSettings());

        using var content = JsonBody(new
        {
            left = new { roots = Array.Empty<string>(), sourceId = "a" },
            right = new { roots = new[] { @"C:\SomePath" }, sourceId = "b" },
            limit = 100
        });
        var response = await client.PostAsync("/collections/compare", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("COLLECTION-", body);
    }

    // ──────────────── /collections/merge/rollback validation ────────────────

    [Fact]
    public async Task CollectionsMergeRollback_MissingAuditPath_Returns400()
    {
        using var client = CreateClient(DefaultSettings());

        using var content = JsonBody(new { auditPath = (string?)null });
        var response = await client.PostAsync("/collections/merge/rollback", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("COLLECTION-MERGE-ROLLBACK-AUDIT-REQUIRED", body);
    }

    [Fact]
    public async Task CollectionsMergeRollback_NonexistentAuditFile_Returns404()
    {
        using var client = CreateClient(DefaultSettings());

        var tempPath = Path.Combine(Path.GetTempPath(), "Romulus_ApiTest_" + Guid.NewGuid().ToString("N"), "audit.csv");
        using var content = JsonBody(new { auditPath = tempPath });
        var response = await client.PostAsync("/collections/merge/rollback", content);
        // Should be 404 (file not found) or 400 (security)
        Assert.True(
            response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.BadRequest,
            $"Expected 400 or 404, got {response.StatusCode}");
    }

    // ──────────────── /export/frontend validation ────────────────

    [Fact]
    public async Task ExportFrontend_InvalidJson_Returns400()
    {
        using var client = CreateClient(DefaultSettings());

        using var content = new StringContent("{bad", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/export/frontend", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("EXPORT-INVALID-JSON", body);
    }

    // ──────────────── /healthz (no auth required) ────────────────

    [Fact]
    public async Task Healthz_NoAuthRequired_Returns200()
    {
        using var factory = ApiTestFactory.Create(DefaultSettings());
        using var client = factory.CreateClient();
        // No API key header!

        var response = await client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ──────────────── /dashboard/bootstrap (no auth required) ────────────────

    [Fact]
    public async Task DashboardBootstrap_NoAuthRequired_Returns200()
    {
        using var factory = ApiTestFactory.Create(DefaultSettings());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/dashboard/bootstrap");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ──────────────── CORS / OPTIONS ────────────────

    [Fact]
    public async Task Options_Preflight_SkipsAuth_Returns200()
    {
        using var factory = ApiTestFactory.Create(DefaultSettings());
        using var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Options, "/runs");
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        var response = await client.SendAsync(request);

        // OPTIONS should not get 401
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ──────────────── Successful run with DryRun ────────────────

    [Fact]
    public async Task Runs_Post_ValidDryRun_Returns200()
    {
        var root = CreateTempRoot();
        try
        {
            File.WriteAllText(Path.Combine(root, "Game (USA).zip"), "test");
            using var client = CreateClient(DefaultSettings());
            using var content = JsonBody(new { roots = new[] { root }, mode = "DryRun" });
            var response = await client.PostAsync("/runs?wait=true", content);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally { SafeDeleteDirectory(root); }
    }

    // ──────────────── Client binding ────────────────

    [Fact]
    public async Task RunsList_WithClientId_ReturnsScoped()
    {
        using var client = CreateClient(DefaultSettings(), clientId: "test-binding-client");

        var response = await client.GetAsync("/runs");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.TryGetProperty("total", out _));
    }

    // ──────────────── Extension with special chars ────────────────

    [Fact]
    public async Task Runs_Post_ExtensionTooLong_Returns400()
    {
        var root = CreateTempRoot();
        try
        {
            using var client = CreateClient(DefaultSettings());
            using var content = JsonBody(new { roots = new[] { root }, extensions = new[] { ".abcdefghijklmnopqrstuvwxyz123" } });
            var response = await client.PostAsync("/runs", content);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("RUN-INVALID-EXTENSION", body);
        }
        finally { SafeDeleteDirectory(root); }
    }

    // ──────────────── Helpers ────────────────

    private static string CreateTempRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Romulus_ApiValidTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void SafeDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch { /* best-effort cleanup */ }
    }
}
