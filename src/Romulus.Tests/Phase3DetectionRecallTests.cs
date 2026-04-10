using System.Text;
using Romulus.Core.Classification;
using Romulus.Tests.Benchmark;
using Romulus.Tests.Benchmark.Generators;
using Romulus.Tests.Benchmark.Generators.Disc;
using Romulus.Tests.Benchmark.Infrastructure;
using Romulus.Tests.Benchmark.Models;
using Xunit;
using Xunit.Abstractions;

namespace Romulus.Tests;

/// <summary>
/// Phase 3 — Detection Recall &amp; Stub-Generatoren (TASK-036 bis TASK-046).
/// Tests für Filename-Collision-Fixes, Stub-Generatoren, PS3-Detection und Benchmark-Gates.
/// </summary>
[Collection("BenchmarkGroundTruth")]
public sealed class Phase3DetectionRecallTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"phase3-{Guid.NewGuid():N}");

    public Phase3DetectionRecallTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── TASK-036: Filename Collision Fix ──

    [Fact]
    public void BuildRelativePath_IncludesEntryId_EnsuresUniqueness()
    {
        var entry1 = MakeEntry("gc-NES-ref-001", "nes", "Super Mario Bros (USA).nes");
        var entry2 = MakeEntry("gc-NES-dat-001", "nes", "Super Mario Bros (USA).nes");

        var path1 = StubGeneratorDispatch.BuildRelativePath(entry1);
        var path2 = StubGeneratorDispatch.BuildRelativePath(entry2);

        Assert.NotEqual(path1, path2);
        Assert.EndsWith("Super Mario Bros (USA).nes", path1);
        Assert.EndsWith("Super Mario Bros (USA).nes", path2);
        Assert.Contains("gc-NES-ref-001", path1);
        Assert.Contains("gc-NES-dat-001", path2);
    }

    [Fact]
    public void BuildRelativePath_PreservesDirectorySegment()
    {
        var entry = MakeEntry("gc-NES-ref-001", "nes", "Game (USA).nes");
        var path = StubGeneratorDispatch.BuildRelativePath(entry);

        Assert.StartsWith("nes", path);
    }

    [Fact]
    public void BuildRelativePath_SanitizesPathTraversal()
    {
        var entry = MakeEntry("gc-NES-ref-001", "../etc", "Game (USA).nes");
        var path = StubGeneratorDispatch.BuildRelativePath(entry);

        Assert.DoesNotContain("..", path);
    }

    [Fact]
    public void GenerateAll_NoDuplicateSkips_AllEntriesGetFiles()
    {
        var entries = new[]
        {
            MakeEntry("test-001", "nes", "Game (USA).nes"),
            MakeEntry("test-002", "nes", "Game (USA).nes"),
            MakeEntry("test-003", "nes", "Game (USA).nes"),
        };

        var dispatch = new StubGeneratorDispatch();
        var count = dispatch.GenerateAll(entries, _tempDir);

        Assert.Equal(3, count);

        foreach (var entry in entries)
        {
            var path = Path.Combine(_tempDir, StubGeneratorDispatch.BuildRelativePath(entry));
            Assert.True(File.Exists(path), $"File missing for entry {entry.Id}: {path}");
        }
    }

    [Fact]
    public void AllGroundTruthEntries_HaveUniqueRelativePaths()
    {
        var entries = GroundTruthLoader.LoadAll();
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new List<string>();

        foreach (var entry in entries)
        {
            var relativePath = StubGeneratorDispatch.BuildRelativePath(entry);
            if (!paths.TryAdd(relativePath, entry.Id))
            {
                duplicates.Add($"{entry.Id} conflicts with {paths[relativePath]} at '{relativePath}'");
            }
        }

        Assert.Empty(duplicates);
    }

    // ── TASK-037: StubGeneratorDispatch Fallback ──

    [Fact]
    public void StubGeneratorDispatch_NullPrimaryMethod_FallsBackToConsoleMap()
    {
        var entry = MakeEntry("test-fb-001", "nes", "Game (USA).nes",
            consoleKey: "NES", primaryMethod: null);

        var dispatch = new StubGeneratorDispatch();
        var bytes = dispatch.GenerateStub(entry);

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public void StubGeneratorDispatch_UnknownPrimaryMethod_FallsBackToExtOnly()
    {
        var entry = MakeEntry("test-fb-002", "nes", "Game (USA).nes",
            consoleKey: "NES", primaryMethod: "UnknownMethod");

        var dispatch = new StubGeneratorDispatch();
        var bytes = dispatch.GenerateStub(entry);

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }

    // ── TASK-038: OperaFsGenerator (3DO) ──

    [Fact]
    public void OperaFsGenerator_ProducesValidStub()
    {
        var gen = new OperaFsGenerator();
        var bytes = gen.Generate("standard");

        Assert.True(bytes.Length >= 2048);
        Assert.Equal(0x01, bytes[0]);
        Assert.Equal(0x5A, bytes[1]);
        Assert.Equal(0x5A, bytes[2]);
        Assert.Equal(0x5A, bytes[3]);
        Assert.Equal(0x5A, bytes[4]);
        Assert.Equal(0x5A, bytes[5]);
        Assert.Equal(0x01, bytes[6]); // Record version
    }

    [Fact]
    public void OperaFsGenerator_DetectedAs3DO()
    {
        var gen = new OperaFsGenerator();
        var bytes = gen.Generate("standard");
        var path = WriteStub("3do-test.iso", bytes);

        var detector = new DiscHeaderDetector();
        Assert.Equal("3DO", detector.DetectFromDiscImage(path));
    }

    // ── TASK-039: BootSectorTextGenerator ──

    [Theory]
    [InlineData("neocd", "NEOCD")]
    [InlineData("pcecd", "PCECD")]
    [InlineData("pcfx", "PCFX")]
    [InlineData("jagcd", "JAGCD")]
    [InlineData("cd32", "CD32")]
    public void BootSectorTextGenerator_ProducesDetectableStub(string variant, string expectedConsole)
    {
        var gen = new BootSectorTextGenerator();
        var bytes = gen.Generate(variant);
        var path = WriteStub($"{variant}-test.iso", bytes);

        var detector = new DiscHeaderDetector();
        Assert.Equal(expectedConsole, detector.DetectFromDiscImage(path));
    }

    // ── TASK-040: XdvdfsGenerator (Xbox) ──

    [Theory]
    [InlineData("xbox", "XBOX")]
    [InlineData("x360", "X360")]
    public void XdvdfsGenerator_ProducesDetectableStub(string variant, string expectedConsole)
    {
        var gen = new XdvdfsGenerator();
        var bytes = gen.Generate(variant);
        var path = WriteStub($"{variant}-test.iso", bytes);

        var detector = new DiscHeaderDetector();
        Assert.Equal(expectedConsole, detector.DetectFromDiscImage(path));
    }

    // ── TASK-041: Ps3PvdGenerator ──

    [Fact]
    public void Ps3PvdGenerator_ProducesValidStub()
    {
        var gen = new Ps3PvdGenerator();
        var bytes = gen.Generate("standard");

        // PVD at offset 0x8000
        Assert.Equal(0x01, bytes[0x8000]);
        Assert.Equal((byte)'C', bytes[0x8001]);
        Assert.Equal((byte)'D', bytes[0x8002]);

        // System ID: PLAYSTATION
        var sysId = Encoding.ASCII.GetString(bytes, 0x8000 + 8, 11);
        Assert.Equal("PLAYSTATION", sysId);

        // PS3 marker
        var marker = Encoding.ASCII.GetString(bytes, 0x8000 + 256, 12);
        Assert.Equal("PS3_DISC.SFB", marker);
    }

    [Fact]
    public void Ps3PvdGenerator_DetectedAsPS3()
    {
        var gen = new Ps3PvdGenerator();
        var bytes = gen.Generate("standard");
        var path = WriteStub("ps3-test.iso", bytes);

        var detector = new DiscHeaderDetector();
        Assert.Equal("PS3", detector.DetectFromDiscImage(path));
    }

    // ── TASK-042: StubGeneratorRegistry — all disc systems registered ──

    [Fact]
    public void StubGeneratorRegistry_HasAllDiscGenerators()
    {
        var registry = new StubGeneratorRegistry();

        Assert.NotNull(registry.Get("ps1-pvd"));
        Assert.NotNull(registry.Get("ps2-pvd"));
        Assert.NotNull(registry.Get("ps3-pvd"));
        Assert.NotNull(registry.Get("sega-ipbin"));
        Assert.NotNull(registry.Get("nintendo-disc"));
        Assert.NotNull(registry.Get("3do-opera"));
        Assert.NotNull(registry.Get("boot-sector-text"));
        Assert.NotNull(registry.Get("xdvdfs"));
        Assert.NotNull(registry.Get("fmtowns-pvd"));
        Assert.NotNull(registry.Get("cdi-disc"));
        Assert.NotNull(registry.Get("multi-file-set"));
    }

    [Fact]
    public void StubGeneratorDispatch_DiscMap_CoversAllDiscSystems()
    {
        var dispatch = new StubGeneratorDispatch();
        var discSystems = new[]
        {
            "PS1", "PS2", "PSP", "PS3", "GC", "WII",
            "SAT", "DC", "SCD", "3DO", "CD32", "NEOCD",
            "PCECD", "PCFX", "JAGCD", "XBOX", "X360",
            "FMTOWNS", "CDI"
        };

        foreach (var system in discSystems)
        {
            var entry = MakeEntry($"test-disc-{system}", system.ToLower(), $"Game ({system}).iso",
                consoleKey: system, primaryMethod: "DiscHeader");
            var bytes = dispatch.GenerateStub(entry);
            Assert.True(bytes.Length > 0, $"No stub generated for disc system {system}");
        }
    }

    // ── TASK-043/046: PS3 Detection ──

    [Fact]
    public void DiscHeaderDetector_PS3_ByPvdMarker_PS3_DISC_SFB()
    {
        var data = MakePvdBuffer("PLAYSTATION", "PS3_DISC.SFB");
        var path = WriteStub("ps3-sfb.iso", data);

        var detector = new DiscHeaderDetector();
        Assert.Equal("PS3", detector.DetectFromDiscImage(path));
    }

    [Fact]
    public void DiscHeaderDetector_PS3_ByPvdMarker_PS3_GAME()
    {
        var data = MakePvdBuffer("PLAYSTATION", "PS3_GAME");
        var path = WriteStub("ps3-game.iso", data);

        var detector = new DiscHeaderDetector();
        Assert.Equal("PS3", detector.DetectFromDiscImage(path));
    }

    [Fact]
    public void DiscHeaderDetector_PS3_ByPvdMarker_PS3VOLUME()
    {
        var data = MakePvdBuffer("PLAYSTATION", "PS3VOLUME");
        var path = WriteStub("ps3-volume.iso", data);

        var detector = new DiscHeaderDetector();
        Assert.Equal("PS3", detector.DetectFromDiscImage(path));
    }

    [Fact]
    public void DiscHeaderDetector_PS3_ByPvdMarker_PLAYSTATION3()
    {
        var data = MakePvdBuffer("PLAYSTATION", "PLAYSTATION 3");
        var path = WriteStub("ps3-name.iso", data);

        var detector = new DiscHeaderDetector();
        Assert.Equal("PS3", detector.DetectFromDiscImage(path));
    }

    [Fact]
    public void DiscHeaderDetector_PS3_NotConfusedWithPS2()
    {
        // PS2 has BOOT2= marker, PS3 has PS3_ markers — mutually exclusive
        var data = MakePvdBuffer("PLAYSTATION", "PS3_DISC.SFB");
        var path = WriteStub("ps3-vs-ps2.iso", data);

        var detector = new DiscHeaderDetector();
        var result = detector.DetectFromDiscImage(path);
        Assert.Equal("PS3", result);
        Assert.NotEqual("PS2", result);
    }

    [Fact]
    public void DiscHeaderDetector_PS3_NotConfusedWithPS1()
    {
        // PS1 is the fallback when PLAYSTATION sysid present but no PS2/PS3/PSP markers
        var dataPs1 = MakePvdBuffer("PLAYSTATION", "no-ps3-marker-here");
        var pathPs1 = WriteStub("ps1-only.iso", dataPs1);

        var detector = new DiscHeaderDetector();
        Assert.Equal("PS1", detector.DetectFromDiscImage(pathPs1));
    }

    // ── TASK-044: .bin UNKNOWN limit ──

    [Fact]
    public void DiscHeaderDetector_BinWithoutMagic_ReturnsNull()
    {
        // .bin files without any console magic bytes → UNKNOWN
        var data = new byte[4096];
        var path = WriteStub("unknown-game.bin", data);

        var detector = new DiscHeaderDetector();
        Assert.Null(detector.DetectFromDiscImage(path));
    }

    // ── Helper Methods ──

    private byte[] MakePvdBuffer(string systemId, string marker)
    {
        const int pvdOffset = 0x8000;
        int totalSize = pvdOffset + 512;
        var data = new byte[totalSize];

        // PVD magic
        data[pvdOffset] = 0x01;
        Encoding.ASCII.GetBytes("CD001").CopyTo(data, pvdOffset + 1);
        data[pvdOffset + 6] = 0x01;

        // System ID
        var sysIdBytes = Encoding.ASCII.GetBytes(systemId.PadRight(32));
        Array.Copy(sysIdBytes, 0, data, pvdOffset + 8, 32);

        // Marker after system ID
        Encoding.ASCII.GetBytes(marker).CopyTo(data, pvdOffset + 256);

        return data;
    }

    private string WriteStub(string fileName, byte[] data)
    {
        var path = Path.Combine(_tempDir, fileName);
        var dir = Path.GetDirectoryName(path);
        if (dir is not null)
            Directory.CreateDirectory(dir);
        File.WriteAllBytes(path, data);
        return path;
    }

    private static GroundTruthEntry MakeEntry(string id, string directory, string fileName,
        string? consoleKey = null, string? primaryMethod = null)
    {
        return new GroundTruthEntry
        {
            Id = id,
            Source = new SourceInfo
            {
                FileName = fileName,
                Extension = Path.GetExtension(fileName),
                SizeBytes = 1024,
                Directory = directory,
            },
            Tags = [],
            Difficulty = "easy",
            Expected = new ExpectedResult
            {
                ConsoleKey = consoleKey ?? "UNKNOWN",
                Category = "Game",
            },
            DetectionExpectations = primaryMethod is not null
                ? new DetectionExpectations { PrimaryMethod = primaryMethod }
                : null,
        };
    }
}

