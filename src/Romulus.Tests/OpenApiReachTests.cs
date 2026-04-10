using System.Text.Json;
using Xunit;

namespace Romulus.Tests;

public sealed class OpenApiReachTests
{
    [Fact]
    public async Task OpenApiSpec_DeclaresReachPaths_AndSchemas()
    {
        using var spec = JsonDocument.Parse(await OpenApiTestHelper.FetchOpenApiJsonAsync());
        var paths = spec.RootElement.GetProperty("paths");
        var schemas = spec.RootElement.GetProperty("components").GetProperty("schemas");

        Assert.True(paths.TryGetProperty("/dashboard/bootstrap", out var bootstrapPath), "Missing /dashboard/bootstrap path in embedded OpenAPI spec.");
        Assert.True(bootstrapPath.TryGetProperty("get", out _), "/dashboard/bootstrap must declare GET.");
        Assert.True(paths.TryGetProperty("/dashboard/summary", out var summaryPath), "Missing /dashboard/summary path in embedded OpenAPI spec.");
        Assert.True(summaryPath.TryGetProperty("get", out _), "/dashboard/summary must declare GET.");
        Assert.True(paths.TryGetProperty("/dats/status", out var datStatusPath), "Missing /dats/status path in embedded OpenAPI spec.");
        Assert.True(datStatusPath.TryGetProperty("get", out _), "/dats/status must declare GET.");

        Assert.True(schemas.TryGetProperty("DashboardBootstrapResponse", out _), "Missing DashboardBootstrapResponse schema.");
        Assert.True(schemas.TryGetProperty("DashboardSummaryResponse", out _), "Missing DashboardSummaryResponse schema.");
        Assert.True(schemas.TryGetProperty("DashboardDatStatusResponse", out _), "Missing DashboardDatStatusResponse schema.");
    }
}
