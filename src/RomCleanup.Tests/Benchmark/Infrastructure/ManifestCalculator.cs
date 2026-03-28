using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using RomCleanup.Tests.Benchmark.Infrastructure;
using RomCleanup.Tests.Benchmark.Models;

namespace RomCleanup.Tests.Benchmark.Infrastructure;

/// <summary>
/// Calculates full coverage metrics from ground-truth JSONL datasets
/// and produces an extended manifest.json with detailed breakdowns.
/// Used by CI/tooling to keep manifest.json in sync with actual data.
/// </summary>
internal static class ManifestCalculator
{
    public static ManifestData Calculate()
    {
        var entries = GroundTruthLoader.LoadAll();
        var files = BenchmarkPaths.AllJsonlFiles;

        // By set (filename without extension)
        var bySet = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var setName = Path.GetFileNameWithoutExtension(file);
            var setEntries = GroundTruthLoader.LoadFile(file);
            if (setEntries.Count > 0)
                bySet[setName] = setEntries.Count;
        }

        // By platform family
        var byPlatformFamily = entries
            .GroupBy(e => PlatformFamilyClassifier.FamilyName(
                PlatformFamilyClassifier.Classify(e.Expected.ConsoleKey ?? "UNKNOWN")))
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        // By difficulty
        var byDifficulty = entries
            .GroupBy(e => e.Difficulty)
            .OrderBy(g => g.Key)
            .ToDictionary(g => $"difficulty-{g.Key}", g => g.Count());

        // Systems covered
        var systemsCovered = entries
            .Select(e => e.Expected.ConsoleKey ?? "UNKNOWN")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Systems by family
        var systemsByFamily = entries
            .GroupBy(e =>
            {
                var key = e.Expected.ConsoleKey ?? "UNKNOWN";
                return PlatformFamilyClassifier.FamilyName(PlatformFamilyClassifier.Classify(key));
            })
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.Expected.ConsoleKey ?? "UNKNOWN")
                      .Distinct(StringComparer.OrdinalIgnoreCase)
                      .Count(),
                StringComparer.OrdinalIgnoreCase);

        // Special area counts
        var specialAreaCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var tags = new HashSet<string>(entry.Tags, StringComparer.OrdinalIgnoreCase);
            if (tags.Contains("bios")) specialAreaCounts["biosTotal"] = specialAreaCounts.GetValueOrDefault("biosTotal") + 1;
            if (tags.Contains("parent") || tags.Contains("arcade-parent")) specialAreaCounts["arcadeParent"] = specialAreaCounts.GetValueOrDefault("arcadeParent") + 1;
            if (tags.Contains("clone") || tags.Contains("arcade-clone")) specialAreaCounts["arcadeClone"] = specialAreaCounts.GetValueOrDefault("arcadeClone") + 1;
            if (tags.Contains("multi-disc")) specialAreaCounts["multiDisc"] = specialAreaCounts.GetValueOrDefault("multiDisc") + 1;
            if (tags.Contains("multi-file")) specialAreaCounts["multiFile"] = specialAreaCounts.GetValueOrDefault("multiFile") + 1;
            if (tags.Contains("cue-bin")) specialAreaCounts["cueBin"] = specialAreaCounts.GetValueOrDefault("cueBin") + 1;
            if (tags.Contains("gdi-tracks")) specialAreaCounts["gdiTracks"] = specialAreaCounts.GetValueOrDefault("gdiTracks") + 1;
            if (tags.Contains("ccd-img") || tags.Contains("mds-mdf")) specialAreaCounts["ccdMds"] = specialAreaCounts.GetValueOrDefault("ccdMds") + 1;
            if (tags.Contains("m3u-playlist")) specialAreaCounts["m3uPlaylist"] = specialAreaCounts.GetValueOrDefault("m3uPlaylist") + 1;
        }

        // Holdout count
        int holdoutCount = 0;
        var holdoutDir = Path.Combine(BenchmarkPaths.BenchmarkDir, "holdout");
        if (Directory.Exists(holdoutDir))
        {
            var holdoutFiles = Directory.GetFiles(holdoutDir, "*.jsonl");
            foreach (var f in holdoutFiles)
                holdoutCount += GroundTruthLoader.LoadFile(f).Count;
        }

        // File checksums
        var fileChecksums = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var hash = ComputeSha256(file);
            fileChecksums[Path.GetFileName(file)] = hash;
        }

        return new ManifestData
        {
            Meta = new ManifestMeta
            {
                Description = "Benchmark dataset manifest",
                Version = "4.0.0",
                GroundTruthVersion = "2.0.0",
                LastModified = DateTime.UtcNow.ToString("yyyy-MM-dd")
            },
            TotalEntries = entries.Count,
            HoldoutEntries = holdoutCount,
            SystemsCovered = systemsCovered.Count,
            BySet = bySet,
            ByPlatformFamily = byPlatformFamily,
            ByDifficulty = byDifficulty,
            SystemsCoveredByFamily = systemsByFamily,
            SpecialAreaCounts = specialAreaCounts,
            FileChecksums = fileChecksums
        };
    }

    public static void WriteManifest(ManifestData data, string? outputPath = null)
    {
        outputPath ??= Path.Combine(BenchmarkPaths.BenchmarkDir, "manifest.json");
        var json = JsonSerializer.Serialize(data, ManifestJsonContext.Default.ManifestData);
        File.WriteAllText(outputPath, json);
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexStringLower(hash);
    }
}

internal sealed record ManifestData
{
    [JsonPropertyName("_meta")]
    public required ManifestMeta Meta { get; init; }

    [JsonPropertyName("totalEntries")]
    public required int TotalEntries { get; init; }

    [JsonPropertyName("holdoutEntries")]
    public required int HoldoutEntries { get; init; }

    [JsonPropertyName("systemsCovered")]
    public required int SystemsCovered { get; init; }

    [JsonPropertyName("bySet")]
    public required Dictionary<string, int> BySet { get; init; }

    [JsonPropertyName("byPlatformFamily")]
    public required Dictionary<string, int> ByPlatformFamily { get; init; }

    [JsonPropertyName("byDifficulty")]
    public required Dictionary<string, int> ByDifficulty { get; init; }

    [JsonPropertyName("systemsCoveredByFamily")]
    public required Dictionary<string, int> SystemsCoveredByFamily { get; init; }

    [JsonPropertyName("specialAreaCounts")]
    public required Dictionary<string, int> SpecialAreaCounts { get; init; }

    [JsonPropertyName("fileChecksums")]
    public required Dictionary<string, string> FileChecksums { get; init; }
}

internal sealed record ManifestMeta
{
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("groundTruthVersion")]
    public required string GroundTruthVersion { get; init; }

    [JsonPropertyName("lastModified")]
    public required string LastModified { get; init; }
}

[JsonSerializable(typeof(ManifestData))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class ManifestJsonContext : JsonSerializerContext
{
}
