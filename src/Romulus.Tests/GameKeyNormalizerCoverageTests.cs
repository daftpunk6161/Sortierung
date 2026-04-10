using System.Text.RegularExpressions;
using Xunit;
using Romulus.Core.GameKeys;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for GameKeyNormalizer: AsciiFold edge cases, Normalize with explicit patterns,
/// RemoveMsDosMetadataTags, RegisterDefaultPatterns / RegisterPatternFactory null guards,
/// StripIsoDateTags (indirect), NormalizeTitleVariants (indirect), ConvertFullwidthAscii (indirect).
/// </summary>
public sealed class GameKeyNormalizerCoverageTests
{
    private static readonly IReadOnlyList<Regex> EmptyPatterns = Array.Empty<Regex>();
    private static readonly IReadOnlyDictionary<string, string> EmptyAliases =
        new Dictionary<string, string>();

    #region AsciiFold

    [Fact]
    public void AsciiFold_Null_ReturnsNull()
        => Assert.Null(GameKeyNormalizer.AsciiFold(null!));

    [Fact]
    public void AsciiFold_Empty_ReturnsEmpty()
        => Assert.Equal("", GameKeyNormalizer.AsciiFold(""));

    [Fact]
    public void AsciiFold_WhitespaceOnly_ReturnsWhitespace()
        => Assert.Equal("   ", GameKeyNormalizer.AsciiFold("   "));

    [Fact]
    public void AsciiFold_Eszett_BecomesSs()
        => Assert.Equal("strasse", GameKeyNormalizer.AsciiFold("straße"));

    [Fact]
    public void AsciiFold_CapitalEszett_BecomesSs()
        => Assert.Equal("STRAssE", GameKeyNormalizer.AsciiFold("STRAẞE"));

    [Fact]
    public void AsciiFold_TurkishDotlessI_BecomesI()
        => Assert.Equal("istanbul", GameKeyNormalizer.AsciiFold("ıstanbul"));

    [Fact]
    public void AsciiFold_TurkishDottedI_BecomesI()
        => Assert.Equal("Istanbul", GameKeyNormalizer.AsciiFold("İstanbul"));

    [Fact]
    public void AsciiFold_SmartQuotes_BecomeAscii()
    {
        var result = GameKeyNormalizer.AsciiFold("\u2018hello\u2019");
        Assert.Equal("'hello'", result);
    }

    [Fact]
    public void AsciiFold_EmDash_BecomesHyphen()
        => Assert.Contains("-", GameKeyNormalizer.AsciiFold("word\u2014word"));

    [Fact]
    public void AsciiFold_EnDash_BecomesHyphen()
        => Assert.Contains("-", GameKeyNormalizer.AsciiFold("word\u2013word"));

    [Fact]
    public void AsciiFold_AeLigature_BecomesAE()
        => Assert.Equal("AEsop", GameKeyNormalizer.AsciiFold("Æsop"));

    [Fact]
    public void AsciiFold_AeLigatureLower_BecomesAe()
        => Assert.Equal("aesop", GameKeyNormalizer.AsciiFold("æsop"));

    [Fact]
    public void AsciiFold_OSlash_BecomesO()
        => Assert.Contains("O", GameKeyNormalizer.AsciiFold("Ø"));

    [Fact]
    public void AsciiFold_OSlashLower_BecomesO()
        => Assert.Contains("o", GameKeyNormalizer.AsciiFold("ø"));

    [Fact]
    public void AsciiFold_Eth_BecomesD()
        => Assert.Equal("D", GameKeyNormalizer.AsciiFold("Đ"));

    [Fact]
    public void AsciiFold_EthLower_BecomesD()
        => Assert.Equal("d", GameKeyNormalizer.AsciiFold("đ"));

    [Fact]
    public void AsciiFold_PolishL_BecomesL()
        => Assert.Equal("L", GameKeyNormalizer.AsciiFold("Ł"));

    [Fact]
    public void AsciiFold_PolishLLower_BecomesL()
        => Assert.Equal("l", GameKeyNormalizer.AsciiFold("ł"));

