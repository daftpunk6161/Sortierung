namespace Romulus.Core.Classification;

/// <summary>
/// Maps console keys to their ROM header skip bytes for headerless hashing.
/// No-Intro DATs hash NES/SNES/Atari 7800/Atari Lynx ROMs WITHOUT headers.
/// </summary>
public static class HeaderSizeMap
{
    private const int DefaultProbeByteCount = 512;
    private const int SnesCopierHeaderBytes = 512;
    private const int SnesLoRomHeaderOffset = 0x7FC0;
    private const int SnesHiRomHeaderOffset = 0xFFC0;
    private const int SnesInternalHeaderLength = 0x20;

    private static readonly IReadOnlyDictionary<string, int> FixedHeaders =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["NES"] = 16,      // iNES header
            ["ATARI7800"] = 128, // A7800 header
            ["ATARILYNX"] = 64   // Lynx header
        };

    /// <summary>
    /// Returns the number of header bytes to skip for headerless hashing.
    /// SNES uses conditional detection: the 512-byte copier header is only skipped
    /// when the shifted internal ROM header is plausible.
    /// Returns 0 when no header skipping is needed.
    /// </summary>
    public static int GetSkipBytes(string consoleKey, ReadOnlySpan<byte> header, long fileSize)
    {
        if (string.IsNullOrWhiteSpace(consoleKey))
            return 0;

        if (consoleKey.Equals("SNES", StringComparison.OrdinalIgnoreCase))
        {
            if (fileSize <= SnesCopierHeaderBytes || fileSize % 1024 != SnesCopierHeaderBytes)
                return 0;

            return HasLikelySnesInternalHeader(header, SnesCopierHeaderBytes)
                ? SnesCopierHeaderBytes
                : 0;
        }

        if (FixedHeaders.TryGetValue(consoleKey, out var skipBytes))
        {
            // Only skip if header magic is actually present
            if (!HasExpectedMagic(consoleKey, header))
                return 0;

            // Ensure file is large enough to have content beyond header
            if (fileSize <= skipBytes)
                return 0;

            return skipBytes;
        }

        return 0;
    }

    /// <summary>
    /// Returns the bytes needed to make a deterministic skip decision.
    /// </summary>
    public static int GetProbeByteCount(string consoleKey)
        => !string.IsNullOrWhiteSpace(consoleKey)
           && consoleKey.Equals("SNES", StringComparison.OrdinalIgnoreCase)
            ? SnesCopierHeaderBytes + SnesHiRomHeaderOffset + SnesInternalHeaderLength
            : DefaultProbeByteCount;

    /// <summary>
    /// Validates a shifted SNES internal ROM header without doing file I/O.
    /// </summary>
    public static bool HasLikelySnesInternalHeader(ReadOnlySpan<byte> bytes, int contentOffset = 0)
        => HasLikelySnesHeaderAt(bytes, contentOffset + SnesLoRomHeaderOffset)
           || HasLikelySnesHeaderAt(bytes, contentOffset + SnesHiRomHeaderOffset);

    /// <summary>Returns true if the console has a known header mapping.</summary>
    public static bool HasMapping(string consoleKey)
    {
        if (string.IsNullOrWhiteSpace(consoleKey))
            return false;

        return FixedHeaders.ContainsKey(consoleKey) ||
               consoleKey.Equals("SNES", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasExpectedMagic(string consoleKey, ReadOnlySpan<byte> header)
    {
        if (header.Length < 4)
            return false;

        return consoleKey.ToUpperInvariant() switch
        {
            "NES" => header.Length >= 4 && header[0] == 0x4E && header[1] == 0x45 && header[2] == 0x53 && header[3] == 0x1A,
            "ATARI7800" => header.Length >= 10 && header[1..10].SequenceEqual("ATARI7800"u8),
            "ATARILYNX" => header.Length >= 4 && header[0] == 0x4C && header[1] == 0x59 && header[2] == 0x4E && header[3] == 0x58,
            _ => false
        };
    }

    private static bool HasLikelySnesHeaderAt(ReadOnlySpan<byte> bytes, int offset)
    {
        if (offset < 0 || bytes.Length < offset + SnesInternalHeaderLength)
            return false;

        var header = bytes.Slice(offset, SnesInternalHeaderLength);
        if (!HasPrintableSnesTitle(header[..21]))
            return false;

        var mapMode = header[0x15];
        if (!IsKnownSnesMapMode(mapMode))
            return false;

        var romSize = header[0x17];
        if (romSize > 0x0D)
            return false;

        var country = header[0x19];
        if (country > 0x14)
            return false;

        var version = header[0x1B];
        if (version > 0x10)
            return false;

        var complement = (ushort)(header[0x1C] | (header[0x1D] << 8));
        var checksum = (ushort)(header[0x1E] | (header[0x1F] << 8));
        return (ushort)(complement + checksum) == 0xFFFF;
    }

    private static bool IsKnownSnesMapMode(byte value)
        => value is 0x20 or 0x21 or 0x22 or 0x23 or 0x25 or 0x30 or 0x31 or 0x32 or 0x35 or 0x3A;

    private static bool HasPrintableSnesTitle(ReadOnlySpan<byte> title)
    {
        var printable = 0;
        foreach (var value in title)
        {
            if (value is >= 0x20 and <= 0x7E)
            {
                if (value != 0x20)
                    printable++;
                continue;
            }

            if (value != 0)
                return false;
        }

        return printable >= 4;
    }
}
