using System.IO;
using System.Text.Json;
using RomCleanup.UI.Wpf.ViewModels;

namespace RomCleanup.UI.Wpf.Services;

/// <summary>
/// Persistence service for user settings.
/// Loads/saves settings from %APPDATA%\RomCleanupRegionDedupe\settings.json.
/// Port of Settings.ps1 persistence logic.
/// </summary>
public sealed class SettingsService
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RomCleanupRegionDedupe");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    /// <summary>Load settings from disk into the ViewModel.</summary>
    public void LoadInto(MainViewModel vm)
    {
        if (!File.Exists(SettingsPath)) return;

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("general", out var general))
            {
                vm.LogLevel = GetString(general, "logLevel", "Info");
                vm.AggressiveJunk = GetBool(general, "aggressiveJunk");
                vm.AliasKeying = GetBool(general, "aliasEditionKeying");

                if (general.TryGetProperty("preferredRegions", out var regions) &&
                    regions.ValueKind == JsonValueKind.Array)
                {
                    vm.PreferEU = false; vm.PreferUS = false; vm.PreferJP = false; vm.PreferWORLD = false;
                    vm.PreferDE = false; vm.PreferFR = false; vm.PreferIT = false; vm.PreferES = false;
                    vm.PreferAU = false; vm.PreferASIA = false; vm.PreferKR = false; vm.PreferCN = false;
                    vm.PreferBR = false; vm.PreferNL = false; vm.PreferSE = false; vm.PreferSCAN = false;
                    foreach (var r in regions.EnumerateArray())
                    {
                        switch (r.GetString()?.ToUpperInvariant())
                        {
                            case "EU": vm.PreferEU = true; break;
                            case "US": vm.PreferUS = true; break;
                            case "JP": vm.PreferJP = true; break;
                            case "WORLD": vm.PreferWORLD = true; break;
                            case "DE": vm.PreferDE = true; break;
                            case "FR": vm.PreferFR = true; break;
                            case "IT": vm.PreferIT = true; break;
                            case "ES": vm.PreferES = true; break;
                            case "AU": vm.PreferAU = true; break;
                            case "ASIA": vm.PreferASIA = true; break;
                            case "KR": vm.PreferKR = true; break;
                            case "CN": vm.PreferCN = true; break;
                            case "BR": vm.PreferBR = true; break;
                            case "NL": vm.PreferNL = true; break;
                            case "SE": vm.PreferSE = true; break;
                            case "SCAN": vm.PreferSCAN = true; break;
                        }
                    }
                }
            }

            if (root.TryGetProperty("toolPaths", out var tools))
            {
                vm.ToolChdman = GetString(tools, "chdman");
                vm.Tool7z = GetString(tools, "7z");
                vm.ToolDolphin = GetString(tools, "dolphintool");
                vm.ToolPsxtract = GetString(tools, "psxtract");
                vm.ToolCiso = GetString(tools, "ciso");
            }

            if (root.TryGetProperty("dat", out var dat))
            {
                vm.UseDat = GetBool(dat, "useDat");
                vm.DatRoot = GetString(dat, "datRoot");
                vm.DatHashType = GetString(dat, "hashType", "SHA1");
                vm.DatFallback = GetBool(dat, "datFallback", true);
            }

            if (root.TryGetProperty("paths", out var paths))
            {
                vm.TrashRoot = GetString(paths, "trashRoot");
                vm.AuditRoot = GetString(paths, "auditRoot");
                vm.Ps3DupesRoot = GetString(paths, "ps3DupesRoot");
            }

            if (root.TryGetProperty("roots", out var roots) &&
                roots.ValueKind == JsonValueKind.Array)
            {
                vm.Roots.Clear();
                foreach (var r in roots.EnumerateArray())
                {
                    var path = r.GetString();
                    if (!string.IsNullOrWhiteSpace(path))
                        vm.Roots.Add(path);
                }
            }
        }
        catch
        {
            // Settings corrupted — continue with defaults
        }
    }

    /// <summary>Save current ViewModel state to disk.</summary>
    public void SaveFrom(MainViewModel vm)
    {
        Directory.CreateDirectory(SettingsDir);

        var settings = new
        {
            general = new
            {
                logLevel = vm.LogLevel,
                preferredRegions = vm.GetPreferredRegions(),
                aggressiveJunk = vm.AggressiveJunk,
                aliasEditionKeying = vm.AliasKeying
            },
            toolPaths = new Dictionary<string, string>
            {
                ["chdman"] = vm.ToolChdman,
                ["dolphintool"] = vm.ToolDolphin,
                ["7z"] = vm.Tool7z,
                ["psxtract"] = vm.ToolPsxtract,
                ["ciso"] = vm.ToolCiso
            },
            dat = new
            {
                useDat = vm.UseDat,
                datRoot = vm.DatRoot,
                hashType = vm.DatHashType,
                datFallback = vm.DatFallback
            },
            paths = new
            {
                trashRoot = vm.TrashRoot,
                auditRoot = vm.AuditRoot,
                ps3DupesRoot = vm.Ps3DupesRoot
            },
            roots = vm.Roots.ToArray()
        };

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }

    private static string GetString(JsonElement el, string prop, string fallback = "")
    {
        return el.TryGetProperty(prop, out var val) && val.ValueKind == JsonValueKind.String
            ? val.GetString() ?? fallback : fallback;
    }

    private static bool GetBool(JsonElement el, string prop, bool fallback = false)
    {
        return el.TryGetProperty(prop, out var val) && val.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? val.GetBoolean() : fallback;
    }
}
