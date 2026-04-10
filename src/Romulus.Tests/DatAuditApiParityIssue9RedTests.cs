using System.Text.Json;
using Romulus.Api;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// TDD RED (Issue9/A-11): DAT audit counters must flow into API DTO and OpenAPI schema.
/// </summary>
public sealed class DatAuditApiParityIssue9RedTests
{
    [Fact]
    public void ApiRunResultMapper_ShouldMapDatAuditCounters_Issue9()
    {
        var run = new RunResult
        {
            DatHaveCount = 10,
            DatHaveWrongNameCount = 9,
            DatMissCount = 8,
            DatUnknownCount = 7,
            DatAmbiguousCount = 6
        };

        var projection = RunProjectionFactory.Create(run);
        var api = ApiRunResultMapper.Map(run, projection);

        Assert.Equal(10, api.DatHaveCount);
        Assert.Equal(9, api.DatHaveWrongNameCount);
        Assert.Equal(8, api.DatMissCount);
        Assert.Equal(7, api.DatUnknownCount);
        Assert.Equal(6, api.DatAmbiguousCount);
    }

    [Fact]
    public async Task OpenApi_ApiRunResultSchema_ShouldContainDatAuditCounters_Issue9()
    {
        using var spec = JsonDocument.Parse(await OpenApiTestHelper.FetchOpenApiJsonAsync());

        var props = spec.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("ApiRunResult")
            .GetProperty("properties");

        Assert.True(props.TryGetProperty("datHaveCount", out _));
        Assert.True(props.TryGetProperty("datHaveWrongNameCount", out _));
        Assert.True(props.TryGetProperty("datMissCount", out _));
        Assert.True(props.TryGetProperty("datUnknownCount", out _));
        Assert.True(props.TryGetProperty("datAmbiguousCount", out _));
    }
}
