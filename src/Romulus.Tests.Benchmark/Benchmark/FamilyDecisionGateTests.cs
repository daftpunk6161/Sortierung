using System.Text.Json;
using Romulus.Contracts.Models;
using Romulus.Tests.Benchmark.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace Romulus.Tests.Benchmark;

[Collection("BenchmarkEvaluation")]
[Trait("Category", "QualityGate")]
public sealed class FamilyDecisionGateTests : IClassFixture<BenchmarkFixture>
{
    private readonly BenchmarkFixture _fixture;
    private readonly ITestOutputHelper _output;

    public FamilyDecisionGateTests(BenchmarkFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public void FamilyDecisionGates_MeetThresholdsFromGatesJson()
    {
        var thresholds = LoadThresholds();
        var results = EvaluateAllSets();

        foreach (var (family, threshold) in thresholds.OrderBy(static kv => kv.Key.ToString()))
        {
            var familyResults = results
                .Where(r => !string.IsNullOrWhiteSpace(r.ExpectedConsoleKey))
                .Where(r => _fixture.Detector.GetPlatformFamily(r.ExpectedConsoleKey!) == family)
                .ToArray();

            // If no benchmark entries exist for a family yet, skip gate enforcement for now.
            if (familyResults.Length == 0)
            {
                _output.WriteLine($"Family {family}: no benchmark entries, gate skipped.");
                continue;
            }

            var total = familyResults.Length;
            var datVerified = familyResults.Count(r => r.ActualSortDecision == SortDecision.DatVerified);
            var falsePositives = familyResults.Count(r =>
                r.Verdict is BenchmarkVerdict.Wrong or BenchmarkVerdict.FalsePositive);
            var unknown = familyResults.Count(r =>
                string.IsNullOrWhiteSpace(r.ActualConsoleKey)
                || string.Equals(r.ActualConsoleKey, "UNKNOWN", StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.ActualConsoleKey, "AMBIGUOUS", StringComparison.OrdinalIgnoreCase));

            var datVerifiedRate = (double)datVerified / total;
            var falsePositiveRate = (double)falsePositives / total;
            var unknownRate = (double)unknown / total;

            _output.WriteLine(
                $"Family={family} total={total} datVerified={datVerifiedRate:P2} falsePositive={falsePositiveRate:P2} unknown={unknownRate:P2}");

            Assert.True(datVerifiedRate >= threshold.MinDatVerifiedRate,
                $"Family {family}: DatVerifiedRate {datVerifiedRate:P2} below min {threshold.MinDatVerifiedRate:P2}");
            Assert.True(falsePositiveRate <= threshold.MaxFalsePositiveRate,
                $"Family {family}: FalsePositiveRate {falsePositiveRate:P2} above max {threshold.MaxFalsePositiveRate:P2}");
            Assert.True(unknownRate <= threshold.MaxUnknownRate,
                $"Family {family}: UnknownRate {unknownRate:P2} above max {threshold.MaxUnknownRate:P2}");
        }
    }

    private static Dictionary<PlatformFamily, FamilyDecisionThreshold> LoadThresholds()
    {
        var json = File.ReadAllText(BenchmarkPaths.GatesJsonPath);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("s1", out var s1)
            || !s1.TryGetProperty("familyDecisionGates", out var gatesElement))
        {
            throw new InvalidOperationException("gates.json is missing s1.familyDecisionGates");
        }

        var result = new Dictionary<PlatformFamily, FamilyDecisionThreshold>();
        foreach (var property in gatesElement.EnumerateObject())
        {
            if (!Enum.TryParse<PlatformFamily>(property.Name, ignoreCase: true, out var family))
                continue;

            var minDatVerifiedRate = property.Value.GetProperty("minDatVerifiedRate").GetDouble();
            var maxFalsePositiveRate = property.Value.GetProperty("maxFalsePositiveRate").GetDouble();
            var maxUnknownRate = property.Value.GetProperty("maxUnknownRate").GetDouble();

            result[family] = new FamilyDecisionThreshold(minDatVerifiedRate, maxFalsePositiveRate, maxUnknownRate);
        }

        return result;
    }

    private List<BenchmarkSampleResult> EvaluateAllSets()
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

        var results = new List<BenchmarkSampleResult>();
        foreach (var set in setFiles)
            results.AddRange(BenchmarkEvaluationRunner.EvaluateSet(_fixture, set));

        return results;
    }

    private readonly record struct FamilyDecisionThreshold(
        double MinDatVerifiedRate,
        double MaxFalsePositiveRate,
        double MaxUnknownRate);
}
