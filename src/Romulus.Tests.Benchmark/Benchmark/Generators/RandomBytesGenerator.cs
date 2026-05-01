namespace Romulus.Tests.Benchmark.Generators;

/// <summary>
/// Generates deterministic pseudo-random bytes, seeded by entry ID for reproducibility.
/// Used for entries that should NOT be detected as any known format.
/// </summary>
internal sealed class RandomBytesGenerator : IStubGenerator
{
    public string GeneratorId => "random-bytes";
    public string Extension => ".*";
    public IReadOnlyList<string> SupportedVariants { get; } = ["default"];

    public byte[] Generate(string? variant = null, IReadOnlyDictionary<string, string>? parameters = null)
    {
        int size = 512;
        int seed = 42;

        if (parameters?.TryGetValue("sizeBytes", out var sizeStr) == true && int.TryParse(sizeStr, out var s))
            size = s;
        if (parameters?.TryGetValue("seed", out var seedStr) == true && int.TryParse(seedStr, out var sd))
            seed = sd;

        var rng = new Random(seed);
        var data = new byte[size];
        rng.NextBytes(data);
        return data;
    }
}
