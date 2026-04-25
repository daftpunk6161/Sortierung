using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Romulus.Api;
using Romulus.Contracts;
using Romulus.Contracts.Errors;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Analysis;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.Index;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Profiles;
using Romulus.Infrastructure.Review;
using Romulus.Infrastructure.Safety;

public partial class Program
{
    internal static void MapRunReadEndpoints(WebApplication app, bool trustForwardedFor)
    {
        app.MapGet("/runs", (HttpContext ctx, string? offset, string? limit, RunLifecycleManager mgr) =>
        {
            var parsedOffset = 0;
            if (!string.IsNullOrWhiteSpace(offset))
            {
                if (!int.TryParse(offset, out parsedOffset) || parsedOffset < 0)
                    return ApiError(400, ApiErrorCodes.RunInvalidOffset, "offset must be a non-negative integer.");
            }

            int? parsedLimit = null;
            if (!string.IsNullOrWhiteSpace(limit))
            {
                if (!int.TryParse(limit, out var limitValue) || limitValue < 1 || limitValue > 1000)
                    return ApiError(400, ApiErrorCodes.RunInvalidLimit, "limit must be an integer between 1 and 1000.");
                parsedLimit = limitValue;
            }

            var requesterClientId = GetClientBindingId(ctx, trustForwardedFor);
            var visibleRuns = mgr.List()
                .Where(run => CanAccessRun(run, requesterClientId))
                .ToArray();

            return Results.Ok(BuildRunList(visibleRuns, parsedOffset, parsedLimit));
        })
            .WithSummary("List visible run history for the current client binding")
            .Produces<ApiRunList>(StatusCodes.Status200OK)
            .Produces<OperationErrorResponse>(StatusCodes.Status400BadRequest);

        app.MapGet("/runs/history", async (HttpContext ctx, string? offset, string? limit, ICollectionIndex collectionIndex, CancellationToken ct) =>
        {
            var parsedOffset = 0;
            if (!string.IsNullOrWhiteSpace(offset))
            {
                if (!int.TryParse(offset, out parsedOffset) || parsedOffset < 0)
                    return ApiError(400, ApiErrorCodes.RunInvalidOffset, "offset must be a non-negative integer.");
            }

            int? parsedLimit = null;
            if (!string.IsNullOrWhiteSpace(limit))
            {
                if (!int.TryParse(limit, out var limitValue) || limitValue < 1 || limitValue > 1000)
                    return ApiError(400, ApiErrorCodes.RunInvalidLimit, "limit must be an integer between 1 and 1000.");
                parsedLimit = limitValue;
            }

            var effectiveLimit = CollectionRunHistoryPageBuilder.NormalizeLimit(parsedLimit);
            var requesterClientId = GetClientBindingId(ctx, trustForwardedFor);
            var snapshots = await collectionIndex.ListRunSnapshotsAsync(int.MaxValue, ct);
            var visibleSnapshots = snapshots
                .Where(snapshot => CanAccessSnapshot(snapshot, requesterClientId))
                .ToArray();

            return Results.Ok(BuildRunHistoryList(
                CollectionRunHistoryPageBuilder.Build(
                    visibleSnapshots,
                    visibleSnapshots.Length,
                    parsedOffset,
                    effectiveLimit)));
        })
            .WithSummary("List persisted run history snapshots from the collection index")
            .Produces<ApiRunHistoryList>(StatusCodes.Status200OK)
            .Produces<OperationErrorResponse>(StatusCodes.Status400BadRequest);

        app.MapGet("/runs/compare", async (string runId, string compareToRunId, HttpContext ctx, ICollectionIndex collectionIndex, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(compareToRunId))
                return ApiError(400, ApiErrorCodes.RunCompareIdsRequired, "runId and compareToRunId are required.");

            var requesterClientId = GetClientBindingId(ctx, trustForwardedFor);
            var snapshots = await collectionIndex.ListRunSnapshotsAsync(3650, ct);
            var visibleSnapshots = snapshots
                .Where(snapshot => CanAccessSnapshot(snapshot, requesterClientId))
                .ToArray();
            var comparison = RunHistoryInsightsService.Compare(visibleSnapshots, runId, compareToRunId);

            return comparison is null
                ? ApiError(404, ApiErrorCodes.RunCompareNotFound, "One or both run snapshots were not found.")
                : Results.Ok(comparison);
        })
            .WithSummary("Compare two persisted run snapshots")
            .Produces<RunSnapshotComparison>(StatusCodes.Status200OK)
            .Produces<OperationErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<OperationErrorResponse>(StatusCodes.Status404NotFound);

