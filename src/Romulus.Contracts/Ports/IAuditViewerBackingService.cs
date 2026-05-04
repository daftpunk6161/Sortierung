using System;
using System.Collections.Generic;

namespace Romulus.Contracts.Ports;

/// <summary>
/// Wave 2 — T-W2-AUDIT-VIEWER-API.
/// Read-only port over the existing audit CSV / sidecar / ledger surface.
/// Used by GUI/CLI/API to browse audit history without ever touching the
/// write paths (<see cref="IAuditStore"/>). The adapter MUST reuse
/// <c>AuditCsvParser</c> + <c>AuditCsvStore</c> + <c>AuditSigningService</c>;
/// it must not duplicate signing or parsing logic.
///
/// <para>
/// Single source of truth: every consumer (GUI Audit-Viewer in T-W4,
/// CLI <c>audit list</c>, API <c>GET /v1-experimental/audit/runs</c>) reads through this
/// port. Reports project these rows; they do not re-parse CSVs.
/// </para>
/// </summary>
public interface IAuditViewerBackingService
{
    /// <summary>
    /// Lists the audit runs discovered under <paramref name="auditRoot"/>
    /// (one entry per audit CSV file). Deterministic order: most recent
    /// <see cref="AuditRunSummary.LastModifiedUtc"/> first, then by file name.
    /// </summary>
    IReadOnlyList<AuditRunSummary> ListRuns(
        string auditRoot,
        AuditRunFilter? filter = null,
        AuditPage? page = null);

    /// <summary>
    /// Reads the rows of a single audit run with pagination + filter.
    /// Filter is applied server-side, pagination after filtering.
    /// </summary>
    AuditRowPage ReadRunRows(
        string auditCsvPath,
        AuditRunFilter? filter = null,
        AuditPage? page = null);

    /// <summary>
    /// Reads the signed metadata sidecar for a single audit CSV. Returns
    /// <c>null</c> when no sidecar exists. Verification status is exposed
    /// via <see cref="AuditSidecarInfo.IsSignatureValid"/> so the consumer
    /// does not re-verify.
    /// </summary>
    AuditSidecarInfo? ReadSidecar(string auditCsvPath);
}

/// <summary>Pagination request. <see cref="PageSize"/> default = 100.</summary>
public sealed record AuditPage(int PageIndex = 0, int PageSize = 100);

/// <summary>
/// Server-side filter applied before pagination. All filters are
/// AND-composed. <c>null</c> fields disable that filter dimension.
/// </summary>
public sealed record AuditRunFilter(
    string? RunId = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    string? Outcome = null);

/// <summary>One audit run (one CSV file) under the audit root.</summary>
public sealed record AuditRunSummary(
    string AuditCsvPath,
    string FileName,
    string? RunId,
    DateTimeOffset LastModifiedUtc,
    long FileSizeBytes,
    int RowCount,
    bool HasSidecar,
    bool IsSidecarValid);

/// <summary>Single audit row projection (read-only).</summary>
public sealed record AuditRowView(
    int LineNumber,
    string RootPath,
    string OldPath,
    string NewPath,
    string Action,
    string Category,
    string Hash,
    string Reason,
    string Timestamp);

/// <summary>Page of audit rows after filter + pagination.</summary>
public sealed record AuditRowPage(
    IReadOnlyList<AuditRowView> Rows,
    int TotalRowCount,
    int FilteredRowCount,
    int PageIndex,
    int PageSize);

/// <summary>Read-only view of a sidecar file.</summary>
public sealed record AuditSidecarInfo(
    string SidecarPath,
    int DeclaredRowCount,
    bool IsSignatureValid,
    IReadOnlyDictionary<string, string> Metadata);
