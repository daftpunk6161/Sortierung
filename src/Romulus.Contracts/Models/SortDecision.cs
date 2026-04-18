namespace Romulus.Contracts.Models;

/// <summary>
/// Sort gate decision — output-facing alias for <see cref="DecisionClass"/>.
/// <para>
/// <b>Architecture note (F-05):</b> <see cref="DecisionClass"/> is the canonical domain model.
/// <see cref="SortDecision"/> exists as a reporting / sorting-gate alias and mirrors the same
/// states. Use <see cref="DecisionClassExtensions.ToSortDecision"/> /
/// <see cref="DecisionClassExtensions.ToDecisionClass"/> for conversion. Do not add new states
/// here without adding them to <see cref="DecisionClass"/> first.
/// </para>
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
