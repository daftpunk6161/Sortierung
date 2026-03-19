using System.IO;
using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.FileSystem;
using RomCleanup.Infrastructure.Audit;

namespace RomCleanup.UI.Wpf.Services;

/// <summary>
/// Extracted from MainWindow.xaml.cs — handles audit-based rollback.
/// RF-004 from gui-ux-deep-audit.md.
/// Uses AuditCsvStore as the single rollback entry point.
/// </summary>
public static class RollbackService
{
    /// <summary>
    /// Execute a rollback from the given audit CSV file.
    /// Must be called from a background thread (performs file I/O).
    /// Returns the rollback result with integrity-verified statistics.
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
            catch
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
}
