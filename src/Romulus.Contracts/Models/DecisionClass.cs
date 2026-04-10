namespace Romulus.Contracts.Models;

/// <summary>
/// Final classification decision for a ROM candidate.
/// Determines processing path: auto-sort, manual review, blocked, or unknown.
/// </summary>
public enum DecisionClass
{
    /// <summary>
    /// Auto-sort allowed. High-confidence match with strong evidence.
    /// Requires Tier0 or Tier1 evidence without unresolved conflict.
    /// </summary>
    Sort,

    /// <summary>
    /// DAT-verified sort. Hash-matched against authoritative DAT.
    /// Highest trust level.
    /// </summary>
    DatVerified,

    /// <summary>
    /// Manual review recommended. Plausible match but insufficient confidence
    /// for unattended sorting.
    /// </summary>
    Review,

    /// <summary>
    /// Sorting blocked. Conflict detected or evidence too weak.
    /// </summary>
    Blocked,

    /// <summary>
    /// No evidence at all. File is genuinely unknown.
    /// </summary>
    Unknown,
}

public static class DecisionClassExtensions
{
    public static SortDecision ToSortDecision(this DecisionClass decision) => decision switch
    {
        DecisionClass.Sort => SortDecision.Sort,
        DecisionClass.DatVerified => SortDecision.DatVerified,
        DecisionClass.Review => SortDecision.Review,
        DecisionClass.Blocked => SortDecision.Blocked,
        _ => SortDecision.Unknown,
    };

    public static DecisionClass ToDecisionClass(this SortDecision decision) => decision switch
    {
        SortDecision.Sort => DecisionClass.Sort,
        SortDecision.DatVerified => DecisionClass.DatVerified,
        SortDecision.Review => DecisionClass.Review,
        SortDecision.Blocked => DecisionClass.Blocked,
        _ => DecisionClass.Unknown,
    };
}
