namespace Romulus.Tests.Benchmark.Generators.Cartridge;

/// <summary>
/// Generates minimal Atari Lynx ROM stubs (.lnx).
/// Lynx ROM header: 4 bytes magic "LYNX" (4C 59 4E 58) at offset 0.
/// </summary>
internal sealed class LynxHeaderGenerator : IStubGenerator
{
    private static readonly byte[] LynxMagic = [0x4C, 0x59, 0x4E, 0x58];

    public string GeneratorId => "lynx-header";
    public string Extension => ".lnx";
    public IReadOnlyList<string> SupportedVariants { get; } = ["standard", "headerless", "truncated"];

    public byte[] Generate(string? variant = null, IReadOnlyDictionary<string, string>? parameters = null)
    {
        variant ??= "standard";
        return variant switch
        {
            "standard" => GenerateStandard(),
            "headerless" => new byte[32768],
            "truncated" => GenerateStandard(8),
            _ => throw new ArgumentException($"Unknown variant '{variant}' for {GeneratorId}")
        };
    }

    private static byte[] GenerateStandard(int totalSize = 65536)
    {
        var data = new byte[totalSize];
        LynxMagic.CopyTo(data, 0);
        // Page size at offset 6 (2 bytes LE)
        if (totalSize > 8)
        {
            data[6] = 0x00;
            data[7] = 0x01; // 256 byte pages
        }
        return data;
    }
}
