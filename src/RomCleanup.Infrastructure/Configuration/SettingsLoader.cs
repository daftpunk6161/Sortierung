using System.Text.Json;
using System.Text.Json.Serialization;
using RomCleanup.Contracts.Models;

namespace RomCleanup.Infrastructure.Configuration;

/// <summary>
/// Loads and merges settings from defaults.json and user settings.json.
/// Port of Settings.ps1 — loads from %APPDATA%\RomCleanupRegionDedupe\settings.json.
/// Falls back to data/defaults.json for missing values.
/// </summary>
public sealed class SettingsLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// User settings path: %APPDATA%\RomCleanupRegionDedupe\settings.json
    /// </summary>
    public static string UserSettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RomCleanupRegionDedupe",
            "settings.json");

    /// <summary>
    /// Load settings with fallback chain: user settings → defaults.json → hardcoded defaults.
    /// </summary>
    public static RomCleanupSettings Load(string? defaultsJsonPath = null)
    {
        var settings = new RomCleanupSettings();

        // Try loading defaults.json first
        if (defaultsJsonPath is not null && File.Exists(defaultsJsonPath))
        {
            MergeFromDefaults(settings, defaultsJsonPath);
        }

        // Then overlay user settings (higher priority)
        var userPath = UserSettingsPath;
        if (File.Exists(userPath))
        {
            MergeFromUserSettings(settings, userPath);
        }

        return settings;
    }

    /// <summary>
    /// Load settings from a specific JSON file (for testing).
    /// </summary>
    public static RomCleanupSettings LoadFrom(string path)
    {
        if (!File.Exists(path))
            return new RomCleanupSettings();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<RomCleanupSettings>(json, JsonOptions)
                   ?? new RomCleanupSettings();
        }
        catch (JsonException)
        {
            return new RomCleanupSettings();
        }
    }

    private static void MergeFromDefaults(RomCleanupSettings settings, string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            var root = doc.RootElement;

            if (root.TryGetProperty("mode", out var mode))
                settings.General.Mode = mode.GetString() ?? "DryRun";
            if (root.TryGetProperty("extensions", out var ext))
                settings.General.Extensions = ext.GetString() ?? settings.General.Extensions;
            if (root.TryGetProperty("logLevel", out var ll))
                settings.General.LogLevel = ll.GetString() ?? "Info";
            if (root.TryGetProperty("theme", out var theme))
                settings.General.Theme = theme.GetString() ?? "dark";
            if (root.TryGetProperty("locale", out var locale))
                settings.General.Locale = locale.GetString() ?? "de";
            if (root.TryGetProperty("datRoot", out var dr))
                settings.Dat.DatRoot = dr.GetString() ?? "";
        }
        catch (JsonException)
        {
            // Malformed defaults — use hardcoded defaults
        }
    }

    private static void MergeFromUserSettings(RomCleanupSettings settings, string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            // P1-BUG-033: Deserialize into nullable model so missing keys stay null
            // instead of defaulting to false and overwriting the actual defaults.
            var user = JsonSerializer.Deserialize<NullableUserSettings>(json, JsonOptions);
            if (user is null) return;

            if (user.General is not null)
            {
                if (user.General.PreferredRegions is { Count: > 0 })
                    settings.General.PreferredRegions = user.General.PreferredRegions;
                if (!string.IsNullOrEmpty(user.General.LogLevel))
                    settings.General.LogLevel = ValidateEnum(user.General.LogLevel, AllowedLogLevels, settings.General.LogLevel);
                if (!string.IsNullOrEmpty(user.General.Mode))
                    settings.General.Mode = ValidateEnum(user.General.Mode, AllowedModes, settings.General.Mode);

                if (user.General.AggressiveJunk.HasValue)
                    settings.General.AggressiveJunk = user.General.AggressiveJunk.Value;
                if (user.General.AliasEditionKeying.HasValue)
                    settings.General.AliasEditionKeying = user.General.AliasEditionKeying.Value;
                if (!string.IsNullOrEmpty(user.General.Extensions))
                    settings.General.Extensions = user.General.Extensions;
                if (!string.IsNullOrEmpty(user.General.Theme))
                    settings.General.Theme = user.General.Theme;
                if (!string.IsNullOrEmpty(user.General.Locale))
                    settings.General.Locale = user.General.Locale;
            }

            if (user.ToolPaths is not null)
            {
                if (!string.IsNullOrEmpty(user.ToolPaths.Chdman))
                    settings.ToolPaths.Chdman = ValidateToolPath(user.ToolPaths.Chdman);
                if (!string.IsNullOrEmpty(user.ToolPaths.SevenZip))
                    settings.ToolPaths.SevenZip = ValidateToolPath(user.ToolPaths.SevenZip);
                if (!string.IsNullOrEmpty(user.ToolPaths.DolphinTool))
                    settings.ToolPaths.DolphinTool = ValidateToolPath(user.ToolPaths.DolphinTool);
            }

            if (user.Dat is not null)
            {
                if (user.Dat.UseDat.HasValue)
                    settings.Dat.UseDat = user.Dat.UseDat.Value;
                if (!string.IsNullOrEmpty(user.Dat.DatRoot))
                    settings.Dat.DatRoot = user.Dat.DatRoot;
                if (!string.IsNullOrEmpty(user.Dat.HashType))
                    settings.Dat.HashType = ValidateEnum(user.Dat.HashType, AllowedHashTypes, settings.Dat.HashType);
                if (user.Dat.DatFallback.HasValue)
                    settings.Dat.DatFallback = user.Dat.DatFallback.Value;
            }
        }
        catch (JsonException)
        {
            // Malformed user settings — keep defaults
        }
    }

    // V2-H06: Allowed enum values for settings validation
    private static readonly HashSet<string> AllowedLogLevels = new(StringComparer.OrdinalIgnoreCase)
        { "Debug", "Info", "Warning", "Error" };

    private static readonly HashSet<string> AllowedModes = new(StringComparer.OrdinalIgnoreCase)
        { "DryRun", "Move" };

    private static readonly HashSet<string> AllowedHashTypes = new(StringComparer.OrdinalIgnoreCase)
        { "SHA1", "SHA256", "MD5", "CRC32" };

    private static readonly HashSet<string> AllowedToolExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".bat", ".cmd" };

    /// <summary>V2-H06: Returns value if it is in allowedValues, otherwise returns fallback.</summary>
    private static string ValidateEnum(string value, HashSet<string> allowedValues, string fallback)
        => allowedValues.Contains(value) ? value : fallback;

    private static string ValidateToolPath(string path)
    {
        if (!File.Exists(path))
            return "";

        var ext = Path.GetExtension(path);
        if (!AllowedToolExtensions.Contains(ext))
            return "";

        // Reject tools in system directories to prevent executing system binaries
        var fullPath = Path.GetFullPath(path);
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var sysDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
        if (!string.IsNullOrEmpty(winDir) && fullPath.StartsWith(winDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return "";
        if (!string.IsNullOrEmpty(sysDir) && fullPath.StartsWith(sysDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return "";

        return fullPath;
    }

    // ── P1-BUG-033: Nullable deserialization model ──
    // Separate model with bool? so that missing JSON keys deserialize as null
    // instead of false, allowing us to distinguish "user explicitly set false"
    // from "key was absent in JSON".

    private sealed class NullableUserSettings
    {
        [JsonPropertyName("general")]
        public NullableGeneralSettings? General { get; set; }

        [JsonPropertyName("toolPaths")]
        public ToolPathSettings? ToolPaths { get; set; }

        [JsonPropertyName("dat")]
        public NullableDatSettings? Dat { get; set; }
    }

    private sealed class NullableGeneralSettings
    {
        [JsonPropertyName("logLevel")]
        public string? LogLevel { get; set; }

        [JsonPropertyName("preferredRegions")]
        public List<string>? PreferredRegions { get; set; }

        [JsonPropertyName("aggressiveJunk")]
        public bool? AggressiveJunk { get; set; }

        [JsonPropertyName("aliasEditionKeying")]
        public bool? AliasEditionKeying { get; set; }

        [JsonPropertyName("mode")]
        public string? Mode { get; set; }

        [JsonPropertyName("extensions")]
        public string? Extensions { get; set; }

        [JsonPropertyName("theme")]
        public string? Theme { get; set; }

        [JsonPropertyName("locale")]
        public string? Locale { get; set; }
    }

    private sealed class NullableDatSettings
    {
        [JsonPropertyName("useDat")]
        public bool? UseDat { get; set; }

        [JsonPropertyName("datRoot")]
        public string? DatRoot { get; set; }

        [JsonPropertyName("hashType")]
        public string? HashType { get; set; }

        [JsonPropertyName("datFallback")]
        public bool? DatFallback { get; set; }
    }
}
