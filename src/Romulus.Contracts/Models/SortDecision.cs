namespace Romulus.Contracts.Models;

/// <summary>
/// Sort gate decision — determines whether a file may be automatically sorted.
/// Computed centrally by HypothesisResolver, consumed by the sorting gate.
/// </summary>
public enum SortDecision
{
    /// <summary>Sort allowed — high confidence, hard evidence, no conflict.</summary>
    Sort,

    /// <summary>Review needed — detection plausible but not fully secured.</summary>
    Review,

    /// <summary>Blocked — confidence too low, conflict, or UNKNOWN.</summary>
    Blocked,

    /// <summary>DAT-verified — hash match, always sortable.</summary>
    DatVerified,

    /// <summary>No evidence at all. File is genuinely unknown.
    /// Different from Blocked: no conflicting evidence, just no evidence.</summary>
    Unknown,
}
