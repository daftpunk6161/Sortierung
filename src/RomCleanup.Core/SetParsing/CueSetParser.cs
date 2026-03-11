namespace RomCleanup.Core.SetParsing;

/// <summary>
/// Parses CUE sheet files to find related BIN/WAV/ISO track files.
/// Mirrors Get-CueRelatedFiles from SetParsing.ps1.
/// </summary>
public static class CueSetParser
{
    private static readonly System.Text.RegularExpressions.Regex RxFile =
        new(@"^\s*FILE\s+""(.+?)""\s+",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Returns all file paths referenced in a CUE sheet (resolved relative to the CUE directory).
    /// </summary>
    public static IReadOnlyList<string> GetRelatedFiles(string cuePath)
    {
        if (string.IsNullOrWhiteSpace(cuePath) || !File.Exists(cuePath))
            return Array.Empty<string>();

        var dir = Path.GetDirectoryName(cuePath) ?? "";
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in File.ReadLines(cuePath))
        {
            var match = RxFile.Match(line);
            if (!match.Success) continue;

            var refPath = match.Groups[1].Value;
            var fullPath = Path.IsPathRooted(refPath)
                ? refPath
                : Path.GetFullPath(Path.Combine(dir, refPath));

            // Path traversal guard: must stay within CUE directory
            if (!fullPath.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                continue;

            if (seen.Add(fullPath))
                result.Add(fullPath);
        }

        return result;
    }

    /// <summary>
    /// Returns referenced files that do NOT exist on disk.
    /// </summary>
    public static IReadOnlyList<string> GetMissingFiles(string cuePath)
    {
        return GetRelatedFiles(cuePath).Where(f => !File.Exists(f)).ToList();
    }
}
