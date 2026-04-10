using System.Security.Cryptography;
using System.Text;
using System.Xml;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.Dat;

/// <summary>
/// DAT file index and hash operations.
/// Port of Dat.ps1 — XML-based DAT parsing with XXE protection,
/// parent/clone mapping, game key resolution.
/// </summary>
public sealed class DatRepositoryAdapter
{
    /// <summary>Maximum DAT file size to parse (100 MB).</summary>
    private const long MaxDatFileSizeBytes = 100 * 1024 * 1024;

    /// <summary>7z magic bytes: '7' 'z' 0xBC 0xAF 0x27 0x1C.</summary>
    private static readonly byte[] SevenZipMagic = { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C };

    private readonly Action<string>? _log;
    private readonly IToolRunner? _toolRunner;

    public DatRepositoryAdapter(Action<string>? log = null, IToolRunner? toolRunner = null)
    {
        _log = log;
        _toolRunner = toolRunner;
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

            var parentMap = GetDatParentCloneIndex(datPath);
            var games = ParseDatFile(datPath, hashType);
            foreach (var game in games)
            {
                // game.Key = gameName, game.Value = list of rom entries
                foreach (var rom in game.Value)
                {
                    if (rom.TryGetValue("hash", out var hash) && !string.IsNullOrWhiteSpace(hash))
                    {
                        rom.TryGetValue("name", out var romFileName);
                        var isBios = rom.TryGetValue("isbios", out var biosFlag)
                            ? IsTruthyFlag(biosFlag)
                            : IsLikelyBiosGameName(game.Key, romFileName);
                        var parentGameName = ResolveParentName(game.Key, parentMap);
                        index.Add(consoleKey, hash, game.Key, romFileName, isBios, parentGameName);
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* If we can't stat the file, let XmlReader handle it */ }

        var settings = CreateSecureXmlSettings();

        try
        {
            return ReadParentCloneIndex(datPath, settings);
        }
        catch (XmlException) when (settings.DtdProcessing == DtdProcessing.Prohibit)
        {
            _log?.Invoke($"[Info] DAT file '{datPath}' triggered DTD prohibition. Retrying with DtdProcessing.Ignore.");
            try
            {
                return ReadParentCloneIndex(datPath, CreateFallbackXmlSettings());
            }
            catch (XmlException ex)
            {
                _log?.Invoke($"[Warning] Could not read parent/clone map from DAT '{datPath}': {ex.Message}. Empty map returned.");
                return parentMap;
            }
        }
        catch (XmlException ex)
        {
            _log?.Invoke($"[Warning] Could not read parent/clone map from DAT '{datPath}': {ex.Message}. Empty map returned.");
            return parentMap;
        }
    }

    private static Dictionary<string, string> ReadParentCloneIndex(string datPath, XmlReaderSettings settings)
    {
        var parentMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var reader = XmlReader.Create(datPath, settings);
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && IsDatGameElement(reader.LocalName))
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
        var requestedHashType = NormalizeHashType(hashType);
        var fallbackWarningEmitted = false;

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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* If we can't stat the file, let XmlReader handle it */ }

        // Detect 7z-compressed DAT files and decompress transparently
        if (Is7zFile(datPath))
        {
            return TryParse7zDat(datPath, hashType, settings);
        }

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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* If we can't read, let XmlReader produce the proper error */ }

        try
        {
            using var reader = XmlReader.Create(datPath, settings);
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && IsDatGameElement(reader.LocalName))
                {
                    var gameName = reader.GetAttribute("name");
                    if (string.IsNullOrEmpty(gameName))
                        continue;

                    var gameElementName = reader.LocalName;
                    var gameIsBios = IsDatBiosOrDevice(reader) || IsLikelyBiosGameName(gameName, romFileName: null);

                    var roms = new List<Dictionary<string, string>>();

                    // Read inner elements until end of game
                    while (reader.Read())
                    {
                        if (reader.NodeType == XmlNodeType.EndElement &&
                            string.Equals(reader.LocalName, gameElementName, StringComparison.OrdinalIgnoreCase))
                            break;

                        if (reader.NodeType == XmlNodeType.Element && IsDatRomElement(reader.LocalName))
                        {
                            var rom = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            var romName = reader.GetAttribute("name");
                            if (romName is not null) rom["name"] = romName;

                            var selectedHashType = requestedHashType;
                            var hash = requestedHashType switch
                            {
                                "SHA256" => reader.GetAttribute("sha256"),
                                "MD5" => reader.GetAttribute("md5"),
                                "CRC" or "CRC32" => reader.GetAttribute("crc"),
                                _ => reader.GetAttribute("sha1") // SHA1 default
                            };

                            // Fallback chain: if the preferred hash is absent, try alternatives.
                            // Many DATs (MAME, FBNeo) only carry CRC32; No-Intro has all four.
                            if (hash is null && requestedHashType is not ("CRC" or "CRC32"))
                            {
                                hash = reader.GetAttribute("md5");
                                if (hash is not null)
                                    selectedHashType = "MD5";
                            }

                            if (hash is null && requestedHashType is not ("CRC" or "CRC32"))
                            {
                                hash = reader.GetAttribute("crc");
                                if (hash is not null)
                                    selectedHashType = "CRC";
                            }

                            if (hash is not null)
                            {
                                rom["hash"] = hash;
                                if (!fallbackWarningEmitted
                                    && !string.Equals(selectedHashType, requestedHashType, StringComparison.OrdinalIgnoreCase))
                                {
                                    _log?.Invoke(
                                        $"[Warning] DAT '{datPath}' lacks requested hash type '{requestedHashType}' for one or more entries. Using '{selectedHashType}' fallback; verify HashType alignment.");
                                    fallbackWarningEmitted = true;
                                }
                            }

                            var size = reader.GetAttribute("size");
                            if (size is not null) rom["size"] = size;
                            if (gameIsBios) rom["isbios"] = "true";

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

    private static string NormalizeHashType(string hashType)
    {
        if (string.IsNullOrWhiteSpace(hashType))
            return "SHA1";

        return hashType.Trim().ToUpperInvariant() switch
        {
            "CRC32" => "CRC32",
            "CRC" => "CRC",
            "MD5" => "MD5",
            "SHA256" => "SHA256",
            _ => "SHA1"
        };
    }

    private static bool IsDatGameElement(string localName)
    {
        return localName.Equals("game", StringComparison.OrdinalIgnoreCase)
            || localName.Equals("machine", StringComparison.OrdinalIgnoreCase)
            || localName.Equals("software", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDatRomElement(string localName)
    {
        return localName.Equals("rom", StringComparison.OrdinalIgnoreCase)
            || localName.Equals("disk", StringComparison.OrdinalIgnoreCase)
            || localName.Equals("chd", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLikelyBiosGameName(string gameName, string? romFileName)
    {
        if (ContainsBiosToken(gameName))
            return true;
        if (ContainsBiosToken(romFileName))
            return true;
        return false;
    }

    private static bool IsDatBiosOrDevice(XmlReader reader)
    {
        return IsTruthyFlag(reader.GetAttribute("isbios"))
            || IsTruthyFlag(reader.GetAttribute("isdevice"))
            || IsTruthyFlag(reader.GetAttribute("bios"));
    }

    private static bool IsTruthyFlag(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("1", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsBiosToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.Contains("bios", StringComparison.OrdinalIgnoreCase)
            || value.Contains("firmware", StringComparison.OrdinalIgnoreCase)
            || value.Contains("boot rom", StringComparison.OrdinalIgnoreCase)
            || value.Contains("bootrom", StringComparison.OrdinalIgnoreCase)
            || value.Contains("sysrom", StringComparison.OrdinalIgnoreCase)
            || value.Contains("ipl", StringComparison.OrdinalIgnoreCase);
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

    /// <summary>Detects 7z magic bytes at file start.</summary>
    private static bool Is7zFile(string filePath)
    {
        try
        {
            using var fs = File.OpenRead(filePath);
            var header = new byte[SevenZipMagic.Length];
            if (fs.Read(header, 0, header.Length) < header.Length)
                return false;
            return header.AsSpan().SequenceEqual(SevenZipMagic);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Decompresses a 7z-packed DAT file to a temp directory, locates the inner XML,
    /// and parses it. Requires an IToolRunner with 7z support.
    /// </summary>
    private Dictionary<string, List<Dictionary<string, string>>> TryParse7zDat(
        string archivePath, string hashType, XmlReaderSettings settings)
    {
        var empty = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);

        if (_toolRunner is null)
        {
            _log?.Invoke($"[Warning] DAT file '{archivePath}' is 7z-compressed but no ToolRunner available. Skipped.");
            return empty;
        }

        var sevenZipPath = _toolRunner.FindTool("7z");
        if (string.IsNullOrEmpty(sevenZipPath))
        {
            _log?.Invoke($"[Warning] DAT file '{archivePath}' is 7z-compressed but 7z tool not found. Skipped.");
            return empty;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "romulus_dat7z_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);

        try
        {
            var outArg = $"-o{tempDir}";
            var result = _toolRunner.InvokeProcess(sevenZipPath, new[] { "x", "-y", outArg, archivePath });
            if (!result.Success)
            {
                _log?.Invoke($"[Warning] Failed to decompress 7z DAT '{archivePath}': exit code {result.ExitCode}. Skipped.");
                return empty;
            }

            // Security: validate extracted paths stay within tempDir
            var normalizedTemp = Path.GetFullPath(tempDir).TrimEnd(Path.DirectorySeparatorChar)
                                 + Path.DirectorySeparatorChar;

            // Find the first .dat or .xml file inside the extracted contents
            var extractedFiles = Directory.GetFiles(tempDir, "*.*", SearchOption.AllDirectories)
                .Where(f =>
                {
                    var ext = Path.GetExtension(f);
                    return ext.Equals(".dat", StringComparison.OrdinalIgnoreCase)
                        || ext.Equals(".xml", StringComparison.OrdinalIgnoreCase);
                })
                .Where(f => Path.GetFullPath(f).StartsWith(normalizedTemp, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (extractedFiles.Count == 0)
            {
                _log?.Invoke($"[Warning] 7z DAT '{archivePath}' contains no .dat/.xml files after extraction. Skipped.");
                return empty;
            }

            var innerDatPath = extractedFiles[0];
            _log?.Invoke($"[Info] Decompressed 7z DAT '{Path.GetFileName(archivePath)}' → parsing '{Path.GetFileName(innerDatPath)}'");

            // Recursively parse the inner file (it should be plain XML)
            return ParseDatFileInternal(innerDatPath, hashType, settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.Invoke($"[Warning] Error decompressing 7z DAT '{archivePath}': {ex.Message}. Skipped.");
            return empty;
        }
        finally
        {
            if (Directory.Exists(tempDir))
                try { Directory.Delete(tempDir, true); }
                catch (IOException) { /* Best-effort cleanup */ }
                catch (UnauthorizedAccessException) { /* Permission denied on cleanup — non-fatal */ }
        }
    }
}
