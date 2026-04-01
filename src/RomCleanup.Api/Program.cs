using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Mvc;
using RomCleanup.Api;
using RomCleanup.Contracts;
using RomCleanup.Contracts.Errors;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure;
using RomCleanup.Infrastructure.Analysis;
using RomCleanup.Infrastructure.Audit;
using RomCleanup.Infrastructure.Conversion;
using RomCleanup.Infrastructure.Dat;
using RomCleanup.Infrastructure.FileSystem;
using RomCleanup.Infrastructure.Index;
using RomCleanup.Infrastructure.Orchestration;
using RomCleanup.Infrastructure.Export;
using RomCleanup.Infrastructure.Profiles;
using RomCleanup.Infrastructure.Review;
using RomCleanup.Infrastructure.Safety;
using RomCleanup.Infrastructure.Workflow;

var builder = WebApplication.CreateBuilder(args);

var configuredApiKey = builder.Configuration["ApiKey"]
                       ?? Environment.GetEnvironmentVariable("ROM_CLEANUP_API_KEY");
var headlessOptions = HeadlessApiOptions.FromConfiguration(builder.Configuration);
headlessOptions.Validate(configuredApiKey, builder.Environment.IsDevelopment());

var port = headlessOptions.Port;
var bindAddress = headlessOptions.BindAddress;
builder.WebHost.UseUrls($"http://{bindAddress}:{port}");

builder.Services.AddRomCleanupCore();
builder.Services.AddSingleton(headlessOptions);
builder.Services.AddSingleton(new AllowedRootPathPolicy(headlessOptions.AllowedRoots));
builder.Services.AddSingleton<RunManager>();
builder.Services.AddSingleton<RunLifecycleManager>(sp =>
    sp.GetRequiredService<RunManager>().Lifecycle);
builder.Services.AddSingleton<ApiAutomationService>();
builder.Services.AddOpenApi(OpenApiSpec.DocumentName, OpenApiSpec.Configure);

var app = builder.Build();

// --- Middleware ---
var apiKey = configuredApiKey;
if (string.IsNullOrEmpty(apiKey))
{
    if (app.Environment.IsDevelopment() && !headlessOptions.AllowRemoteClients)
    {
        apiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        Console.WriteLine($"[Dev] Generated API key: {apiKey}");
    }
    else
    {
        throw new InvalidOperationException("API key required: set --ApiKey or ROM_CLEANUP_API_KEY env var");
    }
}

var corsMode = builder.Configuration.GetValue("CorsMode", "strict-local");
var corsOrigin = builder.Configuration.GetValue("CorsAllowOrigin", "http://127.0.0.1");
var rateLimitMax = builder.Configuration.GetValue("RateLimitRequests", 120);
var rateLimitWindow = TimeSpan.FromSeconds(builder.Configuration.GetValue("RateLimitWindowSeconds", 60));
var trustForwardedFor = builder.Configuration.GetValue("TrustForwardedFor", false);
var sseTimeoutSeconds = Math.Clamp(builder.Configuration.GetValue("SseTimeoutSeconds", 300), 30, 3600);
var sseHeartbeatSeconds = Math.Clamp(builder.Configuration.GetValue("SseHeartbeatSeconds", 15), 5, 120);
var rateLimiter = new RateLimiter(rateLimitMax, rateLimitWindow);
var resolvedCorsOrigin = headlessOptions.AllowRemoteClients
    && Uri.TryCreate(headlessOptions.PublicBaseUrl, UriKind.Absolute, out var publicBaseUri)
        ? publicBaseUri.GetLeftPart(UriPartial.Authority)
        : ResolveCorsOrigin(corsMode, corsOrigin);

// Remove server headers
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers.Remove("Server");
    ctx.Response.Headers.Remove("X-Powered-By");
    ctx.Response.Headers["Cache-Control"] = "no-store";
    ctx.Response.Headers["X-Api-Version"] = ApiVersion;
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";

    // CORS
    if (corsMode != "none")
    {
        var origin = resolvedCorsOrigin;
        ctx.Response.Headers["Access-Control-Allow-Origin"] = origin;
        ctx.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type, X-Api-Key, X-Client-Id";
        ctx.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
        ctx.Response.Headers["Access-Control-Max-Age"] = "600";
        if (origin != "*")
            ctx.Response.Headers["Vary"] = "Origin";
    }

    // OPTIONS preflight (skip auth)
    if (ctx.Request.Method == "OPTIONS")
    {
        ctx.Response.StatusCode = 204;
        return;
    }

    await next();
});

if (headlessOptions.DashboardEnabled)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.Use(async (ctx, next) =>
{
    var rawCorrelationId = ctx.Request.Headers["X-Correlation-ID"].FirstOrDefault();
    var correlationId = SanitizeCorrelationId(rawCorrelationId)
        ?? Guid.NewGuid().ToString("N")[..16];

    ctx.Items["CorrelationId"] = correlationId;
    ctx.Response.Headers["X-Correlation-ID"] = correlationId;

    await next();
});

app.Use(async (ctx, next) =>
{
    if (IsAnonymousEndpoint(ctx.Request.Path))
    {
        await next();
        return;
    }

    // Rate limiting
    var clientIp = ApiClientIdentity.ResolveRateLimitClientId(ctx, trustForwardedFor);
    var rawClientId = ctx.Request.Headers["X-Client-Id"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(rawClientId) && SanitizeClientBindingId(rawClientId) is null)
    {
        await WriteApiError(ctx, 400, "AUTH-INVALID-CLIENT-ID", "Invalid X-Client-Id. Use max 64 chars from [A-Za-z0-9-_.].", ErrorKind.Critical);
        return;
    }
    var clientBindingId = SanitizeClientBindingId(rawClientId) ?? clientIp;
    ctx.Items["ClientBindingId"] = clientBindingId;

    if (!rateLimiter.TryAcquire(clientIp))
    {
        ctx.Response.Headers["Retry-After"] = Math.Max(1, (int)Math.Ceiling(rateLimitWindow.TotalSeconds)).ToString();
        await WriteApiError(ctx, 429, "RUN-RATE-LIMIT", "Too many requests.", ErrorKind.Transient);
        return;
    }

    // API key validation (fixed-time comparison)
    var providedKey = ctx.Request.Headers["X-Api-Key"].FirstOrDefault();
    if (!FixedTimeEquals(apiKey, providedKey))
    {
        await WriteApiError(ctx, 401, "AUTH-UNAUTHORIZED", "Unauthorized", ErrorKind.Critical);
        return;
    }

    await next();
});

// --- Request Logging (P3-API-11) ---
app.Use(async (ctx, next) =>
{
    // V2-M08: Correlation-ID linking HTTP requests to run lifecycle
    var correlationId = ctx.Items.TryGetValue("CorrelationId", out var storedCorrelationId)
        ? storedCorrelationId?.ToString() ?? Guid.NewGuid().ToString("N")[..16]
        : ctx.Request.Headers["X-Correlation-ID"].FirstOrDefault() ?? Guid.NewGuid().ToString("N")[..16];

    var start = DateTime.UtcNow;
    await next();
    var elapsed = (DateTime.UtcNow - start).TotalMilliseconds;
    var method = ctx.Request.Method;
    var path = ctx.Request.Path;
    var status = ctx.Response.StatusCode;
    SafeConsoleWriteLine($"[{start:o}] {correlationId} {method} {path} → {status} ({elapsed:F0}ms)");
});

// --- Endpoints ---

app.MapGet("/healthz", () => Results.Ok(new
{
    status = "ok",
    serverRunning = true,
    utc = DateTime.UtcNow.ToString("o"),
    version = ApiVersion
}))
    .WithSummary("Unauthenticated local liveness probe");

app.MapGet("/health", (RunLifecycleManager mgr) =>
{
    var activeRun = mgr.GetActive();
    return Results.Ok(new
    {
        status = "ok",
        serverRunning = true,
        hasActiveRun = activeRun is not null,
        utc = DateTime.UtcNow.ToString("o"),
        version = ApiVersion
    });
})
    .WithSummary("Authenticated health check");

app.MapGet("/dashboard/bootstrap", (HeadlessApiOptions options, AllowedRootPathPolicy allowedRootPolicy) =>
    Results.Ok(DashboardDataBuilder.BuildBootstrap(options, allowedRootPolicy, ApiVersion)))
    .WithSummary("Anonymous dashboard bootstrap metadata")
    .Produces<DashboardBootstrapResponse>(StatusCodes.Status200OK);

app.MapGet("/dashboard/summary", async (
    RunLifecycleManager mgr,
    ApiAutomationService automationService,
    ICollectionIndex collectionIndex,
    RunProfileService profileService,
    AllowedRootPathPolicy allowedRootPolicy,
    CancellationToken ct) =>
{
    var summary = await DashboardDataBuilder.BuildSummaryAsync(
        mgr,
        automationService,
        collectionIndex,
        profileService,
        allowedRootPolicy,
        ApiVersion,
        ct);
    return Results.Ok(summary);
})
    .WithSummary("Dashboard summary read model built from the existing API/domain state")
    .Produces<DashboardSummaryResponse>(StatusCodes.Status200OK);

app.MapGet("/openapi", () => Results.Redirect($"/openapi/{OpenApiSpec.DocumentName}.json", permanent: false))
    .ExcludeFromDescription();

app.MapOpenApi()
    .ExcludeFromDescription();

