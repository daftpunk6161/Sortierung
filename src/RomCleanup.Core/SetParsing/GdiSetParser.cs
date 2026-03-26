namespace RomCleanup.Core.SetParsing;

/// <summary>
/// Parses Dreamcast GDI files to find related track files.
/// Mirrors Get-GdiRelatedFiles from SetParsing.ps1.
/// </summary>
public static class GdiSetParser
{
    /// <summary>
    /// Returns all track file paths referenced in a GDI file.
    /// Only includes files that exist on disk.
    /// </summary>
    public static IReadOnlyList<string> GetRelatedFiles(string gdiPath)
    {
        return ParseReferencedPaths(gdiPath, existingOnly: true);
    }

    public static IReadOnlyList<string> GetMissingFiles(string gdiPath)
    {
        return ParseReferencedPaths(gdiPath, existingOnly: false)
            .Where(f => !SetParserIo.Exists(f)).ToList();
    }

    private static IReadOnlyList<string> ParseReferencedPaths(string gdiPath, bool existingOnly)
    {
        if (string.IsNullOrWhiteSpace(gdiPath) || !SetParserIo.Exists(gdiPath))
            return Array.Empty<string>();

        var dir = Path.GetDirectoryName(Path.GetFullPath(gdiPath)) ?? "";
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in SetParserIo.ReadLines(gdiPath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // Skip the track-count line (first line, single number)
            if (int.TryParse(trimmed, out _)) continue;

            // GDI format: trackNum startLBA type sectorSize "filename" offset
            // or:          trackNum startLBA type sectorSize filename offset
            // Handle quoted filenames with spaces
            string fileName;
            var quoteStart = trimmed.IndexOf('"');
            if (quoteStart >= 0)
            {
                var quoteEnd = trimmed.IndexOf('"', quoteStart + 1);
                if (quoteEnd < 0) continue;
                fileName = trimmed.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
            }
            else
            {
                var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5) continue;
                fileName = parts[4];
            }

            if (string.IsNullOrWhiteSpace(fileName)) continue;

            string fullPath;
            try
            {
                fullPath = Path.IsPathRooted(fileName)
                    ? fileName
                    : Path.GetFullPath(Path.Combine(dir, fileName));
            }
            catch (ArgumentException)
            {
                continue;
            }

            var normalizedDir = dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(normalizedDir, StringComparison.OrdinalIgnoreCase))
                continue;

            if (seen.Add(fullPath) && (!existingOnly || SetParserIo.Exists(fullPath)))
                result.Add(fullPath);
        }

        return result;
    }
}
