using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.Audit;

/// <summary>
/// Central rollback-availability and recovery-state policy.
/// Keeps API and GUI aligned on verified-sidecar semantics.
/// </summary>
public static class AuditRecoveryStateResolver
{
    public static bool HasVerifiedRollback(IAuditStore auditStore, string? auditPath)
    {
        ArgumentNullException.ThrowIfNull(auditStore);

        if (string.IsNullOrWhiteSpace(auditPath) || !File.Exists(auditPath))
            return false;

        try
        {
            return auditStore.TestMetadataSidecar(auditPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    public static string ResolveRecoveryState(string status, bool canRollback)
    {
        var normalizedStatus = status?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalizedStatus switch
        {
            "running" => "in-progress",
            "completed" when canRollback => "rollback-available",
            "completed" => "not-required",
            "completed_with_errors" when canRollback => "partial-rollback-available",
            "completed_with_errors" => "manual-cleanup-may-be-required",
            "cancelled" when canRollback => "partial-rollback-available",
            "failed" when canRollback => "partial-rollback-available",
            "cancelled" or "failed" => "manual-cleanup-may-be-required",
            _ => "unknown"
        };
    }
}