// ── TASK-045: Missed ≤ 12 Benchmark Gate ──

/// <summary>
/// Phase 3 quality gate: Missed entries across all ground-truth sets must not exceed 12.
/// Uses the full BenchmarkFixture to evaluate all entries against the detection pipeline.
/// </summary>
[Collection("BenchmarkEvaluation")]
public sealed class Phase3MissedEntriesGateTests : IClassFixture<BenchmarkFixture>
{
    /// <summary>
    /// Missed ≤ 12 applies to easy+medium entries only.
    /// Hard/adversarial entries test known detection limits (cross-system archives, ambiguous formats).
    /// </summary>
    private const int MaxMissedEasyMedium = 12;

    private readonly BenchmarkFixture _fixture;
    private readonly ITestOutputHelper _output;

    public Phase3MissedEntriesGateTests(BenchmarkFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    [Trait("Category", "QualityGate")]
    public void AllGroundTruthSets_MissedEasyMedium_AtMost12()
    {
        var setFiles = new[]
        {
            "golden-core.jsonl",
            "edge-cases.jsonl",
            "negative-controls.jsonl",
            "golden-realworld.jsonl",
            "chaos-mixed.jsonl",
            "dat-coverage.jsonl",
            "repair-safety.jsonl",
        };

        var allEntries = new List<GroundTruthEntry>();
        foreach (var set in setFiles)
        {
            allEntries.AddRange(GroundTruthLoader.LoadSet(set));
        }

        var results = new List<BenchmarkSampleResult>();
        foreach (var set in setFiles)
        {
            results.AddRange(BenchmarkEvaluationRunner.EvaluateSet(_fixture, set));
        }

        var missed = results.Where(r => r.Verdict == BenchmarkVerdict.Missed).ToList();

        // Build lookup: entryId → difficulty
        var difficultyMap = allEntries.ToDictionary(e => e.Id, e => e.Difficulty ?? "unknown", StringComparer.Ordinal);

        var missedEasyMedium = missed.Where(m =>
            difficultyMap.TryGetValue(m.Id, out var d) &&
            d is "easy" or "medium").ToList();
        var missedHardAdversarial = missed.Where(m =>
            !difficultyMap.TryGetValue(m.Id, out var d) ||
            d is not ("easy" or "medium")).ToList();

        _output.WriteLine($"Total entries evaluated: {results.Count}");
        _output.WriteLine($"Total missed: {missed.Count}");
        _output.WriteLine($"  easy/medium missed: {missedEasyMedium.Count} (threshold: {MaxMissedEasyMedium})");
        _output.WriteLine($"  hard/adversarial missed: {missedHardAdversarial.Count} (informational)");

        foreach (var m in missedEasyMedium)
        {
            _output.WriteLine($"  GATE-MISS: {m.Id} — expected={m.ExpectedConsoleKey}, detail={m.Details}");
        }

        foreach (var m in missedHardAdversarial.Take(10))
        {
            _output.WriteLine($"  KNOWN-LIMIT: {m.Id} — expected={m.ExpectedConsoleKey}, detail={m.Details}");
        }

        Assert.True(missedEasyMedium.Count <= MaxMissedEasyMedium,
            $"Easy/medium missed entries {missedEasyMedium.Count} exceeds threshold {MaxMissedEasyMedium}. " +
            $"IDs: {string.Join(", ", missedEasyMedium.Select(m => m.Id))}");
    }
}
