namespace Romulus.Core.Regions;

/// <summary>
/// Region detection from ROM filenames.
/// Port of Get-RegionTag from Core.ps1.
/// Uses ordered pattern matching against parenthesized region indicators.
/// </summary>
public static class RegionDetector
{
    private sealed record DetectionConfig(
        IReadOnlyList<RegionRule> OrderedRules,
        IReadOnlyList<RegionRule> TwoLetterRules,
        System.Text.RegularExpressions.Regex MultiRegionPattern,
        IReadOnlyDictionary<string, string> TokenToRegionMap,
        IReadOnlySet<string> LanguageCodes,
        IReadOnlySet<string> EuLanguageCodes);

    private static readonly object RegistrationSync = new();
    private static volatile DetectionConfig? _registeredConfig;
    private static Func<(
        IReadOnlyList<RegionRule> OrderedRules,
        IReadOnlyList<RegionRule> TwoLetterRules,
        System.Text.RegularExpressions.Regex MultiRegionPattern,
        IReadOnlyDictionary<string, string> TokenToRegionMap,
        IReadOnlyCollection<string> LanguageCodes,
        IReadOnlyCollection<string> EuLanguageCodes)>? _registrationFactory;

    /// <summary>
    /// Diagnostic result for region detection.
    /// </summary>
    public sealed record RegionDetectionResult(string Region, string DiagnosticReason);

    /// <summary>Standard region identifiers used across the codebase.</summary>
    public static class Regions
    {
        public const string EU = "EU";
        public const string US = "US";
        public const string JP = "JP";
        public const string World = "WORLD";
        public const string Unknown = "UNKNOWN";
    }

    /// <summary>
    /// Represents a region detection rule: a regex pattern mapped to a region key.
    /// </summary>
    public sealed record RegionRule(string Key, System.Text.RegularExpressions.Regex Pattern);

    private static readonly TimeSpan RegexTimeout = SafeRegex.DefaultTimeout;

    // Fallback rules preserving historical behavior if no external profile is registered.
    private static readonly IReadOnlyList<RegionRule> FallbackOrderedRules = new[]
    {
        // Major regions (priority order)
        R("EU",    @"\((?:Europe|EUR|PAL(?:\d+)?)\)"),
        R("US",    @"\((?:USA|US|U\.S\.A\.|U\.S\.)\)"),
        R("JP",    @"\((?:Japan|JP|JPN|NTSC-J)\)"),
        R("WORLD", @"\((?:World|Export)\)"),
        R("AU",    @"\((?:Australia|AU|AUS)\)"),
        R("ASIA",  @"\((?:Asia|AS)\)"),
        // Scandinavia
        R("EU",    @"\(Scandinavia\)"),
        // Individual countries → mapped to region codes from rules.json
        R("KR",    @"\((?:Korea|KOR)\)"),
        R("CN",    @"\((?:China|CHN)\)"),
        R("BR",    @"\((?:Brazil|BRA)\)"),
        R("EU",    @"\((?:France|FRA)\)"),
        R("EU",    @"\((?:Germany|DEU)\)"),
        R("EU",    @"\((?:Spain|ESP)\)"),
        R("EU",    @"\((?:Italy|ITA)\)"),
        R("EU",    @"\((?:Netherlands|NLD)\)"),
        R("EU",    @"\((?:Sweden|SWE)\)"),
        R("RU",    @"\((?:Russia|RUS)\)"),
        R("PL",    @"\((?:Poland|POL)\)"),
        R("CA",    @"\((?:Canada|CAN)\)"),
        R("LATAM", @"\((?:Latin\s*America)\)"),
        R("TR",    @"\((?:Turkey)\)"),
        R("AE",    @"\((?:United\s*Arab\s*Emirates)\)"),
        R("AU",    @"\((?:New\s*Zealand|NZL)\)"),
        // EU country names → EU
        R("EU",    @"\((?:United\s*Kingdom|Great\s*Britain|England|Belgium|Austria|Portugal|Switzerland|Denmark|Finland|Norway|Czech|Hungary|Croatia|Greece|Ireland|Luxembourg|Romania|Bulgaria|Slovakia|Slovenia|Estonia|Latvia|Lithuania|South\s*Africa)\)"),
        // ASIA countries
        R("ASIA",  @"\((?:Taiwan|Hong\s*Kong|India|Singapore|Thailand|Vietnam|Indonesia|Malaysia|Philippines)\)"),
    };

