using System.Text;
using Romulus.Contracts.Models;

namespace Romulus.Core.Classification;

/// <summary>
/// Pure header analysis for cartridge/disc byte signatures.
/// </summary>
public static class HeaderAnalyzer
{
    /// <summary>
    /// Analyze raw header bytes and return detected platform/format metadata.
    /// </summary>
    /// <param name="headerBytes">Leading bytes of a ROM image.</param>
    /// <param name="fileSize">Full source file size in bytes.</param>
    /// <returns>Detected header info or null if no stable detection is possible.</returns>
    public static RomHeaderInfo? AnalyzeHeader(byte[] headerBytes, long fileSize)
    {
        ArgumentNullException.ThrowIfNull(headerBytes);

        try
        {
            // NES (iNES): 4E 45 53 1A
            if (headerBytes.Length >= 16 && headerBytes[0] == 0x4E && headerBytes[1] == 0x45 &&
                headerBytes[2] == 0x53 && headerBytes[3] == 0x1A)
            {
                var isNes2 = (headerBytes[7] & 0x0C) == 0x08;
                return new RomHeaderInfo("NES", isNes2 ? "NES 2.0" : "iNES",
                    $"PRG={headerBytes[4] * 16}KB, CHR={headerBytes[5] * 8}KB, Mapper={(headerBytes[6] >> 4) | (headerBytes[7] & 0xF0)}");
            }

            // N64 Big-Endian: 80 37
            if (headerBytes.Length >= 0x40 && headerBytes[0] == 0x80 && headerBytes[1] == 0x37)
            {
                var title = Encoding.ASCII.GetString(headerBytes, 0x20, 20).TrimEnd('\0', ' ');
                return new RomHeaderInfo("N64", "Big-Endian (.z64)", $"Title={title}");
            }

            // N64 Byte-Swap: 37 80
            if (headerBytes.Length >= 0x40 && headerBytes[0] == 0x37 && headerBytes[1] == 0x80)
                return new RomHeaderInfo("N64", "Byte-Swapped (.v64)", "");

            // N64 Little-Endian: 40 12
            if (headerBytes.Length >= 0x40 && headerBytes[0] == 0x40 && headerBytes[1] == 0x12)
                return new RomHeaderInfo("N64", "Little-Endian (.n64)", "");

            // GBA: 0x96 at offset 0xB2
            if (headerBytes.Length >= 0xBE && headerBytes[0xB2] == 0x96)
            {
                var title = Encoding.ASCII.GetString(headerBytes, 0xA0, 12).TrimEnd('\0', ' ');
                var code = Encoding.ASCII.GetString(headerBytes, 0xAC, 4).TrimEnd('\0');
                return new RomHeaderInfo("GBA", "GBA ROM", $"Title={title}, Code={code}");
            }

            // SNES LoROM (header at 0x7FC0)
            if (headerBytes.Length >= 0x8000)
            {
                var snesTitle = Encoding.ASCII.GetString(headerBytes, 0x7FC0, 21).TrimEnd('\0', ' ');
                var checksum = headerBytes[0x7FDE] | (headerBytes[0x7FDF] << 8);
                var complement = headerBytes[0x7FDC] | (headerBytes[0x7FDD] << 8);
                if (snesTitle.Length > 0 && snesTitle.All(c => c >= 0x20 && c <= 0x7E) &&
                    (checksum + complement) == 0xFFFF)
                    return new RomHeaderInfo("SNES", "LoROM", $"Title={snesTitle}");
            }

            // SNES HiROM (header at 0xFFC0)
            if (headerBytes.Length >= 0x10000)
            {
                var snesTitle = Encoding.ASCII.GetString(headerBytes, 0xFFC0, 21).TrimEnd('\0', ' ');
                var checksum = headerBytes[0xFFDE] | (headerBytes[0xFFDF] << 8);
                var complement = headerBytes[0xFFDC] | (headerBytes[0xFFDD] << 8);
                if (snesTitle.Length > 0 && snesTitle.All(c => c >= 0x20 && c <= 0x7E) &&
                    (checksum + complement) == 0xFFFF)
                    return new RomHeaderInfo("SNES", "HiROM", $"Title={snesTitle}");
            }

            _ = fileSize; // kept for signature parity and future heuristics.
            return new RomHeaderInfo("Unknown", "Unknown Format",
                $"Magic: {headerBytes[0]:X2} {headerBytes[1]:X2} {headerBytes[2]:X2} {headerBytes[3]:X2}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
