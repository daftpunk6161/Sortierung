using Romulus.Contracts.Models;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Tests that DatRename KPIs propagate correctly through
/// PipelineState → RunResult → RunProjection → CLI/API/Report.
/// </summary>
public sealed class DatRenameKpiPropagationTests
{
    [Fact]
    public void PipelineState_SetDatRenameOutput_StoresResult()
    {
        var state = new PipelineState();
        var renameResult = new DatRenameResult(
            Proposals: Array.Empty<DatRenameProposal>(),
            PathMutations: Array.Empty<PathMutation>(),
            ProposedCount: 5,
            ExecutedCount: 3,
            SkippedCount: 1,
            FailedCount: 1);

        state.SetDatRenameOutput(renameResult);

        Assert.Same(renameResult, state.DatRenameResult);
    }

    [Fact]
    public void PipelineState_SetDatRenameOutput_ThrowsOnDoubleAssign()
    {
        var state = new PipelineState();
        var renameResult = new DatRenameResult(
            Array.Empty<DatRenameProposal>(),
            Array.Empty<PathMutation>(),
            0,
            0,
            0,
            0);
        state.SetDatRenameOutput(renameResult);

        Assert.Throws<InvalidOperationException>(() => state.SetDatRenameOutput(renameResult));
    }

    [Fact]
    public void RunResult_ContainsDatRenameKpis()
    {
        var result = new RunResult
        {
            DatRenameProposedCount = 10,
            DatRenameExecutedCount = 7,
            DatRenameSkippedCount = 2,
            DatRenameFailedCount = 1
        };

        Assert.Equal(10, result.DatRenameProposedCount);
        Assert.Equal(7, result.DatRenameExecutedCount);
        Assert.Equal(2, result.DatRenameSkippedCount);
        Assert.Equal(1, result.DatRenameFailedCount);
    }

    [Fact]
    public void RunResultBuilder_PropagatesDatRenameKpis()
    {
        var builder = new RunResultBuilder
        {
            DatRenameProposedCount = 12,
            DatRenameExecutedCount = 8,
            DatRenameSkippedCount = 3,
            DatRenameFailedCount = 1
        };

        var result = builder.Build();

        Assert.Equal(12, result.DatRenameProposedCount);
        Assert.Equal(8, result.DatRenameExecutedCount);
        Assert.Equal(3, result.DatRenameSkippedCount);
        Assert.Equal(1, result.DatRenameFailedCount);
    }

    [Fact]
    public void RunProjection_IncludesDatRenameKpis()
    {
        var result = new RunResult
        {
            DatRenameProposedCount = 5,
            DatRenameExecutedCount = 4,
            DatRenameSkippedCount = 0,
            DatRenameFailedCount = 1
        };

        var projection = RunProjectionFactory.Create(result);

        Assert.Equal(5, projection.DatRenameProposedCount);
        Assert.Equal(4, projection.DatRenameExecutedCount);
        Assert.Equal(0, projection.DatRenameSkippedCount);
        Assert.Equal(1, projection.DatRenameFailedCount);
    }

    [Fact]
    public void ApiRunResult_IncludesDatRenameKpis()
    {
        var result = new RunResult
        {
            DatRenameProposedCount = 6,
            DatRenameExecutedCount = 5,
            DatRenameSkippedCount = 1,
            DatRenameFailedCount = 0
        };
        var projection = RunProjectionFactory.Create(result);

        var apiResult = Romulus.Api.ApiRunResultMapper.Map(result, projection);

        Assert.Equal(6, apiResult.DatRenameProposedCount);
        Assert.Equal(5, apiResult.DatRenameExecutedCount);
        Assert.Equal(1, apiResult.DatRenameSkippedCount);
        Assert.Equal(0, apiResult.DatRenameFailedCount);
    }

    [Fact]
    public void DatRenameKpis_DefaultToZero()
    {
        var result = new RunResult();
        var projection = RunProjectionFactory.Create(result);

        Assert.Equal(0, projection.DatRenameProposedCount);
        Assert.Equal(0, projection.DatRenameExecutedCount);
        Assert.Equal(0, projection.DatRenameSkippedCount);
        Assert.Equal(0, projection.DatRenameFailedCount);
    }

    [Fact]
    public void ApiRunRequest_SupportsEnableDatAuditAndDatRename()
    {
        var request = new Romulus.Api.RunRequest
        {
            Roots = ["C:\\Roms"],
            EnableDat = true,
            EnableDatAudit = true,
            EnableDatRename = true
        };

        Assert.True(request.EnableDatAudit);
        Assert.True(request.EnableDatRename);
    }
}
