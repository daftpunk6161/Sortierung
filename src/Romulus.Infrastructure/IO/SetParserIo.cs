using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.IO;

/// <summary>
/// Production set parser I/O adapter backed by System.IO.
/// </summary>
public sealed class SetParserIo : ISetParserIo
{
    public bool Exists(string path)
        => File.Exists(path);

    public IEnumerable<string> ReadLines(string path)
        => File.ReadLines(path);
}