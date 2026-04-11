using System.Text.Json;
using Xunit;

namespace Romulus.Tests;

public sealed class Phase15RedTests
{
    [Fact]
    public void TD037_DefaultsJson_MustContainHealthScoreWeights()
    {
        var defaultsPath = ResolveDataFile("defaults.json");
        Assert.True(File.Exists(defaultsPath), $"Missing data file: {defaultsPath}");

        using var doc = JsonDocument.Parse(File.ReadAllText(defaultsPath));
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("healthScoreWeights", out var weights), "defaults.json missing 'healthScoreWeights'.");
        Assert.Equal(JsonValueKind.Object, weights.ValueKind);
    }

    [Fact]
    public void TD038_DefaultsJson_MustContainCategoryPriorityRanks()
    {
        var defaultsPath = ResolveDataFile("defaults.json");
        Assert.True(File.Exists(defaultsPath), $"Missing data file: {defaultsPath}");

        using var doc = JsonDocument.Parse(File.ReadAllText(defaultsPath));
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("categoryPriorityRanks", out var ranks), "defaults.json missing 'categoryPriorityRanks'.");
        Assert.Equal(JsonValueKind.Object, ranks.ValueKind);
    }

    [Fact]
    public void TD038_DeduplicationEngine_MustNotUseHardcodedCategoryRanks()
    {
        var sourcePath = ResolveRepoFile("Romulus.Core", "Deduplication", "DeduplicationEngine.cs");
        Assert.True(File.Exists(sourcePath), $"Missing source file: {sourcePath}");

        var source = File.ReadAllText(sourcePath);
        Assert.DoesNotContain("FileCategory.Game => 5", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FileCategory.Bios => 4", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TD039_SafetyValidator_MustCacheProtectedRootsWithLazy()
    {
        var sourcePath = ResolveRepoFile("Romulus.Infrastructure", "Safety", "SafetyValidator.cs");
        Assert.True(File.Exists(sourcePath), $"Missing source file: {sourcePath}");

        var source = File.ReadAllText(sourcePath);
        Assert.Contains("Lazy<string[]>", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TD043_DatCatalogSchema_MustExist()
    {
        var schemaPath = ResolveDataFile(Path.Combine("schemas", "dat-catalog.schema.json"));
        Assert.True(File.Exists(schemaPath), $"Missing schema file: {schemaPath}");
    }

    private static string ResolveDataFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var direct = Path.Combine(current.FullName, "data", relativePath);
            if (File.Exists(direct))
                return direct;

            current = current.Parent;
        }

        return Path.Combine("data", relativePath);
    }

    private static string ResolveRepoFile(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Romulus.sln")))
            {
                if (Directory.Exists(Path.Combine(current.FullName, "Romulus.Core"))
                    && Directory.Exists(Path.Combine(current.FullName, "Romulus.Infrastructure")))
                {
                    return Path.Combine([current.FullName, .. segments]);
                }

                return Path.Combine([current.FullName, "src", .. segments]);
            }

            current = current.Parent;
        }

        return Path.Combine(segments);
    }
}
