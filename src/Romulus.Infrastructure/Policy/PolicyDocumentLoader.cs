using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Romulus.Contracts.Models;

namespace Romulus.Infrastructure.Policy;

public static class PolicyDocumentLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static LibraryPolicy LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var text = File.ReadAllText(path, Encoding.UTF8);
        return Parse(text);
    }

    public static LibraryPolicy Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new FormatException("Policy document is empty.");

        var trimmed = text.TrimStart();
        var policy = trimmed.StartsWith('{') || trimmed.StartsWith('[')
            ? ParseJson(text)
            : ParseYaml(text);

        Validate(policy);
        return policy;
    }

    public static string ComputeFingerprint(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? ""));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static LibraryPolicy ParseJson(string text)
    {
        try
        {
            return JsonSerializer.Deserialize<LibraryPolicy>(text, JsonOptions)
                   ?? throw new FormatException("Policy JSON did not contain an object.");
        }
        catch (JsonException ex)
        {
            throw new FormatException($"Policy JSON is invalid: {ex.Message}", ex);
        }
    }

    private static LibraryPolicy ParseYaml(string text)
    {
        var id = "";
        var name = "";
        var description = "";
        var preferredRegions = new List<string>();
        var allowedExtensions = new List<string>();
        var deniedTitleTokens = new List<string>();
        var requiredExtensionsByConsole = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        string? currentList = null;
        string? currentConsole = null;

        foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = StripComment(rawLine).TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                var item = Unquote(trimmed[2..].Trim());
                if (string.IsNullOrWhiteSpace(currentList))
                    throw new FormatException($"YAML list item without section: {rawLine.Trim()}");

                AddListItem(currentList, currentConsole, item, preferredRegions, allowedExtensions, deniedTitleTokens, requiredExtensionsByConsole);
                continue;
            }

            var separator = trimmed.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
                throw new FormatException($"Unsupported policy YAML line: {rawLine.Trim()}");

            var key = trimmed[..separator].Trim();
            var value = Unquote(trimmed[(separator + 1)..].Trim());

            currentConsole = null;
            switch (key)
            {
                case "id":
                    id = value;
                    currentList = null;
                    break;
                case "name":
                    name = value;
                    currentList = null;
                    break;
                case "description":
                    description = value;
                    currentList = null;
                    break;
                case "preferredRegions":
                    currentList = "preferredRegions";
                    AddInlineList(value, preferredRegions);
                    break;
                case "allowedExtensions":
                    currentList = "allowedExtensions";
                    AddInlineList(value, allowedExtensions);
                    break;
                case "deniedTitleTokens":
                    currentList = "deniedTitleTokens";
                    AddInlineList(value, deniedTitleTokens);
                    break;
                case "requiredExtensionsByConsole":
                    currentList = "requiredExtensionsByConsole";
                    break;
                default:
                    if (string.Equals(currentList, "requiredExtensionsByConsole", StringComparison.Ordinal))
                    {
                        currentConsole = key;
                        requiredExtensionsByConsole.TryAdd(currentConsole, new List<string>());
                        AddInlineList(value, requiredExtensionsByConsole[currentConsole]);
                    }
                    else
                    {
                        throw new FormatException($"Unsupported policy YAML key: {key}");
                    }
                    break;
            }
        }

        return new LibraryPolicy
        {
            Id = id,
            Name = name,
            Description = description,
            PreferredRegions = preferredRegions.ToArray(),
            AllowedExtensions = allowedExtensions.ToArray(),
            DeniedTitleTokens = deniedTitleTokens.ToArray(),
            RequiredExtensionsByConsole = requiredExtensionsByConsole
                .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    static pair => pair.Key,
                    static pair => pair.Value
                        .Where(static value => !string.IsNullOrWhiteSpace(value))
                        .Select(static value => value.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    StringComparer.OrdinalIgnoreCase)
        };
    }

    private static void AddListItem(
        string section,
        string? currentConsole,
        string item,
        List<string> preferredRegions,
        List<string> allowedExtensions,
        List<string> deniedTitleTokens,
        Dictionary<string, List<string>> requiredExtensionsByConsole)
    {
        switch (section)
        {
            case "preferredRegions":
                preferredRegions.Add(item);
                break;
            case "allowedExtensions":
                allowedExtensions.Add(item);
                break;
            case "deniedTitleTokens":
                deniedTitleTokens.Add(item);
                break;
            case "requiredExtensionsByConsole":
                if (string.IsNullOrWhiteSpace(currentConsole))
                    throw new FormatException("requiredExtensionsByConsole list item must be nested below a console key.");
                requiredExtensionsByConsole.TryAdd(currentConsole, new List<string>());
                requiredExtensionsByConsole[currentConsole].Add(item);
                break;
        }
    }

    private static void AddInlineList(string value, List<string> target)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var normalized = value.Trim();
        if (normalized.StartsWith('[') && normalized.EndsWith(']'))
            normalized = normalized[1..^1];

        foreach (var part in normalized.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            target.Add(Unquote(part));
    }

    private static void Validate(LibraryPolicy policy)
    {
        if (string.IsNullOrWhiteSpace(policy.Id))
            throw new FormatException("Policy id is required.");

        if (string.IsNullOrWhiteSpace(policy.Name))
            throw new FormatException("Policy name is required.");

        var hasAtLeastOneRule =
            policy.PreferredRegions.Length > 0 ||
            policy.AllowedExtensions.Length > 0 ||
            policy.DeniedTitleTokens.Length > 0 ||
            policy.RequiredExtensionsByConsole.Count > 0;

        if (!hasAtLeastOneRule)
            throw new FormatException("Policy must declare at least one rule.");
    }

    private static string StripComment(string line)
    {
        var inSingle = false;
        var inDouble = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '\'' && !inDouble)
                inSingle = !inSingle;
            else if (line[i] == '"' && !inSingle)
                inDouble = !inDouble;
            else if (line[i] == '#' && !inSingle && !inDouble)
                return line[..i];
        }

        return line;
    }

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 &&
            ((trimmed[0] == '"' && trimmed[^1] == '"') ||
             (trimmed[0] == '\'' && trimmed[^1] == '\'')))
        {
            return trimmed[1..^1].Trim();
        }

        return trimmed;
    }
}
