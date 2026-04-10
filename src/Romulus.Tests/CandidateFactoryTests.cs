using Romulus.Contracts.Models;
using Romulus.Core.Classification;
using Xunit;

namespace Romulus.Tests;

public class CandidateFactoryTests
{
    // --- BIOS Isolation: BIOS items get __BIOS__ prefix to prevent grouping with games ---

    [Fact]
    public void Create_BiosCategory_PrefixesGameKey()
    {
        var candidate = CandidateFactory.Create(
            normalizedPath: "bios.bin",
            extension: ".bin",
            sizeBytes: 1024,
            category: FileCategory.Bios,
            gameKey: "scph1001",
            region: "US",
            regionScore: 1000,
            formatScore: 700,
            versionScore: 0,
            headerScore: 0,
            completenessScore: 50,
            sizeTieBreakScore: 1024,
            datMatch: true,
            consoleKey: "ps1");

        Assert.StartsWith("__BIOS__", candidate.GameKey);
        Assert.Equal("__BIOS__US__scph1001", candidate.GameKey);
    }

    [Fact]
    public void Create_GameCategory_NoPrefix()
    {
        var candidate = CandidateFactory.Create(
            normalizedPath: "game.zip",
            extension: ".zip",
            sizeBytes: 2048,
            category: FileCategory.Game,
            gameKey: "mario",
            region: "EU",
            regionScore: 1000,
            formatScore: 500,
            versionScore: 500,
            headerScore: 0,
            completenessScore: 75,
            sizeTieBreakScore: -2048,
            datMatch: false,
            consoleKey: "nes");

        Assert.Equal("mario", candidate.GameKey);
    }

    [Theory]
    [InlineData(FileCategory.Junk)]
    [InlineData(FileCategory.NonGame)]
    [InlineData(FileCategory.Unknown)]
    public void Create_NonBiosCategory_NoPrefix(FileCategory category)
    {
        var candidate = CandidateFactory.Create(
            normalizedPath: "file.zip",
            extension: ".zip",
            sizeBytes: 512,
            category: category,
            gameKey: "testkey",
            region: "EU",
            regionScore: 500,
            formatScore: 300,
            versionScore: 0,
            headerScore: 0,
            completenessScore: 0,
            sizeTieBreakScore: -512,
            datMatch: false,
            consoleKey: "snes");

        Assert.Equal("testkey", candidate.GameKey);
    }

    // --- BIOS and Game with same base key produce different GameKeys ---

    [Fact]
    public void Create_BiosAndGameSameBaseKey_DifferentGameKeys()
    {
        var bios = CandidateFactory.Create(
            normalizedPath: "bios.bin", extension: ".bin", sizeBytes: 1024,
            category: FileCategory.Bios, gameKey: "shared",
            region: "US", regionScore: 1000, formatScore: 700,
            versionScore: 0, headerScore: 0, completenessScore: 50,
            sizeTieBreakScore: 1024, datMatch: true, consoleKey: "ps1");

        var game = CandidateFactory.Create(
            normalizedPath: "game.chd", extension: ".chd", sizeBytes: 500_000,
            category: FileCategory.Game, gameKey: "shared",
            region: "EU", regionScore: 1000, formatScore: 850,
            versionScore: 500, headerScore: 0, completenessScore: 75,
            sizeTieBreakScore: 500_000, datMatch: false, consoleKey: "ps1");

        Assert.NotEqual(bios.GameKey, game.GameKey);
    }

    [Fact]
    public void Create_BiosSameBaseKeyDifferentRegions_DifferentGameKeys()
    {
        var biosUs = CandidateFactory.Create(
            normalizedPath: "bios-us.bin", extension: ".bin", sizeBytes: 1024,
            category: FileCategory.Bios, gameKey: "scph1001",
            region: "US", regionScore: 1000, formatScore: 700,
            versionScore: 0, headerScore: 0, completenessScore: 50,
            sizeTieBreakScore: 1024, datMatch: true, consoleKey: "ps1");

        var biosEu = CandidateFactory.Create(
            normalizedPath: "bios-eu.bin", extension: ".bin", sizeBytes: 1024,
            category: FileCategory.Bios, gameKey: "scph1001",
            region: "EU", regionScore: 1000, formatScore: 700,
            versionScore: 0, headerScore: 0, completenessScore: 50,
            sizeTieBreakScore: 1024, datMatch: true, consoleKey: "ps1");

        Assert.NotEqual(biosUs.GameKey, biosEu.GameKey);
        Assert.Equal("__BIOS__US__scph1001", biosUs.GameKey);
        Assert.Equal("__BIOS__EU__scph1001", biosEu.GameKey);
    }

    // --- All properties are correctly forwarded ---

    [Fact]
    public void Create_AllPropertiesForwarded()
    {
        var candidate = CandidateFactory.Create(
            normalizedPath: "/roms/game.chd",
            extension: ".chd",
            sizeBytes: 999_999,
            category: FileCategory.Game,
            gameKey: "testgame",
            region: "JP",
            regionScore: 998,
            formatScore: 850,
            versionScore: 1575,
            headerScore: 10,
            completenessScore: 100,
            sizeTieBreakScore: 999_999,
            datMatch: true,
            consoleKey: "ps2",
            classificationReasonCode: "game-verified",
            classificationConfidence: 95);

        Assert.Equal("/roms/game.chd", candidate.MainPath);
        Assert.Equal(".chd", candidate.Extension);
        Assert.Equal(999_999, candidate.SizeBytes);
        Assert.Equal(FileCategory.Game, candidate.Category);
        Assert.Equal("testgame", candidate.GameKey);
        Assert.Equal("JP", candidate.Region);
        Assert.Equal(998, candidate.RegionScore);
        Assert.Equal(850, candidate.FormatScore);
        Assert.Equal(1575, candidate.VersionScore);
        Assert.Equal(10, candidate.HeaderScore);
        Assert.Equal(100, candidate.CompletenessScore);
        Assert.Equal(999_999, candidate.SizeTieBreakScore);
        Assert.True(candidate.DatMatch);
        Assert.Equal("ps2", candidate.ConsoleKey);
        Assert.Equal("game-verified", candidate.ClassificationReasonCode);
        Assert.Equal(95, candidate.ClassificationConfidence);
    }
}
