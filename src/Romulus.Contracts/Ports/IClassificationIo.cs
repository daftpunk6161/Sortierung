using System.IO.Compression;

namespace Romulus.Contracts.Ports;

/// <summary>
/// I/O abstraction for classification detectors.
/// </summary>
public interface IClassificationIo
{
    bool FileExists(string path);

    Stream OpenRead(string path);

    long FileLength(string path);

    FileAttributes GetAttributes(string path);

    ZipArchive OpenZipRead(string path);
}