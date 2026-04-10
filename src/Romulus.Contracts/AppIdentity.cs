namespace Romulus.Contracts;

/// <summary>
/// Central application identity constants. Single source of truth for app folder name,
/// preventing hardcoded "Romulus" strings scattered across projects.
/// </summary>
public static class AppIdentity
{
    /// <summary>
    /// Application data folder name under %APPDATA%. Used for settings, profiles,
    /// audit logs, integrity baselines, trend history, and report paths.
    /// </summary>
    public const string AppFolderName = "Romulus";

    /// <summary>
    /// Canonical artifact directory names used by ArtifactPathResolver and entry points.
    /// Single source of truth — prevents scattered "reports" / "audit-logs" magic strings.
    /// </summary>
    public static class ArtifactDirectories
    {
        public const string Reports = "reports";
        public const string AuditLogs = "audit-logs";
    }
}
