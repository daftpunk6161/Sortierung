using Romulus.Contracts;

namespace Romulus.Infrastructure.Orchestration;

/// <summary>
/// Additional execution helpers for the run pipeline.
/// Port of remaining functions from RunHelpers.Execution.ps1
/// not already covered by RunOrchestrator.
/// </summary>
public static class ExecutionHelpers
{
    public static IReadOnlySet<string> GetDiscExtensions() => DiscFormats.AllDiscExtensions;

    internal static readonly HashSet<string> DefaultBlocklist = new(StringComparer.OrdinalIgnoreCase)
    {
        Contracts.RunConstants.WellKnownFolders.TrashRegionDedupe,
        Contracts.RunConstants.WellKnownFolders.TrashJunk,
        Contracts.RunConstants.WellKnownFolders.FolderDupes,
        Contracts.RunConstants.WellKnownFolders.Ps3Dupes,
        Contracts.RunConstants.WellKnownFolders.Quarantine,
        Contracts.RunConstants.WellKnownFolders.Backup
    };

    /// <summary>
    /// Check if a path is on the blocklist (comparing directory name segments).
    /// </summary>
    public static bool IsBlocklisted(string path, IEnumerable<string>? blocklist = null)
    {
        var blocked = blocklist is not null
            ? (blocklist is HashSet<string> hs ? hs : new HashSet<string>(blocklist, StringComparer.OrdinalIgnoreCase))
            : DefaultBlocklist;
        var segments = path.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(s => blocked.Contains(s) ||
            blocked.Any(b =>
                s.StartsWith(b, StringComparison.OrdinalIgnoreCase) && s.Length > b.Length && !char.IsLetterOrDigit(s[b.Length])));
    }
}
