namespace Romulus.Contracts.Models;

/// <summary>
/// Result model for ROM header analysis.
/// </summary>
/// <param name="Platform">Detected platform key.</param>
/// <param name="Format">Detected header/container format.</param>
/// <param name="Details">Human-readable detection details.</param>
public sealed record RomHeaderInfo(string Platform, string Format, string Details);
