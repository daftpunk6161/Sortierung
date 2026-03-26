namespace RomCleanup.Contracts.Ports;

/// <summary>
/// Port interface for file reading operations (text-based).
/// Seam for Set-Parser and other text-file consumers.
/// </summary>
public interface IFileReader
{
    string[] ReadAllLines(string path);
    string ReadAllText(string path);
    bool Exists(string path);
}
