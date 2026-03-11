using System.Text.Json;

namespace RomCleanup.Core.Classification;

/// <summary>
/// Detects console type from folder names and file extensions.
/// Loads console definitions from consoles.json at construction time.
/// Mirrors the heuristic stages of Get-ConsoleType in Classification.ps1
/// (folder-map + unique-extension-map, without disc-header or tool dependencies).
/// </summary>
public sealed class ConsoleDetector
{
    private readonly Dictionary<string, string> _folderMap;     // alias → key
    private readonly Dictionary<string, string> _uniqueExtMap;  // .ext → key
    private readonly Dictionary<string, List<string>> _ambigExtMap; // .ext → [keys]
    private readonly Dictionary<string, ConsoleInfo> _consoles; // key → info
    private readonly DiscHeaderDetector? _discHeaderDetector;

    public ConsoleDetector(IReadOnlyList<ConsoleInfo> consoles, DiscHeaderDetector? discHeaderDetector = null)
    {
        _discHeaderDetector = discHeaderDetector;
        _folderMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _uniqueExtMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _ambigExtMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        _consoles = new Dictionary<string, ConsoleInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var c in consoles)
        {
            _consoles[c.Key] = c;

            foreach (var alias in c.FolderAliases)
            {
                _folderMap[alias] = c.Key;
            }

            foreach (var ext in c.UniqueExts)
            {
                var normalized = ext.StartsWith(".") ? ext : "." + ext;
                if (!_uniqueExtMap.ContainsKey(normalized))
                    _uniqueExtMap[normalized] = c.Key;
            }

            foreach (var ext in c.AmbigExts)
            {
                var normalized = ext.StartsWith(".") ? ext : "." + ext;
                if (!_ambigExtMap.TryGetValue(normalized, out var list))
                {
                    list = new List<string>();
                    _ambigExtMap[normalized] = list;
                }
                if (!list.Contains(c.Key))
                    list.Add(c.Key);
            }
        }
    }

    /// <summary>
    /// Loads console definitions from a consoles.json file.
    /// </summary>
    public static ConsoleDetector LoadFromJson(string jsonContent, DiscHeaderDetector? discHeaderDetector = null)
    {
        var doc = JsonDocument.Parse(jsonContent);
        var consoles = new List<ConsoleInfo>();

        if (doc.RootElement.TryGetProperty("consoles", out var array))
        {
            foreach (var item in array.EnumerateArray())
            {
                var key = item.GetProperty("key").GetString() ?? "";
                var displayName = item.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? key : key;
                var discBased = item.TryGetProperty("discBased", out var db) && db.GetBoolean();

                var uniqueExts = ReadStringArray(item, "uniqueExts");
                var ambigExts = ReadStringArray(item, "ambigExts");
                var aliases = ReadStringArray(item, "folderAliases");

                consoles.Add(new ConsoleInfo(key, displayName, discBased, uniqueExts, ambigExts, aliases));
            }
        }

        return new ConsoleDetector(consoles, discHeaderDetector);
    }

    /// <summary>
    /// Detect console by folder path components (highest confidence heuristic).
    /// Checks each path segment against the folder alias map.
    /// </summary>
    public string? DetectByFolder(string filePath, string rootPath)
    {
        // Only check path segments between root and file (relative path)
        var relativePath = GetRelativePath(filePath, rootPath);
        if (string.IsNullOrEmpty(relativePath))
            return null;

        var segments = relativePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);

        // Check folder segments (skip the filename itself)
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (_folderMap.TryGetValue(segments[i], out var consoleKey))
                return consoleKey;
        }

        return null;
    }

    /// <summary>
    /// Detect console by unique file extension.
    /// </summary>
    public string? DetectByExtension(string extension)
    {
        var ext = extension.StartsWith(".") ? extension : "." + extension;
        return _uniqueExtMap.TryGetValue(ext, out var key) ? key : null;
    }

    /// <summary>
    /// Get ambiguous extension matches (multiple possible consoles).
    /// </summary>
    public IReadOnlyList<string> GetAmbiguousMatches(string extension)
    {
        var ext = extension.StartsWith(".") ? extension : "." + extension;
        return _ambigExtMap.TryGetValue(ext, out var list) ? list : Array.Empty<string>();
    }

    /// <summary>
    /// Multi-method detection: folder → unique ext → ambiguous ext.
    /// Returns console key or "UNKNOWN".
    /// </summary>
    public string Detect(string filePath, string rootPath)
    {
        // Method 1: Folder name
        var byFolder = DetectByFolder(filePath, rootPath);
        if (byFolder is not null)
            return byFolder;

        // Method 2: Unique extension
        var ext = Path.GetExtension(filePath);
        var byExt = DetectByExtension(ext);
        if (byExt is not null)
            return byExt;

        // Method 3: Ambiguous extension (return first match if only one)
        var ambig = GetAmbiguousMatches(ext);
        if (ambig.Count == 1)
            return ambig[0];

        // Method 4: Disc header binary detection (ISO/BIN/GCM/CHD)
        if (_discHeaderDetector is not null)
        {
            var discExt = ext.ToLowerInvariant();
            string? byHeader = discExt == ".chd"
                ? _discHeaderDetector.DetectFromChd(filePath)
                : discExt is ".iso" or ".gcm" or ".img" or ".bin"
                    ? _discHeaderDetector.DetectFromDiscImage(filePath)
                    : null;
            if (byHeader is not null)
                return byHeader;
        }

        return "UNKNOWN";
    }

    /// <summary>
    /// Check if a console key is valid (exists in registry).
    /// </summary>
    public bool IsKnownConsole(string key) => _consoles.ContainsKey(key);

    /// <summary>
    /// Get console info by key.
    /// </summary>
    public ConsoleInfo? GetConsole(string key) =>
        _consoles.TryGetValue(key, out var info) ? info : null;

    /// <summary>
    /// Get all registered console keys.
    /// </summary>
    public IReadOnlyCollection<string> AllConsoleKeys => _consoles.Keys;

    private static string GetRelativePath(string fullPath, string rootPath)
    {
        var normalizedFull = fullPath.Replace('\\', '/').TrimEnd('/');
        var normalizedRoot = rootPath.Replace('\\', '/').TrimEnd('/');

        if (normalizedFull.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            var relative = normalizedFull.Substring(normalizedRoot.Length).TrimStart('/');
            return relative;
        }

        return Path.GetFileName(fullPath);
    }

    private static string[] ReadStringArray(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        var result = new List<string>();
        foreach (var item in arr.EnumerateArray())
        {
            var val = item.GetString();
            if (!string.IsNullOrEmpty(val))
                result.Add(val);
        }
        return result.ToArray();
    }
}

/// <summary>
/// Immutable console definition from consoles.json.
/// </summary>
public sealed record ConsoleInfo(
    string Key,
    string DisplayName,
    bool DiscBased,
    string[] UniqueExts,
    string[] AmbigExts,
    string[] FolderAliases);
