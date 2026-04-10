using Romulus.Contracts.Models;
using Romulus.Core.Classification;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Sorting;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Tests for the family decision gate code review fixes.
/// Covers: DecisionResolver restructure, ClassifyConflictType for Unknown families,
/// ToSafeReasonSegment hardening, BuildSortReasonTag centralization,
/// ConsoleSortUnknown projection parity.
/// </summary>
public sealed class FamilyDecisionGateFixTests
{
    // ──────────────────────────────────────────────────────────────────
    //  F1+F7: DecisionResolver – cross-family gate universal
    // ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(EvidenceTier.Tier0_ExactDat)]
    [InlineData(EvidenceTier.Tier1_Structural)]
    [InlineData(EvidenceTier.Tier2_StrongHeuristic)]
    [InlineData(EvidenceTier.Tier3_WeakHeuristic)]
    [InlineData(EvidenceTier.Tier4_Unknown)]
    public void DecisionResolver_CrossFamily_AlwaysBlocked_AllTiers(EvidenceTier tier)
    {
        var result = DecisionResolver.Resolve(tier, hasConflict: true, confidence: 100,
            datAvailable: true, conflictType: ConflictType.CrossFamily);

        Assert.Equal(DecisionClass.Blocked, result);
    }

    [Fact]
    public void DecisionResolver_CrossFamily_WithConflict_LowConfidence_StillBlocked()
    {
        // CrossFamily + hasConflict = true → always Blocked even at low confidence
        var result = DecisionResolver.Resolve(
            EvidenceTier.Tier1_Structural, hasConflict: true, confidence: 10,
            datAvailable: false, conflictType: ConflictType.CrossFamily);

        Assert.Equal(DecisionClass.Blocked, result);
    }

