using System.Globalization;
using System.Text.Json;

namespace Romulus.Infrastructure.Configuration;

public static class StartupDataSchemaValidator
{
    private static readonly (string DataFile, string SchemaFile)[] RequiredFiles =
    [
        ("consoles.json", "consoles.schema.json"),
        ("console-maps.json", "console-maps.schema.json"),
        ("rules.json", "rules.schema.json"),
        ("defaults.json", "defaults.schema.json"),
        ("format-scores.json", "format-scores.schema.json"),
        ("tool-hashes.json", "tool-hashes.schema.json"),
        ("ui-lookups.json", "ui-lookups.schema.json"),
        ("conversion-registry.json", "conversion-registry.schema.json"),
        ("dat-catalog.json", "dat-catalog.schema.json")
    ];

    public static void ValidateRequiredFiles(string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
            throw new InvalidOperationException("Data directory is required for startup schema validation.");

        foreach (var (dataFile, schemaFile) in RequiredFiles)
        {
            var dataPath = Path.Combine(dataDirectory, dataFile);
            var schemaPath = Path.Combine(dataDirectory, "schemas", schemaFile);
            ValidateFileAgainstSchema(dataPath, schemaPath, dataFile);
        }
    }

    public static void ValidateFileAgainstSchema(string dataPath, string schemaPath, string displayName)
    {
        if (!File.Exists(dataPath))
            throw new InvalidOperationException($"Schema validation failed for '{displayName}': data file is missing.");

        if (!File.Exists(schemaPath))
            throw new InvalidOperationException($"Schema validation failed for '{displayName}': schema file '{Path.GetFileName(schemaPath)}' is missing.");

        using var dataDoc = JsonDocument.Parse(File.ReadAllText(dataPath));
        using var schemaDoc = JsonDocument.Parse(File.ReadAllText(schemaPath));

        var errors = new List<string>();
        ValidateNode(dataDoc.RootElement, schemaDoc.RootElement, "$", errors);

        if (errors.Count > 0)
            throw new InvalidOperationException($"Schema validation failed for '{displayName}': {errors[0]}");
    }

    private static void ValidateNode(JsonElement value, JsonElement schema, string path, List<string> errors)
    {
        if (errors.Count > 0)
            return;

        if (!ValidateType(value, schema, path, errors))
            return;

        ValidateEnum(value, schema, path, errors);
        if (errors.Count > 0)
            return;

        ValidateLengthAndRange(value, schema, path, errors);
        if (errors.Count > 0)
            return;

        if (value.ValueKind == JsonValueKind.Object)
        {
            ValidateObject(value, schema, path, errors);
            return;
        }

        if (value.ValueKind == JsonValueKind.Array)
            ValidateArray(value, schema, path, errors);
    }

    private static bool ValidateType(JsonElement value, JsonElement schema, string path, List<string> errors)
    {
        if (!schema.TryGetProperty("type", out var typeElement))
            return true;

        var allowed = ParseAllowedTypes(typeElement);
        if (allowed.Count == 0)
            return true;

        foreach (var allowedType in allowed)
        {
            if (MatchesType(value, allowedType))
                return true;
        }

        errors.Add($"{path} has type {DescribeValueKind(value.ValueKind)} but expected {string.Join("|", allowed)}.");
        return false;
    }

    private static HashSet<string> ParseAllowedTypes(JsonElement typeElement)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (typeElement.ValueKind == JsonValueKind.String)
        {
            var single = typeElement.GetString();
            if (!string.IsNullOrWhiteSpace(single))
                allowed.Add(single.Trim());
            return allowed;
        }

        if (typeElement.ValueKind != JsonValueKind.Array)
            return allowed;

        foreach (var item in typeElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                continue;

            var text = item.GetString();
            if (!string.IsNullOrWhiteSpace(text))
                allowed.Add(text.Trim());
        }

