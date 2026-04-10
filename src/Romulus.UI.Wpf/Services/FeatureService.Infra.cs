using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Paths;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Tools;
using Romulus.Infrastructure.Reporting;

namespace Romulus.UI.Wpf.Services;

public static partial class FeatureService
{

    // ═══ CONFIG DIFF ════════════════════════════════════════════════════
    // Port of ConfigMerge.ps1

    public static List<ConfigDiffEntry> GetConfigDiff(Dictionary<string, string> current, Dictionary<string, string> saved)
    {
        var result = new List<ConfigDiffEntry>();
        var allKeys = current.Keys.Union(saved.Keys).Distinct();
        foreach (var key in allKeys)
        {
            current.TryGetValue(key, out var curVal);
            saved.TryGetValue(key, out var savedVal);
            if (curVal != savedVal)
                result.Add(new ConfigDiffEntry(key, savedVal ?? "(fehlt)", curVal ?? "(fehlt)"));
        }
        return result;
    }


    // ═══ LOCALIZATION ═══════════════════════════════════════════════════
    // Port of Localization.ps1

    public static Dictionary<string, string> LoadLocale(string locale)
    {
        var dataDir = ResolveDataDirectory("i18n");
        if (dataDir is null)
            return new Dictionary<string, string>();

        var path = Path.Combine(dataDir, $"{locale}.json");
        if (!File.Exists(path))
            path = Path.Combine(dataDir, "de.json");
        if (!File.Exists(path))
            return new Dictionary<string, string>();

        try
        {
            var json = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(path));
            if (json is null) return new Dictionary<string, string>();
            return json.Where(kv => kv.Value.ValueKind == JsonValueKind.String)
                       .ToDictionary(kv => kv.Key, kv => kv.Value.GetString() ?? "");
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[FeatureService] Locale load failed for '{locale}': {ex.Message}");
            return new Dictionary<string, string>();
        }
    }


    /// <summary>Resolve the data/ subdirectory, probing from BaseDirectory upward (max 5 levels).</summary>
    internal static string? ResolveDataDirectory(string? subFolder = null)
    {
        var baseDataDir = RunEnvironmentBuilder.TryResolveDataDir();
        if (string.IsNullOrWhiteSpace(baseDataDir))
            return null;

        if (subFolder is null)
            return baseDataDir;

        var resolvedSubFolder = Path.Combine(baseDataDir, subFolder);
        return Directory.Exists(resolvedSubFolder) ? resolvedSubFolder : null;
    }


    // ═══ PORTABLE MODE CHECK ════════════════════════════════════════════
    // Port of PortableMode.ps1
    // NOTE: The marker file ".portable" is checked relative to AppContext.BaseDirectory,
    // which is the directory containing the executable (e.g. bin/Debug/net10.0-windows/).
    // Place ".portable" next to the .exe, NOT the workspace root.

    public static bool IsPortableMode()
    {
        return AppStoragePathResolver.IsPortableMode();
    }


    // ═══ ACCESSIBILITY ══════════════════════════════════════════════════
    // Port of Accessibility.ps1

    public static bool IsHighContrastActive()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Control Panel\Accessibility\HighContrast");
            var flags = key?.GetValue("Flags") as string;
            return flags is not null && int.TryParse(flags, out var f) && (f & 1) != 0;
        }
        catch { return false; }
    }


    // ═══ MOBILE WEB UI ══════════════════════════════════════════════════

    /// <summary>Try to find the API project path.</summary>
    public static string? FindApiProjectPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Romulus.Api", "Romulus.Api.csproj"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "Romulus.Api", "Romulus.Api.csproj")
        };
        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
    }


}
