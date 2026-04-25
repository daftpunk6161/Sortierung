using System.Text.Json;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Data-validation tests retained from the historical "tracker open findings" set.
/// All source-mirror assertions were removed in Block A
/// of test-suite-remediation-plan-2026-04-25.md.
/// </summary>
public sealed class TrackerPoint2OpenFindingsRedTests
{
    [Fact]
    public void TH_06_ToolHashes_MustNotUseKnownPlaceholderDigests()
    {
        using var toolHashes = JsonDocument.Parse(File.ReadAllText(FindRepoFile("data", "tool-hashes.json")));
        var tools = toolHashes.RootElement.GetProperty("Tools");

        var knownPlaceholders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "0000000000000000000000000000000000000000000000000000000000000001",
            "0000000000000000000000000000000000000000000000000000000000000002",
            "0000000000000000000000000000000000000000000000000000000000000003"
        };

        foreach (var tool in tools.EnumerateObject())
        {
            var hash = tool.Value.GetString() ?? string.Empty;
            Assert.DoesNotContain(hash, knownPlaceholders);
        }
    }

    [Fact]
    public void TEST_04_XunitRunner_MustNotUseUnlimitedParallelThreads()
    {
        using var runnerConfig = JsonDocument.Parse(File.ReadAllText(FindRepoFile("src", "Romulus.Tests", "xunit.runner.json")));
        var maxParallelThreads = runnerConfig.RootElement.GetProperty("maxParallelThreads").GetInt32();

        Assert.NotEqual(-1, maxParallelThreads);
    }

    private static string FindRepoFile(params string[] parts)
    {
        var dataDir = RunEnvironmentBuilder.ResolveDataDir();
        var repoRoot = Directory.GetParent(dataDir)?.FullName
            ?? throw new InvalidOperationException("Repository root could not be resolved from data directory.");
        return Path.Combine(new[] { repoRoot }.Concat(parts).ToArray());
    }
}