    private static readonly IReadOnlyList<RegionRule> FallbackTwoLetterRules = new[]
    {
        R("EU",   @"\b(?:eu|eur|pal)\b"),
        R("US",   @"\b(?:us|usa|ntsc)\b"),
        R("JP",   @"\b(?:jp|jpn|japan)\b"),
        R("KR",   @"\((?:kr)\)"),
        R("CN",   @"\((?:cn)\)"),
        R("BR",   @"\((?:br)\)"),
        R("FR",   @"\((?:fr)\)"),
        R("DE",   @"\((?:de)\)"),
        R("ES",   @"\((?:es)\)"),
        R("IT",   @"\((?:it)\)"),
        R("NL",   @"\((?:nl)\)"),
        R("SE",   @"\((?:se)\)"),
        R("AU",   @"\((?:au)\)"),
        R("ASIA", @"\((?:as)\)"),
        R("RU",   @"\((?:ru)\)"),
        R("PL",   @"\((?:pl)\)"),
        R("CA",   @"\((?:ca)\)"),
    };

    // All language codes from rules.json GameKeyPatterns (expanded set)
    private static readonly System.Text.RegularExpressions.Regex FallbackMultiRegionPattern =
        new(@"\((?:en|fr|de|es|it|pt|nl|sv|no|da|fi|ru|pl|zh|ko|ja|cs|hu|el|tr|ar|he|th|vi|id|ms|ro|bg|uk|hr|sk|sl|et|lv|lt|af|ca|gd|eu)(?:,\s*(?:en|fr|de|es|it|pt|nl|sv|no|da|fi|ru|pl|zh|ko|ja|cs|hu|el|tr|ar|he|th|vi|id|ms|ro|bg|uk|hr|sk|sl|et|lv|lt|af|ca|gd|eu))+\)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled,
            RegexTimeout);

    public static void RegisterRuleFactory(Func<(
        IReadOnlyList<RegionRule> OrderedRules,
        IReadOnlyList<RegionRule> TwoLetterRules,
        System.Text.RegularExpressions.Regex MultiRegionPattern,
        IReadOnlyDictionary<string, string> TokenToRegionMap,
        IReadOnlyCollection<string> LanguageCodes,
        IReadOnlyCollection<string> EuLanguageCodes)> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        lock (RegistrationSync)
        {
            _registrationFactory = factory;
            _registeredConfig = null;
        }
    }

    public static void RegisterDefaultRules(
        IReadOnlyList<RegionRule> orderedRules,
        IReadOnlyList<RegionRule> twoLetterRules,
        System.Text.RegularExpressions.Regex multiRegionPattern,
        IReadOnlyDictionary<string, string> tokenToRegionMap,
        IReadOnlyCollection<string> languageCodes,
        IReadOnlyCollection<string> euLanguageCodes)
    {
        ArgumentNullException.ThrowIfNull(orderedRules);
        ArgumentNullException.ThrowIfNull(twoLetterRules);
        ArgumentNullException.ThrowIfNull(multiRegionPattern);
        ArgumentNullException.ThrowIfNull(tokenToRegionMap);
        ArgumentNullException.ThrowIfNull(languageCodes);
        ArgumentNullException.ThrowIfNull(euLanguageCodes);

        lock (RegistrationSync)
        {
            _registeredConfig = BuildConfig(
                orderedRules,
                twoLetterRules,
                multiRegionPattern,
                tokenToRegionMap,
                languageCodes,
                euLanguageCodes);
        }
    }

