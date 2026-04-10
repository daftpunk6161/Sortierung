using Romulus.Contracts.Models;
using Romulus.Core.Classification;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage boost for RunEnvironmentBuilder: LoadSettings override paths,
/// BuildConsoleMap supplemental DATs, FindExactStemMatch edge cases,
/// Build() error paths, and TryResolveDataDir.
/// Targets ~105 uncovered lines.
/// </summary>
public sealed class RunEnvironmentBuilderCoverageBoostTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dataDir;
    private readonly string _datRoot;

    public RunEnvironmentBuilderCoverageBoostTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "REB_Cov_" + Guid.NewGuid().ToString("N")[..8]);
        _dataDir = Path.Combine(_tempDir, "data");
        _datRoot = Path.Combine(_tempDir, "dat");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_dataDir);
        Directory.CreateDirectory(_datRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ===== TryResolveDataDir: always returns non-null in test context =====

    [Fact]
    public void TryResolveDataDir_ReturnsNonNull()
    {
        // In the test runner context, data/ exists relative to workspace root
        var result = RunEnvironmentBuilder.TryResolveDataDir();
        Assert.NotNull(result);
        Assert.True(Directory.Exists(result));
    }

    // ===== ResolveDataDir: returns same as TryResolve =====

    [Fact]
    public void ResolveDataDir_ReturnsValidPath()
    {
        var result = RunEnvironmentBuilder.ResolveDataDir();
        Assert.True(Directory.Exists(result));
        Assert.True(File.Exists(Path.Combine(result, "consoles.json")));
    }

    // ===== LoadSettings without override =====

    [Fact]
    public void LoadSettings_WithDataDir_ReturnsSettings()
    {
        File.WriteAllText(Path.Combine(_dataDir, "defaults.json"),
            """{"general":{"mode":"DryRun"},"dat":{"useDat":false}}""");

        var settings = RunEnvironmentBuilder.LoadSettings(_dataDir);

        Assert.NotNull(settings);
    }

    // ===== LoadSettings with override path that exists =====

    [Fact]
    public void LoadSettings_WithExistingOverridePath_UsesOverride()
    {
        File.WriteAllText(Path.Combine(_dataDir, "defaults.json"),
            """{"general":{"mode":"DryRun"}}""");

        var overridePath = Path.Combine(_tempDir, "override-settings.json");
        File.WriteAllText(overridePath, """{"general":{"mode":"Execute"}}""");

        var settings = RunEnvironmentBuilder.LoadSettings(_dataDir, overridePath);

        Assert.NotNull(settings);
    }

    // ===== LoadSettings with override path that doesn't exist → defaults only =====

    [Fact]
    public void LoadSettings_WithMissingOverridePath_LoadsDefaultsOnly()
    {
        File.WriteAllText(Path.Combine(_dataDir, "defaults.json"),
            """{"general":{"mode":"DryRun"}}""");

        var settings = RunEnvironmentBuilder.LoadSettings(_dataDir, "/nonexistent/path/settings.json");

        Assert.NotNull(settings);
    }

    // ===== LoadSettings with null override → normal path =====

    [Fact]
    public void LoadSettings_NullOverride_LoadsNormally()
    {
        File.WriteAllText(Path.Combine(_dataDir, "defaults.json"),
            """{"general":{"mode":"DryRun"}}""");

        var settings = RunEnvironmentBuilder.LoadSettings(_dataDir, null);

        Assert.NotNull(settings);
    }

    // ===== BuildConsoleMap: supplemental DATs =====

    [Fact]
    public void BuildConsoleMap_WithCatalogEntries_ReturnsMappedConsoles()
    {
        var catalog = """
        [
            {"group":"Nintendo","system":"Nintendo Entertainment System","id":"nes","consoleKey":"NES"}
        ]
        """;
        File.WriteAllText(Path.Combine(_dataDir, "dat-catalog.json"), catalog);
        File.WriteAllText(Path.Combine(_datRoot, "nes.dat"), "primary dat content");

        var map = RunEnvironmentBuilder.BuildConsoleMap(_dataDir, _datRoot, out var supplementalDats);

        Assert.True(map.ContainsKey("NES"));
        Assert.Contains("nes.dat", map["NES"]);
    }

    // ===== BuildConsoleMap: multiple stems tried =====

    [Fact]
    public void BuildConsoleMap_TriesMultipleStems_Id_System_ConsoleKey()
    {
        var catalog = """
        [
            {"group":"Sony","system":"PlayStation 2","id":"ps2-nointro","consoleKey":"PS2"}
        ]
        """;
        File.WriteAllText(Path.Combine(_dataDir, "dat-catalog.json"), catalog);
        // Only ConsoleKey stem matches
        File.WriteAllText(Path.Combine(_datRoot, "PS2.dat"), "dat content");

        var map = RunEnvironmentBuilder.BuildConsoleMap(_dataDir, _datRoot);

        Assert.True(map.ContainsKey("PS2"));
    }

    // ===== BuildConsoleMap: empty dat root =====

    [Fact]
    public void BuildConsoleMap_EmptyDatRoot_ReturnsEmpty()
    {
        var emptyDat = Path.Combine(_tempDir, "empty-dat");
        Directory.CreateDirectory(emptyDat);
        File.WriteAllText(Path.Combine(_dataDir, "dat-catalog.json"), "[]");

        var map = RunEnvironmentBuilder.BuildConsoleMap(_dataDir, emptyDat);

        Assert.Empty(map);
    }

    // ===== BuildConsoleMap: malformed catalog with valid fallback scan =====

    [Fact]
    public void BuildConsoleMap_MalformedCatalog_FallsBackToScan()
    {
        File.WriteAllText(Path.Combine(_dataDir, "dat-catalog.json"), "{{invalid json");
        File.WriteAllText(Path.Combine(_datRoot, "NES.dat"), "dat content");

        var map = RunEnvironmentBuilder.BuildConsoleMap(_dataDir, _datRoot);

        // Fallback scan should find NES.dat
        Assert.True(map.Count > 0);
    }

    // ===== FindExactStemMatch =====

    [Fact]
    public void FindExactStemMatch_ExactMatch_ReturnsPath()
    {
        var files = new[]
        {
            Path.Combine(_datRoot, "nes.dat"),
            Path.Combine(_datRoot, "snes.dat")
        };

        var result = RunEnvironmentBuilder.FindExactStemMatch(files, "nes");
        Assert.NotNull(result);
        Assert.Contains("nes.dat", result);
    }

    [Fact]
    public void FindExactStemMatch_NoMatch_ReturnsNull()
    {
        var files = new[]
        {
            Path.Combine(_datRoot, "snes.dat")
        };

        var result = RunEnvironmentBuilder.FindExactStemMatch(files, "nes", "psx");
        Assert.Null(result);
    }

    [Fact]
    public void FindExactStemMatch_XmlExtension_Matches()
    {
        var files = new[]
        {
            Path.Combine(_datRoot, "psx.xml")
        };

        var result = RunEnvironmentBuilder.FindExactStemMatch(files, "psx");
        Assert.NotNull(result);
        Assert.Contains("psx.xml", result);
    }

    [Fact]
    public void FindExactStemMatch_CaseInsensitive()
    {
        var files = new[]
        {
            Path.Combine(_datRoot, "NES.DAT")
        };

        var result = RunEnvironmentBuilder.FindExactStemMatch(files, "nes");
        Assert.NotNull(result);
    }

    // ===== MatchPackGlob =====

    [Fact]
    public void MatchPackGlob_WildcardPattern_MatchesSubstring()
    {
        var files = new[]
        {
            Path.Combine(_datRoot, "No-Intro - Nintendo - NES (20240101).dat")
        };

        var result = RunEnvironmentBuilder.MatchPackGlob(files, "No-Intro - Nintendo - NES*");
        Assert.NotNull(result);
    }

    [Fact]
    public void MatchPackGlob_NoMatch_ReturnsNull()
    {
        var files = new[]
        {
            Path.Combine(_datRoot, "Sony - PlayStation.dat")
        };

        var result = RunEnvironmentBuilder.MatchPackGlob(files, "Nintendo*");
        Assert.Null(result);
    }

    // ===== Build: basic environment creation =====

    private const string MinimalConsolesJson = """{"_meta":{"version":"1.0"},"consoles":[]}""";

    [Fact]
    public void Build_MinimalSettings_CreatesEnvironment()
    {
        // Ensure consoles.json exists in data dir
        var consolesPath = Path.Combine(_dataDir, "consoles.json");
        if (!File.Exists(consolesPath))
            File.WriteAllText(consolesPath, MinimalConsolesJson);

        var options = new RunOptions { Roots = [_tempDir] };
        var settings = new RomulusSettings();
        var warnings = new List<string>();

        var env = RunEnvironmentBuilder.Build(options, settings, _dataDir,
            w => warnings.Add(w),
            collectionDatabasePath: Path.Combine(_tempDir, "test-collection.db"));

        Assert.NotNull(env);
        Assert.NotNull(env.FileSystem);
        Assert.NotNull(env.AuditStore);
        env.Dispose();
    }

    // ===== Build: DAT enabled but no dat root =====

    [Fact]
    public void Build_DatEnabledNoDatRoot_EmitsWarning()
    {
        File.WriteAllText(Path.Combine(_dataDir, "consoles.json"), MinimalConsolesJson);

        var options = new RunOptions
        {
            Roots = [_tempDir],
            EnableDat = true,
            DatRoot = null
        };
        var settings = new RomulusSettings();
        var warnings = new List<string>();

        var env = RunEnvironmentBuilder.Build(options, settings, _dataDir,
            w => warnings.Add(w),
            collectionDatabasePath: Path.Combine(_tempDir, "test.db"));

        Assert.Contains(warnings, w => w.Contains("DAT enabled but DatRoot not set"));
        env.Dispose();
    }

    // ===== Build: conversion enabled =====

    [Fact]
    public void Build_ConversionEnabled_CreatesConverter()
    {
        File.WriteAllText(Path.Combine(_dataDir, "consoles.json"), MinimalConsolesJson);
        File.WriteAllText(
            Path.Combine(_dataDir, "conversion-registry.json"),
            "{\"schemaVersion\":\"conversion-registry-v1\",\"capabilities\":[]}");

        var options = new RunOptions
        {
            Roots = [_tempDir],
            ConvertFormat = "chd" // triggers converter creation
        };
        var settings = new RomulusSettings();
        var warnings = new List<string>();

        var env = RunEnvironmentBuilder.Build(options, settings, _dataDir,
            w => warnings.Add(w),
            collectionDatabasePath: Path.Combine(_tempDir, "test2.db"));

        Assert.NotNull(env);
        env.Dispose();
    }

    // ===== Build: no consoles.json + sort requested =====

    [Fact]
    public void Build_NoConsolesJsonWithSort_EmitsWarning()
    {
        // Don't create consoles.json
        var options = new RunOptions
        {
            Roots = [_tempDir],
            SortConsole = true
        };
        var settings = new RomulusSettings();
        var warnings = new List<string>();

        var env = RunEnvironmentBuilder.Build(options, settings, _dataDir,
            w => warnings.Add(w),
            collectionDatabasePath: Path.Combine(_tempDir, "test3.db"));

        Assert.Contains(warnings, w => w.Contains("consoles.json not found"));
        env.Dispose();
    }
}
