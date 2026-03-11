namespace RomCleanup.Core.SetParsing;

/// <summary>
/// Parses Dreamcast GDI files to find related track files.
/// Mirrors Get-GdiRelatedFiles from SetParsing.ps1.
/// </summary>
public static class GdiSetParser
{
    /// <summary>
    /// Returns all track file paths referenced in a GDI file.
    /// </summary>
    public static IReadOnlyList<string> GetRelatedFiles(string gdiPath)
    {
        if (string.IsNullOrWhiteSpace(gdiPath) || !File.Exists(gdiPath))
            return Array.Empty<string>();

        var dir = Path.GetDirectoryName(gdiPath) ?? "";
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in File.ReadLines(gdiPath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // GDI format: trackNum startLBA type sectorSize "filename" offset
            // or:          trackNum startLBA type sectorSize filename offset
            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5) continue;

            // Skip the track-count line (first line, single number)
            if (parts.Length == 1) continue;

            // Extract filename (column 5, 0-indexed = 4)
            var fileName = parts[4].Trim('"');
            if (string.IsNullOrWhiteSpace(fileName)) continue;

            var fullPath = Path.IsPathRooted(fileName)
                ? fileName
                : Path.GetFullPath(Path.Combine(dir, fileName));

            if (!fullPath.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                continue;

            if (seen.Add(fullPath))
                result.Add(fullPath);
        }

        return result;
    }

    public static IReadOnlyList<string> GetMissingFiles(string gdiPath)
    {
        return GetRelatedFiles(gdiPath).Where(f => !File.Exists(f)).ToList();
    }
}
