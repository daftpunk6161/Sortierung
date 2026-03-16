namespace RomCleanup.Infrastructure.Audit;

/// <summary>
/// Centralizes persistent paths for audit integrity artifacts.
/// </summary>
public static class AuditSecurityPaths
{
    public static string GetDefaultSigningKeyPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "RomCleanupRegionDedupe", "security", "audit-signing.key");
    }
}