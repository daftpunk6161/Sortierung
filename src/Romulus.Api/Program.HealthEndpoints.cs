using Romulus.Contracts.Models;
using Romulus.Contracts;
using Romulus.Contracts.Errors;
using Romulus.Infrastructure.Monitoring;
using Romulus.Infrastructure.Safety;

public partial class Program
{
    internal static void MapHealthEndpoints(WebApplication app)
    {
        app.MapGet("/health/collection", async (
            HttpContext ctx,
            CollectionHealthMonitor monitor,
            CancellationToken ct) =>
        {
            var consoleFilter = ctx.Request.Query["console"].FirstOrDefault();
            var report = await monitor.GenerateReportAsync(
                consoleFilter: string.IsNullOrWhiteSpace(consoleFilter) ? null : consoleFilter)
                .ConfigureAwait(false);
            return Results.Ok(report);
        })
        .WithName("GetCollectionHealth")
        .WithTags("Health")
        .Produces<CollectionHealthReport>(200);

        app.MapGet("/collections/{root}/health", async (
            string root,
            HttpContext ctx,
            CollectionHealthMonitor monitor,
            AllowedRootPathPolicy allowedRootPolicy,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(root))
                return ApiError(400, ApiErrorCodes.RunRootEmpty, "root is required.");

            var decodedRoot = Uri.UnescapeDataString(root);
            var rootValidation = ValidateRootSecurity(decodedRoot, allowedRootPolicy);
            if (rootValidation is not null)
                return rootValidation;

            if (!Directory.Exists(decodedRoot))
                return ApiError(400, ApiErrorCodes.IoRootNotFound, $"Root not found: {decodedRoot}");

            var consoleFilter = ctx.Request.Query["console"].FirstOrDefault();
            var report = await monitor.GenerateReportAsync(
                roots: [decodedRoot],
                extensions: RunOptions.DefaultExtensions,
                consoleFilter: string.IsNullOrWhiteSpace(consoleFilter) ? null : consoleFilter,
                ct: ct).ConfigureAwait(false);
            return Results.Ok(report);
        })
        .WithName("GetCollectionHealthForRoot")
        .WithTags("Health")
        .Produces<CollectionHealthReport>(200)
        .Produces<OperationErrorResponse>(400);
    }
}
