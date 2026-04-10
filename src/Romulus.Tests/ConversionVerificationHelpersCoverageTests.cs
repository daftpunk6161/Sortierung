using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for ConversionVerificationHelpers internal static methods.
/// </summary>
public sealed class ConversionVerificationHelpersCoverageTests
{
    // ═══ IsVerificationSuccessful ═════════════════════════════════════

    [Fact]
    public void IsVerificationSuccessful_NullTargetPath_ReturnsFalse()
    {
        var result = new ConversionResult("source.zip", null, ConversionOutcome.Success);
        var converter = new FakeConverter(verifyResult: true);

        Assert.False(ConversionVerificationHelpers.IsVerificationSuccessful(
            result, converter, new ConversionTarget(".chd", "chdman", "convert")));
    }

    [Fact]
    public void IsVerificationSuccessful_Verified_ReturnsTrue()
    {
        var result = new ConversionResult("source.zip", "target.chd", ConversionOutcome.Success)
        {
            VerificationResult = VerificationStatus.Verified
        };

        Assert.True(ConversionVerificationHelpers.IsVerificationSuccessful(
            result, new FakeConverter(false), null));
    }

    [Fact]
    public void IsVerificationSuccessful_VerifyFailed_ReturnsFalse()
    {
        var result = new ConversionResult("source.zip", "target.chd", ConversionOutcome.Success)
        {
            VerificationResult = VerificationStatus.VerifyFailed
        };

        Assert.False(ConversionVerificationHelpers.IsVerificationSuccessful(
            result, new FakeConverter(true), null));
    }

    [Fact]
    public void IsVerificationSuccessful_VerifyNotAvailable_ReturnsFalse()
    {
        var result = new ConversionResult("source.zip", "target.chd", ConversionOutcome.Success)
        {
            VerificationResult = VerificationStatus.VerifyNotAvailable
        };

        Assert.False(ConversionVerificationHelpers.IsVerificationSuccessful(
            result, new FakeConverter(true), null));
    }

    [Fact]
    public void IsVerificationSuccessful_NotAttempted_NullTarget_ReturnsFalse()
    {
        var result = new ConversionResult("source.zip", "target.chd", ConversionOutcome.Success)
        {
            VerificationResult = VerificationStatus.NotAttempted
        };

        Assert.False(ConversionVerificationHelpers.IsVerificationSuccessful(
            result, new FakeConverter(true), null));
    }

    [Fact]
    public void IsVerificationSuccessful_NotAttempted_FallsBackToConverter()
    {
        var result = new ConversionResult("source.zip", "target.chd", ConversionOutcome.Success)
        {
            VerificationResult = VerificationStatus.NotAttempted
        };
        var target = new ConversionTarget(".chd", "chdman", "convert");

        Assert.True(ConversionVerificationHelpers.IsVerificationSuccessful(
            result, new FakeConverter(verifyResult: true), target));
    }

    [Fact]
    public void IsVerificationSuccessful_NotAttempted_ConverterReturnsFalse()
    {
        var result = new ConversionResult("source.zip", "target.chd", ConversionOutcome.Success)
        {
            VerificationResult = VerificationStatus.NotAttempted
        };
        var target = new ConversionTarget(".chd", "chdman", "convert");

        Assert.False(ConversionVerificationHelpers.IsVerificationSuccessful(
            result, new FakeConverter(verifyResult: false), target));
    }

    // ═══ ResolveToolName ═════════════════════════════════════════════

    [Fact]
    public void ResolveToolName_FromPlan_ReturnsPlannedTool()
    {
        var result = new ConversionResult("source.zip", "target.chd", ConversionOutcome.Success)
        {
            Plan = new ConversionPlan
            {
                SourcePath = "source.zip",
                ConsoleKey = "PS1",
                Policy = ConversionPolicy.Auto,
                SourceIntegrity = SourceIntegrity.Unknown,
                Safety = ConversionSafety.Safe,
                Steps =
                [
                    new ConversionStep
                    {
                        Order = 1,
                        InputExtension = ".bin",
                        OutputExtension = ".chd",
                        IsIntermediate = false,
                        Capability = new ConversionCapability
                        {
                            SourceExtension = ".bin",
                            TargetExtension = ".chd",
                            Tool = new ToolRequirement { ToolName = "chdman" },
                            Command = "createcd",
                            ResultIntegrity = SourceIntegrity.Lossless,
                            Lossless = true,
                            Cost = 1,
                            Verification = VerificationMethod.ChdmanVerify
                        }
                    }
                ]
            }
        };

        Assert.Equal("chdman", ConversionVerificationHelpers.ResolveToolName(result, null));
    }

    [Fact]
    public void ResolveToolName_NoPlan_FallsBackToTarget()
    {
        var result = new ConversionResult("source.zip", "target.chd", ConversionOutcome.Success);
        var target = new ConversionTarget(".chd", "chdman-fallback", "convert");

        Assert.Equal("chdman-fallback", ConversionVerificationHelpers.ResolveToolName(result, target));
    }

    [Fact]
    public void ResolveToolName_NoPlanNoTarget_ReturnsUnknown()
    {
        var result = new ConversionResult("source.zip", "target.chd", ConversionOutcome.Success);

        Assert.Equal("unknown", ConversionVerificationHelpers.ResolveToolName(result, null));
    }

    [Fact]
    public void ResolveToolName_EmptySteps_FallsBackToTarget()
    {
        var result = new ConversionResult("source.zip", "target.chd", ConversionOutcome.Success)
        {
            Plan = new ConversionPlan
            {
                SourcePath = "source.zip",
                ConsoleKey = "PS1",
                Policy = ConversionPolicy.Auto,
                SourceIntegrity = SourceIntegrity.Unknown,
                Safety = ConversionSafety.Safe,
                Steps = []
            }
        };
        var target = new ConversionTarget(".chd", "fallback", "cmd");

        Assert.Equal("fallback", ConversionVerificationHelpers.ResolveToolName(result, target));
    }

    // ════════════════════════════════════════════════════════════════

    private sealed class FakeConverter(bool verifyResult) : IFormatConverter
    {
        public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension) => null;
        public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken cancellationToken = default)
            => new(sourcePath, null, ConversionOutcome.Skipped);
        public bool Verify(string targetPath, ConversionTarget target) => verifyResult;
    }
}
