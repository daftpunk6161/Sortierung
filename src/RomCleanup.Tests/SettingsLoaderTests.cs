using RomCleanup.Infrastructure.Configuration;
using Xunit;

namespace RomCleanup.Tests;

public class SettingsLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public SettingsLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RomCleanup_Settings_" + Guid.NewGuid().ToString("N")[..8]);
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
        var settings = RomCleanup.Infrastructure.Configuration.SettingsLoader.LoadFrom(
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
        Assert.Equal("DryRun", settings.General.Mode);
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
    public void UserSettingsPath_ContainsAppData()
    {
        var path = SettingsLoader.UserSettingsPath;
        Assert.Contains("RomCleanupRegionDedupe", path);
        Assert.EndsWith("settings.json", path);
    }
}
