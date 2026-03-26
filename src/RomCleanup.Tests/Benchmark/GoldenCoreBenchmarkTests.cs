using RomCleanup.Tests.Benchmark.Generators;
using RomCleanup.Tests.Benchmark.Infrastructure;
using RomCleanup.Tests.Benchmark.Models;
using Xunit;
using Xunit.Abstractions;

namespace RomCleanup.Tests.Benchmark;

/// <summary>
/// Golden-core benchmark: evaluates the production detection pipeline against
/// all ground-truth entries. Each entry is tested individually via Theory.
/// </summary>
[Collection("BenchmarkEvaluation")]
[Trait("Category", "Benchmark")]
public sealed class GoldenCoreBenchmarkTests : IClassFixture<BenchmarkFixture>
{
    private readonly BenchmarkFixture _fixture;
    private readonly ITestOutputHelper _output;

    public GoldenCoreBenchmarkTests(BenchmarkFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    [Trait("Category", "Benchmark")]
    public void AllSamples_ExistOnDisk()
    {
        var entries = _fixture.AllEntries;
        Assert.True(entries.Count > 0, "No ground-truth entries loaded");

        int missing = 0;
        foreach (var entry in entries)
        {
            var path = _fixture.GetSamplePath(entry);
            if (!File.Exists(path))
            {
                _output.WriteLine($"MISSING: {entry.Id} → {path}");
                missing++;
            }
        }

        Assert.Equal(0, missing);
        _output.WriteLine($"All {entries.Count} samples exist on disk");
    }

    [Theory]
    [BenchmarkData("golden-core.jsonl")]
    [Trait("Category", "Benchmark")]
    public void ConsoleDetection_MatchesGroundTruth(GroundTruthEntry entry)
    {
        var samplePath = _fixture.GetSamplePath(entry);
        Assert.True(File.Exists(samplePath), $"Sample file missing: {samplePath}");

        var result = _fixture.Detector.DetectWithConfidence(samplePath, _fixture.SamplesRoot);
        var verdict = GroundTruthComparator.Compare(entry, result);

        _output.WriteLine($"[{entry.Id}] {verdict.Verdict}: expected={entry.Expected.ConsoleKey ?? "null"}, actual={result.ConsoleKey}, confidence={result.Confidence}");

        // Correct, Acceptable, TrueNegative, and JunkClassified are all passing verdicts
        Assert.True(
            verdict.Verdict is BenchmarkVerdict.Correct or BenchmarkVerdict.Acceptable or BenchmarkVerdict.TrueNegative or BenchmarkVerdict.JunkClassified,
            $"[{entry.Id}] {verdict.Verdict}: {verdict.Details}");
    }

    [Theory]
    [BenchmarkData("negative-controls.jsonl")]
    [Trait("Category", "Benchmark")]
    public void NegativeControls_MustNotMatchConsole(GroundTruthEntry entry)
    {
        // Skip if no negative controls exist yet
        var samplePath = _fixture.GetSamplePath(entry);
        if (!File.Exists(samplePath))
        {
            _output.WriteLine($"SKIP: {entry.Id} (sample not found)");
            return;
        }

        // "expunk" entries have ROM extensions with invalid content — ConsoleDetector will
        // correctly detect by extension. These test enrichment/content validation, not detection.
        if (entry.Id.Contains("-expunk-", StringComparison.OrdinalIgnoreCase))
        {
            _output.WriteLine($"SKIP: {entry.Id} (extension-punking tests enrichment, not detection)");
            return;
        }

        // Homebrew/PD entries with a valid consoleKey and sortDecision="sort" are correctly
        // detected by the console detector. They test junk classification, not detection.
        if (entry.Id.Contains("-homebrew-", StringComparison.OrdinalIgnoreCase)
            && entry.Expected.ConsoleKey is not null
            && string.Equals(entry.Expected.SortDecision, "sort", StringComparison.OrdinalIgnoreCase))
        {
            _output.WriteLine($"SKIP: {entry.Id} (homebrew with valid consoleKey tests classification, not detection)");
            return;
        }

        var result = _fixture.Detector.DetectWithConfidence(samplePath, _fixture.SamplesRoot);
        var verdict = GroundTruthComparator.Compare(entry, result);

        _output.WriteLine($"[{entry.Id}] {verdict.Verdict}: actual={result.ConsoleKey}, confidence={result.Confidence}");

        Assert.True(
            verdict.Verdict is BenchmarkVerdict.TrueNegative,
            $"[{entry.Id}] False positive: detected as '{result.ConsoleKey}' with confidence {result.Confidence}");
    }

    [Fact]
    [Trait("Category", "Benchmark")]
    public void StubGeneration_IsDeterministic()
    {
        // Verify that generating stubs a second time produces identical bytes
        var dispatch = new StubGeneratorDispatch();
        var entries = _fixture.AllEntries.Take(20).ToList(); // Spot-check 20 entries
        var tempDir = Path.Combine(Path.GetTempPath(), $"benchmark-determinism-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempDir);
            dispatch.GenerateAll(entries, tempDir);

            foreach (var entry in entries)
            {
                var originalPath = _fixture.GetSamplePath(entry);
                var tempPath = Path.Combine(tempDir, StubGeneratorDispatch.BuildRelativePath(entry));

                if (!File.Exists(originalPath) || !File.Exists(tempPath))
                    continue;

                var originalBytes = File.ReadAllBytes(originalPath);
                var tempBytes = File.ReadAllBytes(tempPath);

                Assert.Equal(originalBytes.Length, tempBytes.Length);
                Assert.True(originalBytes.AsSpan().SequenceEqual(tempBytes),
                    $"Determinism violation for {entry.Id}: bytes differ on re-generation");
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "Benchmark")]
    public void BenchmarkSummary_PrintsOverallStats()
    {
        var entries = _fixture.AllEntries;
        var results = new List<BenchmarkSampleResult>();

        foreach (var entry in entries)
        {
            var samplePath = _fixture.GetSamplePath(entry);
            if (!File.Exists(samplePath))
                continue;

            var detection = _fixture.Detector.DetectWithConfidence(samplePath, _fixture.SamplesRoot);
            results.Add(GroundTruthComparator.Compare(entry, detection));
        }

        var grouped = results.GroupBy(r => r.Verdict).OrderBy(g => g.Key);
        _output.WriteLine($"=== Benchmark Summary ({results.Count} samples) ===");
        foreach (var g in grouped)
        {
            _output.WriteLine($"  {g.Key}: {g.Count()}");
        }

        int correct = results.Count(r => r.Verdict is BenchmarkVerdict.Correct or BenchmarkVerdict.Acceptable or BenchmarkVerdict.TrueNegative);
        double accuracy = results.Count > 0 ? (double)correct / results.Count * 100 : 0;
        _output.WriteLine($"  Accuracy: {accuracy:F1}%");

        // Log the wrong/missed ones for debugging
        foreach (var r in results.Where(r => r.Verdict is BenchmarkVerdict.Wrong or BenchmarkVerdict.Missed or BenchmarkVerdict.FalsePositive))
        {
            _output.WriteLine($"  FAIL: [{r.Id}] {r.Verdict} — {r.Details}");
        }

        // F-P3-06: Formal false-positive rate gate (must stay below 5%)
        int falsePositives = results.Count(r => r.Verdict == BenchmarkVerdict.FalsePositive);
        double fpRate = results.Count > 0 ? (double)falsePositives / results.Count * 100 : 0;
        _output.WriteLine($"  False-Positive Rate: {fpRate:F2}%");
        Assert.True(fpRate < 5.0,
            $"False-positive rate {fpRate:F2}% exceeds 5% threshold ({falsePositives}/{results.Count})");
    }
}
