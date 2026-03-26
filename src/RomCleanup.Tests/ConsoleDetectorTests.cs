using System.IO.Compression;
using RomCleanup.Core.Classification;
using Xunit;

namespace RomCleanup.Tests;

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
        var resolvedDataDir = RomCleanup.Infrastructure.Orchestration.RunEnvironmentBuilder.TryResolveDataDir();
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
}
