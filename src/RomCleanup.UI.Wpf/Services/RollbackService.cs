using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.Audit;
using RomCleanup.Infrastructure.FileSystem;

namespace RomCleanup.UI.Wpf.Services;

/// <summary>
/// Extracted from MainWindow.xaml.cs — handles audit-based rollback.
/// RF-004 from gui-ux-deep-audit.md.
/// Uses AuditSigningService for HMAC-verified rollback (Issue #13).
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
        var signing = new AuditSigningService(fs, keyFilePath: keyFilePath ?? AuditSecurityPaths.GetDefaultSigningKeyPath());
        var rootArray = roots is string[] arr ? arr : roots.ToArray();
        return signing.Rollback(auditPath, rootArray, rootArray, dryRun: false);
    }
}
