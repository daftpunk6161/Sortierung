using Romulus.Contracts.Models;
using Romulus.UI.Wpf.Services;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for FeatureService pure/static utility methods:
/// SanitizeCsvField, LevenshteinDistance, Truncate, ParseCsvLine, ParseCategory,
/// IsValidHexHash, ParseFilterExpression, EvaluateFilter, GetConfigDiff,
/// ExtractFirstCsvField, BuildCustomDatXmlEntry, FormatSize.
/// </summary>
public sealed class FeatureServiceCoverageTests
{
    #region SanitizeCsvField — CSV Injection Protection (OWASP)

    [Theory]
    [InlineData("", "")]
    [InlineData("normal text", "normal text")]
    [InlineData("hello world", "hello world")]
    public void SanitizeCsvField_SafeStrings_Unchanged(string input, string expected)
    {
        Assert.Equal(expected, FeatureService.SanitizeCsvField(input));
    }

    [Theory]
    [InlineData("=cmd|'calc'|''", "'=cmd|'calc'|''")]
    [InlineData("+cmd", "'+cmd")]
    [InlineData("@sum(A1:A2)", "'@sum(A1:A2)")]
    [InlineData("\tcmd", "'\tcmd")]
    [InlineData("\rcmd", "'\rcmd")]
    public void SanitizeCsvField_FormulaPrefix_EscapedWithQuote(string input, string expected)
    {
        Assert.Equal(expected, FeatureService.SanitizeCsvField(input));
    }

    [Fact]
    public void SanitizeCsvField_DangerousDashNotNumber_Escaped()
    {
        var result = FeatureService.SanitizeCsvField("-cmd");
        Assert.StartsWith("'", result);
    }

    [Fact]
    public void SanitizeCsvField_NegativeNumber_NotEscaped()
    {
        Assert.Equal("-42", FeatureService.SanitizeCsvField("-42"));
        Assert.Equal("-3.14", FeatureService.SanitizeCsvField("-3.14"));
    }

    [Theory]
    [InlineData("has\"quote", "\"has\"\"quote\"")]
    [InlineData("has;semi", "\"has;semi\"")]
    [InlineData("has,comma", "\"has,comma\"")]
    public void SanitizeCsvField_SpecialChars_Quoted(string input, string expected)
    {
        Assert.Equal(expected, FeatureService.SanitizeCsvField(input));
    }

    [Fact]
    public void SanitizeCsvField_Null_ReturnsEmpty()
    {
        Assert.Equal("", FeatureService.SanitizeCsvField(null!));
    }

    #endregion

    #region LevenshteinDistance

