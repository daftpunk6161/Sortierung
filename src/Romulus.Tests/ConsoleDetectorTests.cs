using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Romulus.Core.Classification;
using Xunit;

namespace Romulus.Tests;

public class ConsoleDetectorTests
{
    private static ConsoleDetector CreateDetector()
    {
        var consoles = new[]
        {
            new ConsoleInfo("PS1", "PlayStation", true,
                Array.Empty<string>(), Array.Empty<string>(),
                new[] { "ps1", "psx", "playstation" }),
            new ConsoleInfo("PS2", "PlayStation 2", true,
                Array.Empty<string>(), Array.Empty<string>(),
                new[] { "ps2", "playstation2", "playstation 2" }),
            new ConsoleInfo("NES", "Nintendo Entertainment System", false,
                new[] { ".nes" }, Array.Empty<string>(),
                new[] { "nes", "famicom" }),
            new ConsoleInfo("SNES", "Super Nintendo", false,
                new[] { ".sfc", ".smc" }, Array.Empty<string>(),
                new[] { "snes", "sfc", "super nintendo" }),
            new ConsoleInfo("N64", "Nintendo 64", false,
                new[] { ".n64", ".z64", ".v64" }, Array.Empty<string>(),
                new[] { "n64", "nintendo 64" }),
            new ConsoleInfo("GC", "Nintendo GameCube", true,
                Array.Empty<string>(), new[] { ".gcz", ".rvz" },
                new[] { "gc", "gamecube" }),
            new ConsoleInfo("WII", "Nintendo Wii", true,
                new[] { ".wbfs" }, new[] { ".gcz", ".rvz" },
                new[] { "wii", "nintendo wii" }),
            new ConsoleInfo("GBA", "Game Boy Advance", false,
                new[] { ".gba" }, Array.Empty<string>(),
                new[] { "gba", "game boy advance" }),
            new ConsoleInfo("MD", "Sega Mega Drive", false,
                new[] { ".md", ".gen" }, Array.Empty<string>(),
                new[] { "md", "megadrive", "genesis" }),
        };

        return new ConsoleDetector(consoles);
    }

    // ── Folder detection ────────────────────────────────────────────────

    [Theory]
    [InlineData(@"D:\Roms\PS1\game.bin", @"D:\Roms", "PS1")]
    [InlineData(@"D:\Roms\psx\game.bin", @"D:\Roms", "PS1")]
    [InlineData(@"D:\Roms\NES\Super Mario.nes", @"D:\Roms", "NES")]
    [InlineData(@"D:\Roms\snes\Zelda.sfc", @"D:\Roms", "SNES")]
    [InlineData(@"D:\Roms\gamecube\Game.iso", @"D:\Roms", "GC")]
    [InlineData(@"D:\Roms\genesis\Sonic.md", @"D:\Roms", "MD")]
    public void DetectByFolder_MatchesFolderAlias(string filePath, string root, string expected)
    {
        var detector = CreateDetector();
        Assert.Equal(expected, detector.DetectByFolder(filePath, root));
    }

    [Theory]
    [InlineData(@"D:\Roms\game.bin", @"D:\Roms")]
    [InlineData(@"D:\Roms\UNKNOWN\game.bin", @"D:\Roms")]
    public void DetectByFolder_NoMatch_ReturnsNull(string filePath, string root)
    {
        var detector = CreateDetector();
        Assert.Null(detector.DetectByFolder(filePath, root));
    }

    [Fact]
    public void DetectByFolder_CaseInsensitive()
    {
        var detector = CreateDetector();
        Assert.Equal("PS1", detector.DetectByFolder(@"D:\Roms\PS1\game.bin", @"D:\Roms"));
        Assert.Equal("PS1", detector.DetectByFolder(@"D:\Roms\ps1\game.bin", @"D:\Roms"));
        Assert.Equal("PS1", detector.DetectByFolder(@"D:\Roms\Ps1\game.bin", @"D:\Roms"));
    }

    [Theory]
    [InlineData(@"D:\Roms\PS1\game.bin", @"D:\Roms\PS1", "PS1")]
    [InlineData(@"D:\Roms\psx\game.bin", @"D:\Roms\psx", "PS1")]
    [InlineData(@"D:\Roms\NES\Super Mario.nes", @"D:\Roms\NES", "NES")]
    [InlineData(@"Y:\Games\genesis\Sonic.md", @"Y:\Games\genesis", "MD")]
    public void DetectByFolder_RootItselfIsConsoleFolder(string filePath, string root, string expected)
    {
        var detector = CreateDetector();
        Assert.Equal(expected, detector.DetectByFolder(filePath, root));
    }

