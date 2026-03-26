using System.Text.Json;
using RomCleanup.Tests.Benchmark.Models;

namespace RomCleanup.Tests.Benchmark.Infrastructure;

/// <summary>
/// Evaluates coverage of ground-truth entries against gate thresholds from gates.json.
/// Produces a CoverageReport with pass/fail status for each gate.
/// </summary>
internal sealed class CoverageValidator
{
    private readonly GateConfiguration _gates;

    public CoverageValidator(GateConfiguration gates)
    {
        _gates = gates;
    }

    public static CoverageValidator CreateFromGatesJson()
    {
        var json = File.ReadAllText(BenchmarkPaths.GatesJsonPath);
        var gates = JsonSerializer.Deserialize<GateConfigWrapper>(json)
            ?? throw new InvalidOperationException("Failed to deserialize gates.json");
        return new CoverageValidator(new GateConfiguration { S1 = gates.S1 });
    }

    public CoverageReport Evaluate(IReadOnlyList<GroundTruthEntry> entries)
    {
        var gateResults = new List<GateResult>();

        // Count by platform family
        var byPlatformFamily = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var fam in new[] { "cartridge", "disc", "arcade", "computer", "hybrid" })
            byPlatformFamily[fam] = 0;

        // Count by Fallklasse
        var byFallklasse = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var fc in FallklasseClassifier.FallklasseNames.Keys)
            byFallklasse[fc] = 0;

        // Count by difficulty
        var byDifficulty = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Count by set (source file)
        var bySet = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Count by system
        var systemEntryCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        // Count by special areas
        var bySpecialArea = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var uniqueSystems = new HashSet<string>(StringComparer.Ordinal);
        var coveredFallklassen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            // Platform family
            var consoleKey = entry.Expected.ConsoleKey;
            if (!string.IsNullOrEmpty(consoleKey))
            {
                var family = PlatformFamilyClassifier.Classify(consoleKey);
                var familyName = PlatformFamilyClassifier.FamilyName(family);
                byPlatformFamily[familyName] = byPlatformFamily.GetValueOrDefault(familyName) + 1;

                uniqueSystems.Add(consoleKey);
                systemEntryCounts[consoleKey] = systemEntryCounts.GetValueOrDefault(consoleKey) + 1;
            }

            // Fallklassen
            var fallklassen = FallklasseClassifier.Classify(entry.Tags);
            foreach (var fc in fallklassen)
            {
                byFallklasse[fc] = byFallklasse.GetValueOrDefault(fc) + 1;
                coveredFallklassen.Add(fc);
            }

            // Difficulty
            if (!string.IsNullOrEmpty(entry.Difficulty))
                byDifficulty[entry.Difficulty] = byDifficulty.GetValueOrDefault(entry.Difficulty) + 1;

            // Special areas
            ClassifySpecialAreas(entry, bySpecialArea);
        }

        // === Gate evaluations ===
        var s1 = _gates.S1;

        // Total entries
        gateResults.Add(EvaluateGate("s1.totalEntries", entries.Count, s1.TotalEntries));

        // Systems covered
        gateResults.Add(EvaluateGate("s1.systemsCovered", uniqueSystems.Count, s1.SystemsCovered));

        // Fallklassen covered
        gateResults.Add(EvaluateGate("s1.fallklassenCovered", coveredFallklassen.Count, s1.FallklassenCovered));

        // Platform family gates
        foreach (var (family, threshold) in s1.PlatformFamily)
        {
            var count = byPlatformFamily.GetValueOrDefault(family);
            gateResults.Add(EvaluateGate($"s1.platformFamily.{family}", count, threshold));
        }

        // Tier depth gates
        foreach (var (tier, threshold) in s1.TierDepth)
        {
            if (threshold.Systems is not null)
            {
                // Check minimum per system for this tier
                var minForTier = threshold.Systems
                    .Select(s => systemEntryCounts.GetValueOrDefault(s))
                    .DefaultIfEmpty(0)
                    .Min();
                gateResults.Add(new GateResult
                {
                    GateName = $"s1.tierDepth.{tier}.minPerSystem",
                    Actual = minForTier,
                    Target = threshold.MinPerSystem,
                    HardFail = threshold.HardFail,
                    Status = minForTier >= threshold.MinPerSystem ? GateStatus.Pass
                        : minForTier >= threshold.HardFail ? GateStatus.Warning
                        : GateStatus.Fail
                });
            }
        }

        // Case class gates
        foreach (var (fc, threshold) in s1.CaseClasses)
        {
            var count = byFallklasse.GetValueOrDefault(fc);
            gateResults.Add(EvaluateGate($"s1.caseClasses.{fc}", count,
                new GateThreshold { Target = threshold.Target, HardFail = threshold.HardFail }));
        }

        // Special area gates
        foreach (var (area, threshold) in s1.SpecialAreas)
        {
            var count = bySpecialArea.GetValueOrDefault(area);
            gateResults.Add(EvaluateGate($"s1.specialAreas.{area}", count, threshold));
        }

        var overallPass = gateResults.All(g => g.Status != GateStatus.Fail);

        return new CoverageReport
        {
            TotalEntries = entries.Count,
            SystemsCovered = uniqueSystems.Count,
            FallklassenCovered = coveredFallklassen.Count,
            ByPlatformFamily = byPlatformFamily,
            ByFallklasse = byFallklasse,
            BySpecialArea = bySpecialArea,
            ByDifficulty = byDifficulty,
            BySet = bySet,
            SystemEntryCounts = systemEntryCounts,
            GateResults = gateResults,
            OverallPass = overallPass
        };
    }

    private static GateResult EvaluateGate(string name, int actual, GateThreshold threshold)
    {
        var status = actual >= threshold.Target ? GateStatus.Pass
            : actual >= threshold.HardFail ? GateStatus.Warning
            : GateStatus.Fail;

        return new GateResult
        {
            GateName = name,
            Actual = actual,
            Target = threshold.Target,
            HardFail = threshold.HardFail,
            Status = status
        };
    }

    private static void ClassifySpecialAreas(GroundTruthEntry entry, Dictionary<string, int> counts)
    {
        var tags = new HashSet<string>(entry.Tags, StringComparer.OrdinalIgnoreCase);

        void Inc(string area)
        {
            counts[area] = counts.GetValueOrDefault(area) + 1;
        }

        // BIOS
        if (tags.Contains("bios"))
        {
            Inc("biosTotal");
            // biosSystems tracked at validation level (unique systems with BIOS entries)
        }

        // Arcade
        if (tags.Contains("parent"))
            Inc("arcadeParent");
        if (tags.Contains("clone"))
            Inc("arcadeClone");
        if (tags.Contains("arcade-split") || tags.Contains("arcade-merged") || tags.Contains("arcade-non-merged"))
            Inc("arcadeSplitMergedNonMerged");
        if (tags.Contains("arcade-bios"))
            Inc("arcadeBios");
        if (tags.Contains("arcade-chd"))
            Inc("arcadeChdSupplement");

        // Disambiguation
        var consoleKey = entry.Expected.ConsoleKey ?? "";
        if (tags.Contains("cross-system"))
        {
            if (consoleKey is "PS1" or "PS2" or "PS3")
                Inc("psDisambiguation");
            if (consoleKey is "GB" or "GBC")
                Inc("gbGbcCgb");
            if (consoleKey is "MD" or "32X")
                Inc("md32x");
        }

        // Multi-file / multi-disc
        if (tags.Contains("multi-file"))
            Inc("multiFileSets");
        if (tags.Contains("multi-disc"))
            Inc("multiDisc");

        // CHD
        if (tags.Contains("chd-raw-sha1"))
            Inc("chdRawSha1");

        // DAT ecosystems
        if (tags.Contains("no-intro"))
            Inc("datNoIntro");
        if (tags.Contains("redump"))
            Inc("datRedump");
        if (tags.Contains("mame"))
            Inc("datMame");
        if (tags.Contains("tosec"))
            Inc("datTosec");

        // Directory-based
        if (tags.Contains("directory-based"))
            Inc("directoryBased");

        // Headerless
        if (tags.Contains("headerless"))
            Inc("headerless");
    }

    /// <summary>
    /// Internal wrapper for JSON deserialization of gates.json (which has _meta at root level).
    /// </summary>
    private sealed record GateConfigWrapper
    {
        [System.Text.Json.Serialization.JsonPropertyName("s1")]
        public required S1Gate S1 { get; init; }
    }
}
