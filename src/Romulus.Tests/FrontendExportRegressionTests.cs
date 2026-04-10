using System.Xml.Linq;
using System.Text.Json;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Export;
using Romulus.Infrastructure.FileSystem;
using Xunit;

namespace Romulus.Tests;

public sealed class FrontendExportRegressionTests : IDisposable
{
    private readonly string _tempRoot;

    public FrontendExportRegressionTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "Romulus_FrontendExport_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public async Task FrontendExportService_LaunchBox_EscapesXmlValues_AndRemainsParsable()
    {
        var outputPath = Path.Combine(_tempRoot, "launchbox.xml");
        var candidates = new[]
        {
            new RomCandidate
            {
                MainPath = @"C:\roms\snes\Super <Boss> & Friends.zip",
                GameKey = "super-boss",
                ConsoleKey = "snes",
                Region = "EU",
                Extension = ".zip",
                SizeBytes = 1234,
                DatMatch = true,
                Category = FileCategory.Game
            }
        };

        var result = await FrontendExportService.ExportAsync(
            new FrontendExportRequest(
                FrontendExportTargets.LaunchBox,
                outputPath,
                "Collection",
                [@"C:\roms"],
                [".zip"]),
            new FileSystemAdapter(),
            collectionIndex: null,
            enrichmentFingerprint: null,
            runCandidates: candidates);

        Assert.Equal(FrontendExportTargets.LaunchBox, result.Frontend);
        Assert.Equal(1, result.GameCount);
        Assert.Single(result.Artifacts);

        var xml = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("Super &lt;Boss&gt; &amp; Friends", xml, StringComparison.Ordinal);

        var document = XDocument.Parse(xml);
        var title = document.Root!
            .Element("Game")!
            .Element("Title")!
            .Value;
        Assert.Equal("Super <Boss> & Friends", title);
    }

    [Fact]
    public async Task FrontendExportService_EmulationStation_DirectoryExport_StaysWithinOutputRoot()
    {
        var outputDirectory = Path.Combine(_tempRoot, "export-root");
        var candidates = new[]
        {
            new RomCandidate
            {
                MainPath = @"C:\roms\unsafe\Game One.zip",
                GameKey = "game-one",
                ConsoleKey = "..\\..\\evil",
                Region = "US",
                Extension = ".zip",
                SizeBytes = 321,
                DatMatch = false,
                Category = FileCategory.Game
            }
        };

        var result = await FrontendExportService.ExportAsync(
            new FrontendExportRequest(
                FrontendExportTargets.EmulationStation,
                outputDirectory,
                "Collection",
                [@"C:\roms"],
                [".zip"]),
            new FileSystemAdapter(),
            collectionIndex: null,
            enrichmentFingerprint: null,
            runCandidates: candidates);

        var artifact = Assert.Single(result.Artifacts);
        var fullRoot = Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullArtifactPath = Path.GetFullPath(artifact.Path);
        Assert.StartsWith(fullRoot, fullArtifactPath, StringComparison.OrdinalIgnoreCase);
        var expectedDirectory = Path.Combine(Path.GetFullPath(outputDirectory), "_.._evil");
        Assert.Equal(expectedDirectory, Path.GetDirectoryName(fullArtifactPath), StringComparer.OrdinalIgnoreCase);
        Assert.EndsWith("gamelist.xml", fullArtifactPath, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(fullArtifactPath));
    }

    [Fact]
    public async Task FrontendExportService_Csv_QuotesDangerousFormulaPrefixes()
    {
        var outputPath = Path.Combine(_tempRoot, "collection.csv");
        var candidates = new[]
        {
            new RomCandidate
            {
                MainPath = "=2+3.zip",
                GameKey = "formula-test",
                ConsoleKey = "snes",
                Region = "US",
                Extension = ".zip",
                SizeBytes = 256,
                DatMatch = true,
                Category = FileCategory.Game
            }
        };

        var result = await FrontendExportService.ExportAsync(
            new FrontendExportRequest(
                FrontendExportTargets.Csv,
                outputPath,
                "Collection",
                [@"C:\roms"],
                [".zip"]),
            new FileSystemAdapter(),
            collectionIndex: null,
            enrichmentFingerprint: null,
            runCandidates: candidates);

        Assert.Equal(FrontendExportTargets.Csv, result.Frontend);
        Assert.Single(result.Artifacts);

        var lines = await File.ReadAllLinesAsync(outputPath);
        Assert.Equal(2, lines.Length);

        var dataLine = lines[1];
        Assert.StartsWith("\"=2+3.zip\";", dataLine, StringComparison.Ordinal);
        Assert.EndsWith(";\"=2+3.zip\"", dataLine, StringComparison.Ordinal);
    }

