namespace Romulus.Core.Classification;

/// <summary>
/// Pure classifier that identifies non-ROM file types by examining leading bytes (magic numbers).
/// Accepts a ReadOnlySpan&lt;byte&gt; header and returns a content-type verdict.
/// No I/O — caller is responsible for reading the header bytes.
/// </summary>
public static class ContentSignatureClassifier
{
    /// <summary>
    /// Minimum header size needed for reliable classification.
    /// </summary>
    public const int MinHeaderSize = 8;

    /// <summary>
    /// Checks header bytes against known non-ROM magic signatures.
    /// Returns the detected content type if a known non-ROM signature is found,
    /// or <see cref="ContentType.Unknown"/> if no match.
    /// </summary>
    public static ContentType Classify(ReadOnlySpan<byte> header)
    {
        if (header.Length < 2)
            return ContentType.Unknown;

        // PDF: %PDF (25 50 44 46)
        if (header.Length >= 4 && header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46)
            return ContentType.Pdf;

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (header.Length >= 8
            && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
            && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
            return ContentType.Png;

        // JPEG: FF D8 FF
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return ContentType.Jpeg;

        // GIF: GIF87a or GIF89a
        if (header.Length >= 6
            && header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x38
            && (header[4] == 0x37 || header[4] == 0x39) && header[5] == 0x61)
            return ContentType.Gif;

        // BMP: BM (42 4D)
        if (header[0] == 0x42 && header[1] == 0x4D)
            return ContentType.Bmp;

        // MP3: ID3 tag or MPEG sync word (FF FB, FF FA, FF F3, FF F2)
        if (header.Length >= 3 && header[0] == 0x49 && header[1] == 0x44 && header[2] == 0x33)
            return ContentType.Mp3;
        if (header[0] == 0xFF && (header[1] & 0xE0) == 0xE0)
            return ContentType.Mp3;

        // FLAC: fLaC
        if (header.Length >= 4 && header[0] == 0x66 && header[1] == 0x4C && header[2] == 0x61 && header[3] == 0x43)
            return ContentType.Flac;

        // OGG: OggS
        if (header.Length >= 4 && header[0] == 0x4F && header[1] == 0x67 && header[2] == 0x67 && header[3] == 0x53)
            return ContentType.Ogg;

        // Windows PE / EXE: MZ (4D 5A)
        if (header[0] == 0x4D && header[1] == 0x5A)
            return ContentType.WindowsExe;

        // ELF: 7F 45 4C 46
        if (header.Length >= 4 && header[0] == 0x7F && header[1] == 0x45 && header[2] == 0x4C && header[3] == 0x46)
            return ContentType.Elf;

        // Mach-O: FE ED FA CE / FE ED FA CF / CE FA ED FE / CF FA ED FE
        if (header.Length >= 4)
        {
            if ((header[0] == 0xFE && header[1] == 0xED && header[2] == 0xFA && (header[3] == 0xCE || header[3] == 0xCF))
                || (header[0] == 0xCE && header[1] == 0xFA && header[2] == 0xED && header[3] == 0xFE)
                || (header[0] == 0xCF && header[1] == 0xFA && header[2] == 0xED && header[3] == 0xFE))
                return ContentType.MachO;
        }

        // RIFF (AVI/WAV): RIFF
        if (header.Length >= 4 && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46)
            return ContentType.Riff;

        // XML: <?xm (3C 3F 78 6D) or BOM + <
        if (header.Length >= 4 && header[0] == 0x3C && header[1] == 0x3F && header[2] == 0x78 && header[3] == 0x6D)
            return ContentType.Xml;

        // SQLite: SQLite format 3\000 (first 16 bytes)
        if (header.Length >= 6
            && header[0] == 0x53 && header[1] == 0x51 && header[2] == 0x4C
            && header[3] == 0x69 && header[4] == 0x74 && header[5] == 0x65)
            return ContentType.Sqlite;

        return ContentType.Unknown;
    }

    /// <summary>
    /// Returns true if the classified content type indicates a non-ROM file.
    /// </summary>
    public static bool IsNonRom(ContentType type) =>
        type != ContentType.Unknown;
}

/// <summary>
/// Content types identifiable by file header magic bytes.
/// </summary>
public enum ContentType
{
    Unknown,
    Pdf,
    Png,
    Jpeg,
    Gif,
    Bmp,
    Mp3,
    Flac,
    Ogg,
    WindowsExe,
    Elf,
    MachO,
    Riff,
    Xml,
    Sqlite
}
