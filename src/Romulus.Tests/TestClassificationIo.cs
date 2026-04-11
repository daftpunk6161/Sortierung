using System.IO.Compression;
using Romulus.Contracts.Ports;

namespace Romulus.Tests;

internal sealed class TestClassificationIo : IClassificationIo
{
    public Func<string, bool> FileExistsFunc { get; set; } = _ => false;

    public Func<string, Stream> OpenReadFunc { get; set; } =
        _ => throw new InvalidOperationException("OpenRead delegate not configured.");

    public Func<string, long> FileLengthFunc { get; set; } = _ => 0;

    public Func<string, FileAttributes> GetAttributesFunc { get; set; } = _ => FileAttributes.Normal;

    public Func<string, ZipArchive> OpenZipReadFunc { get; set; } =
        _ => throw new InvalidOperationException("OpenZipRead delegate not configured.");

    public bool FileExists(string path)
        => FileExistsFunc(path);

    public Stream OpenRead(string path)
        => OpenReadFunc(path);

    public long FileLength(string path)
        => FileLengthFunc(path);

    public FileAttributes GetAttributes(string path)
        => GetAttributesFunc(path);

    public ZipArchive OpenZipRead(string path)
        => OpenZipReadFunc(path);
}
