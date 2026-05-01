using Romulus.Api;
using Romulus.CLI;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Reporting;
using Romulus.UI.Wpf.Models;
using System.Text.Json;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Verifies that ConversionReport metrics flow consistently through
/// RunResult → RunProjection → CLI / API / GUI / Report channels.
/// </summary>
public sealed class ConversionReportParityTests
{
    private static RunResult BuildResultWithConversionReport()
    {
        var results = new List<ConversionResult>
        {
            new("C:\\roms\\game1.iso", "C:\\roms\\game1.chd", ConversionOutcome.Success)
            {
                Safety = ConversionSafety.Safe,
                VerificationResult = VerificationStatus.Verified,
                DurationMs = 500
            },
            new("C:\\roms\\game2.cue", null, ConversionOutcome.Blocked, "No tool available")
            {
                Safety = ConversionSafety.Blocked
            },
            new("C:\\roms\\game3.gdi", "C:\\roms\\game3.chd", ConversionOutcome.Success)
            {
                Safety = ConversionSafety.Risky,
                VerificationResult = VerificationStatus.Verified,
                DurationMs = 300
            },
            new("C:\\roms\\game4.bin", null, ConversionOutcome.Error, "Tool crashed")
            {
                Safety = ConversionSafety.Acceptable
            },
            new("C:\\roms\\game5.zip", "C:\\roms\\game5.zip", ConversionOutcome.Skipped, "Already target format")
            {
                Safety = ConversionSafety.Safe
            }
        };

        var report = new ConversionReport
        {
            TotalPlanned = 5,
            Converted = 2,
            Skipped = 1,
            Errors = 1,
            Blocked = 1,
            RequiresReview = 1,
            TotalSavedBytes = 1024,
            Results = results
        };

        var candidates = Enumerable.Range(1, 5).Select(i => new RomCandidate
        {
            MainPath = $"C:\\roms\\game{i}.iso",
            GameKey = $"game{i}",
            Region = "US",
            RegionScore = 100,
            FormatScore = 100,
            VersionScore = 100,
            Extension = ".iso",
            ConsoleKey = "PSX",
            Category = FileCategory.Game,
            ClassificationReasonCode = "test",
            ClassificationConfidence = 100
        }).ToArray();

        return new RunResult
        {
            Status = "ok",
            ExitCode = 0,
            TotalFilesScanned = 5,
            ConvertedCount = 2,
            ConvertErrorCount = 1,
            ConvertSkippedCount = 1,
            ConvertBlockedCount = 1,
            ConvertReviewCount = 1,
            ConvertSavedBytes = 1024,
            ConversionReport = report,
            AllCandidates = candidates,
            DedupeGroups = Array.Empty<DedupeGroup>()
        };
    }

    private static RunResult BuildResultWithWinnerConversion()
    {
        var winner = new RomCandidate
        {
            MainPath = @"C:\roms\game1.zip",
            GameKey = "game1",
            Region = "US",
            RegionScore = 100,
            FormatScore = 500,
            VersionScore = 100,
            Extension = ".zip",
            ConsoleKey = "PSX",
            Category = FileCategory.Game,
            ClassificationReasonCode = "test",
            ClassificationConfidence = 100
        };

        var loser = winner with
        {
            MainPath = @"C:\roms\game1_eu.zip",
            Region = "EU",
            GameKey = "game1-eu"
        };

        return new RunResult
        {
            Status = "ok",
            ExitCode = 0,
            TotalFilesScanned = 2,
            WinnerCount = 1,
            LoserCount = 1,
            ConvertedCount = 1,
            ConversionReport = new ConversionReport
            {
                TotalPlanned = 1,
                Converted = 1,
                Skipped = 0,
                Errors = 0,
                Blocked = 0,
                RequiresReview = 0,
                TotalSavedBytes = 0,
                Results =
                [
                    new ConversionResult(winner.MainPath, @"C:\roms\game1.chd", ConversionOutcome.Success)
                ]
            },
            AllCandidates = [winner, loser],
            DedupeGroups =
            [
                new DedupeGroup
                {
                    GameKey = "game1",
                    Winner = winner,
                    Losers = [loser]
                }
            ]
        };
    }

