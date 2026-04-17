using Romulus.Contracts.Models;
using Romulus.Infrastructure.Metadata;

public partial class Program
{
    internal static void MapMetadataEndpoints(WebApplication app)
    {
        app.MapPost("/metadata/enrich", async (
            HttpContext ctx,
            MetadataEnrichmentService enrichmentService,
            CancellationToken ct) =>
        {
            MetadataEnrichmentRequest? request;
            try
            {
                request = await ctx.Request.ReadFromJsonAsync<MetadataEnrichmentRequest>(ct);
            }
            catch
            {
                return Results.BadRequest(new { error = "Invalid JSON body." });
            }

            if (request is null || string.IsNullOrWhiteSpace(request.GameKey))
                return Results.BadRequest(new { error = "gameKey is required." });

            var result = await enrichmentService.EnrichAsync(request, ct);
            return Results.Ok(result);
        })
            .WithSummary("Enrich a single game with metadata")
            .Produces<MetadataEnrichmentResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

        app.MapPost("/metadata/enrich/batch", async (
            HttpContext ctx,
            MetadataEnrichmentService enrichmentService,
            CancellationToken ct) =>
        {
            List<MetadataEnrichmentRequest>? requests;
            try
            {
                requests = await ctx.Request.ReadFromJsonAsync<List<MetadataEnrichmentRequest>>(ct);
            }
            catch
            {
                return Results.BadRequest(new { error = "Invalid JSON body." });
            }

            if (requests is null || requests.Count == 0)
                return Results.BadRequest(new { error = "Request array must not be empty." });

            if (requests.Count > 100)
                return Results.BadRequest(new { error = "Batch size must not exceed 100." });

            var results = await enrichmentService.EnrichBatchAsync(requests, ct: ct);
            return Results.Ok(results);
        })
            .WithSummary("Enrich a batch of games with metadata (max 100)")
            .Produces<IReadOnlyList<MetadataEnrichmentResult>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);
    }
}
