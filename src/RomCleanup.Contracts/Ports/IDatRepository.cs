using RomCleanup.Contracts.Models;

namespace RomCleanup.Contracts.Ports;

/// <summary>
/// Port interface for DAT file index and hash operations.
/// Maps to New-DatRepositoryPort in PortInterfaces.ps1.
/// Parameters aligned with PS: Get-DatIndex -DatRoot -ConsoleMap -HashType -Log
/// </summary>
public interface IDatRepository
{
    /// <summary>
    /// Build a typed DAT index from DAT files in the given root.
    /// ConsoleMap maps console keys to DAT file paths/names.
    /// </summary>
    DatIndex GetDatIndex(string datRoot, IDictionary<string, string> consoleMap, string hashType = "SHA1");

    /// <summary>Construct a DAT game key from game name and console.</summary>
    string GetDatGameKey(string gameName, string console);

    /// <summary>Parse parent/clone relationships from a DAT file.</summary>
    IDictionary<string, string> GetDatParentCloneIndex(string datPath);

    /// <summary>Walk the parent chain to find the root parent name.</summary>
    string? ResolveParentName(string gameName, IDictionary<string, string> parentMap);
}
