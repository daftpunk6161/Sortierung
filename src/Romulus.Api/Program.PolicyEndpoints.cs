using Romulus.Contracts;
using Romulus.Contracts.Errors;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.Policy;
using Romulus.Infrastructure.Safety;

public partial class Program
{
    internal static void MapPolicyEndpoints(WebApplication app)
    {
        app.MapPost("/policies/validate", async (
            HttpContext ctx,
            ICollectionIndex collectionIndex,
            IPolicyEngine policyEngine,
            AuditSigningService auditSigningService,
            ITimeProvider timeProvider,
            AllowedRootPathPolicy allowedRootPolicy,
            CancellationToken ct) =>
        {
            var requestRead = await ReadJsonBodyAsync<PolicyValidationRequest>(ctx, "POLICY-VALIDATE", ct);
            if (requestRead.Error is not null)
                return requestRead.Error;

            var request = requestRead.Value!;
            if (string.IsNullOrWhiteSpace(request.PolicyText))
                return ApiError(400, ApiErrorCodes.PolicyTextRequired, "policyText is required.");

            if (request.Roots.Length == 0)
                return ApiError(400, ApiErrorCodes.PolicyRootsRequired, "roots[] is required.");

            foreach (var root in request.Roots)
            {
                if (string.IsNullOrWhiteSpace(root))
                    return ApiError(400, ApiErrorCodes.PolicyRootEmpty, "Empty root path in roots[].");

                var rootValidation = ValidateRootSecurity(root, allowedRootPolicy);
                if (rootValidation is not null)
                    return rootValidation;

                if (!Directory.Exists(root))
                    return ApiError(400, ApiErrorCodes.IoRootNotFound, $"Root not found: {root}");
            }

            LibraryPolicy policy;
            try
            {
                policy = PolicyDocumentLoader.Parse(request.PolicyText);
            }
            catch (FormatException ex)
            {
                return ApiError(400, ApiErrorCodes.PolicyInvalid, ex.Message);
            }

            var extensions = NormalizePolicyRequestExtensions(request.Extensions);
            var entries = await collectionIndex.ListEntriesInScopeAsync(request.Roots, extensions, ct);
            var snapshot = LibrarySnapshotProjection.FromCollectionIndex(
                entries,
                request.Roots,
                timeProvider.UtcNow.UtcDateTime);
            var fingerprint = PolicyDocumentLoader.ComputeFingerprint(request.PolicyText);
            var signature = PolicyDocumentLoader.VerifySignatureText(
                request.PolicyText,
                request.PolicySignatureText,
                auditSigningService);
            var report = policyEngine.Validate(snapshot, policy, fingerprint) with
            {
                Signature = signature
            };
            return Results.Ok(report);
        })
            .WithSummary("Validate the persisted collection index against a declarative target-state policy")
            .WithTags("Policy")
            .Accepts<PolicyValidationRequest>("application/json")
            .Produces<PolicyValidationReport>(StatusCodes.Status200OK)
            .Produces<OperationErrorResponse>(StatusCodes.Status400BadRequest);

        app.MapPost("/policies/sign", async (
            HttpContext ctx,
            AuditSigningService auditSigningService,
            ITimeProvider timeProvider,
            CancellationToken ct) =>
        {
            var requestRead = await ReadJsonBodyAsync<PolicySignRequest>(ctx, "POLICY-SIGN", ct);
            if (requestRead.Error is not null)
                return requestRead.Error;

            var request = requestRead.Value!;
            if (string.IsNullOrWhiteSpace(request.PolicyText))
                return ApiError(400, ApiErrorCodes.PolicyTextRequired, "policyText is required.");

            try
            {
                PolicyDocumentLoader.Parse(request.PolicyText);
            }
            catch (FormatException ex)
            {
                return ApiError(400, ApiErrorCodes.PolicyInvalid, ex.Message);
            }

            var signature = PolicyDocumentLoader.CreateSignature(
                request.PolicyText,
                request.PolicyFileName,
                auditSigningService,
                timeProvider.UtcNow.UtcDateTime,
                request.Signer);
            return Results.Ok(signature);
        })
            .WithSummary("Create a detached signature document for a policy text")
            .WithTags("Policy")
            .Accepts<PolicySignRequest>("application/json")
            .Produces<PolicySignatureDocument>(StatusCodes.Status200OK)
            .Produces<OperationErrorResponse>(StatusCodes.Status400BadRequest);
    }

    private static IReadOnlyCollection<string> NormalizePolicyRequestExtensions(IReadOnlyList<string> extensions)
    {
        if (extensions.Count == 0)
            return RunOptions.DefaultExtensions;

        return extensions
            .Where(static extension => !string.IsNullOrWhiteSpace(extension))
            .Select(static extension =>
            {
                var trimmed = extension.Trim();
                return trimmed.StartsWith(".", StringComparison.Ordinal) ? trimmed : "." + trimmed;
            })
            .Select(static extension => extension.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static extension => extension, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