    [Fact]
    public void DetectByFolder_RootIsConsoleFolder_CaseInsensitive()
    {
        var detector = CreateDetector();
        Assert.Equal("PS1", detector.DetectByFolder(@"D:\PS1\game.bin", @"D:\PS1"));
        Assert.Equal("PS1", detector.DetectByFolder(@"D:\ps1\game.bin", @"D:\ps1"));
        Assert.Equal("PS1", detector.DetectByFolder(@"D:\Ps1\game.bin", @"D:\Ps1"));
    }

    [Fact]
    public void DetectByFolder_VendorPrefixedSegment_ResolvesAliasPart()
    {
        var detector = CreateDetector();

        var detected = detector.DetectByFolder(
            @"I:\\Sony - Playstation 2\\Konvert\\game.iso",
            @"I:\\");

        Assert.Equal("PS2", detected);
    }

    [Fact]
    public void DetectByFolder_RootVendorPrefixed_ResolvesAliasPart()
    {
        var detector = CreateDetector();

        var detected = detector.DetectByFolder(
            @"I:\\Sony - Playstation 2\\game.iso",
            @"I:\\Sony - Playstation 2");

        Assert.Equal("PS2", detected);
    }

    [Fact]
    public void DetectByFolder_RootNestedUnderVendorPrefixedParent_ResolvesAliasPart()
    {
        var detector = CreateDetector();

        var detected = detector.DetectByFolder(
            @"I:\\Sony - Playstation 2\\Konvert\\game.iso",
            @"I:\\Sony - Playstation 2\\Konvert");

        Assert.Equal("PS2", detected);
    }

    [Fact]
    public void DetectWithConfidence_GenericPs1Header_WithPs2FolderHint_PrefersPs2()
    {
        var consoles = new[]
        {
            new ConsoleInfo("PS1", "PlayStation", true,
                Array.Empty<string>(), Array.Empty<string>(),
                new[] { "ps1", "psx", "playstation" }),
            new ConsoleInfo("PS2", "PlayStation 2", true,
                Array.Empty<string>(), Array.Empty<string>(),
                new[] { "ps2", "playstation2", "playstation 2" }),
        };
        var detector = new ConsoleDetector(consoles, new DiscHeaderDetector());

        var tempRoot = Path.Combine(Path.GetTempPath(), $"detector-ps2-hint-{Guid.NewGuid():N}");
        var nestedRoot = Path.Combine(tempRoot, "Sony - Playstation 2", "Konvert");
        Directory.CreateDirectory(nestedRoot);

        var data = new byte[0x8000 + 128];
        data[0x8000] = 0x01;
        Encoding.ASCII.GetBytes("CD001").CopyTo(data, 0x8001);
        Encoding.ASCII.GetBytes("PLAYSTATION").CopyTo(data, 0x8008);

        var isoPath = Path.Combine(nestedRoot, "sample.iso");
        File.WriteAllBytes(isoPath, data);

        try
        {
            var result = detector.DetectWithConfidence(isoPath, nestedRoot);
            Assert.Equal("PS2", result.ConsoleKey);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }

    // ── Extension detection ─────────────────────────────────────────────

    [Theory]
    [InlineData(".nes", "NES")]
    [InlineData(".sfc", "SNES")]
    [InlineData(".smc", "SNES")]
    [InlineData(".n64", "N64")]
    [InlineData(".z64", "N64")]
    [InlineData(".gba", "GBA")]
    [InlineData(".md", "MD")]
    [InlineData(".gen", "MD")]
    [InlineData(".wbfs", "WII")]
    public void DetectByExtension_UniqueExt_ReturnsConsole(string ext, string expected)
    {
        var detector = CreateDetector();
        Assert.Equal(expected, detector.DetectByExtension(ext));
    }

    [Theory]
    [InlineData(".iso")]
    [InlineData(".bin")]
    [InlineData(".chd")]
    [InlineData(".zip")]
    public void DetectByExtension_UnknownExt_ReturnsNull(string ext)
    {
        var detector = CreateDetector();
        Assert.Null(detector.DetectByExtension(ext));
    }

    // ── Ambiguous extension ─────────────────────────────────────────────

    [Theory]
    [InlineData(".gcz")]
    [InlineData(".rvz")]
    public void GetAmbiguousMatches_SharedExt_ReturnsMultiple(string ext)
    {
        var detector = CreateDetector();
        var matches = detector.GetAmbiguousMatches(ext);
        Assert.True(matches.Count >= 2);
        Assert.Contains("GC", matches);
        Assert.Contains("WII", matches);
    }

    [Fact]
    public void GetAmbiguousMatches_NoMatch_ReturnsEmpty()
    {
        var detector = CreateDetector();
        Assert.Empty(detector.GetAmbiguousMatches(".xyz"));
    }

    // ── Full detection pipeline ─────────────────────────────────────────

    [Fact]
    public void Detect_FolderWins_OverExtension()
    {
        var detector = CreateDetector();
        // File in PS1 folder with .nes extension → folder wins
        Assert.Equal("PS1", detector.Detect(@"D:\Roms\PS1\weird.nes", @"D:\Roms"));
    }

    [Fact]
    public void Detect_UniqueExt_WhenNoFolder()
    {
        var detector = CreateDetector();
        Assert.Equal("NES", detector.Detect(@"D:\Roms\game.nes", @"D:\Roms"));
    }

    [Fact]
    public void Detect_AmbiguousExt_SingleMatch_Returns()
    {
        // .wbfs is unique to WII (no ambiguous overlap)
        var detector = CreateDetector();
        Assert.Equal("WII", detector.Detect(@"D:\Roms\game.wbfs", @"D:\Roms"));
    }

    [Fact]
    public void Detect_Unknown_WhenNoMatch()
    {
        var detector = CreateDetector();
        Assert.Equal("UNKNOWN", detector.Detect(@"D:\Roms\game.iso", @"D:\Roms"));
    }

    [Fact]
    public void Detect_RootIsConsoleFolder_DetectsCorrectly()
    {
        // Scenario: root IS the console folder (e.g. Y:\Games\Sega CD)
        // Files are directly in root — no subfolder to check
        var consoles = new[]
        {
            new ConsoleInfo("SCD", "Sega CD", true,
                Array.Empty<string>(), new[] { ".chd" },
                new[] { "scd", "sega cd", "megacd", "mega-cd" }),
        };
        var detector = new ConsoleDetector(consoles);
        Assert.Equal("SCD", detector.Detect(@"Y:\Games\Sega CD\game.chd", @"Y:\Games\Sega CD"));
    }

    // ── Console registry ────────────────────────────────────────────────

    [Fact]
    public void IsKnownConsole_Valid()
    {
        var detector = CreateDetector();
        Assert.True(detector.IsKnownConsole("PS1"));
        Assert.True(detector.IsKnownConsole("NES"));
    }

    [Fact]
    public void IsKnownConsole_Invalid()
    {
        var detector = CreateDetector();
        Assert.False(detector.IsKnownConsole("SEGA_DOES"));
    }

    [Fact]
    public void GetConsole_ReturnsInfo()
    {
        var detector = CreateDetector();
        var ps1 = detector.GetConsole("PS1");
        Assert.NotNull(ps1);
        Assert.Equal("PlayStation", ps1!.DisplayName);
        Assert.True(ps1.DiscBased);
    }

    // ── JSON loading ────────────────────────────────────────────────────

    [Fact]
    public void LoadFromJson_ParsesConsoles()
    {
        var json = @"{
            ""consoles"": [
                {
                    ""key"": ""TEST"",
                    ""displayName"": ""Test Console"",
                    ""discBased"": false,
                    ""uniqueExts"": ["".tst""],
                    ""ambigExts"": [],
                    ""folderAliases"": [""test"", ""tst""]
                }
            ]
        }";

        var detector = ConsoleDetector.LoadFromJson(json);
        Assert.True(detector.IsKnownConsole("TEST"));
        Assert.Equal("TEST", detector.DetectByExtension(".tst"));
        Assert.Equal("TEST", detector.DetectByFolder(@"C:\roms\test\game.bin", @"C:\roms"));
    }