    [Fact]
    public void AsciiFold_Oe_BecomesOE()
        => Assert.Equal("OEuvre", GameKeyNormalizer.AsciiFold("Œuvre"));

    [Fact]
    public void AsciiFold_OeLower_BecomesOe()
        => Assert.Equal("oeuvre", GameKeyNormalizer.AsciiFold("œuvre"));

    [Fact]
    public void AsciiFold_Thorn_BecomesTh()
        => Assert.Equal("Th", GameKeyNormalizer.AsciiFold("Þ"));

    [Fact]
    public void AsciiFold_ThornLower_BecomesTh()
        => Assert.Equal("th", GameKeyNormalizer.AsciiFold("þ"));

    [Fact]
    public void AsciiFold_FullwidthExclamation_BecomesAscii()
        => Assert.Equal("!", GameKeyNormalizer.AsciiFold("\uFF01"));

    [Fact]
    public void AsciiFold_FullwidthA_BecomesAscii()
        => Assert.Equal("A", GameKeyNormalizer.AsciiFold("\uFF21"));

    [Fact]
    public void AsciiFold_FullwidthTilde_BecomesAscii()
        => Assert.Equal("~", GameKeyNormalizer.AsciiFold("\uFF5E"));

    [Fact]
    public void AsciiFold_IdeographicSpace_ReturnedAsIs_WhenOnlyWhitespace()
    {
        // \u3000 is classified as whitespace by .NET → IsNullOrWhiteSpace returns true → short-circuit
        var result = GameKeyNormalizer.AsciiFold("\u3000");
        Assert.Equal("\u3000", result);
    }

    [Fact]
    public void AsciiFold_IdeographicSpaceMixedWithText_ConvertedToSpace()
    {
        var result = GameKeyNormalizer.AsciiFold("hello\u3000world");
        Assert.Equal("hello world", result);
    }

    [Fact]
    public void AsciiFold_AccentedChars_Stripped()
    {
        var result = GameKeyNormalizer.AsciiFold("café résumé naïve");
        Assert.Equal("cafe resume naive", result);
    }

    #endregion

    #region Normalize with explicit patterns

    [Fact]
    public void Normalize_EmptyBaseName_ReturnsEmptyKeyNull()
        => Assert.Equal("__empty_key_null", GameKeyNormalizer.Normalize("", EmptyPatterns, EmptyAliases));

    [Fact]
    public void Normalize_WhitespaceBaseName_ReturnsEmptyKeyNull()
        => Assert.Equal("__empty_key_null", GameKeyNormalizer.Normalize("   ", EmptyPatterns, EmptyAliases));

    [Fact]
    public void Normalize_NullBaseName_ReturnsEmptyKeyNull()
        => Assert.Equal("__empty_key_null", GameKeyNormalizer.Normalize(null!, EmptyPatterns, EmptyAliases));

    [Fact]
    public void Normalize_SimpleName_ReturnsLowerNoSpaces()
    {
        var key = GameKeyNormalizer.Normalize("Super Mario World", EmptyPatterns, EmptyAliases);
        Assert.Equal("supermarioworld", key);
    }

    [Fact]
    public void Normalize_WithTagPattern_RemovesTag()
    {
        var regionPattern = new Regex(@"\((?:USA|Europe|Japan)\)", RegexOptions.IgnoreCase);
        var patterns = new[] { regionPattern };
        var key = GameKeyNormalizer.Normalize("Zelda (USA)", patterns, EmptyAliases);
        Assert.Equal("zelda", key);
    }

    [Fact]
    public void Normalize_WithAlias_ResolvesAlias()
    {
        var aliases = new Dictionary<string, string> { ["zelda"] = "legendofzelda" };
        var key = GameKeyNormalizer.Normalize("Zelda", EmptyPatterns, aliases);
        Assert.Equal("legendofzelda", key);
    }

