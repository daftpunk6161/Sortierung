using Romulus.Tests.Benchmark.Infrastructure;
using Xunit;

namespace Romulus.Tests.Benchmark;

/// <summary>
/// Journey Matrix Gate: Validates that the benchmark dataset covers all 11 pipeline
/// journey stages end-to-end. Each test ensures a minimum viable ground truth
/// coverage for one pipeline stage/capability.
///
/// TASK-110: CI-Gate Journey Matrix Gate (11 Gate-Testklassen).
/// </summary>
[Trait("Category", "JourneyMatrixGate")]
[Collection("BenchmarkGroundTruth")]
public sealed class JourneyMatrixGateTests
{
    // ═══ J-01: Classification Journey ═══════════════════════════════════

    [Fact]
    public void Journey_Classification_CoverageMinimum()
    {
        var entries = GroundTruthLoader.LoadAll();
        var classified = entries.Where(e =>
            !string.IsNullOrEmpty(e.Expected.Category)).ToList();

        Assert.True(classified.Count >= 500,
            $"Classification journey needs ≥500 entries with expected.category, found {classified.Count}");

        // Must have all major categories represented (case-insensitive)
        var categories = classified.Select(e => e.Expected.Category)
            .Select(c => c.ToUpperInvariant()).Distinct().ToList();
        Assert.Contains("GAME", categories);
        Assert.Contains("JUNK", categories);
        Assert.Contains("BIOS", categories);
    }

    // ═══ J-02: Console Detection Journey ════════════════════════════════

    [Fact]
    public void Journey_ConsoleDetection_CoverageMinimum()
    {
        var entries = GroundTruthLoader.LoadAll();
        var withConsole = entries.Where(e =>
            !string.IsNullOrEmpty(e.Expected.ConsoleKey)).ToList();

        Assert.True(withConsole.Count >= 1000,
            $"Console detection journey needs ≥1000 entries with expected.consoleKey, found {withConsole.Count}");

        var systems = withConsole.Select(e => e.Expected.ConsoleKey).Distinct().Count();
        Assert.True(systems >= 30,
            $"Console detection journey needs ≥30 distinct consoleKeys, found {systems}");
    }

    // ═══ J-03: Region Detection Journey ═════════════════════════════════

    [Fact]
    public void Journey_RegionDetection_CoverageMinimum()
    {
        var entries = GroundTruthLoader.LoadAll();
        // Region info is carried in filename or tags (e.g. "region-*" tags)
        var withRegion = entries.Where(e =>
            e.Tags.Any(t => t.StartsWith("region-", StringComparison.OrdinalIgnoreCase))).ToList();

        Assert.True(withRegion.Count >= 50,
            $"Region detection journey needs ≥50 entries with region tags, found {withRegion.Count}");
    }

    // ═══ J-04: Deduplication Journey ════════════════════════════════════

