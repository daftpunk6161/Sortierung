namespace Romulus.Contracts.Models;

/// <summary>
/// Hierarchical evidence tier — determines trust level for sort decisions.
/// Higher tier number = lower trust. Only Tier0 and Tier1 allow auto-sort.
/// </summary>
public enum EvidenceTier
{
    /// <summary>DAT hash exact match. Absolute authority. Always auto-sortable.</summary>
    Tier0_ExactDat = 0,

    /// <summary>Structural binary evidence (header magic, serial, disc signature).
    /// Auto-sortable when unambiguous.</summary>
    Tier1_Structural = 1,

    /// <summary>Strong heuristic (unique extension, archive content analysis).
    /// Review-gate required — never auto-sortable.</summary>
    Tier2_StrongHeuristic = 2,

    /// <summary>Weak heuristic (folder name, filename keyword, ambiguous extension).
    /// Never auto-sortable. Always Review or Blocked.</summary>
    Tier3_WeakHeuristic = 3,

    /// <summary>No evidence. Unknown. Blocked.</summary>
    Tier4_Unknown = 4,
}