    [Fact]
    public void Normalize_WithEditionAlias_WhenEnabled_ResolvesEditionAlias()
    {
        var editionAliases = new Dictionary<string, string> { ["zeldagoty"] = "zelda" };
        var key = GameKeyNormalizer.Normalize("Zelda GOTY", EmptyPatterns, EmptyAliases,
            editionAliasMap: editionAliases, aliasEditionKeying: true);
        Assert.Equal("zelda", key);
    }

    [Fact]
    public void Normalize_WithEditionAlias_WhenDisabled_DoesNotResolve()
    {
        var editionAliases = new Dictionary<string, string> { ["zeldagoty"] = "zelda" };
        var key = GameKeyNormalizer.Normalize("Zelda GOTY", EmptyPatterns, EmptyAliases,
            editionAliasMap: editionAliases, aliasEditionKeying: false);
        Assert.Equal("zeldagoty", key);
    }

    [Fact]
    public void Normalize_TrailingArticle_Removed()
    {
        var key = GameKeyNormalizer.Normalize("Legend, The", EmptyPatterns, EmptyAliases);
        Assert.Equal("legend", key);
    }

    [Fact]
    public void Normalize_LeadingArticle_Removed()
    {
        var key = GameKeyNormalizer.Normalize("The Legend", EmptyPatterns, EmptyAliases);
        Assert.Equal("legend", key);
    }

    [Fact]
    public void Normalize_DiscPadding_Normalized()
    {
        // (disc 01) → (disc 1)
        var key = GameKeyNormalizer.Normalize("Game (disc 01)", EmptyPatterns, EmptyAliases);
        Assert.Contains("disc1", key);
    }

    [Fact]
    public void Normalize_IsoDateTag_Stripped()
    {
        var key = GameKeyNormalizer.Normalize("Game (2024-01-15)", EmptyPatterns, EmptyAliases);
        Assert.DoesNotContain("2024", key);
    }

    [Fact]
    public void Normalize_IsIdempotent()
    {
        var key1 = GameKeyNormalizer.Normalize("Super Mario (USA) (Rev 1)", EmptyPatterns, EmptyAliases);
        var key2 = GameKeyNormalizer.Normalize(key1, EmptyPatterns, EmptyAliases);
        Assert.Equal(key1, key2);
    }

    [Fact]
    public void Normalize_ZeroWidthSpace_DoesNotChangeKey_FindingF05()
    {
        var plain = GameKeyNormalizer.Normalize("Pokemon", EmptyPatterns, EmptyAliases);
        var withZeroWidthSpace = GameKeyNormalizer.Normalize("Po\u200Bkemon", EmptyPatterns, EmptyAliases);

        Assert.Equal(plain, withZeroWidthSpace);
    }

    [Fact]
    public void Normalize_ResultAlwaysLowercase()
    {
        var key = GameKeyNormalizer.Normalize("SUPER MARIO WORLD", EmptyPatterns, EmptyAliases);
        Assert.Equal(key, key.ToLowerInvariant());
    }

    [Fact]
    public void Normalize_AliasAndEditionAlias_AlwaysAliasTakesPrecedence()
    {
        var aliases = new Dictionary<string, string> { ["game"] = "alias1" };
        var editionAliases = new Dictionary<string, string> { ["game"] = "alias2" };
        // Always-alias applied first, so edition alias won't match "alias1"
        var key = GameKeyNormalizer.Normalize("Game", EmptyPatterns, aliases,
            editionAliasMap: editionAliases, aliasEditionKeying: true);
        Assert.Equal("alias1", key);
    }

    #endregion

    #region Normalize with DOS console type

    [Fact]
    public void Normalize_DosConsoleType_StripsTrailingParens()
    {
        // DOS-specific: trailing non-disc paren tags removed
        var key = GameKeyNormalizer.Normalize("Game (DeVeL)", EmptyPatterns, EmptyAliases, consoleType: "DOS");
        Assert.DoesNotContain("devel", key);
        Assert.Equal("game", key);
    }

    [Fact]
    public void Normalize_DosConsoleType_StripsTrailingBrackets()
    {
        // DOS-specific: trailing bracket tags removed
        var key = GameKeyNormalizer.Normalize("Game [ID42]", EmptyPatterns, EmptyAliases, consoleType: "DOS");
        Assert.DoesNotContain("[", key);
        Assert.Equal("game", key);
    }

