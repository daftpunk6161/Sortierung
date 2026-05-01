namespace Romulus.Tests.Benchmark.Generators.Cartridge;

/// <summary>
/// Generates minimal Game Boy / Game Boy Color ROM stubs (.gb/.gbc).
/// GB/GBC ROMs have the Nintendo logo at offset 0x104 (48 bytes)
/// and a CGB flag at 0x143: 0x00 = DMG, 0x80 = dual, 0xC0 = CGB-only.
/// </summary>
internal sealed class GbHeaderGenerator : IStubGenerator
{
    // Compressed Nintendo logo (first 4 bytes used for detection)
    private static readonly byte[] LogoStart = [0xCE, 0xED, 0x66, 0x66];

    public string GeneratorId => "gb-header";
    public string Extension => ".gb";
    public IReadOnlyList<string> SupportedVariants { get; } = ["dmg", "cgb-dual", "cgb-only", "headerless", "truncated"];

    public byte[] Generate(string? variant = null, IReadOnlyDictionary<string, string>? parameters = null)
    {
        variant ??= "dmg";
        return variant switch
        {
            "dmg" => GenerateWithCgbFlag(0x00),
            "cgb-dual" => GenerateWithCgbFlag(0x80),
            "cgb-only" => GenerateWithCgbFlag(0xC0),
            "headerless" => new byte[32768],
            "truncated" => GenerateWithCgbFlag(0x00, 32),
            _ => throw new ArgumentException($"Unknown variant '{variant}' for {GeneratorId}")
        };
    }

    private static byte[] GenerateWithCgbFlag(byte cgbFlag, int totalSize = 32768)
    {
        var data = new byte[totalSize];

        // Nintendo logo at 0x104
        if (totalSize > 0x108)
            LogoStart.CopyTo(data, 0x104);

        // Game title at 0x134 (up to 15 bytes)
        if (totalSize > 0x143)
        {
            System.Text.Encoding.ASCII.GetBytes("BENCHMARK").CopyTo(data, 0x134);
            // CGB flag at 0x143
            data[0x143] = cgbFlag;
        }

        // SGB flag at 0x146
        if (totalSize > 0x146)
            data[0x146] = 0x00;

        // ROM size at 0x148 (0x00 = 32KB)
        if (totalSize > 0x148)
            data[0x148] = 0x00;

        return data;
    }
}
