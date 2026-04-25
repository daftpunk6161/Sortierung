using System.Security.Cryptography;
using System.Text;
using System.Xml;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.FileSystem;

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
    private const long MaxSevenZipExtractedTotalBytes = 512 * 1024 * 1024;
    private const int MaxSevenZipEntryCount = 256;

    /// <summary>7z magic bytes: '7' 'z' 0xBC 0xAF 0x27 0x1C.</summary>
    private static readonly byte[] SevenZipMagic = { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C };
    private static readonly ToolRequirement SevenZipRequirement = new() { ToolName = "7z" };

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
                        var normalizedHash = hash.Trim();
                        if (normalizedHash.Length == 0)
                            continue;

                        rom.TryGetValue("name", out var romFileName);
                        var isBios = rom.TryGetValue("isbios", out var biosFlag)
                            ? IsTruthyFlag(biosFlag)
                            : IsLikelyBiosGameName(game.Key, romFileName);
                        var parentGameName = ResolveParentName(game.Key, parentMap);

                        var aliasHashes = CollectAliasHashes(rom, normalizedHash);
                        var primaryHashType = rom.TryGetValue("hashType", out var hashTypeValue)
                            ? hashTypeValue
                            : NormalizeHashType(hashType);
                        index.AddWithAliases(
                            consoleKey,
                            primaryHashType,
                            normalizedHash,
                            aliasHashes,
                            game.Key,
                            romFileName,
                            isBios,
                            parentGameName);
                    }
                }
            }
        }

        return index;
    }

    public string GetDatGameKey(string gameName, string console)
    {
        // Normalize: lowercase, trim whitespace
        return $"{console.Trim()}|{gameName.Trim()}".ToLowerInvariant();
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
        => ParseDatFileInternal(datPath, hashType, CreateSecureXmlSettings(), archiveDepth: 0);

    private Dictionary<string, List<Dictionary<string, string>>> ParseDatFileInternal(
        string datPath, string hashType, XmlReaderSettings settings, int archiveDepth)
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
            if (archiveDepth > 0)
            {
                _log?.Invoke($"[Warning] Nested 7z DAT '{datPath}' is not allowed. Skipped.");
                return games;
            }

            return TryParse7zDat(datPath, hashType, settings, archiveDepth);
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

                            var sha1 = NormalizeHashValue(reader.GetAttribute("sha1"));
                            var sha256 = NormalizeHashValue(reader.GetAttribute("sha256"));
                            var md5 = NormalizeHashValue(reader.GetAttribute("md5"));
                            var crc = NormalizeHashValue(reader.GetAttribute("crc"));

                            if (sha1 is not null) rom["sha1"] = sha1;
                            if (sha256 is not null) rom["sha256"] = sha256;
                            if (md5 is not null) rom["md5"] = md5;
                            if (crc is not null) rom["crc"] = crc;

                            var selectedHash = SelectHashByPreference(requestedHashType, sha1, sha256, md5, crc);
                            var selectedHashType = selectedHash.HashType;
                            var hash = selectedHash.Hash;

                            if (hash is not null)
                            {
                                var normalizedHash = NormalizeHashValue(hash);
                                if (!string.IsNullOrWhiteSpace(normalizedHash))
                                {
                                    rom["hash"] = normalizedHash;
                                    rom["hashType"] = selectedHashType;
                                    if (!fallbackWarningEmitted
                                        && !string.Equals(selectedHashType, requestedHashType, StringComparison.OrdinalIgnoreCase))
                                    {
                                        _log?.Invoke(
                                            $"[Warning] DAT '{datPath}' lacks requested hash type '{requestedHashType}' for one or more entries. Using '{selectedHashType}' fallback; verify HashType alignment.");
                                        fallbackWarningEmitted = true;
                                    }
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
            return ParseDatFileInternal(datPath, hashType, CreateFallbackXmlSettings(), archiveDepth);
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
            "CRC" => "CRC32",
            "MD5" => "MD5",
            "SHA256" => "SHA256",
            "SHA1" => "SHA1",
            _ => "SHA1"
        };
    }

    private static (string HashType, string? Hash) SelectHashByPreference(
        string requestedHashType,
        string? sha1,
        string? sha256,
        string? md5,
        string? crc)
    {
        foreach (var hashType in GetFallbackHashTypeOrder(requestedHashType))
        {
            var hash = hashType switch
            {
                "SHA256" => sha256,
                "MD5" => md5,
                "CRC32" => crc,
                _ => sha1
            };

            if (!string.IsNullOrWhiteSpace(hash))
                return (hashType, hash);
        }

        return (requestedHashType, null);
    }

    private static IEnumerable<string> GetFallbackHashTypeOrder(string requestedHashType)
    {
        var ordered = new List<string>(capacity: 4);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Add(requestedHashType);
        Add("SHA1");
        Add("MD5");
        Add("CRC32");
        Add("SHA256");

        return ordered;

        void Add(string hashType)
        {
            var normalized = NormalizeHashType(hashType);
            if (seen.Add(normalized))
                ordered.Add(normalized);
        }
    }

    private static string? NormalizeHashValue(string? hashValue)
    {
        if (string.IsNullOrWhiteSpace(hashValue))
            return null;

        var normalized = hashValue.Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static IReadOnlyCollection<(string HashType, string Hash)> CollectAliasHashes(
        IReadOnlyDictionary<string, string> rom,
        string primaryHash)
    {
        var aliases = new HashSet<(string HashType, string Hash)>();

        AddAlias("SHA1", "sha1");
        AddAlias("MD5", "md5");
        AddAlias("CRC32", "crc");
        AddAlias("SHA256", "sha256");

        return aliases;

        void AddAlias(string hashType, string key)
        {
            if (!rom.TryGetValue(key, out var hashValue) || string.IsNullOrWhiteSpace(hashValue))
                return;

            var normalized = hashValue.Trim();
            if (normalized.Length == 0)
                return;

            if (string.Equals(normalized, primaryHash, StringComparison.OrdinalIgnoreCase))
                return;

            aliases.Add((NormalizeHashType(hashType), normalized));
        }
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
        string archivePath,
        string hashType,
        XmlReaderSettings settings,
        int archiveDepth)
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

        var entries = ListSevenZipEntries(archivePath, sevenZipPath);
        if (!ValidateSevenZipEntryList(entries, out var totalDeclaredBytes, out var entryFailureReason))
        {
            _log?.Invoke($"[Warning] 7z DAT '{archivePath}' rejected before extraction: {entryFailureReason}.");
            return empty;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "romulus_dat7z_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);

        try
        {
            if (!HasAvailableTempSpace(tempDir, totalDeclaredBytes))
            {
                _log?.Invoke($"[Warning] 7z DAT '{archivePath}' rejected: not enough temporary disk space.");
                return empty;
            }

            var outArg = $"-o{tempDir}";
            var result = _toolRunner.InvokeProcess(
                sevenZipPath,
                new[] { "x", "-y", "-snl-", outArg, archivePath },
                SevenZipRequirement,
                "7z DAT extract",
                TimeSpan.FromMinutes(10),
                CancellationToken.None);
            if (!result.Success)
            {
                _log?.Invoke($"[Warning] Failed to decompress 7z DAT '{archivePath}': exit code {result.ExitCode}. Skipped.");
                return empty;
            }

            // Security: validate extracted paths stay within tempDir
            var normalizedTemp = Path.GetFullPath(tempDir).TrimEnd(Path.DirectorySeparatorChar)
                                 + Path.DirectorySeparatorChar;
            if (!ValidateExtractedTree(tempDir, normalizedTemp))
            {
                _log?.Invoke($"[Warning] 7z DAT '{archivePath}' produced unsafe extracted contents. Skipped.");
                return empty;
            }

            // Find the first .dat or .xml file inside the extracted contents
            var extractedFiles = new FileSystemAdapter().GetFilesSafe(tempDir, [".dat", ".xml"])
                .Where(f => Path.GetFullPath(f).StartsWith(normalizedTemp, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (extractedFiles.Count == 0)
            {
                _log?.Invoke($"[Warning] 7z DAT '{archivePath}' contains no .dat/.xml files after extraction. Skipped.");
                return empty;
            }

            if (extractedFiles.Count == 1)
            {
                var innerDatPath = extractedFiles[0];
                _log?.Invoke($"[Info] Decompressed 7z DAT '{Path.GetFileName(archivePath)}' -> parsing '{Path.GetFileName(innerDatPath)}'");
                return ParseDatFileInternal(innerDatPath, hashType, settings, archiveDepth + 1);
            }

            _log?.Invoke($"[Warning] 7z DAT '{archivePath}' contains {extractedFiles.Count} .dat/.xml files. Parsing and merging all entries deterministically.");
            var merged = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);
            foreach (var innerDatPath in extractedFiles)
            {
                _log?.Invoke($"[Info] Decompressed 7z DAT '{Path.GetFileName(archivePath)}' -> parsing '{Path.GetFileName(innerDatPath)}'");
                MergeParsedDat(merged, ParseDatFileInternal(innerDatPath, hashType, settings, archiveDepth + 1));
            }

            return merged;
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

    private static void MergeParsedDat(
        Dictionary<string, List<Dictionary<string, string>>> target,
        Dictionary<string, List<Dictionary<string, string>>> source)
    {
        foreach (var (gameName, roms) in source.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!target.TryGetValue(gameName, out var targetRoms))
            {
                targetRoms = new List<Dictionary<string, string>>();
                target[gameName] = targetRoms;
            }

            targetRoms.AddRange(roms);
        }
    }

    private List<SevenZipEntryInfo> ListSevenZipEntries(string archivePath, string sevenZipPath)
    {
        var result = _toolRunner!.InvokeProcess(
            sevenZipPath,
            new[] { "l", "-slt", archivePath },
            SevenZipRequirement,
            "7z DAT list",
            TimeSpan.FromMinutes(5),
            CancellationToken.None);
        if (!result.Success)
            return [];

        var entries = new List<SevenZipEntryInfo>();
        var archiveName = Path.GetFileName(archivePath);
        var pastSeparator = false;
        string? currentPath = null;
        long currentSize = 0;

        void FlushCurrent()
        {
            if (string.IsNullOrWhiteSpace(currentPath))
                return;

            if (!currentPath.Equals(archiveName, StringComparison.OrdinalIgnoreCase))
                entries.Add(new SevenZipEntryInfo(currentPath, currentSize));

            currentPath = null;
            currentSize = 0;
        }

        foreach (var line in (result.Output ?? string.Empty).Split('\n', StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("-----", StringComparison.Ordinal))
            {
                pastSeparator = true;
                continue;
            }

            if (!pastSeparator)
                continue;

            if (line.StartsWith("Path = ", StringComparison.OrdinalIgnoreCase))
            {
                FlushCurrent();
                currentPath = line["Path = ".Length..].Trim();
                currentSize = 0;
                continue;
            }

            if (currentPath is not null
                && line.StartsWith("Size = ", StringComparison.OrdinalIgnoreCase)
                && long.TryParse(line["Size = ".Length..].Trim(), out var parsedSize))
            {
                currentSize = parsedSize;
            }
        }

        FlushCurrent();
        entries.Sort(static (left, right) => string.Compare(left.Path, right.Path, StringComparison.OrdinalIgnoreCase));
        return entries;
    }

    private static bool ValidateSevenZipEntryList(
        IReadOnlyList<SevenZipEntryInfo> entries,
        out long totalDeclaredBytes,
        out string failureReason)
    {
        totalDeclaredBytes = 0;
        failureReason = string.Empty;

        if (entries.Count == 0)
        {
            failureReason = "archive-empty-or-unlisted";
            return false;
        }

        if (entries.Count > MaxSevenZipEntryCount)
        {
            failureReason = "archive-too-many-entries";
            return false;
        }

        foreach (var entry in entries)
        {
            if (!IsSafeArchiveEntryPath(entry.Path))
            {
                failureReason = "archive-path-traversal-detected";
                return false;
            }

            if (entry.Size < 0)
            {
                failureReason = "archive-entry-size-invalid";
                return false;
            }

            totalDeclaredBytes += entry.Size;
            if (totalDeclaredBytes > MaxSevenZipExtractedTotalBytes)
            {
                failureReason = "archive-extraction-size-exceeded";
                return false;
            }
        }

        return true;
    }

    private static bool IsSafeArchiveEntryPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (Path.IsPathRooted(path))
            return false;

        var parts = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return parts.All(static part => part is not "." and not "..");
    }

    private static bool ValidateExtractedTree(string tempDir, string normalizedTemp)
    {
        foreach (var dir in EnumerateDirectoriesWithoutFollowingReparsePoints(tempDir))
        {
            if (!Path.GetFullPath(dir).StartsWith(normalizedTemp, StringComparison.OrdinalIgnoreCase))
                return false;

            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
                return false;
        }

        foreach (var file in Directory.EnumerateFiles(tempDir, "*", SearchOption.AllDirectories))
        {
            if (!Path.GetFullPath(file).StartsWith(normalizedTemp, StringComparison.OrdinalIgnoreCase))
                return false;

            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                return false;
        }

        return true;
    }

    private static IEnumerable<string> EnumerateDirectoriesWithoutFollowingReparsePoints(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            string[] children;
            try
            {
                children = Directory.GetDirectories(current);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                yield break;
            }

            Array.Sort(children, StringComparer.OrdinalIgnoreCase);
            foreach (var child in children)
            {
                yield return child;
                FileAttributes attrs;
                try
                {
                    attrs = File.GetAttributes(child);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                if ((attrs & FileAttributes.ReparsePoint) == 0)
                    stack.Push(child);
            }
        }
    }

    private static bool HasAvailableTempSpace(string tempDir, long requiredBytes)
    {
        if (requiredBytes <= 0)
            return true;

        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(tempDir));
            if (string.IsNullOrWhiteSpace(root))
                return false;

            var drive = new DriveInfo(root);
            return drive.AvailableFreeSpace > requiredBytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private sealed record SevenZipEntryInfo(string Path, long Size);
}
