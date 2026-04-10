namespace Romulus.Core.Classification;

/// <summary>
/// Maps console keys to their ROM header skip bytes for headerless hashing.
/// No-Intro DATs hash NES/SNES/Atari 7800/Atari Lynx ROMs WITHOUT headers.
/// </summary>
public static class HeaderSizeMap
{
    private static readonly IReadOnlyDictionary<string, int> FixedHeaders =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["NES"] = 16,      // iNES header
            ["ATARI7800"] = 128, // A7800 header
            ["ATARILYNX"] = 64   // Lynx header
        };

    /// <summary>
    /// Returns the number of header bytes to skip for headerless hashing.
    /// SNES uses conditional detection (copier header = 512 bytes when fileSize % 1024 == 512).
    /// Returns 0 when no header skipping is needed.
    /// </summary>
    public static int GetSkipBytes(string consoleKey, ReadOnlySpan<byte> header, long fileSize)
    {
        if (string.IsNullOrWhiteSpace(consoleKey))
            return 0;

        // SNES: copier header detection via file size heuristic (RISK-004)
        if (consoleKey.Equals("SNES", StringComparison.OrdinalIgnoreCase))
            return (fileSize % 1024 == 512) ? 512 : 0;

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
}
