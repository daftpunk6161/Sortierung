using System.Reflection;
using Romulus.Tests.Benchmark.Infrastructure;
using Romulus.Tests.Benchmark.Models;
using Xunit.Sdk;

namespace Romulus.Tests.Benchmark;

/// <summary>
/// Custom xUnit DataAttribute that provides GroundTruthEntry instances as Theory data.
/// Supports optional filtering by set file name, difficulty, and tags.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
internal sealed class BenchmarkDataAttribute : DataAttribute
{
    private readonly string? _setFileName;
    private readonly string? _difficulty;
    private readonly string[]? _tags;

    /// <param name="setFileName">Optional JSONL file name (e.g., "golden-core.jsonl"). If null, loads all entries.</param>
    /// <param name="difficulty">Optional difficulty filter (e.g., "easy", "medium", "hard").</param>
    /// <param name="tags">Optional tag filter — entry must contain at least one of these tags.</param>
    public BenchmarkDataAttribute(
        string? setFileName = null,
        string? difficulty = null,
        params string[] tags)
    {
        _setFileName = setFileName;
        _difficulty = difficulty;
        _tags = tags?.Length > 0 ? tags : null;
    }

    public override IEnumerable<object[]> GetData(MethodInfo testMethod)
    {
        IEnumerable<GroundTruthEntry> entries = _setFileName is not null
            ? GroundTruthLoader.LoadSet(_setFileName)
            : GroundTruthLoader.LoadAll();

        if (_difficulty is not null)
            entries = entries.Where(e =>
                string.Equals(e.Difficulty, _difficulty, StringComparison.OrdinalIgnoreCase));

        if (_tags is not null)
            entries = entries.Where(e =>
                e.Tags is not null && _tags.Any(t =>
                    e.Tags.Contains(t, StringComparer.OrdinalIgnoreCase)));

        foreach (var entry in entries)
        {
            yield return [entry];
        }
    }
}
