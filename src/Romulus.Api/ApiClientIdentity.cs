using System.Net;

namespace Romulus.Api;

/// <summary>
/// Resolves the client identity used for API rate limiting.
/// Trust X-Forwarded-For only when explicitly enabled and the direct peer is loopback.
/// </summary>
public static class ApiClientIdentity
{
    public static string ResolveRateLimitClientId(HttpContext context, bool trustForwardedFor)
    {
        ArgumentNullException.ThrowIfNull(context);

        var remoteIp = context.Connection.RemoteIpAddress;
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        var canTrustForwardedFor = trustForwardedFor && remoteIp is not null && IPAddress.IsLoopback(remoteIp);

        if (canTrustForwardedFor && !string.IsNullOrWhiteSpace(forwardedFor))
        {
            var first = forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(first))
                return first;
        }

        return remoteIp?.ToString() ?? "unknown";
    }
}
