using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RomCleanup.Api;

var builder = WebApplication.CreateBuilder(args);

// Bind to loopback only (security: no network exposure)
var port = builder.Configuration.GetValue("Port", 7878);
var bindAddress = builder.Configuration.GetValue("BindAddress", "127.0.0.1");
builder.WebHost.UseUrls($"http://{bindAddress}:{port}");

// V2-H08: Warn if binding to non-loopback address
if (bindAddress != "127.0.0.1" && bindAddress != "localhost" && bindAddress != "::1")
{
    Console.WriteLine($"WARNING: API bound to non-loopback address '{bindAddress}'. " +
        "This exposes the API to the network. Ensure firewall rules and TLS are configured.");
}

builder.Services.AddSingleton<RomCleanup.Contracts.Ports.IFileSystem, RomCleanup.Infrastructure.FileSystem.FileSystemAdapter>();
builder.Services.AddSingleton<RomCleanup.Contracts.Ports.IAuditStore, RomCleanup.Infrastructure.Audit.AuditCsvStore>();
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
    var clientIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    if (!rateLimiter.TryAcquire(clientIp))
    {
        ctx.Response.StatusCode = 429;
        await ctx.Response.WriteAsJsonAsync(new { error = "Too many requests." });
        return;
    }

    // API key validation (fixed-time comparison)
    var providedKey = ctx.Request.Headers["X-Api-Key"].FirstOrDefault();
    if (!FixedTimeEquals(apiKey, providedKey))
    {
        ctx.Response.StatusCode = 401;
        await ctx.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
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
    Console.WriteLine($"[{start:o}] {correlationId} {method} {path} → {status} ({elapsed:F0}ms)");
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
        return Results.BadRequest(new { error = "Content-Type must be application/json." });

    // Read and validate body (max 1MB)
    ctx.Request.EnableBuffering();
    if (ctx.Request.ContentLength is > 1_048_576)
        return Results.BadRequest(new { error = "Request body too large (max 1MB)." });

    string body;
    using (var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8, leaveOpen: true))
    {
        // Limit read to 1MB + 1 byte to detect oversized chunked bodies
        var buffer = new char[1_048_577];
        var charsRead = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
        if (charsRead > 1_048_576)
            return Results.BadRequest(new { error = "Request body too large (max 1MB)." });
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
        return Results.BadRequest(new { error = "Invalid JSON." });
    }

    if (request is null || request.Roots is null || request.Roots.Length == 0)
        return Results.BadRequest(new { error = "roots[] is required." });

    // Validate roots
    foreach (var root in request.Roots)
    {
        if (string.IsNullOrWhiteSpace(root))
            return Results.BadRequest(new { error = "Empty root path." });
        if (!Directory.Exists(root))
            return Results.BadRequest(new { error = $"Root not found: {root}" });

        // P2-API-07: Block symlinks/junctions as roots (bypass system-dir check)
        try
        {
            var dirInfo = new DirectoryInfo(root);
            if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                return Results.BadRequest(new { error = $"Symlink/junction not allowed as root: {root}" });
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
                return Results.BadRequest(new { error = $"System directory not allowed: {root}" });
        }
        // Block drive root
        if (full.Length <= 3)
            return Results.BadRequest(new { error = $"Drive root not allowed: {root}" });
    }

    var mode = request.Mode ?? "DryRun";
    if (!mode.Equals("DryRun", StringComparison.OrdinalIgnoreCase) &&
        !mode.Equals("Move", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { error = "mode must be DryRun or Move." });

    // Normalize to canonical casing
    mode = mode.Equals("Move", StringComparison.OrdinalIgnoreCase) ? "Move" : "DryRun";

    // TASK-200: Validate PreferRegions to prevent injection
    if (request.PreferRegions is { Length: > 0 })
    {
        foreach (var region in request.PreferRegions)
        {
            if (string.IsNullOrWhiteSpace(region) || region.Length > 10 ||
                !region.All(c => char.IsLetterOrDigit(c) || c == '-'))
                return Results.BadRequest(new { error = $"Invalid region: '{region}'. Only alphanumeric and '-' allowed." });
        }
    }

    var waitSync = ctx.Request.Query.ContainsKey("wait");

    var run = mgr.TryCreate(request, mode);
    if (run is null)
        return Results.Conflict(new { error = "A run is already active." });

    if (waitSync)
    {
        // Block until run completes, respecting client disconnect and 10-minute timeout
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
        cts.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            while (true)
            {
                var current = mgr.Get(run.RunId);
                if (current is null || current.Status != "running")
                    break;
                await Task.Delay(250, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            return Results.StatusCode(504); // Gateway Timeout
        }
        var completedRun = mgr.Get(run.RunId);
        return Results.Ok(new { run = completedRun, result = completedRun?.Result });
    }

    return Results.Accepted($"/runs/{run.RunId}", new { run });
});

app.MapGet("/runs/{runId}", (string runId, RunManager mgr) =>
{
    var run = mgr.Get(runId);
    return run is null
        ? Results.NotFound(new { error = "Run not found.", runId })
        : Results.Ok(new { run });
});

app.MapGet("/runs/{runId}/result", (string runId, RunManager mgr) =>
{
    var run = mgr.Get(runId);
    if (run is null)
        return Results.NotFound(new { error = "Run not found.", runId });
    if (run.Status == "running")
        return Results.Conflict(new { error = "Run still in progress.", runId });
    return Results.Ok(new { run, result = run.Result });
});

app.MapPost("/runs/{runId}/cancel", (string runId, RunManager mgr) =>
{
    var run = mgr.Get(runId);
    if (run is null)
        return Results.NotFound(new { error = "Run not found.", runId });
    if (run.Status != "running")
        return Results.Conflict(new { error = "Run is not active.", runId });

    mgr.Cancel(runId);
    var updated = mgr.Get(runId);
    return Results.Ok(new { run = updated });
});

app.MapGet("/runs/{runId}/stream", async (string runId, HttpContext ctx, RunManager mgr) =>
{
    var run = mgr.Get(runId);
    if (run is null)
    {
        ctx.Response.StatusCode = 404;
        await ctx.Response.WriteAsJsonAsync(new { error = "Run not found.", runId });
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
                await WriteSseEvent(writer, encoding, "error", new { error = "Run not found.", runId });
                break;
            }

            var json = JsonSerializer.Serialize(current);

            if (!string.Equals(json, lastJson, StringComparison.Ordinal))
            {
                lastJson = json;
                lastHeartbeat = DateTime.UtcNow;
                if (current.Status != "running")
                {
                    await WriteSseEvent(writer, encoding, "completed", new { run = current, result = current.Result });
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

static async Task WriteSseEvent(Stream stream, Encoding encoding, string eventName, object data)
{
    var json = JsonSerializer.Serialize(data);
    var payload = $"event: {eventName}\ndata: {json}\n\n";
    await stream.WriteAsync(encoding.GetBytes(payload));
    await stream.FlushAsync();
}

public partial class Program
{
    // V2-H10: API version from assembly metadata, not hardcoded
    internal static readonly string ApiVersion =
        typeof(Program).Assembly.GetName().Version?.ToString(2) ?? "1.0";
}
