using LiteDB;
using System.Text;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.Index;

/// <summary>
/// LiteDB-backed implementation of <see cref="ICollectionIndex"/>.
/// The adapter persists already-computed state and must not recompute business decisions.
/// </summary>
public sealed class LiteDbCollectionIndex : ICollectionIndex, IDisposable
{
    private const int CurrentSchemaVersion = 2;
    private static readonly byte[] LiteDbSignature = "** This is a LiteDB file **"u8.ToArray();
    private const int MutationCompactionThreshold = 5000;
    private const string MetadataCollectionName = "metadata";
    private const string EntriesCollectionName = "entries";
    private const string HashesCollectionName = "hashes";
    private const string SnapshotsCollectionName = "snapshots";
    private const string MetadataId = "collection-index";

    private readonly string _databasePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Action<string>? _onWarning;
    private LiteDatabase _database;
    private int _pendingMutationCount;
    private bool _disposed;

    public LiteDbCollectionIndex(string databasePath, Action<string>? onWarning = null)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Database path must not be empty.", nameof(databasePath));

        _databasePath = Path.GetFullPath(databasePath);
        _onWarning = onWarning;
        _database = OpenOrRecoverDatabase();
        EnsureSchema();
    }

    public async ValueTask<CollectionIndexMetadata> GetMetadataAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            ct.ThrowIfCancellationRequested();
            return ReadMetadata();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<int> CountEntriesAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            ct.ThrowIfCancellationRequested();
            return _database.GetCollection<CollectionIndexEntryDocument>(EntriesCollectionName).Count();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<CollectionIndexEntry?> TryGetByPathAsync(string path, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            ct.ThrowIfCancellationRequested();

            var normalizedPath = NormalizePath(path);
            var document = _database.GetCollection<CollectionIndexEntryDocument>(EntriesCollectionName)
                .FindById(normalizedPath);
            return document is null ? null : ToContract(document);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<IReadOnlyList<CollectionIndexEntry>> GetByPathsAsync(
        IReadOnlyList<string> paths,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            ct.ThrowIfCancellationRequested();

            var collection = _database.GetCollection<CollectionIndexEntryDocument>(EntriesCollectionName);
            var entries = new List<CollectionIndexEntry>(paths.Count);
            foreach (var path in paths)
            {
                ct.ThrowIfCancellationRequested();
                var normalizedPath = NormalizePath(path);
                var document = collection.FindById(normalizedPath);
                if (document is not null)
                    entries.Add(ToContract(document));
            }

            return entries;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<IReadOnlyList<CollectionIndexEntry>> ListByConsoleAsync(
        string consoleKey,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(consoleKey))
            return Array.Empty<CollectionIndexEntry>();

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            ct.ThrowIfCancellationRequested();

            return _database.GetCollection<CollectionIndexEntryDocument>(EntriesCollectionName)
                .Find(document => document.ConsoleKey == consoleKey.Trim())
                .OrderBy(document => document.Path, StringComparer.OrdinalIgnoreCase)
                .Select(ToContract)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<IReadOnlyList<CollectionIndexEntry>> ListEntriesInScopeAsync(
        IReadOnlyList<string> roots,
        IReadOnlyCollection<string> extensions,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(extensions);

        if (roots.Count == 0 || extensions.Count == 0)
            return Array.Empty<CollectionIndexEntry>();

        var normalizedRoots = roots
            .Where(static root => !string.IsNullOrWhiteSpace(root))
            .Select(NormalizeRootPrefix)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var normalizedExtensions = extensions
            .Where(static extension => !string.IsNullOrWhiteSpace(extension))
            .Select(static extension => extension.Trim().StartsWith('.')
                ? extension.Trim().ToLowerInvariant()
                : "." + extension.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (normalizedRoots.Length == 0 || normalizedExtensions.Count == 0)
            return Array.Empty<CollectionIndexEntry>();

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            ct.ThrowIfCancellationRequested();

            return _database.GetCollection<CollectionIndexEntryDocument>(EntriesCollectionName)
                .FindAll()
                .Where(document => IsDocumentInScope(document, normalizedRoots, normalizedExtensions))
                .OrderBy(document => document.Path, StringComparer.OrdinalIgnoreCase)
                .Select(ToContract)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask UpsertEntriesAsync(
        IReadOnlyList<CollectionIndexEntry> entries,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            ct.ThrowIfCancellationRequested();

            var collection = _database.GetCollection<CollectionIndexEntryDocument>(EntriesCollectionName);
            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                collection.Upsert(ToDocument(entry));
            }

            TouchMetadata();
            RegisterMutationAndMaybeCompact(entries.Count);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask RemovePathsAsync(
        IReadOnlyList<string> paths,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            ct.ThrowIfCancellationRequested();

            var collection = _database.GetCollection<CollectionIndexEntryDocument>(EntriesCollectionName);
            foreach (var path in paths)
            {
                ct.ThrowIfCancellationRequested();
                collection.Delete(NormalizePath(path));
            }

            TouchMetadata();
            RegisterMutationAndMaybeCompact(paths.Count);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<CollectionHashCacheEntry?> TryGetHashAsync(
        string path,
        string algorithm,
        long sizeBytes,
        DateTime lastWriteUtc,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            ct.ThrowIfCancellationRequested();

            var key = BuildHashKey(path, algorithm, sizeBytes, lastWriteUtc);
            var document = _database.GetCollection<CollectionHashCacheDocument>(HashesCollectionName)
                .FindById(key);
            return document is null ? null : ToContract(document);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask SetHashAsync(CollectionHashCacheEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            ct.ThrowIfCancellationRequested();

            _database.GetCollection<CollectionHashCacheDocument>(HashesCollectionName)
                .Upsert(ToDocument(entry));
            TouchMetadata();
            RegisterMutationAndMaybeCompact(1);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask AppendRunSnapshotAsync(CollectionRunSnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            ct.ThrowIfCancellationRequested();

            _database.GetCollection<CollectionRunSnapshotDocument>(SnapshotsCollectionName)
                .Upsert(ToDocument(snapshot));
            TouchMetadata();
            RegisterMutationAndMaybeCompact(1);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<int> CountRunSnapshotsAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            ct.ThrowIfCancellationRequested();
            return _database.GetCollection<CollectionRunSnapshotDocument>(SnapshotsCollectionName).Count();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<IReadOnlyList<CollectionRunSnapshot>> ListRunSnapshotsAsync(
        int limit = 50,
        CancellationToken ct = default)
    {
        var boundedLimit = Math.Max(1, limit);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            ct.ThrowIfCancellationRequested();

            return _database.GetCollection<CollectionRunSnapshotDocument>(SnapshotsCollectionName)
                .FindAll()
                .OrderByDescending(document => document.CompletedUtcTicks)
                .ThenBy(document => document.Id, StringComparer.Ordinal)
                .Take(boundedLimit)
                .Select(ToContract)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    private void RegisterMutationAndMaybeCompact(int mutationCount)
    {
        if (mutationCount <= 0)
            return;

        _pendingMutationCount += mutationCount;
        if (_pendingMutationCount < MutationCompactionThreshold)
            return;

        try
        {
            _database.Rebuild();
        }
        catch (Exception ex)
        {
            _onWarning?.Invoke($"[CollectionIndex] Periodic compaction skipped: {ex.Message}");
        }
        finally
        {
            _pendingMutationCount = 0;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _database.Dispose();
        _gate.Dispose();
    }

    private LiteDatabase OpenOrRecoverDatabase()
    {
        if (File.Exists(_databasePath) && !IsRecognizableLiteDbFile(_databasePath))
        {
            _onWarning?.Invoke("[CollectionIndex] Recovering database after signature validation failure.");
            RecoverDatabaseFile("open-failure");
        }

        return OpenDatabase();
    }

    private LiteDatabase OpenDatabase()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        return new LiteDatabase(new ConnectionString
        {
            Filename = _databasePath,
            Connection = ConnectionType.Shared
        });
    }

    private void EnsureSchema()
    {
        MetadataDocument? metadata;
        ILiteCollection<MetadataDocument> metadataCollection;
        try
        {
            metadataCollection = _database.GetCollection<MetadataDocument>(MetadataCollectionName);
            metadata = metadataCollection.FindById(MetadataId);
        }
        catch (Exception ex) when (ex is LiteException or IOException or InvalidOperationException)
        {
            _onWarning?.Invoke($"[CollectionIndex] Recovering database after metadata read failure: {ex.Message}");
            RecreateDatabase("schema-read-failure");
            return;
        }

        if (metadata is null)
        {
            var now = DateTime.UtcNow;
            metadataCollection.Upsert(new MetadataDocument
            {
                Id = MetadataId,
                SchemaVersion = CurrentSchemaVersion,
                CreatedUtcTicks = now.Ticks,
                UpdatedUtcTicks = now.Ticks
            });
            EnsureIndexes();
            return;
        }

        if (metadata.SchemaVersion != CurrentSchemaVersion)
        {
            _onWarning?.Invoke($"[CollectionIndex] Recovering database after schema mismatch: {metadata.SchemaVersion}");
            RecreateDatabase("schema-mismatch");
            return;
        }

        EnsureIndexes();
    }

    private void RecreateDatabase(string reason)
    {
        _database.Dispose();
        RecoverDatabaseFile(reason);
        _database = OpenDatabase();

        var now = DateTime.UtcNow;
        _database.GetCollection<MetadataDocument>(MetadataCollectionName).Upsert(new MetadataDocument
        {
            Id = MetadataId,
            SchemaVersion = CurrentSchemaVersion,
            CreatedUtcTicks = now.Ticks,
            UpdatedUtcTicks = now.Ticks
        });

        EnsureIndexes();
    }

    private void EnsureIndexes()
    {
        var entries = _database.GetCollection<CollectionIndexEntryDocument>(EntriesCollectionName);
        entries.EnsureIndex(nameof(CollectionIndexEntryDocument.ConsoleKey));
        entries.EnsureIndex(nameof(CollectionIndexEntryDocument.GameKey));
        entries.EnsureIndex(nameof(CollectionIndexEntryDocument.PrimaryHash));

        var hashes = _database.GetCollection<CollectionHashCacheDocument>(HashesCollectionName);
        hashes.EnsureIndex(nameof(CollectionHashCacheDocument.Path));
        hashes.EnsureIndex(nameof(CollectionHashCacheDocument.Algorithm));

        var snapshots = _database.GetCollection<CollectionRunSnapshotDocument>(SnapshotsCollectionName);
        snapshots.EnsureIndex(nameof(CollectionRunSnapshotDocument.CompletedUtcTicks));
        snapshots.EnsureIndex(nameof(CollectionRunSnapshotDocument.RootFingerprint));
    }

    private CollectionIndexMetadata ReadMetadata()
    {
        var metadata = _database.GetCollection<MetadataDocument>(MetadataCollectionName).FindById(MetadataId)
            ?? throw new InvalidOperationException("Collection index metadata missing.");

        return new CollectionIndexMetadata
        {
            SchemaVersion = metadata.SchemaVersion,
            CreatedUtc = new DateTime(metadata.CreatedUtcTicks, DateTimeKind.Utc),
            UpdatedUtc = new DateTime(metadata.UpdatedUtcTicks, DateTimeKind.Utc)
        };
    }

    private void TouchMetadata()
    {
        var collection = _database.GetCollection<MetadataDocument>(MetadataCollectionName);
        var current = collection.FindById(MetadataId);
        var nowTicks = DateTime.UtcNow.Ticks;

        if (current is null)
        {
            collection.Upsert(new MetadataDocument
            {
                Id = MetadataId,
                SchemaVersion = CurrentSchemaVersion,
                CreatedUtcTicks = nowTicks,
                UpdatedUtcTicks = nowTicks
            });
            return;
        }

        current.UpdatedUtcTicks = nowTicks;
        collection.Update(current);
    }

    private void RecoverDatabaseFile(string reason)
    {
        var directory = Path.GetDirectoryName(_databasePath)!;
        Directory.CreateDirectory(directory);

        if (!File.Exists(_databasePath))
            return;

        var backupPath = Path.Combine(
            directory,
            $"{Path.GetFileName(_databasePath)}.{reason}.{DateTime.UtcNow:yyyyMMddHHmmssfff}.bak");

        File.Move(_databasePath, backupPath, overwrite: true);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path must not be empty.", nameof(path));

        return NormalizePathForKey(path);
    }

    internal static string NormalizePathForKey(string path)
        => Path.GetFullPath(path).Normalize(NormalizationForm.FormC);

    private static string NormalizeRootPrefix(string root)
    {
        var normalizedRoot = NormalizePath(root);
        return normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;
    }

    private static bool IsDocumentInScope(
        CollectionIndexEntryDocument document,
        IReadOnlyList<string> normalizedRoots,
        IReadOnlySet<string> normalizedExtensions)
    {
        if (!normalizedExtensions.Contains(document.Extension))
            return false;

        for (var i = 0; i < normalizedRoots.Count; i++)
        {
            if (document.Path.StartsWith(normalizedRoots[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsRecognizableLiteDbFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length == 0)
                return false;

            const int signatureOffset = 0x20;
            if (stream.Length < signatureOffset + LiteDbSignature.Length)
                return false;

            stream.Seek(signatureOffset, SeekOrigin.Begin);
            Span<byte> buffer = stackalloc byte[LiteDbSignature.Length];
            var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            return read == buffer.Length && buffer.SequenceEqual(LiteDbSignature);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    private static string NormalizeAlgorithm(string algorithm)
    {
        if (string.IsNullOrWhiteSpace(algorithm))
            throw new ArgumentException("Algorithm must not be empty.", nameof(algorithm));

        return algorithm.Trim().ToUpperInvariant() switch
        {
            "CRC" => "CRC32",
            var normalized => normalized
        };
    }

    private static string NormalizeHash(string? hash)
        => string.IsNullOrWhiteSpace(hash) ? string.Empty : hash.Trim().ToLowerInvariant();

    private static string BuildHashKey(string path, string algorithm, long sizeBytes, DateTime lastWriteUtc)
    {
        var normalizedPath = NormalizePath(path);
        var normalizedAlgorithm = NormalizeAlgorithm(algorithm);
        var utc = NormalizeUtc(lastWriteUtc);
        return $"{normalizedAlgorithm}|{normalizedPath}|{sizeBytes}|{utc.Ticks}";
    }

    private static CollectionIndexEntryDocument ToDocument(CollectionIndexEntry entry)
        => new()
        {
            Id = NormalizePath(entry.Path),
            Root = NormalizePath(entry.Root),
            FileName = entry.FileName ?? "",
            Extension = entry.Extension ?? "",
            SizeBytes = entry.SizeBytes,
            LastWriteUtcTicks = NormalizeUtc(entry.LastWriteUtc).Ticks,
            LastScannedUtcTicks = NormalizeUtc(entry.LastScannedUtc).Ticks,
            EnrichmentFingerprint = entry.EnrichmentFingerprint ?? "",
            PrimaryHashType = NormalizeAlgorithm(entry.PrimaryHashType),
            PrimaryHash = string.IsNullOrWhiteSpace(entry.PrimaryHash) ? null : NormalizeHash(entry.PrimaryHash),
            HeaderlessHash = string.IsNullOrWhiteSpace(entry.HeaderlessHash) ? null : NormalizeHash(entry.HeaderlessHash),
            ConsoleKey = entry.ConsoleKey ?? "UNKNOWN",
            GameKey = entry.GameKey ?? "",
            Region = entry.Region ?? "UNKNOWN",
            RegionScore = entry.RegionScore,
            FormatScore = entry.FormatScore,
            VersionScore = entry.VersionScore,
            HeaderScore = entry.HeaderScore,
            CompletenessScore = entry.CompletenessScore,
            SizeTieBreakScore = entry.SizeTieBreakScore,
            Category = entry.Category,
            DatMatch = entry.DatMatch,
            DatGameName = entry.DatGameName,
            DatAuditStatus = entry.DatAuditStatus,
            SortDecision = entry.SortDecision,
            DecisionClass = entry.DecisionClass,
            EvidenceTier = entry.EvidenceTier,
            PrimaryMatchKind = entry.PrimaryMatchKind,
            DetectionConfidence = entry.DetectionConfidence,
            DetectionConflict = entry.DetectionConflict,
            HasHardEvidence = entry.HasHardEvidence,
            IsSoftOnly = entry.IsSoftOnly,
            MatchLevel = entry.MatchEvidence.Level,
            MatchReasoning = entry.MatchEvidence.Reasoning ?? string.Empty,
            MatchSources = entry.MatchEvidence.Sources?.ToList() ?? [],
            MatchHasHardEvidence = entry.MatchEvidence.HasHardEvidence,
            MatchHasConflict = entry.MatchEvidence.HasConflict,
            MatchDatVerified = entry.MatchEvidence.DatVerified,
            MatchTier = entry.MatchEvidence.Tier,
            MatchPrimaryMatchKind = entry.MatchEvidence.PrimaryMatchKind,
            PlatformFamily = entry.PlatformFamily,
            ClassificationReasonCode = entry.ClassificationReasonCode ?? "game-default",
            ClassificationConfidence = entry.ClassificationConfidence
        };

    private static CollectionIndexEntry ToContract(CollectionIndexEntryDocument document)
        => new()
        {
            Path = document.Id,
            Root = document.Root,
            FileName = document.FileName,
            Extension = document.Extension,
            SizeBytes = document.SizeBytes,
            LastWriteUtc = new DateTime(document.LastWriteUtcTicks, DateTimeKind.Utc),
            LastScannedUtc = new DateTime(document.LastScannedUtcTicks, DateTimeKind.Utc),
            EnrichmentFingerprint = document.EnrichmentFingerprint,
            PrimaryHashType = document.PrimaryHashType,
            PrimaryHash = document.PrimaryHash,
            HeaderlessHash = document.HeaderlessHash,
            ConsoleKey = document.ConsoleKey,
            GameKey = document.GameKey,
            Region = document.Region,
            RegionScore = document.RegionScore,
            FormatScore = document.FormatScore,
            VersionScore = document.VersionScore,
            HeaderScore = document.HeaderScore,
            CompletenessScore = document.CompletenessScore,
            SizeTieBreakScore = document.SizeTieBreakScore,
            Category = document.Category,
            DatMatch = document.DatMatch,
            DatGameName = document.DatGameName,
            DatAuditStatus = document.DatAuditStatus,
            SortDecision = document.SortDecision,
            DecisionClass = document.DecisionClass,
            EvidenceTier = document.EvidenceTier,
            PrimaryMatchKind = document.PrimaryMatchKind,
            DetectionConfidence = document.DetectionConfidence,
            DetectionConflict = document.DetectionConflict,
            HasHardEvidence = document.HasHardEvidence,
            IsSoftOnly = document.IsSoftOnly,
            MatchEvidence = new MatchEvidence
            {
                Level = document.MatchLevel,
                Reasoning = document.MatchReasoning,
                Sources = document.MatchSources.ToArray(),
                HasHardEvidence = document.MatchHasHardEvidence,
                HasConflict = document.MatchHasConflict,
                DatVerified = document.MatchDatVerified,
                Tier = document.MatchTier,
                PrimaryMatchKind = document.MatchPrimaryMatchKind
            },
            PlatformFamily = document.PlatformFamily,
            ClassificationReasonCode = document.ClassificationReasonCode,
            ClassificationConfidence = document.ClassificationConfidence
        };

    private static CollectionHashCacheDocument ToDocument(CollectionHashCacheEntry entry)
        => new()
        {
            Id = BuildHashKey(entry.Path, entry.Algorithm, entry.SizeBytes, entry.LastWriteUtc),
            Path = NormalizePath(entry.Path),
            Algorithm = NormalizeAlgorithm(entry.Algorithm),
            SizeBytes = entry.SizeBytes,
            LastWriteUtcTicks = NormalizeUtc(entry.LastWriteUtc).Ticks,
            Hash = NormalizeHash(entry.Hash),
            RecordedUtcTicks = NormalizeUtc(entry.RecordedUtc).Ticks
        };

    private static CollectionHashCacheEntry ToContract(CollectionHashCacheDocument document)
        => new()
        {
            Path = document.Path,
            Algorithm = document.Algorithm,
            SizeBytes = document.SizeBytes,
            LastWriteUtc = new DateTime(document.LastWriteUtcTicks, DateTimeKind.Utc),
            Hash = document.Hash,
            RecordedUtc = new DateTime(document.RecordedUtcTicks, DateTimeKind.Utc)
        };

    private static CollectionRunSnapshotDocument ToDocument(CollectionRunSnapshot snapshot)
        => new()
        {
            Id = string.IsNullOrWhiteSpace(snapshot.RunId)
                ? throw new ArgumentException("RunId must not be empty.", nameof(snapshot))
                : snapshot.RunId,
            StartedUtcTicks = NormalizeUtc(snapshot.StartedUtc).Ticks,
            CompletedUtcTicks = NormalizeUtc(snapshot.CompletedUtc).Ticks,
            Mode = snapshot.Mode ?? RunConstants.ModeDryRun,
            Status = snapshot.Status ?? RunConstants.StatusOk,
            Roots = snapshot.Roots.Select(NormalizePath).ToList(),
            RootFingerprint = snapshot.RootFingerprint ?? "",
            DurationMs = snapshot.DurationMs,
            TotalFiles = snapshot.TotalFiles,
            CollectionSizeBytes = snapshot.CollectionSizeBytes,
            Games = snapshot.Games,
            Dupes = snapshot.Dupes,
            Junk = snapshot.Junk,
            DatMatches = snapshot.DatMatches,
            ConvertedCount = snapshot.ConvertedCount,
            FailCount = snapshot.FailCount,
            SavedBytes = snapshot.SavedBytes,
            ConvertSavedBytes = snapshot.ConvertSavedBytes,
            HealthScore = snapshot.HealthScore
        };

    private static CollectionRunSnapshot ToContract(CollectionRunSnapshotDocument document)
        => new()
        {
            RunId = document.Id,
            StartedUtc = new DateTime(document.StartedUtcTicks, DateTimeKind.Utc),
            CompletedUtc = new DateTime(document.CompletedUtcTicks, DateTimeKind.Utc),
            Mode = document.Mode,
            Status = document.Status,
            Roots = document.Roots.ToArray(),
            RootFingerprint = document.RootFingerprint,
            DurationMs = document.DurationMs,
            TotalFiles = document.TotalFiles,
            CollectionSizeBytes = document.CollectionSizeBytes,
            Games = document.Games,
            Dupes = document.Dupes,
            Junk = document.Junk,
            DatMatches = document.DatMatches,
            ConvertedCount = document.ConvertedCount,
            FailCount = document.FailCount,
            SavedBytes = document.SavedBytes,
            ConvertSavedBytes = document.ConvertSavedBytes,
            HealthScore = document.HealthScore
        };

    private sealed class MetadataDocument
    {
        public string Id { get; set; } = MetadataId;
        public int SchemaVersion { get; set; }
        public long CreatedUtcTicks { get; set; }
        public long UpdatedUtcTicks { get; set; }
    }

    private sealed class CollectionIndexEntryDocument
    {
        public string Id { get; set; } = "";
        public string Path => Id;
        public string Root { get; set; } = "";
        public string FileName { get; set; } = "";
        public string Extension { get; set; } = "";
        public long SizeBytes { get; set; }
        public long LastWriteUtcTicks { get; set; }
        public long LastScannedUtcTicks { get; set; }
        public string EnrichmentFingerprint { get; set; } = "";
        public string PrimaryHashType { get; set; } = "SHA1";
        public string? PrimaryHash { get; set; }
        public string? HeaderlessHash { get; set; }
        public string ConsoleKey { get; set; } = "UNKNOWN";
        public string GameKey { get; set; } = "";
        public string Region { get; set; } = "UNKNOWN";
        public int RegionScore { get; set; }
        public int FormatScore { get; set; }
        public long VersionScore { get; set; }
        public int HeaderScore { get; set; }
        public int CompletenessScore { get; set; }
        public long SizeTieBreakScore { get; set; }
        public FileCategory Category { get; set; } = FileCategory.Game;
        public bool DatMatch { get; set; }
        public string? DatGameName { get; set; }
        public DatAuditStatus DatAuditStatus { get; set; } = DatAuditStatus.Unknown;
        public SortDecision SortDecision { get; set; } = SortDecision.Blocked;
        public DecisionClass DecisionClass { get; set; } = DecisionClass.Unknown;
        public EvidenceTier EvidenceTier { get; set; } = EvidenceTier.Tier4_Unknown;
        public MatchKind PrimaryMatchKind { get; set; } = MatchKind.None;
        public int DetectionConfidence { get; set; }
        public bool DetectionConflict { get; set; }
        public bool HasHardEvidence { get; set; }
        public bool IsSoftOnly { get; set; } = true;
        public MatchLevel MatchLevel { get; set; } = MatchLevel.None;
        public string MatchReasoning { get; set; } = string.Empty;
        public List<string> MatchSources { get; set; } = [];
        public bool MatchHasHardEvidence { get; set; }
        public bool MatchHasConflict { get; set; }
        public bool MatchDatVerified { get; set; }
        public EvidenceTier MatchTier { get; set; } = EvidenceTier.Tier4_Unknown;
        public MatchKind MatchPrimaryMatchKind { get; set; } = MatchKind.None;
        public PlatformFamily PlatformFamily { get; set; } = PlatformFamily.Unknown;
        public string ClassificationReasonCode { get; set; } = "game-default";
        public int ClassificationConfidence { get; set; } = 100;
    }

    private sealed class CollectionHashCacheDocument
    {
        public string Id { get; set; } = "";
        public string Path { get; set; } = "";
        public string Algorithm { get; set; } = "SHA1";
        public long SizeBytes { get; set; }
        public long LastWriteUtcTicks { get; set; }
        public string Hash { get; set; } = "";
        public long RecordedUtcTicks { get; set; }
    }

    private sealed class CollectionRunSnapshotDocument
    {
        public string Id { get; set; } = "";
        public long StartedUtcTicks { get; set; }
        public long CompletedUtcTicks { get; set; }
        public string Mode { get; set; } = RunConstants.ModeDryRun;
        public string Status { get; set; } = RunConstants.StatusOk;
        public List<string> Roots { get; set; } = [];
        public string RootFingerprint { get; set; } = "";
        public long DurationMs { get; set; }
        public int TotalFiles { get; set; }
        public long CollectionSizeBytes { get; set; }
        public int Games { get; set; }
        public int Dupes { get; set; }
        public int Junk { get; set; }
        public int DatMatches { get; set; }
        public int ConvertedCount { get; set; }
        public int FailCount { get; set; }
        public long SavedBytes { get; set; }
        public long ConvertSavedBytes { get; set; }
        public int HealthScore { get; set; }
    }
}
