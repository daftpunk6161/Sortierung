using Romulus.Contracts.Models;
using Romulus.Tests.Benchmark.Models;
using Romulus.Tests.Benchmark.Infrastructure;
using Romulus.Core.Classification;

namespace Romulus.Tests.Benchmark;

internal static class BenchmarkEvaluationRunner
{
    public static BenchmarkSampleResult Evaluate(BenchmarkFixture fixture, GroundTruthEntry entry)
    {
        var samplePath = fixture.GetSamplePath(entry);
        if (!File.Exists(samplePath))
        {
            return new BenchmarkSampleResult(
                entry.Id,
                BenchmarkVerdict.Missed,
                entry.Expected.ConsoleKey,
                "UNKNOWN",
                0,
                false,
                $"Sample file missing: {samplePath}",
                ExpectedCategory: entry.Expected.Category,
                ActualCategory: "Unknown",
                ActualSortDecision: SortDecision.Blocked);
        }

        var detection = fixture.Detector.DetectWithConfidence(samplePath, fixture.SamplesRoot);
        var fileName = Path.GetFileNameWithoutExtension(samplePath);
        var extension = Path.GetExtension(samplePath);
        var sizeBytes = new FileInfo(samplePath).Length;
        var classification = FileClassifier.Analyze(fileName, extension, sizeBytes);

        return GroundTruthComparator.Compare(entry, detection, classification.Category.ToString());
    }

    public static List<BenchmarkSampleResult> EvaluateSet(BenchmarkFixture fixture, string setFileName)
    {
        var entries = GroundTruthLoader.LoadSet(setFileName);
        return entries
            .OrderBy(entry => entry.Id, StringComparer.Ordinal)
            .Select(entry => Evaluate(fixture, entry))
            .ToList();
    }
}
