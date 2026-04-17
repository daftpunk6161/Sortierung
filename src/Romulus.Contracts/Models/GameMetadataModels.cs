namespace Romulus.Contracts.Models;

/// <summary>
/// External game metadata retrieved from metadata providers (ScreenScraper, LibRetro-DB, etc.).
/// This is a contract-safe model: no I/O, no provider-specific details.
/// </summary>
public sealed record GameMetadata
{
    /// <summary>Canonical game title from the metadata source.</summary>
    public string Title { get; init; } = "";

    /// <summary>Short textual description or synopsis.</summary>
    public string? Description { get; init; }

    /// <summary>Developer name.</summary>
    public string? Developer { get; init; }

    /// <summary>Publisher name.</summary>
    public string? Publisher { get; init; }

    /// <summary>Release year (e.g. 1994), when available.</summary>
    public int? ReleaseYear { get; init; }

    /// <summary>Genre tags (e.g. "Action", "RPG").</summary>
    public IReadOnlyList<string> Genres { get; init; } = [];

    /// <summary>Community rating from 0.0 to 5.0, when available.</summary>
    public double? Rating { get; init; }

    /// <summary>Number of players supported.</summary>
    public string? Players { get; init; }

    /// <summary>URL or local path to cover art (box front).</summary>
    public string? CoverArtUrl { get; init; }

    /// <summary>URL or local path to screenshot.</summary>
    public string? ScreenshotUrl { get; init; }

    /// <summary>URL or local path to title screen.</summary>
    public string? TitleScreenUrl { get; init; }

    /// <summary>URL or local path to fan art / banner.</summary>
    public string? FanArtUrl { get; init; }

    /// <summary>External identifier from the metadata source (e.g. ScreenScraper game ID).</summary>
    public string? ExternalId { get; init; }

    /// <summary>Name of the metadata source that produced this record.</summary>
    public string Source { get; init; } = "";

    /// <summary>UTC timestamp when this metadata was fetched.</summary>
    public DateTime FetchedUtc { get; init; }
}

/// <summary>
/// Request to enrich a game with external metadata.
/// </summary>
public sealed record MetadataEnrichmentRequest
{
    /// <summary>Console key (e.g. "SNES", "PS1").</summary>
    public required string ConsoleKey { get; init; }

    /// <summary>Normalized game key used for matching.</summary>
    public required string GameKey { get; init; }

    /// <summary>SHA1 hash of the ROM for hash-based lookup.</summary>
    public string? Sha1Hash { get; init; }

    /// <summary>CRC32 hash of the ROM for hash-based lookup.</summary>
    public string? Crc32Hash { get; init; }

    /// <summary>MD5 hash of the ROM for hash-based lookup.</summary>
    public string? Md5Hash { get; init; }

    /// <summary>Original file name (for name-based fallback search).</summary>
    public string? FileName { get; init; }
}

/// <summary>
/// Result of a metadata enrichment attempt.
/// </summary>
public sealed record MetadataEnrichmentResult
{
    /// <summary>Whether metadata was found.</summary>
    public bool Found { get; init; }

    /// <summary>The resolved metadata, or null when not found.</summary>
    public GameMetadata? Metadata { get; init; }

    /// <summary>Source that provided the metadata (e.g. "ScreenScraper", "LibRetro-DB", "Cache").</summary>
    public string Source { get; init; } = "";

    /// <summary>Reason when not found (e.g. "NotFound", "RateLimited", "ApiError").</summary>
    public string? FailureReason { get; init; }

    /// <summary>Whether this result came from the local cache.</summary>
    public bool FromCache { get; init; }
}
