using LiteDB;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.Review;

/// <summary>
/// LiteDB-backed implementation of <see cref="IReviewDecisionStore"/> sharing the collection index database file.
/// </summary>
public sealed class LiteDbReviewDecisionStore : IReviewDecisionStore, IDisposable
{
    private static readonly byte[] LiteDbSignature = "** This is a LiteDB file **"u8.ToArray();
    private const string ApprovalsCollectionName = "review_approvals";

    private readonly string _databasePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Action<string>? _onWarning;
    private LiteDatabase _database;
    private bool _disposed;

    public LiteDbReviewDecisionStore(string databasePath, Action<string>? onWarning = null)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Database path must not be empty.", nameof(databasePath));

        _databasePath = Path.GetFullPath(databasePath);
        _onWarning = onWarning;
        _database = OpenOrRecoverDatabase();
        EnsureIndexes();
    }

    public async ValueTask UpsertApprovalsAsync(
        IReadOnlyList<ReviewApprovalEntry> approvals,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(approvals);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            ct.ThrowIfCancellationRequested();

            var collection = _database.GetCollection<ReviewApprovalDocument>(ApprovalsCollectionName);
            foreach (var approval in approvals)
            {
                ct.ThrowIfCancellationRequested();
                collection.Upsert(ToDocument(approval));
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<IReadOnlyList<ReviewApprovalEntry>> ListApprovalsAsync(
        IReadOnlyList<string> paths,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (paths.Count == 0)
            return Array.Empty<ReviewApprovalEntry>();

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            ct.ThrowIfCancellationRequested();

            var collection = _database.GetCollection<ReviewApprovalDocument>(ApprovalsCollectionName);
            var approvals = new List<ReviewApprovalEntry>(paths.Count);
            foreach (var path in paths)
            {
                ct.ThrowIfCancellationRequested();
                var document = collection.FindById(NormalizePath(path));
                if (document is not null)
                    approvals.Add(ToContract(document));
            }

            return approvals;
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
            _onWarning?.Invoke("[ReviewStore] Recovering database after signature validation failure.");
            RecoverDatabaseFile("review-open-failure");
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
            return new LiteDatabase(new ConnectionString
            {
                Filename = _databasePath,
                Connection = ConnectionType.Shared
            });
        }
        catch (Exception ex) when (ex is IOException or LiteException or InvalidOperationException)
        {
            _onWarning?.Invoke($"[ReviewStore] Recovering database after open failure: {ex.Message}");
            RecoverDatabaseFile("review-open-failure");
            Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
            return new LiteDatabase(new ConnectionString
            {
                Filename = _databasePath,
                Connection = ConnectionType.Shared
            });
        }
    }

    private void EnsureIndexes()
    {
        var approvals = _database.GetCollection<ReviewApprovalDocument>(ApprovalsCollectionName);
        approvals.EnsureIndex(nameof(ReviewApprovalDocument.ConsoleKey));
        approvals.EnsureIndex(nameof(ReviewApprovalDocument.ApprovedUtcTicks));
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
        catch
        {
            return false;
        }
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path must not be empty.", nameof(path));

        return Path.GetFullPath(path);
    }

    private static string NormalizeSource(string source)
        => string.IsNullOrWhiteSpace(source) ? "manual" : source.Trim().ToLowerInvariant();

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    private static ReviewApprovalDocument ToDocument(ReviewApprovalEntry approval)
        => new()
        {
            Id = NormalizePath(approval.Path),
            ConsoleKey = string.IsNullOrWhiteSpace(approval.ConsoleKey) ? "UNKNOWN" : approval.ConsoleKey.Trim(),
            SortDecision = approval.SortDecision,
            MatchLevel = approval.MatchLevel,
            MatchReasoning = approval.MatchReasoning ?? string.Empty,
            Source = NormalizeSource(approval.Source),
            ApprovedUtcTicks = NormalizeUtc(approval.ApprovedUtc).Ticks,
            FileLastWriteUtcTicks = approval.FileLastWriteUtcTicks
        };

    private static ReviewApprovalEntry ToContract(ReviewApprovalDocument document)
        => new()
        {
            Path = document.Id,
            ConsoleKey = document.ConsoleKey,
            SortDecision = document.SortDecision,
            MatchLevel = document.MatchLevel,
            MatchReasoning = document.MatchReasoning,
            Source = document.Source,
            ApprovedUtc = new DateTime(document.ApprovedUtcTicks, DateTimeKind.Utc),
            FileLastWriteUtcTicks = document.FileLastWriteUtcTicks
        };

    private sealed class ReviewApprovalDocument
    {
        public string Id { get; set; } = "";
        public string ConsoleKey { get; set; } = "UNKNOWN";
        public SortDecision SortDecision { get; set; } = SortDecision.Review;
        public MatchLevel MatchLevel { get; set; } = MatchLevel.None;
        public string MatchReasoning { get; set; } = string.Empty;
        public string Source { get; set; } = "manual";
        public long ApprovedUtcTicks { get; set; }
        public long? FileLastWriteUtcTicks { get; set; }
    }
}
