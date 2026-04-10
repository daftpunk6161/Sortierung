using System.Buffers;

namespace Romulus.Infrastructure.Hashing;

/// <summary>
/// Table-based CRC32 implementation (IEEE 802.3 polynomial 0xEDB88320).
/// Port of inline C# from Dat.ps1 lines 664-704.
/// Returns lowercase hex-8 string identical to the PowerShell version.
/// </summary>
public static class Crc32
{
    private static readonly uint[] Table = InitTable();

    private static uint[] InitTable()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (var j = 0; j < 8; j++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[i] = c;
        }
        return t;
    }

    public static string HashFile(string path)
    {
        using var fs = File.OpenRead(path);
        return HashStream(fs);
    }

    public static string HashStream(Stream s)
    {
        var crc = 0xFFFFFFFFu;
        var buf = ArrayPool<byte>.Shared.Rent(81_920); // 80 KB — avoids LOH allocation
        try
        {
            int read;
            while ((read = s.Read(buf, 0, buf.Length)) > 0)
            {
                for (var i = 0; i < read; i++)
                    crc = Table[(crc ^ buf[i]) & 0xFF] ^ (crc >> 8);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
        crc ^= 0xFFFFFFFFu;
        return crc.ToString("x8");
    }
}
