using System.Text.Json;
using Romulus.Api;
using Romulus.CLI;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests.Conversion;

/// <summary>
/// Integration tests verifying ConvertReviewCount and ConvertSavedBytes
/// flow through the full projection chain: RunResult → RunProjection → CLI/API.
/// </summary>
public sealed class ConversionMetricsPipelineTests
{
    private static RunResult MakeRunResult(int reviewCount = 7, long savedBytes = 123456)
    {
        return new RunResult
        {
            Status = "ok",
            TotalFilesScanned = 10,
            ConvertedCount = 5,
            ConvertErrorCount = 1,
            ConvertSkippedCount = 2,
            ConvertBlockedCount = 3,
            ConvertReviewCount = reviewCount,
            ConvertSavedBytes = savedBytes,
            AllCandidates = Array.Empty<RomCandidate>(),
            DedupeGroups = Array.Empty<DedupeGroup>()
        };
    }

    // -----------------------------------------------------------------------
    // RunResultBuilder → RunResult
    // -----------------------------------------------------------------------

    [Fact]
    public void RunResultBuilder_ConvertReviewCountAndSavedBytes_IncludedInBuild()
    {
        var builder = new RunResultBuilder
        {
            ConvertReviewCount = 12,
            ConvertSavedBytes = 4096
        };

        var result = builder.Build();

        Assert.Equal(12, result.ConvertReviewCount);
        Assert.Equal(4096, result.ConvertSavedBytes);
    }

    // -----------------------------------------------------------------------
    // RunResult → RunProjection
    // -----------------------------------------------------------------------

    [Fact]
    public void RunProjectionFactory_ConvertReviewCountAndSavedBytes_ProjectedCorrectly()
    {
        var result = MakeRunResult(reviewCount: 7, savedBytes: 123456);

        var projection = RunProjectionFactory.Create(result);

        Assert.Equal(7, projection.ConvertReviewCount);
        Assert.Equal(123456, projection.ConvertSavedBytes);
    }

    [Fact]
    public void RunProjectionFactory_ZeroValues_ProjectedAsZero()
    {
        var result = MakeRunResult(reviewCount: 0, savedBytes: 0);

        var projection = RunProjectionFactory.Create(result);

        Assert.Equal(0, projection.ConvertReviewCount);
        Assert.Equal(0, projection.ConvertSavedBytes);
    }

    // -----------------------------------------------------------------------
    // RunProjection → CLI DryRun JSON
    // -----------------------------------------------------------------------

    [Fact]
    public void CliDryRunJson_ConvertReviewCountAndSavedBytes_InOutput()
    {
        var result = MakeRunResult(reviewCount: 3, savedBytes: 99999);
        var projection = RunProjectionFactory.Create(result);

        var json = CliOutputWriter.FormatDryRunJson(projection, Array.Empty<DedupeGroup>());
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(3, root.GetProperty("ConvertReviewCount").GetInt32());
        Assert.Equal(99999, root.GetProperty("ConvertSavedBytes").GetInt64());
    }

    // -----------------------------------------------------------------------
    // RunProjection → API Response
    // -----------------------------------------------------------------------

    [Fact]
    public void ApiRunResultMapper_ConvertReviewCountAndSavedBytes_Mapped()
    {
        var result = MakeRunResult(reviewCount: 5, savedBytes: 500000);
        var projection = RunProjectionFactory.Create(result);

        var apiResult = ApiRunResultMapper.Map(result, projection);

        Assert.Equal(5, apiResult.ConvertReviewCount);
        Assert.Equal(500000, apiResult.ConvertSavedBytes);
    }

    // -----------------------------------------------------------------------
    // Full chain: Builder → RunResult → Projection → CLI + API
    // -----------------------------------------------------------------------

