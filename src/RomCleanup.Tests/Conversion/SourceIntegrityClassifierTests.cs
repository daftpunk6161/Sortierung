using RomCleanup.Contracts.Models;
using RomCleanup.Core.Conversion;
using Xunit;

namespace RomCleanup.Tests.Conversion;

public sealed class SourceIntegrityClassifierTests
{
    [Theory]
    [InlineData(".cue", SourceIntegrity.Lossless)]
    [InlineData(".bin", SourceIntegrity.Lossless)]
    [InlineData(".iso", SourceIntegrity.Lossless)]
    [InlineData(".img", SourceIntegrity.Lossless)]
    [InlineData(".gdi", SourceIntegrity.Lossless)]
    [InlineData(".gcm", SourceIntegrity.Lossless)]
    [InlineData(".wbfs", SourceIntegrity.Lossless)]
    [InlineData(".gcz", SourceIntegrity.Lossless)]
    [InlineData(".wia", SourceIntegrity.Lossless)]
    [InlineData(".wud", SourceIntegrity.Lossless)]
    [InlineData(".cso", SourceIntegrity.Lossy)]
    [InlineData(".pbp", SourceIntegrity.Lossy)]
    [InlineData(".cdi", SourceIntegrity.Lossy)]
    [InlineData(".mystery", SourceIntegrity.Unknown)]
    [InlineData("", SourceIntegrity.Unknown)]
    public void Classify_ByExtension_ReturnsExpectedIntegrity(string extension, SourceIntegrity expected)
    {
        var result = SourceIntegrityClassifier.Classify(extension);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Classify_NkitFilename_ForcesLossy()
    {
        var result = SourceIntegrityClassifier.Classify(".iso", "My.Game.nkit.iso");
        Assert.Equal(SourceIntegrity.Lossy, result);
    }
}
