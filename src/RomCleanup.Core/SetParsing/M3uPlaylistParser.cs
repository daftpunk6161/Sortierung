namespace RomCleanup.Core.SetParsing;

/// <summary>
/// Parses M3U playlist files to find referenced disc image files.
/// Supports recursive M3U references with depth limit (BUG-022 anti-DoS).
/// Mirrors Get-M3URelatedFiles from SetParsing.ps1.
/// </summary>
public static class M3uPlaylistParser
{
    private const int MaxDepth = 20;

    /// <summary>
    /// Returns all file paths referenced in an M3U playlist (recursive).
    /// Only includes files that exist on disk.
    /// </summary>
    public static IReadOnlyList<string> GetRelatedFiles(string m3uPath)
    {
        if (string.IsNullOrWhiteSpace(m3uPath) || !SetParserIo.Exists(m3uPath))
            return Array.Empty<string>();

        var result = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ResolveRecursive(m3uPath, result, visited, 0, existingOnly: true);

        return result;
    }

    public static IReadOnlyList<string> GetMissingFiles(string m3uPath)
    {
        if (string.IsNullOrWhiteSpace(m3uPath) || !SetParserIo.Exists(m3uPath))
            return Array.Empty<string>();

        var result = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ResolveRecursive(m3uPath, result, visited, 0, existingOnly: false);

        return result.Where(f => !SetParserIo.Exists(f)).ToList();
    }

    private static void ResolveRecursive(
        string m3uPath, List<string> result,
        HashSet<string> visited, int depth, bool existingOnly)
    {
        if (depth >= MaxDepth)
        {
            // V2-BUG-M02: Stop at max recursion depth — do NOT add warning strings
            // to the path list as they would be treated as file paths downstream.
            return;
        }

        var normalizedPath = Path.GetFullPath(m3uPath);
        if (!visited.Add(normalizedPath)) return; // circular reference guard
        if (!SetParserIo.Exists(normalizedPath)) return;

        var dir = Path.GetDirectoryName(normalizedPath) ?? "";

        foreach (var rawLine in SetParserIo.ReadLines(normalizedPath))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                continue;

            var refPath = Path.IsPathRooted(line)
                ? line
                : Path.GetFullPath(Path.Combine(dir, line));

            // Path traversal guard: must stay within M3U directory
            var normalizedDir = dir.TrimEnd(Path.DirectorySeparatorChar)
                                + Path.DirectorySeparatorChar;
            if (!refPath.StartsWith(normalizedDir, StringComparison.OrdinalIgnoreCase))
                continue;

            // Recursive M3U
            if (refPath.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) ||
                refPath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
            {
                ResolveRecursive(refPath, result, visited, depth + 1, existingOnly);
                continue;
            }

            if (!visited.Contains(refPath) && (!existingOnly || SetParserIo.Exists(refPath)))
            {
                visited.Add(refPath);
                result.Add(refPath);
            }
        }
    }
}
