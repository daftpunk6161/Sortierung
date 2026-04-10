using Romulus.Contracts.Models;

namespace Romulus.Core.Classification;

/// <summary>
/// Central tier-based decision resolver for recognition results.
/// </summary>
public static class DecisionResolver
{
    /// <summary>
    /// Resolve decision from evidence tier, conflict state, and confidence.
    /// Backward-compatible overload without DAT-gate or family-conflict awareness.
    /// </summary>
    public static DecisionClass Resolve(EvidenceTier tier, bool hasConflict, int confidence)
        => Resolve(tier, hasConflict, confidence, datAvailable: false, conflictType: ConflictType.None);

    /// <summary>
    /// Resolve decision with full DAT-gate and family-conflict awareness.
    /// <para>
    /// Conservative DAT gate: When a DAT index is loaded (<paramref name="datAvailable"/> = true)
    /// but the file did NOT hash-match (tier &gt; Tier0), Tier1 structural evidence
    /// is capped at <see cref="DecisionClass.Review"/>. Rationale: a loaded DAT that
    /// doesn't contain the hash is a negative signal — the file may be undumped,
    /// bad, or misidentified.
    /// </para>
    /// <para>
    /// Family-conflict gate: Cross-family conflicts always produce
    /// <see cref="DecisionClass.Blocked"/>; intra-family conflicts without
    /// structural evidence produce <see cref="DecisionClass.Review"/>.
    /// </para>
    /// </summary>
    public static DecisionClass Resolve(
        EvidenceTier tier,
        bool hasConflict,
        int confidence,
        bool datAvailable,
        ConflictType conflictType)
    {
        // Normalize: if a non-None conflict type is supplied, hasConflict must be true.
        // This prevents callers from accidentally bypassing the cross-family gate
        // by passing conflictType=CrossFamily but hasConflict=false.
        if (conflictType != ConflictType.None)
            hasConflict = true;

        // ── Cross-family conflict gate (applies to ALL tiers) ──
        // Fundamentally different platform families (e.g. PS1/RedumpDisc vs Vita/Hybrid)
        // are always blocked regardless of evidence strength.
        if (hasConflict && conflictType == ConflictType.CrossFamily)
            return DecisionClass.Blocked;

        // ── Tier 0: Exact DAT hash match ──
        if (tier == EvidenceTier.Tier0_ExactDat)
        {
            return hasConflict ? DecisionClass.Review : DecisionClass.DatVerified;
        }

        // ── Tier 1: Structural evidence (disc header, cartridge header, unique ext) ──
        if (tier == EvidenceTier.Tier1_Structural)
        {
            // Conservative DAT gate: DAT loaded but file hash NOT in DAT → negative signal.
            // Cap at Review even without conflict. Rationale: a loaded DAT that doesn't
            // contain the hash suggests the file may be undumped, bad, or misidentified.
            if (datAvailable)
                return DecisionClass.Review;

            // Without DAT: structural evidence can reach Sort if high-confidence and no conflict.
            if (!hasConflict && confidence >= 85)
                return DecisionClass.Sort;

            return DecisionClass.Review;
        }

        // ── Tier 2: Strong heuristic (folder + extension combo) ──
        if (tier == EvidenceTier.Tier2_StrongHeuristic)
            return DecisionClass.Review;

        // ── Tier 3: Weak heuristic (ambiguous extension only) ──
        if (tier == EvidenceTier.Tier3_WeakHeuristic)
            return DecisionClass.Blocked;

        // ── Tier 4+: No evidence ──
        return DecisionClass.Unknown;
    }
}
