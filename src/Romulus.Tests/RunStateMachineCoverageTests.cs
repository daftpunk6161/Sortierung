using Romulus.UI.Wpf.Models;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Full state transition matrix tests for RunStateMachine.
/// </summary>
public sealed class RunStateMachineCoverageTests
{
    // ── Identity transitions (from == to) ─────────────────────────
    [Theory]
    [InlineData(RunState.Idle)]
    [InlineData(RunState.Preflight)]
    [InlineData(RunState.Scanning)]
    [InlineData(RunState.Deduplicating)]
    [InlineData(RunState.Sorting)]
    [InlineData(RunState.Moving)]
    [InlineData(RunState.Converting)]
    [InlineData(RunState.Completed)]
    [InlineData(RunState.CompletedDryRun)]
    [InlineData(RunState.Failed)]
    [InlineData(RunState.Cancelled)]
    public void SameState_IsAlwaysValid(RunState state)
    {
        Assert.True(RunStateMachine.IsValidTransition(state, state));
    }

    // ── Happy path forward transitions ────────────────────────────
    [Theory]
    [InlineData(RunState.Idle, RunState.Preflight)]
    [InlineData(RunState.Preflight, RunState.Idle)] // SEC-001: dialog-decline abort
    [InlineData(RunState.Preflight, RunState.Scanning)]
    [InlineData(RunState.Scanning, RunState.Deduplicating)]
    [InlineData(RunState.Deduplicating, RunState.Sorting)]
    [InlineData(RunState.Deduplicating, RunState.Moving)]
    [InlineData(RunState.Sorting, RunState.Moving)]
    [InlineData(RunState.Moving, RunState.Sorting)]
    [InlineData(RunState.Moving, RunState.Converting)]
    public void ForwardTransitions_AreValid(RunState from, RunState to)
    {
        Assert.True(RunStateMachine.IsValidTransition(from, to));
    }

    // ── Skip transitions ──────────────────────────────────────────
    [Theory]
    [InlineData(RunState.Scanning, RunState.Sorting)]
    [InlineData(RunState.Scanning, RunState.Moving)]
    [InlineData(RunState.Scanning, RunState.Converting)]
    [InlineData(RunState.Deduplicating, RunState.Converting)]
    [InlineData(RunState.Sorting, RunState.Converting)]
    public void SkipTransitions_AreValid(RunState from, RunState to)
    {
        Assert.True(RunStateMachine.IsValidTransition(from, to));
    }

    // ── Terminal state transitions ────────────────────────────────
    [Theory]
    [InlineData(RunState.Preflight, RunState.Completed)]
    [InlineData(RunState.Preflight, RunState.CompletedDryRun)]
    [InlineData(RunState.Preflight, RunState.Failed)]
    [InlineData(RunState.Preflight, RunState.Cancelled)]
    [InlineData(RunState.Scanning, RunState.Completed)]
    [InlineData(RunState.Scanning, RunState.Failed)]
    [InlineData(RunState.Scanning, RunState.Cancelled)]
    [InlineData(RunState.Deduplicating, RunState.Completed)]
    [InlineData(RunState.Deduplicating, RunState.Failed)]
    [InlineData(RunState.Sorting, RunState.Completed)]
    [InlineData(RunState.Sorting, RunState.Failed)]
    [InlineData(RunState.Moving, RunState.Completed)]
    [InlineData(RunState.Moving, RunState.Failed)]
    [InlineData(RunState.Converting, RunState.Completed)]
    [InlineData(RunState.Converting, RunState.Failed)]
    [InlineData(RunState.Converting, RunState.Cancelled)]
    public void AnyActiveState_CanTransitionToTerminal(RunState from, RunState to)
    {
        Assert.True(RunStateMachine.IsValidTransition(from, to));
    }

    // ── Recovery from terminal states ─────────────────────────────
    [Theory]
    [InlineData(RunState.Completed, RunState.Idle)]
    [InlineData(RunState.Completed, RunState.Preflight)]
    [InlineData(RunState.CompletedDryRun, RunState.Idle)]
    [InlineData(RunState.CompletedDryRun, RunState.Preflight)]
    [InlineData(RunState.Failed, RunState.Idle)]
    [InlineData(RunState.Failed, RunState.Preflight)]
    [InlineData(RunState.Cancelled, RunState.Idle)]
    [InlineData(RunState.Cancelled, RunState.Preflight)]
    public void TerminalState_CanRecoverToIdleOrPreflight(RunState from, RunState to)
    {
        Assert.True(RunStateMachine.IsValidTransition(from, to));
    }

    // ── Invalid transitions ───────────────────────────────────────
    [Theory]
    [InlineData(RunState.Idle, RunState.Scanning)]
    [InlineData(RunState.Idle, RunState.Deduplicating)]
    [InlineData(RunState.Idle, RunState.Sorting)]
    [InlineData(RunState.Idle, RunState.Moving)]
    [InlineData(RunState.Idle, RunState.Converting)]
    [InlineData(RunState.Idle, RunState.Completed)]
    [InlineData(RunState.Completed, RunState.Scanning)]
    [InlineData(RunState.Failed, RunState.Scanning)]
    [InlineData(RunState.Cancelled, RunState.Converting)]
    [InlineData(RunState.Converting, RunState.Scanning)]
    [InlineData(RunState.Moving, RunState.Preflight)]
    [InlineData(RunState.Sorting, RunState.Preflight)]
    public void InvalidTransitions_ReturnFalse(RunState from, RunState to)
    {
        Assert.False(RunStateMachine.IsValidTransition(from, to));
    }
}
