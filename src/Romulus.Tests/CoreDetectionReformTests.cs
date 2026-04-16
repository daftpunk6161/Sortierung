using System.Collections.Generic;
using System.Linq;
using Romulus.Contracts.Models;
using Romulus.Core.Classification;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Tests for the 3-Eingriffe core detection reform:
///   E1: ConfidenceRating() decoupled from enum ordinal
///   E2: hasHardEvidence gate replaces confidence >= 85 in DecisionResolver
///   E3: SoftOnlyCap 79, tier-based AMBIGUOUS guard
/// </summary>
public sealed class CoreDetectionReformTests
{
    // ──────────────────────────────────────────────────────────────────
    //  E1: ConfidenceRating invariants
    // ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(DetectionSource.DatHash, 100)]
    [InlineData(DetectionSource.DiscHeader, 95)]
    [InlineData(DetectionSource.CartridgeHeader, 90)]
    [InlineData(DetectionSource.SerialNumber, 80)]
    [InlineData(DetectionSource.UniqueExtension, 80)]
    [InlineData(DetectionSource.ArchiveContent, 60)]
    [InlineData(DetectionSource.FolderName, 45)]
    [InlineData(DetectionSource.FilenameKeyword, 40)]
    [InlineData(DetectionSource.AmbiguousExtension, 25)]
    public void ConfidenceRating_ReturnsExpectedValues(DetectionSource source, int expected)
    {
        Assert.Equal(expected, source.ConfidenceRating());
    }

    [Fact]
    public void ConfidenceRating_IsDecoupledFromEnumOrdinal()
    {
        // The whole point of the reform: enum value != confidence rating.
        // Verify at least one case where they diverge.
        Assert.NotEqual((int)DetectionSource.FolderName, DetectionSource.FolderName.ConfidenceRating());
        Assert.NotEqual((int)DetectionSource.UniqueExtension, DetectionSource.UniqueExtension.ConfidenceRating());
        Assert.NotEqual((int)DetectionSource.AmbiguousExtension, DetectionSource.AmbiguousExtension.ConfidenceRating());
    }

    [Fact]
    public void ConfidenceRating_HardSourcesAlwaysHigherThanSoft()
    {
        var hardSources = new[] { DetectionSource.DatHash, DetectionSource.DiscHeader, DetectionSource.CartridgeHeader, DetectionSource.SerialNumber };
        var softSources = new[] { DetectionSource.FolderName, DetectionSource.FilenameKeyword, DetectionSource.AmbiguousExtension };

        var minHard = hardSources.Min(s => s.ConfidenceRating());
        var maxSoft = softSources.Max(s => s.ConfidenceRating());

        Assert.True(minHard > maxSoft,
            $"Minimum hard confidence ({minHard}) must exceed maximum soft confidence ({maxSoft})");
    }

    [Fact]
    public void EnumOrdinals_Unchanged_BackwardCompatibility()
    {
        // Enum ordinals must remain stable for serialization compatibility.
        Assert.Equal(100, (int)DetectionSource.DatHash);
        Assert.Equal(95, (int)DetectionSource.UniqueExtension);
        Assert.Equal(92, (int)DetectionSource.DiscHeader);
        Assert.Equal(90, (int)DetectionSource.CartridgeHeader);
        Assert.Equal(88, (int)DetectionSource.SerialNumber);
        Assert.Equal(85, (int)DetectionSource.FolderName);
        Assert.Equal(80, (int)DetectionSource.ArchiveContent);
        Assert.Equal(75, (int)DetectionSource.FilenameKeyword);
        Assert.Equal(40, (int)DetectionSource.AmbiguousExtension);
    }

