using System.IO.Compression;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.Sorting;

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
    /// Sort ZIPs contained in the given roots into console-specific subdirectories
    /// based on their internal file extensions matched against a console-to-extensions mapping.
    /// Generalized version that supports any number of consoles.
    /// </summary>
    /// <param name="roots">Root directories to scan for ZIP files.</param>
    /// <param name="fs">Filesystem abstraction.</param>
    /// <param name="consoleExtensionMap">
    /// Mapping from console key (e.g. "PS1", "PS2", "SAT") to the set of
    /// internal-ZIP extensions that uniquely identify that console.
    /// Typically loaded from consoles.json zipIdentifyingExts.
    /// </param>
    /// <param name="dryRun">When true, only counts without moving files.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static ZipSortResult SortByConsole(
        IReadOnlyList<string> roots,
        IFileSystem fs,
        IReadOnlyDictionary<string, IReadOnlySet<string>> consoleExtensionMap,
        bool dryRun = true,
        CancellationToken cancellationToken = default)
    {
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

                // N-way classification: find all console matches
                string? matchedConsole = null;
                bool ambiguous = false;

                foreach (var (consoleKey, identifyingExts) in consoleExtensionMap)
                {
                    if (extensions.Any(e => identifyingExts.Contains(e)))
                    {
                        if (matchedConsole is not null)
                        {
                            // Multiple consoles match — ambiguous
                            ambiguous = true;
                            break;
                        }
                        matchedConsole = consoleKey;
                    }
                }

                if (ambiguous || matchedConsole is null)
                {
                    skipped++;
                    continue;
                }

                var targetDir = Path.Combine(root, matchedConsole);
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
                var destPath = fs.ResolveChildPathWithinRoot(root, Path.Combine(matchedConsole, fileName));
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

    /// <summary>
    /// Sort ZIPs contained in the given roots into PS1/PS2 subdirectories
    /// based on their internal file extensions.
    /// Port of Invoke-ZipSortPS1PS2. Delegates to <see cref="SortByConsole"/>.
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
        var map = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["PS1"] = ps1Extensions ?? DefaultPs1Extensions,
            ["PS2"] = ps2Extensions ?? DefaultPs2Extensions
        };
        return SortByConsole(roots, fs, map, dryRun, cancellationToken);
    }
}
