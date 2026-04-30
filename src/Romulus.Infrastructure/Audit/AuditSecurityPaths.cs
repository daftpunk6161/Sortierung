namespace Romulus.Infrastructure.Audit;

using Romulus.Infrastructure.Paths;

/// <summary>
/// Centralizes persistent paths for audit integrity artifacts.
/// </summary>
public static class AuditSecurityPaths
{
    public static string GetDefaultSigningKeyPath()
    {
        return AppStoragePathResolver.ResolveRoamingPath("security", "audit-signing.key");
    }

    public static string GetDefaultAuditDirectory()
    {
        return AppStoragePathResolver.ResolveRoamingPath("audit");
    }

    public static string GetDefaultReportDirectory()
    {
        return AppStoragePathResolver.ResolveRoamingPath("reports");
    }

    /// <summary>
    /// Wave 7 — T-W7-PROVENANCE-TRAIL. Standard-Wurzel des per-ROM Provenance-Trails.
    /// Liegt unter %APPDATA%\Romulus\provenance, damit der Trail Run-uebergreifend
    /// persistiert. Layout pro Datei wird in <see cref="Provenance.JsonlProvenanceStore"/> gehalten.
    /// </summary>
    public static string GetDefaultProvenanceRoot()
    {
        return AppStoragePathResolver.ResolveRoamingPath("provenance");
    }
}