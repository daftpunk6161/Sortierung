using System.Text;

namespace RomCleanup.Tests.Benchmark.Generators.Disc;

/// <summary>
/// Generates minimal FM Towns ISO PVD stubs.
/// FM Towns discs have "FM TOWNS" in the PVD system identifier or nearby text.
/// </summary>
internal sealed class FmTownsPvdGenerator : IStubGenerator
{
    private const int SectorSize = 2048;
    private const int PvdSector = 16;
    private const int PvdOffset = PvdSector * SectorSize;

    public string GeneratorId => "fmtowns-pvd";
    public string Extension => ".iso";
    public IReadOnlyList<string> SupportedVariants { get; } = ["standard"];

    public byte[] Generate(string? variant = null, IReadOnlyDictionary<string, string>? parameters = null)
    {
        int totalSize = (PvdSector + 1) * SectorSize;
        var data = new byte[totalSize];

        int offset = PvdOffset;
        data[offset] = 0x01;
        Encoding.ASCII.GetBytes("CD001").CopyTo(data, offset + 1);
        data[offset + 6] = 0x01;

        // System identifier: FM TOWNS (32 bytes, space-padded)
        var sysId = Encoding.ASCII.GetBytes("FM TOWNS".PadRight(32));
        Array.Copy(sysId, 0, data, offset + 8, 32);

        return data;
    }
}
