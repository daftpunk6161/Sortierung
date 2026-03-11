using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RomCleanup.Api;

var builder = WebApplication.CreateBuilder(args);

// Bind to loopback only (security: no network exposure)
var port = builder.Configuration.GetValue("Port", 7878);
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

builder.Services.AddSingleton<RunManager>();

var app = builder.Build();

// --- Middleware ---
var apiKey = builder.Configuration["ApiKey"]
             ?? Environment.GetEnvironmentVariable("ROM_CLEANUP_API_KEY")
             ?? throw new InvalidOperationException("API key required: set --ApiKey or ROM_CLEANUP_API_KEY env var");

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

    // CORS
    if (corsMode != "none")
    {
        var origin = corsMode switch
        {
            "local-dev" => "*",
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

// --- Endpoints ---

app.MapGet("/health", (RunManager mgr) =>
{
    var activeRun = mgr.GetActive();
    return Results.Ok(new
    {
        status = "ok",
        serverRunning = true,
        activeRunId = activeRun?.RunId,
        utc = DateTime.UtcNow.ToString("o")
    });
});

app.MapGet("/openapi", () => Results.Content(OpenApiSpec.Json, "application/json"));

app.MapPost("/runs", async (HttpContext ctx, RunManager mgr) =>
{
    // Read and validate body (max 1MB)
    ctx.Request.EnableBuffering();
    if (ctx.Request.ContentLength > 1_048_576)
        return Results.BadRequest(new { error = "Request body too large (max 1MB)." });

    string body;
    using (var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8, leaveOpen: true))
    {
        body = await reader.ReadToEndAsync();
    }
    if (body.Length > 1_048_576)
        return Results.BadRequest(new { error = "Request body too large (max 1MB)." });

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
            if (!string.IsNullOrEmpty(sys) &&
                full.Equals(sys.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = $"System directory not allowed: {root}" });
        }
        // Block drive root
        if (full.Length <= 3)
            return Results.BadRequest(new { error = $"Drive root not allowed: {root}" });
    }

    var mode = request.Mode ?? "DryRun";
    if (mode != "DryRun" && mode != "Move")
        return Results.BadRequest(new { error = "mode must be DryRun or Move." });

    var waitSync = ctx.Request.Query.ContainsKey("wait");

    var run = mgr.TryCreate(request, mode);
    if (run is null)
        return Results.Conflict(new { error = "A run is already active." });

    if (waitSync)
    {
        // Block until run completes
        await mgr.WaitForCompletion(run.RunId);
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
    string? lastHash = null;

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
        var hash = Convert.ToHexString(SHA256.HashData(encoding.GetBytes(json)));

        if (hash != lastHash)
        {
            lastHash = hash;
            if (current.Status != "running")
            {
                await WriteSseEvent(writer, encoding, "completed", new { run = current, result = current.Result });
                break;
            }
            await WriteSseEvent(writer, encoding, "status", current);
        }

        await Task.Delay(250, ctx.RequestAborted).ContinueWith(_ => { });
    }

    if (DateTime.UtcNow - start >= timeout)
    {
        await WriteSseEvent(writer, encoding, "timeout", new { runId, seconds = 300 });
    }
});

app.Run();

// --- Helpers ---

static bool FixedTimeEquals(string expected, string? actual)
{
    if (actual is null) return false;
    var a = Encoding.UTF8.GetBytes(expected);
    var b = Encoding.UTF8.GetBytes(actual);
    return CryptographicOperations.FixedTimeEquals(a, b);
}

static async Task WriteSseEvent(Stream stream, Encoding encoding, string eventName, object data)
{
    var json = JsonSerializer.Serialize(data);
    var payload = $"event: {eventName}\ndata: {json}\n\n";
    await stream.WriteAsync(encoding.GetBytes(payload));
    await stream.FlushAsync();
}
