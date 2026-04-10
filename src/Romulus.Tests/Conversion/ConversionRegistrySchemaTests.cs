using System.Text.Json;
using Romulus.Contracts.Models;
using Romulus.Core.Conversion;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests.Conversion;

public sealed class ConversionRegistrySchemaTests
{
    private static readonly HashSet<string> BlockedSystems = new(StringComparer.OrdinalIgnoreCase)
    {
        "ARCADE", "NEOGEO"
    };

    [Fact]
    public void DataFiles_Exist()
    {
        var dataDir = RunEnvironmentBuilder.ResolveDataDir();

        Assert.True(File.Exists(Path.Combine(dataDir, "conversion-registry.json")));
        Assert.True(File.Exists(Path.Combine(dataDir, "consoles.json")));
        Assert.True(File.Exists(Path.Combine(dataDir, "schemas", "consoles.schema.json")));
    }

    [Fact]
    public void ConsolesSchema_DefinesConversionPolicyEnumAndDefaults()
    {
        using var schema = OpenJson("schemas", "consoles.schema.json");
        var policy = schema.RootElement
            .GetProperty("properties")
            .GetProperty("consoles")
            .GetProperty("items")
            .GetProperty("properties")
            .GetProperty("conversionPolicy");

        var enumValues = policy.GetProperty("enum").EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.Contains("Auto", enumValues);
        Assert.Contains("ArchiveOnly", enumValues);
        Assert.Contains("ManualOnly", enumValues);
        Assert.Contains("None", enumValues);
        Assert.Equal("None", policy.GetProperty("default").GetString());
    }

    [Fact]
    public void ConsolesSchema_DefinesPreferredAndAlternativeTargets()
    {
        using var schema = OpenJson("schemas", "consoles.schema.json");
        var properties = schema.RootElement
            .GetProperty("properties")
            .GetProperty("consoles")
            .GetProperty("items")
            .GetProperty("properties");

        var preferred = properties.GetProperty("preferredConversionTarget").GetProperty("type")
            .EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.Contains("string", preferred);
        Assert.Contains("null", preferred);

        var alternativeType = properties.GetProperty("alternativeTargets").GetProperty("type").GetString();
        Assert.Equal("array", alternativeType);
    }

