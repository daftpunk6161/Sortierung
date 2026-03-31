using System.Text.Json;
using RomCleanup.Api;
using Xunit;

namespace RomCleanup.Tests;

public sealed class OpenApiSpecRegressionTests
{
    [Fact]
    public void OpenApiSpec_DeclaresRollbackPath_And_RunStatusSchema()
    {
        using var spec = JsonDocument.Parse(OpenApiSpec.Json);
        var paths = spec.RootElement.GetProperty("paths");
        var schemas = spec.RootElement.GetProperty("components").GetProperty("schemas");

        Assert.True(paths.TryGetProperty("/runs/{runId}/rollback", out var rollbackPath), "Missing rollback path in embedded OpenAPI spec.");
        Assert.True(rollbackPath.TryGetProperty("post", out _), "Rollback path must declare POST.");
        Assert.True(schemas.TryGetProperty("RunStatusDto", out _), "Missing RunStatusDto schema in embedded OpenAPI spec.");
        Assert.True(schemas.TryGetProperty("AuditRollbackResult", out _), "Missing AuditRollbackResult schema in embedded OpenAPI spec.");
    }

    [Fact]
    public void OpenApiSpec_DeclaresReviewPagination_Metadata()
    {
        using var spec = JsonDocument.Parse(OpenApiSpec.Json);

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
    public void OpenApiSpec_DeclaresPublicHealthz_WithoutSecurityRequirement()
    {
        using var spec = JsonDocument.Parse(OpenApiSpec.Json);

        var healthzGet = spec.RootElement
            .GetProperty("paths")
            .GetProperty("/healthz")
            .GetProperty("get");

        Assert.True(healthzGet.TryGetProperty("security", out var security));
        Assert.Equal(JsonValueKind.Array, security.ValueKind);
        Assert.Equal(0, security.GetArrayLength());
    }
}
