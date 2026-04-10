using Romulus.Contracts.Models;

namespace Romulus.Infrastructure.Audit;

internal static class AuditRollbackRootMetadata
{
    internal static IDictionary<string, object> WithAllowedRoots(
        RunOptions options,
        IDictionary<string, object> metadata)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(metadata);

        var restoreRoots = options.Roots
            .Where(static root => !string.IsNullOrWhiteSpace(root))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static root => root, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var currentRoots = restoreRoots
            .Concat(string.IsNullOrWhiteSpace(options.TrashRoot)
                ? Array.Empty<string>()
                : new[] { Path.GetFullPath(options.TrashRoot) })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static root => root, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        metadata["AllowedRestoreRoots"] = restoreRoots;
        metadata["AllowedCurrentRoots"] = currentRoots;
        return metadata;
    }
}
