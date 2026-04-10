using Romulus.Contracts.Models;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Tests that RunResultBuilder.Build() produces a faithful pass-through of builder state.
/// </summary>
public sealed class RunResultBuilderInvariantTests
{
    [Fact]
    public void Build_PassesThroughConvertErrorCount_RegardlessOfReportContent()
    {
        // Pipeline counters (ConvertErrorCount) include post-verify failures
        // that are NOT reflected in ConversionResult.Outcome — pipeline is authoritative.
        var builder = new RunResultBuilder
        {
            ConvertErrorCount = 1,
            ConversionReport = new ConversionReport
            {
                TotalPlanned = 1,
                Converted = 0,
                Skipped = 0,
                Errors = 1,
                Blocked = 0,
                RequiresReview = 0,
                TotalSavedBytes = 0,
                Results = new[]
                {
                    // Original outcome is Success, but verify failed post-hoc → counted as error by pipeline
                    new ConversionResult("a.iso", "a.chd", ConversionOutcome.Success)
                }
            }
        };

        var result = builder.Build();

        Assert.Equal(1, result.ConvertErrorCount);
    }

    [Fact]
    public void Build_KeepsConvertErrorCount_WhenNoReport()
    {
        var builder = new RunResultBuilder
        {
            ConvertErrorCount = 5
        };

        var result = builder.Build();

        Assert.Equal(5, result.ConvertErrorCount);
    }

    [Fact]
    public void Build_PropagatesAllMetricFields()
    {
        var builder = new RunResultBuilder
        {
            ConvertedCount = 3,
            ConvertErrorCount = 2,
            ConvertSkippedCount = 1,
            ConvertBlockedCount = 4,
            DatRenameFailedCount = 7,
            JunkRemovedCount = 5
        };

        var result = builder.Build();

        Assert.Equal(3, result.ConvertedCount);
        Assert.Equal(2, result.ConvertErrorCount);
        Assert.Equal(1, result.ConvertSkippedCount);
        Assert.Equal(4, result.ConvertBlockedCount);
        Assert.Equal(7, result.DatRenameFailedCount);
        Assert.Equal(5, result.JunkRemovedCount);
    }
}