    [Theory]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("", "", 0)]
    [InlineData("abc", "", 3)]
    [InlineData("", "xyz", 3)]
    [InlineData("same", "same", 0)]
    [InlineData("a", "b", 1)]
    public void LevenshteinDistance_CorrectValues(string s, string t, int expected)
    {
        Assert.Equal(expected, FeatureService.LevenshteinDistance(s, t));
    }

    [Fact]
    public void LevenshteinDistance_Symmetry()
    {
        Assert.Equal(
            FeatureService.LevenshteinDistance("abc", "def"),
            FeatureService.LevenshteinDistance("def", "abc"));
    }

    #endregion

    #region Truncate

    [Fact]
    public void Truncate_ShortString_Unchanged()
    {
        Assert.Equal("abc", FeatureService.Truncate("abc", 10));
    }

    [Fact]
    public void Truncate_ExactLength_Unchanged()
    {
        Assert.Equal("abcde", FeatureService.Truncate("abcde", 5));
    }

    [Fact]
    public void Truncate_Long_AddsDots()
    {
        var result = FeatureService.Truncate("Hello World!", 8);
        Assert.Equal(8, result.Length);
        Assert.EndsWith("...", result);
    }

    #endregion

    #region ParseCsvLine (RFC 4180)

    [Fact]
    public void ParseCsvLine_SimpleFields()
    {
        var result = FeatureService.ParseCsvLine("a,b,c");
        Assert.Equal(["a", "b", "c"], result);
    }

    [Fact]
    public void ParseCsvLine_QuotedFields()
    {
        var result = FeatureService.ParseCsvLine("\"hello, world\",b");
        Assert.Equal(2, result.Length);
        Assert.Equal("hello, world", result[0]);
    }

    [Fact]
    public void ParseCsvLine_EscapedQuotes()
    {
        var result = FeatureService.ParseCsvLine("\"a\"\"b\",c");
        Assert.Equal(2, result.Length);
        Assert.Equal("a\"b", result[0]);
    }

    [Fact]
    public void ParseCsvLine_EmptyLine_SingleEmptyField()
    {
        var result = FeatureService.ParseCsvLine("");
        Assert.Single(result);
        Assert.Equal("", result[0]);
    }

    [Fact]
    public void ParseCsvLine_TrailingComma_ExtraEmptyField()
    {
        var result = FeatureService.ParseCsvLine("a,b,");
        Assert.Equal(3, result.Length);
        Assert.Equal("", result[2]);
    }

    #endregion

    #region ParseCategory

    [Theory]
    [InlineData("Game", FileCategory.Game)]
    [InlineData("game", FileCategory.Game)]
    [InlineData("Junk", FileCategory.Junk)]
    [InlineData("NonGame", FileCategory.NonGame)]
    public void ParseCategory_ValidValues(string input, FileCategory expected)
    {
        Assert.Equal(expected, FeatureService.ParseCategory(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ParseCategory_NullOrWhitespace_DefaultsToGame(string? input)
    {
        Assert.Equal(FileCategory.Game, FeatureService.ParseCategory(input));
    }

    [Fact]
    public void ParseCategory_InvalidValue_ReturnsUnknown()
    {
        Assert.Equal(FileCategory.Unknown, FeatureService.ParseCategory("NotACategory"));
    }

    #endregion

    #region IsValidHexHash

    [Theory]
    [InlineData("ABCDEF01", 8, true)]
    [InlineData("abcdef01", 8, true)]
    [InlineData("0123456789abcdef0123456789abcdef01234567", 40, true)]
    public void IsValidHexHash_Valid(string hash, int len, bool expected)
    {
        Assert.Equal(expected, FeatureService.IsValidHexHash(hash, len));
    }

    [Theory]
    [InlineData("GHIJ0000", 8)]
    [InlineData("ABC", 8)]
    [InlineData("ABCDEF012", 8)]
    [InlineData("", 8)]
    public void IsValidHexHash_Invalid(string hash, int len)
    {
        Assert.False(FeatureService.IsValidHexHash(hash, len));
    }

    #endregion

    #region ParseFilterExpression

    [Theory]
    [InlineData("console=PS2", "console", "=", "PS2")]
    [InlineData("sizemb>100", "sizemb", ">", "100")]
    [InlineData("sizemb<50", "sizemb", "<", "50")]
    [InlineData("sizemb>=100", "sizemb", ">=", "100")]
    [InlineData("sizemb<=50", "sizemb", "<=", "50")]
    public void ParseFilterExpression_ValidExpressions(string input, string field, string op, string value)
    {
        var result = FeatureService.ParseFilterExpression(input);
        Assert.NotNull(result);
        Assert.Equal(field, result.Value.Field);
        Assert.Equal(op, result.Value.Op);
        Assert.Equal(value, result.Value.Value);
    }

    [Fact]
    public void ParseFilterExpression_NoOperator_ReturnsNull()
    {
        Assert.Null(FeatureService.ParseFilterExpression("justtext"));
    }

    #endregion

    #region EvaluateFilter

    [Fact]
    public void EvaluateFilter_Eq_MatchesRegion()
    {
        var c = new RomCandidate { Region = "US", GameKey = "test", MainPath = "test.rom", Extension = ".rom" };
        Assert.True(FeatureService.EvaluateFilter(c, "region", "eq", "US"));
        Assert.False(FeatureService.EvaluateFilter(c, "region", "eq", "EU"));
    }

    [Fact]
    public void EvaluateFilter_Neq()
    {
        var c = new RomCandidate { Region = "US", GameKey = "test", MainPath = "test.rom", Extension = ".rom" };
        Assert.True(FeatureService.EvaluateFilter(c, "region", "neq", "EU"));
        Assert.False(FeatureService.EvaluateFilter(c, "region", "neq", "US"));
    }

    [Fact]
    public void EvaluateFilter_Contains()
    {
        var c = new RomCandidate { GameKey = "supermario", MainPath = "test.rom", Extension = ".rom", Region = "US" };
        Assert.True(FeatureService.EvaluateFilter(c, "gamekey", "contains", "mario"));
    }

    [Fact]
    public void EvaluateFilter_Gt_Numeric()
    {
        var c = new RomCandidate { SizeBytes = 200 * 1024 * 1024, GameKey = "test", MainPath = "test.rom", Extension = ".rom", Region = "US" };
        Assert.True(FeatureService.EvaluateFilter(c, "sizemb", "gt", "100"));
    }

    [Fact]
    public void EvaluateFilter_Lt_Numeric()
    {
        var c = new RomCandidate { SizeBytes = 10 * 1024 * 1024, GameKey = "test", MainPath = "test.rom", Extension = ".rom", Region = "US" };
        Assert.True(FeatureService.EvaluateFilter(c, "sizemb", "lt", "100"));
    }

    [Fact]
    public void EvaluateFilter_Regex()
    {
        var c = new RomCandidate { Region = "US", GameKey = "test", MainPath = "test.rom", Extension = ".rom" };
        Assert.True(FeatureService.EvaluateFilter(c, "region", "regex", "^U"));
        Assert.False(FeatureService.EvaluateFilter(c, "region", "regex", "^E"));
    }

    [Fact]
    public void EvaluateFilter_UnknownOp_ReturnsFalse()
    {
        var c = new RomCandidate { Region = "US", GameKey = "test", MainPath = "test.rom", Extension = ".rom" };
        Assert.False(FeatureService.EvaluateFilter(c, "region", "bogus", "US"));
    }

    [Fact]
    public void EvaluateFilter_Regex_InvalidPattern_ReturnsFalse()
    {
        var c = new RomCandidate { Region = "US", GameKey = "test", MainPath = "test.rom", Extension = ".rom" };
        Assert.False(FeatureService.EvaluateFilter(c, "region", "regex", "[invalid"));
    }

    #endregion

    #region ApplyFilter

    [Fact]
    public void ApplyFilter_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Empty(FeatureService.ApplyFilter(null!, "region", "eq", "US"));
        Assert.Empty(FeatureService.ApplyFilter([], "region", "eq", "US"));
    }

    [Fact]
    public void ApplyFilter_FiltersCorrectly()
    {
        var candidates = new[]
        {
            new RomCandidate { Region = "US", GameKey = "a", MainPath = "a.rom", Extension = ".rom" },
            new RomCandidate { Region = "EU", GameKey = "b", MainPath = "b.rom", Extension = ".rom" },
            new RomCandidate { Region = "US", GameKey = "c", MainPath = "c.rom", Extension = ".rom" },
        };

        var result = FeatureService.ApplyFilter(candidates, "region", "eq", "US");
        Assert.Equal(2, result.Count);
    }

    #endregion

    #region GetConfigDiff

    [Fact]
    public void GetConfigDiff_IdenticalMaps_NoDiff()
    {
        var a = new Dictionary<string, string> { ["key"] = "val" };
        var b = new Dictionary<string, string> { ["key"] = "val" };
        Assert.Empty(FeatureService.GetConfigDiff(a, b));
    }

    [Fact]
    public void GetConfigDiff_ChangedValue_Detected()
    {
        var current = new Dictionary<string, string> { ["key"] = "new" };
        var saved = new Dictionary<string, string> { ["key"] = "old" };

        var diff = FeatureService.GetConfigDiff(current, saved);
        Assert.Single(diff);
        Assert.Equal("key", diff[0].Key);
        Assert.Equal("old", diff[0].SavedValue);
        Assert.Equal("new", diff[0].CurrentValue);
    }

    [Fact]
    public void GetConfigDiff_AddedKey_Detected()
    {
        var current = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" };
        var saved = new Dictionary<string, string> { ["a"] = "1" };

        var diff = FeatureService.GetConfigDiff(current, saved);
        Assert.Single(diff);
        Assert.Equal("b", diff[0].Key);
    }

    [Fact]
    public void GetConfigDiff_RemovedKey_Detected()
    {
        var current = new Dictionary<string, string> { ["a"] = "1" };
        var saved = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" };

        var diff = FeatureService.GetConfigDiff(current, saved);
        Assert.Single(diff);
        Assert.Equal("(fehlt)", diff[0].CurrentValue);
    }

    #endregion

    #region ExtractFirstCsvField

    [Theory]
    [InlineData("", "")]
    [InlineData("hello", "hello")]
    [InlineData("a;b;c", "a")]
    [InlineData("a,b,c", "a")]
    [InlineData("\"quoted;field\";other", "quoted;field")]
    [InlineData("\"esc\"\"ape\",next", "esc\"ape")]
    public void ExtractFirstCsvField_CorrectExtraction(string input, string expected)
    {
        Assert.Equal(expected, FeatureService.ExtractFirstCsvField(input));
    }

    [Fact]
    public void ExtractFirstCsvField_SemicolonBeforeComma()
    {
        var result = FeatureService.ExtractFirstCsvField("a;b,c");
        Assert.Equal("a", result);
    }

    #endregion

    #region BuildCustomDatXmlEntry

    [Fact]
    public void BuildCustomDatXmlEntry_ValidInput()
    {
        var result = FeatureService.BuildCustomDatXmlEntry(
            "Mario & Luigi", "rom.bin", "AABBCCDD", "0123456789ABCDEF0123456789ABCDEF01234567");

        Assert.Contains("Mario &amp; Luigi", result);
        Assert.Contains("crc=\"AABBCCDD\"", result);
        Assert.Contains("sha1=\"0123456789ABCDEF0123456789ABCDEF01234567\"", result);
        Assert.Contains("<game name=", result);
        Assert.Contains("<rom name=", result);
    }

    [Fact]
    public void BuildCustomDatXmlEntry_EmptySha1_OmitsSha1Attr()
    {
        var result = FeatureService.BuildCustomDatXmlEntry("Game", "rom.bin", "AABB0011", "");
        Assert.DoesNotContain("sha1=", result);
    }

    [Fact]
    public void BuildCustomDatXmlEntry_HtmlSafeEncoding()
    {
        var result = FeatureService.BuildCustomDatXmlEntry(
            "<script>alert(1)</script>", "rom.bin", "00000000", "");
        Assert.DoesNotContain("<script>", result);
        Assert.Contains("&lt;script&gt;", result);
    }

    #endregion

    #region FormatSize

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(500, "500 B")]
    [InlineData(1024, "1.00 KB")]
    [InlineData(1048576, "1.00 MB")]
    [InlineData(1073741824, "1.00 GB")]
    public void FormatSize_FormatsCorrectly(long bytes, string expected)
    {
        Assert.Equal(expected, FeatureService.FormatSize(bytes));
    }

    #endregion

    #region ResolveField

    [Fact]
    public void ResolveField_Format_ReturnsExtension()
    {
        var c = new RomCandidate { Extension = ".chd", GameKey = "test", MainPath = "test.chd", Region = "US" };
        Assert.Equal(".chd", FeatureService.ResolveField(c, "format"));
    }

    [Fact]
    public void ResolveField_GameKey_ReturnsGameKey()
    {
        var c = new RomCandidate { GameKey = "supermario", MainPath = "test.rom", Extension = ".rom", Region = "US" };
        Assert.Equal("supermario", FeatureService.ResolveField(c, "gamekey"));
    }

    [Fact]
    public void ResolveField_DatStatus_Verified()
    {
        var c = new RomCandidate { DatMatch = true, GameKey = "test", MainPath = "test.rom", Extension = ".rom", Region = "US" };
        Assert.Equal("Verified", FeatureService.ResolveField(c, "datstatus"));
    }

    [Fact]
    public void ResolveField_DatStatus_Unverified()
    {
        var c = new RomCandidate { DatMatch = false, GameKey = "test", MainPath = "test.rom", Extension = ".rom", Region = "US" };
        Assert.Equal("Unverified", FeatureService.ResolveField(c, "datstatus"));
    }

    [Fact]
    public void ResolveField_UnknownField_ReturnsEmpty()
    {
        var c = new RomCandidate { GameKey = "test", MainPath = "test.rom", Extension = ".rom", Region = "US" };
        Assert.Equal("", FeatureService.ResolveField(c, "nonexistent"));
    }

    #endregion

    #region DatCatalogItemVm Computed Properties

    [Theory]
    [InlineData(DatInstallStatus.Installed, "✓ Aktuell")]
    [InlineData(DatInstallStatus.Missing, "✗ Fehlend")]
    [InlineData(DatInstallStatus.Stale, "⟳ Veraltet")]
    [InlineData(DatInstallStatus.Error, "⚠ Fehler")]
    public void DatCatalogItemVm_StatusDisplay(DatInstallStatus status, string expected)
    {
        var vm = new Romulus.UI.Wpf.ViewModels.DatCatalogItemVm { Status = status };
        Assert.Equal(expected, vm.StatusDisplay);
    }

    [Theory]
    [InlineData(null, "—")]
    [InlineData(0L, "—")]
    [InlineData(500L, "500 B")]
    [InlineData(2048L, "2.0 KB")]
    [InlineData(2 * 1024 * 1024L, "2.0 MB")]
    public void DatCatalogItemVm_FileSizeDisplay(long? bytes, string expected)
    {
        var vm = new Romulus.UI.Wpf.ViewModels.DatCatalogItemVm { FileSizeBytes = bytes };
        Assert.Equal(expected, vm.FileSizeDisplay);
    }

    [Fact]
    public void DatCatalogItemVm_ActionDisplay_AutoMissing()
    {
        var vm = new Romulus.UI.Wpf.ViewModels.DatCatalogItemVm
        {
            Status = DatInstallStatus.Missing,
            DownloadStrategy = DatDownloadStrategy.Auto
        };
        Assert.Equal("Herunterladen", vm.ActionDisplay);
    }

    [Fact]
    public void DatCatalogItemVm_ActionDisplay_Installed()
    {
        var vm = new Romulus.UI.Wpf.ViewModels.DatCatalogItemVm { Status = DatInstallStatus.Installed };
        Assert.Equal("Aktuell", vm.ActionDisplay);
    }

    [Fact]
    public void DatCatalogItemVm_ActionDisplay_ManualLogin()
    {
        var vm = new Romulus.UI.Wpf.ViewModels.DatCatalogItemVm
        {
            Status = DatInstallStatus.Missing,
            DownloadStrategy = DatDownloadStrategy.ManualLogin
        };
        Assert.Equal("Manuell (redump.org)", vm.ActionDisplay);
    }

    [Fact]
    public void DatCatalogItemVm_InstalledDateDisplay_NoDate()
    {
        var vm = new Romulus.UI.Wpf.ViewModels.DatCatalogItemVm();
        Assert.Equal("—", vm.InstalledDateDisplay);
    }

    [Fact]
    public void DatCatalogItemVm_InstalledDateDisplay_WithDate()
    {
        var vm = new Romulus.UI.Wpf.ViewModels.DatCatalogItemVm
        {
            InstalledDate = new DateTime(2025, 6, 15)
        };
        Assert.Equal("2025-06-15", vm.InstalledDateDisplay);
    }

    [Fact]
    public void DatCatalogItemVm_IsSelected_PropertyChanged()
    {
        var vm = new Romulus.UI.Wpf.ViewModels.DatCatalogItemVm();
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        vm.IsSelected = true;
        Assert.Contains("IsSelected", changed);
    }

    [Fact]
    public void DatCatalogItemVm_Status_FiresStatusDisplayAndActionDisplay()
    {
        var vm = new Romulus.UI.Wpf.ViewModels.DatCatalogItemVm { Status = DatInstallStatus.Missing };
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        vm.Status = DatInstallStatus.Installed;
        Assert.Contains("StatusDisplay", changed);
        Assert.Contains("ActionDisplay", changed);
    }

    #endregion
}