    // ──────────────────────────────────────────────────────────────────
    //  E2: hasHardEvidence gate in DecisionResolver
    // ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false, DecisionClass.Review)]
    [InlineData(true, DecisionClass.Sort)]
    public void DecisionResolver_Tier1_SortRequiresHardEvidence(bool hasHardEvidence, DecisionClass expected)
    {
        var actual = DecisionResolver.Resolve(
            EvidenceTier.Tier1_Structural,
            hasConflict: false, confidence: 95,
            datAvailable: false, conflictType: ConflictType.None,
            hasHardEvidence: hasHardEvidence);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DecisionResolver_Tier1_HardEvidence_WithConflict_StillReview()
    {
        // Even with hard evidence, conflicts cap at Review.
        var actual = DecisionResolver.Resolve(
            EvidenceTier.Tier1_Structural,
            hasConflict: true, confidence: 95,
            datAvailable: false, conflictType: ConflictType.None,
            hasHardEvidence: true);

        Assert.Equal(DecisionClass.Review, actual);
    }

    [Fact]
    public void DecisionResolver_Tier1_HardEvidence_DatAvailable_CappedAtReview()
    {
        // DAT loaded but no hash match → conservative gate overrides hard evidence.
        var actual = DecisionResolver.Resolve(
            EvidenceTier.Tier1_Structural,
            hasConflict: false, confidence: 95,
            datAvailable: true, conflictType: ConflictType.None,
            hasHardEvidence: true);

        Assert.Equal(DecisionClass.Review, actual);
    }

    [Fact]
    public void DecisionResolver_Tier0_IgnoresHardEvidenceFlag()
    {
        // Tier0 (DAT hash match) always DatVerified regardless of hasHardEvidence.
        Assert.Equal(DecisionClass.DatVerified,
            DecisionResolver.Resolve(EvidenceTier.Tier0_ExactDat, false, 100, false, ConflictType.None, hasHardEvidence: false));
        Assert.Equal(DecisionClass.DatVerified,
            DecisionResolver.Resolve(EvidenceTier.Tier0_ExactDat, false, 100, false, ConflictType.None, hasHardEvidence: true));
    }

    [Theory]
    [InlineData(EvidenceTier.Tier2_StrongHeuristic, DecisionClass.Review)]
    [InlineData(EvidenceTier.Tier3_WeakHeuristic, DecisionClass.Blocked)]
    [InlineData(EvidenceTier.Tier4_Unknown, DecisionClass.Unknown)]
    public void DecisionResolver_LowerTiers_UnaffectedByHardEvidence(EvidenceTier tier, DecisionClass expected)
    {
        // Tiers 2-4 should produce the same result regardless of hasHardEvidence.
        var withHard = DecisionResolver.Resolve(tier, false, 95, false, ConflictType.None, hasHardEvidence: true);
        var withoutHard = DecisionResolver.Resolve(tier, false, 95, false, ConflictType.None, hasHardEvidence: false);

        Assert.Equal(expected, withHard);
        Assert.Equal(expected, withoutHard);
    }

    // ──────────────────────────────────────────────────────────────────
    //  E2: HypothesisResolver propagates hasHardEvidence correctly
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void HypothesisResolver_DiscHeader_GetsSort()
    {
        // DiscHeader = hard evidence → Sort via hasHardEvidence gate
        var result = HypothesisResolver.Resolve([
            new("PS1", DetectionSource.DiscHeader.ConfidenceRating(), DetectionSource.DiscHeader, "disc-header=PS1")
        ]);

        Assert.Equal("PS1", result.ConsoleKey);
        Assert.True(result.HasHardEvidence);
        Assert.Equal(SortDecision.Sort, result.SortDecision);
    }

    [Fact]
    public void HypothesisResolver_UniqueExtOnly_NoHardEvidence_Review()
    {
        // UniqueExtension is Tier2/soft → Review (not Sort, even with high confidence)
        var result = HypothesisResolver.Resolve([
            new("GBA", DetectionSource.UniqueExtension.ConfidenceRating(), DetectionSource.UniqueExtension, "ext=.gba")
        ]);

        Assert.Equal("GBA", result.ConsoleKey);
        Assert.False(result.HasHardEvidence);
        Assert.Equal(SortDecision.Review, result.SortDecision);
    }

    [Fact]
    public void HypothesisResolver_SerialOnly_HardEvidence_Sort()
    {
        // SerialNumber = hard evidence → Sort even with single-source cap
        var result = HypothesisResolver.Resolve([
            new("PS1", DetectionSource.SerialNumber.ConfidenceRating(), DetectionSource.SerialNumber, "serial=SLUS-00123")
        ]);

        Assert.True(result.HasHardEvidence);
        Assert.Equal(SortDecision.Sort, result.SortDecision);
    }

    // ──────────────────────────────────────────────────────────────────
    //  E3a: SoftOnlyCap boundary
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void SoftOnlyCap_Is79()
    {
        Assert.Equal(79, HypothesisResolver.SoftOnlyCap);
    }

    [Fact]
    public void SoftOnly_MultiSource_CappedAt79()
    {
        // FolderName(cap=80) + FilenameKeyword(cap=60) → min(79, 80+15) = 79
        var result = HypothesisResolver.Resolve([
            new("NES", DetectionSource.FolderName.ConfidenceRating(), DetectionSource.FolderName, "folder=NES"),
            new("NES", DetectionSource.FilenameKeyword.ConfidenceRating(), DetectionSource.FilenameKeyword, "[NES]"),
        ]);

        Assert.True(result.IsSoftOnly);
        Assert.True(result.Confidence <= 79,
            $"Soft-only multi-source confidence ({result.Confidence}) must not exceed 79");
    }

    [Fact]
    public void SoftOnly_NeverReachesSort()
    {
        // Even with multiple agreeing soft sources, should never reach Sort.
        var result = HypothesisResolver.Resolve([
            new("NES", DetectionSource.FolderName.ConfidenceRating(), DetectionSource.FolderName, "folder=NES"),
            new("NES", DetectionSource.FilenameKeyword.ConfidenceRating(), DetectionSource.FilenameKeyword, "[NES]"),
            new("NES", DetectionSource.AmbiguousExtension.ConfidenceRating(), DetectionSource.AmbiguousExtension, "ext=.nes"),
        ]);

        Assert.True(result.IsSoftOnly);
        Assert.NotEqual(SortDecision.Sort, result.SortDecision);
    }

    // ──────────────────────────────────────────────────────────────────
    //  E3b: AMBIGUOUS tier-based guard
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void AMBIGUOUS_TwoTier2Sources_SamePriority_TriggersAmbiguous()
    {
        // Two ArchiveContent hypotheses (both Tier2, same priority) → AMBIGUOUS when comparable.
        var result = HypothesisResolver.Resolve([
            new("GBA", DetectionSource.ArchiveContent.ConfidenceRating(), DetectionSource.ArchiveContent, "archive-inner=.gba"),
            new("NDS", DetectionSource.ArchiveContent.ConfidenceRating(), DetectionSource.ArchiveContent, "archive-inner=.nds"),
        ]);

        Assert.Equal("AMBIGUOUS", result.ConsoleKey);
        Assert.True(result.HasConflict);
        Assert.Equal(SortDecision.Blocked, result.SortDecision);
    }

    [Fact]
    public void AMBIGUOUS_TwoTier1Sources_TriggersAmbiguous()
    {
        // DiscHeader vs CartridgeHeader = both Tier1 → AMBIGUOUS when comparable.
        var result = HypothesisResolver.Resolve([
            new("PS1", DetectionSource.DiscHeader.ConfidenceRating(), DetectionSource.DiscHeader, "disc-header=PS1"),
            new("PS2", DetectionSource.CartridgeHeader.ConfidenceRating(), DetectionSource.CartridgeHeader, "header=PS2"),
        ]);

        Assert.Equal("AMBIGUOUS", result.ConsoleKey);
        Assert.True(result.HasConflict);
        Assert.Equal(SortDecision.Blocked, result.SortDecision);
    }

    [Fact]
    public void AMBIGUOUS_TwoTier3_DoesNot_Trigger()
    {
        // FolderName vs FolderName = both Tier3 → does NOT trigger AMBIGUOUS
        // because Tier3 is below the Tier2 threshold for the AMBIGUOUS guard.
        var result = HypothesisResolver.Resolve([
            new("NES", DetectionSource.FolderName.ConfidenceRating(), DetectionSource.FolderName, "folder=NES"),
            new("SNES", DetectionSource.FolderName.ConfidenceRating(), DetectionSource.FolderName, "folder=SNES"),
        ]);

        Assert.NotEqual("AMBIGUOUS", result.ConsoleKey);
        Assert.True(result.HasConflict);
    }

    [Fact]
    public void AMBIGUOUS_Tier1VsTier3_DoesNot_Trigger()
    {
        // Hard evidence (DiscHeader T1) vs weak (FolderName T3) → not AMBIGUOUS,
        // hard evidence wins clearly.
        var result = HypothesisResolver.Resolve([
            new("PS1", DetectionSource.DiscHeader.ConfidenceRating(), DetectionSource.DiscHeader, "disc-header=PS1"),
            new("PS2", DetectionSource.FolderName.ConfidenceRating(), DetectionSource.FolderName, "folder=PS2"),
        ]);

        Assert.NotEqual("AMBIGUOUS", result.ConsoleKey);
        Assert.Equal("PS1", result.ConsoleKey);
        Assert.True(result.HasHardEvidence);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Regression: Preview/Execute parity invariant
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_SameInput_SameOutput_Deterministic()
    {
        // Same hypotheses must always produce the same result.
        var hypotheses = new List<DetectionHypothesis>
        {
            new("PS1", DetectionSource.DiscHeader.ConfidenceRating(), DetectionSource.DiscHeader, "disc-header=PS1"),
            new("PS2", DetectionSource.FolderName.ConfidenceRating(), DetectionSource.FolderName, "folder=PS2"),
        };

        var result1 = HypothesisResolver.Resolve(hypotheses);
        var result2 = HypothesisResolver.Resolve(hypotheses);

        Assert.Equal(result1.ConsoleKey, result2.ConsoleKey);
        Assert.Equal(result1.Confidence, result2.Confidence);
        Assert.Equal(result1.SortDecision, result2.SortDecision);
        Assert.Equal(result1.HasHardEvidence, result2.HasHardEvidence);
    }

    [Fact]
    public void NoProductionCode_CastsEnumToConfidence()
    {
        // Regression guard: ConfidenceRating() values should NOT equal enum ordinals
        // for sources where we intentionally decoupled them.
        var decoupled = new[]
        {
            DetectionSource.UniqueExtension,
            DetectionSource.FolderName,
            DetectionSource.ArchiveContent,
            DetectionSource.FilenameKeyword,
            DetectionSource.AmbiguousExtension,
        };

        foreach (var source in decoupled)
        {
            Assert.NotEqual((int)source, source.ConfidenceRating());
        }
    }
}
