using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

public sealed class FamilyPipelineSelectorTests
{
    [Fact]
    public void Apply_CrossFamilyDatMismatch_EscalatesToBlocked()
    {
        var selector = new FamilyPipelineSelector();
        var input = new FamilyPipelineInput(
            DecisionClass: DecisionClass.DatVerified,
            SortDecision: SortDecision.DatVerified,
            MatchEvidence: new MatchEvidence
            {
                Level = MatchLevel.Exact,
                Reasoning = "Exact DAT hash match.",
                HasConflict = false,
                PrimaryMatchKind = MatchKind.ExactDatHash,
                Tier = EvidenceTier.Tier0_ExactDat,
            },
            DetectionConfidence: 100,
            DetectionConflict: false,
            ConflictType: ConflictType.None,
            DatMatch: true,
            DetectedFamily: PlatformFamily.RedumpDisc,
            ResolvedFamily: PlatformFamily.Hybrid,
            FinalMatchKind: MatchKind.ExactDatHash);

        var output = selector.Apply(input);

        Assert.Equal(DecisionClass.Blocked, output.DecisionClass);
        Assert.Equal(SortDecision.Blocked, output.SortDecision);
        Assert.True(output.DetectionConflict);
        Assert.Equal(ConflictType.CrossFamily, output.ConflictType);
        Assert.Contains("Cross-family mismatch", output.MatchEvidence.Reasoning);
    }

    [Fact]
    public void Apply_DiscHeaderWithoutDat_BoostsConfidence()
    {
        var selector = new FamilyPipelineSelector();
        var input = new FamilyPipelineInput(
            DecisionClass: DecisionClass.Review,
            SortDecision: SortDecision.Review,
            MatchEvidence: new MatchEvidence
            {
                Level = MatchLevel.Probable,
                Reasoning = "Disc header hit",
                HasConflict = false,
                PrimaryMatchKind = MatchKind.DiscHeaderSignature,
                Tier = EvidenceTier.Tier1_Structural,
            },
            DetectionConfidence: 70,
            DetectionConflict: false,
            ConflictType: ConflictType.None,
            DatMatch: false,
            DetectedFamily: PlatformFamily.RedumpDisc,
            ResolvedFamily: PlatformFamily.RedumpDisc,
            FinalMatchKind: MatchKind.DiscHeaderSignature);

        var output = selector.Apply(input);

        Assert.True(output.DetectionConfidence >= 92);
        Assert.Equal(DecisionClass.Review, output.DecisionClass);
    }

    [Fact]
    public void Apply_ArcadeWithoutDat_DowngradesSortToReview()
    {
        var selector = new FamilyPipelineSelector();
        var input = new FamilyPipelineInput(
            DecisionClass: DecisionClass.Sort,
            SortDecision: SortDecision.Sort,
            MatchEvidence: new MatchEvidence
            {
                Level = MatchLevel.Strong,
                Reasoning = "Archive content + extension",
                HasConflict = false,
                PrimaryMatchKind = MatchKind.ArchiveContentExtension,
                Tier = EvidenceTier.Tier2_StrongHeuristic,
            },
            DetectionConfidence: 86,
            DetectionConflict: false,
            ConflictType: ConflictType.None,
            DatMatch: false,
            DetectedFamily: PlatformFamily.Arcade,
            ResolvedFamily: PlatformFamily.Arcade,
            FinalMatchKind: MatchKind.ArchiveContentExtension);

        var output = selector.Apply(input);

        Assert.Equal(DecisionClass.Review, output.DecisionClass);
        Assert.Equal(SortDecision.Review, output.SortDecision);
        Assert.Contains("Arcade family without DAT verification", output.MatchEvidence.Reasoning);
    }

    [Fact]
    public void Apply_SameFamilyDatMatch_KeepsDecision()
    {
        var selector = new FamilyPipelineSelector();
        var input = new FamilyPipelineInput(
            DecisionClass: DecisionClass.DatVerified,
            SortDecision: SortDecision.DatVerified,
            MatchEvidence: new MatchEvidence
            {
                Level = MatchLevel.Exact,
                Reasoning = "Exact DAT hash match.",
                HasConflict = false,
                PrimaryMatchKind = MatchKind.ExactDatHash,
                Tier = EvidenceTier.Tier0_ExactDat,
            },
            DetectionConfidence: 100,
            DetectionConflict: false,
            ConflictType: ConflictType.None,
            DatMatch: true,
            DetectedFamily: PlatformFamily.NoIntroCartridge,
            ResolvedFamily: PlatformFamily.NoIntroCartridge,
            FinalMatchKind: MatchKind.ExactDatHash);

        var output = selector.Apply(input);

        Assert.Equal(DecisionClass.DatVerified, output.DecisionClass);
        Assert.Equal(SortDecision.DatVerified, output.SortDecision);
        Assert.Equal(ConflictType.None, output.ConflictType);
    }
}
