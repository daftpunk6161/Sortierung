using System.Text.Json;
using System.Text.RegularExpressions;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// RED tests for still-open tracker findings starting from priority point 2.
/// Scope:
/// API-02, ERR-08, TH-06, DI-01, TEST-04, TEST-05.
/// </summary>
public sealed class TrackerPoint2OpenFindingsRedTests
{
    [Fact]
    public void API_02_ApiErrors_MustNotExposeRawExceptionMessages()
    {
        var program = File.ReadAllText(FindRepoFile("src", "Romulus.Api", "Program.cs"));
        var runWatch = File.ReadAllText(FindRepoFile("src", "Romulus.Api", "Program.RunWatchEndpoints.cs"));

        Assert.DoesNotMatch(new Regex(@"ApiError\s*\([^)]*ex\.Message", RegexOptions.CultureInvariant), program);
        Assert.DoesNotMatch(new Regex(@"ApiError\s*\([^)]*ex\.Message", RegexOptions.CultureInvariant), runWatch);
        Assert.DoesNotContain("errors.Add($\"{entry.Id}: {ex.Message}\")", program, StringComparison.Ordinal);
    }

    [Fact]
    public void ERR_08_Cli_MustNotUseSyncOverAsyncGetResult()
    {
        var cliProgram = File.ReadAllText(FindRepoFile("src", "Romulus.CLI", "Program.cs"));
        var mapper = File.ReadAllText(FindRepoFile("src", "Romulus.CLI", "CliOptionsMapper.cs"));

        Assert.DoesNotContain("GetAwaiter().GetResult()", cliProgram, StringComparison.Ordinal);
        Assert.DoesNotContain("GetAwaiter().GetResult()", mapper, StringComparison.Ordinal);
    }

    [Fact]
    public void DI_01_Cli_MustUseDiCompositionRoot()
    {
        var cliProgram = File.ReadAllText(FindRepoFile("src", "Romulus.CLI", "Program.cs"));

        Assert.Contains("new ServiceCollection()", cliProgram, StringComparison.Ordinal);
        Assert.Contains("BuildServiceProvider", cliProgram, StringComparison.Ordinal);
    }

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

    [Fact]
    public void TEST_05_ParitySuite_MustBeInNonParallelCollection()
    {
        var paritySuite = File.ReadAllText(FindRepoFile("src", "Romulus.Tests", "HardCoreInvariantRegressionSuiteTests.cs"));
        var collectionDefinition = File.ReadAllText(FindRepoFile("src", "Romulus.Tests", "TestFixtures", "SerialExecutionCollection.cs"));

        Assert.Contains("[Collection(\"SerialExecution\")]", paritySuite, StringComparison.Ordinal);
        Assert.Contains("[CollectionDefinition(\"SerialExecution\", DisableParallelization = true)]", collectionDefinition, StringComparison.Ordinal);
    }

    private static string FindRepoFile(params string[] parts)
    {
        var dataDir = RunEnvironmentBuilder.ResolveDataDir();
        var repoRoot = Directory.GetParent(dataDir)?.FullName
            ?? throw new InvalidOperationException("Repository root could not be resolved from data directory.");
        return Path.Combine(new[] { repoRoot }.Concat(parts).ToArray());
    }
}
