using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Romulus.Core.GameKeys;

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
    private static readonly TimeSpan RegexTimeout = SafeRegex.DefaultTimeout;

    /// <summary>
    /// Registered tag patterns from rules.json. Set by Infrastructure at startup via
    /// <see cref="RegisterDefaultPatterns"/>. When set, the convenience <see cref="Normalize(string)"/>
    /// overload uses these instead of requiring explicit pattern injection.
    /// </summary>
    private static readonly object _registrationLock = new();
    private static volatile IReadOnlyList<System.Text.RegularExpressions.Regex>? _registeredPatterns;
    private static volatile IReadOnlyDictionary<string, string>? _registeredAliasMap;
    private static volatile Func<(IReadOnlyList<System.Text.RegularExpressions.Regex>? Patterns, IReadOnlyDictionary<string, string> Aliases)>? _patternFactory;

    /// <summary>
    /// Registers the default tag patterns and alias map (typically loaded from rules.json).
    /// Call once at application startup from Infrastructure.
    /// </summary>
    public static void RegisterDefaultPatterns(
        IReadOnlyList<System.Text.RegularExpressions.Regex> tagPatterns,
        IReadOnlyDictionary<string, string> alwaysAliasMap)
    {
        ArgumentNullException.ThrowIfNull(tagPatterns);
        ArgumentNullException.ThrowIfNull(alwaysAliasMap);

        lock (_registrationLock)
        {
            _registeredPatterns = tagPatterns;
            _registeredAliasMap = alwaysAliasMap;
        }
    }

    /// <summary>
    /// Registers a lazy pattern factory. Called by Infrastructure so that the convenience
    /// <see cref="Normalize(string)"/> overload can resolve patterns on first use without
    /// coupling Core to Infrastructure at compile time.
    /// </summary>
    public static void RegisterPatternFactory(
        Func<(IReadOnlyList<System.Text.RegularExpressions.Regex>? Patterns, IReadOnlyDictionary<string, string> Aliases)> factory)
    {
        lock (_registrationLock)
        {
            _patternFactory = factory ?? throw new ArgumentNullException(nameof(factory));
        }
    }

    private static void EnsurePatternsLoaded()
    {
        if (_registeredPatterns is not null) return;

        lock (_registrationLock)
        {
            if (_registeredPatterns is not null) return;
            if (_patternFactory is null) return;
            var (patterns, aliases) = _patternFactory();
            if (patterns is not null)
            {
                _registeredPatterns = patterns;
                _registeredAliasMap = aliases;
            }
        }
    }

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
    /// Convenience overload using registered tag patterns from rules.json.
    /// Patterns are resolved lazily via the registered pattern factory if not yet loaded.
    /// </summary>
    public static string Normalize(string baseName)
    {
        EnsurePatternsLoaded();
        return Normalize(baseName, _registeredPatterns ?? [], _registeredAliasMap ?? EmptyAliasMap);
    }

    /// <summary>
    /// Folds Unicode text to ASCII by removing diacritical marks.
    /// Port of ConvertTo-AsciiFold from Core.ps1.
    /// </summary>
    public static string AsciiFold(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var work = ConvertFullwidthAscii(text)
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
                category != UnicodeCategory.EnclosingMark &&
                category != UnicodeCategory.Format &&
                category != UnicodeCategory.Control)
            {
                sb.Append(ch);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string ConvertFullwidthAscii(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            // Convert fullwidth ASCII variants to halfwidth ASCII.
            if (ch >= '\uFF01' && ch <= '\uFF5E')
            {
                sb.Append((char)(ch - 0xFEE0));
            }
            else if (ch == '\u3000')
            {
                sb.Append(' ');
            }
            else
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
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