    private static DetectionConfig BuildConfig(
        IReadOnlyList<RegionRule>? orderedRules,
        IReadOnlyList<RegionRule>? twoLetterRules,
        System.Text.RegularExpressions.Regex? multiRegionPattern,
        IReadOnlyDictionary<string, string>? tokenToRegionMap,
        IReadOnlyCollection<string>? languageCodes,
        IReadOnlyCollection<string>? euLanguageCodes)
    {
        var mergedTokenMap = new Dictionary<string, string>(FallbackTokenToRegion, StringComparer.OrdinalIgnoreCase);
        if (tokenToRegionMap is not null)
        {
            foreach (var kv in tokenToRegionMap)
                mergedTokenMap[kv.Key] = kv.Value;
        }

        var mergedLanguageCodes = new HashSet<string>(FallbackLanguageCodes, StringComparer.OrdinalIgnoreCase);
        if (languageCodes is not null)
        {
            foreach (var languageCode in languageCodes)
                mergedLanguageCodes.Add(languageCode);
        }

        var mergedEuLanguageCodes = new HashSet<string>(FallbackEuLanguageCodes, StringComparer.OrdinalIgnoreCase);
        if (euLanguageCodes is not null)
        {
            foreach (var euLanguageCode in euLanguageCodes)
                mergedEuLanguageCodes.Add(euLanguageCode);
        }

        return new DetectionConfig(
            MergeRules(orderedRules, FallbackOrderedRules),
            MergeRules(twoLetterRules, FallbackTwoLetterRules),
            multiRegionPattern ?? FallbackMultiRegionPattern,
            mergedTokenMap,
            mergedLanguageCodes,
            mergedEuLanguageCodes);
    }

    private static IReadOnlyList<RegionRule> MergeRules(
        IReadOnlyList<RegionRule>? primary,
        IReadOnlyList<RegionRule> fallback)
    {
        var result = new List<RegionRule>();
        var seenSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static string Signature(RegionRule rule) => $"{rule.Key}\0{rule.Pattern}";

        if (primary is not null)
        {
            foreach (var rule in primary)
            {
                if (seenSignatures.Add(Signature(rule)))
                    result.Add(rule);
            }
        }

        foreach (var rule in fallback)
        {
            if (seenSignatures.Add(Signature(rule)))
                result.Add(rule);
        }

        return result;
    }

    private static DetectionConfig EnsureRulesLoaded()
    {
        var cached = _registeredConfig;
        if (cached is not null)
            return cached;

        lock (RegistrationSync)
        {
            cached = _registeredConfig;
            if (cached is not null)
                return cached;

            if (_registrationFactory is not null)
            {
                var loaded = _registrationFactory();
                _registeredConfig = BuildConfig(
                    loaded.OrderedRules,
                    loaded.TwoLetterRules,
                    loaded.MultiRegionPattern,
                    loaded.TokenToRegionMap,
                    loaded.LanguageCodes,
                    loaded.EuLanguageCodes);
            }

            _registeredConfig ??= BuildConfig(
                orderedRules: null,
                twoLetterRules: null,
                FallbackMultiRegionPattern,
                tokenToRegionMap: null,
                languageCodes: null,
                euLanguageCodes: null);

            return _registeredConfig;
        }
    }

    private static string NormalizeRegionKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return Regions.Unknown;

