using System.Text.Json;
using Xunit;

namespace Romulus.Tests;

public sealed class OpenApiProductizationTests
{
    [Fact]
    public async Task OpenApiSpec_DeclaresProductizationPaths_AndSchemas()
    {
        using var spec = JsonDocument.Parse(await OpenApiTestHelper.FetchOpenApiJsonAsync());
        var paths = spec.RootElement.GetProperty("paths");
        var schemas = spec.RootElement.GetProperty("components").GetProperty("schemas");

        Assert.True(paths.TryGetProperty("/profiles", out var profilesPath), "Missing /profiles path in embedded OpenAPI spec.");
        Assert.True(profilesPath.TryGetProperty("get", out _), "/profiles must declare GET.");
        Assert.True(paths.TryGetProperty("/profiles/{id}", out var profileByIdPath), "Missing /profiles/{id} path in embedded OpenAPI spec.");
        Assert.True(profileByIdPath.TryGetProperty("get", out _), "/profiles/{id} must declare GET.");
        Assert.True(profileByIdPath.TryGetProperty("put", out _), "/profiles/{id} must declare PUT.");
        Assert.True(profileByIdPath.TryGetProperty("delete", out _), "/profiles/{id} must declare DELETE.");

        Assert.True(paths.TryGetProperty("/workflows", out var workflowsPath), "Missing /workflows path in embedded OpenAPI spec.");
        Assert.True(workflowsPath.TryGetProperty("get", out _), "/workflows must declare GET.");
        Assert.True(paths.TryGetProperty("/workflows/{id}", out var workflowByIdPath), "Missing /workflows/{id} path in embedded OpenAPI spec.");
        Assert.True(workflowByIdPath.TryGetProperty("get", out _), "/workflows/{id} must declare GET.");

        Assert.True(paths.TryGetProperty("/runs/compare", out var comparePath), "Missing /runs/compare path in embedded OpenAPI spec.");
        Assert.True(comparePath.TryGetProperty("get", out _), "/runs/compare must declare GET.");
        Assert.True(paths.TryGetProperty("/runs/trends", out var trendsPath), "Missing /runs/trends path in embedded OpenAPI spec.");
        Assert.True(trendsPath.TryGetProperty("get", out _), "/runs/trends must declare GET.");

        Assert.True(paths.TryGetProperty("/export/frontend", out var exportPath), "Missing /export/frontend path in embedded OpenAPI spec.");
        Assert.True(exportPath.TryGetProperty("post", out _), "/export/frontend must declare POST.");

        Assert.True(schemas.TryGetProperty("RunProfileDocument", out _), "Missing RunProfileDocument schema in embedded OpenAPI spec.");
        Assert.True(schemas.TryGetProperty("RunProfileSummary", out _), "Missing RunProfileSummary schema in embedded OpenAPI spec.");
        Assert.True(schemas.TryGetProperty("WorkflowScenarioDefinition", out _), "Missing WorkflowScenarioDefinition schema in embedded OpenAPI spec.");
        Assert.True(schemas.TryGetProperty("RunSnapshotComparison", out _), "Missing RunSnapshotComparison schema in embedded OpenAPI spec.");
        Assert.True(schemas.TryGetProperty("StorageInsightReport", out _), "Missing StorageInsightReport schema in embedded OpenAPI spec.");
        Assert.True(schemas.TryGetProperty("FrontendExportResult", out _), "Missing FrontendExportResult schema in embedded OpenAPI spec.");
    }
}
