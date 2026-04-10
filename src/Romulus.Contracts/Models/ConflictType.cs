namespace Romulus.Contracts.Models;

/// <summary>
/// Classifies the type of detection conflict between competing hypotheses.
/// Used by DecisionResolver and HypothesisResolver for escalation decisions.
/// </summary>
public enum ConflictType
{
    /// <summary>No conflict detected.</summary>
    None,

    /// <summary>
    /// Conflict between consoles in the same platform family (e.g. PS1 vs PS2, NES vs Famicom).
    /// May be resolvable with structural evidence (disc header, cartridge header).
    /// </summary>
    IntraFamily,

    /// <summary>
    /// Conflict between consoles in different platform families (e.g. PS1 vs Vita, NES vs ARCADE).
    /// Indicates a fundamental mismatch — always escalated to Blocked.
    /// </summary>
    CrossFamily,
}
