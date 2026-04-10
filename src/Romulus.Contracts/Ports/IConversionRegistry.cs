using Romulus.Contracts.Models;

namespace Romulus.Contracts.Ports;

/// <summary>
/// Provides configured conversion capabilities and policies.
/// </summary>
public interface IConversionRegistry
{
    /// <summary>Returns all registered conversion capabilities.</summary>
    IReadOnlyList<ConversionCapability> GetCapabilities();

    /// <summary>Returns conversion policy for a console key.</summary>
    ConversionPolicy GetPolicy(string consoleKey);

    /// <summary>Returns preferred target extension for a console key.</summary>
    string? GetPreferredTarget(string consoleKey);

    /// <summary>Returns optional alternative target extensions for a console key.</summary>
    IReadOnlyList<string> GetAlternativeTargets(string consoleKey);

    /// <summary>Returns externalized compression ratio estimates keyed by "source_target" (e.g. "bin_chd").</summary>
    IReadOnlyDictionary<string, double> GetCompressionEstimates() => new Dictionary<string, double>();
}
