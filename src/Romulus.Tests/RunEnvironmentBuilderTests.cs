using Romulus.Contracts.Models;
using Romulus.Core.Classification;
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
        var settings = new RomulusSettings();

        using var env = RunEnvironmentBuilder.Build(options, settings, _dataDir, warnings.Add);

        Assert.NotNull(env.FileSystem);
        Assert.NotNull(env.Audit);
        Assert.Contains(warnings, w => w.Contains("DAT enabled", StringComparison.OrdinalIgnoreCase));
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
}