        return key.Trim().ToUpperInvariant() switch
        {
            "USA" => Regions.US,
            "JPN" => Regions.JP,
            "EUROPE" => Regions.EU,
            "FR" or "DE" or "ES" or "IT" or "NL" or "SE" or "SCAN"
                or "AT" or "BE" or "PT" or "CH" or "DK" or "FI" or "NO"
                or "GR" or "IE" or "LU" or "RO" or "BG" or "SK" or "SI"
                or "EE" or "LV" or "LT" or "ZA" => Regions.EU,
            var value => value
        };
    }

    // Helper to create compiled, case-insensitive RegionRule
    private static RegionRule R(string key, string pattern)
        => new(key, new System.Text.RegularExpressions.Regex(pattern,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled,
            RegexTimeout));

    // Pre-compiled pattern for extracting parenthesized groups (BUG-M02)
    private static readonly System.Text.RegularExpressions.Regex ParenGroupPattern =
        new(@"\(([^)]+)\)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled,
            RegexTimeout);

    /// <summary>
    /// Convenience overload using default detection rules from rules.json.
    /// </summary>
    public static string GetRegionTag(string name)
    {
        var config = EnsureRulesLoaded();
        return Detect(name, config.OrderedRules, config.TwoLetterRules, config.MultiRegionPattern);
    }

    /// <summary>
    /// Convenience overload returning region and diagnostic reason using default rules.
    /// </summary>
    public static RegionDetectionResult GetRegionTagWithDiagnostics(string name)
    {
        var config = EnsureRulesLoaded();
        return DetectWithDiagnostics(name, config.OrderedRules, config.TwoLetterRules, config.MultiRegionPattern);
    }

    /// <summary>
    /// Detects the primary region from a ROM filename.
    /// Port of Get-RegionTag from Core.ps1.
    /// </summary>
    /// <param name="name">ROM filename (with or without extension).</param>
    /// <param name="orderedRules">Primary region rules in priority order.</param>
    /// <param name="twoLetterRules">Secondary two-letter region rules.</param>
    /// <param name="multiRegionPattern">Pattern that matches multi-region indicators.</param>
    /// <returns>Region string: EU, US, JP, WORLD, or UNKNOWN.</returns>
    public static string Detect(
        string name,
        IReadOnlyList<RegionRule> orderedRules,
        IReadOnlyList<RegionRule> twoLetterRules,
        System.Text.RegularExpressions.Regex multiRegionPattern)
        => DetectWithDiagnostics(name, orderedRules, twoLetterRules, multiRegionPattern).Region;

    /// <summary>
    /// Detects the primary region and returns diagnostics for the matched rule path.
    /// </summary>
    public static RegionDetectionResult DetectWithDiagnostics(
        string name,
        IReadOnlyList<RegionRule> orderedRules,
        IReadOnlyList<RegionRule> twoLetterRules,
        System.Text.RegularExpressions.Regex multiRegionPattern)
    {
        if (string.IsNullOrWhiteSpace(name))
            return new RegionDetectionResult(Regions.Unknown, "empty-input");

        // Multi-language tags: all-EU languages map to EU; mixed language families map to WORLD.
        var config = EnsureRulesLoaded();

        if (TryResolveLanguageMultiTag(name, config.LanguageCodes, config.EuLanguageCodes, out var languageRegion))
        {
            var reason = string.Equals(languageRegion, Regions.EU, StringComparison.Ordinal)
                ? "language-multi-eu"
                : "language-multi-world";
            return new RegionDetectionResult(languageRegion, reason);
        }

        if (TryResolveCommaSeparatedRegionGroup(name, config.TokenToRegionMap, out var commaSeparatedRegion))
        {
            var reason = string.Equals(commaSeparatedRegion, Regions.World, StringComparison.Ordinal)
                ? "comma-region-world"
                : "comma-region-single";
            return new RegionDetectionResult(commaSeparatedRegion, reason);
        }

        // Multi-region → WORLD
        if (multiRegionPattern.IsMatch(name))
            return new RegionDetectionResult(Regions.World, "multi-region-pattern");

        // Try ordered rules (bracket-based, more specific)
        foreach (var rule in orderedRules)
        {
            if (rule.Pattern.IsMatch(name))
            {
                var normalized = NormalizeRegionKey(rule.Key);
                return new RegionDetectionResult(normalized, $"ordered-rule:{normalized}");
            }
        }

        // Try token-based parsing (less specific, e.g. NTSC → US)
        var tokenResult = ResolveRegionFromTokens(name, config.TokenToRegionMap);
        if (tokenResult is not null && tokenResult != Regions.Unknown)
            return new RegionDetectionResult(tokenResult, "token-fallback");

        // Try two-letter rules
        foreach (var rule in twoLetterRules)
        {
            if (rule.Pattern.IsMatch(name))
            {
                var normalized = NormalizeRegionKey(rule.Key);
                return new RegionDetectionResult(normalized, $"two-letter-rule:{normalized}");
            }
        }

        return new RegionDetectionResult(Regions.Unknown, "no-match");
    }

    /// <summary>
    /// Attempts region resolution from comma-separated tokens in parentheses.
    /// Port of Resolve-RegionTagFromTokens from Core.ps1.
    /// </summary>
    internal static string? ResolveRegionFromTokens(string name)
    {
        var config = EnsureRulesLoaded();
        return ResolveRegionFromTokens(name, config.TokenToRegionMap);
    }

    internal static bool TryResolveCommaSeparatedRegionGroup(
        string name,
        IReadOnlyDictionary<string, string> tokenToRegionMap,
        out string region)
    {
        region = Regions.Unknown;

        var matches = ParenGroupPattern.Matches(name);
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var content = match.Groups[1].Value;
            var tokens = content.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            if (tokens.Length < 2)
                continue;

            var foundRegions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var token in tokens)
            {
                var normalizedToken = token.Trim().ToLowerInvariant();
                if (IsRegionToken(normalizedToken, tokenToRegionMap, out var mappedRegion))
                    foundRegions.Add(mappedRegion);
            }

            if (foundRegions.Count == 0)
                continue;

            region = foundRegions.Count > 1 ? Regions.World : foundRegions.Single();
            return true;
        }

        return false;
    }

    internal static string? ResolveRegionFromTokens(string name, IReadOnlyDictionary<string, string> tokenToRegionMap)
    {
        // Extract all parenthesized groups
        var matches = ParenGroupPattern.Matches(name);

        if (matches.Count == 0)
            return null;

        var foundRegions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var content = match.Groups[1].Value;
            var tokens = content.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            foreach (var token in tokens)
            {
                var normalizedToken = token.Trim().ToLowerInvariant();
                if (IsRegionToken(normalizedToken, tokenToRegionMap, out var mappedRegion))
                    foundRegions.Add(mappedRegion);
            }
        }

        if (foundRegions.Count == 0)
            return null;
        if (foundRegions.Count > 1)
            return Regions.World;

        return foundRegions.Single();
    }

    private static readonly IReadOnlyDictionary<string, string> FallbackTokenToRegion =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        // EU major
        ["europe"] = Regions.EU, ["eu"] = Regions.EU, ["eur"] = Regions.EU, ["pal"] = Regions.EU,
        // EU countries
        ["uk"] = Regions.EU, ["united kingdom"] = Regions.EU, ["great britain"] = Regions.EU, ["england"] = Regions.EU,
        ["germany"] = Regions.EU, ["france"] = Regions.EU,
        ["spain"] = Regions.EU, ["italy"] = Regions.EU,
        ["netherlands"] = Regions.EU, ["sweden"] = Regions.EU,
        ["belgium"] = Regions.EU, ["austria"] = Regions.EU,
        ["portugal"] = Regions.EU, ["switzerland"] = Regions.EU,
        ["denmark"] = Regions.EU, ["finland"] = Regions.EU,
        ["norway"] = Regions.EU, ["czech"] = Regions.EU,
        ["hungary"] = Regions.EU, ["croatia"] = Regions.EU,
        ["greece"] = Regions.EU, ["ireland"] = Regions.EU,
        ["luxembourg"] = Regions.EU, ["romania"] = Regions.EU,
        ["bulgaria"] = Regions.EU, ["slovakia"] = Regions.EU,
        ["slovenia"] = Regions.EU, ["estonia"] = Regions.EU,
        ["latvia"] = Regions.EU, ["lithuania"] = Regions.EU,
        ["scandinavia"] = Regions.EU,
        // US
        ["usa"] = Regions.US, ["us"] = Regions.US, ["america"] = Regions.US,
        ["ntsc-u"] = Regions.US, ["ntsc - u"] = Regions.US, ["ntsc"] = Regions.US,
        // Japan
        ["japan"] = Regions.JP, ["jp"] = Regions.JP, ["jpn"] = Regions.JP,
        ["ntsc-j"] = Regions.JP, ["ntsc - j"] = Regions.JP,
        // World
        ["world"] = Regions.World, ["export"] = Regions.World,
        // Asia
        ["asia"] = "ASIA", ["china"] = "CN", ["taiwan"] = "ASIA",
        ["hong kong"] = "ASIA", ["india"] = "ASIA",
        ["singapore"] = "ASIA", ["thailand"] = "ASIA",
        ["vietnam"] = "ASIA", ["indonesia"] = "ASIA",
        ["malaysia"] = "ASIA", ["philippines"] = "ASIA",
        // Korea
        ["korea"] = "KR", ["kor"] = "KR", ["kr"] = "KR",
        // Other individual regions
        ["brazil"] = "BR", ["bra"] = "BR", ["br"] = "BR",
        ["australia"] = "AU", ["au"] = "AU", ["aus"] = "AU",
        ["canada"] = "CA", ["ca"] = "CA", ["can"] = "CA",
        ["russia"] = "RU", ["rus"] = "RU", ["ru"] = "RU",
        ["poland"] = "PL", ["pol"] = "PL", ["pl"] = "PL",
        ["latin america"] = "LATAM",
        ["turkey"] = "TR", ["tr"] = "TR",
        ["united arab emirates"] = "AE",
        // Two-letter codes that could appear as comma-separated tokens
        ["de"] = Regions.EU, ["fr"] = Regions.EU, ["es"] = Regions.EU,
        ["it"] = Regions.EU, ["nl"] = Regions.EU, ["se"] = Regions.EU,
        ["at"] = Regions.EU, ["be"] = Regions.EU, ["pt"] = Regions.EU,
        ["ch"] = Regions.EU, ["dk"] = Regions.EU, ["fi"] = Regions.EU,
        ["no"] = Regions.EU, ["cz"] = Regions.EU, ["hu"] = Regions.EU,
        ["tw"] = "ASIA", ["hk"] = "ASIA", ["in"] = "ASIA",
        ["cn"] = "CN",
        // New Zealand / South Africa
        ["new zealand"] = "AU", ["nz"] = "AU",
        ["south africa"] = Regions.EU, ["za"] = Regions.EU,
    };

    private static bool IsRegionToken(string token, IReadOnlyDictionary<string, string> tokenToRegionMap, out string region)
    {
        region = Regions.Unknown;

        if (tokenToRegionMap.TryGetValue(token, out var mapped))
        {
            region = NormalizeRegionKey(mapped);
            return true;
        }

        return false;
    }

    private static readonly IReadOnlySet<string> FallbackLanguageCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "en", "fr", "de", "es", "it", "pt", "nl", "sv", "no", "da", "fi", "ru", "pl", "zh", "ko", "ja",
        "cs", "hu", "el", "tr", "ar", "he", "th", "vi", "id", "ms", "ro", "bg", "uk", "hr", "sk", "sl",
        "et", "lv", "lt", "af", "ca", "gd", "eu"
    };

    private static readonly IReadOnlySet<string> FallbackEuLanguageCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "fr", "de", "es", "it", "pt", "nl", "sv", "no", "da", "fi", "pl", "cs", "hu", "el", "ro", "bg",
        "uk", "hr", "sk", "sl", "et", "lv", "lt", "ca", "gd", "eu"
    };

    private static bool TryResolveLanguageMultiTag(
        string name,
        IReadOnlySet<string> languageCodes,
        IReadOnlySet<string> euLanguageCodes,
        out string region)
    {
        region = Regions.Unknown;

        var matches = ParenGroupPattern.Matches(name);
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var content = match.Groups[1].Value;
            var tokens = content.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2)
                continue;

            var hasOnlyLanguageCodes = true;
            var hasOnlyEuLanguageCodes = true;
            foreach (var token in tokens)
            {
                var t = token.Trim();
                if (!languageCodes.Contains(t))
                {
                    hasOnlyLanguageCodes = false;
                    break;
                }

                if (!euLanguageCodes.Contains(t))
                    hasOnlyEuLanguageCodes = false;
            }

            if (!hasOnlyLanguageCodes)
                continue;

            region = hasOnlyEuLanguageCodes ? Regions.EU : Regions.World;
            return true;
        }

        return false;
    }
}
