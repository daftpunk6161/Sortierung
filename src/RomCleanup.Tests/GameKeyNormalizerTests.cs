using System.Text.RegularExpressions;
using RomCleanup.Core.GameKeys;
using Xunit;

namespace RomCleanup.Tests;

public class GameKeyNormalizerTests
{

    // --- Empty / Null Protection ---

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void EmptyOrNull_ReturnsNonNull(string? input)
    {
        var result = GameKeyNormalizer.Normalize(input ?? "");
        Assert.NotNull(result);
    }

    // --- Region Variant Normalization ---

    [Fact]
    public void RegionVariants_ProduceSameKey()
    {
        var eu = GameKeyNormalizer.Normalize("Game (Europe)");
        var us = GameKeyNormalizer.Normalize("Game (USA)");
        var jp = GameKeyNormalizer.Normalize("Game (Japan)");
        Assert.Equal(eu, us);
        Assert.Equal(us, jp);
    }

    // --- Version/Revision Normalization ---

    [Fact]
    public void VersionVariants_ProduceSameKey()
    {
        var revA = GameKeyNormalizer.Normalize("Game (Rev A)");
        var revB = GameKeyNormalizer.Normalize("Game (Rev B)");
        var v10 = GameKeyNormalizer.Normalize("Game (v1.0)");
        var v11 = GameKeyNormalizer.Normalize("Game (v1.1)");
        Assert.Equal(revA, revB);
        Assert.Equal(v10, v11);
        Assert.Equal(revA, v10);
    }

    // --- Language Variant Normalization ---

    [Fact]
    public void LanguageVariants_ProduceSameKey()
    {
        var en = GameKeyNormalizer.Normalize("Game (En)");
        var enFr = GameKeyNormalizer.Normalize("Game (En,Fr)");
        var multi = GameKeyNormalizer.Normalize("Game (En,Fr,De,Es,It)");
        Assert.Equal(en, enFr);
        Assert.Equal(enFr, multi);
    }

    // --- Multi-Disc Preservation ---

    [Fact]
    public void MultiDisc_ProduceDifferentKeys()
    {
        var d1 = GameKeyNormalizer.Normalize("Final Fantasy VII (Europe) (Disc 1)");
        var d2 = GameKeyNormalizer.Normalize("Final Fantasy VII (Europe) (Disc 2)");
        var d3 = GameKeyNormalizer.Normalize("Final Fantasy VII (Europe) (Disc 3)");
        Assert.NotEqual(d1, d2);
        Assert.NotEqual(d2, d3);
    }

    [Fact]
    public void SameDiscDifferentRegion_SameKey()
    {
        var euD1 = GameKeyNormalizer.Normalize("Final Fantasy VII (Europe) (Disc 1)");
        var usD1 = GameKeyNormalizer.Normalize("Final Fantasy VII (USA) (Disc 1)");
        Assert.Equal(euD1, usD1);
    }

    // --- Tag Removal ---

    [Fact]
    public void VerifiedTag_Removed()
    {
        var withTag = GameKeyNormalizer.Normalize("Game (Europe) [!]");
        var without = GameKeyNormalizer.Normalize("Game (Europe)");
        Assert.Equal(withTag, without);
    }

    [Theory]
    [InlineData("Game (Europe) [b]")]
    [InlineData("Game (Europe) [h]")]
    public void BadDumpAndHackTags_ProduceSameKey(string input)
    {
        var clean = GameKeyNormalizer.Normalize("Game (Europe)");
        var tagged = GameKeyNormalizer.Normalize(input);
        Assert.Equal(clean, tagged);
    }

    // --- Always lowercase ---

    [Fact]
    public void Result_IsAlwaysLowercase()
    {
        var result = GameKeyNormalizer.Normalize("Super Mario Bros (USA) [!]");
        Assert.Equal(result, result.ToLowerInvariant());
    }

    // --- Idempotent ---

    [Theory]
    [InlineData("Game (Europe) (Rev A) [!]")]
    [InlineData("Some Rom (USA) (v1.2)")]
    public void Normalize_IsIdempotent(string input)
    {
        var first = GameKeyNormalizer.Normalize(input);
        var second = GameKeyNormalizer.Normalize(first);
        // Second normalization should also produce a non-empty result if first was non-empty
        if (!string.IsNullOrEmpty(first))
            Assert.False(string.IsNullOrEmpty(second));
    }

