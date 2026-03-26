using System.Text.Json;
using RomCleanup.Tests.Benchmark.Generators;
using RomCleanup.Tests.Benchmark.Infrastructure;
using RomCleanup.Tests.Benchmark.Models;
using Xunit;
using Xunit.Abstractions;

namespace RomCleanup.Tests.Benchmark;

/// <summary>
/// Phase 6B data fill tests — generates performance-scale (5000 entries),
/// expands holdout (50→200), and updates manifest.json.
/// Tagged DatasetGeneration — NOT part of regular CI.
/// </summary>
public sealed class Phase6BDataFillTests
{
    private readonly ITestOutputHelper _output;

    public Phase6BDataFillTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "DatasetGeneration")]
    public void FillPerformanceScale_Generates5000Entries()
    {
        var generator = new Generators.ScaleDatasetGenerator();
        var count = generator.WriteToFile(5000);

        Assert.Equal(5000, count);
        Assert.True(File.Exists(generator.OutputPath), "performance-scale.jsonl must exist");

        var lines = File.ReadAllLines(generator.OutputPath)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();
        Assert.Equal(5000, lines.Length);

        // Verify first entry is parseable
        var first = JsonSerializer.Deserialize<GroundTruthEntry>(lines[0], new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        Assert.NotNull(first);
        Assert.StartsWith("ps-", first!.Id);

        // Verify determinism: same seed produces same IDs
        var ids = lines.Select(l =>
        {
            var e = JsonSerializer.Deserialize<GroundTruthEntry>(l, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return e?.Id;
        }).Where(id => id is not null).ToHashSet();
        Assert.Equal(5000, ids.Count);

        _output.WriteLine($"Written {count} entries to {generator.OutputPath}");
    }

    [Fact]
    [Trait("Category", "DatasetGeneration")]
    public void ExpandHoldout_ReachesTarget200()
    {
        var existing = HoldoutEvaluator.LoadHoldoutEntries();
        var existingCount = existing.Count;
        _output.WriteLine($"Existing holdout entries: {existingCount}");

        if (existingCount >= 200)
        {
            _output.WriteLine("Holdout already at target — skipping expansion");
            return;
        }

        var expander = new HoldoutExpander();
        var newEntries = expander.GenerateExpansion(existingCount, targetTotal: 200);

        Assert.True(newEntries.Count > 0, "Must generate new entries");

        // Verify no ID collisions with existing entries
        var existingIds = existing.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in newEntries)
        {
            Assert.DoesNotContain(entry.Id, existingIds);
        }

        // Verify chaos quota ≥ 30%
        var chaosCount = newEntries.Count(e =>
            e.Difficulty is "hard" or "adversarial" ||
            (e.Tags?.Any(t => t.Contains("chaos") || t.Contains("conflict") || t.Contains("disambiguation")) ?? false));
        var chaosRate = 100.0 * chaosCount / newEntries.Count;
        _output.WriteLine($"New entries: {newEntries.Count}, chaos: {chaosCount} ({chaosRate:F1}%)");
        Assert.True(chaosRate >= 30, $"Chaos rate {chaosRate:F1}% must be ≥ 30%");

        // Write to file
        var written = expander.WriteExpansion(newEntries);
        Assert.Equal(newEntries.Count, written);

        // Verify total
        var total = HoldoutEvaluator.LoadHoldoutEntries();
        Assert.True(total.Count >= 200, $"Holdout total {total.Count} must be ≥ 200");

        // Verify unique IDs
        var allIds = total.Select(e => e.Id).ToList();
        Assert.Equal(allIds.Count, allIds.Distinct(StringComparer.Ordinal).Count());

        _output.WriteLine($"Holdout expanded: {existingCount} → {total.Count} entries");
    }

    [Fact]
    [Trait("Category", "DatasetGeneration")]
    public void UpdateManifest_ReflectsActualCounts()
    {
        var allEntries = GroundTruthLoader.LoadAll();
        var holdoutEntries = HoldoutEvaluator.LoadHoldoutEntries();

        var bySets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in BenchmarkPaths.AllJsonlFiles)
        {
            var setName = Path.GetFileNameWithoutExtension(file);
            var entries = GroundTruthLoader.LoadFile(file);
            bySets[setName] = entries.Count;
        }

        var manifest = new
        {
            _meta = new
            {
                description = "Benchmark dataset manifest",
                version = "3.1.0",
                groundTruthVersion = "2.0.0",
                lastModified = DateTime.UtcNow.ToString("yyyy-MM-dd")
            },
            totalEntries = allEntries.Count,
            holdoutEntries = holdoutEntries.Count,
            bySet = bySets
        };

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        File.WriteAllText(BenchmarkPaths.ManifestJsonPath, json);
        _output.WriteLine($"Manifest updated: {allEntries.Count} entries, {holdoutEntries.Count} holdout");
        _output.WriteLine(json);

        Assert.True(allEntries.Count >= 2000, $"Total entries {allEntries.Count} must be ≥ 2000");
    }
}
