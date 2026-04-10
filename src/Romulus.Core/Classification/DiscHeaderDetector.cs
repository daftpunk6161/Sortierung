using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using Romulus.Core.Caching;

namespace Romulus.Core.Classification;

/// <summary>
/// Detects console type by reading binary disc header signatures.
/// Port of Get-DiscHeaderConsole, Get-ChdDiscHeaderConsole, Resolve-ConsoleFromDiscText
/// from Classification.ps1.
/// Supports ISO/GCM/IMG/BIN (128 KB scan) and CHD (64 KB metadata scan).
/// Thread-safe — uses LruCache for result caching.
/// </summary>
public sealed class DiscHeaderDetector
{
    private static readonly RegexOptions RxOpts = RegexOptions.IgnoreCase | RegexOptions.Compiled;
    private static readonly TimeSpan RxTimeout = SafeRegex.ShortTimeout;

    // Pre-compiled patterns for ResolveConsoleFromText (TASK-001 ReDoS fix)
    private static readonly Regex RxDreamcast = new(@"SEGA.SEGAKATANA|SEGA.?DREAMCAST|SEGA\s*KATANA|DREAMCAST", RxOpts, RxTimeout);
    private static readonly Regex RxSaturn = new(@"SEGA.SATURN|SEGASATURN|SEGA\s*SATURN", RxOpts, RxTimeout);
    private static readonly Regex RxSegaCd = new(@"SEGADISCSYSTEM|SEGA.MEGA.?CD|SEGA\s*CD", RxOpts, RxTimeout);
    private static readonly Regex RxNeoGeoCd = new(@"NEOGEO\s*CD|NEO[\s-]GEO", RxOpts, RxTimeout);
    private static readonly Regex RxPcFx = new(@"PC-FX:Hu_CD|PC-FX|NEC.*PC-FX", RxOpts, RxTimeout);
    private static readonly Regex RxPcEngine = new(@"PC\s*Engine|NEC\s*HOME\s*ELECTRONICS|TURBOGRAFX", RxOpts, RxTimeout);
    private static readonly Regex RxJaguar = new(@"ATARI\s*JAGUAR", RxOpts, RxTimeout);
    private static readonly Regex RxCdtv = new(@"CDTV", RxOpts, RxTimeout);
    private static readonly Regex RxCd32 = new(@"AMIGA\s*BOOT|CD32", RxOpts, RxTimeout);
    private static readonly Regex RxCdi = new(@"CD-RTOS|CD-I\s*READY|PHILIPS\s*CD-I", RxOpts, RxTimeout);
    private static readonly Regex RxFmTowns = new(@"FM\s*TOWNS", RxOpts, RxTimeout);
    private static readonly Regex RxPlayStation = new(@"Sony\s*Computer\s*Entertainment|PLAYSTATION", RxOpts, RxTimeout);
    private static readonly Regex RxPsp = new(@"PSP\s*GAME", RxOpts, RxTimeout);
    private static readonly Regex RxPs2Boot = new(@"BOOT2\s*=|cdrom0:", RxOpts, RxTimeout);
    private static readonly Regex RxPs2Name = new(@"playstation\s*2", RxOpts, RxTimeout);
    private static readonly Regex RxPs3Marker = new(@"PS3_DISC\.SFB|PS3_GAME|PLAYSTATION\s*3|PS3VOLUME", RxOpts, RxTimeout);
    private static readonly Regex RxXbox = new(@"MICROSOFT\*XBOX\*MEDIA", RxOpts, RxTimeout);
    private static readonly Regex RxXbox360Marker = new(@"XBOX\s*360|XEX2|XGD2|XGD3", RxOpts, RxTimeout);

    // ScanDiscImage patterns
    private static readonly Regex RxIpDreamcast = new(@"SEGA.SEGAKATANA|SEGA.DREAMCAST", RxOpts, RxTimeout);
    private static readonly Regex RxIpSaturn = new(@"SEGA.SATURN|SEGASATURN", RxOpts, RxTimeout);
    private static readonly Regex RxIpSegaCd = new(@"SEGADISCSYSTEM|SEGA.MEGA.CD", RxOpts, RxTimeout);
    private static readonly Regex RxPvdPlayStation = new(@"PLAYSTATION", RxOpts, RxTimeout);
    private static readonly Regex RxPvdFmTowns = new(@"FM.?TOWNS", RxOpts, RxTimeout);

