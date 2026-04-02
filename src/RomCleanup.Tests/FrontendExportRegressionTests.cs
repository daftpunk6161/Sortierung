using System.Xml.Linq;
using System.Text.Json;
using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.Export;
using RomCleanup.Infrastructure.FileSystem;
using Xunit;

namespace RomCleanup.Tests;

public sealed class FrontendExportRegressionTests : IDisposable
{
    private readonly string _tempRoot;

    public FrontendExportRegressionTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RomCleanup_FrontendExport_" + Guid.NewGuid().ToString("N"));
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
        var csv = RomCleanup.Infrastructure.Analysis.CollectionExportService.ExportCollectionCsv(
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
}
