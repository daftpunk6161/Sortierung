using Romulus.Contracts.Models;
using Romulus.Core.Classification;
using Xunit;

namespace Romulus.Tests;

public sealed class MatchKindTests
{
    [Theory]
    [InlineData(MatchKind.ExactDatHash, EvidenceTier.Tier0_ExactDat)]
    [InlineData(MatchKind.ArchiveInnerExactDat, EvidenceTier.Tier0_ExactDat)]
    [InlineData(MatchKind.DiscHeaderSignature, EvidenceTier.Tier1_Structural)]
    [InlineData(MatchKind.CartridgeHeaderMagic, EvidenceTier.Tier1_Structural)]
    [InlineData(MatchKind.UniqueExtensionMatch, EvidenceTier.Tier2_StrongHeuristic)]
    [InlineData(MatchKind.ArchiveContentExtension, EvidenceTier.Tier2_StrongHeuristic)]
    [InlineData(MatchKind.FolderNameMatch, EvidenceTier.Tier3_WeakHeuristic)]
    [InlineData(MatchKind.FilenameKeywordMatch, EvidenceTier.Tier3_WeakHeuristic)]
    [InlineData(MatchKind.None, EvidenceTier.Tier4_Unknown)]
    public void GetTier_MapsMatchKindToExpectedEvidenceTier(MatchKind kind, EvidenceTier expected)
    {
        Assert.Equal(expected, kind.GetTier());
    }

    [Theory]
    [InlineData(DetectionSource.DatHash, MatchKind.ExactDatHash)]
    [InlineData(DetectionSource.DiscHeader, MatchKind.DiscHeaderSignature)]
    [InlineData(DetectionSource.ArchiveContent, MatchKind.ArchiveContentExtension)]
    [InlineData(DetectionSource.FilenameKeyword, MatchKind.FilenameKeywordMatch)]
    public void ResolveRecognition_MapsDetectionSourceToSignalMatchKind(
        DetectionSource source,
        MatchKind expectedKind)
    {
        var recognition = HypothesisResolver.ResolveRecognition([
            new DetectionHypothesis("NES", (int)source, source, "evidence")
        ]);

        var signal = Assert.Single(recognition.Signals);
        Assert.Equal(expectedKind, signal.Kind);
        Assert.Equal(expectedKind.GetTier(), signal.Tier);
    }
}