    private readonly LruCache<string, string?> _isoCache;
    private readonly LruCache<string, string?> _chdCache;

    public DiscHeaderDetector(int isoCacheSize = 4096, int chdCacheSize = 2048)
    {
        _isoCache = new LruCache<string, string?>(isoCacheSize, StringComparer.OrdinalIgnoreCase);
        _chdCache = new LruCache<string, string?>(chdCacheSize, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Detect console from a disc image file (.iso/.gcm/.img/.bin) by reading up to 128 KB.
    /// Returns console key (e.g. "GC", "PS1", "DC") or null if unknown.
    /// </summary>
    public string? DetectFromDiscImage(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !ClassificationIo.FileExists(path))
            return null;

        var normalizedPath = Path.GetFullPath(path);
        if (_isoCache.TryGet(normalizedPath, out var cached))
            return cached;

        string? result = null;
        try
        {
            result = ScanDiscImage(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        _isoCache.Set(normalizedPath, result);
        return result;
    }

    /// <summary>
    /// Detect console from a CHD file by scanning metadata for platform strings.
    /// Returns console key or null.
    /// </summary>
    public string? DetectFromChd(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !ClassificationIo.FileExists(path))
            return null;

        var normalizedPath = Path.GetFullPath(path);
        if (_chdCache.TryGet(normalizedPath, out var cached))
            return cached;

        string? result = null;
        try
        {
            result = ScanChdMetadata(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        _chdCache.Set(normalizedPath, result);
        return result;
    }

    /// <summary>
    /// Batch detect console for multiple files. Dispatches by extension.
    /// Returns a dictionary of path → console key (null if unknown).
    /// </summary>
    public IReadOnlyDictionary<string, string?> DetectBatch(IEnumerable<string> paths, IProgress<int>? progress = null)
    {
        var results = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        int processed = 0;
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            // Skip reparse points (symlinks/junctions) — security rule
            try
            {
                var attrs = ClassificationIo.GetAttributes(path);
                if ((attrs & FileAttributes.ReparsePoint) != 0)
                {
                    results[path] = null;
                    processed++;
                    continue;
                }
            }
            catch (IOException)
            {
                results[path] = null;
                processed++;
                continue;
            }

            var ext = Path.GetExtension(path).ToLowerInvariant();
            results[path] = ext == ".chd"
                ? DetectFromChd(path)
                : ext is ".iso" or ".gcm" or ".img" or ".bin"
                    ? DetectFromDiscImage(path)
                    : null;

            processed++;
            if (processed % 50 == 0)
                progress?.Report(processed);
        }
        progress?.Report(processed);
        return results;
    }

    /// <summary>
    /// Resolve console from printable ASCII text extracted from disc data.
    /// Port of Resolve-ConsoleFromDiscText.
    /// </summary>
    public static string? ResolveConsoleFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        try
        {
            // Sega disc systems (IP.BIN / header strings)
            if (RxDreamcast.IsMatch(text)) return "DC";
            if (RxSaturn.IsMatch(text)) return "SAT";
            if (RxSegaCd.IsMatch(text)) return "SCD";
            // SNK Neo Geo CD
            if (RxNeoGeoCd.IsMatch(text)) return "NEOCD";
            // NEC PC-FX (before PC Engine to avoid substring overlap)
            if (RxPcFx.IsMatch(text)) return "PCFX";
            // NEC PC Engine CD
            if (RxPcEngine.IsMatch(text)) return "PCECD";
            // Atari Jaguar CD
            if (RxJaguar.IsMatch(text)) return "JAGCD";
            // Commodore CDTV (before CD32 to avoid substring overlap)
            if (RxCdtv.IsMatch(text)) return "CDTV";
            // Amiga CD32
            if (RxCd32.IsMatch(text)) return "CD32";
            // Philips CD-i
            if (RxCdi.IsMatch(text)) return "CDI";
            // Fujitsu FM Towns
            if (RxFmTowns.IsMatch(text)) return "FMTOWNS";
            // Sony PlayStation family
            if (RxPlayStation.IsMatch(text))
            {
                if (RxPsp.IsMatch(text)) return "PSP";
                if (RxPs2Boot.IsMatch(text)) return "PS2";
                if (RxPs2Name.IsMatch(text)) return "PS2";
                if (RxPs3Marker.IsMatch(text)) return "PS3";
                return "PS1";
            }
            // Microsoft Xbox
            if (RxXbox.IsMatch(text)) return "XBOX";
        }
        catch (RegexMatchTimeoutException) { }

        return null;
    }

    // --- Private scanning methods ---

    private static string? ScanDiscImage(string path)
    {
        using var fs = ClassificationIo.OpenRead(path);
        if (fs.Length < 32)
            return null;

        // Pre-check: read first 80 bytes for magic-number detection (GC/Wii/3DO)
        var preBuffer = new byte[80];
        int preRead = fs.Read(preBuffer, 0, preBuffer.Length);
        if (preRead < 32)
            return null;

        // GC magic at offset 0x1C: C2 33 9F 3D
        if (preBuffer[0x1C] == 0xC2 && preBuffer[0x1D] == 0x33 &&
            preBuffer[0x1E] == 0x9F && preBuffer[0x1F] == 0x3D)
            return "GC";

        // Wii magic at offset 0x18: 5D 1C 9E A3
        if (preBuffer[0x18] == 0x5D && preBuffer[0x19] == 0x1C &&
            preBuffer[0x1A] == 0x9E && preBuffer[0x1B] == 0xA3)
            return "WII";

        // Wii U magic at offset 0x18: same as Wii but combined with unique disc type
        // Wii U disc identifier: bytes 0x00-0x03 contain the game ID, byte 0x0F = 0x01 for Wii U
        // More reliable: presence of "WUP-" prefix pattern at offset 0x00-0x03
        if (preRead >= 0x20 &&
            preBuffer[0x00] == 0x57 && preBuffer[0x01] == 0x55 &&
            preBuffer[0x02] == 0x50 && preBuffer[0x03] == 0x2D)
            return "WIIU";

        // 3DO: Opera filesystem — record type 0x01 + five 0x5A sync bytes
        // Additional check: offset 0x28 must be ASCII "CD-ROM" or offset 0x40 must contain "opera" label area
        if (preBuffer[0] == 0x01 && preBuffer[1] == 0x5A && preBuffer[2] == 0x5A &&
            preBuffer[3] == 0x5A && preBuffer[4] == 0x5A && preBuffer[5] == 0x5A &&
            preBuffer.Length >= 0x50)
        {
            // Verify Opera FS volume structure: byte at offset 6 should be record version (0x01)
            // and bytes 0x06-0x07 are typically 0x01 0x00 for standard Opera volumes
            if (preBuffer[6] == 0x01 || preBuffer[6] == 0x02)
                return "3DO";
        }

        // No early match — read remaining bytes up to 128 KB for full scan
        int scanSize = (int)Math.Min(131072, fs.Length);
        var buffer = new byte[scanSize];
        int preLen = Math.Min(preRead, scanSize);
        Array.Copy(preBuffer, 0, buffer, 0, preLen);
        if (scanSize > preLen)
        {
            int bytesRead = fs.ReadAtLeast(buffer.AsSpan(preLen, scanSize - preLen), scanSize - preLen, throwOnEndOfStream: false);
            scanSize = preLen + bytesRead;
        }

        // Xbox / Xbox 360: XDVDFS signature "MICROSOFT*XBOX*MEDIA" at offset 0x10000
        if (scanSize >= 0x10000 + 20)
        {
            var xboxSig = Encoding.ASCII.GetString(buffer, 0x10000, 20);
            if (xboxSig == "MICROSOFT*XBOX*MEDIA")
            {
                // CORE-03 FIX: Limit Xbox 360 marker scan to the header area (first 8 KB)
                // to avoid false positives from "XBOX 360" text appearing in game data further
                // in the 128 KB buffer. Binary markers (XEX2/XGD2/XGD3) are also checked.
                int headerProbeLen = Math.Min(8192, scanSize);
                var headerProbeText = ExtractPrintableAscii(buffer, 0, headerProbeLen);
                if (RxXbox360Marker.IsMatch(headerProbeText))
                    return "X360";
                return "XBOX";
            }
        }

        // Sega IP.BIN detection (DC / SAT / SCD)
        // Check at offset 0x0000 (2048-byte sector ISO) and 0x0010 (2352-byte raw sector)
        foreach (var dataOff in new[] { 0x0000, 0x0010 })
        {
            if (scanSize >= dataOff + 48)
            {
                var ipStr = ExtractPrintableAscii(buffer, dataOff, 48);
                if (RxIpDreamcast.IsMatch(ipStr))
                    return "DC";
                if (RxIpSaturn.IsMatch(ipStr))
                    return "SAT";
                if (RxIpSegaCd.IsMatch(ipStr))
                    return "SCD";
            }
        }

        // Boot-sector keyword scan for remaining disc-based platforms (first 8 KB)
        int bootScanLen = Math.Min(8192, scanSize);
        if (bootScanLen > 0)
        {
            var bootText = ExtractPrintableAscii(buffer, 0, bootScanLen);
            var bootResult = ResolveConsoleFromText(bootText);
            if (bootResult is not null)
                return bootResult;
        }

        // PS1/PS2/PSP via ISO9660 Primary Volume Descriptor
        // PVD at different offsets depending on sector size:
        //   2048 bytes/sector: sector 16 → offset 0x8000
        //   2352 bytes/sector Mode 1:     sector 16 → 16*2352 + 16 = 0x9310
        //   2352 bytes/sector Mode 2/XA:  sector 16 → 16*2352 + 24 = 0x9318
        foreach (var pvdOff in new[] { 0x8000, 0x9310, 0x9318 })
        {
            if (scanSize >= pvdOff + 0x28)
            {
                // PVD magic: type byte 0x01 + "CD001"
                if (buffer[pvdOff] == 0x01 &&
                    buffer[pvdOff + 1] == 0x43 &&  // C
                    buffer[pvdOff + 2] == 0x44 &&  // D
                    buffer[pvdOff + 3] == 0x30 &&  // 0
                    buffer[pvdOff + 4] == 0x30 &&  // 0
                    buffer[pvdOff + 5] == 0x31)    // 1
                {
                    // System Identifier at PVD+8 (32 bytes field)
                    int sysIdLen = Math.Min(32, scanSize - (pvdOff + 8));
                    var sysId = Encoding.ASCII.GetString(buffer, pvdOff + 8, sysIdLen).Trim();

                    if (RxPvdPlayStation.IsMatch(sysId))
                    {
                        // Scan remaining buffer for PS2/PSP/PS3 distinguishing markers
                        int markerScanLen = Math.Min(scanSize - pvdOff, 65536);
                        var pvdText = Encoding.ASCII.GetString(buffer, pvdOff, markerScanLen);
                        if (RxPsp.IsMatch(pvdText))
                            return "PSP";
                        if (RxPs2Boot.IsMatch(pvdText))
                            return "PS2";
                        if (RxPs3Marker.IsMatch(pvdText))
                            return "PS3";
                        return "PS1";
                    }

                    // FM Towns: PVD system identifier contains "FM TOWNS"
                    if (RxPvdFmTowns.IsMatch(sysId))
                        return "FMTOWNS";

                    // Philips CD-i: PVD system identifier contains "CD-RTOS"
                    if (RxCdi.IsMatch(sysId))
                        return "CDI";
                }
            }
        }

        return null;
    }

    private static string? ScanChdMetadata(string path)
    {
        using var fs = ClassificationIo.OpenRead(path);
        if (fs.Length < 8)
            return null;

        var headerSize = (int)Math.Min(0x80, fs.Length);
        var header = new byte[headerSize];
        var headerRead = fs.ReadAtLeast(header, headerSize, throwOnEndOfStream: false);

        // Verify CHD magic "MComprHD"
        if (headerRead < 8)
            return null;
        var magic = Encoding.ASCII.GetString(header, 0, 8);
        if (magic != "MComprHD")
            return null;

        // CHD v5: parse metadata tags first, then fall back to text scan.
        if (headerRead >= 0x38)
        {
            var version = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(12, 4));
            if (version == 5)
            {
                var metaOffset = BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(0x30, 8));

                // Structured metadata tag scan — definitively identifies GD-ROM (Dreamcast).
                var tagResult = ScanChdMetaTags(fs, metaOffset);
                if (tagResult is not null)
                    return tagResult;

                // Fallback: raw text scan over the metadata area.
                if (metaOffset > 0 && metaOffset < (ulong)fs.Length)
                {
                    fs.Position = (long)metaOffset;
                    var metaScanSize = (int)Math.Min(131072, fs.Length - fs.Position);
                    if (metaScanSize > 0)
                    {
                        var metaBuffer = new byte[metaScanSize];
                        var metaRead = fs.ReadAtLeast(metaBuffer, metaScanSize, throwOnEndOfStream: false);
                        if (metaRead > 0)
                        {
                            var metaText = ExtractPrintableAscii(metaBuffer, 0, metaRead);
                            var fromMeta = ResolveConsoleFromText(metaText);
                            if (fromMeta is not null)
                                return fromMeta;
                        }
                    }
                }
            }
        }

        // Fallback for older/atypical files: scan first chunk for printable metadata.
        fs.Position = 0;
        int scanSize = (int)Math.Min(65536, fs.Length);
        var raw = new byte[scanSize];
        scanSize = fs.ReadAtLeast(raw, scanSize, throwOnEndOfStream: false);

        // Convert printable ASCII bytes to searchable string
        var meta = ExtractPrintableAscii(raw, 0, scanSize);
        return ResolveConsoleFromText(meta);
    }

