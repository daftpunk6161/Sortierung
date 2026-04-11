using System.Text.Json;
using Romulus.Core.Regions;
using Romulus.Core.Scoring;
using Xunit;

namespace Romulus.Tests;

public sealed class Phase12RedTests
{
    [Fact]
    public void TD029_RegionDetector_MustNotKeepHardcodedRuleTables()
    {
        var sourcePath = ResolveRepoFile("Romulus.Core", "Regions", "RegionDetector.cs");
        Assert.True(File.Exists(sourcePath), $"Missing source file: {sourcePath}");

        var source = File.ReadAllText(sourcePath);
        Assert.DoesNotContain("DefaultOrderedRules", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RegionTokenMap = new", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TD030_FormatScorer_MustNotUseHardcodedSwitchTable()
    {
        var sourcePath = ResolveRepoFile("Romulus.Core", "Scoring", "FormatScorer.cs");
        Assert.True(File.Exists(sourcePath), $"Missing source file: {sourcePath}");

        var source = File.ReadAllText(sourcePath);
        Assert.DoesNotContain("return ext switch", source, StringComparison.Ordinal);
        Assert.DoesNotContain("switch (type.ToUpperInvariant())", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TD034_VersionScorer_DefaultCtor_MustNotHardcodeLanguagePattern()
    {
        var sourcePath = ResolveRepoFile("Romulus.Core", "Scoring", "VersionScorer.cs");
        Assert.True(File.Exists(sourcePath), $"Missing source file: {sourcePath}");

        var source = File.ReadAllText(sourcePath);
        Assert.DoesNotContain("langPattern: @\"\\((en|fr|de", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TD034_RulesJson_MustDeclareUnifiedLanguageCodeArrays()
    {
        var rulesPath = ResolveDataFile("rules.json");
        Assert.True(File.Exists(rulesPath), $"Missing data file: {rulesPath}");

        using var doc = JsonDocument.Parse(File.ReadAllText(rulesPath));
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("languageCodes", out var languageCodes), "rules.json missing 'languageCodes'.");
        Assert.Equal(JsonValueKind.Array, languageCodes.ValueKind);

        Assert.True(root.TryGetProperty("euLanguageCodes", out var euLanguageCodes), "rules.json missing 'euLanguageCodes'.");
        Assert.Equal(JsonValueKind.Array, euLanguageCodes.ValueKind);
    }

    [Fact]
    public void TD043_GoldStandardVectors_ForRegionFormatAndVersionScoring_StayDeterministic()
    {
        // Goldstandard vectors are intentionally taken from established expectations
        // in RegionDetectorTests, FormatScorerTests and VersionScorerTests.
        var regionVectors = new[]
        {
            (Input: "Game (Europe)", Expected: "EU"),
            (Input: "Game (USA)", Expected: "US"),
            (Input: "Game (Japan)", Expected: "JP"),
            (Input: "Game (USA, Asia)", Expected: "WORLD"),
            (Input: "Game (UK)", Expected: "EU"),
            (Input: "Random Game Name", Expected: "UNKNOWN")
        };

        var formatVectors = new[]
        {
            (Extension: ".chd", SetType: (string?)null, Expected: 850),
            (Extension: ".iso", SetType: (string?)null, Expected: 700),
            (Extension: ".zip", SetType: (string?)null, Expected: 500),
            (Extension: ".rar", SetType: (string?)null, Expected: 400),
            (Extension: ".xyz123", SetType: (string?)null, Expected: 300),
            (Extension: ".bin", SetType: "M3USET", Expected: 900)
        };

        var versionVectors = new[]
        {
            (Input: "Game [!]", Expected: 500L),
            (Input: "Game (Rev B)", Expected: 20L),
            (Input: "Game (v1.2.3)", Expected: 1_002_003L),
            (Input: "Game (en,de)", Expected: 85L)
        };

        var versionScorer = new VersionScorer();
        for (var run = 0; run < 50; run++)
        {
            foreach (var vector in regionVectors)
            {
                Assert.Equal(vector.Expected, RegionDetector.GetRegionTag(vector.Input));
            }

            foreach (var vector in formatVectors)
            {
                Assert.Equal(vector.Expected, FormatScorer.GetFormatScore(vector.Extension, vector.SetType));
            }

            foreach (var vector in versionVectors)
            {
                Assert.Equal(vector.Expected, versionScorer.GetVersionScore(vector.Input));
            }
        }
    }

    private static string ResolveDataFile(string fileName)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var directDataPath = Path.Combine(current.FullName, "data", fileName);
            if (File.Exists(directDataPath))
                return directDataPath;

            var srcDataPath = Path.Combine(current.FullName, "src", "Romulus.Tests", "bin", "Debug", "net10.0-windows", "data", fileName);
            if (File.Exists(srcDataPath))
                return srcDataPath;

            current = current.Parent;
        }

        return Path.Combine("data", fileName);
    }

    private static string ResolveRepoFile(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Romulus.sln")))
            {
                if (Directory.Exists(Path.Combine(current.FullName, "Romulus.Core"))
                    && Directory.Exists(Path.Combine(current.FullName, "Romulus.Tests")))
                {
                    return Path.Combine([current.FullName, .. segments]);
                }

                return Path.Combine([current.FullName, "src", .. segments]);
            }

            if (Directory.Exists(Path.Combine(current.FullName, "Romulus.Tests"))
                && Directory.Exists(Path.Combine(current.FullName, "Romulus.Core")))
            {
                return Path.Combine([current.FullName, .. segments]);
            }

            current = current.Parent;
        }

        return Path.Combine(segments);
    }
}
