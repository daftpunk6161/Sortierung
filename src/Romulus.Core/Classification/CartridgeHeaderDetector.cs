using Romulus.Core.Caching;

namespace Romulus.Core.Classification;

/// <summary>
/// Detects console type by reading binary cartridge ROM header signatures.
/// Supports: NES (iNES/NES 2.0), SNES (internal header), Genesis/Mega Drive,
/// N64, Game Boy, Game Boy Color, Game Boy Advance.
/// Thread-safe — uses LruCache for result caching.
/// </summary>
public sealed class CartridgeHeaderDetector
{
    // iNES magic: "NES\x1A" at offset 0
    private static ReadOnlySpan<byte> INesMagic => [0x4E, 0x45, 0x53, 0x1A];

    // Genesis: "SEGA MEGA DRIVE" or "SEGA GENESIS" at offset 0x100
    private static ReadOnlySpan<byte> GenesisMagic1 => "SEGA MEGA DRIVE"u8;
    private static ReadOnlySpan<byte> GenesisMagic2 => "SEGA GENESIS"u8;
    private static ReadOnlySpan<byte> Sega32XMagic => "SEGA 32X"u8;

    // N64: Magic bytes at offset 0 (big-endian: 80 37 12 40, byte-swapped: 37 80 40 12, little-endian: 40 12 37 80)
    private static ReadOnlySpan<byte> N64MagicBE => [0x80, 0x37, 0x12, 0x40];
    private static ReadOnlySpan<byte> N64MagicBS => [0x37, 0x80, 0x40, 0x12];
    private static ReadOnlySpan<byte> N64MagicLE => [0x40, 0x12, 0x37, 0x80];

    // GBA: Nintendo logo at offset 0x04 (first 4 bytes of the 156-byte logo)
    private static ReadOnlySpan<byte> GbaLogoStart => [0x24, 0xFF, 0xAE, 0x51];

    // GB/GBC: Nintendo logo at offset 0x104 (first 4 bytes of the 48-byte logo)
    private static ReadOnlySpan<byte> GbLogoStart => [0xCE, 0xED, 0x66, 0x66];

    // Atari 7800: "ATARI7800" at offset 1
    private static ReadOnlySpan<byte> Atari7800Magic => "ATARI7800"u8;

    // Atari Lynx: "LYNX" at offset 0
    private static ReadOnlySpan<byte> LynxMagic => [0x4C, 0x59, 0x4E, 0x58];

    private readonly LruCache<string, string?> _cache;

