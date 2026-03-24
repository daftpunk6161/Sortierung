namespace RomCleanup.Contracts.Ports;

/// <summary>
/// Port for computing headerless ROM hashes.
/// No-Intro DATs hash NES/SNES/Atari 7800/Atari Lynx ROMs without their headers.
/// </summary>
public interface IHeaderlessHasher
{
    /// <summary>
    /// Computes a hash of the ROM content after skipping header bytes.
    /// Returns null if the file cannot be read or the console has no header mapping.
    /// </summary>
    string? ComputeHeaderlessHash(string filePath, string consoleKey, string hashType = "SHA1");
}
