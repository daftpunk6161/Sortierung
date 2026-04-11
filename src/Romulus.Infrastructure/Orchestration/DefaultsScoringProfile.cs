using System.Text.Json;
using Romulus.Core.Deduplication;
using Romulus.Core.Scoring;

namespace Romulus.Infrastructure.Orchestration;

public static class DefaultsScoringProfile
{
    private static readonly Lazy<DefaultsScoringData?> _lazy = new(Load);

    public static void EnsureRegistered()
    {
        HealthScorer.RegisterWeightFactory(() => _lazy.Value?.HealthWeights ?? FallbackHealthWeights());
        DeduplicationEngine.RegisterCategoryRankFactory(() => _lazy.Value?.CategoryRanks ?? FallbackCategoryRanks());
        VersionScorer.RegisterMaxVersionSegmentsFactory(() => _lazy.Value?.VersionScoreMaxSegments);

        var loaded = _lazy.Value;
        if (loaded is null)
            return;

        HealthScorer.RegisterWeights(loaded.HealthWeights);
        DeduplicationEngine.RegisterCategoryRanks(loaded.CategoryRanks);
        VersionScorer.RegisterMaxVersionSegments(loaded.VersionScoreMaxSegments);
    }

    private static DefaultsScoringData? Load()
    {
        try
        {
            var defaultsPath = ResolveDefaultsJsonPath();
            if (string.IsNullOrWhiteSpace(defaultsPath) || !File.Exists(defaultsPath))
                return null;

            using var doc = JsonDocument.Parse(File.ReadAllText(defaultsPath));
            var root = doc.RootElement;

            var healthWeights = ParseHealthWeights(root);
            var categoryRanks = ParseCategoryRanks(root);
            var versionScoreMaxSegments = ParseVersionScoreMaxSegments(root);
            if (categoryRanks.Count == 0)
                return null;

            return new DefaultsScoringData(healthWeights, categoryRanks, versionScoreMaxSegments);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static HealthScorer.HealthScoreWeights ParseHealthWeights(JsonElement root)
    {
        if (!root.TryGetProperty("healthScoreWeights", out var weightsElement)
            || weightsElement.ValueKind != JsonValueKind.Object)
        {
            return FallbackHealthWeights();
        }

        double GetNumber(string propertyName, double fallback)
        {
            if (!weightsElement.TryGetProperty(propertyName, out var property)
                || property.ValueKind != JsonValueKind.Number)
            {
                return fallback;
            }

            return property.TryGetDouble(out var value) ? value : fallback;
        }

        var fallbackWeights = FallbackHealthWeights();
        return new HealthScorer.HealthScoreWeights(
            BaseScoreMax: GetNumber("baseScoreMax", fallbackWeights.BaseScoreMax),
            JunkPenaltyCap: GetNumber("junkPenaltyCap", fallbackWeights.JunkPenaltyCap),
            JunkPenaltyPerPercent: GetNumber("junkPenaltyPerPercent", fallbackWeights.JunkPenaltyPerPercent),
            ExtremeJunkThresholdPercent: GetNumber("extremeJunkThresholdPercent", fallbackWeights.ExtremeJunkThresholdPercent),
            ExtremeJunkPenaltyFloor: GetNumber("extremeJunkPenaltyFloor", fallbackWeights.ExtremeJunkPenaltyFloor),
            VerifiedBonusCap: GetNumber("verifiedBonusCap", fallbackWeights.VerifiedBonusCap),
            VerifiedBonusPerPercent: GetNumber("verifiedBonusPerPercent", fallbackWeights.VerifiedBonusPerPercent),
            ErrorPenaltyCap: GetNumber("errorPenaltyCap", fallbackWeights.ErrorPenaltyCap),
            ErrorPenaltyPerError: GetNumber("errorPenaltyPerError", fallbackWeights.ErrorPenaltyPerError));
    }

    private static IReadOnlyDictionary<string, int> ParseCategoryRanks(JsonElement root)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("categoryPriorityRanks", out var ranksElement)
            || ranksElement.ValueKind != JsonValueKind.Object)
        {
            return FallbackCategoryRanks();
        }

        foreach (var property in ranksElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Number)
                continue;

            if (property.Value.TryGetInt32(out var rank))
                result[property.Name] = rank;
        }

        return result.Count == 0 ? FallbackCategoryRanks() : result;
    }

    private static int ParseVersionScoreMaxSegments(JsonElement root)
    {
        if (!root.TryGetProperty("versionScoreMaxSegments", out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var parsed)
            || parsed < 1)
        {
            return 6;
        }

        return parsed;
    }

    private static HealthScorer.HealthScoreWeights FallbackHealthWeights()
        => new(
            BaseScoreMax: 100.0,
            JunkPenaltyCap: 30.0,
            JunkPenaltyPerPercent: 0.3,
            ExtremeJunkThresholdPercent: 90.0,
            ExtremeJunkPenaltyFloor: 70.0,
            VerifiedBonusCap: 10.0,
            VerifiedBonusPerPercent: 0.15,
            ErrorPenaltyCap: 20.0,
            ErrorPenaltyPerError: 2.0);

    private static IReadOnlyDictionary<string, int> FallbackCategoryRanks()
        => new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Game"] = 5,
            ["Bios"] = 4,
            ["NonGame"] = 3,
            ["Junk"] = 2,
            ["Unknown"] = 1
        };

    private static string? ResolveDefaultsJsonPath()
    {
        var envDataDir = Environment.GetEnvironmentVariable("ROMULUS_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(envDataDir))
        {
            var envDefaults = Path.Combine(envDataDir, "defaults.json");
            if (File.Exists(envDefaults))
                return envDefaults;
        }

        static string? Walk(string? start)
        {
            if (string.IsNullOrWhiteSpace(start))
                return null;

            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "data", "defaults.json");
                if (File.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }

            return null;
        }

        return Walk(AppContext.BaseDirectory) ?? Walk(Directory.GetCurrentDirectory());
    }

    private sealed record DefaultsScoringData(
        HealthScorer.HealthScoreWeights HealthWeights,
        IReadOnlyDictionary<string, int> CategoryRanks,
        int VersionScoreMaxSegments);
}
