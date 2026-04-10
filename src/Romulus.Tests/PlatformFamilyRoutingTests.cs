using Romulus.Contracts.Models;
using Romulus.Core.Classification;
using Xunit;

namespace Romulus.Tests;

public sealed class PlatformFamilyRoutingTests
{
    [Fact]
    public void LoadFromJson_ReadsFamilyHashStrategyAndDatSources()
    {
        const string json = """
        {
          "consoles": [
            {
              "key": "PS1",
              "displayName": "Sony PlayStation",
              "discBased": true,
              "family": "RedumpDisc",
              "hashStrategy": "track-sha1",
              "datSources": ["redump", "non-redump"],
              "uniqueExts": ["cue"],
              "ambigExts": ["bin"],
              "folderAliases": ["PS1"],
              "keywords": ["PS1"]
            }
          ]
        }
        """;

        var detector = ConsoleDetector.LoadFromJson(json);
        var console = detector.GetConsole("PS1");

        Assert.NotNull(console);
        Assert.Equal(PlatformFamily.RedumpDisc, detector.GetPlatformFamily("PS1"));
        Assert.Equal("track-sha1", console!.HashStrategy);
        Assert.Equal(new[] { "redump", "non-redump" }, console.DatSources);
    }

    [Fact]
    public void LoadFromJson_DiscBasedWithoutFamily_DefaultsToRedumpDisc()
    {
        const string json = """
        {
          "consoles": [
            {
              "key": "SAT",
              "displayName": "Sega Saturn",
              "discBased": true,
              "hashStrategy": "track-sha1",
              "datSources": ["redump"],
              "uniqueExts": ["cue"],
              "ambigExts": ["bin"],
              "folderAliases": ["SAT"]
            }
          ]
        }
        """;

        var detector = ConsoleDetector.LoadFromJson(json);

        Assert.Equal(PlatformFamily.RedumpDisc, detector.GetPlatformFamily("SAT"));
    }

    [Fact]
    public void LoadFromJson_NonDiscWithoutFamily_DefaultsToUnknown()
    {
        const string json = """
        {
          "consoles": [
            {
              "key": "NES",
              "displayName": "Nintendo Entertainment System",
              "discBased": false,
              "hashStrategy": "headerless-sha1",
              "datSources": ["nointro"],
              "uniqueExts": ["nes"],
              "ambigExts": [],
              "folderAliases": ["NES"]
            }
          ]
        }
        """;

        var detector = ConsoleDetector.LoadFromJson(json);

        Assert.Equal(PlatformFamily.Unknown, detector.GetPlatformFamily("NES"));
        Assert.Equal(PlatformFamily.Unknown, detector.GetPlatformFamily("DOES_NOT_EXIST"));
    }
}
