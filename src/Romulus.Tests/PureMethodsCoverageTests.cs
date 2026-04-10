using Xunit;
using Romulus.Core.GameKeys;
using Romulus.Core.Classification;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Conversion;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for pure/static methods across Core and Contracts:
/// FolderKeyNormalizer, SourcePathFormatDetector, DecisionClass/MatchKind/CollectionMergeDecision extensions,
/// DetectionSource extensions, HypothesisResolver internal helpers.
/// </summary>
public sealed class PureMethodsCoverageTests
{
    #region FolderKeyNormalizer.GetFolderBaseKey

    [Fact]
    public void GetFolderBaseKey_Null_ReturnsEmpty()
        => Assert.Equal("", FolderKeyNormalizer.GetFolderBaseKey(null!));

    [Fact]
    public void GetFolderBaseKey_Empty_ReturnsEmpty()
        => Assert.Equal("", FolderKeyNormalizer.GetFolderBaseKey(""));

    [Fact]
    public void GetFolderBaseKey_WhitespaceOnly_ReturnsFallback()
    {
        // Whitespace collapses to empty → fallback to original trimmed + toLower
        var result = FolderKeyNormalizer.GetFolderBaseKey("   ");
        Assert.NotNull(result);
    }

    [Fact]
    public void GetFolderBaseKey_SimpleName_ReturnsLowercase()
        => Assert.Equal("super mario world", FolderKeyNormalizer.GetFolderBaseKey("Super Mario World"));

    [Fact]
    public void GetFolderBaseKey_StripsBracketTags()
    {
        var result = FolderKeyNormalizer.GetFolderBaseKey("Game [v1.0] [crack]");
        Assert.DoesNotContain("[", result);
        Assert.DoesNotContain("v1.0", result);
    }

    [Fact]
    public void GetFolderBaseKey_StripsNonPreservedParenTags()
    {
        var result = FolderKeyNormalizer.GetFolderBaseKey("Game (USA) (En)");
        Assert.DoesNotContain("USA", result);
        Assert.DoesNotContain("En", result);
    }

    [Fact]
    public void GetFolderBaseKey_PreservesDiscMarker()
    {
        var result = FolderKeyNormalizer.GetFolderBaseKey("Game (Disc 1) (USA)");
        Assert.Contains("disc 1", result);
    }

    [Fact]
    public void GetFolderBaseKey_PreservesSideMarker()
    {
        var result = FolderKeyNormalizer.GetFolderBaseKey("Game (Side A) (En)");
        Assert.Contains("side a", result);
    }

    [Fact]
    public void GetFolderBaseKey_PreservesCdMarker()
    {
        var result = FolderKeyNormalizer.GetFolderBaseKey("Game (CD2) (USA)");
        Assert.Contains("cd2", result);
    }

    [Fact]
    public void GetFolderBaseKey_PreservesAGA()
    {
        var result = FolderKeyNormalizer.GetFolderBaseKey("Game (AGA) (En)");
        Assert.Contains("aga", result);
    }

    [Fact]
    public void GetFolderBaseKey_PreservesNTSC()
    {
        var result = FolderKeyNormalizer.GetFolderBaseKey("Game (NTSC) (En)");
        Assert.Contains("ntsc", result);
    }

    [Fact]
    public void GetFolderBaseKey_PreservesPAL()
    {
        var result = FolderKeyNormalizer.GetFolderBaseKey("Game (PAL) (En)");
        Assert.Contains("pal", result);
    }

    [Fact]
    public void GetFolderBaseKey_StripsVersionSuffix()
    {
        var result = FolderKeyNormalizer.GetFolderBaseKey("Game v2.1.0");
        Assert.DoesNotContain("2.1.0", result);
        Assert.StartsWith("game", result);
    }

    [Fact]
    public void GetFolderBaseKey_CollapsesMultipleSpaces()
    {
        var result = FolderKeyNormalizer.GetFolderBaseKey("Game    Title");
        Assert.Equal("game title", result);
    }

