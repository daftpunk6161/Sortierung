using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.Orchestration;
using Xunit;

namespace RomCleanup.Tests;

public sealed class RunEnvironmentBuilderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dataDir;
    private readonly string _datRoot;

    public RunEnvironmentBuilderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RunEnv_" + Guid.NewGuid().ToString("N"));
        _dataDir = Path.Combine(_tempDir, "data");
        _datRoot = Path.Combine(_tempDir, "dat");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_dataDir);
        Directory.CreateDirectory(_datRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void BuildConsoleMap_UsesCatalogEntries_WhenMappedDatExists()
    {
        File.WriteAllText(Path.Combine(_datRoot, "psx.dat"), "dummy");
        File.WriteAllText(
            Path.Combine(_dataDir, "dat-catalog.json"),
            "[{\"group\":\"Sony\",\"system\":\"PlayStation\",\"id\":\"psx\",\"consoleKey\":\"PSX\"}]");

        var map = RunEnvironmentBuilder.BuildConsoleMap(_dataDir, _datRoot);

        Assert.True(map.ContainsKey("PSX"));
        Assert.EndsWith("psx.dat", map["PSX"], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildConsoleMap_MalformedCatalog_FallsBackToDirectoryScan()
    {
        File.WriteAllText(Path.Combine(_dataDir, "dat-catalog.json"), "{ not-json }");
        File.WriteAllText(Path.Combine(_datRoot, "SATURN.dat"), "dummy");

        var map = RunEnvironmentBuilder.BuildConsoleMap(_dataDir, _datRoot);

        Assert.True(map.ContainsKey("SATURN"));
    }

    [Fact]
    public void BuildConsoleMap_WithoutCatalog_ScansDatRootByStem()
    {
        File.WriteAllText(Path.Combine(_datRoot, "SNES.dat"), "dummy");

        var map = RunEnvironmentBuilder.BuildConsoleMap(_dataDir, _datRoot);

        Assert.True(map.ContainsKey("SNES"));
    }

    [Fact]
    public void LoadSettings_WithoutDefaultsFile_ReturnsFallbackSettings()
    {
        var settings = RunEnvironmentBuilder.LoadSettings(_dataDir);

        Assert.NotNull(settings);
        Assert.NotNull(settings.General);
        Assert.NotNull(settings.Dat);
    }

    [Fact]
    public void LoadSettings_WithDefaultsFile_ReturnsValidSettingsObject()
    {
        File.WriteAllText(
            Path.Combine(_dataDir, "defaults.json"),
            "{\"mode\":\"Move\",\"hashType\":\"MD5\",\"useDat\":true,\"preferredRegions\":[\"US\",\"EU\"]}");

        var settings = RunEnvironmentBuilder.LoadSettings(_dataDir);

        Assert.NotNull(settings);
        Assert.NotNull(settings.General);
        Assert.NotNull(settings.Dat);
        Assert.False(string.IsNullOrWhiteSpace(settings.Dat.HashType));
        Assert.NotNull(settings.General.PreferredRegions);
    }

    [Fact]
    public void Build_WhenDatEnabledButRootMissing_EmitsWarningAndBuildsEnvironment()
    {
        var warnings = new List<string>();
        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            EnableDat = true,
            DatRoot = Path.Combine(_tempDir, "missing-dat")
        };
        var settings = new RomCleanupSettings();

        var env = RunEnvironmentBuilder.Build(options, settings, _dataDir, warnings.Add);

        Assert.NotNull(env.FileSystem);
        Assert.NotNull(env.Audit);
        Assert.Contains(warnings, w => w.Contains("DAT enabled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_WhenConvertFormatConfigured_CreatesConverter()
    {
        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            ConvertFormat = "chd"
        };
        var settings = new RomCleanupSettings();

        var env = RunEnvironmentBuilder.Build(options, settings, _dataDir);

        Assert.NotNull(env.Converter);
    }

    [Fact]
    public void Build_WhenConsolesJsonMissingAndSortEnabled_EmitsWarning()
    {
        var warnings = new List<string>();
        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            SortConsole = true
        };

        var env = RunEnvironmentBuilder.Build(options, new RomCleanupSettings(), _dataDir, warnings.Add);

        Assert.Null(env.ConsoleDetector);
        Assert.Contains(warnings, w => w.Contains("consoles.json", StringComparison.OrdinalIgnoreCase));
    }
}
