using Romulus.Contracts.Models;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// TDD RED (Issue9/A-16): orchestrator/phase-plan integration tests for DAT rename.
/// Intentionally failing until DatRename phase is wired into phase planning.
/// </summary>
public sealed class DatRenameOrchestratorIntegrationIssue9RedTests
{
    [Fact]
    public void Build_ShouldIncludeDatRenamePhase_WhenEnableDatRenameAndModeMove_Issue9()
    {
        // Arrange
        var options = new RunOptions
        {
            Mode = "Move",
            EnableDatRename = true
        };

        var actions = new StandardPhaseStepActions
        {
            Deduplicate = (_, _) => PhaseStepResult.Ok(),
            JunkRemoval = (_, _) => PhaseStepResult.Ok(),
            DatRename = (_, _) => PhaseStepResult.Ok(),
            Move = (_, _) => PhaseStepResult.Ok(),
            ConsoleSort = (_, _) => PhaseStepResult.Ok(),
            WinnerConversion = (_, _) => PhaseStepResult.Ok()
        };

        // Act
        var phases = new PhasePlanBuilder().Build(options, actions);

        // Assert
        Assert.Contains(phases, p => p.Name == "DatRename");
    }

    [Fact]
    public void Build_ShouldPlaceDatRename_BetweenJunkRemovalAndMove_Issue9()
    {
        // Arrange
        var options = new RunOptions
        {
            Mode = "Move",
            EnableDatRename = true
        };

        var actions = new StandardPhaseStepActions
        {
            Deduplicate = (_, _) => PhaseStepResult.Ok(),
            JunkRemoval = (_, _) => PhaseStepResult.Ok(),
            DatRename = (_, _) => PhaseStepResult.Ok(),
            Move = (_, _) => PhaseStepResult.Ok(),
            ConsoleSort = (_, _) => PhaseStepResult.Ok(),
            WinnerConversion = (_, _) => PhaseStepResult.Ok()
        };

        // Act
        var phases = new PhasePlanBuilder().Build(options, actions).Select(p => p.Name).ToArray();

        // Assert
        var junkIndex = Array.IndexOf(phases, "JunkRemoval");
        var renameIndex = Array.IndexOf(phases, "DatRename");
        var moveIndex = Array.IndexOf(phases, "Move");

        Assert.True(junkIndex >= 0);
        Assert.True(renameIndex > junkIndex);
        Assert.True(moveIndex > renameIndex);
    }

    [Fact]
    public void Build_ShouldNotIncludeDatRename_WhenModeIsDryRun_Issue9()
    {
        // Arrange
        var options = new RunOptions
        {
            Mode = "DryRun",
            EnableDatRename = true
        };

        var actions = new StandardPhaseStepActions
        {
            Deduplicate = (_, _) => PhaseStepResult.Ok(),
            JunkRemoval = (_, _) => PhaseStepResult.Ok(),
            DatRename = (_, _) => PhaseStepResult.Ok(),
            Move = (_, _) => PhaseStepResult.Ok(),
            ConsoleSort = (_, _) => PhaseStepResult.Ok(),
            WinnerConversion = (_, _) => PhaseStepResult.Ok()
        };

        // Act
        var phases = new PhasePlanBuilder().Build(options, actions);

        // Assert
        Assert.DoesNotContain(phases, p => p.Name == "DatRename");
    }
}
