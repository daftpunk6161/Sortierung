namespace Romulus.Contracts.Models;

/// <summary>
/// An entry from the RetroAchievements game catalog, keyed by hash.
/// </summary>
public sealed record RetroAchievementsCatalogEntry
{
    public string GameId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string ConsoleKey { get; init; } = string.Empty;
    public string? Sha1Hash { get; init; }
    public string? Md5Hash { get; init; }
    public string? Crc32Hash { get; init; }
    public bool RequiresPatch { get; init; }
    public string? PatchHint { get; init; }
}

/// <summary>
/// Request to check a ROM file against the RetroAchievements catalog.
/// </summary>
public sealed record RetroAchievementsCheckRequest
{
    public string ConsoleKey { get; init; } = string.Empty;
    public string? Sha1Hash { get; init; }
    public string? Md5Hash { get; init; }
    public string? Crc32Hash { get; init; }
}

/// <summary>
/// Result of a RetroAchievements compatibility check.
/// </summary>
public sealed record RetroAchievementsCheckResult
{
    public bool IsCompatible { get; init; }
    public string? GameId { get; init; }
    public string? Title { get; init; }
    /// <summary>"sha1", "md5", or "crc32" depending on which hash matched.</summary>
    public string? MatchedBy { get; init; }
    public bool RequiresPatch { get; init; }
    public string? PatchHint { get; init; }
    /// <summary>Set to "InvalidRequest" when no hash was supplied.</summary>
    public string? FailureReason { get; init; }
}
