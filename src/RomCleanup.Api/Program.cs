using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RomCleanup.Api;
using RomCleanup.Contracts.Errors;
using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure;
using RomCleanup.Infrastructure.Audit;
using RomCleanup.Infrastructure.FileSystem;

var builder = WebApplication.CreateBuilder(args);

// Bind to loopback only (security: no network exposure)
var port = builder.Configuration.GetValue("Port", 7878);
var bindAddress = builder.Configuration.GetValue("BindAddress", "127.0.0.1");
builder.WebHost.UseUrls($"http://{bindAddress}:{port}");

// Security hardening: the API currently binds via HTTP only, so non-loopback is forbidden.
if (!IsLoopbackAddress(bindAddress))
{
    throw new InvalidOperationException(
        $"Refusing to bind API to non-loopback address '{bindAddress}' over plain HTTP. " +
        "Use loopback (127.0.0.1/localhost/::1) or terminate TLS in a reverse proxy.");
}

builder.Services.AddRomCleanupCore();
builder.Services.AddSingleton<RunManager>();
builder.Services.AddSingleton<RunLifecycleManager>(sp =>
    sp.GetRequiredService<RunManager>().Lifecycle);

var app = builder.Build();

// --- Middleware ---
var apiKey = builder.Configuration["ApiKey"]
             ?? Environment.GetEnvironmentVariable("ROM_CLEANUP_API_KEY");
if (string.IsNullOrEmpty(apiKey))
{
    if (app.Environment.IsDevelopment())
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
var resolvedCorsOrigin = ResolveCorsOrigin(corsMode, corsOrigin);

// Remove server headers
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers.Remove("Server");
    ctx.Response.Headers.Remove("X-Powered-By");
    ctx.Response.Headers["Cache-Control"] = "no-store";
    ctx.Response.Headers["X-Api-Version"] = ApiVersion;

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
});

app.MapGet("/openapi", () => Results.Content(
    OpenApiSpec.Json.Replace("\"version\": \"1.0.0\"", $"\"version\": \"{Program.ApiVersion}\""),
    "application/json"));

app.MapPost("/runs", async (HttpContext ctx, RunLifecycleManager mgr) =>
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

    if (request is null || request.Roots is null || request.Roots.Length == 0)
        return ApiError(400, "RUN-ROOTS-REQUIRED", "roots[] is required.");

    // Validate roots
    foreach (var root in request.Roots)
    {
        if (string.IsNullOrWhiteSpace(root))
            return ApiError(400, "RUN-ROOT-EMPTY", "Empty root path.");
        if (!Directory.Exists(root))
            return ApiError(400, "IO-ROOT-NOT-FOUND", $"Root not found: {root}");

        // P2-API-07: Block symlinks/junctions as roots (bypass system-dir check)
        try
        {
            var dirInfo = new DirectoryInfo(root);
            if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                return ApiError(400, SecurityErrorCodes.RootReparsePoint, $"Symlink/junction not allowed as root: {root}", ErrorKind.Critical);
        }
        catch (Exception ex)
        {
            // SEC: Fail closed — if we cannot verify attributes, reject the root
            return ApiError(400, SecurityErrorCodes.RootAttributeCheckFailed,
                $"Cannot verify attributes for root: {root} ({ex.GetType().Name})", ErrorKind.Critical);
        }

        // Block system directories (single source of truth: SafetyValidator)
        var full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        if (RomCleanup.Infrastructure.Safety.SafetyValidator.IsProtectedSystemPath(full))
            return ApiError(400, SecurityErrorCodes.SystemDirectoryRoot, $"System directory not allowed: {root}", ErrorKind.Critical);
        // Block drive root
        if (RomCleanup.Infrastructure.Safety.SafetyValidator.IsDriveRoot(full))
            return ApiError(400, SecurityErrorCodes.DriveRootNotAllowed, $"Drive root not allowed: {root}", ErrorKind.Critical);
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
        var pathError = ValidatePathSecurity(request.TrashRoot.Trim(), "trashRoot");
        if (pathError is not null) return pathError;
    }

    // SEC: Validate DatRoot
    if (!string.IsNullOrWhiteSpace(request.DatRoot))
    {
        var pathError = ValidatePathSecurity(request.DatRoot.Trim(), "datRoot");
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

    var waitSync = ctx.Request.Query.TryGetValue("wait", out var waitValue)
        ? !string.Equals(waitValue.ToString(), "false", StringComparison.OrdinalIgnoreCase)
        : false;

    var waitTimeoutMs = 600_000;
    if (ctx.Request.Query.TryGetValue("waitTimeoutMs", out var waitTimeoutValue))
    {
        if (!int.TryParse(waitTimeoutValue, out waitTimeoutMs) || waitTimeoutMs < 1 || waitTimeoutMs > 1_800_000)
            return ApiError(400, "RUN-INVALID-WAIT-TIMEOUT", "waitTimeoutMs must be an integer between 1 and 1800000.");
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
});

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
});

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
});

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
});

