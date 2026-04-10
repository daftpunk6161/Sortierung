using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Analysis;
using Romulus.Infrastructure.FileSystem;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage boost for CollectionCompareService: NormalizeScope edge cases,
/// RootFingerprintMismatch, ResolveEffectiveEnrichmentFingerprint branches,
/// NormalizeExtensions, NormalizeLabel, and AreEntriesIdentical.
/// Targets ~45 uncovered lines.
/// </summary>
public sealed class CollectionCompareCoverageBoostTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSystemAdapter _fs = new();

    public CollectionCompareCoverageBoostTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CC_Cov_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private string CreateRoot(string name)
    {
        var path = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private string CreateFile(string root, string name, string content = "data")
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, content);
        return path;
    }

    // ===== NormalizeSourceId =====

    [Theory]
    [InlineData(null, "source")]
    [InlineData("", "source")]
    [InlineData("  ", "source")]
    [InlineData("  my-id  ", "my-id")]
    public void NormalizeSourceId_ReturnsExpected(string? input, string expected)
    {
        Assert.Equal(expected, CollectionCompareService.NormalizeSourceId(input));
    }

    // ===== NormalizeLabel =====

    [Theory]
    [InlineData(null, null, "source")]
    [InlineData(null, "  custom  ", "custom")]
    [InlineData("My Label", null, "My Label")]
    [InlineData("  My Label  ", "ignored", "My Label")]
    public void NormalizeLabel_ReturnsExpected(string? label, string? sourceId, string expected)
    {
        Assert.Equal(expected, CollectionCompareService.NormalizeLabel(label, sourceId));
    }

    // ===== NormalizeExtensions: defaults when empty =====

    [Fact]
    public void NormalizeExtensions_EmptyInput_ReturnsDefaults()
    {
        var result = CollectionCompareService.NormalizeExtensions([]);
        Assert.NotEmpty(result);
        Assert.All(result, ext => Assert.StartsWith(".", ext));
    }

    // ===== NormalizeExtensions: adds dot prefix, lowercases =====

    [Fact]
    public void NormalizeExtensions_NormalizesFormat()
    {
        var result = CollectionCompareService.NormalizeExtensions(["SFC", ".NES", "  gba  "]);
        Assert.Contains(".sfc", result);
        Assert.Contains(".nes", result);
        Assert.Contains(".gba", result);
    }

    // ===== NormalizeExtensions: deduplicates =====

    [Fact]
    public void NormalizeExtensions_DeduplicatesCaseInsensitive()
    {
        var result = CollectionCompareService.NormalizeExtensions([".sfc", ".SFC", "sfc"]);
        Assert.Single(result);
    }

    // ===== NormalizeScope: RootFingerprintMismatch =====

    [Fact]
    public void NormalizeScope_FingerprintMismatch_SetsMarker()
    {
        var root = CreateRoot("mismatched");
        var scope = new CollectionSourceScope
        {
            SourceId = "test",
            Label = "Test",
            Roots = [root],
            Extensions = [".sfc"],
            RootFingerprint = "wrong-fingerprint-not-matching-computed"
        };

        var normalized = CollectionCompareService.NormalizeScope(scope);

        Assert.Equal("__root-fingerprint-mismatch__", normalized.RootFingerprint);
    }

    // ===== NormalizeScope: blank fingerprint → computed =====

    [Fact]
    public void NormalizeScope_BlankFingerprint_ComputesFromRoots()
    {
        var root = CreateRoot("computed");
        var scope = new CollectionSourceScope
        {
            SourceId = "test",
            Roots = [root],
            Extensions = [".sfc"]
        };

        var normalized = CollectionCompareService.NormalizeScope(scope);

        Assert.NotEqual("__root-fingerprint-mismatch__", normalized.RootFingerprint);
        Assert.False(string.IsNullOrWhiteSpace(normalized.RootFingerprint));
    }

    // ===== NormalizeScope: filters blank roots =====

    [Fact]
    public void NormalizeScope_BlankRoots_Filtered()
    {
        var root = CreateRoot("valid");
        var scope = new CollectionSourceScope
        {
            Roots = [root, "", "  "],
            Extensions = [".sfc"]
        };

        var normalized = CollectionCompareService.NormalizeScope(scope);

        Assert.Single(normalized.Roots);
    }

    // ===== TryMaterializeSourceAsync: null index =====

    [Fact]
    public async Task TryMaterialze_NullIndex_ReturnsUnavailable()
    {
        var root = CreateRoot("noindex");
        var scope = new CollectionSourceScope
        {
            Roots = [root],
            Extensions = [".sfc"]
        };

        var result = await CollectionCompareService.TryMaterializeSourceAsync(null, _fs, scope);

        Assert.False(result.CanUse);
        Assert.Contains("unavailable", result.Reason!);
    }

    // ===== TryMaterializeSourceAsync: fingerprint mismatch =====

    [Fact]
    public async Task TryMaterialize_FingerprintMismatch_ReturnsUnavailable()
    {
        var root = CreateRoot("fpmismatch");
        CreateFile(root, "rom.sfc");
        var scope = new CollectionSourceScope
        {
            Roots = [root],
            Extensions = [".sfc"],
            RootFingerprint = "totally-wrong"
        };

        var result = await CollectionCompareService.TryMaterializeSourceAsync(
            new StubCollectionIndex(), _fs, scope);

        Assert.False(result.CanUse);
        Assert.Contains("mismatch", result.Reason!);
    }

    // ===== TryMaterializeSourceAsync: empty roots =====

    [Fact]
    public async Task TryMaterialize_NoRoots_ReturnsUnavailable()
    {
        var scope = new CollectionSourceScope
        {
            Roots = [],
            Extensions = [".sfc"]
        };

        var result = await CollectionCompareService.TryMaterializeSourceAsync(
            new StubCollectionIndex(), _fs, scope);

        Assert.False(result.CanUse);
        Assert.Contains("roots", result.Reason!);
    }

    // ===== TryMaterializeSourceAsync: no entries in index =====

    [Fact]
    public async Task TryMaterialize_EmptyIndex_ReturnsUnavailable()
    {
        var root = CreateRoot("empty");
        CreateFile(root, "rom.sfc");
        var scope = new CollectionSourceScope
        {
            Roots = [root],
            Extensions = [".sfc"]
        };

        var result = await CollectionCompareService.TryMaterializeSourceAsync(
            new StubCollectionIndex(), _fs, scope);

        Assert.False(result.CanUse);
    }

    // ===== TryMaterializeSourceAsync: entries but no filesystem files =====

    [Fact]
    public async Task TryMaterialize_EmptyScopeWithEmptyIndex_ReturnsSuccess()
    {
        var root = CreateRoot("emptyscope");
        // No files created → both scopedEntries and scopedPaths are empty
        var scope = new CollectionSourceScope
        {
            Roots = [root],
            Extensions = [".sfc"]
        };

        var result = await CollectionCompareService.TryMaterializeSourceAsync(
            new StubCollectionIndex(), _fs, scope);

        Assert.True(result.CanUse);
        Assert.Empty(result.Entries);
    }

    // ===== ResolveEffectiveEnrichmentFingerprint: explicit match =====

    [Fact]
    public void ResolveEffectiveFingerprint_ExplicitMatch_IsReturned()
    {
        var scope = new CollectionSourceScope
        {
            EnrichmentFingerprint = "fp-1",
            Roots = [],
            Extensions = []
        };
        var entries = new[]
        {
            new CollectionIndexEntry { EnrichmentFingerprint = "fp-1", Path = "a.sfc", Extension = ".sfc" },
            new CollectionIndexEntry { EnrichmentFingerprint = "fp-1", Path = "b.sfc", Extension = ".sfc" }
        };

        Assert.Equal("fp-1", CollectionCompareService.ResolveEffectiveEnrichmentFingerprint(scope, entries));
    }

    // ===== ResolveEffectiveEnrichmentFingerprint: mixed → null =====

    [Fact]
    public void ResolveEffectiveFingerprint_MixedInEntries_ReturnsNull()
    {
        var scope = new CollectionSourceScope
        {
            EnrichmentFingerprint = "",
            Roots = [],
            Extensions = []
        };
        var entries = new[]
        {
            new CollectionIndexEntry { EnrichmentFingerprint = "fp-1", Path = "a.sfc", Extension = ".sfc" },
            new CollectionIndexEntry { EnrichmentFingerprint = "fp-2", Path = "b.sfc", Extension = ".sfc" }
        };

        Assert.Null(CollectionCompareService.ResolveEffectiveEnrichmentFingerprint(scope, entries));
    }

    // ===== ResolveEffectiveEnrichmentFingerprint: explicit mismatch → null =====

    [Fact]
    public void ResolveEffectiveFingerprint_ExplicitMismatch_ReturnsNull()
    {
        var scope = new CollectionSourceScope
        {
            EnrichmentFingerprint = "fp-expected",
            Roots = [],
            Extensions = []
        };
        var entries = new[]
        {
            new CollectionIndexEntry { EnrichmentFingerprint = "fp-different", Path = "a.sfc", Extension = ".sfc" }
        };

        Assert.Null(CollectionCompareService.ResolveEffectiveEnrichmentFingerprint(scope, entries));
    }

    // ===== AreEntriesIdentical =====

    [Fact]
    public void AreEntriesIdentical_SameHash_True()
    {
        var left = new CollectionIndexEntry
        {
            Path = "a.sfc", Extension = ".sfc",
            PrimaryHash = "abc123", PrimaryHashType = "SHA1",
            GameKey = "mario", Region = "US", ConsoleKey = "SNES",
            SortDecision = SortDecision.Sort
        };
        var right = new CollectionIndexEntry
        {
            Path = "b.sfc", Extension = ".sfc",
            PrimaryHash = "abc123", PrimaryHashType = "SHA1",
            GameKey = "mario", Region = "US", ConsoleKey = "SNES",
            SortDecision = SortDecision.Sort
        };

        Assert.True(CollectionCompareService.AreEntriesIdentical(left, right));
    }

    [Fact]
    public void AreEntriesIdentical_DifferentHash_False()
    {
        var left = new CollectionIndexEntry
        {
            Path = "a.sfc", Extension = ".sfc",
            PrimaryHash = "abc123", PrimaryHashType = "SHA1",
            GameKey = "mario", Region = "US", ConsoleKey = "SNES",
            SortDecision = SortDecision.Sort
        };
        var right = new CollectionIndexEntry
        {
            Path = "b.sfc", Extension = ".sfc",
            PrimaryHash = "def456", PrimaryHashType = "SHA1",
            GameKey = "mario", Region = "US", ConsoleKey = "SNES",
            SortDecision = SortDecision.Sort
        };

        Assert.False(CollectionCompareService.AreEntriesIdentical(left, right));
    }

    // ===== GetIdentitySignature =====

    [Fact]
    public void GetIdentitySignature_Deterministic()
    {
        var entry = new CollectionIndexEntry
        {
            Path = "a.sfc", Extension = ".sfc",
            PrimaryHash = "abc123", PrimaryHashType = "SHA1",
            ConsoleKey = "SNES", GameKey = "mario", Region = "US",
            RegionScore = 100, FormatScore = 50, VersionScore = 1,
            SortDecision = SortDecision.Sort
        };

        var sig1 = CollectionCompareService.GetIdentitySignature(entry);
        var sig2 = CollectionCompareService.GetIdentitySignature(entry);

        Assert.Equal(sig1, sig2);
        Assert.NotEmpty(sig1);
    }

    // ===== MaterializeCandidates =====

    [Fact]
    public void MaterializeCandidates_OrdersByPath()
    {
        var entries = new[]
        {
            new CollectionIndexEntry { Path = "z.sfc", Extension = ".sfc", GameKey = "z", ConsoleKey = "SNES", Region = "US", PrimaryHash = "h1", PrimaryHashType = "SHA1" },
            new CollectionIndexEntry { Path = "a.sfc", Extension = ".sfc", GameKey = "a", ConsoleKey = "SNES", Region = "US", PrimaryHash = "h2", PrimaryHashType = "SHA1" }
        };

        var candidates = CollectionCompareService.MaterializeCandidates(entries);

        Assert.Equal(2, candidates.Count);
        Assert.True(string.Compare(candidates[0].MainPath, candidates[1].MainPath, StringComparison.OrdinalIgnoreCase) < 0);
    }

    /// <summary>Minimal collection index stub returning empty results.</summary>
    private sealed class StubCollectionIndex : ICollectionIndex
    {
        public ValueTask<CollectionIndexMetadata> GetMetadataAsync(CancellationToken ct = default)
            => ValueTask.FromResult(new CollectionIndexMetadata());
        public ValueTask<int> CountEntriesAsync(CancellationToken ct = default)
            => ValueTask.FromResult(0);
        public ValueTask<CollectionIndexEntry?> TryGetByPathAsync(string path, CancellationToken ct = default)
            => ValueTask.FromResult<CollectionIndexEntry?>(null);
        public ValueTask<IReadOnlyList<CollectionIndexEntry>> GetByPathsAsync(IReadOnlyList<string> paths, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<CollectionIndexEntry>>([]);
        public ValueTask<IReadOnlyList<CollectionIndexEntry>> ListByConsoleAsync(string consoleKey, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<CollectionIndexEntry>>([]);
        public ValueTask<IReadOnlyList<CollectionIndexEntry>> ListEntriesInScopeAsync(IReadOnlyList<string> roots, IReadOnlyCollection<string> extensions, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<CollectionIndexEntry>>([]);
        public ValueTask UpsertEntriesAsync(IReadOnlyList<CollectionIndexEntry> entries, CancellationToken ct = default)
            => ValueTask.CompletedTask;
        public ValueTask RemovePathsAsync(IReadOnlyList<string> paths, CancellationToken ct = default)
            => ValueTask.CompletedTask;
        public ValueTask<CollectionHashCacheEntry?> TryGetHashAsync(string path, string algorithm, long sizeBytes, DateTime lastWriteUtc, CancellationToken ct = default)
            => ValueTask.FromResult<CollectionHashCacheEntry?>(null);
        public ValueTask SetHashAsync(CollectionHashCacheEntry entry, CancellationToken ct = default)
            => ValueTask.CompletedTask;
        public ValueTask AppendRunSnapshotAsync(CollectionRunSnapshot snapshot, CancellationToken ct = default)
            => ValueTask.CompletedTask;
        public ValueTask<int> CountRunSnapshotsAsync(CancellationToken ct = default)
            => ValueTask.FromResult(0);
        public ValueTask<IReadOnlyList<CollectionRunSnapshot>> ListRunSnapshotsAsync(int limit = 50, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<CollectionRunSnapshot>>([]);
    }
}
