using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;

namespace RomCleanup.Infrastructure.Orchestration;

/// <summary>
/// Additional execution helpers for the run pipeline.
/// Port of remaining functions from RunHelpers.Execution.ps1
/// not already covered by RunOrchestrator.
/// </summary>
public static class ExecutionHelpers
{
    /// <summary>
    /// Standard disc-based file extensions recognized by the system.
    /// Port of Get-StandaloneDiscExtensionSet.
    /// </summary>
    public static HashSet<string> GetDiscExtensions() => new(StringComparer.OrdinalIgnoreCase)
    {
        ".chd", ".iso", ".cue", ".bin", ".img", ".mdf", ".mds",
        ".ccd", ".sub", ".gdi", ".cso", ".pbp", ".rvz", ".gcz",
        ".wbfs", ".nrg", ".ecm", ".zip", ".7z"
    };

    /// <summary>
    /// Default blocklist of paths that should never be processed.
    /// </summary>
    public static HashSet<string> GetDefaultBlocklist() => new(StringComparer.OrdinalIgnoreCase)
    {
        "_TRASH_REGION_DEDUPE",
        "_FOLDER_DUPES",
        "PS3_DUPES",
        "_QUARANTINE",
        "_BACKUP"
    };

    /// <summary>
    /// Check if a path is on the blocklist (comparing directory name segments).
    /// </summary>
    public static bool IsBlocklisted(string path, IEnumerable<string>? blocklist = null)
    {
        var blocked = blocklist ?? GetDefaultBlocklist();
        var segments = path.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(s => blocked.Contains(s));
    }

    /// <summary>
    /// Build a unique audit filename with root path hash appended (BUG RUN-013).
    /// </summary>
    public static string BuildAuditFileName(string baseName, IReadOnlyList<string> roots)
    {
        if (roots.Count == 0) return baseName;

        var rootHash = roots.Count == 1
            ? Math.Abs(roots[0].GetHashCode()).ToString("X8")
            : Math.Abs(string.Join("|", roots).GetHashCode()).ToString("X8");

        var ext = Path.GetExtension(baseName);
        var name = Path.GetFileNameWithoutExtension(baseName);
        return $"{name}_{rootHash}{ext}";
    }

    /// <summary>
    /// Scan roots for standalone disc files eligible for conversion.
    /// Port of Get-StandaloneConversionPreview.
    /// </summary>
    public static StandaloneConversionPreview GetConversionPreview(
        IFileSystem fs,
        IReadOnlyList<string> roots,
        IReadOnlyList<string>? allowedRoots = null,
        int previewLimit = 12)
    {
        var discExts = GetDiscExtensions();
        var preview = new List<string>();
        int candidateCount = 0;
        var acceptedRoots = new List<string>();
        var blockedRoots = new List<string>();
        var scannedByRoot = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            if (allowedRoots is not null && !allowedRoots.Any(r =>
                root.StartsWith(r, StringComparison.OrdinalIgnoreCase)))
            {
                blockedRoots.Add(root);
                continue;
            }

            acceptedRoots.Add(root);
            var files = fs.GetFilesSafe(root, discExts);
            scannedByRoot[root] = files.Count;
            candidateCount += files.Count;

            foreach (var file in files)
            {
                if (preview.Count < previewLimit)
                    preview.Add(file);
            }
        }

        return new StandaloneConversionPreview
        {
            CandidateCount = candidateCount,
            PreviewItems = preview,
            AcceptedRoots = acceptedRoots,
            BlockedRoots = blockedRoots,
            ScannedFilesByRoot = scannedByRoot
        };
    }
}

/// <summary>
/// Preview of standalone conversion candidates.
/// </summary>
public sealed class StandaloneConversionPreview
{
    public int CandidateCount { get; init; }
    public IReadOnlyList<string> PreviewItems { get; init; } = [];
    public IReadOnlyList<string> AcceptedRoots { get; init; } = [];
    public IReadOnlyList<string> BlockedRoots { get; init; } = [];
    public IReadOnlyDictionary<string, int> ScannedFilesByRoot { get; init; } = new Dictionary<string, int>();
}
