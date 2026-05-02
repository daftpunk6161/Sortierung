using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Romulus.Contracts.Errors;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Provenance;

public partial class Program
{
    internal static void MapProvenanceEndpoints(WebApplication app)
    {
        app.MapGet("/roms/{fingerprint}/provenance", (string fingerprint, IProvenanceStore store) =>
        {
            try
            {
                var trail = ProvenanceTrailProjection.Project(store, fingerprint);
                return Results.Ok(trail);
            }
            catch (ArgumentException ex)
            {
                return ApiError(400, ApiErrorCodes.ProvenanceInvalidFingerprint, ex.Message);
            }
        })
            .WithSummary("Read the append-only provenance trail for one ROM fingerprint")
            .Produces<ProvenanceTrail>(StatusCodes.Status200OK)
            .Produces<OperationErrorResponse>(StatusCodes.Status400BadRequest);
    }
}
