using Microsoft.AspNetCore.Http;
using Romulus.Api;
using Romulus.Contracts;
using Romulus.Contracts.Errors;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Profiles;
using Romulus.Infrastructure.Workflow;

public partial class Program
{
    internal static void MapProfileWorkflowEndpoints(WebApplication app)
    {
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
                ? ApiError(404, ApiErrorCodes.ProfileNotFound, $"Profile '{id}' was not found.")
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
                return ApiError(400, ApiErrorCodes.ProfileInvalid, ex.Message);
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
                    : ApiError(404, ApiErrorCodes.ProfileNotFound, $"Profile '{id}' was not found.");
            }
            catch (InvalidOperationException ex)
            {
                return ApiError(400, ApiErrorCodes.ProfileDeleteBlocked, ex.Message);
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
                ? ApiError(404, ApiErrorCodes.WorkflowNotFound, $"Workflow '{id}' was not found.")
                : Results.Ok(workflow);
        })
            .WithSummary("List guided workflow scenarios or fetch one by id")
            .Produces<ApiWorkflowListResponse>(StatusCodes.Status200OK)
            .Produces<OperationErrorResponse>(StatusCodes.Status404NotFound);

        app.MapGet("/workflows/{id}", (string id) =>
        {
            var workflow = WorkflowScenarioCatalog.TryGet(id);
            return workflow is null
                ? ApiError(404, ApiErrorCodes.WorkflowNotFound, $"Workflow '{id}' was not found.")
                : Results.Ok(workflow);
        })
            .WithSummary("Get a guided workflow scenario")
            .Produces<OperationErrorResponse>(StatusCodes.Status404NotFound);
    }
}