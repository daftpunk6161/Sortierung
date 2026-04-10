using Romulus.Contracts.Models;

namespace Romulus.Contracts.Ports;

/// <summary>
/// Storage-agnostic port for the persisted collection index.
/// Implementations may back this contract with LiteDB, JSON, or other local stores,
/// but callers must observe the same deterministic contract semantics.
/// </summary>
public interface ICollectionIndex
{
    /// <summary>
    /// Returns store metadata including the current schema version.
    /// </summary>
    ValueTask<CollectionIndexMetadata> GetMetadataAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the number of persisted collection entries.
    /// </summary>
    ValueTask<int> CountEntriesAsync(CancellationToken ct = default);

    /// <summary>
    /// Resolves one entry by absolute normalized path.
    /// Returns null when the file is not present in the persisted index.
    /// </summary>
    ValueTask<CollectionIndexEntry?> TryGetByPathAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Resolves multiple entries by absolute normalized path.
    /// Implementations should return deterministic ordering for identical inputs.
    /// </summary>
    ValueTask<IReadOnlyList<CollectionIndexEntry>> GetByPathsAsync(
        IReadOnlyList<string> paths,
        CancellationToken ct = default);

    /// <summary>
    /// Lists entries for a detected console key.
    /// Implementations should return deterministic ordering.
    /// </summary>
    ValueTask<IReadOnlyList<CollectionIndexEntry>> ListByConsoleAsync(
        string consoleKey,
        CancellationToken ct = default);

    /// <summary>
    /// Lists persisted entries within the current scan scope.
    /// Implementations must treat roots as absolute directory scopes and apply extension filters deterministically.
    /// </summary>
    ValueTask<IReadOnlyList<CollectionIndexEntry>> ListEntriesInScopeAsync(
        IReadOnlyList<string> roots,
        IReadOnlyCollection<string> extensions,
        CancellationToken ct = default);

    /// <summary>
    /// Upserts one or more collection entries.
    /// The caller is responsible for passing normalized paths, UTC timestamps, and canonical hash casing.
    /// </summary>
    ValueTask UpsertEntriesAsync(
        IReadOnlyList<CollectionIndexEntry> entries,
        CancellationToken ct = default);

    /// <summary>
    /// Removes persisted entries by absolute normalized path.
    /// </summary>
    ValueTask RemovePathsAsync(
        IReadOnlyList<string> paths,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves a reusable hash entry for a concrete file state.
    /// Callers must pass the exact size and UTC last-write timestamp used for cache validation.
    /// </summary>
    ValueTask<CollectionHashCacheEntry?> TryGetHashAsync(
        string path,
        string algorithm,
        long sizeBytes,
        DateTime lastWriteUtc,
        CancellationToken ct = default);

    /// <summary>
    /// Persists a reusable hash entry.
    /// </summary>
    ValueTask SetHashAsync(CollectionHashCacheEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Appends one run snapshot derived from the existing run truth.
    /// </summary>
    ValueTask AppendRunSnapshotAsync(CollectionRunSnapshot snapshot, CancellationToken ct = default);

    /// <summary>
    /// Returns the number of persisted run snapshots.
    /// </summary>
    ValueTask<int> CountRunSnapshotsAsync(CancellationToken ct = default);

    /// <summary>
    /// Lists persisted run snapshots ordered from newest to oldest.
    /// </summary>
    ValueTask<IReadOnlyList<CollectionRunSnapshot>> ListRunSnapshotsAsync(
        int limit = 50,
        CancellationToken ct = default);
}
