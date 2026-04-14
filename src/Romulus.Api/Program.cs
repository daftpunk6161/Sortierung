using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Mvc;
using Romulus.Api;
using Romulus.Contracts;
using Romulus.Contracts.Errors;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure;
using Romulus.Infrastructure.Analysis;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.Conversion;
using Romulus.Infrastructure.Dat;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Index;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Export;
using Romulus.Infrastructure.Profiles;
using Romulus.Infrastructure.Review;
using Romulus.Infrastructure.Safety;
using Romulus.Infrastructure.Workflow;

var builder = WebApplication.CreateBuilder(args);

var configuredApiKey = builder.Configuration["ApiKey"]
                       ?? Environment.GetEnvironmentVariable("ROM_CLEANUP_API_KEY");
var headlessOptions = HeadlessApiOptions.FromConfiguration(builder.Configuration);
headlessOptions.Validate(configuredApiKey, builder.Environment.IsDevelopment());

var port = headlessOptions.Port;
var bindAddress = headlessOptions.BindAddress;
builder.WebHost.UseUrls($"http://{bindAddress}:{port}");
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 1_048_576; // SEC-BODY-01: 1 MB hard limit at transport level
});

builder.Services.AddRomulusCore();
builder.Services.AddHttpClient(DatSourceService.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Romulus/2.0 (DAT-Updater)");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/zip, application/octet-stream, application/xml, text/xml, */*");
});
builder.Services.AddSingleton(headlessOptions);
builder.Services.AddSingleton(new AllowedRootPathPolicy(headlessOptions.AllowedRoots));
builder.Services.AddSingleton<RunManager>();
builder.Services.AddSingleton<RunLifecycleManager>(sp =>
    sp.GetRequiredService<RunManager>().Lifecycle);
builder.Services.AddSingleton<ApiAutomationService>();
builder.Services.AddOpenApi(OpenApiSpec.DocumentName, OpenApiSpec.Configure);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    // FIN-06 / I18N-07: keep user-facing non-ASCII names readable in JSON output.
    options.SerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
});

var app = builder.Build();
var timeProvider = app.Services.GetRequiredService<ITimeProvider>();

app.UseExceptionHandler(exceptionApp =>
{
    exceptionApp.Run(async context =>
    {
        var exceptionFeature = context.Features.Get<IExceptionHandlerFeature>();
        if (exceptionFeature?.Error is not null)
            app.Logger.LogError(
                "Unhandled exception captured by global exception handler. ExceptionType={ExceptionType}",
                exceptionFeature.Error.GetType().Name);

        if (context.Response.HasStarted)
            return;

        context.Response.Clear();
        await WriteApiError(
            context,
            StatusCodes.Status500InternalServerError,
            ApiErrorCodes.InternalError,
            "An unexpected error occurred.",
            ErrorKind.Critical);
    });
});

// --- Middleware ---
var apiKeys = ParseApiKeys(configuredApiKey);
if (apiKeys.Count == 0)
{
    if (app.Environment.IsDevelopment() && !headlessOptions.AllowRemoteClients)
    {
        var generatedApiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        apiKeys = [generatedApiKey];
        Console.WriteLine($"[Dev] Generated API key: {generatedApiKey}");
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
var trustForwardedForConfigured = builder.Configuration.GetValue("TrustForwardedFor", false);
var trustForwardedFor = trustForwardedForConfigured && IsLoopbackBindAddress(bindAddress);
if (trustForwardedForConfigured && !trustForwardedFor)
{
    SafeConsoleWriteLine("[SECURITY WARNING] TrustForwardedFor ignored because BindAddress is not loopback-only.");
    SafeConsoleWriteLine("[SECURITY WARNING] Configure the API behind a local reverse proxy and bind to 127.0.0.1 or localhost.");
}
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
        await WriteApiError(ctx, 400, ApiErrorCodes.AuthInvalidClientId, "Invalid X-Client-Id. Use max 64 chars from [A-Za-z0-9-_.].", ErrorKind.Critical);
        return;
    }
    var clientBindingId = SanitizeClientBindingId(rawClientId) ?? clientIp;
    ctx.Items["ClientBindingId"] = clientBindingId;

    // API key validation (fixed-time comparison)
    var providedKey = ctx.Request.Headers["X-Api-Key"].FirstOrDefault();
    if (!FixedTimeEqualsAny(apiKeys, providedKey))
    {
        await WriteApiError(ctx, 401, ApiErrorCodes.AuthUnauthorized, "Unauthorized", ErrorKind.Critical);
        return;
    }

    // R3-006 FIX: Use stable client binding identity, not raw user-provided key.
    // Raw providedKey allows timing-based bucket enumeration by key probing.
    var rateLimitBucket = BuildRateLimitBucketId(clientBindingId);

    if (!rateLimiter.TryAcquire(rateLimitBucket))
    {
        ctx.Response.Headers["Retry-After"] = Math.Max(1, (int)Math.Ceiling(rateLimitWindow.TotalSeconds)).ToString();
        await WriteApiError(ctx, 429, ApiErrorCodes.RunRateLimit, "Too many requests.", ErrorKind.Transient);
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

    var start = timeProvider.UtcNow.UtcDateTime;
    await next();
    var elapsed = (timeProvider.UtcNow.UtcDateTime - start).TotalMilliseconds;
    var method = ctx.Request.Method;
    var path = ctx.Request.Path;
    var status = ctx.Response.StatusCode;
    SafeConsoleWriteLine($"[{start:o}] {correlationId} {method} {path} → {status} ({elapsed:F0}ms)");
});