    /// <summary>
    /// Parse CHD v5 metadata entry tags to identify the disc system.
    /// CHD metadata is a linked list: 4-byte tag | 4-byte flags+length | 8-byte next offset | data.
    /// CHGD (0x43484744) = GD-ROM → definitively Dreamcast.
    /// CHCD/CHT2 = standard CD-ROM (ambiguous, not enough to identify system alone).
    /// </summary>
    private static string? ScanChdMetaTags(Stream fs, ulong metaOffset)
    {
        if (metaOffset == 0 || metaOffset >= (ulong)fs.Length)
            return null;

        var entryHeader = new byte[16];
        var currentOffset = metaOffset;
        int maxEntries = 200; // Safety limit against malformed linked lists.

        while (currentOffset > 0 && currentOffset < (ulong)fs.Length && maxEntries-- > 0)
        {
            fs.Position = (long)currentOffset;
            var read = fs.ReadAtLeast(entryHeader, 16, throwOnEndOfStream: false);
            if (read < 16)
                break;

            var tag = BinaryPrimitives.ReadUInt32BigEndian(entryHeader.AsSpan(0, 4));
            var nextOffset = BinaryPrimitives.ReadUInt64BigEndian(entryHeader.AsSpan(8, 8));

            // CHGD (0x43484744) = GD-ROM track metadata → definitively Dreamcast/Naomi.
            if (tag == 0x43484744)
                return "DC";

            currentOffset = nextOffset;
        }

        return null;
    }

    /// <summary>
    /// Extract printable ASCII from a byte buffer, replacing non-printable bytes with spaces.
    /// </summary>
    private static string ExtractPrintableAscii(byte[] buffer, int offset, int length)
    {
        var chars = new char[length];
        for (int i = 0; i < length; i++)
        {
            byte b = buffer[offset + i];
            chars[i] = b >= 32 && b <= 126 ? (char)b : ' ';
        }
        return new string(chars);
    }
}
