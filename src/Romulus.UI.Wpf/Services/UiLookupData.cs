using System.IO;
using System.Diagnostics;
using System.Text.Json;

namespace Romulus.UI.Wpf.Services;

/// <summary>
/// GUI-041: Loads data-driven lookup tables from data/ui-lookups.json.
/// Falls back to empty collections if the file is missing.
/// </summary>
public sealed class UiLookupData
{
    public Dictionary<string, double> CompressionRatios { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string[]> ConsoleFormatPriority { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<GenreKeywordEntry> GenreKeywords { get; init; } = [];
    public Dictionary<string, string> CoreMapping { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> SortTemplates { get; init; } = [];
    public Dictionary<string, string> ExtensionTargetFormats { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public static UiLookupData Load()
    {
        var dataDir = FeatureService.ResolveDataDirectory()
                      ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
        var path = Path.Combine(dataDir, "ui-lookups.json");

        UiLookupData result;
        if (!File.Exists(path))
        {
            Trace.WriteLine("[UiLookupData] ui-lookups.json missing; using built-in defaults.");
            result = new UiLookupData();
        }
        else
        {
            try
            {
                var json = File.ReadAllText(path);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                result = JsonSerializer.Deserialize<UiLookupData>(json, options) ?? new UiLookupData();
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                Trace.WriteLine($"[UiLookupData] Failed to load ui-lookups.json: {ex.Message}. Using built-in defaults.");
                result = new UiLookupData();
            }
        }

        PopulateDefaults(result);
        return result;
    }

    private static void PopulateDefaults(UiLookupData data)
    {
        if (data.CompressionRatios.Count == 0)
        {
            foreach (var kv in LoadCompressionEstimatesFromRegistry())
                data.CompressionRatios[kv.Key] = kv.Value;
        }

        if (data.CompressionRatios.Count == 0)
        {
            var fallback = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["bin_chd"] = 0.50, ["cue_chd"] = 0.50, ["iso_chd"] = 0.60,
                ["iso_rvz"] = 0.40, ["gcz_rvz"] = 0.70, ["zip_7z"] = 0.90,
                ["rar_7z"] = 0.95, ["cso_chd"] = 0.80, ["pbp_chd"] = 0.70,
                ["iso_cso"] = 0.65, ["wbfs_rvz"] = 0.45, ["nkit_rvz"] = 0.50
            };
            foreach (var kv in fallback) data.CompressionRatios[kv.Key] = kv.Value;
        }

        if (data.ExtensionTargetFormats.Count == 0)
        {
            var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["bin"] = "chd", ["cue"] = "chd", ["iso"] = "chd", ["cso"] = "chd", ["pbp"] = "chd",
                ["gcz"] = "rvz", ["wbfs"] = "rvz", ["nkit"] = "rvz",
                ["zip"] = "7z", ["rar"] = "7z"
            };
            foreach (var kv in defaults) data.ExtensionTargetFormats[kv.Key] = kv.Value;
        }

        if (data.ConsoleFormatPriority.Count == 0)
        {
            data.ConsoleFormatPriority["ps1"] = ["chd", "bin/cue", "pbp", "cso", "iso"];
            data.ConsoleFormatPriority["ps2"] = ["chd", "iso"];
            data.ConsoleFormatPriority["psp"] = ["chd", "pbp", "cso", "iso"];
            data.ConsoleFormatPriority["dreamcast"] = ["chd", "gdi", "cdi"];
            data.ConsoleFormatPriority["saturn"] = ["chd", "bin/cue", "iso"];
            data.ConsoleFormatPriority["gc"] = ["rvz", "iso", "nkit", "gcz"];
            data.ConsoleFormatPriority["wii"] = ["rvz", "iso", "nkit", "wbfs"];
            data.ConsoleFormatPriority["nes"] = ["zip", "7z", "nes"];
            data.ConsoleFormatPriority["snes"] = ["zip", "7z", "sfc", "smc"];
            data.ConsoleFormatPriority["gba"] = ["zip", "7z", "gba"];
            data.ConsoleFormatPriority["n64"] = ["zip", "7z", "z64", "v64", "n64"];
            data.ConsoleFormatPriority["gb"] = ["zip", "7z", "gb"];
            data.ConsoleFormatPriority["gbc"] = ["zip", "7z", "gbc"];
            data.ConsoleFormatPriority["nds"] = ["zip", "7z", "nds"];
            data.ConsoleFormatPriority["3ds"] = ["zip", "7z", "3ds"];
            data.ConsoleFormatPriority["genesis"] = ["zip", "7z", "md", "gen"];
            data.ConsoleFormatPriority["arcade"] = ["zip", "7z"];
        }
    }

    private static Dictionary<string, double> LoadCompressionEstimatesFromRegistry()
    {
        var estimates = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var dataDir = FeatureService.ResolveDataDirectory()
                      ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
        var registryPath = Path.Combine(dataDir, "conversion-registry.json");
        if (!File.Exists(registryPath))
            return estimates;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(registryPath));
            if (!doc.RootElement.TryGetProperty("compressionEstimates", out var element)
                || element.ValueKind != JsonValueKind.Object)
            {
                return estimates;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Number
                    && property.Value.TryGetDouble(out var value))
                {
                    estimates[property.Name] = value;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"[UiLookupData] Failed to load compression estimates from conversion-registry.json: {ex.Message}.");
        }

        return estimates;
    }

    private static readonly Lazy<UiLookupData> _instance = new(Load);
    public static UiLookupData Instance => _instance.Value;
}

public sealed record GenreKeywordEntry(string Keyword, string Genre);
