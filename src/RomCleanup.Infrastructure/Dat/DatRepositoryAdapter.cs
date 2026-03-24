using System.Security.Cryptography;
using System.Text;
using System.Xml;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;

namespace RomCleanup.Infrastructure.Dat;

/// <summary>
/// DAT file index and hash operations.
/// Port of Dat.ps1 — XML-based DAT parsing with XXE protection,
/// parent/clone mapping, game key resolution.
/// </summary>
public sealed class DatRepositoryAdapter : IDatRepository
{
    /// <summary>Maximum DAT file size to parse (100 MB).</summary>
    private const long MaxDatFileSizeBytes = 100 * 1024 * 1024;

    private readonly Action<string>? _log;

    public DatRepositoryAdapter(Action<string>? log = null)
    {
        _log = log;
    }

    public DatIndex GetDatIndex(string datRoot, IDictionary<string, string> consoleMap,
                                string hashType = "SHA1")
    {
        var index = new DatIndex();

        if (string.IsNullOrWhiteSpace(datRoot) || !Directory.Exists(datRoot))
            return index;

        foreach (var entry in consoleMap)
        {
            var consoleKey = entry.Key;
            var datFileName = entry.Value;
            var datPath = Path.IsPathRooted(datFileName) ? datFileName : Path.Combine(datRoot, datFileName);

            if (!File.Exists(datPath))
                continue;

            var games = ParseDatFile(datPath, hashType);
            foreach (var game in games)
            {
                // game.Key = gameName, game.Value = list of rom entries
                foreach (var rom in game.Value)
                {
                    if (rom.TryGetValue("hash", out var hash) && !string.IsNullOrWhiteSpace(hash))
                    {
                        rom.TryGetValue("name", out var romFileName);
                        index.Add(consoleKey, hash, game.Key, romFileName);
                    }
                }
            }
        }

        return index;
    }

    public string GetDatGameKey(string gameName, string console)
    {
        // Normalize: lowercase, trim whitespace
        return $"{console}|{gameName}".ToLowerInvariant().Trim();
    }

    public IDictionary<string, string> GetDatParentCloneIndex(string datPath)
    {
        var parentMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(datPath))
            return parentMap;

        try
        {
            var fileSize = new FileInfo(datPath).Length;
            if (fileSize > MaxDatFileSizeBytes)
                return parentMap;
        }
        catch { /* If we can't stat the file, let XmlReader handle it */ }

        var settings = CreateSecureXmlSettings();

