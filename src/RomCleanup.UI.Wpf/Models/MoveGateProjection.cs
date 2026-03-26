namespace RomCleanup.UI.Wpf.Models;

/// <summary>
/// TASK-121: Immutable snapshot of the move/execute gate state for SmartActionBar.
/// </summary>
public sealed record MoveGateProjection(
    bool ShowStartMoveButton,
    bool HasRunResult,
    bool CanRollback,
    string LastReportPath)
{
    public static MoveGateProjection Idle { get; } = new(false, false, false, "");
}
