using System.IO.Compression;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;

namespace RomCleanup.Infrastructure.Sorting;

/// <summary>
/// Sorts ZIP archives based on internal file extensions.
/// Port of ZipSort.ps1 (Get-ZipEntryExtensions, Invoke-ZipSortPS1PS2).
/// </summary>
public static class ZipSorter
{
    /// <summary>Default PS1-specific extensions inside ZIPs. Overridable via <see cref="SortPS1PS2"/>.</summary>
    public static readonly HashSet<string> DefaultPs1Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".ccd", ".sub", ".pbp" };

    /// <summary>Default PS2-specific extensions inside ZIPs. Overridable via <see cref="SortPS1PS2"/>.</summary>
    public static readonly HashSet<string> DefaultPs2Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".nrg", ".mdf", ".mds" };

    /// <summary>
    /// Extract all distinct file extensions from entries inside a ZIP archive.
    /// Returns lowercase extensions with leading dot. Empty array on failure.
    /// </summary>
    public static string[] GetZipEntryExtensions(string zipPath)
    {
        if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
            return Array.Empty<string>();

        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                var ext = Path.GetExtension(entry.FullName);
                if (!string.IsNullOrEmpty(ext))
                    extensions.Add(ext.ToLowerInvariant());
            }
            return extensions.ToArray();
        }
        catch (InvalidDataException) { return Array.Empty<string>(); }
        catch (IOException) { return Array.Empty<string>(); }
    }

    /// <summary>
    /// Sort ZIPs contained in the given roots into PS1/PS2 subdirectories
    /// based on their internal file extensions.
    /// Port of Invoke-ZipSortPS1PS2.
    /// </summary>
    /// <param name="ps1Extensions">Override PS1 extensions; defaults to <see cref="DefaultPs1Extensions"/>.</param>
    /// <param name="ps2Extensions">Override PS2 extensions; defaults to <see cref="DefaultPs2Extensions"/>.</param>
    public static ZipSortResult SortPS1PS2(
        IReadOnlyList<string> roots,
        IFileSystem fs,
        bool dryRun = true,
        CancellationToken cancellationToken = default,
        IReadOnlySet<string>? ps1Extensions = null,
        IReadOnlySet<string>? ps2Extensions = null)
    {
        var ps1Exts = ps1Extensions ?? DefaultPs1Extensions;
        var ps2Exts = ps2Extensions ?? DefaultPs2Extensions;
        int total = 0, moved = 0, skipped = 0, errors = 0;

        foreach (var root in roots)
        {
            if (cancellationToken.IsCancellationRequested) break;
            if (!fs.TestPath(root, "Container")) continue;

            var zipFiles = fs.GetFilesSafe(root, new[] { ".zip" });

            foreach (var zipPath in zipFiles)
            {
                if (cancellationToken.IsCancellationRequested) break;
                total++;

                var extensions = GetZipEntryExtensions(zipPath);
                if (extensions.Length == 0)
                {
                    skipped++;
                    continue;
                }

                bool hasPs1 = extensions.Any(e => ps1Exts.Contains(e));
                bool hasPs2 = extensions.Any(e => ps2Exts.Contains(e));

                // Ambiguous: contains both PS1 and PS2 extensions — skip
                if (hasPs1 && hasPs2)
                {
                    skipped++;
                    continue;
                }

                string? targetConsole = hasPs1 ? "PS1" : hasPs2 ? "PS2" : null;
                if (targetConsole is null)
                {
                    skipped++;
                    continue;
                }

                var targetDir = Path.Combine(root, targetConsole);
                var fileName = Path.GetFileName(zipPath);

                // Already in correct folder?
                var currentDir = Path.GetDirectoryName(zipPath) ?? "";
                if (currentDir.Equals(targetDir, StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    continue;
                }

                if (dryRun)
                {
                    moved++;
                    continue;
                }

                fs.EnsureDirectory(targetDir);
                var destPath = fs.ResolveChildPathWithinRoot(root, Path.Combine(targetConsole, fileName));
                if (destPath is null)
                {
                    errors++;
                    continue;
                }

                if (fs.MoveItemSafely(zipPath, destPath) is not null)
                    moved++;
                else
                    errors++;
            }
        }

        return new ZipSortResult(total, moved, skipped, errors);
    }
}
