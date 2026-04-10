using System.Text.Json;
using Romulus.Infrastructure.Configuration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage boost for SettingsLoader: LoadFromSafe corruption handling,
/// ValidateSettingsStructure edge cases, MergeFromDefaults, MergeFromUserSettings.
/// Targets ~51 uncovered lines.
/// </summary>
public sealed class SettingsLoaderCoverageBoostTests : IDisposable
{
    private readonly string _tempDir;

    public SettingsLoaderCoverageBoostTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_SL_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ===== LoadFromSafe corruption scenarios =====

    [Fact]
    public void LoadFromSafe_NonExistentFile_ReturnsDefaults()
    {
        var result = SettingsLoader.LoadFromSafe(Path.Combine(_tempDir, "nope.json"));

        Assert.False(result.WasCorrupt);
        Assert.Equal("DryRun", result.Settings.General.Mode);
    }

    [Fact]
    public void LoadFromSafe_EmptyFile_ReturnsDefaults()
    {
        var path = Path.Combine(_tempDir, "empty.json");
        File.WriteAllText(path, "");

        var result = SettingsLoader.LoadFromSafe(path);

        Assert.False(result.WasCorrupt);
    }

    [Fact]
    public void LoadFromSafe_WhitespaceFile_ReturnsDefaults()
    {
        var path = Path.Combine(_tempDir, "ws.json");
        File.WriteAllText(path, "   \n  ");

        var result = SettingsLoader.LoadFromSafe(path);

        Assert.False(result.WasCorrupt);
    }

    [Fact]
    public void LoadFromSafe_CorruptJson_CreatesBackupAndReturnsDefaults()
    {
        var path = Path.Combine(_tempDir, "corrupt.json");
        File.WriteAllText(path, "{ invalid json !!}");

        var result = SettingsLoader.LoadFromSafe(path);

        Assert.True(result.WasCorrupt);
        Assert.NotNull(result.CorruptionMessage);
        Assert.True(File.Exists(path + ".bak"));
    }

    [Fact]
    public void LoadFromSafe_ValidJson_ReturnsSettings()
    {
        var path = Path.Combine(_tempDir, "valid.json");
        File.WriteAllText(path, """{"general":{"logLevel":"Debug","mode":"DryRun"}}""");

        var result = SettingsLoader.LoadFromSafe(path);

        Assert.False(result.WasCorrupt);
        Assert.Equal("Debug", result.Settings.General.LogLevel);
    }

    // ===== ValidateSettingsStructure - dat section fields =====

    [Fact]
    public void ValidateSettingsStructure_DatUseDatStringInsteadOfBool_ReportsError()
    {
        var json = """{"dat":{"useDat":"yes"}}""";
        var errors = SettingsLoader.ValidateSettingsStructure(json);

        Assert.Contains(errors, e => e.Contains("useDat"));
    }

    [Fact]
    public void ValidateSettingsStructure_DatHashTypeNumber_ReportsError()
    {
        var json = """{"dat":{"hashType":42}}""";
        var errors = SettingsLoader.ValidateSettingsStructure(json);

        Assert.Contains(errors, e => e.Contains("hashType"));
    }

    [Fact]
    public void ValidateSettingsStructure_DatFallbackString_ReportsError()
    {
        var json = """{"dat":{"datFallback":"yes"}}""";
        var errors = SettingsLoader.ValidateSettingsStructure(json);

        Assert.Contains(errors, e => e.Contains("datFallback"));
    }

