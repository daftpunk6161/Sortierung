using System.Text.Json;
using System.Text.Json.Serialization;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Paths;

namespace Romulus.Infrastructure.Configuration;

/// <summary>
/// Loads and merges settings from defaults.json and user settings.json.
/// Port of Settings.ps1 — loads from %APPDATA%\Romulus\settings.json.
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
    /// User settings path: %APPDATA%\Romulus\settings.json
    /// </summary>
    public static string UserSettingsPath => AppStoragePathResolver.ResolveRoamingPath("settings.json");

    /// <summary>
    /// Load settings with fallback chain: user settings → defaults.json → hardcoded defaults.
    /// </summary>
    public static RomulusSettings Load(string? defaultsJsonPath = null, Action<string>? onWarning = null)
    {
        var settings = LoadDefaultsOnly(defaultsJsonPath);

        // Then overlay user settings (higher priority)
        var userPath = UserSettingsPath;
        if (File.Exists(userPath))
        {
            MergeFromUserSettings(settings, userPath, onWarning);
        }

        return settings;
    }

    /// <summary>
    /// Load only repo defaults.json on top of hardcoded fallbacks, without user overlay.
    /// </summary>
    public static RomulusSettings LoadDefaultsOnly(string? defaultsJsonPath = null)
    {
        var settings = new RomulusSettings();
        defaultsJsonPath ??= ResolveDefaultsJsonPath();

        if (defaultsJsonPath is not null && File.Exists(defaultsJsonPath))
            MergeFromDefaults(settings, defaultsJsonPath);

        return settings;
    }

    /// <summary>
    /// Load settings with an explicit user settings path instead of %APPDATA% (TASK-161).
    /// Used by the API to decouple from per-user desktop settings on server deployments.
    /// </summary>
    public static RomulusSettings LoadWithExplicitUserPath(string? defaultsJsonPath, string userSettingsPath, Action<string>? onWarning = null)
    {
        var settings = LoadDefaultsOnly(defaultsJsonPath);

        if (File.Exists(userSettingsPath))
            MergeFromUserSettings(settings, userSettingsPath, onWarning);

        return settings;
    }

    /// <summary>
    /// Resolve data/defaults.json from the current application or workspace layout.
    /// Honors ROMULUS_DATA_DIR environment variable as highest-priority override.
    /// </summary>
    public static string? ResolveDefaultsJsonPath()
    {
        // Highest priority: explicit environment override
        var envOverride = Environment.GetEnvironmentVariable("ROMULUS_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(envOverride))
        {
            var envCandidate = Path.Combine(Path.GetFullPath(envOverride), "defaults.json");
            if (File.Exists(envCandidate))
                return envCandidate;
        }

        var searchRoots = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
        }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(Path.GetFullPath)
        .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var root in searchRoots)
        {
            var current = new DirectoryInfo(root);
            while (current is not null)
            {
                var candidate = Path.Combine(current.FullName, "data", "defaults.json");
                if (File.Exists(candidate))
                    return candidate;

                current = current.Parent;
            }
        }

        return null;
    }

    /// <summary>
    /// Load settings from a specific JSON file (for testing).
    /// </summary>
    public static RomulusSettings LoadFrom(string path)
    {
        return LoadFromSafe(path).Settings;
    }

    /// <summary>
    /// TASK-173: Safe settings load with corruption detection and .bak backup.
    /// Returns a <see cref="SettingsLoadResult"/> with WasCorrupt=true if the file
    /// contained malformed JSON. Creates a .bak backup of the corrupt file.
    /// </summary>
    public static SettingsLoadResult LoadFromSafe(string path)
    {
        if (!File.Exists(path))
            return new SettingsLoadResult(new RomulusSettings());

        try
        {
            var json = SettingsFileAccess.TryReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
                return new SettingsLoadResult(new RomulusSettings());

            // FEAT-05: Validate JSON structure before deserializing
            ValidateSettingsStructure(json);

            var settings = JsonSerializer.Deserialize<RomulusSettings>(json, JsonOptions)
                   ?? new RomulusSettings();
            var validationErrors = RomulusSettingsValidator.Validate(settings);
            if (validationErrors.Count > 0)
            {
                return new SettingsLoadResult(
                    new RomulusSettings(),
                    WasCorrupt: true,
                    CorruptionMessage: string.Join("; ", validationErrors));
            }
            return new SettingsLoadResult(settings);
        }
        catch (JsonException ex)
        {
            // TASK-173: Create .bak backup of corrupt file before resetting
            try
            {
                File.Copy(path, path + ".bak", overwrite: true);
            }
            catch (IOException)
            {
                // Best-effort backup — if it fails, still return defaults
            }

            return new SettingsLoadResult(
                new RomulusSettings(),
                WasCorrupt: true,
                CorruptionMessage: ex.Message);
        }
    }

    /// <summary>
    /// FEAT-05: Validate settings JSON structure against expected schema.
    /// Checks required top-level keys and value types.
    /// </summary>
    public static IReadOnlyList<string> ValidateSettingsStructure(string json)
    {
        var errors = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                errors.Add("Root must be a JSON object");
                return errors;
            }

            // Check known top-level sections
            var allowedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "general", "toolPaths", "dat", "rules", "schemaVersion" };

            foreach (var prop in root.EnumerateObject())
            {
                if (!allowedKeys.Contains(prop.Name))
                    errors.Add($"Unknown top-level key: '{prop.Name}'");
            }

            // Validate section types
            ValidateSectionType(root, "general", JsonValueKind.Object, errors);
            ValidateSectionType(root, "toolPaths", JsonValueKind.Object, errors);
            ValidateSectionType(root, "dat", JsonValueKind.Object, errors);

            // Validate known field types within general
            if (root.TryGetProperty("general", out var general) && general.ValueKind == JsonValueKind.Object)
            {
                ValidateFieldType(general, "logLevel", JsonValueKind.String, errors, "general");
                ValidateFieldType(general, "preferredRegions", JsonValueKind.Array, errors, "general");
                ValidateFieldType(general, "aggressiveJunk", errors, "general", JsonValueKind.True, JsonValueKind.False);
                ValidateFieldType(general, "aliasEditionKeying", errors, "general", JsonValueKind.True, JsonValueKind.False);
            }

            // Validate known field types within dat
            if (root.TryGetProperty("dat", out var dat) && dat.ValueKind == JsonValueKind.Object)
            {
                ValidateFieldType(dat, "useDat", errors, "dat", JsonValueKind.True, JsonValueKind.False);
                ValidateFieldType(dat, "hashType", JsonValueKind.String, errors, "dat");
                ValidateFieldType(dat, "datFallback", errors, "dat", JsonValueKind.True, JsonValueKind.False);
            }
        }
        catch (JsonException ex)
        {
            errors.Add($"Invalid JSON: {ex.Message}");
        }
        return errors;
    }

    private static void ValidateSectionType(JsonElement root, string name, JsonValueKind expected, List<string> errors)
    {
        if (root.TryGetProperty(name, out var section) && section.ValueKind != expected)
            errors.Add($"'{name}' must be {expected}, got {section.ValueKind}");
    }

    private static void ValidateFieldType(JsonElement section, string name, JsonValueKind expected, List<string> errors, string sectionName)
    {
        if (section.TryGetProperty(name, out var field) && field.ValueKind != expected)
            errors.Add($"'{sectionName}.{name}' must be {expected}, got {field.ValueKind}");
    }

    private static void ValidateFieldType(JsonElement section, string name, List<string> errors, string sectionName, params JsonValueKind[] expected)
    {
        if (section.TryGetProperty(name, out var field) && !expected.Contains(field.ValueKind))
            errors.Add($"'{sectionName}.{name}' has unexpected type {field.ValueKind}");
    }

    private static void MergeFromDefaults(RomulusSettings settings, string path)
    {
        try
        {
            var json = SettingsFileAccess.TryReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
                return;

            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            var root = doc.RootElement;

            if (root.TryGetProperty("mode", out var mode))
                settings.General.Mode = ValidateEnum(mode.GetString(), AllowedModes, settings.General.Mode);
            if (root.TryGetProperty("extensions", out var ext))
                settings.General.Extensions = ext.GetString() ?? settings.General.Extensions;
            if (root.TryGetProperty("logLevel", out var ll))
                settings.General.LogLevel = ValidateEnum(ll.GetString(), AllowedLogLevels, settings.General.LogLevel);
            if (root.TryGetProperty("theme", out var theme))
                settings.General.Theme = theme.GetString() ?? "dark";
            if (root.TryGetProperty("locale", out var locale))
                settings.General.Locale = locale.GetString() ?? "de";
            if (root.TryGetProperty("preferredRegions", out var preferredRegions) && preferredRegions.ValueKind == JsonValueKind.Array)
            {
                var parsedRegions = preferredRegions
                    .EnumerateArray()
                    .Select(value => value.GetString())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!.Trim().ToUpperInvariant())
                    .ToList();

                if (parsedRegions.Count > 0)
                    settings.General.PreferredRegions = parsedRegions;
            }
            if (root.TryGetProperty("datRoot", out var dr))
                settings.Dat.DatRoot = dr.GetString() ?? "";
            if (root.TryGetProperty("useDat", out var useDat) && (useDat.ValueKind is JsonValueKind.True or JsonValueKind.False))
                settings.Dat.UseDat = useDat.GetBoolean();
            if (root.TryGetProperty("hashType", out var hashType))
                settings.Dat.HashType = ValidateEnum(hashType.GetString(), AllowedHashTypes, settings.Dat.HashType);
            if (root.TryGetProperty("datFallback", out var datFallback) && (datFallback.ValueKind is JsonValueKind.True or JsonValueKind.False))
                settings.Dat.DatFallback = datFallback.GetBoolean();
        }
        catch (JsonException)
        {
            // Malformed defaults — use hardcoded defaults
        }
    }

    private static void MergeFromUserSettings(RomulusSettings settings, string path, Action<string>? onWarning = null)
    {
        try
        {
            var json = SettingsFileAccess.TryReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
                return;

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
                    settings.ToolPaths.Chdman = ToolPathValidator.ValidateOrEmpty(user.ToolPaths.Chdman);
                if (!string.IsNullOrEmpty(user.ToolPaths.SevenZip))
                    settings.ToolPaths.SevenZip = ToolPathValidator.ValidateOrEmpty(user.ToolPaths.SevenZip);
                if (!string.IsNullOrEmpty(user.ToolPaths.DolphinTool))
                    settings.ToolPaths.DolphinTool = ToolPathValidator.ValidateOrEmpty(user.ToolPaths.DolphinTool);
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

            var validationErrors = RomulusSettingsValidator.Validate(settings);
            if (validationErrors.Count > 0)
            {
                // Fail closed to sane defaults if merged settings become invalid.
                settings.General = new GeneralSettings();
                settings.Dat = new DatSettings();
                onWarning?.Invoke($"[Warning] User settings '{path}' produced invalid values; reverted to safe defaults.");
            }
        }
        catch (JsonException ex)
        {
            TryBackupCorruptUserSettings(path);
            onWarning?.Invoke($"[Warning] Corrupt user settings '{path}' ignored: {ex.Message}");
        }
    }

    private static void TryBackupCorruptUserSettings(string path)
    {
        try
        {
            File.Copy(path, path + ".bak", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort backup.
        }
    }

    // V2-H06: Allowed enum values for settings validation
    private static readonly HashSet<string> AllowedLogLevels = new(StringComparer.OrdinalIgnoreCase)
        { "Debug", "Info", "Warning", "Error" };

    private static readonly HashSet<string> AllowedModes = new(StringComparer.OrdinalIgnoreCase)
        { RunConstants.ModeDryRun, RunConstants.ModeMove };

    private static readonly HashSet<string> AllowedHashTypes = new(StringComparer.OrdinalIgnoreCase)
        { "SHA1", "SHA256", "MD5", "CRC32" };

    /// <summary>V2-H06: Returns value if it is in allowedValues, otherwise returns fallback.</summary>
    private static string ValidateEnum(string? value, HashSet<string> allowedValues, string fallback)
        => !string.IsNullOrWhiteSpace(value) && allowedValues.Contains(value) ? value : fallback;

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
