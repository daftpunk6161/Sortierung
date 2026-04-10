using Romulus.Contracts.Models;
using Romulus.Core.Classification;
using Xunit;

namespace Romulus.Tests;

public sealed class DecisionResolverTests
{
    [Theory]
    [InlineData(EvidenceTier.Tier0_ExactDat, false, 100, DecisionClass.DatVerified)]
    [InlineData(EvidenceTier.Tier0_ExactDat, true, 100, DecisionClass.Review)]
    [InlineData(EvidenceTier.Tier1_Structural, false, 85, DecisionClass.Sort)]
    [InlineData(EvidenceTier.Tier1_Structural, false, 84, DecisionClass.Review)]
    [InlineData(EvidenceTier.Tier1_Structural, true, 95, DecisionClass.Review)]
    [InlineData(EvidenceTier.Tier2_StrongHeuristic, false, 100, DecisionClass.Review)]
    [InlineData(EvidenceTier.Tier3_WeakHeuristic, false, 100, DecisionClass.Blocked)]
    [InlineData(EvidenceTier.Tier4_Unknown, false, 0, DecisionClass.Unknown)]
    public void Resolve_MapsTierConflictAndConfidence(
        EvidenceTier tier,
        bool hasConflict,
        int confidence,
        DecisionClass expected)
    {
        var actual = DecisionResolver.Resolve(tier, hasConflict, confidence);

        Assert.Equal(expected, actual);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Phase 1: Conservative DAT Gate
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_Tier1_DatAvailable_NoDatMatch_ReturnsReview()
    {
        // Structural evidence (e.g., disc header) but DAT is loaded and hash didn't match.
        // Conservative gate: cap at Review.
        var actual = DecisionResolver.Resolve(
            EvidenceTier.Tier1_Structural, hasConflict: false, confidence: 95,
            datAvailable: true, conflictType: ConflictType.None);

        Assert.Equal(DecisionClass.Review, actual);
    }

    [Fact]
    public void Resolve_Tier1_DatNotAvailable_HighConf_ReturnsSort()
    {
        // Structural evidence, no DAT loaded → original behavior preserved.
        var actual = DecisionResolver.Resolve(
            EvidenceTier.Tier1_Structural, hasConflict: false, confidence: 90,
            datAvailable: false, conflictType: ConflictType.None);

        Assert.Equal(DecisionClass.Sort, actual);
    }

    [Fact]
    public void Resolve_Tier0_DatMatch_DatAvailable_DatVerified()
    {
        // Exact DAT hash match — always DatVerified regardless of datAvailable flag.
        var actual = DecisionResolver.Resolve(
            EvidenceTier.Tier0_ExactDat, hasConflict: false, confidence: 100,
            datAvailable: true, conflictType: ConflictType.None);

        Assert.Equal(DecisionClass.DatVerified, actual);
    }

    [Fact]
    public void Resolve_Tier1_DatAvailable_WithConflict_StillReview()
    {
        // DAT loaded, no match, structural evidence with conflict → Review (not Sort).
        var actual = DecisionResolver.Resolve(
            EvidenceTier.Tier1_Structural, hasConflict: true, confidence: 92,
            datAvailable: true, conflictType: ConflictType.IntraFamily);

        Assert.Equal(DecisionClass.Review, actual);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Phase 2: Family-aware Conflict Escalation
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_CrossFamilyConflict_Tier1_ReturnsBlocked()
    {
        // Cross-family conflict (e.g., PS1/RedumpDisc vs Vita/Hybrid) → always Blocked.
        var actual = DecisionResolver.Resolve(
            EvidenceTier.Tier1_Structural, hasConflict: true, confidence: 90,
            datAvailable: false, conflictType: ConflictType.CrossFamily);

        Assert.Equal(DecisionClass.Blocked, actual);
    }

    [Fact]
    public void Resolve_CrossFamilyConflict_Tier2_ReturnsBlocked()
    {
        var actual = DecisionResolver.Resolve(
            EvidenceTier.Tier2_StrongHeuristic, hasConflict: true, confidence: 80,
            datAvailable: false, conflictType: ConflictType.CrossFamily);

        Assert.Equal(DecisionClass.Blocked, actual);
    }

    [Fact]
    public void Resolve_CrossFamilyConflict_Tier0_ReturnsBlocked()
    {
        // Even Tier0 DAT match with cross-family conflict → Blocked.
        var actual = DecisionResolver.Resolve(
            EvidenceTier.Tier0_ExactDat, hasConflict: true, confidence: 100,
            datAvailable: true, conflictType: ConflictType.CrossFamily);

        Assert.Equal(DecisionClass.Blocked, actual);
    }

    [Fact]
    public void Resolve_IntraFamilyConflict_Tier1_ReturnsReview()
    {
        // Intra-family conflict (e.g., PS1 vs PS2) with structural evidence → Review.
        var actual = DecisionResolver.Resolve(
            EvidenceTier.Tier1_Structural, hasConflict: true, confidence: 90,
            datAvailable: false, conflictType: ConflictType.IntraFamily);

        Assert.Equal(DecisionClass.Review, actual);
    }

    [Fact]
    public void Resolve_IntraFamilyConflict_Tier0_NoConflict_NormalizedToReview()
    {
        // Non-None conflict type normalizes hasConflict to true.
        // Therefore Tier0 + IntraFamily yields Review.
        var actual = DecisionResolver.Resolve(
            EvidenceTier.Tier0_ExactDat, hasConflict: false, confidence: 100,
            datAvailable: true, conflictType: ConflictType.IntraFamily);

        Assert.Equal(DecisionClass.Review, actual);
    }

    [Fact]
    public void Resolve_BackwardCompatible_3ArgOverload_StillWorks()
    {
        // Ensure the 3-arg overload (no datAvailable/conflictType) preserves old behavior.
        Assert.Equal(DecisionClass.Sort,
            DecisionResolver.Resolve(EvidenceTier.Tier1_Structural, false, 85));
        Assert.Equal(DecisionClass.DatVerified,
            DecisionResolver.Resolve(EvidenceTier.Tier0_ExactDat, false, 100));
        Assert.Equal(DecisionClass.Review,
            DecisionResolver.Resolve(EvidenceTier.Tier0_ExactDat, true, 100));
    }
}
