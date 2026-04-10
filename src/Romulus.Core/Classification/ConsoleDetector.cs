using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Romulus.Core.Caching;
using Romulus.Core.SetParsing;
using Romulus.Contracts.Models;

namespace Romulus.Core.Classification;

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
    private (Regex Pattern, string ConsoleKey)[] _keywordPatterns; // dynamic keywords
    private readonly DiscHeaderDetector? _discHeaderDetector;
    private readonly CartridgeHeaderDetector? _cartridgeHeaderDetector;
    private readonly Func<string, IReadOnlyList<string>>? _archiveEntryProvider;
    private long _keywordRegexTimeoutCount;

    // V2-H11: Folder-level detection cache — avoids re-scanning path segments per file
    // V2-BUG-H01: Bounded LruCache instead of unbounded Dictionary to prevent OOM at scale
    private readonly LruCache<string, string> _folderDetectCache = new(65536);

    public ConsoleDetector(
        IReadOnlyList<ConsoleInfo> consoles,
        DiscHeaderDetector? discHeaderDetector = null,
        Func<string, IReadOnlyList<string>>? archiveEntryProvider = null,
        CartridgeHeaderDetector? cartridgeHeaderDetector = null)
    {
        _discHeaderDetector = discHeaderDetector;
        _archiveEntryProvider = archiveEntryProvider;
        _cartridgeHeaderDetector = cartridgeHeaderDetector;
        _folderMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _uniqueExtMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _ambigExtMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        _consoles = new Dictionary<string, ConsoleInfo>(StringComparer.OrdinalIgnoreCase);

        var kwPatterns = new List<(Regex, string)>();

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

            // Build keyword regex patterns from consoles.json keywords
            foreach (var kw in c.Keywords)
            {
                if (string.IsNullOrWhiteSpace(kw)) continue;
                var escaped = Regex.Escape(kw);
                var pattern = $@"\[{escaped}\]|\({escaped}\)";
                try
                {
                    kwPatterns.Add((new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase, SafeRegex.ShortTimeout), c.Key));
                }
                catch (ArgumentException) { /* skip malformed patterns */ }
            }
        }

        _keywordPatterns = kwPatterns.ToArray();
    }

    /// <summary>
    /// Number of Regex timeout events seen while evaluating dynamic keyword patterns.
    /// Exposed for diagnostics and regression tests.
    /// </summary>
    public long KeywordRegexTimeoutCount => Interlocked.Read(ref _keywordRegexTimeoutCount);

    internal void SetKeywordPatternsForTesting((Regex Pattern, string ConsoleKey)[] patterns)
    {
        _keywordPatterns = patterns ?? Array.Empty<(Regex Pattern, string ConsoleKey)>();
    }

    /// <summary>
    /// Detect console from keyword tags in filename (e.g. "[PS1]", "(GBA)").
    /// Uses dynamic patterns built from consoles.json keywords, with fallback to hardcoded patterns.
    /// Returns (consoleKey, confidence=75) or null.
    /// </summary>
    internal (string ConsoleKey, int Confidence)? DetectByKeywordDynamic(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        // Dynamic patterns from consoles.json (preferred — Single Source of Truth)
        foreach (var (pattern, key) in _keywordPatterns)
        {
            try
            {
                if (pattern.IsMatch(fileName))
                    return (key, 75);
            }
            catch (RegexMatchTimeoutException)
            {
                Interlocked.Increment(ref _keywordRegexTimeoutCount);
            }
        }

        // Fallback: hardcoded patterns for backward compatibility when keywords not in JSON
        return FilenameConsoleAnalyzer.DetectByKeyword(fileName);
    }

    /// <summary>
    /// Returns the CategoryOverride for a given console key, or null if none configured.
    /// </summary>
    public string? GetCategoryOverride(string consoleKey)
    {
        return _consoles.TryGetValue(consoleKey, out var info) ? info.CategoryOverride : null;
    }

    /// <summary>
    /// Loads console definitions from a consoles.json file.
    /// </summary>
    public static ConsoleDetector LoadFromJson(
        string jsonContent,
        DiscHeaderDetector? discHeaderDetector = null,
        Func<string, IReadOnlyList<string>>? archiveEntryProvider = null,
        CartridgeHeaderDetector? cartridgeHeaderDetector = null)
    {
        using var doc = JsonDocument.Parse(jsonContent);
        var consoles = new List<ConsoleInfo>();

        if (doc.RootElement.TryGetProperty("consoles", out var array))
        {
            foreach (var item in array.EnumerateArray())
            {
                var key = item.GetProperty("key").GetString() ?? "";
                // V2-BUG-M04: Reject consoles with empty keys from malformed consoles.json
                if (string.IsNullOrWhiteSpace(key))
                    continue;
                var displayName = item.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? key : key;
                var discBased = item.TryGetProperty("discBased", out var db) && db.GetBoolean();
                var familyText = item.TryGetProperty("family", out var fam)
                    ? fam.GetString()
                    : null;
                var family = ParsePlatformFamily(familyText, discBased);
                var hashStrategy = item.TryGetProperty("hashStrategy", out var hs)
                    ? hs.GetString()
                    : null;

                var uniqueExts = ReadStringArray(item, "uniqueExts");
                var ambigExts = ReadStringArray(item, "ambigExts");
                var aliases = ReadStringArray(item, "folderAliases");
                var categoryOverride = item.TryGetProperty("categoryOverride", out var co) ? co.GetString() : null;
                var keywords = ReadStringArray(item, "keywords");
                var datSources = ReadStringArray(item, "datSources");

                consoles.Add(new ConsoleInfo(
                    key,
                    displayName,
                    discBased,
                    uniqueExts,
                    ambigExts,
                    aliases,
                    categoryOverride,
                    keywords,
                    family,
                    hashStrategy,
                    datSources));
            }
        }

        return new ConsoleDetector(consoles, discHeaderDetector, archiveEntryProvider, cartridgeHeaderDetector);
    }

    /// <summary>
    /// Detect console by folder path components (highest confidence heuristic).
    /// Checks each path segment against the folder alias map.
    /// V2-H11: Results are cached per directory path to avoid repeated segment scanning.
    /// </summary>
    public string? DetectByFolder(string filePath, string rootPath)
    {
        // Cache key: directory of the file relative to root (normalized for case-insensitive match)
        var dir = Path.GetDirectoryName(filePath) ?? "";
        var normalizedRoot = NormalizePathForCache(rootPath);
        var normalizedDir = NormalizePathForCache(dir);
        var cacheKey = $"{normalizedRoot}|{normalizedDir}";
        if (_folderDetectCache.TryGet(cacheKey, out var cached))
            return cached.Length > 0 ? cached : null;

        // Only check path segments between root and file (relative path)
        var relativePath = GetRelativePath(filePath, rootPath);
        if (string.IsNullOrEmpty(relativePath))
        {
            _folderDetectCache.Set(cacheKey, "");
            return null;
        }

        var segments = relativePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);

        // Check folder segments (skip the filename itself)
        for (int i = 0; i < segments.Length - 1; i++)
        {
            var consoleKey = ResolveFolderAlias(segments[i]);
            if (consoleKey is not null)
            {
                _folderDetectCache.Set(cacheKey, consoleKey);
                return consoleKey;
            }
        }

        // Fallback: check the root folder's own name (e.g. root = "Y:\Games\Sega CD")
        var rootName = Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var rootConsole = ResolveFolderAlias(rootName);
        if (rootConsole is not null)
        {
            _folderDetectCache.Set(cacheKey, rootConsole);
            return rootConsole;
        }

        // Fallback: when root is nested (e.g. "I:\\Sony - Playstation 2\\Konvert"),
        // inspect parent root segments to recover the console hint.
        var rootSegments = rootPath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = rootSegments.Length - 2; i >= 0; i--)
        {
            var parentConsole = ResolveFolderAlias(rootSegments[i]);
            if (parentConsole is not null)
            {
                _folderDetectCache.Set(cacheKey, parentConsole);
                return parentConsole;
            }
        }

        _folderDetectCache.Set(cacheKey, "");
        return null;
    }

    // Known console vendor prefixes — used to strip vendor from folder names
    // like "Sony Playstation 2" → try "Playstation 2" against folderMap.
    private static readonly string[] KnownVendorPrefixes =
    [
        "Sony", "Microsoft", "Nintendo", "Sega", "Atari", "SNK", "NEC",
        "Commodore", "Amstrad", "Bandai", "Coleco", "Mattel", "Magnavox",
        "Sharp", "Panasonic", "Philips", "Casio", "Tiger", "Watara",
        "GCE", "Emerson", "Fairchild"
    ];

    private string? ResolveFolderAlias(string? folderSegment)
    {
        if (string.IsNullOrWhiteSpace(folderSegment))
            return null;

        if (_folderMap.TryGetValue(folderSegment, out var direct))
            return direct;

        // Support vendor-prefixed folder names like "Sony - Playstation 2".
        var parts = folderSegment.Split(" - ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length > 1)
        {
            foreach (var part in parts)
            {
                if (_folderMap.TryGetValue(part, out var resolved))
                    return resolved;
            }
        }

        // Support vendor-prefixed folder names WITHOUT hyphen: "Sony Playstation 2".
        // Strip known vendor prefix and try the remainder as alias.
        foreach (var vendor in KnownVendorPrefixes)
        {
            if (folderSegment.StartsWith(vendor + " ", StringComparison.OrdinalIgnoreCase)
                && folderSegment.Length > vendor.Length + 1)
            {
                var remainder = folderSegment[(vendor.Length + 1)..].Trim();
                if (remainder.Length > 0 && _folderMap.TryGetValue(remainder, out var vendorResolved))
                    return vendorResolved;
            }
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
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        if (IsClearlyInvalidFile(filePath))
            return "UNKNOWN";

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

        // Method 3b: Disc header binary detection (ISO/BIN/GCM/CHD) — higher confidence than archive interior
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

        // Method 4: Archive interior extension (ZIP/7z contain ROM files whose extension reveals the console)
        var byArchive = DetectByArchiveContent(filePath, ext);
        if (byArchive is not null)
            return byArchive;

        // Method 5: Cartridge header binary detection (NES/SNES/MD/N64/GBA/GB)
        if (_cartridgeHeaderDetector is not null)
        {
            var byCartridge = _cartridgeHeaderDetector.Detect(filePath);
            if (byCartridge is not null)
                return byCartridge;
        }

        // Method 6: Filename serial numbers and system keywords (e.g. SLUS-00123 → PS1, [GBA] tag)
        var byFilename = FilenameConsoleAnalyzer.Detect(fileName);
        if (byFilename is not null)
            return byFilename.Value.ConsoleKey;

        return "UNKNOWN";
    }

    /// <summary>
    /// Multi-method detection with confidence scoring.
    /// Collects hypotheses from ALL available detection methods and resolves via HypothesisResolver.
    /// Unlike Detect(), this runs ALL methods (not short-circuit) to gather maximum evidence.
    /// </summary>
    public ConsoleDetectionResult DetectWithConfidence(string filePath, string rootPath)
    {
        var hypotheses = new List<DetectionHypothesis>();
        var ext = Path.GetExtension(filePath);
        var fileName = Path.GetFileNameWithoutExtension(filePath);

        if (IsClearlyInvalidFile(filePath))
            return ConsoleDetectionResult.Unknown;


        // Method 1: Folder name
        var byFolder = DetectByFolder(filePath, rootPath);
        if (byFolder is not null)
            hypotheses.Add(new DetectionHypothesis(byFolder, (int)DetectionSource.FolderName,
                DetectionSource.FolderName, $"folder={byFolder}"));

        // Method 2: Unique extension
        var byExt = DetectByExtension(ext);
        if (byExt is not null)
            hypotheses.Add(new DetectionHypothesis(byExt, (int)DetectionSource.UniqueExtension,
                DetectionSource.UniqueExtension, $"ext={ext}"));

        // Method 3: Ambiguous extension
        var ambig = GetAmbiguousMatches(ext);
        if (ambig.Count == 1)
            hypotheses.Add(new DetectionHypothesis(ambig[0], (int)DetectionSource.AmbiguousExtension,
                DetectionSource.AmbiguousExtension, $"ambig-ext={ext}"));

        // Method 4: Disc header
        if (_discHeaderDetector is not null)
        {
            var discExt = ext.ToLowerInvariant();
            string? byHeader = discExt == ".chd"
                ? _discHeaderDetector.DetectFromChd(filePath)
                : discExt is ".iso" or ".gcm" or ".img" or ".bin"
                    ? _discHeaderDetector.DetectFromDiscImage(filePath)
                    : null;
            if (byHeader is not null)
            {
                var source = DetectionSource.DiscHeader;
                var confidence = (int)DetectionSource.DiscHeader;

                // Generic PS1 header strings can appear in PS2 images without BOOT2 markers.
                // If folder evidence points to PS2 on disc-like extensions, treat this as soft evidence.
                if (ShouldDowngradeGenericPs1Header(byFolder, byHeader, discExt))
                {
                    source = DetectionSource.AmbiguousExtension;
                    confidence = (int)DetectionSource.AmbiguousExtension;
                }

                hypotheses.Add(new DetectionHypothesis(byHeader, confidence,
                    source, $"disc-header={byHeader}"));
            }
        }

        // Method 5: Archive interior
        var byArchive = DetectByArchiveContent(filePath, ext);
        if (byArchive is not null)
            hypotheses.Add(new DetectionHypothesis(byArchive, (int)DetectionSource.ArchiveContent,
                DetectionSource.ArchiveContent, $"archive-inner={byArchive}"));

        // Method 6: Cartridge header
        if (_cartridgeHeaderDetector is not null)
        {
            var byCartridge = _cartridgeHeaderDetector.Detect(filePath);
            if (byCartridge is not null)
                hypotheses.Add(new DetectionHypothesis(byCartridge, (int)DetectionSource.CartridgeHeader,
                    DetectionSource.CartridgeHeader, $"cartridge-header={byCartridge}"));
        }

        // Method 7: Filename serial numbers
        var bySerial = FilenameConsoleAnalyzer.DetectBySerial(fileName);
        if (bySerial is not null)
            hypotheses.Add(new DetectionHypothesis(bySerial.Value.ConsoleKey, bySerial.Value.Confidence,
                DetectionSource.SerialNumber, $"serial={fileName}"));

        // Method 8: Filename keywords (dynamic from consoles.json + hardcoded fallback)
        var byKeyword = DetectByKeywordDynamic(fileName);
        if (byKeyword is not null)
            hypotheses.Add(new DetectionHypothesis(byKeyword.Value.ConsoleKey, byKeyword.Value.Confidence,
                DetectionSource.FilenameKeyword, $"keyword={fileName}"));

        return HypothesisResolver.Resolve(hypotheses, GetPlatformFamily);
    }

    private static bool ShouldDowngradeGenericPs1Header(string? byFolder, string byHeader, string discExt)
    {
        if (!string.Equals(byHeader, "PS1", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.Equals(byFolder, "PS2", StringComparison.OrdinalIgnoreCase))
            return false;

        return discExt is ".iso" or ".bin" or ".img" or ".chd";
    }

    private static bool IsClearlyInvalidFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !ClassificationIo.FileExists(filePath))
            return false;

        try
        {
            return ClassificationIo.FileLength(filePath) == 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }


    /// <summary>
    /// Detect console by inspecting informative entries inside a ZIP or 7z archive.
    /// Scans all entries and chooses the strongest deterministic signal instead of
    /// relying only on the largest or longest entry.
    /// Returns null if not a supported archive or no match found.
    /// </summary>
    public string? DetectByArchiveContent(string filePath, string outerExt)
    {
        var lowerExt = string.IsNullOrWhiteSpace(outerExt)
            ? string.Empty
            : outerExt.ToLowerInvariant();

        // Be robust against incorrect caller-provided outerExt and prefer the real file extension.
        if (lowerExt is not ".zip" and not ".7z")
            lowerExt = Path.GetExtension(filePath).ToLowerInvariant();

        if (lowerExt == ".zip")
            return DetectByZipContent(filePath);

        if (lowerExt == ".7z" && _archiveEntryProvider is not null)
            return DetectByArchiveEntryNames(_archiveEntryProvider(filePath), filePath);

        return null;
    }

    private string? DetectByZipContent(string filePath)
    {
        try
        {
            using var archive = ClassificationIo.OpenZipRead(filePath);
            ArchiveEntryDetectionCandidate? bestEntry = null;
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue; // skip directories
                var candidate = CreateArchiveEntryDetectionCandidate(entry.FullName, entry.Length, filePath);
                if (candidate is null)
                    continue;

                if (bestEntry is null || CompareArchiveCandidates(candidate, bestEntry) < 0)
                    bestEntry = candidate;
            }

            if (bestEntry is null)
                return null;

            return bestEntry.ConsoleKey;
        }
        catch (InvalidDataException) { /* corrupt/not-a-zip */ }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return null;
    }

    /// <summary>
    /// Detect console from a list of archive entry names (e.g. from 7z listing).
    /// Finds the entry with the longest name (heuristic: largest ROM path) and runs extension detection.
    /// </summary>
    private string? DetectByArchiveEntryNames(IReadOnlyList<string> entryNames, string? outerArchivePath)
    {
        if (entryNames.Count == 0)
            return null;

        ArchiveEntryDetectionCandidate? bestEntry = null;
        foreach (var name in entryNames)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            var candidate = CreateArchiveEntryDetectionCandidate(name, sizeBytes: null, outerArchivePath);
            if (candidate is null)
                continue;

            if (bestEntry is null || CompareArchiveCandidates(candidate, bestEntry) < 0)
                bestEntry = candidate;
        }

        if (bestEntry is null)
            return null;

        return bestEntry.ConsoleKey;
    }

    private ArchiveEntryDetectionCandidate? CreateArchiveEntryDetectionCandidate(
        string entryName,
        long? sizeBytes,
        string? outerArchivePath)
    {
        if (string.IsNullOrWhiteSpace(entryName))
            return null;

        var fileName = Path.GetFileName(entryName);
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        var innerExt = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(innerExt))
            return null;

        var normalizedExt = innerExt.ToLowerInvariant();
        if (FileClassifier.IsNonRomExtension(normalizedExt))
            return null;

        var outerBaseName = string.IsNullOrWhiteSpace(outerArchivePath)
            ? null
            : Path.GetFileNameWithoutExtension(outerArchivePath);
        var innerBaseName = Path.GetFileNameWithoutExtension(fileName);

        var byExt = DetectByExtension(normalizedExt);
        if (byExt is not null)
            return new ArchiveEntryDetectionCandidate(byExt, 4, sizeBytes ?? 0, entryName, normalizedExt);

        var serial = FilenameConsoleAnalyzer.DetectBySerial(innerBaseName)
            ?? (outerBaseName is null ? null : FilenameConsoleAnalyzer.DetectBySerial(outerBaseName));
        if (serial is not null)
            return new ArchiveEntryDetectionCandidate(serial.Value.ConsoleKey, 3, sizeBytes ?? 0, entryName, normalizedExt);

        var keyword = DetectByKeywordDynamic(innerBaseName)
            ?? (outerBaseName is null ? null : DetectByKeywordDynamic(outerBaseName));
        if (keyword is not null)
        {
            var priority = SetDescriptorSupport.IsDescriptorExtension(normalizedExt) ? 3 : 2;
            return new ArchiveEntryDetectionCandidate(keyword.Value.ConsoleKey, priority, sizeBytes ?? 0, entryName, normalizedExt);
        }

        var ambig = GetAmbiguousMatches(normalizedExt);
        if (ambig.Count == 1)
            return new ArchiveEntryDetectionCandidate(ambig[0], 1, sizeBytes ?? 0, entryName, normalizedExt);

        return null;
    }

    private static int CompareArchiveCandidates(ArchiveEntryDetectionCandidate left, ArchiveEntryDetectionCandidate right)
    {
        var byPriority = right.Priority.CompareTo(left.Priority);
        if (byPriority != 0)
            return byPriority;

        var bySize = right.SizeBytes.CompareTo(left.SizeBytes);
        if (bySize != 0)
            return bySize;

        var leftIsDescriptor = SetDescriptorSupport.IsDescriptorExtension(left.Extension);
        var rightIsDescriptor = SetDescriptorSupport.IsDescriptorExtension(right.Extension);
        if (leftIsDescriptor != rightIsDescriptor)
            return rightIsDescriptor.CompareTo(leftIsDescriptor);

        var byPath = string.Compare(left.EntryName, right.EntryName, StringComparison.OrdinalIgnoreCase);
        if (byPath != 0)
            return byPath;

        return string.Compare(left.EntryName, right.EntryName, StringComparison.Ordinal);
    }

    private sealed record ArchiveEntryDetectionCandidate(
        string ConsoleKey,
        int Priority,
        long SizeBytes,
        string EntryName,
        string Extension);

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
    /// Returns the configured platform family for a console key.
    /// </summary>
    public PlatformFamily GetPlatformFamily(string consoleKey)
    {
        if (_consoles.TryGetValue(consoleKey, out var info))
            return info.Family;

        return PlatformFamily.Unknown;
    }

    /// <summary>
    /// Get all registered console keys.
    /// </summary>
    public IReadOnlyCollection<string> AllConsoleKeys => _consoles.Keys;

    private static string GetRelativePath(string fullPath, string rootPath)
    {
        try
        {
            return Path.GetRelativePath(rootPath, fullPath);
        }
        catch (ArgumentException)
        {
            return Path.GetFileName(fullPath);
        }
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

    private static string NormalizePathForCache(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToUpperInvariant();
        }
        catch (ArgumentException)
        {
            return path.Trim().ToUpperInvariant();
        }
    }

    private static PlatformFamily ParsePlatformFamily(string? value, bool discBased)
    {
        if (!string.IsNullOrWhiteSpace(value) && Enum.TryParse<PlatformFamily>(value, ignoreCase: true, out var parsed))
            return parsed;

        return discBased ? PlatformFamily.RedumpDisc : PlatformFamily.Unknown;
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
    string[] FolderAliases,
    string? CategoryOverride = null,
    string[] Keywords = null!,
    PlatformFamily Family = PlatformFamily.Unknown,
    string? HashStrategy = null,
    string[] DatSources = null!)
{
    public string[] Keywords { get; init; } = Keywords ?? Array.Empty<string>();
    public string[] DatSources { get; init; } = DatSources ?? Array.Empty<string>();
}
