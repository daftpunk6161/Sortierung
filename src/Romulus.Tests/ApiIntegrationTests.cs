using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Romulus.Api;
using Romulus.Contracts.Errors;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.FileSystem;
using Xunit;

namespace Romulus.Tests;

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
    public async Task Healthz_WithoutApiKey_ReturnsOk()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal("ok", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("serverRunning").GetBoolean());
        Assert.False(root.TryGetProperty("hasActiveRun", out _));
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
    public async Task Cors_Preflight_CustomInvalidOrigin_FallsBackToStrictLocal()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["CorsMode"] = "custom",
            ["CorsAllowOrigin"] = "invalid-origin"
        });
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Options, "/runs");
        request.Headers.Add("Origin", "http://example.test");
        request.Headers.Add("Access-Control-Request-Method", "POST");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins));
        Assert.Contains("http://127.0.0.1", origins);
    }

    [Fact]
    public async Task Health_InvalidClientIdHeader_ReturnsBadRequest()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        client.DefaultRequestHeaders.Add("X-Client-Id", "bad id with spaces");

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertError(doc.RootElement, "AUTH-INVALID-CLIENT-ID", ErrorKind.Critical, "Invalid X-Client-Id");
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
    public async Task RateLimit_DifferentApiKeys_SameClient_HaveIndependentBuckets()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["ApiKey"] = "integration-test-key;integration-test-key-2",
            ["RateLimitRequests"] = "1",
            ["RateLimitWindowSeconds"] = "60"
        });

        using var firstClient = CreateClientWithApiKey(factory, "integration-test-key");
        using var secondClient = CreateClientWithApiKey(factory, "integration-test-key-2");

        var firstKeyFirstRequest = await firstClient.GetAsync("/health");
        var secondKeyFirstRequest = await secondClient.GetAsync("/health");
        var firstKeySecondRequest = await firstClient.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, firstKeyFirstRequest.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondKeyFirstRequest.StatusCode);
        Assert.Equal((HttpStatusCode)429, firstKeySecondRequest.StatusCode);
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
    public async Task Runs_List_ReturnsVisibleRunsNewestFirst_WithPagination()
    {
        using var factory = CreateFactory();
        using var ownerClient = CreateClientWithApiKey(factory);
        using var otherClient = CreateClientWithApiKey(factory);
        ownerClient.DefaultRequestHeaders.Add("X-Client-Id", "owner-history");
        otherClient.DefaultRequestHeaders.Add("X-Client-Id", "other-history");

        var firstRoot = CreateTempRoot();
        var secondRoot = CreateTempRoot();
        var foreignRoot = CreateTempRoot();

        try
        {
            async Task<string> CreateRunAsync(HttpClient client, string root)
            {
                var payload = JsonSerializer.Serialize(new { roots = new[] { root }, mode = "DryRun" });
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                var response = await client.PostAsync("/runs?wait=true", content);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return doc.RootElement.GetProperty("run").GetProperty("runId").GetString()!;
            }

            var firstRunId = await CreateRunAsync(ownerClient, firstRoot);
            await Task.Delay(25);
            _ = await CreateRunAsync(otherClient, foreignRoot);
            await Task.Delay(25);
            var secondRunId = await CreateRunAsync(ownerClient, secondRoot);

            var listResponse = await ownerClient.GetAsync("/runs");
            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
            using var listDoc = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
            var listRoot = listDoc.RootElement;
            Assert.Equal(2, listRoot.GetProperty("total").GetInt32());
            Assert.Equal(2, listRoot.GetProperty("returned").GetInt32());
            Assert.True(listRoot.TryGetProperty("runs", out var runs));
            Assert.Equal(secondRunId, runs[0].GetProperty("runId").GetString());
            Assert.Equal(firstRunId, runs[1].GetProperty("runId").GetString());

            var pagedResponse = await ownerClient.GetAsync("/runs?offset=1&limit=1");
            Assert.Equal(HttpStatusCode.OK, pagedResponse.StatusCode);
            using var pagedDoc = JsonDocument.Parse(await pagedResponse.Content.ReadAsStringAsync());
            var pagedRoot = pagedDoc.RootElement;
            Assert.Equal(2, pagedRoot.GetProperty("total").GetInt32());
            Assert.Equal(1, pagedRoot.GetProperty("offset").GetInt32());
            Assert.Equal(1, pagedRoot.GetProperty("limit").GetInt32());
            Assert.Equal(1, pagedRoot.GetProperty("returned").GetInt32());
            Assert.False(pagedRoot.GetProperty("hasMore").GetBoolean());
            Assert.Equal(firstRunId, pagedRoot.GetProperty("runs")[0].GetProperty("runId").GetString());
        }
        finally
        {
            SafeDeleteDirectory(firstRoot);
            SafeDeleteDirectory(secondRoot);
            SafeDeleteDirectory(foreignRoot);
        }
    }

    [Fact]
    public async Task Runs_History_ReturnsPersistedSnapshots_WithPagination()
    {
        var fakeIndex = new FakeCollectionIndex(
        [
            new CollectionRunSnapshot
            {
                RunId = "run-new",
                StartedUtc = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
                CompletedUtc = new DateTime(2026, 4, 1, 10, 1, 0, DateTimeKind.Utc),
                Mode = "Move",
                Status = "completed_with_errors",
                Roots = [@"C:\Roms\SNES"],
                RootFingerprint = "abc123",
                DurationMs = 60000,
                TotalFiles = 100,
                CollectionSizeBytes = 333000000,
                Games = 80,
                Dupes = 20,
                Junk = 5,
                DatMatches = 70,
                ConvertedCount = 10,
                FailCount = 2,
                SavedBytes = 1234,
                ConvertSavedBytes = 5678,
                HealthScore = 90
            },
            new CollectionRunSnapshot
            {
                RunId = "run-old",
                StartedUtc = new DateTime(2026, 3, 31, 10, 0, 0, DateTimeKind.Utc),
                CompletedUtc = new DateTime(2026, 3, 31, 10, 1, 0, DateTimeKind.Utc),
                Mode = "DryRun",
                Status = "ok",
                Roots = [@"D:\Roms\NES"],
                RootFingerprint = "def456",
                DurationMs = 30000,
                TotalFiles = 50,
                CollectionSizeBytes = 111000000,
                Games = 40,
                Dupes = 10,
                Junk = 0,
                DatMatches = 35,
                ConvertedCount = 0,
                FailCount = 0,
                SavedBytes = 0,
                ConvertSavedBytes = 0,
                HealthScore = 95
            }
        ]);

        using var factory = CreateFactory(collectionIndex: fakeIndex);
        using var client = CreateClientWithApiKey(factory);

        var response = await client.GetAsync("/runs/history?offset=0&limit=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal(2, root.GetProperty("total").GetInt32());
        Assert.Equal(1, root.GetProperty("returned").GetInt32());
        Assert.True(root.GetProperty("hasMore").GetBoolean());

        var run = root.GetProperty("runs")[0];
        Assert.Equal("run-new", run.GetProperty("runId").GetString());
        Assert.Equal("Move", run.GetProperty("mode").GetString());
        Assert.Equal("completed_with_errors", run.GetProperty("status").GetString());
        Assert.Equal(1, run.GetProperty("rootCount").GetInt32());
        Assert.Equal(100, run.GetProperty("totalFiles").GetInt32());
        Assert.Equal(333000000L, run.GetProperty("collectionSizeBytes").GetInt64());
    }

    [Fact]
    public async Task Runs_Compare_ReturnsPersistedSnapshotDelta()
    {
        var fakeIndex = new FakeCollectionIndex(
        [
            new CollectionRunSnapshot
            {
                RunId = "run-new",
                CompletedUtc = new DateTime(2026, 4, 1, 10, 1, 0, DateTimeKind.Utc),
                Status = "completed_with_errors",
                TotalFiles = 100,
                CollectionSizeBytes = 333000000,
                Games = 80,
                Dupes = 20,
                Junk = 5,
                DatMatches = 70,
                ConvertedCount = 10,
                FailCount = 2,
                SavedBytes = 1234,
                ConvertSavedBytes = 5678,
                HealthScore = 90
            },
            new CollectionRunSnapshot
            {
                RunId = "run-old",
                CompletedUtc = new DateTime(2026, 3, 31, 10, 1, 0, DateTimeKind.Utc),
                Status = "ok",
                TotalFiles = 50,
                CollectionSizeBytes = 111000000,
                Games = 40,
                Dupes = 10,
                Junk = 0,
                DatMatches = 35,
                ConvertedCount = 0,
                FailCount = 0,
                SavedBytes = 100,
                ConvertSavedBytes = 200,
                HealthScore = 95
            }
        ]);

        using var factory = CreateFactory(collectionIndex: fakeIndex);
        using var client = CreateClientWithApiKey(factory);

        var response = await client.GetAsync("/runs/compare?runId=run-new&compareToRunId=run-old");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal("run-new", root.GetProperty("runId").GetString());
        Assert.Equal("run-old", root.GetProperty("compareToRunId").GetString());

        var totalFiles = root.GetProperty("totalFiles");
        Assert.Equal(100, totalFiles.GetProperty("current").GetInt64());
        Assert.Equal(50, totalFiles.GetProperty("previous").GetInt64());
        Assert.Equal(50, totalFiles.GetProperty("delta").GetInt64());

        var size = root.GetProperty("collectionSizeBytes");
        Assert.Equal(333000000L, size.GetProperty("current").GetInt64());
        Assert.Equal(111000000L, size.GetProperty("previous").GetInt64());
        Assert.Equal(222000000L, size.GetProperty("delta").GetInt64());
    }

    [Fact]
    public async Task Runs_Compare_MissingSnapshot_ReturnsNotFound()
    {
        var fakeIndex = new FakeCollectionIndex(
        [
            new CollectionRunSnapshot
            {
                RunId = "run-existing",
                CompletedUtc = new DateTime(2026, 4, 1, 10, 1, 0, DateTimeKind.Utc),
                Status = "ok",
                TotalFiles = 10
            }
        ]);

        using var factory = CreateFactory(collectionIndex: fakeIndex);
        using var client = CreateClientWithApiKey(factory);

        var response = await client.GetAsync("/runs/compare?runId=run-existing&compareToRunId=run-missing");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertError(doc.RootElement, "RUN-COMPARE-NOT-FOUND", ErrorKind.Recoverable, "not found");
    }

    [Fact]
    public async Task Runs_Trends_ReturnsStorageInsights_FromPersistedSnapshots()
    {
        var fakeIndex = new FakeCollectionIndex(
        [
            new CollectionRunSnapshot
            {
                RunId = "run-old",
                CompletedUtc = new DateTime(2026, 3, 31, 10, 1, 0, DateTimeKind.Utc),
                TotalFiles = 50,
                CollectionSizeBytes = 111000000,
                SavedBytes = 100,
                ConvertSavedBytes = 200,
                HealthScore = 95
            },
            new CollectionRunSnapshot
            {
                RunId = "run-new",
                CompletedUtc = new DateTime(2026, 4, 1, 10, 1, 0, DateTimeKind.Utc),
                TotalFiles = 100,
                CollectionSizeBytes = 333000000,
                SavedBytes = 1234,
                ConvertSavedBytes = 5678,
                HealthScore = 90
            }
        ]);

        using var factory = CreateFactory(collectionIndex: fakeIndex);
        using var client = CreateClientWithApiKey(factory);

        var response = await client.GetAsync("/runs/trends?limit=30");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal(2, root.GetProperty("sampleCount").GetInt32());
        Assert.Equal(1234, root.GetProperty("latestSavedBytes").GetInt64());
        Assert.Equal(5678, root.GetProperty("latestConvertSavedBytes").GetInt64());
        Assert.Equal(1334, root.GetProperty("cumulativeSavedBytes").GetInt64());
        Assert.Equal(5878, root.GetProperty("cumulativeConvertSavedBytes").GetInt64());

        var files = root.GetProperty("totalFiles");
        Assert.Equal(100, files.GetProperty("current").GetInt64());
        Assert.Equal(50, files.GetProperty("previous").GetInt64());
        Assert.Equal(50, files.GetProperty("delta").GetInt64());

        var size = root.GetProperty("collectionSizeBytes");
        Assert.Equal(222000000L, size.GetProperty("delta").GetInt64());
        Assert.Equal(222000000d, root.GetProperty("averageRunGrowthBytes").GetDouble());
    }

    [Fact]
    public async Task Watch_StartStatusStop_RoundTrips_ForOwnerClient()
    {
        using var factory = CreateFactory();
        using var client = CreateClientWithApiKey(factory);

        var root = CreateTempRoot();
        try
        {
            client.DefaultRequestHeaders.Add("X-Client-Id", "watch-owner");
            var payload = JsonSerializer.Serialize(new
            {
                roots = new[] { root },
                mode = "DryRun"
            });

            using var startContent = new StringContent(payload, Encoding.UTF8, "application/json");
            var startResponse = await client.PostAsync("/watch/start?intervalMinutes=15&debounceSeconds=4", startContent);
            Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);

            using var startDoc = JsonDocument.Parse(await startResponse.Content.ReadAsStringAsync());
            Assert.True(startDoc.RootElement.GetProperty("active").GetBoolean());
            Assert.Equal(4, startDoc.RootElement.GetProperty("debounceSeconds").GetInt32());
            Assert.Equal(15, startDoc.RootElement.GetProperty("intervalMinutes").GetInt32());
            Assert.Equal(1, startDoc.RootElement.GetProperty("watchedRootCount").GetInt32());

            var statusResponse = await client.GetAsync("/watch/status");
            Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
            using var statusDoc = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync());
            Assert.True(statusDoc.RootElement.GetProperty("active").GetBoolean());
            Assert.Equal("DryRun", statusDoc.RootElement.GetProperty("mode").GetString());
            Assert.Equal(root, statusDoc.RootElement.GetProperty("roots")[0].GetString());

            var stopResponse = await client.PostAsync("/watch/stop", null);
            Assert.Equal(HttpStatusCode.OK, stopResponse.StatusCode);
            using var stopDoc = JsonDocument.Parse(await stopResponse.Content.ReadAsStringAsync());
            Assert.False(stopDoc.RootElement.GetProperty("active").GetBoolean());
            Assert.Empty(stopDoc.RootElement.GetProperty("roots").EnumerateArray());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Watch_Status_IsForbidden_ForForeignClientWhileActive()
    {
        using var factory = CreateFactory();
        using var ownerClient = CreateClientWithApiKey(factory);
        using var foreignClient = CreateClientWithApiKey(factory);

        var root = CreateTempRoot();
        try
        {
            ownerClient.DefaultRequestHeaders.Add("X-Client-Id", "watch-owner");
            foreignClient.DefaultRequestHeaders.Add("X-Client-Id", "watch-foreign");

            var payload = JsonSerializer.Serialize(new
            {
                roots = new[] { root },
                mode = "DryRun"
            });

            using var startContent = new StringContent(payload, Encoding.UTF8, "application/json");
            var startResponse = await ownerClient.PostAsync("/watch/start?intervalMinutes=5", startContent);
            Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);

            var statusResponse = await foreignClient.GetAsync("/watch/status");
            Assert.Equal(HttpStatusCode.Forbidden, statusResponse.StatusCode);

            using var errorDoc = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync());
            AssertError(errorDoc.RootElement, "AUTH-FORBIDDEN", ErrorKind.Critical, "different client");

            _ = await ownerClient.PostAsync("/watch/stop", null);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunReportDownload_ReturnsHtmlArtifact_ForCompletedRun()
    {
        var root = CreateTempRoot();
        var reportPath = Path.Combine(root, "artifacts", "report-test.html");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);

        using var factory = CreateFactory(executor: (run, _, _, _) =>
        {
            File.WriteAllText(reportPath, "<html><body>report-ok</body></html>");
            run.ReportPath = reportPath;
            return new RunExecutionOutcome(ApiRunStatus.Completed, new ApiRunResult
            {
                OrchestratorStatus = "ok",
                ExitCode = 0
            });
        });
        using var client = CreateClientWithApiKey(factory);

        try
        {
            var payload = JsonSerializer.Serialize(new { roots = new[] { root }, mode = "DryRun" });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var createResponse = await client.PostAsync("/runs?wait=true", content);
            Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
            using var createDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
            var runId = createDoc.RootElement.GetProperty("run").GetProperty("runId").GetString()!;

            var response = await client.GetAsync($"/runs/{runId}/report");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("report-ok", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
            Assert.Equal("report-test.html", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAuditDownload_ReturnsCsvArtifact_ForCompletedRun()
    {
        var root = CreateTempRoot();
        var auditPath = Path.Combine(root, "artifacts", "audit-test.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(auditPath)!);

        using var factory = CreateFactory(executor: (run, _, _, _) =>
        {
            File.WriteAllText(auditPath, "RootPath,OldPath,NewPath,Action\nC:\\roms\\a,C:\\roms\\a,C:\\trash\\a,move\n");
            run.AuditPath = auditPath;
            return new RunExecutionOutcome(ApiRunStatus.Completed, new ApiRunResult
            {
                OrchestratorStatus = "ok",
                ExitCode = 0
            });
        });
        using var client = CreateClientWithApiKey(factory);

        try
        {
            var payload = JsonSerializer.Serialize(new { roots = new[] { root }, mode = "Move" });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var createResponse = await client.PostAsync("/runs?wait=true", content);
            Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
            using var createDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
            var runId = createDoc.RootElement.GetProperty("run").GetProperty("runId").GetString()!;

            var response = await client.GetAsync($"/runs/{runId}/audit");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("RootPath,OldPath,NewPath,Action", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
            Assert.Equal("audit-test.csv", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
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
    public async Task Runs_ApproveReviews_Flag_IsExposedInStatusDto()
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
                approveReviews = true
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var createResponse = await client.PostAsync("/runs?wait=true", content);
            Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

            using var createDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
            var runId = createDoc.RootElement.GetProperty("run").GetProperty("runId").GetString();
            Assert.False(string.IsNullOrWhiteSpace(runId));

            var statusResponse = await client.GetAsync($"/runs/{runId}");
            Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);

            using var statusDoc = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync());
            var run = statusDoc.RootElement.GetProperty("run");
            Assert.True(run.GetProperty("approveReviews").GetBoolean());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Runs_ReviewsEndpoints_ReturnQueue_AndAllowApprovalByPath()
    {
        using var factory = CreateFactory();
        using var client = CreateClientWithApiKey(factory);

        var root = CreateTempRoot();
        try
        {
            // Generate at least one low-confidence candidate so review queue is populated.
            File.WriteAllText(Path.Combine(root, "mystery_title_no_console_hint.bin"), "x");

            var payload = JsonSerializer.Serialize(new
            {
                roots = new[] { root },
                mode = "DryRun"
            });

            using var createContent = new StringContent(payload, Encoding.UTF8, "application/json");
            var createResponse = await client.PostAsync("/runs?wait=true", createContent);
            Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

            using var createDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
            var runId = createDoc.RootElement.GetProperty("run").GetProperty("runId").GetString();
            Assert.False(string.IsNullOrWhiteSpace(runId));

            var reviewsResponse = await client.GetAsync($"/runs/{runId}/reviews");
            Assert.Equal(HttpStatusCode.OK, reviewsResponse.StatusCode);

            using var reviewsDoc = JsonDocument.Parse(await reviewsResponse.Content.ReadAsStringAsync());
            var items = reviewsDoc.RootElement.GetProperty("items");
            Assert.True(items.GetArrayLength() >= 1);

            var firstPath = items[0].GetProperty("mainPath").GetString();
            Assert.False(string.IsNullOrWhiteSpace(firstPath));

            var approvePayload = JsonSerializer.Serialize(new
            {
                paths = new[] { firstPath }
            });
            using var approveContent = new StringContent(approvePayload, Encoding.UTF8, "application/json");
            var approveResponse = await client.PostAsync($"/runs/{runId}/reviews/approve", approveContent);
            Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

            using var approveDoc = JsonDocument.Parse(await approveResponse.Content.ReadAsStringAsync());
            Assert.Equal(1, approveDoc.RootElement.GetProperty("approvedCount").GetInt32());

            var updatedItems = approveDoc.RootElement.GetProperty("queue").GetProperty("items");
            var approvedItem = updatedItems.EnumerateArray()
                .First(item => string.Equals(item.GetProperty("mainPath").GetString(), firstPath, StringComparison.OrdinalIgnoreCase));
            Assert.True(approvedItem.GetProperty("approved").GetBoolean());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Runs_ReviewsEndpoint_SupportsOffsetAndLimitPagination()
    {
        using var factory = CreateFactory();
        using var client = CreateClientWithApiKey(factory);

        var root = CreateTempRoot();
        try
        {
            File.WriteAllText(Path.Combine(root, "alpha_unknown.bin"), "a");
            File.WriteAllText(Path.Combine(root, "beta_unknown.bin"), "b");
            File.WriteAllText(Path.Combine(root, "gamma_unknown.bin"), "c");

            var payload = JsonSerializer.Serialize(new
            {
                roots = new[] { root },
                mode = "DryRun"
            });

            using var createContent = new StringContent(payload, Encoding.UTF8, "application/json");
            var createResponse = await client.PostAsync("/runs?wait=true", createContent);
            Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

            using var createDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
            var runId = createDoc.RootElement.GetProperty("run").GetProperty("runId").GetString();
            Assert.False(string.IsNullOrWhiteSpace(runId));

            var fullResponse = await client.GetAsync($"/runs/{runId}/reviews");
            Assert.Equal(HttpStatusCode.OK, fullResponse.StatusCode);

            using var fullDoc = JsonDocument.Parse(await fullResponse.Content.ReadAsStringAsync());
            var fullItems = fullDoc.RootElement.GetProperty("items");
            Assert.True(fullItems.GetArrayLength() >= 3);

            var pagedResponse = await client.GetAsync($"/runs/{runId}/reviews?offset=1&limit=1");
            Assert.Equal(HttpStatusCode.OK, pagedResponse.StatusCode);

            using var pagedDoc = JsonDocument.Parse(await pagedResponse.Content.ReadAsStringAsync());
            var pagedRoot = pagedDoc.RootElement;
            Assert.Equal(1, pagedRoot.GetProperty("offset").GetInt32());
            Assert.Equal(1, pagedRoot.GetProperty("limit").GetInt32());
            Assert.Equal(1, pagedRoot.GetProperty("returned").GetInt32());
            Assert.True(pagedRoot.TryGetProperty("hasMore", out _));

            var pagedItems = pagedRoot.GetProperty("items");
            Assert.Equal(1, pagedItems.GetArrayLength());
            Assert.Equal(
                fullItems[1].GetProperty("mainPath").GetString(),
                pagedItems[0].GetProperty("mainPath").GetString());
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

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var components = doc.RootElement.GetProperty("components");
        var securitySchemes = components.GetProperty("securitySchemes");
        var apiKeyScheme = securitySchemes.GetProperty("ApiKey");
        var security = doc.RootElement.GetProperty("security");

        Assert.Equal("apiKey", apiKeyScheme.GetProperty("type").GetString());
        Assert.Equal("header", apiKeyScheme.GetProperty("in").GetString());
        Assert.Equal("X-Api-Key", apiKeyScheme.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Array, security.ValueKind);
        Assert.True(security.GetArrayLength() > 0);
        Assert.True(security[0].TryGetProperty("ApiKey", out _));
    }

    private static WebApplicationFactory<Program> CreateFactory(
        Dictionary<string, string?>? overrides = null,
        Func<RunRecord, Romulus.Contracts.Ports.IFileSystem, Romulus.Contracts.Ports.IAuditStore, CancellationToken, RunExecutionOutcome>? executor = null,
        ICollectionIndex? collectionIndex = null)
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

        return ApiTestFactory.Create(settings, executor, collectionIndex);
    }

    private static HttpClient CreateClientWithApiKey(WebApplicationFactory<Program> factory)
        => CreateClientWithApiKey(factory, ApiKey);

    private static HttpClient CreateClientWithApiKey(WebApplicationFactory<Program> factory, string apiKey)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "Romulus_ApiInt_" + Guid.NewGuid().ToString("N"));
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
        Assert.Contains("RUN-INVALID-CONFIG", body, StringComparison.OrdinalIgnoreCase);
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
        var trashRoot = Path.Combine(Path.GetTempPath(), "Romulus_ApiTrash_" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(Path.Combine(root, "MegaGame (Europe).zip"), "eu");
            File.WriteAllText(Path.Combine(root, "MegaGame (USA).zip"), "us");

            var payload = JsonSerializer.Serialize(new
            {
                roots = new[] { root },
                mode = "Move",
                removeJunk = false,
                trashRoot,
                preferRegions = new[] { "US", "EU" }
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var createResponse = await client.PostAsync("/runs?wait=true", content);
            Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

            using var createDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
            var runId = createDoc.RootElement.GetProperty("run").GetProperty("runId").GetString();
            Assert.False(string.IsNullOrWhiteSpace(runId));

            // SEC-07: rollback defaults to dryRun=true — explicit false for actual rollback
            var rollbackResponse = await client.PostAsync($"/runs/{runId}/rollback?dryRun=false", new StringContent("", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, rollbackResponse.StatusCode);

            using var rollbackDoc = JsonDocument.Parse(await rollbackResponse.Content.ReadAsStringAsync());
            var rollback = rollbackDoc.RootElement.GetProperty("rollback");
            Assert.True(rollback.GetProperty("rolledBack").GetInt32() >= 1);
            Assert.Equal(0, rollback.GetProperty("failed").GetInt32());
        }
        finally
        {
            SafeDeleteDirectory(root);
            SafeDeleteDirectory(trashRoot);
        }
    }

    [Fact]
    public async Task Runs_MoveMode_MovesLosersToTrash()
    {
        using var factory = CreateFactory();
        using var client = CreateClientWithApiKey(factory);

        var root = CreateTempRoot();
        var trashRoot = Path.Combine(Path.GetTempPath(), "Romulus_ApiTrash_" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(Path.Combine(root, "MegaGame (Europe).zip"), "eu");
            File.WriteAllText(Path.Combine(root, "MegaGame (USA).zip"), "us");

            var payload = JsonSerializer.Serialize(new
            {
                roots = new[] { root },
                mode = "Move",
                removeJunk = false,
                trashRoot,
                preferRegions = new[] { "US", "EU" }
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await client.PostAsync("/runs?wait=true", content);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var trashDir = Path.Combine(trashRoot, "_TRASH_REGION_DEDUPE");
            Assert.True(Directory.Exists(trashDir));
            var moved = Directory.GetFiles(trashDir);
            Assert.NotEmpty(moved);
        }
        finally
        {
            SafeDeleteDirectory(root);
            SafeDeleteDirectory(trashRoot);
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

    [Fact]
    public async Task Healthz_ResponseContainsVersionHeader()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/healthz");

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

    private sealed class FakeCollectionIndex : ICollectionIndex
    {
        private readonly IReadOnlyList<CollectionRunSnapshot> _snapshots;

        public FakeCollectionIndex(IReadOnlyList<CollectionRunSnapshot> snapshots)
        {
            _snapshots = snapshots;
        }

        public ValueTask<CollectionIndexMetadata> GetMetadataAsync(CancellationToken ct = default)
            => ValueTask.FromResult(new CollectionIndexMetadata { CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow });

        public ValueTask<int> CountEntriesAsync(CancellationToken ct = default)
            => ValueTask.FromResult(0);

        public ValueTask<CollectionIndexEntry?> TryGetByPathAsync(string path, CancellationToken ct = default)
            => ValueTask.FromResult<CollectionIndexEntry?>(null);

        public ValueTask<IReadOnlyList<CollectionIndexEntry>> GetByPathsAsync(IReadOnlyList<string> paths, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<CollectionIndexEntry>>(Array.Empty<CollectionIndexEntry>());

        public ValueTask<IReadOnlyList<CollectionIndexEntry>> ListByConsoleAsync(string consoleKey, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<CollectionIndexEntry>>(Array.Empty<CollectionIndexEntry>());

        public ValueTask<IReadOnlyList<CollectionIndexEntry>> ListEntriesInScopeAsync(IReadOnlyList<string> roots, IReadOnlyCollection<string> extensions, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<CollectionIndexEntry>>(Array.Empty<CollectionIndexEntry>());

        public ValueTask UpsertEntriesAsync(IReadOnlyList<CollectionIndexEntry> entries, CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask RemovePathsAsync(IReadOnlyList<string> paths, CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask<CollectionHashCacheEntry?> TryGetHashAsync(string path, string algorithm, long sizeBytes, DateTime lastWriteUtc, CancellationToken ct = default)
            => ValueTask.FromResult<CollectionHashCacheEntry?>(null);

        public ValueTask SetHashAsync(CollectionHashCacheEntry entry, CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask AppendRunSnapshotAsync(CollectionRunSnapshot snapshot, CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask<int> CountRunSnapshotsAsync(CancellationToken ct = default)
            => ValueTask.FromResult(_snapshots.Count);

        public ValueTask<IReadOnlyList<CollectionRunSnapshot>> ListRunSnapshotsAsync(int limit = 50, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<CollectionRunSnapshot>>(_snapshots.Take(limit).ToArray());
    }
}
