using System.Text.RegularExpressions;

namespace Romulus.Core;

/// <summary>
/// Centralised regex-timeout-safe wrappers. Eliminates duplicate SafeIsMatch / SafeReplace /
/// IsMatchSafe / MatchSafe helpers that existed in GameKeyNormalizer, VersionScorer and FileClassifier.
/// </summary>
public static class SafeRegex
{
    /// <summary>Default timeout for heavy classification/normalization regexes.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(500);

    /// <summary>Short timeout for lightweight parsing helpers.</summary>
    public static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Timeout-safe <see cref="Regex.IsMatch(string)"/>.
    /// Returns <c>false</c> on <see cref="RegexMatchTimeoutException"/>.
    /// </summary>
    public static bool IsMatch(Regex regex, string input)
    {
        try
        {
            return regex.IsMatch(input);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// Timeout-safe <see cref="Regex.Match(string)"/>.
    /// Returns <see cref="Match.Empty"/> on <see cref="RegexMatchTimeoutException"/>.
    /// </summary>
    public static Match Match(Regex regex, string input)
    {
        try
        {
            return regex.Match(input);
        }
        catch (RegexMatchTimeoutException)
        {
            return System.Text.RegularExpressions.Match.Empty;
        }
    }

    /// <summary>
    /// Timeout-safe <see cref="Regex.Replace(string, string)"/> using a pre-compiled <see cref="Regex"/>.
    /// Returns the original <paramref name="input"/> on <see cref="RegexMatchTimeoutException"/>.
    /// </summary>
    public static string Replace(Regex regex, string input, string replacement)
    {
        try
        {
            return regex.Replace(input, replacement);
        }
        catch (RegexMatchTimeoutException)
        {
            return input;
        }
    }

    /// <summary>
    /// Timeout-safe <see cref="Regex.Replace(string, string, string, RegexOptions, TimeSpan)"/> for ad-hoc patterns.
    /// Returns the original <paramref name="input"/> on <see cref="RegexMatchTimeoutException"/>.
    /// </summary>
    public static string Replace(string input, string pattern, string replacement, RegexOptions options, TimeSpan timeout)
    {
        try
        {
            return Regex.Replace(input, pattern, replacement, options, timeout);
        }
        catch (RegexMatchTimeoutException)
        {
            return input;
        }
    }
}
