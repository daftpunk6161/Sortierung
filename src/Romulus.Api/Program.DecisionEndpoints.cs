using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Romulus.Api;
using Romulus.Contracts;
using Romulus.Contracts.Errors;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Reporting;

/// <summary>
/// Wave 4 — T-W4-DECISION-EXPLAINER. API surface for the Decision Explainer.
/// Returns the same projection that the GUI Drawer and the CLI
/// <c>romulus explain</c> subcommand consume — single source of truth lives
/// in <see cref="DecisionExplainerProjection"/>.
/// </summary>
public partial class Program
{
    internal static void MapDecisionExplainerEndpoints(WebApplication app, bool trustForwardedFor)
    {
        // Full list for a run.
        app.MapGet("/runs/{runId}/decisions", (string runId, HttpContext ctx, RunLifecycleManager mgr) =>
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
            if (run.CoreRunResult is null)
                return ApiError(404, ApiErrorCodes.RunNotFound, "Run has no result available.", runId: runId);

            var explanations = DecisionExplainerProjection.Project(run.CoreRunResult);
            return Results.Ok(new { count = explanations.Count, explanations });
        })
            .WithSummary("List decision explanations for a completed run")
            .Produces(StatusCodes.Status200OK)
            .Produces<OperationErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<OperationErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<OperationErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<OperationErrorResponse>(StatusCodes.Status409Conflict);

        // Single decision lookup. {key} is "consoleKey/gameKey" composite, separated by '!'
        // because '/' would conflict with route separators. The CLI/GUI use the
        // same convention to build the URL.
        app.MapGet("/runs/{runId}/decisions/{key}", (string runId, string key, HttpContext ctx, RunLifecycleManager mgr) =>
        {
            if (!Guid.TryParse(runId, out _))
                return ApiError(400, ApiErrorCodes.RunInvalidId, "Invalid run ID format.");
            if (string.IsNullOrWhiteSpace(key) || !key.Contains('!', StringComparison.Ordinal))
                return ApiError(400, ApiErrorCodes.RunInvalidId, "key must be 'consoleKey!gameKey'.");

            var run = mgr.Get(runId);
            if (run is null)
                return ApiError(404, ApiErrorCodes.RunNotFound, "Run not found.", runId: runId);
            if (!CanAccessRun(run, GetClientBindingId(ctx, trustForwardedFor)))
                return ApiError(403, ApiErrorCodes.AuthForbidden, "Run belongs to a different client.", ErrorKind.Critical, runId: runId);
            if (run.Status == RunConstants.StatusRunning)
                return ApiError(409, ApiErrorCodes.RunInProgress, "Run still in progress.", runId: runId);
            if (run.CoreRunResult is null)
                return ApiError(404, ApiErrorCodes.RunNotFound, "Run has no result available.", runId: runId);

            var parts = key.Split('!', 2);
            var explanations = DecisionExplainerProjection.Project(run.CoreRunResult);
            var hit = DecisionExplainerProjection.Find(explanations, parts[0], parts[1]);
            return hit is null
                ? ApiError(404, ApiErrorCodes.RunNotFound, "Decision explanation not found.", runId: runId)
                : Results.Ok(hit);
        })
            .WithSummary("Get a single decision explanation by consoleKey!gameKey")
            .Produces<DecisionExplanation>(StatusCodes.Status200OK)
            .Produces<OperationErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<OperationErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<OperationErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<OperationErrorResponse>(StatusCodes.Status409Conflict);
    }
}
