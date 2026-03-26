namespace RomCleanup.UI.Wpf.Models;

/// <summary>
/// TASK-121: Immutable snapshot of system/tool readiness indicators for UI binding.
/// </summary>
public sealed record StatusProjection(
    string StatusRoots,
    StatusLevel RootsStatusLevel,
    string StatusTools,
    StatusLevel ToolsStatusLevel,
    string StatusDat,
    StatusLevel DatStatusLevel,
    string StatusReady,
    StatusLevel ReadyStatusLevel,
    string StatusRuntime,
    string ChdmanStatusText,
    string DolphinStatusText,
    string SevenZipStatusText,
    string PsxtractStatusText,
    string CisoStatusText)
{
    public static StatusProjection Empty { get; } = new(
        "", StatusLevel.Missing, "", StatusLevel.Missing, "", StatusLevel.Missing,
        "", StatusLevel.Missing, "", "–", "–", "–", "–", "–");
}
