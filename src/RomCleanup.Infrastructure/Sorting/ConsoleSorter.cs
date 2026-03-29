using System.Text.RegularExpressions;
using RomCleanup.Contracts;
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
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly string[] ExcludedFolders =
    {
        RunConstants.WellKnownFolders.TrashRegionDedupe,
        RunConstants.WellKnownFolders.TrashJunk,
        RunConstants.WellKnownFolders.Bios,
        RunConstants.WellKnownFolders.Junk,
        RunConstants.WellKnownFolders.Review
    };

    private readonly IFileSystem _fs;
    private readonly ConsoleDetector _consoleDetector;
    private readonly IAuditStore? _audit;
    private readonly string? _auditPath;

    public ConsoleSorter(IFileSystem fs, ConsoleDetector consoleDetector,
        IAuditStore? audit = null, string? auditPath = null)
    {
        _fs = fs;
        _consoleDetector = consoleDetector;
        _audit = audit;
        _auditPath = auditPath;
    }

    /// <summary>
    /// Sort files in the given roots into console subdirectories.
    /// </summary>
    /// <param name="roots">Root directories to scan.</param>
    /// <param name="extensions">File extensions to include (e.g. ".chd", ".iso").</param>
    /// <param name="dryRun">If true, don't actually move files.</param>
    /// <param name="cancellationToken">Cancellation support.</param>
    /// <param name="enrichedConsoleKeys">
    /// Pre-enriched ConsoleKey mappings (filePath → consoleKey) from the enrichment phase.
    /// Sorting is intentionally enrichment-driven to keep one deterministic source of truth.
    /// If this map is missing, files are skipped with an audit warning.
    /// </param>
    /// <param name="enrichedSortDecisions">
    /// Optional: pre-enriched SortDecision mappings (filePath → "Sort"/"Review"/"Blocked"/"DatVerified").
    /// When provided, controls routing: Review → _REVIEW/{ConsoleKey}/, Blocked → no move.
    /// </param>
    /// <param name="enrichedCategories">
    /// Optional: pre-enriched category mappings (filePath → "Game"/"Junk"/"NonGame"/"Bios"/"Unknown").
    /// When provided, Junk files with a known console go to _TRASH_JUNK/{ConsoleKey}/.
    /// </param>
    /// <returns>Sort result with counters.</returns>
    public ConsoleSortResult Sort(
        IReadOnlyList<string> roots,
        IEnumerable<string>? extensions = null,
        bool dryRun = true,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? enrichedConsoleKeys = null,
        IReadOnlyDictionary<string, string>? enrichedSortDecisions = null,
        IReadOnlyDictionary<string, string>? enrichedCategories = null)
    {
        int total = 0, moved = 0, skipped = 0, unknown = 0, setMembersMoved = 0, failed = 0, reviewed = 0, blocked = 0;
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

            if (enrichedConsoleKeys is null)
            {
                // Phase-2 contract: never perform local re-detection in sort phase.
                // Keep enrichment as the single truth and skip safely if missing.
                total += files.Count;
                skipped += files.Count;
                IncrementReason(unknownReasons, "missing-enriched-console-keys");
                WriteAuditWarning(root, "missing-enriched-console-keys");
                continue;
            }

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

                // Sorting uses enrichment as the single source of truth.
                var hasEnrichedKey = enrichedConsoleKeys.TryGetValue(filePath, out var enrichedKey);
                var consoleKey = hasEnrichedKey && !string.IsNullOrEmpty(enrichedKey)
                    ? enrichedKey
                    : "UNKNOWN";

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

                // SortDecision routing: determine destination based on enriched decision
                var sortDecision = enrichedSortDecisions is not null &&
                    enrichedSortDecisions.TryGetValue(filePath, out var sd) ? sd : null;
                var category = enrichedCategories is not null &&
                    enrichedCategories.TryGetValue(filePath, out var cat) ? cat : null;

                // Blocked files are not moved
                if (string.Equals(sortDecision, "Blocked", StringComparison.OrdinalIgnoreCase))
                {
                    // Junk with known console → _TRASH_JUNK/{ConsoleKey}/
                    if (string.Equals(category, "Junk", StringComparison.OrdinalIgnoreCase))
                    {
                        var junkDir = Path.Combine(root, RunConstants.WellKnownFolders.TrashJunk, consoleKey);
                        var junkFileName = Path.GetFileName(filePath);
                        if (MoveFile(root, filePath, junkDir, junkFileName, dryRun))
                        {
                            blocked++;
                            WriteAuditRow(root, filePath, Path.Combine(junkDir, junkFileName), $"junk-sort:{consoleKey}");
                        }
                        else
                        {
                            failed++;
                        }
                        continue;
                    }

                    blocked++;
                    continue;
                }

                // Review files → _REVIEW/{ConsoleKey}/
                if (string.Equals(sortDecision, "Review", StringComparison.OrdinalIgnoreCase))
                {
                    var reviewDir = Path.Combine(root, RunConstants.WellKnownFolders.Review, consoleKey);
                    var reviewFileName = Path.GetFileName(filePath);
                    if (MoveFile(root, filePath, reviewDir, reviewFileName, dryRun))
                    {
                        reviewed++;
                        WriteAuditRow(root, filePath, Path.Combine(reviewDir, reviewFileName), $"review-sort:{consoleKey}");
                    }
                    else
                    {
                        failed++;
                    }
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

                // Move file (with atomic set-move + rollback for multi-file sets)
                if (setPrimaryToMembers.TryGetValue(filePath, out var members))
                {
                    // Atomic set-move: primary + all members succeed, or all roll back
                    if (dryRun)
                    {
                        moved++;
                        setMembersMoved += members.Count;
                    }
                    else
                    {
                        var (primaryMoved, membersMoved) = MoveSetAtomically(
                            root, filePath, members, expectedDir, dryRun);
                        if (primaryMoved) moved++;
                        else failed += members.Count + 1;
                        setMembersMoved += membersMoved;
                    }
                }
                else
                {
                    // Standalone file — no set members
                    if (MoveFile(root, filePath, expectedDir, fileName, dryRun))
                    {
                        moved++;
                        WriteAuditRow(root, filePath, Path.Combine(expectedDir, fileName), consoleKey);
                    }
                    else
                    {
                        failed++;
                    }
                }
            }
        }

        return new ConsoleSortResult(total, moved, setMembersMoved, skipped, unknown, unknownReasons, failed, reviewed, blocked);
    }

    /// <summary>
    /// Moves a primary file and all its set members atomically.
    /// If any member fails to move, all previously moved files are rolled back.
    /// </summary>
    private (bool PrimaryMoved, int MembersMoved) MoveSetAtomically(
        string root,
        string primaryPath,
        List<string> members,
        string destDir,
        bool dryRun)
    {
        if (dryRun) return (true, members.Count);

        // Track all moves so we can roll back on partial failure
        var completedMoves = new List<(string Source, string Dest)>();

        try
        {
            // Move primary first
            var primaryDest = ResolveMoveDestination(root, primaryPath, destDir);
            if (primaryDest is null) return (false, 0);
            _fs.EnsureDirectory(destDir);
            if (_fs.MoveItemSafely(primaryPath, primaryDest) is null)
                return (false, 0);
            completedMoves.Add((primaryPath, primaryDest));

            // Move each member
            foreach (var member in members)
            {
                var memberDest = ResolveMoveDestination(root, member, destDir);
                if (memberDest is null)
                    throw new InvalidOperationException(
                        $"Path traversal blocked for set member: {member}");

                if (_fs.MoveItemSafely(member, memberDest) is null)
                    throw new InvalidOperationException(
                        $"Move failed for set member: {member}");
                completedMoves.Add((member, memberDest));
            }

            // Audit all moves in the atomic set after all succeeded
            var consoleKey = Path.GetFileName(destDir);
            WriteAuditRow(root, primaryPath, primaryDest, consoleKey);
            foreach (var (src, dst) in completedMoves.Skip(1)) // skip primary, already written
                WriteAuditRow(root, src, dst, consoleKey + ":set-member");

            return (true, members.Count);
        }
        catch (Exception ex)
        {
            // Roll back all completed moves in reverse order using safe move
            var rollbackFailures = new List<string>();
            foreach (var (source, dest) in completedMoves.AsEnumerable().Reverse())
            {
                try
                {
                    var actualDest = FindActualDestination(dest);
                    if (actualDest is not null && File.Exists(actualDest))
                        _ = _fs.MoveItemSafely(actualDest, source);
                }
                catch (Exception rbEx)
                {
                    rollbackFailures.Add($"Rollback failed for {dest} → {source}: {rbEx.Message}");
                }
            }

            if (rollbackFailures.Count > 0)
            {
                throw new AggregateException(
                    $"Set move failed ({ex.Message}) and {rollbackFailures.Count} rollback(s) also failed: {string.Join("; ", rollbackFailures)}",
                    ex);
            }

            return (false, 0);
        }
    }

    private string? ResolveMoveDestination(string root, string sourcePath, string destDir)
    {
        var fileName = Path.GetFileName(sourcePath);
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedDest = Path.GetFullPath(destDir);
        var relativeDest = normalizedDest.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? normalizedDest[(normalizedRoot.Length + 1)..]
            : Path.GetFileName(destDir);
        return _fs.ResolveChildPathWithinRoot(root, Path.Combine(relativeDest, fileName));
    }

    /// <summary>
    /// Finds the actual file on disk, accounting for __DUP collision renaming.
    /// Returns the exact path if found, or null.
    /// </summary>
    private static string? FindActualDestination(string intendedDest)
    {
        if (File.Exists(intendedDest))
            return intendedDest;

        // Check for __DUP renamed versions using directory listing
        var dir = Path.GetDirectoryName(intendedDest) ?? "";
        if (!Directory.Exists(dir))
            return null;

        var baseName = Path.GetFileNameWithoutExtension(intendedDest);
        var ext = Path.GetExtension(intendedDest);
        var pattern = $"{baseName}__DUP*{ext}";

        var matches = Directory.GetFiles(dir, pattern);
        if (matches.Length == 0) return null;
        if (matches.Length == 1) return matches[0];
        // SEC-SORT-01: Sort for determinism using numeric DUP suffix first.
        // Lexical ordering breaks for 10+ duplicates (__DUP10 comes before __DUP2).
        Array.Sort(matches, (left, right) =>
        {
            var leftDup = ParseDupSuffix(left, baseName, ext);
            var rightDup = ParseDupSuffix(right, baseName, ext);
            var byDup = leftDup.CompareTo(rightDup);
            if (byDup != 0)
                return byDup;

            return StringComparer.OrdinalIgnoreCase.Compare(left, right);
        });
        return matches[^1];
    }

    private static int ParseDupSuffix(string path, string baseName, string ext)
    {
        var fileName = Path.GetFileName(path);
        if (string.IsNullOrEmpty(fileName))
            return 0;

        var expectedPrefix = baseName + "__DUP";
        if (!fileName.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase)
            || !fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var numberPart = fileName.Substring(expectedPrefix.Length, fileName.Length - expectedPrefix.Length - ext.Length);
        return int.TryParse(numberPart, out var parsed) ? parsed : 0;
    }

    private bool MoveFile(string root, string sourcePath, string destDir, string fileName, bool dryRun)
    {
        if (dryRun) return true;

        _fs.EnsureDirectory(destDir);
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedDest = Path.GetFullPath(destDir);
        var relativeDest = normalizedDest.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? normalizedDest[(normalizedRoot.Length + 1)..]
            : Path.GetFileName(destDir);
        var destPath = _fs.ResolveChildPathWithinRoot(root, Path.Combine(relativeDest, fileName));

        if (destPath is null) return false; // path traversal blocked

        return _fs.MoveItemSafely(sourcePath, destPath) is not null;
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

    private void WriteAuditRow(string root, string oldPath, string newPath, string consoleKey)
    {
        if (_audit is null || string.IsNullOrEmpty(_auditPath))
            return;

        _audit.AppendAuditRow(_auditPath, root, oldPath, newPath,
            "CONSOLE_SORT", "GAME", "", $"console-sort:{consoleKey}");
    }

    private void WriteAuditWarning(string root, string reason)
    {
        if (_audit is null || string.IsNullOrEmpty(_auditPath))
            return;

        _audit.AppendAuditRow(_auditPath, root, root, root,
            "CONSOLE_SORT", "SYSTEM", "", reason);
    }

    private static void IncrementReason(Dictionary<string, int> reasons, string reason)
    {
        reasons[reason] = reasons.TryGetValue(reason, out var count) ? count + 1 : 1;
    }
}
