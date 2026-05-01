using Romulus.UI.Wpf.Services;
using Romulus.UI.Wpf.ViewModels;
using Romulus.Contracts.Models;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for pure static helpers in FeatureService and MainViewModel.
/// Covers: LevenshteinDistance, ParseCsvLine, Truncate, ParseCategory,
/// SanitizeCsvField, IsPlainNegativeNumber, TrySplitProgressMessage,
/// TryParseProgressFraction.
/// </summary>
public sealed class WpfPureHelperCoverageTests
{
    // ── LevenshteinDistance ─────────────────────────────────────────

    [Fact]
    public void Levenshtein_IdenticalStrings_ReturnsZero()
    {
        Assert.Equal(0, FeatureService.LevenshteinDistance("abc", "abc"));
    }

    [Fact]
    public void Levenshtein_EmptySource_ReturnsTargetLength()
    {
        Assert.Equal(3, FeatureService.LevenshteinDistance("", "abc"));
    }

    [Fact]
    public void Levenshtein_EmptyTarget_ReturnsSourceLength()
    {
        Assert.Equal(4, FeatureService.LevenshteinDistance("test", ""));
    }

    [Fact]
    public void Levenshtein_BothEmpty_ReturnsZero()
    {
        Assert.Equal(0, FeatureService.LevenshteinDistance("", ""));
    }

    [Fact]
    public void Levenshtein_SingleInsertion()
    {
        Assert.Equal(1, FeatureService.LevenshteinDistance("cat", "cats"));
    }

    [Fact]
    public void Levenshtein_SingleDeletion()
    {
        Assert.Equal(1, FeatureService.LevenshteinDistance("cats", "cat"));
    }

    [Fact]
    public void Levenshtein_SingleSubstitution()
    {
        Assert.Equal(1, FeatureService.LevenshteinDistance("cat", "bat"));
    }

    [Fact]
    public void Levenshtein_CompletelyDifferent()
    {
        Assert.Equal(3, FeatureService.LevenshteinDistance("abc", "xyz"));
    }

    [Fact]
    public void Levenshtein_KnownDistance_KittenSitting()
    {
        Assert.Equal(3, FeatureService.LevenshteinDistance("kitten", "sitting"));
    }

    // ── ParseCsvLine ────────────────────────────────────────────────

    [Fact]
    public void ParseCsvLine_SimpleValues()
    {
        var result = FeatureService.ParseCsvLine("a,b,c");
        Assert.Equal(new[] { "a", "b", "c" }, result);
    }

    [Fact]
    public void ParseCsvLine_QuotedValues()
    {
        var result = FeatureService.ParseCsvLine("\"hello\",\"world\"");
        Assert.Equal(new[] { "hello", "world" }, result);
    }

    [Fact]
    public void ParseCsvLine_QuotedWithComma()
    {
        var result = FeatureService.ParseCsvLine("\"a,b\",c");
        Assert.Equal(new[] { "a,b", "c" }, result);
    }

    [Fact]
    public void ParseCsvLine_EscapedDoubleQuotes()
    {
        var result = FeatureService.ParseCsvLine("\"he said \"\"hello\"\"\",world");
        Assert.Equal(new[] { "he said \"hello\"", "world" }, result);
    }

    [Fact]
    public void ParseCsvLine_EmptyFields()
    {
        var result = FeatureService.ParseCsvLine(",,");
        Assert.Equal(new[] { "", "", "" }, result);
    }

    [Fact]
    public void ParseCsvLine_SingleField()
    {
        var result = FeatureService.ParseCsvLine("only");
        Assert.Equal(new[] { "only" }, result);
    }

    [Fact]
    public void ParseCsvLine_EmptyString()
    {
        var result = FeatureService.ParseCsvLine("");
        Assert.Equal(new[] { "" }, result);
    }

    // ── Truncate ────────────────────────────────────────────────────

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
    public void Truncate_LongString_TruncatedWithEllipsis()
    {
        var result = FeatureService.Truncate("abcdefghij", 7);
        Assert.Equal("abcd...", result);
        Assert.Equal(7, result.Length);
    }

    // ── ParseCategory ───────────────────────────────────────────────

    [Fact]
    public void ParseCategory_Null_ReturnsGame()
    {
        Assert.Equal(FileCategory.Game, FeatureService.ParseCategory(null));
    }

