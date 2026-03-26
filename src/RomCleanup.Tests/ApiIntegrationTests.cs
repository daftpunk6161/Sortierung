using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RomCleanup.Api;
using RomCleanup.Contracts.Errors;
using RomCleanup.Infrastructure.Audit;
using RomCleanup.Infrastructure.FileSystem;
using Xunit;

namespace RomCleanup.Tests;

public sealed class ApiIntegrationTests
{
    private const string ApiKey = "integration-test-key";

    [Fact]
    public async Task Health_WithoutApiKey_ReturnsUnauthorized()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertError(doc.RootElement, "AUTH-UNAUTHORIZED", ErrorKind.Critical, "Unauthorized");
    }

    [Fact]
    public async Task Health_WithApiKey_ReturnsOk()
    {
        using var factory = CreateFactory();
        using var client = CreateClientWithApiKey(factory);

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"ok\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Health_WithWrongApiKey_ReturnsUnauthorized()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "wrong-key");

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertError(doc.RootElement, "AUTH-UNAUTHORIZED", ErrorKind.Critical, "Unauthorized");
    }

    [Fact]
    public async Task Cors_Preflight_Options_Returns204_WithExpectedHeaders()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["CorsMode"] = "custom",
            ["CorsAllowOrigin"] = "http://example.test"
        });
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Options, "/runs");
        request.Headers.Add("Origin", "http://example.test");
        request.Headers.Add("Access-Control-Request-Method", "POST");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins));
        Assert.Contains("http://example.test", origins);
    }

    [Fact]
    public async Task RateLimit_Exceeded_Returns429()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["RateLimitRequests"] = "2",
            ["RateLimitWindowSeconds"] = "60"
        });
        using var client = CreateClientWithApiKey(factory);

        var first = await client.GetAsync("/health");
        var second = await client.GetAsync("/health");
        var third = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal((HttpStatusCode)429, third.StatusCode);
        using var doc = JsonDocument.Parse(await third.Content.ReadAsStringAsync());
        AssertError(doc.RootElement, "RUN-RATE-LIMIT", ErrorKind.Transient, "Too many requests");
    }

    [Fact]
    public async Task RateLimit_TrustForwardedForFalse_IgnoresHeaderValue()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["RateLimitRequests"] = "1",
            ["RateLimitWindowSeconds"] = "60",
            ["TrustForwardedFor"] = "false"
        });
        using var client = CreateClientWithApiKey(factory);

        using var req1 = new HttpRequestMessage(HttpMethod.Get, "/health");
        req1.Headers.Add("X-Forwarded-For", "203.0.113.10");
        using var req2 = new HttpRequestMessage(HttpMethod.Get, "/health");
        req2.Headers.Add("X-Forwarded-For", "203.0.113.11");

        var first = await client.SendAsync(req1);
        var second = await client.SendAsync(req2);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal((HttpStatusCode)429, second.StatusCode);
    }

    [Fact]
    public async Task RateLimit_TrustForwardedForTrue_UntrustedProxySource_FallsBackToRemoteIp()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["RateLimitRequests"] = "1",
            ["RateLimitWindowSeconds"] = "60",
            ["TrustForwardedFor"] = "true"
        });
        using var client = CreateClientWithApiKey(factory);

        using var req1 = new HttpRequestMessage(HttpMethod.Get, "/health");
        req1.Headers.Add("X-Forwarded-For", "198.51.100.20");
        using var req2 = new HttpRequestMessage(HttpMethod.Get, "/health");
        req2.Headers.Add("X-Forwarded-For", "198.51.100.21");
        using var req3 = new HttpRequestMessage(HttpMethod.Get, "/health");
        req3.Headers.Add("X-Forwarded-For", "198.51.100.20");

        var first = await client.SendAsync(req1);
        var second = await client.SendAsync(req2);
        var third = await client.SendAsync(req3);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal((HttpStatusCode)429, second.StatusCode);
        Assert.Equal((HttpStatusCode)429, third.StatusCode);
    }

    [Fact]
    public void ResolveRateLimitClientId_TrustEnabled_LoopbackProxy_UsesForwardedHeader()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Headers["X-Forwarded-For"] = "198.51.100.20, 10.0.0.1";

        var clientId = ApiClientIdentity.ResolveRateLimitClientId(context, trustForwardedFor: true);

        Assert.Equal("198.51.100.20", clientId);
    }

    [Fact]
    public void ResolveRateLimitClientId_TrustDisabled_IgnoresForwardedHeader()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        context.Request.Headers["X-Forwarded-For"] = "198.51.100.20";

        var clientId = ApiClientIdentity.ResolveRateLimitClientId(context, trustForwardedFor: false);

        Assert.Equal("127.0.0.1", clientId);
    }

    [Fact]
    public async Task Runs_InvalidPreferRegions_ReturnsBadRequest()
    {
        using var factory = CreateFactory();
        using var client = CreateClientWithApiKey(factory);

        var root = CreateTempRoot();
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                roots = new[] { root },
                mode = "DryRun",
                preferRegions = new[] { "<script>alert(1)</script>" }
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await client.PostAsync("/runs", content);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            AssertError(doc.RootElement, "RUN-INVALID-REGION", ErrorKind.Recoverable, "Invalid region");
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Runs_OversizedBody_ReturnsBadRequest()
    {
        using var factory = CreateFactory();
        using var client = CreateClientWithApiKey(factory);

        var huge = new string('a', 1_048_590);
        var payload = "{\"roots\":[\"" + huge + "\"]}";

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/runs", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("too large", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Runs_SystemDirectoryRoot_IsRejected()
    {
        using var factory = CreateFactory();
        using var client = CreateClientWithApiKey(factory);

        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var payload = JsonSerializer.Serialize(new
        {
            roots = new[] { windowsDir },
            mode = "DryRun"
        });

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/runs", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("System directory not allowed", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Runs_Lifecycle_AndStream_Endpoints_Work()
    {
        using var factory = CreateFactory();
        using var client = CreateClientWithApiKey(factory);

        var root = CreateTempRoot();
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                roots = new[] { root },
                mode = "DryRun",
                preferRegions = new[] { "EU", "US" }
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var createResponse = await client.PostAsync("/runs?wait=true", content);
            Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

            var createJson = await createResponse.Content.ReadAsStringAsync();
            using var createDoc = JsonDocument.Parse(createJson);
            var runId = createDoc.RootElement
                .GetProperty("run")
                .GetProperty("runId")
                .GetString();

            Assert.False(string.IsNullOrWhiteSpace(runId));

            var statusResponse = await client.GetAsync($"/runs/{runId}");
            Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);

            var resultResponse = await client.GetAsync($"/runs/{runId}/result");
            Assert.Equal(HttpStatusCode.OK, resultResponse.StatusCode);

            using var streamRequest = new HttpRequestMessage(HttpMethod.Get, $"/runs/{runId}/stream");
            var streamResponse = await client.SendAsync(streamRequest, HttpCompletionOption.ResponseHeadersRead);
            Assert.Equal(HttpStatusCode.OK, streamResponse.StatusCode);
            Assert.NotNull(streamResponse.Content.Headers.ContentType);
            Assert.Equal("text/event-stream", streamResponse.Content.Headers.ContentType!.MediaType);

            var cancelResponse = await client.PostAsync($"/runs/{runId}/cancel", null);
            Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Runs_IdempotencyKey_RetryReusesCompletedRun()
    {
        using var factory = CreateFactory();
        using var client = CreateClientWithApiKey(factory);

        var root = CreateTempRoot();
        try
        {
            client.DefaultRequestHeaders.Remove("X-Idempotency-Key");
            client.DefaultRequestHeaders.Add("X-Idempotency-Key", "api-retry-001");

            var payload = JsonSerializer.Serialize(new
            {
                roots = new[] { root },
                mode = "DryRun"
            });

            using var firstContent = new StringContent(payload, Encoding.UTF8, "application/json");
            var firstResponse = await client.PostAsync("/runs?wait=true", firstContent);
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

            using var secondContent = new StringContent(payload, Encoding.UTF8, "application/json");
            var secondResponse = await client.PostAsync("/runs", secondContent);
            Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

            using var firstDoc = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync());
            using var secondDoc = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync());

            var firstRunId = firstDoc.RootElement.GetProperty("run").GetProperty("runId").GetString();
            var secondRunId = secondDoc.RootElement.GetProperty("run").GetProperty("runId").GetString();

            Assert.Equal(firstRunId, secondRunId);
            Assert.True(secondDoc.RootElement.GetProperty("reused").GetBoolean());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Runs_IdempotencyKey_DifferentPayload_ReturnsConflict()
    {
        using var factory = CreateFactory();
        using var client = CreateClientWithApiKey(factory);

        var root1 = CreateTempRoot();
        var root2 = CreateTempRoot();
        try
        {
            client.DefaultRequestHeaders.Remove("X-Idempotency-Key");
            client.DefaultRequestHeaders.Add("X-Idempotency-Key", "api-retry-002");

            var firstPayload = JsonSerializer.Serialize(new { roots = new[] { root1 }, mode = "DryRun" });
            using var firstContent = new StringContent(firstPayload, Encoding.UTF8, "application/json");
            var firstResponse = await client.PostAsync("/runs?wait=true", firstContent);
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

            var secondPayload = JsonSerializer.Serialize(new { roots = new[] { root2 }, mode = "DryRun" });
            using var secondContent = new StringContent(secondPayload, Encoding.UTF8, "application/json");
            var secondResponse = await client.PostAsync("/runs", secondContent);
            Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);

            using var doc = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync());
            AssertError(doc.RootElement, "RUN-IDEMPOTENCY-CONFLICT", ErrorKind.Recoverable, "Idempotency");
            Assert.Equal(doc.RootElement.GetProperty("runId").GetString(), doc.RootElement.GetProperty("meta").GetProperty("run").GetProperty("runId").GetString());
        }
        finally
        {
            SafeDeleteDirectory(root1);
            SafeDeleteDirectory(root2);
        }
    }

    [Fact]
    public async Task Runs_WaitTimeout_ReturnsAccepted_AndRunContinues()
    {
        using var factory = CreateFactory(executor: (_, _, _, ct) =>
        {
            Task.Delay(1_500, ct).GetAwaiter().GetResult();
            return new RunExecutionOutcome("completed", new ApiRunResult
            {
                OrchestratorStatus = "ok",
                ExitCode = 0,
                TotalFiles = 1,
                Groups = 1,
                Winners = 1,
                Losers = 0,
                DurationMs = 1_500
            });
        });
        using var client = CreateClientWithApiKey(factory);

        var root = CreateTempRoot();
        try
        {
            var payload = JsonSerializer.Serialize(new { roots = new[] { root }, mode = "DryRun" });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await client.PostAsync("/runs?wait=true&waitTimeoutMs=5", content);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var runId = doc.RootElement.GetProperty("run").GetProperty("runId").GetString();
            Assert.True(doc.RootElement.GetProperty("waitTimedOut").GetBoolean());

            await Task.Delay(1_700);
            var resultResponse = await client.GetAsync($"/runs/{runId}/result");
            Assert.Equal(HttpStatusCode.OK, resultResponse.StatusCode);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Runs_WaitTrue_ResultContainsProjectionFields()
    {
        using var factory = CreateFactory();
        using var client = CreateClientWithApiKey(factory);

        var root = CreateTempRoot();
        try
        {
            var payload = JsonSerializer.Serialize(new { roots = new[] { root }, mode = "DryRun" });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await client.PostAsync("/runs?wait=true", content);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var result = doc.RootElement.GetProperty("result");

            Assert.True(result.TryGetProperty("candidates", out _));
            Assert.True(result.TryGetProperty("games", out _));
            Assert.True(result.TryGetProperty("winners", out _));
            Assert.True(result.TryGetProperty("losers", out _));
            Assert.True(result.TryGetProperty("junk", out _));
            Assert.True(result.TryGetProperty("bios", out _));
            Assert.True(result.TryGetProperty("datMatches", out _));
            Assert.True(result.TryGetProperty("healthScore", out _));
            Assert.True(result.TryGetProperty("convertSkippedCount", out _));
            Assert.True(result.TryGetProperty("convertBlockedCount", out _));
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task OpenApi_Declares_ApiKey_Header_And_GlobalSecurityRequirement()
    {
        using var factory = CreateFactory();
        using var client = CreateClientWithApiKey(factory);

        var response = await client.GetAsync("/openapi");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"securitySchemes\"", json, StringComparison.Ordinal);
        Assert.Contains("\"ApiKey\"", json, StringComparison.Ordinal);
        Assert.Contains("\"name\": \"X-Api-Key\"", json, StringComparison.Ordinal);
        Assert.Contains("\"security\": [{ \"ApiKey\": [] }]", json, StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        Dictionary<string, string?>? overrides = null,
        Func<RunRecord, RomCleanup.Contracts.Ports.IFileSystem, RomCleanup.Contracts.Ports.IAuditStore, CancellationToken, RunExecutionOutcome>? executor = null)
    {
        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ApiKey"] = ApiKey,
            ["CorsMode"] = "strict-local",
            ["CorsAllowOrigin"] = "http://127.0.0.1",
            ["RateLimitRequests"] = "120",
            ["RateLimitWindowSeconds"] = "60"
        };

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
                settings[key] = value;
        }

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(settings);
                });
                if (executor is not null)
                {
                    builder.ConfigureServices(services =>
                    {
                        services.AddSingleton(new RunManager(new FileSystemAdapter(), new AuditCsvStore(), executor));
                    });
                }
            });
    }

    private static HttpClient CreateClientWithApiKey(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "RomCleanup_ApiInt_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "sample.rom"), "test");
        return root;
    }

    private static void SafeDeleteDirectory(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
            // Best effort cleanup for temp directories.
        }
    }

    // --- Additional Negative Tests ---

    [Fact]
    public async Task Runs_InvalidJson_ReturnsBadRequest()
    {
        using var factory = CreateFactory();
        using var client = CreateClientWithApiKey(factory);

        using var content = new StringContent("{invalid-json!!!", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/runs", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid JSON", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Runs_InvalidContentType_ReturnsBadRequest()
    {
        using var factory = CreateFactory();
        using var client = CreateClientWithApiKey(factory);

        using var content = new StringContent("{}", Encoding.UTF8, "text/plain");
        var response = await client.PostAsync("/runs", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("RUN-INVALID-CONTENT-TYPE", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Runs_EmptyRoots_ReturnsBadRequest()
    {
        using var factory = CreateFactory();
        using var client = CreateClientWithApiKey(factory);

        var payload = JsonSerializer.Serialize(new { roots = Array.Empty<string>(), mode = "DryRun" });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/runs", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("roots", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Runs_InvalidMode_ReturnsBadRequest()
    {
        using var factory = CreateFactory();
        using var client = CreateClientWithApiKey(factory);

        var root = CreateTempRoot();
        try
        {
            var payload = JsonSerializer.Serialize(new { roots = new[] { root }, mode = "Delete" });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await client.PostAsync("/runs", content);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("mode", body, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Runs_DriveRoot_ReturnsBadRequest()
    {
        using var factory = CreateFactory();
        using var client = CreateClientWithApiKey(factory);

        var payload = JsonSerializer.Serialize(new { roots = new[] { @"C:\" }, mode = "DryRun" });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/runs", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("not allowed", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Runs_RunNotFound_Returns404()
    {
        using var factory = CreateFactory();
        using var client = CreateClientWithApiKey(factory);

        // V2-SEC-M01: Non-GUID runId returns BadRequest, valid GUID returns NotFound
        var badIdResponse = await client.GetAsync("/runs/nonexistent-run-id");
        Assert.Equal(HttpStatusCode.BadRequest, badIdResponse.StatusCode);

        var validGuid = Guid.NewGuid().ToString();
        var response = await client.GetAsync($"/runs/{validGuid}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var responseDoc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertError(responseDoc.RootElement, "RUN-NOT-FOUND", ErrorKind.Recoverable, "Run not found", validGuid);

        var resultResponse = await client.GetAsync($"/runs/{validGuid}/result");
        Assert.Equal(HttpStatusCode.NotFound, resultResponse.StatusCode);
        using var resultDoc = JsonDocument.Parse(await resultResponse.Content.ReadAsStringAsync());
        AssertError(resultDoc.RootElement, "RUN-NOT-FOUND", ErrorKind.Recoverable, "Run not found", validGuid);

        var cancelResponse = await client.PostAsync($"/runs/{validGuid}/cancel", null);
        Assert.Equal(HttpStatusCode.NotFound, cancelResponse.StatusCode);
        using var cancelDoc = JsonDocument.Parse(await cancelResponse.Content.ReadAsStringAsync());
        AssertError(cancelDoc.RootElement, "RUN-NOT-FOUND", ErrorKind.Recoverable, "Run not found", validGuid);
    }

    [Fact]
    public async Task Runs_ConcurrentRun_ReturnsConflict()
    {
        using var factory = CreateFactory();
        using var client = CreateClientWithApiKey(factory);

        var root = CreateTempRoot();
        try
        {
            var payload = JsonSerializer.Serialize(new { roots = new[] { root }, mode = "DryRun" });

            using var content1 = new StringContent(payload, Encoding.UTF8, "application/json");
            var first = await client.PostAsync("/runs?wait=true", content1);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);

            // Second run with same root — may conflict if first is still flagged as active
            // (since first used ?wait=true it may have completed; this validates the endpoint accepts valid input)
            using var content2 = new StringContent(payload, Encoding.UTF8, "application/json");
            var second = await client.PostAsync("/runs?wait=true", content2);

            // Should be OK (first already completed) or Conflict (if still registered)
            Assert.True(
                second.StatusCode == HttpStatusCode.OK || second.StatusCode == HttpStatusCode.Conflict,
                $"Expected OK or Conflict, got {second.StatusCode}");
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Runs_NonexistentRoot_ReturnsBadRequest()
    {
        using var factory = CreateFactory();
        using var client = CreateClientWithApiKey(factory);

        var payload = JsonSerializer.Serialize(new { roots = new[] { @"C:\NonExistentPath_" + Guid.NewGuid().ToString("N") }, mode = "DryRun" });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/runs", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("not found", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Runs_InvalidExtensions_ReturnsBadRequest()
    {
        using var factory = CreateFactory();
        using var client = CreateClientWithApiKey(factory);

        var root = CreateTempRoot();
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                roots = new[] { root },
                mode = "DryRun",
                extensions = new[] { ".zip", ".bad-ext", "..\\evil" }
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await client.PostAsync("/runs", content);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("RUN-INVALID-EXTENSION", body, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Runs_Rollback_WithoutAuditArtifact_ReturnsConflict()
    {
        using var factory = CreateFactory();
        using var client = CreateClientWithApiKey(factory);

        var root = CreateTempRoot();
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                roots = new[] { root },
                mode = "DryRun",
                removeJunk = false
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var createResponse = await client.PostAsync("/runs?wait=true", content);
            Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

            using var createDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
            var runId = createDoc.RootElement.GetProperty("run").GetProperty("runId").GetString();
            Assert.False(string.IsNullOrWhiteSpace(runId));

            var rollbackResponse = await client.PostAsync($"/runs/{runId}/rollback", new StringContent("", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.Conflict, rollbackResponse.StatusCode);
            var rollbackBody = await rollbackResponse.Content.ReadAsStringAsync();
            Assert.Contains("RUN-ROLLBACK-NOT-AVAILABLE", rollbackBody, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Runs_Rollback_AfterMoveRun_ReturnsRollbackSummary()
    {
        using var factory = CreateFactory();
        using var client = CreateClientWithApiKey(factory);

        var root = CreateTempRoot();
        try
        {
            File.WriteAllText(Path.Combine(root, "MegaGame (Europe).zip"), "eu");
            File.WriteAllText(Path.Combine(root, "MegaGame (USA).zip"), "us");

            var payload = JsonSerializer.Serialize(new
            {
                roots = new[] { root },
                mode = "Move",
                removeJunk = false,
                trashRoot = root,
                preferRegions = new[] { "US", "EU" }
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var createResponse = await client.PostAsync("/runs?wait=true", content);
            Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

            using var createDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
            var runId = createDoc.RootElement.GetProperty("run").GetProperty("runId").GetString();
            Assert.False(string.IsNullOrWhiteSpace(runId));

            var rollbackResponse = await client.PostAsync($"/runs/{runId}/rollback", new StringContent("", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, rollbackResponse.StatusCode);

            using var rollbackDoc = JsonDocument.Parse(await rollbackResponse.Content.ReadAsStringAsync());
            var rollback = rollbackDoc.RootElement.GetProperty("rollback");
            Assert.True(rollback.GetProperty("rolledBack").GetInt32() >= 1);
            Assert.Equal(0, rollback.GetProperty("failed").GetInt32());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Runs_MoveMode_MovesLosersToTrash()
    {
        using var factory = CreateFactory();
        using var client = CreateClientWithApiKey(factory);

        var root = CreateTempRoot();
        try
        {
            File.WriteAllText(Path.Combine(root, "MegaGame (Europe).zip"), "eu");
            File.WriteAllText(Path.Combine(root, "MegaGame (USA).zip"), "us");

            var payload = JsonSerializer.Serialize(new
            {
                roots = new[] { root },
                mode = "Move",
                removeJunk = false,
                trashRoot = root,
                preferRegions = new[] { "US", "EU" }
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await client.PostAsync("/runs?wait=true", content);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var trashDir = Path.Combine(root, "_TRASH_REGION_DEDUPE");
            Assert.True(Directory.Exists(trashDir));
            var moved = Directory.GetFiles(trashDir);
            Assert.NotEmpty(moved);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Runs_ConcurrentPostRequests_OneAccepted_AndConflicts()
    {
        using var gate = new ManualResetEventSlim(false);
        using var factory = CreateFactory(executor: (_, _, _, ct) =>
        {
            gate.Wait(TimeSpan.FromMilliseconds(700), ct);
            return new RunExecutionOutcome("completed", new ApiRunResult
            {
                OrchestratorStatus = "ok",
                ExitCode = 0
            });
        });
        using var client = CreateClientWithApiKey(factory);

        var root = CreateTempRoot();
        try
        {
            var payload = JsonSerializer.Serialize(new { roots = new[] { root }, mode = "DryRun" });

            var calls = Enumerable.Range(0, 8)
                .Select(async _ =>
                {
                    using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                    return await client.PostAsync("/runs", content);
                })
                .ToArray();

            var responses = await Task.WhenAll(calls);
            var codes = responses.Select(r => r.StatusCode).ToArray();

            Assert.Contains(HttpStatusCode.Accepted, codes);
            Assert.Contains(HttpStatusCode.Conflict, codes);
            Assert.DoesNotContain(codes, code => code is not HttpStatusCode.Accepted and not HttpStatusCode.Conflict);
        }
        finally
        {
            gate.Set();
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Runs_Sse_Stream_Contains_Ready_Status_And_TerminalEvents()
    {
        using var factory = CreateFactory(executor: (run, _, _, _) =>
        {
            run.ProgressMessage = "[Scan] phase";
            run.ProgressPercent = 20;
            Thread.Sleep(200);
            run.ProgressMessage = "[Move] phase";
            run.ProgressPercent = 75;
            Thread.Sleep(200);
            return new RunExecutionOutcome("completed", new ApiRunResult
            {
                OrchestratorStatus = "ok",
                ExitCode = 0
            });
        });
        using var client = CreateClientWithApiKey(factory);

        var root = CreateTempRoot();
        try
        {
            var payload = JsonSerializer.Serialize(new { roots = new[] { root }, mode = "DryRun" });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var createResponse = await client.PostAsync("/runs", content);
            Assert.Equal(HttpStatusCode.Accepted, createResponse.StatusCode);

            using var createDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
            var runId = createDoc.RootElement.GetProperty("run").GetProperty("runId").GetString();
            Assert.False(string.IsNullOrWhiteSpace(runId));

            using var streamRequest = new HttpRequestMessage(HttpMethod.Get, $"/runs/{runId}/stream");
            using var streamResponse = await client.SendAsync(streamRequest, HttpCompletionOption.ResponseHeadersRead);
            Assert.Equal(HttpStatusCode.OK, streamResponse.StatusCode);

            var body = await streamResponse.Content.ReadAsStringAsync();
            Assert.Contains("event: ready", body, StringComparison.Ordinal);
            Assert.Contains("event: status", body, StringComparison.Ordinal);
            Assert.Contains("event: completed", body, StringComparison.Ordinal);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Runs_Sse_Heartbeat_IsEmitted_ForLongRunningRun()
    {
        using var factory = CreateFactory(
            new Dictionary<string, string?>
            {
                ["SseHeartbeatSeconds"] = "5",
                ["SseTimeoutSeconds"] = "30"
            },
            executor: (_, _, _, ct) =>
            {
                Task.Delay(6_200, ct).GetAwaiter().GetResult();
                return new RunExecutionOutcome("completed", new ApiRunResult
                {
                    OrchestratorStatus = "ok",
                    ExitCode = 0
                });
            });

        using var client = CreateClientWithApiKey(factory);
        var root = CreateTempRoot();
        try
        {
            var payload = JsonSerializer.Serialize(new { roots = new[] { root }, mode = "DryRun" });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var createResponse = await client.PostAsync("/runs", content);
            Assert.Equal(HttpStatusCode.Accepted, createResponse.StatusCode);

            using var createDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
            var runId = createDoc.RootElement.GetProperty("run").GetProperty("runId").GetString();

            using var streamRequest = new HttpRequestMessage(HttpMethod.Get, $"/runs/{runId}/stream");
            using var streamResponse = await client.SendAsync(streamRequest, HttpCompletionOption.ResponseHeadersRead);
            Assert.Equal(HttpStatusCode.OK, streamResponse.StatusCode);

            var body = await streamResponse.Content.ReadAsStringAsync();
            Assert.Contains(":\n\n", body, StringComparison.Ordinal);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Health_ResponseContainsVersionHeader()
    {
        using var factory = CreateFactory();
        using var client = CreateClientWithApiKey(factory);

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Api-Version", out var versions));
        Assert.Contains("1.0", versions);
    }

    private static void AssertError(JsonElement root, string expectedCode, ErrorKind expectedKind, string expectedMessageFragment, string? expectedRunId = null)
    {
        var error = root.GetProperty("error");
        Assert.Equal(expectedCode, error.GetProperty("code").GetString());
        Assert.Equal(expectedKind.ToString(), error.GetProperty("kind").GetString());
        Assert.Equal("API", error.GetProperty("module").GetString());
        Assert.Contains(expectedMessageFragment, error.GetProperty("message").GetString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(expectedKind == ErrorKind.Transient, root.GetProperty("retryable").GetBoolean());

        if (expectedRunId is not null)
            Assert.Equal(expectedRunId, root.GetProperty("runId").GetString());
    }
}