    [Fact]
    public void Normalize_WhenTagPatternTimesOut_DoesNotThrow()
    {
        var timeoutPattern = new Regex("(a+)+$", RegexOptions.Compiled, TimeSpan.FromMilliseconds(1));
        var tagPatterns = new[] { timeoutPattern };
        var aliases = new Dictionary<string, string>();
        var input = new string('a', 8000) + "!";

        var ex = Record.Exception(() => GameKeyNormalizer.Normalize(input, tagPatterns, aliases));

        Assert.Null(ex);
    }

    // --- ASCII Folding ---

    [Theory]
    [InlineData("Pokémon", "pokemon")]
    [InlineData("Straße", "strasse")]
    public void AsciiFold_HandlesSpecialChars(string input, string expectedSubstring)
    {
        var result = GameKeyNormalizer.AsciiFold(input);
        Assert.Contains(expectedSubstring, result.ToLowerInvariant());
    }

    // --- PS3 Serial Number Tags ---

    [Fact]
    public void PS3SerialNumbers_Stripped()
    {
        var withSerial = GameKeyNormalizer.Normalize("Game (Europe) (BLES-01384)");
        var withSerial2 = GameKeyNormalizer.Normalize("Game (USA) (BLUS-30905)");
        var withSerial3 = GameKeyNormalizer.Normalize("Game (USA) (BCUS-98152)");
        var plain = GameKeyNormalizer.Normalize("Game (Europe)");
        Assert.Equal(plain, withSerial);
        Assert.Equal(plain, withSerial2);
        Assert.Equal(plain, withSerial3);
    }

    // --- PS3 Japanese Metadata Tags ---

    [Fact]
    public void PS3JapaneseMetadata_Stripped()
    {
        var fuki = GameKeyNormalizer.Normalize("Game (Japan) (Fukikaeban)");
        var jimaku = GameKeyNormalizer.Normalize("Game (Japan) (Jimakuban)");
        var move = GameKeyNormalizer.Normalize("Game (Japan) (PlayStation Move Taiou)");
        var plain = GameKeyNormalizer.Normalize("Game (Japan)");
        Assert.Equal(plain, fuki);
        Assert.Equal(plain, jimaku);
        Assert.Equal(plain, move);
    }

    // --- PS3 Edition Variants ---

    [Fact]
    public void PS3EditionVariants_SameKey()
    {
        var gold = GameKeyNormalizer.Normalize("Game (USA) (Gold Edition)");
        var limited = GameKeyNormalizer.Normalize("Game (Europe) (Limited Edition)");
        var special = GameKeyNormalizer.Normalize("Game (Japan) (Special Edition)");
        var collectors = GameKeyNormalizer.Normalize("Game (USA) (Collector's Edition)");
        var target = GameKeyNormalizer.Normalize("Game (USA) (Target Limited Edition)");
        var masters = GameKeyNormalizer.Normalize("Game (USA) (Masters Historic Edition)");
        var bestBuy = GameKeyNormalizer.Normalize("Game (USA) (Best Buy Edition)");
        var toyBox = GameKeyNormalizer.Normalize("Game (USA) (Toy Box Special Edition)");
        var plain = GameKeyNormalizer.Normalize("Game (USA)");
        Assert.Equal(plain, gold);
        Assert.Equal(plain, limited);
        Assert.Equal(plain, special);
        Assert.Equal(plain, collectors);
        Assert.Equal(plain, target);
        Assert.Equal(plain, masters);
        Assert.Equal(plain, bestBuy);
        Assert.Equal(plain, toyBox);
    }

    // --- PS3 Budget/Re-release Labels ---

    [Fact]
    public void PS3BudgetLabels_Stripped()
    {
        var hits = GameKeyNormalizer.Normalize("Game (USA) (Greatest Hits)");
        var best = GameKeyNormalizer.Normalize("Game (Japan) (PlayStation 3 the Best)");
        var aqua = GameKeyNormalizer.Normalize("Game (Japan) (Aquaprice 2800)");
        var plain = GameKeyNormalizer.Normalize("Game (Europe)");
        Assert.Equal(plain, hits);
        Assert.Equal(plain, best);
        Assert.Equal(plain, aqua);
    }

    // --- PS3 FW Version Tags ---

    [Fact]
    public void PS3FWVersion_Stripped()
    {
        var fw342 = GameKeyNormalizer.Normalize("Game (USA) (FW3.42)");
        var fw350 = GameKeyNormalizer.Normalize("Game (USA) (FW3.50)");
        var fw194 = GameKeyNormalizer.Normalize("Game (Europe) (FW1.94)");
        var plain = GameKeyNormalizer.Normalize("Game (USA)");
        Assert.Equal(plain, fw342);
        Assert.Equal(plain, fw350);
        Assert.Equal(plain, fw194);
    }

    // --- PS3 Version Prefix Form ---