    [Fact]
    public void DetectByKeywordDynamic_RegexTimeout_IsNonFatalAndCounted()
    {
        var detector = CreateDetector();
        detector.SetKeywordPatternsForTesting(
        [
            (new Regex("(a+)+$", RegexOptions.Compiled, TimeSpan.FromMilliseconds(1)), "SNES")
        ]);

        var input = new string('a', 50_000) + "!";
        var result = detector.DetectByKeywordDynamic(input);

        Assert.Null(result);
        Assert.True(detector.KeywordRegexTimeoutCount > 0);
    }

    // ── Spaceless folder aliases (fix for user folders like atari2600) ───

    [Theory]
    [InlineData("atari2600", "A26")]
    [InlineData("atari5200", "A52")]
    [InlineData("atari7800", "A78")]
    [InlineData("atari800", "A800")]
    [InlineData("msx1", "MSX")]
    [InlineData("wswan", "WS")]
    [InlineData("wswanc", "WSC")]
    public void DetectByFolder_SpacelessAliases_Recognized(string folderName, string expectedKey)
    {
        var consoles = new[]
        {
            new ConsoleInfo("A26", "Atari 2600", false,
                new[] { ".a26" }, Array.Empty<string>(),
                new[] { "a26", "atari 2600", "atari2600", "vcs" }),
            new ConsoleInfo("A52", "Atari 5200", false,
                new[] { ".a52" }, Array.Empty<string>(),
                new[] { "a52", "atari 5200", "atari5200" }),
            new ConsoleInfo("A78", "Atari 7800", false,
                new[] { ".a78" }, Array.Empty<string>(),
                new[] { "a78", "atari 7800", "atari7800" }),
            new ConsoleInfo("A800", "Atari 8-bit", false,
                new[] { ".atr", ".xex", ".xfd" }, Array.Empty<string>(),
                new[] { "a800", "atari800", "atari 800" }),
            new ConsoleInfo("MSX", "MSX", false,
                new[] { ".mx1", ".mx2" }, Array.Empty<string>(),
                new[] { "msx", "msx1", "msx2" }),
            new ConsoleInfo("WS", "WonderSwan", false,
                new[] { ".ws" }, Array.Empty<string>(),
                new[] { "ws", "wswan", "wonderswan" }),
            new ConsoleInfo("WSC", "WonderSwan Color", false,
                new[] { ".wsc" }, Array.Empty<string>(),
                new[] { "wsc", "wswanc", "wonderswan color" }),
        };

        var detector = new ConsoleDetector(consoles);
        Assert.Equal(expectedKey, detector.DetectByFolder($@"D:\Roms\{folderName}\game.bin", @"D:\Roms"));
    }

