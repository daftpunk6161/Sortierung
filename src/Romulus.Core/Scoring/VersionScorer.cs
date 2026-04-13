using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Romulus.Core.Scoring;

/// <summary>
/// Version, revision, and language scoring for ROM deduplication.
/// Port of Get-VersionScore from Core.ps1 lines 495-565.
/// Regex patterns sourced from data/rules.json.
/// DESIGN-04: Intentionally a sealed class (not static) because it holds compiled Regex instances.
/// FormatScorer is static because it uses only pure dictionary lookups with no state.
/// </summary>
public sealed class VersionScorer
{
    private const string FallbackLangPattern = @"\((en|fr|de|es|it|pt|nl|sv|no|da|fi|ru|pl|zh|ko|ja|cs|hu|el|tr|ar|he|th|vi|id|ms|ro|bg|uk|hr|sk|sl|et|lv|lt|af|ca|gd|eu)(?:,\s*(?:en|fr|de|es|it|pt|nl|sv|no|da|fi|ru|pl|zh|ko|ja|cs|hu|el|tr|ar|he|th|vi|id|ms|ro|bg|uk|hr|sk|sl|et|lv|lt|af|ca|gd|eu))*\)";
    private const int FallbackMaxVersionSegments = 6;

    private static readonly object Sync = new();
    private static volatile string? _registeredLangPattern;
    private static int? _registeredMaxVersionSegments;
    private static Func<string>? _langPatternFactory;
    private static Func<int?>? _maxVersionSegmentsFactory;

    private readonly Regex _rxVerified;
    private readonly Regex _rxRevision;
    private readonly Regex _rxVersion;
    private readonly Regex _rxLang;

    private static readonly TimeSpan RxTimeout = SafeRegex.DefaultTimeout;

