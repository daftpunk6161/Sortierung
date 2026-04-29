namespace Romulus.Contracts.Models;

/// <summary>
/// Wave 5 — T-W5-BEFORE-AFTER-SIMULATOR. Action a single ROM file undergoes
/// when the planned run is applied. Deterministic, projected purely from the
/// underlying <see cref="RunResult"/>.
/// </summary>
public enum BeforeAfterAction
{
    /// <summary>File stays at its current path (winner; no rename, no convert).</summary>
    Keep,
    /// <summary>File is removed from the active library (loser moved to trash / junk).</summary>
    Remove,
    /// <summary>File is converted to a different format (winner re-encoded).</summary>
    Convert,
    /// <summary>File is renamed in place (DAT-rename).</summary>
    Rename
}

/// <summary>
/// Wave 5 — T-W5-BEFORE-AFTER-SIMULATOR. One projected before/after entry.
/// <see cref="TargetPath"/> is <c>null</c> for <see cref="BeforeAfterAction.Remove"/>;
/// for <see cref="BeforeAfterAction.Keep"/> it equals <see cref="SourcePath"/>.
/// </summary>
public sealed record BeforeAfterEntry(
    string SourcePath,
    string? TargetPath,
    BeforeAfterAction Action,
    long SizeBytes,
    string? Reason = null);

/// <summary>
/// Wave 5 — T-W5-BEFORE-AFTER-SIMULATOR. Aggregate counts derived from
/// <see cref="BeforeAfterSimulationResult.Items"/>. Projection only —
/// no independent calculation.
/// </summary>
public sealed record BeforeAfterSummary(
    int TotalBefore,
    int TotalAfter,
    int Kept,
    int Removed,
    int Converted,
    int Renamed,
    long PotentialSavedBytes);

/// <summary>
/// Wave 5 — T-W5-BEFORE-AFTER-SIMULATOR. Output of
/// <see cref="Romulus.Contracts.Ports.IBeforeAfterSimulator"/>. Carries the
/// <see cref="UnderlyingPlan"/> so callers can verify that the projection
/// matches the canonical <c>RunOrchestrator</c>/<c>PhasePlanBuilder</c> result
/// (Single Source of Truth invariant).
/// </summary>
public sealed record BeforeAfterSimulationResult(
    IReadOnlyList<BeforeAfterEntry> Items,
    BeforeAfterSummary Summary,
    RunResult UnderlyingPlan);