    [Fact]
    public void DetectByFolder_CacheKey_NormalizesRootAndDirPaths()
    {
        var detector = CreateDetector();

        var first = detector.DetectByFolder(@"D:\Roms\PS1\game.bin", @"D:\Roms\");
        var second = detector.DetectByFolder(@"d:\roms\ps1\game.bin", @"d:\roms");

        Assert.Equal("PS1", first);
        Assert.Equal("PS1", second);
    }

    // ── Archive content detection ───────────────────────────────────────

    [Fact]
    public void DetectByArchiveContent_ZipWithNesRom_ReturnsNES()
    {
        var detector = CreateDetector();
        var zipPath = CreateTestZip("game.nes", 1024);
        try
        {
            Assert.Equal("NES", detector.DetectByArchiveContent(zipPath, ".zip"));
        }
        finally { File.Delete(zipPath); }
    }

    [Fact]
    public void DetectByArchiveContent_ZipWithGbaRom_ReturnsGBA()
    {
        var detector = CreateDetector();
        var zipPath = CreateTestZip("Pokemon.gba", 2048);
        try
        {
            Assert.Equal("GBA", detector.DetectByArchiveContent(zipPath, ".gba"));
            // .gba is NOT the outer ext  — pass .zip
            Assert.Equal("GBA", detector.DetectByArchiveContent(zipPath, ".zip"));
        }
        finally { File.Delete(zipPath); }
    }

    [Fact]
    public void DetectByArchiveContent_NonZipExtension_ReturnsNull()
    {
        var detector = CreateDetector();
        Assert.Null(detector.DetectByArchiveContent(@"D:\Roms\game.7z", ".7z"));
    }

    [Fact]
    public void DetectByArchiveContent_ZipWithUnknownInner_ReturnsNull()
    {
        var detector = CreateDetector();
        var zipPath = CreateTestZip("readme.txt", 100);
        try
        {
            Assert.Null(detector.DetectByArchiveContent(zipPath, ".zip"));
        }
        finally { File.Delete(zipPath); }
    }

    [Fact]
    public void DetectByArchiveContent_CorruptZip_ReturnsNull()
    {
        var corruptPath = Path.Combine(Path.GetTempPath(), $"corrupt_{Guid.NewGuid():N}.zip");
        File.WriteAllBytes(corruptPath, new byte[] { 0x00, 0x01, 0x02, 0x03, 0xFF });
        try
        {
            var detector = CreateDetector();
            Assert.Null(detector.DetectByArchiveContent(corruptPath, ".zip"));
        }
        finally { File.Delete(corruptPath); }
    }

    [Fact]
    public void DetectByArchiveContent_PicksLargestEntry()
    {
        // ZIP contains a small .txt and a large .sfc → should detect SNES from the .sfc
        var zipPath = Path.Combine(Path.GetTempPath(), $"multi_{Guid.NewGuid():N}.zip");
        using (var fs = new FileStream(zipPath, FileMode.Create))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var readme = archive.CreateEntry("readme.txt");
            using (var w = new StreamWriter(readme.Open())) w.Write("small");

            var rom = archive.CreateEntry("game.sfc");
            using (var s = rom.Open()) s.Write(new byte[4096]);
        }

        try
        {
            var detector = CreateDetector();
            Assert.Equal("SNES", detector.DetectByArchiveContent(zipPath, ".zip"));
        }
        finally { File.Delete(zipPath); }
    }

