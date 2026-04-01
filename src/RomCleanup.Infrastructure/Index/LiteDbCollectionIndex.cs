using LiteDB;
using RomCleanup.Contracts;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;

namespace RomCleanup.Infrastructure.Index;

/// <summary>
/// LiteDB-backed implementation of <see cref="ICollectionIndex"/>.
/// The adapter persists already-computed state and must not recompute business decisions.
/// </summary>
public sealed class LiteDbCollectionIndex : ICollectionIndex, IDisposable
{
    private const int CurrentSchemaVersion = 1;
    private static readonly byte[] LiteDbSignature = "** This is a LiteDB file **"u8.ToArray();
    private const string MetadataCollectionName = "metadata";
    private const string EntriesCollectionName = "entries";
    private const string HashesCollectionName = "hashes";
    private const string SnapshotsCollectionName = "snapshots";
    private const string MetadataId = "collection-index";

    private readonly string _databasePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Action<string>? _onWarning;
    private LiteDatabase _database;
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
        entries.EnsureIndex(document => document.ConsoleKey);
        entries.EnsureIndex(document => document.GameKey);
        entries.EnsureIndex(document => document.PrimaryHash);

        var hashes = _database.GetCollection<CollectionHashCacheDocument>(HashesCollectionName);
        hashes.EnsureIndex(document => document.Path);
        hashes.EnsureIndex(document => document.Algorithm);

        var snapshots = _database.GetCollection<CollectionRunSnapshotDocument>(SnapshotsCollectionName);
        snapshots.EnsureIndex(document => document.CompletedUtcTicks);
        snapshots.EnsureIndex(document => document.RootFingerprint);
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

        return Path.GetFullPath(path);
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
            PrimaryHashType = NormalizeAlgorithm(entry.PrimaryHashType),
            PrimaryHash = string.IsNullOrWhiteSpace(entry.PrimaryHash) ? null : NormalizeHash(entry.PrimaryHash),
            ConsoleKey = entry.ConsoleKey ?? "UNKNOWN",
            GameKey = entry.GameKey ?? "",
            Region = entry.Region ?? "UNKNOWN",
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
            PrimaryHashType = document.PrimaryHashType,
            PrimaryHash = document.PrimaryHash,
            ConsoleKey = document.ConsoleKey,
            GameKey = document.GameKey,
            Region = document.Region,
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
        public string PrimaryHashType { get; set; } = "SHA1";
        public string? PrimaryHash { get; set; }
        public string ConsoleKey { get; set; } = "UNKNOWN";
        public string GameKey { get; set; } = "";
        public string Region { get; set; } = "UNKNOWN";
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
