namespace Romulus.Tests.Benchmark.Generators;

/// <summary>
/// Generates a minimal 1-byte or empty file — used for extension-only or folder-only detection entries.
/// </summary>
internal sealed class ExtOnlyGenerator : IStubGenerator
{
    public string GeneratorId => "ext-only";
    public string Extension => ".*";
    public IReadOnlyList<string> SupportedVariants { get; } = ["default", "empty"];

    public byte[] Generate(string? variant = null, IReadOnlyDictionary<string, string>? parameters = null)
    {
        return variant == "empty" ? [] : [0x00];
    }
}
