using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Romulus.Api;
using Romulus.Contracts.Errors;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.FileSystem;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// TDD Red Phase — API failing tests exposing gaps in response models,
/// status mapping, SSE/polling consistency, error shape, request validation,
/// and API-CLI-GUI parity. Issue #9.
///
/// Categories:
///   A: Response field completeness — fields SHOULD be in the response but AREN'T
///   B: Status model — statuses that should be correctly mapped/exposed
///   C: SSE / Polling consistency — event names and data must match polling state
///   D: Error object consistency — error shape gaps
///   E: API / CLI / GUI parity — features available in CLI/GUI but missing in API
/// </summary>
public sealed class ApiRedPhaseTests : IDisposable
{
    private const string ApiKey = "red-phase-api-key";
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
            SafeDeleteDirectory(dir);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  A: Response Field Completeness (RED)
    //
    //  Bug: API responses omit fields that callers need for dashboards,
    //  debugging, and feature parity with CLI/GUI.
    //
    //  Goal:  Every API response carries the full information contract.
    //  Why RED: Fields missing from ApiRunResult / health / error envelopes.
    //  Fix target: RunManager.cs (RunRecord, ApiRunResult), Program.cs endpoints.
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Ziel: Health body muss API-Version enthalten (nicht nur als Header).
    /// Warum rot: Health-Endpoint liefert nur status/serverRunning/hasActiveRun/utc,
    ///   kein "version"-Feld im Body. Clients ohne Header-Zugriff (JS fetch mit opaque response) verlieren die Info.
    /// Betroffene Dateien: src/Romulus.Api/Program.cs (GET /health endpoint)
    /// </summary>
    [Fact]
    public async Task Health_Body_Should_Contain_Version_Issue9()
    {
        using var factory = CreateFactory();
        using var client = CreateAuthClient(factory);

        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        // Body must include the API version (matching the X-Api-Version header)
        Assert.True(root.TryGetProperty("version", out var version),
            "Health response body must include 'version' field.");
        Assert.False(string.IsNullOrWhiteSpace(version.GetString()));
    }

    /// <summary>
    /// Ziel: Completed-Run-Result muss Preflight-Warnungen transportieren.
    /// Warum rot: ApiRunResult hat kein PreflightWarnings-Feld; Warnungen aus
    ///   RunResult.Preflight gehen bei der Projektion verloren.
    /// Betroffene Dateien: src/Romulus.Api/RunManager.cs (ApiRunResult, ExecuteWithOrchestrator)
    /// </summary>
    [Fact]
    public async Task Result_Should_Contain_PreflightWarnings_Issue9()
    {
        using var factory = CreateFactory();
        using var client = CreateAuthClient(factory);
        var root = CreateTempRoot();

        var payload = JsonSerializer.Serialize(new
        {
            roots = new[] { root },
            mode = "DryRun"
        });

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/runs?wait=true", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = doc.RootElement.GetProperty("result");

        Assert.True(result.TryGetProperty("preflightWarnings", out var warnings),
            "Result must include 'preflightWarnings' array (even if empty).");
        Assert.Equal(JsonValueKind.Array, warnings.ValueKind);
    }

    /// <summary>
    /// Ziel: Completed-Run-Result muss Phase-Metriken enthalten (Scan/Dedupe/Move-Zeiten).
    /// Warum rot: ApiRunResult hat kein phaseMetrics-Feld; RunResult.PhaseMetrics
    ///   wird von RunProjectionFactory nicht weitergereicht.
    /// Betroffene Dateien: src/Romulus.Api/RunManager.cs (ApiRunResult),
    ///   src/Romulus.Infrastructure/Orchestration/RunProjection.cs
    /// </summary>
    [Fact]
    public async Task Result_Should_Contain_PhaseMetrics_Issue9()
    {
        using var factory = CreateFactory();
        using var client = CreateAuthClient(factory);
        var root = CreateTempRoot();

        var payload = JsonSerializer.Serialize(new
        {
            roots = new[] { root },
            mode = "DryRun"
        });

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/runs?wait=true", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = doc.RootElement.GetProperty("result");

        Assert.True(result.TryGetProperty("phaseMetrics", out var metrics),
            "Result must include 'phaseMetrics' object with per-phase timing breakdown.");
        Assert.Equal(JsonValueKind.Object, metrics.ValueKind);
    }

