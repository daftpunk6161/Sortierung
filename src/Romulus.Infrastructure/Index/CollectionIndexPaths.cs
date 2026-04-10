using Romulus.Contracts;
using Romulus.Infrastructure.Paths;

namespace Romulus.Infrastructure.Index;

/// <summary>
/// Central path resolver for persisted collection index artifacts.
/// Keeps storage-path decisions out of contracts and callers.
/// </summary>
public static class CollectionIndexPaths
{
    public static string ResolveDatabasePath(string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
            return Path.GetFullPath(overridePath);

        return ResolveDefaultDatabasePath();
    }

    /// <summary>
    /// Resolve the default index database path.
    /// Uses centralized storage resolution for portable vs standard app data mode.
    /// </summary>
    public static string ResolveDefaultDatabasePath()
    {
        return AppStoragePathResolver.ResolveRoamingPath("collection.db");
    }
}
