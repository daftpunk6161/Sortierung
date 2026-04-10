using Romulus.Contracts.Models;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Tests for RunOrchestrator static helper methods: ResolveRunOutcome, ApplyConversionReport, ResolveTotalTargetBytes.
/// </summary>
public sealed class RunOrchestratorHelperTests
{
    // ═══ ResolveRunOutcome ════════════════════════════════════════════

    [Fact]
    public void ResolveRunOutcome_AllZero_ReturnsOk()
    {
        var builder = new RunResultBuilder();

        var outcome = RunOrchestrator.ResolveRunOutcome(builder);

        Assert.Equal(RunOutcome.Ok, outcome);
    }

    [Fact]
    public void ResolveRunOutcome_ConvertErrorCount_ReturnsCompletedWithErrors()
    {
        var builder = new RunResultBuilder { ConvertErrorCount = 1 };

        Assert.Equal(RunOutcome.CompletedWithErrors, RunOrchestrator.ResolveRunOutcome(builder));
    }

    [Fact]
    public void ResolveRunOutcome_ConvertVerifyFailed_ReturnsCompletedWithErrors()
    {
        var builder = new RunResultBuilder { ConvertVerifyFailedCount = 3 };

        Assert.Equal(RunOutcome.CompletedWithErrors, RunOrchestrator.ResolveRunOutcome(builder));
    }

    [Fact]
    public void ResolveRunOutcome_MoveResultFailed_ReturnsCompletedWithErrors()
    {
        var builder = new RunResultBuilder
        {
            MoveResult = new MovePhaseResult(MoveCount: 10, FailCount: 1, SavedBytes: 0)
        };

        Assert.Equal(RunOutcome.CompletedWithErrors, RunOrchestrator.ResolveRunOutcome(builder));
    }

    [Fact]
    public void ResolveRunOutcome_JunkMoveFailed_ReturnsCompletedWithErrors()
    {
        var builder = new RunResultBuilder
        {
            JunkMoveResult = new MovePhaseResult(MoveCount: 5, FailCount: 2, SavedBytes: 0)
        };

        Assert.Equal(RunOutcome.CompletedWithErrors, RunOrchestrator.ResolveRunOutcome(builder));
    }

    [Fact]
    public void ResolveRunOutcome_DatRenameFailed_ReturnsCompletedWithErrors()
    {
        var builder = new RunResultBuilder { DatRenameFailedCount = 1 };

        Assert.Equal(RunOutcome.CompletedWithErrors, RunOrchestrator.ResolveRunOutcome(builder));
    }

    [Fact]
    public void ResolveRunOutcome_ConsoleSortFailed_ReturnsCompletedWithErrors()
    {
        var builder = new RunResultBuilder
        {
            ConsoleSortResult = new ConsoleSortResult(
                Total: 10, Moved: 8, SetMembersMoved: 0, Skipped: 0,
                Unknown: 0, UnknownReasons: new Dictionary<string, int>(), Failed: 2)
        };

        Assert.Equal(RunOutcome.CompletedWithErrors, RunOrchestrator.ResolveRunOutcome(builder));
    }

    [Fact]
    public void ResolveRunOutcome_MoveSuccessNoErrors_ReturnsOk()
    {
        var builder = new RunResultBuilder
        {
            MoveResult = new MovePhaseResult(MoveCount: 50, FailCount: 0, SavedBytes: 1000)
        };

        Assert.Equal(RunOutcome.Ok, RunOrchestrator.ResolveRunOutcome(builder));
    }

    // ═══ ApplyConversionReport ════════════════════════════════════════

    [Fact]
    public void ApplyConversionReport_EmptyResults_SetsZeroMetrics()
    {
        var builder = new RunResultBuilder();
        var results = Array.Empty<ConversionResult>().AsReadOnly();

        RunOrchestrator.ApplyConversionReport(results, builder);

        Assert.Equal(0, builder.ConvertReviewCount);
        Assert.Equal(0, builder.ConvertLossyWarningCount);
        Assert.Equal(0, builder.ConvertVerifyPassedCount);
        Assert.Equal(0, builder.ConvertVerifyFailedCount);
        Assert.Equal(0L, builder.ConvertSavedBytes);
        Assert.NotNull(builder.ConversionReport);
        Assert.Equal(0, builder.ConversionReport!.TotalPlanned);
    }

