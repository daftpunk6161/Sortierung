namespace Romulus.UI.Wpf.Models;

/// <summary>
/// Explicit run lifecycle states — replaces implicit IsBusy/CanRollback inference.
/// </summary>
public enum RunState
{
    Idle,
    Preflight,
    Scanning,
    Deduplicating,
    Sorting,
    Moving,
    Converting,
    Completed,
    CompletedDryRun,
    Failed,
    Cancelled
}
