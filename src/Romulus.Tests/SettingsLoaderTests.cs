using Romulus.Infrastructure.Configuration;
using Romulus.Infrastructure.Paths;
using Xunit;

namespace Romulus.Tests;

public class SettingsLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public SettingsLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_Settings_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void LoadFrom_DefaultsOnly_ReturnsDefaults()
    {
        var settings = Romulus.Infrastructure.Configuration.SettingsLoader.LoadFrom(
            Path.Combine(_tempDir, "nope.json"));

        Assert.Equal("DryRun", settings.General.Mode);
        Assert.Equal("Info", settings.General.LogLevel);
        Assert.Contains("EU", settings.General.PreferredRegions);
    }

    [Fact]
    public void LoadFrom_ValidJson_ParsesCorrectly()
    {
        var json = @"{
            ""general"": {
                ""logLevel"": ""Debug"",
                ""preferredRegions"": [""JP"", ""EU""],
                ""aggressiveJunk"": true,
                ""mode"": ""Move""
            },
            ""toolPaths"": {
                ""chdman"": ""C:\\Tools\\chdman.exe""
            },
            ""dat"": {
                ""useDat"": false,
                ""hashType"": ""SHA256""
            }
        }";
        var path = Path.Combine(_tempDir, "settings.json");
        File.WriteAllText(path, json);

        var settings = SettingsLoader.LoadFrom(path);

        Assert.Equal("Debug", settings.General.LogLevel);
        Assert.Equal(new[] { "JP", "EU" }, settings.General.PreferredRegions);
        Assert.True(settings.General.AggressiveJunk);
        Assert.Equal("Move", settings.General.Mode);
        Assert.Equal("C:\\Tools\\chdman.exe", settings.ToolPaths.Chdman);
        Assert.False(settings.Dat.UseDat);
        Assert.Equal("SHA256", settings.Dat.HashType);
    }

    [Fact]
    public void LoadFrom_MalformedJson_ReturnsDefaults()
    {
        var path = Path.Combine(_tempDir, "bad.json");
        File.WriteAllText(path, "{ not valid json");

        var settings = SettingsLoader.LoadFrom(path);
        Assert.NotNull(settings);
        // Verify all hardcoded defaults are preserved
        Assert.Equal("DryRun", settings.General.Mode);
        Assert.Equal("Info", settings.General.LogLevel);
        Assert.Equal("dark", settings.General.Theme);
        Assert.Equal("de", settings.General.Locale);
        Assert.False(settings.General.AggressiveJunk);
        Assert.Contains("EU", settings.General.PreferredRegions);
        Assert.True(settings.Dat.UseDat);
        Assert.True(settings.Dat.DatFallback);
        Assert.Equal("SHA1", settings.Dat.HashType);
    }

    [Fact]
    public void Load_WithDefaultsJson_MergesValues()
    {
        // Use LoadFrom with a defaults-like structure wrapped in 'general'
        var settingsJson = @"{
            ""general"": {
                ""logLevel"": ""Warning"",
                ""extensions"": "".zip,.7z"",
                ""theme"": ""light"",
                ""locale"": ""en""
            }
        }";
        var path = Path.Combine(_tempDir, "settings.json");
        File.WriteAllText(path, settingsJson);

        var settings = SettingsLoader.LoadFrom(path);

        Assert.Equal("Warning", settings.General.LogLevel);
        Assert.Equal(".zip,.7z", settings.General.Extensions);
        Assert.Equal("light", settings.General.Theme);
        Assert.Equal("en", settings.General.Locale);
    }

    [Fact]
    public void Load_WithRepoDefaultsPath_UsesRepoDefaultParity()
    {
        var defaultsPath = SettingsLoader.ResolveDefaultsJsonPath();

        Assert.False(string.IsNullOrWhiteSpace(defaultsPath));

        var settings = SettingsLoader.LoadDefaultsOnly(defaultsPath);

        Assert.Equal("DryRun", settings.General.Mode);
        Assert.Equal("Info", settings.General.LogLevel);
        Assert.Equal(new[] { "EU", "US", "JP", "WORLD" }, settings.General.PreferredRegions);
        Assert.True(settings.Dat.UseDat);
        Assert.True(settings.Dat.DatFallback);
        Assert.Equal("SHA1", settings.Dat.HashType);
        Assert.Equal("dark", settings.General.Theme);
        Assert.Equal("de", settings.General.Locale);
    }

    [Fact]
    public void UserSettingsPath_UsesResolvedStorageBase()
    {
        var path = SettingsLoader.UserSettingsPath;
        var expectedBase = AppStoragePathResolver.ResolveRoamingAppDirectory();
        Assert.StartsWith(expectedBase, path, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("settings.json", path);
    }

    // ── P1-BUG-033: Missing bool keys must not overwrite defaults ──

    [Fact]
    public void MergeUserSettings_MissingBoolKeys_PreservesDefaults()
    {
        // Test that LoadFrom with only a logLevel key doesn't zero-out bools.
        // We use LoadFrom (not Load) to avoid interference from real user settings.json.
        var userJson = @"{ ""general"": { ""logLevel"": ""Debug"" } }";
        var userPath = Path.Combine(_tempDir, "user-settings.json");
        File.WriteAllText(userPath, userJson);

        var settings = SettingsLoader.LoadFrom(userPath);

        // Hardcoded defaults for bools should be preserved when missing from JSON:
        Assert.True(settings.Dat.DatFallback, "DatFallback should stay true when missing from JSON");
        Assert.True(settings.Dat.UseDat, "UseDat should stay true when missing from JSON");
        Assert.False(settings.General.AggressiveJunk, "AggressiveJunk default is false");
    }

    [Fact]
    public void MergeUserSettings_ExplicitBoolFalse_OverridesDefaults()
    {
        // User explicitly sets useDat=false and datFallback=false
        var userJson = @"{
            ""dat"": {
                ""useDat"": false,
                ""datFallback"": false
            }
        }";
        var userPath = Path.Combine(_tempDir, "explicit-false.json");
        File.WriteAllText(userPath, userJson);

        var settings = SettingsLoader.LoadFrom(userPath);

        // Explicit false should be honored
        Assert.False(settings.Dat.UseDat);
        Assert.False(settings.Dat.DatFallback);
    }

    [Fact]
    public void MergeUserSettings_ExplicitBoolTrue_SetsBool()
    {
        var userJson = @"{
            ""general"": {
                ""aggressiveJunk"": true,
                ""aliasEditionKeying"": true
            }
        }";
        var userPath = Path.Combine(_tempDir, "explicit-true.json");
        File.WriteAllText(userPath, userJson);

        var settings = SettingsLoader.LoadFrom(userPath);

        Assert.True(settings.General.AggressiveJunk);
        Assert.True(settings.General.AliasEditionKeying);
    }

    [Fact]
    public async Task LoadFrom_TemporarilyLockedFile_RetriesAndParses()
    {
        var json = @"{
            ""general"": {
                ""logLevel"": ""Debug"",
                ""preferredRegions"": [""US"", ""JP""]
            }
        }";
        var path = Path.Combine(_tempDir, "locked-settings.json");
        File.WriteAllText(path, json);

        var lockedStream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var releaseTask = Task.Run(() =>
        {
            Thread.Sleep(90);
            lockedStream.Dispose();
        });

        var settings = SettingsLoader.LoadFrom(path);
        await releaseTask;

        Assert.Equal("Debug", settings.General.LogLevel);
        Assert.Equal(new[] { "US", "JP" }, settings.General.PreferredRegions);
    }
}
