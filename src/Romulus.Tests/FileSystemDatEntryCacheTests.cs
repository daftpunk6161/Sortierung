using Romulus.Infrastructure.Dat;
using Xunit;

namespace Romulus.Tests;

public sealed class FileSystemDatEntryCacheTests : IDisposable
{
    private readonly string _tempDir;
    private readonly List<string> _log = [];

    public FileSystemDatEntryCacheTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DatEntryCache_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Constructor_RejectsBlankCacheDirectory()
    {
        Assert.Throws<ArgumentException>(() => new FileSystemDatEntryCache(" "));
    }

    [Fact]
    public void TryGetAndSet_IgnoreMissingOrInvalidDatInputs()
    {
        var cache = new FileSystemDatEntryCache(_tempDir, _log.Add);

        Assert.False(cache.TryGet("", "SHA1", out _));
        Assert.False(cache.TryGet(Path.Combine(_tempDir, "missing.dat"), "SHA1", out _));

        cache.Set(Path.Combine(_tempDir, "missing.dat"), "SHA1", CreatePayload());

        Assert.Empty(Directory.GetFiles(_tempDir, "*.dcache.json"));
        Assert.Empty(_log);
    }

    [Fact]
    public void SetThenTryGet_RoundTripsPayloadFromDisk_AndUsesHotCache()
    {
        var datPath = CreateDatFile("roundtrip.dat");
        var writer = new FileSystemDatEntryCache(_tempDir, _log.Add);
        writer.Set(datPath, "sha1", CreatePayload());

        var reader = new FileSystemDatEntryCache(_tempDir, _log.Add);

        Assert.True(reader.TryGet(datPath, "SHA1", out var loaded));
        Assert.Equal("Parent Game", loaded.ParentMap["clone game"]);
        Assert.Equal("Rom One", loaded.Games["game one"][0]["name"]);
        Assert.Equal("ABC123", loaded.Games["GAME ONE"][0]["sha1"]);

        foreach (var file in Directory.GetFiles(_tempDir, "*.dcache.json"))
            File.Delete(file);

        Assert.True(reader.TryGet(datPath, "sha1", out var hotLoaded));
        Assert.Equal("Parent Game", hotLoaded.ParentMap["CLONE GAME"]);
    }

    [Fact]
    public void TryGet_ReturnsMiss_WhenHashTypeOrSourceMetadataChanges()
    {
        var datPath = CreateDatFile("stale.dat");
        var writer = new FileSystemDatEntryCache(_tempDir, _log.Add);
        writer.Set(datPath, "SHA1", CreatePayload());

        var hashMissReader = new FileSystemDatEntryCache(_tempDir, _log.Add);
        Assert.False(hashMissReader.TryGet(datPath, "MD5", out _));

        var previousWriteTime = File.GetLastWriteTimeUtc(datPath);
        File.SetLastWriteTimeUtc(datPath, previousWriteTime.AddMinutes(1));

        var staleReader = new FileSystemDatEntryCache(_tempDir, _log.Add);
        Assert.False(staleReader.TryGet(datPath, "SHA1", out _));
    }

    [Fact]
    public void TryGet_DiscardCorruptCache_LogsAndReturnsFalse()
    {
        var datPath = CreateDatFile("corrupt.dat");
        var writer = new FileSystemDatEntryCache(_tempDir, _log.Add);
        writer.Set(datPath, "SHA1", CreatePayload());

        var cacheFile = Assert.Single(Directory.GetFiles(_tempDir, "*.dcache.json"));
        File.WriteAllText(cacheFile, "{not-json");

        var reader = new FileSystemDatEntryCache(_tempDir, _log.Add);

        Assert.False(reader.TryGet(datPath, "SHA1", out _));
        Assert.Contains(_log, entry => entry.Contains("Cache-Hit verworfen", StringComparison.OrdinalIgnoreCase));
    }

    private string CreateDatFile(string name)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, "<datafile />");
        return path;
    }

    private static CachedDatPayload CreatePayload()
        => new()
        {
            ParentMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Clone Game"] = "Parent Game"
            },
            Games = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Game One"] =
                [
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Name"] = "Rom One",
                        ["SHA1"] = "ABC123"
                    }
                ]
            }
        };
}

public sealed class DatPrewarmServiceTests : IDisposable
{
    private readonly string _tempDir;

    public DatPrewarmServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DatPrewarm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task StartAsync_InvalidRoot_CompletesWithoutCacheAccess()
    {
        var cache = new HitOnlyCache();
        var service = new DatPrewarmService(cache);

        await service.StartAsync("");
        await service.StartAsync(Path.Combine(_tempDir, "missing"));

        Assert.Empty(cache.Requests);
    }

