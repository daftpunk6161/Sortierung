using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RomCleanup.Api;
using RomCleanup.Contracts.Errors;

var builder = WebApplication.CreateBuilder(args);

// Bind to loopback only (security: no network exposure)
var port = builder.Configuration.GetValue("Port", 7878);
var bindAddress = builder.Configuration.GetValue("BindAddress", "127.0.0.1");
var allowInsecureNetwork = builder.Configuration.GetValue("AllowInsecureNetwork", false);
builder.WebHost.UseUrls($"http://{bindAddress}:{port}");

// F-05 FIX: Hard-fail if binding to non-loopback address without explicit opt-in
if (bindAddress != "127.0.0.1" && bindAddress != "localhost" && bindAddress != "::1")
{
    if (!allowInsecureNetwork)
    {
        throw new InvalidOperationException(
            $"Refusing to bind to non-loopback address '{bindAddress}' over plain HTTP. " +
            "API key would be transmitted in cleartext. " +
            "Set --AllowInsecureNetwork=true to override (NOT recommended for production).");
    }
    Console.WriteLine($"WARNING: API bound to non-loopback address '{bindAddress}' with --AllowInsecureNetwork. " +
        "API key is transmitted in cleartext. Ensure firewall rules and TLS are configured.");
}

builder.Services.AddSingleton<RomCleanup.Contracts.Ports.IFileSystem, RomCleanup.Infrastructure.FileSystem.FileSystemAdapter>();
builder.Services.AddSingleton<RomCleanup.Contracts.Ports.IAuditStore>(sp =>
    new RomCleanup.Infrastructure.Audit.AuditCsvStore(
        sp.GetRequiredService<RomCleanup.Contracts.Ports.IFileSystem>(),
        Console.WriteLine,
        RomCleanup.Infrastructure.Audit.AuditSecurityPaths.GetDefaultSigningKeyPath()));
builder.Services.AddSingleton<RunManager>();

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
var rateLimiter = new RateLimiter(rateLimitMax, rateLimitWindow);

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
        var origin = corsMode switch
        {
            "local-dev" => "http://localhost:3000",
            "strict-local" => "http://127.0.0.1",
            "custom" => corsOrigin,
            _ => "http://127.0.0.1"
        };
        ctx.Response.Headers["Access-Control-Allow-Origin"] = origin;
        ctx.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type, X-Api-Key";
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

    // Rate limiting
    var forwardedFor = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault();
    var clientIp = !string.IsNullOrWhiteSpace(forwardedFor)
        ? forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "unknown"
        : ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    if (!rateLimiter.TryAcquire(clientIp))
    {
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
    var correlationId = ctx.Request.Headers["X-Correlation-ID"].FirstOrDefault()
        ?? Guid.NewGuid().ToString("N")[..16];
    ctx.Response.Headers["X-Correlation-ID"] = correlationId;

    var start = DateTime.UtcNow;
    await next();
    var elapsed = (DateTime.UtcNow - start).TotalMilliseconds;
    var method = ctx.Request.Method;
    var path = ctx.Request.Path;
    var status = ctx.Response.StatusCode;
    SafeConsoleWriteLine($"[{start:o}] {correlationId} {method} {path} → {status} ({elapsed:F0}ms)");
});

// --- Endpoints ---

app.MapGet("/health", (RunManager mgr) =>
{
    var activeRun = mgr.GetActive();
    return Results.Ok(new
    {
        status = "ok",
        serverRunning = true,
        hasActiveRun = activeRun is not null,
        utc = DateTime.UtcNow.ToString("o")
    });
});

app.MapGet("/openapi", () => Results.Content(OpenApiSpec.Json, "application/json"));