    [Theory]
    [InlineData(ConflictType.CrossFamily, DecisionClass.Blocked)]
    [InlineData(ConflictType.IntraFamily, DecisionClass.Review)]
    public void DecisionResolver_ConflictTypeNormalizesHasConflict(ConflictType conflictType, DecisionClass expected)
    {
        // Even when hasConflict=false, a non-None conflictType must be respected.
        // The guard normalizes hasConflict to true, preventing accidental bypass.
        var result = DecisionResolver.Resolve(
            EvidenceTier.Tier0_ExactDat, hasConflict: false, confidence: 100,
            datAvailable: true, conflictType: conflictType);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void DecisionResolver_ConflictTypeNone_DoesNotForceConflict()
    {
        var result = DecisionResolver.Resolve(
            EvidenceTier.Tier0_ExactDat,
            hasConflict: false,
            confidence: 100,
            datAvailable: true,
            conflictType: ConflictType.None);

        Assert.Equal(DecisionClass.DatVerified, result);
    }

    // ──────────────────────────────────────────────────────────────────
    //  F4: ClassifyConflictType – Unknown family handling
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ClassifyConflictType_WinnerUnknownFamily_ReturnsNone()
    {
        PlatformFamily lookup(string key) => PlatformFamily.Unknown;

        var hypotheses = new List<DetectionHypothesis>
        {
            new("MYSTERY1", 90, DetectionSource.FolderName, "folder=unknown"),
            new("MYSTERY2", 85, DetectionSource.FolderName, "folder=unknown2"),
        };

        var result = HypothesisResolver.Resolve(hypotheses, lookup);

        Assert.True(result.HasConflict);
        Assert.Equal(ConflictType.None, result.ConflictType);
    }

    [Fact]
    public void ClassifyConflictType_AllCompetitorsUnknown_ReturnsNone()
    {
        PlatformFamily lookup(string key) => key == "PS1" ? PlatformFamily.RedumpDisc : PlatformFamily.Unknown;

        var hypotheses = new List<DetectionHypothesis>
        {
            new("PS1", 90, DetectionSource.DiscHeader, "disc-header=PS1"),
            new("CUSTOM1", 80, DetectionSource.FolderName, "folder=custom"),
            new("CUSTOM2", 75, DetectionSource.FolderName, "folder=custom2"),
        };

        var result = HypothesisResolver.Resolve(hypotheses, lookup);

        Assert.True(result.HasConflict);
        // All competitors Unknown → None (not IntraFamily)
        Assert.Equal(ConflictType.None, result.ConflictType);
    }

    [Fact]
    public void ClassifyConflictType_KnownCompetitor_SameFamily_ReturnsIntraFamily()
    {
        // PS1 vs PS2 (both RedumpDisc) → IntraFamily
        PlatformFamily lookup(string key) => key switch
        {
            "PS1" => PlatformFamily.RedumpDisc,
            "PS2" => PlatformFamily.RedumpDisc,
            _ => PlatformFamily.Unknown
        };

        var hypotheses = new List<DetectionHypothesis>
        {
            new("PS1", 90, DetectionSource.DiscHeader, "disc-header=PS1"),
            new("PS2", 85, DetectionSource.FolderName, "folder=ps2"),
        };

        var result = HypothesisResolver.Resolve(hypotheses, lookup);

        Assert.True(result.HasConflict);
        Assert.Equal(ConflictType.IntraFamily, result.ConflictType);
    }

    // ──────────────────────────────────────────────────────────────────
    //  F6: ToSafeReasonSegment – Windows reserved names & traversal
    // ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("CON", "fallback")]
    [InlineData("con", "fallback")]
    [InlineData("NUL", "fallback")]
    [InlineData("PRN", "fallback")]
    [InlineData("AUX", "fallback")]
    [InlineData("COM1", "fallback")]
    [InlineData("LPT3", "fallback")]
    public void ToSafeReasonSegment_ReservedWindowsName_ReturnsFallback(string reason, string expected)
    {
        var result = ConsoleSorter.ToSafeReasonSegment(reason, "fallback");
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("..", "fallback")]          // pure traversal → regex strips both dots → empty → fallback
    [InlineData("../etc/passwd", "etc-passwd")]  // dots/slashes stripped by regex → safe segment
    [InlineData("..\\system32", "system32")]     // backslash stripped by regex → safe segment
    public void ToSafeReasonSegment_PathTraversal_ProducesSafeOutput(string reason, string expected)
    {
        var result = ConsoleSorter.ToSafeReasonSegment(reason, "fallback");
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null, "fallback")]
    [InlineData("", "fallback")]
    [InlineData("   ", "fallback")]
    public void ToSafeReasonSegment_NullOrEmpty_ReturnsFallback(string? reason, string expected)
    {
        var result = ConsoleSorter.ToSafeReasonSegment(reason, "fallback");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ToSafeReasonSegment_ValidReason_ReturnsNormalized()
    {
        var result = ConsoleSorter.ToSafeReasonSegment("Cross Family Conflict!", "fallback");
        Assert.Equal("cross-family-conflict", result);
    }

    [Fact]
    public void ToSafeReasonSegment_LongReason_TruncatedTo64()
    {
        var longReason = new string('a', 100);
        var result = ConsoleSorter.ToSafeReasonSegment(longReason, "fallback");
        Assert.Equal(64, result.Length);
    }

    // ──────────────────────────────────────────────────────────────────
    //  F9: BuildSortReasonTag – centralized in ConsoleSorter
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildSortReasonTag_CrossFamilyConflict_ReturnsCrossFamilyTag()
    {
        var candidate = CreateCandidate(SortDecision.Blocked, conflictType: ConflictType.CrossFamily);

        Assert.Equal("cross-family-conflict", ConsoleSorter.BuildSortReasonTag(candidate));
    }

    [Fact]
    public void BuildSortReasonTag_IntraFamilyConflict_ReturnsIntraFamilyTag()
    {
        var candidate = CreateCandidate(SortDecision.Review, conflictType: ConflictType.IntraFamily);

        Assert.Equal("intra-family-conflict", ConsoleSorter.BuildSortReasonTag(candidate));
    }

    [Fact]
    public void BuildSortReasonTag_Unknown_ReturnsInsufficientEvidence()
    {
        var candidate = CreateCandidate(SortDecision.Unknown);

        Assert.Equal("insufficient-evidence", ConsoleSorter.BuildSortReasonTag(candidate));
    }

    [Fact]
    public void BuildSortReasonTag_BlockedJunk_ReturnsJunkCategory()
    {
        var candidate = CreateCandidate(SortDecision.Blocked, category: FileCategory.Junk);

        Assert.Equal("junk-category", ConsoleSorter.BuildSortReasonTag(candidate));
    }

    // ──────────────────────────────────────────────────────────────────
    //  F2+F8: ConsoleSortUnknown projection parity
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void RunProjection_ConsoleSortUnknown_PropagatesFromResult()
    {
        var result = new RunResult
        {
            Status = "ok",
            ConsoleSortResult = new ConsoleSortResult(
                Total: 10, Moved: 5, SetMembersMoved: 0, Skipped: 0,
                Unknown: 3, UnknownReasons: new Dictionary<string, int> { ["low-confidence"] = 3 },
                Blocked: 2)
        };

        var projection = RunProjectionFactory.Create(result);

        Assert.Equal(3, projection.ConsoleSortUnknown);
        Assert.Equal(2, projection.ConsoleSortBlocked);
        Assert.Equal(5, projection.ConsoleSortMoved);
    }

    [Fact]
    public void RunProjection_NoConsoleSortResult_UnknownIsZero()
    {
        var result = new RunResult { Status = "ok" };

        var projection = RunProjectionFactory.Create(result);

        Assert.Equal(0, projection.ConsoleSortUnknown);
        Assert.Equal(0, projection.ConsoleSortBlocked);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────────

    private static RomCandidate CreateCandidate(
        SortDecision sortDecision,
        string reasoning = "",
        ConflictType conflictType = ConflictType.None,
        FileCategory category = FileCategory.Game)
    {
        return new RomCandidate
        {
            MainPath = "test.rom",
            SizeBytes = 1024,
            SortDecision = sortDecision,
            DetectionConflictType = conflictType,
            Category = category,
            MatchEvidence = new MatchEvidence
            {
                Level = MatchLevel.None,
                Reasoning = reasoning,
                HasHardEvidence = false,
                HasConflict = conflictType != ConflictType.None,
            },
            PrimaryMatchKind = MatchKind.None,
            ClassificationReasonCode = "test",
        };
    }
}