    [Fact]
    public async Task StartAsync_CacheHitPath_EnumeratesDatAndXmlOnly_AndLogsSummary()
    {
        var nested = Path.Combine(_tempDir, "nested");
        Directory.CreateDirectory(nested);
        var dat = Path.Combine(_tempDir, "one.dat");
        var xml = Path.Combine(_tempDir, "two.xml");
        var nestedDat = Path.Combine(nested, "three.DAT");
        File.WriteAllText(dat, "<datafile />");
        File.WriteAllText(xml, "<datafile />");
        File.WriteAllText(nestedDat, "<datafile />");
        File.WriteAllText(Path.Combine(_tempDir, "ignored.txt"), "not a dat");
        var cache = new HitOnlyCache();
        var log = new List<string>();
        var service = new DatPrewarmService(cache, log.Add);

        await service.StartAsync(_tempDir, hashType: "MD5");

        Assert.Equal(
            new[] { dat, nestedDat, xml }.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase),
            cache.Requests.Select(static request => request.Path).OrderBy(static path => path, StringComparer.OrdinalIgnoreCase));
        Assert.All(cache.Requests, request => Assert.Equal("MD5", request.HashType));
        Assert.Contains(log, entry => entry.Contains("fertig", StringComparison.OrdinalIgnoreCase)
                                      && entry.Contains("3 DAT(s)", StringComparison.Ordinal)
                                      && entry.Contains("Cache: 3", StringComparison.Ordinal)
                                      && entry.Contains("neu geparst: 0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartAsync_MissPath_ParsesDatAndStoresPayload()
    {
        var dat = Path.Combine(_tempDir, "parse.dat");
        await File.WriteAllTextAsync(dat, """
            <?xml version="1.0"?>
            <datafile>
              <game name="Game One" cloneof="Parent Game">
                <rom name="game.bin" sha1="ABC123" size="42" />
              </game>
            </datafile>
            """);
        var cache = new MissThenStoreCache();
        var log = new List<string>();
        var service = new DatPrewarmService(cache, log.Add);

        await service.StartAsync(_tempDir, hashType: "SHA1");

        var stored = Assert.Single(cache.Stored);
        Assert.Equal(dat, stored.Path);
        Assert.True(stored.Payload.ParentMap.ContainsKey("Game One"));
        Assert.True(stored.Payload.Games.ContainsKey("Game One"));
        Assert.Contains(log, entry => entry.Contains("neu geparst: 1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartAsync_WhenCancelledDuringScan_LogsAbort()
    {
        File.WriteAllText(Path.Combine(_tempDir, "one.dat"), "<datafile />");
        File.WriteAllText(Path.Combine(_tempDir, "two.dat"), "<datafile />");
        using var cts = new CancellationTokenSource();
        var cache = new CancellingHitCache(cts);
        var log = new List<string>();
        var service = new DatPrewarmService(cache, log.Add);

        await service.StartAsync(_tempDir, externalToken: cts.Token);

        Assert.Single(cache.Requests);
        Assert.Contains(log, entry => entry.Contains("abgebrochen", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class HitOnlyCache : IDatEntryCache
    {
        public List<(string Path, string HashType)> Requests { get; } = [];

        public bool TryGet(string datPath, string hashType, out CachedDatPayload payload)
        {
            Requests.Add((datPath, hashType));
            payload = new CachedDatPayload();
            return true;
        }

        public void Set(string datPath, string hashType, CachedDatPayload payload)
        {
            throw new InvalidOperationException("Cache-hit prewarm path must not write payloads.");
        }
    }

    private sealed class MissThenStoreCache : IDatEntryCache
    {
        public List<(string Path, string HashType, CachedDatPayload Payload)> Stored { get; } = [];

        public bool TryGet(string datPath, string hashType, out CachedDatPayload payload)
        {
            payload = null!;
            return false;
        }

        public void Set(string datPath, string hashType, CachedDatPayload payload)
            => Stored.Add((datPath, hashType, payload));
    }

    private sealed class CancellingHitCache : IDatEntryCache
    {
        private readonly CancellationTokenSource _cts;

        public CancellingHitCache(CancellationTokenSource cts)
        {
            _cts = cts;
        }

        public List<string> Requests { get; } = [];

        public bool TryGet(string datPath, string hashType, out CachedDatPayload payload)
        {
            Requests.Add(datPath);
            _cts.Cancel();
            payload = new CachedDatPayload();
            return true;
        }

        public void Set(string datPath, string hashType, CachedDatPayload payload)
            => throw new InvalidOperationException("Cancelled cache-hit path must not write payloads.");
    }
}
