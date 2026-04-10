using System.IO.Compression;
using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.IO;

/// <summary>
/// Production classification I/O adapter backed by System.IO.
/// </summary>
public sealed class ClassificationIo : IClassificationIo
{
    public bool FileExists(string path)
        => File.Exists(path);

    public Stream OpenRead(string path)
        => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    public long FileLength(string path)
        => new FileInfo(path).Length;

    public FileAttributes GetAttributes(string path)
        => File.GetAttributes(path);

    public ZipArchive OpenZipRead(string path)
        => ZipFile.OpenRead(path);
}