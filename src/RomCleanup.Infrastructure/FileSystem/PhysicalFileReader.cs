using RomCleanup.Contracts.Ports;

namespace RomCleanup.Infrastructure.FileSystem;

/// <summary>
/// Production implementation of <see cref="IFileReader"/> using physical file system.
/// </summary>
public sealed class PhysicalFileReader : IFileReader
{
    public string[] ReadAllLines(string path) => File.ReadAllLines(path);
    public string ReadAllText(string path) => File.ReadAllText(path);
    public bool Exists(string path) => File.Exists(path);
}
