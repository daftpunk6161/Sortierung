using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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

/// <summary>
/// Security-focused API tests: path traversal, info disclosure, injection,
/// idempotency, cancellation safety, field consistency.
/// </summary>
public sealed class ApiSecurityTests : IDisposable
{
    private const string ApiKey = "sec-test-api-key";
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
            SafeDeleteDirectory(dir);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  SEC-1: Path Traversal — TrashRoot must be validated
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TrashRoot_SystemDirectory_IsRejected()
    {
        using var factory = CreateFactory();
        using var client = CreateAuthClient(factory);
        var root = CreateTempRoot();

        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var payload = JsonSerializer.Serialize(new
        {
            roots = new[] { root },
            mode = "DryRun",
            trashRoot = windowsDir
        });

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/runs", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("SEC-", body);
    }

    [Fact]
    public async Task TrashRoot_DriveRoot_IsRejected()
    {
        using var factory = CreateFactory();
        using var client = CreateAuthClient(factory);
        var root = CreateTempRoot();

        var payload = JsonSerializer.Serialize(new
        {
            roots = new[] { root },
            mode = "DryRun",
            trashRoot = "C:\\"
        });

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/runs", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("SEC-", body);
    }

    [Fact]
    public async Task DatRoot_SystemDirectory_IsRejected()
    {
        using var factory = CreateFactory();
        using var client = CreateAuthClient(factory);
        var root = CreateTempRoot();

        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var payload = JsonSerializer.Serialize(new
        {
            roots = new[] { root },
            mode = "DryRun",
            datRoot = windowsDir
        });

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/runs", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("SEC-", body);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  SEC-2: ConvertFormat Allowlist
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ConvertFormat_InvalidValue_Returns400()
    {
        using var factory = CreateFactory();
        using var client = CreateAuthClient(factory);
        var root = CreateTempRoot();

        var payload = JsonSerializer.Serialize(new
        {
            roots = new[] { root },
            mode = "DryRun",
            convertFormat = "../../etc/passwd"
        });

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/runs", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("RUN-INVALID-CONVERT-FORMAT", body);
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("chd")]
    [InlineData("rvz")]
    [InlineData("zip")]
    [InlineData("7z")]
    public async Task ConvertFormat_ValidValues_Accepted(string format)
    {
        using var factory = CreateFactory();
        using var client = CreateAuthClient(factory);
        var root = CreateTempRoot();

        var payload = JsonSerializer.Serialize(new
        {
            roots = new[] { root },
            mode = "DryRun",
            convertFormat = format
        });

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/runs?wait=true", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  SEC-3: Correlation-ID Injection
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("valid-correlation-id-123")]
    [InlineData("abc123def456")]
    public async Task CorrelationId_ValidValues_EchoedBack(string correlationId)
    {
        using var factory = CreateFactory();
        using var client = CreateAuthClient(factory);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Correlation-ID", correlationId);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Correlation-ID", out var values));
        Assert.Equal(correlationId, values!.First());
    }

    [Fact]
    public async Task CorrelationId_WithNewlines_IsSanitized()
    {
        using var factory = CreateFactory();
        using var client = CreateAuthClient(factory);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", "injected\nfake-header: malicious");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Correlation-ID", out var values));
        // Injected value must be rejected — server generates its own
        var actual = values!.First();
        Assert.DoesNotContain("\n", actual);
        Assert.NotEqual("injected", actual);
    }

    [Fact]
    public async Task CorrelationId_TooLong_IsSanitized()
    {
        using var factory = CreateFactory();
        using var client = CreateAuthClient(factory);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", new string('A', 200));
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Correlation-ID", out var values));
        Assert.True(values!.First().Length <= 64, "Oversized correlation ID should be replaced by server-generated one.");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  SEC-4: Information Disclosure — Error messages must not leak internals
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FailedRun_ErrorMessage_DoesNotLeakExceptionDetails()
    {
        using var factory = CreateFactory(executor: (_, _, _, _) =>
            throw new InvalidOperationException("Secret DB password is XYZ123!"));
        using var client = CreateAuthClient(factory);
        var root = CreateTempRoot();

        var payload = JsonSerializer.Serialize(new { roots = new[] { root }, mode = "DryRun" });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/runs?wait=true", content);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var runId = doc.RootElement.GetProperty("run").GetProperty("runId").GetString()!;

        var resultResponse = await client.GetAsync($"/runs/{runId}/result");
        using var resultDoc = JsonDocument.Parse(await resultResponse.Content.ReadAsStringAsync());
        var result = resultDoc.RootElement.GetProperty("result");

        if (result.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
        {
            var message = error.GetProperty("message").GetString()!;
            Assert.DoesNotContain("XYZ123", message);
            Assert.DoesNotContain("Secret DB", message);
        }
    }

    [Fact]
    public void OperationError_InnerException_NotSerialized()
    {
        var error = new OperationError("TEST-001", "test error", ErrorKind.Critical, "Test",
            new InvalidOperationException("secret stack trace"));

        var json = JsonSerializer.Serialize(error);

        Assert.DoesNotContain("secret stack trace", json);
        Assert.DoesNotContain("innerException", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stackTrace", json, StringComparison.OrdinalIgnoreCase);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  SEC-5: Cancellation Safety — no misleading CancelledAtUtc
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CompletedRun_WhenCancelRequestedLate_ClearsCancel()
    {
        // Simulate: cancel is requested but executor completes before CTS fires
        using var factory = CreateFactory(executor: (run, _, _, _) =>
        {
            // Simulate cancellation request arriving during execution but not stopping it
            run.CancelledAtUtc = DateTime.UtcNow;
            return new RunExecutionOutcome("completed", new ApiRunResult
            {
                OrchestratorStatus = "ok",
                ExitCode = 0
            });
        });
        using var client = CreateAuthClient(factory);
        var root = CreateTempRoot();

        var payload = JsonSerializer.Serialize(new { roots = new[] { root }, mode = "DryRun" });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/runs?wait=true", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var run = doc.RootElement.GetProperty("run");
        var status = run.GetProperty("status").GetString();

        Assert.Equal("completed", status);
        // CancelledAtUtc should be null if the run actually completed
        if (run.TryGetProperty("cancelledAtUtc", out var cancelTs))
        {
            Assert.True(cancelTs.ValueKind == JsonValueKind.Null,
                "Completed run must not show cancelledAtUtc — it's misleading.");
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  SEC-6: Idempotency key — replay after eviction
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void IdempotencyConflict_DifferentPayload_ReturnsConflict()
    {
        var mgr = new RunManager(new FileSystemAdapter(), new AuditCsvStore());
        var root = CreateTempRoot();

        var first = mgr.TryCreateOrReuse(
            new RunRequest { Roots = new[] { root }, Mode = "DryRun" },
            "DryRun", "sec-idem-001");

        var second = mgr.TryCreateOrReuse(
            new RunRequest { Roots = new[] { root }, Mode = "Move" },
            "Move", "sec-idem-001");

        Assert.Equal(RunCreateDisposition.Created, first.Disposition);
        Assert.Equal(RunCreateDisposition.IdempotencyConflict, second.Disposition);
    }

    [Fact]
    public async Task RunEndpoints_Forbid_DifferentClientBinding()
    {
        using var factory = CreateFactory();
        using var ownerClient = CreateAuthClient(factory, "owner-a");
        using var otherClient = CreateAuthClient(factory, "owner-b");
        var root = CreateTempRoot();

        var payload = JsonSerializer.Serialize(new { roots = new[] { root }, mode = "DryRun" });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var createResponse = await ownerClient.PostAsync("/runs", content);
        Assert.True(createResponse.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var runId = doc.RootElement.GetProperty("run").GetProperty("runId").GetString()!;

        var statusResponse = await otherClient.GetAsync($"/runs/{runId}");
        Assert.Equal(HttpStatusCode.Forbidden, statusResponse.StatusCode);

        var cancelResponse = await otherClient.PostAsync($"/runs/{runId}/cancel", null);
        Assert.Equal(HttpStatusCode.Forbidden, cancelResponse.StatusCode);

        var resultResponse = await otherClient.GetAsync($"/runs/{runId}/result");
        Assert.True(resultResponse.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task RunStatus_DoesNotExpose_SensitiveRunFields()
    {
        using var factory = CreateFactory();
        using var client = CreateAuthClient(factory, "owner-redact");
        var root = CreateTempRoot();

        var payload = JsonSerializer.Serialize(new
        {
            roots = new[] { root },
            mode = "DryRun",
            datRoot = root,
            trashRoot = root
        });

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var createResponse = await client.PostAsync("/runs", content);
        Assert.True(createResponse.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var runId = doc.RootElement.GetProperty("run").GetProperty("runId").GetString()!;

        var statusResponse = await client.GetAsync($"/runs/{runId}");
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        using var statusDoc = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync());
        var run = statusDoc.RootElement.GetProperty("run");

        Assert.False(run.TryGetProperty("roots", out _));
        Assert.False(run.TryGetProperty("datRoot", out _));
        Assert.False(run.TryGetProperty("trashRoot", out _));
        Assert.False(run.TryGetProperty("idempotencyKey", out _));
        Assert.False(run.TryGetProperty("requestFingerprint", out _));
        Assert.False(run.TryGetProperty("ownerClientId", out _));
        Assert.False(run.TryGetProperty("auditPath", out _));
        Assert.False(run.TryGetProperty("reportPath", out _));
    }

    [Fact]
    public async Task RunResult_DoesNotExpose_ArtifactFileSystemPaths()
    {
        using var factory = CreateFactory();
        using var client = CreateAuthClient(factory, "owner-redact-result");
        var root = CreateTempRoot();

        var payload = JsonSerializer.Serialize(new
        {
            roots = new[] { root },
            mode = "DryRun"
        });

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var createResponse = await client.PostAsync("/runs?wait=true", content);
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        using var doc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var result = doc.RootElement.GetProperty("result");

        Assert.False(result.TryGetProperty("auditPath", out _));
        Assert.False(result.TryGetProperty("reportPath", out _));
    }

    [Fact]
    public async Task OpenApi_Contains_SecurityAndParityFields()
    {
        using var factory = CreateFactory();
        using var client = CreateAuthClient(factory);

        var response = await client.GetAsync("/openapi");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var spec = await response.Content.ReadAsStringAsync();

        Assert.Contains("X-Client-Id", spec, StringComparison.Ordinal);
        Assert.Contains("conflictPolicy", spec, StringComparison.Ordinal);
        Assert.Contains("convertOnly", spec, StringComparison.Ordinal);
        Assert.Contains("preflightWarnings", spec, StringComparison.Ordinal);
        Assert.Contains("OperationError", spec, StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════════

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
                        services.AddSingleton(new RunManager(
                            new FileSystemAdapter(),
                            new AuditCsvStore(),
                            executor));
                    });
                }
            });
    }

    private static HttpClient CreateAuthClient(WebApplicationFactory<Program> factory, string? clientId = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        if (!string.IsNullOrWhiteSpace(clientId))
            client.DefaultRequestHeaders.Add("X-Client-Id", clientId);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "RomCleanup_SecTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "sample.rom"), "test");
        _tempDirs.Add(root);
        return root;
    }

    private static void SafeDeleteDirectory(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch { }
    }
}
