namespace Romulus.Contracts.Ports;

/// <summary>
/// Port for extracting the inner data SHA1 from CHD files using chdman.
/// The "data SHA1" is the SHA1 of the raw sector data stored inside the CHD container,
/// distinct from the CHD file's own container SHA1.
/// Used for No-Intro CHD matching where DATs record the inner data hash.
/// </summary>
public interface IChdTrackHashExtractor
{
    /// <summary>
    /// Extracts the data SHA1 hash from a CHD file via chdman info.
    /// Returns null if chdman is unavailable, the file is missing,
    /// the file is not a valid CHD, or extraction fails.
    /// </summary>
    string? ExtractDataSha1(
        string chdPath,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}