    [Fact]
    public void GetFolderBaseKey_MultipleBrackets_AllStripped()
    {
        var result = FolderKeyNormalizer.GetFolderBaseKey("Game [tag1] [tag2]");
        Assert.DoesNotContain("[", result);
    }

    #endregion

    #region FolderKeyNormalizer.IsMultidiscFolder

    [Theory]
    [InlineData("Game Disc 1", true)]
    [InlineData("Game Disc1", true)]
    [InlineData("Game Disk 2", true)]
    [InlineData("Game CD3", true)]
    [InlineData("Game Side 1", true)]
    [InlineData("Not a disc game", false)]
    [InlineData("Game", false)]
    [InlineData("", false)]
    public void IsMultidiscFolder_VariousInputs(string folderName, bool expected)
        => Assert.Equal(expected, FolderKeyNormalizer.IsMultidiscFolder(folderName));

    #endregion

    #region SourcePathFormatDetector.ResolveSourceExtension

    [Fact]
    public void ResolveSourceExtension_NkitIso_ReturnsNkitIso()
        => Assert.Equal(".nkit.iso", SourcePathFormatDetector.ResolveSourceExtension(@"C:\roms\game.nkit.iso"));

    [Fact]
    public void ResolveSourceExtension_NkitGcz_ReturnsNkitGcz()
        => Assert.Equal(".nkit.gcz", SourcePathFormatDetector.ResolveSourceExtension(@"C:\roms\game.nkit.gcz"));

    [Fact]
    public void ResolveSourceExtension_RegularIso_ReturnsIso()
        => Assert.Equal(".iso", SourcePathFormatDetector.ResolveSourceExtension(@"C:\roms\game.iso"));

    [Fact]
    public void ResolveSourceExtension_Zip_ReturnsZip()
        => Assert.Equal(".zip", SourcePathFormatDetector.ResolveSourceExtension(@"C:\roms\game.zip"));

    [Fact]
    public void ResolveSourceExtension_CaseInsensitive_NkitIso()
        => Assert.Equal(".nkit.iso", SourcePathFormatDetector.ResolveSourceExtension(@"C:\roms\game.NKIT.ISO"));

