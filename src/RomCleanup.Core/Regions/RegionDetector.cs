namespace RomCleanup.Core.Regions;

/// <summary>
/// Region detection from ROM filenames.
/// Port of Get-RegionTag from Core.ps1.
/// Uses ordered pattern matching against parenthesized region indicators.
/// </summary>
public static class RegionDetector
{
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

    // Default rules matching rules.json RegionOrdered — full set
    private static readonly IReadOnlyList<RegionRule> DefaultOrderedRules = new[]
    {
        // Major regions (priority order)
        R("EU",    @"\((?:Europe|EUR|PAL)\)"),
        R("US",    @"\((?:USA|US|U\.S\.A\.|U\.S\.)\b(?:,\s*\w+)*\)"),
        R("JP",    @"\((?:Japan|JP|JPN|NTSC-J)\)"),
        R("WORLD", @"\((?:World|Export)\)"),
        R("AU",    @"\((?:Australia|AU|AUS)\)"),
        R("ASIA",  @"\((?:Asia|AS)\)"),
        // Scandinavia
        R("SCAN",  @"\(Scandinavia\)"),
        // Individual countries → mapped to region codes from rules.json
        R("KR",    @"\((?:Korea|KOR)\)"),
        R("CN",    @"\((?:China|CHN)\)"),
        R("BR",    @"\((?:Brazil|BRA)\)"),
        R("FR",    @"\((?:France|FRA)\)"),
        R("DE",    @"\((?:Germany|DEU)\)"),
        R("ES",    @"\((?:Spain|ESP)\)"),
        R("IT",    @"\((?:Italy|ITA)\)"),
        R("NL",    @"\((?:Netherlands|NLD)\)"),
        R("SE",    @"\((?:Sweden|SWE)\)"),
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

    private static readonly IReadOnlyList<RegionRule> DefaultTwoLetterRules = new[]
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
    private static readonly System.Text.RegularExpressions.Regex DefaultMultiRegionPattern =
        new(@"\((?:en|fr|de|es|it|pt|nl|sv|no|da|fi|ru|pl|zh|ko|ja|cs|hu|el|tr|ar|he|th|vi|id|ms|ro|bg|uk|hr|sk|sl|et|lv|lt|af|ca|gd|eu)(?:,\s*(?:en|fr|de|es|it|pt|nl|sv|no|da|fi|ru|pl|zh|ko|ja|cs|hu|el|tr|ar|he|th|vi|id|ms|ro|bg|uk|hr|sk|sl|et|lv|lt|af|ca|gd|eu))+\)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    // Helper to create compiled, case-insensitive RegionRule
    private static RegionRule R(string key, string pattern)
        => new(key, new System.Text.RegularExpressions.Regex(pattern,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled));

    /// <summary>
    /// Convenience overload using default detection rules from rules.json.
    /// </summary>
    public static string GetRegionTag(string name)
        => Detect(name, DefaultOrderedRules, DefaultTwoLetterRules, DefaultMultiRegionPattern);

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
    {
        if (string.IsNullOrWhiteSpace(name))
            return Regions.Unknown;

        // UK / Great Britain → EU
        if (System.Text.RegularExpressions.Regex.IsMatch(name,
            @"\((?:[^)]*\b(?:uk|united\s*kingdom|great\s*britain|england)\b[^)]*)\)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            return Regions.EU;
        }

        // Multi-region → WORLD
        if (multiRegionPattern.IsMatch(name))
            return Regions.World;

        // Try token-based parsing
        var tokenResult = ResolveRegionFromTokens(name);
        if (tokenResult is not null && tokenResult != Regions.Unknown)
            return tokenResult;

        // Try ordered rules
        foreach (var rule in orderedRules)
        {
            if (rule.Pattern.IsMatch(name))
                return rule.Key;
        }

        // Try two-letter rules
        foreach (var rule in twoLetterRules)
        {
            if (rule.Pattern.IsMatch(name))
                return rule.Key;
        }

        return Regions.Unknown;
    }

    /// <summary>
    /// Attempts region resolution from comma-separated tokens in parentheses.
    /// Port of Resolve-RegionTagFromTokens from Core.ps1.
    /// </summary>
    internal static string? ResolveRegionFromTokens(string name)
    {
        // Extract all parenthesized groups
        var matches = System.Text.RegularExpressions.Regex.Matches(
            name, @"\(([^)]+)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (matches.Count == 0)
            return null;

        var foundRegions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var content = match.Groups[1].Value;
            var tokens = content.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            foreach (var token in tokens)
            {
                var t = token.Trim().ToLowerInvariant();
                if (IsRegionToken(t, out var region))
                    foundRegions.Add(region);
            }
        }

        if (foundRegions.Count == 0) return null;
        if (foundRegions.Count > 1) return Regions.World;

        return foundRegions.First();
    }

    private static readonly Dictionary<string, string> RegionTokenMap = new(StringComparer.OrdinalIgnoreCase)
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
        ["ntsc-u"] = Regions.US, ["ntsc"] = Regions.US,
        // Japan
        ["japan"] = Regions.JP, ["jp"] = Regions.JP, ["jpn"] = Regions.JP,
        ["ntsc-j"] = Regions.JP,
        // World
        ["world"] = Regions.World, ["export"] = Regions.World,
        // Asia
        ["asia"] = "ASIA", ["china"] = "ASIA", ["taiwan"] = "ASIA",
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
        ["cn"] = "ASIA",
        // New Zealand / South Africa
        ["new zealand"] = "AU", ["nz"] = "AU",
        ["south africa"] = Regions.EU, ["za"] = Regions.EU,
    };

    private static bool IsRegionToken(string token, out string region)
    {
        region = Regions.Unknown;

        if (RegionTokenMap.TryGetValue(token, out var mapped))
        {
            region = mapped;
            return true;
        }

        return false;
    }
}
