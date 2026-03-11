using System.Text;
using System.Text.RegularExpressions;
using RomCleanup.Core.Caching;

namespace RomCleanup.Core.Classification;

/// <summary>
/// Detects console type by reading binary disc header signatures.
/// Port of Get-DiscHeaderConsole, Get-ChdDiscHeaderConsole, Resolve-ConsoleFromDiscText
/// from Classification.ps1.
/// Supports ISO/GCM/IMG/BIN (128 KB scan) and CHD (64 KB metadata scan).
/// Thread-safe — uses LruCache for result caching.
/// </summary>
public sealed class DiscHeaderDetector
{
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
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        if (_isoCache.TryGet(path, out var cached))
            return cached;

        string? result = null;
        try
        {
            result = ScanDiscImage(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        _isoCache.Set(path, result);
        return result;
    }

    /// <summary>
    /// Detect console from a CHD file by scanning metadata for platform strings.
    /// Returns console key or null.
    /// </summary>
    public string? DetectFromChd(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        if (_chdCache.TryGet(path, out var cached))
            return cached;

        string? result = null;
        try
        {
            result = ScanChdMetadata(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        _chdCache.Set(path, result);
        return result;
    }

    /// <summary>
    /// Batch detect console for multiple files. Dispatches by extension.
    /// Returns a dictionary of path → console key (null if unknown).
    /// </summary>
    public IReadOnlyDictionary<string, string?> DetectBatch(IEnumerable<string> paths)
    {
        var results = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            var ext = Path.GetExtension(path).ToLowerInvariant();
            results[path] = ext == ".chd"
                ? DetectFromChd(path)
                : ext is ".iso" or ".gcm" or ".img" or ".bin"
                    ? DetectFromDiscImage(path)
                    : null;
        }
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

        // Sega disc systems (IP.BIN / header strings)
        if (Regex.IsMatch(text, @"(?i)SEGA.SEGAKATANA|SEGA.?DREAMCAST|SEGA\s*KATANA|DREAMCAST"))
            return "DC";
        if (Regex.IsMatch(text, @"(?i)SEGA.SATURN|SEGASATURN|SEGA\s*SATURN"))
            return "SAT";
        if (Regex.IsMatch(text, @"(?i)SEGADISCSYSTEM|SEGA.MEGA.?CD|SEGA\s*CD"))
            return "SCD";
        // SNK Neo Geo CD
        if (Regex.IsMatch(text, @"(?i)NEOGEO\s*CD|NEO.?GEO"))
            return "NEOCD";
        // NEC PC-FX (before PC Engine to avoid substring overlap)
        if (Regex.IsMatch(text, @"(?i)PC-FX:Hu_CD|PC-FX|NEC.*PC-FX"))
            return "PCFX";
        // NEC PC Engine CD
        if (Regex.IsMatch(text, @"(?i)PC\s*Engine|NEC\s*HOME\s*ELECTRONICS|TURBOGRAFX"))
            return "PCECD";
        // Atari Jaguar CD
        if (Regex.IsMatch(text, @"(?i)ATARI\s*JAGUAR"))
            return "JAGCD";
        // Amiga CD32
        if (Regex.IsMatch(text, @"(?i)AMIGA\s*BOOT|CDTV|CD32"))
            return "CD32";
        // Fujitsu FM Towns
        if (Regex.IsMatch(text, @"(?i)FM\s*TOWNS"))
            return "FMTOWNS";
        // Sony PlayStation family
        if (Regex.IsMatch(text, @"(?i)Sony\s*Computer\s*Entertainment|PLAYSTATION"))
        {
            if (Regex.IsMatch(text, @"(?i)PSP\s*GAME"))
                return "PSP";
            if (Regex.IsMatch(text, @"(?i)BOOT2\s*=|cdrom0:"))
                return "PS2";
            if (Regex.IsMatch(text, @"(?i)playstation\s*2"))
                return "PS2";
            return "PS1";
        }
        // Microsoft Xbox
        if (Regex.IsMatch(text, @"(?i)MICROSOFT\*XBOX\*MEDIA"))
            return "XBOX";

        return null;
    }

    // --- Private scanning methods ---

    private static string? ScanDiscImage(string path)
    {
        using var fs = File.OpenRead(path);
        if (fs.Length < 32)
            return null;

        // Pre-check: read first 32 bytes for magic-number detection (GC/Wii/3DO)
        var preBuffer = new byte[32];
        if (fs.Read(preBuffer, 0, 32) < 32)
            return null;

        // GC magic at offset 0x1C: C2 33 9F 3D
        if (preBuffer[0x1C] == 0xC2 && preBuffer[0x1D] == 0x33 &&
            preBuffer[0x1E] == 0x9F && preBuffer[0x1F] == 0x3D)
            return "GC";

        // Wii magic at offset 0x18: 5D 1C 9E A3
        if (preBuffer[0x18] == 0x5D && preBuffer[0x19] == 0x1C &&
            preBuffer[0x1A] == 0x9E && preBuffer[0x1B] == 0xA3)
            return "WII";

        // 3DO: Opera filesystem — record type 0x01 + five 0x5A sync bytes
        if (preBuffer[0] == 0x01 && preBuffer[1] == 0x5A && preBuffer[2] == 0x5A &&
            preBuffer[3] == 0x5A && preBuffer[4] == 0x5A && preBuffer[5] == 0x5A)
            return "3DO";

        // No early match — read remaining bytes up to 128 KB for full scan
        int scanSize = (int)Math.Min(131072, fs.Length);
        var buffer = new byte[scanSize];
        Array.Copy(preBuffer, 0, buffer, 0, 32);
        if (scanSize > 32)
            fs.ReadAtLeast(buffer.AsSpan(32, scanSize - 32), scanSize - 32, throwOnEndOfStream: false);

        // Xbox / Xbox 360: XDVDFS signature "MICROSOFT*XBOX*MEDIA" at offset 0x10000
        if (scanSize >= 0x10000 + 20)
        {
            var xboxSig = Encoding.ASCII.GetString(buffer, 0x10000, 20);
            if (xboxSig == "MICROSOFT*XBOX*MEDIA")
                return "XBOX";
        }

        // Sega IP.BIN detection (DC / SAT / SCD)
        // Check at offset 0x0000 (2048-byte sector ISO) and 0x0010 (2352-byte raw sector)
        foreach (var dataOff in new[] { 0x0000, 0x0010 })
        {
            if (scanSize >= dataOff + 48)
            {
                var ipStr = ExtractPrintableAscii(buffer, dataOff, 48);
                if (Regex.IsMatch(ipStr, @"SEGA.SEGAKATANA|SEGA.DREAMCAST"))
                    return "DC";
                if (Regex.IsMatch(ipStr, @"SEGA.SATURN|SEGASATURN"))
                    return "SAT";
                if (Regex.IsMatch(ipStr, @"SEGADISCSYSTEM|SEGA.MEGA.CD"))
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

                    if (Regex.IsMatch(sysId, @"(?i)PLAYSTATION"))
                    {
                        // Scan remaining buffer for PS2/PSP distinguishing markers
                        int markerScanLen = Math.Min(scanSize - pvdOff, 65536);
                        var pvdText = Encoding.ASCII.GetString(buffer, pvdOff, markerScanLen);
                        if (Regex.IsMatch(pvdText, @"(?i)PSP\s*GAME"))
                            return "PSP";
                        if (Regex.IsMatch(pvdText, @"(?i)BOOT2\s*=|cdrom0:"))
                            return "PS2";
                        return "PS1";
                    }

                    // FM Towns: PVD system identifier contains "FM TOWNS"
                    if (Regex.IsMatch(sysId, @"(?i)FM.?TOWNS"))
                        return "FMTOWNS";
                }
            }
        }

        return null;
    }

    private static string? ScanChdMetadata(string path)
    {
        using var fs = File.OpenRead(path);
        int scanSize = (int)Math.Min(65536, fs.Length);
        var raw = new byte[scanSize];
        fs.ReadAtLeast(raw, scanSize, throwOnEndOfStream: false);

        // Verify CHD magic "MComprHD"
        if (scanSize < 8)
            return null;
        var magic = Encoding.ASCII.GetString(raw, 0, 8);
        if (magic != "MComprHD")
            return null;

        // Convert printable ASCII bytes to searchable string
        var meta = ExtractPrintableAscii(raw, 0, scanSize);
        return ResolveConsoleFromText(meta);
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
