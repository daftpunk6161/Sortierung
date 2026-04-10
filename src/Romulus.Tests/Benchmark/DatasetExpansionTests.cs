using Romulus.Tests.Benchmark.Infrastructure;
using Romulus.Tests.Benchmark.Models;
using Xunit;

namespace Romulus.Tests.Benchmark;

/// <summary>
/// One-shot test to run the DatasetExpander and write expanded JSONL files.
/// Tagged DatasetGeneration — NOT part of regular CI, run manually when expanding ground-truth.
/// </summary>
[Collection("BenchmarkGroundTruth")]
public sealed class DatasetExpansionTests
{
    [Fact]
    [Trait("Category", "DatasetGeneration")]
    public void RunExpansion_GeneratesAndWritesEntries()
    {
        // Generate from scratch — always regenerate all entries
        var expander = new DatasetExpander([]);
        var generated = expander.GenerateExpansion();
        var allGenerated = generated.Values.SelectMany(v => v).ToList();

        // Validate generated entries BEFORE writing
        var validator = SchemaValidator.CreateFromConsolesJson();
        var allErrors = validator.ValidateAll(allGenerated);
        if (allErrors.Count > 0)
        {
            var errorSummary = string.Join("\n",
                allErrors.Take(20).Select(kv => $"  {kv.Key}: {string.Join("; ", kv.Value)}"));
            Assert.Fail($"Validation errors in {allErrors.Count} generated entries:\n{errorSummary}");
        }

        // Check all 65 systems covered
        var coveredSystems = allGenerated
            .Where(e => !string.IsNullOrEmpty(e.Expected.ConsoleKey))
            .Select(e => e.Expected.ConsoleKey!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(k => k)
            .ToList();
        Assert.True(coveredSystems.Count >= 78,
            $"Expected >= 78 systems, got {coveredSystems.Count}: {string.Join(", ", coveredSystems)}");

        // Check all 20 Fallklassen covered
        var coveredFc = allGenerated
            .SelectMany(e => FallklasseClassifier.Classify(e.Tags))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(fc => fc)
            .ToList();
        Assert.True(coveredFc.Count >= 20,
            $"Expected >= 20 FC, got {coveredFc.Count}: {string.Join(", ", coveredFc)}");

        // Write result (overwrite with generated-only)
        DatasetExpander.WriteToFiles(new Dictionary<string, List<GroundTruthEntry>>(), generated);

        // Verify: reload everything
        var reloaded = GroundTruthLoader.LoadAll();

        // Unique ID check
        var ids = reloaded.Select(e => e.Id).ToList();
        var uniqueIds = ids.Distinct(StringComparer.Ordinal).ToList();
        Assert.Equal(ids.Count, uniqueIds.Count);

        // Validate ALL (existing + generated)
        var allReloadErrors = validator.ValidateAll(reloaded);
        if (allReloadErrors.Count > 0)
        {
            var errorSummary = string.Join("\n",
                allReloadErrors.Take(20).Select(kv => $"  {kv.Key}: {string.Join("; ", kv.Value)}"));
            Assert.Fail($"Validation errors after merge in {allReloadErrors.Count} entries:\n{errorSummary}");
        }

        // Total count check
        Assert.True(reloaded.Count >= 2000,
            $"Expected >= 2000 total entries, got {reloaded.Count}");
    }
}
