using System.Text.Json;
using RomCleanup.Contracts.Models;
using RomCleanup.Core.Conversion;
using RomCleanup.Infrastructure.Orchestration;
using Xunit;

namespace RomCleanup.Tests.Conversion;

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

    private static JsonDocument OpenJson(params string[] pathParts)
    {
        var dataDir = RunEnvironmentBuilder.ResolveDataDir();
        var path = Path.Combine(new[] { dataDir }.Concat(pathParts).ToArray());
        return JsonDocument.Parse(File.ReadAllText(path));
    }
}
