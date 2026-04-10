using System.Text.RegularExpressions;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Classification;
using Romulus.Core.SetParsing;

namespace Romulus.Infrastructure.Sorting;

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

    private static readonly Regex RxReasonSegment = new(@"[^a-z0-9_-]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly string[] ExcludedFolders =
    {
        RunConstants.WellKnownFolders.TrashRegionDedupe,
        RunConstants.WellKnownFolders.TrashJunk,
        RunConstants.WellKnownFolders.Bios,
        RunConstants.WellKnownFolders.Junk,
        RunConstants.WellKnownFolders.Review,
        RunConstants.WellKnownFolders.Blocked,
        RunConstants.WellKnownFolders.Unknown
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
    /// When provided, controls routing: Review → _REVIEW/{ConsoleKey}/,
    /// Blocked → _BLOCKED/{Reason}/, Unknown → _UNKNOWN/.
    /// </param>
    /// <param name="enrichedSortReasons">
    /// Optional: pre-enriched reason tag mappings (filePath → reason).
    /// Used for reason-aware review/blocked audit tags and blocked subfolders.
    /// </param>
    /// <param name="enrichedCategories">
    /// Optional: pre-enriched category mappings (filePath → "Game"/"Junk"/"NonGame"/"Bios"/"Unknown").
    /// When provided, Junk files with a known console go to _TRASH_JUNK/{ConsoleKey}/.
    /// </param>
    /// <param name="candidatePaths">
    /// Optional: pre-enumerated file paths to sort. When provided, avoids a second filesystem
    /// scan and keeps preview/execute aligned with the orchestrator's already enriched candidate set.
    /// </param>
    /// <returns>Sort result with counters.</returns>
    public ConsoleSortResult Sort(
        IReadOnlyList<string> roots,
        IEnumerable<string>? extensions = null,
        bool dryRun = true,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? enrichedConsoleKeys = null,
        IReadOnlyDictionary<string, string>? enrichedSortDecisions = null,
        IReadOnlyDictionary<string, string>? enrichedSortReasons = null,
        IReadOnlyDictionary<string, string>? enrichedCategories = null,
        IReadOnlyList<string>? candidatePaths = null,
        string? conflictPolicy = null)
    {
        int total = 0, moved = 0, skipped = 0, unknown = 0, setMembersMoved = 0, failed = 0, reviewed = 0, blocked = 0;
        var unknownReasons = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var pathMutations = new List<PathMutation>();
        var overwriteConflicts = string.Equals(conflictPolicy, "Overwrite", StringComparison.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            if (cancellationToken.IsCancellationRequested) break;
            if (!_fs.TestPath(root, "Container")) continue;

            var files = GetFilesForRoot(root, extensions, candidatePaths);

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

                // SortDecision routing: determine destination based on enriched decision
                var sortDecision = enrichedSortDecisions is not null &&
                    enrichedSortDecisions.TryGetValue(filePath, out var sd) ? sd : null;
                var sortReason = enrichedSortReasons is not null &&
                    enrichedSortReasons.TryGetValue(filePath, out var sr) ? sr : null;
                var category = enrichedCategories is not null &&
                    enrichedCategories.TryGetValue(filePath, out var cat) ? cat : null;

                // Sorting uses enrichment as the single source of truth.
                var hasEnrichedKey = enrichedConsoleKeys.TryGetValue(filePath, out var enrichedKey);
                var consoleKey = hasEnrichedKey && !string.IsNullOrEmpty(enrichedKey)
                    ? enrichedKey
                    : "UNKNOWN";

                var hasKnownConsole = !string.IsNullOrEmpty(consoleKey)
                    && !string.Equals(consoleKey, "UNKNOWN", StringComparison.OrdinalIgnoreCase)
                    && RxValidConsoleKey.IsMatch(consoleKey);

                var isBlockedDecision = string.Equals(sortDecision, "Blocked", StringComparison.OrdinalIgnoreCase);
                var isUnknownDecision = string.Equals(sortDecision, "Unknown", StringComparison.OrdinalIgnoreCase);

                // Blocked/Unknown routing
                if (isBlockedDecision || isUnknownDecision)
                {
                    // Junk with known/unknown console → _TRASH_JUNK/{ConsoleKey}/
                    if (string.Equals(category, "Junk", StringComparison.OrdinalIgnoreCase))
                    {
                        var junkConsoleKey = hasKnownConsole ? consoleKey : "UNKNOWN";
                        var junkDir = Path.Combine(root, RunConstants.WellKnownFolders.TrashJunk, junkConsoleKey);
                        var junkFileName = Path.GetFileName(filePath);
                        if (setPrimaryToMembers.TryGetValue(filePath, out var junkMembers))
                        {
                            var setMoveResult = MoveSetAtomically(
                                root,
                                filePath,
                                junkMembers,
                                junkDir,
                                dryRun,
                                $"junk-sort:{junkConsoleKey}",
                                overwriteConflicts);

                            if (setMoveResult.PrimaryMoved)
                            {
                                if (isBlockedDecision)
                                    blocked++;
                                else
                                    unknown++;
                                setMembersMoved += setMoveResult.MembersMoved;
                                pathMutations.AddRange(setMoveResult.PathMutations);
                            }
                            else
                            {
                                failed += junkMembers.Count + 1;
                            }
                        }
                        else
                        {
                            if (TryMoveFile(root, filePath, junkDir, junkFileName, dryRun, overwriteConflicts, out var actualDest))
                            {
                                if (isBlockedDecision)
                                    blocked++;
                                else
                                    unknown++;
                                if (actualDest is not null)
                                {
                                    pathMutations.Add(new PathMutation(filePath, actualDest));
                                    WriteAuditRow(root, filePath, actualDest, $"junk-sort:{junkConsoleKey}");
                                }
                            }
                            else
                            {
                                failed++;
                            }
                        }

                        continue;
                    }

                    // Non-junk blocked/unknown files are moved to dedicated staging folders.
                    var reasonSegment = ToSafeReasonSegment(sortReason,
                        isBlockedDecision ? "blocked" : "unknown");
                    var decisionDir = isBlockedDecision
                        ? Path.Combine(root, RunConstants.WellKnownFolders.Blocked, reasonSegment)
                        : Path.Combine(root, RunConstants.WellKnownFolders.Unknown);
                    var decisionFileName = Path.GetFileName(filePath);
                    if (setPrimaryToMembers.TryGetValue(filePath, out var decisionMembers))
                    {
                        var setMoveResult = MoveSetAtomically(
                            root,
                            filePath,
                            decisionMembers,
                            decisionDir,
                            dryRun,
                            isBlockedDecision
                                ? $"blocked-sort:{reasonSegment}"
                                : $"unknown-sort:{reasonSegment}",
                            overwriteConflicts);

                        if (setMoveResult.PrimaryMoved)
                        {
                            if (isBlockedDecision)
                                blocked++;
                            else
                                unknown++;
                            setMembersMoved += setMoveResult.MembersMoved;
                            pathMutations.AddRange(setMoveResult.PathMutations);
                        }
                        else
                        {
                            failed += decisionMembers.Count + 1;
                        }
                    }
                    else
                    {
                        if (TryMoveFile(root, filePath, decisionDir, decisionFileName, dryRun, overwriteConflicts, out var actualDest))
                        {
                            if (isBlockedDecision)
                                blocked++;
                            else
                                unknown++;
                            if (actualDest is not null)
                            {
                                pathMutations.Add(new PathMutation(filePath, actualDest));
                                WriteAuditRow(root, filePath, actualDest,
                                    isBlockedDecision
                                        ? $"blocked-sort:{reasonSegment}"
                                        : $"unknown-sort:{reasonSegment}");
                            }
                        }
                        else
                        {
                            failed++;
                        }
                    }

                    continue;
                }

                // Review files → _REVIEW/{ConsoleKey}/
                if (string.Equals(sortDecision, "Review", StringComparison.OrdinalIgnoreCase))
                {
                    var reviewConsoleKey = hasKnownConsole ? consoleKey : "UNKNOWN";
                    var reviewReason = ToSafeReasonSegment(sortReason, "review");
                    var reviewDir = Path.Combine(root, RunConstants.WellKnownFolders.Review, reviewConsoleKey);
                    var reviewFileName = Path.GetFileName(filePath);
                    if (setPrimaryToMembers.TryGetValue(filePath, out var reviewMembers))
                    {
                        var setMoveResult = MoveSetAtomically(
                            root,
                            filePath,
                            reviewMembers,
                            reviewDir,
                            dryRun,
                            $"review-sort:{reviewConsoleKey}:{reviewReason}",
                            overwriteConflicts);

                        if (setMoveResult.PrimaryMoved)
                        {
                            reviewed++;
                            setMembersMoved += setMoveResult.MembersMoved;
                            pathMutations.AddRange(setMoveResult.PathMutations);
                        }
                        else
                        {
                            failed += reviewMembers.Count + 1;
                        }
                    }
                    else
                    {
                        if (TryMoveFile(root, filePath, reviewDir, reviewFileName, dryRun, overwriteConflicts, out var actualDest))
                        {
                            reviewed++;
                            if (actualDest is not null)
                            {
                                pathMutations.Add(new PathMutation(filePath, actualDest));
                                WriteAuditRow(root, filePath, actualDest, $"review-sort:{reviewConsoleKey}:{reviewReason}");
                            }
                        }
                        else
                        {
                            failed++;
                        }
                    }

                    continue;
                }

                if (!hasKnownConsole)
                {
                    unknown++;
                    IncrementReason(unknownReasons,
                        string.IsNullOrWhiteSpace(consoleKey) || string.Equals(consoleKey, "UNKNOWN", StringComparison.OrdinalIgnoreCase)
                            ? "no-match"
                            : "invalid-key");
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
                        var setMoveResult = MoveSetAtomically(
                            root,
                            filePath,
                            members,
                            expectedDir,
                            dryRun,
                            consoleKey,
                            overwriteConflicts);
                        if (setMoveResult.PrimaryMoved)
                        {
                            moved++;
                            pathMutations.AddRange(setMoveResult.PathMutations);
                        }
                        else failed += members.Count + 1;
                        setMembersMoved += setMoveResult.MembersMoved;
                    }
                }
                else
                {
                    // Standalone file — no set members
                    if (TryMoveFile(root, filePath, expectedDir, fileName, dryRun, overwriteConflicts, out var actualDest))
                    {
                        moved++;
                        if (actualDest is not null)
                        {
                            pathMutations.Add(new PathMutation(filePath, actualDest));
                            WriteAuditRow(root, filePath, actualDest, consoleKey);
                        }
                    }
                    else
                    {
                        failed++;
                    }
                }
            }
        }

        return new ConsoleSortResult(total, moved, setMembersMoved, skipped, unknown, unknownReasons, failed, reviewed, blocked, pathMutations);
    }

    /// <summary>
    /// Moves a primary file and all its set members atomically.
    /// If any member fails to move, all previously moved files are rolled back.
    /// </summary>
    private (bool PrimaryMoved, int MembersMoved, IReadOnlyList<PathMutation> PathMutations) MoveSetAtomically(
        string root,
        string primaryPath,
        List<string> members,
        string destDir,
        bool dryRun,
        string auditReasonTag,
        bool overwriteConflicts)
    {
        if (dryRun) return (true, members.Count, Array.Empty<PathMutation>());

        // Track all moves so we can roll back on partial failure
        var completedMoves = new List<(string Source, string Dest)>();

        try
        {
            // Move primary first
            var primaryDest = ResolveMoveDestination(root, primaryPath, destDir);
            if (primaryDest is null) return (false, 0, Array.Empty<PathMutation>());
            _fs.EnsureDirectory(destDir);
            var primaryActualDest = _fs.MoveItemSafely(primaryPath, primaryDest, overwriteConflicts);
            if (primaryActualDest is null)
                return (false, 0, Array.Empty<PathMutation>());
            completedMoves.Add((primaryPath, primaryActualDest));

            // Move each member
            foreach (var member in members)
            {
                var memberDest = ResolveMoveDestination(root, member, destDir);
                if (memberDest is null)
                    throw new InvalidOperationException(
                        $"Path traversal blocked for set member: {member}");

                var memberActualDest = _fs.MoveItemSafely(member, memberDest, overwriteConflicts);
                if (memberActualDest is null)
                    throw new InvalidOperationException(
                        $"Move failed for set member: {member}");
                completedMoves.Add((member, memberActualDest));
            }

            // Audit all moves in the atomic set after all succeeded
            WriteAuditRow(root, primaryPath, primaryActualDest, auditReasonTag);
            foreach (var (src, dst) in completedMoves.Skip(1)) // skip primary, already written
                WriteAuditRow(root, src, dst, auditReasonTag + ":set-member");

            return (true, members.Count, completedMoves.Select(static move => new PathMutation(move.Source, move.Dest)).ToArray());
        }
        catch (Exception ex)
        {
            // Roll back all completed moves in reverse order using safe move
            var rollbackFailures = new List<string>();
            foreach (var (source, dest) in completedMoves.AsEnumerable().Reverse())
            {
                try
                {
                    if (_fs.FileExists(dest))
                        _ = _fs.MoveItemSafely(dest, source);
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

            return (false, 0, Array.Empty<PathMutation>());
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

    private bool TryMoveFile(
        string root,
        string sourcePath,
        string destDir,
        string fileName,
        bool dryRun,
        bool overwriteConflicts,
        out string? actualDestinationPath)
    {
        if (dryRun)
        {
            actualDestinationPath = null;
            return true;
        }

        _fs.EnsureDirectory(destDir);
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedDest = Path.GetFullPath(destDir);
        var relativeDest = normalizedDest.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? normalizedDest[(normalizedRoot.Length + 1)..]
            : Path.GetFileName(destDir);
        var destPath = _fs.ResolveChildPathWithinRoot(root, Path.Combine(relativeDest, fileName));

        if (destPath is null)
        {
            actualDestinationPath = null;
            return false; // path traversal blocked
        }

        actualDestinationPath = _fs.MoveItemSafely(sourcePath, destPath, overwriteConflicts);
        return actualDestinationPath is not null;
    }

    private List<string> GetFilesForRoot(
        string root,
        IEnumerable<string>? extensions,
        IReadOnlyList<string>? candidatePaths)
    {
        if (candidatePaths is null)
        {
            return _fs.GetFilesSafe(root, extensions)
                .Where(f => !IsInExcludedFolder(f, root))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        HashSet<string>? extensionSet = null;
        if (extensions is not null)
        {
            extensionSet = extensions
                .Where(static e => !string.IsNullOrWhiteSpace(e))
                .Select(static e => e.StartsWith('.') ? e : "." + e)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        return candidatePaths
            .Where(path => IsPathWithinRoot(path, root))
            .Where(path => extensionSet is null || extensionSet.Contains(Path.GetExtension(path)))
            .Where(path => !IsInExcludedFolder(path, root))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    internal static bool IsInExcludedFolder(string filePath, string root)
    {
        var relative = filePath.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var firstSegment = relative.Split(new[] { '/', '\\' }, 2, StringSplitOptions.RemoveEmptyEntries);
        return firstSegment.Length > 0 &&
               ExcludedFolders.Any(e => e.Equals(firstSegment[0], StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsPathWithinRoot(string filePath, string root)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(root))
            return false;

        try
        {
            var normalizedRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var normalizedPath = Path.GetFullPath(filePath);
            return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    // Windows reserved device names (case-insensitive).
    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    internal static string ToSafeReasonSegment(string? reason, string fallback)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return fallback;

        var normalized = reason.Trim().ToLowerInvariant();
        normalized = RxReasonSegment.Replace(normalized, "-").Trim('-');

        if (normalized.Length == 0)
            return fallback;

        // Defense-in-depth: strip path traversal sequences
        normalized = normalized.Replace("..", "").Trim('-');
        if (normalized.Length == 0)
            return fallback;

        // Block Windows reserved device names
        if (WindowsReservedNames.Contains(normalized))
            return fallback;

        return normalized.Length > 64
            ? normalized[..64]
            : normalized;
    }

    private void WriteAuditRow(string root, string oldPath, string newPath, string reasonTag)
    {
        if (_audit is null || string.IsNullOrEmpty(_auditPath))
            return;

        _audit.AppendAuditRow(_auditPath, root, oldPath, newPath,
            "CONSOLE_SORT", "GAME", "", $"console-sort:{reasonTag}");
    }

    /// <summary>
    /// Computes a deterministic sort-reason tag from enriched candidate properties.
    /// Shared between orchestration (reason dictionary population) and any future consumers.
    /// </summary>
    internal static string BuildSortReasonTag(RomCandidate candidate)
    {
        if (candidate.DetectionConflictType == ConflictType.CrossFamily)
            return "cross-family-conflict";

        if (candidate.DetectionConflictType == ConflictType.IntraFamily)
            return "intra-family-conflict";

        if (candidate.SortDecision == SortDecision.Unknown)
            return "insufficient-evidence";

        if (candidate.SortDecision == SortDecision.Blocked && candidate.Category == FileCategory.Junk)
            return "junk-category";

        if (!string.IsNullOrWhiteSpace(candidate.MatchEvidence.Reasoning))
            return candidate.MatchEvidence.Reasoning;

        return candidate.PrimaryMatchKind != MatchKind.None
            ? candidate.PrimaryMatchKind.ToString()
            : candidate.ClassificationReasonCode;
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
