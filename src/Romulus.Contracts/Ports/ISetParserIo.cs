namespace Romulus.Contracts.Ports;

/// <summary>
/// I/O abstraction for set descriptor parsing.
/// </summary>
public interface ISetParserIo
{
    bool Exists(string path);

    IEnumerable<string> ReadLines(string path);
}