        return allowed;
    }

    private static bool MatchesType(JsonElement value, string schemaType)
    {
        switch (schemaType)
        {
            case "object":
                return value.ValueKind == JsonValueKind.Object;
            case "array":
                return value.ValueKind == JsonValueKind.Array;
            case "string":
                return value.ValueKind == JsonValueKind.String;
            case "number":
                return value.ValueKind == JsonValueKind.Number;
            case "integer":
                return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _);
            case "boolean":
                return value.ValueKind is JsonValueKind.True or JsonValueKind.False;
            case "null":
                return value.ValueKind == JsonValueKind.Null;
            default:
                return true;
        }
    }

    private static void ValidateEnum(JsonElement value, JsonElement schema, string path, List<string> errors)
    {
        if (!schema.TryGetProperty("enum", out var enumElement) || enumElement.ValueKind != JsonValueKind.Array)
            return;

        foreach (var allowed in enumElement.EnumerateArray())
        {
            if (JsonElementEqualityComparer.Equals(value, allowed))
                return;
        }

        errors.Add($"{path} has value {DescribeValue(value)} which is not in enum.");
    }

    private static void ValidateLengthAndRange(JsonElement value, JsonElement schema, string path, List<string> errors)
    {
        if (value.ValueKind == JsonValueKind.String
            && schema.TryGetProperty("minLength", out var minLengthElement)
            && minLengthElement.ValueKind == JsonValueKind.Number
            && minLengthElement.TryGetInt32(out var minLength)
            && value.GetString()!.Length < minLength)
        {
            errors.Add($"{path} must have minLength {minLength}.");
            return;
        }

        if (value.ValueKind != JsonValueKind.Number)
            return;

        if (!schema.TryGetProperty("minimum", out var minimumElement)
            || minimumElement.ValueKind != JsonValueKind.Number
            || !minimumElement.TryGetDouble(out var minimum)
            || !value.TryGetDouble(out var numericValue))
        {
            return;
        }

        if (numericValue < minimum)
            errors.Add($"{path} must be >= {minimum.ToString(CultureInfo.InvariantCulture)}.");
    }

    private static void ValidateObject(JsonElement value, JsonElement schema, string path, List<string> errors)
    {
        var properties = ReadProperties(schema);
        ValidateRequiredProperties(value, schema, path, errors);
        if (errors.Count > 0)
            return;

        var additionalPropertySchema = ReadAdditionalPropertySchema(schema);
        var allowAdditional = ReadAllowAdditionalProperties(schema);
        foreach (var property in value.EnumerateObject())
        {
            if (!properties.TryGetValue(property.Name, out var propertySchema))
            {
                if (additionalPropertySchema.HasValue)
                {
                    ValidateNode(property.Value, additionalPropertySchema.Value, $"{path}.{property.Name}", errors);
                    if (errors.Count > 0)
                        return;

                    continue;
                }

                if (!allowAdditional)
                {
                    errors.Add($"{path}.{property.Name} is not allowed by schema.");
                    return;
                }

                continue;
            }

            ValidateNode(property.Value, propertySchema, $"{path}.{property.Name}", errors);
            if (errors.Count > 0)
                return;
        }
    }

    private static Dictionary<string, JsonElement> ReadProperties(JsonElement schema)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (!schema.TryGetProperty("properties", out var propertiesElement)
            || propertiesElement.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var property in propertiesElement.EnumerateObject())
            result[property.Name] = property.Value;

        return result;
    }

    private static void ValidateRequiredProperties(JsonElement value, JsonElement schema, string path, List<string> errors)
    {
        if (!schema.TryGetProperty("required", out var requiredElement)
            || requiredElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var requiredName in requiredElement.EnumerateArray())
        {
            if (requiredName.ValueKind != JsonValueKind.String)
                continue;

            var propertyName = requiredName.GetString();
            if (string.IsNullOrWhiteSpace(propertyName))
                continue;

            if (!value.TryGetProperty(propertyName, out _))
            {
                errors.Add($"{path}.{propertyName} is required.");
                return;
            }
        }
    }

    private static bool ReadAllowAdditionalProperties(JsonElement schema)
    {
        if (!schema.TryGetProperty("additionalProperties", out var additionalPropertiesElement))
            return true;

        if (additionalPropertiesElement.ValueKind == JsonValueKind.False)
            return false;

        return true;
    }

    private static JsonElement? ReadAdditionalPropertySchema(JsonElement schema)
    {
        if (!schema.TryGetProperty("additionalProperties", out var additionalPropertiesElement))
            return null;

        return additionalPropertiesElement.ValueKind == JsonValueKind.Object
            ? additionalPropertiesElement
            : null;
    }

    private static void ValidateArray(JsonElement value, JsonElement schema, string path, List<string> errors)
    {
        if (!schema.TryGetProperty("items", out var itemsElement))
            return;

        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            ValidateNode(item, itemsElement, $"{path}[{index}]", errors);
            if (errors.Count > 0)
                return;

            index++;
        }
    }

    private static string DescribeValueKind(JsonValueKind kind)
    {
        return kind switch
        {
            JsonValueKind.Object => "object",
            JsonValueKind.Array => "array",
            JsonValueKind.String => "string",
            JsonValueKind.Number => "number",
            JsonValueKind.True => "boolean",
            JsonValueKind.False => "boolean",
            JsonValueKind.Null => "null",
            _ => kind.ToString().ToLowerInvariant()
        };
    }

    private static string DescribeValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => $"'{value.GetString()}'",
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            _ => value.GetRawText()
        };
    }

    private static class JsonElementEqualityComparer
    {
        public static bool Equals(JsonElement left, JsonElement right)
        {
            if (left.ValueKind != right.ValueKind)
                return false;

            return left.ValueKind switch
            {
                JsonValueKind.String => string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal),
                JsonValueKind.Number => left.GetRawText() == right.GetRawText(),
                JsonValueKind.True => true,
                JsonValueKind.False => true,
                JsonValueKind.Null => true,
                _ => left.GetRawText() == right.GetRawText()
            };
        }
    }
}
