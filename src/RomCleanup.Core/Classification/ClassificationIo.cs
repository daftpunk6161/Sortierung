using System.IO.Compression;

namespace RomCleanup.Core.Classification;

/// <summary>
/// Abstracts I/O for classification detectors so Core stays testable.
/// Defaults to System.IO; Infrastructure or tests can override via <see cref="Configure"/>.
/// </summary>
internal static class ClassificationIo
{
    private static Func<string, bool> _fileExists = System.IO.File.Exists;
    private static Func<string, Stream> _openRead = path =>
        new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    private static Func<string, long> _fileLength = path => new FileInfo(path).Length;
    private static Func<string, FileAttributes> _getAttributes = System.IO.File.GetAttributes;
    private static Func<string, ZipArchive> _openZipRead = ZipFile.OpenRead;

    public static bool FileExists(string path) => _fileExists(path);
    public static Stream OpenRead(string path) => _openRead(path);
    public static long FileLength(string path) => _fileLength(path);
    public static FileAttributes GetAttributes(string path) => _getAttributes(path);
    public static ZipArchive OpenZipRead(string path) => _openZipRead(path);

    /// <summary>
    /// Replace I/O delegates (for Infrastructure wiring or testing).
    /// Pass null to keep current delegate.
    /// </summary>
    public static void Configure(
        Func<string, bool>? fileExists = null,
        Func<string, Stream>? openRead = null,
        Func<string, long>? fileLength = null,
        Func<string, FileAttributes>? getAttributes = null,
        Func<string, ZipArchive>? openZipRead = null)
    {
        if (fileExists is not null) _fileExists = fileExists;
        if (openRead is not null) _openRead = openRead;
        if (fileLength is not null) _fileLength = fileLength;
        if (getAttributes is not null) _getAttributes = getAttributes;
        if (openZipRead is not null) _openZipRead = openZipRead;
    }

    /// <summary>
    /// Reset to default System.IO delegates.
    /// </summary>
    public static void ResetDefaults()
    {
        _fileExists = System.IO.File.Exists;
        _openRead = path => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        _fileLength = path => new FileInfo(path).Length;
        _getAttributes = System.IO.File.GetAttributes;
        _openZipRead = ZipFile.OpenRead;
    }
}
