using System.Xml.Linq;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Analysis;
using Xunit;

namespace Romulus.Tests;

public sealed class DatAnalysisServiceFixDatTests
{
    [Fact]
    public void BuildFixDatFromCompleteness_GeneratesLogiqxDocumentForMissingGames()
    {
        var datIndex = new DatIndex();
        datIndex.Add("ps1", new string('a', 40), "Metal Gear Solid", "Metal Gear Solid (Disc 1).chd");
        datIndex.Add("ps1", "1a2b3c4d", "Tekken 3", null);

        var report = new CompletenessReport(
        [
            new CompletenessEntry(
                "ps1",
                TotalInDat: 2,
                Verified: 0,
                MissingCount: 2,
                Percentage: 0.0,
                MissingGames: ["Metal Gear Solid", "Tekken 3"])
        ]);

        var generatedUtc = new DateTime(2026, 04, 05, 00, 00, 00, DateTimeKind.Utc);
        var result = DatAnalysisService.BuildFixDatFromCompleteness(datIndex, report, "Romulus-Test-FixDAT", generatedUtc);

        Assert.Equal("Romulus-Test-FixDAT", result.DatName);
        Assert.Equal(1, result.ConsoleCount);
        Assert.Equal(2, result.MissingGames);
        Assert.Equal(2, result.MissingRoms);

        var doc = XDocument.Parse(result.XmlContent);
        var games = doc.Root!.Descendants("game").ToArray();
        Assert.Equal(2, games.Length);

        var mgs = Assert.Single(games, game => string.Equals(game.Attribute("name")?.Value, "Metal Gear Solid", StringComparison.OrdinalIgnoreCase));
        var mgsRom = Assert.Single(mgs.Elements("rom"));
        Assert.Equal("Metal Gear Solid (Disc 1).chd", mgsRom.Attribute("name")?.Value);
        Assert.Equal(new string('a', 40), mgsRom.Attribute("sha1")?.Value);

        var tekken = Assert.Single(games, game => string.Equals(game.Attribute("name")?.Value, "Tekken 3", StringComparison.OrdinalIgnoreCase));
        var tekkenRom = Assert.Single(tekken.Elements("rom"));
        Assert.Equal("Tekken 3.bin", tekkenRom.Attribute("name")?.Value);
        Assert.Equal("1a2b3c4d", tekkenRom.Attribute("crc")?.Value);
    }

    [Fact]
    public void BuildFixDatFromCompleteness_NoMissingGames_ReturnsHeaderOnlyDocument()
    {
        var datIndex = new DatIndex();
        datIndex.Add("snes", new string('b', 40), "Super Mario World", "Super Mario World.sfc");

        var report = new CompletenessReport(
        [
            new CompletenessEntry(
                "snes",
                TotalInDat: 1,
                Verified: 1,
                MissingCount: 0,
                Percentage: 100.0,
                MissingGames: Array.Empty<string>())
        ]);

        var generatedUtc = new DateTime(2026, 04, 05, 00, 00, 00, DateTimeKind.Utc);
        var result = DatAnalysisService.BuildFixDatFromCompleteness(datIndex, report, "Empty-FixDAT", generatedUtc);

        Assert.Equal(0, result.ConsoleCount);
        Assert.Equal(0, result.MissingGames);
        Assert.Equal(0, result.MissingRoms);

        var doc = XDocument.Parse(result.XmlContent);
        Assert.Empty(doc.Root!.Descendants("game"));
    }

    [Fact]
    public void BuildFixDatFromCompleteness_WithFixedTimestamp_IsDeterministic_AndIncludesDeclaration()
    {
        var datIndex = new DatIndex();
        datIndex.Add("psx", new string('c', 40), "Chrono Trigger", "Chrono Trigger (Disc 1).chd");

        var report = new CompletenessReport(
        [
            new CompletenessEntry(
                "psx",
                TotalInDat: 1,
                Verified: 0,
                MissingCount: 1,
                Percentage: 0.0,
                MissingGames: ["Chrono Trigger"])
        ]);

        var generatedUtc = new DateTime(2026, 04, 05, 12, 30, 45, DateTimeKind.Utc);

        var first = DatAnalysisService.BuildFixDatFromCompleteness(datIndex, report, "Fixed-FixDAT", generatedUtc);
        var second = DatAnalysisService.BuildFixDatFromCompleteness(datIndex, report, "Fixed-FixDAT", generatedUtc);

        Assert.Equal(first.XmlContent, second.XmlContent);
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", first.XmlContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<version>2026-04-05</version>", first.XmlContent, StringComparison.Ordinal);
    }
}
