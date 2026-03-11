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
}