        app.MapGet("/runs/trends", async (HttpContext ctx, int? limit, ICollectionIndex collectionIndex, CancellationToken ct) =>
        {
            var requesterClientId = GetClientBindingId(ctx, trustForwardedFor);
            var snapshots = await collectionIndex.ListRunSnapshotsAsync(int.MaxValue, ct);
            var visibleSnapshots = snapshots
                .Where(snapshot => CanAccessSnapshot(snapshot, requesterClientId))
                .ToArray();
            var report = RunHistoryInsightsService.BuildStorageInsights(visibleSnapshots, limit ?? 30);
            return Results.Ok(report);
        })
            .WithSummary("Build storage and trend insights from persisted run history")
            .Produces<StorageInsightReport>(StatusCodes.Status200OK);
    }

    internal static void MapRunCompletenessEndpoints(WebApplication app, bool trustForwardedFor)
    {
        app.MapGet("/runs/{runId}/completeness",
            (string runId, HttpContext ctx, RunLifecycleManager mgr, IRunEnvironmentFactory runEnvironmentFactory, AllowedRootPathPolicy allowedRootPolicy, CancellationToken ct)
                => HandleRunCompletenessAsync(runId, ctx, mgr, runEnvironmentFactory, allowedRootPolicy, trustForwardedFor, ct));

        app.MapPost("/runs/{runId}/fixdat", (
            string runId,
            [FromQuery] string? outputPath,
            [FromQuery] string? name,
            HttpContext ctx,
            RunLifecycleManager mgr,
            IRunEnvironmentFactory runEnvironmentFactory,
            AllowedRootPathPolicy allowedRootPolicy,
            CancellationToken ct)
                => HandleRunFixDatAsync(runId, outputPath, name, ctx, mgr, runEnvironmentFactory, allowedRootPolicy, trustForwardedFor, ct))
            .WithSummary("Generate a FixDAT from run completeness and persist it to disk")
            .Produces(StatusCodes.Status200OK)
            .Produces<OperationErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<OperationErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<OperationErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<OperationErrorResponse>(StatusCodes.Status409Conflict);
    }

    internal static void MapRunWatchEndpoints(
        WebApplication app,
        bool trustForwardedFor,
        ITimeProvider timeProvider,
        int sseTimeoutSeconds,
        int sseHeartbeatSeconds)
    {
        app.MapPost("/runs", async (
            HttpContext ctx,
            string? wait,
            [FromQuery(Name = "waitTimeoutMs")] string? waitTimeoutMsQuery,
            RunLifecycleManager mgr,
            RunConfigurationMaterializer runConfigurationMaterializer,
            AllowedRootPathPolicy allowedRootPolicy) =>
        {
            // Validate Content-Type
            var contentType = ctx.Request.ContentType;
            if (contentType is null || !contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                return ApiError(400, ApiErrorCodes.RunInvalidContentType, "Content-Type must be application/json.");
        
            // Read and validate body (max 1MB)
            ctx.Request.EnableBuffering();
            if (ctx.Request.ContentLength is > 1_048_576)
                return ApiError(400, ApiErrorCodes.RunBodyTooLarge, "Request body too large (max 1MB).", ErrorKind.Transient);
        
            string body;
            using (var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8, leaveOpen: true))
            {
                // Limit read to 1MB + 1 byte to detect oversized chunked bodies
                var buffer = new char[1_048_577];
                var charsRead = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
                if (charsRead > 1_048_576)
                    return ApiError(400, ApiErrorCodes.RunBodyTooLarge, "Request body too large (max 1MB).", ErrorKind.Transient);
                body = new string(buffer, 0, charsRead);
            }
        
            RunRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<RunRequest>(body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException)
            {
                return ApiError(400, ApiErrorCodes.RunInvalidJson, "Invalid JSON.");
            }
        
            if (request is null)
                return ApiError(400, ApiErrorCodes.RunInvalidJson, "Invalid JSON.");
        
            ApiResolvedRunConfiguration resolvedRunRequest;
            try
            {
                using var requestDocument = JsonDocument.Parse(body);
                var dataDir = RunEnvironmentBuilder.TryResolveDataDir()
                    ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
                var settings = RunEnvironmentBuilder.LoadSettings(dataDir);
                resolvedRunRequest = await ApiRunConfigurationMapper.ResolveAsync(
                    request,
                    requestDocument.RootElement,
                    settings,
                    runConfigurationMaterializer,
                    ctx.RequestAborted);
                request = resolvedRunRequest.Request;
            }
            catch (ConfigurationValidationException ex)
            {
                var (code, message) = MapConfigurationError(ex, "RUN");
                return ApiError(400, code, message);
            }
            catch (InvalidOperationException ex)
            {
                SafeConsoleWriteLine($"[API-WARN] Run configuration rejected: {ex.GetType().Name}");
                return ApiError(400, ApiErrorCodes.RunInvalidConfig, "Run configuration is invalid.");
            }
        
            if (request.Roots is null || request.Roots.Length == 0)
                return ApiError(400, ApiErrorCodes.RunRootsRequired, "roots[] is required.");
        
            // Validate roots
            foreach (var root in request.Roots)
            {
                if (string.IsNullOrWhiteSpace(root))
                    return ApiError(400, ApiErrorCodes.RunRootEmpty, "Empty root path.");
                if (!Directory.Exists(root))
                    return ApiError(400, ApiErrorCodes.IoRootNotFound, $"Root not found: {root}");
        
                var pathError = ValidateRootSecurity(root, allowedRootPolicy);
                if (pathError is not null)
                    return pathError;
            }

            request.Roots = request.Roots
                .Select(static root => root.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        
            var mode = request.Mode ?? "DryRun";
            if (!mode.Equals("DryRun", StringComparison.OrdinalIgnoreCase) &&
                !mode.Equals("Move", StringComparison.OrdinalIgnoreCase))
                return ApiError(400, ApiErrorCodes.RunInvalidMode, "mode must be DryRun or Move.");
        
            // Normalize to canonical casing
            mode = mode.Equals("Move", StringComparison.OrdinalIgnoreCase) ? "Move" : "DryRun";
        
            var idempotencyKey = ctx.Request.Headers["X-Idempotency-Key"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                if (idempotencyKey.Length > 128 ||
                    !idempotencyKey.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.'))
                {
                    return ApiError(400, ApiErrorCodes.RunInvalidIdempotencyKey, "Invalid X-Idempotency-Key. Use max 128 chars from [A-Za-z0-9-_.].");
                }
            }
        
            // TASK-200: Validate PreferRegions to prevent injection
            if (request.PreferRegions is { Length: > 0 })
            {
                // SEC-API-01: Limit array length to prevent abuse
                if (request.PreferRegions.Length > Romulus.Contracts.RunConstants.MaxPreferRegions)
                    return ApiError(400, ApiErrorCodes.RunTooManyRegions, $"PreferRegions must contain at most {Romulus.Contracts.RunConstants.MaxPreferRegions} entries.");
        
                foreach (var region in request.PreferRegions)
                {
                    if (string.IsNullOrWhiteSpace(region) || region.Length > 10 ||
                        !region.All(c => char.IsLetterOrDigit(c) || c == '-'))
                        return ApiError(400, ApiErrorCodes.RunInvalidRegion, $"Invalid region: '{region}'. Only alphanumeric and '-' allowed.");
                }
            }
        
            // Validate hash type
            if (!string.IsNullOrWhiteSpace(request.HashType))
            {
                var hashType = request.HashType.Trim().ToUpperInvariant();
                if (hashType is not "SHA1" and not "SHA256" and not "MD5")
                    return ApiError(400, ApiErrorCodes.RunInvalidHashType, "hashType must be one of: SHA1, SHA256, MD5.");
            }
        
            // Validate extensions
            if (request.Extensions is { Length: > 0 })
            {
                foreach (var extension in request.Extensions)
                {
                    if (string.IsNullOrWhiteSpace(extension))
                        return ApiError(400, ApiErrorCodes.RunInvalidExtension, "extensions must not contain empty values.");
        
                    var normalized = extension.Trim();
                    if (!normalized.StartsWith('.'))
                        normalized = "." + normalized;
        
                    if (normalized.Length < 2 || normalized.Length > 20 ||
                        !normalized.Skip(1).All(ch => char.IsLetterOrDigit(ch)))
                    {
                        return ApiError(400, ApiErrorCodes.RunInvalidExtension, $"Invalid extension '{extension}'. Use alphanumeric values like .chd, .iso, .zip.");
                    }
                }
            }
        
            // Validate conflict policy
            if (!string.IsNullOrWhiteSpace(request.ConflictPolicy))
            {
                var normalizedPolicy = request.ConflictPolicy.Trim();
                if (normalizedPolicy.Equals("rename", StringComparison.OrdinalIgnoreCase))
                    request.ConflictPolicy = "Rename";
                else if (normalizedPolicy.Equals("skip", StringComparison.OrdinalIgnoreCase))
                    request.ConflictPolicy = "Skip";
                else if (normalizedPolicy.Equals("overwrite", StringComparison.OrdinalIgnoreCase))
                    request.ConflictPolicy = "Overwrite";
                else
                    return ApiError(400, ApiErrorCodes.RunInvalidConflictPolicy, "conflictPolicy must be one of: Rename, Skip, Overwrite.");
            }
        
            // SEC: Validate TrashRoot — same safety rules as Roots
            if (!string.IsNullOrWhiteSpace(request.TrashRoot))
            {
                var pathError = ValidatePathSecurity(request.TrashRoot.Trim(), "trashRoot", allowedRootPolicy);
                if (pathError is not null) return pathError;
            }
        
            // SEC: Validate DatRoot
            if (!string.IsNullOrWhiteSpace(request.DatRoot))
            {
                var pathError = ValidatePathSecurity(request.DatRoot.Trim(), "datRoot", allowedRootPolicy);
                if (pathError is not null) return pathError;
            }
        
            // Validate convertFormat (allowlist)
            if (!string.IsNullOrWhiteSpace(request.ConvertFormat))
            {
                var fmt = request.ConvertFormat.Trim().ToLowerInvariant();
                if (fmt is not "auto" and not "chd" and not "rvz" and not "zip" and not "7z")
                    return ApiError(400, ApiErrorCodes.RunInvalidConvertFormat, "convertFormat must be one of: auto, chd, rvz, zip, 7z.");
            }
        
            // OnlyGames policy guard
            if (!request.OnlyGames && !request.KeepUnknownWhenOnlyGames)
                return ApiError(400, ApiErrorCodes.RunInvalidUnknownPolicy, "keepUnknownWhenOnlyGames can only be set when onlyGames is true.");
        
            var waitSync = !string.IsNullOrWhiteSpace(wait) &&
                !string.Equals(wait, "false", StringComparison.OrdinalIgnoreCase);
        
            var waitTimeoutMs = 600_000;
            if (!string.IsNullOrWhiteSpace(waitTimeoutMsQuery))
            {
                if (!int.TryParse(waitTimeoutMsQuery, out var parsedWaitTimeoutMs) || parsedWaitTimeoutMs < 1 || parsedWaitTimeoutMs > 1_800_000)
                    return ApiError(400, ApiErrorCodes.RunInvalidWaitTimeout, "waitTimeoutMs must be an integer between 1 and 1800000.");
                waitTimeoutMs = parsedWaitTimeoutMs;
            }
        
            var ownerClientId = GetClientBindingId(ctx, trustForwardedFor);
            var create = mgr.TryCreateOrReuse(request, mode, idempotencyKey, ownerClientId);
            if (create.Disposition == RunCreateDisposition.ActiveConflict)
            {
                if (create.Run is not null && !CanAccessRun(create.Run, ownerClientId))
                    return ApiError(403, ApiErrorCodes.AuthForbidden, "Run belongs to a different client.", ErrorKind.Critical, runId: create.Run.RunId);
                return ApiError(409, ApiErrorCodes.RunActiveConflict, create.Error ?? "Another run is already active.", runId: create.Run?.RunId, meta: CreateMeta(("activeRun", create.Run is null ? null : create.Run.ToDto())));
            }
        
            if (create.Disposition == RunCreateDisposition.IdempotencyConflict)
            {
                if (create.Run is not null && !CanAccessRun(create.Run, ownerClientId))
                    return ApiError(403, ApiErrorCodes.AuthForbidden, "Run belongs to a different client.", ErrorKind.Critical, runId: create.Run.RunId);
                return ApiError(409, ApiErrorCodes.RunIdempotencyConflict, create.Error ?? "Idempotency key reuse with different payload is not allowed.", runId: create.Run?.RunId, meta: CreateMeta(("run", create.Run is null ? null : create.Run.ToDto())));
            }
        
            var run = create.Run!;
            if (!CanAccessRun(run, ownerClientId))
                return ApiError(403, ApiErrorCodes.AuthForbidden, "Run belongs to a different client.", ErrorKind.Critical, runId: run.RunId);
        
            if (waitSync)
            {
                var waitResult = await mgr.WaitForCompletion(
                    run.RunId,
                    timeout: TimeSpan.FromMilliseconds(waitTimeoutMs),
                    cancellationToken: ctx.RequestAborted);
        
                if (waitResult.Disposition == RunWaitDisposition.ClientDisconnected)
                    return Results.Empty;
        
                var current = mgr.Get(run.RunId);
                if (waitResult.Disposition == RunWaitDisposition.TimedOut)
                {
                    return Results.Accepted($"/runs/{run.RunId}", new
                    {
                        run = current is null ? null : current.ToDto(),
                        reused = create.Disposition == RunCreateDisposition.Reused,
                        waitTimedOut = true
                    });
                }
        
                ctx.Response.Headers.Location = $"/runs/{run.RunId}";
        
                return Results.Ok(new
                {
                    run = current is null ? null : current.ToDto(),
                    result = current?.Result,
                    reused = create.Disposition == RunCreateDisposition.Reused
                });
            }
        
            if (create.Disposition == RunCreateDisposition.Reused && run.Status != RunConstants.StatusRunning)
                return Results.Ok(new { run = run.ToDto(), result = run.Result, reused = true });
        
            return Results.Accepted($"/runs/{run.RunId}", new { run = run.ToDto(), reused = create.Disposition == RunCreateDisposition.Reused });
        })
            .WithSummary("Create and execute a deduplication run")
            .Produces<RunStartEnvelope>(StatusCodes.Status200OK)
            .Produces<RunStartEnvelope>(StatusCodes.Status202Accepted)
            .Produces<OperationErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<OperationErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<OperationErrorResponse>(StatusCodes.Status409Conflict);
        
        app.MapGet("/runs/{runId}", (string runId, HttpContext ctx, RunLifecycleManager mgr) =>
        {
            if (!Guid.TryParse(runId, out _))
                return ApiError(400, ApiErrorCodes.RunInvalidId, "Invalid run ID format.");
            var run = mgr.Get(runId);
            if (run is null)
                return ApiError(404, ApiErrorCodes.RunNotFound, "Run not found.", runId: runId);
        
            if (!CanAccessRun(run, GetClientBindingId(ctx, trustForwardedFor)))
                return ApiError(403, ApiErrorCodes.AuthForbidden, "Run belongs to a different client.", ErrorKind.Critical, runId: runId);
        
            return Results.Ok(new { run = run.ToDto() });
        })
            .WithSummary("Get run status")
            .Produces<RunEnvelope>(StatusCodes.Status200OK)
            .Produces<OperationErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<OperationErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<OperationErrorResponse>(StatusCodes.Status404NotFound);
        
        app.MapGet("/runs/{runId}/result", (string runId, HttpContext ctx, RunLifecycleManager mgr) =>
        {
            if (!Guid.TryParse(runId, out _))
                return ApiError(400, ApiErrorCodes.RunInvalidId, "Invalid run ID format.");
            var run = mgr.Get(runId);
            if (run is null)
                return ApiError(404, ApiErrorCodes.RunNotFound, "Run not found.", runId: runId);
            if (!CanAccessRun(run, GetClientBindingId(ctx, trustForwardedFor)))
                return ApiError(403, ApiErrorCodes.AuthForbidden, "Run belongs to a different client.", ErrorKind.Critical, runId: runId);
            if (run.Status == RunConstants.StatusRunning)
                return ApiError(409, ApiErrorCodes.RunInProgress, "Run still in progress.", runId: runId);
            return Results.Ok(new { run = run.ToDto(), result = run.Result });
        })
            .WithSummary("Get completed run result")
            .Produces<RunResultEnvelope>(StatusCodes.Status200OK)
            .Produces<OperationErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<OperationErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<OperationErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<OperationErrorResponse>(StatusCodes.Status409Conflict);
        
        app.MapGet("/runs/{runId}/report", (string runId, HttpContext ctx, RunLifecycleManager mgr) =>
        {
            if (!Guid.TryParse(runId, out _))
                return ApiError(400, ApiErrorCodes.RunInvalidId, "Invalid run ID format.");
        
            var run = mgr.Get(runId);
            if (run is null)
                return ApiError(404, ApiErrorCodes.RunNotFound, "Run not found.", runId: runId);
            if (!CanAccessRun(run, GetClientBindingId(ctx, trustForwardedFor)))
                return ApiError(403, ApiErrorCodes.AuthForbidden, "Run belongs to a different client.", ErrorKind.Critical, runId: runId);
            if (run.Status == RunConstants.StatusRunning)
                return ApiError(409, ApiErrorCodes.RunInProgress, "Run still in progress.", runId: runId);
        
            return CreateArtifactDownloadResult(
                run.ReportPath,
                "text/html; charset=utf-8",
                $"report-{runId}.html",
                "RUN-REPORT-NOT-AVAILABLE",
                "No report artifact available for this run.",
                runId);
        })
            .WithSummary("Download the generated HTML report for a completed run")
            .Produces(StatusCodes.Status200OK, contentType: "text/html")
            .Produces<OperationErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<OperationErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<OperationErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<OperationErrorResponse>(StatusCodes.Status409Conflict);
        
        app.MapGet("/runs/{runId}/audit", (string runId, HttpContext ctx, RunLifecycleManager mgr) =>
        {
            if (!Guid.TryParse(runId, out _))
                return ApiError(400, ApiErrorCodes.RunInvalidId, "Invalid run ID format.");
        
            var run = mgr.Get(runId);
            if (run is null)
                return ApiError(404, ApiErrorCodes.RunNotFound, "Run not found.", runId: runId);
            if (!CanAccessRun(run, GetClientBindingId(ctx, trustForwardedFor)))
                return ApiError(403, ApiErrorCodes.AuthForbidden, "Run belongs to a different client.", ErrorKind.Critical, runId: runId);
            if (run.Status == RunConstants.StatusRunning)
                return ApiError(409, ApiErrorCodes.RunInProgress, "Run still in progress.", runId: runId);
        
            return CreateArtifactDownloadResult(
                run.AuditPath,
                "text/csv; charset=utf-8",
                $"audit-{runId}.csv",
                "RUN-AUDIT-NOT-AVAILABLE",
                "No audit artifact available for this run.",
                runId);
        })
            .WithSummary("Download the generated audit CSV for a completed run")
            .Produces(StatusCodes.Status200OK, contentType: "text/csv")
            .Produces<OperationErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<OperationErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<OperationErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<OperationErrorResponse>(StatusCodes.Status409Conflict);
        
        app.MapPost("/runs/{runId}/cancel", (string runId, HttpContext ctx, RunLifecycleManager mgr) =>
        {
            if (!Guid.TryParse(runId, out _))
                return ApiError(400, ApiErrorCodes.RunInvalidId, "Invalid run ID format.");
            var current = mgr.Get(runId);
            if (current is null)
                return ApiError(404, ApiErrorCodes.RunNotFound, "Run not found.", runId: runId);
            if (!CanAccessRun(current, GetClientBindingId(ctx, trustForwardedFor)))
                return ApiError(403, ApiErrorCodes.AuthForbidden, "Run belongs to a different client.", ErrorKind.Critical, runId: runId);
        
            var cancel = mgr.Cancel(runId);
            if (cancel.Disposition == RunCancelDisposition.NotFound)
                return ApiError(404, ApiErrorCodes.RunNotFound, "Run not found.", runId: runId);
            var updated = mgr.Get(runId);
            return Results.Ok(new
            {
                run = updated is null ? null : updated.ToDto(),
                cancelAccepted = cancel.Disposition == RunCancelDisposition.Accepted,
                idempotent = cancel.Disposition != RunCancelDisposition.Accepted,
                cancelledAtUtc = updated?.CancelledAtUtc?.ToString("o")
            });
        })
            .WithSummary("Cancel a run idempotently")
            .Produces<RunCancelEnvelope>(StatusCodes.Status200OK)
            .Produces<OperationErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<OperationErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<OperationErrorResponse>(StatusCodes.Status404NotFound);
        
        app.MapPost("/runs/{runId}/rollback", (string runId, HttpContext ctx, string? dryRun, RunLifecycleManager mgr, AllowedRootPathPolicy allowedRootPolicy, AuditSigningService auditSigningService) =>
        {
            if (!Guid.TryParse(runId, out _))
                return ApiError(400, ApiErrorCodes.RunInvalidId, "Invalid run ID format.");
        
            var run = mgr.Get(runId);
            if (run is null)
                return ApiError(404, ApiErrorCodes.RunNotFound, "Run not found.", runId: runId);
        
            if (!CanAccessRun(run, GetClientBindingId(ctx, trustForwardedFor)))
                return ApiError(403, ApiErrorCodes.AuthForbidden, "Run belongs to a different client.", ErrorKind.Critical, runId: runId);
        
            if (run.Status == RunConstants.StatusRunning)
                return ApiError(409, ApiErrorCodes.RunInProgress, "Rollback is only available for completed runs.", runId: runId);
        
            if (string.IsNullOrWhiteSpace(run.AuditPath) || !File.Exists(run.AuditPath))
                return ApiError(409, ApiErrorCodes.RunRollbackNotAvailable, "No audit artifact available for rollback.", runId: runId);
        
            // SEC-ROLLBACK-01: Default to dry-run to prevent accidental data changes.
            // Safety sequence: DryRun → Summary → Bestätigung → Apply (Projektregeln §4)
            bool isDryRun = !string.Equals(dryRun, "false", StringComparison.OrdinalIgnoreCase);
        
            var resolvedRootSet = AuditRollbackRootResolver.Resolve(run.AuditPath);
            var restoreRoots = resolvedRootSet.RestoreRoots.Count > 0
                ? resolvedRootSet.RestoreRoots
                : (run.Roots ?? Array.Empty<string>());
            var currentRoots = resolvedRootSet.CurrentRoots.Count > 0
                ? resolvedRootSet.CurrentRoots
                : (string.IsNullOrWhiteSpace(run.TrashRoot)
                    ? restoreRoots
                    : restoreRoots.Append(run.TrashRoot).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        
            if (allowedRootPolicy.IsEnforced
                && (restoreRoots.Any(root => !allowedRootPolicy.IsPathAllowed(root))
                    || currentRoots.Any(root => !allowedRootPolicy.IsPathAllowed(root))))
            {
                return ApiError(400, SecurityErrorCodes.OutsideAllowedRoots, "Rollback paths are outside configured AllowedRoots.", ErrorKind.Critical, runId: runId);
            }
        
            var rollback = auditSigningService.Rollback(run.AuditPath, restoreRoots, currentRoots, dryRun: isDryRun);
        
            return Results.Ok(new
            {
                run = run.ToDto(),
                dryRun = isDryRun,
                rollback
            });
        })
            .WithSummary("Preview or execute audit-based rollback for a completed run")
            .Produces<RunRollbackEnvelope>(StatusCodes.Status200OK)
            .Produces<OperationErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<OperationErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<OperationErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<OperationErrorResponse>(StatusCodes.Status409Conflict);
        
        app.MapGet("/runs/{runId}/reviews", async (string runId, HttpContext ctx, string? offset, string? limit, RunLifecycleManager mgr, PersistedReviewDecisionService reviewDecisionService, CancellationToken ct) =>
        {
            if (!Guid.TryParse(runId, out _))
                return ApiError(400, ApiErrorCodes.RunInvalidId, "Invalid run ID format.");
        
            var run = mgr.Get(runId);
            if (run is null)
                return ApiError(404, ApiErrorCodes.RunNotFound, "Run not found.", runId: runId);
        
            if (!CanAccessRun(run, GetClientBindingId(ctx, trustForwardedFor)))
                return ApiError(403, ApiErrorCodes.AuthForbidden, "Run belongs to a different client.", ErrorKind.Critical, runId: runId);
        
            var parsedOffset = 0;
            if (!string.IsNullOrWhiteSpace(offset))
            {
                if (!int.TryParse(offset, out parsedOffset) || parsedOffset < 0)
                    return ApiError(400, ApiErrorCodes.RunInvalidReviewOffset, "offset must be a non-negative integer.");
            }
        
            int? parsedLimit = null;
            if (!string.IsNullOrWhiteSpace(limit))
            {
                if (!int.TryParse(limit, out var limitValue) || limitValue < 1 || limitValue > 1000)
                    return ApiError(400, ApiErrorCodes.RunInvalidReviewLimit, "limit must be an integer between 1 and 1000.");
                parsedLimit = limitValue;
            }
        
            var queue = await BuildReviewQueueAsync(run, reviewDecisionService, parsedOffset, parsedLimit, ct);
            return Results.Ok(queue);
        })
            .WithSummary("Get review queue for a run")
            .Produces<ApiReviewQueue>(StatusCodes.Status200OK)
            .Produces<OperationErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<OperationErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<OperationErrorResponse>(StatusCodes.Status404NotFound);
        
        app.MapPost("/runs/{runId}/reviews/approve", async (string runId, HttpContext ctx, RunLifecycleManager mgr, PersistedReviewDecisionService reviewDecisionService, CancellationToken ct) =>
        {
            if (!Guid.TryParse(runId, out _))
                return ApiError(400, ApiErrorCodes.RunInvalidId, "Invalid run ID format.");
        
            var run = mgr.Get(runId);
            if (run is null)
                return ApiError(404, ApiErrorCodes.RunNotFound, "Run not found.", runId: runId);
        
            if (!CanAccessRun(run, GetClientBindingId(ctx, trustForwardedFor)))
                return ApiError(403, ApiErrorCodes.AuthForbidden, "Run belongs to a different client.", ErrorKind.Critical, runId: runId);
        
            // SEC-API-04: Reject oversized request bodies to prevent memory exhaustion
            if (ctx.Request.ContentLength is > 1_048_576)
                return ApiError(400, ApiErrorCodes.RunPayloadTooLarge, "Request body exceeds 1 MB limit.");
        
            ApiReviewApprovalRequest request;
            try
            {
                request = await ctx.Request.ReadFromJsonAsync(ApiJsonSerializerContext.Default.ApiReviewApprovalRequest) ?? new ApiReviewApprovalRequest();
            }
            catch (JsonException)
            {
                return ApiError(400, ApiErrorCodes.RunInvalidJson, "Invalid JSON.");
            }
        
            // SEC-API-05: Limit Paths array size to prevent quadratic complexity
            if (request.Paths is { Length: > 10_000 })
                return ApiError(400, ApiErrorCodes.RunTooManyPaths, "Paths array exceeds 10,000 entries.");
        
            // Use HashSet for O(1) lookup instead of O(n) Contains on array
            var pathFilter = request.Paths is { Length: > 0 }
                ? new HashSet<string>(request.Paths, StringComparer.OrdinalIgnoreCase)
                : null;
        
            var queue = await BuildReviewQueueAsync(run, reviewDecisionService, ct: ct);
            var matched = queue.Items.Where(item =>
                    (string.IsNullOrWhiteSpace(request.ConsoleKey) || string.Equals(item.ConsoleKey, request.ConsoleKey, StringComparison.OrdinalIgnoreCase)) &&
                    (string.IsNullOrWhiteSpace(request.MatchLevel) || string.Equals(item.MatchLevel, request.MatchLevel, StringComparison.OrdinalIgnoreCase)) &&
                    (pathFilter is null || pathFilter.Contains(item.MainPath)))
                .GroupBy(item => item.MainPath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
        
            foreach (var item in matched)
                run.TryApproveReviewPath(item.MainPath);
        
            var coreRunResult = run.CoreRunResult;
            if (coreRunResult is not null)
            {
                var projectedArtifacts = RunArtifactProjection.Project(coreRunResult);
                var approvedPaths = matched
                    .Select(static item => item.MainPath)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
        
                var approvedCandidates = projectedArtifacts.AllCandidates
                    .Where(candidate => approvedPaths.Contains(candidate.MainPath))
                    .ToArray();
        
                if (approvedCandidates.Length > 0)
                    await reviewDecisionService.PersistApprovalsAsync(approvedCandidates, "api", ct);
            }
        
            var updated = await BuildReviewQueueAsync(run, reviewDecisionService, ct: ct);
            return Results.Ok(new
            {
                runId,
                approvedCount = matched.Length,
                totalApproved = run.ApprovedReviewCount,
                queue = updated
            });
        })
            .WithSummary("Approve review items for a run")
            .Accepts<ApiReviewApprovalRequest>("application/json")
            .Produces<RunReviewApprovalEnvelope>(StatusCodes.Status200OK)
            .Produces<OperationErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<OperationErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<OperationErrorResponse>(StatusCodes.Status404NotFound);
        
        app.MapPost("/watch/start", async (
            HttpContext ctx,
            string? debounceSeconds,
            string? intervalMinutes,
            string? cron,
            ApiAutomationService automation,
            RunConfigurationMaterializer runConfigurationMaterializer,
            AllowedRootPathPolicy allowedRootPolicy) =>
        {
            var requesterClientId = GetClientBindingId(ctx, trustForwardedFor);
            if (!automation.CanAccess(requesterClientId))
                return ApiError(403, ApiErrorCodes.AuthForbidden, "Automation belongs to a different client.", ErrorKind.Critical);
        
            RunRequest? request;
            string watchBody;
            try
            {
                ctx.Request.EnableBuffering();
                using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8, leaveOpen: true);
                watchBody = await reader.ReadToEndAsync();
                ctx.Request.Body.Position = 0;
                request = JsonSerializer.Deserialize<RunRequest>(watchBody,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException)
            {
                return ApiError(400, ApiErrorCodes.WatchInvalidJson, "Invalid JSON.");
            }
        
            if (request is null)
                return ApiError(400, ApiErrorCodes.WatchInvalidJson, "Invalid JSON.");
        
            ApiResolvedRunConfiguration resolvedWatchRequest;
            try
            {
                var dataDir = RunEnvironmentBuilder.TryResolveDataDir()
                    ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
                var settings = RunEnvironmentBuilder.LoadSettings(dataDir);
                using var jsonDocument = JsonDocument.Parse(watchBody);
                resolvedWatchRequest = await ApiRunConfigurationMapper.ResolveAsync(
                    request,
                    jsonDocument.RootElement,
                    settings,
                    runConfigurationMaterializer,
                    ctx.RequestAborted);
                request = resolvedWatchRequest.Request;
            }
            catch (ConfigurationValidationException ex)
            {
                var (code, message) = MapConfigurationError(ex, "WATCH");
                return ApiError(400, code, message);
            }
            catch (InvalidOperationException ex)
            {
                SafeConsoleWriteLine($"[API-WARN] Watch configuration rejected: {ex.GetType().Name}");
                return ApiError(400, ApiErrorCodes.WatchInvalidConfig, "Watch configuration is invalid.");
            }
        
            if (request.Roots is null || request.Roots.Length == 0)
                return ApiError(400, ApiErrorCodes.WatchRootsRequired, "roots[] is required.");
        
            var parsedDebounceSeconds = 5;
            if (!string.IsNullOrWhiteSpace(debounceSeconds)
                && (!int.TryParse(debounceSeconds, out parsedDebounceSeconds) || parsedDebounceSeconds < 1 || parsedDebounceSeconds > 300))
            {
                return ApiError(400, ApiErrorCodes.WatchInvalidDebounce, "debounceSeconds must be an integer between 1 and 300.");
            }
        
            int? parsedIntervalMinutes = null;
            if (!string.IsNullOrWhiteSpace(intervalMinutes))
            {
                if (!int.TryParse(intervalMinutes, out var intervalValue) || intervalValue < 1 || intervalValue > 10080)
                    return ApiError(400, ApiErrorCodes.WatchInvalidInterval, "intervalMinutes must be an integer between 1 and 10080.");
        
                parsedIntervalMinutes = intervalValue;
            }
        
            if (parsedIntervalMinutes is null && string.IsNullOrWhiteSpace(cron))
                return ApiError(400, ApiErrorCodes.WatchScheduleRequired, "Specify either intervalMinutes or cron.");
        
            if (!string.IsNullOrWhiteSpace(cron))
            {
                var normalizedCron = cron.Trim();
                if (!Romulus.Infrastructure.Watch.CronScheduleEvaluator.TryValidateCronExpression(normalizedCron, out var cronValidationError))
                    return ApiError(400, ApiErrorCodes.WatchInvalidCron, cronValidationError ?? "Invalid cron expression.");
        
                cron = normalizedCron;
            }
        
            foreach (var root in request.Roots)
            {
                if (string.IsNullOrWhiteSpace(root))
                    return ApiError(400, ApiErrorCodes.WatchRootEmpty, "Empty root path.");
        
                var pathError = ValidatePathSecurity(root, "roots", allowedRootPolicy);
                if (pathError is not null)
                    return pathError;
        
                if (!Directory.Exists(root))
                    return ApiError(400, ApiErrorCodes.IoRootNotFound, $"Root not found: {root}");
            }
        
            var mode = request.Mode ?? RunConstants.ModeDryRun;
            if (!mode.Equals(RunConstants.ModeDryRun, StringComparison.OrdinalIgnoreCase)
                && !mode.Equals(RunConstants.ModeMove, StringComparison.OrdinalIgnoreCase))
            {
                return ApiError(400, ApiErrorCodes.WatchInvalidMode, "mode must be DryRun or Move.");
            }
        
            mode = mode.Equals(RunConstants.ModeMove, StringComparison.OrdinalIgnoreCase)
                ? RunConstants.ModeMove
                : RunConstants.ModeDryRun;
        
            var status = automation.Start(
                request,
                mode,
                requesterClientId,
                parsedDebounceSeconds,
                parsedIntervalMinutes,
                cron);
        
            return Results.Ok(status);
        })
            .WithSummary("Start shared watch/schedule automation")
            .Produces<ApiWatchStatus>(StatusCodes.Status200OK)
            .Produces<OperationErrorResponse>(StatusCodes.Status400BadRequest);
        
        app.MapPost("/watch/stop", (HttpContext ctx, ApiAutomationService automation) =>
        {
            if (!automation.CanAccess(GetClientBindingId(ctx, trustForwardedFor)))
                return ApiError(403, ApiErrorCodes.AuthForbidden, "Automation belongs to a different client.", ErrorKind.Critical);
        
            return Results.Ok(automation.Stop());
        })
            .WithSummary("Stop shared watch/schedule automation")
            .Produces<ApiWatchStatus>(StatusCodes.Status200OK)
            .Produces<OperationErrorResponse>(StatusCodes.Status403Forbidden);
        
        app.MapGet("/watch/status", (HttpContext ctx, ApiAutomationService automation) =>
        {
            if (!automation.CanAccess(GetClientBindingId(ctx, trustForwardedFor)))
                return ApiError(403, ApiErrorCodes.AuthForbidden, "Automation belongs to a different client.", ErrorKind.Critical);
        
            return Results.Ok(automation.GetStatus());
        })
            .WithSummary("Get shared watch/schedule automation status")
            .Produces<ApiWatchStatus>(StatusCodes.Status200OK)
            .Produces<OperationErrorResponse>(StatusCodes.Status403Forbidden);
        
        app.MapGet("/runs/{runId}/stream", async (string runId, HttpContext ctx, RunLifecycleManager mgr) =>
        {
            if (!Guid.TryParse(runId, out _))
            {
                await WriteApiError(ctx, 400, ApiErrorCodes.RunInvalidId, "Invalid run ID format.");
                return;
            }
            var run = mgr.Get(runId);
            if (run is null)
            {
                await WriteApiError(ctx, 404, ApiErrorCodes.RunNotFound, "Run not found.", runId: runId);
                return;
            }
            if (!CanAccessRun(run, GetClientBindingId(ctx, trustForwardedFor)))
            {
                await WriteApiError(ctx, 403, ApiErrorCodes.AuthForbidden, "Run belongs to a different client.", ErrorKind.Critical, runId: runId);
                return;
            }
        
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers["Cache-Control"] = "no-cache";
        
            var writer = ctx.Response.Body;
            var encoding = Encoding.UTF8;
        
            await WriteSseEvent(writer, encoding, "ready", new { runId, utc = timeProvider.UtcNow.ToString("o") }, ctx.RequestAborted);
        
            var timeout = TimeSpan.FromSeconds(sseTimeoutSeconds);
            var start = timeProvider.UtcNow.UtcDateTime;
            string? lastStateJson = null;
            var lastHeartbeat = timeProvider.UtcNow.UtcDateTime;
        
            try
            {
                while (timeProvider.UtcNow.UtcDateTime - start < timeout)
                {
                    if (ctx.RequestAborted.IsCancellationRequested)
                        break;
        
                    var current = mgr.Get(runId);
                    if (current is null)
                    {
                        await WriteSseEvent(writer, encoding, "error", CreateErrorResponse(
                            StatusCodes.Status404NotFound,
                            ApiErrorCodes.RunNotFound,
                            "Run not found.",
                            ErrorKind.Recoverable,
                            runId,
                            instance: $"/runs/{runId}/stream"), ctx.RequestAborted);
                        break;
                    }
        
                    var stateSnapshot = new
                    {
                        current.Status,
                        current.ProgressPercent,
                        current.ProgressMessage,
                        current.CancellationRequested,
                        current.CancelledAtUtc,
                        current.CompletedUtc,
                        current.RecoveryState
                    };
                    var stateJson = SerializeApiJson(stateSnapshot);
        
                    if (!string.Equals(stateJson, lastStateJson, StringComparison.Ordinal))
                    {
                        lastStateJson = stateJson;
                        lastHeartbeat = timeProvider.UtcNow.UtcDateTime;
                        if (current.Status != RunConstants.StatusRunning)
                        {
                            var terminalEvent = current.Status switch
                            {
                                RunConstants.StatusCancelled => RunConstants.StatusCancelled,
                                RunConstants.StatusFailed => RunConstants.StatusFailed,
                                RunConstants.StatusCompletedWithErrors => RunConstants.StatusCompletedWithErrors,
                                _ => RunConstants.StatusCompleted
                            };
                            await WriteSseEvent(writer, encoding, terminalEvent, new { run = current.ToDto(), result = current.Result }, ctx.RequestAborted);
                            break;
                        }
                        await WriteSseEvent(writer, encoding, "status", current.ToDto(), ctx.RequestAborted);
                    }
                    else if ((timeProvider.UtcNow.UtcDateTime - lastHeartbeat).TotalSeconds >= sseHeartbeatSeconds)
                    {
                        // V2-H05: SSE heartbeat to prevent proxy/browser timeouts
                        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
                        heartbeatCts.CancelAfter(TimeSpan.FromSeconds(5));
                        await writer.WriteAsync(encoding.GetBytes(":\n\n"), heartbeatCts.Token);
                        await writer.FlushAsync(heartbeatCts.Token);
                        lastHeartbeat = timeProvider.UtcNow.UtcDateTime;
                    }
        
                    await Task.Delay(250, ctx.RequestAborted).ContinueWith(_ => { });
                }
        
                if (timeProvider.UtcNow.UtcDateTime - start >= timeout)
                {
                    await WriteSseEvent(writer, encoding, "timeout", new { runId, seconds = sseTimeoutSeconds }, ctx.RequestAborted);
                }
            }
            catch (IOException)
            {
                // Client disconnected — expected for SSE streams
            }
            catch (OperationCanceledException)
            {
                // Client disconnected via RequestAborted
            }
        });
    }
}
