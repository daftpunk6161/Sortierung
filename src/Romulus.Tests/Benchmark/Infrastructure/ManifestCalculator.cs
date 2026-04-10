using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Romulus.Tests.Benchmark.Infrastructure;
using Romulus.Tests.Benchmark.Models;

namespace Romulus.Tests.Benchmark.Infrastructure;

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

        // By difficulty (without prefix for cleaner JSON)
        var byDifficulty = entries
            .GroupBy(e => e.Difficulty)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.Count());

        // Systems covered (full list)
        var systemsList = entries
            .Select(e => e.Expected.ConsoleKey ?? "UNKNOWN")
            .Where(s => s != "UNKNOWN")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Systems by family (list of system keys per family)
        var systemsCoveredByFamily = entries
            .Where(e => !string.IsNullOrEmpty(e.Expected.ConsoleKey))
            .GroupBy(e => PlatformFamilyClassifier.FamilyName(
                PlatformFamilyClassifier.Classify(e.Expected.ConsoleKey!)))
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.Expected.ConsoleKey!)
                      .Distinct(StringComparer.OrdinalIgnoreCase)
                      .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                      .ToList(),
                StringComparer.OrdinalIgnoreCase);

        // Fallklasse counts (FC-01..FC-20)
        var fallklasseCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var fc in FallklasseClassifier.FallklasseNames.Keys)
            fallklasseCounts[fc] = 0;

        foreach (var entry in entries)
        {
            var fcs = FallklasseClassifier.Classify(entry.Tags);
            foreach (var fc in fcs)
                fallklasseCounts[fc] = fallklasseCounts.GetValueOrDefault(fc) + 1;
        }

        // DAT ecosystem counts
        var datEcosystemCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["no-intro"] = 0, ["redump"] = 0, ["mame"] = 0, ["tosec"] = 0
        };
        foreach (var entry in entries)
        {
            var eco = entry.Expected.DatEcosystem;
            if (!string.IsNullOrEmpty(eco) && datEcosystemCounts.ContainsKey(eco))
                datEcosystemCounts[eco]++;
        }

        // Special area counts (comprehensive – mirrors CoverageValidator.ClassifySpecialAreas)
        var specialAreaCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var biosSystems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var tags = new HashSet<string>(entry.Tags, StringComparer.OrdinalIgnoreCase);
            var ck = entry.Expected.ConsoleKey ?? "";

            void Inc(string area) => specialAreaCounts[area] = specialAreaCounts.GetValueOrDefault(area) + 1;

            if (tags.Contains("bios")) { Inc("biosTotal"); if (!string.IsNullOrEmpty(ck)) biosSystems.Add(ck); }
            if (tags.Contains("bios-wrong-name") || tags.Contains("bios-wrong-folder")
                || tags.Contains("bios-false-positive") || tags.Contains("bios-shared"))
                Inc("biosErrorModes");
            if (tags.Contains("parent") || tags.Contains("arcade-parent")) Inc("arcadeParent");
            if (tags.Contains("clone") || tags.Contains("arcade-clone")) Inc("arcadeClone");
            if (tags.Contains("arcade-bios")) Inc("arcadeBios");
            if (tags.Contains("arcade-split") || tags.Contains("arcade-merged")
                || tags.Contains("arcade-non-merged") || tags.Contains("arcade-nonmerged"))
                Inc("arcadeSplitMergedNonMerged");
            if (tags.Contains("arcade-chd") || tags.Contains("arcade-game-chd")) Inc("arcadeChdSupplement");
            if (tags.Contains("arcade-game-chd")) Inc("arcadeGameChd");
            if (tags.Contains("arcade-confusion-split-merged") || tags.Contains("arcade-confusion-merged-nonmerged"))
                Inc("arcadeConfusion");
            if (tags.Contains("multi-disc")) Inc("multiDisc");
            if (tags.Contains("multi-file")) Inc("multiFileSets");
            if (tags.Contains("cue-bin")) Inc("cueBin");
            if (tags.Contains("gdi-tracks")) Inc("gdiTracks");
            if (tags.Contains("ccd-img") || tags.Contains("mds-mdf")) Inc("ccdMds");
            if (tags.Contains("m3u-playlist")) Inc("m3uPlaylist");
            if (tags.Contains("serial-number")) Inc("serialNumber");
            if (tags.Contains("header-vs-headerless-pair")) Inc("headerVsHeaderlessPairs");
            if (tags.Contains("container-cso") || tags.Contains("container-wia")
                || tags.Contains("container-rvz") || tags.Contains("container-wbfs"))
                Inc("containerVariants");
            if (tags.Contains("chd-raw-sha1") || tags.Contains("chd-single")) Inc("chdRawSha1");
            if (tags.Contains("no-intro") || tags.Contains("dat-nointro")) Inc("datNoIntro");
            if (tags.Contains("redump") || tags.Contains("dat-redump")) Inc("datRedump");
            if (tags.Contains("mame") || tags.Contains("dat-mame")) Inc("datMame");
            if (tags.Contains("tosec") || tags.Contains("dat-tosec")) Inc("datTosec");
            if (tags.Contains("directory-based")) Inc("directoryBased");
            if (tags.Contains("keyword-detection")) Inc("keywordOnly");
            if (tags.Contains("headerless")) Inc("headerless");
            if ((tags.Contains("cross-system") || tags.Contains("cross-system-ambiguity") || tags.Contains("ps-disambiguation")) && ck is "PS1" or "PS2" or "PS3" or "PSP")
                Inc("psDisambiguation");
            if ((tags.Contains("cross-system") || tags.Contains("cross-system-ambiguity") || tags.Contains("gb-gbc-ambiguity")) && ck is "GB" or "GBC")
                Inc("gbGbcCgb");
            if ((tags.Contains("cross-system") || tags.Contains("cross-system-ambiguity") || tags.Contains("md-32x-ambiguity")) && ck is "MD" or "32X")
                Inc("md32x");
            if ((tags.Contains("cross-system-ambiguity") || tags.Contains("sat-dc-disambiguation")) && ck is "SAT" or "DC")
                Inc("satDcDisambiguation");
            if ((tags.Contains("cross-system-ambiguity") || tags.Contains("pce-pcecd-disambiguation")) && ck is "PCE" or "PCECD")
                Inc("pcePcecdDisambiguation");
        }
        specialAreaCounts["biosSystems"] = biosSystems.Count;

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

        // Coverage targets vs actuals from gates.json
        var coverageTargets = BuildCoverageTargets();
        var coverageActuals = BuildCoverageActuals(entries, byPlatformFamily, fallklasseCounts, specialAreaCounts, byDifficulty);

        return new ManifestData
        {
            Meta = new ManifestMeta
            {
                Description = "Benchmark dataset manifest – auto-generated by ManifestCalculator",
                Version = "5.0.0",
                GroundTruthVersion = "2.1.0",
                LastModified = DateTime.UtcNow.ToString("yyyy-MM-dd")
            },
            TotalEntries = entries.Count,
            HoldoutEntries = holdoutCount,
            SystemsCovered = systemsList.Count,
            SystemsList = systemsList,
            BySet = bySet,
            ByPlatformFamily = byPlatformFamily,
            ByDifficulty = byDifficulty,
            SystemsCoveredByFamily = systemsCoveredByFamily,
            FallklasseCounts = fallklasseCounts,
            DatEcosystemCounts = datEcosystemCounts,
            SpecialAreaCounts = specialAreaCounts,
            CoverageTargets = coverageTargets,
            CoverageActuals = coverageActuals,
            FileChecksums = fileChecksums
        };
    }

    public static void WriteManifest(ManifestData data, string? outputPath = null)
    {
        outputPath ??= Path.Combine(BenchmarkPaths.BenchmarkDir, "manifest.json");
        var json = JsonSerializer.Serialize(data, ManifestJsonContext.Default.ManifestData);
        File.WriteAllText(outputPath, json);
    }

    /// <summary>
    /// Builds coverage target thresholds from gates.json for inclusion in the manifest.
    /// </summary>
    private static Dictionary<string, ManifestThreshold> BuildCoverageTargets()
    {
        var targets = new Dictionary<string, ManifestThreshold>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(BenchmarkPaths.GatesJsonPath))
            return targets;

        var json = File.ReadAllText(BenchmarkPaths.GatesJsonPath);
        using var doc = JsonDocument.Parse(json);
        var s1 = doc.RootElement.GetProperty("s1");

        // Top-level thresholds
        AddThreshold(targets, "totalEntries", s1);
        AddThreshold(targets, "systemsCovered", s1);
        AddThreshold(targets, "fallklassenCovered", s1);

        // Platform family
        if (s1.TryGetProperty("platformFamily", out var pf))
        {
            foreach (var prop in pf.EnumerateObject())
                targets[$"platformFamily.{prop.Name}"] = new ManifestThreshold
                {
                    Target = prop.Value.GetProperty("target").GetInt32(),
                    HardFail = prop.Value.GetProperty("hardFail").GetInt32()
                };
        }

        // Case classes
        if (s1.TryGetProperty("caseClasses", out var cc))
        {
            foreach (var prop in cc.EnumerateObject())
                targets[$"caseClasses.{prop.Name}"] = new ManifestThreshold
                {
                    Target = prop.Value.GetProperty("target").GetInt32(),
                    HardFail = prop.Value.GetProperty("hardFail").GetInt32()
                };
        }

        // Special areas
        if (s1.TryGetProperty("specialAreas", out var sa))
        {
            foreach (var prop in sa.EnumerateObject())
                targets[$"specialAreas.{prop.Name}"] = new ManifestThreshold
                {
                    Target = prop.Value.GetProperty("target").GetInt32(),
                    HardFail = prop.Value.GetProperty("hardFail").GetInt32()
                };
        }

        return targets;
    }

    private static void AddThreshold(Dictionary<string, ManifestThreshold> targets, string name, JsonElement s1)
    {
        if (s1.TryGetProperty(name, out var el))
        {
            targets[name] = new ManifestThreshold
            {
                Target = el.GetProperty("target").GetInt32(),
                HardFail = el.GetProperty("hardFail").GetInt32()
            };
        }
    }

    /// <summary>
    /// Builds coverage actuals dictionary with same keys as coverageTargets for direct comparison.
    /// </summary>
    private static Dictionary<string, int> BuildCoverageActuals(
        IReadOnlyList<GroundTruthEntry> entries,
        Dictionary<string, int> byPlatformFamily,
        Dictionary<string, int> fallklasseCounts,
        Dictionary<string, int> specialAreaCounts,
        Dictionary<string, int> byDifficulty)
    {
        var actuals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var systemCount = entries
            .Select(e => e.Expected.ConsoleKey)
            .Where(k => !string.IsNullOrEmpty(k))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var fcCount = fallklasseCounts.Values.Count(v => v > 0);

        actuals["totalEntries"] = entries.Count;
        actuals["systemsCovered"] = systemCount;
        actuals["fallklassenCovered"] = fcCount;

        foreach (var (family, count) in byPlatformFamily)
            actuals[$"platformFamily.{family}"] = count;

        foreach (var (fc, count) in fallklasseCounts)
            actuals[$"caseClasses.{fc}"] = count;

        foreach (var (area, count) in specialAreaCounts)
            actuals[$"specialAreas.{area}"] = count;

        return actuals;
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

    [JsonPropertyName("systemsList")]
    public required List<string> SystemsList { get; init; }

    [JsonPropertyName("bySet")]
    public required Dictionary<string, int> BySet { get; init; }

    [JsonPropertyName("byPlatformFamily")]
    public required Dictionary<string, int> ByPlatformFamily { get; init; }

    [JsonPropertyName("byDifficulty")]
    public required Dictionary<string, int> ByDifficulty { get; init; }

    [JsonPropertyName("systemsCoveredByFamily")]
    public required Dictionary<string, List<string>> SystemsCoveredByFamily { get; init; }

    [JsonPropertyName("fallklasseCounts")]
    public required Dictionary<string, int> FallklasseCounts { get; init; }

    [JsonPropertyName("datEcosystemCounts")]
    public required Dictionary<string, int> DatEcosystemCounts { get; init; }

    [JsonPropertyName("specialAreaCounts")]
    public required Dictionary<string, int> SpecialAreaCounts { get; init; }

    [JsonPropertyName("coverageTargets")]
    public required Dictionary<string, ManifestThreshold> CoverageTargets { get; init; }

    [JsonPropertyName("coverageActuals")]
    public required Dictionary<string, int> CoverageActuals { get; init; }

    [JsonPropertyName("fileChecksums")]
    public required Dictionary<string, string> FileChecksums { get; init; }
}

internal sealed record ManifestThreshold
{
    [JsonPropertyName("target")]
    public required int Target { get; init; }

    [JsonPropertyName("hardFail")]
    public required int HardFail { get; init; }
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
[JsonSerializable(typeof(ManifestThreshold))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class ManifestJsonContext : JsonSerializerContext
{
}
