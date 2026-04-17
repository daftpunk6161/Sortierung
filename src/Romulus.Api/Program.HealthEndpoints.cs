using Romulus.Contracts.Models;
using Romulus.Infrastructure.Monitoring;

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
    }
}
