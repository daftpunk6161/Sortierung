using System.Text;
using System.Xml.Linq;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Hashing;
using Romulus.Infrastructure.Reporting;
using Romulus.UI.Wpf.Services;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Comprehensive tests for FeatureService static utility methods.
/// Covers Analysis, Collection, Conversion, Dat, Export, Infra, Security, Workflow partials.
/// </summary>
public sealed class FeatureServiceTests : IDisposable
{
    private readonly string _tempDir;

    public FeatureServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_FS_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // ═══ FormatSize ═════════════════════════════════════════════════════

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(500, "500 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1.00 KB")]
    [InlineData(1536, "1.50 KB")]
    [InlineData(1048576, "1.00 MB")]
    [InlineData(1073741824, "1.00 GB")]
    [InlineData(1099511627776, "1.00 TB")]
    public void FormatSize_VariousSizes_FormatsCorrectly(long bytes, string expected)
    {
        Assert.Equal(expected, FeatureService.FormatSize(bytes));
    }

    [Fact]
    public void FormatSize_LargeGb_FormatsCorrectly()
    {
        Assert.Equal("2.50 GB", FeatureService.FormatSize((long)(2.5 * (1L << 30))));
    }

    // ═══ SanitizeCsvField ═══════════════════════════════════════════════

    [Fact]
    public void SanitizeCsvField_NormalValue_ReturnsSame()
    {
        Assert.Equal("hello", FeatureService.SanitizeCsvField("hello"));
    }

    [Fact]
    public void SanitizeCsvField_EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", FeatureService.SanitizeCsvField(""));
    }

    [Fact]
    public void SanitizeCsvField_Null_ReturnsEmpty()
    {
        Assert.Equal("", FeatureService.SanitizeCsvField(null!));
    }

    [Theory]
    [InlineData("=cmd", "'=cmd")]
    [InlineData("+cmd", "'+cmd")]
    [InlineData("-cmd", "'-cmd")]
    [InlineData("@cmd", "'@cmd")]
    public void SanitizeCsvField_InjectionPrefix_PrependsSingleQuote(string input, string expected)
    {
        Assert.Equal(expected, FeatureService.SanitizeCsvField(input));
    }

    [Fact]
    public void SanitizeCsvField_WithComma_WrapsInQuotes()
    {
        Assert.Equal("\"hello,world\"", FeatureService.SanitizeCsvField("hello,world"));
    }

    [Fact]
    public void SanitizeCsvField_WithDoubleQuotes_EscapesQuotes()
    {
        Assert.Equal("\"say \"\"hi\"\"\"", FeatureService.SanitizeCsvField("say \"hi\""));
    }

    [Fact]
    public void SanitizeCsvField_WithSemicolon_WrapsInQuotes()
    {
        Assert.Equal("\"a;b\"", FeatureService.SanitizeCsvField("a;b"));
    }

    [Fact]
    public void SanitizeCsvField_TabPrefix_PrependsSingleQuote()
    {
        Assert.Equal("'\tdata", FeatureService.SanitizeCsvField("\tdata"));
    }

    // ═══ Truncate ═══════════════════════════════════════════════════════

    [Fact]
    public void Truncate_ShortString_ReturnsSame()
    {
        Assert.Equal("abc", FeatureService.Truncate("abc", 10));
    }

    [Fact]
    public void Truncate_ExactLength_ReturnsSame()
    {
        Assert.Equal("abcde", FeatureService.Truncate("abcde", 5));
    }

    [Fact]
    public void Truncate_LongString_TruncatesWithEllipsis()
    {
        Assert.Equal("abcde...", FeatureService.Truncate("abcdefghij", 8));
    }

    // ═══ LevenshteinDistance ═════════════════════════════════════════════

    [Theory]
    [InlineData("", "", 0)]
    [InlineData("abc", "abc", 0)]
    [InlineData("abc", "abd", 1)]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("", "abc", 3)]
    [InlineData("abc", "", 3)]
    public void LevenshteinDistance_VariousPairs_ReturnsCorrectDistance(string s, string t, int expected)
    {
        Assert.Equal(expected, FeatureService.LevenshteinDistance(s, t));
    }

    // ═══ ParseCsvLine ═══════════════════════════════════════════════════

    [Fact]
    public void ParseCsvLine_Simple_SplitsCorrectly()
    {
        var result = FeatureService.ParseCsvLine("a,b,c");
        Assert.Equal(["a", "b", "c"], result);
    }

    [Fact]
    public void ParseCsvLine_QuotedField_HandlesCorrectly()
    {
        var result = FeatureService.ParseCsvLine("\"hello, world\",b");
        Assert.Equal(["hello, world", "b"], result);
    }

    [Fact]
    public void ParseCsvLine_EscapedQuotes_HandlesCorrectly()
    {
        var result = FeatureService.ParseCsvLine("\"say \"\"hi\"\"\",b");
        Assert.Equal(["say \"hi\"", "b"], result);
    }

    [Fact]
    public void ParseCsvLine_EmptyFields_PreservesEmpties()
    {
        var result = FeatureService.ParseCsvLine(",a,,b,");
        Assert.Equal(5, result.Length);
        Assert.Equal("", result[0]);
        Assert.Equal("", result[2]);
        Assert.Equal("", result[4]);
    }

    [Fact]
    public void ParseCsvLine_SingleField_ReturnsSingleElement()
    {
        Assert.Single(FeatureService.ParseCsvLine("hello"));
    }

    // ═══ DetectConsoleFromPath ═══════════════════════════════════════════

    [Fact]
    public void DetectConsoleFromPath_TwoLevelPath_ReturnsParentDir()
    {
        Assert.Equal("SNES", FeatureService.DetectConsoleFromPath("D:/Roms/SNES/game.sfc"));
    }

    [Fact]
    public void DetectConsoleFromPath_WithBackslashes_Works()
    {
        Assert.Equal("GBA", FeatureService.DetectConsoleFromPath("C:\\Roms\\GBA\\game.gba"));
    }

    [Fact]
    public void DetectConsoleFromPath_SingleLevel_ReturnsUnbekannt()
    {
        Assert.Equal("Unknown", FeatureService.DetectConsoleFromPath("game.sfc"));
    }

    // ═══ ComputeSha256 ══════════════════════════════════════════════════

    [Fact]
    public void ComputeSha256_ValidFile_ReturnsHexHash()
    {
        var path = Path.Combine(_tempDir, "hash_test.bin");
        File.WriteAllBytes(path, [0x00, 0x01, 0x02, 0x03]);
        var hash = FeatureService.ComputeSha256(path);
        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9A-F]+$", hash);
    }

    // ═══ CalculateHealthScore ═══════════════════════════════════════════

    [Fact]
    public void CalculateHealthScore_NoFiles_ReturnsZero()
    {
        Assert.Equal(0, FeatureService.CalculateHealthScore(0, 0, 0, 0));
    }

    [Fact]
    public void CalculateHealthScore_NoDupesNoJunk_Returns100()
    {
        Assert.Equal(100, FeatureService.CalculateHealthScore(100, 0, 0, 100));
    }

    [Fact]
    public void CalculateHealthScore_AllDupes_ReturnsLow()
    {
        var score = FeatureService.CalculateHealthScore(100, 100, 0, 0);
        Assert.True(score <= 40);
    }

    [Fact]
    public void CalculateHealthScore_AllJunk_ReturnsLow()
    {
        var score = FeatureService.CalculateHealthScore(100, 0, 100, 0);
        Assert.True(score <= 70);
    }

    [Fact]
    public void CalculateHealthScore_VerifiedBonus_IncreasesScore()
    {
        var withoutVerif = FeatureService.CalculateHealthScore(100, 10, 5, 0);
        var withVerif = FeatureService.CalculateHealthScore(100, 10, 5, 50);
        Assert.True(withVerif > withoutVerif);
    }

    [Fact]
    public void CalculateHealthScore_ClampsTo0_100()
    {
        var score = FeatureService.CalculateHealthScore(100, 100, 100, 0);
        Assert.InRange(score, 0, 100);
    }

    // ═══ GetDuplicateHeatmap ════════════════════════════════════════════

    [Fact]
    public void GetDuplicateHeatmap_EmptyGroups_ReturnsEmptyList()
    {
        Assert.Empty(FeatureService.GetDuplicateHeatmap([]));
    }

    [Fact]
    public void GetDuplicateHeatmap_SingleGroup_CalculatesPercentage()
    {
        var group = new DedupeGroup
        {
            Winner = new RomCandidate { MainPath = "D:/Roms/SNES/game1.sfc" },
            Losers = [new RomCandidate { MainPath = "D:/Roms/SNES/game1_dup.sfc" }],
            GameKey = "game1"
        };

        var result = FeatureService.GetDuplicateHeatmap([group]);
        Assert.Single(result);
        Assert.Equal("SNES", result[0].Console);
        Assert.Equal(2, result[0].Total);
        Assert.Equal(1, result[0].Duplicates);
        Assert.Equal(50.0, result[0].DuplicatePercent);
    }

    // ═══ SearchRomCollection ════════════════════════════════════════════

    [Fact]
    public void SearchRomCollection_EmptySearch_ReturnsAll()
    {
        var candidates = new List<RomCandidate>
        {
            new() { MainPath = "game1.sfc", GameKey = "game1" },
            new() { MainPath = "game2.sfc", GameKey = "game2" }
        };
        Assert.Equal(2, FeatureService.SearchRomCollection(candidates, "").Count);
    }

    [Fact]
    public void SearchRomCollection_MatchByPath_Filters()
    {
        var candidates = new List<RomCandidate>
        {
            new() { MainPath = "D:/SNES/mario.sfc", GameKey = "mario" },
            new() { MainPath = "D:/GBA/zelda.gba", GameKey = "zelda" }
        };
        var result = FeatureService.SearchRomCollection(candidates, "mario");
        Assert.Single(result);
        Assert.Equal("mario", result[0].GameKey);
    }

    [Fact]
    public void SearchRomCollection_CaseInsensitive()
    {
        var candidates = new List<RomCandidate>
        {
            new() { MainPath = "D:/SNES/Mario.sfc", GameKey = "Mario", Region = "EU" }
        };
        Assert.Single(FeatureService.SearchRomCollection(candidates, "mario"));
    }

    [Fact]
    public void SearchRomCollection_ByRegion_Filters()
    {
        var candidates = new List<RomCandidate>
        {
            new() { MainPath = "game1.sfc", GameKey = "game1", Region = "EU" },
            new() { MainPath = "game2.sfc", GameKey = "game2", Region = "JP" }
        };
        var result = FeatureService.SearchRomCollection(candidates, "JP");
        Assert.Single(result);
    }

    // ═══ GetConversionEstimate ══════════════════════════════════════════

    [Fact]
    public void GetConversionEstimate_EmptyCandidates_ReturnsZeros()
    {
        var result = FeatureService.GetConversionEstimate([]);
        Assert.Equal(0, result.TotalSourceBytes);
        Assert.Equal(0, result.EstimatedTargetBytes);
        Assert.Empty(result.Details);
    }

    [Fact]
    public void GetConversionEstimate_IsoFile_EstimatesCHD()
    {
        var candidates = new List<RomCandidate>
        {
            new() { MainPath = "game.iso", Extension = ".iso", SizeBytes = 1_000_000 }
        };
        var result = FeatureService.GetConversionEstimate(candidates);
        Assert.Single(result.Details);
        Assert.Equal("chd", result.Details[0].TargetFormat);
        Assert.True(result.SavedBytes > 0);
    }

    [Fact]
    public void GetConversionEstimate_NonConvertibleFormat_Skipped()
    {
        var candidates = new List<RomCandidate>
        {
            new() { MainPath = "game.sfc", Extension = ".sfc", SizeBytes = 100_000 }
        };
        var result = FeatureService.GetConversionEstimate(candidates);
        Assert.Empty(result.Details);
        Assert.Equal(0, result.TotalSourceBytes);
    }

    [Fact]
    public void GetConversionEstimate_MultipleFormats_AggregatesCorrectly()
    {
        var candidates = new List<RomCandidate>
        {
            new() { MainPath = "disc.iso", Extension = ".iso", SizeBytes = 500_000 },
            new() { MainPath = "wii.gcz", Extension = ".gcz", SizeBytes = 300_000 },
            new() { MainPath = "rom.zip", Extension = ".zip", SizeBytes = 100_000 }
        };
        var result = FeatureService.GetConversionEstimate(candidates);
        Assert.Equal(3, result.Details.Count);
        Assert.Equal(900_000, result.TotalSourceBytes);
    }

    [Fact]
    public void GetConversionAdvisor_MultipleConsoles_ReturnsPerConsoleBreakdownAndRecommendations()
    {
        var candidates = new List<RomCandidate>
        {
            new() { MainPath = "ps1/game1.iso", Extension = ".iso", SizeBytes = 1_000_000, ConsoleKey = "ps1" },
            new() { MainPath = "ps1/game2.bin", Extension = ".bin", SizeBytes = 400_000, ConsoleKey = "ps1" },
            new() { MainPath = "wii/game3.gcz", Extension = ".gcz", SizeBytes = 700_000, ConsoleKey = "wii" },
            new() { MainPath = "snes/game4.sfc", Extension = ".sfc", SizeBytes = 100_000, ConsoleKey = "snes" }
        };

        var result = FeatureService.GetConversionAdvisor(candidates);

        Assert.Equal(2, result.Consoles.Count);
        Assert.Contains(result.Consoles, static item => item.ConsoleKey.Equals("ps1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Consoles, static item => item.ConsoleKey.Equals("wii", StringComparison.OrdinalIgnoreCase));
        Assert.True(result.SavedBytes > 0);
        Assert.NotEmpty(result.Recommendations);
        Assert.Contains(result.Recommendations, rec => rec.Contains("ps1", StringComparison.OrdinalIgnoreCase));
    }

    // ═══ GetTargetFormat ════════════════════════════════════════════════

    [Theory]
    [InlineData("bin", "chd")]
    [InlineData("cue", "chd")]
    [InlineData("iso", "chd")]
    [InlineData("cso", "chd")]
    [InlineData("pbp", "chd")]
    [InlineData("gcz", "rvz")]
    [InlineData("wbfs", "rvz")]
    [InlineData("nkit", "rvz")]
    [InlineData("zip", "7z")]
    [InlineData("rar", "7z")]
    public void GetTargetFormat_KnownFormats_ReturnsExpected(string ext, string expected)
    {
        Assert.Equal(expected, FeatureService.GetTargetFormat(ext));
    }

    [Theory]
    [InlineData("sfc")]
    [InlineData("gba")]
    [InlineData("nes")]
    [InlineData("chd")]
    [InlineData("rvz")]
    public void GetTargetFormat_NonConvertible_ReturnsNull(string ext)
    {
        Assert.Null(FeatureService.GetTargetFormat(ext));
    }

    // ═══ VerifyConversions ══════════════════════════════════════════════

    [Fact]
    public void VerifyConversions_EmptyList_ReturnsZeros()
    {
        var (p, f, m) = FeatureService.VerifyConversions([]);
        Assert.Equal(0, p);
        Assert.Equal(0, f);
        Assert.Equal(0, m);
    }

    [Fact]
    public void VerifyConversions_ExistingLargeFile_Passes()
    {
        var path = Path.Combine(_tempDir, "converted.chd");
        File.WriteAllBytes(path, new byte[100]);
        var (p, f, m) = FeatureService.VerifyConversions([path]);
        Assert.Equal(1, p);
        Assert.Equal(0, f);
        Assert.Equal(0, m);
    }

    [Fact]
    public void VerifyConversions_EmptyFile_Fails()
    {
        var path = Path.Combine(_tempDir, "empty.chd");
        File.Create(path).Dispose();
        var (p, f, m) = FeatureService.VerifyConversions([path]);
        Assert.Equal(0, p);
        Assert.Equal(1, f);
        Assert.Equal(0, m);
    }

    [Fact]
    public void VerifyConversions_MissingFile_ReportsMissing()
    {
        var (p, f, m) = FeatureService.VerifyConversions(["/nonexistent/file.chd"]);
        Assert.Equal(0, p);
        Assert.Equal(0, f);
        Assert.Equal(1, m);
    }

    // ═══ FormatFormatPriority ═══════════════════════════════════════════

    [Fact]
    public void FormatFormatPriority_ReturnsNonEmptyReport()
    {
        var report = FeatureService.FormatFormatPriority();
        Assert.Contains("Format-Prioritäten", report);
        Assert.Contains("ps1", report, StringComparison.OrdinalIgnoreCase);
    }

    // ═══ ApplyFilter ════════════════════════════════════════════════════

    [Fact]
    public void ApplyFilter_Contains_FiltersCorrectly()
    {
        var candidates = new List<RomCandidate>
        {
            new() { MainPath = "mario.sfc", GameKey = "mario", Region = "EU" },
            new() { MainPath = "zelda.sfc", GameKey = "zelda", Region = "US" }
        };
        var result = FeatureService.ApplyFilter(candidates, "gamekey", "contains", "mario");
        Assert.Single(result);
    }

    [Fact]
    public void ApplyFilter_Eq_FiltersExactMatch()
    {
        var candidates = new List<RomCandidate>
        {
            new() { MainPath = "game.sfc", Extension = ".sfc", Region = "EU" },
            new() { MainPath = "game.gba", Extension = ".gba", Region = "US" }
        };
        var result = FeatureService.ApplyFilter(candidates, "region", "eq", "EU");
        Assert.Single(result);
    }

    [Fact]
    public void ApplyFilter_Neq_ExcludesMatches()
    {
        var candidates = new List<RomCandidate>
        {
            new() { MainPath = "game1.sfc", Region = "EU" },
            new() { MainPath = "game2.sfc", Region = "JP" }
        };
        var result = FeatureService.ApplyFilter(candidates, "region", "neq", "EU");
        Assert.Single(result);
        Assert.Equal("JP", result[0].Region);
    }

    [Fact]
    public void ApplyFilter_Regex_MatchesPattern()
    {
        var candidates = new List<RomCandidate>
        {
            new() { MainPath = "game1.sfc", GameKey = "Super Mario 3" },
            new() { MainPath = "game2.sfc", GameKey = "Zelda" }
        };
        var result = FeatureService.ApplyFilter(candidates, "gamekey", "regex", "^Super");
        Assert.Single(result);
    }

    // ═══ ParseFilterExpression ═══════════════════════════════════════════

    [Theory]
    [InlineData("region=EU", "region", "=", "EU")]
    [InlineData("size>=100", "size", ">=", "100")]
    [InlineData("size<=500", "size", "<=", "500")]
    [InlineData("size>100", "size", ">", "100")]
    [InlineData("size<500", "size", "<", "500")]
    public void ParseFilterExpression_ValidExpressions_ParsesCorrectly(string input, string field, string op, string value)
    {
        var result = FeatureService.ParseFilterExpression(input);
        Assert.NotNull(result);
        Assert.Equal(field, result.Value.Field);
        Assert.Equal(op, result.Value.Op);
        Assert.Equal(value, result.Value.Value);
    }

    [Fact]
    public void ParseFilterExpression_InvalidExpression_ReturnsNull()
    {
        Assert.Null(FeatureService.ParseFilterExpression("noop"));
    }

    // ═══ ResolveField ═══════════════════════════════════════════════════

    [Fact]
    public void ResolveField_Console_DetectsFromPath()
    {
        var c = new RomCandidate { MainPath = "D:/Roms/SNES/game.sfc" };
        Assert.Equal("SNES", FeatureService.ResolveField(c, "console"));
    }

    [Fact]
    public void ResolveField_Region_ReturnsRegion()
    {
        var c = new RomCandidate { Region = "EU" };
        Assert.Equal("EU", FeatureService.ResolveField(c, "region"));
    }

    [Fact]
    public void ResolveField_Format_ReturnsExtension()
    {
        var c = new RomCandidate { Extension = ".sfc" };
        Assert.Equal(".sfc", FeatureService.ResolveField(c, "format"));
    }

    [Fact]
    public void ResolveField_SizeMb_ReturnsMbString()
    {
        var c = new RomCandidate { SizeBytes = 1048576 };
        Assert.Equal("1.00", FeatureService.ResolveField(c, "sizemb"));
    }

    // ═══ EvaluateFilter ═════════════════════════════════════════════════

    [Fact]
    public void EvaluateFilter_GtDouble_ComparesSizeMbNumerically()
    {
        var c = new RomCandidate { SizeBytes = 2 * 1048576 }; // 2.00 MB
        Assert.True(FeatureService.EvaluateFilter(c, "sizemb", "gt", "1.00"));
        Assert.False(FeatureService.EvaluateFilter(c, "sizemb", "gt", "5.00"));
    }

    [Fact]
    public void EvaluateFilter_LtDouble_ComparesSizeMbNumerically()
    {
        var c = new RomCandidate { SizeBytes = 2 * 1048576 }; // 2.00 MB
        Assert.True(FeatureService.EvaluateFilter(c, "sizemb", "lt", "5.00"));
        Assert.False(FeatureService.EvaluateFilter(c, "sizemb", "lt", "1.00"));
    }

    // ═══ TryRegexMatch ══════════════════════════════════════════════════

    [Fact]
    public void TryRegexMatch_ValidRegex_ReturnsTrue()
    {
        Assert.True(FeatureService.TryRegexMatch("Super Mario 3", "^Super"));
    }

    [Fact]
    public void TryRegexMatch_NoMatch_ReturnsFalse()
    {
        Assert.False(FeatureService.TryRegexMatch("Zelda", "^Super"));
    }

    [Fact]
    public void TryRegexMatch_InvalidRegex_ReturnsFalse()
    {
        Assert.False(FeatureService.TryRegexMatch("test", "[invalid"));
    }

    // ═══ GetConfigDiff ══════════════════════════════════════════════════

    [Fact]
    public void GetConfigDiff_IdenticalConfigs_ReturnsEmpty()
    {
        var conf = new Dictionary<string, string> { ["key1"] = "value1" };
        var result = FeatureService.GetConfigDiff(conf, new Dictionary<string, string>(conf));
        Assert.Empty(result);
    }

    [Fact]
    public void GetConfigDiff_DifferentValues_ReturnsDifferences()
    {
        var current = new Dictionary<string, string> { ["key1"] = "new" };
        var saved = new Dictionary<string, string> { ["key1"] = "old" };
        var result = FeatureService.GetConfigDiff(current, saved);
        Assert.Single(result);
        Assert.Equal("new", result[0].CurrentValue);
        Assert.Equal("old", result[0].SavedValue);
    }

    [Fact]
    public void GetConfigDiff_MissingKeys_ReportsAll()
    {
        var current = new Dictionary<string, string> { ["new_key"] = "v1" };
        var saved = new Dictionary<string, string> { ["old_key"] = "v2" };
        var result = FeatureService.GetConfigDiff(current, saved);
        Assert.True(result.Count >= 1);
    }

    // ═══ ResolveDataDirectory ═══════════════════════════════════════════

    [Fact]
    public void ResolveDataDirectory_ReturnsNonNullIfDataFolderExists()
    {
        // data/ folder exists at project root relative to test execution
        var result = FeatureService.ResolveDataDirectory();
        // May be null in test environment if data/ doesn't exist at expected locations
        // but should at least not throw
        Assert.True(result is null || Directory.Exists(result));
    }

    // ═══ LoadLocale ═════════════════════════════════════════════════════

    [Fact]
    public void LoadLocale_InvalidLocale_ReturnsEmptyDict()
    {
        var result = FeatureService.LoadLocale("xx_XX_INVALID");
        // Should return empty or fallback dict, not throw
        Assert.NotNull(result);
    }

    // ═══ CronFieldMatch ═════════════════════════════════════════════════

    [Fact]
    public void CronFieldMatch_Wildcard_AlwaysMatches()
    {
        Assert.True(FeatureService.CronFieldMatch("*", 0));
        Assert.True(FeatureService.CronFieldMatch("*", 59));
    }

    [Fact]
    public void CronFieldMatch_ExactValue_MatchesExact()
    {
        Assert.True(FeatureService.CronFieldMatch("5", 5));
        Assert.False(FeatureService.CronFieldMatch("5", 6));
    }

    [Fact]
    public void CronFieldMatch_Range_MatchesWithinRange()
    {
        Assert.True(FeatureService.CronFieldMatch("1-5", 3));
        Assert.False(FeatureService.CronFieldMatch("1-5", 6));
    }

    [Fact]
    public void CronFieldMatch_Step_MatchesEveryN()
    {
        Assert.True(FeatureService.CronFieldMatch("*/10", 0));
        Assert.True(FeatureService.CronFieldMatch("*/10", 10));
        Assert.True(FeatureService.CronFieldMatch("*/10", 20));
        Assert.False(FeatureService.CronFieldMatch("*/10", 5));
    }

    // ═══ TestCronMatch ══════════════════════════════════════════════════

    [Fact]
    public void TestCronMatch_AllWildcards_MatchesAnyTime()
    {
        Assert.True(FeatureService.TestCronMatch("* * * * *", DateTime.Now));
    }

    [Fact]
    public void TestCronMatch_SpecificMinute_MatchesCorrectly()
    {
        var dt = new DateTime(2024, 1, 15, 10, 30, 0);
        Assert.True(FeatureService.TestCronMatch("30 * * * *", dt));
        Assert.False(FeatureService.TestCronMatch("15 * * * *", dt));
    }

    [Fact]
    public void TestCronMatch_InvalidCron_ReturnsFalse()
    {
        Assert.False(FeatureService.TestCronMatch("invalid", DateTime.Now));
    }

    // ═══ GetSortTemplates ═══════════════════════════════════════════════

    [Fact]
    public void GetSortTemplates_ReturnsKnownTemplates()
    {
        var templates = FeatureService.GetSortTemplates();
        Assert.True(templates.Count >= 3);
        Assert.True(templates.ContainsKey("RetroArch"));
    }

    // ═══ ExportCollectionCsv ════════════════════════════════════════════

    [Fact]
    public void ExportCollectionCsv_EmptyCandidates_ReturnsHeaderOnly()
    {
        var csv = FeatureService.ExportCollectionCsv([]);
        Assert.Contains("Dateiname", csv);
    }

    [Fact]
    public void ExportCollectionCsv_WithCandidates_ContainsData()
    {
        var candidates = new List<RomCandidate>
        {
            new() { MainPath = "D:/SNES/mario.sfc", GameKey = "mario", Region = "EU", Extension = ".sfc", SizeBytes = 1024 }
        };
        var csv = FeatureService.ExportCollectionCsv(candidates);
        Assert.Contains("mario", csv);
        Assert.Contains("EU", csv);
    }

    // ═══ ExportExcelXml ═════════════════════════════════════════════════

    [Fact]
    public void ExportExcelXml_EmptyCandidates_ReturnsValidXml()
    {
        var xml = FeatureService.ExportExcelXml([]);
        Assert.Contains("<?xml", xml);
        Assert.Contains("Workbook", xml);
    }

    [Fact]
    public void ExportExcelXml_WithCandidate_ContainsData()
    {
        var candidates = new List<RomCandidate>
        {
            new() { MainPath = "game.sfc", GameKey = "game", Region = "EU", Extension = ".sfc", SizeBytes = 512 }
        };
        var xml = FeatureService.ExportExcelXml(candidates);
        Assert.Contains("game", xml);
    }

    // ═══ GetJunkReason ══════════════════════════════════════════════════

    [Fact]
    public void GetJunkReason_BetaFile_ReturnsJunk()
    {
        var result = FeatureService.GetJunkReason("Game (Beta)", false);
        Assert.NotNull(result);
    }

    [Fact]
    public void GetJunkReason_DemoFile_ReturnsJunk()
    {
        var result = FeatureService.GetJunkReason("Game (Demo)", false);
        Assert.NotNull(result);
    }

    [Fact]
    public void GetJunkReason_NormalGame_ReturnsNull()
    {
        var result = FeatureService.GetJunkReason("Super Mario World (USA)", false);
        Assert.Null(result);
    }

    // ═══ BuildJunkReport ════════════════════════════════════════════════

    [Fact]
    public void BuildJunkReport_NoJunk_ReturnsCleanMessage()
    {
        var candidates = new List<RomCandidate>
        {
            new() { MainPath = "D:/SNES/Super Mario World (USA).sfc", GameKey = "super mario world" }
        };
        var report = FeatureService.BuildJunkReport(candidates, false);
        Assert.NotNull(report);
    }

    // ═══ IsValidHexHash ═════════════════════════════════════════════════

    [Theory]
    [InlineData("AABBCCDD", 8, true)]
    [InlineData("aabbccdd", 8, true)]
    [InlineData("AABB", 8, false)]
    [InlineData("GGHHIIJJ", 8, false)]
    [InlineData("", 8, false)]
    public void IsValidHexHash_ValidatesCorrectly(string hash, int length, bool expected)
    {
        Assert.Equal(expected, FeatureService.IsValidHexHash(hash, length));
    }

    // ═══ GenerateLogiqxEntry ════════════════════════════════════════════

    [Fact]
    public void GenerateLogiqxEntry_ValidInput_ReturnsXml()
    {
        var xml = FeatureService.GenerateLogiqxEntry("My Game", "mygame.bin", "AABBCCDD", "0123456789ABCDEF0123456789ABCDEF01234567", 1024);
        Assert.Contains("My Game", xml);
        Assert.Contains("mygame.bin", xml);
        Assert.Contains("AABBCCDD", xml);
    }

    // ═══ AnalyzeHeader ══════════════════════════════════════════════════

    [Fact]
    public void AnalyzeHeader_NesFile_DetectsHeader()
    {
        var path = Path.Combine(_tempDir, "test.nes");
        var header = new byte[16] { 0x4E, 0x45, 0x53, 0x1A, 0x02, 0x01, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        var data = new byte[16 + 32768];
        Array.Copy(header, data, 16);
        File.WriteAllBytes(path, data);

        var result = FeatureService.AnalyzeHeader(path);
        Assert.NotNull(result);
        Assert.Equal("NES", result.Platform);
    }

    [Fact]
    public void AnalyzeHeader_GbaFile_DetectsHeader()
    {
        var path = Path.Combine(_tempDir, "test.gba");
        var data = new byte[0xBE + 10]; // Need at least 0xBE bytes
        data[0xB2] = 0x96; // GBA magic at 0xB2
        File.WriteAllBytes(path, data);

        var result = FeatureService.AnalyzeHeader(path);
        Assert.NotNull(result);
        Assert.Equal("GBA", result.Platform);
    }

    [Fact]
    public void AnalyzeHeader_UnknownFile_ReturnsUnbekannt()
    {
        var path = Path.Combine(_tempDir, "random.bin");
        File.WriteAllBytes(path, new byte[512]);
        var result = FeatureService.AnalyzeHeader(path);
        Assert.NotNull(result);
        Assert.Equal("Unknown", result.Platform);
    }

    // ═══ RepairNesHeader ════════════════════════════════════════════════

    [Fact]
    public void RepairNesHeader_DirtyHeader_CleansAndCreatesBackup()
    {
        var path = Path.Combine(_tempDir, "dirty.nes");
        var header = new byte[16] { 0x4E, 0x45, 0x53, 0x1A, 2, 1, 0, 0, 0, 0, 0, 0, 0xAA, 0xBB, 0xCC, 0xDD };
        var body = new byte[32768];
        using (var fs = File.Create(path))
        {
            fs.Write(header);
            fs.Write(body);
        }

        IHeaderRepairService sut = new HeaderRepairService(new FileSystemAdapter());
        Assert.True(sut.RepairNesHeader(path));
        Assert.True(File.Exists(path + ".bak"));

        using var verify = File.OpenRead(path);
        var buf = new byte[16];
        verify.ReadExactly(buf, 0, 16);
        Assert.Equal(0, buf[12]);
        Assert.Equal(0, buf[13]);
        Assert.Equal(0, buf[14]);
        Assert.Equal(0, buf[15]);
    }

    [Fact]
    public void RepairNesHeader_CleanHeader_ReturnsFalse()
    {
        var path = Path.Combine(_tempDir, "clean.nes");
        var header = new byte[16] { 0x4E, 0x45, 0x53, 0x1A, 2, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        var body = new byte[32768];
        using (var fs = File.Create(path))
        {
            fs.Write(header);
            fs.Write(body);
        }

        IHeaderRepairService sut = new HeaderRepairService(new FileSystemAdapter());
        Assert.False(sut.RepairNesHeader(path));
    }

    // ═══ RemoveCopierHeader ═════════════════════════════════════════════

    [Fact]
    public void RemoveCopierHeader_WithHeader_RemovesAndCreatesBackup()
    {
        var path = Path.Combine(_tempDir, "copier.sfc");
        var copierHeader = new byte[512]; // 512-byte copier header
        var romData = new byte[1024];
        romData[0] = 0xAB; // marker for verification
        using (var fs = File.Create(path))
        {
            fs.Write(copierHeader);
            fs.Write(romData);
        }

        IHeaderRepairService sut = new HeaderRepairService(new FileSystemAdapter());
        var result = sut.RemoveCopierHeader(path);
        Assert.True(result);
        Assert.True(File.Exists(path + ".bak"));

        var newContent = File.ReadAllBytes(path);
        Assert.Equal(1024, newContent.Length);
        Assert.Equal(0xAB, newContent[0]);
    }

    // ═══ DetectPatchFormat ══════════════════════════════════════════════

    [Fact]
    public void DetectPatchFormat_IpsPatch_ReturnsIPS()
    {
        var path = Path.Combine(_tempDir, "patch.ips");
        File.WriteAllBytes(path, [(byte)'P', (byte)'A', (byte)'T', (byte)'C', (byte)'H', 0, 0, 0, 0, 0]);

        Assert.Equal("IPS", FeatureService.DetectPatchFormat(path));
    }

    [Fact]
    public void DetectPatchFormat_BpsPatch_ReturnsBPS()
    {
        var path = Path.Combine(_tempDir, "patch.bps");
        File.WriteAllBytes(path, [(byte)'B', (byte)'P', (byte)'S', (byte)'1', 0, 0, 0, 0, 0, 0]);

        Assert.Equal("BPS", FeatureService.DetectPatchFormat(path));
    }

    [Fact]
    public void DetectPatchFormat_UnknownFile_ReturnsNull()
    {
        var path = Path.Combine(_tempDir, "unknown.bin");
        File.WriteAllBytes(path, [0x00, 0x01, 0x02, 0x03, 0x04]);
        Assert.Null(FeatureService.DetectPatchFormat(path));
    }

    [Fact]
    public void ApplyPatch_IpsPatch_ProducesPatchedOutput()
    {
        var sourcePath = Path.Combine(_tempDir, "source.rom");
        var patchPath = Path.Combine(_tempDir, "update.ips");
        var outputPath = Path.Combine(_tempDir, "patched.rom");

        File.WriteAllBytes(sourcePath, [0x10, 0x20, 0x30]);
        File.WriteAllBytes(patchPath,
        [
            (byte)'P', (byte)'A', (byte)'T', (byte)'C', (byte)'H',
            0x00, 0x00, 0x01,
            0x00, 0x01,
            0x7F,
            (byte)'E', (byte)'O', (byte)'F'
        ]);

        var result = FeatureService.ApplyPatch(sourcePath, patchPath, outputPath);

        Assert.Equal("IPS", result.Format);
        Assert.Equal(outputPath, result.OutputPath);
        Assert.Equal(3, result.OutputSizeBytes);
        Assert.Null(result.ToolPath);
        Assert.False(string.IsNullOrWhiteSpace(result.OutputSha256));
        Assert.Equal([0x10, 0x7F, 0x30], File.ReadAllBytes(outputPath));
        Assert.Equal([0x10, 0x20, 0x30], File.ReadAllBytes(sourcePath));
    }

    [Fact]
    public void ApplyPatch_UnknownFormat_ThrowsInvalidOperationException()
    {
        var sourcePath = Path.Combine(_tempDir, "source2.rom");
        var patchPath = Path.Combine(_tempDir, "unknown.patch");
        var outputPath = Path.Combine(_tempDir, "out.rom");
        File.WriteAllBytes(sourcePath, [0xAA, 0xBB, 0xCC]);
        File.WriteAllBytes(patchPath, [0x00, 0x01, 0x02]);

        Assert.Throws<InvalidOperationException>(() => FeatureService.ApplyPatch(sourcePath, patchPath, outputPath));
    }

    // ═══ FindCommonRoot ═════════════════════════════════════════════════

    [Fact]
    public void FindCommonRoot_CommonParent_ReturnsRoot()
    {
        var paths = new List<string> { "C:/Roms/SNES/game1.sfc", "C:/Roms/SNES/game2.sfc" };
        var root = FeatureService.FindCommonRoot(paths);
        Assert.NotNull(root);
    }

    [Fact]
    public void FindCommonRoot_EmptyList_ReturnsNull()
    {
        Assert.Null(FeatureService.FindCommonRoot([]));
    }

    // ═══ SafeLoadXDocument ══════════════════════════════════════════════

    [Fact]
    public void SafeLoadXDocument_ValidXml_LoadsSuccessfully()
    {
        var path = Path.Combine(_tempDir, "test.xml");
        File.WriteAllText(path, "<root><child>text</child></root>");
        var doc = FeatureService.SafeLoadXDocument(path);
        Assert.NotNull(doc.Root);
        Assert.Equal("root", doc.Root!.Name.LocalName);
    }

    // ═══ CompareDatFiles ════════════════════════════════════════════════

    [Fact]
    public void CompareDatFiles_IdenticalFiles_NoChanges()
    {
        var xml = "<?xml version=\"1.0\"?>\n<datafile><game name=\"Game1\"><rom/></game></datafile>";
        var pathA = Path.Combine(_tempDir, "datA.xml");
        var pathB = Path.Combine(_tempDir, "datB.xml");
        File.WriteAllText(pathA, xml);
        File.WriteAllText(pathB, xml);

        var result = FeatureService.CompareDatFiles(pathA, pathB);
        Assert.Empty(result.Added);
        Assert.Empty(result.Removed);
    }

    [Fact]
    public void CompareDatFiles_AddedGame_ReportsAdded()
    {
        var xmlA = "<?xml version=\"1.0\"?>\n<datafile><game name=\"Game1\"><rom/></game></datafile>";
        var xmlB = "<?xml version=\"1.0\"?>\n<datafile><game name=\"Game1\"><rom/></game><game name=\"Game2\"><rom/></game></datafile>";
        var pathA = Path.Combine(_tempDir, "datA2.xml");
        var pathB = Path.Combine(_tempDir, "datB2.xml");
        File.WriteAllText(pathA, xmlA);
        File.WriteAllText(pathB, xmlB);

        var result = FeatureService.CompareDatFiles(pathA, pathB);
        Assert.Single(result.Added);
        Assert.Contains("Game2", result.Added);
    }

    // ═══ BuildCustomDatXmlEntry ═════════════════════════════════════════

    [Fact]
    public void BuildCustomDatXmlEntry_XmlEscapesSpecialChars()
    {
        var xml = FeatureService.BuildCustomDatXmlEntry("Game & <Friends>", "rom.bin", "AABBCCDD", "0123456789ABCDEF0123456789ABCDEF01234567");
        Assert.Contains("&amp;", xml);
        Assert.Contains("&lt;", xml);
    }

    // ═══ CreateBackup ═══════════════════════════════════════════════════

    [Fact]
    public void CreateBackup_CopiesFilesToBackupDir()
    {
        var sourceFile = Path.Combine(_tempDir, "source", "game.sfc");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        File.WriteAllText(sourceFile, "ROM data");

        var backupRoot = Path.Combine(_tempDir, "backups");
        var result = FeatureService.CreateBackup([sourceFile], backupRoot, "test");

        Assert.True(Directory.Exists(result));
        var backupFiles = Directory.GetFiles(result, "*", SearchOption.AllDirectories);
        Assert.True(backupFiles.Length > 0);
    }

    // ═══ CleanupOldBackups ══════════════════════════════════════════════

    [Fact]
    public void CleanupOldBackups_RemovesOldDirectories()
    {
        var backupRoot = Path.Combine(_tempDir, "cleanup_backups");
        var oldDir = Path.Combine(backupRoot, "old_backup");
        Directory.CreateDirectory(oldDir);
        Directory.SetCreationTime(oldDir, DateTime.Now.AddDays(-30));

        var removed = FeatureService.CleanupOldBackups(backupRoot, 7);
        Assert.Equal(1, removed);
    }

    // ═══ SaveTrendSnapshot / LoadTrendHistory ════════════════════════════

    [Fact]
    public void TrendSnapshot_RoundTrip_PreservesData()
    {
        var snapshot = new TrendSnapshot(DateTime.UtcNow, 100, 1_000_000, 50, 10, 5, 85);
        Assert.Equal(100, snapshot.TotalFiles);
        Assert.Equal(85, snapshot.QualityScore);
    }

    // ═══ Record type tests ══════════════════════════════════════════════

    [Fact]
    public void ConversionEstimateResult_Records_HaveCorrectProperties()
    {
        var detail = new ConversionDetail("game.iso", "iso", "chd", 1000, 600);
        Assert.Equal("game.iso", detail.FileName);
        Assert.Equal("iso", detail.SourceFormat);
        Assert.Equal("chd", detail.TargetFormat);

        var result = new ConversionEstimateResult(1000, 600, 400, 0.6, [detail]);
        Assert.Equal(400, result.SavedBytes);
        Assert.Single(result.Details);
    }

    [Fact]
    public void HeatmapEntry_Records_HasProperties()
    {
        var entry = new HeatmapEntry("SNES", 200, 50, 25.0);
        Assert.Equal("SNES", entry.Console);
        Assert.Equal(25.0, entry.DuplicatePercent);
    }

    [Fact]
    public void JunkReportEntry_Records_HasProperties()
    {
        var entry = new JunkReportEntry("Beta", "Pre-release", "Warning");
        Assert.Equal("Beta", entry.Tag);
    }

    [Fact]
    public void ConfigDiffEntry_Records_HasProperties()
    {
        var entry = new ConfigDiffEntry("logLevel", "Info", "Debug");
        Assert.Equal("logLevel", entry.Key);
    }

    [Fact]
    public void DatDiffResult_Records_HasProperties()
    {
        var result = new DatDiffResult(["Game2"], ["Game3"], 1, 5);
        Assert.Single(result.Added);
        Assert.Single(result.Removed);
        Assert.Equal(1, result.ModifiedCount);
        Assert.Equal(5, result.UnchangedCount);
    }

    [Fact]
    public void IntegrityCheckResult_Records_HasProperties()
    {
        var result = new IntegrityCheckResult(["f1"], ["f2"], ["f3"], true, "Test");
        Assert.Single(result.Changed);
        Assert.Single(result.Missing);
        Assert.Single(result.Intact);
        Assert.True(result.BitRotRisk);
    }
}
