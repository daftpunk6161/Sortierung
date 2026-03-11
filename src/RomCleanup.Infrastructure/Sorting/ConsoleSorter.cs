using System.Text.RegularExpressions;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Core.Classification;
using RomCleanup.Core.SetParsing;

namespace RomCleanup.Infrastructure.Sorting;

/// <summary>
/// Sorts ROM files into console-specific subdirectories.
/// Port of Invoke-ConsoleSort from ConsoleSort.ps1.
/// Thread-safe (stateless per invocation).
/// </summary>
public sealed class ConsoleSorter
{
    private static readonly Regex RxValidConsoleKey = new(@"^[A-Z0-9_-]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] ExcludedFolders =
        { "_TRASH_REGION_DEDUPE", "_BIOS", "_JUNK" };

    private readonly IFileSystem _fs;
    private readonly ConsoleDetector _consoleDetector;

    public ConsoleSorter(IFileSystem fs, ConsoleDetector consoleDetector)
    {
        _fs = fs;
        _consoleDetector = consoleDetector;
    }

    /// <summary>
    /// Sort files in the given roots into console subdirectories.
    /// </summary>
    /// <param name="roots">Root directories to scan.</param>
    /// <param name="extensions">File extensions to include (e.g. ".chd", ".iso").</param>
    /// <param name="dryRun">If true, don't actually move files.</param>
    /// <param name="cancellationToken">Cancellation support.</param>
    /// <returns>Sort result with counters.</returns>
    public ConsoleSortResult Sort(
        IReadOnlyList<string> roots,
        IEnumerable<string>? extensions = null,
        bool dryRun = true,
        CancellationToken cancellationToken = default)
    {
        int total = 0, moved = 0, skipped = 0, unknown = 0, setMembersMoved = 0;
        var unknownReasons = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            if (cancellationToken.IsCancellationRequested) break;
            if (!_fs.TestPath(root, "Container")) continue;

            var files = _fs.GetFilesSafe(root, extensions)
                .Where(f => !IsInExcludedFolder(f, root))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Pre-scan: identify set members (CUE+BIN, GDI, CCD, M3U, MDS)
            var setDependents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var setPrimaryToMembers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            BuildSetMemberships(files, setDependents, setPrimaryToMembers);

            foreach (var filePath in files)
            {
                if (cancellationToken.IsCancellationRequested) break;
                total++;

                // Skip dependent set members — they move with their primary
                if (setDependents.Contains(filePath))
                {
                    skipped++;
                    continue;
                }

                // Detect console
                var consoleKey = _consoleDetector.Detect(filePath, root);

                if (consoleKey == "UNKNOWN" || string.IsNullOrEmpty(consoleKey))
                {
                    unknown++;
                    IncrementReason(unknownReasons, "no-match");
                    continue;
                }

                // Security: validate console key format
                if (!RxValidConsoleKey.IsMatch(consoleKey))
                {
                    unknown++;
                    IncrementReason(unknownReasons, "invalid-key");
                    continue;
                }

                // Check if already in correct subfolder
                var fileName = Path.GetFileName(filePath);
                var expectedDir = Path.Combine(root, consoleKey);
                var currentDir = Path.GetDirectoryName(filePath) ?? "";
                if (currentDir.Equals(expectedDir, StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    continue;
                }

                // Move file
                if (MoveFile(root, filePath, expectedDir, fileName, dryRun))
                    moved++;

                // Move set members
                if (setPrimaryToMembers.TryGetValue(filePath, out var members))
                {
                    foreach (var member in members)
                    {
                        var memberName = Path.GetFileName(member);
                        if (MoveFile(root, member, expectedDir, memberName, dryRun))
                            setMembersMoved++;
                    }
                }
            }
        }

        return new ConsoleSortResult(total, moved, setMembersMoved, skipped, unknown, unknownReasons);
    }

    private bool MoveFile(string root, string sourcePath, string destDir, string fileName, bool dryRun)
    {
        if (dryRun) return true;

        _fs.EnsureDirectory(destDir);
        var destPath = _fs.ResolveChildPathWithinRoot(root, Path.Combine(
            Path.GetFileName(destDir), fileName));

        if (destPath is null) return false; // path traversal blocked

        return _fs.MoveItemSafely(sourcePath, destPath);
    }

    private static void BuildSetMemberships(
        List<string> files,
        HashSet<string> setDependents,
        Dictionary<string, List<string>> setPrimaryToMembers)
    {
        foreach (var file in files)
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            IReadOnlyList<string> members = ext switch
            {
                ".cue" => CueSetParser.GetRelatedFiles(file),
                ".gdi" => GdiSetParser.GetRelatedFiles(file),
                ".ccd" => CcdSetParser.GetRelatedFiles(file),
                ".m3u" => M3uPlaylistParser.GetRelatedFiles(file),
                ".mds" => MdsSetParser.GetRelatedFiles(file),
                _ => Array.Empty<string>()
            };

            if (members.Count > 0)
            {
                setPrimaryToMembers[file] = members.ToList();
                foreach (var m in members)
                    setDependents.Add(m);
            }
        }
    }

    private static bool IsInExcludedFolder(string filePath, string root)
    {
        var relative = filePath.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var firstSegment = relative.Split(new[] { '/', '\\' }, 2, StringSplitOptions.RemoveEmptyEntries);
        return firstSegment.Length > 0 &&
               ExcludedFolders.Any(e => e.Equals(firstSegment[0], StringComparison.OrdinalIgnoreCase));
    }

    private static void IncrementReason(Dictionary<string, int> reasons, string reason)
    {
        reasons[reason] = reasons.TryGetValue(reason, out var count) ? count + 1 : 1;
    }
}
