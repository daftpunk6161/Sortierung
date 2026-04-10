namespace Romulus.Contracts.Ports;

/// <summary>
/// Port for computing DAT-compatible normalized ROM hashes.
/// This covers classic headerless hashing and format normalization where
/// DAT sets expect a canonical byte layout.
/// </summary>
public interface IHeaderlessHasher
{
    /// <summary>
    /// Computes a DAT-compatible normalized hash for a ROM file.
    /// Returns null if the file cannot be read or the console has no special normalization.
    /// </summary>
    string? ComputeHeaderlessHash(string filePath, string consoleKey, string hashType = "SHA1");
}
