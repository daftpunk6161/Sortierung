namespace RomCleanup.UI.Wpf.Models;

/// <summary>
/// TASK-121: Immutable snapshot of pipeline progress state for UI binding.
/// </summary>
public sealed record ProgressProjection(
    double Progress,
    string ProgressText,
    string CurrentPhase,
    string CurrentFile,
    RunState CurrentRunState,
    bool IsBusy,
    string BusyHint)
{
    public static ProgressProjection Idle { get; } = new(0, "", "Phase: –", "Datei: –", RunState.Idle, false, "");
}
