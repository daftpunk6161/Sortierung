using System.IO.Compression;
using Romulus.Contracts.Ports;

// F-1 Project-Split (Wave-2): TestClassificationIo lebt im Benchmark-
// Projekt (Deps-Leaf), bleibt aber im Romulus.Tests-Namespace, damit
// existierende Konsumenten (CartridgeHeaderDetectorCoverageTests,
// ClassificationIoMockingTests, HoldoutExpanderTests) keinen using-Wechsel
// brauchen. Romulus.Tests sieht den Typ via ProjectReference auf Benchmark.
// "public" statt "internal", da der Helper jetzt assembly-uebergreifend
// referenziert wird.
namespace Romulus.Tests;

public sealed class TestClassificationIo : IClassificationIo
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
