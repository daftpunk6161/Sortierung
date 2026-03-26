using System.Text.Json;
using RomCleanup.Contracts.Models;

namespace RomCleanup.Infrastructure.History;

/// <summary>
/// [v2.1 deferred] Scan index tracker — persists file fingerprints for change detection.
/// Mirrors RunIndex.ps1. Not wired into RunOrchestrator pipeline yet.
/// </summary>
public sealed class ScanIndexService
{
    private const string SchemaVersion = "scan-index-v1";

    /// <summary>
    /// Loads scan index from JSON file.
    /// </summary>
    public Dictionary<string, ScanIndexEntry> Load(string indexPath)
    {
        var index = new Dictionary<string, ScanIndexEntry>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(indexPath))
            return index;

        try
        {
            var json = File.ReadAllText(indexPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in entries.EnumerateObject())
                {
                    var entry = new ScanIndexEntry { Path = prop.Name };

                    if (prop.Value.TryGetProperty("fingerprint", out var fp))
                        entry.Fingerprint = fp.GetString() ?? "";
                    if (prop.Value.TryGetProperty("hash", out var hash))
                        entry.Hash = hash.GetString();
                    if (prop.Value.TryGetProperty("lastScan", out var ls) && ls.TryGetDateTime(out var dt))
                        entry.LastScan = dt;

                    index[prop.Name] = entry;
                }
            }
        }
        catch (JsonException)
        {
            // Return empty index on parse failure
        }

        return index;
    }

    /// <summary>
    /// Saves scan index to JSON file.
    /// </summary>
    public void Save(string indexPath, Dictionary<string, ScanIndexEntry> index)
    {
        var entries = new Dictionary<string, object>();
        foreach (var (key, entry) in index)
        {
            entries[key] = new
            {
                fingerprint = entry.Fingerprint,
                hash = entry.Hash,
                lastScan = entry.LastScan.ToString("o")
            };
        }

        var doc = new
        {
            schema = SchemaVersion,
            timestamp = DateTime.UtcNow.ToString("o"),
            entries
        };

        var dir = Path.GetDirectoryName(indexPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });

        // Atomic write: write to temp file first, then rename to prevent corruption on crash
        var tmpPath = indexPath + ".tmp";
        File.WriteAllText(tmpPath, json, System.Text.Encoding.UTF8);
        File.Move(tmpPath, indexPath, overwrite: true);
    }

    /// <summary>
    /// Creates a path fingerprint (FullName|Length|LastWriteTimeTicks).
    /// </summary>
    public static string GetPathFingerprint(string filePath)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists)
            return $"{Path.GetFullPath(filePath)}|0|0";
        return $"{Path.GetFullPath(filePath).ToUpperInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
    }
}
