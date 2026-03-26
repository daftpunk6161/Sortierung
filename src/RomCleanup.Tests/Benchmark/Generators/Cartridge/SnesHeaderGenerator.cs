using System.Text;

namespace RomCleanup.Tests.Benchmark.Generators.Cartridge;

/// <summary>
/// Generates minimal SNES ROM stubs (.sfc/.smc) with valid internal headers.
/// SNES has two memory maps: LoROM (header @ 0x7FC0) and HiROM (header @ 0xFFC0).
/// </summary>
internal sealed class SnesHeaderGenerator : IStubGenerator
{
    public string GeneratorId => "snes-header";
    public string Extension => ".sfc";
    public IReadOnlyList<string> SupportedVariants { get; } = ["lorom", "hirom", "headerless", "truncated"];

    public byte[] Generate(string? variant = null, IReadOnlyDictionary<string, string>? parameters = null)
    {
        variant ??= "lorom";
        return variant switch
        {
            "lorom" => GenerateInternal(0x7FC0, parameters),
            "hirom" => GenerateInternal(0xFFC0, parameters),
            "headerless" => new byte[32768],
            "truncated" => GenerateTruncated(),
            _ => throw new ArgumentException($"Unknown variant '{variant}' for {GeneratorId}")
        };
    }

    private static byte[] GenerateInternal(int headerOffset, IReadOnlyDictionary<string, string>? parameters)
    {
        // Need at least headerOffset + 32 bytes for the internal header
        int totalSize = headerOffset + 64;
        var data = new byte[totalSize];

        string title = "BENCHMARK TEST      "; // 21 chars, space-padded
        if (parameters?.TryGetValue("title", out var t) == true)
            title = t.PadRight(21)[..21];

        // Internal header at headerOffset:
        // Bytes 0-20: Game title (21 bytes ASCII)
        // Byte 21: ROM layout mode
        // Byte 22: ROM type
        // Byte 23: ROM size (log2)
        // Byte 24: RAM size (log2)
        // Bytes 25-26: Country/region
        // Bytes 28-29: Checksum complement
        // Bytes 30-31: Checksum
        Encoding.ASCII.GetBytes(title).CopyTo(data, headerOffset);
        data[headerOffset + 21] = (byte)(headerOffset == 0xFFC0 ? 0x21 : 0x20); // HiROM or LoROM
        data[headerOffset + 22] = 0x00; // ROM only
        data[headerOffset + 23] = 0x08; // 256KB
        data[headerOffset + 24] = 0x00; // No RAM

        // Checksum complement + checksum (simple fill)
        data[headerOffset + 28] = 0xFF;
        data[headerOffset + 29] = 0xFF;
        data[headerOffset + 30] = 0x00;
        data[headerOffset + 31] = 0x00;

        return data;
    }

    private static byte[] GenerateTruncated()
    {
        // Valid start but truncated before header area
        return new byte[256];
    }
}