    public CartridgeHeaderDetector(int cacheSize = 8192)
    {
        _cache = new LruCache<string, string?>(cacheSize, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Detect console from a cartridge ROM file by reading header signatures.
    /// Returns console key (e.g. "NES", "SNES", "MD", "N64", "GBA", "GB") or null if unknown.
    /// Only call for non-disc, non-archive files.
    /// </summary>
    public string? Detect(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !ClassificationIo.FileExists(path))
            return null;

        var normalizedPath = Path.GetFullPath(path);
        if (_cache.TryGet(normalizedPath, out var cached))
            return cached;

        string? result = null;
        try
        {
            result = ScanHeader(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        _cache.Set(normalizedPath, result);
        return result;
    }

    private static string? ScanHeader(string path)
    {
        // Read first 512 bytes — enough for all cartridge header checks
        Span<byte> buffer = stackalloc byte[512];
        int bytesRead;

        using (var fs = ClassificationIo.OpenRead(path))
        {
            bytesRead = fs.Read(buffer);
        }

        if (bytesRead < 4)
            return null;

        var data = buffer[..bytesRead];

        // NES: iNES header "NES\x1A" at offset 0
        if (bytesRead >= 4 && data[..4].SequenceEqual(INesMagic))
            return "NES";

        // N64: Magic at offset 0 (three byte-order variants)
        if (bytesRead >= 4)
        {
            if (data[..4].SequenceEqual(N64MagicBE) ||
                data[..4].SequenceEqual(N64MagicBS) ||
                data[..4].SequenceEqual(N64MagicLE))
                return "N64";
        }

        // Atari Lynx: "LYNX" at offset 0
        if (bytesRead >= 4 && data[..4].SequenceEqual(LynxMagic))
            return "LYNX";

        // GBA: Nintendo logo at offset 0x04
        if (bytesRead >= 8 && data.Slice(0x04, 4).SequenceEqual(GbaLogoStart))
            return "GBA";

        // Atari 7800: "ATARI7800" at offset 1
        if (bytesRead >= 10 && data.Slice(1, 9).SequenceEqual(Atari7800Magic))
            return "7800";

        // Genesis/Mega Drive: "SEGA MEGA DRIVE" or "SEGA GENESIS" at offset 0x100
        if (bytesRead >= 0x110)
        {
            var seg100 = data.Slice(0x100, 16);
            if (seg100[..GenesisMagic1.Length].SequenceEqual(GenesisMagic1))
                return "MD";
            if (seg100[..GenesisMagic2.Length].SequenceEqual(GenesisMagic2))
                return "MD";
            // 32X uses the same header area
            if (seg100[..Sega32XMagic.Length].SequenceEqual(Sega32XMagic))
                return "32X";
        }

        // GB/GBC: Nintendo logo at offset 0x104
        if (bytesRead >= 0x108)
        {
            if (data.Slice(0x104, 4).SequenceEqual(GbLogoStart))
            {
                // GBC flag at 0x143: 0x80 = dual (GB+GBC), 0xC0 = GBC only
                if (bytesRead > 0x143)
                {
                    var cgbFlag = data[0x143];
                    return cgbFlag is 0x80 or 0xC0 ? "GBC" : "GB";
                }
                return "GB";
            }
        }

        // SNES: Internal header at 0x7FC0 (LoROM) or 0xFFC0 (HiROM)
        // We can't read that far with 512 bytes — need a second read for SNES
        return ScanSnesHeader(path);
    }

    private static string? ScanSnesHeader(string path)
    {
        // SNES ROMs can be 256KB–6MB; internal header at 0x7FC0 (LoROM) or 0xFFC0 (HiROM)
        // Some ROMs have a 512-byte copier header, shifting offsets by 512
        try
        {
            var fileSize = ClassificationIo.FileLength(path);
            if (fileSize < 0x8000) // Too small for SNES
                return null;

            using var fs = ClassificationIo.OpenRead(path);
            Span<byte> titleBuf = stackalloc byte[21];
            Span<byte> checksumBuf = stackalloc byte[4];
            Span<byte> mapModeBuf = stackalloc byte[1];

            // Classic copier headers are 512-byte prefixes.
            // Only probe header-shifted offsets when that layout is plausible.
            var hasCopierHeader = (fileSize % 1024) == 512;

            // Check standard offsets (with and without 512-byte copier header)
            int[] offsets = fileSize > 0x10000
                ? (hasCopierHeader
                    ? [0x7FC0, 0xFFC0, 0x7FC0 + 512, 0xFFC0 + 512]
                    : [0x7FC0, 0xFFC0])
                : (hasCopierHeader
                    ? [0x7FC0, 0x7FC0 + 512]
                    : [0x7FC0]);

            foreach (var offset in offsets)
            {
                if (offset + 32 > fileSize)
                    continue;

                fs.Seek(offset, SeekOrigin.Begin);
                if (fs.Read(titleBuf) < 21)
                    continue;

                // SNES internal header: 21-byte title at offset, followed by map mode + ROM type
                // Valid title bytes are ASCII printable (0x20-0x7E) or Japanese characters (0x80+)
                if (IsSnesTitle(titleBuf))
                {
                    fs.Seek(offset + 21, SeekOrigin.Begin);
                    if (fs.Read(mapModeBuf) != 1 || !IsLikelySnesMapMode(mapModeBuf[0]))
                        continue;

                    // Verify checksum complement at offset+28..31
                    fs.Seek(offset + 28, SeekOrigin.Begin);
                    if (fs.Read(checksumBuf) == 4)
                    {
                        int complement = checksumBuf[0] | (checksumBuf[1] << 8);
                        int checksum = checksumBuf[2] | (checksumBuf[3] << 8);
                        if ((complement ^ checksum) == 0xFFFF)
                            return "SNES";
                    }
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return null;
    }

    private static bool IsLikelySnesMapMode(byte mapMode)
    {
        // Common SNES map mode values observed in No-Intro/GoodTools sets.
        return mapMode is 0x20 or 0x21 or 0x22 or 0x23 or 0x25 or 0x30 or 0x31 or 0x32 or 0x35 or 0x3A;
    }

    private static bool IsSnesTitle(ReadOnlySpan<byte> title)
    {
        int validChars = 0;
        foreach (var b in title)
        {
            // Valid: ASCII printable (0x20-0x7E), Japanese upper (0x80+), or padding (0x00)
            if (b is >= 0x20 and <= 0x7E or >= 0x80 or 0x00)
                validChars++;
        }
        // At least 15 of 21 characters should be valid, and not all zeros
        return validChars >= 15 && title.IndexOfAnyExcept((byte)0x00) >= 0;
    }
}
