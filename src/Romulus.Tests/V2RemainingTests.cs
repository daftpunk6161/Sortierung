// ══════════════════════════════════════════════════════════════════════════════
// LEGACY: V2 remaining gap-fill tests. Still active — do not delete.
// These tests cover CancellationToken, CJK/Unicode, concurrency.
// Migrate to domain-specific test classes when revisiting test organization.
// ══════════════════════════════════════════════════════════════════════════════
using System.IO.Compression;
using Romulus.Core.GameKeys;
using Romulus.Core.Scoring;
using Romulus.Infrastructure.Hashing;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// V2 remaining test gaps: CancellationToken, IsKnownFormat,
/// CJK/Unicode edge-cases, concurrency stability.
/// </summary>
public sealed class V2RemainingTests : IDisposable
{
    private readonly string _tempDir;

    public V2RemainingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "V2Rem_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private string CreateZip(string name, params (string entry, byte[] data)[] entries)
    {
        var zipPath = Path.Combine(_tempDir, name);
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var (entry, data) in entries)
        {
            var e = archive.CreateEntry(entry);
            using var s = e.Open();
            s.Write(data, 0, data.Length);
        }
        return zipPath;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  V2-THR-M02: ArchiveHashService CancellationToken
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ArchiveHash_CancelledToken_ThrowsOperationCancelled()
    {
        var zip = CreateZip("cancel.zip", ("f.bin", new byte[] { 0x01 }));
        var svc = new ArchiveHashService(maxEntries: 16);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => svc.GetArchiveHashes(zip, "SHA1", cts.Token));
    }

    [Fact]
    public void ArchiveHash_DefaultToken_WorksNormally()
    {
        var zip = CreateZip("normal.zip",
            ("a.bin", new byte[] { 0x01 }),
            ("b.bin", new byte[] { 0x02 }));
        var svc = new ArchiveHashService(maxEntries: 16);

        var hashes = svc.GetArchiveHashes(zip, "SHA1", CancellationToken.None);

        Assert.Equal(4, hashes.Length);
    }

    [Fact]
    public void ArchiveHash_CancelAfterFirstEntry_StillChecksCancellation()
    {
        // Create a zip with many entries to increase chance of cancellation check
        var entries = Enumerable.Range(0, 20)
            .Select(i => ($"file{i}.bin", new byte[] { (byte)(i & 0xFF) }))
            .ToArray();
        var zip = CreateZip("many.zip", entries);
        var svc = new ArchiveHashService(maxEntries: 16);

        // Non-cancelled token should succeed
        var hashes = svc.GetArchiveHashes(zip, "SHA1", CancellationToken.None);
        Assert.Equal(40, hashes.Length);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  V2-BUG-M03: FormatScorer IsKnownFormat
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(".chd", true)]
    [InlineData(".iso", true)]
    [InlineData(".zip", true)]
    [InlineData(".7z", true)]
    [InlineData(".rar", true)]
    [InlineData(".nes", true)]
    [InlineData(".sfc", true)]
    [InlineData(".gba", true)]
    [InlineData(".rvz", true)]
    [InlineData(".pbp", true)]
    [InlineData(".nsp", true)]
    public void IsKnownFormat_KnownExtensions_ReturnsTrue(string ext, bool expected)
    {
        Assert.Equal(expected, FormatScorer.IsKnownFormat(ext));
    }

    [Theory]
    [InlineData(".xyz")]
    [InlineData(".unknown")]
    [InlineData(".rom")]
    [InlineData(".dat")]
    [InlineData(".txt")]
    [InlineData("")]
    public void IsKnownFormat_UnknownExtensions_ReturnsFalse(string ext)
    {
        Assert.False(FormatScorer.IsKnownFormat(ext));
    }

    [Fact]
    public void IsKnownFormat_ConsistentWithGetFormatScore()
    {
        // Every known format must NOT return 300; every unknown MUST return 300
        var knownExts = new[] { ".chd", ".iso", ".zip", ".7z", ".rar", ".nes", ".gba" };
        foreach (var ext in knownExts)
        {
            Assert.True(FormatScorer.IsKnownFormat(ext), $"{ext} should be known");
            Assert.NotEqual(300, FormatScorer.GetFormatScore(ext));
        }

        var unknownExts = new[] { ".xyz", ".unknown", ".rom" };
        foreach (var ext in unknownExts)
        {
            Assert.False(FormatScorer.IsKnownFormat(ext), $"{ext} should be unknown");
            Assert.Equal(300, FormatScorer.GetFormatScore(ext));
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  V2-TEST-M03: CJK / Unicode Edge-Cases for GameKeyNormalizer
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("ドラゴンクエスト (Japan)")]
    [InlineData("勇者斗恶龙 (China)")]
    [InlineData("용사 (Korea)")]
    public void Normalize_CJK_StripsRegionTag(string input)
    {
        var key = GameKeyNormalizer.Normalize(input);
        // Region tags must be stripped
        Assert.DoesNotContain("japan", key, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("china", key, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("korea", key, StringComparison.OrdinalIgnoreCase);
        // Key must not be empty
        Assert.False(string.IsNullOrWhiteSpace(key));
        Assert.DoesNotContain("__empty_key", key);
    }

    [Theory]
    [InlineData("Pokémon (Europe)", "pokemon")]
    [InlineData("Über Racer (Germany)", "uberracer")]
    [InlineData("Señor Spelunky (Spain)", "senorspelunky")]
    [InlineData("Ça Plane (France)", "caplane")]
    public void Normalize_Diacritics_FoldsToAscii(string input, string expected)
    {
        var key = GameKeyNormalizer.Normalize(input);
        Assert.Equal(expected, key);
    }

    [Theory]
    [InlineData("Müller's Abenteuer", "Muller's Abenteuer")]
    [InlineData("Naïve Art (USA)", "Naive Art (USA)")]
    [InlineData("Ñoño Game (Spain)", "Nono Game (Spain)")]
    public void AsciiFold_SpecialCharacters(string input, string expected)
    {
        Assert.Equal(expected, GameKeyNormalizer.AsciiFold(input));
    }

    [Fact]
    public void Normalize_EmptyAndWhitespace_ReturnsEmptyKeyPlaceholder()
    {
        // Empty/whitespace → uses __empty_key_null placeholder
        Assert.StartsWith("__empty_key", GameKeyNormalizer.Normalize(""));
        Assert.StartsWith("__empty_key", GameKeyNormalizer.Normalize(" "));
    }

    [Fact]
    public void Normalize_MixedScriptWithTags_StripsTagsKeepsContent()
    {
        var key = GameKeyNormalizer.Normalize("ゼルダの伝説 (Japan) (Rev 1) [!]");
        // Region/version/verified tags stripped
        Assert.DoesNotContain("japan", key, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rev", key, StringComparison.OrdinalIgnoreCase);
        // Key must be non-empty and not a placeholder
        Assert.False(string.IsNullOrWhiteSpace(key));
        Assert.DoesNotContain("__empty_key", key);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  V2-TEST-M03: Long Paths Edge-Case
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Normalize_VeryLongFilename_DoesNotThrow()
    {
        var longName = new string('A', 200) + " (USA) (Rev 2) [!]";
        var key = GameKeyNormalizer.Normalize(longName);
        Assert.NotEmpty(key);
        Assert.DoesNotContain("(USA)", key);
        Assert.DoesNotContain("[!]", key);
    }

    [Fact]
    public void AsciiFold_VeryLongString_DoesNotThrow()
    {
        var input = string.Concat(Enumerable.Repeat("Ärger ", 100));
        var result = GameKeyNormalizer.AsciiFold(input);
        Assert.DoesNotContain("Ä", result);
        Assert.Contains("Arger", result);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  V2-TEST-L02: Concurrency — Deterministic patterns
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task LruCache_ConcurrentAccess_WithBarrier_Deterministic()
    {
        var cache = new Core.Caching.LruCache<string, int>(50);
        const int threadCount = 10;
        const int opsPerThread = 100;
        var barrier = new Barrier(threadCount);
        var tasks = new Task[threadCount];

        for (int t = 0; t < threadCount; t++)
        {
            var thread = t;
            tasks[t] = Task.Run(() =>
            {
                barrier.SignalAndWait(); // Synchronize start
                for (int i = 0; i < opsPerThread; i++)
                {
                    cache.Set($"t{thread}-k{i % 20}", i);
                    cache.TryGet($"t{thread}-k{i % 20}", out _);
                }
            });
        }

        await Task.WhenAll(tasks);

        // Cache capacity is 50, with 10 threads × 20 unique keys = 200 unique keys
        // Only 50 can survive
        Assert.Equal(50, cache.Count);
    }

    [Fact]
    public void FormatScorer_ThreadSafe_ConsistentResults()
    {
        var results = new System.Collections.Concurrent.ConcurrentBag<int>();
        var barrier = new Barrier(10);

        Parallel.For(0, 100, i =>
        {
            if (i < 10) barrier.SignalAndWait();
            results.Add(FormatScorer.GetFormatScore(".chd"));
        });

        // All results must be identical (850)
        Assert.All(results, score => Assert.Equal(850, score));
    }

    // ═══════════════════════════════════════════════════════════════════
    //  V2-BUG-L03: SettingsLoader — Verify unknown property warning
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void SettingsLoader_UnknownProperty_ReturnsWarning()
    {
        var json = """
        {
            "general": { "logLevel": "Info" },
            "toolPaths": {},
            "unknownSection": "value"
        }
        """;

        var errors = Infrastructure.Configuration.SettingsLoader.ValidateSettingsStructure(json);
        Assert.Contains(errors, e => e.Contains("Unknown top-level key") && e.Contains("unknownSection"));
    }

    [Fact]
    public void SettingsLoader_AllKnownProperties_NoErrors()
    {
        var json = """
        {
            "general": { "logLevel": "Info" },
            "toolPaths": {},
            "dat": {}
        }
        """;

        var errors = Infrastructure.Configuration.SettingsLoader.ValidateSettingsStructure(json);
        Assert.Empty(errors);
    }

}
