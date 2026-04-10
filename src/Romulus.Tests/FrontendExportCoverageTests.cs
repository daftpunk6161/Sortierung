using System.Text.Json;
using System.Xml.Linq;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Export;
using Romulus.Infrastructure.FileSystem;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for FrontendExportService branches not exercised by regression tests.
/// Targets: RetroArch file/dir, LaunchBox dir, ES file, Playnite file/dir, JSON, Excel,
/// empty games, M3u fallback, merged M3u, unsupported frontend, fallback candidate factory.
/// </summary>
public sealed class FrontendExportCoverageTests : IDisposable
{
    private readonly string _tempRoot;

    public FrontendExportCoverageTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "FEC_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private static RomCandidate MakeCandidate(string path, string consoleKey, string gameKey = "game", string region = "US", string ext = ".zip")
        => new()
        {
            MainPath = path,
            GameKey = gameKey,
            ConsoleKey = consoleKey,
            Region = region,
            Extension = ext,
            SizeBytes = 1024,
            DatMatch = true,
            Category = FileCategory.Game
        };

    private FrontendExportRequest MakeRequest(string frontend, string outputPath, string collection = "Test")
        => new(frontend, outputPath, collection, [@"C:\roms"], [".zip", ".chd", ".gba"]);

    #region RetroArch