    [Fact]
    public void CollectionExportService_Csv_QuotesSemicolonFields_WhenDelimiterIsSemicolon()
    {
        var csv = Romulus.Infrastructure.Analysis.CollectionExportService.ExportCollectionCsv(
        [
            new RomCandidate
            {
                MainPath = @"C:\roms\A;B\Game;One.zip",
                GameKey = "game-one",
                ConsoleKey = "SNES",
                Region = "EU",
                Extension = ".zip",
                SizeBytes = 2048,
                Category = FileCategory.Game
            }
        ]);

        var dataLine = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)[1];
        Assert.Contains("\"Game;One.zip\"", dataLine, StringComparison.Ordinal);
        Assert.EndsWith(";\"C:\\roms\\A;B\\Game;One.zip\"", dataLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FrontendExportService_Csv_FileMode_BlocksProtectedSystemPath()
    {
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windowsDir))
            return;

        var outputPath = Path.Combine(windowsDir, "romulus-export.csv");
        var candidates = new[]
        {
            new RomCandidate
            {
                MainPath = @"C:\roms\safe\Game.zip",
                GameKey = "safe-game",
                ConsoleKey = "SNES",
                Region = "US",
                Extension = ".zip",
                SizeBytes = 1024,
                DatMatch = true,
                Category = FileCategory.Game
            }
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            FrontendExportService.ExportAsync(
                new FrontendExportRequest(
                    FrontendExportTargets.Csv,
                    outputPath,
                    "Collection",
                    [@"C:\roms"],
                    [".zip"]),
                new FileSystemAdapter(),
                collectionIndex: null,
                enrichmentFingerprint: null,
                runCandidates: candidates));

