using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;

namespace RomCleanup.Infrastructure.Orchestration;

/// <summary>
/// Shared verification and tool-name resolution helpers for conversion pipeline phases.
/// Extracted from WinnerConversionPipelinePhase and ConvertOnlyPipelinePhase to eliminate
/// duplicated private static methods.
/// </summary>
internal static class ConversionVerificationHelpers
{
    /// <summary>
    /// Determines whether a conversion result represents a successfully verified output.
    /// Checks the embedded VerificationResult first; falls back to the converter's Verify method
    /// if the result was NotAttempted and a target format is available.
    /// </summary>
    internal static bool IsVerificationSuccessful(ConversionResult convResult, IFormatConverter converter, ConversionTarget? target)
    {
        if (convResult.TargetPath is null)
            return false;

        if (convResult.VerificationResult != VerificationStatus.NotAttempted)
            return convResult.VerificationResult == VerificationStatus.Verified;

        if (target is null)
            return false;

        return converter.Verify(convResult.TargetPath, target);
    }

    /// <summary>
    /// Resolves the tool name from a conversion result's plan, or falls back to the target's tool name.
    /// </summary>
    internal static string ResolveToolName(ConversionResult convResult, ConversionTarget? fallbackTarget)
    {
        var plannedTool = convResult.Plan?.Steps.FirstOrDefault()?.Capability?.Tool?.ToolName;
        if (!string.IsNullOrWhiteSpace(plannedTool))
            return plannedTool;

        return fallbackTarget?.ToolName ?? "unknown";
    }
}