    [Fact]
    public async Task RetroArch_SingleFile_WritesJsonPlaylist()
    {
        var output = Path.Combine(_tempRoot, "retroarch.lpl");
        var candidates = new[] { MakeCandidate(@"C:\roms\snes\Game.zip", "snes") };

        var result = await FrontendExportService.ExportAsync(
            MakeRequest(FrontendExportTargets.RetroArch, output),
            new FileSystemAdapter(), null, null, runCandidates: candidates);

        Assert.Equal(FrontendExportTargets.RetroArch, result.Frontend);
        Assert.Single(result.Artifacts);
        var content = await File.ReadAllTextAsync(output);
        using var json = JsonDocument.Parse(content);
        Assert.Equal("1.5", json.RootElement.GetProperty("version").GetString());
        Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task RetroArch_Directory_GroupsByConsole()
    {
        var output = Path.Combine(_tempRoot, "ra-dir");
        var candidates = new[]
        {
            MakeCandidate(@"C:\roms\snes\Game1.zip", "snes", "game1"),
            MakeCandidate(@"C:\roms\gb\Game2.zip", "gb", "game2")
        };

        var result = await FrontendExportService.ExportAsync(
            MakeRequest(FrontendExportTargets.RetroArch, output),
            new FileSystemAdapter(), null, null, runCandidates: candidates);

        Assert.Equal(2, result.Artifacts.Count);
        Assert.All(result.Artifacts, a => Assert.EndsWith(".lpl", a.Path, StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region LaunchBox

    [Fact]
    public async Task LaunchBox_Directory_GroupsByConsole()
    {
        var output = Path.Combine(_tempRoot, "lb-dir");
        var candidates = new[]
        {
            MakeCandidate(@"C:\roms\snes\Game1.zip", "snes", "g1"),
            MakeCandidate(@"C:\roms\nes\Game2.zip", "nes", "g2")
        };

        var result = await FrontendExportService.ExportAsync(
            MakeRequest(FrontendExportTargets.LaunchBox, output),
            new FileSystemAdapter(), null, null, runCandidates: candidates);

        Assert.Equal(2, result.Artifacts.Count);
        Assert.All(result.Artifacts, a => Assert.EndsWith(".xml", a.Path, StringComparison.OrdinalIgnoreCase));

        foreach (var artifact in result.Artifacts)
        {
            var xml = XDocument.Load(artifact.Path);
            Assert.NotNull(xml.Root?.Element("Game"));
        }
    }

    #endregion

    #region EmulationStation

    [Fact]
    public async Task EmulationStation_SingleFile_WritesGameListXml()
    {
        var output = Path.Combine(_tempRoot, "es.xml");
        var candidates = new[] { MakeCandidate(@"C:\roms\gba\Tetris.gba", "gba", ext: ".gba") };

        var result = await FrontendExportService.ExportAsync(
            MakeRequest(FrontendExportTargets.EmulationStation, output),
            new FileSystemAdapter(), null, null, runCandidates: candidates);

        Assert.Single(result.Artifacts);
        var xml = XDocument.Load(output);
        Assert.Equal("gameList", xml.Root!.Name.LocalName);
        Assert.NotNull(xml.Root.Element("game"));
    }

    #endregion

    #region Playnite

    [Fact]
    public async Task Playnite_SingleFile_WritesLibraryJson()
    {
        var output = Path.Combine(_tempRoot, "playnite.json");
        var candidates = new[] { MakeCandidate(@"C:\roms\ps1\FF7.chd", "ps1", "ff7", ext: ".chd") };

        var result = await FrontendExportService.ExportAsync(
            MakeRequest(FrontendExportTargets.Playnite, output),
            new FileSystemAdapter(), null, null, runCandidates: candidates);

        Assert.Single(result.Artifacts);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(output));
        Assert.Equal("playnite-library", json.RootElement.GetProperty("format").GetString());
        Assert.Single(json.RootElement.GetProperty("games").EnumerateArray());
    }

    [Fact]
    public async Task Playnite_Directory_WritesPerGameJsonFiles()
    {
        var output = Path.Combine(_tempRoot, "playnite-dir");
        var candidates = new[]
        {
            MakeCandidate(@"C:\roms\ps1\FF7.chd", "ps1", "ff7", ext: ".chd"),
            MakeCandidate(@"C:\roms\ps1\FF8.chd", "ps1", "ff8", ext: ".chd")
        };

        var result = await FrontendExportService.ExportAsync(
            MakeRequest(FrontendExportTargets.Playnite, output),
            new FileSystemAdapter(), null, null, runCandidates: candidates);

        Assert.Equal(2, result.Artifacts.Count);
        Assert.All(result.Artifacts, a => Assert.EndsWith(".json", a.Path, StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region JSON / Excel

    [Fact]
    public async Task Json_Export_WritesValidJson()
    {
        var output = Path.Combine(_tempRoot, "collection.json");
        var candidates = new[] { MakeCandidate(@"C:\roms\snes\Game.zip", "snes") };

        var result = await FrontendExportService.ExportAsync(
            MakeRequest(FrontendExportTargets.Json, output),
            new FileSystemAdapter(), null, null, runCandidates: candidates);

        Assert.Equal(FrontendExportTargets.Json, result.Frontend);
        Assert.Single(result.Artifacts);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(output));
        Assert.Single(json.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task Excel_Export_WritesXml()
    {
        var output = Path.Combine(_tempRoot, "collection.xml");
        var candidates = new[] { MakeCandidate(@"C:\roms\snes\Game.zip", "snes") };

        var result = await FrontendExportService.ExportAsync(
            MakeRequest(FrontendExportTargets.Excel, output),
            new FileSystemAdapter(), null, null, runCandidates: candidates);

        Assert.Equal(FrontendExportTargets.Excel, result.Frontend);
        Assert.Single(result.Artifacts);
        var content = await File.ReadAllTextAsync(output);
        Assert.Contains("Workbook", content, StringComparison.Ordinal);
    }

    #endregion

    #region M3u edge cases

    [Fact]
    public async Task M3u_Directory_NoDiscEntries_WritesFallbackPlaylist()
    {
        var output = Path.Combine(_tempRoot, "m3u-fallback");
        // Games without disc markers → no multi-disc playlists → fallback path
        var candidates = new[]
        {
            MakeCandidate(@"C:\roms\ps1\Tekken 3.chd", "ps1", "tekken", ext: ".chd"),
            MakeCandidate(@"C:\roms\ps1\Ridge Racer.chd", "ps1", "ridge", ext: ".chd")
        };

        var result = await FrontendExportService.ExportAsync(
            MakeRequest(FrontendExportTargets.M3u, output, "MyCollection"),
            new FileSystemAdapter(), null, null, runCandidates: candidates);

        Assert.Single(result.Artifacts);
        var artifact = result.Artifacts[0];
        Assert.EndsWith("MyCollection.m3u", artifact.Path, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, artifact.ItemCount);

        var content = await File.ReadAllTextAsync(artifact.Path);
        Assert.Contains("#EXTM3U", content, StringComparison.Ordinal);
        Assert.Contains("Tekken 3", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task M3u_SingleFile_MergedMultiPlaylist_GroupsMultiDiscSeries()
    {
        var output = Path.Combine(_tempRoot, "merged.m3u");
        var candidates = new[]
        {
            MakeCandidate(@"C:\roms\ps1\FF7 (Disc 1).chd", "ps1", "ff7-d1", ext: ".chd"),
            MakeCandidate(@"C:\roms\ps1\FF7 (Disc 2).chd", "ps1", "ff7-d2", ext: ".chd"),
            MakeCandidate(@"C:\roms\ps1\MGS (Disc 1).chd", "ps1", "mgs-d1", ext: ".chd"),
            MakeCandidate(@"C:\roms\ps1\MGS (Disc 2).chd", "ps1", "mgs-d2", ext: ".chd")
        };

        var result = await FrontendExportService.ExportAsync(
            MakeRequest(FrontendExportTargets.M3u, output, "Multi"),
            new FileSystemAdapter(), null, null, runCandidates: candidates);

        Assert.Single(result.Artifacts);
        var content = await File.ReadAllTextAsync(output);
        // Merged mode: multiple playlists → GROUP headers
        Assert.Contains("#GROUP:", content, StringComparison.Ordinal);
        Assert.Contains("FF7", content, StringComparison.Ordinal);
        Assert.Contains("MGS", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task M3u_SingleFile_SinglePlaylist_NoCgroups()
    {
        var output = Path.Combine(_tempRoot, "single-series.m3u");
        // Only one multi-disc series → merged mode with playlists.Count == 1 → simple M3u
        var candidates = new[]
        {
            MakeCandidate(@"C:\roms\ps1\FF7 (Disc 1).chd", "ps1", "ff7-d1", ext: ".chd"),
            MakeCandidate(@"C:\roms\ps1\FF7 (Disc 2).chd", "ps1", "ff7-d2", ext: ".chd")
        };

        var result = await FrontendExportService.ExportAsync(
            MakeRequest(FrontendExportTargets.M3u, output, "Single"),
            new FileSystemAdapter(), null, null, runCandidates: candidates);

        Assert.Single(result.Artifacts);
        var content = await File.ReadAllTextAsync(output);
        // Single playlist mode: uses playlist name, not collection name
        Assert.Contains("#PLAYLIST:", content, StringComparison.Ordinal);
        Assert.DoesNotContain("#GROUP:", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task M3u_SingleFile_NoConvertibleEntries_FallsBackToAllGames()
    {
        var output = Path.Combine(_tempRoot, "no-discs.m3u");
        var candidates = new[] { MakeCandidate(@"C:\roms\snes\Zelda.zip", "snes", "zelda") };

        var result = await FrontendExportService.ExportAsync(
            MakeRequest(FrontendExportTargets.M3u, output, "NoDiscs"),
            new FileSystemAdapter(), null, null, runCandidates: candidates);

        Assert.Single(result.Artifacts);
        var content = await File.ReadAllTextAsync(output);
        Assert.Contains("#PLAYLIST:NoDiscs", content, StringComparison.Ordinal);
        Assert.Contains("Zelda", content, StringComparison.Ordinal);
    }

    #endregion

    #region Error paths

    [Fact]
    public async Task UnsupportedFrontend_ThrowsInvalidOperationException()
    {
        var candidates = new[] { MakeCandidate(@"C:\roms\snes\Game.zip", "snes") };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            FrontendExportService.ExportAsync(
                new FrontendExportRequest("nonexistent-frontend", Path.Combine(_tempRoot, "out.txt"), "C", [@"C:\roms"], [".zip"]),
                new FileSystemAdapter(), null, null, runCandidates: candidates));
    }

    [Fact]
    public async Task NullRequest_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            FrontendExportService.ExportAsync(null!, new FileSystemAdapter(), null, null));
    }

    [Fact]
    public async Task NullFileSystem_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            FrontendExportService.ExportAsync(
                MakeRequest(FrontendExportTargets.Csv, Path.Combine(_tempRoot, "out.csv")),
                null!, null, null));
    }

    [Fact]
    public async Task NoRunCandidates_NoIndex_NoFactory_ThrowsInvalidOperationException()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            FrontendExportService.ExportAsync(
                MakeRequest(FrontendExportTargets.Json, Path.Combine(_tempRoot, "out.json")),
                new FileSystemAdapter(), null, null));
    }

    [Fact]
    public async Task FallbackCandidateFactory_UsedWhenNoIndexAndNoRunCandidates()
    {
        var output = Path.Combine(_tempRoot, "fallback.json");
        var factoryCalled = false;
        var candidates = new[] { MakeCandidate(@"C:\roms\snes\FromFactory.zip", "snes", "factory") };

        var result = await FrontendExportService.ExportAsync(
            MakeRequest(FrontendExportTargets.Json, output),
            new FileSystemAdapter(), null, null,
            fallbackCandidateFactory: ct =>
            {
                factoryCalled = true;
                return Task.FromResult<IReadOnlyList<RomCandidate>>(candidates);
            });

        Assert.True(factoryCalled);
        Assert.Equal(1, result.GameCount);
    }

    #endregion

    #region Empty games

    [Fact]
    public async Task RetroArch_EmptyCollection_ProducesValidOutput()
    {
        var output = Path.Combine(_tempRoot, "empty.lpl");
        // Empty runCandidates (Count==0) falls through to fallback factory
        var result = await FrontendExportService.ExportAsync(
            MakeRequest(FrontendExportTargets.RetroArch, output),
            new FileSystemAdapter(), null, null,
            fallbackCandidateFactory: _ => Task.FromResult<IReadOnlyList<RomCandidate>>(Array.Empty<RomCandidate>()));

        Assert.Equal(0, result.GameCount);
        Assert.Single(result.Artifacts);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(output));
        Assert.Empty(json.RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task LaunchBox_EmptyCollection_WritesValidXml()
    {
        var output = Path.Combine(_tempRoot, "empty-lb.xml");
        var result = await FrontendExportService.ExportAsync(
            MakeRequest(FrontendExportTargets.LaunchBox, output),
            new FileSystemAdapter(), null, null,
            fallbackCandidateFactory: _ => Task.FromResult<IReadOnlyList<RomCandidate>>(Array.Empty<RomCandidate>()));

        Assert.Equal(0, result.GameCount);
        var xml = XDocument.Load(output);
        Assert.Equal("LaunchBox", xml.Root!.Name.LocalName);
        Assert.Empty(xml.Root.Elements("Game"));
    }

    [Fact]
    public async Task Playnite_EmptyCollection_WritesValidJson()
    {
        var output = Path.Combine(_tempRoot, "empty-pn.json");
        var result = await FrontendExportService.ExportAsync(
            MakeRequest(FrontendExportTargets.Playnite, output),
            new FileSystemAdapter(), null, null,
            fallbackCandidateFactory: _ => Task.FromResult<IReadOnlyList<RomCandidate>>(Array.Empty<RomCandidate>()));

        Assert.Equal(0, result.GameCount);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(output));
        Assert.Empty(json.RootElement.GetProperty("games").EnumerateArray());
    }

    #endregion

    #region NonGame filtering

    [Fact]
    public async Task Export_FiltersOutNonGameCandidates()
    {
        var output = Path.Combine(_tempRoot, "filtered.json");
        var candidates = new[]
        {
            MakeCandidate(@"C:\roms\snes\RealGame.zip", "snes", "real"),
            new RomCandidate
            {
                MainPath = @"C:\roms\snes\README.txt",
                GameKey = "readme",
                ConsoleKey = "snes",
                Region = "US",
                Extension = ".txt",
                SizeBytes = 100,
                DatMatch = false,
                Category = FileCategory.Junk
            }
        };

        var result = await FrontendExportService.ExportAsync(
            MakeRequest(FrontendExportTargets.Json, output),
            new FileSystemAdapter(), null, null, runCandidates: candidates);

        Assert.Equal(1, result.GameCount);
    }

    #endregion

    #region MiSTer / AnaloguePocket / OnionOS single-file mode

    [Fact]
    public async Task MiSTer_SingleFile_WritesManifestJson()
    {
        var output = Path.Combine(_tempRoot, "mister.json");
        var candidates = new[] { MakeCandidate(@"C:\roms\snes\Game.zip", "snes") };

        var result = await FrontendExportService.ExportAsync(
            MakeRequest(FrontendExportTargets.MiSTer, output),
            new FileSystemAdapter(), null, null, runCandidates: candidates);

        Assert.Single(result.Artifacts);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(output));
        Assert.Equal("mister-library", json.RootElement.GetProperty("format").GetString());
    }

    [Fact]
    public async Task AnaloguePocket_SingleFile_WritesManifestJson()
    {
        var output = Path.Combine(_tempRoot, "pocket.json");
        var candidates = new[] { MakeCandidate(@"C:\roms\gba\Game.gba", "gba", ext: ".gba") };

        var result = await FrontendExportService.ExportAsync(
            MakeRequest(FrontendExportTargets.AnaloguePocket, output),
            new FileSystemAdapter(), null, null, runCandidates: candidates);

        Assert.Single(result.Artifacts);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(output));
        Assert.Equal("analogue-pocket-library", json.RootElement.GetProperty("format").GetString());
    }

    [Fact]
    public async Task OnionOs_SingleFile_WritesManifestJson()
    {
        var output = Path.Combine(_tempRoot, "onion.json");
        var candidates = new[] { MakeCandidate(@"C:\roms\gb\Tetris.gb", "gb", ext: ".gb") };

        var result = await FrontendExportService.ExportAsync(
            MakeRequest(FrontendExportTargets.OnionOs, output),
            new FileSystemAdapter(), null, null, runCandidates: candidates);

        Assert.Single(result.Artifacts);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(output));
        Assert.Equal("onionos-library", json.RootElement.GetProperty("format").GetString());
    }

    #endregion
}
