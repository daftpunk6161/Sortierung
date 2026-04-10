using System.Text.Json;
using System.Text.RegularExpressions;
using Romulus.Core;
using Romulus.Core.GameKeys;

namespace Romulus.Infrastructure.Orchestration;

public static class GameKeyNormalizationProfile
{
    private static readonly Lazy<GameKeyNormalizationData> _lazy = new(Load);

    public static IReadOnlyList<Regex>? TagPatterns => _lazy.Value.TagPatterns;
    public static IReadOnlyDictionary<string, string> AlwaysAliasMap => _lazy.Value.AlwaysAliasMap;

    /// <summary>
    /// Registers the lazy pattern factory with Core. Called automatically via
    /// <see cref="GameKeyNormalizationModuleInit"/> module initializer.
    /// </summary>
    internal static void EnsureRegistered()
    {
        GameKeyNormalizer.RegisterPatternFactory(() =>
        {
            var data = _lazy.Value;
            return (data.TagPatterns, data.AlwaysAliasMap);
        });
    }

    private static GameKeyNormalizationData Load()
    {
        try
        {
            var rulesPath = ResolveRulesJsonPath();
            if (string.IsNullOrWhiteSpace(rulesPath) || !File.Exists(rulesPath))
                return GameKeyNormalizationData.Empty;

            using var doc = JsonDocument.Parse(File.ReadAllText(rulesPath));
            var root = doc.RootElement;

            var patterns = new List<Regex>();
            if (root.TryGetProperty("GameKeyPatterns", out var gameKeyPatterns) && gameKeyPatterns.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in gameKeyPatterns.EnumerateArray())
                {
                    var pattern = item.GetString();
                    if (string.IsNullOrWhiteSpace(pattern))
                        continue;

                    patterns.Add(new Regex(pattern,
                        RegexOptions.IgnoreCase | RegexOptions.Compiled,
                        SafeRegex.DefaultTimeout));
                }
            }

            var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("AlwaysAliasMap", out var aliasMap) && aliasMap.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in aliasMap.EnumerateObject())
                {
                    var key = prop.Name?.Trim().ToLowerInvariant();
                    var value = prop.Value.GetString()?.Trim().ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                        continue;

                    aliases[key] = value;
                }
            }

            if (patterns.Count == 0 && aliases.Count == 0)
                return GameKeyNormalizationData.Empty;

            var data = new GameKeyNormalizationData(patterns, aliases);

            // Register with Core so the convenience Normalize(string) overload works
            GameKeyNormalizer.RegisterDefaultPatterns(patterns, aliases);

            return data;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return GameKeyNormalizationData.Empty;
        }
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

    private sealed record GameKeyNormalizationData(
        IReadOnlyList<Regex>? TagPatterns,
        IReadOnlyDictionary<string, string> AlwaysAliasMap)
    {
        public static GameKeyNormalizationData Empty { get; } = new(null, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }
}
