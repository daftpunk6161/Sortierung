using Romulus.Contracts.Models;

namespace Romulus.Contracts.Ports;

/// <summary>
/// Port for looking up ROM entries in the RetroAchievements catalog.
/// Supports SHA1, MD5, and CRC32 lookups with console-key scoping.
/// </summary>
public interface IRetroAchievementsCatalog
{
    ValueTask<RetroAchievementsCatalogEntry?> FindBySha1Async(
        string consoleKey, string sha1, CancellationToken ct = default);

    ValueTask<RetroAchievementsCatalogEntry?> FindByMd5Async(
        string consoleKey, string md5, CancellationToken ct = default);

    ValueTask<RetroAchievementsCatalogEntry?> FindByCrc32Async(
        string consoleKey, string crc32, CancellationToken ct = default);
}