        try
        {
            return ReadParentCloneIndex(datPath, settings);
        }
        catch (XmlException) when (settings.DtdProcessing == DtdProcessing.Prohibit)
        {
            _log?.Invoke($"[Info] DAT file '{datPath}' triggered DTD prohibition. Retrying with DtdProcessing.Ignore.");
            return ReadParentCloneIndex(datPath, CreateFallbackXmlSettings());
        }
    }

    private static Dictionary<string, string> ReadParentCloneIndex(string datPath, XmlReaderSettings settings)
    {
        var parentMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var reader = XmlReader.Create(datPath, settings);
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "game")
            {
                var name = reader.GetAttribute("name");
                var cloneOf = reader.GetAttribute("cloneof");

                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(cloneOf))
                    parentMap[name] = cloneOf;
            }
        }

        return parentMap;
    }

    public string? ResolveParentName(string gameName, IDictionary<string, string> parentMap)
    {
        if (string.IsNullOrEmpty(gameName))
            return null;

        // Walk the parent chain (max depth to prevent infinite loops)
        var current = gameName;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { current };
        const int maxDepth = 10;

        for (int i = 0; i < maxDepth; i++)
        {
            if (parentMap.TryGetValue(current, out var parent))
            {
                if (!visited.Add(parent))
                    break; // cycle detected
                current = parent;
            }
            else
            {
                break;
            }
        }

        // V2-BUG-M01: Return deepest resolved parent (current) instead of null at MaxDepth
        return string.Equals(current, gameName, StringComparison.OrdinalIgnoreCase) ? null : current;
    }

    private Dictionary<string, List<Dictionary<string, string>>> ParseDatFile(string datPath, string hashType)
        => ParseDatFileInternal(datPath, hashType, CreateSecureXmlSettings());

    private Dictionary<string, List<Dictionary<string, string>>> ParseDatFileInternal(
        string datPath, string hashType, XmlReaderSettings settings)
    {
        var games = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);

        // P2-DAT-04: Reject excessively large DAT files to prevent unbounded memory growth
        try
        {
            var fileSize = new FileInfo(datPath).Length;
            if (fileSize > MaxDatFileSizeBytes)
            {
                _log?.Invoke($"[Warning] DAT file '{datPath}' exceeds {MaxDatFileSizeBytes / (1024 * 1024)}MB limit ({fileSize / (1024 * 1024)}MB). Skipped.");
                return games;
            }
        }
        catch { /* If we can't stat the file, let XmlReader handle it */ }

        // Pre-check: reject empty or obviously non-XML files before parsing
        try
        {
            using var probe = new StreamReader(datPath, detectEncodingFromByteOrderMarks: true);
            var firstChar = probe.Read();
            if (firstChar == -1)
            {
                _log?.Invoke($"[Warning] DAT file '{datPath}' is empty. Skipped.");
                return games;
            }
            // Valid XML must start with '<' (possibly after BOM/whitespace)
            if (firstChar != '<' && !char.IsWhiteSpace((char)firstChar))
            {
                _log?.Invoke($"[Warning] DAT file '{datPath}' is not valid XML (starts with '{(char)firstChar}'). Skipped.");
                return games;
            }
        }
        catch { /* If we can't read, let XmlReader produce the proper error */ }

        try
        {
            using var reader = XmlReader.Create(datPath, settings);
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "game")
                {
                    var gameName = reader.GetAttribute("name");
                    if (string.IsNullOrEmpty(gameName))
                        continue;

                    var roms = new List<Dictionary<string, string>>();

                    // Read inner elements until end of game
                    while (reader.Read())
                    {
                        if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "game")
                            break;

                        if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "rom")
                        {
                            var rom = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            var romName = reader.GetAttribute("name");
                            if (romName is not null) rom["name"] = romName;

                            var hash = hashType.ToUpperInvariant() switch
                            {
                                "SHA256" => reader.GetAttribute("sha256"),
                                "MD5" => reader.GetAttribute("md5"),
                                "CRC" or "CRC32" => reader.GetAttribute("crc"),
                                _ => reader.GetAttribute("sha1") // SHA1 default
                            };
                            if (hash is not null) rom["hash"] = hash;

                            var size = reader.GetAttribute("size");
                            if (size is not null) rom["size"] = size;

                            roms.Add(rom);
                        }
                    }

                    if (games.TryGetValue(gameName, out var existing))
                        existing.AddRange(roms);
                    else
                        games[gameName] = roms;
                }
            }
        }
        catch (XmlException) when (settings.DtdProcessing == DtdProcessing.Prohibit)
        {
            // SEC-XML-01 fallback: real DATs with DOCTYPE → retry with Ignore
            _log?.Invoke($"[Info] DAT file '{datPath}' triggered DTD prohibition. Retrying with DtdProcessing.Ignore.");
            return ParseDatFileInternal(datPath, hashType, CreateFallbackXmlSettings());
        }
        catch (XmlException ex)
        {
            // Malformed DAT file — return partial results with warning
            _log?.Invoke($"[Warning] Malformed DAT file '{datPath}': {ex.Message}. Partial results returned.");
        }

        return games;
    }

    /// <summary>
    /// Creates XmlReaderSettings with XXE protection active.
    /// SEC-XML-01: DtdProcessing.Prohibit blocks all DTD declarations.
    /// XmlResolver=null prevents external resource resolution (SSRF protection).
    /// Callers should catch XmlException and retry with CreateFallbackXmlSettings
    /// for real DATs (No-Intro, Redump) that contain DOCTYPE declarations.
    /// </summary>
    private static XmlReaderSettings CreateSecureXmlSettings()
    {
        return new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true
        };
    }

    /// <summary>
    /// Fallback settings for DATs with DOCTYPE declarations.
    /// DtdProcessing.Ignore skips DTD without entity expansion.
    /// </summary>
    private static XmlReaderSettings CreateFallbackXmlSettings()
    {
        return new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true
        };
    }
}