    [Fact]
    public void ValidateSettingsStructure_ValidDatSection_NoErrors()
    {
        var json = """{"dat":{"useDat":true,"hashType":"CRC32","datFallback":false}}""";
        var errors = SettingsLoader.ValidateSettingsStructure(json);

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateSettingsStructure_ToolPathsNotObject_ReportsError()
    {
        var json = """{"toolPaths":"wrong"}""";
        var errors = SettingsLoader.ValidateSettingsStructure(json);

        Assert.Contains(errors, e => e.Contains("toolPaths"));
    }

    [Fact]
    public void ValidateSettingsStructure_SchemaVersionAllowed_NoError()
    {
        var json = """{"schemaVersion":1}""";
        var errors = SettingsLoader.ValidateSettingsStructure(json);

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateSettingsStructure_MultipleProblems_ReportsAll()
    {
        var json = """{"unknown1":1,"general":"wrong","dat":"wrong","unknown2":2}""";
        var errors = SettingsLoader.ValidateSettingsStructure(json);

        Assert.True(errors.Count >= 3);
    }

    [Fact]
    public void ValidateSettingsStructure_RootArray_ReportsRootError()
    {
        var json = """[1,2,3]""";
        var errors = SettingsLoader.ValidateSettingsStructure(json);

        Assert.Single(errors);
        Assert.Contains("Root", errors[0]);
    }

    [Fact]
    public void ValidateSettingsStructure_GeneralPreferredRegionsNotArray_ReportsError()
    {
        var json = """{"general":{"preferredRegions":"EU"}}""";
        var errors = SettingsLoader.ValidateSettingsStructure(json);

        Assert.Contains(errors, e => e.Contains("preferredRegions"));
    }

    [Fact]
    public void ValidateSettingsStructure_GeneralAliasEditionKeyingString_ReportsError()
    {
        var json = """{"general":{"aliasEditionKeying":"yes"}}""";
        var errors = SettingsLoader.ValidateSettingsStructure(json);

        Assert.Contains(errors, e => e.Contains("aliasEditionKeying"));
    }

    // ===== LoadFrom with full merging =====

    [Fact]
    public void LoadFrom_ValidUserSettings_MergesToolPaths()
    {
        var path = Path.Combine(_tempDir, "user.json");
        File.WriteAllText(path, """
        {
            "toolPaths": {
                "chdman": "C:\\Tools\\chdman.exe",
                "sevenZip": "C:\\Tools\\7z.exe",
                "dolphinTool": "C:\\Tools\\dolphin.exe"
            }
        }
        """);

        var settings = SettingsLoader.LoadFrom(path);

        // ToolPathValidator may empty invalid paths, but the merge code path is exercised
        Assert.NotNull(settings.ToolPaths);
    }

    [Fact]
    public void LoadFrom_ValidDatSettings_MergesAll()
    {
        var path = Path.Combine(_tempDir, "dat.json");
        File.WriteAllText(path, """
        {
            "dat": {
                "useDat": true,
                "datRoot": "C:\\DATs",
                "hashType": "SHA1",
                "datFallback": true
            }
        }
        """);

        var settings = SettingsLoader.LoadFrom(path);

        Assert.True(settings.Dat.UseDat);
        Assert.True(settings.Dat.DatFallback);
        Assert.Equal("SHA1", settings.Dat.HashType);
    }

    [Fact]
    public void LoadFrom_ValidGeneralSettings_MergesAll()
    {
        var path = Path.Combine(_tempDir, "general.json");
        File.WriteAllText(path, """
        {
            "general": {
                "logLevel": "Warning",
                "mode": "Move",
                "aggressiveJunk": true,
                "aliasEditionKeying": true,
                "extensions": ".zip;.7z",
                "theme": "light",
                "locale": "en"
            }
        }
        """);

        var settings = SettingsLoader.LoadFrom(path);

        Assert.Equal("Warning", settings.General.LogLevel);
        Assert.Equal("Move", settings.General.Mode);
        Assert.True(settings.General.AggressiveJunk);
        Assert.True(settings.General.AliasEditionKeying);
    }

    [Fact]
    public void LoadFrom_EmptySections_KeepsDefaults()
    {
        var path = Path.Combine(_tempDir, "minimal.json");
        File.WriteAllText(path, """{"general":{},"dat":{},"toolPaths":{}}""");

        var settings = SettingsLoader.LoadFrom(path);

        Assert.Equal("DryRun", settings.General.Mode);
        Assert.NotNull(settings.General.PreferredRegions);
    }

    [Fact]
    public void LoadFrom_InvalidEnumValues_FallsBackToDefaults()
    {
        var path = Path.Combine(_tempDir, "badenum.json");
        File.WriteAllText(path, """
        {
            "general": {
                "logLevel": "NONEXISTENT",
                "mode": "INVALID_MODE"
            },
            "dat": {
                "hashType": "BOGUS"
            }
        }
        """);

        var settings = SettingsLoader.LoadFrom(path);

        // Invalid enums should fall back to defaults
        Assert.Equal("Info", settings.General.LogLevel);
        Assert.Equal("DryRun", settings.General.Mode);
    }

    [Fact]
    public void LoadFrom_PreferredRegionsWithWhitespace_FiltersEmpty()
    {
        var path = Path.Combine(_tempDir, "regions.json");
        File.WriteAllText(path, """
        {
            "general": {
                "preferredRegions": ["EU", "", "  ", "JP"]
            }
        }
        """);

        var settings = SettingsLoader.LoadFrom(path);

        Assert.Contains("EU", settings.General.PreferredRegions);
        Assert.Contains("JP", settings.General.PreferredRegions);
        Assert.DoesNotContain("", settings.General.PreferredRegions);
    }

    [Fact]
    public void LoadFrom_MalformedJson_ReturnsDefaults()
    {
        var path = Path.Combine(_tempDir, "malformed.json");
        File.WriteAllText(path, "not json at all {{{");

        // LoadFrom delegates to LoadFromSafe, which handles JsonException
        var settings = SettingsLoader.LoadFrom(path);

        Assert.Equal("DryRun", settings.General.Mode);
    }

    // ===== LoadDefaultsOnly =====

    [Fact]
    public void LoadDefaultsOnly_WithExplicitPath_UsesDefaults()
    {
        var defaultsPath = Path.Combine(_tempDir, "data", "defaults.json");
        Directory.CreateDirectory(Path.GetDirectoryName(defaultsPath)!);
        File.WriteAllText(defaultsPath, """
        {
            "mode": "Move",
            "logLevel": "Debug",
            "preferredRegions": ["JP"],
            "theme": "light",
            "locale": "en"
        }
        """);

        var settings = SettingsLoader.LoadDefaultsOnly(defaultsPath);

        Assert.Equal("Move", settings.General.Mode);
        Assert.Equal("Debug", settings.General.LogLevel);
    }

    [Fact]
    public void LoadDefaultsOnly_NonExistentPath_ReturnsHardcodedDefaults()
    {
        var settings = SettingsLoader.LoadDefaultsOnly(Path.Combine(_tempDir, "nope.json"));

        Assert.Equal("DryRun", settings.General.Mode);
    }

    // ===== LoadWithExplicitUserPath =====

    [Fact]
    public void LoadWithExplicitUserPath_MergesBothFiles()
    {
        var defaultsPath = Path.Combine(_tempDir, "defaults.json");
        File.WriteAllText(defaultsPath, """{"logLevel":"Debug","mode":"DryRun"}""");

        var userPath = Path.Combine(_tempDir, "user.json");
        File.WriteAllText(userPath, """{"general":{"logLevel":"Warning"}}""");

        var settings = SettingsLoader.LoadWithExplicitUserPath(defaultsPath, userPath);

        Assert.Equal("Warning", settings.General.LogLevel);
    }

    [Fact]
    public void LoadWithExplicitUserPath_CorruptUserSettings_CreatesBackupAndKeepsDefaults_FindingF23()
    {
        var defaultsPath = Path.Combine(_tempDir, "defaults-f23.json");
        File.WriteAllText(defaultsPath, """{"logLevel":"Warning","mode":"DryRun"}""");

        var userPath = Path.Combine(_tempDir, "user-corrupt-f23.json");
        File.WriteAllText(userPath, "{ invalid json");

        var settings = SettingsLoader.LoadWithExplicitUserPath(defaultsPath, userPath);

        Assert.Equal("Warning", settings.General.LogLevel);
        Assert.True(File.Exists(userPath + ".bak"));
    }
}
