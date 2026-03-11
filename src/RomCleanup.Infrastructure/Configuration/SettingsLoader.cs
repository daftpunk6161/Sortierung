using System.Text.Json;
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
            var user = JsonSerializer.Deserialize<RomCleanupSettings>(json, JsonOptions);
            if (user is null) return;

            // Overlay non-default values from user settings
            if (user.General is not null)
            {
                if (user.General.PreferredRegions.Count > 0)
                    settings.General.PreferredRegions = user.General.PreferredRegions;
                if (!string.IsNullOrEmpty(user.General.LogLevel))
                    settings.General.LogLevel = user.General.LogLevel;
                if (!string.IsNullOrEmpty(user.General.Mode))
                    settings.General.Mode = user.General.Mode;

                settings.General.AggressiveJunk = user.General.AggressiveJunk;
                settings.General.AliasEditionKeying = user.General.AliasEditionKeying;
            }

            if (user.ToolPaths is not null)
            {
                if (!string.IsNullOrEmpty(user.ToolPaths.Chdman))
                    settings.ToolPaths.Chdman = user.ToolPaths.Chdman;
                if (!string.IsNullOrEmpty(user.ToolPaths.SevenZip))
                    settings.ToolPaths.SevenZip = user.ToolPaths.SevenZip;
                if (!string.IsNullOrEmpty(user.ToolPaths.DolphinTool))
                    settings.ToolPaths.DolphinTool = user.ToolPaths.DolphinTool;
            }

            if (user.Dat is not null)
            {
                settings.Dat.UseDat = user.Dat.UseDat;
                if (!string.IsNullOrEmpty(user.Dat.DatRoot))
                    settings.Dat.DatRoot = user.Dat.DatRoot;
                if (!string.IsNullOrEmpty(user.Dat.HashType))
                    settings.Dat.HashType = user.Dat.HashType;
                settings.Dat.DatFallback = user.Dat.DatFallback;
            }
        }
        catch (JsonException)
        {
            // Malformed user settings — keep defaults
        }
    }
}
