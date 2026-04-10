using Romulus.Core.GameKeys;
using Romulus.Core.Regions;
using Romulus.Core.Scoring;
using Romulus.Contracts.Models;
using Romulus.Core.Deduplication;
using Romulus.Infrastructure.Dat;
using Romulus.Infrastructure.Reporting;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// TEST-CHAOS: Chaos, property-based, and negative/adversarial tests.
/// Covers random unicode filenames, corrupt input, ReDoS, zero-byte files,
/// adversarial paths, and boundary conditions.
/// </summary>
public sealed class ChaosTests : IDisposable
{
    private readonly string _tempDir;

    public ChaosTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_Chaos_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // ── TEST-CHAOS-01: Random unicode filenames ──

    [Theory]
    [InlineData("ゲーム (Japan)")]
    [InlineData("Игра (Russia)")]
    [InlineData("게임 (Korea)")]
    [InlineData("لعبة (UAE)")]
    [InlineData("Spiel (Germany) 🎮")]
    [InlineData("Jogo (Brazil) [!]")]
    [InlineData("Pokémon Édition Spéciale (France)")]
    public void Unicode_GameKey_NeverThrows(string input)
    {
        var ex = Record.Exception(() => GameKeyNormalizer.Normalize(input));
        Assert.Null(ex);
        Assert.NotNull(GameKeyNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("ゲーム (Japan)")]
    [InlineData("Игра (Russia)")]
    [InlineData("Pokémon (Europe)")]
    public void Unicode_RegionDetector_NeverThrows(string input)
    {
        var ex = Record.Exception(() => RegionDetector.GetRegionTag(input));
        Assert.Null(ex);
    }

    // ── TEST-CHAOS-02: Random region tags ──

    [Theory]
    [InlineData("Game (XYZ)")]
    [InlineData("Game (12345)")]
    [InlineData("Game (!!!)")]
    [InlineData("Game ()")]
    [InlineData("Game (\t\n)")]
    public void InvalidRegionTags_ReturnUnknown(string input)
    {
        var result = RegionDetector.GetRegionTag(input);
        Assert.Equal("UNKNOWN", result);
    }

    // ── TEST-CHAOS-03: Corrupt DAT file handling ──

    [Fact]
    public void CorruptDat_BinaryGarbage_DoesNotThrow()
    {
        var datPath = Path.Combine(_tempDir, "corrupt.dat");
        var random = new Random(42);
        var bytes = new byte[1024];
        random.NextBytes(bytes);
        File.WriteAllBytes(datPath, bytes);

        var dat = new DatRepositoryAdapter();
        var index = dat.GetDatIndex(_tempDir, new Dictionary<string, string> { ["TEST"] = "corrupt.dat" });
        Assert.NotNull(index);
        Assert.Equal(0, index.TotalEntries);
    }

    [Fact]
    public void CorruptDat_EmptyFile_DoesNotThrow()
    {
        File.WriteAllText(Path.Combine(_tempDir, "empty.dat"), "");

        var dat = new DatRepositoryAdapter();
        var index = dat.GetDatIndex(_tempDir,
            new Dictionary<string, string> { ["TEST"] = "empty.dat" });
        Assert.NotNull(index);
        Assert.Equal(0, index.TotalEntries);
    }

    [Fact]
    public void CorruptDat_PartialXml_DoesNotThrow()
    {
        File.WriteAllText(Path.Combine(_tempDir, "partial.dat"),
            "<?xml version=\"1.0\"?><datafile><game name=\"test\">");

        var dat = new DatRepositoryAdapter();
        var index = dat.GetDatIndex(_tempDir,
            new Dictionary<string, string> { ["TEST"] = "partial.dat" });
        Assert.NotNull(index);
    }

    // ── TEST-CHAOS-04: ReDoS regression — very long (…) groups ──

    [Fact]
    public void RegionDetector_LongParens_CompletesQuickly()
    {
        // Create a filename with deeply nested/long parenthesized groups
        var longContent = "Game " + string.Join(" ", Enumerable.Range(0, 100).Select(i => $"(Tag{i})"));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = RegionDetector.GetRegionTag(longContent);
        sw.Stop();

        Assert.NotNull(result);
        Assert.True(sw.ElapsedMilliseconds < 5000, $"Took {sw.ElapsedMilliseconds}ms — possible ReDoS");
    }

    [Fact]
    public void GameKeyNormalizer_LongInput_CompletesQuickly()
    {
        var longInput = "Game " + string.Join(" ", Enumerable.Range(0, 100).Select(i => $"(Region{i})"));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = GameKeyNormalizer.Normalize(longInput);
        sw.Stop();

        Assert.NotNull(result);
        Assert.True(sw.ElapsedMilliseconds < 5000, $"Took {sw.ElapsedMilliseconds}ms — possible ReDoS");
    }

    // ── TEST-CHAOS-05: Zero-byte files ──

    [Fact]
    public void VersionScorer_EmptyString_Zero()
    {
        var scorer = new VersionScorer();
        Assert.Equal(0, scorer.GetVersionScore(""));
        Assert.Equal(0, scorer.GetVersionScore("   "));
    }

    [Fact]
    public void FormatScorer_EmptyExtension_DefaultScore()
    {
        // Empty extension should get default score
        Assert.Equal(300, FormatScorer.GetFormatScore(""));
    }

    // ── TEST-CHAOS-06: Path traversal in filenames ──

    [Theory]
    [InlineData(@"..\..\etc\passwd")]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    [InlineData(@"/dev/null")]
    [InlineData("CON")]
    [InlineData("NUL")]
    [InlineData("PRN")]
    [InlineData("COM1")]
    [InlineData(@"\\server\share\game.zip")]
    public void AdversarialPaths_GameKey_NeverThrows(string input)
    {
        var ex = Record.Exception(() => GameKeyNormalizer.Normalize(input));
        Assert.Null(ex);
    }

    // ── TEST-CHAOS-07: Very large candidate list for dedup ──

    [Fact]
    public void Dedup_LargeGroup_CompletesQuickly()
    {
        var candidates = Enumerable.Range(0, 1000).Select(i => new RomCandidate
        {
            MainPath = $"game_{i:D4}.zip",
            GameKey = "samegame",
            RegionScore = i % 100,
            VersionScore = i % 50,
            FormatScore = 500
        }).ToArray();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var results = DeduplicationEngine.Deduplicate(candidates);
        sw.Stop();

        Assert.Single(results);
        Assert.Equal(999, results[0].Losers.Count);
        Assert.True(sw.ElapsedMilliseconds < 2000, $"Took {sw.ElapsedMilliseconds}ms");
    }

    // ── TEST-CHAOS-08: Report with adversarial entries ──

    [Fact]
    public void HtmlReport_ExtremeValues_NoCrash()
    {
        var entries = new List<ReportEntry>
        {
            new ReportEntry
            {
                GameKey = new string('X', 10000),
                Action = "KEEP",
                FileName = new string('Y', 5000),
                Extension = ".chd",
                SizeBytes = long.MaxValue
            }
        };
        var summary = new ReportSummary { TotalFiles = 1, KeepCount = 1, SavedBytes = long.MaxValue };

        var html = ReportGenerator.GenerateHtml(summary, entries);
        Assert.False(string.IsNullOrWhiteSpace(html));
        Assert.Contains("<html", html);
        Assert.Contains(new string('X', 100), html); // Extreme GameKey is HTML-present (may be encoded)
    }

    [Fact]
    public void CsvReport_EmptyEntries_StillProducesHeader()
    {
        var csv = ReportGenerator.GenerateCsv(Array.Empty<ReportEntry>());
        Assert.Contains("GameKey", csv);
    }

    // ── TEST-CHAOS: Determinism under randomized order ──

    [Fact]
    public void Dedup_RandomizedInputOrder_DeterministicWinner()
    {
        var rng = new Random(42);
        var candidates = Enumerable.Range(0, 20).Select(i => new RomCandidate
        {
            MainPath = $"game_{i:D2}.zip",
            GameKey = "samegame",
            RegionScore = (i * 7 + 3) % 20,
            VersionScore = (i * 13 + 5) % 30,
            FormatScore = 500
        }).ToArray();

        var firstWinner = DeduplicationEngine.SelectWinner(candidates);

        // Shuffle and re-run 10 times
        for (int run = 0; run < 10; run++)
        {
            var shuffled = candidates.OrderBy(_ => rng.Next()).ToArray();
            var winner = DeduplicationEngine.SelectWinner(shuffled);
            Assert.Equal(firstWinner!.MainPath, winner!.MainPath);
        }
    }
}
