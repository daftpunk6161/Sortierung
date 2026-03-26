namespace RomCleanup.Infrastructure.Audit;

/// <summary>
/// Centralizes persistent paths for audit integrity artifacts.
/// </summary>
public static class AuditSecurityPaths
{
    public static string GetDefaultSigningKeyPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, Contracts.AppIdentity.AppFolderName, "security", "audit-signing.key");
    }

    public static string GetDefaultAuditDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, Contracts.AppIdentity.AppFolderName, "audit");
    }

    public static string GetDefaultReportDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, Contracts.AppIdentity.AppFolderName, "reports");
    }
}