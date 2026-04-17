using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Monitoring;
using Xunit;

namespace Romulus.Tests.Monitoring;

public sealed class RetroAchievementsComplianceServiceTests
{
    [Fact]
    public async Task CheckAsync_WithSha1Match_ReturnsCompatible()
    {
        var catalog = new FakeRetroAchievementsCatalog
        {
            Sha1Entry = new RetroAchievementsCatalogEntry
            {
                GameId = "12345",
                Title = "Super Metroid",
                ConsoleKey = "snes",
                Sha1Hash = "ABCDEF",
                RequiresPatch = false
            }
        };
        var service = new RetroAchievementsComplianceService(catalog);

        var result = await service.CheckAsync(new RetroAchievementsCheckRequest
        {
            ConsoleKey = "snes",
            Sha1Hash = "abcdef"
        });

        Assert.True(result.IsCompatible);
        Assert.Equal("12345", result.GameId);
        Assert.Equal("Super Metroid", result.Title);
        Assert.Equal("sha1", result.MatchedBy);
        Assert.False(result.RequiresPatch);
    }

    [Fact]
    public async Task CheckAsync_FallsBackToMd5_WhenSha1Misses()
    {
        var catalog = new FakeRetroAchievementsCatalog
        {
            Md5Entry = new RetroAchievementsCatalogEntry
            {
                GameId = "222",
                Title = "Mega Man X",
                ConsoleKey = "snes",
                Md5Hash = "ABCD1234",
                RequiresPatch = true,
                PatchHint = "Use no-intro verified dump"
            }
        };
        var service = new RetroAchievementsComplianceService(catalog);

        var result = await service.CheckAsync(new RetroAchievementsCheckRequest
        {
            ConsoleKey = "snes",
            Sha1Hash = "not-found",
            Md5Hash = "abcd1234"
        });

        Assert.True(result.IsCompatible);
        Assert.Equal("md5", result.MatchedBy);
        Assert.True(result.RequiresPatch);
        Assert.Equal("Use no-intro verified dump", result.PatchHint);
    }

    [Fact]
    public async Task CheckAsync_WithoutHashes_ReturnsInvalidRequest()
    {
        var service = new RetroAchievementsComplianceService(new FakeRetroAchievementsCatalog());

        var result = await service.CheckAsync(new RetroAchievementsCheckRequest
        {
            ConsoleKey = "snes"
        });

        Assert.False(result.IsCompatible);
        Assert.Equal("InvalidRequest", result.FailureReason);
    }

    [Fact]
    public async Task CheckBatchAsync_ProcessesAllRequests()
    {
        var catalog = new FakeRetroAchievementsCatalog
        {
            Sha1Entry = new RetroAchievementsCatalogEntry
            {
                GameId = "1",
                Title = "Game A",
                ConsoleKey = "snes",
                Sha1Hash = "AAA"
            }
        };
        var service = new RetroAchievementsComplianceService(catalog);

        var results = await service.CheckBatchAsync(
        [
            new RetroAchievementsCheckRequest { ConsoleKey = "snes", Sha1Hash = "aaa" },
            new RetroAchievementsCheckRequest { ConsoleKey = "snes", Sha1Hash = "bbb" }
        ]);

        Assert.Equal(2, results.Count);
        Assert.True(results[0].IsCompatible);
        Assert.False(results[1].IsCompatible);
    }

    private sealed class FakeRetroAchievementsCatalog : IRetroAchievementsCatalog
    {
        public RetroAchievementsCatalogEntry? Sha1Entry { get; init; }
        public RetroAchievementsCatalogEntry? Md5Entry { get; init; }
        public RetroAchievementsCatalogEntry? Crc32Entry { get; init; }

        public ValueTask<RetroAchievementsCatalogEntry?> FindBySha1Async(string consoleKey, string sha1, CancellationToken ct = default)
            => new(Sha1Entry is not null && string.Equals(Sha1Entry.ConsoleKey, consoleKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Sha1Entry.Sha1Hash, sha1, StringComparison.OrdinalIgnoreCase)
                ? Sha1Entry
                : null);

        public ValueTask<RetroAchievementsCatalogEntry?> FindByMd5Async(string consoleKey, string md5, CancellationToken ct = default)
            => new(Md5Entry is not null && string.Equals(Md5Entry.ConsoleKey, consoleKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Md5Entry.Md5Hash, md5, StringComparison.OrdinalIgnoreCase)
                ? Md5Entry
                : null);

        public ValueTask<RetroAchievementsCatalogEntry?> FindByCrc32Async(string consoleKey, string crc32, CancellationToken ct = default)
            => new(Crc32Entry is not null && string.Equals(Crc32Entry.ConsoleKey, consoleKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Crc32Entry.Crc32Hash, crc32, StringComparison.OrdinalIgnoreCase)
                ? Crc32Entry
                : null);
    }
}
