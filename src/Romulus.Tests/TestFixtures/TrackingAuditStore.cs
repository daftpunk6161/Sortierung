using Romulus.Contracts.Ports;

namespace Romulus.Tests.TestFixtures;

/// <summary>
/// In-memory IAuditStore that records all appended rows for test assertions.
/// </summary>
internal sealed class TrackingAuditStore : IAuditStore
{
    public List<AuditRow> Rows { get; } = [];
    public List<string> FlushedPaths { get; } = [];
    public Dictionary<string, IDictionary<string, object>> Sidecars { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void AppendAuditRow(string auditCsvPath, string rootPath, string oldPath,
        string newPath, string action, string category = "", string hash = "", string reason = "")
    {
        Rows.Add(new AuditRow(auditCsvPath, rootPath, oldPath, newPath, action, category, hash, reason));
    }

    public void Flush(string auditCsvPath) => FlushedPaths.Add(auditCsvPath);

    public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata)
        => Sidecars[auditCsvPath] = metadata;

    public bool TestMetadataSidecar(string auditCsvPath) => Sidecars.ContainsKey(auditCsvPath);

    public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots,
        string[] allowedCurrentRoots, bool dryRun = false) => [];

    public sealed record AuditRow(string AuditPath, string Root, string OldPath,
        string NewPath, string Action, string Category, string Hash, string Reason);
}
