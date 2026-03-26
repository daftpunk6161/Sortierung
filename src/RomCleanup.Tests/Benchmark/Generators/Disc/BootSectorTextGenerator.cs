using System.Text;

namespace RomCleanup.Tests.Benchmark.Generators.Disc;

/// <summary>
/// Generates minimal boot-sector text marker stubs for disc systems that are
/// identified by ASCII text patterns near the start of the disc image.
/// Covers: Neo Geo CD, PC Engine CD, PC-FX, Jaguar CD, CD32 (Amiga).
/// </summary>
internal sealed class BootSectorTextGenerator : IStubGenerator
{
    public string GeneratorId => "boot-sector-text";
    public string Extension => ".bin";
    public IReadOnlyList<string> SupportedVariants { get; } =
        ["neocd", "pcecd", "pcfx", "jagcd", "cd32"];

    public byte[] Generate(string? variant = null, IReadOnlyDictionary<string, string>? parameters = null)
    {
        variant ??= "neocd";
        return variant switch
        {
            "neocd" => GenerateWithMarker("NEOGEO CD"),
            "pcecd" => GenerateWithMarker("PC Engine CD-ROM SYSTEM"),
            "pcfx" => GenerateWithMarker("PC-FX:Hu_CD"),
            "jagcd" => GenerateWithMarker("ATARI JAGUAR"),
            "cd32" => GenerateWithMarker("AMIGA BOOT"),
            _ => throw new ArgumentException($"Unknown variant '{variant}' for {GeneratorId}")
        };
    }

    private static byte[] GenerateWithMarker(string marker)
    {
        // Place marker in a 32 KB block — typical for boot sectors scanned by DiscHeaderDetector
        var data = new byte[32768];
        var markerBytes = Encoding.ASCII.GetBytes(marker);
        markerBytes.CopyTo(data, 0);

        // Pad header region with spaces
        for (int i = markerBytes.Length; i < 256; i++)
            data[i] = 0x20;

        return data;
    }
}
