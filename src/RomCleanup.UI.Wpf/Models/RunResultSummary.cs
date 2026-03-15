namespace RomCleanup.UI.Wpf.Models;

/// <summary>
/// GUI-027: Immutable summary of a pipeline run result for dashboard display.
/// Replaces 15 individual dashboard string properties with a single record.
/// </summary>
public sealed record RunResultSummary(
    string Mode,
    int Winners,
    int Dupes,
    int Junk,
    int Games,
    int DatHits,
    long DurationMs,
    int TotalFiles)
{
    public string DurationText => $"{DurationMs / 1000.0:F1}s";
    public string HealthScore => TotalFiles > 0 ? $"{100.0 * Winners / TotalFiles:F0}%" : "–";
    public string DedupeRate => (Winners + Dupes) > 0 ? $"{100.0 * Dupes / (Winners + Dupes):F0}%" : "–";
}
