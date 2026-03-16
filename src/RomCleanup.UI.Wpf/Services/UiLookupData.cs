using System.IO;
using System.Text.Json;

namespace RomCleanup.UI.Wpf.Services;

/// <summary>
/// GUI-041: Loads data-driven lookup tables from data/ui-lookups.json.
/// Falls back to empty collections if the file is missing.
/// </summary>
public sealed class UiLookupData
{
    public Dictionary<string, double> CompressionRatios { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string[]> ConsoleFormatPriority { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Dictionary<string, string>> EmulatorMatrix { get; init; } = [];
    public List<GenreKeywordEntry> GenreKeywords { get; init; } = [];
    public Dictionary<string, string> CoreMapping { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> SortTemplates { get; init; } = [];

    public static UiLookupData Load()
    {
        var dataDir = FeatureService.ResolveDataDirectory()
                      ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
        var path = Path.Combine(dataDir, "ui-lookups.json");

        if (!File.Exists(path))
            return new UiLookupData();

        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<UiLookupData>(json, options) ?? new UiLookupData();
    }

    private static readonly Lazy<UiLookupData> _instance = new(Load);
    public static UiLookupData Instance => _instance.Value;
}

public sealed record GenreKeywordEntry(string Keyword, string Genre);
