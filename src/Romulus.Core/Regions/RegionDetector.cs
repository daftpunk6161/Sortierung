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
    private static readonly System.Text.RegularExpressions.Regex EmptyMultiRegionPattern =
        new(@"$a",
            System.Text.RegularExpressions.RegexOptions.Compiled,
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
        var effectiveOrderedRules = orderedRules is null
            ? Array.Empty<RegionRule>()
            : orderedRules.ToArray();

        var effectiveTwoLetterRules = twoLetterRules is null
            ? Array.Empty<RegionRule>()
            : twoLetterRules.ToArray();

        var effectiveTokenMap = tokenToRegionMap is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(tokenToRegionMap, StringComparer.OrdinalIgnoreCase);

        var effectiveLanguageCodes = languageCodes is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(languageCodes, StringComparer.OrdinalIgnoreCase);

        var effectiveEuLanguageCodes = euLanguageCodes is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(euLanguageCodes, StringComparer.OrdinalIgnoreCase);

        return new DetectionConfig(
            effectiveOrderedRules,
            effectiveTwoLetterRules,
            multiRegionPattern ?? EmptyMultiRegionPattern,
            effectiveTokenMap,
            effectiveLanguageCodes,
            effectiveEuLanguageCodes);
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
                EmptyMultiRegionPattern,
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
