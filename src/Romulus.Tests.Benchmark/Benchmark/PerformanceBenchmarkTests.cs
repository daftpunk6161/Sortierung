using System.Diagnostics;
using Romulus.Core.Classification;
using Romulus.Tests.Benchmark.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace Romulus.Tests.Benchmark;

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
        var tempRoot = Path.Combine(Path.GetTempPath(), $"romulus-scale-{Guid.NewGuid():N}");

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

            // Threshold: isolated runs achieve ~2500/s.
            // 100/s still catches genuine regressions while tolerating parallel I/O pressure.
            Assert.True(rate > 100, $"Throughput too low: {rate:F1}/s (expected > 100/s)");
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, recursive: true);
            }
            catch (IOException)
            {
                // Temp cleanup may fail under parallel execution (AV, indexer, or other tests
                // holding handles). Swallow — OS will clean temp on next boot.
            }
        }
    }
}
