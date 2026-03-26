using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace RomCleanup.Core.GameKeys;

/// <summary>
/// Pure game key normalization logic.
/// Port of ConvertTo-GameKey and ConvertTo-AsciiFold from Core.ps1.
/// GameKey algorithm:
///   1. ASCII-Fold (diacritics: ß→ss, é→e etc.)
///   2. Apply region/version/junk tag patterns
///   3. Whitespace normalize
///   4. Alias map lookup
///   5. Return normalized key
/// </summary>
public static class GameKeyNormalizer
{
    // Default tag patterns — comprehensive set from rules.json GameKeyPatterns
    private static readonly IReadOnlyList<System.Text.RegularExpressions.Regex> DefaultTagPatterns =
        BuildDefaultTagPatterns();

    private static System.Text.RegularExpressions.Regex[] BuildDefaultTagPatterns()
    {
        var opts = System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled;
        var timeout = TimeSpan.FromMilliseconds(500);
        return new[]
        {
            // 1. Region tags (all countries/codes from rules.json GameKeyPatterns[0])
            new System.Text.RegularExpressions.Regex(
                @"\s*\((?:" +
                // Major regions
                @"europe|eu|eur|pal|usa|us|u\.s\.a\.|u\.s\.|japan|jp|jpn|world|export|" +
                // Asia
                @"asia|as|korea|kr|kor|china|cn|chn|taiwan|tw|hong\s*kong|hk|india|in|singapore|thailand|vietnam|indonesia|malaysia|philippines|" +
                // Americas
                @"brazil|br|bra|australia|au|aus|canada|ca|can|latin\s*america|" +
                // EU countries (full + ISO codes)
                @"france|fr|fra|germany|de|deu|spain|es|esp|italy|it|ita|netherlands|nl|nld|sweden|se|swe|scandinavia|" +
                @"uk|united\s*kingdom|great\s*britain|england|belgium|be|austria|at|portugal|pt|switzerland|ch|" +
                @"denmark|dk|finland|fi|norway|no|czech|cz|hungary|hu|croatia|hr|greece|el|ireland|ie|" +
                @"luxembourg|romania|ro|bulgaria|bg|slovakia|sk|slovenia|si|estonia|et|latvia|lv|lithuania|lt|" +
                // Other
                @"russia|ru|rus|poland|pl|pol|turkey|tr|united\s*arab\s*emirates|ntsc-u|ntsc-j|ntsc|" +
                @"south\s*africa|za|new\s*zealand|nz|nzl" +
                @")(?:,\s*(?:" +
                // Same list for multi-region combos like (USA, Asia) or (Europe, Australia)
                @"europe|eu|eur|pal|usa|us|japan|jp|jpn|world|asia|as|korea|kr|china|cn|brazil|br|australia|au|" +
                @"france|fr|germany|de|spain|es|italy|it|netherlands|nl|sweden|se|scandinavia|canada|ca|" +
                @"russia|ru|rus|poland|pl|pol|uk|united\s*kingdom|great\s*britain|england|belgium|be|austria|at|" +
                @"portugal|pt|switzerland|ch|denmark|dk|finland|fi|norway|no|czech|cz|hungary|hu|taiwan|tw|" +
                @"hong\s*kong|hk|india|in|latin\s*america|turkey|tr|south\s*africa|new\s*zealand|nz" +
                @"))*\)\s*", opts, timeout),

            // 2. Headered/Headerless
            new System.Text.RegularExpressions.Regex(@"\s*\((headered|headerless)\)\s*", opts, timeout),

            // 3. Revision tags
            new System.Text.RegularExpressions.Regex(@"\s*\((rev\s*[a-z0-9.]+|revision\s*[a-z0-9.]+)\)\s*", opts, timeout),

            // 4. Version tags (v1.0, v02.01, etc.)
            new System.Text.RegularExpressions.Regex(@"\s*\((v\s*[0-9][0-9.]*[a-z]?)\)\s*", opts, timeout),

            // 5. Demo/Beta/Proto/Kiosk/Trial/Taikenban and other pre-release tags
            new System.Text.RegularExpressions.Regex(
                @"\s*\((alpha\s*\d*|beta\s*\d*|proto(?:type)?\s*\d*|sample|sampler|demo|preview|pre[\s-]*release|promo|kiosk(?:\s*demo)?|debug|trial(?:\s*version)?|taikenban|rehearsal-?\s*ban|location\s*test|test\s*program)\)\s*", opts, timeout),

            // 6. Utility/Program tags
            new System.Text.RegularExpressions.Regex(
                @"\s*\((program|application|utility|enhancement\s*chip|test\s*program|test\s*cartridge|competition\s*cart|service\s*disc|diagnostic|check\s*program)\)\s*", opts, timeout),

            // 7. Hack/Pirate/Homebrew
            new System.Text.RegularExpressions.Regex(
                @"\s*\((hack|pirate|bootleg|homebrew|aftermarket|translated|translation)\)\s*", opts, timeout),

            // 8. Unlicensed/NFR
            new System.Text.RegularExpressions.Regex(@"\s*\((unl|unlicensed|not\s*for\s*resale|nfr)\)\s*", opts, timeout),

            // 9. BIOS/Firmware/FW Update/IDU
            new System.Text.RegularExpressions.Regex(@"\s*\((bios|firmware)\)\s*", opts, timeout),
            new System.Text.RegularExpressions.Regex(@"\s*\b(?:IDU|FW\s*Update|FW\s*\d+\.\d+)\b\s*", opts, timeout),

            // 10. Language tags (full set including Af, Ca, Gd, Eu etc.)
            new System.Text.RegularExpressions.Regex(
                @"\s*\((en|fr|de|es|it|pt|nl|sv|no|da|fi|ru|pl|zh|ko|ja|cs|hu|el|tr|ar|he|th|vi|id|ms|ro|bg|uk|hr|sk|sl|et|lv|lt|af|ca|gd|eu)" +
                @"(?:,\s*(?:en|fr|de|es|it|pt|nl|sv|no|da|fi|ru|pl|zh|ko|ja|cs|hu|el|tr|ar|he|th|vi|id|ms|ro|bg|uk|hr|sk|sl|et|lv|lt|af|ca|gd|eu))*\)\s*", opts, timeout),

            // 11. Bracket tags: [!] [b] [h] [o] [p] [t] [f] [a] [cr...] [tr...] [m ...]
            new System.Text.RegularExpressions.Regex(@"\s*\[(?:\!|b\d*|h\d*|o\d*|p\d*|t\d*|f\d*|a\d*|cr[^\]]*|tr[^\]]*|m\s[^\]]*)\]\s*", opts, timeout),

            // 12. Virtual Console / Switch Online / Classic Mini
            new System.Text.RegularExpressions.Regex(@"\s*\((virtual\s*console|switch\s*online|classic\s*mini|wii\s*u|gamecube)\)\s*", opts, timeout),

            // 13. Reprint/Alt/Collection labels
            new System.Text.RegularExpressions.Regex(@"\s*\((reprint|rerelease|rerip|alt|alt\s*\d*|collection)\)\s*", opts, timeout),

            // 14. Collection/Anniversary/Archives/Museum/Classics (with optional surrounding text)
            new System.Text.RegularExpressions.Regex(@"\s*\(([^\)]*\b(?:collection|classics?|anniversary|antholog(?:y|ies)|archives?|museum|evercade|retro-?bit(?:\s*generations)?)\b[^\)]*)\)\s*", opts, timeout),

            // 15. EDC/Subchannel/LibCrypt
            new System.Text.RegularExpressions.Regex(@"\s*\((edc|no\s*edc|libcrypt|sbi|subchannel)\)\s*", opts, timeout),

            // 16. Sector count tags like (2S, 3S)
            new System.Text.RegularExpressions.Regex(@"\s*\((\d+S(?:,\s*\d+S)*)\)\s*", opts, timeout),

            // 17. "Made in X" tags
            new System.Text.RegularExpressions.Regex(@"\s*\((Made\s+in\s+\w+)\)\s*", opts, timeout),

            // 18. Parenthesized Edition catch-all (Gold Edition, Target Limited Edition, etc.)
            new System.Text.RegularExpressions.Regex(@"\s*\([^)]*\bEdition\b[^)]*\)\s*", opts, timeout),

            // 19. Non-parenthesized edition/budget labels (word-boundary fallback)
            new System.Text.RegularExpressions.Regex(
                @"\s*(?:-\s*)?\b(?:Collector'?s\s*Edition|Game\s*of\s*the\s*Year\s*Edition|Legendary\s*Edition|" +
                @"Ultimate\s*Edition|Complete\s*Edition|Special\s*Edition|Limited\s*Edition|Gold\s*Edition|" +
                @"National\s*Treasure\s*Edition|Game\s*of\s*the\s*Century\s*Edition|" +
                @"5th\s*Anniversary\s*Edition|Double\s*Pack|HD\s*(?:Collection|Edition|Remaster)|" +
                @"PlayStation\s*3\s*the\s*Best|Rockstar\s*Classics|Platinum|Greatest\s*Hits|" +
                @"Player'?s\s*Choice|Nintendo\s*Selects|PlayStation\s*Hits|Budget|Essentials|" +
                @"Best\s*Price|The\s*Best|Taikenban)\b\s*", opts, timeout),

            // 20. Date-stamped beta/proto tags like (Beta) (2010-07-08) or (2011-07-16)
            new System.Text.RegularExpressions.Regex(@"\s*\(\d{4}-\d{2}-\d{2}\)\s*", opts, timeout),

            // 21. FW version tags like (FW3.40), (FW3.50) and IDU prefixes
            new System.Text.RegularExpressions.Regex(@"\s*\(FW\d+\.\d+\)\s*", opts, timeout),

            // 22. Budget/re-release labels in parentheses
            new System.Text.RegularExpressions.Regex(
                @"\s*\((?:Greatest\s*Hits|PlayStation\s*\d+\s*the\s*Best|Platinum|Essentials|Budget|Best\s*Price|" +
                @"The\s*Best|Player'?s\s*Choice|Nintendo\s*Selects|PlayStation\s*Hits|Rockstar\s*Classics|" +
                @"Aquaprice\s*\d+)\)\s*", opts, timeout),

            // 23. Serial number tags (BLES-01384, BLUS-30905, BCUS-98152, etc.)
            new System.Text.RegularExpressions.Regex(@"\s*\([A-Z]{4}-\d{4,5}\)\s*", opts, timeout),

            // 24. Japanese-specific metadata and feature markers
            new System.Text.RegularExpressions.Regex(
                @"\s*\((?:Fukikaeban|Jimakuban|PlayStation\s*Move\s*Taiou|3D\s*Compatible)\)\s*", opts, timeout),

            // 25. Version prefix form: (Version 2.0)
            new System.Text.RegularExpressions.Regex(@"\s*\(Version\s*\d+\.?\d*\)\s*", opts, timeout),

            // 26. Empty parentheses cleanup (left after tag content removal)
            new System.Text.RegularExpressions.Regex(@"\s*\(\s*\)\s*", opts, timeout),
        };
    }

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(500);

