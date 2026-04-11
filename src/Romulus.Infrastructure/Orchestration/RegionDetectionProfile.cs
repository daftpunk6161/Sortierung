using System.Text.Json;
using System.Text.RegularExpressions;
using Romulus.Core;
using Romulus.Core.Regions;

namespace Romulus.Infrastructure.Orchestration;

public static class RegionDetectionProfile
{
    private static readonly Lazy<RegionDetectionData> _lazy = new(Load);

    public static void EnsureRegistered()
    {
        RegionDetector.RegisterRuleFactory(() =>
        {
            var data = _lazy.Value;
            return (
                data.OrderedRules,
                data.TwoLetterRules,
                data.MultiRegionPattern,
                data.TokenToRegionMap,
                data.LanguageCodes,
                data.EuLanguageCodes);
        });

        var loaded = _lazy.Value;
        RegionDetector.RegisterDefaultRules(
            loaded.OrderedRules,
            loaded.TwoLetterRules,
            loaded.MultiRegionPattern,
            loaded.TokenToRegionMap,
            loaded.LanguageCodes,
            loaded.EuLanguageCodes);
    }

    private static RegionDetectionData Load()
    {
        try
        {
            var rulesPath = ResolveRulesJsonPath();
            if (string.IsNullOrWhiteSpace(rulesPath) || !File.Exists(rulesPath))
                return RegionDetectionData.Empty;

            using var doc = JsonDocument.Parse(File.ReadAllText(rulesPath));
            var root = doc.RootElement;

            var ordered = ParseRules(root, "RegionOrdered");
            var twoLetter = ParseRules(root, "Region2Letter");
            var multiRegionPattern = ParseRegex(root, "MultiRegionPattern", @"\((?:en|fr)(?:,\s*(?:en|fr))+\)");
            var tokenMap = ParseTokenMap(root, "regionTokenMap");
            var languageCodes = ParseStringArray(root, "languageCodes");
            var euLanguageCodes = ParseStringArray(root, "euLanguageCodes");

            if (ordered.Count == 0 || twoLetter.Count == 0)
                return RegionDetectionData.Empty;

            return new RegionDetectionData(
                ordered,
                twoLetter,
                multiRegionPattern,
                tokenMap,
                languageCodes,
                euLanguageCodes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return RegionDetectionData.Empty;
        }
    }

    private static List<RegionDetector.RegionRule> ParseRules(JsonElement root, string propertyName)
    {
        var result = new List<RegionDetector.RegionRule>();
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var entry in value.EnumerateArray())
        {
            if (!entry.TryGetProperty("Key", out var keyElement)
                || !entry.TryGetProperty("Pattern", out var patternElement))
            {
                continue;
            }

            var key = keyElement.GetString();
            var pattern = patternElement.GetString();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(pattern))
                continue;

            result.Add(new RegionDetector.RegionRule(
                key,
                new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled, SafeRegex.DefaultTimeout)));
        }

        return result;
    }

    private static Regex ParseRegex(JsonElement root, string propertyName, string fallbackPattern)
    {
        if (root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
        {
            var pattern = value.GetString();
            if (!string.IsNullOrWhiteSpace(pattern))
            {
                return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled, SafeRegex.DefaultTimeout);
            }
        }

        return new Regex(fallbackPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled, SafeRegex.DefaultTimeout);
    }

    private static IReadOnlyDictionary<string, string> ParseTokenMap(JsonElement root, string propertyName)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Object)
            return result;

        foreach (var prop in value.EnumerateObject())
        {
            var mapValue = prop.Value.GetString();
            if (string.IsNullOrWhiteSpace(prop.Name) || string.IsNullOrWhiteSpace(mapValue))
                continue;

            result[prop.Name.Trim()] = mapValue.Trim();
        }

        return result;
    }

    private static IReadOnlyCollection<string> ParseStringArray(JsonElement root, string propertyName)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in value.EnumerateArray())
        {
            var text = item.GetString();
            if (!string.IsNullOrWhiteSpace(text))
                result.Add(text.Trim());
        }

        return result;
    }

    private static string? ResolveRulesJsonPath()
    {
        var envDataDir = Environment.GetEnvironmentVariable("ROMULUS_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(envDataDir))
        {
            var envRules = Path.Combine(envDataDir, "rules.json");
            if (File.Exists(envRules))
                return envRules;
        }

        static string? Walk(string? start)
        {
            if (string.IsNullOrWhiteSpace(start))
                return null;

            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "data", "rules.json");
                if (File.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }

            return null;
        }

        return Walk(AppContext.BaseDirectory) ?? Walk(Directory.GetCurrentDirectory());
    }

    private sealed record RegionDetectionData(
        IReadOnlyList<RegionDetector.RegionRule> OrderedRules,
        IReadOnlyList<RegionDetector.RegionRule> TwoLetterRules,
        Regex MultiRegionPattern,
        IReadOnlyDictionary<string, string> TokenToRegionMap,
        IReadOnlyCollection<string> LanguageCodes,
        IReadOnlyCollection<string> EuLanguageCodes)
    {
        public static RegionDetectionData Empty { get; } = new(
            Array.Empty<RegionDetector.RegionRule>(),
            Array.Empty<RegionDetector.RegionRule>(),
            new Regex(@"\((?:en|fr)(?:,\s*(?:en|fr))+\)", RegexOptions.IgnoreCase | RegexOptions.Compiled, SafeRegex.DefaultTimeout),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Array.Empty<string>(),
            Array.Empty<string>());
    }
}