app.MapGet("/runs", (HttpContext ctx, string? offset, string? limit, RunLifecycleManager mgr) =>
{
    var parsedOffset = 0;
    if (!string.IsNullOrWhiteSpace(offset))
    {
        if (!int.TryParse(offset, out parsedOffset) || parsedOffset < 0)
            return ApiError(400, "RUN-INVALID-OFFSET", "offset must be a non-negative integer.");
    }

    int? parsedLimit = null;
    if (!string.IsNullOrWhiteSpace(limit))
    {
        if (!int.TryParse(limit, out var limitValue) || limitValue < 1 || limitValue > 1000)
            return ApiError(400, "RUN-INVALID-LIMIT", "limit must be an integer between 1 and 1000.");
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

app.MapGet("/runs/history", async (string? offset, string? limit, RomCleanup.Contracts.Ports.ICollectionIndex collectionIndex, CancellationToken ct) =>
{
    var parsedOffset = 0;
    if (!string.IsNullOrWhiteSpace(offset))
    {
        if (!int.TryParse(offset, out parsedOffset) || parsedOffset < 0)
            return ApiError(400, "RUN-INVALID-OFFSET", "offset must be a non-negative integer.");
    }

    int? parsedLimit = null;
    if (!string.IsNullOrWhiteSpace(limit))
    {
        if (!int.TryParse(limit, out var limitValue) || limitValue < 1 || limitValue > 1000)
            return ApiError(400, "RUN-INVALID-LIMIT", "limit must be an integer between 1 and 1000.");
        parsedLimit = limitValue;
    }

    var effectiveLimit = CollectionRunHistoryPageBuilder.NormalizeLimit(parsedLimit);
    var fetchLimit = parsedOffset > int.MaxValue - effectiveLimit
        ? int.MaxValue
        : parsedOffset + effectiveLimit;
    var total = await collectionIndex.CountRunSnapshotsAsync(ct);
    var snapshots = await collectionIndex.ListRunSnapshotsAsync(fetchLimit, ct);
    return Results.Ok(BuildRunHistoryList(CollectionRunHistoryPageBuilder.Build(snapshots, total, parsedOffset, effectiveLimit)));
})
    .WithSummary("List persisted run history snapshots from the collection index")
    .Produces<ApiRunHistoryList>(StatusCodes.Status200OK)
    .Produces<OperationErrorResponse>(StatusCodes.Status400BadRequest);

app.MapGet("/profiles", async (RunProfileService profileService, CancellationToken ct) =>
{
    var profiles = await profileService.ListAsync(ct);
    return Results.Ok(new ApiProfileListResponse { Profiles = profiles.ToArray() });
})
    .WithSummary("List built-in and user-defined run profiles")
    .Produces<ApiProfileListResponse>(StatusCodes.Status200OK);

app.MapGet("/profiles/{id}", async (string id, RunProfileService profileService, CancellationToken ct) =>
{
    var profile = await profileService.TryGetAsync(id, ct);
    return profile is null
        ? ApiError(404, "PROFILE-NOT-FOUND", $"Profile '{id}' was not found.")
        : Results.Ok(profile);
})
    .WithSummary("Get a specific run profile")
    .Produces<OperationErrorResponse>(StatusCodes.Status404NotFound);

app.MapPut("/profiles/{id}", async (string id, RunProfileDocument profile, RunProfileService profileService, CancellationToken ct) =>
{
    try
    {
        var normalized = profile with
        {
            Id = id,
            BuiltIn = false
        };
        var saved = await profileService.SaveAsync(normalized, ct);
        return Results.Ok(saved);
    }
    catch (InvalidOperationException ex)
    {
        return ApiError(400, "PROFILE-INVALID", ex.Message);
    }
})
    .WithSummary("Create or update a user-defined run profile")
    .Produces<OperationErrorResponse>(StatusCodes.Status400BadRequest);

app.MapDelete("/profiles/{id}", async (string id, RunProfileService profileService, CancellationToken ct) =>
{
    try
    {
        var deleted = await profileService.DeleteAsync(id, ct);
        return deleted
            ? Results.Ok(new { deleted = true, id })
            : ApiError(404, "PROFILE-NOT-FOUND", $"Profile '{id}' was not found.");
    }
    catch (InvalidOperationException ex)
    {
        return ApiError(400, "PROFILE-DELETE-BLOCKED", ex.Message);
    }
})
    .WithSummary("Delete a user-defined run profile")
    .Produces<OperationErrorResponse>(StatusCodes.Status400BadRequest)
    .Produces<OperationErrorResponse>(StatusCodes.Status404NotFound);

app.MapGet("/workflows", (string? id) =>
{
    if (string.IsNullOrWhiteSpace(id))
        return Results.Ok(new ApiWorkflowListResponse { Workflows = WorkflowScenarioCatalog.List().ToArray() });

    var workflow = WorkflowScenarioCatalog.TryGet(id);
    return workflow is null
        ? ApiError(404, "WORKFLOW-NOT-FOUND", $"Workflow '{id}' was not found.")
        : Results.Ok(workflow);
})
    .WithSummary("List guided workflow scenarios or fetch one by id")
    .Produces<ApiWorkflowListResponse>(StatusCodes.Status200OK)
    .Produces<OperationErrorResponse>(StatusCodes.Status404NotFound);

app.MapGet("/workflows/{id}", (string id) =>
{
    var workflow = WorkflowScenarioCatalog.TryGet(id);
    return workflow is null
        ? ApiError(404, "WORKFLOW-NOT-FOUND", $"Workflow '{id}' was not found.")
        : Results.Ok(workflow);
})
    .WithSummary("Get a guided workflow scenario")
    .Produces<OperationErrorResponse>(StatusCodes.Status404NotFound);

app.MapGet("/runs/compare", async (string runId, string compareToRunId, ICollectionIndex collectionIndex, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(compareToRunId))
        return ApiError(400, "RUN-COMPARE-IDS-REQUIRED", "runId and compareToRunId are required.");

    var comparison = await RunHistoryInsightsService.CompareAsync(collectionIndex, runId, compareToRunId, ct);
    return comparison is null
        ? ApiError(404, "RUN-COMPARE-NOT-FOUND", "One or both run snapshots were not found.")
        : Results.Ok(comparison);
})
    .WithSummary("Compare two persisted run snapshots")
    .Produces<RunSnapshotComparison>(StatusCodes.Status200OK)
    .Produces<OperationErrorResponse>(StatusCodes.Status400BadRequest)
    .Produces<OperationErrorResponse>(StatusCodes.Status404NotFound);

app.MapGet("/runs/trends", async (int? limit, ICollectionIndex collectionIndex, CancellationToken ct) =>
{
    var report = await RunHistoryInsightsService.BuildStorageInsightsAsync(collectionIndex, limit ?? 30, ct);
    return Results.Ok(report);
})
    .WithSummary("Build storage and trend insights from persisted run history")
    .Produces<StorageInsightReport>(StatusCodes.Status200OK);

app.MapPost("/collections/compare", async (
    HttpContext ctx,
    ICollectionIndex collectionIndex,
    IFileSystem fileSystem,
    AllowedRootPathPolicy allowedRootPolicy,
    CancellationToken ct) =>
{
    var requestRead = await ReadJsonBodyAsync<CollectionCompareRequest>(ctx, "COLLECTION-COMPARE", ct);
    if (requestRead.Error is not null)
        return requestRead.Error;

    var request = requestRead.Value!;
    if (request.Limit < 1 || request.Limit > 5000)
        return ApiError(400, "COLLECTION-COMPARE-INVALID-LIMIT", "limit must be an integer between 1 and 5000.");

    var leftValidation = ValidateCollectionScopeSecurity(request.Left, "left", allowedRootPolicy, requireExistingRoots: true);
    if (leftValidation is not null)
        return leftValidation;

    var rightValidation = ValidateCollectionScopeSecurity(request.Right, "right", allowedRootPolicy, requireExistingRoots: true);
    if (rightValidation is not null)
        return rightValidation;

    var build = await CollectionCompareService.CompareAsync(collectionIndex, fileSystem, request, ct);
    return !build.CanUse || build.Result is null
        ? ApiError(409, "COLLECTION-COMPARE-NOT-READY", build.Reason ?? "Collection compare unavailable.")
        : Results.Ok(build.Result);
})
    .WithSummary("Compare two persisted collection scopes")
    .Accepts<CollectionCompareRequest>("application/json")
    .Produces<CollectionCompareResult>(StatusCodes.Status200OK)
    .Produces<OperationErrorResponse>(StatusCodes.Status400BadRequest)
    .Produces<OperationErrorResponse>(StatusCodes.Status409Conflict);

app.MapPost("/collections/merge", async (
    HttpContext ctx,
    ICollectionIndex collectionIndex,
    IFileSystem fileSystem,
    AllowedRootPathPolicy allowedRootPolicy,
    CancellationToken ct) =>
{
    var requestRead = await ReadJsonBodyAsync<CollectionMergeRequest>(ctx, "COLLECTION-MERGE", ct);
    if (requestRead.Error is not null)
        return requestRead.Error;

    var request = requestRead.Value!;
    var mergeValidation = ValidateCollectionMergeRequest(request, allowedRootPolicy);
    if (mergeValidation is not null)
        return mergeValidation;

    var build = await CollectionMergeService.BuildPlanAsync(collectionIndex, fileSystem, request, ct);
    return !build.CanUse || build.Plan is null
        ? ApiError(409, "COLLECTION-MERGE-NOT-READY", build.Reason ?? "Collection merge unavailable.")
        : Results.Ok(build.Plan);
})
    .WithSummary("Build a deterministic merge plan for two collection scopes")
    .Accepts<CollectionMergeRequest>("application/json")
    .Produces<CollectionMergePlan>(StatusCodes.Status200OK)
    .Produces<OperationErrorResponse>(StatusCodes.Status400BadRequest)
    .Produces<OperationErrorResponse>(StatusCodes.Status409Conflict);

app.MapPost("/collections/merge/apply", async (
    HttpContext ctx,
    ICollectionIndex collectionIndex,
    IFileSystem fileSystem,
    IAuditStore auditStore,
    AllowedRootPathPolicy allowedRootPolicy,
    CancellationToken ct) =>
{
    var requestRead = await ReadJsonBodyAsync<CollectionMergeApplyRequest>(ctx, "COLLECTION-MERGE-APPLY", ct);
    if (requestRead.Error is not null)
        return requestRead.Error;

    var request = requestRead.Value!;
    var mergeValidation = ValidateCollectionMergeRequest(request.MergeRequest, allowedRootPolicy);
    if (mergeValidation is not null)
        return mergeValidation;

    if (!string.IsNullOrWhiteSpace(request.AuditPath))
    {
        var auditPathError = ValidatePathSecurity(request.AuditPath.Trim(), "auditPath", allowedRootPolicy);
        if (auditPathError is not null)
            return auditPathError;
    }

    var result = await CollectionMergeService.ApplyAsync(collectionIndex, fileSystem, auditStore, request, ct);
    return !string.IsNullOrWhiteSpace(result.BlockedReason)
        ? ApiError(409, "COLLECTION-MERGE-APPLY-NOT-READY", result.BlockedReason)
        : Results.Ok(result);
})
    .WithSummary("Apply a previously previewable collection merge with audit and rollback metadata")
    .Accepts<CollectionMergeApplyRequest>("application/json")
    .Produces<CollectionMergeApplyResult>(StatusCodes.Status200OK)
    .Produces<OperationErrorResponse>(StatusCodes.Status400BadRequest)
    .Produces<OperationErrorResponse>(StatusCodes.Status409Conflict);

app.MapPost("/collections/merge/rollback", async (
    HttpContext ctx,
    IFileSystem fileSystem,
    AllowedRootPathPolicy allowedRootPolicy,
    CancellationToken ct) =>
{
    var requestRead = await ReadJsonBodyAsync<CollectionMergeRollbackRequest>(ctx, "COLLECTION-MERGE-ROLLBACK", ct);
    if (requestRead.Error is not null)
        return requestRead.Error;

    var request = requestRead.Value!;
    if (string.IsNullOrWhiteSpace(request.AuditPath))
        return ApiError(400, "COLLECTION-MERGE-ROLLBACK-AUDIT-REQUIRED", "auditPath is required.");

    var auditPathError = ValidatePathSecurity(request.AuditPath.Trim(), "auditPath", allowedRootPolicy);
    if (auditPathError is not null)
        return auditPathError;

    var auditPath = Path.GetFullPath(request.AuditPath.Trim());
    if (!File.Exists(auditPath))
        return ApiError(404, "COLLECTION-MERGE-ROLLBACK-AUDIT-NOT-FOUND", $"Audit file not found: {auditPath}");

    var rootSet = AuditRollbackRootResolver.Resolve(auditPath);
    if (rootSet.RestoreRoots.Count == 0 || rootSet.CurrentRoots.Count == 0)
        return ApiError(400, "COLLECTION-MERGE-ROLLBACK-ROOTS-UNAVAILABLE", "Rollback roots could not be resolved from audit metadata.");

    if (allowedRootPolicy.IsEnforced
        && (!rootSet.RestoreRoots.All(allowedRootPolicy.IsPathAllowed)
            || !rootSet.CurrentRoots.All(allowedRootPolicy.IsPathAllowed)))
    {
        return ApiError(400, SecurityErrorCodes.OutsideAllowedRoots, "Rollback paths are outside configured AllowedRoots.", ErrorKind.Critical);
    }

    var signing = new AuditSigningService(fileSystem, keyFilePath: AuditSecurityPaths.GetDefaultSigningKeyPath());
    var rollback = signing.Rollback(auditPath, rootSet.RestoreRoots, rootSet.CurrentRoots, dryRun: request.DryRun);
    return Results.Ok(rollback);
})
    .WithSummary("Rollback a collection merge audit using persisted root metadata")
    .Accepts<CollectionMergeRollbackRequest>("application/json")
    .Produces<AuditRollbackResult>(StatusCodes.Status200OK)
    .Produces<OperationErrorResponse>(StatusCodes.Status400BadRequest)
    .Produces<OperationErrorResponse>(StatusCodes.Status404NotFound);

app.MapPost("/export/frontend", async (
    HttpContext ctx,
    IFileSystem fileSystem,
    ICollectionIndex collectionIndex,
    IRunEnvironmentFactory runEnvironmentFactory,
    RunLifecycleManager mgr,
    AllowedRootPathPolicy allowedRootPolicy,
    CancellationToken ct) =>
{
    if (ctx.Request.ContentLength is > 1_048_576)
        return ApiError(400, "EXPORT-BODY-TOO-LARGE", "Request body too large (max 1MB).");

    string body;
    try
    {
        using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8);
        body = await reader.ReadToEndAsync(ct);
    }
    catch (IOException)
    {
        return ApiError(400, "EXPORT-READ-ERROR", "Failed to read request body.");
    }

    ApiFrontendExportRequest? request;
    try
    {
        request = JsonSerializer.Deserialize<ApiFrontendExportRequest>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
    catch (JsonException)
    {
        return ApiError(400, "EXPORT-INVALID-JSON", "Invalid JSON.");
    }

    if (request is null || string.IsNullOrWhiteSpace(request.Frontend))
        return ApiError(400, "EXPORT-FRONTEND-REQUIRED", "frontend is required.");

    if (string.IsNullOrWhiteSpace(request.OutputPath))
        return ApiError(400, "EXPORT-OUTPUT-REQUIRED", "outputPath is required.");

    var outputPathError = ValidatePathSecurity(request.OutputPath.Trim(), "outputPath", allowedRootPolicy);
    if (outputPathError is not null)
        return outputPathError;

    RunRecord? run = null;
    if (!string.IsNullOrWhiteSpace(request.RunId))
    {
        run = mgr.Get(request.RunId);
        if (run is null)
            return ApiError(404, "RUN-NOT-FOUND", "Run not found.", runId: request.RunId);

        if (!CanAccessRun(run, GetClientBindingId(ctx, trustForwardedFor)))
            return ApiError(403, "AUTH-FORBIDDEN", "Run belongs to a different client.", ErrorKind.Critical, runId: request.RunId);
    }

    var roots = request.Roots?.Where(static root => !string.IsNullOrWhiteSpace(root)).ToArray()
        ?? run?.Roots
        ?? Array.Empty<string>();
    if (roots.Length == 0)
        return ApiError(400, "EXPORT-ROOTS-REQUIRED", "roots[] or runId is required.");

    var extensions = request.Extensions?.Where(static ext => !string.IsNullOrWhiteSpace(ext)).ToArray()
        ?? run?.Extensions
        ?? RunOptions.DefaultExtensions;

    foreach (var root in roots)
    {
        var pathError = ValidatePathSecurity(root, "roots", allowedRootPolicy);
        if (pathError is not null)
            return pathError;

        if (!Directory.Exists(root))
            return ApiError(400, "IO-ROOT-NOT-FOUND", $"Root not found: {root}");
    }

    var runOptions = new RunOptions
    {
        Roots = roots,
        Extensions = extensions,
        EnableDat = run?.EnableDat ?? false,
        EnableDatAudit = run?.EnableDatAudit ?? false,
        EnableDatRename = run?.EnableDatRename ?? false,
        DatRoot = run?.DatRoot,
        HashType = run?.HashType ?? RunConstants.DefaultHashType,
        Mode = run?.Mode ?? RunConstants.ModeDryRun
    };

    using var env = runEnvironmentFactory.Create(runOptions);
    try
    {
        var exportResult = await FrontendExportService.ExportAsync(
            new FrontendExportRequest(
                request.Frontend,
                request.OutputPath.Trim(),
                string.IsNullOrWhiteSpace(request.CollectionName) ? "Romulus" : request.CollectionName.Trim(),
                roots,
                extensions),
            fileSystem,
            collectionIndex,
            env.EnrichmentFingerprint,
            runCandidates: run?.CoreRunResult?.AllCandidates,
            ct: ct);

        return Results.Ok(exportResult);
    }
    catch (InvalidOperationException ex)
    {
        return ApiError(409, "EXPORT-NOT-READY", ex.Message);
    }
})
    .WithSummary("Export collection data to frontend-specific artifacts")
    .Produces<FrontendExportResult>(StatusCodes.Status200OK)
    .Produces<OperationErrorResponse>(StatusCodes.Status400BadRequest)
    .Produces<OperationErrorResponse>(StatusCodes.Status403Forbidden)
    .Produces<OperationErrorResponse>(StatusCodes.Status404NotFound)
    .Produces<OperationErrorResponse>(StatusCodes.Status409Conflict);

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
        return ApiError(400, "RUN-INVALID-CONTENT-TYPE", "Content-Type must be application/json.");

    // Read and validate body (max 1MB)
    ctx.Request.EnableBuffering();
    if (ctx.Request.ContentLength is > 1_048_576)
        return ApiError(400, "RUN-BODY-TOO-LARGE", "Request body too large (max 1MB).", ErrorKind.Transient);

    string body;
    using (var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8, leaveOpen: true))
    {
        // Limit read to 1MB + 1 byte to detect oversized chunked bodies
        var buffer = new char[1_048_577];
        var charsRead = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
        if (charsRead > 1_048_576)
            return ApiError(400, "RUN-BODY-TOO-LARGE", "Request body too large (max 1MB).", ErrorKind.Transient);
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
        return ApiError(400, "RUN-INVALID-JSON", "Invalid JSON.");
    }

    if (request is null)
        return ApiError(400, "RUN-INVALID-JSON", "Invalid JSON.");

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
    catch (InvalidOperationException ex)
    {
        var (code, message) = MapRunConfigurationError(ex.Message);
        return ApiError(400, code, message);
    }

    if (request.Roots is null || request.Roots.Length == 0)
        return ApiError(400, "RUN-ROOTS-REQUIRED", "roots[] is required.");

    // Validate roots
    foreach (var root in request.Roots)
    {
        if (string.IsNullOrWhiteSpace(root))
            return ApiError(400, "RUN-ROOT-EMPTY", "Empty root path.");
        if (!Directory.Exists(root))
            return ApiError(400, "IO-ROOT-NOT-FOUND", $"Root not found: {root}");

        var pathError = ValidateRootSecurity(root, allowedRootPolicy);
        if (pathError is not null)
            return pathError;
    }

    var mode = request.Mode ?? "DryRun";
    if (!mode.Equals("DryRun", StringComparison.OrdinalIgnoreCase) &&
        !mode.Equals("Move", StringComparison.OrdinalIgnoreCase))
        return ApiError(400, "RUN-INVALID-MODE", "mode must be DryRun or Move.");

    // Normalize to canonical casing
    mode = mode.Equals("Move", StringComparison.OrdinalIgnoreCase) ? "Move" : "DryRun";

    var idempotencyKey = ctx.Request.Headers["X-Idempotency-Key"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(idempotencyKey))
    {
        if (idempotencyKey.Length > 128 ||
            !idempotencyKey.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.'))
        {
            return ApiError(400, "RUN-INVALID-IDEMPOTENCY-KEY", "Invalid X-Idempotency-Key. Use max 128 chars from [A-Za-z0-9-_.].");
        }
    }

    // TASK-200: Validate PreferRegions to prevent injection
    if (request.PreferRegions is { Length: > 0 })
    {
        // SEC-API-01: Limit array length to prevent abuse
        if (request.PreferRegions.Length > RomCleanup.Contracts.RunConstants.MaxPreferRegions)
            return ApiError(400, "RUN-TOO-MANY-REGIONS", $"PreferRegions must contain at most {RomCleanup.Contracts.RunConstants.MaxPreferRegions} entries.");

        foreach (var region in request.PreferRegions)
        {
            if (string.IsNullOrWhiteSpace(region) || region.Length > 10 ||
                !region.All(c => char.IsLetterOrDigit(c) || c == '-'))
                return ApiError(400, "RUN-INVALID-REGION", $"Invalid region: '{region}'. Only alphanumeric and '-' allowed.");
        }
    }

    // Validate hash type
    if (!string.IsNullOrWhiteSpace(request.HashType))
    {
        var hashType = request.HashType.Trim().ToUpperInvariant();
        if (hashType is not "SHA1" and not "SHA256" and not "MD5")
            return ApiError(400, "RUN-INVALID-HASH-TYPE", "hashType must be one of: SHA1, SHA256, MD5.");
    }

    // Validate extensions
    if (request.Extensions is { Length: > 0 })
    {
        foreach (var extension in request.Extensions)
        {
            if (string.IsNullOrWhiteSpace(extension))
                return ApiError(400, "RUN-INVALID-EXTENSION", "extensions must not contain empty values.");

            var normalized = extension.Trim();
            if (!normalized.StartsWith('.'))
                normalized = "." + normalized;

            if (normalized.Length < 2 || normalized.Length > 20 ||
                !normalized.Skip(1).All(ch => char.IsLetterOrDigit(ch)))
            {
                return ApiError(400, "RUN-INVALID-EXTENSION", $"Invalid extension '{extension}'. Use alphanumeric values like .chd, .iso, .zip.");
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
            return ApiError(400, "RUN-INVALID-CONFLICT-POLICY", "conflictPolicy must be one of: Rename, Skip, Overwrite.");
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
            return ApiError(400, "RUN-INVALID-CONVERT-FORMAT", "convertFormat must be one of: auto, chd, rvz, zip, 7z.");
    }

    // OnlyGames policy guard
    if (!request.OnlyGames && !request.KeepUnknownWhenOnlyGames)
        return ApiError(400, "RUN-INVALID-UNKNOWN-POLICY", "keepUnknownWhenOnlyGames can only be set when onlyGames is true.");

    var waitSync = !string.IsNullOrWhiteSpace(wait) &&
        !string.Equals(wait, "false", StringComparison.OrdinalIgnoreCase);

    var waitTimeoutMs = 600_000;
    if (!string.IsNullOrWhiteSpace(waitTimeoutMsQuery))
    {
        if (!int.TryParse(waitTimeoutMsQuery, out var parsedWaitTimeoutMs) || parsedWaitTimeoutMs < 1 || parsedWaitTimeoutMs > 1_800_000)
            return ApiError(400, "RUN-INVALID-WAIT-TIMEOUT", "waitTimeoutMs must be an integer between 1 and 1800000.");
        waitTimeoutMs = parsedWaitTimeoutMs;
    }

    var ownerClientId = GetClientBindingId(ctx, trustForwardedFor);
    var create = mgr.TryCreateOrReuse(request, mode, idempotencyKey, ownerClientId);
    if (create.Disposition == RunCreateDisposition.ActiveConflict)
    {
        if (create.Run is not null && !CanAccessRun(create.Run, ownerClientId))
            return ApiError(403, "AUTH-FORBIDDEN", "Run belongs to a different client.", ErrorKind.Critical, runId: create.Run.RunId);
        return ApiError(409, "RUN-ACTIVE-CONFLICT", create.Error ?? "Another run is already active.", runId: create.Run?.RunId, meta: CreateMeta(("activeRun", create.Run is null ? null : create.Run.ToDto())));
    }

    if (create.Disposition == RunCreateDisposition.IdempotencyConflict)
    {
        if (create.Run is not null && !CanAccessRun(create.Run, ownerClientId))
            return ApiError(403, "AUTH-FORBIDDEN", "Run belongs to a different client.", ErrorKind.Critical, runId: create.Run.RunId);
        return ApiError(409, "RUN-IDEMPOTENCY-CONFLICT", create.Error ?? "Idempotency key reuse with different payload is not allowed.", runId: create.Run?.RunId, meta: CreateMeta(("run", create.Run is null ? null : create.Run.ToDto())));
    }

    var run = create.Run!;
    if (!CanAccessRun(run, ownerClientId))
        return ApiError(403, "AUTH-FORBIDDEN", "Run belongs to a different client.", ErrorKind.Critical, runId: run.RunId);

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

    if (create.Disposition == RunCreateDisposition.Reused && run.Status != "running")
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
        return ApiError(400, "RUN-INVALID-ID", "Invalid run ID format.");
    var run = mgr.Get(runId);
    if (run is null)
        return ApiError(404, "RUN-NOT-FOUND", "Run not found.", runId: runId);

    if (!CanAccessRun(run, GetClientBindingId(ctx, trustForwardedFor)))
        return ApiError(403, "AUTH-FORBIDDEN", "Run belongs to a different client.", ErrorKind.Critical, runId: runId);

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
        return ApiError(400, "RUN-INVALID-ID", "Invalid run ID format.");
    var run = mgr.Get(runId);
    if (run is null)
        return ApiError(404, "RUN-NOT-FOUND", "Run not found.", runId: runId);
    if (!CanAccessRun(run, GetClientBindingId(ctx, trustForwardedFor)))
        return ApiError(403, "AUTH-FORBIDDEN", "Run belongs to a different client.", ErrorKind.Critical, runId: runId);
    if (run.Status == "running")
        return ApiError(409, "RUN-IN-PROGRESS", "Run still in progress.", runId: runId);
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
        return ApiError(400, "RUN-INVALID-ID", "Invalid run ID format.");

    var run = mgr.Get(runId);
    if (run is null)
        return ApiError(404, "RUN-NOT-FOUND", "Run not found.", runId: runId);
    if (!CanAccessRun(run, GetClientBindingId(ctx, trustForwardedFor)))
        return ApiError(403, "AUTH-FORBIDDEN", "Run belongs to a different client.", ErrorKind.Critical, runId: runId);
    if (run.Status == ApiRunStatus.Running)
        return ApiError(409, "RUN-IN-PROGRESS", "Run still in progress.", runId: runId);

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
        return ApiError(400, "RUN-INVALID-ID", "Invalid run ID format.");

    var run = mgr.Get(runId);
    if (run is null)
        return ApiError(404, "RUN-NOT-FOUND", "Run not found.", runId: runId);
    if (!CanAccessRun(run, GetClientBindingId(ctx, trustForwardedFor)))
        return ApiError(403, "AUTH-FORBIDDEN", "Run belongs to a different client.", ErrorKind.Critical, runId: runId);
    if (run.Status == ApiRunStatus.Running)
        return ApiError(409, "RUN-IN-PROGRESS", "Run still in progress.", runId: runId);

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
        return ApiError(400, "RUN-INVALID-ID", "Invalid run ID format.");
    var current = mgr.Get(runId);
    if (current is null)
        return ApiError(404, "RUN-NOT-FOUND", "Run not found.", runId: runId);
    if (!CanAccessRun(current, GetClientBindingId(ctx, trustForwardedFor)))
        return ApiError(403, "AUTH-FORBIDDEN", "Run belongs to a different client.", ErrorKind.Critical, runId: runId);

    var cancel = mgr.Cancel(runId);
    if (cancel.Disposition == RunCancelDisposition.NotFound)
        return ApiError(404, "RUN-NOT-FOUND", "Run not found.", runId: runId);
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

app.MapPost("/runs/{runId}/rollback", (string runId, HttpContext ctx, string? dryRun, RunLifecycleManager mgr, AllowedRootPathPolicy allowedRootPolicy) =>
{
    if (!Guid.TryParse(runId, out _))
        return ApiError(400, "RUN-INVALID-ID", "Invalid run ID format.");

    var run = mgr.Get(runId);
    if (run is null)
        return ApiError(404, "RUN-NOT-FOUND", "Run not found.", runId: runId);

    if (!CanAccessRun(run, GetClientBindingId(ctx, trustForwardedFor)))
        return ApiError(403, "AUTH-FORBIDDEN", "Run belongs to a different client.", ErrorKind.Critical, runId: runId);

    if (run.Status == "running")
        return ApiError(409, "RUN-IN-PROGRESS", "Rollback is only available for completed runs.", runId: runId);

    if (string.IsNullOrWhiteSpace(run.AuditPath) || !File.Exists(run.AuditPath))
        return ApiError(409, "RUN-ROLLBACK-NOT-AVAILABLE", "No audit artifact available for rollback.", runId: runId);

    // SEC-ROLLBACK-01: Default to dry-run to prevent accidental data changes.
    // Safety sequence: DryRun → Summary → Bestätigung → Apply (Projektregeln §4)
    bool isDryRun = !string.Equals(dryRun, "false", StringComparison.OrdinalIgnoreCase);

    var restoreRoots = run.Roots ?? Array.Empty<string>();
    var currentRoots = string.IsNullOrWhiteSpace(run.TrashRoot)
        ? restoreRoots
        : restoreRoots.Append(run.TrashRoot).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    if (allowedRootPolicy.IsEnforced
        && (restoreRoots.Any(root => !allowedRootPolicy.IsPathAllowed(root))
            || currentRoots.Any(root => !allowedRootPolicy.IsPathAllowed(root))))
    {
        return ApiError(400, SecurityErrorCodes.OutsideAllowedRoots, "Rollback paths are outside configured AllowedRoots.", ErrorKind.Critical, runId: runId);
    }

    var signing = new AuditSigningService(new FileSystemAdapter(), keyFilePath: AuditSecurityPaths.GetDefaultSigningKeyPath());
    var rollback = signing.Rollback(run.AuditPath, restoreRoots, currentRoots, dryRun: isDryRun);

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
        return ApiError(400, "RUN-INVALID-ID", "Invalid run ID format.");

    var run = mgr.Get(runId);
    if (run is null)
        return ApiError(404, "RUN-NOT-FOUND", "Run not found.", runId: runId);

    if (!CanAccessRun(run, GetClientBindingId(ctx, trustForwardedFor)))
        return ApiError(403, "AUTH-FORBIDDEN", "Run belongs to a different client.", ErrorKind.Critical, runId: runId);

    var parsedOffset = 0;
    if (!string.IsNullOrWhiteSpace(offset))
    {
        if (!int.TryParse(offset, out parsedOffset) || parsedOffset < 0)
            return ApiError(400, "RUN-INVALID-REVIEW-OFFSET", "offset must be a non-negative integer.");
    }

    int? parsedLimit = null;
    if (!string.IsNullOrWhiteSpace(limit))
    {
        if (!int.TryParse(limit, out var limitValue) || limitValue < 1 || limitValue > 1000)
            return ApiError(400, "RUN-INVALID-REVIEW-LIMIT", "limit must be an integer between 1 and 1000.");
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
        return ApiError(400, "RUN-INVALID-ID", "Invalid run ID format.");

    var run = mgr.Get(runId);
    if (run is null)
        return ApiError(404, "RUN-NOT-FOUND", "Run not found.", runId: runId);

    if (!CanAccessRun(run, GetClientBindingId(ctx, trustForwardedFor)))
        return ApiError(403, "AUTH-FORBIDDEN", "Run belongs to a different client.", ErrorKind.Critical, runId: runId);

    // SEC-API-04: Reject oversized request bodies to prevent memory exhaustion
    if (ctx.Request.ContentLength is > 1_048_576)
        return ApiError(400, "RUN-PAYLOAD-TOO-LARGE", "Request body exceeds 1 MB limit.");

    ApiReviewApprovalRequest request;
    try
    {
        request = await ctx.Request.ReadFromJsonAsync(ApiJsonSerializerContext.Default.ApiReviewApprovalRequest) ?? new ApiReviewApprovalRequest();
    }
    catch (JsonException)
    {
        return ApiError(400, "RUN-INVALID-JSON", "Invalid JSON.");
    }

    // SEC-API-05: Limit Paths array size to prevent quadratic complexity
    if (request.Paths is { Length: > 10_000 })
        return ApiError(400, "RUN-TOO-MANY-PATHS", "Paths array exceeds 10,000 entries.");

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
    if (coreRunResult is not null && coreRunResult.AllCandidates.Count > 0)
    {
        var approvedPaths = matched
            .Select(static item => item.MainPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var approvedCandidates = coreRunResult.AllCandidates
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
        return ApiError(403, "AUTH-FORBIDDEN", "Automation belongs to a different client.", ErrorKind.Critical);

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
        return ApiError(400, "WATCH-INVALID-JSON", "Invalid JSON.");
    }

    if (request is null)
        return ApiError(400, "WATCH-INVALID-JSON", "Invalid JSON.");

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
    catch (InvalidOperationException ex)
    {
        var (code, message) = MapWatchConfigurationError(ex.Message);
        return ApiError(400, code, message);
    }

    if (request.Roots is null || request.Roots.Length == 0)
        return ApiError(400, "WATCH-ROOTS-REQUIRED", "roots[] is required.");

    var parsedDebounceSeconds = 5;
    if (!string.IsNullOrWhiteSpace(debounceSeconds)
        && (!int.TryParse(debounceSeconds, out parsedDebounceSeconds) || parsedDebounceSeconds < 1 || parsedDebounceSeconds > 300))
    {
        return ApiError(400, "WATCH-INVALID-DEBOUNCE", "debounceSeconds must be an integer between 1 and 300.");
    }

    int? parsedIntervalMinutes = null;
    if (!string.IsNullOrWhiteSpace(intervalMinutes))
    {
        if (!int.TryParse(intervalMinutes, out var intervalValue) || intervalValue < 1 || intervalValue > 10080)
            return ApiError(400, "WATCH-INVALID-INTERVAL", "intervalMinutes must be an integer between 1 and 10080.");

        parsedIntervalMinutes = intervalValue;
    }

    if (parsedIntervalMinutes is null && string.IsNullOrWhiteSpace(cron))
        return ApiError(400, "WATCH-SCHEDULE-REQUIRED", "Specify either intervalMinutes or cron.");

    if (!string.IsNullOrWhiteSpace(cron) && !RomCleanup.Infrastructure.Watch.CronScheduleEvaluator.TestCronMatch(cron.Trim(), DateTime.Now.AddMinutes(1)))
    {
        // best-effort sanity gate: reject obviously invalid cron syntax without introducing parser shadow logic
        var cronFields = cron.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (cronFields.Length != 5)
            return ApiError(400, "WATCH-INVALID-CRON", "cron must contain exactly five fields.");
    }

    foreach (var root in request.Roots)
    {
        if (string.IsNullOrWhiteSpace(root))
            return ApiError(400, "WATCH-ROOT-EMPTY", "Empty root path.");

        var pathError = ValidatePathSecurity(root, "roots", allowedRootPolicy);
        if (pathError is not null)
            return pathError;

        if (!Directory.Exists(root))
            return ApiError(400, "IO-ROOT-NOT-FOUND", $"Root not found: {root}");
    }

    var mode = request.Mode ?? RunConstants.ModeDryRun;
    if (!mode.Equals(RunConstants.ModeDryRun, StringComparison.OrdinalIgnoreCase)
        && !mode.Equals(RunConstants.ModeMove, StringComparison.OrdinalIgnoreCase))
    {
        return ApiError(400, "WATCH-INVALID-MODE", "mode must be DryRun or Move.");
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
        return ApiError(403, "AUTH-FORBIDDEN", "Automation belongs to a different client.", ErrorKind.Critical);

    return Results.Ok(automation.Stop());
})
    .WithSummary("Stop shared watch/schedule automation")
    .Produces<ApiWatchStatus>(StatusCodes.Status200OK)
    .Produces<OperationErrorResponse>(StatusCodes.Status403Forbidden);

app.MapGet("/watch/status", (HttpContext ctx, ApiAutomationService automation) =>
{
    if (!automation.CanAccess(GetClientBindingId(ctx, trustForwardedFor)))
        return ApiError(403, "AUTH-FORBIDDEN", "Automation belongs to a different client.", ErrorKind.Critical);

    return Results.Ok(automation.GetStatus());
})
    .WithSummary("Get shared watch/schedule automation status")
    .Produces<ApiWatchStatus>(StatusCodes.Status200OK)
    .Produces<OperationErrorResponse>(StatusCodes.Status403Forbidden);

app.MapGet("/runs/{runId}/stream", async (string runId, HttpContext ctx, RunLifecycleManager mgr) =>
{
    if (!Guid.TryParse(runId, out _))
    {
        await WriteApiError(ctx, 400, "RUN-INVALID-ID", "Invalid run ID format.");
        return;
    }
    var run = mgr.Get(runId);
    if (run is null)
    {
        await WriteApiError(ctx, 404, "RUN-NOT-FOUND", "Run not found.", runId: runId);
        return;
    }
    if (!CanAccessRun(run, GetClientBindingId(ctx, trustForwardedFor)))
    {
        await WriteApiError(ctx, 403, "AUTH-FORBIDDEN", "Run belongs to a different client.", ErrorKind.Critical, runId: runId);
        return;
    }

    ctx.Response.ContentType = "text/event-stream";
    ctx.Response.Headers["Cache-Control"] = "no-cache";

    var writer = ctx.Response.Body;
    var encoding = Encoding.UTF8;

    await WriteSseEvent(writer, encoding, "ready", new { runId, utc = DateTime.UtcNow.ToString("o") });

    var timeout = TimeSpan.FromSeconds(sseTimeoutSeconds);
    var start = DateTime.UtcNow;
    string? lastStateJson = null;
    var lastHeartbeat = DateTime.UtcNow;

    try
    {
        while (DateTime.UtcNow - start < timeout)
        {
            if (ctx.RequestAborted.IsCancellationRequested)
                break;

            var current = mgr.Get(runId);
            if (current is null)
            {
                await WriteSseEvent(writer, encoding, "error", CreateErrorResponse("RUN-NOT-FOUND", "Run not found.", ErrorKind.Recoverable, runId));
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
            var stateJson = JsonSerializer.Serialize(stateSnapshot);

            if (!string.Equals(stateJson, lastStateJson, StringComparison.Ordinal))
            {
                lastStateJson = stateJson;
                lastHeartbeat = DateTime.UtcNow;
                if (current.Status != "running")
                {
                    var terminalEvent = current.Status switch
                    {
                        "cancelled" => "cancelled",
                        "failed" => "failed",
                        "completed_with_errors" => "completed_with_errors",
                        _ => "completed"
                    };
                    await WriteSseEvent(writer, encoding, terminalEvent, new { run = current.ToDto(), result = current.Result });
                    break;
                }
                await WriteSseEvent(writer, encoding, "status", current.ToDto());
            }
            else if ((DateTime.UtcNow - lastHeartbeat).TotalSeconds >= sseHeartbeatSeconds)
            {
                // V2-H05: SSE heartbeat to prevent proxy/browser timeouts
                await writer.WriteAsync(encoding.GetBytes(":\n\n"));
                await writer.FlushAsync();
                lastHeartbeat = DateTime.UtcNow;
            }

            await Task.Delay(250, ctx.RequestAborted).ContinueWith(_ => { });
        }

        if (DateTime.UtcNow - start >= timeout)
        {
            await WriteSseEvent(writer, encoding, "timeout", new { runId, seconds = sseTimeoutSeconds });
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

// --- DAT Management Endpoints (B2) ---

app.MapGet("/dats/status", async (AllowedRootPathPolicy allowedRootPolicy, CancellationToken ct) =>
    Results.Ok(await DashboardDataBuilder.BuildDatStatusAsync(allowedRootPolicy, ct)))
    .Produces<DashboardDatStatusResponse>(StatusCodes.Status200OK);

app.MapPost("/dats/update", async (HttpContext ctx, AllowedRootPathPolicy allowedRootPolicy) =>
{
    var dataDir = RunEnvironmentBuilder.TryResolveDataDir()
        ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
    var settings = RunEnvironmentBuilder.LoadSettings(dataDir);
    var datRoot = settings.Dat?.DatRoot;

    if (string.IsNullOrWhiteSpace(datRoot))
        return ApiError(400, "DAT-ROOT-NOT-CONFIGURED", "DatRoot is not configured in settings.");

    var datRootError = ValidatePathSecurity(datRoot, "datRoot", allowedRootPolicy);
    if (datRootError is not null)
        return datRootError;

    if (!Directory.Exists(datRoot))
    {
        try { Directory.CreateDirectory(datRoot); }
        catch (Exception ex)
        {
            return ApiError(500, "DAT-ROOT-CREATE-FAILED", $"Cannot create DatRoot: {ex.Message}");
        }
    }

    // Parse optional body for force flag
    bool force = false;
    if (ctx.Request.ContentLength is > 0)
    {
        try
        {
            using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("force", out var forceProp))
                    force = forceProp.GetBoolean();
            }
        }
        catch (JsonException)
        {
            return ApiError(400, "DAT-INVALID-JSON", "Invalid JSON body.");
        }
    }

    var catalogPath = Path.Combine(dataDir, "dat-catalog.json");
    if (!File.Exists(catalogPath))
        return ApiError(404, "DAT-CATALOG-NOT-FOUND", "dat-catalog.json not found.");

    List<DatCatalogEntry> catalog;
    try { catalog = DatSourceService.LoadCatalog(catalogPath); }
    catch (Exception ex) { return ApiError(500, "DAT-CATALOG-LOAD-ERROR", $"Failed to load catalog: {ex.Message}"); }

    if (catalog.Count == 0)
        return ApiError(400, "DAT-CATALOG-EMPTY", "dat-catalog.json contains no entries.");

    int downloaded = 0, skipped = 0, failed = 0;
    var errors = new List<string>();

    using var datService = new DatSourceService(datRoot);
    foreach (var entry in catalog.Where(e => !string.IsNullOrWhiteSpace(e.Url) && !string.Equals(e.Format, "nointro-pack", StringComparison.OrdinalIgnoreCase)))
    {
        var fileName = entry.Id + ".dat";
        var targetPath = Path.Combine(datRoot, fileName);

        if (!force && File.Exists(targetPath))
        {
            skipped++;
            continue;
        }

        try
        {
            var result = await datService.DownloadDatByFormatAsync(entry.Url, fileName, entry.Format, ct: ctx.RequestAborted);
            if (result is not null)
                downloaded++;
            else
            {
                failed++;
                errors.Add($"{entry.Id}: download returned null");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or IOException)
        {
            failed++;
            errors.Add($"{entry.Id}: {ex.Message}");
        }
    }

    return Results.Ok(new
    {
        downloaded,
        skipped,
        failed,
        totalCatalogEntries = catalog.Count,
        force,
        errors = errors.Count > 0 ? errors.ToArray() : null as string[]
    });
});

app.MapPost("/dats/import", async (HttpContext ctx, AllowedRootPathPolicy allowedRootPolicy) =>
{
    var dataDir = RunEnvironmentBuilder.TryResolveDataDir()
        ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
    var settings = RunEnvironmentBuilder.LoadSettings(dataDir);
    var datRoot = settings.Dat?.DatRoot;

    if (string.IsNullOrWhiteSpace(datRoot))
        return ApiError(400, "DAT-ROOT-NOT-CONFIGURED", "DatRoot is not configured in settings.");

    if (!Directory.Exists(datRoot))
        return ApiError(400, "DAT-ROOT-NOT-FOUND", $"DatRoot does not exist: {datRoot}");

    var datRootError = ValidatePathSecurity(datRoot, "datRoot", allowedRootPolicy);
    if (datRootError is not null)
        return datRootError;

    // Read body
    if (ctx.Request.ContentLength is > 1_048_576)
        return ApiError(400, "DAT-BODY-TOO-LARGE", "Request body too large (max 1MB).");

    string body;
    try
    {
        using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8);
        body = await reader.ReadToEndAsync();
    }
    catch (IOException)
    {
        return ApiError(400, "DAT-READ-ERROR", "Failed to read request body.");
    }

    string? sourcePath;
    try
    {
        using var doc = JsonDocument.Parse(body);
        sourcePath = doc.RootElement.TryGetProperty("path", out var pathProp)
            ? pathProp.GetString() : null;
    }
    catch (JsonException)
    {
        return ApiError(400, "DAT-INVALID-JSON", "Invalid JSON body.");
    }

    if (string.IsNullOrWhiteSpace(sourcePath))
        return ApiError(400, "DAT-PATH-REQUIRED", "\"path\" is required in the request body.");

    // Security: validate source path
    var pathError = ValidatePathSecurity(sourcePath.Trim(), "path", allowedRootPolicy);
    if (pathError is not null) return pathError;

    sourcePath = Path.GetFullPath(sourcePath.Trim());

    if (!File.Exists(sourcePath))
        return ApiError(404, "DAT-SOURCE-NOT-FOUND", $"Source file not found: {sourcePath}");

    var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
    if (ext is not ".dat" and not ".xml")
        return ApiError(400, "DAT-INVALID-FORMAT", "Only .dat and .xml files can be imported.");

    try
    {
        var targetPath = DatAnalysisService.ImportDatFileToRoot(sourcePath, datRoot);
        return Results.Ok(new
        {
            imported = true,
            sourcePath,
            targetPath,
            fileName = Path.GetFileName(targetPath)
        });
    }
    catch (InvalidOperationException ex)
    {
        return ApiError(400, "DAT-IMPORT-BLOCKED", ex.Message, ErrorKind.Critical);
    }
    catch (IOException ex)
    {
        return ApiError(500, "DAT-IMPORT-IO-ERROR", $"Import failed: {ex.Message}");
    }
});

// --- Standalone Conversion Endpoint (B3) ---

app.MapPost("/convert", async (HttpContext ctx, AllowedRootPathPolicy allowedRootPolicy) =>
{
    if (ctx.Request.ContentLength is > 1_048_576)
        return ApiError(400, "CONVERT-BODY-TOO-LARGE", "Request body too large (max 1MB).");

    string body;
    try
    {
        using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8);
        body = await reader.ReadToEndAsync();
    }
    catch (IOException)
    {
        return ApiError(400, "CONVERT-READ-ERROR", "Failed to read request body.");
    }

    string? inputPath;
    string? consoleKey = null;
    string? targetFormat = null;
    bool approveConversionReview = false;
    try
    {
        using var doc = JsonDocument.Parse(body);
        inputPath = doc.RootElement.TryGetProperty("input", out var inp) ? inp.GetString() : null;
        if (doc.RootElement.TryGetProperty("consoleKey", out var ck)) consoleKey = ck.GetString();
        if (doc.RootElement.TryGetProperty("target", out var tf)) targetFormat = tf.GetString();
        if (doc.RootElement.TryGetProperty("approveConversionReview", out var approval)) approveConversionReview = approval.GetBoolean();
    }
    catch (JsonException)
    {
        return ApiError(400, "CONVERT-INVALID-JSON", "Invalid JSON body.");
    }

    if (string.IsNullOrWhiteSpace(inputPath))
        return ApiError(400, "CONVERT-INPUT-REQUIRED", "\"input\" path is required.");

    // Security: validate input path
    var pathError = ValidatePathSecurity(inputPath.Trim(), "input", allowedRootPolicy);
    if (pathError is not null) return pathError;

    inputPath = Path.GetFullPath(inputPath.Trim());

    if (!File.Exists(inputPath) && !Directory.Exists(inputPath))
        return ApiError(404, "CONVERT-INPUT-NOT-FOUND", $"Input not found: {inputPath}");

    using var service = StandaloneConversionService.Create(inputPath, approveConversionReview);
    if (service is null)
        return ApiError(500, "CONVERT-NO-CONVERTER", "No converter available. Check tool installation.");

    if (File.Exists(inputPath))
    {
        var result = service.ConvertFile(inputPath, consoleKey, targetFormat, ctx.RequestAborted);
        return Results.Ok(new
        {
            input = inputPath,
            outcome = result.Outcome.ToString(),
            targetPath = result.TargetPath,
            reason = result.Reason,
            converted = result.Outcome == ConversionOutcome.Success ? 1 : 0,
            skipped = result.Outcome == ConversionOutcome.Skipped ? 1 : 0,
            errors = (result.Outcome != ConversionOutcome.Success && result.Outcome != ConversionOutcome.Skipped) ? 1 : 0
        });
    }
    else
    {
        var report = service.ConvertDirectory(inputPath, consoleKey, targetFormat, cancellationToken: ctx.RequestAborted);
        return Results.Ok(new
        {
            input = inputPath,
            converted = report.Converted,
            skipped = report.Skipped,
            errors = report.Errors,
            results = report.Results.Select(r => new
            {
                source = r.SourcePath,
                target = r.TargetPath,
                outcome = r.Outcome.ToString(),
                reason = r.Reason
            }).ToArray()
        });
    }
});

// --- Completeness Report Endpoint (B4) ---

app.MapGet("/runs/{runId}/completeness", async (string runId, HttpContext ctx, RunLifecycleManager mgr, AllowedRootPathPolicy allowedRootPolicy, CancellationToken ct) =>
{
    if (!Guid.TryParse(runId, out _))
        return ApiError(400, "RUN-INVALID-ID", "Invalid run ID format.");

    var run = mgr.Get(runId);
    if (run is null)
        return ApiError(404, "RUN-NOT-FOUND", "Run not found.", runId: runId);

    if (!CanAccessRun(run, GetClientBindingId(ctx, trustForwardedFor)))
        return ApiError(403, "AUTH-FORBIDDEN", "Run belongs to a different client.", ErrorKind.Critical, runId: runId);

    if (run.Status == "running")
        return ApiError(409, "RUN-IN-PROGRESS", "Run still in progress.", runId: runId);

    // Try to build completeness from the run's DAT index and roots
    var dataDir = RunEnvironmentBuilder.TryResolveDataDir()
        ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
    var settings = RunEnvironmentBuilder.LoadSettings(dataDir);

    var runRoots = run.Roots ?? Array.Empty<string>();
    if (runRoots.Length == 0)
        return ApiError(400, "RUN-NO-ROOTS", "Run has no roots configured.", runId: runId);

    foreach (var runRoot in runRoots)
    {
        var pathError = ValidatePathSecurity(runRoot, "roots", allowedRootPolicy);
        if (pathError is not null)
            return pathError;
    }

    var effectiveDatRoot = run.DatRoot ?? settings.Dat?.DatRoot;
    if (!string.IsNullOrWhiteSpace(effectiveDatRoot))
    {
        var pathError = ValidatePathSecurity(effectiveDatRoot, "datRoot", allowedRootPolicy);
        if (pathError is not null)
            return pathError;
    }

    // Build DAT index from settings
    var runOptions = new RomCleanup.Contracts.Models.RunOptions
    {
        Roots = runRoots,
        EnableDat = true,
        DatRoot = effectiveDatRoot,
        Extensions = run.Extensions
    };

    using var env = new RunEnvironmentFactory().Create(runOptions);
    if (env.DatIndex is null || env.DatIndex.TotalEntries == 0)
        return ApiError(400, "DAT-NOT-AVAILABLE", "No DAT index available. Configure DatRoot in settings.", runId: runId);

    var report = await CompletenessReportService.BuildAsync(
        env.DatIndex,
        runOptions.Roots,
        env.CollectionIndex,
        runOptions.Extensions,
        run.CoreRunResult?.AllCandidates,
        ct);

    return Results.Ok(new
    {
        runId,
        source = report.Source,
        sourceItemCount = report.SourceItemCount,
        entries = report.Entries.Select(e => new
        {
            e.ConsoleKey,
            e.TotalInDat,
            e.Verified,
            e.MissingCount,
            e.Percentage,
            missingGames = e.MissingGames.Take(100).ToArray(),
            truncated = e.MissingGames.Count > 100
        }).ToArray(),
        totalInDat = report.Entries.Sum(e => e.TotalInDat),
        totalVerified = report.Entries.Sum(e => e.Verified),
        totalMissing = report.Entries.Sum(e => e.MissingCount),
        overallPercentage = report.Entries.Sum(e => e.TotalInDat) > 0
            ? Math.Round(100.0 * report.Entries.Sum(e => e.Verified) / report.Entries.Sum(e => e.TotalInDat), 1)
            : 0.0
    });
});

// Graceful shutdown: cancel active runs before process exits
app.Lifetime.ApplicationStopping.Register(() =>
{
    var mgr = app.Services.GetRequiredService<RunLifecycleManager>();
    mgr.ShutdownAsync().GetAwaiter().GetResult();
});

// V2-H08: Warn if binding is not loopback-only (security risk without TLS)
foreach (var url in app.Urls)
{
    if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
    {
        var host = uri.Host;
        if (host != "127.0.0.1" && host != "localhost" && host != "[::1]" && host != "0.0.0.0")
        {
            Console.WriteLine($"[SECURITY WARNING] Binding to non-loopback address: {url}");
            Console.WriteLine("  API key is transmitted in plain text without TLS.");
            Console.WriteLine("  Consider enabling HTTPS or binding to 127.0.0.1 only.");
        }
        else if (host == "0.0.0.0")
        {
            Console.WriteLine($"[SECURITY WARNING] Binding to all interfaces: {url}");
            Console.WriteLine("  API key will be exposed on the network without TLS.");
            Console.WriteLine("  Use 127.0.0.1 for local-only access or enable HTTPS.");
        }
        if (uri.Scheme == "http" && host != "127.0.0.1" && host != "localhost" && host != "[::1]")
        {
            Console.WriteLine("  Strongly recommend using HTTPS for non-loopback bindings.");
        }
    }
}

app.Run();

// --- Helpers ---

static bool FixedTimeEquals(string expected, string? actual)
{
    if (actual is null) return false;
    // HMAC both values to normalize length before comparison,
    // eliminating the length oracle from FixedTimeEquals.
    var key = Encoding.UTF8.GetBytes(expected);
    var a = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(expected));
    var b = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(actual));
    return CryptographicOperations.FixedTimeEquals(a, b);
}

static void SafeConsoleWriteLine(string message)
{
    try
    {
        Console.WriteLine(message);
    }
    catch (ObjectDisposedException)
    {
        // Some tests temporarily replace/dispose Console.Out. Logging must never break request handling.
    }
}

static async Task WriteSseEvent(Stream stream, Encoding encoding, string eventName, object data)
{
    // SEC: Prevent SSE event injection via newlines in event name
    var safeEventName = SanitizeSseEventName(eventName);
    var json = JsonSerializer.Serialize(data);
    var payload = $"event: {safeEventName}\ndata: {json}\n\n";
    await stream.WriteAsync(encoding.GetBytes(payload));
    await stream.FlushAsync();
}

static string? SanitizeCorrelationId(string? raw)
{
    if (string.IsNullOrWhiteSpace(raw)) return null;
    if (raw.Length > 64) return null;
    // Only allow printable ASCII, no control chars / newlines / whitespace besides space
    foreach (var ch in raw)
    {
        if (ch < 0x21 || ch > 0x7E) return null; // reject control chars, newlines, non-ASCII
    }
    return raw;
}

static string? SanitizeClientBindingId(string? raw)
{
    if (string.IsNullOrWhiteSpace(raw)) return null;
    if (raw.Length > 64) return null;
    foreach (var ch in raw)
    {
        if (!(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.'))
            return null;
    }

    return raw;
}

static bool IsAnonymousEndpoint(PathString path)
{
    return path.Equals("/healthz", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/dashboard/bootstrap", StringComparison.OrdinalIgnoreCase);
}

static string ResolveCorsOrigin(string mode, string customOrigin)
{
    return mode switch
    {
        "local-dev" => "http://localhost:3000",
        "strict-local" => "http://127.0.0.1",
        "custom" => IsValidCorsOrigin(customOrigin) ? customOrigin : "http://127.0.0.1",
        _ => "http://127.0.0.1"
    };
}

static bool IsValidCorsOrigin(string origin)
{
    if (string.IsNullOrWhiteSpace(origin))
        return false;

    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        return false;

    return uri.Scheme is "http" or "https" && !string.IsNullOrWhiteSpace(uri.Host);
}

static string GetClientBindingId(HttpContext context, bool trustForwardedFor)
{
    if (context.Items.TryGetValue("ClientBindingId", out var existing) && existing is string cached && !string.IsNullOrWhiteSpace(cached))
        return cached;

    var rawClientId = context.Request.Headers["X-Client-Id"].FirstOrDefault();
    var resolved = SanitizeClientBindingId(rawClientId)
        ?? ApiClientIdentity.ResolveRateLimitClientId(context, trustForwardedFor);
    context.Items["ClientBindingId"] = resolved;
    return resolved;
}

static bool CanAccessRun(RunRecord run, string requesterClientId)
{
    if (string.IsNullOrWhiteSpace(run.OwnerClientId))
        return true;

    return string.Equals(run.OwnerClientId, requesterClientId, StringComparison.Ordinal);
}

static ApiRunList BuildRunList(IReadOnlyList<RunRecord> runs, int offset = 0, int? limit = null)
{
    var safeOffset = Math.Min(offset, runs.Count);
    var pageSize = limit ?? Math.Max(runs.Count - safeOffset, 0);
    var pageRuns = limit is null
        ? runs.Skip(safeOffset).ToArray()
        : runs.Skip(safeOffset).Take(limit.Value).ToArray();

    return new ApiRunList
    {
        Total = runs.Count,
        Offset = offset,
        Limit = pageSize,
        Returned = pageRuns.Length,
        HasMore = safeOffset + pageRuns.Length < runs.Count,
        Runs = pageRuns.Select(run => run.ToDto()).ToArray()
    };
}

static ApiRunHistoryList BuildRunHistoryList(CollectionRunHistoryPage page)
{
    return new ApiRunHistoryList
    {
        Total = page.Total,
        Offset = page.Offset,
        Limit = page.Limit,
        Returned = page.Returned,
        HasMore = page.HasMore,
        Runs = page.Runs
            .Select(snapshot => new ApiRunHistoryEntry
        {
            RunId = snapshot.RunId,
            StartedUtc = snapshot.StartedUtc,
            CompletedUtc = snapshot.CompletedUtc,
            Mode = snapshot.Mode,
            Status = snapshot.Status,
            RootCount = snapshot.RootCount,
            RootFingerprint = snapshot.RootFingerprint,
            DurationMs = snapshot.DurationMs,
            TotalFiles = snapshot.TotalFiles,
            CollectionSizeBytes = snapshot.CollectionSizeBytes,
            Games = snapshot.Games,
            Dupes = snapshot.Dupes,
            Junk = snapshot.Junk,
            DatMatches = snapshot.DatMatches,
            ConvertedCount = snapshot.ConvertedCount,
            FailCount = snapshot.FailCount,
            SavedBytes = snapshot.SavedBytes,
            ConvertSavedBytes = snapshot.ConvertSavedBytes,
            HealthScore = snapshot.HealthScore
        })
            .ToArray()
    };
}

static async Task<ApiReviewQueue> BuildReviewQueueAsync(
    RunRecord run,
    PersistedReviewDecisionService? reviewDecisionService,
    int offset = 0,
    int? limit = null,
    CancellationToken ct = default)
{
    var core = run.CoreRunResult;
    if (core is null)
    {
        return new ApiReviewQueue
        {
            RunId = run.RunId,
            Total = 0,
            Offset = offset,
            Limit = limit ?? 0,
            Returned = 0,
            HasMore = false,
            Items = Array.Empty<ApiReviewItem>()
        };
    }

    var approvedPaths = reviewDecisionService is null
        ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        : await reviewDecisionService.GetApprovedPathSetAsync(
            core.AllCandidates.Select(static candidate => candidate.MainPath).ToArray(),
            ct);

    var items = core.AllCandidates
        .Where(c => c.SortDecision is SortDecision.Review or SortDecision.Blocked or SortDecision.Unknown)
        .OrderBy(c => c.ConsoleKey, StringComparer.OrdinalIgnoreCase)
        .ThenBy(c => c.MainPath, StringComparer.OrdinalIgnoreCase)
        .Select(c => new ApiReviewItem
        {
            MainPath = c.MainPath,
            FileName = Path.GetFileName(c.MainPath),
            ConsoleKey = c.ConsoleKey,
            SortDecision = c.SortDecision.ToString(),
            DecisionClass = c.DecisionClass.ToString(),
            EvidenceTier = c.EvidenceTier.ToString(),
            PrimaryMatchKind = c.PrimaryMatchKind.ToString(),
            PlatformFamily = c.PlatformFamily.ToString(),
            MatchLevel = c.MatchEvidence.Level.ToString(),
            MatchReasoning = c.MatchEvidence.Reasoning,
            DetectionConfidence = c.DetectionConfidence,
            Approved = run.IsReviewPathApproved(c.MainPath) || approvedPaths.Contains(c.MainPath)
        })
        .ToArray();

    var safeOffset = Math.Min(offset, items.Length);
    var pageSize = limit ?? Math.Max(items.Length - safeOffset, 0);
    var pageItems = limit is null
        ? items.Skip(safeOffset).ToArray()
        : items.Skip(safeOffset).Take(limit.Value).ToArray();

    return new ApiReviewQueue
    {
        RunId = run.RunId,
        Total = items.Length,
        Offset = offset,
        Limit = pageSize,
        Returned = pageItems.Length,
        HasMore = safeOffset + pageItems.Length < items.Length,
        Items = pageItems
    };
}

static string SanitizeSseEventName(string eventName)
{
    // SSE event names must be single-line printable ASCII
    foreach (var ch in eventName)
    {
        if (ch is '\n' or '\r' or ':') return "error";
    }
    return eventName;
}

static IResult CreateArtifactDownloadResult(
    string? artifactPath,
    string contentType,
    string fallbackFileName,
    string unavailableCode,
    string unavailableMessage,
    string runId)
{
    if (string.IsNullOrWhiteSpace(artifactPath) || !File.Exists(artifactPath))
        return ApiError(409, unavailableCode, unavailableMessage, runId: runId);

    var downloadName = Path.GetFileName(artifactPath);
    if (string.IsNullOrWhiteSpace(downloadName))
        downloadName = fallbackFileName;

    return Results.File(artifactPath, contentType, downloadName);
}

static IResult? ValidateRootSecurity(string root, AllowedRootPathPolicy allowedRootPolicy)
{
    try
    {
        var dirInfo = new DirectoryInfo(root);
        if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            return ApiError(400, SecurityErrorCodes.RootReparsePoint, $"Symlink/junction not allowed as root: {root}", ErrorKind.Critical);
        }
    }
    catch (Exception ex)
    {
        return ApiError(400, SecurityErrorCodes.RootAttributeCheckFailed,
            $"Cannot verify attributes for root: {root} ({ex.GetType().Name})", ErrorKind.Critical);
    }

    var full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
    if (SafetyValidator.IsProtectedSystemPath(full))
        return ApiError(400, SecurityErrorCodes.SystemDirectoryRoot, $"System directory not allowed: {root}", ErrorKind.Critical);
    if (SafetyValidator.IsDriveRoot(full))
        return ApiError(400, SecurityErrorCodes.DriveRootNotAllowed, $"Drive root not allowed: {root}", ErrorKind.Critical);
    if (allowedRootPolicy.IsEnforced && !allowedRootPolicy.IsPathAllowed(full))
    {
        return ApiError(400, SecurityErrorCodes.OutsideAllowedRoots, $"Root is outside configured AllowedRoots: {root}", ErrorKind.Critical);
    }

    return null;
}

static IResult? ValidateCollectionScopeSecurity(
    CollectionSourceScope scope,
    string fieldName,
    AllowedRootPathPolicy allowedRootPolicy,
    bool requireExistingRoots)
{
    if (scope.Roots.Count == 0)
        return ApiError(400, $"COLLECTION-{fieldName.ToUpperInvariant()}-ROOTS-REQUIRED", $"{fieldName}.roots[] is required.");

    foreach (var root in scope.Roots)
    {
        if (string.IsNullOrWhiteSpace(root))
            return ApiError(400, $"COLLECTION-{fieldName.ToUpperInvariant()}-ROOT-EMPTY", $"Empty root path in {fieldName}.roots.");

        var pathError = ValidateRootSecurity(root, allowedRootPolicy);
        if (pathError is not null)
            return pathError;

        if (requireExistingRoots && !Directory.Exists(root))
            return ApiError(400, "IO-ROOT-NOT-FOUND", $"Root not found: {root}");
    }

    return null;
}

static IResult? ValidateCollectionMergeRequest(CollectionMergeRequest request, AllowedRootPathPolicy allowedRootPolicy)
{
    var leftValidation = ValidateCollectionScopeSecurity(request.CompareRequest.Left, "left", allowedRootPolicy, requireExistingRoots: true);
    if (leftValidation is not null)
        return leftValidation;

    var rightValidation = ValidateCollectionScopeSecurity(request.CompareRequest.Right, "right", allowedRootPolicy, requireExistingRoots: true);
    if (rightValidation is not null)
        return rightValidation;

    if (request.CompareRequest.Limit < 1 || request.CompareRequest.Limit > 5000)
        return ApiError(400, "COLLECTION-MERGE-INVALID-LIMIT", "compareRequest.limit must be an integer between 1 and 5000.");

    if (string.IsNullOrWhiteSpace(request.TargetRoot))
        return ApiError(400, "COLLECTION-MERGE-TARGET-REQUIRED", "targetRoot is required.");

    return ValidatePathSecurity(request.TargetRoot.Trim(), "targetRoot", allowedRootPolicy);
}

static IResult? ValidatePathSecurity(string path, string fieldName, AllowedRootPathPolicy? allowedRootPolicy = null)
{
    if (string.IsNullOrWhiteSpace(path)) return null;

    string full;
    try { full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar); }
    catch (Exception ex) when (ex is ArgumentException or NotSupportedException or System.Security.SecurityException) { return ApiError(400, SecurityErrorCodes.InvalidPath, $"Invalid path for {fieldName}.", ErrorKind.Critical); }

    // Block reparse points
    try
    {
        if (Directory.Exists(full))
        {
            var dirInfo = new DirectoryInfo(full);
            if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                return ApiError(400, SecurityErrorCodes.ReparsePoint, $"Symlink/junction not allowed for {fieldName}.", ErrorKind.Critical);
        }
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
    {
        return ApiError(400, SecurityErrorCodes.AttributeCheckFailed, $"Cannot verify attributes for {fieldName}: {ex.GetType().Name}.", ErrorKind.Critical);
    }

    // Block system directories (single source of truth: SafetyValidator)
    if (RomCleanup.Infrastructure.Safety.SafetyValidator.IsProtectedSystemPath(full))
        return ApiError(400, SecurityErrorCodes.SystemDirectory, $"System directory not allowed for {fieldName}.", ErrorKind.Critical);

    // Block drive root
    if (RomCleanup.Infrastructure.Safety.SafetyValidator.IsDriveRoot(full))
        return ApiError(400, SecurityErrorCodes.DriveRoot, $"Drive root not allowed for {fieldName}.", ErrorKind.Critical);

    if (allowedRootPolicy?.IsEnforced == true && !allowedRootPolicy.IsPathAllowed(full))
    {
        return ApiError(400, SecurityErrorCodes.OutsideAllowedRoots, $"Path for {fieldName} is outside configured AllowedRoots.", ErrorKind.Critical);
    }

    return null;
}

static async Task<(T? Value, IResult? Error)> ReadJsonBodyAsync<T>(
    HttpContext context,
    string codePrefix,
    CancellationToken ct)
{
    if (context.Request.ContentLength is > 1_048_576)
        return (default, ApiError(400, $"{codePrefix}-BODY-TOO-LARGE", "Request body too large (max 1MB)."));

    string body;
    try
    {
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
        body = await reader.ReadToEndAsync(ct);
    }
    catch (IOException)
    {
        return (default, ApiError(400, $"{codePrefix}-READ-ERROR", "Failed to read request body."));
    }

    if (string.IsNullOrWhiteSpace(body))
        return (default, ApiError(400, $"{codePrefix}-INVALID-JSON", "Invalid JSON."));

    try
    {
        var value = JsonSerializer.Deserialize<T>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return value is null
            ? (default, ApiError(400, $"{codePrefix}-INVALID-JSON", "Invalid JSON."))
            : (value, null);
    }
    catch (JsonException)
    {
        return (default, ApiError(400, $"{codePrefix}-INVALID-JSON", "Invalid JSON."));
    }
}

static IResult ApiError(
    int statusCode,
    string code,
    string message,
    ErrorKind kind = ErrorKind.Recoverable,
    string? runId = null,
    IDictionary<string, object>? meta = null)
{
    return Results.Json(CreateErrorResponse(code, message, kind, runId, meta), statusCode: statusCode);
}

static Task WriteApiError(
    HttpContext context,
    int statusCode,
    string code,
    string message,
    ErrorKind kind = ErrorKind.Recoverable,
    string? runId = null,
    IDictionary<string, object>? meta = null)
{
    context.Response.StatusCode = statusCode;
    return context.Response.WriteAsJsonAsync(CreateErrorResponse(code, message, kind, runId, meta));
}

static OperationErrorResponse CreateErrorResponse(
    string code,
    string message,
    ErrorKind kind = ErrorKind.Recoverable,
    string? runId = null,
    IDictionary<string, object>? meta = null)
{
    return new OperationErrorResponse(new OperationError(code, message, kind, "API"), runId, meta);
}

static IDictionary<string, object> CreateMeta(params (string Key, object? Value)[] entries)
{
    var meta = new Dictionary<string, object>(StringComparer.Ordinal);
    foreach (var (key, value) in entries)
    {
        if (value is not null)
            meta[key] = value;
    }

    return meta;
}

static (string Code, string Message) MapRunConfigurationError(string message)
{
    if (message.Contains("protected system path", StringComparison.OrdinalIgnoreCase))
        return (SecurityErrorCodes.SystemDirectory, message);

    if (message.Contains("drive root", StringComparison.OrdinalIgnoreCase))
        return (SecurityErrorCodes.DriveRoot, message);

    if (message.Contains("UNC path", StringComparison.OrdinalIgnoreCase) ||
        message.Contains(" is invalid:", StringComparison.OrdinalIgnoreCase))
    {
        return (SecurityErrorCodes.InvalidPath, message);
    }

    if (message.Contains("Invalid region", StringComparison.OrdinalIgnoreCase))
        return ("RUN-INVALID-REGION", message);

    if (message.Contains("Invalid extension", StringComparison.OrdinalIgnoreCase))
        return ("RUN-INVALID-EXTENSION", message);

    if (message.Contains("Invalid hashType", StringComparison.OrdinalIgnoreCase))
        return ("RUN-INVALID-HASH-TYPE", message);

    if (message.Contains("Invalid convertFormat", StringComparison.OrdinalIgnoreCase))
        return ("RUN-INVALID-CONVERT-FORMAT", message);

    if (message.Contains("Invalid conflictPolicy", StringComparison.OrdinalIgnoreCase))
        return ("RUN-INVALID-CONFLICT-POLICY", message);

    if (message.Contains("Invalid mode", StringComparison.OrdinalIgnoreCase))
        return ("RUN-INVALID-MODE", message);

    if (message.Contains("Workflow '", StringComparison.OrdinalIgnoreCase))
        return ("RUN-WORKFLOW-NOT-FOUND", message);

    if (message.Contains("Profile '", StringComparison.OrdinalIgnoreCase))
        return ("RUN-PROFILE-NOT-FOUND", message);

    return ("RUN-INVALID-CONFIG", message);
}

static (string Code, string Message) MapWatchConfigurationError(string message)
{
    if (message.Contains("protected system path", StringComparison.OrdinalIgnoreCase))
        return (SecurityErrorCodes.SystemDirectory, message);

    if (message.Contains("drive root", StringComparison.OrdinalIgnoreCase))
        return (SecurityErrorCodes.DriveRoot, message);

    if (message.Contains("UNC path", StringComparison.OrdinalIgnoreCase) ||
        message.Contains(" is invalid:", StringComparison.OrdinalIgnoreCase))
    {
        return (SecurityErrorCodes.InvalidPath, message);
    }

    if (message.Contains("Invalid region", StringComparison.OrdinalIgnoreCase))
        return ("WATCH-INVALID-REGION", message);

    if (message.Contains("Invalid extension", StringComparison.OrdinalIgnoreCase))
        return ("WATCH-INVALID-EXTENSION", message);

    if (message.Contains("Invalid hashType", StringComparison.OrdinalIgnoreCase))
        return ("WATCH-INVALID-HASH-TYPE", message);

    if (message.Contains("Invalid convertFormat", StringComparison.OrdinalIgnoreCase))
        return ("WATCH-INVALID-CONVERT-FORMAT", message);

    if (message.Contains("Invalid conflictPolicy", StringComparison.OrdinalIgnoreCase))
        return ("WATCH-INVALID-CONFLICT-POLICY", message);

    if (message.Contains("Invalid mode", StringComparison.OrdinalIgnoreCase))
        return ("WATCH-INVALID-MODE", message);

    if (message.Contains("Workflow '", StringComparison.OrdinalIgnoreCase))
        return ("WATCH-WORKFLOW-NOT-FOUND", message);

    if (message.Contains("Profile '", StringComparison.OrdinalIgnoreCase))
        return ("WATCH-PROFILE-NOT-FOUND", message);

    return ("WATCH-INVALID-CONFIG", message);
}

internal sealed class ApiFrontendExportRequest
{
    public string? Frontend { get; set; }
    public string? OutputPath { get; set; }
    public string? CollectionName { get; set; }
    public string[]? Roots { get; set; }
    public string[]? Extensions { get; set; }
    public string? RunId { get; set; }
}

public partial class Program
{
    // V2-H10: API version from assembly metadata, not hardcoded
    internal static readonly string ApiVersion =
        typeof(Program).Assembly.GetName().Version?.ToString(2) ?? "1.0";
}
