using System.Text.Json;
using Romulus.Core.Scoring;

namespace Romulus.Infrastructure.Orchestration;

public static class VersionScoringProfile
{
    private static readonly Lazy<string?> _lazyLangPattern = new(LoadLangPattern);

    public static void EnsureRegistered()
    {
        VersionScorer.RegisterLanguagePatternFactory(() => _lazyLangPattern.Value ?? string.Empty);

        var pattern = _lazyLangPattern.Value;
        if (!string.IsNullOrWhiteSpace(pattern))
            VersionScorer.RegisterDefaultLanguagePattern(pattern);
    }

    private static string? LoadLangPattern()
    {
        try
        {
            var rulesPath = ResolveRulesJsonPath();
            if (string.IsNullOrWhiteSpace(rulesPath) || !File.Exists(rulesPath))
                return null;

            using var doc = JsonDocument.Parse(File.ReadAllText(rulesPath));
            var root = doc.RootElement;

            if (!root.TryGetProperty("LangPattern", out var langPattern) || langPattern.ValueKind != JsonValueKind.String)
                return null;

            var pattern = langPattern.GetString();
            return string.IsNullOrWhiteSpace(pattern) ? null : pattern;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
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
}