    [Fact]
    public void ApplyConversionReport_CountsReviewAndLossyResults()
    {
        var results = new List<ConversionResult>
        {
            new("a.iso", "a.chd", ConversionOutcome.Success) { Safety = ConversionSafety.Risky },
            new("b.iso", "b.chd", ConversionOutcome.Success) { SourceIntegrity = SourceIntegrity.Lossy },
            new("c.iso", "c.chd", ConversionOutcome.Success) { VerificationResult = VerificationStatus.Verified },
            new("d.iso", "d.chd", ConversionOutcome.Error) { VerificationResult = VerificationStatus.VerifyFailed },
        }.AsReadOnly();

        var builder = new RunResultBuilder();
        RunOrchestrator.ApplyConversionReport(results, builder);

        Assert.Equal(2, builder.ConvertReviewCount); // Risky + Lossy
        Assert.Equal(1, builder.ConvertLossyWarningCount);
        Assert.Equal(1, builder.ConvertVerifyPassedCount);
        Assert.Equal(1, builder.ConvertVerifyFailedCount);
    }

    [Fact]
    public void ApplyConversionReport_CalculatesSavedBytes_FromSourceAndTargetBytes()
    {
        var results = new List<ConversionResult>
        {
            new("a.iso", "a.chd", ConversionOutcome.Success)
            {
                SourceBytes = 1000,
                TargetBytes = 400
            },
        }.AsReadOnly();

        var builder = new RunResultBuilder();
        RunOrchestrator.ApplyConversionReport(results, builder);

        Assert.Equal(600, builder.ConvertSavedBytes);
    }

    [Fact]
    public void ApplyConversionReport_PopulatesConversionReport()
    {
        var results = new List<ConversionResult>
        {
            new("a.iso", "a.chd", ConversionOutcome.Success),
            new("b.iso", "b.chd", ConversionOutcome.Error),
        }.AsReadOnly();

        var builder = new RunResultBuilder { ConvertedCount = 1, ConvertErrorCount = 1 };
        RunOrchestrator.ApplyConversionReport(results, builder);

        Assert.NotNull(builder.ConversionReport);
        Assert.Equal(2, builder.ConversionReport!.TotalPlanned);
        Assert.Equal(1, builder.ConversionReport.Converted);
        Assert.Equal(1, builder.ConversionReport.Errors);
        Assert.Same(results, builder.ConversionReport.Results);
    }

    // ═══ ResolveTotalTargetBytes ══════════════════════════════════════

    [Fact]
    public void ResolveTotalTargetBytes_WithTargetBytes_ReturnsValue()
    {
        var result = new ConversionResult("a.iso", "a.chd", ConversionOutcome.Success)
        {
            TargetBytes = 500
        };

        Assert.Equal(500, RunOrchestrator.ResolveTotalTargetBytes(result));
    }

    [Fact]
    public void ResolveTotalTargetBytes_NoBytesNoFile_ReturnsNull()
    {
        var result = new ConversionResult("a.iso", null, ConversionOutcome.Success);

        Assert.Null(RunOrchestrator.ResolveTotalTargetBytes(result));
    }

    [Fact]
    public void ResolveTotalTargetBytes_NoTargetPathNoBytes_ReturnsNull()
    {
        var result = new ConversionResult("a.iso", "", ConversionOutcome.Success);

        Assert.Null(RunOrchestrator.ResolveTotalTargetBytes(result));
    }

    [Fact]
    public void ResolveTotalTargetBytes_TargetBytesWithAdditionalPaths_SumsAll()
    {
        var result = new ConversionResult("a.iso", "a.chd", ConversionOutcome.Success)
        {
            TargetBytes = 300,
            AdditionalTargetPaths = [] // no extra files
        };

        // Only TargetBytes counted since additional paths don't exist on disk
        Assert.Equal(300, RunOrchestrator.ResolveTotalTargetBytes(result));
    }
}
