namespace RomCleanup.Tests.Benchmark.Generators.Cartridge;

/// <summary>
/// Generates minimal GBA ROM stubs (.gba).
/// GBA ROMs have a Nintendo logo at offset 0x04 (156 bytes)
/// and an internal header at 0xA0 (game title, 12 bytes).
/// The fixed entry point at 0x00 is an ARM branch instruction.
/// </summary>
internal sealed class GbaHeaderGenerator : IStubGenerator
{
    // Simplified Nintendo logo (first 4 bytes are sufficient for detection)
    private static readonly byte[] LogoStart = [0x24, 0xFF, 0xAE, 0x51];

    public string GeneratorId => "gba-header";
    public string Extension => ".gba";
    public IReadOnlyList<string> SupportedVariants { get; } = ["standard", "headerless", "truncated"];

    public byte[] Generate(string? variant = null, IReadOnlyDictionary<string, string>? parameters = null)
    {
        variant ??= "standard";
        return variant switch
        {
            "standard" => GenerateStandard(parameters),
            "headerless" => new byte[32768],
            "truncated" => GenerateStandard(null, 64),
            _ => throw new ArgumentException($"Unknown variant '{variant}' for {GeneratorId}")
        };
    }

    private static byte[] GenerateStandard(IReadOnlyDictionary<string, string>? parameters, int totalSize = 32768)
    {
        var data = new byte[totalSize];

        // ARM branch instruction at 0x00
        data[0] = 0xEA;
        data[1] = 0x00;
        data[2] = 0x00;
        data[3] = 0x2E;

        // Nintendo logo at 0x04c
        if (totalSize > 8)
            LogoStart.CopyTo(data, 4);

        // Game title at 0xA0 (12 bytes, uppercase ASCII)
        if (totalSize > 0xAC)
        {
            string title = "BENCHMARKROM";
            if (parameters?.TryGetValue("title", out var t) == true)
                title = t.PadRight(12)[..12];
            System.Text.Encoding.ASCII.GetBytes(title).CopyTo(data, 0xA0);
        }

        // Game code at 0xAC (4 bytes)
        if (totalSize > 0xB0)
            System.Text.Encoding.ASCII.GetBytes("ABCE").CopyTo(data, 0xAC);

        return data;
    }
}