    [Fact]
    public void RunProjection_CarriesConversionMetrics()
    {
        var result = BuildResultWithConversionReport();
        var projection = RunProjectionFactory.Create(result);

        Assert.Equal(2, projection.ConvertedCount);
        Assert.Equal(1, projection.ConvertErrorCount);
        Assert.Equal(1, projection.ConvertSkippedCount);
        Assert.Equal(1, projection.ConvertBlockedCount);
        Assert.Equal(1, projection.ConvertReviewCount);
        Assert.Equal(1024, projection.ConvertSavedBytes);
    }

    [Fact]
    public void CliDryRunJson_ContainsConversionPlansAndBlocked()
    {
        var result = BuildResultWithConversionReport();
        var projection = RunProjectionFactory.Create(result);

        var json = CliOutputWriter.FormatDryRunJson(projection, result.DedupeGroups, result.ConversionReport);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Aggregate metrics
        Assert.Equal(2, root.GetProperty("ConvertedCount").GetInt32());
        Assert.Equal(1, root.GetProperty("ConvertBlockedCount").GetInt32());
        Assert.Equal(1, root.GetProperty("ConvertReviewCount").GetInt32());
        Assert.Equal(1024, root.GetProperty("ConvertSavedBytes").GetInt64());

        // ConversionPlans array (Success + Skipped)
        var plans = root.GetProperty("ConversionPlans");
        Assert.Equal(JsonValueKind.Array, plans.ValueKind);
        Assert.Equal(3, plans.GetArrayLength()); // 2 Success + 1 Skipped

        // ConversionBlocked array (Blocked + Error)
        var blocked = root.GetProperty("ConversionBlocked");
        Assert.Equal(JsonValueKind.Array, blocked.ValueKind);
        Assert.Equal(2, blocked.GetArrayLength()); // 1 Blocked + 1 Error

        // Verify structure of first plan
        var firstPlan = plans[0];
        Assert.Equal("C:\\roms\\game1.iso", firstPlan.GetProperty("SourcePath").GetString());
        Assert.Equal(".chd", firstPlan.GetProperty("TargetExtension").GetString());
        Assert.Equal("Safe", firstPlan.GetProperty("Safety").GetString());
        Assert.Equal("Success", firstPlan.GetProperty("Outcome").GetString());

        // Verify structure of first blocked
        var firstBlocked = blocked[0];
        Assert.Equal("C:\\roms\\game2.cue", firstBlocked.GetProperty("SourcePath").GetString());
        Assert.Equal("No tool available", firstBlocked.GetProperty("Reason").GetString());
        Assert.Equal("Blocked", firstBlocked.GetProperty("Safety").GetString());
    }

    [Fact]
    public void ApiRunResult_ContainsConversionPlansAndBlocked()
    {
        var result = BuildResultWithConversionReport();
        var projection = RunProjectionFactory.Create(result);

        var apiResult = ApiRunResultMapper.Map(result, projection);

        Assert.Equal(2, apiResult.ConvertedCount);
        Assert.Equal(1, apiResult.ConvertBlockedCount);
        Assert.Equal(1, apiResult.ConvertReviewCount);
        Assert.Equal(1024, apiResult.ConvertSavedBytes);
        Assert.Equal(3, apiResult.ConversionPlans.Length); // 2 Success + 1 Skipped
        Assert.Equal(2, apiResult.ConversionBlocked.Length); // 1 Blocked + 1 Error
    }

    [Fact]
    public void DashboardProjection_ShowsConversionBreakdown()
    {
        var result = BuildResultWithConversionReport();
        var projection = RunProjectionFactory.Create(result);

        var dashboard = DashboardProjection.From(projection, result, isConvertOnlyRun: false);

        Assert.Equal("2", dashboard.ConvertedDisplay);
        Assert.Equal("1", dashboard.ConvertBlockedDisplay);
        Assert.Equal("1", dashboard.ConvertReviewDisplay);
        Assert.NotEqual("–", dashboard.ConvertSavedBytesDisplay);
    }

    [Fact]
    public void ApiRunResult_ProjectsConvertedWinnerPath_InDedupeGroups()
    {
        var result = BuildResultWithWinnerConversion();
        var projection = RunProjectionFactory.Create(result);

        var apiResult = ApiRunResultMapper.Map(result, projection);

        Assert.Single(apiResult.DedupeGroups);
        Assert.Equal(@"C:\roms\game1.chd", apiResult.DedupeGroups[0].Winner.MainPath);
    }