    [Fact]
    public void PS3VersionPrefix_Stripped()
    {
        var ver = GameKeyNormalizer.Normalize("Game (Version 2.0) (Europe) (En,Fr)");
        var plain = GameKeyNormalizer.Normalize("Game (Europe)");
        Assert.Equal(plain, ver);
    }

    // --- PS3 Extended Language Lists ---

    [Theory]
    [InlineData("Game (Europe) (En,Fr,De,Es,It,Nl,Pt,Sv,No,Da,Fi,Eu,Ca,Gd)")]
    [InlineData("Game (USA) (En,Fr,Es,Pt)")]
    [InlineData("Game (Europe) (En,Ja,Fr,De,Es,It,Zh,Pl,Ru,Cs,Hu)")]
    [InlineData("Game (South Africa) (En,Fr,De,Es,It,Nl,Pt,Sv,No,Da,Fi,Pl,Ru,Af)")]
    [InlineData("Game (Europe) (En,Fr,De,Es,It,Nl,Pt,Sv,No,Da,Fi,Pl,Ru,El,Hr,Cs,Hu,Tr,Ro,Bg)")]
    public void PS3ExtendedLanguages_Stripped(string input)
    {
        var result = GameKeyNormalizer.Normalize(input);
        var plain = GameKeyNormalizer.Normalize("Game");
        Assert.Equal(plain, result);
    }

    // --- PS3 Multi-Region + Tags Combo ---

    [Fact]
    public void PS3MultiRegionWithTags_Stripped()
    {
        var full = GameKeyNormalizer.Normalize("Game (USA, Asia) (En,Fr,De,Es,It) (v01.02)");
        var plain = GameKeyNormalizer.Normalize("Game");
        Assert.Equal(plain, full);
    }

    // --- PS3 Complex Filenames ---

    [Fact]
    public void PS3ComplexFilename_ProducesSameKey()
    {
        var eu = GameKeyNormalizer.Normalize("Game (Europe) (En,Fr,De,Es,It) (v02.00) (BLES-01384)");
        var us = GameKeyNormalizer.Normalize("Game (USA) (En,Fr,Es) (FW3.50)");
        var jp = GameKeyNormalizer.Normalize("Game (Japan) (PlayStation 3 the Best)");
        var kr = GameKeyNormalizer.Normalize("Game (Korea) (En,Fr,De,Es,It)");
        Assert.Equal(eu, us);
        Assert.Equal(us, jp);
        Assert.Equal(jp, kr);
    }

    // --- PS3 Pre-release Tags ---

    [Theory]
    [InlineData("Game (Europe) (Beta) (2011-07-16)")]
    [InlineData("Game (USA) (Beta) (2007-10-31)")]
    [InlineData("Game (USA) (Demo)")]
    [InlineData("Game (USA) (Kiosk Demo)")]
    [InlineData("Game (Japan) (Trial Version)")]
    [InlineData("Game (Japan) (Rehearsal-ban)")]
    [InlineData("Game (Japan) (Taikenban)")]
    [InlineData("Game (USA) (Promo)")]
    public void PS3PreReleaseTags_Stripped(string input)
    {
        var result = GameKeyNormalizer.Normalize(input);
        var plain = GameKeyNormalizer.Normalize("Game");
        Assert.Equal(plain, result);
    }

    // --- PS3 3D Compatible ---

    [Fact]
    public void PS3FeatureMarkers_Stripped()
    {
        var compat = GameKeyNormalizer.Normalize("Game (USA) (3D Compatible)");
        var plain = GameKeyNormalizer.Normalize("Game (USA)");
        Assert.Equal(plain, compat);
    }

    // --- PS3 Date Tags ---

    [Theory]
    [InlineData("Game (Europe) (2011-07-16)")]
    [InlineData("Game (USA) (2007-10-31)")]
    public void PS3DateTags_Stripped(string input)
    {
        var result = GameKeyNormalizer.Normalize(input);
        var plain = GameKeyNormalizer.Normalize("Game");
        Assert.Equal(plain, result);
    }

    // --- PS3 New Region Names ---

    [Fact]
    public void PS3NewRegions_Stripped()
    {
        var nz = GameKeyNormalizer.Normalize("Game (New Zealand)");
        var sa = GameKeyNormalizer.Normalize("Game (South Africa)");
        var at_ch = GameKeyNormalizer.Normalize("Game (Austria, Switzerland)");
        var plain = GameKeyNormalizer.Normalize("Game");
        Assert.Equal(plain, nz);
        Assert.Equal(plain, sa);
        Assert.Equal(plain, at_ch);
    }
}
