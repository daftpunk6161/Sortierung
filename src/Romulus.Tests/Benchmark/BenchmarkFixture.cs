using Romulus.Core.Classification;
using Romulus.Tests.Benchmark.Generators;
using Romulus.Tests.Benchmark.Infrastructure;
using Romulus.Tests.Benchmark.Models;
using Xunit;

namespace Romulus.Tests.Benchmark;

/// <summary>
/// Shared xUnit fixture for benchmark tests. Lazily generates stub files from
/// ground-truth entries on first use, then provides a configured ConsoleDetector
/// for the evaluation runner.
/// </summary>
public sealed class BenchmarkFixture : IAsyncLifetime
{
    private readonly object _lock = new();
    private bool _initialized;

    /// <summary>Root directory containing all generated stub files.</summary>
    public string SamplesRoot { get; private set; } = null!;

    /// <summary>All loaded ground-truth entries.</summary>
    public IReadOnlyList<GroundTruthEntry> AllEntries { get; private set; } = [];

    /// <summary>Production-quality console detector loaded from consoles.json.</summary>
    public ConsoleDetector Detector { get; private set; } = null!;

    public Task InitializeAsync()
    {
        lock (_lock)
        {
            if (_initialized)
                return Task.CompletedTask;

            SamplesRoot = Path.Combine(BenchmarkPaths.BenchmarkDir, "samples");
            AllEntries = GroundTruthLoader.LoadAll();

            // Also include holdout entries for stub generation so they get their own files
            var holdoutEntries = HoldoutEvaluator.LoadHoldoutEntries();
            var allForGeneration = new List<GroundTruthEntry>(AllEntries);
            allForGeneration.AddRange(holdoutEntries);

            // Generate stubs if not already present or entry count changed
            var markerFile = Path.Combine(SamplesRoot, ".generated");
            var expectedMarker = $"{allForGeneration.Count} entries generated";
            var needsRegeneration = !File.Exists(markerFile)
                || File.ReadAllText(markerFile).Trim() != expectedMarker;

            if (needsRegeneration)
            {
                if (Directory.Exists(SamplesRoot))
                    Directory.Delete(SamplesRoot, recursive: true);

                Directory.CreateDirectory(SamplesRoot);
                var dispatch = new StubGeneratorDispatch();
                dispatch.GenerateAll(allForGeneration, SamplesRoot);

                // Write marker with entry count for fingerprinting
                File.WriteAllText(markerFile, expectedMarker);
            }

            // Load production ConsoleDetector with header detectors
            var consolesJson = File.ReadAllText(BenchmarkPaths.ConsolesJsonPath);
            var discDetector = new DiscHeaderDetector();
            var cartridgeDetector = new CartridgeHeaderDetector();
            Detector = ConsoleDetector.LoadFromJson(
                consolesJson,
                discHeaderDetector: discDetector,
                cartridgeHeaderDetector: cartridgeDetector);

            _initialized = true;
        }

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        // Stubs are left on disk for debugging. CI .gitignore excludes benchmark/samples/.
        return Task.CompletedTask;
    }

    /// <summary>Returns entries from a specific JSONL set file.</summary>
    public IReadOnlyList<GroundTruthEntry> GetEntriesForSet(string setFileName)
    {
        return AllEntries
            .Where(e => HasMatchingSource(e, setFileName))
            .ToList();
    }

    /// <summary>Returns entries matching the given tags.</summary>
    public IReadOnlyList<GroundTruthEntry> GetEntriesByTag(params string[] tags)
    {
        return AllEntries
            .Where(e => e.Tags is not null && tags.Any(t => e.Tags.Contains(t, StringComparer.OrdinalIgnoreCase)))
            .ToList();
    }

    /// <summary>Get the full file path for a ground-truth entry in the samples directory.</summary>
    public string GetSamplePath(GroundTruthEntry entry)
    {
        var relativePath = StubGeneratorDispatch.BuildRelativePath(entry);
        return Path.Combine(SamplesRoot, relativePath);
    }

    private static bool HasMatchingSource(GroundTruthEntry entry, string setFileName)
    {
        // Entries originating from a specific JSONL are identified by their ID prefix
        // e.g., "gc-NES-..." → golden-core, "ec-NES-..." → edge-cases
        var prefix = setFileName.Replace(".jsonl", "");
        return prefix switch
        {
            "golden-core" => entry.Id.StartsWith("gc-", StringComparison.OrdinalIgnoreCase),
            "edge-cases" => entry.Id.StartsWith("ec-", StringComparison.OrdinalIgnoreCase),
            "negative-controls" => entry.Id.StartsWith("nc-", StringComparison.OrdinalIgnoreCase),
            "golden-realworld" => entry.Id.StartsWith("gr-", StringComparison.OrdinalIgnoreCase),
            "chaos-mixed" => entry.Id.StartsWith("cm-", StringComparison.OrdinalIgnoreCase),
            "dat-coverage" => entry.Id.StartsWith("dc-", StringComparison.OrdinalIgnoreCase),
            "repair-safety" => entry.Id.StartsWith("rs-", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }
}
