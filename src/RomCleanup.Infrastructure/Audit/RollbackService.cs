using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.FileSystem;

namespace RomCleanup.Infrastructure.Audit;

/// <summary>
/// Audit-based rollback service.
/// Lives in Infrastructure so CLI/API/WPF can share the same rollback behavior.
/// </summary>
public static class RollbackService
{
    /// <summary>
    /// Execute a rollback from the given audit CSV file.
    /// Returns a rollback result with integrity-verified statistics.
    /// </summary>
    public static AuditRollbackResult Execute(string auditPath, IReadOnlyList<string> roots, string? keyFilePath = null)
    {
        var fs = new FileSystemAdapter();
        var signingService = new AuditSigningService(fs, keyFilePath: keyFilePath ?? AuditSecurityPaths.GetDefaultSigningKeyPath());
        var rootArray = roots is string[] arr ? arr : roots.ToArray();

        // Preserve explicit integrity failure semantics used by UI/tests.
        if (File.Exists(auditPath + ".meta.json"))
        {
            try
            {
                signingService.VerifyMetadataSidecar(auditPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
            {
                return new AuditRollbackResult
                {
                    AuditCsvPath = auditPath,
                    Failed = 1,
                    DryRun = false
                };
            }
        }

        return signingService.Rollback(auditPath, rootArray, rootArray, dryRun: false);
    }

    /// <summary>
    /// TASK-175: Pre-flight trash integrity check. Runs a DryRun rollback to detect
    /// missing trash files before actual rollback. Never moves any files.
    /// </summary>
    public static AuditRollbackResult VerifyTrashIntegrity(string auditPath, IReadOnlyList<string> roots, string? keyFilePath = null)
    {
        if (!File.Exists(auditPath))
            return new AuditRollbackResult { DryRun = true };

        var fs = new FileSystemAdapter();
        var signingService = new AuditSigningService(fs, keyFilePath: keyFilePath ?? AuditSecurityPaths.GetDefaultSigningKeyPath());
        var rootArray = roots is string[] arr ? arr : roots.ToArray();

        return signingService.Rollback(auditPath, rootArray, rootArray, dryRun: true);
    }
}
