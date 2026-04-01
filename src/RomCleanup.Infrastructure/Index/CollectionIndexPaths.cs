using RomCleanup.Contracts;

namespace RomCleanup.Infrastructure.Index;

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
    /// Uses portable mode when a `.portable` marker exists next to the executable.
    /// Otherwise stores the database in the application's roaming AppData folder.
    /// </summary>
    public static string ResolveDefaultDatabasePath()
    {
        var baseDir = File.Exists(Path.Combine(AppContext.BaseDirectory, ".portable"))
            ? Path.Combine(AppContext.BaseDirectory, ".romcleanup")
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppIdentity.AppFolderName);

        return Path.Combine(baseDir, "collection.db");
    }
}
