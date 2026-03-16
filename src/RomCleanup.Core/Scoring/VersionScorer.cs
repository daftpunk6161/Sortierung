using System.Text.RegularExpressions;

namespace RomCleanup.Core.Scoring;

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

    private static readonly TimeSpan RxTimeout = TimeSpan.FromMilliseconds(500);

    // Pre-compiled patterns for revision parsing (TASK-154)
    private static readonly Regex RxPureLetters = new(@"^[a-z]+$", RegexOptions.Compiled, RxTimeout);
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
            versionPattern: @"\(v\s*(\d+)\.?(\d*)\)",
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
        if (_rxVerified.IsMatch(baseName)) score += 500;

        // Revision scoring
        var revMatch = _rxRevision.Match(baseName);
        if (revMatch.Success)
        {
            var rev = revMatch.Groups[1].Value.ToLowerInvariant();

            if (RxPureLetters.IsMatch(rev))
            {
                // Pure letter revision: a=1, b=2, ..., z=26, aa=27 etc.
                long letterScore = 0;
                foreach (var ch in rev)
                {
                    letterScore = (letterScore * 26) + (ch - 'a' + 1);
                }
                score += letterScore * 10;
            }
            else if (RxNumericSuffix.IsMatch(rev))
            {
                var numericMatch = RxNumericSuffix.Match(rev);
                var numeric = int.Parse(numericMatch.Groups[1].Value);
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
            else if (RxLeadingDigits.IsMatch(rev))
            {
                var digitMatch = RxLeadingDigits.Match(rev);
                score += int.Parse(digitMatch.Value) * 10L;
            }
        }

        // Version scoring (e.g. "(v1.2)")
        var verMatch = _rxVersion.Match(baseName);
        if (verMatch.Success)
        {
            var segments = new List<int>();
            foreach (Match seg in RxDigits.Matches(verMatch.Value))
            {
                segments.Add(int.Parse(seg.Value));
            }

            if (segments.Count > 0)
            {
                long weight = 1;
                for (var i = 1; i < segments.Count; i++)
                    weight *= 1000;

                long versionScore = 0;
                foreach (var seg in segments)
                {
                    versionScore += seg * weight;
                    if (weight > 1) weight /= 1000;
                }
                score += versionScore;
            }
        }

        // Language bonus: en = +50 + multi-lang bonus, de = +25
        var langMatch = _rxLang.Match(baseName);
        if (langMatch.Success)
        {
            var langs = langMatch.Value.ToLowerInvariant();
            if (langs.Contains("en"))
            {
                score += 50;
                var langCount = langs.Split(',').Length;
                score += langCount * 5;
            }
            if (langs.Contains("de"))
            {
                score += 25;
            }
        }

        return score;
    }
}
