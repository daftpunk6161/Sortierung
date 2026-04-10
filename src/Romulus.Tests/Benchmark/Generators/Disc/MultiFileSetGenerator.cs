using System.Text;

namespace Romulus.Tests.Benchmark.Generators.Disc;

/// <summary>
/// Generates multi-file disc image stubs: CUE+BIN and GDI+Track sets.
/// The primary file (CUE or GDI) is returned by Generate();
/// companion files (BIN tracks) are written as siblings via GenerateSet().
/// </summary>
internal sealed class MultiFileSetGenerator : IStubGenerator
{
    private const int SectorSize = 2048;

    public string GeneratorId => "multi-file-set";
    public string Extension => ".cue";
    public IReadOnlyList<string> SupportedVariants { get; } =
        ["cue-single", "cue-multi", "gdi-single", "gdi-multi"];

    public byte[] Generate(string? variant = null, IReadOnlyDictionary<string, string>? parameters = null)
    {
        variant ??= "cue-single";
        string baseName = parameters?.GetValueOrDefault("baseName") ?? "game";

        return variant switch
        {
            "cue-single" => GenerateCueSheet(baseName, trackCount: 1),
            "cue-multi" => GenerateCueSheet(baseName, trackCount: 3),
            "gdi-single" => GenerateGdiSheet(baseName, trackCount: 1),
            "gdi-multi" => GenerateGdiSheet(baseName, trackCount: 3),
            _ => throw new ArgumentException($"Unknown variant '{variant}' for {GeneratorId}")
        };
    }

    /// <summary>
    /// Generates the full file set (sheet + binary tracks) into the specified directory.
    /// Returns the paths of all generated files.
    /// </summary>
    public IReadOnlyList<string> GenerateSet(
        string outputDir, string baseName, string variant = "cue-single")
    {
        Directory.CreateDirectory(outputDir);
        var paths = new List<string>();

        int trackCount = variant.Contains("multi") ? 3 : 1;
        bool isGdi = variant.StartsWith("gdi");

        if (isGdi)
        {
            // Write GDI sheet
            string gdiPath = Path.Combine(outputDir, baseName + ".gdi");
            File.WriteAllBytes(gdiPath, GenerateGdiSheet(baseName, trackCount));
            paths.Add(gdiPath);

            // Write track raw files
            for (int i = 1; i <= trackCount; i++)
            {
                string trackPath = Path.Combine(outputDir, $"{baseName} (Track {i}).raw");
                File.WriteAllBytes(trackPath, GenerateTrackData(i, SectorSize * 150));
                paths.Add(trackPath);
            }
        }
        else
        {
            // Write CUE sheet
            string cuePath = Path.Combine(outputDir, baseName + ".cue");
            File.WriteAllBytes(cuePath, GenerateCueSheet(baseName, trackCount));
            paths.Add(cuePath);

            // Write BIN file(s)
            if (trackCount == 1)
            {
                string binPath = Path.Combine(outputDir, baseName + ".bin");
                File.WriteAllBytes(binPath, GenerateTrackData(1, SectorSize * 300));
                paths.Add(binPath);
            }
            else
            {
                for (int i = 1; i <= trackCount; i++)
                {
                    string binPath = Path.Combine(outputDir, $"{baseName} (Track {i:D2}).bin");
                    File.WriteAllBytes(binPath, GenerateTrackData(i, SectorSize * 150));
                    paths.Add(binPath);
                }
            }
        }

        return paths;
    }

    private static byte[] GenerateCueSheet(string baseName, int trackCount)
    {
        var sb = new StringBuilder();
        if (trackCount == 1)
        {
            sb.AppendLine($"FILE \"{baseName}.bin\" BINARY");
            sb.AppendLine("  TRACK 01 MODE1/2352");
            sb.AppendLine("    INDEX 01 00:00:00");
        }
        else
        {
            for (int i = 1; i <= trackCount; i++)
            {
                string trackType = i == 1 ? "MODE1/2352" : "AUDIO";
                sb.AppendLine($"FILE \"{baseName} (Track {i:D2}).bin\" BINARY");
                sb.AppendLine($"  TRACK {i:D2} {trackType}");
                sb.AppendLine("    INDEX 01 00:00:00");
            }
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static byte[] GenerateGdiSheet(string baseName, int trackCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine(trackCount.ToString());
        int lba = 0;
        for (int i = 1; i <= trackCount; i++)
        {
            int trackType = i == 1 ? 4 : 0; // 4=data, 0=audio
            sb.AppendLine($"{i} {lba} {trackType} 2352 \"{baseName} (Track {i}).raw\" 0");
            lba += 300;
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static byte[] GenerateTrackData(int trackNumber, int size)
    {
        var data = new byte[size];
        // Put a minimal marker at the start for identification
        data[0] = (byte)trackNumber;
        data[1] = 0xCD;
        data[2] = 0x00;
        data[3] = 0x01;
        return data;
    }
}
