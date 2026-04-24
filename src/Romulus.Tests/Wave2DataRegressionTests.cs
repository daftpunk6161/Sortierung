using System.Text.Json;
using Xunit;

namespace Romulus.Tests;

public sealed class Wave2DataRegressionTests
{
    [Fact]
    public void FormatScores_DiscExtensions_ExcludeContainersPatchAndPspFormats()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(RepoPath("data", "format-scores.json")));
        var discExtensions = ReadStringSet(doc.RootElement.GetProperty("discExtensions"));

        foreach (var extension in new[]
                 { ".zip", ".7z", ".rar", ".ecm", ".cso", ".pbp", ".dax", ".jso", ".zso", ".nsp", ".xci", ".nsz", ".xcz" })
        {
            Assert.DoesNotContain(extension, discExtensions);
        }

        Assert.Contains(".chd", discExtensions);
        Assert.Contains(".iso", discExtensions);
        Assert.Contains(".cue", discExtensions);
    }

    [Fact]
    public void Defaults_Extensions_IsArrayAndUnique()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(RepoPath("data", "defaults.json")));
        var extensions = doc.RootElement.GetProperty("extensions");

        Assert.Equal(JsonValueKind.Array, extensions.ValueKind);
        var values = ReadStringSet(extensions);
        Assert.Equal(values.Count, extensions.GetArrayLength());
        Assert.Contains(".zip", values);
        Assert.Contains(".gba", values);
    }

    [Theory]
    [InlineData("a800", "A800")]
    [InlineData("atari 800", "A800")]
    [InlineData("gameshark-updates", "GSUPD")]
    [InlineData("mugen", "MUGEN")]
    public void ConsoleMaps_FolderAliases_MapNewWave2Consoles(string alias, string expected)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(RepoPath("data", "console-maps.json")));
        var map = doc.RootElement.GetProperty("ConsoleFolderMap");

        Assert.Equal(expected, map.GetProperty(alias).GetString());
    }

    [Fact]
    public void RulesSchema_RegionLists_UseRegionKeyDefinition()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(RepoPath("data", "schemas", "rules.schema.json")));
        var root = doc.RootElement;
        var regionKeyEnum = root.GetProperty("$defs").GetProperty("regionKey").GetProperty("enum");

        Assert.Contains(regionKeyEnum.EnumerateArray(), value => value.GetString() == "BR");
        Assert.Contains(regionKeyEnum.EnumerateArray(), value => value.GetString() == "AU");
        Assert.Equal("#/$defs/regionKey",
            root.GetProperty("properties").GetProperty("RegionOrdered").GetProperty("items")
                .GetProperty("properties").GetProperty("Key").GetProperty("$ref").GetString());
        Assert.Equal("#/$defs/regionKey",
            root.GetProperty("properties").GetProperty("Region2Letter").GetProperty("items")
                .GetProperty("properties").GetProperty("Key").GetProperty("$ref").GetString());
    }

    private static HashSet<string> ReadStringSet(JsonElement array)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in array.EnumerateArray())
        {
            var value = item.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                set.Add(value);
        }

        return set;
    }

    private static string RepoPath(params string[] parts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(new[] { current.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        throw new FileNotFoundException("Repository file not found.", Path.Combine(parts));
    }
}
