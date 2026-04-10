using System.Text;

namespace Romulus.Tests.Benchmark.Generators.Disc;

/// <summary>
/// Generates minimal Sega IP.BIN disc header stubs.
/// Saturn: "SEGA SATURN" at offset 0x00
/// Dreamcast: "SEGA SEGAKATANA" at offset 0x00
/// Sega CD: "SEGADISCSYSTEM" at offset 0x00
/// </summary>
internal sealed class SegaIpBinGenerator : IStubGenerator
{
    public string GeneratorId => "sega-ipbin";
    public string Extension => ".bin";
    public IReadOnlyList<string> SupportedVariants { get; } = ["saturn", "dreamcast", "segacd"];

    public byte[] Generate(string? variant = null, IReadOnlyDictionary<string, string>? parameters = null)
    {
        variant ??= "saturn";
        return variant switch
        {
            "saturn" => GenerateWithMarker("SEGA SATURN     "),
            "dreamcast" => GenerateWithMarker("SEGA SEGAKATANA "),
            "segacd" => GenerateWithMarker("SEGADISCSYSTEM  "),
            _ => throw new ArgumentException($"Unknown variant '{variant}' for {GeneratorId}")
        };
    }

    private static byte[] GenerateWithMarker(string marker)
    {
        // IP.BIN is typically 0x8000 (32KB) for Saturn/DC
        var data = new byte[32768];
        Encoding.ASCII.GetBytes(marker.PadRight(16)[..16]).CopyTo(data, 0);

        // Pad header region with spaces (typical for IP.BIN)
        for (int i = 16; i < 256; i++)
            data[i] = 0x20;

        return data;
    }
}
