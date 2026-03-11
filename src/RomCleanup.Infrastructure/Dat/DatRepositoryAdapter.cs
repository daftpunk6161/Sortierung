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
                        index.Add(consoleKey, hash, game.Key);
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

        var settings = CreateSecureXmlSettings();

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

        return string.Equals(current, gameName, StringComparison.OrdinalIgnoreCase) ? null : current;
    }

    private Dictionary<string, List<Dictionary<string, string>>> ParseDatFile(string datPath, string hashType)
    {
        var games = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);
        var settings = CreateSecureXmlSettings();

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

                    games[gameName] = roms;
                }
            }
        }
        catch (XmlException)
        {
            // Malformed DAT file — return what we have
        }

        return games;
    }

    /// <summary>
    /// Creates XmlReaderSettings with XXE protection active.
    /// DtdProcessing.Ignore skips DTD declarations (no entity expansion).
    /// XmlResolver=null prevents external resource resolution (SSRF protection).
    /// Uses Ignore instead of Prohibit because real DATs (No-Intro, Redump) contain DOCTYPE declarations.
    /// </summary>
    private static XmlReaderSettings CreateSecureXmlSettings()
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