// --- Endpoints ---

app.MapGet("/healthz", () => Results.Ok(new
{
    status = RunConstants.StatusOk,
    serverRunning = true,
    utc = timeProvider.UtcNow.ToString("o"),
    version = ApiVersion
}))
    .WithSummary("Unauthenticated local liveness probe");

app.MapGet("/health", (RunLifecycleManager mgr) =>
{
    var activeRun = mgr.GetActive();
    return Results.Ok(new
    {
        status = RunConstants.StatusOk,
        serverRunning = true,
        hasActiveRun = activeRun is not null,
        utc = timeProvider.UtcNow.ToString("o"),
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

MapRunReadEndpoints(app, trustForwardedFor);
MapProfileWorkflowEndpoints(app);

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
        return ApiError(400, ApiErrorCodes.CollectionCompareInvalidLimit, "limit must be an integer between 1 and 5000.");

    var leftValidation = ValidateCollectionScopeSecurity(request.Left, "left", allowedRootPolicy, requireExistingRoots: true);
    if (leftValidation is not null)
        return leftValidation;

    var rightValidation = ValidateCollectionScopeSecurity(request.Right, "right", allowedRootPolicy, requireExistingRoots: true);
    if (rightValidation is not null)
        return rightValidation;

    var build = await CollectionCompareService.CompareAsync(collectionIndex, fileSystem, request, ct);
    return !build.CanUse || build.Result is null
        ? ApiError(409, ApiErrorCodes.CollectionCompareNotReady, build.Reason ?? "Collection compare unavailable.")
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
        ? ApiError(409, ApiErrorCodes.CollectionMergeNotReady, build.Reason ?? "Collection merge unavailable.")
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
        ? ApiError(409, ApiErrorCodes.CollectionMergeApplyNotReady, result.BlockedReason)
        : Results.Ok(result);
})
    .WithSummary("Apply a previously previewable collection merge with audit and rollback metadata")
    .Accepts<CollectionMergeApplyRequest>("application/json")
    .Produces<CollectionMergeApplyResult>(StatusCodes.Status200OK)
    .Produces<OperationErrorResponse>(StatusCodes.Status400BadRequest)
    .Produces<OperationErrorResponse>(StatusCodes.Status409Conflict);

app.MapPost("/collections/merge/rollback", async (
    HttpContext ctx,
    AuditSigningService auditSigningService,
    AllowedRootPathPolicy allowedRootPolicy,
    CancellationToken ct) =>
{
    var requestRead = await ReadJsonBodyAsync<CollectionMergeRollbackRequest>(ctx, "COLLECTION-MERGE-ROLLBACK", ct);
    if (requestRead.Error is not null)
        return requestRead.Error;

    var request = requestRead.Value!;
    if (string.IsNullOrWhiteSpace(request.AuditPath))
        return ApiError(400, ApiErrorCodes.CollectionMergeRollbackAuditRequired, "auditPath is required.");

    var auditPathError = ValidatePathSecurity(request.AuditPath.Trim(), "auditPath", allowedRootPolicy);
    if (auditPathError is not null)
        return auditPathError;

    var auditPath = Path.GetFullPath(request.AuditPath.Trim());
    if (!File.Exists(auditPath))
        return ApiError(404, ApiErrorCodes.CollectionMergeRollbackAuditNotFound, $"Audit file not found: {auditPath}");

    var rootSet = AuditRollbackRootResolver.Resolve(auditPath);
    if (rootSet.RestoreRoots.Count == 0 || rootSet.CurrentRoots.Count == 0)
        return ApiError(400, ApiErrorCodes.CollectionMergeRollbackRootsUnavailable, "Rollback roots could not be resolved from audit metadata.");

    if (allowedRootPolicy.IsEnforced
        && (!rootSet.RestoreRoots.All(allowedRootPolicy.IsPathAllowed)
            || !rootSet.CurrentRoots.All(allowedRootPolicy.IsPathAllowed)))
    {
        return ApiError(400, SecurityErrorCodes.OutsideAllowedRoots, "Rollback paths are outside configured AllowedRoots.", ErrorKind.Critical);
    }

    var rollback = auditSigningService.Rollback(auditPath, rootSet.RestoreRoots, rootSet.CurrentRoots, dryRun: request.DryRun);
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
    var requestRead = await ReadJsonBodyAsync<ApiFrontendExportRequest>(ctx, "EXPORT", ct);
    if (requestRead.Error is not null)
        return requestRead.Error;

    var request = requestRead.Value;

    if (request is null || string.IsNullOrWhiteSpace(request.Frontend))
        return ApiError(400, ApiErrorCodes.ExportFrontendRequired, "frontend is required.");

    if (string.IsNullOrWhiteSpace(request.OutputPath))
        return ApiError(400, ApiErrorCodes.ExportOutputRequired, "outputPath is required.");

    var outputPathError = ValidatePathSecurity(request.OutputPath.Trim(), "outputPath", allowedRootPolicy);
    if (outputPathError is not null)
        return outputPathError;

    RunRecord? run = null;
    if (!string.IsNullOrWhiteSpace(request.RunId))
    {
        run = mgr.Get(request.RunId);
        if (run is null)
            return ApiError(404, ApiErrorCodes.RunNotFound, "Run not found.", runId: request.RunId);

        if (!CanAccessRun(run, GetClientBindingId(ctx, trustForwardedFor)))
            return ApiError(403, ApiErrorCodes.AuthForbidden, "Run belongs to a different client.", ErrorKind.Critical, runId: request.RunId);
    }

    var roots = request.Roots?.Where(static root => !string.IsNullOrWhiteSpace(root)).ToArray()
        ?? run?.Roots
        ?? Array.Empty<string>();
    if (roots.Length == 0)
        return ApiError(400, ApiErrorCodes.ExportRootsRequired, "roots[] or runId is required.");

    var extensions = request.Extensions?.Where(static ext => !string.IsNullOrWhiteSpace(ext)).ToArray()
        ?? run?.Extensions
        ?? RunOptions.DefaultExtensions;

    foreach (var root in roots)
    {
        var pathError = ValidatePathSecurity(root, "roots", allowedRootPolicy);
        if (pathError is not null)
            return pathError;

        if (!Directory.Exists(root))
            return ApiError(400, ApiErrorCodes.IoRootNotFound, $"Root not found: {root}");
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
            runCandidates: run?.CoreRunResult is { } exportRunResult
                ? RunArtifactProjection.Project(exportRunResult).AllCandidates
                : null,
            ct: ct);

        return Results.Ok(exportResult);
    }
    catch (InvalidOperationException ex)
    {
        SafeConsoleWriteLine($"[API-WARN] Frontend export not ready: {ex.Message}");
        return ApiError(409, ApiErrorCodes.ExportNotReady, "Export is not ready for the requested input.");
    }
})
    .WithSummary("Export collection data to frontend-specific artifacts")
    .Produces<FrontendExportResult>(StatusCodes.Status200OK)
    .Produces<OperationErrorResponse>(StatusCodes.Status400BadRequest)
    .Produces<OperationErrorResponse>(StatusCodes.Status403Forbidden)
    .Produces<OperationErrorResponse>(StatusCodes.Status404NotFound)
    .Produces<OperationErrorResponse>(StatusCodes.Status409Conflict);

MapRunWatchEndpoints(app, trustForwardedFor, timeProvider, sseTimeoutSeconds, sseHeartbeatSeconds);

// --- DAT Management Endpoints (B2) ---

app.MapGet("/dats/status", async (AllowedRootPathPolicy allowedRootPolicy, CancellationToken ct) =>
    Results.Ok(await DashboardDataBuilder.BuildDatStatusAsync(allowedRootPolicy, ct)))
    .Produces<DashboardDatStatusResponse>(StatusCodes.Status200OK);

app.MapPost("/dats/update", async (HttpContext ctx, AllowedRootPathPolicy allowedRootPolicy, IHttpClientFactory httpClientFactory) =>
{
    var dataDir = RunEnvironmentBuilder.TryResolveDataDir()
        ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
    var settings = RunEnvironmentBuilder.LoadSettings(dataDir);
    var datRoot = settings.Dat?.DatRoot;

    if (string.IsNullOrWhiteSpace(datRoot))
        return ApiError(400, ApiErrorCodes.DatRootNotConfigured, "DatRoot is not configured in settings.");

    var datRootError = ValidatePathSecurity(datRoot, "datRoot", allowedRootPolicy);
    if (datRootError is not null)
        return datRootError;

    if (!Directory.Exists(datRoot))
    {
        try { Directory.CreateDirectory(datRoot); }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "Failed to create DatRoot at '{DatRoot}'", datRoot);
            return ApiError(500, ApiErrorCodes.DatRootCreateFailed, "Cannot create DatRoot.");
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
            return ApiError(400, ApiErrorCodes.DatInvalidJson, "Invalid JSON body.");
        }
    }

    var catalogPath = Path.Combine(dataDir, "dat-catalog.json");
    if (!File.Exists(catalogPath))
        return ApiError(404, ApiErrorCodes.DatCatalogNotFound, "dat-catalog.json not found.");

    List<DatCatalogEntry> catalog;
    try { catalog = DatSourceService.LoadCatalog(catalogPath); }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Failed to load DAT catalog from {CatalogPath}", catalogPath);
        return ApiError(500, ApiErrorCodes.DatCatalogLoadError, "Failed to load DAT catalog.");
    }

    if (catalog.Count == 0)
        return ApiError(400, ApiErrorCodes.DatCatalogEmpty, "dat-catalog.json contains no entries.");

    int downloaded = 0, skipped = 0, failed = 0;
    var errors = new List<string>();

    using var datService = new DatSourceService(datRoot, httpClientFactory.CreateClient(DatSourceService.HttpClientName));
    foreach (var entry in catalog.Where(e => !string.IsNullOrWhiteSpace(e.Url) && !string.Equals(e.Format, RunConstants.FormatNoIntroPack, StringComparison.OrdinalIgnoreCase)))
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
            app.Logger.LogWarning(ex, "DAT update failed for catalog entry '{EntryId}'", entry.Id);
            errors.Add($"{entry.Id}: download failed");
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
        return ApiError(400, ApiErrorCodes.DatRootNotConfigured, "DatRoot is not configured in settings.");

    if (!Directory.Exists(datRoot))
        return ApiError(400, ApiErrorCodes.DatRootNotFound, $"DatRoot does not exist: {datRoot}");

    var datRootError = ValidatePathSecurity(datRoot, "datRoot", allowedRootPolicy);
    if (datRootError is not null)
        return datRootError;

    // Read body
    if (ctx.Request.ContentLength is > 1_048_576)
        return ApiError(400, ApiErrorCodes.DatBodyTooLarge, "Request body too large (max 1MB).");

    string body;
    try
    {
        using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8);
        body = await reader.ReadToEndAsync();
    }
    catch (IOException)
    {
        return ApiError(400, ApiErrorCodes.DatReadError, "Failed to read request body.");
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
        return ApiError(400, ApiErrorCodes.DatInvalidJson, "Invalid JSON body.");
    }

    if (string.IsNullOrWhiteSpace(sourcePath))
        return ApiError(400, ApiErrorCodes.DatPathRequired, "\"path\" is required in the request body.");

    // Security: validate source path
    var pathError = ValidatePathSecurity(sourcePath.Trim(), "path", allowedRootPolicy);
    if (pathError is not null) return pathError;

    sourcePath = Path.GetFullPath(sourcePath.Trim());

    if (!File.Exists(sourcePath))
        return ApiError(404, ApiErrorCodes.DatSourceNotFound, $"Source file not found: {sourcePath}");

    var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
    if (ext is not ".dat" and not ".xml")
        return ApiError(400, ApiErrorCodes.DatInvalidFormat, "Only .dat and .xml files can be imported.");

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
        app.Logger.LogWarning(ex, "DAT import blocked for source path '{SourcePath}'", sourcePath);
        return ApiError(400, ApiErrorCodes.DatImportBlocked, "DAT import blocked by policy.", ErrorKind.Critical);
    }
    catch (IOException ex)
    {
        app.Logger.LogError(ex, "DAT import failed for source path '{SourcePath}'", sourcePath);
        return ApiError(500, ApiErrorCodes.DatImportIoError, "DAT import failed due to an I/O error.");
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

MapRunCompletenessEndpoints(app, trustForwardedFor);

// Graceful shutdown: cancel active runs before process exits
app.Lifetime.ApplicationStopping.Register(() =>
{
    var mgr = app.Services.GetRequiredService<RunLifecycleManager>();
    // SYNC-JUSTIFIED: ApplicationStopping callback is synchronous; blocking here ensures
    // active run cancellation and recovery metadata complete before host teardown.
    mgr.ShutdownAsync().GetAwaiter().GetResult();

    // Explicit singleton disposal for deterministic teardown of long-lived resources.
    if (app.Services.GetService<ApiAutomationService>() is IDisposable automation)
        automation.Dispose();
    if (app.Services.GetService<PersistedReviewDecisionService>() is IDisposable reviewService)
        reviewService.Dispose();
    if (app.Services.GetService<ICollectionIndex>() is IDisposable collectionIndex)
        collectionIndex.Dispose();
    if (app.Services.GetService<IReviewDecisionStore>() is IDisposable reviewStore)
        reviewStore.Dispose();
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

// Helpers, models and ApiVersion are in ProgramHelpers.cs (partial class Program)