app.MapPost("/runs/{runId}/rollback", (string runId, HttpContext ctx, RunLifecycleManager mgr) =>
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
    var dryRunParam = ctx.Request.Query["dryRun"].FirstOrDefault();
    bool isDryRun = !string.Equals(dryRunParam, "false", StringComparison.OrdinalIgnoreCase);

    var restoreRoots = run.Roots ?? Array.Empty<string>();
    var currentRoots = string.IsNullOrWhiteSpace(run.TrashRoot)
        ? restoreRoots
        : restoreRoots.Append(run.TrashRoot).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    var signing = new AuditSigningService(new FileSystemAdapter(), keyFilePath: AuditSecurityPaths.GetDefaultSigningKeyPath());
    var rollback = signing.Rollback(run.AuditPath, restoreRoots, currentRoots, dryRun: isDryRun);

    return Results.Ok(new
    {
        run = run.ToDto(),
        dryRun = isDryRun,
        rollback
    });
});

app.MapGet("/runs/{runId}/reviews", (string runId, HttpContext ctx, RunLifecycleManager mgr) =>
{
    if (!Guid.TryParse(runId, out _))
        return ApiError(400, "RUN-INVALID-ID", "Invalid run ID format.");

    var run = mgr.Get(runId);
    if (run is null)
        return ApiError(404, "RUN-NOT-FOUND", "Run not found.", runId: runId);

    if (!CanAccessRun(run, GetClientBindingId(ctx, trustForwardedFor)))
        return ApiError(403, "AUTH-FORBIDDEN", "Run belongs to a different client.", ErrorKind.Critical, runId: runId);

    var queue = BuildReviewQueue(run);
    return Results.Ok(queue);
});

app.MapPost("/runs/{runId}/reviews/approve", async (string runId, HttpContext ctx, RunLifecycleManager mgr) =>
{
    if (!Guid.TryParse(runId, out _))
        return ApiError(400, "RUN-INVALID-ID", "Invalid run ID format.");

    var run = mgr.Get(runId);
    if (run is null)
        return ApiError(404, "RUN-NOT-FOUND", "Run not found.", runId: runId);

    if (!CanAccessRun(run, GetClientBindingId(ctx, trustForwardedFor)))
        return ApiError(403, "AUTH-FORBIDDEN", "Run belongs to a different client.", ErrorKind.Critical, runId: runId);

    ApiReviewApprovalRequest request;
    try
    {
        request = await ctx.Request.ReadFromJsonAsync<ApiReviewApprovalRequest>() ?? new ApiReviewApprovalRequest();
    }
    catch (JsonException)
    {
        return ApiError(400, "RUN-INVALID-JSON", "Invalid JSON.");
    }

    var queue = BuildReviewQueue(run);
    var matched = queue.Items.Where(item =>
            (string.IsNullOrWhiteSpace(request.ConsoleKey) || string.Equals(item.ConsoleKey, request.ConsoleKey, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(request.MatchLevel) || string.Equals(item.MatchLevel, request.MatchLevel, StringComparison.OrdinalIgnoreCase)) &&
            (request.Paths is null || request.Paths.Length == 0 || request.Paths.Contains(item.MainPath, StringComparer.OrdinalIgnoreCase)))
        .ToArray();

    foreach (var item in matched)
        run.ApprovedReviewPaths.Add(item.MainPath);

    var updated = BuildReviewQueue(run);
    return Results.Ok(new
    {
        runId,
        approvedCount = matched.Length,
        totalApproved = run.ApprovedReviewPaths.Count,
        queue = updated
    });
});

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

static bool IsLoopbackAddress(string host)
{
    if (string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(host, "[::1]", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    return false;
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

static ApiReviewQueue BuildReviewQueue(RunRecord run)
{
    var core = run.CoreRunResult;
    if (core is null)
        return new ApiReviewQueue { RunId = run.RunId, Total = 0, Items = Array.Empty<ApiReviewItem>() };

    var items = core.AllCandidates
        .Where(c => c.SortDecision == SortDecision.Review || c.SortDecision == SortDecision.Blocked)
        .OrderBy(c => c.ConsoleKey, StringComparer.OrdinalIgnoreCase)
        .ThenBy(c => c.MainPath, StringComparer.OrdinalIgnoreCase)
        .Select(c => new ApiReviewItem
        {
            MainPath = c.MainPath,
            FileName = Path.GetFileName(c.MainPath),
            ConsoleKey = c.ConsoleKey,
            SortDecision = c.SortDecision.ToString(),
            MatchLevel = c.MatchEvidence.Level.ToString(),
            MatchReasoning = c.MatchEvidence.Reasoning,
            DetectionConfidence = c.DetectionConfidence,
            Approved = run.ApprovedReviewPaths.Contains(c.MainPath)
        })
        .ToArray();

    return new ApiReviewQueue
    {
        RunId = run.RunId,
        Total = items.Length,
        Items = items
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

static IResult? ValidatePathSecurity(string path, string fieldName)
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

    return null;
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

public partial class Program
{
    // V2-H10: API version from assembly metadata, not hardcoded
    internal static readonly string ApiVersion =
        typeof(Program).Assembly.GetName().Version?.ToString(2) ?? "1.0";
}