    [Fact]
    public void FullChain_ConvertMetrics_ConsistentAcrossAllChannels()
    {
        const int expectedReview = 11;
        const long expectedSavedBytes = 987654321;

        // 1) Build RunResult via builder
        var builder = new RunResultBuilder
        {
            ConvertReviewCount = expectedReview,
            ConvertSavedBytes = expectedSavedBytes,
            AllCandidates = Array.Empty<RomCandidate>(),
            DedupeGroups = Array.Empty<DedupeGroup>()
        };
        var result = builder.Build();

        // 2) Project
        var projection = RunProjectionFactory.Create(result);

        // 3) CLI
        var json = CliOutputWriter.FormatDryRunJson(projection, Array.Empty<DedupeGroup>());
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // 4) API
        var apiResult = ApiRunResultMapper.Map(result, projection);

        // Assert consistency across all channels
        Assert.Equal(expectedReview, projection.ConvertReviewCount);
        Assert.Equal(expectedSavedBytes, projection.ConvertSavedBytes);
        Assert.Equal(expectedReview, root.GetProperty("ConvertReviewCount").GetInt32());
        Assert.Equal(expectedSavedBytes, root.GetProperty("ConvertSavedBytes").GetInt64());
        Assert.Equal(expectedReview, apiResult.ConvertReviewCount);
        Assert.Equal(expectedSavedBytes, apiResult.ConvertSavedBytes);
    }

    [Fact]
    public void RunResultBuilder_VerifyAndLossyCounters_AreProjectedAndMapped()
    {
        var builder = new RunResultBuilder
        {
            ConvertLossyWarningCount = 2,
            ConvertVerifyPassedCount = 4,
            ConvertVerifyFailedCount = 1
        };

        var result = builder.Build();
        var projection = RunProjectionFactory.Create(result);
        var apiResult = ApiRunResultMapper.Map(result, projection);

        Assert.Equal(2, projection.ConvertLossyWarningCount);
        Assert.Equal(4, projection.ConvertVerifyPassedCount);
        Assert.Equal(1, projection.ConvertVerifyFailedCount);
        Assert.Equal(2, apiResult.ConvertLossyWarningCount);
        Assert.Equal(4, apiResult.ConvertVerifyPassedCount);
        Assert.Equal(1, apiResult.ConvertVerifyFailedCount);
    }

    [Fact]
    public void ConversionReport_UsesByteSnapshots_WhenSourceFileNoLongerExists()
    {
        var conversionResults = new[]
        {
            new ConversionResult(
                SourcePath: "C:/missing/source.iso",
                TargetPath: "C:/roms/game.chd",
                Outcome: ConversionOutcome.Success,
                Reason: null,
                ExitCode: 0)
            {
                SourceBytes = 10_000,
                TargetBytes = 7_000,
                SourceIntegrity = SourceIntegrity.Lossless,
                Safety = ConversionSafety.Safe,
                VerificationResult = VerificationStatus.Verified
            }
        };

        var report = new ConversionReport
        {
            TotalPlanned = conversionResults.Length,
            Converted = 1,
            Skipped = 0,
            Errors = 0,
            Blocked = 0,
            RequiresReview = 0,
            TotalSavedBytes = conversionResults.Sum(r => (r.SourceBytes ?? 0) - (r.TargetBytes ?? 0)),
            Results = conversionResults
        };

        var runResult = new RunResult
        {
            ConvertedCount = 1,
            ConvertSkippedCount = 0,
            ConvertErrorCount = 0,
            ConvertBlockedCount = 0,
            ConvertReviewCount = 0,
            ConvertSavedBytes = report.TotalSavedBytes,
            ConvertLossyWarningCount = conversionResults.Count(r => r.SourceIntegrity == SourceIntegrity.Lossy),
            ConvertVerifyPassedCount = conversionResults.Count(r => r.VerificationResult == VerificationStatus.Verified),
            ConvertVerifyFailedCount = conversionResults.Count(r => r.VerificationResult == VerificationStatus.VerifyFailed),
            ConversionReport = report,
            AllCandidates = Array.Empty<RomCandidate>(),
            DedupeGroups = Array.Empty<DedupeGroup>()
        };

        var projection = RunProjectionFactory.Create(runResult);

        Assert.Equal(3000, projection.ConvertSavedBytes);
        Assert.Equal(0, projection.ConvertLossyWarningCount);
        Assert.Equal(1, projection.ConvertVerifyPassedCount);
        Assert.Equal(0, projection.ConvertVerifyFailedCount);
    }
}
