namespace Romulus.Tests.Benchmark.Generators;

/// <summary>
/// Contract for ROM stub generators. Each generator creates a minimal byte[]
/// that mimics a valid (or intentionally invalid) ROM format signature.
/// </summary>
internal interface IStubGenerator
{
    /// <summary>
    /// Short identifier used in ground-truth JSONL (e.g., "nes-ines", "ps1-pvd").
    /// </summary>
    string GeneratorId { get; }

    /// <summary>
    /// File extension the generator targets (e.g., ".nes", ".bin").
    /// </summary>
    string Extension { get; }

    /// <summary>
    /// Supported variants (e.g., "standard", "headerless", "corrupt").
    /// </summary>
    IReadOnlyList<string> SupportedVariants { get; }

    /// <summary>
    /// Generates a minimal ROM stub byte array.
    /// </summary>
    /// <param name="variant">Optional variant name. Defaults to "standard" if null.</param>
    /// <param name="parameters">Optional extra parameters for specialized generation.</param>
    byte[] Generate(string? variant = null, IReadOnlyDictionary<string, string>? parameters = null);
}
