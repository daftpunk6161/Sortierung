using System.Text;

namespace Romulus.Tests.Benchmark.Generators.Disc;

/// <summary>
/// Generates CCD+IMG+SUB disc image stubs.
/// The primary file (CCD) is returned by Generate();
/// companion files (IMG, SUB) are written by GenerateSet().
/// </summary>
internal sealed class CcdImgGenerator : IStubGenerator
{
    private const int SectorSize = 2352;

    public string GeneratorId => "ccd-img";
    public string Extension => ".ccd";
    public IReadOnlyList<string> SupportedVariants { get; } = ["single-track", "multi-track"];

    public byte[] Generate(string? variant = null, IReadOnlyDictionary<string, string>? parameters = null)
    {
        variant ??= "single-track";
        string baseName = parameters?.GetValueOrDefault("baseName") ?? "game";
        int trackCount = variant == "multi-track" ? 3 : 1;
        return GenerateCcdSheet(baseName, trackCount);
    }

    public IReadOnlyList<string> GenerateSet(
        string outputDir, string baseName, string variant = "single-track")
    {
        Directory.CreateDirectory(outputDir);
        var paths = new List<string>();
        int trackCount = variant == "multi-track" ? 3 : 1;
        int sectors = 300;

        string ccdPath = Path.Combine(outputDir, baseName + ".ccd");
        File.WriteAllBytes(ccdPath, GenerateCcdSheet(baseName, trackCount));
        paths.Add(ccdPath);

        string imgPath = Path.Combine(outputDir, baseName + ".img");
        File.WriteAllBytes(imgPath, GenerateImgData(trackCount, sectors));
        paths.Add(imgPath);

        string subPath = Path.Combine(outputDir, baseName + ".sub");
        File.WriteAllBytes(subPath, GenerateSubData(trackCount, sectors));
        paths.Add(subPath);

        return paths;
    }

    private static byte[] GenerateCcdSheet(string baseName, int trackCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[CloneCD]");
        sb.AppendLine("Version=3");
        sb.AppendLine();
        sb.AppendLine("[Disc]");
        sb.AppendLine($"TocEntries={trackCount + 3}");
        sb.AppendLine($"Sessions=1");
        sb.AppendLine($"DataTracksScrambled=0");
        sb.AppendLine();

        int lba = 0;
        for (int i = 1; i <= trackCount; i++)
        {
            sb.AppendLine($"[Session 1]");
            sb.AppendLine($"PreGapMode=1");
            sb.AppendLine();
            sb.AppendLine($"[Entry {i - 1}]");
            sb.AppendLine($"Session=1");
            sb.AppendLine($"Point=0x{i:X2}");
            sb.AppendLine($"ADR=0x01");
            sb.AppendLine($"Control=0x{(i == 1 ? "04" : "00")}");
            sb.AppendLine($"TrackNo=0");
            sb.AppendLine($"PLBA={lba}");
            sb.AppendLine();
            lba += 300;
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static byte[] GenerateImgData(int trackCount, int sectorsPerTrack)
    {
        int totalSize = trackCount * sectorsPerTrack * SectorSize;
        var data = new byte[totalSize];
        for (int t = 0; t < trackCount; t++)
        {
            int offset = t * sectorsPerTrack * SectorSize;
            data[offset] = (byte)(t + 1);
            data[offset + 1] = 0xCC;
            data[offset + 2] = 0xDD;
        }
        return data;
    }

    private static byte[] GenerateSubData(int trackCount, int sectorsPerTrack)
    {
        // SUB channel: 96 bytes per sector
        int totalSize = trackCount * sectorsPerTrack * 96;
        return new byte[totalSize];
    }
}