    [Fact]
    public void Journey_Deduplication_HasDupeAndKeepEntries()
    {
        var entries = GroundTruthLoader.LoadAll();
        // Deduplication is tracked via sortDecision: "sort" = sorted/kept, "block" = blocked/duped
        var sortEntries = entries.Where(e =>
            string.Equals(e.Expected.SortDecision, "sort", StringComparison.OrdinalIgnoreCase)).ToList();
        var blockEntries = entries.Where(e =>
            string.Equals(e.Expected.SortDecision, "block", StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.True(sortEntries.Count >= 100,
            $"Deduplication journey needs ≥100 sort entries, found {sortEntries.Count}");
        Assert.True(blockEntries.Count >= 100,
            $"Deduplication journey needs ≥100 block entries, found {blockEntries.Count}");
    }

    // ═══ J-05: DAT Verification Journey ═════════════════════════════════

    [Fact]
    public void Journey_DatVerification_CoverageMinimum()
    {
        var entries = GroundTruthLoader.LoadAll();
        var withDat = entries.Where(e =>
            !string.IsNullOrEmpty(e.Expected.DatMatchLevel) ||
            !string.IsNullOrEmpty(e.Expected.DatEcosystem)).ToList();

        Assert.True(withDat.Count >= 200,
            $"DAT verification journey needs ≥200 entries with datMatchLevel/datEcosystem, found {withDat.Count}");
    }

    // ═══ J-06: Sorting Journey ══════════════════════════════════════════

    [Fact]
    public void Journey_Sorting_CoverageMinimum()
    {
        var entries = GroundTruthLoader.LoadAll();
        var withSortDecision = entries.Where(e =>
            !string.IsNullOrEmpty(e.Expected.SortDecision)).ToList();

        Assert.True(withSortDecision.Count >= 300,
            $"Sorting journey needs ≥300 entries with expected.sortDecision, found {withSortDecision.Count}");

        var decisions = withSortDecision.Select(e => e.Expected.SortDecision).Distinct().ToList();
        Assert.True(decisions.Count >= 2,
            $"Sorting journey needs ≥2 distinct sortDecision values, found {decisions.Count}");
    }

    // ═══ J-07: Junk Detection Journey ═══════════════════════════════════

    [Fact]
    public void Journey_JunkDetection_CoverageMinimum()
    {
        var entries = GroundTruthLoader.LoadAll();
        var junk = entries.Where(e =>
            string.Equals(e.Expected.Category, "Junk", StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.True(junk.Count >= 100,
            $"Junk detection journey needs ≥100 Junk entries, found {junk.Count}");

        // Should cover multiple junk reasons via tags
        var junkTags = junk.SelectMany(e => e.Tags).Distinct().Count();
        Assert.True(junkTags >= 3,
            $"Junk detection journey should cover ≥3 distinct tags, found {junkTags}");
    }

    // ═══ J-08: Multi-File Set Journey ═══════════════════════════════════

    [Fact]
    public void Journey_MultiFileSet_CoverageMinimum()
    {
        var entries = GroundTruthLoader.LoadAll();
        var multiFile = entries.Where(e =>
            (e.FileModel?.SetFiles is { Length: > 0 }) ||
            e.Tags.Any(t => t.Contains("multi-file", StringComparison.OrdinalIgnoreCase) ||
                            t.Contains("cue-bin", StringComparison.OrdinalIgnoreCase) ||
                            t.Contains("gdi", StringComparison.OrdinalIgnoreCase) ||
                            t.Contains("m3u", StringComparison.OrdinalIgnoreCase))).ToList();

        Assert.True(multiFile.Count >= 15,
            $"Multi-file set journey needs ≥15 entries, found {multiFile.Count}");
    }

    // ═══ J-09: BIOS Handling Journey ════════════════════════════════════

    [Fact]
    public void Journey_BiosHandling_CoverageMinimum()
    {
        var entries = GroundTruthLoader.LoadAll();
        var bios = entries.Where(e =>
            string.Equals(e.Expected.Category, "BIOS", StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.True(bios.Count >= 30,
            $"BIOS handling journey needs ≥30 BIOS entries, found {bios.Count}");

        var biosSystems = bios
            .Select(e => e.Expected.ConsoleKey)
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct().Count();
        Assert.True(biosSystems >= 10,
            $"BIOS handling journey should cover ≥10 distinct consoleKeys, found {biosSystems}");
    }

    // ═══ J-10: Negative Controls Journey ════════════════════════════════

    [Fact]
    public void Journey_NegativeControls_CoverageMinimum()
    {
        var entries = GroundTruthLoader.LoadAll();
        var negative = entries.Where(e =>
            e.Tags.Any(t => t.Contains("negative", StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(e.Expected.Category, "Unknown", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.Expected.Category, "NonGame", StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.True(negative.Count >= 30,
            $"Negative controls journey needs ≥30 entries, found {negative.Count}");
    }

    // ═══ J-11: Holdout Blind Journey ════════════════════════════════════

    [Fact]
    public void Journey_HoldoutBlind_CoverageMinimum()
    {
        var holdoutPath = BenchmarkPaths.HoldoutJsonlPath;
        Assert.True(File.Exists(holdoutPath),
            $"Holdout file not found at {holdoutPath}");

        var holdout = GroundTruthLoader.LoadFile(holdoutPath);
        Assert.True(holdout.Count >= 20,
            $"Holdout blind journey needs ≥20 entries, found {holdout.Count}");

        // Holdout entries should not appear in main ground truth
        var mainIds = new HashSet<string>(
            GroundTruthLoader.LoadAll().Select(e => e.Id), StringComparer.Ordinal);
        var overlap = holdout.Where(e => mainIds.Contains(e.Id)).ToList();

        Assert.True(overlap.Count == 0,
            $"Holdout entries must not overlap with main GT. Overlap: {string.Join(", ", overlap.Select(e => e.Id))}");
    }
}
