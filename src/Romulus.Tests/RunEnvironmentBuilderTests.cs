using Romulus.Contracts.Models;
using Romulus.Core.Classification;
using Romulus.Infrastructure.Dat;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

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
    public void BuildConsoleMap_PackMatch_MatchesNoIntroDailyPackFilename()
    {
        // Simulate extracted No-Intro daily pack DAT
        File.WriteAllText(
            Path.Combine(_datRoot, "Nintendo - Nintendo Entertainment System (Headered) (20260315-050000).dat"),
            "dummy");
        File.WriteAllText(
            Path.Combine(_dataDir, "dat-catalog.json"),
            "[{\"group\":\"No-Intro\",\"system\":\"Nintendo - NES\",\"id\":\"nointro-nes\",\"consoleKey\":\"NES\",\"packMatch\":\"Nintendo - Nintendo Entertainment System (Headered)*\"}]");

        var map = RunEnvironmentBuilder.BuildConsoleMap(_dataDir, _datRoot);

        Assert.True(map.ContainsKey("NES"));
        Assert.Contains("Nintendo - Nintendo Entertainment System (Headered)", map["NES"]);
    }

    [Fact]
    public void BuildConsoleMap_PackMatch_PicksNewestDateSuffix()
    {
        File.WriteAllText(
            Path.Combine(_datRoot, "Sega - Mega Drive - Genesis (20260101-010000).dat"), "old");
        File.WriteAllText(
            Path.Combine(_datRoot, "Sega - Mega Drive - Genesis (20260315-050000).dat"), "new");
        File.WriteAllText(
            Path.Combine(_dataDir, "dat-catalog.json"),
            "[{\"group\":\"No-Intro\",\"system\":\"Sega MD\",\"id\":\"nointro-md\",\"consoleKey\":\"MD\",\"packMatch\":\"Sega - Mega Drive - Genesis*\"}]");

        var map = RunEnvironmentBuilder.BuildConsoleMap(_dataDir, _datRoot);

        Assert.True(map.ContainsKey("MD"));
        Assert.Contains("20260315", map["MD"]);
    }

    [Fact]
    public void BuildConsoleMap_PackMatch_PrioritizesFilenameStemOverFolderName()
    {
        var olderDir = Path.Combine(_datRoot, "zzz-older");
        var newerDir = Path.Combine(_datRoot, "aaa-newer");
        Directory.CreateDirectory(olderDir);
        Directory.CreateDirectory(newerDir);

        File.WriteAllText(Path.Combine(olderDir, "Nintendo - Nintendo Entertainment System (Headered) (20260301-000000).dat"), "old");
        File.WriteAllText(Path.Combine(newerDir, "Nintendo - Nintendo Entertainment System (Headered) (20260327-000000).dat"), "new");
        File.WriteAllText(
            Path.Combine(_dataDir, "dat-catalog.json"),
            "[{\"group\":\"No-Intro\",\"system\":\"Nintendo - NES\",\"id\":\"nointro-nes\",\"consoleKey\":\"NES\",\"packMatch\":\"Nintendo - Nintendo Entertainment System (Headered)*\"}]");

        var map = RunEnvironmentBuilder.BuildConsoleMap(_dataDir, _datRoot);

        Assert.True(map.ContainsKey("NES"));
        Assert.Contains("20260327", map["NES"]);
    }

    [Fact]
    public void BuildConsoleMap_ExactMatch_TakesPriorityOverPackMatch()
    {
        // Both an exact ID match and a PackMatch-compatible file exist
        File.WriteAllText(Path.Combine(_datRoot, "nointro-nes.dat"), "exact");
        File.WriteAllText(
            Path.Combine(_datRoot, "Nintendo - Nintendo Entertainment System (Headered) (20260315).dat"), "pack");
        File.WriteAllText(
            Path.Combine(_dataDir, "dat-catalog.json"),
            "[{\"group\":\"No-Intro\",\"system\":\"Nintendo - NES\",\"id\":\"nointro-nes\",\"consoleKey\":\"NES\",\"packMatch\":\"Nintendo - Nintendo Entertainment System (Headered)*\"}]");

        var map = RunEnvironmentBuilder.BuildConsoleMap(_dataDir, _datRoot);

        Assert.True(map.ContainsKey("NES"));
        Assert.EndsWith("nointro-nes.dat", map["NES"], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildConsoleMap_ExactIdMatch_FindsDatInNestedFolder()
    {
        var nestedDir = Path.Combine(_datRoot, "No-Intro");
        Directory.CreateDirectory(nestedDir);
        File.WriteAllText(Path.Combine(nestedDir, "nointro-nes.dat"), "dummy");
        File.WriteAllText(
            Path.Combine(_dataDir, "dat-catalog.json"),
            "[{\"group\":\"No-Intro\",\"system\":\"Nintendo - NES\",\"id\":\"nointro-nes\",\"consoleKey\":\"NES\"}]");

        var map = RunEnvironmentBuilder.BuildConsoleMap(_dataDir, _datRoot);

        Assert.True(map.ContainsKey("NES"));
        Assert.Contains(Path.Combine("No-Intro", "nointro-nes.dat"), map["NES"], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildConsoleMap_ExactIdMatch_FindsXmlInNestedFolder()
    {
        var nestedDir = Path.Combine(_datRoot, "No-Intro");
        Directory.CreateDirectory(nestedDir);
        File.WriteAllText(Path.Combine(nestedDir, "nointro-gba.xml"), "<datafile/>");
        File.WriteAllText(
            Path.Combine(_dataDir, "dat-catalog.json"),
            "[{\"group\":\"No-Intro\",\"system\":\"Nintendo - GBA\",\"id\":\"nointro-gba\",\"consoleKey\":\"GBA\"}]");

        var map = RunEnvironmentBuilder.BuildConsoleMap(_dataDir, _datRoot);

        Assert.True(map.ContainsKey("GBA"));
        Assert.Contains(Path.Combine("No-Intro", "nointro-gba.xml"), map["GBA"], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildConsoleMap_FallbackScan_FindsNestedDatByStem()
    {
        var nestedDir = Path.Combine(_datRoot, "Non-Redump");
        Directory.CreateDirectory(nestedDir);
        File.WriteAllText(Path.Combine(nestedDir, "DOS.dat"), "dummy");

        var map = RunEnvironmentBuilder.BuildConsoleMap(_dataDir, _datRoot);

        Assert.True(map.ContainsKey("DOS"));
        Assert.Contains(Path.Combine("Non-Redump", "DOS.dat"), map["DOS"], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildConsoleMap_PackMatch_NoMatchReturnsEmpty()
    {
        File.WriteAllText(
            Path.Combine(_datRoot, "Unrelated File.dat"), "dummy");
        File.WriteAllText(
            Path.Combine(_dataDir, "dat-catalog.json"),
            "[{\"group\":\"No-Intro\",\"system\":\"NES\",\"id\":\"nointro-nes\",\"consoleKey\":\"NES\",\"packMatch\":\"Nintendo - Nintendo Entertainment System*\"}]");

        var map = RunEnvironmentBuilder.BuildConsoleMap(_dataDir, _datRoot);

        // NES not from catalog, but "UNRELATED FILE" from fallback scan
        Assert.False(map.ContainsKey("NES"));
    }

    [Fact]
    public void MatchPackGlob_EmptyPattern_ReturnsNull()
    {
        var result = RunEnvironmentBuilder.MatchPackGlob(["a.dat"], "");
        Assert.Null(result);
    }

    [Fact]
    public void MatchPackGlob_EmptyFiles_ReturnsNull()
    {
        var result = RunEnvironmentBuilder.MatchPackGlob([], "Prefix*");
        Assert.Null(result);
    }

    [Fact]
    public void MatchPackGlob_CaseInsensitiveMatch()
    {
        var files = new[] { "/dat/nintendo - nes (headered) (2026).dat" };
        var result = RunEnvironmentBuilder.MatchPackGlob(files, "Nintendo - NES (Headered)*");
        Assert.NotNull(result);
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
    public void Build_WhenDatEnabledButRootMissing_AutoDetectsOrWarnsAndBuildsEnvironment()
    {
        var warnings = new List<string>();
        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            EnableDat = true,
            DatRoot = Path.Combine(_tempDir, "missing-dat")
        };
        var settings = new RomulusSettings();

        using var env = RunEnvironmentBuilder.Build(options, settings, _dataDir, warnings.Add);

        Assert.NotNull(env.FileSystem);
        Assert.NotNull(env.Audit);
        Assert.True(
            warnings.Any(w => w.Contains("DAT enabled", StringComparison.OrdinalIgnoreCase)) ||
            warnings.Any(w => w.Contains("DatRoot automatisch erkannt", StringComparison.OrdinalIgnoreCase)),
            "Expected either missing-DAT warning or auto-detected DAT root info.");
    }

    [Fact]
    public void Build_WhenDatEnabled_CreatesPersistentHashService()
    {
        var warnings = new List<string>();
        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            EnableDat = true,
            DatRoot = _datRoot
        };
        var settings = new RomulusSettings();

        using var env = RunEnvironmentBuilder.Build(options, settings, _dataDir, warnings.Add);

        Assert.NotNull(env.HashService);
        Assert.True(env.HashService!.IsPersistent);
        Assert.NotNull(env.CollectionIndex);
        Assert.False(string.IsNullOrWhiteSpace(env.EnrichmentFingerprint));
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
        var settings = new RomulusSettings();

        using var env = RunEnvironmentBuilder.Build(options, settings, _dataDir);

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

        using var env = RunEnvironmentBuilder.Build(options, new RomulusSettings(), _dataDir, warnings.Add);

        Assert.Null(env.ConsoleDetector);
        Assert.Contains(warnings, w => w.Contains("consoles.json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResolveEffectiveDatRoot_RunOptionTakesPriority()
    {
        var resolution = RunEnvironmentBuilder.ResolveEffectiveDatRoot(
            runOptionDatRoot: _datRoot,
            settingsDatRoot: null,
            dataDir: _dataDir,
            statePath: Path.Combine(_tempDir, "missing-state.json"));

        Assert.Equal(RunEnvironmentBuilder.DatRootResolutionSource.RunOption, resolution.Source);
        Assert.Equal(Path.GetFullPath(_datRoot), resolution.Path);
    }

    [Fact]
    public void ResolveEffectiveDatRoot_AutoDetectsFromCatalogState_WhenConfiguredRootsMissing()
    {
        var detectedRoot = Path.Combine(_tempDir, "state-dats");
        Directory.CreateDirectory(detectedRoot);
        var datPath = Path.Combine(detectedRoot, "state-entry.dat");
        File.WriteAllText(datPath, "state");

        var statePath = Path.Combine(_tempDir, "dat-catalog-state.json");
        var state = new DatCatalogState
        {
            Entries = new Dictionary<string, DatLocalInfo>(StringComparer.OrdinalIgnoreCase)
            {
                ["state-entry"] = new DatLocalInfo
                {
                    InstalledDate = DateTime.UtcNow,
                    FileSha256 = "hash",
                    FileSizeBytes = 5,
                    LocalPath = datPath
                }
            }
        };
        DatCatalogStateService.SaveState(statePath, state);

        var resolution = RunEnvironmentBuilder.ResolveEffectiveDatRoot(
            runOptionDatRoot: null,
            settingsDatRoot: null,
            dataDir: _dataDir,
            statePath: statePath);

        Assert.Equal(RunEnvironmentBuilder.DatRootResolutionSource.CatalogState, resolution.Source);
        Assert.Equal(Path.GetFullPath(detectedRoot), resolution.Path);
    }

    [Fact]
    public void ResolveEffectiveDatRoot_SettingsRootWithoutDatFiles_FallsBackToAutoDetectedStateRoot()
    {
        var emptySettingsRoot = Path.Combine(_tempDir, "settings-empty");
        Directory.CreateDirectory(emptySettingsRoot);

        var detectedRoot = Path.Combine(_tempDir, "state-dats-fallback");
        Directory.CreateDirectory(detectedRoot);
        var datPath = Path.Combine(detectedRoot, "fallback-entry.dat");
        File.WriteAllText(datPath, "state");

        var statePath = Path.Combine(_tempDir, "dat-catalog-state-fallback.json");
        var state = new DatCatalogState
        {
            Entries = new Dictionary<string, DatLocalInfo>(StringComparer.OrdinalIgnoreCase)
            {
                ["fallback-entry"] = new DatLocalInfo
                {
                    InstalledDate = DateTime.UtcNow,
                    FileSha256 = "hash",
                    FileSizeBytes = 5,
                    LocalPath = datPath
                }
            }
        };
        DatCatalogStateService.SaveState(statePath, state);

        var resolution = RunEnvironmentBuilder.ResolveEffectiveDatRoot(
            runOptionDatRoot: null,
            settingsDatRoot: emptySettingsRoot,
            dataDir: _dataDir,
            statePath: statePath);

        Assert.Equal(RunEnvironmentBuilder.DatRootResolutionSource.CatalogState, resolution.Source);
        Assert.Equal(Path.GetFullPath(detectedRoot), resolution.Path);
    }

    [Fact]
    public void ResolveEffectiveDatRoot_AutoDetectsFromConventionalDataDirectory()
    {
        var conventionalRoot = Path.Combine(_dataDir, "dats");
        Directory.CreateDirectory(conventionalRoot);
        File.WriteAllText(Path.Combine(conventionalRoot, "conventional.dat"), "dat");

        var resolution = RunEnvironmentBuilder.ResolveEffectiveDatRoot(
            runOptionDatRoot: null,
            settingsDatRoot: null,
            dataDir: _dataDir,
            statePath: Path.Combine(_tempDir, "missing-conventional-state.json"));

        Assert.Equal(RunEnvironmentBuilder.DatRootResolutionSource.ConventionalPath, resolution.Source);
        Assert.Equal(Path.GetFullPath(conventionalRoot), resolution.Path);
    }

    [Fact]
    public void NormalizeRuntimeDatMappings_RemapsDescriptorKeysAndKeepsSanitizedFallbacks()
    {
        var gbcDat = Path.Combine(_datRoot, "Nintendo - Game Boy Color (20260328-141827).dat");
        var nesDat = Path.Combine(_datRoot, "NES.dat");
        var invalidDat = Path.Combine(_datRoot, "invalid descriptor.dat");
        File.WriteAllText(gbcDat, "gbc");
        File.WriteAllText(nesDat, "nes");
        File.WriteAllText(invalidDat, "invalid");

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Nintendo - Game Boy Color (20260328-141827)"] = gbcDat,
            ["NES"] = nesDat,
            ["invalid descriptor"] = invalidDat
        };
        var supplemental = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        var detector = ConsoleDetector.LoadFromJson("""
        {
            "consoles": [
                {
                    "key": "GBC",
                    "displayName": "Game Boy Color",
                    "discBased": false,
                    "uniqueExts": [".gbc"],
                    "ambigExts": [],
                    "folderAliases": ["gbc", "game boy color"]
                },
                {
                    "key": "NES",
                    "displayName": "Nintendo Entertainment System",
                    "discBased": false,
                    "uniqueExts": [".nes"],
                    "ambigExts": [],
                    "folderAliases": ["nes", "famicom"]
                }
            ]
        }
        """);

        RunEnvironmentBuilder.NormalizeRuntimeDatMappings(
            map,
            supplemental,
            detector,
            _datRoot,
            onWarning: null);

        // GBC and NES resolved via ConsoleDetector, "invalid descriptor" kept as sanitized key
        Assert.Equal(3, map.Count);
        Assert.Equal(gbcDat, map["GBC"]);
        Assert.Equal(nesDat, map["NES"]);
        // "invalid descriptor" → sanitized to "INVALID_DESCRIPTOR"
        Assert.True(map.ContainsKey("INVALID_DESCRIPTOR"));
        Assert.Equal(invalidDat, map["INVALID_DESCRIPTOR"]);
        Assert.Empty(supplemental);
    }

    [Fact]
    public void NormalizeRuntimeDatMappings_DropsSentinelConsoleKeys()
    {
        var unknownDat = Path.Combine(_datRoot, "UNKNOWN.dat");
        var ambiguousDat = Path.Combine(_datRoot, "AMBIGUOUS.dat");
        var nesDat = Path.Combine(_datRoot, "NES.dat");
        File.WriteAllText(unknownDat, "unknown");
        File.WriteAllText(ambiguousDat, "ambiguous");
        File.WriteAllText(nesDat, "nes");

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["UNKNOWN"] = unknownDat,
            ["AMBIGUOUS"] = ambiguousDat,
            ["NES"] = nesDat
        };
        var supplemental = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        RunEnvironmentBuilder.NormalizeRuntimeDatMappings(
            map,
            supplemental,
            consoleDetector: null,
            _datRoot,
            onWarning: null);

        Assert.Single(map);
        Assert.Equal(nesDat, map["NES"]);
        Assert.False(map.ContainsKey("UNKNOWN"));
        Assert.False(map.ContainsKey("AMBIGUOUS"));
        Assert.Empty(supplemental);
    }

    // ── BridgeDatSourceAliases ──────────────────────────────────────────

    [Fact]
    public void BridgeDatSourceAliases_BridgesArcadeToMameDat()
    {
        // Catalog has MAME entry with ConsoleKey "MAME" and Id "mame"
        File.WriteAllText(
            Path.Combine(_dataDir, "dat-catalog.json"),
            """[{"group":"MAME","system":"MAME","id":"mame","consoleKey":"MAME"}]""");

        // ARCADE console references datSources ["mame"]
        var detector = ConsoleDetector.LoadFromJson("""
        {
            "consoles": [
                {
                    "key": "ARCADE",
                    "displayName": "Arcade",
                    "discBased": false,
                    "uniqueExts": [],
                    "ambigExts": [],
                    "folderAliases": ["arcade"],
                    "datSources": ["mame"]
                },
                {
                    "key": "MAME",
                    "displayName": "MAME",
                    "discBased": false,
                    "uniqueExts": [],
                    "ambigExts": [],
                    "folderAliases": ["mame"],
                    "datSources": ["mame"]
                }
            ]
        }
        """);

        // Initial map only has MAME (from BuildConsoleMap)
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MAME"] = Path.Combine(_datRoot, "mame.dat")
        };

        RunEnvironmentBuilder.BridgeDatSourceAliases(map, detector, _dataDir);

        Assert.True(map.ContainsKey("ARCADE"));
        Assert.Equal(map["MAME"], map["ARCADE"]);
    }

    [Fact]
    public void BridgeDatSourceAliases_DoesNotOverwriteExistingMapping()
    {
        File.WriteAllText(
            Path.Combine(_dataDir, "dat-catalog.json"),
            """[{"group":"MAME","system":"MAME","id":"mame","consoleKey":"MAME"}]""");

        var detector = ConsoleDetector.LoadFromJson("""
        {
            "consoles": [
                {
                    "key": "ARCADE",
                    "displayName": "Arcade",
                    "discBased": false,
                    "uniqueExts": [],
                    "ambigExts": [],
                    "folderAliases": ["arcade"],
                    "datSources": ["mame"]
                }
            ]
        }
        """);

        var arcadeDat = Path.Combine(_datRoot, "fbneo-arcade.dat");
        var mameDat = Path.Combine(_datRoot, "mame.dat");
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ARCADE"] = arcadeDat,
            ["MAME"] = mameDat
        };

        RunEnvironmentBuilder.BridgeDatSourceAliases(map, detector, _dataDir);

        // ARCADE already mapped — must NOT be overwritten
        Assert.Equal(arcadeDat, map["ARCADE"]);
    }

    [Fact]
    public void BridgeDatSourceAliases_NoCatalog_DoesNothing()
    {
        // No dat-catalog.json exists
        var detector = ConsoleDetector.LoadFromJson("""
        {
            "consoles": [
                {
                    "key": "ARCADE",
                    "displayName": "Arcade",
                    "discBased": false,
                    "uniqueExts": [],
                    "ambigExts": [],
                    "folderAliases": ["arcade"],
                    "datSources": ["mame"]
                }
            ]
        }
        """);

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        RunEnvironmentBuilder.BridgeDatSourceAliases(map, detector, _dataDir);

        Assert.Empty(map);
    }

    [Fact]
    public void BridgeDatSourceAliases_NoDatSources_SkipsConsole()
    {
        File.WriteAllText(
            Path.Combine(_dataDir, "dat-catalog.json"),
            """[{"group":"No-Intro","system":"SNES","id":"nointro-snes","consoleKey":"SNES"}]""");

        var detector = ConsoleDetector.LoadFromJson("""
        {
            "consoles": [
                {
                    "key": "NES",
                    "displayName": "NES",
                    "discBased": false,
                    "uniqueExts": [".nes"],
                    "ambigExts": [],
                    "folderAliases": ["nes"]
                }
            ]
        }
        """);

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SNES"] = Path.Combine(_datRoot, "nointro-snes.dat")
        };

        RunEnvironmentBuilder.BridgeDatSourceAliases(map, detector, _dataDir);

        // NES has no datSources, so no bridge should be created
        Assert.False(map.ContainsKey("NES"));
        Assert.Single(map);
    }

    [Fact]
    public void BridgeDatSourceAliases_FallsBackToSecondDatSource()
    {
        // Catalog has both mame and fbneo entries
        File.WriteAllText(
            Path.Combine(_dataDir, "dat-catalog.json"),
            """
            [
                {"group":"MAME","system":"MAME","id":"mame","consoleKey":"MAME"},
                {"group":"FBNeo","system":"FBNeo","id":"fbneo","consoleKey":"FBNEO"}
            ]
            """);

        var detector = ConsoleDetector.LoadFromJson("""
        {
            "consoles": [
                {
                    "key": "ARCADE",
                    "displayName": "Arcade",
                    "discBased": false,
                    "uniqueExts": [],
                    "ambigExts": [],
                    "folderAliases": ["arcade"],
                    "datSources": ["mame", "fbneo"]
                }
            ]
        }
        """);

        // Only FBNEO mapped (MAME DAT not downloaded)
        var fbneoPath = Path.Combine(_datRoot, "fbneo.dat");
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["FBNEO"] = fbneoPath
        };

        RunEnvironmentBuilder.BridgeDatSourceAliases(map, detector, _dataDir);

        Assert.True(map.ContainsKey("ARCADE"));
        Assert.Equal(fbneoPath, map["ARCADE"]);
    }

    // ── Supplemental DATs ───────────────────────────────────────────────

    [Fact]
    public void BuildConsoleMap_SupplementalDats_CollectsSecondDatForSameConsoleKey()
    {
        // Primary DAT for NES (No-Intro)
        File.WriteAllText(Path.Combine(_datRoot, "nointro-nes.dat"), "primary");
        // Supplemental DAT for NES (FBNeo)
        File.WriteAllText(Path.Combine(_datRoot, "fbneo-nes.dat"), "supplement");

        File.WriteAllText(
            Path.Combine(_dataDir, "dat-catalog.json"),
            """
            [
                {"group":"No-Intro","system":"NES","id":"nointro-nes","consoleKey":"NES"},
                {"group":"FBNeo","system":"FBNeo NES","id":"fbneo-nes","consoleKey":"NES"}
            ]
            """);

        var map = RunEnvironmentBuilder.BuildConsoleMap(_dataDir, _datRoot, out var supplementalDats);

        // Primary map should contain NES → nointro-nes.dat
        Assert.True(map.ContainsKey("NES"));
        Assert.EndsWith("nointro-nes.dat", map["NES"], StringComparison.OrdinalIgnoreCase);

        // Supplemental should contain fbneo-nes.dat for NES
        Assert.True(supplementalDats.ContainsKey("NES"));
        Assert.Single(supplementalDats["NES"]);
        Assert.EndsWith("fbneo-nes.dat", supplementalDats["NES"][0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildConsoleMap_SupplementalDats_ExcludedFromFallbackScan()
    {
        // Catalog maps NES to nointro-nes.dat and fbneo-nes.dat as supplemental
        File.WriteAllText(Path.Combine(_datRoot, "nointro-nes.dat"), "primary");
        File.WriteAllText(Path.Combine(_datRoot, "fbneo-nes.dat"), "supplement");

        File.WriteAllText(
            Path.Combine(_dataDir, "dat-catalog.json"),
            """
            [
                {"group":"No-Intro","system":"NES","id":"nointro-nes","consoleKey":"NES"},
                {"group":"FBNeo","system":"FBNeo NES","id":"fbneo-nes","consoleKey":"NES"}
            ]
            """);

        var map = RunEnvironmentBuilder.BuildConsoleMap(_dataDir, _datRoot, out _);

        // Fallback scan must NOT create phantom "FBNEO-NES" or "NOINTRO-NES" keys
        Assert.False(map.ContainsKey("FBNEO-NES"));
        Assert.False(map.ContainsKey("NOINTRO-NES"));
        Assert.Single(map); // Only NES
    }

    [Fact]
    public void BuildConsoleMap_SupplementalDats_MultipleSupplementalsForSameKey()
    {
        File.WriteAllText(Path.Combine(_datRoot, "primary.dat"), "primary");
        File.WriteAllText(Path.Combine(_datRoot, "supp-a.dat"), "supp-a");
        File.WriteAllText(Path.Combine(_datRoot, "supp-b.dat"), "supp-b");

        File.WriteAllText(
            Path.Combine(_dataDir, "dat-catalog.json"),
            """
            [
                {"group":"Main","system":"MD","id":"primary","consoleKey":"MD"},
                {"group":"FBNeo","system":"FBNeo MD","id":"supp-a","consoleKey":"MD"},
                {"group":"Other","system":"Other MD","id":"supp-b","consoleKey":"MD"}
            ]
            """);

        var map = RunEnvironmentBuilder.BuildConsoleMap(_dataDir, _datRoot, out var supplementalDats);

        Assert.Single(map); // Only MD
        Assert.True(supplementalDats.ContainsKey("MD"));
        Assert.Equal(2, supplementalDats["MD"].Count);
    }

    [Fact]
    public void BuildConsoleMap_SupplementalDats_EmptyWhenNoOverlap()
    {
        File.WriteAllText(Path.Combine(_datRoot, "nes.dat"), "nes");
        File.WriteAllText(Path.Combine(_datRoot, "snes.dat"), "snes");

        File.WriteAllText(
            Path.Combine(_dataDir, "dat-catalog.json"),
            """
            [
                {"group":"No-Intro","system":"NES","id":"nes","consoleKey":"NES"},
                {"group":"No-Intro","system":"SNES","id":"snes","consoleKey":"SNES"}
            ]
            """);

        var map = RunEnvironmentBuilder.BuildConsoleMap(_dataDir, _datRoot, out var supplementalDats);

        Assert.Equal(2, map.Count);
        Assert.Empty(supplementalDats);
    }

    // ── Stem-Fallback DATs for unknown platforms ───────────────────────

    [Fact]
    public void ResolveRuntimeDatConsoleKey_UnknownPlatformStem_ReturnsSanitizedKey()
    {
        // DAT stem for a platform not in consoles.json — should produce a sanitized key, not null
        var result = RunEnvironmentBuilder.ResolveRuntimeDatConsoleKey(
            "ACT - Apricot PC Xi (20211125-165629)",
            Path.Combine(_datRoot, "ACT - Apricot PC Xi (20211125-165629).dat"),
            _datRoot,
            consoleDetector: null);

        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.DoesNotContain(" ", result);
        Assert.DoesNotContain("(", result);
        Assert.DoesNotContain(")", result);
        Assert.Matches("^[A-Z0-9_-]+$", result);
    }

    [Fact]
    public void ResolveRuntimeDatConsoleKey_NoIntroDatStem_WithoutDetector_ReturnsSanitizedKey()
    {
        var result = RunEnvironmentBuilder.ResolveRuntimeDatConsoleKey(
            "Nintendo - Game Boy Advance (20260401-133204)",
            Path.Combine(_datRoot, "Nintendo - Game Boy Advance (20260401-133204).dat"),
            _datRoot,
            consoleDetector: null);

        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.Matches("^[A-Z0-9_-]+$", result);
    }

    [Fact]
    public void ResolveRuntimeDatConsoleKey_TosecDatStem_WithoutDetector_ReturnsSanitizedKey()
    {
        var result = RunEnvironmentBuilder.ResolveRuntimeDatConsoleKey(
            "3DO 3DO Interactive Multiplayer - Games (TOSEC-v2020-11-20)",
            Path.Combine(_datRoot, "3DO 3DO Interactive Multiplayer - Games (TOSEC-v2020-11-20).dat"),
            _datRoot,
            consoleDetector: null);

        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.Matches("^[A-Z0-9_-]+$", result);
    }

    [Fact]
    public void NormalizeRuntimeDatMappings_KeepsUnknownPlatformDats()
    {
        var unknownPlatformDat = Path.Combine(_datRoot, "ACT - Apricot PC Xi (20211125-165629).dat");
        var nesDat = Path.Combine(_datRoot, "NES.dat");
        File.WriteAllText(unknownPlatformDat, "apricot");
        File.WriteAllText(nesDat, "nes");

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ACT - Apricot PC Xi (20211125-165629)"] = unknownPlatformDat,
            ["NES"] = nesDat
        };
        var supplemental = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        RunEnvironmentBuilder.NormalizeRuntimeDatMappings(
            map,
            supplemental,
            consoleDetector: null,
            _datRoot,
            onWarning: null);

        // Both DATs should be kept — NES as-is, Apricot under a sanitized key
        Assert.Equal(2, map.Count);
        Assert.Contains("NES", map.Keys);
        // The sanitized key for the Apricot DAT
        Assert.Contains(unknownPlatformDat, map.Values);
    }

    [Fact]
    public void ResolveRuntimeDatConsoleKey_SentinelKeys_StillDropped()
    {
        Assert.Null(RunEnvironmentBuilder.ResolveRuntimeDatConsoleKey(
            "UNKNOWN", Path.Combine(_datRoot, "UNKNOWN.dat"), _datRoot, null));
        Assert.Null(RunEnvironmentBuilder.ResolveRuntimeDatConsoleKey(
            "AMBIGUOUS", Path.Combine(_datRoot, "AMBIGUOUS.dat"), _datRoot, null));
    }

    [Fact]
    public void ResolveRuntimeDatConsoleKey_EmptyAndWhitespace_StillDropped()
    {
        Assert.Null(RunEnvironmentBuilder.ResolveRuntimeDatConsoleKey(
            "", Path.Combine(_datRoot, "empty.dat"), _datRoot, null));
        Assert.Null(RunEnvironmentBuilder.ResolveRuntimeDatConsoleKey(
            "   ", Path.Combine(_datRoot, "space.dat"), _datRoot, null));
    }
}
