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
            langPattern: @"\((en|fr|de|es|it|pt|nl|sv|no|da|fi|ru|pl|zh|ko|ja|cs|hu|el|tr|ar|he|th|vi|id|ms|ro|bg|uk|hr|sk|sl|et|lv|lt|af|ca|gd|eu)(?:,\s*(?:en|fr|de|es|it|pt|nl|sv|no|da|fi|ru|pl|zh|ko|ja|cs|hu|el|tr|ar|he|th|vi|id|ms|ro|bg|uk|hr|sk|sl|et|lv|lt|af|ca|gd|eu))*\)")
    {
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
                long letterScore = 0;
                foreach (var ch in rev)
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
                    foreach (var ch in suffix.ToLowerInvariant())
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
            catch (RegexMatchTimeoutException) { }

            if (segments.Count > 0)
            {
                // Clamp to max 6 segments to prevent long overflow (1000^5 ≈ 10^15 fits in long).
                // CORE-02 FIX: If truncated, add +1 per extra segment so versions with
                // more segments score slightly higher than those with fewer.
                const int maxSegments = 6;
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
                    score += segments.Count - maxSegments;
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
