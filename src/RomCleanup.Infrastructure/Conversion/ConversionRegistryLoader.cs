namespace RomCleanup.Infrastructure.Conversion;

using System.Text.Json;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;

/// <summary>
/// Loads conversion capabilities and policies from JSON configuration.
/// </summary>
public sealed class ConversionRegistryLoader : IConversionRegistry
{
    private readonly IReadOnlyList<ConversionCapability> _capabilities;
    private readonly IReadOnlyDictionary<string, ConversionPolicy> _policies;
    private readonly IReadOnlyDictionary<string, string?> _preferredTargets;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _alternativeTargets;
    private readonly IReadOnlyDictionary<string, double> _compressionEstimates;

    public ConversionRegistryLoader(string conversionRegistryPath, string consolesJsonPath)
    {
        if (string.IsNullOrWhiteSpace(conversionRegistryPath))
            throw new ArgumentException("Path must not be empty.", nameof(conversionRegistryPath));
        if (string.IsNullOrWhiteSpace(consolesJsonPath))
            throw new ArgumentException("Path must not be empty.", nameof(consolesJsonPath));

        var (capabilities, compressionEstimates) = LoadCapabilities(conversionRegistryPath);
        _capabilities = capabilities;
        _compressionEstimates = compressionEstimates;

        var (policies, preferred, alternatives, knownKeys) = LoadConsolePolicies(consolesJsonPath);
        _policies = policies;
        _preferredTargets = preferred;
        _alternativeTargets = alternatives;

        ValidateConsoleKeys(knownKeys, _capabilities);
    }

    public IReadOnlyList<ConversionCapability> GetCapabilities() => _capabilities;

    public ConversionPolicy GetPolicy(string consoleKey)
    {
        if (string.IsNullOrWhiteSpace(consoleKey))
            return ConversionPolicy.None;

        return _policies.TryGetValue(consoleKey, out var policy)
            ? policy
            : ConversionPolicy.None;
    }

    public string? GetPreferredTarget(string consoleKey)
    {
        if (string.IsNullOrWhiteSpace(consoleKey))
            return null;

        return _preferredTargets.TryGetValue(consoleKey, out var target)
            ? target
            : null;
    }

    public IReadOnlyList<string> GetAlternativeTargets(string consoleKey)
    {
        if (string.IsNullOrWhiteSpace(consoleKey))
            return Array.Empty<string>();

        return _alternativeTargets.TryGetValue(consoleKey, out var targets)
            ? targets
            : Array.Empty<string>();
    }

    public IReadOnlyDictionary<string, double> GetCompressionEstimates() => _compressionEstimates;

    private static (IReadOnlyList<ConversionCapability>, IReadOnlyDictionary<string, double>) LoadCapabilities(string path)
    {
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);

        ValidateAllowedProperties(
            doc.RootElement,
            ["schemaVersion", "capabilities", "compressionEstimates"],
            "conversion-registry root");

        _ = ReadRequiredString(doc.RootElement, "schemaVersion");

        if (!doc.RootElement.TryGetProperty("capabilities", out var capabilitiesElement)
            || capabilitiesElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("conversion-registry.json must contain a 'capabilities' array.");
        }

        var capabilities = new List<ConversionCapability>();
        foreach (var item in capabilitiesElement.EnumerateArray())
        {
            capabilities.Add(ParseCapability(item));
        }

