using RomCleanup.Contracts.Models;

namespace RomCleanup.Core.Classification;

/// <summary>
/// Central tier-based decision resolver for recognition results.
/// </summary>
public static class DecisionResolver
{
    public static DecisionClass Resolve(EvidenceTier tier, bool hasConflict, int confidence)
    {
        if (tier == EvidenceTier.Tier0_ExactDat && !hasConflict)
            return DecisionClass.DatVerified;

        if (tier == EvidenceTier.Tier0_ExactDat && hasConflict)
            return DecisionClass.Review;

        if (tier == EvidenceTier.Tier1_Structural && !hasConflict && confidence >= 85)
            return DecisionClass.Sort;

        if (tier == EvidenceTier.Tier1_Structural)
            return DecisionClass.Review;

        if (tier == EvidenceTier.Tier2_StrongHeuristic)
            return DecisionClass.Review;

        if (tier == EvidenceTier.Tier3_WeakHeuristic)
            return DecisionClass.Blocked;

        return DecisionClass.Unknown;
    }
}