    [Fact]
    public void ResolveSourceExtension_NullOrWhitespace_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => SourcePathFormatDetector.ResolveSourceExtension(null!));
        Assert.ThrowsAny<ArgumentException>(() => SourcePathFormatDetector.ResolveSourceExtension(""));
        Assert.ThrowsAny<ArgumentException>(() => SourcePathFormatDetector.ResolveSourceExtension("   "));
    }

    [Fact]
    public void ResolveSourceExtension_NoExtension_ReturnsEmpty()
        => Assert.Equal("", SourcePathFormatDetector.ResolveSourceExtension(@"C:\roms\game"));

    #endregion

    #region DecisionClass extensions

    [Theory]
    [InlineData(DecisionClass.Sort, SortDecision.Sort)]
    [InlineData(DecisionClass.DatVerified, SortDecision.DatVerified)]
    [InlineData(DecisionClass.Review, SortDecision.Review)]
    [InlineData(DecisionClass.Blocked, SortDecision.Blocked)]
    [InlineData(DecisionClass.Unknown, SortDecision.Unknown)]
    public void ToSortDecision_MapsCorrectly(DecisionClass input, SortDecision expected)
        => Assert.Equal(expected, input.ToSortDecision());

    [Theory]
    [InlineData(SortDecision.Sort, DecisionClass.Sort)]
    [InlineData(SortDecision.DatVerified, DecisionClass.DatVerified)]
    [InlineData(SortDecision.Review, DecisionClass.Review)]
    [InlineData(SortDecision.Blocked, DecisionClass.Blocked)]
    [InlineData(SortDecision.Unknown, DecisionClass.Unknown)]
    public void ToDecisionClass_MapsCorrectly(SortDecision input, DecisionClass expected)
        => Assert.Equal(expected, input.ToDecisionClass());

    [Fact]
    public void ToSortDecision_UndefinedValue_ReturnsUnknown()
        => Assert.Equal(SortDecision.Unknown, ((DecisionClass)999).ToSortDecision());

    [Fact]
    public void ToDecisionClass_UndefinedValue_ReturnsUnknown()
        => Assert.Equal(DecisionClass.Unknown, ((SortDecision)999).ToDecisionClass());

    #endregion

    #region MatchKind.GetTier

    [Theory]
    [InlineData(MatchKind.ExactDatHash, EvidenceTier.Tier0_ExactDat)]
    [InlineData(MatchKind.ArchiveInnerExactDat, EvidenceTier.Tier0_ExactDat)]
    [InlineData(MatchKind.HeaderlessDatHash, EvidenceTier.Tier0_ExactDat)]
    [InlineData(MatchKind.ChdRawDatHash, EvidenceTier.Tier0_ExactDat)]
    [InlineData(MatchKind.DiscHeaderSignature, EvidenceTier.Tier1_Structural)]
    [InlineData(MatchKind.CartridgeHeaderMagic, EvidenceTier.Tier1_Structural)]
    [InlineData(MatchKind.SerialNumberMatch, EvidenceTier.Tier1_Structural)]
    [InlineData(MatchKind.ChdMetadataTag, EvidenceTier.Tier1_Structural)]
    [InlineData(MatchKind.UniqueExtensionMatch, EvidenceTier.Tier2_StrongHeuristic)]
    [InlineData(MatchKind.ArchiveContentExtension, EvidenceTier.Tier2_StrongHeuristic)]
    [InlineData(MatchKind.DatNameOnlyMatch, EvidenceTier.Tier2_StrongHeuristic)]
    [InlineData(MatchKind.FolderNameMatch, EvidenceTier.Tier3_WeakHeuristic)]
    [InlineData(MatchKind.FilenameKeywordMatch, EvidenceTier.Tier3_WeakHeuristic)]
    [InlineData(MatchKind.AmbiguousExtensionSingle, EvidenceTier.Tier3_WeakHeuristic)]
    [InlineData(MatchKind.FilenameGuess, EvidenceTier.Tier3_WeakHeuristic)]
    [InlineData(MatchKind.None, EvidenceTier.Tier4_Unknown)]
    public void GetTier_AllMatchKinds_ReturnCorrectTier(MatchKind kind, EvidenceTier expected)
        => Assert.Equal(expected, kind.GetTier());

    [Fact]
    public void GetTier_UndefinedValue_ReturnsTier4()
        => Assert.Equal(EvidenceTier.Tier4_Unknown, ((MatchKind)9999).GetTier());

    #endregion

    #region CollectionMergeDecision.IsMutating

    [Theory]
    [InlineData(CollectionMergeDecision.CopyToTarget, true)]
    [InlineData(CollectionMergeDecision.MoveToTarget, true)]
    [InlineData(CollectionMergeDecision.KeepExistingTarget, false)]
    [InlineData(CollectionMergeDecision.SkipAsDuplicate, false)]
    [InlineData(CollectionMergeDecision.ReviewRequired, false)]
    [InlineData(CollectionMergeDecision.Blocked, false)]
    public void IsMutating_AllValues(CollectionMergeDecision decision, bool expected)
        => Assert.Equal(expected, decision.IsMutating());

    #endregion

    #region DetectionSource extensions

    [Theory]
    [InlineData(DetectionSource.DatHash, true)]
    [InlineData(DetectionSource.DiscHeader, true)]
    [InlineData(DetectionSource.CartridgeHeader, true)]
    [InlineData(DetectionSource.SerialNumber, true)]
    [InlineData(DetectionSource.UniqueExtension, false)]
    [InlineData(DetectionSource.FolderName, false)]
    [InlineData(DetectionSource.ArchiveContent, false)]
    [InlineData(DetectionSource.FilenameKeyword, false)]
    [InlineData(DetectionSource.AmbiguousExtension, false)]
    public void IsHardEvidence_AllSources(DetectionSource source, bool expected)
        => Assert.Equal(expected, source.IsHardEvidence());

    [Theory]
    [InlineData(DetectionSource.DatHash, EvidenceTier.Tier0_ExactDat)]
    [InlineData(DetectionSource.DiscHeader, EvidenceTier.Tier1_Structural)]
    [InlineData(DetectionSource.CartridgeHeader, EvidenceTier.Tier1_Structural)]
    [InlineData(DetectionSource.SerialNumber, EvidenceTier.Tier1_Structural)]
    [InlineData(DetectionSource.UniqueExtension, EvidenceTier.Tier2_StrongHeuristic)]
    [InlineData(DetectionSource.ArchiveContent, EvidenceTier.Tier2_StrongHeuristic)]
    [InlineData(DetectionSource.FolderName, EvidenceTier.Tier3_WeakHeuristic)]
    [InlineData(DetectionSource.FilenameKeyword, EvidenceTier.Tier3_WeakHeuristic)]
    [InlineData(DetectionSource.AmbiguousExtension, EvidenceTier.Tier3_WeakHeuristic)]
    public void ToEvidenceTier_AllSources(DetectionSource source, EvidenceTier expected)
        => Assert.Equal(expected, source.ToEvidenceTier());

    [Theory]
    [InlineData(DetectionSource.DatHash, 100)]
    [InlineData(DetectionSource.UniqueExtension, 95)]
    [InlineData(DetectionSource.DiscHeader, 92)]
    [InlineData(DetectionSource.CartridgeHeader, 90)]
    [InlineData(DetectionSource.SerialNumber, 75)]
    [InlineData(DetectionSource.ArchiveContent, 70)]
    [InlineData(DetectionSource.FolderName, 80)]
    [InlineData(DetectionSource.FilenameKeyword, 60)]
    [InlineData(DetectionSource.AmbiguousExtension, 55)]
    public void SingleSourceCap_AllSources(DetectionSource source, int expected)
        => Assert.Equal(expected, source.SingleSourceCap());

    [Fact]
    public void SingleSourceCap_UndefinedSource_Returns60()
        => Assert.Equal(60, ((DetectionSource)0).SingleSourceCap());

    #endregion

    #region HypothesisResolver.MapDetectionSourceToMatchKind

    [Theory]
    [InlineData(DetectionSource.DatHash, MatchKind.ExactDatHash)]
    [InlineData(DetectionSource.UniqueExtension, MatchKind.UniqueExtensionMatch)]
    [InlineData(DetectionSource.DiscHeader, MatchKind.DiscHeaderSignature)]
    [InlineData(DetectionSource.CartridgeHeader, MatchKind.CartridgeHeaderMagic)]
    [InlineData(DetectionSource.SerialNumber, MatchKind.SerialNumberMatch)]
    [InlineData(DetectionSource.FolderName, MatchKind.FolderNameMatch)]
    [InlineData(DetectionSource.ArchiveContent, MatchKind.ArchiveContentExtension)]
    [InlineData(DetectionSource.FilenameKeyword, MatchKind.FilenameKeywordMatch)]
    [InlineData(DetectionSource.AmbiguousExtension, MatchKind.AmbiguousExtensionSingle)]
    public void MapDetectionSourceToMatchKind_AllSources(DetectionSource source, MatchKind expected)
        => Assert.Equal(expected, HypothesisResolver.MapDetectionSourceToMatchKind(source));

    [Fact]
    public void MapDetectionSourceToMatchKind_UndefinedSource_ReturnsNone()
        => Assert.Equal(MatchKind.None, HypothesisResolver.MapDetectionSourceToMatchKind((DetectionSource)0));

    #endregion

    #region HypothesisResolver.DetermineSortDecision

    [Fact]
    public void DetermineSortDecision_HighConfidence_NoConflict_Structural_Sort()
    {
        var decision = HypothesisResolver.DetermineSortDecision(
            EvidenceTier.Tier1_Structural, 95, conflict: false);
        Assert.Equal(SortDecision.Sort, decision);
    }

    [Fact]
    public void DetermineSortDecision_LowConfidence_Unknown()
    {
        var decision = HypothesisResolver.DetermineSortDecision(
            EvidenceTier.Tier3_WeakHeuristic, 30, conflict: false);
        Assert.True(decision is SortDecision.Blocked or SortDecision.Unknown);
    }

    [Fact]
    public void DetermineSortDecision_CompatOverload_WithDatEvidence()
    {
        var decision = HypothesisResolver.DetermineSortDecision(
            95, conflict: false, hardEvidence: true, sourceCount: 1, hasDatEvidence: true);
        Assert.Equal(SortDecision.DatVerified, decision);
    }

    [Fact]
    public void DetermineSortDecision_CompatOverload_SoftOnly_Blocked()
    {
        var decision = HypothesisResolver.DetermineSortDecision(
            50, conflict: false, hardEvidence: false, sourceCount: 1, hasDatEvidence: false);
        Assert.Equal(SortDecision.Blocked, decision);
    }

    #endregion

    #region HypothesisResolver.ClassifyConflictType

    [Fact]
    public void ClassifyConflictType_SingleGroup_ReturnsNone()
    {
        var groups = MakeGroupList(("PS1", DetectionSource.DiscHeader, 90));
        Assert.Equal(ConflictType.None,
            HypothesisResolver.ClassifyConflictType(groups, _ => PlatformFamily.RedumpDisc));
    }

    [Fact]
    public void ClassifyConflictType_SameFamily_ReturnsIntraFamily()
    {
        var groups = MakeGroupList(
            ("PS1", DetectionSource.DiscHeader, 90),
            ("PS2", DetectionSource.DiscHeader, 60));
        Assert.Equal(ConflictType.IntraFamily,
            HypothesisResolver.ClassifyConflictType(groups, _ => PlatformFamily.RedumpDisc));
    }

    [Fact]
    public void ClassifyConflictType_DifferentFamily_ReturnsCrossFamily()
    {
        var groups = MakeGroupList(
            ("PS1", DetectionSource.DiscHeader, 90),
            ("GBA", DetectionSource.CartridgeHeader, 60));
        PlatformFamily FamilyLookup(string key) => key == "PS1" ? PlatformFamily.RedumpDisc : PlatformFamily.NoIntroCartridge;
        Assert.Equal(ConflictType.CrossFamily,
            HypothesisResolver.ClassifyConflictType(groups, FamilyLookup));
    }

    [Fact]
    public void ClassifyConflictType_WinnerUnknownFamily_ReturnsNone()
    {
        var groups = MakeGroupList(
            ("UNKNOWN", DetectionSource.FolderName, 70),
            ("PS1", DetectionSource.DiscHeader, 60));
        PlatformFamily FamilyLookup(string key) => key == "PS1" ? PlatformFamily.RedumpDisc : PlatformFamily.Unknown;
        Assert.Equal(ConflictType.None,
            HypothesisResolver.ClassifyConflictType(groups, FamilyLookup));
    }

    [Fact]
    public void ClassifyConflictType_CompetitorUnknownFamily_ReturnsNone()
    {
        var groups = MakeGroupList(
            ("PS1", DetectionSource.DiscHeader, 90),
            ("CUSTOM", DetectionSource.FolderName, 60));
        PlatformFamily FamilyLookup(string key) => key == "PS1" ? PlatformFamily.RedumpDisc : PlatformFamily.Unknown;
        Assert.Equal(ConflictType.None,
            HypothesisResolver.ClassifyConflictType(groups, FamilyLookup));
    }

    private static List<KeyValuePair<string, (int TotalConfidence, int MaxSingleConfidence, List<DetectionHypothesis> Items, int MaxSourcePriority)>> MakeGroupList(
        params (string Key, DetectionSource Source, int Confidence)[] entries)
    {
        return entries.Select(e =>
            new KeyValuePair<string, (int, int, List<DetectionHypothesis>, int)>(
                e.Key,
                (e.Confidence, e.Confidence,
                 new List<DetectionHypothesis> { new(e.Key, e.Confidence, e.Source, "test") },
                 (int)e.Source)))
            .ToList();
    }

    #endregion
}
