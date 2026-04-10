using System.Text.Json;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Analysis;
using Romulus.UI.Wpf.Services;
using Xunit;

namespace Romulus.Tests;

public sealed class CollectionConsoleResolutionTests
{
    [Fact]
    public void ResolveConsoleLabel_PrefersCandidateConsoleKey_WhenPathDiffers()
    {
        var candidate = new RomCandidate
        {
            MainPath = @"D:\Roms\Misc\game.sfc",
            ConsoleKey = "SNES"
        };

        var resolved = CollectionAnalysisService.ResolveConsoleLabel(candidate);

        Assert.Equal("SNES", resolved);
    }

    [Fact]
    public void GetDuplicateHeatmap_PrefersCandidateConsoleKey_WhenPathDiffers()
    {
        DedupeGroup[] groups =
        [
            new DedupeGroup
            {
                GameKey = "game",
                Winner = new RomCandidate
                {
                    MainPath = @"D:\Roms\Misc\game.sfc",
                    ConsoleKey = "SNES"
                },
                Losers =
                [
                    new RomCandidate
                    {
                        MainPath = @"D:\Roms\Misc\game-copy.sfc",
                        ConsoleKey = "SNES"
                    }
                ]
            }
        ];

        var result = CollectionAnalysisService.GetDuplicateHeatmap(groups);

        var entry = Assert.Single(result);
        Assert.Equal("SNES", entry.Console);
        Assert.Equal(2, entry.Total);
        Assert.Equal(1, entry.Duplicates);
    }

    [Fact]
    public void ExportCollectionCsv_PrefersCandidateConsoleKey_WhenPathDiffers()
    {
        var csv = CollectionExportService.ExportCollectionCsv(
        [
            new RomCandidate
            {
                MainPath = @"D:\Roms\Misc\game.sfc",
                ConsoleKey = "SNES",
                Region = "EU",
                Extension = ".sfc",
                SizeBytes = 1024,
                Category = FileCategory.Game
            }
        ]);

        var dataLine = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)[1];
        var fields = dataLine.Split(';');

        Assert.Equal("SNES", fields[1]);
    }

    [Fact]
    public void ExportRetroArchPlaylist_PrefersCandidateConsoleKey_ForCoreLookup()
    {
        var json = CollectionAnalysisService.ExportRetroArchPlaylist(
        [
            new RomCandidate
            {
                MainPath = @"D:\Roms\Misc\game.sfc",
                ConsoleKey = "snes",
                Region = "EU",
                Extension = ".sfc",
                SizeBytes = 1024
            }
        ], "MyList");

        using var document = JsonDocument.Parse(json);
        var item = document.RootElement.GetProperty("items")[0];

        Assert.Equal("snes9x_libretro", item.GetProperty("core_path").GetString());
    }

    [Fact]
    public void FeatureService_ResolveField_Console_PrefersCandidateConsoleKey_WhenPathDiffers()
    {
        var candidate = new RomCandidate
        {
            MainPath = @"D:\Roms\Misc\game.sfc",
            ConsoleKey = "SNES"
        };

        var value = FeatureService.ResolveField(candidate, "console");

        Assert.Equal("SNES", value);
    }

    [Fact]
    public void FeatureService_ExportCollectionCsv_PrefersCandidateConsoleKey_WhenPathDiffers()
    {
        var csv = FeatureService.ExportCollectionCsv(
        [
            new RomCandidate
            {
                MainPath = @"D:\Roms\Misc\game.sfc",
                ConsoleKey = "SNES",
                Region = "EU",
                Extension = ".sfc",
                SizeBytes = 1024,
                Category = FileCategory.Game
            }
        ]);

        var dataLine = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)[1];
        var fields = dataLine.Split(';');

        Assert.Equal("SNES", fields[1]);
    }
}
