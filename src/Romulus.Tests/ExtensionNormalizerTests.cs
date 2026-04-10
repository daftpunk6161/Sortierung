using Romulus.Core.Classification;
using Xunit;

namespace Romulus.Tests;

public class ExtensionNormalizerTests
{
    [Theory]
    [InlineData("game.iso", ".iso")]
    [InlineData("game.chd", ".chd")]
    [InlineData("game.zip", ".zip")]
    [InlineData("game.7z", ".7z")]
    [InlineData("game.bin", ".bin")]
    public void SimpleExtension_ReturnsNormalized(string fileName, string expected)
    {
        Assert.Equal(expected, ExtensionNormalizer.GetNormalizedExtension(fileName));
    }

    [Theory]
    [InlineData("game.nkit.iso", ".nkit.iso")]
    [InlineData("game.nkit.gcz", ".nkit.gcz")]
    [InlineData("game.ecm.bin", ".ecm.bin")]
    [InlineData("game.wia.gcz", ".wia.gcz")]
    public void DoubleExtension_ReturnsCompound(string fileName, string expected)
    {
        Assert.Equal(expected, ExtensionNormalizer.GetNormalizedExtension(fileName));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("noext", "")]
    [InlineData(".", "")]
    public void EdgeCases_ReturnsEmpty(string fileName, string expected)
    {
        Assert.Equal(expected, ExtensionNormalizer.GetNormalizedExtension(fileName));
    }

    [Fact]
    public void CaseInsensitive_ReturnsLowerCase()
    {
        var result = ExtensionNormalizer.GetNormalizedExtension("Game.NKIT.ISO");
        Assert.Equal(".nkit.iso", result);
    }
}
