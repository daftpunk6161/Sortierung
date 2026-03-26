using System.Text.Json;
using RomCleanup.Api;
using RomCleanup.CLI;
using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.Orchestration;
using RomCleanup.Infrastructure.Reporting;
using Xunit;

namespace RomCleanup.Tests;

/// <summary>
/// Phase 4 / TASK-031: Cross-channel parity tests.
/// Same input data flows through CLI JSON, API response, and HTML report — all must show identical DAT audit numbers.
/// </summary>
public sealed class DatAuditCrossChannelParityTests
{
    // ── Shared fixture: identical DAT audit numbers ───────────────

    private const int Have = 42;
    private const int HaveWrongName = 13;
    private const int Miss = 7;
    private const int Unknown = 3;
    private const int Ambiguous = 2;

    private static RunResult CreateRunResult() => new()
    {
        DatHaveCount = Have,
        DatHaveWrongNameCount = HaveWrongName,
        DatMissCount = Miss,
        DatUnknownCount = Unknown,
        DatAmbiguousCount = Ambiguous
    };

    // ── CLI tests ─────────────────────────────────────────────────

    [Fact]
    public void CliDryRunJson_ContainsDatAuditCounters_WithCorrectValues()
    {
        var result = CreateRunResult();
        var projection = RunProjectionFactory.Create(result);

        var json = CliOutputWriter.FormatDryRunJson(projection, Array.Empty<DedupeGroup>());
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(Have, root.GetProperty("DatHaveCount").GetInt32());
        Assert.Equal(HaveWrongName, root.GetProperty("DatHaveWrongNameCount").GetInt32());
        Assert.Equal(Miss, root.GetProperty("DatMissCount").GetInt32());
        Assert.Equal(Unknown, root.GetProperty("DatUnknownCount").GetInt32());
        Assert.Equal(Ambiguous, root.GetProperty("DatAmbiguousCount").GetInt32());
    }

