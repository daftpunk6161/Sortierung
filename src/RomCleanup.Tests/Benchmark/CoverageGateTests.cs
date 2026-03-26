using RomCleanup.Tests.Benchmark.Generators;
using RomCleanup.Tests.Benchmark.Generators.Cartridge;
using RomCleanup.Tests.Benchmark.Generators.Disc;
using RomCleanup.Tests.Benchmark.Infrastructure;
using RomCleanup.Tests.Benchmark.Models;
using Xunit;

namespace RomCleanup.Tests.Benchmark;

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

    // ═══ STUB GENERATOR ═════════════════════════════════════════════════

    [Fact]
    public void StubGeneratorRegistry_ContainsExpectedGenerators()
    {
        var registry = new StubGeneratorRegistry();
        Assert.Contains("nes-ines", registry.RegisteredIds);
        Assert.Contains("ps1-pvd", registry.RegisteredIds);
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
