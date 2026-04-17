namespace Romulus.Contracts.Models;

/// <summary>
/// Configuration for a game metadata provider.
/// Stored in user settings, not hardcoded.
/// </summary>
public sealed record MetadataProviderSettings
{
    /// <summary>Provider name for identification.</summary>
    public string ProviderName { get; init; } = "";

    /// <summary>Whether this provider is enabled.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Developer/API user identifier. Not a secret per se (ScreenScraper requires one for all calls).
    /// Must be provided by the user in their settings.
    /// </summary>
    public string? DevId { get; init; }

    /// <summary>Developer/API password. Stored in user settings, not in code.</summary>
    public string? DevPassword { get; init; }

    /// <summary>User's ScreenScraper account username (for higher rate limits).</summary>
    public string? Username { get; init; }

    /// <summary>User's ScreenScraper account password.</summary>
    public string? UserPassword { get; init; }

    /// <summary>Maximum requests per second to this provider.</summary>
    public double MaxRequestsPerSecond { get; init; } = 1.0;

    /// <summary>Preferred media region for artwork (e.g. "us", "eu", "jp", "wor").</summary>
    public string PreferredMediaRegion { get; init; } = "us";
}