    [Fact]
    public void CapabilityConsoleKeys_AllExistInConsoles()
    {
        using var registry = OpenJson("conversion-registry.json");
        using var consoles = OpenJson("consoles.json");

        var knownConsoleKeys = consoles.RootElement.GetProperty("consoles")
            .EnumerateArray()
            .Select(c => c.GetProperty("key").GetString())
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var capability in registry.RootElement.GetProperty("capabilities").EnumerateArray())
        {
            if (!capability.TryGetProperty("applicableConsoles", out var applicable)
                || applicable.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var key in applicable.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                Assert.Contains(key!, knownConsoleKeys);
            }
        }
    }

    [Fact]
    public void CapabilityList_DoesNotContainBlockedArcadeSystems()
    {
        using var registry = OpenJson("conversion-registry.json");

        foreach (var capability in registry.RootElement.GetProperty("capabilities").EnumerateArray())
        {
            if (!capability.TryGetProperty("applicableConsoles", out var applicable)
                || applicable.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var key in applicable.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                Assert.DoesNotContain(key!, BlockedSystems);
            }
        }
    }

    [Fact]
    public void Consoles_HaveValidConversionPolicyValues()
    {
        using var consoles = OpenJson("consoles.json");

        foreach (var console in consoles.RootElement.GetProperty("consoles").EnumerateArray())
        {
            var policy = console.TryGetProperty("conversionPolicy", out var p)
                ? p.GetString() ?? "None"
                : "None";

            var parsed = Enum.TryParse<ConversionPolicy>(policy, ignoreCase: true, out _);
            Assert.True(parsed, $"Invalid conversionPolicy '{policy}' for console '{console.GetProperty("key").GetString()}'.");
        }
    }

    [Fact]
    public void Capabilities_HaveRequiredFields()
    {
        using var registry = OpenJson("conversion-registry.json");

        var requiredFields = new[]
        {
            "sourceExtension", "targetExtension", "tool", "command", "resultIntegrity", "lossless", "cost", "verification", "condition"
        };

        foreach (var capability in registry.RootElement.GetProperty("capabilities").EnumerateArray())
        {
            foreach (var field in requiredFields)
            {
                Assert.True(capability.TryGetProperty(field, out _), $"Capability missing required field '{field}'.");
            }
        }
    }

    [Fact]
    public void Consoles_HaveUniqueKeys()
    {
        using var consoles = OpenJson("consoles.json");

        var keys = consoles.RootElement.GetProperty("consoles")
            .EnumerateArray()
            .Select(x => x.GetProperty("key").GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        Assert.Equal(keys.Length, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Consoles_ConversionPolicyDistribution_MatchesReleaseBaseline()
    {
        using var consoles = OpenJson("consoles.json");

        var entries = consoles.RootElement.GetProperty("consoles").EnumerateArray().ToArray();
        Assert.Equal(163, entries.Length);

        var policyCounts = entries
            .GroupBy(
                console => console.TryGetProperty("conversionPolicy", out var policy)
                    ? policy.GetString() ?? "None"
                    : "None",
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        Assert.Equal(20, GetPolicyCount(policyCounts, "Auto"));
        Assert.Equal(52, GetPolicyCount(policyCounts, "ArchiveOnly"));
        Assert.Equal(6, GetPolicyCount(policyCounts, "ManualOnly"));
        Assert.Equal(85, GetPolicyCount(policyCounts, "None"));
    }

    [Fact]
    public void AutoAndArchiveOnlyConsoles_HavePreferredConversionTarget()
    {
        using var consoles = OpenJson("consoles.json");

        var missing = consoles.RootElement.GetProperty("consoles")
            .EnumerateArray()
            .Where(console =>
            {
                var policy = console.TryGetProperty("conversionPolicy", out var conversionPolicy)
                    ? conversionPolicy.GetString() ?? "None"
                    : "None";

                if (!string.Equals(policy, "Auto", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(policy, "ArchiveOnly", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var preferred = console.TryGetProperty("preferredConversionTarget", out var target)
                    ? target.GetString()
                    : null;

                return string.IsNullOrWhiteSpace(preferred);
            })
            .Select(console => console.GetProperty("key").GetString())
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToArray();

        Assert.Empty(missing);
    }

    [Fact]
    public void NkitCapabilities_ArePinnedToExpectedToolHash()
    {
        using var registry = OpenJson("conversion-registry.json");
        using var toolHashes = OpenJson("tool-hashes.json");

        var expectedHash = toolHashes.RootElement
            .GetProperty("Tools")
            .GetProperty("nkitprocessingapp.exe")
            .GetString();

        Assert.False(string.IsNullOrWhiteSpace(expectedHash));

        var nkitCapabilities = registry.RootElement.GetProperty("capabilities")
            .EnumerateArray()
            .Where(capability =>
            {
                var source = capability.GetProperty("sourceExtension").GetString();
                return string.Equals(source, ".nkit.iso", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(source, ".nkit.gcz", StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();

        Assert.Equal(2, nkitCapabilities.Length);

        foreach (var capability in nkitCapabilities)
        {
            var tool = capability.GetProperty("tool");
            Assert.Equal("nkit", tool.GetProperty("toolName").GetString());
            Assert.Equal(expectedHash, tool.GetProperty("expectedHash").GetString());
        }
    }

    [Fact]
    public void EcmCapability_RemainsFailClosedUntilToolHashIsPinned()
    {
        using var registry = OpenJson("conversion-registry.json");
        using var toolHashes = OpenJson("tool-hashes.json");

        var ecmCapability = registry.RootElement.GetProperty("capabilities")
            .EnumerateArray()
            .Single(capability => string.Equals(
                capability.GetProperty("sourceExtension").GetString(),
                ".ecm",
                StringComparison.OrdinalIgnoreCase));

        var tool = ecmCapability.GetProperty("tool");
        Assert.Equal("unecm", tool.GetProperty("toolName").GetString());
        Assert.False(tool.TryGetProperty("expectedHash", out _));
        Assert.False(toolHashes.RootElement.GetProperty("Tools").TryGetProperty("unecm.exe", out _));
    }

    private static int GetPolicyCount(IReadOnlyDictionary<string, int> policyCounts, string key)
        => policyCounts.TryGetValue(key, out var count) ? count : 0;

    private static JsonDocument OpenJson(params string[] pathParts)
    {
        var dataDir = RunEnvironmentBuilder.ResolveDataDir();
        var path = Path.Combine(new[] { dataDir }.Concat(pathParts).ToArray());
        return JsonDocument.Parse(File.ReadAllText(path));
    }
}