app.MapPost("/runs", async (HttpContext ctx, RunManager mgr) =>
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
                return ApiError(400, "SEC-ROOT-REPARSE-POINT", $"Symlink/junction not allowed as root: {root}", ErrorKind.Critical);
        }
        catch { /* if we can't check attributes, let subsequent validation handle it */ }

        // Block system directories
        var full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var systemDirs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };
        foreach (var sys in systemDirs)
        {
            if (string.IsNullOrEmpty(sys)) continue;
            var normalizedSys = sys.TrimEnd(Path.DirectorySeparatorChar);
            if (full.Equals(normalizedSys, StringComparison.OrdinalIgnoreCase) ||
                full.StartsWith(normalizedSys + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return ApiError(400, "SEC-SYSTEM-DIRECTORY-ROOT", $"System directory not allowed: {root}", ErrorKind.Critical);
        }
        // Block drive root
        if (full.Length <= 3)
            return ApiError(400, "SEC-DRIVE-ROOT-NOT-ALLOWED", $"Drive root not allowed: {root}", ErrorKind.Critical);
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

    var create = mgr.TryCreateOrReuse(request, mode, idempotencyKey);
    if (create.Disposition == RunCreateDisposition.ActiveConflict)
        return ApiError(409, "RUN-ACTIVE-CONFLICT", create.Error ?? "Another run is already active.", runId: create.Run?.RunId, meta: CreateMeta(("activeRun", create.Run)));
    if (create.Disposition == RunCreateDisposition.IdempotencyConflict)
        return ApiError(409, "RUN-IDEMPOTENCY-CONFLICT", create.Error ?? "Idempotency key reuse with different payload is not allowed.", runId: create.Run?.RunId, meta: CreateMeta(("run", create.Run)));

    var run = create.Run!;

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
                run = current,
                reused = create.Disposition == RunCreateDisposition.Reused,
                waitTimedOut = true
            });
        }

        return Results.Ok(new
        {
            run = current,
            result = current?.Result,
            reused = create.Disposition == RunCreateDisposition.Reused
        });
    }

    if (create.Disposition == RunCreateDisposition.Reused && run.Status != "running")
        return Results.Ok(new { run, result = run.Result, reused = true });

    return Results.Accepted($"/runs/{run.RunId}", new { run, reused = create.Disposition == RunCreateDisposition.Reused });
});

app.MapGet("/runs/{runId}", (string runId, RunManager mgr) =>
{
    if (!Guid.TryParse(runId, out _))
        return ApiError(400, "RUN-INVALID-ID", "Invalid run ID format.");
    var run = mgr.Get(runId);
    return run is null
        ? ApiError(404, "RUN-NOT-FOUND", "Run not found.", runId: runId)
        : Results.Ok(new { run });
});

app.MapGet("/runs/{runId}/result", (string runId, RunManager mgr) =>
{
    if (!Guid.TryParse(runId, out _))
        return ApiError(400, "RUN-INVALID-ID", "Invalid run ID format.");
    var run = mgr.Get(runId);
    if (run is null)
        return ApiError(404, "RUN-NOT-FOUND", "Run not found.", runId: runId);
    if (run.Status == "running")
        return ApiError(409, "RUN-IN-PROGRESS", "Run still in progress.", runId: runId);
    return Results.Ok(new { run, result = run.Result });
});

app.MapPost("/runs/{runId}/cancel", (string runId, RunManager mgr) =>
{
    if (!Guid.TryParse(runId, out _))
        return ApiError(400, "RUN-INVALID-ID", "Invalid run ID format.");
    var cancel = mgr.Cancel(runId);
    if (cancel.Disposition == RunCancelDisposition.NotFound)
        return ApiError(404, "RUN-NOT-FOUND", "Run not found.", runId: runId);
    var updated = mgr.Get(runId);
    return Results.Ok(new
    {
        run = updated,
        cancelAccepted = cancel.Disposition == RunCancelDisposition.Accepted,
        idempotent = cancel.Disposition != RunCancelDisposition.Accepted
    });
});

app.MapGet("/runs/{runId}/stream", async (string runId, HttpContext ctx, RunManager mgr) =>
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

    ctx.Response.ContentType = "text/event-stream";
    ctx.Response.Headers["Cache-Control"] = "no-cache";

    var writer = ctx.Response.Body;
    var encoding = Encoding.UTF8;

    await WriteSseEvent(writer, encoding, "ready", new { runId, utc = DateTime.UtcNow.ToString("o") });

    var timeout = TimeSpan.FromSeconds(300);
    var start = DateTime.UtcNow;
    string? lastJson = null;
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

            var json = JsonSerializer.Serialize(current);

            if (!string.Equals(json, lastJson, StringComparison.Ordinal))
            {
                lastJson = json;
                lastHeartbeat = DateTime.UtcNow;
                if (current.Status != "running")
                {
                    var terminalEvent = current.Status switch
                    {
                        "cancelled" => "cancelled",
                        "failed" => "failed",
                        _ => "completed"
                    };
                    await WriteSseEvent(writer, encoding, terminalEvent, new { run = current, result = current.Result });
                    break;
                }
                await WriteSseEvent(writer, encoding, "status", current);
            }
            else if ((DateTime.UtcNow - lastHeartbeat).TotalSeconds >= 15)
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
            await WriteSseEvent(writer, encoding, "timeout", new { runId, seconds = 300 });
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
    var mgr = app.Services.GetRequiredService<RunManager>();
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
    var json = JsonSerializer.Serialize(data);
    var payload = $"event: {eventName}\ndata: {json}\n\n";
    await stream.WriteAsync(encoding.GetBytes(payload));
    await stream.FlushAsync();
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
