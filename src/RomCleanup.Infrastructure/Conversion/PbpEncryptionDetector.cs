namespace RomCleanup.Infrastructure.Conversion;

/// <summary>
/// TASK-057: Detects whether a PBP (PlayStation Portable Binary Package) file is encrypted.
/// PBP header: bytes 0-3 = 0x00 'P' 'B' 'P', bytes 4-7 = version,
/// bytes 8-39 = offset table (8 entries × 4 bytes each).
/// The 7th offset (index 6, bytes 32-35) points to the DATA.PSP section.
/// If DATA.PSP begins with 0x7E 'P' 'S' 'P' (~PSP), it's an executable—
/// byte at DATA.PSP+0xD4 != 0 indicates encryption.
/// </summary>
public static class PbpEncryptionDetector
{
    private static readonly byte[] PbpMagic = [0x00, (byte)'P', (byte)'B', (byte)'P'];
    private static readonly byte[] PspMagic = [(byte)'~', (byte)'P', (byte)'S', (byte)'P'];

    /// <summary>
    /// Returns true if the file at <paramref name="path"/> is an encrypted PBP.
    /// Returns false for non-PBP files, unreadable files, or unencrypted PBPs.
    /// </summary>
    public static bool IsEncrypted(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);

            // PBP header: 4 magic + 4 version + 8×4 offsets = 40 bytes minimum
            Span<byte> header = stackalloc byte[40];
            if (fs.ReadAtLeast(header, 40, throwOnEndOfStream: false) < 40)
                return false;

            // Check PBP magic: 0x00 'P' 'B' 'P'
            if (!header[..4].SequenceEqual(PbpMagic))
                return false;

            // Read DATA.PSP offset from 7th entry in offset table (little-endian uint32 at byte 32)
            var dataPspOffset = BitConverter.ToUInt32(header.Slice(32, 4));
            if (dataPspOffset == 0)
                return false;

            // Seek to DATA.PSP section and read ~PSP header + encryption flag area
            const int pspHeaderNeeded = 0xD5; // We need up to offset 0xD4
            if (fs.Length < dataPspOffset + pspHeaderNeeded)
                return false;

            fs.Seek(dataPspOffset, SeekOrigin.Begin);
            Span<byte> pspHeader = stackalloc byte[pspHeaderNeeded];
            if (fs.ReadAtLeast(pspHeader, pspHeaderNeeded, throwOnEndOfStream: false) < pspHeaderNeeded)
                return false;

            // Check ~PSP magic
            if (!pspHeader[..4].SequenceEqual(PspMagic))
                return false;

            // Byte at offset 0xD4 in the PSP section: non-zero = encrypted
            return pspHeader[0xD4] != 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