    // Pre-compiled patterns for revision parsing (TASK-154)
    private static readonly Regex RxPureLetters = new(@"^[a-z]+$", RegexOptions.Compiled, RxTimeout);
    private static readonly Regex RxDottedNumeric = new(@"^\d+(?:\.\d+)+$", RegexOptions.Compiled, RxTimeout);
    private static readonly Regex RxNumericSuffix = new(@"^(\d+)([a-z]+)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled, RxTimeout);
    private static readonly Regex RxLeadingDigits = new(@"^\d+", RegexOptions.Compiled, RxTimeout);
    private static readonly Regex RxDigits = new(@"\d+", RegexOptions.Compiled, RxTimeout);

    /// <summary>
    /// Default patterns matching data/rules.json.
    /// </summary>
    public VersionScorer()
        : this(
            verifiedPattern: @"\[!\]",
            revisionPattern: @"\(rev\s*([a-z0-9.]+)\)",
            versionPattern: @"\(v\s*([\d.]+)\)",
            langPattern: ResolveLanguagePattern())
    {
    }

    /// <summary>
    /// Resets all registered state. For test isolation only – never call in production.
    /// </summary>
    internal static void ResetForTesting()
    {
        lock (Sync)
        {
            _registeredLangPattern = null;
            _registeredMaxVersionSegments = null;
            _langPatternFactory = null;
            _maxVersionSegmentsFactory = null;
        }
    }

    public static void RegisterDefaultLanguagePattern(string langPattern)
    {
        if (string.IsNullOrWhiteSpace(langPattern))
            return;

        lock (Sync)
        {
            _registeredLangPattern = langPattern;
        }
    }

    public static void RegisterLanguagePatternFactory(Func<string> languagePatternFactory)
    {
        ArgumentNullException.ThrowIfNull(languagePatternFactory);

        lock (Sync)
        {
            _langPatternFactory = languagePatternFactory;
            _registeredLangPattern = null;
        }
    }

    public static void RegisterMaxVersionSegments(int maxVersionSegments)
    {
        if (maxVersionSegments < 1)
            return;

        lock (Sync)
        {
            _registeredMaxVersionSegments = maxVersionSegments;
        }
    }

    public static void RegisterMaxVersionSegmentsFactory(Func<int?> maxVersionSegmentsFactory)
    {
        ArgumentNullException.ThrowIfNull(maxVersionSegmentsFactory);

        lock (Sync)
        {
            _maxVersionSegmentsFactory = maxVersionSegmentsFactory;
            _registeredMaxVersionSegments = null;
        }
    }

    private static string ResolveLanguagePattern()
    {
        var cached = _registeredLangPattern;
        if (!string.IsNullOrWhiteSpace(cached))
            return cached;

        lock (Sync)
        {
            cached = _registeredLangPattern;
            if (!string.IsNullOrWhiteSpace(cached))
                return cached;

            if (_langPatternFactory is not null)
            {
                var loaded = _langPatternFactory();
                if (!string.IsNullOrWhiteSpace(loaded))
                {
                    _registeredLangPattern = loaded;
                    return loaded;
                }
            }

            _registeredLangPattern = FallbackLangPattern;
            return FallbackLangPattern;
        }
    }

    private static int ResolveMaxVersionSegments()
    {
        var cached = _registeredMaxVersionSegments;
        if (cached.HasValue)
            return Math.Max(1, cached.Value);

        lock (Sync)
        {
            cached = _registeredMaxVersionSegments;
            if (cached.HasValue)
                return Math.Max(1, cached.Value);

            if (_maxVersionSegmentsFactory is not null)
            {
                var loaded = _maxVersionSegmentsFactory();
                if (loaded is > 0)
                {
                    _registeredMaxVersionSegments = loaded.Value;
                    return loaded.Value;
                }
            }

            _registeredMaxVersionSegments = FallbackMaxVersionSegments;
            return FallbackMaxVersionSegments;
        }
    }

    public VersionScorer(string verifiedPattern, string revisionPattern,
        string versionPattern, string langPattern)
    {
        _rxVerified = new Regex(verifiedPattern, RegexOptions.Compiled, RxTimeout);
        _rxRevision = new Regex(revisionPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled, RxTimeout);
        _rxVersion = new Regex(versionPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled, RxTimeout);
        _rxLang = new Regex(langPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled, RxTimeout);
    }

    /// <summary>
    /// Calculates the version score for a ROM filename.
    /// Port of Get-VersionScore from Core.ps1.
    /// </summary>
    public long GetVersionScore(string baseName)
    {
        if (string.IsNullOrWhiteSpace(baseName)) return 0;

        long score = 0;

        // Verified dump [!] = +500
        if (SafeRegex.IsMatch(_rxVerified, baseName)) score += 500;

        // Revision scoring
        var revMatch = SafeRegex.Match(_rxRevision, baseName);
        if (revMatch.Success)
        {
            var rev = revMatch.Groups[1].Value.ToLowerInvariant();

            if (SafeRegex.IsMatch(RxPureLetters, rev))
            {
                // Pure letter revision: a=1, b=2, ..., z=26, aa=27 etc.
                // Clamp to 8 chars to prevent long overflow (26^13 > long.MaxValue).
                var effectiveRev = rev.Length > 8 ? rev[..8] : rev;
                long letterScore = 0;
                foreach (var ch in effectiveRev)
                {
                    letterScore = (letterScore * 26) + (ch - 'a' + 1);
                }
                score += letterScore * 10;
            }
            else if (SafeRegex.IsMatch(RxDottedNumeric, rev))
            {
                var segments = rev.Split('.', StringSplitOptions.RemoveEmptyEntries);
                long weight = 1;
                for (var i = 1; i < segments.Length; i++)
                    weight *= 1000;

                long dottedScore = 0;
                foreach (var segment in segments)
                {
                    if (!long.TryParse(segment, out var segVal))
                        break;
                    dottedScore += segVal * weight;
                    if (weight > 1) weight /= 1000;
                }

                score += dottedScore;
            }
            else if (SafeRegex.IsMatch(RxNumericSuffix, rev))
            {
                var numericMatch = SafeRegex.Match(RxNumericSuffix, rev);
                if (!numericMatch.Success)
                    return score;

                if (!long.TryParse(numericMatch.Groups[1].Value, out var numeric))
                    return score;
                var suffix = numericMatch.Groups[2].Value;
                long suffixScore = 0;
                if (!string.IsNullOrWhiteSpace(suffix))
                {
                    // Clamp to 8 chars to prevent long overflow.
                    var effectiveSuffix = suffix.Length > 8 ? suffix[..8] : suffix;
                    foreach (var ch in effectiveSuffix.ToLowerInvariant())
                    {
                        suffixScore = (suffixScore * 26) + (ch - 'a' + 1);
                    }
                }
                // BUG-FIX: Removed dead remainder code — RxNumericSuffix is anchored (^...$)
                // so numericMatch.Length == rev.Length, meaning remainder was always empty.
                score += (numeric * 10L) + suffixScore;
            }
            else if (SafeRegex.IsMatch(RxLeadingDigits, rev))
            {
                var digitMatch = SafeRegex.Match(RxLeadingDigits, rev);
                if (!digitMatch.Success)
                    return score;

                if (long.TryParse(digitMatch.Value, out var leadingDigit))
                    score += leadingDigit * 10L;
            }
        }

        // Version scoring (e.g. "(v1.2)")
        var verMatch = SafeRegex.Match(_rxVersion, baseName);
        if (verMatch.Success)
        {
            var segments = new List<int>();
            try
            {
                foreach (Match seg in RxDigits.Matches(verMatch.Value))
                {
                    if (int.TryParse(seg.Value, out var segVal))
                        segments.Add(segVal);
                }
            }
            catch (RegexMatchTimeoutException)
            {
                Trace.TraceWarning(
                    "version-score-timeout: regex segment parsing timed out for '{0}'.",
                    baseName);
            }

            if (segments.Count > 0)
            {
                // Clamp to configured max segment count to prevent long overflow in weighted scoring.
                // CORE-02 FIX: If truncated, add +1 per extra segment so versions with
                // more segments score slightly higher than those with fewer.
                var maxSegments = ResolveMaxVersionSegments();
                bool wasTruncated = segments.Count > maxSegments;
                var effectiveSegments = wasTruncated
                    ? segments.GetRange(0, maxSegments)
                    : segments;

                long weight = 1;
                for (var i = 1; i < effectiveSegments.Count; i++)
                    weight *= 1000;

                long versionScore = 0;
                foreach (var seg in effectiveSegments)
                {
                    versionScore += seg * weight;
                    if (weight > 1) weight /= 1000;
                }
                score += versionScore;

                // CORE-02: Differentiate versions with trailing segments beyond the clamp
                if (wasTruncated)
                {
                    score += segments.Count - maxSegments;
                    Trace.WriteLine($"[VersionScorer] Version segment list truncated to {maxSegments} segment(s) for '{baseName}'.");
                }
            }
        }

        // Language bonus: en = +50 + multi-lang bonus, de = +25
        // Security/Correctness: evaluate exact language tokens, never substring matches.
        var langMatch = SafeRegex.Match(_rxLang, baseName);
        if (langMatch.Success)
        {
            var langTokens = langMatch.Value
                .Trim('(', ')')
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var hasEn = false;
            var hasDe = false;
            foreach (var token in langTokens)
            {
                if (token.Equals("en", StringComparison.OrdinalIgnoreCase))
                    hasEn = true;
                else if (token.Equals("de", StringComparison.OrdinalIgnoreCase))
                    hasDe = true;
            }

            if (hasEn)
            {
                score += 50;
                score += langTokens.Length * 5;
            }

            if (hasDe)
                score += 25;
        }

        return score;
    }
}
