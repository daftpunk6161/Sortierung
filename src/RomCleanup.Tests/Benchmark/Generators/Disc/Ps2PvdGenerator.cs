using System.Text;

namespace RomCleanup.Tests.Benchmark.Generators.Disc;

/// <summary>
/// Generates minimal PS2 ISO stubs with PVD at sector 16.
/// PS2 ISOs have "PLAYSTATION" or boot2 markers in the PVD system identifier area.
/// Also supports PSP PVD ("PSP_GAME" in certain paths).
/// </summary>
internal sealed class Ps2PvdGenerator : IStubGenerator
{
    private const int SectorSize = 2048;
    private const int PvdSector = 16;
    private const int PvdOffset = PvdSector * SectorSize;

    public string GeneratorId => "ps2-pvd";
    public string Extension => ".iso";
    public IReadOnlyList<string> SupportedVariants { get; } = ["standard", "psp", "no-system-id"];

    public byte[] Generate(string? variant = null, IReadOnlyDictionary<string, string>? parameters = null)
    {
        variant ??= "standard";
        return variant switch
        {
            "standard" => GeneratePvd("PLAYSTATION", "BOOT2 = cdrom0:\\SLUS_123.45;1"),
            "psp" => GeneratePvd("PLAYSTATION", "PSP GAME"),
            "no-system-id" => GeneratePvd(""),
            _ => throw new ArgumentException($"Unknown variant '{variant}' for {GeneratorId}")
        };
    }

    private static byte[] GeneratePvd(string systemIdentifier, string? bootMarker = null)
    {
        int totalSize = (PvdSector + 2) * SectorSize; // Extra sector for boot marker
        var data = new byte[totalSize];

        int offset = PvdOffset;
        data[offset] = 0x01;
        Encoding.ASCII.GetBytes("CD001").CopyTo(data, offset + 1);
        data[offset + 6] = 0x01;
        var sysIdBytes = Encoding.ASCII.GetBytes(systemIdentifier.PadRight(32));
        Array.Copy(sysIdBytes, 0, data, offset + 8, Math.Min(sysIdBytes.Length, 32));

        // Write boot marker after the PVD (within scan range used by DiscHeaderDetector)
        if (bootMarker is not null)
        {
            Encoding.ASCII.GetBytes(bootMarker).CopyTo(data, offset + 256);
        }

        return data;
    }
}
