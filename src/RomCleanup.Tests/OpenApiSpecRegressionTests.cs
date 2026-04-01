using System.Text.Json;
using Xunit;

namespace RomCleanup.Tests;

public sealed class OpenApiSpecRegressionTests
{
    [Fact]
    public async Task OpenApiSpec_DeclaresRollbackPath_And_RunStatusSchema()
    {
        using var spec = JsonDocument.Parse(await OpenApiTestHelper.FetchOpenApiJsonAsync());
        var paths = spec.RootElement.GetProperty("paths");
        var schemas = spec.RootElement.GetProperty("components").GetProperty("schemas");

        Assert.True(paths.TryGetProperty("/runs/{runId}/rollback", out var rollbackPath), "Missing rollback path in embedded OpenAPI spec.");
        Assert.True(rollbackPath.TryGetProperty("post", out _), "Rollback path must declare POST.");
        Assert.True(schemas.TryGetProperty("RunStatusDto", out _), "Missing RunStatusDto schema in embedded OpenAPI spec.");
        Assert.True(schemas.TryGetProperty("AuditRollbackResult", out _), "Missing AuditRollbackResult schema in embedded OpenAPI spec.");
    }

    [Fact]
    public async Task OpenApiSpec_DeclaresReviewPagination_Metadata()
    {
        using var spec = JsonDocument.Parse(await OpenApiTestHelper.FetchOpenApiJsonAsync());

        var reviewsGet = spec.RootElement
            .GetProperty("paths")
            .GetProperty("/runs/{runId}/reviews")
            .GetProperty("get");

        var parameters = reviewsGet.GetProperty("parameters");
        Assert.Contains(parameters.EnumerateArray(), parameter => parameter.GetProperty("name").GetString() == "offset");
        Assert.Contains(parameters.EnumerateArray(), parameter => parameter.GetProperty("name").GetString() == "limit");

        var queueProps = spec.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("ApiReviewQueue")
            .GetProperty("properties");

        Assert.True(queueProps.TryGetProperty("offset", out _));
        Assert.True(queueProps.TryGetProperty("limit", out _));
        Assert.True(queueProps.TryGetProperty("returned", out _));
        Assert.True(queueProps.TryGetProperty("hasMore", out _));
    }

    [Fact]
    public async Task OpenApiSpec_DeclaresPublicHealthz_WithoutSecurityRequirement()
    {
        using var spec = JsonDocument.Parse(await OpenApiTestHelper.FetchOpenApiJsonAsync());

        var healthzGet = spec.RootElement
            .GetProperty("paths")
            .GetProperty("/healthz")
            .GetProperty("get");

        Assert.True(healthzGet.TryGetProperty("security", out var security));
        Assert.Equal(JsonValueKind.Array, security.ValueKind);
        Assert.Equal(0, security.GetArrayLength());
    }

    [Fact]
    public async Task OpenApiSpec_DeclaresRunHistory_And_ArtifactDownloadPaths()
    {
        using var spec = JsonDocument.Parse(await OpenApiTestHelper.FetchOpenApiJsonAsync());
        var paths = spec.RootElement.GetProperty("paths");
        var schemas = spec.RootElement.GetProperty("components").GetProperty("schemas");

        var runsGet = paths
            .GetProperty("/runs")
            .GetProperty("get");
        var runsParameters = runsGet.GetProperty("parameters");

        Assert.Contains(runsParameters.EnumerateArray(), parameter => parameter.GetProperty("name").GetString() == "offset");
        Assert.Contains(runsParameters.EnumerateArray(), parameter => parameter.GetProperty("name").GetString() == "limit");
        Assert.True(paths.TryGetProperty("/runs/{runId}/report", out var reportPath), "Missing report download path in embedded OpenAPI spec.");
        Assert.True(reportPath.TryGetProperty("get", out _), "Report download path must declare GET.");
        Assert.True(paths.TryGetProperty("/runs/{runId}/audit", out var auditPath), "Missing audit download path in embedded OpenAPI spec.");
        Assert.True(auditPath.TryGetProperty("get", out _), "Audit download path must declare GET.");
        Assert.True(schemas.TryGetProperty("ApiRunList", out _), "Missing ApiRunList schema in embedded OpenAPI spec.");
        Assert.True(paths.TryGetProperty("/runs/history", out var historyPath), "Missing persisted run history path in embedded OpenAPI spec.");
        Assert.True(historyPath.TryGetProperty("get", out _), "Persisted run history path must declare GET.");
        Assert.True(schemas.TryGetProperty("ApiRunHistoryList", out _), "Missing ApiRunHistoryList schema in embedded OpenAPI spec.");
    }

    [Fact]
    public async Task OpenApiSpec_DeclaresWatchAutomationPaths_And_StatusSchema()
    {
        using var spec = JsonDocument.Parse(await OpenApiTestHelper.FetchOpenApiJsonAsync());
        var paths = spec.RootElement.GetProperty("paths");
        var schemas = spec.RootElement.GetProperty("components").GetProperty("schemas");

        Assert.True(paths.TryGetProperty("/watch/start", out var watchStartPath), "Missing watch start path in embedded OpenAPI spec.");
        Assert.True(watchStartPath.TryGetProperty("post", out _), "Watch start path must declare POST.");
        Assert.True(paths.TryGetProperty("/watch/stop", out var watchStopPath), "Missing watch stop path in embedded OpenAPI spec.");
        Assert.True(watchStopPath.TryGetProperty("post", out _), "Watch stop path must declare POST.");
        Assert.True(paths.TryGetProperty("/watch/status", out var watchStatusPath), "Missing watch status path in embedded OpenAPI spec.");
        Assert.True(watchStatusPath.TryGetProperty("get", out _), "Watch status path must declare GET.");
        Assert.True(schemas.TryGetProperty("ApiWatchStatus", out _), "Missing ApiWatchStatus schema in embedded OpenAPI spec.");
    }
}
