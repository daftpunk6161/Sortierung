namespace RomCleanup.Tests.Benchmark.Generators.Disc;

/// <summary>
/// Generates a minimal CDI (Philips CD-i) disc stub.
/// CDI discs have no universally unique magic; this generates a minimal file
/// that can be detected by extension or folder-based methods.
/// </summary>
internal sealed class CdiDiscGenerator : IStubGenerator
{
    public string GeneratorId => "cdi-disc";
    public string Extension => ".cdi";
    public IReadOnlyList<string> SupportedVariants { get; } = ["standard"];

    public byte[] Generate(string? variant = null, IReadOnlyDictionary<string, string>? parameters = null)
    {
        // CDI has no standardized binary magic. Generate a minimal placeholder
        // that allows extension/folder-based detection to function.
        var data = new byte[2048];
        // CDI files often start with sector header data
        data[0] = 0x00;
        data[1] = 0xFF;
        data[2] = 0xFF;
        data[15] = 0x01;
        return data;
    }
}
