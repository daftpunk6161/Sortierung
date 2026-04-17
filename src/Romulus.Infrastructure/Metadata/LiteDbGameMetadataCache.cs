using LiteDB;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.Metadata;

/// <summary>
/// LiteDB-backed implementation of <see cref="IGameMetadataCache"/>.
/// Stores resolved game metadata and negative-cache entries to avoid repeated external API calls.
/// </summary>
public sealed class LiteDbGameMetadataCache : IGameMetadataCache, IDisposable
{
    private const string MetadataCollectionName = "game_metadata";
    private const string NegativeCacheCollectionName = "game_metadata_notfound";
    private static readonly TimeSpan NegativeCacheTtl = TimeSpan.FromDays(7);
    private static readonly TimeSpan PositiveCacheTtl = TimeSpan.FromDays(90);

    private readonly LiteDatabase _database;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public LiteDbGameMetadataCache(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Database path must not be empty.", nameof(databasePath));

        var dir = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        _database = new LiteDatabase($"Filename={Path.GetFullPath(databasePath)};Connection=direct");
        EnsureIndexes();
    }

    internal LiteDbGameMetadataCache(LiteDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        EnsureIndexes();
    }

    public async Task<GameMetadata?> TryGetAsync(string consoleKey, string gameKey, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var collection = _database.GetCollection<GameMetadataDocument>(MetadataCollectionName);
            var key = BuildKey(consoleKey, gameKey);
            var doc = collection.FindById(key);
            if (doc is null) return null;

            // TTL check
            if (DateTime.UtcNow - doc.FetchedUtc > PositiveCacheTtl)
            {
                collection.Delete(key);
                return null;
            }

            return ToModel(doc);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetAsync(string consoleKey, string gameKey, GameMetadata metadata, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var collection = _database.GetCollection<GameMetadataDocument>(MetadataCollectionName);
            var doc = ToDocument(consoleKey, gameKey, metadata);
            collection.Upsert(doc);

            // Clear any negative cache entry
            _database.GetCollection<NegativeCacheDocument>(NegativeCacheCollectionName)
                .Delete(BuildKey(consoleKey, gameKey));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetNotFoundAsync(string consoleKey, string gameKey, string providerName, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var collection = _database.GetCollection<NegativeCacheDocument>(NegativeCacheCollectionName);
            collection.Upsert(new NegativeCacheDocument
            {
                Id = BuildKey(consoleKey, gameKey),
                ConsoleKey = consoleKey,
                GameKey = gameKey,
                ProviderName = providerName,
                RecordedUtc = DateTime.UtcNow
            });
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> IsNotFoundCachedAsync(string consoleKey, string gameKey, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var collection = _database.GetCollection<NegativeCacheDocument>(NegativeCacheCollectionName);
            var key = BuildKey(consoleKey, gameKey);
            var doc = collection.FindById(key);
            if (doc is null) return false;

            // TTL check
            if (DateTime.UtcNow - doc.RecordedUtc > NegativeCacheTtl)
            {
                collection.Delete(key);
                return false;
            }

            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Dispose();
        _database.Dispose();
    }

    private void EnsureIndexes()
    {
        var metaCol = _database.GetCollection<GameMetadataDocument>(MetadataCollectionName);
        metaCol.EnsureIndex(x => x.ConsoleKey);

        var negCol = _database.GetCollection<NegativeCacheDocument>(NegativeCacheCollectionName);
        negCol.EnsureIndex(x => x.ConsoleKey);
    }

    private static string BuildKey(string consoleKey, string gameKey)
        => $"{consoleKey.ToUpperInvariant()}|{gameKey}";

    private static GameMetadataDocument ToDocument(string consoleKey, string gameKey, GameMetadata m)
        => new()
        {
            Id = BuildKey(consoleKey, gameKey),
            ConsoleKey = consoleKey,
            GameKey = gameKey,
            Title = m.Title,
            Description = m.Description,
            Developer = m.Developer,
            Publisher = m.Publisher,
            ReleaseYear = m.ReleaseYear,
            Genres = m.Genres.ToList(),
            Rating = m.Rating,
            Players = m.Players,
            CoverArtUrl = m.CoverArtUrl,
            ScreenshotUrl = m.ScreenshotUrl,
            TitleScreenUrl = m.TitleScreenUrl,
            FanArtUrl = m.FanArtUrl,
            ExternalId = m.ExternalId,
            Source = m.Source,
            FetchedUtc = m.FetchedUtc
        };

    private static GameMetadata ToModel(GameMetadataDocument d)
        => new()
        {
            Title = d.Title,
            Description = d.Description,
            Developer = d.Developer,
            Publisher = d.Publisher,
            ReleaseYear = d.ReleaseYear,
            Genres = d.Genres,
            Rating = d.Rating,
            Players = d.Players,
            CoverArtUrl = d.CoverArtUrl,
            ScreenshotUrl = d.ScreenshotUrl,
            TitleScreenUrl = d.TitleScreenUrl,
            FanArtUrl = d.FanArtUrl,
            ExternalId = d.ExternalId,
            Source = d.Source,
            FetchedUtc = d.FetchedUtc
        };

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    // ── Internal Documents ─────────────────────────────────────

    private sealed class GameMetadataDocument
    {
        public string Id { get; set; } = "";
        public string ConsoleKey { get; set; } = "";
        public string GameKey { get; set; } = "";
        public string Title { get; set; } = "";
        public string? Description { get; set; }
        public string? Developer { get; set; }
        public string? Publisher { get; set; }
        public int? ReleaseYear { get; set; }
        public List<string> Genres { get; set; } = [];
        public double? Rating { get; set; }
        public string? Players { get; set; }
        public string? CoverArtUrl { get; set; }
        public string? ScreenshotUrl { get; set; }
        public string? TitleScreenUrl { get; set; }
        public string? FanArtUrl { get; set; }
        public string? ExternalId { get; set; }
        public string Source { get; set; } = "";
        public DateTime FetchedUtc { get; set; }
    }

    private sealed class NegativeCacheDocument
    {
        public string Id { get; set; } = "";
        public string ConsoleKey { get; set; } = "";
        public string GameKey { get; set; } = "";
        public string ProviderName { get; set; } = "";
        public DateTime RecordedUtc { get; set; }
    }
}
