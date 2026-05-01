using System.Text;

namespace Romulus.Tests.Benchmark.Generators.Cartridge;

/// <summary>
/// Generates minimal Mega Drive / Genesis / 32X ROM stubs (.md/.gen/.32x).
/// MD header at 0x100: "SEGA MEGA DRIVE" or "SEGA GENESIS" (16 bytes).
/// 32X header at 0x100: "SEGA 32X" (16 bytes).
/// </summary>
internal sealed class MdHeaderGenerator : IStubGenerator
{
    public string GeneratorId => "md-header";
    public string Extension => ".md";
    public IReadOnlyList<string> SupportedVariants { get; } = ["megadrive", "genesis", "32x", "headerless", "truncated"];

    public byte[] Generate(string? variant = null, IReadOnlyDictionary<string, string>? parameters = null)
    {
        variant ??= "megadrive";
        return variant switch
        {
            "megadrive" => GenerateWithSystemId("SEGA MEGA DRIVE "),
            "genesis" => GenerateWithSystemId("SEGA GENESIS    "),
            "32x" => GenerateWithSystemId("SEGA 32X        "),
            "headerless" => new byte[32768],
            "truncated" => GenerateWithSystemId("SEGA MEGA DRIVE ", 32),
            _ => throw new ArgumentException($"Unknown variant '{variant}' for {GeneratorId}")
        };
    }

    private static byte[] GenerateWithSystemId(string systemId, int totalSize = 131072)
    {
        var data = new byte[totalSize];

        // System type at 0x100 (16 bytes)
        if (totalSize > 0x110)
            Encoding.ASCII.GetBytes(systemId.PadRight(16)[..16]).CopyTo(data, 0x100);

        // Domestic name at 0x120 (48 bytes)
        if (totalSize > 0x150)
            Encoding.ASCII.GetBytes("BENCHMARK TEST ROM".PadRight(48)).CopyTo(data, 0x120);

        // Overseas name at 0x150 (48 bytes)
        if (totalSize > 0x180)
            Encoding.ASCII.GetBytes("BENCHMARK TEST ROM".PadRight(48)).CopyTo(data, 0x150);

        return data;
    }
}
