using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Romulus.Api;
using Romulus.Contracts.Errors;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Xunit;

namespace Romulus.Tests;

public sealed class ApiCollectionDiffMergeTests : IDisposable
{
    private const string ApiKey = "collection-diff-merge-api-key";
    private readonly string _tempDir;

    public ApiCollectionDiffMergeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApiCollectionDiffMerge_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    [Fact]
    public async Task CollectionsCompare_ReturnsDeterministicDiffPayload()
    {
        var leftRoot = CreateRoot("left");
        var rightRoot = CreateRoot("right");
        var leftPath = CreateFile(leftRoot, "SNES", "Mario.sfc", "left");
        var rightPath = CreateFile(rightRoot, "SNES", "Mario.sfc", "right");

        using var factory = CreateFactory(collectionIndex: new FakeCollectionIndex(
        [
            CreateEntry(leftPath, leftRoot, "SNES", "mario", "hash-1", "fp-1"),
            CreateEntry(rightPath, rightRoot, "SNES", "mario", "hash-1", "fp-1")
        ]));
        using var client = CreateAuthClient(factory);

        using var content = CreateJsonContent(new CollectionCompareRequest
        {
            Left = CreateScope("left", "Left", leftRoot),
            Right = CreateScope("right", "Right", rightRoot),
            Limit = 50
        });

        var response = await client.PostAsync("/collections/compare", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("summary").GetProperty("totalEntries").GetInt32());
        Assert.Equal("identical-primary-hash", root.GetProperty("entries")[0].GetProperty("reasonCode").GetString());
    }

