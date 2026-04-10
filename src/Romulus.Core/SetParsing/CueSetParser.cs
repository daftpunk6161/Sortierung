namespace Romulus.Core.SetParsing;

/// <summary>
/// Parses CUE sheet files to find related BIN/WAV/ISO track files.
/// Mirrors Get-CueRelatedFiles from SetParsing.ps1.
/// </summary>
public static class CueSetParser
{
    private static readonly System.Text.RegularExpressions.Regex RxCueEntry =
        new(@"^\s*FILE\s+(?:""(.+?)""|(\S+))\s+",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Returns all file paths referenced in a CUE sheet (resolved relative to the CUE directory).
    /// Only includes files that exist on disk.
    /// </summary>
    public static IReadOnlyList<string> GetRelatedFiles(string cuePath)
    {
        return ParseReferencedPaths(cuePath, existingOnly: true);
    }

    /// <summary>
    /// Returns referenced files that do NOT exist on disk.
    /// </summary>
    public static IReadOnlyList<string> GetMissingFiles(string cuePath)
    {
        return ParseReferencedPaths(cuePath, existingOnly: false)
            .Where(f => !SetParserIo.Exists(f)).ToList();
    }

    private static IReadOnlyList<string> ParseReferencedPaths(string cuePath, bool existingOnly)
    {
        if (string.IsNullOrWhiteSpace(cuePath) || !SetParserIo.Exists(cuePath))
            return Array.Empty<string>();

        var dir = Path.GetDirectoryName(Path.GetFullPath(cuePath)) ?? "";
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // BUG-FIX: Use absolute directory from GetFullPath to handle relative CUE paths
        // and prevent the guard from being ineffective when dir is empty.
        var normalizedDir = dir.TrimEnd(Path.DirectorySeparatorChar)
                            + Path.DirectorySeparatorChar;

        foreach (var line in SetParserIo.ReadLines(cuePath))
        {
            var match = RxCueEntry.Match(line);
            if (!match.Success) continue;

            var refPath = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            var fullPath = Path.IsPathRooted(refPath)
                ? Path.GetFullPath(refPath)
                : Path.GetFullPath(Path.Combine(dir, refPath));

            // Path traversal guard: must stay within CUE directory
            if (!fullPath.StartsWith(normalizedDir, StringComparison.OrdinalIgnoreCase))
                continue;

            if (seen.Add(fullPath) && (!existingOnly || SetParserIo.Exists(fullPath)))
                result.Add(fullPath);
        }

        return result;
    }
}
