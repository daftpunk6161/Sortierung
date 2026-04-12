using System.Text.Json;
using System.Diagnostics;
using Romulus.Core.Scoring;

namespace Romulus.Infrastructure.Orchestration;

public static class FormatScoringProfile
{
    private static readonly Lazy<FormatScoreData> _lazy = new(Load);

    public static void EnsureRegistered()
    {
        FormatScorer.RegisterScoreFactory(() =>
        {
            var data = _lazy.Value;
            return (data.FormatScores, data.SetTypeScores, data.DiscExtensions);
        });

        var loaded = _lazy.Value;
        if (loaded.FormatScores.Count > 0)
            FormatScorer.RegisterScoreProfile(loaded.FormatScores, loaded.SetTypeScores, loaded.DiscExtensions);
    }

    private static FormatScoreData Load()
    {
        try
        {
            var filePath = ResolveFormatScoresPath();
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                Trace.WriteLine("[FormatScoringProfile] format-scores.json missing; falling back to built-in FormatScorer defaults.");
                return FormatScoreData.Empty;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(filePath));
            var root = doc.RootElement;

            var formatScores = ParseScoreObject(root, "formatScores");
            var setTypeScores = ParseScoreObject(root, "setTypeScores");
            var discExtensions = ParseStringArray(root, "discExtensions");

            if (formatScores.Count == 0)
            {
                Trace.WriteLine("[FormatScoringProfile] formatScores is empty; falling back to built-in FormatScorer defaults.");
                return FormatScoreData.Empty;
            }

            return new FormatScoreData(formatScores, setTypeScores, discExtensions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Trace.WriteLine($"[FormatScoringProfile] Failed to load format-scores.json: {ex.Message}. Falling back to built-in FormatScorer defaults.");
            return FormatScoreData.Empty;
        }
    }

    private static IReadOnlyDictionary<string, int> ParseScoreObject(JsonElement root, string propertyName)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Object)
            return result;

        foreach (var prop in value.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Number)
                continue;

            if (prop.Value.TryGetInt32(out var score))
                result[prop.Name] = score;
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

    private static string? ResolveFormatScoresPath()
    {
        var envDataDir = Environment.GetEnvironmentVariable("ROMULUS_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(envDataDir))
        {
            var envPath = Path.Combine(envDataDir, "format-scores.json");
            if (File.Exists(envPath))
                return envPath;
        }

        static string? Walk(string? start)
        {
            if (string.IsNullOrWhiteSpace(start))
                return null;

            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "data", "format-scores.json");
                if (File.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }

            return null;
        }

        return Walk(AppContext.BaseDirectory) ?? Walk(Directory.GetCurrentDirectory());
    }

    private sealed record FormatScoreData(
        IReadOnlyDictionary<string, int> FormatScores,
        IReadOnlyDictionary<string, int> SetTypeScores,
        IReadOnlyCollection<string> DiscExtensions)
    {
        public static FormatScoreData Empty { get; } = new(
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            Array.Empty<string>());
    }
}
