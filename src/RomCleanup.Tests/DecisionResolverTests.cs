using RomCleanup.Contracts.Models;
using RomCleanup.Core.Classification;
using Xunit;

namespace RomCleanup.Tests;

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
}