    /// <summary>
    /// Ziel: Polling eines laufenden Runs muss elapsedMs mitliefern.
    /// Warum rot: GET /runs/{id} serialisiert den RunRecord, der kein berechnetes
    ///   ElapsedMs-Feld hat — Clients müssen selbst rechnen, was bei Zeitzonen-Drift bricht.
    /// Betroffene Dateien: src/Romulus.Api/RunManager.cs (RunRecord),
    ///   src/Romulus.Api/Program.cs (GET /runs/{id})
    /// </summary>
    [Fact]
    public async Task RunStatus_Running_Should_Contain_ElapsedMs_Issue9()
    {
        var gate = new ManualResetEventSlim(false);
        using var factory = CreateFactory(executor: (_, _, _, ct) =>
        {
            gate.Wait(ct);
            return new RunExecutionOutcome("completed", new ApiRunResult
            {
                OrchestratorStatus = "ok",
                ExitCode = 0
            });
        });
        using var client = CreateAuthClient(factory);
        var root = CreateTempRoot();

        try
        {
            var payload = JsonSerializer.Serialize(new { roots = new[] { root }, mode = "DryRun" });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");

            // Create run (async, no wait)
            var createResponse = await client.PostAsync("/runs", content);
            Assert.True(
                createResponse.StatusCode == HttpStatusCode.Accepted ||
                createResponse.StatusCode == HttpStatusCode.OK);

            using var createDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
            var runId = createDoc.RootElement.GetProperty("run").GetProperty("runId").GetString();

            // Small delay to let elapsed accumulate
            await Task.Delay(100);

            // Poll while running
            var statusResponse = await client.GetAsync($"/runs/{runId}");
            Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);

            using var statusDoc = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync());
            var run = statusDoc.RootElement.GetProperty("run");

