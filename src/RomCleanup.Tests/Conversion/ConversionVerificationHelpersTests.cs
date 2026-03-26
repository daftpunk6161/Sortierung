using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Orchestration;
using Xunit;

namespace RomCleanup.Tests.Conversion;

/// <summary>
/// Tests for ConversionVerificationHelpers — extracted from the duplicate
/// private methods in WinnerConversionPipelinePhase and ConvertOnlyPipelinePhase.
/// </summary>
public sealed class ConversionVerificationHelpersTests
{
    private static ConversionResult MakeResult(
        string sourcePath = "/src.iso",
        string? targetPath = null,
        ConversionOutcome outcome = ConversionOutcome.Success,
        VerificationStatus verification = VerificationStatus.NotAttempted,
        ConversionPlan? plan = null)
    {
        return new ConversionResult(sourcePath, targetPath, outcome)
        {
            VerificationResult = verification,
            Plan = plan
        };
    }

    #region IsVerificationSuccessful

    [Fact]
    public void IsVerificationSuccessful_NullTargetPath_ReturnsFalse()
    {
        var result = MakeResult(targetPath: null, verification: VerificationStatus.Verified);

        Assert.False(ConversionVerificationHelpers.IsVerificationSuccessful(
            result, new StubConverter(verifyReturns: true), null));
    }

    [Fact]
    public void IsVerificationSuccessful_Verified_ReturnsTrue()
    {
        var result = MakeResult(targetPath: "/some/path.chd", verification: VerificationStatus.Verified);

        Assert.True(ConversionVerificationHelpers.IsVerificationSuccessful(
            result, new StubConverter(verifyReturns: false), null));
    }

    [Fact]
    public void IsVerificationSuccessful_Failed_ReturnsFalse()
    {
        var result = MakeResult(targetPath: "/some/path.chd", verification: VerificationStatus.VerifyFailed);

        Assert.False(ConversionVerificationHelpers.IsVerificationSuccessful(
            result, new StubConverter(verifyReturns: true), null));
    }

    [Fact]
    public void IsVerificationSuccessful_NotAttempted_NullTarget_ReturnsFalse()
    {
        var result = MakeResult(targetPath: "/some/path.chd", verification: VerificationStatus.NotAttempted);

        Assert.False(ConversionVerificationHelpers.IsVerificationSuccessful(
            result, new StubConverter(verifyReturns: true), null));
    }

    [Fact]
    public void IsVerificationSuccessful_NotAttempted_WithTarget_DelegatesToConverter()
    {
        var result = MakeResult(targetPath: "/some/path.chd", verification: VerificationStatus.NotAttempted);
        var target = new ConversionTarget(".chd", "chdman", "createcd");

        Assert.True(ConversionVerificationHelpers.IsVerificationSuccessful(
            result, new StubConverter(verifyReturns: true), target));

        Assert.False(ConversionVerificationHelpers.IsVerificationSuccessful(
            result, new StubConverter(verifyReturns: false), target));
    }

    #endregion

    #region ResolveToolName

    [Fact]
    public void ResolveToolName_PlanHasToolName_ReturnsPlanTool()
    {
        var capability = new ConversionCapability
        {
            Tool = new ToolRequirement { ToolName = "chdman" },
            SourceExtension = ".iso",
            TargetExtension = ".chd",
            Command = "createcd",
            ResultIntegrity = SourceIntegrity.Lossless,
            Lossless = true,
            Cost = 1,
            Verification = VerificationMethod.ChdmanVerify
        };

        var plan = new ConversionPlan
        {
            SourcePath = "/x.iso",
            ConsoleKey = "PS1",
            Policy = ConversionPolicy.Auto,
            SourceIntegrity = SourceIntegrity.Lossless,
            Safety = ConversionSafety.Safe,
            Steps =
            [
                new ConversionStep
                {
                    Order = 0,
                    InputExtension = ".iso",
                    OutputExtension = ".chd",
                    Capability = capability,
                    IsIntermediate = false
                }
            ]
        };

        var result = MakeResult(plan: plan);

        Assert.Equal("chdman", ConversionVerificationHelpers.ResolveToolName(result, null));
    }

    [Fact]
    public void ResolveToolName_NoPlan_UsesFallbackTarget()
    {
        var result = MakeResult();
        var target = new ConversionTarget(".rvz", "dolphintool", "convert");

        Assert.Equal("dolphintool", ConversionVerificationHelpers.ResolveToolName(result, target));
    }

    [Fact]
    public void ResolveToolName_NoPlanNoTarget_ReturnsUnknown()
    {
        var result = MakeResult();

        Assert.Equal("unknown", ConversionVerificationHelpers.ResolveToolName(result, null));
    }

    #endregion

    #region Stub

    private sealed class StubConverter(bool verifyReturns) : IFormatConverter
    {
        public ConversionTarget? GetTargetFormat(string consoleKey, string extension) => null;

        public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken ct = default)
            => new(sourcePath, null, ConversionOutcome.Skipped);

        public bool Verify(string outputPath, ConversionTarget target) => verifyReturns;
    }

    #endregion
}
