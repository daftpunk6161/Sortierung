using System.Text;

namespace Romulus.Tests.Benchmark.Generators.Disc;

/// <summary>
/// Generates M3U playlist stubs that reference disc image files.
/// Used for multi-disc games where an M3U file lists individual disc images.
/// </summary>
internal sealed class M3uPlaylistGenerator : IStubGenerator
{
    public string GeneratorId => "m3u-playlist";
    public string Extension => ".m3u";
    public IReadOnlyList<string> SupportedVariants { get; } = ["two-disc", "three-disc", "four-disc"];

    public byte[] Generate(string? variant = null, IReadOnlyDictionary<string, string>? parameters = null)
    {
        variant ??= "two-disc";
        string baseName = parameters?.GetValueOrDefault("baseName") ?? "Game";
        string discExt = parameters?.GetValueOrDefault("discExt") ?? ".cue";

        int discCount = variant switch
        {
            "two-disc" => 2,
            "three-disc" => 3,
            "four-disc" => 4,
            _ => throw new ArgumentException($"Unknown variant '{variant}' for {GeneratorId}")
        };

        return GenerateM3u(baseName, discCount, discExt);
    }

    public IReadOnlyList<string> GenerateSet(
        string outputDir, string baseName, string variant = "two-disc", string discExt = ".cue")
    {
        Directory.CreateDirectory(outputDir);
        var paths = new List<string>();

        int discCount = variant switch
        {
            "two-disc" => 2,
            "three-disc" => 3,
            "four-disc" => 4,
            _ => 2
        };

        string m3uPath = Path.Combine(outputDir, baseName + ".m3u");
        File.WriteAllBytes(m3uPath, GenerateM3u(baseName, discCount, discExt));
        paths.Add(m3uPath);

        // Create placeholder disc files so the M3U references are valid
        for (int i = 1; i <= discCount; i++)
        {
            string discPath = Path.Combine(outputDir, $"{baseName} (Disc {i}){discExt}");
            File.WriteAllBytes(discPath, Encoding.UTF8.GetBytes($"# placeholder disc {i}"));
            paths.Add(discPath);
        }

        return paths;
    }

    private static byte[] GenerateM3u(string baseName, int discCount, string discExt)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {baseName} - Multi-Disc Playlist");
        for (int i = 1; i <= discCount; i++)
        {
            sb.AppendLine($"{baseName} (Disc {i}){discExt}");
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}