        // TASK-055: Load externalized compression estimates
        var estimates = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (doc.RootElement.TryGetProperty("compressionEstimates", out var estimatesElement)
            && estimatesElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in estimatesElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Number)
                    estimates[prop.Name] = prop.Value.GetDouble();
            }
        }

        return (capabilities, estimates);
    }

    private static ConversionCapability ParseCapability(JsonElement item)
    {
        ValidateAllowedProperties(
            item,
            [
                "sourceExtension",
                "targetExtension",
                "tool",
                "command",
                "applicableConsoles",
                "requiredSourceIntegrity",
                "resultIntegrity",
                "lossless",
                "cost",
                "verification",
                "description",
                "condition"
            ],
            "conversion capability");

        var sourceExtension = ReadRequiredString(item, "sourceExtension");
        var targetExtension = ReadRequiredString(item, "targetExtension");
        if (!item.TryGetProperty("tool", out var toolElement) || toolElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Missing required property 'tool'.");

        ValidateAllowedProperties(toolElement, ["toolName", "expectedHash", "minVersion"], "conversion tool");

        var tool = new ToolRequirement
        {
            ToolName = ReadRequiredString(toolElement, "toolName"),
            ExpectedHash = ReadOptionalString(toolElement, "expectedHash"),
            MinVersion = ReadOptionalString(toolElement, "minVersion")
        };

        var applicableConsoles = ReadOptionalStringArray(item, "applicableConsoles")
            ?.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var requiredSourceIntegrity = ParseOptionalEnum<SourceIntegrity>(ReadOptionalString(item, "requiredSourceIntegrity"));
        var resultIntegrity = ParseRequiredEnum<SourceIntegrity>(ReadRequiredString(item, "resultIntegrity"));
        var verification = ParseRequiredEnum<VerificationMethod>(ReadRequiredString(item, "verification"));
        var condition = ParseOptionalEnum<ConversionCondition>(ReadOptionalString(item, "condition")) ?? ConversionCondition.None;

        return new ConversionCapability
        {
            SourceExtension = sourceExtension,
            TargetExtension = targetExtension,
            Tool = tool,
            Command = ReadRequiredString(item, "command"),
            ApplicableConsoles = applicableConsoles,
            RequiredSourceIntegrity = requiredSourceIntegrity,
            ResultIntegrity = resultIntegrity,
            Lossless = item.GetProperty("lossless").GetBoolean(),
            Cost = item.GetProperty("cost").GetInt32(),
            Verification = verification,
            Description = ReadOptionalString(item, "description"),
            Condition = condition
        };
    }

    private static (IReadOnlyDictionary<string, ConversionPolicy> Policies,
        IReadOnlyDictionary<string, string?> Preferred,
        IReadOnlyDictionary<string, IReadOnlyList<string>> Alternatives,
        HashSet<string> KnownKeys) LoadConsolePolicies(string path)
    {
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);

        ValidateAllowedProperties(doc.RootElement, ["_meta", "consoles"], "consoles root");

        if (!doc.RootElement.TryGetProperty("consoles", out var consolesElement)
            || consolesElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("consoles.json must contain a 'consoles' array.");
        }

        var policies = new Dictionary<string, ConversionPolicy>(StringComparer.OrdinalIgnoreCase);
        var preferred = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var alternatives = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var knownKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var console in consolesElement.EnumerateArray())
        {
            ValidateAllowedProperties(
                console,
                [
                    "key",
                    "displayName",
                    "discBased",
                    "uniqueExts",
                    "ambigExts",
                    "folderAliases",
                    "aliases",
                    "hints",
                    "conversionPolicy",
                    "preferredConversionTarget",
                    "alternativeTargets"
                ],
                "console entry");

            var key = ReadRequiredString(console, "key");
            knownKeys.Add(key);

            var policyString = ReadOptionalString(console, "conversionPolicy") ?? "None";
            var policy = ParseRequiredEnum<ConversionPolicy>(policyString);
            policies[key] = policy;

            preferred[key] = ReadOptionalString(console, "preferredConversionTarget");
            alternatives[key] = ReadOptionalStringArray(console, "alternativeTargets") ?? Array.Empty<string>();
        }

        return (policies, preferred, alternatives, knownKeys);
    }

    private static void ValidateConsoleKeys(HashSet<string> knownKeys, IEnumerable<ConversionCapability> capabilities)
    {
        foreach (var capability in capabilities)
        {
            if (capability.ApplicableConsoles is not { Count: > 0 })
                continue;

            foreach (var key in capability.ApplicableConsoles)
            {
                if (!knownKeys.Contains(key))
                    throw new InvalidOperationException($"Unknown console key '{key}' in conversion capability.");
            }
        }
    }

    private static string ReadRequiredString(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException($"Missing required property '{propertyName}'.");

        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException($"Property '{propertyName}' cannot be empty.");

        return text;
    }

    private static string? ReadOptionalString(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null => null,
            _ => throw new InvalidOperationException($"Property '{propertyName}' must be string or null.")
        };
    }

    private static IReadOnlyList<string>? ReadOptionalStringArray(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.Null)
            return null;

        if (value.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Property '{propertyName}' must be an array.");

        var values = new List<string>();
        foreach (var element in value.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String)
                throw new InvalidOperationException($"Property '{propertyName}' must contain only strings.");

            var text = element.GetString();
            if (!string.IsNullOrWhiteSpace(text))
                values.Add(text);
        }

        return values;
    }

    private static TEnum ParseRequiredEnum<TEnum>(string value)
        where TEnum : struct, Enum
    {
        if (!Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed))
            throw new InvalidOperationException($"Invalid value '{value}' for enum {typeof(TEnum).Name}.");

        return parsed;
    }

    private static TEnum? ParseOptionalEnum<TEnum>(string? value)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed))
            throw new InvalidOperationException($"Invalid value '{value}' for enum {typeof(TEnum).Name}.");

        return parsed;
    }

    private static void ValidateAllowedProperties(JsonElement element, string[] allowedProperties, string context)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Expected object for {context}.");

        var allowSet = new HashSet<string>(allowedProperties, StringComparer.Ordinal);

        foreach (var property in element.EnumerateObject())
        {
            if (!allowSet.Contains(property.Name))
                throw new InvalidOperationException($"Unexpected property '{property.Name}' in {context}.");
        }
    }
}
