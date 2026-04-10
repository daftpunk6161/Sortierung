using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Analysis;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.FileSystem;
using Romulus.Tests.TestFixtures;
using Xunit;

namespace Romulus.Tests;

public sealed class CollectionMergeServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSystemAdapter _fileSystem = new();

    public CollectionMergeServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CollectionMerge_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task BuildPlanAsync_LeftOnlyToThirdRoot_ReturnsCopyToTarget()
    {
        var leftRoot = CreateRoot("left");
        var rightRoot = CreateRoot("right");
        var targetRoot = CreateRoot("target");
        var leftPath = CreateFile(leftRoot, "SNES", "Mario.sfc", "left");

        var build = await CollectionMergeService.BuildPlanAsync(
            new MutableCollectionIndex(
            [
                CreateEntry(leftPath, leftRoot, "SNES", "mario", "hash-left", "fp-1")
            ]),
            _fileSystem,
            CreateMergeRequest(leftRoot, rightRoot, targetRoot, allowMoves: false));

        Assert.True(build.CanUse);
        var entry = Assert.Single(build.Plan!.Entries);
        Assert.Equal(CollectionMergeDecision.CopyToTarget, entry.Decision);
        Assert.Equal(Path.Combine(targetRoot, "SNES", "Mario.sfc"), entry.TargetPath);
        Assert.Equal(1, build.Plan.Summary.CopyToTarget);
    }

    [Fact]
    public async Task BuildPlanAsync_TargetContainsIdenticalElsewhere_SkipsAsDuplicate()
    {
        var leftRoot = CreateRoot("left");
        var rightRoot = CreateRoot("right");
        var targetRoot = CreateRoot("target");
        var leftPath = CreateFile(leftRoot, "SNES", "Mario.sfc", "left");
        var targetPath = CreateFile(targetRoot, "Dupes", "Mario Copy.sfc", "target");

        var build = await CollectionMergeService.BuildPlanAsync(
            new MutableCollectionIndex(
            [
                CreateEntry(leftPath, leftRoot, "SNES", "mario", "hash-identical", "fp-1"),
                CreateEntry(targetPath, targetRoot, "SNES", "mario-copy", "hash-identical", "fp-1")
            ]),
            _fileSystem,
            CreateMergeRequest(leftRoot, rightRoot, targetRoot, allowMoves: false));

        Assert.True(build.CanUse);
        var entry = Assert.Single(build.Plan!.Entries);
        Assert.Equal(CollectionMergeDecision.SkipAsDuplicate, entry.Decision);
        Assert.Equal("merge-target-has-duplicate", entry.ReasonCode);
        Assert.NotNull(entry.ExistingTarget);
    }

    [Fact]
    public async Task BuildPlanAsync_PreferredEntryAlreadyInTarget_KeepsExistingTarget()
    {
        var leftRoot = CreateRoot("left");
        var rightRoot = CreateRoot("right");
        var leftPath = CreateFile(leftRoot, "", "Game.sfc", "left");
        var rightPath = CreateFile(rightRoot, "", "Game.sfc", "right");

        var build = await CollectionMergeService.BuildPlanAsync(
            new MutableCollectionIndex(
            [
                CreateEntry(leftPath, leftRoot, "SNES", "game", "hash-left", "fp-1", regionScore: 100),
                CreateEntry(rightPath, rightRoot, "SNES", "game", "hash-right", "fp-1", regionScore: 200)
            ]),
            _fileSystem,
            CreateMergeRequest(leftRoot, rightRoot, rightRoot, allowMoves: false));

        Assert.True(build.CanUse);
        var entry = Assert.Single(build.Plan!.Entries);
        Assert.Equal(CollectionMergeDecision.KeepExistingTarget, entry.Decision);
        Assert.Equal("merge-source-already-in-target", entry.ReasonCode);
    }

    [Fact]
    public async Task BuildPlanAsync_LeftOnlyIntoIndexedTargetConflict_RequiresReview()
    {
        var leftRoot = CreateRoot("left");
        var rightRoot = CreateRoot("right");
        var targetRoot = CreateRoot("target");
        var leftPath = CreateFile(leftRoot, "SNES", "Game.sfc", "left");
        var targetPath = CreateFile(targetRoot, "SNES", "Game.sfc", "target");

        var build = await CollectionMergeService.BuildPlanAsync(
            new MutableCollectionIndex(
            [
                CreateEntry(leftPath, leftRoot, "SNES", "game", "hash-left", "fp-1"),
                CreateEntry(targetPath, targetRoot, "SNES", "game", "hash-target", "fp-1")
            ]),
            _fileSystem,
            CreateMergeRequest(leftRoot, rightRoot, targetRoot, allowMoves: false));

        Assert.True(build.CanUse);
        var entry = Assert.Single(build.Plan!.Entries);
        Assert.Equal(CollectionMergeDecision.ReviewRequired, entry.Decision);
        Assert.Equal("merge-target-conflict-existing", entry.ReasonCode);
    }

    [Fact]
    public async Task BuildPlanAsync_AppliesOffsetAndLimit_WithoutChangingFullSummary()
    {
        var leftRoot = CreateRoot("left");
        var rightRoot = CreateRoot("right");
        var targetRoot = CreateRoot("target");
        var alphaPath = CreateFile(leftRoot, "", "Alpha.sfc", "alpha");
        var betaPath = CreateFile(leftRoot, "", "Beta.sfc", "beta");

        var build = await CollectionMergeService.BuildPlanAsync(
            new MutableCollectionIndex(
            [
                CreateEntry(alphaPath, leftRoot, "SNES", "alpha", "hash-alpha", "fp-1"),
                CreateEntry(betaPath, leftRoot, "SNES", "beta", "hash-beta", "fp-1")
            ]),
            _fileSystem,
            CreateMergeRequest(leftRoot, rightRoot, targetRoot, allowMoves: false) with
            {
                CompareRequest = CreateMergeRequest(leftRoot, rightRoot, targetRoot, allowMoves: false).CompareRequest with
                {
                    Offset = 1,
                    Limit = 1
                }
            });

        Assert.True(build.CanUse);
        Assert.Equal(2, build.Plan!.Summary.TotalEntries);
        var entry = Assert.Single(build.Plan.Entries);
        Assert.Equal("game|SNES|beta", entry.DiffKey);
        Assert.Equal(CollectionMergeDecision.CopyToTarget, entry.Decision);
    }

    [Fact]
    public async Task ApplyAsync_CopyToTarget_WritesAuditAndKeepsSource()
    {
        var leftRoot = CreateRoot("left");
        var rightRoot = CreateRoot("right");
        var targetRoot = CreateRoot("target");
        var leftPath = CreateFile(leftRoot, "SNES", "Mario.sfc", "left");
        var index = new MutableCollectionIndex(
        [
            CreateEntry(leftPath, leftRoot, "SNES", "mario", "hash-left", "fp-1")
        ]);
        var auditStore = new TrackingAuditStore();
        var auditPath = Path.Combine(_tempDir, "copy-audit.csv");

        var result = await CollectionMergeService.ApplyAsync(
            index,
            _fileSystem,
            auditStore,
            new CollectionMergeApplyRequest
            {
                MergeRequest = CreateMergeRequest(leftRoot, rightRoot, targetRoot, allowMoves: false),
                AuditPath = auditPath
            });

        var targetPath = Path.Combine(targetRoot, "SNES", "Mario.sfc");
        Assert.Null(result.BlockedReason);
        Assert.Equal(1, result.Summary.Applied);
        Assert.Equal(1, result.Summary.Copied);
        Assert.True(File.Exists(leftPath));
        Assert.True(File.Exists(targetPath));
        Assert.Equal(2, auditStore.Rows.Count);
        Assert.Contains(auditStore.Rows, row => row.Action == "COPY_PENDING");
        Assert.Contains(auditStore.Rows, row => row.Action == RunConstants.AuditActions.Copy);
        Assert.True(auditStore.Sidecars.ContainsKey(auditPath));
        Assert.NotNull(index.FindByPath(targetPath));
        Assert.NotNull(index.FindByPath(leftPath));
    }

    [Fact]
    public async Task ApplyAsync_MoveToTarget_RemovesSourceAndUpdatesIndex()
    {
        var leftRoot = CreateRoot("left");
        var rightRoot = CreateRoot("right");
        var targetRoot = CreateRoot("target");
        var leftPath = CreateFile(leftRoot, "SNES", "Mario.sfc", "left");
        var index = new MutableCollectionIndex(
        [
            CreateEntry(leftPath, leftRoot, "SNES", "mario", "hash-left", "fp-1")
        ]);
        var auditStore = new TrackingAuditStore();

        var result = await CollectionMergeService.ApplyAsync(
            index,
            _fileSystem,
            auditStore,
            new CollectionMergeApplyRequest
            {
                MergeRequest = CreateMergeRequest(leftRoot, rightRoot, targetRoot, allowMoves: true),
                AuditPath = Path.Combine(_tempDir, "move-audit.csv")
            });

        var targetPath = Path.Combine(targetRoot, "SNES", "Mario.sfc");
        Assert.Null(result.BlockedReason);
        Assert.Equal(1, result.Summary.Applied);
        Assert.Equal(1, result.Summary.Moved);
        Assert.False(File.Exists(leftPath));
        Assert.True(File.Exists(targetPath));
        Assert.Null(index.FindByPath(leftPath));
        Assert.NotNull(index.FindByPath(targetPath));
        Assert.Contains(auditStore.Rows, row => row.Action == RunConstants.AuditActions.Move);
    }

    [Fact]
    public async Task ApplyAsync_IndexMutationFailure_RevertsCopiedOutput()
    {
        var leftRoot = CreateRoot("left");
        var rightRoot = CreateRoot("right");
        var targetRoot = CreateRoot("target");
        var leftPath = CreateFile(leftRoot, "SNES", "Mario.sfc", "left");
        var index = new MutableCollectionIndex(
            [CreateEntry(leftPath, leftRoot, "SNES", "mario", "hash-left", "fp-1")],
            throwOnUpsert: true);

        var result = await CollectionMergeService.ApplyAsync(
            index,
            _fileSystem,
            new TrackingAuditStore(),
            new CollectionMergeApplyRequest
            {
                MergeRequest = CreateMergeRequest(leftRoot, rightRoot, targetRoot, allowMoves: false),
                AuditPath = Path.Combine(_tempDir, "failed-audit.csv")
            });

        var targetPath = Path.Combine(targetRoot, "SNES", "Mario.sfc");
        Assert.Equal(1, result.Summary.Failed);
        Assert.True(File.Exists(leftPath));
        Assert.False(File.Exists(targetPath));
    }

    [Fact]
    public async Task ApplyAsync_MoveIndexRemoveFailure_RevertsFilesystemAndIndex()
    {
        var leftRoot = CreateRoot("left");
        var rightRoot = CreateRoot("right");
        var targetRoot = CreateRoot("target");
        var leftPath = CreateFile(leftRoot, "SNES", "Mario.sfc", "left");
        var index = new MutableCollectionIndex(
            [CreateEntry(leftPath, leftRoot, "SNES", "mario", "hash-left", "fp-1")],
            throwOnRemove: true);

        var result = await CollectionMergeService.ApplyAsync(
            index,
            _fileSystem,
            new TrackingAuditStore(),
            new CollectionMergeApplyRequest
            {
                MergeRequest = CreateMergeRequest(leftRoot, rightRoot, targetRoot, allowMoves: true),
                AuditPath = Path.Combine(_tempDir, "failed-move-audit.csv")
            });

        var targetPath = Path.Combine(targetRoot, "SNES", "Mario.sfc");
        Assert.Equal(1, result.Summary.Failed);
        Assert.True(File.Exists(leftPath));
        Assert.False(File.Exists(targetPath));
        Assert.NotNull(index.FindByPath(leftPath));
        Assert.Null(index.FindByPath(targetPath));
    }

    [Fact]
    public async Task ApplyAsync_FinalAuditWriteFailure_RevertsFilesystemAndIndex()
    {
        var leftRoot = CreateRoot("left");
        var rightRoot = CreateRoot("right");
        var targetRoot = CreateRoot("target");
        var leftPath = CreateFile(leftRoot, "SNES", "Mario.sfc", "left");
        var index = new MutableCollectionIndex(
            [CreateEntry(leftPath, leftRoot, "SNES", "mario", "hash-left", "fp-1")]);

        var result = await CollectionMergeService.ApplyAsync(
            index,
            _fileSystem,
            new ThrowOnFinalAuditStore(),
            new CollectionMergeApplyRequest
            {
                MergeRequest = CreateMergeRequest(leftRoot, rightRoot, targetRoot, allowMoves: true),
                AuditPath = Path.Combine(_tempDir, "audit-append-failure.csv")
            });

        var targetPath = Path.Combine(targetRoot, "SNES", "Mario.sfc");
        Assert.Equal(1, result.Summary.Failed);
        Assert.True(File.Exists(leftPath));
        Assert.False(File.Exists(targetPath));
        Assert.NotNull(index.FindByPath(leftPath));
        Assert.Null(index.FindByPath(targetPath));
    }

    [Fact]
    public void AuditSigningService_Rollback_CopyAction_RemovesCopiedTarget()
    {
        var leftRoot = CreateRoot("left");
        var targetRoot = CreateRoot("target");
        var sourcePath = CreateFile(leftRoot, "SNES", "Mario.sfc", "left");
        var targetPath = Path.Combine(targetRoot, "SNES", "Mario.sfc");
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.Copy(sourcePath, targetPath);

        var auditPath = Path.Combine(_tempDir, "rollback-copy.csv");
        var keyFilePath = AuditSecurityPaths.GetDefaultSigningKeyPath();
        var auditStore = new AuditCsvStore(_fileSystem, keyFilePath: keyFilePath);
        auditStore.AppendAuditRow(auditPath, targetRoot, sourcePath, targetPath, RunConstants.AuditActions.Copy, "GAME", "hash-left", "merge-copy-to-target");
        auditStore.Flush(auditPath);
        auditStore.WriteMetadataSidecar(auditPath, new Dictionary<string, object>
        {
            ["Mode"] = "CollectionMerge",
            ["AllowedRestoreRoots"] = new[] { leftRoot, targetRoot },
            ["AllowedCurrentRoots"] = new[] { leftRoot, targetRoot }
        });

        var signing = new AuditSigningService(_fileSystem, keyFilePath: keyFilePath);
        var rollback = signing.Rollback(auditPath, [leftRoot, targetRoot], [leftRoot, targetRoot], dryRun: false);

        Assert.Equal(1, rollback.RolledBack);
        Assert.True(File.Exists(sourcePath));
        Assert.False(File.Exists(targetPath));
    }

    private CollectionMergeRequest CreateMergeRequest(string leftRoot, string rightRoot, string targetRoot, bool allowMoves)
        => new()
        {
            CompareRequest = new CollectionCompareRequest
            {
                Left = new CollectionSourceScope
                {
                    SourceId = "left",
                    Label = "Left",
                    Roots = [leftRoot],
                    Extensions = [".sfc"]
                },
                Right = new CollectionSourceScope
                {
                    SourceId = "right",
                    Label = "Right",
                    Roots = [rightRoot],
                    Extensions = [".sfc"]
                }
            },
            TargetRoot = targetRoot,
            AllowMoves = allowMoves
        };

    private string CreateRoot(string name)
    {
        var root = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(root);
        return root;
    }

    private static string CreateFile(string root, string relativeDirectory, string fileName, string content)
    {
        var directory = string.IsNullOrWhiteSpace(relativeDirectory)
            ? root
            : Path.Combine(root, relativeDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, content);
        return path;
    }

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

    private sealed class MutableCollectionIndex : ICollectionIndex
    {
        private readonly List<CollectionIndexEntry> _entries;
        private readonly bool _throwOnUpsert;
        private readonly bool _throwOnRemove;
        private bool _removeFaultTriggered;

        public MutableCollectionIndex(
            IReadOnlyList<CollectionIndexEntry>? entries = null,
            bool throwOnUpsert = false,
            bool throwOnRemove = false)
        {
            _entries = entries?.ToList() ?? [];
            _throwOnUpsert = throwOnUpsert;
            _throwOnRemove = throwOnRemove;
        }

        public CollectionIndexEntry? FindByPath(string path)
            => _entries.FirstOrDefault(entry => string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase));

        public ValueTask<CollectionIndexMetadata> GetMetadataAsync(CancellationToken ct = default)
            => ValueTask.FromResult(new CollectionIndexMetadata());

        public ValueTask<int> CountEntriesAsync(CancellationToken ct = default)
            => ValueTask.FromResult(_entries.Count);

        public ValueTask<CollectionIndexEntry?> TryGetByPathAsync(string path, CancellationToken ct = default)
            => ValueTask.FromResult(FindByPath(path));

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
            if (_throwOnUpsert)
                throw new IOException("Simulated index persistence failure.");

            foreach (var entry in entries)
            {
                _entries.RemoveAll(existing => string.Equals(existing.Path, entry.Path, StringComparison.OrdinalIgnoreCase));
                _entries.Add(entry);
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask RemovePathsAsync(IReadOnlyList<string> paths, CancellationToken ct = default)
        {
            if (_throwOnRemove && !_removeFaultTriggered)
            {
                _removeFaultTriggered = true;
                throw new IOException("Simulated index remove failure.");
            }

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

    private sealed class ThrowOnFinalAuditStore : IAuditStore
    {
        private int _appendCount;

        public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata)
        {
        }

        public bool TestMetadataSidecar(string auditCsvPath) => true;

        public void Flush(string auditCsvPath)
        {
        }

        public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots, string[] allowedCurrentRoots, bool dryRun = false)
            => Array.Empty<string>();

        public void AppendAuditRow(string auditCsvPath, string rootPath, string oldPath, string newPath, string action, string category = "", string hash = "", string reason = "")
        {
            _appendCount++;
            if (_appendCount >= 2)
                throw new IOException("Simulated final audit append failure.");
        }
    }
}