            Assert.True(run.TryGetProperty("elapsedMs", out var elapsed),
                "Running run must include 'elapsedMs' field (computed server-side).");
            Assert.True(elapsed.GetInt64() > 0, "elapsedMs must be > 0 for a running run.");
        }
        finally
        {
            gate.Set();
        }
    }

    /// <summary>
    /// Ziel: Cancel-Response muss cancelledAtUtc-Zeitstempel enthalten.
    /// Warum rot: POST /runs/{id}/cancel gibt nur run + cancelAccepted + idempotent
    ///   zurück, kein dedizierter cancelledAtUtc-Timestamp.
    /// Betroffene Dateien: src/Romulus.Api/Program.cs (POST /runs/{id}/cancel)
    /// </summary>
    [Fact]
    public async Task Cancel_Response_Should_Contain_CancelledAtUtc_Issue9()
    {
        var gate = new ManualResetEventSlim(false);
        using var factory = CreateFactory(executor: (_, _, _, ct) =>
        {
            gate.Wait(ct);
            return new RunExecutionOutcome("completed", new ApiRunResult());
        });
        using var client = CreateAuthClient(factory);
        var root = CreateTempRoot();

        try
        {
            var payload = JsonSerializer.Serialize(new { roots = new[] { root }, mode = "DryRun" });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var createResponse = await client.PostAsync("/runs", content);
            using var createDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
            var runId = createDoc.RootElement.GetProperty("run").GetProperty("runId").GetString();

            var cancelResponse = await client.PostAsync($"/runs/{runId}/cancel", null);
            Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);

            using var cancelDoc = JsonDocument.Parse(await cancelResponse.Content.ReadAsStringAsync());
            var cancelRoot = cancelDoc.RootElement;

            Assert.True(cancelRoot.TryGetProperty("cancelledAtUtc", out var ts),
                "Cancel response must include 'cancelledAtUtc' ISO-8601 timestamp.");
            Assert.False(string.IsNullOrWhiteSpace(ts.GetString()));
        }
        finally
        {
            gate.Set();
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  B: Status Model (RED)
    //
    //  Bug: "completed_with_errors" ist ein gültiger Orchestrator-Status,
    //  aber SSE mappt ihn auf den generischen "completed"-Event — Clients
    //  können partial failures nicht erkennen.
    //
    //  Goal:  Jeder fachliche Status hat einen eigenen SSE-Event-Namen.
    //  Why RED: SSE switch default → "completed" statt "completed_with_errors".
    //  Fix target: src/Romulus.Api/Program.cs (SSE terminalEvent switch).
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Ziel: SSE muss "completed_with_errors" als eigenen Event-Namen senden.
    /// Warum rot: SSE-Code verwendet switch mit "cancelled"/"failed"/default→"completed",
    ///   status "completed_with_errors" wird zu "completed" — Client verliert die Unterscheidung.
    /// Betroffene Dateien: src/Romulus.Api/Program.cs (SSE /runs/{id}/stream switch)
    /// </summary>
    [Fact]
    public async Task SSE_CompletedWithErrors_Should_Be_DistinctEventName_Issue9()
    {
        using var factory = CreateFactory(executor: (_, _, _, _) =>
            new RunExecutionOutcome("completed_with_errors", new ApiRunResult
            {
                OrchestratorStatus = "completed_with_errors",
                ExitCode = 0,
                TotalFiles = 10,
                FailCount = 2
            }));
        using var client = CreateAuthClient(factory);
        var root = CreateTempRoot();

        // Create run and wait for completion
        var payload = JsonSerializer.Serialize(new { roots = new[] { root }, mode = "DryRun" });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var createResponse = await client.PostAsync("/runs?wait=true", content);
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        using var createDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var runId = createDoc.RootElement.GetProperty("run").GetProperty("runId").GetString();

        // Connect SSE for already-completed run
        using var sseRequest = new HttpRequestMessage(HttpMethod.Get, $"/runs/{runId}/stream");
        var sseResponse = await client.SendAsync(sseRequest, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, sseResponse.StatusCode);

        var sseContent = await ReadSseContentWithTimeout(sseResponse, 5000);
        var events = ParseSseEvents(sseContent);

        // Must have a terminal event with name "completed_with_errors" (not generic "completed")
        var terminalEvent = events.FirstOrDefault(e =>
            e.EventName != "ready" && e.EventName != "status");

        Assert.NotNull(terminalEvent);
        Assert.Equal("completed_with_errors", terminalEvent.EventName);
    }

    /// <summary>
    /// Ziel: Failed-Run-Result muss strukturierten Fehler als Objekt enthalten
    ///   (code/kind/message), nicht nur einen flachen String.
    /// Warum rot: ApiRunResult.Error ist string?, kein OperationError-Objekt.
    ///   Clients können Error-Code und Kind nicht maschinell auswerten.
    /// Betroffene Dateien: src/Romulus.Api/RunManager.cs (ApiRunResult.Error,
    ///   ExecuteRun catch-Block)
    /// </summary>
    [Fact]
    public async Task Failed_Result_Error_Should_Be_StructuredObject_Issue9()
    {
        using var factory = CreateFactory(executor: (_, _, _, _) =>
            throw new InvalidOperationException("Simulated pipeline failure"));
        using var client = CreateAuthClient(factory);
        var root = CreateTempRoot();

        var payload = JsonSerializer.Serialize(new { roots = new[] { root }, mode = "DryRun" });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var createResponse = await client.PostAsync("/runs?wait=true", content);

        using var createDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var runId = createDoc.RootElement.GetProperty("run").GetProperty("runId").GetString()!;

        var resultResponse = await client.GetAsync($"/runs/{runId}/result");
        Assert.Equal(HttpStatusCode.OK, resultResponse.StatusCode);

        using var resultDoc = JsonDocument.Parse(await resultResponse.Content.ReadAsStringAsync());
        var result = resultDoc.RootElement.GetProperty("result");

        // Error must be a structured object, not a flat string
        Assert.True(result.TryGetProperty("error", out var error),
            "Failed result must contain 'error' field.");
        Assert.Equal(JsonValueKind.Object, error.ValueKind);
        Assert.True(error.TryGetProperty("code", out _), "Error must have 'code'.");
        Assert.True(error.TryGetProperty("kind", out _), "Error must have 'kind'.");
        Assert.True(error.TryGetProperty("message", out _), "Error must have 'message'.");
    }

    /// <summary>
    /// Ziel: Cancelled Run muss DurationMs > 0 berichten (nicht 0).
    /// Warum rot: Bei Cancel setzt ExecuteRun den Result mit DurationMs = 0 default;
    ///   die tatsächliche Laufzeit bis zum Cancel wird nicht erfasst.
    /// Betroffene Dateien: src/Romulus.Api/RunManager.cs (ExecuteRun catch-Block)
    /// </summary>
    [Fact]
    public async Task Cancelled_Run_Should_Report_Actual_DurationMs_Issue9()
    {
        var gate = new ManualResetEventSlim(false);
        using var factory = CreateFactory(executor: (_, _, _, ct) =>
        {
            gate.Wait(ct);
            return new RunExecutionOutcome("completed", new ApiRunResult());
        });
        using var client = CreateAuthClient(factory);
        var root = CreateTempRoot();

        try
        {
            var payload = JsonSerializer.Serialize(new { roots = new[] { root }, mode = "DryRun" });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var createResponse = await client.PostAsync("/runs", content);
            using var createDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
            var runId = createDoc.RootElement.GetProperty("run").GetProperty("runId").GetString()!;

            // Let it run for a bit, then cancel
            await Task.Delay(200);
            await client.PostAsync($"/runs/{runId}/cancel", null);

            // Wait for cancel to take effect
            await Task.Delay(500);

            var resultResponse = await client.GetAsync($"/runs/{runId}/result");
            Assert.Equal(HttpStatusCode.OK, resultResponse.StatusCode);

            using var resultDoc = JsonDocument.Parse(await resultResponse.Content.ReadAsStringAsync());
            var result = resultDoc.RootElement.GetProperty("result");

            var durationMs = result.GetProperty("durationMs").GetInt64();
            Assert.True(durationMs > 0,
                $"Cancelled run must report actual elapsed time, got durationMs={durationMs}.");
        }
        finally
        {
            gate.Set();
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  C: SSE / Polling Consistency (RED)
    //
    //  Bug: SSE-Events und Polling-Responses divergieren bei Status und
    //  Metadaten. Clients, die zwischen SSE und Polling wechseln, sehen
    //  inkonsistente Zustände.
    //
    //  Goal:  SSE und Polling liefern identische Status-Werte und Felder.
    //  Fix target: src/Romulus.Api/Program.cs (SSE + GET /runs/{id}).
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Ziel: SSE-Stream muss progressPercent in Status-Events liefern.
    /// Warum rot: SSE serialisiert nur den RunRecord; es gibt kein
    ///   berechnetes progressPercent-Feld auf RunRecord.
    /// Betroffene Dateien: src/Romulus.Api/RunManager.cs (RunRecord),
    ///   src/Romulus.Api/Program.cs (SSE status event serialization)
    /// </summary>
    [Fact]
    public async Task SSE_Status_Events_Should_Include_ProgressPercent_Issue9()
    {
        var step = 0;
        var gate = new ManualResetEventSlim(false);
        using var factory = CreateFactory(executor: (run, _, _, ct) =>
        {
            run.ProgressMessage = "Phase 1/3: Scanning...";
            Interlocked.Exchange(ref step, 1);
            gate.Wait(ct);
            return new RunExecutionOutcome("completed", new ApiRunResult
            {
                OrchestratorStatus = "ok",
                ExitCode = 0
            });
        });
        using var client = CreateAuthClient(factory);
        var root = CreateTempRoot();

        try
        {
            var payload = JsonSerializer.Serialize(new { roots = new[] { root }, mode = "DryRun" });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var createResponse = await client.PostAsync("/runs", content);
            using var createDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
            var runId = createDoc.RootElement.GetProperty("run").GetProperty("runId").GetString()!;

            // Wait for executor to start
            while (Interlocked.CompareExchange(ref step, 0, 0) < 1)
                await Task.Delay(50);

            // Poll the run — must have progressPercent
            var statusResponse = await client.GetAsync($"/runs/{runId}");
            using var statusDoc = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync());
            var run = statusDoc.RootElement.GetProperty("run");

            Assert.True(run.TryGetProperty("progressPercent", out var pct),
                "Run status must include 'progressPercent' field for progress tracking.");
            Assert.InRange(pct.GetInt32(), 0, 100);
        }
        finally
        {
            gate.Set();
        }
    }

    /// <summary>
    /// Ziel: POST /runs mit wait=true bei erfolgreichem Abschluss muss
    ///   Location-Header setzen für konsistentes Polling-Verhalten.
    /// Warum rot: Results.Ok(...) setzt keinen Location-Header;
    ///   nur Results.Accepted(...) tut das. Clients die Location nutzen, bekommen null.
    /// Betroffene Dateien: src/Romulus.Api/Program.cs (POST /runs wait=true OK path)
    /// </summary>
    [Fact]
    public async Task WaitTrue_OK_Response_Should_Set_LocationHeader_Issue9()
    {
        using var factory = CreateFactory();
        using var client = CreateAuthClient(factory);
        var root = CreateTempRoot();

        var payload = JsonSerializer.Serialize(new { roots = new[] { root }, mode = "DryRun" });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/runs?wait=true", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.NotNull(response.Headers.Location);
        Assert.Contains("/runs/", response.Headers.Location!.ToString());
    }



    // ═══════════════════════════════════════════════════════════════════
    //  D: Error Object Consistency (RED)
    //
    //  Bug: Fehlerobjekte fehlen Felder, die Clients für Log-Korrelation
    //  und automatische Retry-Logik brauchen.
    //
    //  Goal:  Jede Fehlerantwort enthält utc, correlationId, requestPath.
    //  Fix target: src/Romulus.Api/Program.cs (middleware order, error shape).
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Ziel: Alle Fehlerantworten müssen utc-Zeitstempel im Body enthalten.
    /// Warum rot: OperationErrorResponse hat kein utc-Feld; es wird nie gesetzt.
    /// Betroffene Dateien: src/Romulus.Contracts/Errors/OperationErrorResponse.cs,
    ///   src/Romulus.Api/Program.cs (CreateErrorResponse)
    /// </summary>
    [Fact]
    public async Task ErrorResponse_Should_Contain_UtcTimestamp_Issue9()
    {
        using var factory = CreateFactory();
        using var client = CreateAuthClient(factory);

        // Trigger a 404 error
        var validGuid = Guid.NewGuid().ToString();
        var response = await client.GetAsync($"/runs/{validGuid}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.TryGetProperty("utc", out var utc),
            "Error response must include 'utc' ISO-8601 timestamp.");
        Assert.False(string.IsNullOrWhiteSpace(utc.GetString()));
    }

    /// <summary>
    /// Ziel: Auth-Fehler (401) müssen X-Correlation-ID Header haben.
    /// Warum rot: Correlation-ID-Middleware läuft NACH der Auth-Middleware;
    ///   bei 401 wird die Correlation-Middleware nie erreicht.
    /// Betroffene Dateien: src/Romulus.Api/Program.cs (middleware order)
    /// </summary>
    [Fact]
    public async Task AuthError_Should_Have_CorrelationId_Header_Issue9()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        // No API key → should get 401

        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        Assert.True(
            response.Headers.TryGetValues("X-Correlation-ID", out var values),
            "Auth error (401) must include X-Correlation-ID header. " +
            "Correlation middleware runs after auth, so 401 responses miss it.");
        Assert.Single(values!);
    }

    /// <summary>
    /// Ziel: 429 Rate-Limit-Fehler müssen Retry-After Header enthalten.
    /// Warum rot: RateLimiter gibt nur 429 zurück, ohne Retry-After Header.
    ///   RFC 6585 §4 empfiehlt Retry-After für 429-Antworten.
    /// Betroffene Dateien: src/Romulus.Api/Program.cs (rate limit middleware),
    ///   src/Romulus.Api/RateLimiter.cs
    /// </summary>
    [Fact]
    public async Task RateLimitError_Should_Include_RetryAfterHeader_Issue9()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["RateLimitRequests"] = "1",
            ["RateLimitWindowSeconds"] = "60"
        });
        using var client = CreateAuthClient(factory);

        await client.GetAsync("/health"); // use up the 1 request
        var response = await client.GetAsync("/health"); // 429

        Assert.Equal((HttpStatusCode)429, response.StatusCode);
        Assert.True(
            response.Headers.TryGetValues("Retry-After", out var retryValues),
            "429 response must include Retry-After header per RFC 6585.");
        var retryAfter = int.Parse(retryValues!.First());
        Assert.InRange(retryAfter, 1, 60);
    }

    /// <summary>
    /// Ziel: 429 Rate-Limit-Fehler müssen ebenfalls X-Correlation-ID haben.
    /// Warum rot: Gleicher Middleware-Order-Bug wie bei 401 — Rate-Limit-Prüfung
    ///   kommt VOR der Correlation-ID-Middleware.
    /// Betroffene Dateien: src/Romulus.Api/Program.cs (middleware order)
    /// </summary>
    [Fact]
    public async Task RateLimitError_Should_Have_CorrelationId_Header_Issue9()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["RateLimitRequests"] = "1",
            ["RateLimitWindowSeconds"] = "60"
        });
        using var client = CreateAuthClient(factory);

        await client.GetAsync("/health");
        var response = await client.GetAsync("/health"); // 429

        Assert.Equal((HttpStatusCode)429, response.StatusCode);
        Assert.True(
            response.Headers.TryGetValues("X-Correlation-ID", out _),
            "429 response must include X-Correlation-ID header.");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  E: API / CLI / GUI Parity (RED)
    //
    //  Bug: API bietet weniger Konfigurations-Optionen als CLI/GUI.
    //  Gleiche RunOrchestrator-Logik, aber API-Caller können ConflictPolicy
    //  und ConvertOnly nicht setzen → erzwungene Defaults.
    //
    //  Goal:  API exposes the same options as CLI and GUI.
    //  Why RED: RunRequest is missing ConflictPolicy and ConvertOnly fields.
    //  Fix target: src/Romulus.Api/RunManager.cs (RunRequest, RunRecord,
    //   ExecuteWithOrchestrator → RunOptions mapping).
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Ziel: API muss ConflictPolicy akzeptieren (Rename|Skip|Overwrite),
    ///   wie CLI --conflictpolicy und GUI ConflictPolicy-ComboBox.
    /// Warum rot: RunRequest hat kein ConflictPolicy-Property;
    ///   JSON-Feld wird beim Deserialisieren stillschweigend ignoriert.
    /// Betroffene Dateien: src/Romulus.Api/RunManager.cs (RunRequest, RunRecord,
    ///   ExecuteWithOrchestrator)
    /// </summary>
    [Fact]
    public async Task RunRequest_Should_Support_ConflictPolicy_Issue9()
    {
        using var factory = CreateFactory();
        using var client = CreateAuthClient(factory);
        var root = CreateTempRoot();

        var payload = JsonSerializer.Serialize(new
        {
            roots = new[] { root },
            mode = "DryRun",
            conflictPolicy = "Skip"
        });

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/runs?wait=true", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var run = doc.RootElement.GetProperty("run");

        Assert.True(run.TryGetProperty("conflictPolicy", out var policy),
            "RunRecord must expose 'conflictPolicy' in the response.");
        Assert.Equal("Skip", policy.GetString());
    }

    /// <summary>
    /// Ziel: API muss ConvertOnly akzeptieren (bool), wie CLI --convertonly
    ///   und GUI ConvertOnly-CheckBox.
    /// Warum rot: RunRequest hat kein ConvertOnly-Property;
    ///   JSON-Feld wird beim Deserialisieren stillschweigend ignoriert.
    /// Betroffene Dateien: src/Romulus.Api/RunManager.cs (RunRequest, RunRecord,
    ///   ExecuteWithOrchestrator)
    /// </summary>
    [Fact]
    public async Task RunRequest_Should_Support_ConvertOnly_Issue9()
    {
        using var factory = CreateFactory();
        using var client = CreateAuthClient(factory);
        var root = CreateTempRoot();

        var payload = JsonSerializer.Serialize(new
        {
            roots = new[] { root },
            mode = "DryRun",
            convertOnly = true
        });

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/runs?wait=true", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var run = doc.RootElement.GetProperty("run");

        Assert.True(run.TryGetProperty("convertOnly", out var co),
            "RunRecord must expose 'convertOnly' in the response.");
        Assert.True(co.GetBoolean());
    }

    /// <summary>
    /// Ziel: DryRun-Result muss Dedupe-Gruppen-Detail enthalten (wie CLI JSON-Output).
    /// Warum rot: API gibt nur aggregierte Zählwerte zurück (groups, keep, dupes),
    ///   nicht die einzelnen Gruppen mit Winners/Losers/GameKey. CLI liefert dieses Detail
    ///   in FormatDryRunJson. API-Clients müssen es auch auswerten können.
    /// Betroffene Dateien: src/Romulus.Api/RunManager.cs (ApiRunResult),
    ///   src/Romulus.Api/Program.cs (POST /runs result shape)
    /// </summary>
    [Fact]
    public async Task DryRun_Result_Should_Include_DedupeGroupsDetail_Issue9()
    {
        using var factory = CreateFactory();
        using var client = CreateAuthClient(factory);
        var root = CreateTempRoot();

        var payload = JsonSerializer.Serialize(new { roots = new[] { root }, mode = "DryRun" });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/runs?wait=true", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = doc.RootElement.GetProperty("result");

        Assert.True(result.TryGetProperty("dedupeGroups", out var groups),
            "DryRun result must include 'dedupeGroups' array with group details " +
            "(gameKey, winners, losers) for parity with CLI JSON output.");
        Assert.Equal(JsonValueKind.Array, groups.ValueKind);
    }

    /// <summary>
    /// Ziel: API muss InvalidConflictPolicy validieren (Rename|Skip|Overwrite).
    /// Warum rot: RunRequest hat kein ConflictPolicy-Feld, also gibt es auch keine
    ///   Validierung dafür. Ungültige Werte werden nie geprüft.
    /// Betroffene Dateien: src/Romulus.Api/Program.cs (POST /runs validation),
    ///   src/Romulus.Api/RunManager.cs (RunRequest)
    /// </summary>
    [Fact]
    public async Task Runs_InvalidConflictPolicy_Returns400_Issue9()
    {
        using var factory = CreateFactory();
        using var client = CreateAuthClient(factory);
        var root = CreateTempRoot();

        var payload = JsonSerializer.Serialize(new
        {
            roots = new[] { root },
            mode = "DryRun",
            conflictPolicy = "DeleteForever"
        });

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/runs", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("conflictPolicy", body, StringComparison.OrdinalIgnoreCase);
    }





    // ═══════════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════════

    private static WebApplicationFactory<Program> CreateFactory(
        Dictionary<string, string?>? overrides = null,
        Func<RunRecord, Romulus.Contracts.Ports.IFileSystem, Romulus.Contracts.Ports.IAuditStore, CancellationToken, RunExecutionOutcome>? executor = null)
    {
        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ApiKey"] = ApiKey,
            ["CorsMode"] = "strict-local",
            ["CorsAllowOrigin"] = "http://127.0.0.1",
            ["RateLimitRequests"] = "120",
            ["RateLimitWindowSeconds"] = "60"
        };

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
                settings[key] = value;
        }

        return ApiTestFactory.Create(settings, executor);
    }

    private static HttpClient CreateAuthClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "Romulus_ApiRed_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "sample.rom"), "test");
        _tempDirs.Add(root);
        return root;
    }

    private static void SafeDeleteDirectory(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch { }
    }

    private static async Task<string> ReadSseContentWithTimeout(HttpResponseMessage response, int timeoutMs)
    {
        var readTask = response.Content.ReadAsStringAsync();
        var completed = await Task.WhenAny(readTask, Task.Delay(timeoutMs));
        if (completed != readTask)
            throw new TimeoutException($"SSE stream did not close within {timeoutMs}ms.");
        return await readTask;
    }

    private static List<SseEvent> ParseSseEvents(string content)
    {
        var events = new List<SseEvent>();
        string? currentEvent = null;
        string? currentData = null;

        foreach (var line in content.Split('\n'))
        {
            if (line.StartsWith("event: "))
                currentEvent = line[7..].Trim();
            else if (line.StartsWith("data: "))
                currentData = line[6..].Trim();
            else if (line.Trim().Length == 0 && currentEvent is not null && currentData is not null)
            {
                events.Add(new SseEvent(currentEvent, currentData));
                currentEvent = null;
                currentData = null;
            }
        }

        return events;
    }

    private sealed record SseEvent(string EventName, string Data);
}