    private static readonly System.Text.RegularExpressions.Regex MsDosTrailingBracketRegex =
        new(@"\s*(?:\[[^\]]+\]\s*)+$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled,
            RegexTimeout);

    private static readonly System.Text.RegularExpressions.Regex MsDosTrailingParenRegex =
        new(@"\s*\((?!\s*(?:disc|disk|side|cd\s*\d*|floppy|tape)\b)[^)]*\)\s*$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled,
            RegexTimeout);

    private static readonly System.Text.RegularExpressions.Regex LeadingArticleRegex =
        new(@"^\s*the\s+", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled, RegexTimeout);

    private static readonly System.Text.RegularExpressions.Regex TrailingArticleRegex =
        new(@"\s*,\s*the\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled, RegexTimeout);

    private static readonly System.Text.RegularExpressions.Regex DiscPaddingRegex =
        new(@"\((disc|disk|cd)\s*0+(\d+)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled, RegexTimeout);

    private static readonly IReadOnlyDictionary<string, string> EmptyAliasMap =
        new Dictionary<string, string>();

    /// <summary>
    /// Convenience overload using default tag patterns from rules.json.
    /// </summary>
    public static string Normalize(string baseName)
        => Normalize(baseName, DefaultTagPatterns, EmptyAliasMap);

    /// <summary>
    /// Folds Unicode text to ASCII by removing diacritical marks.
    /// Port of ConvertTo-AsciiFold from Core.ps1.
    /// </summary>
    public static string AsciiFold(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var work = text
            .Replace("ß", "ss").Replace("ẞ", "ss")
            .Replace("\u0131", "i").Replace("\u0130", "I") // BUG-027: Turkish İ/ı
            .Replace("\u2019", "'").Replace("\u2018", "'")
            .Replace("\u2013", "-").Replace("\u2014", "-")
            // V2-M25: Non-decomposable ligatures and Nordic letters
            .Replace("Æ", "AE").Replace("æ", "ae")
            .Replace("Ø", "O").Replace("ø", "o")
            .Replace("Đ", "D").Replace("đ", "d")
            .Replace("Ł", "L").Replace("ł", "l")
            .Replace("Œ", "OE").Replace("œ", "oe")
            .Replace("Þ", "Th").Replace("þ", "th");

        var normalized = work.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category != UnicodeCategory.NonSpacingMark &&
                category != UnicodeCategory.SpacingCombiningMark &&
                category != UnicodeCategory.EnclosingMark)
            {
                sb.Append(ch);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Converts a ROM filename to a normalized game key for grouping duplicates.
    /// Port of ConvertTo-GameKey from Core.ps1.
    /// </summary>
    /// <param name="baseName">ROM filename without extension.</param>
    /// <param name="tagPatterns">Compiled regex patterns to strip (region, version, junk tags).</param>
    /// <param name="alwaysAliasMap">Alias map always applied.</param>
    /// <param name="editionAliasMap">Alias map only applied when edition keying is enabled.</param>
    /// <param name="aliasEditionKeying">Whether to apply edition alias map.</param>
    /// <param name="consoleType">Console type for special handling (e.g. DOS).</param>
    public static string Normalize(
        string baseName,
        IReadOnlyList<System.Text.RegularExpressions.Regex> tagPatterns,
        IReadOnlyDictionary<string, string> alwaysAliasMap,
        IReadOnlyDictionary<string, string>? editionAliasMap = null,
        bool aliasEditionKeying = false,
        string? consoleType = null)
    {
        if (string.IsNullOrWhiteSpace(baseName))
            return "__empty_key_null";

        var s = AsciiFold(baseName);

        // DOS-specific: strip trailing metadata tags
        if (string.Equals(consoleType?.Trim(), "DOS", StringComparison.OrdinalIgnoreCase))
        {
            s = RemoveMsDosMetadataTags(s);
        }

        // Apply all tag patterns (region, version, junk)
        foreach (var rx in tagPatterns)
        {
            s = SafeRegex.Replace(rx, s, " ");
        }

        // Deterministic fallback: strip parenthesized ISO date tags without regex.
        // This prevents behavior drift if a regex replacement path times out.
        s = StripIsoDateTags(s);

        // Normalize common title variants so article/disc naming maps to one key.
        s = NormalizeTitleVariants(s);

        // Normalize and collapse whitespace
        var key = s.Trim().ToLowerInvariant();
        key = SafeRegex.Replace(key, @"\s+", "", System.Text.RegularExpressions.RegexOptions.None, RegexTimeout);

        // Apply alias maps
        if (alwaysAliasMap.TryGetValue(key, out var aliased))
            key = aliased;

        if (aliasEditionKeying && editionAliasMap is not null &&
            editionAliasMap.TryGetValue(key, out var editionAliased))
            key = editionAliased;

        // Fallback for empty keys
        if (string.IsNullOrWhiteSpace(key))
            key = baseName.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(key))
            key = "__empty_key_" + ComputeStableKeySuffix(baseName);

        return key;
    }

    private static string ComputeStableKeySuffix(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes.AsSpan(0, 4)).ToLowerInvariant();
    }

    private static string NormalizeTitleVariants(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var normalized = SafeRegex.Replace(TrailingArticleRegex, value, string.Empty);
        normalized = SafeRegex.Replace(LeadingArticleRegex, normalized, string.Empty);
        normalized = SafeRegex.Replace(DiscPaddingRegex, normalized, "($1 $2)");
        return normalized;
    }

    private static string StripIsoDateTags(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var text = value;
        var index = 0;
        while (index < text.Length)
        {
            var open = text.IndexOf('(', index);
            if (open < 0)
                break;

            var close = text.IndexOf(')', open + 1);
            if (close < 0)
                break;

            var len = close - open - 1;
            if (len == 10)
            {
                var token = text.Substring(open + 1, len);
                if (IsIsoDateToken(token))
                {
                    text = text.Remove(open, (close - open) + 1);
                    continue;
                }
            }

            index = close + 1;
        }

        return text;
    }

    private static bool IsIsoDateToken(string token)
    {
        // Expected format: YYYY-MM-DD
        if (token.Length != 10)
            return false;

        return char.IsDigit(token[0]) && char.IsDigit(token[1]) && char.IsDigit(token[2]) && char.IsDigit(token[3])
            && token[4] == '-'
            && char.IsDigit(token[5]) && char.IsDigit(token[6])
            && token[7] == '-'
            && char.IsDigit(token[8]) && char.IsDigit(token[9]);
    }

    /// <summary>
    /// Removes trailing DOS metadata tags like [tag] and non-disc (parenthesized) tags.
    /// Port of Remove-MsDosMetadataTags from Core.ps1.
    /// </summary>
    internal static string RemoveMsDosMetadataTags(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // Remove trailing bracket tags: [anything]
        var value = SafeRegex.Replace(MsDosTrailingBracketRegex, text, " ");

        // Remove trailing non-disc parenthesized tags (limit iterations to prevent infinite loop)
        for (int i = 0; i < 20 && SafeRegex.IsMatch(MsDosTrailingParenRegex, value); i++)
        {
            value = SafeRegex.Replace(MsDosTrailingParenRegex, value, " ");
        }

        return value;
    }
}
