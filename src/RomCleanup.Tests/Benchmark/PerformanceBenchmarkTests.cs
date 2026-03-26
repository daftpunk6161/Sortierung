using System.Diagnostics;
using RomCleanup.Core.Classification;
using RomCleanup.Tests.Benchmark.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace RomCleanup.Tests.Benchmark;

public sealed class PerformanceBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public PerformanceBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "BenchmarkPerformance")]
    public void FullPipeline_5000Files_CompletesWithinBudget()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"romcleanup-scale-{Guid.NewGuid():N}");

        try
        {
            var generator = new ScaleDatasetGenerator();
            var files = generator.Generate(tempRoot, 5000);

            var consolesJson = File.ReadAllText(BenchmarkPaths.ConsolesJsonPath);
            var detector = ConsoleDetector.LoadFromJson(
                consolesJson,
                discHeaderDetector: new DiscHeaderDetector(),
                cartridgeHeaderDetector: new CartridgeHeaderDetector());

            var sw = Stopwatch.StartNew();
            foreach (var file in files)
            {
                detector.DetectWithConfidence(file, tempRoot);
            }

            sw.Stop();
            var rate = files.Count / sw.Elapsed.TotalSeconds;
            _output.WriteLine($"Processed {files.Count} files in {sw.Elapsed.TotalSeconds:F2}s ({rate:F1}/s)");

            Assert.True(rate > 100, $"Throughput too low: {rate:F1}/s (expected > 100/s)");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