    [Fact]
    public void Normalize_DosConsoleType_CaseInsensitive()
    {
        var key1 = GameKeyNormalizer.Normalize("Game [ID]", EmptyPatterns, EmptyAliases, consoleType: "DOS");
        var key2 = GameKeyNormalizer.Normalize("Game [ID]", EmptyPatterns, EmptyAliases, consoleType: "dos");
        Assert.Equal(key1, key2);
    }

    [Fact]
    public void Normalize_NonDosConsoleType_KeepsMetadata()
    {
        var key = GameKeyNormalizer.Normalize("Game [ID42]", EmptyPatterns, EmptyAliases, consoleType: "SNES");
        Assert.Contains("[id42]", key);
    }

    #endregion

    #region RemoveMsDosMetadataTags (internal)

    [Fact]
    public void RemoveMsDosMetadataTags_NullInput_ReturnsNull()
        => Assert.Null(GameKeyNormalizer.RemoveMsDosMetadataTags(null!));

    [Fact]
    public void RemoveMsDosMetadataTags_Empty_ReturnsEmpty()
        => Assert.Equal("", GameKeyNormalizer.RemoveMsDosMetadataTags(""));

    [Fact]
    public void RemoveMsDosMetadataTags_WhitespaceOnly_ReturnsWhitespace()
        => Assert.Equal("   ", GameKeyNormalizer.RemoveMsDosMetadataTags("   "));

    [Fact]
    public void RemoveMsDosMetadataTags_TrailingBracket_Stripped()
    {
        var result = GameKeyNormalizer.RemoveMsDosMetadataTags("Game Title [v1.0]");
        Assert.DoesNotContain("[v1.0]", result);
        Assert.StartsWith("Game Title", result);
    }

    [Fact]
    public void RemoveMsDosMetadataTags_MultipleBrackets_AllStripped()
    {
        var result = GameKeyNormalizer.RemoveMsDosMetadataTags("Game [tag1] [tag2] [tag3]");
        Assert.DoesNotContain("[", result);
        Assert.StartsWith("Game", result);
    }

    [Fact]
    public void RemoveMsDosMetadataTags_TrailingNonDiscParen_Stripped()
    {
        var result = GameKeyNormalizer.RemoveMsDosMetadataTags("Game (SomeLabel)");
        Assert.DoesNotContain("SomeLabel", result);
    }

    [Fact]
    public void RemoveMsDosMetadataTags_DiscParen_Preserved()
    {
        // (disc 1) should NOT be stripped
        var result = GameKeyNormalizer.RemoveMsDosMetadataTags("Game (disc 1)");
        Assert.Contains("disc 1", result);
    }

    [Fact]
    public void RemoveMsDosMetadataTags_SideParen_Preserved()
    {
        var result = GameKeyNormalizer.RemoveMsDosMetadataTags("Game (Side A)");
        Assert.Contains("Side A", result);
    }

    [Fact]
    public void RemoveMsDosMetadataTags_CdParen_Preserved()
    {
        var result = GameKeyNormalizer.RemoveMsDosMetadataTags("Game (CD2)");
        Assert.Contains("CD2", result);
    }

    #endregion

    #region RegisterDefaultPatterns – null guards

    [Fact]
    public void RegisterDefaultPatterns_NullPatterns_Throws()
        => Assert.Throws<ArgumentNullException>(() =>
            GameKeyNormalizer.RegisterDefaultPatterns(null!, EmptyAliases));

    [Fact]
    public void RegisterDefaultPatterns_NullAliasMap_Throws()
        => Assert.Throws<ArgumentNullException>(() =>
            GameKeyNormalizer.RegisterDefaultPatterns(EmptyPatterns, null!));

    #endregion

    #region RegisterPatternFactory – null guard

    [Fact]
    public void RegisterPatternFactory_Null_Throws()
        => Assert.Throws<ArgumentNullException>(() =>
            GameKeyNormalizer.RegisterPatternFactory(null!));

    #endregion
}
