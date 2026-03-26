using System.Text;

namespace RomCleanup.Tests.Benchmark.Generators.Cartridge;

/// <summary>
/// Generates minimal Atari 7800 ROM stubs (.a78).
/// A7800 header: "ATARI7800" at offset 0x01 (9 bytes).
/// Full header is 128 bytes.
/// </summary>
internal sealed class A7800HeaderGenerator : IStubGenerator
{
    private static readonly byte[] A7800Sig = Encoding.ASCII.GetBytes("ATARI7800");

    public string GeneratorId => "a7800-header";
    public string Extension => ".a78";
    public IReadOnlyList<string> SupportedVariants { get; } = ["standard", "headerless", "truncated"];

    public byte[] Generate(string? variant = null, IReadOnlyDictionary<string, string>? parameters = null)
    {
        variant ??= "standard";
        return variant switch
        {
            "standard" => GenerateStandard(),
            "headerless" => new byte[32768],
            "truncated" => GenerateStandard(16),
            _ => throw new ArgumentException($"Unknown variant '{variant}' for {GeneratorId}")
        };
    }

    private static byte[] GenerateStandard(int totalSize = 32896)
    {
        // 128-byte header + 32KB ROM data
        var data = new byte[totalSize];

        // Header version at offset 0
        data[0] = 0x01;

        // "ATARI7800" signature at offset 1
        if (totalSize > 10)
            A7800Sig.CopyTo(data, 1);

        // Game title at offset 17 (32 bytes)
        if (totalSize > 49)
            Encoding.ASCII.GetBytes("BENCHMARK".PadRight(32)).CopyTo(data, 17);

        // ROM size at offset 49 (4 bytes BE)
        if (totalSize > 53)
        {
            int romSize = totalSize - 128;
            data[49] = (byte)(romSize >> 24);
            data[50] = (byte)(romSize >> 16);
            data[51] = (byte)(romSize >> 8);
            data[52] = (byte)(romSize);
        }

        return data;
    }
}