    [Fact]
    public void CliDryRunJson_DatAuditFields_ExistInOutput()
    {
        var result = CreateRunResult();
        var projection = RunProjectionFactory.Create(result);

        var json = CliOutputWriter.FormatDryRunJson(projection, Array.Empty<DedupeGroup>());
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("DatHaveCount", out _));
        Assert.True(root.TryGetProperty("DatHaveWrongNameCount", out _));
        Assert.True(root.TryGetProperty("DatMissCount", out _));
        Assert.True(root.TryGetProperty("DatUnknownCount", out _));
        Assert.True(root.TryGetProperty("DatAmbiguousCount", out _));
    }

    [Fact]
    public void CliDryRunJson_Zero_DatAuditCounters_SerializedAsZero()
    {
        var result = new RunResult();
        var projection = RunProjectionFactory.Create(result);

        var json = CliOutputWriter.FormatDryRunJson(projection, Array.Empty<DedupeGroup>());
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(0, root.GetProperty("DatHaveCount").GetInt32());
        Assert.Equal(0, root.GetProperty("DatHaveWrongNameCount").GetInt32());
        Assert.Equal(0, root.GetProperty("DatMissCount").GetInt32());
        Assert.Equal(0, root.GetProperty("DatUnknownCount").GetInt32());
        Assert.Equal(0, root.GetProperty("DatAmbiguousCount").GetInt32());
    }

    // ── API tests ─────────────────────────────────────────────────

    [Fact]
    public void ApiRunResult_ContainsDatAuditCounters_WithCorrectValues()
    {
        var result = CreateRunResult();
        var projection = RunProjectionFactory.Create(result);
        var api = ApiRunResultMapper.Map(result, projection);

        Assert.Equal(Have, api.DatHaveCount);
        Assert.Equal(HaveWrongName, api.DatHaveWrongNameCount);
        Assert.Equal(Miss, api.DatMissCount);
        Assert.Equal(Unknown, api.DatUnknownCount);
        Assert.Equal(Ambiguous, api.DatAmbiguousCount);
    }

    [Fact]
    public void ApiRunResult_SerializesToJson_WithDatAuditFields()
    {
        var result = CreateRunResult();
        var projection = RunProjectionFactory.Create(result);
        var api = ApiRunResultMapper.Map(result, projection);

        var json = JsonSerializer.Serialize(api, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(Have, root.GetProperty("datHaveCount").GetInt32());
        Assert.Equal(HaveWrongName, root.GetProperty("datHaveWrongNameCount").GetInt32());
        Assert.Equal(Miss, root.GetProperty("datMissCount").GetInt32());
        Assert.Equal(Unknown, root.GetProperty("datUnknownCount").GetInt32());
        Assert.Equal(Ambiguous, root.GetProperty("datAmbiguousCount").GetInt32());
    }

    [Fact]
    public void OpenApiSpec_DatAuditCounterTypes_AllInteger()
    {
        using var spec = JsonDocument.Parse(OpenApiSpec.Json);
        var props = spec.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("ApiRunResult")
            .GetProperty("properties");

        foreach (var field in new[] { "datHaveCount", "datHaveWrongNameCount", "datMissCount", "datUnknownCount", "datAmbiguousCount" })
        {
            Assert.True(props.TryGetProperty(field, out var prop), $"Missing property: {field}");
            Assert.Equal("integer", prop.GetProperty("type").GetString());
        }
    }

    // ── Report tests ──────────────────────────────────────────────

    [Fact]
    public void ReportSummary_ContainsDatAuditCounters_WithCorrectValues()
    {
        var result = CreateRunResult();
        var summary = RunReportWriter.BuildSummary(result, "DryRun");

        Assert.Equal(Have, summary.DatHaveCount);
        Assert.Equal(HaveWrongName, summary.DatHaveWrongNameCount);
        Assert.Equal(Miss, summary.DatMissCount);
        Assert.Equal(Unknown, summary.DatUnknownCount);
        Assert.Equal(Ambiguous, summary.DatAmbiguousCount);
    }

    [Fact]
    public void HtmlReport_RendersDatAuditCounterValues()
    {
        var summary = new ReportSummary
        {
            DatHaveCount = Have,
            DatHaveWrongNameCount = HaveWrongName,
            DatMissCount = Miss,
            DatUnknownCount = Unknown,
            DatAmbiguousCount = Ambiguous
        };

        var html = ReportGenerator.GenerateHtml(summary, Array.Empty<ReportEntry>());

        // Verify labels present
        Assert.Contains("DAT Have", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DAT WrongName", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DAT Miss", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DAT Unknown", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DAT Ambiguous", html, StringComparison.OrdinalIgnoreCase);

        // Verify actual counter values rendered (they should appear near the labels)
        Assert.Contains(Have.ToString(), html);
        Assert.Contains(HaveWrongName.ToString(), html);
        Assert.Contains(Miss.ToString(), html);
    }

    [Fact]
    public void HtmlReport_ZeroCounters_OmitsDatAuditCards()
    {
        var summary = new ReportSummary
        {
            DatHaveCount = 0,
            DatHaveWrongNameCount = 0,
            DatMissCount = 0,
            DatUnknownCount = 0,
            DatAmbiguousCount = 0
        };

        var html = ReportGenerator.GenerateHtml(summary, Array.Empty<ReportEntry>());

        // DAT audit cards are only rendered when count > 0
        Assert.DoesNotContain("DAT Have", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DAT Miss", html, StringComparison.OrdinalIgnoreCase);
    }

    // ── Cross-channel parity: same input → same numbers ──────────

    [Fact]
    public void CrossChannel_AllThreeOutputs_ShowIdenticalDatAuditNumbers()
    {
        var result = CreateRunResult();
        var projection = RunProjectionFactory.Create(result);

        // CLI
        var cliJson = CliOutputWriter.FormatDryRunJson(projection, Array.Empty<DedupeGroup>());
        using var cliDoc = JsonDocument.Parse(cliJson);
        var cli = cliDoc.RootElement;

        // API
        var api = ApiRunResultMapper.Map(result, projection);

        // Report
        var summary = RunReportWriter.BuildSummary(result, "DryRun");

        // Assert all three show identical numbers
        Assert.Equal(cli.GetProperty("DatHaveCount").GetInt32(), api.DatHaveCount);
        Assert.Equal(api.DatHaveCount, summary.DatHaveCount);

        Assert.Equal(cli.GetProperty("DatHaveWrongNameCount").GetInt32(), api.DatHaveWrongNameCount);
        Assert.Equal(api.DatHaveWrongNameCount, summary.DatHaveWrongNameCount);

        Assert.Equal(cli.GetProperty("DatMissCount").GetInt32(), api.DatMissCount);
        Assert.Equal(api.DatMissCount, summary.DatMissCount);

        Assert.Equal(cli.GetProperty("DatUnknownCount").GetInt32(), api.DatUnknownCount);
        Assert.Equal(api.DatUnknownCount, summary.DatUnknownCount);

        Assert.Equal(cli.GetProperty("DatAmbiguousCount").GetInt32(), api.DatAmbiguousCount);
        Assert.Equal(api.DatAmbiguousCount, summary.DatAmbiguousCount);
    }
}