        Assert.Contains("protected system path", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FrontendExportService_MiSTer_DirectoryExport_WritesGamesManifestWithCoreMapping()
    {
        var outputDirectory = Path.Combine(_tempRoot, "mister-export");
        var candidates = new[]
        {
            new RomCandidate
            {
                MainPath = @"C:\roms\ps1\Chrono Cross.chd",
                GameKey = "chrono-cross",
                ConsoleKey = "ps1",
                Region = "US",
                Extension = ".chd",
                SizeBytes = 42,
                DatMatch = true,
                Category = FileCategory.Game
            }
        };

        var result = await FrontendExportService.ExportAsync(
            new FrontendExportRequest(
                FrontendExportTargets.MiSTer,
                outputDirectory,
                "Collection",
                [@"C:\roms"],
                [".chd"]),
            new FileSystemAdapter(),
            collectionIndex: null,
            enrichmentFingerprint: null,
            runCandidates: candidates);

        Assert.Equal(FrontendExportTargets.MiSTer, result.Frontend);
        var artifact = Assert.Single(result.Artifacts);
        Assert.Contains(Path.Combine("games", "ps1"), artifact.Path, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("_romulus-index.json", artifact.Path, StringComparison.OrdinalIgnoreCase);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(artifact.Path));
        Assert.Equal("ps1", json.RootElement.GetProperty("system").GetString());
        Assert.Equal("mednafen_psx_hw_libretro", json.RootElement.GetProperty("core").GetString());
    }

    [Fact]
    public async Task FrontendExportService_AnaloguePocket_DirectoryExport_WritesAssetsLibrary()
    {
        var outputDirectory = Path.Combine(_tempRoot, "pocket-export");
        var candidates = new[]
        {
            new RomCandidate
            {
                MainPath = @"C:\roms\gba\Golden Sun.gba",
                GameKey = "golden-sun",
                ConsoleKey = "gba",
                Region = "EU",
                Extension = ".gba",
                SizeBytes = 1024,
                DatMatch = true,
                Category = FileCategory.Game
            }
        };

        var result = await FrontendExportService.ExportAsync(
            new FrontendExportRequest(
                FrontendExportTargets.AnaloguePocket,
                outputDirectory,
                "Collection",
                [@"C:\roms"],
                [".gba"]),
            new FileSystemAdapter(),
            collectionIndex: null,
            enrichmentFingerprint: null,
            runCandidates: candidates);

        Assert.Equal(FrontendExportTargets.AnaloguePocket, result.Frontend);
        var artifact = Assert.Single(result.Artifacts);
        Assert.Contains(Path.Combine("Assets", "gba"), artifact.Path, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("library.json", artifact.Path, StringComparison.OrdinalIgnoreCase);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(artifact.Path));
        var game = json.RootElement.GetProperty("games")[0];
        Assert.Contains("Assets/gba", game.GetProperty("assetPath").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FrontendExportService_OnionOs_DirectoryExport_WritesRomsList()
    {
        var outputDirectory = Path.Combine(_tempRoot, "onion-export");
        var candidates = new[]
        {
            new RomCandidate
            {
                MainPath = @"C:\roms\gb\Tetris.gb",
                GameKey = "tetris",
                ConsoleKey = "gb",
                Region = "WORLD",
                Extension = ".gb",
                SizeBytes = 2048,
                DatMatch = false,
                Category = FileCategory.Game
            }
        };

        var result = await FrontendExportService.ExportAsync(
            new FrontendExportRequest(
                FrontendExportTargets.OnionOs,
                outputDirectory,
                "Collection",
                [@"C:\roms"],
                [".gb"]),
            new FileSystemAdapter(),
            collectionIndex: null,
            enrichmentFingerprint: null,
            runCandidates: candidates);

        Assert.Equal(FrontendExportTargets.OnionOs, result.Frontend);
        var artifact = Assert.Single(result.Artifacts);
        Assert.Contains(Path.Combine("Roms", "gb"), artifact.Path, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("romlist.json", artifact.Path, StringComparison.OrdinalIgnoreCase);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(artifact.Path));
        var rom = json.RootElement.GetProperty("roms")[0];
        Assert.Contains("Roms/gb", rom.GetProperty("targetPath").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FrontendExportService_M3u_DirectoryExport_GroupsAndOrdersDiscEntries()
    {
        var outputDirectory = Path.Combine(_tempRoot, "m3u-export");
        var candidates = new[]
        {
            new RomCandidate
            {
                MainPath = @"C:\roms\ps1\Metal Gear Solid (Disc 2).chd",
                GameKey = "metal-gear-solid",
                ConsoleKey = "ps1",
                Region = "US",
                Extension = ".chd",
                SizeBytes = 1024,
                DatMatch = true,
                Category = FileCategory.Game
            },
            new RomCandidate
            {
                MainPath = @"C:\roms\ps1\Metal Gear Solid (Disc 1).chd",
                GameKey = "metal-gear-solid",
                ConsoleKey = "ps1",
                Region = "US",
                Extension = ".chd",
                SizeBytes = 1024,
                DatMatch = true,
                Category = FileCategory.Game
            },
            new RomCandidate
            {
                MainPath = @"C:\roms\ps1\Tekken 3.chd",
                GameKey = "tekken-3",
                ConsoleKey = "ps1",
                Region = "US",
                Extension = ".chd",
                SizeBytes = 2048,
                DatMatch = true,
                Category = FileCategory.Game
            }
        };

        var result = await FrontendExportService.ExportAsync(
            new FrontendExportRequest(
                FrontendExportTargets.M3u,
                outputDirectory,
                "Collection",
                [@"C:\roms"],
                [".chd"]),
            new FileSystemAdapter(),
            collectionIndex: null,
            enrichmentFingerprint: null,
            runCandidates: candidates);

        Assert.Equal(FrontendExportTargets.M3u, result.Frontend);
        var artifact = Assert.Single(result.Artifacts);
        Assert.EndsWith(".m3u", artifact.Path, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(artifact.Path));

        var content = await File.ReadAllTextAsync(artifact.Path);
        var disc1Path = candidates[1].MainPath.Replace('\\', '/');
        var disc2Path = candidates[0].MainPath.Replace('\\', '/');

        var disc1Index = content.IndexOf(disc1Path, StringComparison.Ordinal);
        var disc2Index = content.IndexOf(disc2Path, StringComparison.Ordinal);

        Assert.True(disc1Index >= 0);
        Assert.True(disc2Index >= 0);
        Assert.True(disc1Index < disc2Index, "Disc 1 entry must appear before Disc 2.");
        Assert.DoesNotContain("Tekken 3", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FrontendExportService_M3u_SingleFileExport_SanitizesControlCharsAndCommentPrefix()
    {
        var outputPath = Path.Combine(_tempRoot, "single-playlist.m3u");
        var candidates = new[]
        {
            new RomCandidate
            {
                MainPath = "#Saga (Disc 1).chd\r\n#EXTINF:-1,HACK",
                GameKey = "saga",
                ConsoleKey = "ps1",
                Region = "US",
                Extension = ".chd",
                SizeBytes = 1024,
                DatMatch = true,
                Category = FileCategory.Game
            },
            new RomCandidate
            {
                MainPath = "#Saga (Disc 2).chd",
                GameKey = "saga",
                ConsoleKey = "ps1",
                Region = "US",
                Extension = ".chd",
                SizeBytes = 1024,
                DatMatch = true,
                Category = FileCategory.Game
            }
        };

        var result = await FrontendExportService.ExportAsync(
            new FrontendExportRequest(
                FrontendExportTargets.M3u,
                outputPath,
                "Collection",
                ["C:/roms"],
                [".chd"]),
            new FileSystemAdapter(),
            collectionIndex: null,
            enrichmentFingerprint: null,
            runCandidates: candidates);

        Assert.Equal(FrontendExportTargets.M3u, result.Frontend);
        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal(Path.GetFullPath(outputPath), artifact.Path, StringComparer.OrdinalIgnoreCase);

        var content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("#PLAYLIST:", content, StringComparison.Ordinal);
        Assert.DoesNotContain("\n#EXTINF:-1,HACK", content, StringComparison.Ordinal);
        Assert.Contains("_#Saga (Disc 1).chd", content, StringComparison.Ordinal);
        Assert.Contains("_#Saga (Disc 2).chd", content, StringComparison.Ordinal);
    }
}
