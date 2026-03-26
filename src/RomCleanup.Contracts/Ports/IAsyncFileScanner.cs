using RomCleanup.Contracts.Models;

namespace RomCleanup.Contracts.Ports;

/// <summary>
/// Asynchronous scanner port for streaming file enumeration.
/// </summary>
public interface IAsyncFileScanner
{
    IAsyncEnumerable<ScannedFileEntry> EnumerateFilesAsync(
        IReadOnlyList<string> roots,
        IReadOnlyCollection<string> extensions,
        CancellationToken cancellationToken = default);
}
