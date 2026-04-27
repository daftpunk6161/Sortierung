using System.Text;
using Romulus.Core.Safety;

namespace Romulus.Infrastructure.FileSystem;

/// <summary>
/// Same-directory temp-file promotion helpers for durable artifact writes.
///
/// Sicherheits-Gate <see cref="EnsureSafeWriteTarget"/> wird vor jeder Write-/Copy-/Append-Operation
/// aufgerufen, damit kein Aufrufer versehentlich in Reparse-Points, ueber ADS-Suffixe oder auf
/// Windows-Reserved-Device-Names schreiben kann (Single-Source-of-Truth, siehe Deep Dive Audit
/// Safety / FileSystem / Security).
/// </summary>
public static class AtomicFileWriter
{
    public static void WriteAllText(string path, string content, Encoding? encoding = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        EnsureSafeWriteTarget(fullPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var tempPath = BuildTempPath(fullPath);
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, encoding ?? Encoding.UTF8))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            TryDeleteTemp(tempPath);
        }
    }

    public static void CopyFile(string sourcePath, string destinationPath, bool overwrite)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var fullSource = Path.GetFullPath(sourcePath);
        var fullDestination = Path.GetFullPath(destinationPath);
        EnsureSafeWriteTarget(fullDestination);
        if (!File.Exists(fullSource))
            throw new FileNotFoundException("Source file not found.", fullSource);
        if (!overwrite && File.Exists(fullDestination))
            throw new IOException($"Destination already exists: {fullDestination}");

        var directory = Path.GetDirectoryName(fullDestination);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var tempPath = BuildTempPath(fullDestination);
        try
        {
            using (var source = new FileStream(fullSource, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var target = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                source.CopyTo(target);
                target.Flush(flushToDisk: true);
            }

            File.Move(tempPath, fullDestination, overwrite: overwrite);
        }
        finally
        {
            TryDeleteTemp(tempPath);
        }
    }

    public static void WriteAllBytes(string path, byte[] content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);

        var fullPath = Path.GetFullPath(path);
        EnsureSafeWriteTarget(fullPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var tempPath = BuildTempPath(fullPath);
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            TryDeleteTemp(tempPath);
        }
    }

    public static void AppendText(string path, string text, Encoding? encoding = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        EnsureSafeWriteTarget(fullPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var tempPath = BuildTempPath(fullPath);
        try
        {
            if (File.Exists(fullPath))
                File.Copy(fullPath, tempPath, overwrite: false);
            else
                File.WriteAllText(tempPath, string.Empty, encoding ?? Encoding.UTF8);

            using (var stream = new FileStream(tempPath, FileMode.Append, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, encoding ?? Encoding.UTF8))
            {
                writer.Write(text);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            TryDeleteTemp(tempPath);
        }
    }

    private static string BuildTempPath(string fullPath)
    {
        var directory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        var fileName = Path.GetFileName(fullPath);
        return Path.Combine(directory, $".{fileName}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
    }

    private static void TryDeleteTemp(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup; caller already gets the original write failure if any.
        }
    }

    /// <summary>
    /// Zentraler Sicherheits-Gate fuer alle Write-/Copy-/Append-Pfade.
    ///
    /// Wirft fruehzeitig (vor jeder I/O) wenn:
    ///   - der Dateiname einen Alternate-Data-Stream-Suffix (':') traegt,
    ///   - der Dateiname ein Windows Reserved Device Name ist (CON/PRN/AUX/NUL/COM0-9/LPT0-9),
    ///   - die Zieldatei selbst bereits ein Reparse Point ist,
    ///   - ein existierendes Vorfahrverzeichnis ein Reparse Point ist (Junction/Symlink-Schutz).
    ///
    /// Stoppt die Vorfahr-Suche am Volume-Root. Nicht-existente Vorfahren werden uebersprungen.
    /// Begrenzt die Walk-Tiefe defensiv auf 256 Ebenen (DoS-Schutz).
    /// </summary>
    /// <exception cref="ArgumentException">Bei ADS-Suffix oder Reserved Device Name.</exception>
    /// <exception cref="IOException">Bei Reparse Point in Ziel oder Ancestry.</exception>
    private static void EnsureSafeWriteTarget(string fullPath)
    {
        var fileName = Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(fileName))
            throw new ArgumentException($"AtomicFileWriter: target path has no file name: '{fullPath}'.", nameof(fullPath));

        if (fileName.Contains(':'))
            throw new ArgumentException($"AtomicFileWriter: alternate data stream targets are not allowed: '{fileName}'.", nameof(fullPath));

        if (WindowsFileNameRules.IsReservedDeviceName(fileName))
            throw new ArgumentException($"AtomicFileWriter: Windows reserved device name not allowed: '{fileName}'.", nameof(fullPath));

        if (File.Exists(fullPath) && (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"AtomicFileWriter: target is a reparse point: '{fullPath}'.");

        var volumeRoot = Path.GetPathRoot(fullPath);
        var current = Path.GetDirectoryName(fullPath);
        var depth = 0;
        while (!string.IsNullOrEmpty(current)
               && (volumeRoot is null || !current.Equals(volumeRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)))
        {
            if (++depth > 256)
                throw new IOException($"AtomicFileWriter: ancestry depth exceeds limit for '{fullPath}'.");

            if (Directory.Exists(current))
            {
                FileAttributes attrs;
                try
                {
                    attrs = File.GetAttributes(current);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new IOException($"AtomicFileWriter: cannot inspect ancestor '{current}'.", ex);
                }

                if ((attrs & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"AtomicFileWriter: ancestor is a reparse point: '{current}'.");
            }

            var parent = Path.GetDirectoryName(current);
            if (string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                break;
            current = parent;
        }
    }
}
