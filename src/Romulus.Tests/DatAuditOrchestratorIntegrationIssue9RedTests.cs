using Romulus.Contracts.Models;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// TDD RED (Issue9/A-08): orchestrator/phase-plan integration tests for DAT audit.
/// These tests are expected to fail until DatAudit is wired into the phase plan.
/// </summary>
public sealed class DatAuditOrchestratorIntegrationIssue9RedTests
{
    [Fact]
    public void Build_ShouldIncludeDatAuditPhase_WhenEnableDatAudit_Issue9()
    {
        var options = new RunOptions
        {
            Mode = "DryRun",
            EnableDatAudit = true
        };

        var actions = new StandardPhaseStepActions
        {
            DatAudit = (_, _) => PhaseStepResult.Ok(),
            Deduplicate = (_, _) => PhaseStepResult.Ok(),
            JunkRemoval = (_, _) => PhaseStepResult.Ok(),
            DatRename = (_, _) => PhaseStepResult.Ok(),
            Move = (_, _) => PhaseStepResult.Ok(),
            ConsoleSort = (_, _) => PhaseStepResult.Ok(),
            WinnerConversion = (_, _) => PhaseStepResult.Ok()
        };

        var phases = new PhasePlanBuilder().Build(options, actions);

        Assert.Contains(phases, p => p.Name == "DatAudit");
    }

    [Fact]
    public void Build_ShouldPlaceDatAudit_BeforeDeduplicate_Issue9()
    {
        var options = new RunOptions
        {
            Mode = "DryRun",
            EnableDatAudit = true
        };

        var actions = new StandardPhaseStepActions
        {
            DatAudit = (_, _) => PhaseStepResult.Ok(),
            Deduplicate = (_, _) => PhaseStepResult.Ok(),
            JunkRemoval = (_, _) => PhaseStepResult.Ok(),
            DatRename = (_, _) => PhaseStepResult.Ok(),
            Move = (_, _) => PhaseStepResult.Ok(),
            ConsoleSort = (_, _) => PhaseStepResult.Ok(),
            WinnerConversion = (_, _) => PhaseStepResult.Ok()
        };

        var phases = new PhasePlanBuilder().Build(options, actions).Select(p => p.Name).ToArray();

        var datAuditIndex = Array.IndexOf(phases, "DatAudit");
        var dedupeIndex = Array.IndexOf(phases, "Deduplicate");

        Assert.True(datAuditIndex >= 0);
        Assert.True(dedupeIndex > datAuditIndex);
    }

    [Fact]
    public void Build_ShouldNotIncludeDatAudit_WhenFlagDisabled_Issue9()
    {
        var options = new RunOptions
        {
            Mode = "DryRun",
            EnableDatAudit = false
        };

        var actions = new StandardPhaseStepActions
        {
            DatAudit = (_, _) => PhaseStepResult.Ok(),
            Deduplicate = (_, _) => PhaseStepResult.Ok(),
            JunkRemoval = (_, _) => PhaseStepResult.Ok(),
            DatRename = (_, _) => PhaseStepResult.Ok(),
            Move = (_, _) => PhaseStepResult.Ok(),
            ConsoleSort = (_, _) => PhaseStepResult.Ok(),
            WinnerConversion = (_, _) => PhaseStepResult.Ok()
        };

        var phases = new PhasePlanBuilder().Build(options, actions);

        Assert.DoesNotContain(phases, p => p.Name == "DatAudit");
    }

    [Fact]
    public void PipelineState_ShouldStoreDatAuditResult_Issue9()
    {
        var state = new PipelineState();
        var result = new DatAuditResult(
            Entries: Array.Empty<DatAuditEntry>(),
            HaveCount: 0,
            HaveWrongNameCount: 0,
            MissCount: 0,
            UnknownCount: 0,
            AmbiguousCount: 0);

        state.SetDatAuditOutput(result);

        Assert.NotNull(state.DatAuditResult);
    }
}
