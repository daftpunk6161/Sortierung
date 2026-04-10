using System.Text;

namespace Romulus.Tests.Benchmark.Generators.Disc;

/// <summary>
/// Generates MDS+MDF disc image stubs (Alcohol 120% format).
/// The primary file (MDS) is returned by Generate();
/// companion MDF file is written by GenerateSet().
/// </summary>
internal sealed class MdsMdfGenerator : IStubGenerator
{
    private const int SectorSize = 2352;

    public string GeneratorId => "mds-mdf";
    public string Extension => ".mds";
    public IReadOnlyList<string> SupportedVariants { get; } = ["single-track", "multi-track"];

    public byte[] Generate(string? variant = null, IReadOnlyDictionary<string, string>? parameters = null)
    {
        variant ??= "single-track";
        int trackCount = variant == "multi-track" ? 3 : 1;
        return GenerateMdsHeader(trackCount);
    }

    public IReadOnlyList<string> GenerateSet(
        string outputDir, string baseName, string variant = "single-track")
    {
        Directory.CreateDirectory(outputDir);
        var paths = new List<string>();
        int trackCount = variant == "multi-track" ? 3 : 1;

        string mdsPath = Path.Combine(outputDir, baseName + ".mds");
        File.WriteAllBytes(mdsPath, GenerateMdsHeader(trackCount));
        paths.Add(mdsPath);

        string mdfPath = Path.Combine(outputDir, baseName + ".mdf");
        File.WriteAllBytes(mdfPath, GenerateMdfData(trackCount));
        paths.Add(mdfPath);

        return paths;
    }

    private static byte[] GenerateMdsHeader(int trackCount)
    {
        // MDS signature: "MEDIA DESCRIPTOR" at offset 0
        var header = new byte[88 + trackCount * 80];
        var sig = Encoding.ASCII.GetBytes("MEDIA DESCRIPTOR");
        Array.Copy(sig, header, sig.Length);

        // Version 1.x marker
        header[16] = 0x01;
        header[17] = 0x00;

        // Session count
        header[20] = 0x01;

        // Track count at offset 24
        header[24] = (byte)trackCount;

        // Per-track block (minimal stub markers at offset 88+)
        for (int i = 0; i < trackCount; i++)
        {
            int offset = 88 + i * 80;
            header[offset] = (byte)(i + 1);         // track number
            header[offset + 1] = (byte)(i == 0 ? 0xA9 : 0x00); // data vs audio
            header[offset + 4] = (byte)(i * 0x96);  // sector offset (low byte)
        }

        return header;
    }

    private static byte[] GenerateMdfData(int trackCount)
    {
        int sectorsPerTrack = 150;
        int totalSize = trackCount * sectorsPerTrack * SectorSize;
        var data = new byte[totalSize];
        for (int t = 0; t < trackCount; t++)
        {
            int offset = t * sectorsPerTrack * SectorSize;
            data[offset] = (byte)(t + 1);
            data[offset + 1] = 0xDF;
            data[offset + 2] = 0xAA;
        }
        return data;
    }
}
