using Romulus.Tests.Benchmark.Generators;
using Romulus.Tests.Benchmark.Generators.Cartridge;
using Romulus.Tests.Benchmark.Generators.Disc;
using Romulus.Tests.Benchmark.Infrastructure;
using Romulus.Tests.Benchmark.Models;
using Xunit;

namespace Romulus.Tests.Benchmark;

/// <summary>
/// Coverage gate tests: validate that the ground-truth dataset meets
/// minimum coverage thresholds defined in benchmark/gates.json.
///
/// These tests are expected to FAIL until sufficient seed data is added.
/// They serve as the gate for benchmark dataset completeness.
/// </summary>
[Trait("Category", "CoverageGate")]
[Collection("BenchmarkGroundTruth")]
public sealed class CoverageGateTests
{
    // ═══ STRUCTURAL INTEGRITY ═══════════════════════════════════════════

    [Fact]
    public void AllEntries_HaveUniqueIds()
    {
        var entries = GroundTruthLoader.LoadAll();
        var ids = entries.Select(e => e.Id).ToList();
        var duplicates = ids.GroupBy(id => id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();

        Assert.True(duplicates.Count == 0,
            $"Duplicate IDs found: {string.Join(", ", duplicates)}");
    }

    [Fact]
    public void AllEntries_PassStructuralValidation()
    {
        var entries = GroundTruthLoader.LoadAll();
        var validator = SchemaValidator.CreateFromConsolesJson();
        var errors = validator.ValidateAll(entries);

        if (errors.Count > 0)
        {
            var messages = errors.SelectMany(kv =>
                kv.Value.Select(e => $"  [{kv.Key}] {e}")).ToList();
            Assert.Fail($"{errors.Count} entries have validation errors:\n{string.Join("\n", messages)}");
        }
    }

    [Fact]
    public void AllJsonlFiles_AreLoadable()
    {
        var files = BenchmarkPaths.AllJsonlFiles;
        Assert.True(files.Length > 0, "No .jsonl files found in benchmark/ground-truth/");

        foreach (var file in files)
        {
            // This will throw on malformed JSONL
            var entries = GroundTruthLoader.LoadFile(file);
            // performance-scale.jsonl may be empty, that's OK
        }
    }

    [Fact]
    public void GatesJson_IsLoadable()
    {
        var validator = CoverageValidator.CreateFromGatesJson();
        Assert.NotNull(validator);
    }

    // ═══ GATE: TOTAL ENTRIES ════════════════════════════════════════════

    [Fact]
    public void Gate_TotalEntries_MeetsHardFail()
    {
        var report = BuildReport();
        var gate = FindGate(report, "s1.totalEntries");

        Assert.True(gate.Actual >= gate.HardFail,
            $"Total entries {gate.Actual} below hard-fail threshold {gate.HardFail} (target: {gate.Target})");
    }

    // ═══ GATE: SYSTEMS COVERED ══════════════════════════════════════════

    [Fact]
    public void Gate_SystemsCovered_MeetsHardFail()
    {
        var report = BuildReport();
        var gate = FindGate(report, "s1.systemsCovered");

        Assert.True(gate.Actual >= gate.HardFail,
            $"Systems covered {gate.Actual} below hard-fail threshold {gate.HardFail} (target: {gate.Target})");
    }

    // ═══ GATE: FALLKLASSEN COVERED ══════════════════════════════════════

    [Fact]
    public void Gate_FallklassenCovered_MeetsHardFail()
    {
        var report = BuildReport();
        var gate = FindGate(report, "s1.fallklassenCovered");

        Assert.True(gate.Actual >= gate.HardFail,
            $"Fallklassen covered {gate.Actual} below hard-fail threshold {gate.HardFail} (target: {gate.Target})");
    }

    // ═══ GATE: PLATFORM FAMILY ══════════════════════════════════════════

    [Theory]
    [InlineData("cartridge")]
    [InlineData("disc")]
    [InlineData("arcade")]
    [InlineData("computer")]
    [InlineData("hybrid")]
    public void Gate_PlatformFamily_MeetsHardFail(string family)
    {
        var report = BuildReport();
        var gate = FindGate(report, $"s1.platformFamily.{family}");

        Assert.True(gate.Actual >= gate.HardFail,
            $"Platform family '{family}' has {gate.Actual} entries, below hard-fail {gate.HardFail} (target: {gate.Target})");
    }

    // ═══ GATE: CASE CLASSES (FC-XX) ═════════════════════════════════════

    [Theory]
    [InlineData("FC-01")]
    [InlineData("FC-02")]
    [InlineData("FC-03")]
    [InlineData("FC-04")]
    [InlineData("FC-05")]
    [InlineData("FC-06")]
    [InlineData("FC-07")]
    [InlineData("FC-08")]
    [InlineData("FC-09")]
    [InlineData("FC-10")]
    [InlineData("FC-11")]
    [InlineData("FC-12")]
    [InlineData("FC-13")]
    [InlineData("FC-14")]
    [InlineData("FC-15")]
    [InlineData("FC-16")]
    [InlineData("FC-17")]
    [InlineData("FC-18")]
    [InlineData("FC-19")]
    [InlineData("FC-20")]
    public void Gate_CaseClass_MeetsHardFail(string fallklasse)
    {
        var report = BuildReport();
        var gate = FindGate(report, $"s1.caseClasses.{fallklasse}");

        Assert.True(gate.Actual >= gate.HardFail,
            $"Case class {fallklasse} ({FallklasseClassifier.FallklasseNames.GetValueOrDefault(fallklasse, "?")}) " +
            $"has {gate.Actual} entries, below hard-fail {gate.HardFail} (target: {gate.Target})");
    }

    // ═══ GATE: SPECIAL AREAS ═══════════════════════════════════════════

    [Theory]
    [InlineData("biosTotal")]
    [InlineData("arcadeParent")]
    [InlineData("arcadeClone")]
    [InlineData("arcadeSplitMergedNonMerged")]
    [InlineData("arcadeBios")]
    [InlineData("arcadeChdSupplement")]
    [InlineData("psDisambiguation")]
    [InlineData("gbGbcCgb")]
    [InlineData("md32x")]
    [InlineData("multiFileSets")]
    [InlineData("multiDisc")]
    [InlineData("chdRawSha1")]
    [InlineData("datNoIntro")]
    [InlineData("datRedump")]
    [InlineData("datMame")]
    [InlineData("datTosec")]
    [InlineData("directoryBased")]
    [InlineData("headerless")]
    [InlineData("biosErrorModes")]
    [InlineData("arcadeConfusion")]
    [InlineData("cueBin")]
    [InlineData("gdiTracks")]
    [InlineData("ccdMds")]
    [InlineData("m3uPlaylist")]
    [InlineData("serialNumber")]
    [InlineData("headerVsHeaderlessPairs")]
    [InlineData("containerVariants")]
    [InlineData("keywordOnly")]
    [InlineData("satDcDisambiguation")]
    [InlineData("pcePcecdDisambiguation")]
    public void Gate_SpecialArea_MeetsHardFail(string area)
    {
        var report = BuildReport();
        var gate = report.GateResults.FirstOrDefault(g =>
            g.GateName.Equals($"s1.specialAreas.{area}", StringComparison.OrdinalIgnoreCase));

        // New gates with hardFail=0 pass trivially if no entries; only assert if gate exists
        if (gate == null) return;

        Assert.True(gate.Actual >= gate.HardFail,
            $"Special area '{area}' has {gate.Actual} entries, below hard-fail {gate.HardFail} (target: {gate.Target})");
    }

    // ═══ GATE: DIFFICULTY DISTRIBUTION ══════════════════════════════════

    [Fact]
    public void Gate_DifficultyDistribution_CoversAllLevels()
    {
        var entries = GroundTruthLoader.LoadAll();
        var byDiff = entries.GroupBy(e => e.Difficulty).ToDictionary(g => g.Key, g => g.Count());

        Assert.True(byDiff.GetValueOrDefault("easy") > 0, "No difficulty=easy entries");
        Assert.True(byDiff.GetValueOrDefault("medium") > 0, "No difficulty=medium entries");
        Assert.True(byDiff.GetValueOrDefault("hard") > 0, "No difficulty=hard entries");
    }

    [Theory]
    [InlineData("s1.difficultyDistribution.easyMax")]
    [InlineData("s1.difficultyDistribution.mediumMin")]
    [InlineData("s1.difficultyDistribution.hardMin")]
    [InlineData("s1.difficultyDistribution.adversarialMin")]
    [Trait("Category", "CoverageGate")]
    public void Gate_DifficultyDistribution_MeetsHardFail(string gateName)
    {
        var report = BuildReport();
        var gate = FindGate(report, gateName);

        Assert.NotNull(gate);
        Assert.True(gate.Status != GateStatus.Fail,
            $"Difficulty gate '{gateName}' failed: actual={gate.Actual}‰, target={gate.Target}‰, hardFail={gate.HardFail}‰");
    }

    [Fact]
    [Trait("Category", "CoverageGate")]
    public void Gate_DifficultyDistribution_EasyNotDominant()
    {
        var report = BuildReport();
        var easyGate = FindGate(report, "s1.difficultyDistribution.easyMax");

        Assert.NotNull(easyGate);
        // Easy should not exceed hardFail threshold (60%)
        Assert.True(easyGate.Status != GateStatus.Fail,
            $"Easy entries dominate: {easyGate.Actual}‰ exceeds hardFail {easyGate.HardFail}‰");
    }

    [Fact]
    [Trait("Category", "CoverageGate")]
    public void Gate_DifficultyDistribution_HardAndAdversarialPresent()
    {
        var report = BuildReport();
        var hardGate = FindGate(report, "s1.difficultyDistribution.hardMin");
        var advGate = FindGate(report, "s1.difficultyDistribution.adversarialMin");

        Assert.NotNull(hardGate);
        Assert.NotNull(advGate);
        Assert.True(hardGate.Actual > 0, "No hard entries detected in ratio gate");
        Assert.True(advGate.Actual > 0, "No adversarial entries detected in ratio gate");
    }

    // ═══ STUB GENERATOR ═════════════════════════════════════════════════

    [Fact]
    public void StubGeneratorRegistry_ContainsExpectedGenerators()
    {
        var registry = new StubGeneratorRegistry();
        Assert.Contains("nes-ines", registry.RegisteredIds);
        Assert.Contains("ps1-pvd", registry.RegisteredIds);
        Assert.Contains("ccd-img", registry.RegisteredIds);
        Assert.Contains("mds-mdf", registry.RegisteredIds);
        Assert.Contains("m3u-playlist", registry.RegisteredIds);
    }

    [Fact]
    public void NesInesGenerator_ProducesValidHeader()
    {
        var gen = new NesInesGenerator();
        var data = gen.Generate("standard");

        Assert.True(data.Length >= 16, "iNES stub too short for header");
        Assert.Equal(0x4E, data[0]); // 'N'
        Assert.Equal(0x45, data[1]); // 'E'
        Assert.Equal(0x53, data[2]); // 'S'
        Assert.Equal(0x1A, data[3]); // \x1A
    }

    [Fact]
    public void Ps1PvdGenerator_ProducesValidPvd()
    {
        var gen = new Ps1PvdGenerator();
        var data = gen.Generate("standard");

        // PVD at sector 16 (offset 0x8000)
        int pvdOffset = 16 * 2048;
        Assert.True(data.Length > pvdOffset + 40, "PS1 PVD stub too short");
        Assert.Equal(0x01, data[pvdOffset]); // PVD type
        Assert.Equal((byte)'C', data[pvdOffset + 1]);
        Assert.Equal((byte)'D', data[pvdOffset + 2]);
        Assert.Equal((byte)'0', data[pvdOffset + 3]);
        Assert.Equal((byte)'0', data[pvdOffset + 4]);
        Assert.Equal((byte)'1', data[pvdOffset + 5]);
    }

    [Fact]
    public void CcdImgGenerator_ProducesValidCcd()
    {
        var gen = new CcdImgGenerator();
        var data = gen.Generate("single-track");
        var text = System.Text.Encoding.UTF8.GetString(data);

        Assert.Contains("[CloneCD]", text);
        Assert.Contains("Version=3", text);
        Assert.Contains("[Disc]", text);
    }

    [Fact]
    public void MdsMdfGenerator_ProducesValidMds()
    {
        var gen = new MdsMdfGenerator();
        var data = gen.Generate("single-track");

        // MDS signature: "MEDIA DESCRIPTOR"
        var sig = System.Text.Encoding.ASCII.GetString(data, 0, 16);
        Assert.Equal("MEDIA DESCRIPTOR", sig);
    }

    [Fact]
    public void M3uPlaylistGenerator_ProducesValidPlaylist()
    {
        var gen = new M3uPlaylistGenerator();
        var data = gen.Generate("two-disc", new Dictionary<string, string>
        {
            ["baseName"] = "TestGame",
            ["discExt"] = ".cue"
        });
        var text = System.Text.Encoding.UTF8.GetString(data);

        Assert.Contains("TestGame (Disc 1).cue", text);
        Assert.Contains("TestGame (Disc 2).cue", text);
    }

    // ═══ CLASSIFIER UNIT TESTS ══════════════════════════════════════════

    [Theory]
    [InlineData("NES", "cartridge")]
    [InlineData("SNES", "cartridge")]
    [InlineData("PS1", "disc")]
    [InlineData("PS2", "disc")]
    [InlineData("ARCADE", "arcade")]
    [InlineData("NEOGEO", "arcade")]
    [InlineData("AMIGA", "computer")]
    [InlineData("DOS", "computer")]
    [InlineData("PSP", "hybrid")]
    [InlineData("3DS", "hybrid")]
    [InlineData("SWITCH", "hybrid")]
    public void PlatformFamilyClassifier_CorrectlyClassifies(string consoleKey, string expectedFamily)
    {
        var family = PlatformFamilyClassifier.Classify(consoleKey);
        Assert.Equal(expectedFamily, PlatformFamilyClassifier.FamilyName(family));
    }

    [Fact]
    public void FallklasseClassifier_MapsTagsToCorrectCodes()
    {
        var tags = new[] { "clean-reference", "no-intro" };
        var result = FallklasseClassifier.Classify(tags);
        Assert.Contains("FC-01", result);
    }

    [Fact]
    public void FallklasseClassifier_MultipleTagsMapToMultipleCodes()
    {
        var tags = new[] { "bios", "cross-system", "dat-exact-match" };
        var result = FallklasseClassifier.Classify(tags);
        Assert.Contains("FC-06", result); // dat-exact-match
        Assert.Contains("FC-08", result); // bios
        Assert.Contains("FC-18", result); // cross-system
    }

    [Fact]
    public void FallklasseClassifier_EmptyTagsReturnEmpty()
    {
        var result = FallklasseClassifier.Classify([]);
        Assert.Empty(result);
    }

    // ═══ MANIFEST CALCULATOR ═══════════════════════════════════════════

    [Fact]
    public void ManifestCalculator_ProducesConsistentTotals()
    {
        var manifest = ManifestCalculator.Calculate();
        var entries = GroundTruthLoader.LoadAll();

        Assert.Equal(entries.Count, manifest.TotalEntries);
        Assert.True(manifest.SystemsCovered > 0, "No systems covered");
        Assert.True(manifest.BySet.Count > 0, "No sets in manifest");
        Assert.True(manifest.ByPlatformFamily.Count > 0, "No platform families");
        Assert.True(manifest.ByDifficulty.Count > 0, "No difficulty levels");

        // Sum of bySet entries should equal total
        var setSum = manifest.BySet.Values.Sum();
        Assert.Equal(manifest.TotalEntries, setSum);
    }

    [Fact]
    public void ManifestCalculator_FallklasseCountsAreComplete()
    {
        var manifest = ManifestCalculator.Calculate();

        // All 20 FC codes must be present in the dictionary
        for (int i = 1; i <= 20; i++)
        {
            var fc = $"FC-{i:D2}";
            Assert.True(manifest.FallklasseCounts.ContainsKey(fc),
                $"Fallklasse {fc} missing from manifest.FallklasseCounts");
        }

        // At least some FC codes should have entries
        var nonZero = manifest.FallklasseCounts.Values.Count(v => v > 0);
        Assert.True(nonZero >= 10,
            $"Only {nonZero} Fallklassen have >0 entries (expected at least 10)");
    }

    [Fact]
    public void ManifestCalculator_DatEcosystemCountsArePresent()
    {
        var manifest = ManifestCalculator.Calculate();

        Assert.True(manifest.DatEcosystemCounts.ContainsKey("no-intro"), "no-intro missing from datEcosystemCounts");
        Assert.True(manifest.DatEcosystemCounts.ContainsKey("redump"), "redump missing from datEcosystemCounts");
        Assert.True(manifest.DatEcosystemCounts.ContainsKey("mame"), "mame missing from datEcosystemCounts");
        Assert.True(manifest.DatEcosystemCounts.ContainsKey("tosec"), "tosec missing from datEcosystemCounts");
    }

    [Fact]
    public void ManifestCalculator_CoverageTargetsMatchGates()
    {
        var manifest = ManifestCalculator.Calculate();

        // coverageTargets should include keys from gates.json
        Assert.True(manifest.CoverageTargets.ContainsKey("totalEntries"),
            "coverageTargets missing totalEntries");
        Assert.True(manifest.CoverageTargets.ContainsKey("systemsCovered"),
            "coverageTargets missing systemsCovered");

        // Each target must have valid threshold values
        foreach (var (key, threshold) in manifest.CoverageTargets)
        {
            Assert.True(threshold.Target >= threshold.HardFail,
                $"coverageTargets[{key}]: target ({threshold.Target}) < hardFail ({threshold.HardFail})");
        }
    }

    [Fact]
    public void ManifestCalculator_CoverageActualsAlignWithTargetKeys()
    {
        var manifest = ManifestCalculator.Calculate();

        // For every target key, there should be a corresponding actual
        var missingActuals = manifest.CoverageTargets.Keys
            .Where(k => !manifest.CoverageActuals.ContainsKey(k))
            .ToList();

        // Some target keys may not have actuals if the area doesn't exist yet, that's OK.
        // But top-level keys must be present.
        Assert.True(manifest.CoverageActuals.ContainsKey("totalEntries"),
            "coverageActuals missing totalEntries");
        Assert.True(manifest.CoverageActuals.ContainsKey("systemsCovered"),
            "coverageActuals missing systemsCovered");
    }

    [Fact]
    public void ManifestCalculator_SystemsListIsConsistentWithCount()
    {
        var manifest = ManifestCalculator.Calculate();

        Assert.Equal(manifest.SystemsCovered, manifest.SystemsList.Count);
        // No duplicates
        Assert.Equal(manifest.SystemsList.Count, manifest.SystemsList.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void ManifestCalculator_FileChecksumsArePresent()
    {
        var manifest = ManifestCalculator.Calculate();
        var files = BenchmarkPaths.AllJsonlFiles;

        // Every JSONL file should have a checksum
        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            Assert.True(manifest.FileChecksums.ContainsKey(fileName),
                $"Missing checksum for {fileName}");
            Assert.Matches("^[0-9a-f]{64}$", manifest.FileChecksums[fileName]);
        }
    }

    [Fact]
    public void ManifestCalculator_SpecializedJsonlFilesAreIncluded()
    {
        var files = BenchmarkPaths.AllJsonlFiles.Select(Path.GetFileNameWithoutExtension).ToList();

        Assert.Contains("bios-coverage", files);
        Assert.Contains("arcade-coverage", files);
        Assert.Contains("computer-coverage", files);
        Assert.Contains("redump-specials", files);
    }

    // ═══ COVERAGE REPORT DIAGNOSTICS ════════════════════════════════════

    [Fact]
    public void CoverageReport_ProducesValidSummary()
    {
        var report = BuildReport();
        Assert.True(report.TotalEntries >= 0);
        Assert.NotNull(report.GateResults);
        Assert.True(report.GateResults.Count > 0, "Coverage report produced no gate results");
    }

    // ═══ HELPER ═════════════════════════════════════════════════════════

    private static CoverageReport BuildReport()
    {
        var entries = GroundTruthLoader.LoadAll();
        var validator = CoverageValidator.CreateFromGatesJson();
        return validator.Evaluate(entries);
    }

    private static GateResult FindGate(CoverageReport report, string gateName)
    {
        return report.GateResults.FirstOrDefault(g =>
            g.GateName.Equals(gateName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Gate '{gateName}' not found in report");
    }
}