    [Fact]
    public void DashboardProjection_UsesProjectedConvertedWinnerFileName()
    {
        var result = BuildResultWithWinnerConversion();
        var projection = RunProjectionFactory.Create(result);

        var dashboard = DashboardProjection.From(projection, result, isConvertOnlyRun: false);

        Assert.Single(dashboard.DedupeGroups);
        Assert.Equal("game1.chd", dashboard.DedupeGroups[0].Winner.FileName);
    }

    [Fact]
    public void DashboardProjection_NoConversion_ShowsDash()
    {
        var result = new RunResult
        {
            Status = "ok",
            TotalFilesScanned = 3,
            AllCandidates = Array.Empty<RomCandidate>(),
            DedupeGroups = Array.Empty<DedupeGroup>()
        };
        var projection = RunProjectionFactory.Create(result);

        var dashboard = DashboardProjection.From(projection, result, isConvertOnlyRun: false);

        Assert.Equal("–", dashboard.ConvertedDisplay);
        Assert.Equal("–", dashboard.ConvertBlockedDisplay);
        Assert.Equal("–", dashboard.ConvertReviewDisplay);
        Assert.Equal("–", dashboard.ConvertSavedBytesDisplay);
    }

    [Fact]
    public void ReportSummary_IncludesConversionReviewAndSavedBytes()
    {
        var result = BuildResultWithConversionReport();
        var summary = RunReportWriter.BuildSummary(result, "DryRun");

        Assert.Equal(2, summary.ConvertedCount);
        Assert.Equal(1, summary.ConvertErrorCount);
        Assert.Equal(1, summary.ConvertSkippedCount);
        Assert.Equal(1, summary.ConvertBlockedCount);
        Assert.Equal(1, summary.ConvertReviewCount);
        Assert.Equal(1024, summary.ConvertSavedBytes);
    }

    [Fact]
    public void CliDryRunJson_NullConversionReport_EmptyArrays()
    {
        var result = new RunResult
        {
            Status = "ok",
            TotalFilesScanned = 1,
            AllCandidates = Array.Empty<RomCandidate>(),
            DedupeGroups = Array.Empty<DedupeGroup>()
        };
        var projection = RunProjectionFactory.Create(result);

        var json = CliOutputWriter.FormatDryRunJson(projection, result.DedupeGroups, null);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(0, doc.RootElement.GetProperty("ConversionPlans").GetArrayLength());
        Assert.Equal(0, doc.RootElement.GetProperty("ConversionBlocked").GetArrayLength());
    }

    [Fact]
    public void CliDryRunJson_IncludesPreflightWarnings()
    {
        var result = BuildResultWithConversionReport();
        var projection = RunProjectionFactory.Create(result);
        var warnings = new[]
        {
            "ConvertOnly is enabled but conversion will be skipped in DryRun mode. Use Mode=Move to apply."
        };

        var json = CliOutputWriter.FormatDryRunJson(projection, result.DedupeGroups, result.ConversionReport, warnings);
        using var doc = JsonDocument.Parse(json);
        var preflightWarnings = doc.RootElement.GetProperty("PreflightWarnings");

        Assert.Equal(JsonValueKind.Array, preflightWarnings.ValueKind);
        Assert.Single(preflightWarnings.EnumerateArray());
        Assert.Contains(preflightWarnings.EnumerateArray().Select(e => e.GetString()), w =>
            string.Equals(w, warnings[0], StringComparison.Ordinal));
    }

    [Fact]
    public async Task OpenApiSchema_ContainsConversionPlanAndBlockedSchemas()
    {
        using var spec = JsonDocument.Parse(await OpenApiTestHelper.FetchOpenApiJsonAsync());
        var schemas = spec.RootElement
            .GetProperty("components")
            .GetProperty("schemas");

        Assert.True(schemas.TryGetProperty("ApiConversionPlan", out _), "Missing ApiConversionPlan schema");
        Assert.True(schemas.TryGetProperty("ApiConversionBlocked", out _), "Missing ApiConversionBlocked schema");

        var resultProps = schemas
            .GetProperty("ApiRunResult")
            .GetProperty("properties");
        Assert.True(resultProps.TryGetProperty("conversionPlans", out _), "Missing conversionPlans in ApiRunResult");
        Assert.True(resultProps.TryGetProperty("conversionBlocked", out _), "Missing conversionBlocked in ApiRunResult");
    }
}
