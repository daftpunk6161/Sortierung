using Romulus.Contracts.Models;

namespace Romulus.Contracts.Ports;

/// <summary>
/// Port interface for audit trail and rollback operations.
/// Maps to New-AuditStorePort in PortInterfaces.ps1.
/// </summary>
public interface IAuditStore
{
    void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata);
    bool TestMetadataSidecar(string auditCsvPath);
    /// <summary>Flush any buffered audit rows to disk.</summary>
    void Flush(string auditCsvPath);
    IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots, string[] allowedCurrentRoots, bool dryRun = false);
    void AppendAuditRow(string auditCsvPath, string rootPath, string oldPath,
        string newPath, string action, string category = "", string hash = "", string reason = "");
    void AppendAuditRows(string auditCsvPath, IReadOnlyList<AuditAppendRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        foreach (var row in rows)
        {
            AppendAuditRow(
                auditCsvPath,
                row.RootPath,
                row.OldPath,
                row.NewPath,
                row.Action,
                row.Category,
                row.Hash,
                row.Reason);
        }
    }
}
