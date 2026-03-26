using RomCleanup.Contracts.Models;
using RomCleanup.Core.Conversion;
using Xunit;

namespace RomCleanup.Tests.Conversion;

public sealed class ConversionPolicyEvaluatorTests
{
    private readonly ConversionPolicyEvaluator _sut = new();

    [Theory]
    [InlineData("ARCADE")]
    [InlineData("NEOGEO")]
    public void GetEffectivePolicy_SetProtectedSystems_AlwaysNone(string console)
    {
        var policy = _sut.GetEffectivePolicy(console, ConversionPolicy.Auto);
        Assert.Equal(ConversionPolicy.None, policy);
    }

    [Fact]
    public void GetEffectivePolicy_NonProtected_ReturnsConfiguredPolicy()
    {
        var policy = _sut.GetEffectivePolicy("PS1", ConversionPolicy.ArchiveOnly);
        Assert.Equal(ConversionPolicy.ArchiveOnly, policy);
    }

    [Fact]
    public void EvaluateSafety_NonePolicy_Blocks()
    {
        var safety = _sut.EvaluateSafety(ConversionPolicy.None, SourceIntegrity.Lossless, [], allToolsAvailable: true);
        Assert.Equal(ConversionSafety.Blocked, safety);
    }

    [Fact]
    public void EvaluateSafety_MissingTool_Blocks()
    {
        var safety = _sut.EvaluateSafety(ConversionPolicy.Auto, SourceIntegrity.Lossless, [], allToolsAvailable: false);
        Assert.Equal(ConversionSafety.Blocked, safety);
    }

    [Fact]
    public void EvaluateSafety_ManualOnly_IsRisky()
    {
        var safety = _sut.EvaluateSafety(ConversionPolicy.ManualOnly, SourceIntegrity.Lossless, [], allToolsAvailable: true);
        Assert.Equal(ConversionSafety.Risky, safety);
    }

    [Fact]
    public void EvaluateSafety_UnknownIntegrity_AndLossyPath_Blocks()
    {
        var lossyEdge = new ConversionCapability
        {
            SourceExtension = ".cso",
            TargetExtension = ".iso",
            Tool = new ToolRequirement { ToolName = "ciso" },
            Command = "decompress",
            ResultIntegrity = SourceIntegrity.Lossy,
            Lossless = false,
            Cost = 5,
            Verification = VerificationMethod.FileExistenceCheck,
            Condition = ConversionCondition.None
        };

        var safety = _sut.EvaluateSafety(ConversionPolicy.Auto, SourceIntegrity.Unknown, [lossyEdge], allToolsAvailable: true);
        Assert.Equal(ConversionSafety.Blocked, safety);
    }

    [Fact]
    public void EvaluateSafety_LossyIntegrity_IsAcceptable()
    {
        var safety = _sut.EvaluateSafety(ConversionPolicy.Auto, SourceIntegrity.Lossy, [], allToolsAvailable: true);
        Assert.Equal(ConversionSafety.Acceptable, safety);
    }

    [Fact]
    public void EvaluateSafety_AutoLossless_IsSafe()
    {
        var safety = _sut.EvaluateSafety(ConversionPolicy.Auto, SourceIntegrity.Lossless, [], allToolsAvailable: true);
        Assert.Equal(ConversionSafety.Safe, safety);
    }
}