    [Fact]
    public async Task CollectionsMerge_ReturnsPreviewPlan_WithPagedEntries()
    {
        var leftRoot = CreateRoot("left");
        var rightRoot = CreateRoot("right");
        var targetRoot = CreateRoot("target");
        var alphaPath = CreateFile(leftRoot, "SNES", "Alpha.sfc", "alpha");
        var betaPath = CreateFile(leftRoot, "SNES", "Beta.sfc", "beta");

        using var factory = CreateFactory(collectionIndex: new FakeCollectionIndex(
        [
            CreateEntry(alphaPath, leftRoot, "SNES", "alpha", "hash-alpha", "fp-1"),
            CreateEntry(betaPath, leftRoot, "SNES", "beta", "hash-beta", "fp-1")
        ]));
        using var client = CreateAuthClient(factory);

        using var content = CreateJsonContent(new CollectionMergeRequest
        {
            CompareRequest = new CollectionCompareRequest
            {
                Left = CreateScope("left", "Left", leftRoot),
                Right = CreateScope("right", "Right", rightRoot),
                Offset = 1,
                Limit = 1
            },
            TargetRoot = targetRoot
        });

        var response = await client.PostAsync("/collections/merge", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal(2, root.GetProperty("summary").GetProperty("totalEntries").GetInt32());
        Assert.Equal(1, root.GetProperty("entries").GetArrayLength());
        Assert.Equal("game|SNES|beta", root.GetProperty("entries")[0].GetProperty("diffKey").GetString());
    }

    [Fact]
    public async Task CollectionsMergeApply_ThenRollback_RestoresFilesystemState()
    {
        var leftRoot = CreateRoot("left");
        var rightRoot = CreateRoot("right");
        var targetRoot = CreateRoot("target");
        var leftPath = CreateFile(leftRoot, "SNES", "Mario.sfc", "left");
        var auditPath = Path.Combine(_tempDir, "collection-merge.csv");

        using var factory = CreateFactory(collectionIndex: new FakeCollectionIndex(
        [
            CreateEntry(leftPath, leftRoot, "SNES", "mario", "hash-left", "fp-1")
        ]));
        using var client = CreateAuthClient(factory);

        using var applyContent = CreateJsonContent(new CollectionMergeApplyRequest
        {
            MergeRequest = new CollectionMergeRequest
            {
                CompareRequest = new CollectionCompareRequest
                {
                    Left = CreateScope("left", "Left", leftRoot),
                    Right = CreateScope("right", "Right", rightRoot),
                    Limit = 50
                },
                TargetRoot = targetRoot,
                AllowMoves = false
            },
            AuditPath = auditPath
        });

        var applyResponse = await client.PostAsync("/collections/merge/apply", applyContent);

        Assert.Equal(HttpStatusCode.OK, applyResponse.StatusCode);
        Assert.True(File.Exists(leftPath));
        Assert.True(File.Exists(Path.Combine(targetRoot, "SNES", "Mario.sfc")));
        Assert.True(File.Exists(auditPath));

        using var rollbackContent = CreateJsonContent(new CollectionMergeRollbackRequest
        {
            AuditPath = auditPath,
            DryRun = false
        });

        var rollbackResponse = await client.PostAsync("/collections/merge/rollback", rollbackContent);

        Assert.Equal(HttpStatusCode.OK, rollbackResponse.StatusCode);
        using var doc = JsonDocument.Parse(await rollbackResponse.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("rolledBack").GetInt32());
        Assert.True(File.Exists(leftPath));
        Assert.False(File.Exists(Path.Combine(targetRoot, "SNES", "Mario.sfc")));
    }

    [Fact]
    public async Task CollectionsMerge_TargetOutsideAllowedRoots_IsRejected()
    {
        var allowedRoot = CreateRoot("allowed");
        var blockedTarget = CreateRoot("blocked-target");
        var leftRoot = Path.Combine(allowedRoot, "left");
        var rightRoot = Path.Combine(allowedRoot, "right");
        Directory.CreateDirectory(leftRoot);
        Directory.CreateDirectory(rightRoot);

        using var factory = CreateFactory(
            new Dictionary<string, string?>
            {
                ["AllowRemoteClients"] = "true",
                ["PublicBaseUrl"] = "https://romulus.example",
                ["AllowedRoots:0"] = allowedRoot
            },
            collectionIndex: new FakeCollectionIndex());
        using var client = CreateAuthClient(factory);

        using var content = CreateJsonContent(new CollectionMergeRequest
        {
            CompareRequest = new CollectionCompareRequest
            {
                Left = CreateScope("left", "Left", leftRoot),
                Right = CreateScope("right", "Right", rightRoot),
                Limit = 50
            },
            TargetRoot = blockedTarget
        });

        var response = await client.PostAsync("/collections/merge", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(SecurityErrorCodes.OutsideAllowedRoots, body);
    }

    private WebApplicationFactory<Program> CreateFactory(
        IDictionary<string, string?>? settings = null,
        ICollectionIndex? collectionIndex = null)
    {
        var merged = new Dictionary<string, string?>
        {
            ["ApiKey"] = ApiKey
        };

        if (settings is not null)
        {
            foreach (var pair in settings)
                merged[pair.Key] = pair.Value;
        }

        return ApiTestFactory.Create(merged, collectionIndex: collectionIndex);
    }

    private static HttpClient CreateAuthClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static StringContent CreateJsonContent<T>(T value)
        => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private string CreateRoot(string name)
    {
        var root = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(root);
        return root;
    }

    private static string CreateFile(string root, string relativeDirectory, string fileName, string content)
    {
        var directory = Path.Combine(root, relativeDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private static CollectionSourceScope CreateScope(string sourceId, string label, string root)
        => new()
        {
            SourceId = sourceId,
            Label = label,
            Roots = [root],
            Extensions = [".sfc"]
        };

    private static CollectionIndexEntry CreateEntry(
        string path,
        string root,
        string consoleKey,
        string gameKey,
        string hash,
        string enrichmentFingerprint,
        int regionScore = 0)
    {
        var info = new FileInfo(path);
        return new CollectionIndexEntry
        {
            Path = path,
            Root = root,
            FileName = Path.GetFileName(path),
            Extension = Path.GetExtension(path),
            SizeBytes = info.Length,
            LastWriteUtc = info.LastWriteTimeUtc,
            LastScannedUtc = info.LastWriteTimeUtc,
            EnrichmentFingerprint = enrichmentFingerprint,
            PrimaryHashType = "SHA1",
            PrimaryHash = hash,
            ConsoleKey = consoleKey,
            GameKey = gameKey,
            Region = "EU",
            RegionScore = regionScore,
            FormatScore = 100,
            VersionScore = 1,
            HeaderScore = 10,
            CompletenessScore = 1,
            SizeTieBreakScore = info.Length,
            Category = FileCategory.Game,
            SortDecision = SortDecision.Sort
        };
    }

    private sealed class FakeCollectionIndex : ICollectionIndex
    {
        private readonly List<CollectionIndexEntry> _entries;

        public FakeCollectionIndex(IReadOnlyList<CollectionIndexEntry>? entries = null)
        {
            _entries = entries?.ToList() ?? [];
        }

        public ValueTask<CollectionIndexMetadata> GetMetadataAsync(CancellationToken ct = default)
            => ValueTask.FromResult(new CollectionIndexMetadata());

        public ValueTask<int> CountEntriesAsync(CancellationToken ct = default)
            => ValueTask.FromResult(_entries.Count);

        public ValueTask<CollectionIndexEntry?> TryGetByPathAsync(string path, CancellationToken ct = default)
            => ValueTask.FromResult(_entries.FirstOrDefault(entry => string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase)));

        public ValueTask<IReadOnlyList<CollectionIndexEntry>> GetByPathsAsync(IReadOnlyList<string> paths, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<CollectionIndexEntry>>(
                _entries.Where(entry => paths.Contains(entry.Path, StringComparer.OrdinalIgnoreCase)).ToArray());

        public ValueTask<IReadOnlyList<CollectionIndexEntry>> ListByConsoleAsync(string consoleKey, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<CollectionIndexEntry>>(
                _entries.Where(entry => string.Equals(entry.ConsoleKey, consoleKey, StringComparison.OrdinalIgnoreCase)).ToArray());

        public ValueTask<IReadOnlyList<CollectionIndexEntry>> ListEntriesInScopeAsync(
            IReadOnlyList<string> roots,
            IReadOnlyCollection<string> extensions,
            CancellationToken ct = default)
        {
            var normalizedRoots = roots
                .Where(static root => !string.IsNullOrWhiteSpace(root))
                .Select(static root => Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var normalizedExtensions = extensions
                .Where(static extension => !string.IsNullOrWhiteSpace(extension))
                .Select(static extension => extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var scoped = _entries
                .Where(entry =>
                {
                    var normalizedPath = Path.GetFullPath(entry.Path);
                    return normalizedRoots.Any(root => normalizedPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                           && normalizedExtensions.Contains(entry.Extension.ToLowerInvariant());
                })
                .OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Path, StringComparer.Ordinal)
                .ToArray();

            return ValueTask.FromResult<IReadOnlyList<CollectionIndexEntry>>(scoped);
        }

        public ValueTask UpsertEntriesAsync(IReadOnlyList<CollectionIndexEntry> entries, CancellationToken ct = default)
        {
            foreach (var entry in entries)
            {
                _entries.RemoveAll(existing => string.Equals(existing.Path, entry.Path, StringComparison.OrdinalIgnoreCase));
                _entries.Add(entry);
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask RemovePathsAsync(IReadOnlyList<string> paths, CancellationToken ct = default)
        {
            foreach (var path in paths)
                _entries.RemoveAll(existing => string.Equals(existing.Path, path, StringComparison.OrdinalIgnoreCase));
            return ValueTask.CompletedTask;
        }

        public ValueTask<CollectionHashCacheEntry?> TryGetHashAsync(string path, string algorithm, long sizeBytes, DateTime lastWriteUtc, CancellationToken ct = default)
            => ValueTask.FromResult<CollectionHashCacheEntry?>(null);

        public ValueTask SetHashAsync(CollectionHashCacheEntry entry, CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask AppendRunSnapshotAsync(CollectionRunSnapshot snapshot, CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask<int> CountRunSnapshotsAsync(CancellationToken ct = default)
            => ValueTask.FromResult(0);

        public ValueTask<IReadOnlyList<CollectionRunSnapshot>> ListRunSnapshotsAsync(int limit = 50, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<CollectionRunSnapshot>>(Array.Empty<CollectionRunSnapshot>());
    }
}
