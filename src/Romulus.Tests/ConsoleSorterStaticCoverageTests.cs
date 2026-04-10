using Romulus.Contracts.Models;
using Romulus.Infrastructure.Sorting;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Tests for ConsoleSorter internal static helpers: ToSafeReasonSegment, BuildSortReasonTag.
/// </summary>
public sealed class ConsoleSorterStaticCoverageTests
{
    #region ToSafeReasonSegment

    [Fact]
    public void ToSafeReasonSegment_Null_ReturnsFallback()
    {
        Assert.Equal("unknown", ConsoleSorter.ToSafeReasonSegment(null, "unknown"));
    }

    [Fact]
    public void ToSafeReasonSegment_Empty_ReturnsFallback()
    {
        Assert.Equal("fb", ConsoleSorter.ToSafeReasonSegment("", "fb"));
    }

    [Fact]
    public void ToSafeReasonSegment_Whitespace_ReturnsFallback()
    {
        Assert.Equal("fb", ConsoleSorter.ToSafeReasonSegment("   ", "fb"));
    }

    [Fact]
    public void ToSafeReasonSegment_SimpleText_LowercaseNormalized()
    {
        var result = ConsoleSorter.ToSafeReasonSegment("DatHash Match", "fb");
        Assert.Equal("dathash-match", result);
    }

    [Fact]
    public void ToSafeReasonSegment_SpecialChars_Stripped()
    {
        var result = ConsoleSorter.ToSafeReasonSegment("foo!@#bar", "fb");
        Assert.Equal("foo-bar", result);
    }

    [Fact]
    public void ToSafeReasonSegment_DoubleDot_Stripped()
    {
        // Path traversal defense
        var result = ConsoleSorter.ToSafeReasonSegment("..traversal..", "fb");
        Assert.DoesNotContain("..", result);
    }

    [Fact]
    public void ToSafeReasonSegment_WindowsReservedName_ReturnsFallback()
    {
        Assert.Equal("fb", ConsoleSorter.ToSafeReasonSegment("CON", "fb"));
        Assert.Equal("fb", ConsoleSorter.ToSafeReasonSegment("PRN", "fb"));
        Assert.Equal("fb", ConsoleSorter.ToSafeReasonSegment("NUL", "fb"));
        Assert.Equal("fb", ConsoleSorter.ToSafeReasonSegment("COM1", "fb"));
        Assert.Equal("fb", ConsoleSorter.ToSafeReasonSegment("LPT3", "fb"));
    }

    [Fact]
    public void ToSafeReasonSegment_LongString_TruncatedAt64()
    {
        var longReason = new string('a', 100);
        var result = ConsoleSorter.ToSafeReasonSegment(longReason, "fb");
        Assert.Equal(64, result.Length);
    }

    [Fact]
    public void ToSafeReasonSegment_ExactLength64_NotTruncated()
    {
        var reason = new string('x', 64);
        var result = ConsoleSorter.ToSafeReasonSegment(reason, "fb");
        Assert.Equal(64, result.Length);
    }

    #endregion

    #region BuildSortReasonTag

    [Fact]
    public void BuildSortReasonTag_CrossFamilyConflict_ReturnsTag()
    {
        var c = new RomCandidate { DetectionConflictType = ConflictType.CrossFamily };
        Assert.Equal("cross-family-conflict", ConsoleSorter.BuildSortReasonTag(c));
    }

    [Fact]
    public void BuildSortReasonTag_IntraFamilyConflict_ReturnsTag()
    {
        var c = new RomCandidate { DetectionConflictType = ConflictType.IntraFamily };
        Assert.Equal("intra-family-conflict", ConsoleSorter.BuildSortReasonTag(c));
    }

    [Fact]
    public void BuildSortReasonTag_UnknownDecision_ReturnsInsufficientEvidence()
    {
        var c = new RomCandidate { SortDecision = SortDecision.Unknown };
        Assert.Equal("insufficient-evidence", ConsoleSorter.BuildSortReasonTag(c));
    }

    [Fact]
    public void BuildSortReasonTag_JunkBlocked_ReturnsJunkCategory()
    {
        var c = new RomCandidate { SortDecision = SortDecision.Blocked, Category = FileCategory.Junk };
        Assert.Equal("junk-category", ConsoleSorter.BuildSortReasonTag(c));
    }

    [Fact]
    public void BuildSortReasonTag_WithReasoning_ReturnsReasoning()
    {
        var c = new RomCandidate
        {
            SortDecision = SortDecision.DatVerified,
            MatchEvidence = new MatchEvidence { Reasoning = "dat-hash-match" }
        };
        Assert.Equal("dat-hash-match", ConsoleSorter.BuildSortReasonTag(c));
    }

    [Fact]
    public void BuildSortReasonTag_WithMatchKind_ReturnsKindName()
    {
        var c = new RomCandidate
        {
            SortDecision = SortDecision.DatVerified,
            PrimaryMatchKind = MatchKind.ExactDatHash
        };
        Assert.Equal("ExactDatHash", ConsoleSorter.BuildSortReasonTag(c));
    }

    [Fact]
    public void BuildSortReasonTag_NoEvidence_FallsBackToClassificationReason()
    {
        var c = new RomCandidate
        {
            SortDecision = SortDecision.Blocked,
            Category = FileCategory.Game,
            PrimaryMatchKind = MatchKind.None,
            ClassificationReasonCode = "game-default"
        };
        Assert.Equal("game-default", ConsoleSorter.BuildSortReasonTag(c));
    }

    #endregion
}
