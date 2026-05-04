using Romulus.Contracts.Errors;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.Safety;

public partial class Program
{
    internal static void MapAuditViewerEndpoints(WebApplication app)
    {
        app.MapGet("/audit/runs", (
            string? auditRoot,
            string? runId,
            DateTimeOffset? fromUtc,
            DateTimeOffset? toUtc,
            int? pageIndex,
            int? pageSize,
            IAuditViewerBackingService auditViewer,
            AllowedRootPathPolicy allowedRootPolicy) =>
        {
            var rootResult = ResolveAuditViewerRoot(auditRoot, allowedRootPolicy);
            if (rootResult.Error is not null)
                return rootResult.Error;

            var page = NormalizeAuditPage(pageIndex, pageSize);
            var filter = new AuditRunFilter(RunId: NormalizeQueryValue(runId), FromUtc: fromUtc, ToUtc: toUtc);
            var allRuns = auditViewer.ListRuns(rootResult.Root, filter);
            var runs = allRuns
                .Skip(page.PageIndex * page.PageSize)
                .Take(page.PageSize)
                .ToArray();

            return Results.Ok(new AuditRunsResponse(
                AuditRoot: rootResult.Root,
                Total: allRuns.Count,
                Returned: runs.Length,
                PageIndex: page.PageIndex,
                PageSize: page.PageSize,
                Runs: runs));
        })
            .WithSummary("List audit runs through the read-only audit viewer backing service")
            .WithTags("Audit")
            .Produces<AuditRunsResponse>(StatusCodes.Status200OK)
            .Produces<OperationErrorResponse>(StatusCodes.Status400BadRequest);

        app.MapGet("/audit/runs/{id}/rows", (
            string id,
            string? auditRoot,
            string? outcome,
            DateTimeOffset? fromUtc,
            DateTimeOffset? toUtc,
            int? pageIndex,
            int? pageSize,
            IAuditViewerBackingService auditViewer,
            AllowedRootPathPolicy allowedRootPolicy) =>
        {
            var resolved = ResolveAuditRun(id, auditRoot, auditViewer, allowedRootPolicy);
            if (resolved.Error is not null)
                return resolved.Error;

            var page = NormalizeAuditPage(pageIndex, pageSize);
            var filter = new AuditRunFilter(
                FromUtc: fromUtc,
                ToUtc: toUtc,
                Outcome: NormalizeQueryValue(outcome));
            var rows = auditViewer.ReadRunRows(resolved.Run!.AuditCsvPath, filter, page);
            return Results.Ok(new AuditRunRowsResponse(resolved.Run, rows));
        })
            .WithSummary("Read audit rows for one audit run")
            .WithTags("Audit")
            .Produces<AuditRunRowsResponse>(StatusCodes.Status200OK)
            .Produces<OperationErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<OperationErrorResponse>(StatusCodes.Status404NotFound);

        app.MapGet("/audit/runs/{id}/sidecar", (
            string id,
            string? auditRoot,
            IAuditViewerBackingService auditViewer,
            AllowedRootPathPolicy allowedRootPolicy) =>
        {
            var resolved = ResolveAuditRun(id, auditRoot, auditViewer, allowedRootPolicy);
            if (resolved.Error is not null)
                return resolved.Error;

            var sidecar = auditViewer.ReadSidecar(resolved.Run!.AuditCsvPath);
            return Results.Ok(new AuditRunSidecarResponse(
                Run: resolved.Run,
                HasSidecar: sidecar is not null,
                Sidecar: sidecar));
        })
            .WithSummary("Read signed audit sidecar metadata for one audit run")
            .WithTags("Audit")
            .Produces<AuditRunSidecarResponse>(StatusCodes.Status200OK)
            .Produces<OperationErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<OperationErrorResponse>(StatusCodes.Status404NotFound);

        app.MapGet("/audit/runs/{id}/verification", (
            string id,
            string? auditRoot,
            IAuditViewerBackingService auditViewer,
            AllowedRootPathPolicy allowedRootPolicy) =>
        {
            var resolved = ResolveAuditRun(id, auditRoot, auditViewer, allowedRootPolicy);
            if (resolved.Error is not null)
                return resolved.Error;

            var sidecar = auditViewer.ReadSidecar(resolved.Run!.AuditCsvPath);
            var status = sidecar is null
                ? "missing-sidecar"
                : sidecar.IsSignatureValid ? "valid" : "invalid";

            return Results.Ok(new AuditRunVerificationResponse(
                Run: resolved.Run,
                Status: status,
                HasSidecar: sidecar is not null,
                IsSignatureValid: sidecar?.IsSignatureValid ?? false,
                DeclaredRowCount: sidecar?.DeclaredRowCount,
                ActualRowCount: resolved.Run.RowCount));
        })
            .WithSummary("Read audit sidecar verification status for one audit run")
            .WithTags("Audit")
            .Produces<AuditRunVerificationResponse>(StatusCodes.Status200OK)
            .Produces<OperationErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<OperationErrorResponse>(StatusCodes.Status404NotFound);
    }

    private static (string Root, IResult? Error) ResolveAuditViewerRoot(
        string? auditRoot,
        AllowedRootPathPolicy allowedRootPolicy)
    {
        var root = string.IsNullOrWhiteSpace(auditRoot)
            ? AuditSecurityPaths.GetDefaultAuditDirectory()
            : auditRoot.Trim();

        var pathError = ValidatePathSecurity(root, "auditRoot", allowedRootPolicy);
        if (pathError is not null)
            return (root, pathError);

        return (Path.GetFullPath(root), null);
    }

    private static (AuditRunSummary? Run, IResult? Error) ResolveAuditRun(
        string id,
        string? auditRoot,
        IAuditViewerBackingService auditViewer,
        AllowedRootPathPolicy allowedRootPolicy)
    {
        if (string.IsNullOrWhiteSpace(id))
            return (null, ApiError(400, ApiErrorCodes.RunInvalidId, "Audit run id is required."));

        var rootResult = ResolveAuditViewerRoot(auditRoot, allowedRootPolicy);
        if (rootResult.Error is not null)
            return (null, rootResult.Error);

        var runs = auditViewer.ListRuns(rootResult.Root);
        var run = runs.FirstOrDefault(summary =>
            string.Equals(summary.RunId, id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(summary.FileName, id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileNameWithoutExtension(summary.FileName), id, StringComparison.OrdinalIgnoreCase));

        return run is null
            ? (null, ApiError(404, ApiErrorCodes.RunNotFound, "Audit run not found."))
            : (run, null);
    }

    private static AuditPage NormalizeAuditPage(int? pageIndex, int? pageSize)
    {
        var normalizedPageIndex = Math.Max(0, pageIndex ?? 0);
        var normalizedPageSize = Math.Clamp(pageSize ?? 100, 1, 1000);
        return new AuditPage(normalizedPageIndex, normalizedPageSize);
    }

    private static string? NormalizeQueryValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record AuditRunsResponse(
    string AuditRoot,
    int Total,
    int Returned,
    int PageIndex,
    int PageSize,
    IReadOnlyList<AuditRunSummary> Runs);

public sealed record AuditRunRowsResponse(AuditRunSummary Run, AuditRowPage Rows);

public sealed record AuditRunSidecarResponse(
    AuditRunSummary Run,
    bool HasSidecar,
    AuditSidecarInfo? Sidecar);

public sealed record AuditRunVerificationResponse(
    AuditRunSummary Run,
    string Status,
    bool HasSidecar,
    bool IsSignatureValid,
    int? DeclaredRowCount,
    int ActualRowCount);
