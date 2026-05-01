using System.Text.Json;
using Romulus.Tests.Benchmark.Models;

namespace Romulus.Tests.Benchmark.Infrastructure;

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

        // Difficulty distribution gates (ratio-based)
        if (s1.DifficultyDistribution is { } dd && entries.Count > 0)
        {
            double total = entries.Count;
            double easyRatio = byDifficulty.GetValueOrDefault("easy") / total;
            double mediumRatio = byDifficulty.GetValueOrDefault("medium") / total;
            double hardRatio = byDifficulty.GetValueOrDefault("hard") / total;
            double adversarialRatio = byDifficulty.GetValueOrDefault("adversarial") / total;

            gateResults.Add(EvaluateRatioMaxGate("s1.difficultyDistribution.easyMax", easyRatio, dd.EasyMax));
            gateResults.Add(EvaluateRatioMinGate("s1.difficultyDistribution.mediumMin", mediumRatio, dd.MediumMin));
            gateResults.Add(EvaluateRatioMinGate("s1.difficultyDistribution.hardMin", hardRatio, dd.HardMin));
            gateResults.Add(EvaluateRatioMinGate("s1.difficultyDistribution.adversarialMin", adversarialRatio, dd.AdversarialMin));
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

    /// <summary>
    /// Evaluates a ratio-based MIN gate: actual ratio must be >= target.
    /// Stores permille (×1000) in Actual/Target/HardFail for integer display.
    /// </summary>
    private static GateResult EvaluateRatioMinGate(string name, double actualRatio, RatioThreshold threshold)
    {
        var status = actualRatio >= threshold.Target ? GateStatus.Pass
            : actualRatio >= threshold.HardFail ? GateStatus.Warning
            : GateStatus.Fail;

        return new GateResult
        {
            GateName = name,
            Actual = (int)(actualRatio * 1000),
            Target = (int)(threshold.Target * 1000),
            HardFail = (int)(threshold.HardFail * 1000),
            Status = status
        };
    }

    /// <summary>
    /// Evaluates a ratio-based MAX gate: actual ratio must be <= target.
    /// Stores permille (×1000) in Actual/Target/HardFail for integer display.
    /// </summary>
    private static GateResult EvaluateRatioMaxGate(string name, double actualRatio, RatioThreshold threshold)
    {
        var status = actualRatio <= threshold.Target ? GateStatus.Pass
            : actualRatio <= threshold.HardFail ? GateStatus.Warning
            : GateStatus.Fail;

        return new GateResult
        {
            GateName = name,
            Actual = (int)(actualRatio * 1000),
            Target = (int)(threshold.Target * 1000),
            HardFail = (int)(threshold.HardFail * 1000),
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

        // BIOS error modes
        if (tags.Contains("bios-wrong-name") || tags.Contains("bios-wrong-folder")
            || tags.Contains("bios-false-positive") || tags.Contains("bios-shared"))
            Inc("biosErrorModes");

        // Arcade
        if (tags.Contains("parent") || tags.Contains("arcade-parent"))
            Inc("arcadeParent");
        if (tags.Contains("clone") || tags.Contains("arcade-clone"))
            Inc("arcadeClone");
        if (tags.Contains("arcade-split") || tags.Contains("arcade-merged") || tags.Contains("arcade-non-merged")
            || tags.Contains("arcade-nonmerged"))
            Inc("arcadeSplitMergedNonMerged");
        if (tags.Contains("arcade-bios"))
            Inc("arcadeBios");
        if (tags.Contains("arcade-chd") || tags.Contains("arcade-game-chd"))
            Inc("arcadeChdSupplement");

        // Arcade confusion
        if (tags.Contains("arcade-confusion-split-merged") || tags.Contains("arcade-confusion-merged-nonmerged"))
            Inc("arcadeConfusion");

        // Disambiguation
        var consoleKey = entry.Expected.ConsoleKey ?? "";
        if (tags.Contains("cross-system") || tags.Contains("cross-system-ambiguity"))
        {
            if (consoleKey is "PS1" or "PS2" or "PS3" or "PSP")
                Inc("psDisambiguation");
            if (consoleKey is "GB" or "GBC")
                Inc("gbGbcCgb");
            if (consoleKey is "MD" or "32X")
                Inc("md32x");
        }

        // SAT/DC disambiguation
        if (tags.Contains("cross-system-ambiguity") && consoleKey is "SAT" or "DC")
            Inc("satDcDisambiguation");

        // PCE/PCECD disambiguation
        if (tags.Contains("cross-system-ambiguity") && consoleKey is "PCE" or "PCECD")
            Inc("pcePcecdDisambiguation");

        // Multi-file / multi-disc
        if (tags.Contains("multi-file"))
            Inc("multiFileSets");
        if (tags.Contains("multi-disc"))
            Inc("multiDisc");

        // Disc format variants
        if (tags.Contains("cue-bin"))
            Inc("cueBin");
        if (tags.Contains("gdi-tracks"))
            Inc("gdiTracks");
        if (tags.Contains("ccd-img") || tags.Contains("mds-mdf"))
            Inc("ccdMds");
        if (tags.Contains("m3u-playlist"))
            Inc("m3uPlaylist");

        // Serial number
        if (tags.Contains("serial-number"))
            Inc("serialNumber");

        // Header vs headerless pairs
        if (tags.Contains("header-vs-headerless-pair"))
            Inc("headerVsHeaderlessPairs");

        // Container variants
        if (tags.Contains("container-cso") || tags.Contains("container-wia")
            || tags.Contains("container-rvz") || tags.Contains("container-wbfs"))
            Inc("containerVariants");

        // CHD
        if (tags.Contains("chd-raw-sha1"))
            Inc("chdRawSha1");

        // DAT ecosystems
        if (tags.Contains("no-intro") || tags.Contains("dat-nointro"))
            Inc("datNoIntro");
        if (tags.Contains("redump") || tags.Contains("dat-redump"))
            Inc("datRedump");
        if (tags.Contains("mame") || tags.Contains("dat-mame"))
            Inc("datMame");
        if (tags.Contains("tosec") || tags.Contains("dat-tosec"))
            Inc("datTosec");

        // Directory-based
        if (tags.Contains("directory-based"))
            Inc("directoryBased");

        // Keyword-only detection
        if (tags.Contains("keyword-detection"))
            Inc("keywordOnly");

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
