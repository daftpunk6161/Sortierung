namespace Romulus.Contracts.Ports;

/// <summary>
/// Port for cartridge header repair operations.
/// </summary>
public interface IHeaderRepairService
{
    /// <summary>
    /// Repairs dirty trailing bytes in an iNES header and creates a backup file.
    /// </summary>
    /// <param name="path">Absolute path to the ROM file.</param>
    /// <returns>True when a repair was applied; otherwise false.</returns>
    bool RepairNesHeader(string path);

    /// <summary>
    /// Removes a 512-byte copier header from SNES ROMs and creates a backup file.
    /// </summary>
    /// <param name="path">Absolute path to the ROM file.</param>
    /// <returns>True when a header was removed; otherwise false.</returns>
    bool RemoveCopierHeader(string path);
}
