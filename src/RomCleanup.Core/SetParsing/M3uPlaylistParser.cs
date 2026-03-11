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
    /// </summary>
    public static IReadOnlyList<string> GetRelatedFiles(string m3uPath)
    {
        if (string.IsNullOrWhiteSpace(m3uPath) || !File.Exists(m3uPath))
            return Array.Empty<string>();

        var result = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ResolveRecursive(m3uPath, result, visited, 0);

        return result;
    }

    private static void ResolveRecursive(
        string m3uPath, List<string> result,
        HashSet<string> visited, int depth)
    {
        if (depth >= MaxDepth) return;

        var normalizedPath = Path.GetFullPath(m3uPath);
        if (!visited.Add(normalizedPath)) return; // circular reference guard
        if (!File.Exists(normalizedPath)) return;

        var dir = Path.GetDirectoryName(normalizedPath) ?? "";

        foreach (var rawLine in File.ReadLines(normalizedPath))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                continue;

            var refPath = Path.IsPathRooted(line)
                ? line
                : Path.GetFullPath(Path.Combine(dir, line));

            // Path traversal guard
            if (!refPath.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                continue;

            // Recursive M3U
            if (refPath.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) ||
                refPath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
            {
                ResolveRecursive(refPath, result, visited, depth + 1);
                continue;
            }

            if (!visited.Contains(refPath))
            {
                visited.Add(refPath);
                result.Add(refPath);
            }
        }
    }

    public static IReadOnlyList<string> GetMissingFiles(string m3uPath)
    {
        return GetRelatedFiles(m3uPath).Where(f => !File.Exists(f)).ToList();
    }
}