    [Fact]
    public void ParseCategory_Empty_ReturnsGame()
    {
        Assert.Equal(FileCategory.Game, FeatureService.ParseCategory(""));
    }

    [Fact]
    public void ParseCategory_ValidValue_Parsed()
    {
        Assert.Equal(FileCategory.Junk, FeatureService.ParseCategory("Junk"));
    }

    [Fact]
    public void ParseCategory_CaseInsensitive()
    {
        Assert.Equal(FileCategory.Bios, FeatureService.ParseCategory("bios"));
    }

    [Fact]
    public void ParseCategory_Invalid_ReturnsUnknown()
    {
        Assert.Equal(FileCategory.Unknown, FeatureService.ParseCategory("NotACategory"));
    }

    // ── SanitizeCsvField ────────────────────────────────────────────

    [Fact]
    public void SanitizeCsvField_EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", FeatureService.SanitizeCsvField(""));
    }

    [Fact]
    public void SanitizeCsvField_NullString_ReturnsEmpty()
    {
        Assert.Equal("", FeatureService.SanitizeCsvField(null!));
    }

    [Fact]
    public void SanitizeCsvField_NormalValue_Unchanged()
    {
        Assert.Equal("hello", FeatureService.SanitizeCsvField("hello"));
    }

    [Theory]
    [InlineData("=CMD()", "'=CMD()")]
    [InlineData("+42", "'+42")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    public void SanitizeCsvField_FormulaPrefix_Escaped(string input, string expected)
    {
        Assert.Equal(expected, FeatureService.SanitizeCsvField(input));
    }

    [Fact]
    public void SanitizeCsvField_TabPrefix_Escaped()
    {
        Assert.Equal("'\tdata", FeatureService.SanitizeCsvField("\tdata"));
    }

    [Fact]
    public void SanitizeCsvField_CarriageReturnPrefix_Escaped()
    {
        Assert.Equal("'\rdata", FeatureService.SanitizeCsvField("\rdata"));
    }

    [Fact]
    public void SanitizeCsvField_NegativeNumber_NotEscaped()
    {
        Assert.Equal("-42", FeatureService.SanitizeCsvField("-42"));
    }

    [Fact]
    public void SanitizeCsvField_NegativeDecimal_NotEscaped()
    {
        Assert.Equal("-3.14", FeatureService.SanitizeCsvField("-3.14"));
    }

    [Fact]
    public void SanitizeCsvField_DashNonNumeric_Escaped()
    {
        Assert.Equal("'-formula", FeatureService.SanitizeCsvField("-formula"));
    }

    [Fact]
    public void SanitizeCsvField_ValueWithComma_Quoted()
    {
        Assert.Equal("\"hello,world\"", FeatureService.SanitizeCsvField("hello,world"));
    }

    [Fact]
    public void SanitizeCsvField_ValueWithQuotes_DoubleQuoted()
    {
        Assert.Equal("\"say \"\"hello\"\"\"", FeatureService.SanitizeCsvField("say \"hello\""));
    }

    [Fact]
    public void SanitizeCsvField_ValueWithSemicolon_Quoted()
    {
        Assert.Equal("\"a;b\"", FeatureService.SanitizeCsvField("a;b"));
    }

    // ── IsPlainNegativeNumber ───────────────────────────────────────

    [Theory]
    [InlineData("-1", true)]
    [InlineData("-42", true)]
    [InlineData("-3.14", true)]
    [InlineData("-0.5", true)]
    public void IsPlainNegativeNumber_ValidNegativeNumbers(string value, bool expected)
    {
        Assert.Equal(expected, FeatureService.IsPlainNegativeNumber(value));
    }

    [Theory]
    [InlineData("-", false)]
    [InlineData("-abc", false)]
    [InlineData("42", false)]
    [InlineData("", false)]
    [InlineData("a", false)]
    public void IsPlainNegativeNumber_NotNegativeNumbers(string value, bool expected)
    {
        Assert.Equal(expected, FeatureService.IsPlainNegativeNumber(value));
    }

    // ── TrySplitProgressMessage ─────────────────────────────────────

    [Fact]
    public void TrySplitProgressMessage_ValidMessage_SplitsCorrectly()
    {
        Assert.True(MainViewModel.TrySplitProgressMessage("[Hashing] file.zip 3/10", out var phase, out var detail));
        Assert.Equal("[Hashing]", phase);
        Assert.Equal("file.zip 3/10", detail);
    }

    [Fact]
    public void TrySplitProgressMessage_PhaseOnly_EmptyDetail()
    {
        Assert.True(MainViewModel.TrySplitProgressMessage("[Done]", out var phase, out var detail));
        Assert.Equal("[Done]", phase);
        Assert.Equal("", detail);
    }

    [Fact]
    public void TrySplitProgressMessage_NullMessage_ReturnsFalse()
    {
        Assert.False(MainViewModel.TrySplitProgressMessage(null!, out _, out _));
    }

    [Fact]
    public void TrySplitProgressMessage_EmptyMessage_ReturnsFalse()
    {
        Assert.False(MainViewModel.TrySplitProgressMessage("", out _, out _));
    }

    [Fact]
    public void TrySplitProgressMessage_NoBracket_ReturnsFalse()
    {
        Assert.False(MainViewModel.TrySplitProgressMessage("plain message", out _, out _));
    }

    [Fact]
    public void TrySplitProgressMessage_OnlyOpenBracket_ReturnsFalse()
    {
        Assert.False(MainViewModel.TrySplitProgressMessage("[", out _, out _));
    }

    [Fact]
    public void TrySplitProgressMessage_EmptyBrackets_ReturnsFalse()
    {
        // "[]" → closingBracket is 1, which is not > 1
        Assert.False(MainViewModel.TrySplitProgressMessage("[]", out _, out _));
    }

    [Fact]
    public void TrySplitProgressMessage_WhitespaceAfterBracket_Trimmed()
    {
        Assert.True(MainViewModel.TrySplitProgressMessage("[Phase]   detail  ", out _, out var detail));
        Assert.Equal("detail", detail);
    }

    // ── TryParseProgressFraction ────────────────────────────────────

    [Fact]
    public void TryParseProgressFraction_ValidFraction_ParsedCorrectly()
    {
        Assert.True(MainViewModel.TryParseProgressFraction("Processing 3/10 files", out var fraction));
        Assert.Equal(0.3, fraction, precision: 4);
    }

    [Fact]
    public void TryParseProgressFraction_ExactNumbers_NoSurroundingText()
    {
        Assert.True(MainViewModel.TryParseProgressFraction("5/20", out var fraction));
        Assert.Equal(0.25, fraction, precision: 4);
    }

    [Fact]
    public void TryParseProgressFraction_ResultClampedToOne()
    {
        Assert.True(MainViewModel.TryParseProgressFraction("15/10", out var fraction));
        Assert.Equal(1.0, fraction, precision: 4);
    }

    [Fact]
    public void TryParseProgressFraction_ZeroNumerator()
    {
        Assert.True(MainViewModel.TryParseProgressFraction("0/100", out var fraction));
        Assert.Equal(0.0, fraction, precision: 4);
    }

    [Fact]
    public void TryParseProgressFraction_NullMessage_ReturnsFalse()
    {
        Assert.False(MainViewModel.TryParseProgressFraction(null!, out _));
    }

    [Fact]
    public void TryParseProgressFraction_EmptyMessage_ReturnsFalse()
    {
        Assert.False(MainViewModel.TryParseProgressFraction("", out _));
    }

    [Fact]
    public void TryParseProgressFraction_NoSlash_ReturnsFalse()
    {
        Assert.False(MainViewModel.TryParseProgressFraction("no fraction here", out _));
    }

    [Fact]
    public void TryParseProgressFraction_SlashOnly_ReturnsFalse()
    {
        Assert.False(MainViewModel.TryParseProgressFraction("/", out _));
    }

    [Fact]
    public void TryParseProgressFraction_NoDigitsBeforeSlash_ReturnsFalse()
    {
        Assert.False(MainViewModel.TryParseProgressFraction("abc/10", out _));
    }

    [Fact]
    public void TryParseProgressFraction_NoDigitsAfterSlash_ReturnsFalse()
    {
        Assert.False(MainViewModel.TryParseProgressFraction("10/abc", out _));
    }

    [Fact]
    public void TryParseProgressFraction_ZeroDenominator_ReturnsFalse()
    {
        Assert.False(MainViewModel.TryParseProgressFraction("5/0", out _));
    }
}
