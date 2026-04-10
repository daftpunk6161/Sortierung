using Romulus.Contracts.Models;
using Romulus.Infrastructure.Conversion;
using Xunit;

namespace Romulus.Tests.Conversion;

public sealed class RvzFormatHelperTests : IDisposable
{
    private readonly string _root;

    public RvzFormatHelperTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "Romulus.RvzFormatHelperTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void VerifyMagicBytes_ValidRvz_ReturnsTrue()
    {
        var path = Path.Combine(_root, "valid.rvz");
        File.WriteAllBytes(path, [(byte)'R', (byte)'V', (byte)'Z', 0x01, 0x00]);

        Assert.True(RvzFormatHelper.VerifyMagicBytes(path));
    }

    [Fact]
    public void VerifyMagicBytes_InvalidMagic_ReturnsFalse()
    {
        var path = Path.Combine(_root, "invalid.rvz");
        File.WriteAllBytes(path, [0x00, 0x01, 0x02, 0x03, 0x04]);

        Assert.False(RvzFormatHelper.VerifyMagicBytes(path));
    }

    [Fact]
    public void VerifyMagicBytes_EmptyFile_ReturnsFalse()
    {
        var path = Path.Combine(_root, "empty.rvz");
        File.WriteAllBytes(path, []);

        Assert.False(RvzFormatHelper.VerifyMagicBytes(path));
    }

    [Fact]
    public void BuildDolphinRvzArguments_UsesDefaultsForInvalidValues()
    {
        var capability = new ConversionCapability
        {
            SourceExtension = ".iso",
            TargetExtension = ".rvz",
            Tool = new ToolRequirement { ToolName = "dolphintool" },
            Command = "convert",
            CompressionAlgorithm = "bad value",
            CompressionLevel = -1,
            BlockSize = 1,
            ResultIntegrity = SourceIntegrity.Lossless,
            Lossless = true,
            Cost = 0,
            Verification = VerificationMethod.RvzMagicByte,
            Condition = ConversionCondition.None
        };

        var args = RvzFormatHelper.BuildDolphinRvzArguments("convert", "input.iso", "output.rvz", capability);

        Assert.Equal(RvzFormatHelper.DefaultCompressionAlgorithm, args[Array.IndexOf(args, "-c") + 1]);
        Assert.Equal(RvzFormatHelper.DefaultCompressionLevel.ToString(), args[Array.IndexOf(args, "-l") + 1]);
        Assert.Equal(RvzFormatHelper.DefaultBlockSize.ToString(), args[Array.IndexOf(args, "-b") + 1]);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }
        catch
        {
        }
    }
}
