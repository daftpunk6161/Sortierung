using System.IO;
using System.Text.Json;
using Romulus.Infrastructure.Paths;
using Romulus.Infrastructure.Safety;

namespace Romulus.UI.Wpf.Services;

/// <summary>
/// Manages profile CRUD: delete, import, export, config-diff.
/// Extracted from MainWindow.xaml.cs (RF-008).
/// </summary>
public sealed class ProfileService
{
    private static readonly string SettingsDir =
        AppStoragePathResolver.ResolveRoamingAppDirectory();

    private static string SettingsPath => Path.Combine(SettingsDir, "settings.json");

    /// <summary>Delete the saved profile. Returns true if deleted.</summary>
    public static bool Delete()
    {
        if (!File.Exists(SettingsPath)) return false;

        // Bounded retry for transient file locks (parallel tests, autosave race).
        // Deterministic behavior: max 3 retries, then fail with false.
        const int maxAttempts = 4;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                File.Delete(SettingsPath);
                return true;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(25 * attempt);
            }
            catch (UnauthorizedAccessException) when (attempt < maxAttempts)
            {
                Thread.Sleep(25 * attempt);
            }
        }

        return !File.Exists(SettingsPath);
    }

    /// <summary>Import a JSON profile from the given path. Creates backup of existing.</summary>
    public static void Import(string sourcePath)
    {
        var json = File.ReadAllText(sourcePath);
        // V2-BUG-L04: Validate JSON structure by deserializing, not just parsing syntax
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Profile must be a JSON object.");

        // GUI-059: Validate required top-level properties from settings schema
        var root = doc.RootElement;
        string[] requiredProperties = ["general", "toolPaths", "dat"];
        foreach (var prop in requiredProperties)
        {
            if (!root.TryGetProperty(prop, out _))
                throw new InvalidOperationException($"Profile is missing required section: \"{prop}\".");
        }

        Directory.CreateDirectory(SettingsDir);
        if (File.Exists(SettingsPath))
        {
            var backupPath = SettingsPath + $".{DateTime.UtcNow:yyyyMMddHHmmss}.bak";
            File.Copy(SettingsPath, backupPath, overwrite: false);
        }

        File.Copy(sourcePath, SettingsPath, overwrite: true);
    }

    /// <summary>Export current config map to a JSON file.</summary>
    public static void Export(string targetPath, Dictionary<string, string> configMap)
    {
        var safeTargetPath = SafetyValidator.EnsureSafeOutputPath(targetPath);
        File.WriteAllText(safeTargetPath, JsonSerializer.Serialize(configMap, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Get the saved config as a flattened key-value dictionary. Returns null if not found.</summary>
    public static Dictionary<string, string>? LoadSavedConfigFlat()
    {
        if (!File.Exists(SettingsPath)) return null;
        var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
        var result = new Dictionary<string, string>();
        FlattenJson(doc.RootElement, "", result);
        return result;
    }

    private static void FlattenJson(JsonElement element, string prefix, Dictionary<string, string> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                    FlattenJson(prop.Value, string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}", result);
                break;
            case JsonValueKind.Array:
                int i = 0;
                foreach (var item in element.EnumerateArray())
                    FlattenJson(item, $"{prefix}[{i++}]", result);
                break;
            default:
                result[prefix] = element.ToString();
                break;
        }
    }
}
