using System.IO;
using System.Text.Json;
using RomCleanup.UI.Wpf.ViewModels;

namespace RomCleanup.UI.Wpf.Services;

/// <summary>
/// Persistence service for user settings.
/// Loads/saves settings from %APPDATA%\RomCleanupRegionDedupe\settings.json.
/// Port of Settings.ps1 persistence logic.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    /// <summary>Current settings schema version. Increment when breaking changes are made.</summary>
    private const int CurrentVersion = 1;

    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RomCleanupRegionDedupe");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    /// <summary>Last audit path loaded from settings (for rollback after restart).</summary>
    public string? LastAuditPath { get; private set; }

    /// <summary>Theme name loaded from settings (Dark/Light/HighContrast).</summary>
    public string LastTheme { get; private set; } = "Dark";

    /// <summary>RF-010: Load settings from disk as DTO (decoupled from ViewModel).</summary>
    public SettingsDto? Load()
    {
        if (!File.Exists(SettingsPath)) return null;

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // GUI-060: Check schema version and migrate if needed
            var fileVersion = 0;
            if (root.TryGetProperty("version", out var versionEl) && versionEl.ValueKind == JsonValueKind.Number)
                fileVersion = versionEl.GetInt32();

            if (fileVersion > CurrentVersion)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[WARN] Settings version {fileVersion} is newer than supported {CurrentVersion}. Using defaults for unknown fields.");
            }

            var dto = new SettingsDto();

            if (root.TryGetProperty("general", out var general))
            {
                dto = dto with
                {
                    LogLevel = GetString(general, "logLevel", "Info"),
                    AggressiveJunk = GetBool(general, "aggressiveJunk"),
                    AliasKeying = GetBool(general, "aliasEditionKeying")
                };

                if (general.TryGetProperty("preferredRegions", out var regions) &&
                    regions.ValueKind == JsonValueKind.Array)
                {
                    var regionList = new List<string>();
                    foreach (var r in regions.EnumerateArray())
                    {
                        var val = r.GetString();
                        if (!string.IsNullOrWhiteSpace(val))
                            regionList.Add(val.ToUpperInvariant());
                    }
                    dto = dto with { PreferredRegions = [.. regionList] };
                }
            }

            if (root.TryGetProperty("toolPaths", out var tools))
            {
                dto = dto with
                {
                    ToolChdman = GetString(tools, "chdman"),
                    Tool7z = GetString(tools, "7z"),
                    ToolDolphin = GetString(tools, "dolphintool"),
                    ToolPsxtract = GetString(tools, "psxtract"),
                    ToolCiso = GetString(tools, "ciso")
                };
            }

            if (root.TryGetProperty("dat", out var dat))
            {
                dto = dto with
                {
                    UseDat = GetBool(dat, "useDat"),
                    DatRoot = GetString(dat, "datRoot"),
                    DatHashType = GetString(dat, "hashType", "SHA1"),
                    DatFallback = GetBool(dat, "datFallback", true)
                };
            }

            if (root.TryGetProperty("paths", out var paths))
            {
                dto = dto with
                {
                    TrashRoot = GetString(paths, "trashRoot"),
                    AuditRoot = GetString(paths, "auditRoot"),
                    Ps3DupesRoot = GetString(paths, "ps3DupesRoot"),
                    LastAuditPath = GetString(paths, "lastAuditPath")
                };
            }

            if (root.TryGetProperty("ui", out var ui))
            {
                var cp = Models.ConflictPolicy.Rename;
                if (ui.TryGetProperty("conflictPolicy", out var cpEl) && cpEl.ValueKind == JsonValueKind.String)
                {
                    if (!Enum.TryParse(cpEl.GetString(), true, out cp))
                        System.Diagnostics.Debug.WriteLine($"[WARN] Unknown ConflictPolicy '{cpEl.GetString()}', using default Rename");
                }

                dto = dto with
                {
                    SortConsole = GetBool(ui, "sortConsole"),
                    DryRun = GetBool(ui, "dryRun", true),
                    ConvertEnabled = GetBool(ui, "convertEnabled"),
                    ConfirmMove = GetBool(ui, "confirmMove", true),
                    ConflictPolicy = cp,
                    Theme = GetString(ui, "theme", "Dark")
                };
            }

            if (root.TryGetProperty("roots", out var roots) &&
                roots.ValueKind == JsonValueKind.Array)
            {
                var rootList = new List<string>();
                foreach (var r in roots.EnumerateArray())
                {
                    var path = r.GetString();
                    if (!string.IsNullOrWhiteSpace(path))
                        rootList.Add(path);
                }
                dto = dto with { Roots = [.. rootList] };
            }

            // Update service-level state
            LastAuditPath = dto.LastAuditPath;
            LastTheme = dto.Theme;

            return dto;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return null;
        }
    }

    /// <summary>Load settings from disk into the ViewModel (delegates to Load).</summary>
    public void LoadInto(MainViewModel vm)
    {
        var dto = Load();
        if (dto is null) return;

        ApplyToViewModel(vm, dto);
    }

    /// <summary>Apply a SettingsDto to the ViewModel properties.</summary>
    public static void ApplyToViewModel(MainViewModel vm, SettingsDto dto)
    {
        vm.LogLevel = dto.LogLevel;
        vm.AggressiveJunk = dto.AggressiveJunk;
        vm.AliasKeying = dto.AliasKeying;

        // Region preferences
        var regions = new HashSet<string>(dto.PreferredRegions, StringComparer.OrdinalIgnoreCase);
        vm.PreferEU = regions.Contains("EU");
        vm.PreferUS = regions.Contains("US");
        vm.PreferJP = regions.Contains("JP");
        vm.PreferWORLD = regions.Contains("WORLD");
        vm.PreferDE = regions.Contains("DE");
        vm.PreferFR = regions.Contains("FR");
        vm.PreferIT = regions.Contains("IT");
        vm.PreferES = regions.Contains("ES");
        vm.PreferAU = regions.Contains("AU");
        vm.PreferASIA = regions.Contains("ASIA");
        vm.PreferKR = regions.Contains("KR");
        vm.PreferCN = regions.Contains("CN");
        vm.PreferBR = regions.Contains("BR");
        vm.PreferNL = regions.Contains("NL");
        vm.PreferSE = regions.Contains("SE");
        vm.PreferSCAN = regions.Contains("SCAN");

        // Tool paths
        vm.ToolChdman = dto.ToolChdman;
        vm.Tool7z = dto.Tool7z;
        vm.ToolDolphin = dto.ToolDolphin;
        vm.ToolPsxtract = dto.ToolPsxtract;
        vm.ToolCiso = dto.ToolCiso;

        // DAT
        vm.UseDat = dto.UseDat;
        vm.DatRoot = dto.DatRoot;
        vm.DatHashType = dto.DatHashType;
        vm.DatFallback = dto.DatFallback;

        // Paths
        vm.TrashRoot = dto.TrashRoot;
        vm.AuditRoot = dto.AuditRoot;
        vm.Ps3DupesRoot = dto.Ps3DupesRoot;

        // UI
        vm.SortConsole = dto.SortConsole;
        vm.DryRun = dto.DryRun;
        vm.ConvertEnabled = dto.ConvertEnabled;
        vm.ConfirmMove = dto.ConfirmMove;
        vm.ConflictPolicy = dto.ConflictPolicy;

        // Roots
        vm.Roots.Clear();
        foreach (var r in dto.Roots)
            vm.Roots.Add(r);
    }

    /// <summary>Save current ViewModel state to disk.</summary>
    public bool SaveFrom(MainViewModel vm, string? lastAuditPath = null)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);

            var settings = new
            {
                version = CurrentVersion,
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
                    ps3DupesRoot = vm.Ps3DupesRoot,
                    lastAuditPath = lastAuditPath ?? ""
                },
                roots = vm.Roots.ToArray(),
                ui = new
                {
                    sortConsole = vm.SortConsole,
                    dryRun = vm.DryRun,
                    convertEnabled = vm.ConvertEnabled,
                    confirmMove = vm.ConfirmMove,
                    conflictPolicy = vm.ConflictPolicy.ToString(),
                    theme = vm.CurrentThemeName
                }
            };

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            var tmpPath = SettingsPath + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, SettingsPath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Clean up temp file on failure
            try { File.Delete(SettingsPath + ".tmp"); } catch { /* best effort */ }
            return false;
        }
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
