namespace RomCleanup.UI.Wpf.Models;

/// <summary>
/// Shared transition rules for the explicit run lifecycle state machine.
/// </summary>
public static class RunStateMachine
{
    public static bool IsValidTransition(RunState from, RunState to)
    {
        if (from == to) return true;
        return (from, to) switch
        {
            (RunState.Idle, RunState.Preflight) => true,
            (RunState.Preflight, RunState.Scanning) => true,
            (RunState.Scanning, RunState.Deduplicating) => true,
            (RunState.Deduplicating, RunState.Sorting) => true,
            (RunState.Sorting, RunState.Moving) => true,
            (RunState.Moving, RunState.Converting) => true,
            (RunState.Preflight or RunState.Scanning or RunState.Deduplicating or
             RunState.Sorting or RunState.Moving or RunState.Converting,
             RunState.Completed or RunState.CompletedDryRun or RunState.Failed or RunState.Cancelled) => true,
            (RunState.Scanning, RunState.Sorting or RunState.Moving or RunState.Converting) => true,
            (RunState.Deduplicating, RunState.Moving or RunState.Converting) => true,
            (RunState.Sorting, RunState.Converting) => true,
            (RunState.Completed or RunState.CompletedDryRun or RunState.Failed or RunState.Cancelled,
             RunState.Idle or RunState.Preflight) => true,
            _ => false,
        };
    }
}