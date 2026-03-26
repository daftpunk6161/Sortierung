using System.Text.RegularExpressions;

namespace RomCleanup.Core.GameKeys;

/// <summary>
/// Normalizes folder names into grouping keys for folder-level deduplication.
/// Preserves disc/side markers and platform-variant tags (AGA/ECS/OCS/NTSC/PAL).
/// Pure domain logic — no I/O. Extracted from FolderDeduplicator (ADR-0007 §3.4).
/// </summary>
public static class FolderKeyNormalizer
{
    private static readonly TimeSpan RxTimeout = TimeSpan.FromMilliseconds(200);

    private static readonly Regex PreservePattern = new(
        @"(?:Disk|Disc|CD|Side)\s*[\dA-Z]|AGA|ECS|OCS|NTSC|PAL|WHDLoad|ADF",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, RxTimeout);

    private static readonly Regex ParenthesisPattern = new(
        @"\([^)]*\)", RegexOptions.Compiled, RxTimeout);

    private static readonly Regex TrailingBracketPattern = new(
        @"\s*\[[^\]]*\]\s*$", RegexOptions.Compiled, RxTimeout);

    private static readonly Regex VersionSuffixPattern = new(
        @"\s+v?\d+(\.\d+)+\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled, RxTimeout);

    private static readonly Regex MultiSpacePattern = new(
        @"\s{2,}", RegexOptions.Compiled, RxTimeout);

    private static readonly Regex MultidiscPattern = new(
        @"(?:Disc|Disk|CD|Side)\s*\d", RegexOptions.IgnoreCase | RegexOptions.Compiled, RxTimeout);

    /// <summary>
    /// Normalize a folder name into a grouping key for deduplication.
    /// </summary>
    public static string GetFolderBaseKey(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
            return "";

        var result = folderName;

        // Unicode normalization (FormC for basic folding)
        result = result.Normalize(System.Text.NormalizationForm.FormC);

        // Collect preserved parenthetical tags, strip all parens, re-append preserved
        var preserved = new List<string>();
        foreach (Match m in ParenthesisPattern.Matches(result))
        {
            if (PreservePattern.IsMatch(m.Value))
                preserved.Add(m.Value);
        }
        result = ParenthesisPattern.Replace(result, "");
        if (preserved.Count > 0)
            result = result.TrimEnd() + " " + string.Join(" ", preserved);

        // Strip all trailing bracket groups
        while (TrailingBracketPattern.IsMatch(result))
            result = TrailingBracketPattern.Replace(result, "");

        // Strip version-like suffixes
        result = VersionSuffixPattern.Replace(result, "");

        // Collapse multiple spaces
        result = MultiSpacePattern.Replace(result, " ").Trim();

        return string.IsNullOrWhiteSpace(result)
            ? folderName.Trim().ToLowerInvariant()
            : result.ToLowerInvariant();
    }

    /// <summary>
    /// Check if a folder name indicates a multi-disc game.
    /// </summary>
    public static bool IsMultidiscFolder(string folderName)
        => MultidiscPattern.IsMatch(folderName);
}
