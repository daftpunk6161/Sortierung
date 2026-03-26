namespace RomCleanup.Contracts;

/// <summary>
/// Central application identity constants. Single source of truth for app folder name,
/// preventing hardcoded "RomCleanupRegionDedupe" strings scattered across projects.
/// </summary>
public static class AppIdentity
{
    /// <summary>
    /// Application data folder name under %APPDATA%. Used for settings, profiles,
    /// audit logs, integrity baselines, trend history, and report paths.
    /// </summary>
    public const string AppFolderName = "RomCleanupRegionDedupe";
}
