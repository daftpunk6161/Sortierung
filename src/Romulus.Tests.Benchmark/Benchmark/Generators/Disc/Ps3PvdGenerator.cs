using System.Text;

namespace Romulus.Tests.Benchmark.Generators.Disc;

/// <summary>
/// Generates minimal PS3 disc image stubs.
/// PS3 discs are identified by "PS3_DISC.SFB" or "PS3_GAME" markers
/// within the PVD or disc structure.
/// </summary>
internal sealed class Ps3PvdGenerator : IStubGenerator
{
    private const int SectorSize = 2048;
    private const int PvdSector = 16;
    private const int PvdOffset = PvdSector * SectorSize;

    public string GeneratorId => "ps3-pvd";
    public string Extension => ".iso";
    public IReadOnlyList<string> SupportedVariants { get; } = ["standard"];

    public byte[] Generate(string? variant = null, IReadOnlyDictionary<string, string>? parameters = null)
    {
        int totalSize = (PvdSector + 2) * SectorSize;
        var data = new byte[totalSize];

        int offset = PvdOffset;
        data[offset] = 0x01;
        Encoding.ASCII.GetBytes("CD001").CopyTo(data, offset + 1);
        data[offset + 6] = 0x01;

        // System identifier: PLAYSTATION
        var sysId = Encoding.ASCII.GetBytes("PLAYSTATION".PadRight(32));
        Array.Copy(sysId, 0, data, offset + 8, 32);

        // PS3 marker after PVD (within scan range)
        Encoding.ASCII.GetBytes("PS3_DISC.SFB").CopyTo(data, offset + 256);

        return data;
    }
}
