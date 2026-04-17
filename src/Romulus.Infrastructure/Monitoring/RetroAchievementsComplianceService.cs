using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.Monitoring;

/// <summary>
/// Checks ROM files for RetroAchievements compatibility using the catalog port.
/// Hash lookup priority: SHA1 → MD5 → CRC32.
/// </summary>
public sealed class RetroAchievementsComplianceService(IRetroAchievementsCatalog catalog)
{
    private readonly IRetroAchievementsCatalog _catalog =
        catalog ?? throw new ArgumentNullException(nameof(catalog));

    public async Task<RetroAchievementsCheckResult> CheckAsync(
        RetroAchievementsCheckRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Sha1Hash)
            && string.IsNullOrWhiteSpace(request.Md5Hash)
            && string.IsNullOrWhiteSpace(request.Crc32Hash))
        {
            return new RetroAchievementsCheckResult
            {
                IsCompatible = false,
                FailureReason = "InvalidRequest"
            };
        }

        if (!string.IsNullOrWhiteSpace(request.Sha1Hash))
        {
            var entry = await _catalog.FindBySha1Async(request.ConsoleKey, request.Sha1Hash, ct);
            if (entry is not null)
                return BuildResult(entry, "sha1");
        }

        if (!string.IsNullOrWhiteSpace(request.Md5Hash))
        {
            var entry = await _catalog.FindByMd5Async(request.ConsoleKey, request.Md5Hash, ct);
            if (entry is not null)
                return BuildResult(entry, "md5");
        }

        if (!string.IsNullOrWhiteSpace(request.Crc32Hash))
        {
            var entry = await _catalog.FindByCrc32Async(request.ConsoleKey, request.Crc32Hash, ct);
            if (entry is not null)
                return BuildResult(entry, "crc32");
        }

        return new RetroAchievementsCheckResult { IsCompatible = false };
    }

    public async Task<IReadOnlyList<RetroAchievementsCheckResult>> CheckBatchAsync(
        IReadOnlyList<RetroAchievementsCheckRequest> requests,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(requests);

        var results = new RetroAchievementsCheckResult[requests.Count];
        for (var i = 0; i < requests.Count; i++)
            results[i] = await CheckAsync(requests[i], ct);

        return results;
    }

    private static RetroAchievementsCheckResult BuildResult(
        RetroAchievementsCatalogEntry entry,
        string matchedBy)
        => new()
        {
            IsCompatible = true,
            GameId = entry.GameId,
            Title = entry.Title,
            MatchedBy = matchedBy,
            RequiresPatch = entry.RequiresPatch,
            PatchHint = entry.PatchHint
        };
}
