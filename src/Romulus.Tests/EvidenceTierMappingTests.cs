using Romulus.Contracts.Models;
using Romulus.Core.Classification;
using Xunit;

namespace Romulus.Tests;

public sealed class EvidenceTierMappingTests
{
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
    public void ToEvidenceTier_MapsEachDetectionSource(DetectionSource source, EvidenceTier expectedTier)
    {
        Assert.Equal(expectedTier, source.ToEvidenceTier());
    }

    [Theory]
    [InlineData(DetectionSource.DatHash, true)]
    [InlineData(DetectionSource.DiscHeader, true)]
    [InlineData(DetectionSource.CartridgeHeader, true)]
    [InlineData(DetectionSource.SerialNumber, true)]
    [InlineData(DetectionSource.UniqueExtension, false)]
    [InlineData(DetectionSource.ArchiveContent, false)]
    [InlineData(DetectionSource.FolderName, false)]
    [InlineData(DetectionSource.FilenameKeyword, false)]
    [InlineData(DetectionSource.AmbiguousExtension, false)]
    public void IsHardEvidence_MatchesDatFirstPolicy(DetectionSource source, bool expected)
    {
        Assert.Equal(expected, source.IsHardEvidence());
    }
}
