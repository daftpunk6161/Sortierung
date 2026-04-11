using Romulus.Contracts.Ports;
using Romulus.Contracts;

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
            RunConstants.StatusRunning => "in-progress",
            RunConstants.StatusCompleted when canRollback => "rollback-available",
            RunConstants.StatusCompleted => "not-required",
            RunConstants.StatusCompletedWithErrors when canRollback => "partial-rollback-available",
            RunConstants.StatusCompletedWithErrors => "manual-cleanup-may-be-required",
            RunConstants.StatusCancelled when canRollback => "partial-rollback-available",
            RunConstants.StatusFailed when canRollback => "partial-rollback-available",
            RunConstants.StatusCancelled or RunConstants.StatusFailed => "manual-cleanup-may-be-required",
            _ => "unknown"
        };
    }
}