    [Fact]
    public void DetectByArchiveContent_SmallerRomEntry_BeatsLargerNonRomEntry()
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"archive_signal_{Guid.NewGuid():N}.zip");
        using (var fs = new FileStream(zipPath, FileMode.Create))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var image = archive.CreateEntry("cover.png");
            using (var stream = image.Open()) stream.Write(new byte[8192]);

            var rom = archive.CreateEntry("game.nes");
            using (var stream = rom.Open()) stream.Write(new byte[1024]);
        }

        try
        {
            var detector = CreateDetector();
            Assert.Equal("NES", detector.DetectByArchiveContent(zipPath, ".zip"));
        }
        finally { File.Delete(zipPath); }
    }

    [Fact]
    public void DetectByArchiveContent_DescriptorWithSerialInnerName_ReturnsConsole()
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"archive_descriptor_{Guid.NewGuid():N}.zip");
        using (var fs = new FileStream(zipPath, FileMode.Create))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var cue = archive.CreateEntry("SLUS-00001.cue");
            using (var writer = new StreamWriter(cue.Open()))
                writer.WriteLine("FILE \"track01.bin\" BINARY");

            var bin = archive.CreateEntry("track01.bin");
            using (var stream = bin.Open()) stream.Write(new byte[4096]);
        }

        try
        {
            var detector = CreateDetector();
            Assert.Equal("PS1", detector.DetectByArchiveContent(zipPath, ".zip"));
        }
        finally { File.Delete(zipPath); }
    }

    [Fact]
    public void DetectByArchiveContent_7zProvider_UsesBestInformativeEntry()
    {
        var detector = new ConsoleDetector(
            [
                new ConsoleInfo("NES", "Nintendo Entertainment System", false, [".nes"], Array.Empty<string>(), ["nes", "famicom"]),
            ],
            archiveEntryProvider: _ =>
            [
                "docs/this_is_a_very_long_readme_file_name.txt",
                "roms/game.nes",
            ]);

        Assert.Equal("NES", detector.DetectByArchiveContent(@"D:\Roms\game.7z", ".7z"));
    }

    [Fact]
    public void Detect_ZipInUnknownFolder_UsesArchiveContent()
    {
        var detector = CreateDetector();
        var zipPath = CreateTestZip("Zelda.sfc", 2048, "game.zip");
        try
        {
            // No folder match, .zip not a unique ext → should peek inside and find SNES
            var root = Path.GetDirectoryName(zipPath)!;
            Assert.Equal("SNES", detector.Detect(zipPath, root));
        }
        finally { File.Delete(zipPath); }
    }

    // ── Disc extension ambiguous matching ───────────────────────────────

    [Fact]
    public void DiscBasedConsoles_HaveAmbiguousDiscExtensions()
    {
        // Load from real consoles.json to verify data fix
        var resolvedDataDir = Romulus.Infrastructure.Orchestration.RunEnvironmentBuilder.TryResolveDataDir();
        if (resolvedDataDir is null)
            return; // Skip if not available in test context
        var jsonPath = Path.Combine(resolvedDataDir, "consoles.json");
        if (!File.Exists(jsonPath))
            return; // Skip if not available in test context

        var detector = ConsoleDetector.LoadFromJson(File.ReadAllText(jsonPath));
        var discExts = new[] { ".iso", ".bin", ".chd", ".cue", ".img" };

        foreach (var ext in discExts)
        {
            var matches = detector.GetAmbiguousMatches(ext);
            Assert.True(matches.Count >= 2,
                $"Expected disc extension {ext} to match at least 2 consoles, but got {matches.Count}: [{string.Join(", ", matches)}]");
        }
    }

    // ── Data integrity invariants (consoles.json) ───────────────────────

    [Fact]
    public void ConsolesJson_NoUniqueExtConflicts()
    {
        var resolvedDataDir = Romulus.Infrastructure.Orchestration.RunEnvironmentBuilder.TryResolveDataDir();
        if (resolvedDataDir is null) return;
        var jsonPath = Path.Combine(resolvedDataDir, "consoles.json");
        if (!File.Exists(jsonPath)) return;

        var json = File.ReadAllText(jsonPath);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var extOwners = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in doc.RootElement.GetProperty("consoles").EnumerateArray())
        {
            var key = item.GetProperty("key").GetString()!;
            if (!item.TryGetProperty("uniqueExts", out var exts)) continue;
            foreach (var ext in exts.EnumerateArray())
            {
                var e = ext.GetString()!.ToLowerInvariant();
                if (!extOwners.TryGetValue(e, out var list))
                {
                    list = new List<string>();
                    extOwners[e] = list;
                }
                list.Add(key);
            }
        }

        var conflicts = extOwners.Where(kv => kv.Value.Count > 1).ToList();
        Assert.True(conflicts.Count == 0,
            "UniqueExt conflicts found — same extension claimed by multiple consoles:\n" +
            string.Join("\n", conflicts.Select(c => $"  {c.Key} → [{string.Join(", ", c.Value)}]")));
    }

    [Fact]
    public void ConsolesJson_NoFolderAliasConflicts()
    {
        var resolvedDataDir = Romulus.Infrastructure.Orchestration.RunEnvironmentBuilder.TryResolveDataDir();
        if (resolvedDataDir is null) return;
        var jsonPath = Path.Combine(resolvedDataDir, "consoles.json");
        if (!File.Exists(jsonPath)) return;

        var json = File.ReadAllText(jsonPath);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var aliasOwners = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in doc.RootElement.GetProperty("consoles").EnumerateArray())
        {
            var key = item.GetProperty("key").GetString()!;
            if (!item.TryGetProperty("folderAliases", out var aliases)) continue;
            foreach (var alias in aliases.EnumerateArray())
            {
                var a = alias.GetString()!;
                if (!aliasOwners.TryGetValue(a, out var list))
                {
                    list = new List<string>();
                    aliasOwners[a] = list;
                }
                list.Add(key);
            }
        }

        var conflicts = aliasOwners.Where(kv => kv.Value.Count > 1).ToList();
        Assert.True(conflicts.Count == 0,
            "FolderAlias conflicts found — same alias claimed by multiple consoles:\n" +
            string.Join("\n", conflicts.Select(c => $"  \"{c.Key}\" → [{string.Join(", ", c.Value)}]")));
    }

    [Fact]
    public void ConsolesJson_NoEmptyKeys()
    {
        var resolvedDataDir = Romulus.Infrastructure.Orchestration.RunEnvironmentBuilder.TryResolveDataDir();
        if (resolvedDataDir is null) return;
        var jsonPath = Path.Combine(resolvedDataDir, "consoles.json");
        if (!File.Exists(jsonPath)) return;

        var json = File.ReadAllText(jsonPath);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var keys = new List<string>();

        foreach (var item in doc.RootElement.GetProperty("consoles").EnumerateArray())
        {
            var key = item.GetProperty("key").GetString() ?? "";
            Assert.False(string.IsNullOrWhiteSpace(key), "consoles.json contains entry with empty key");
            keys.Add(key);
        }

        var dupes = keys.GroupBy(k => k, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.True(dupes.Count == 0,
            $"Duplicate console keys: [{string.Join(", ", dupes)}]");
    }

    [Fact]
    public void ConsolesJson_NoGenericExtensionsInUniqueExts()
    {
        var resolvedDataDir = Romulus.Infrastructure.Orchestration.RunEnvironmentBuilder.TryResolveDataDir();
        if (resolvedDataDir is null) return;
        var jsonPath = Path.Combine(resolvedDataDir, "consoles.json");
        if (!File.Exists(jsonPath)) return;

        // Extensions that are too generic to be unique — they appear across many systems
        var forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".bin", ".rom", ".iso", ".img", ".cue", ".chd", ".zip", ".7z",
            ".cso", ".dat", ".exe", ".com", ".dsk"
        };

        var json = File.ReadAllText(jsonPath);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var violations = new List<string>();

        foreach (var item in doc.RootElement.GetProperty("consoles").EnumerateArray())
        {
            var key = item.GetProperty("key").GetString()!;
            if (!item.TryGetProperty("uniqueExts", out var exts)) continue;
            foreach (var ext in exts.EnumerateArray())
            {
                var e = ext.GetString()!;
                if (forbidden.Contains(e))
                    violations.Add($"{key}: {e}");
            }
        }

        Assert.True(violations.Count == 0,
            "Generic extensions found in uniqueExts (must be in ambigExts instead):\n" +
            string.Join("\n", violations.Select(v => $"  {v}")));
    }

    [Fact]
    public void KeywordAndSerialPatterns_ReferenceValidConsoleKeys()
    {
        var resolvedDataDir = Romulus.Infrastructure.Orchestration.RunEnvironmentBuilder.TryResolveDataDir();
        if (resolvedDataDir is null) return;
        var jsonPath = Path.Combine(resolvedDataDir, "consoles.json");
        if (!File.Exists(jsonPath)) return;

        var json = File.ReadAllText(jsonPath);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var validKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in doc.RootElement.GetProperty("consoles").EnumerateArray())
            validKeys.Add(item.GetProperty("key").GetString()!);

        // Verify keyword detection targets exist
        var keywordTargets = new[]
        {
            "PS1", "PS2", "PS3", "PSP", "VITA", "GC", "WII", "NDS", "3DS", "N64",
            "SNES", "NES", "GBA", "GBC", "GB", "MD", "SMS", "GG", "DC", "SAT",
            "SCD", "PCE", "NEOGEO", "ARCADE", "SWITCH", "XBOX", "X360", "32X", "LYNX", "JAG"
        };

        var missing = keywordTargets.Where(k => !validKeys.Contains(k)).ToList();
        Assert.True(missing.Count == 0,
            $"Keyword/Serial patterns reference non-existent console keys: [{string.Join(", ", missing)}]");
    }

    [Fact]
    public void ConsolesJson_DiscBasedConsoles_HaveDiscAmbigExts()
    {
        var resolvedDataDir = Romulus.Infrastructure.Orchestration.RunEnvironmentBuilder.TryResolveDataDir();
        if (resolvedDataDir is null) return;
        var jsonPath = Path.Combine(resolvedDataDir, "consoles.json");
        if (!File.Exists(jsonPath)) return;

        var json = File.ReadAllText(jsonPath);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var discExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".iso", ".bin", ".chd", ".cue", ".img" };
        var violations = new List<string>();

        foreach (var item in doc.RootElement.GetProperty("consoles").EnumerateArray())
        {
            var key = item.GetProperty("key").GetString()!;
            var isDisc = item.TryGetProperty("discBased", out var db) && db.GetBoolean();
            if (!isDisc) continue;

            var hasDiscExt = false;
            if (item.TryGetProperty("ambigExts", out var ambig))
            {
                foreach (var ext in ambig.EnumerateArray())
                {
                    if (discExts.Contains(ext.GetString()!))
                    {
                        hasDiscExt = true;
                        break;
                    }
                }
            }
            if (!hasDiscExt)
                violations.Add(key);
        }

        Assert.True(violations.Count == 0,
            $"Disc-based consoles missing disc ambigExts (.iso/.bin/.chd/.cue/.img): [{string.Join(", ", violations)}]");
    }

    [Fact]
    public void AmbiguousDiscExt_SingleDiscConsole_Returns()
    {
        // If only one disc console has a given ambig ext, Detect returns it
        var consoles = new[]
        {
            new ConsoleInfo("DC", "Sega Dreamcast", true,
                new[] { ".gdi", ".cdi" }, new[] { ".chd" },
                new[] { "dc", "dreamcast" }),
        };
        var detector = new ConsoleDetector(consoles);
        Assert.Equal("DC", detector.Detect(@"D:\Roms\game.chd", @"D:\Roms"));
    }

    [Fact]
    public void DetectWithConfidence_JunkMarkerName_StillDetectsConsoleByEvidence()
    {
        var detector = CreateDetector();
        var tempDir = Path.Combine(Path.GetTempPath(), $"detector-junk-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "Game (Demo) (USA).nes");
        File.WriteAllBytes(filePath, new byte[512]);

        try
        {
            var result = detector.DetectWithConfidence(filePath, tempDir);
            Assert.Equal("NES", result.ConsoleKey);
            Assert.InRange(result.Confidence, 90, 100);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void DetectWithConfidence_NonTinyUniqueExtension_ReturnsConsole()
    {
        var detector = CreateDetector();
        var tempDir = Path.Combine(Path.GetTempPath(), $"detector-unique-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "normal.nes");
        File.WriteAllBytes(filePath, new byte[256]);

        try
        {
            var result = detector.DetectWithConfidence(filePath, tempDir);
            Assert.Equal("NES", result.ConsoleKey);
            Assert.InRange(result.Confidence, 90, 100);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // ── Helper ──────────────────────────────────────────────────────────

    private static string CreateTestZip(string innerFileName, int innerSize, string? outerName = null)
    {
        var zipName = outerName ?? $"test_{Guid.NewGuid():N}.zip";
        var zipPath = Path.Combine(Path.GetTempPath(), zipName);
        using var fs = new FileStream(zipPath, FileMode.Create);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create);
        var entry = archive.CreateEntry(innerFileName);
        using var stream = entry.Open();
        stream.Write(new byte[innerSize]);
        return zipPath;
    }

    // ── DetectByKeywordDynamic from consoles.json ─────────────────────

    private static ConsoleDetector CreateDetectorFromJson([System.Runtime.CompilerServices.CallerFilePath] string? callerPath = null)
    {
        var repoRoot = FindRepoRoot(callerPath);
        var json = File.ReadAllText(Path.Combine(repoRoot, "data", "consoles.json"));
        return ConsoleDetector.LoadFromJson(json);
    }

    private static string FindRepoRoot(string? callerPath)
    {
        var dir = Path.GetDirectoryName(callerPath);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "src", "Romulus.sln")) ||
                File.Exists(Path.Combine(dir, "data", "consoles.json")))
                return dir;

            dir = Path.GetDirectoryName(dir);
        }

        return Directory.GetCurrentDirectory();
    }

    [Theory]
    [InlineData("[CPC] Game.dsk", "CPC")]
    [InlineData("(Amstrad CPC) Game.dsk", "CPC")]
    [InlineData("[SuperGrafx] Game.pce", "SGX")]
    [InlineData("(Satellaview) Game.sfc", "BSX")]
    [InlineData("[CDTV] Game.bin", "CDTV")]
    [InlineData("[MSX2] Game.rom", "MSX2")]
    [InlineData("[Atomiswave] Game.bin", "AWAVE")]
    [InlineData("(Naomi) Game.bin", "NAOMI")]
    public void DetectByKeywordDynamic_JsonKeywords_MatchCorrectConsole(string fileName, string expectedKey)
    {
        var detector = CreateDetectorFromJson();
        var result = detector.DetectByKeywordDynamic(fileName);
        Assert.NotNull(result);
        Assert.Equal(expectedKey, result.Value.ConsoleKey);
        Assert.Equal(75, result.Value.Confidence);
    }

    [Theory]
    [InlineData("[PS1] Game.bin", "PS1")]
    [InlineData("(GBA) Game.gba", "GBA")]
    [InlineData("[Dreamcast] Game.gdi", "DC")]
    public void DetectByKeywordDynamic_FallbackPatterns_StillWork(string fileName, string expectedKey)
    {
        // These keywords are both in consoles.json AND in hardcoded fallback.
        // Verifies the dynamic path catches them (or fallback does).
        var detector = CreateDetectorFromJson();
        var result = detector.DetectByKeywordDynamic(fileName);
        Assert.NotNull(result);
        Assert.Equal(expectedKey, result.Value.ConsoleKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Game Without Tags.bin")]
    public void DetectByKeywordDynamic_NoMatch_ReturnsNull(string fileName)
    {
        var detector = CreateDetectorFromJson();
        Assert.Null(detector.DetectByKeywordDynamic(fileName));
    }

    [Fact]
    public void DetectByKeywordDynamic_Null_ReturnsNull()
    {
        var detector = CreateDetectorFromJson();
        Assert.Null(detector.DetectByKeywordDynamic(null!));
    }

    // ── Vendor-prefix folder detection ──────────────────────────────────

    [Theory]
    [InlineData("Sony Playstation 2", "PS2")]
    [InlineData("Sony Playstation", "PS1")]
    [InlineData("Sony PSP", "PSP")]
    [InlineData("Microsoft Xbox 360", "X360")]
    [InlineData("Microsoft Xbox", "XBOX")]
    [InlineData("Sega Dreamcast", "DC")]
    [InlineData("Nintendo Game Boy Advance", "GBA")]
    [InlineData("Sega Game Gear", "GG")]
    [InlineData("Sega Genesis", "MD")]
    [InlineData("NEC PC Engine CD", "PCECD")]
    [InlineData("SNK Neo Geo", "NEOGEO")]
    [InlineData("Atari Jaguar", "JAG")]
    public void DetectByFolder_VendorPrefixWithoutHyphen_ResolvesConsole(string folderName, string expectedKey)
    {
        var detector = CreateDetectorFromJson();
        var filePath = $@"A:\Collections\{folderName}\roms\game.bin";
        var root = @"A:\Collections";
        Assert.Equal(expectedKey, detector.DetectByFolder(filePath, root));
    }

    [Theory]
    [InlineData("Sony - Playstation 2", "PS2")]
    [InlineData("Sony - PSP", "PSP")]
    [InlineData("Microsoft - Xbox 360", "X360")]
    public void DetectByFolder_VendorPrefixWithHyphen_StillWorks(string folderName, string expectedKey)
    {
        var detector = CreateDetectorFromJson();
        var filePath = $@"A:\Collections\{folderName}\roms\game.bin";
        var root = @"A:\Collections";
        Assert.Equal(expectedKey, detector.DetectByFolder(filePath, root));
    }

    [Fact]
    public void DetectByFolder_VendorPrefixCaseInsensitive()
    {
        var detector = CreateDetectorFromJson();
        Assert.Equal("PS2", detector.DetectByFolder(@"A:\SONY PLAYSTATION 2\game.bin", @"A:\"));
        Assert.Equal("PS2", detector.DetectByFolder(@"A:\sony playstation 2\game.bin", @"A:\"));
    }

    [Fact]
    public void DetectByFolder_UnknownVendorPrefix_ReturnsNull()
    {
        var detector = CreateDetectorFromJson();
        Assert.Null(detector.DetectByFolder(@"A:\Acme FooBar\game.bin", @"A:\"));
    }

    [Fact]
    public void DetectByFolder_MugenFolder_ReturnsMUGEN()
    {
        var detector = CreateDetectorFromJson();
        Assert.Equal("MUGEN", detector.DetectByFolder(@"A:\Collections\MUGEN\roms\game.zip", @"A:\Collections"));
    }

    [Fact]
    public void DetectByFolder_MugenFolder_PreventsAtariSTFalsePositive()
    {
        var detector = CreateDetectorFromJson();
        // Even though the zip may contain .st files, the folder detection should take priority
        var result = detector.Detect(@"A:\Collections\MUGEN\roms\game.zip", @"A:\Collections");
        Assert.Equal("MUGEN", result);
        Assert.NotEqual("ATARIST", result);
    }
}
