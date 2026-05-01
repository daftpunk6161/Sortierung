using System.Text;

namespace Romulus.Tests.Benchmark.Generators.Disc;

/// <summary>
/// Generates minimal PS1 Primary Volume Descriptor stub data.
/// PS1 ISOs contain a PVD at sector 16 (0x8000) with "CD001" marker
/// and "PLAYSTATION" system identifier.
/// </summary>
internal sealed class Ps1PvdGenerator : IStubGenerator
{
    private const int SectorSize = 2048;
    private const int PvdSector = 16; // ISO 9660 PVD always at sector 16
    private const int PvdOffset = PvdSector * SectorSize; // 0x8000

    public string GeneratorId => "ps1-pvd";
    public string Extension => ".bin";
    public IReadOnlyList<string> SupportedVariants { get; } = ["standard", "ps2-header", "no-system-id"];

    public byte[] Generate(string? variant = null, IReadOnlyDictionary<string, string>? parameters = null)
    {
        variant ??= "standard";

        return variant switch
        {
            "standard" => GenerateStandard("PLAYSTATION"),
            "ps2-header" => GenerateStandard("PLAYSTATION2"),
            "no-system-id" => GenerateStandard(""),
            _ => throw new ArgumentException($"Unknown variant '{variant}' for {GeneratorId}")
        };
    }

    private static byte[] GenerateStandard(string systemIdentifier)
    {
        // Minimum: need sector 16 + 1 sector for PVD = 17 sectors
        int totalSize = (PvdSector + 1) * SectorSize;
        var data = new byte[totalSize];

        // PVD structure at offset 0x8000:
        // Byte 0: Volume descriptor type (1 = PVD)
        // Bytes 1-5: "CD001" standard identifier
        // Byte 6: Version (1)
        // Bytes 8-39: System Identifier (32 bytes, padded with spaces)
        int offset = PvdOffset;
        data[offset] = 0x01; // PVD type
        Encoding.ASCII.GetBytes("CD001").CopyTo(data, offset + 1);
        data[offset + 6] = 0x01; // Version

        // System identifier at offset +8, 32 bytes, space-padded
        var sysIdBytes = Encoding.ASCII.GetBytes(systemIdentifier.PadRight(32));
        Array.Copy(sysIdBytes, 0, data, offset + 8, Math.Min(sysIdBytes.Length, 32));

        return data;
    }
}
