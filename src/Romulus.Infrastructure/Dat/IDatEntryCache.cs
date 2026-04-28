namespace Romulus.Infrastructure.Dat;

/// <summary>
/// Per-DAT-File cache for parsed DAT payloads. The cache key is implicitly
/// (canonicalPath + size + mtimeUtcTicks + hashType + parser version). Any change
/// to the source DAT or hash-type invalidates the entry, so callers can always
/// trust a hit without further validation.
/// </summary>
public interface IDatEntryCache
{
    /// <summary>
    /// Try to load a cached payload for the given DAT path + hash type.
    /// Returns <c>true</c> when a valid (non-stale) cache entry exists.
    /// </summary>
    bool TryGet(string datPath, string hashType, out CachedDatPayload payload);

    /// <summary>
    /// Persist a parsed payload for the given DAT path + hash type.
    /// Overwrites any prior entry for the same key.
    /// </summary>
    void Set(string datPath, string hashType, CachedDatPayload payload);
}

/// <summary>
/// Snapshot of a single parsed DAT file: parent/clone map plus games-with-roms.
/// Mirror of <c>(GetDatParentCloneIndex, ParseDatFile)</c> output, suitable for
/// JSON serialization to disk.
/// </summary>
public sealed class CachedDatPayload
{
    public Dictionary<string, string> ParentMap { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<Dictionary<string, string>>> Games { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
