using Romulus.Core.Classification;
using Romulus.Tests.Benchmark.Generators;
using Romulus.Tests.Benchmark.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace Romulus.Tests.Benchmark;

/// <summary>
/// Exercises the ScaleDatasetGenerator to verify throughput and detection consistency
/// over a large synthetic dataset (5000+ files).
/// </summary>
[Collection("BenchmarkEvaluation")]
public sealed class PerformanceScaleTests : IClassFixture<BenchmarkFixture>
{
    private readonly BenchmarkFixture _fixture;
    private readonly ITestOutputHelper _output;

    public PerformanceScaleTests(BenchmarkFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    [Trait("Category", "Benchmark")]
    [Trait("Category", "PerformanceScale")]
    public void ScaleDataset_GeneratesExpectedCount()
    {
        var generator = new Generators.ScaleDatasetGenerator();
        var entries = generator.Generate(count: 500); // Reduced for CI speed

        Assert.Equal(500, entries.Count);

        // Verify determinism: IDs are unique
        var ids = entries.Select(e => e.Id).ToHashSet();
        Assert.Equal(entries.Count, ids.Count);

        _output.WriteLine($"Generated {entries.Count} scale entries with {ids.Count} unique IDs");
    }

    [Fact]
    [Trait("Category", "Benchmark")]
    [Trait("Category", "PerformanceScale")]
    public void ScaleDataset_DetectionRate_AboveBaseline()
    {
        var generator = new Generators.ScaleDatasetGenerator();
        var entries = generator.Generate(count: 200); // Smaller set for evaluation

        // Generate stubs into a temporary directory
        var tempDir = Path.Combine(Path.GetTempPath(), $"romulus-scale-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var dispatch = new StubGeneratorDispatch();
            dispatch.GenerateAll(entries, tempDir);

            int detected = 0;
            int total = 0;

            foreach (var entry in entries)
            {
                var relativePath = StubGeneratorDispatch.BuildRelativePath(entry);
                var samplePath = Path.Combine(tempDir, relativePath);

                if (!File.Exists(samplePath))
                    continue;

                total++;
                var detection = _fixture.Detector.DetectWithConfidence(samplePath, tempDir);
                if (detection.ConsoleKey is not null
                    && !string.Equals(detection.ConsoleKey, "UNKNOWN", StringComparison.OrdinalIgnoreCase))
                {
                    detected++;
                }
            }

            double detectionRate = total > 0 ? 100.0 * detected / total : 0;
            _output.WriteLine($"Scale detection: {detected}/{total} = {detectionRate:F1}%");

            // Baseline: at least 30% should be detected (many are folder/extension based)
            Assert.True(detectionRate >= 30.0,
                $"Scale detection rate {detectionRate:F1}% below 30% baseline");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
