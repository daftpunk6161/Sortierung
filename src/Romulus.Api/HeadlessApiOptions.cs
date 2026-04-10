namespace Romulus.Api;

public sealed class HeadlessApiOptions
{
    public int Port { get; init; } = 7878;
    public string BindAddress { get; init; } = "127.0.0.1";
    public bool AllowRemoteClients { get; init; }
    public string? PublicBaseUrl { get; init; }
    public bool DashboardEnabled { get; init; } = true;
    public string[] AllowedRoots { get; init; } = Array.Empty<string>();

    public bool RequiresAllowedRoots => AllowRemoteClients || !IsLoopbackAddress(BindAddress);

    public static HeadlessApiOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new HeadlessApiOptions
        {
            Port = configuration.GetValue("Port", 7878),
            BindAddress = configuration.GetValue("BindAddress", "127.0.0.1"),
            AllowRemoteClients = configuration.GetValue("AllowRemoteClients", false),
            PublicBaseUrl = configuration["PublicBaseUrl"],
            DashboardEnabled = configuration.GetValue("DashboardEnabled", true),
            AllowedRoots = configuration.GetSection("AllowedRoots").Get<string[]>() ?? Array.Empty<string>()
        };
    }

    public string ResolveCorsOrigin(string configuredOrigin, Func<string, string, string> fallbackResolver)
    {
        ArgumentNullException.ThrowIfNull(fallbackResolver);

        if (AllowRemoteClients && Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out var publicUri))
            return publicUri.GetLeftPart(UriPartial.Authority);

        return fallbackResolver("custom", configuredOrigin);
    }

    public void Validate(string? configuredApiKey, bool isDevelopment)
    {
        if (!IsLoopbackAddress(BindAddress) && !AllowRemoteClients)
        {
            throw new InvalidOperationException(
                $"Refusing to bind API to non-loopback address '{BindAddress}' without AllowRemoteClients=true.");
        }

        if (!AllowRemoteClients)
            return;

        if (string.IsNullOrWhiteSpace(configuredApiKey))
        {
            throw new InvalidOperationException(
                "Remote/headless mode requires an explicit ApiKey or ROM_CLEANUP_API_KEY. Development auto-generated keys are not allowed.");
        }

        if (!Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out var publicUri))
        {
            throw new InvalidOperationException(
                "Remote/headless mode requires PublicBaseUrl as an absolute HTTPS URL.");
        }

        if (!string.Equals(publicUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Remote/headless mode requires PublicBaseUrl to use HTTPS.");
        }

        if (string.IsNullOrWhiteSpace(publicUri.Host))
        {
            throw new InvalidOperationException(
                "Remote/headless mode requires PublicBaseUrl with a non-empty host.");
        }

        if (AllowedRoots.Length == 0)
        {
            throw new InvalidOperationException(
                "Remote/headless mode requires at least one AllowedRoots entry.");
        }
    }

    private static bool IsLoopbackAddress(string host)
    {
        return string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "[::1]", StringComparison.OrdinalIgnoreCase);
    }
}
