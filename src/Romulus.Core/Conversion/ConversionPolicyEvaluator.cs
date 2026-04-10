namespace Romulus.Core.Conversion;

using Romulus.Contracts.Models;

/// <summary>
/// Applies policy and safety rules to planned conversion paths.
/// </summary>
public sealed class ConversionPolicyEvaluator
{
    private static readonly IReadOnlySet<string> SetProtectedSystems =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ARCADE", "NEOGEO" };

    /// <summary>
    /// Returns the effective policy with hard set-protection enforcement.
    /// </summary>
    public ConversionPolicy GetEffectivePolicy(string consoleKey, ConversionPolicy configuredPolicy)
    {
        if (SetProtectedSystems.Contains(consoleKey))
            return ConversionPolicy.None;

        return configuredPolicy;
    }

    /// <summary>
    /// Computes plan safety from policy, integrity and tool availability.
    /// </summary>
    public ConversionSafety EvaluateSafety(
        ConversionPolicy policy,
        SourceIntegrity integrity,
        IReadOnlyList<ConversionCapability> pathCapabilities,
        bool allToolsAvailable)
    {
        if (policy == ConversionPolicy.None)
            return ConversionSafety.Blocked;

        if (!allToolsAvailable)
            return ConversionSafety.Blocked;

        if (policy == ConversionPolicy.ManualOnly)
            return ConversionSafety.Risky;

        if (integrity == SourceIntegrity.Unknown && pathCapabilities.Any(c => !c.Lossless))
            return ConversionSafety.Blocked;

        if (integrity == SourceIntegrity.Lossy)
            return ConversionSafety.Acceptable;

        return ConversionSafety.Safe;
    }
}
