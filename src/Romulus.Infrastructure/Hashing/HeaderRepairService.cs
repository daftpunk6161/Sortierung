using System.Collections.Concurrent;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.FileSystem;

namespace Romulus.Infrastructure.Hashing;

/// <summary>
/// Header repair implementation for iNES and SNES copier headers.
/// </summary>
public sealed class HeaderRepairService : IHeaderRepairService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly IFileSystem _fileSystem;
    private readonly Action<string>? _audit;

    public HeaderRepairService(IFileSystem fileSystem, Action<string>? audit = null)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _audit = audit;
    }

    public bool RepairNesHeader(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !_fileSystem.TestPath(path, "Leaf"))
            return false;

        var fullPath = Path.GetFullPath(path);
        var gate = FileLocks.GetOrAdd(fullPath, static _ => new SemaphoreSlim(1, 1));
        if (!gate.Wait(TimeSpan.FromSeconds(30)))
            return false;

        try
        {
            var header = new byte[16];
            using (var readStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (readStream.Length < 16 || readStream.Read(header, 0, header.Length) < header.Length)
                    return false;
            }

            if (header.Length < 16)
                return false;

            if (header[0] != 0x4E || header[1] != 0x45 || header[2] != 0x53 || header[3] != 0x1A)
                return false;

            var dirty = false;
            for (var i = 12; i <= 15; i++)
            {
                if (header[i] == 0x00)
                    continue;

                dirty = true;
                break;
            }

            if (!dirty)
                return false;

            var backupPath = BuildBackupPath(fullPath, "nes-header");
            AtomicFileWriter.CopyFile(fullPath, backupPath, overwrite: false);
            if (!FilesEqual(fullPath, backupPath))
                return false;

            using (var writeStream = new FileStream(fullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                writeStream.Position = 12;
                writeStream.Write([0x00, 0x00, 0x00, 0x00]);
                writeStream.Flush(flushToDisk: true);
            }

            var verified = VerifyNesHeaderRepaired(fullPath);
            if (!verified)
            {
                AtomicFileWriter.CopyFile(backupPath, fullPath, overwrite: true);
                return false;
            }

            _audit?.Invoke($"header-repair:nes:{fullPath}:{backupPath}");
            return verified;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            gate.Release();
        }
    }

    public bool RemoveCopierHeader(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !_fileSystem.TestPath(path, "Leaf"))
            return false;

        var fullPath = Path.GetFullPath(path);
        var gate = FileLocks.GetOrAdd(fullPath, static _ => new SemaphoreSlim(1, 1));
        if (!gate.Wait(TimeSpan.FromSeconds(30)))
            return false;

        try
        {
            var fi = new FileInfo(fullPath);
            if (fi.Length < 512 || fi.Length % 1024 != 512)
                return false;

            var backupPath = BuildBackupPath(fullPath, "snes-copier");
            AtomicFileWriter.CopyFile(fullPath, backupPath, overwrite: false);
            if (!FilesEqual(fullPath, backupPath))
                return false;

            var tempPath = BuildTempPath(fullPath);
            try
            {
                using (var source = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var target = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    source.Position = 512;
                    source.CopyTo(target);
                    target.Flush(flushToDisk: true);
                }

                File.Move(tempPath, fullPath, overwrite: true);
            }
            finally
            {
                TryDelete(tempPath);
            }

            var verified = VerifyCopierHeaderRemoved(fullPath, backupPath);
            if (!verified)
            {
                AtomicFileWriter.CopyFile(backupPath, fullPath, overwrite: true);
                return false;
            }

            _audit?.Invoke($"header-repair:snes-copier:{fullPath}:{backupPath}");
            return verified;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            gate.Release();
        }
    }

    private static string BuildBackupPath(string path, string reason)
        => path + $".{DateTime.UtcNow:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}.{reason}.bak";

    private static string BuildTempPath(string fullPath)
    {
        var directory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        var fileName = Path.GetFileName(fullPath);
        return Path.Combine(directory, $".{fileName}.{Environment.ProcessId}.{Guid.NewGuid():N}.header.tmp");
    }

    private static bool VerifyNesHeaderRepaired(string path)
    {
        try
        {
            var header = new byte[16];
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Read(header, 0, header.Length) < header.Length)
                return false;

            return header[0] == 0x4E
                && header[1] == 0x45
                && header[2] == 0x53
                && header[3] == 0x1A
                && header[12] == 0x00
                && header[13] == 0x00
                && header[14] == 0x00
                && header[15] == 0x00;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool VerifyCopierHeaderRemoved(string path, string backupPath)
    {
        try
        {
            using var backup = new FileStream(backupPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var current = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (backup.Length < 512 || current.Length != backup.Length - 512)
                return false;

            backup.Position = 512;
            return StreamsEqual(backup, current);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool FilesEqual(string leftPath, string rightPath)
    {
        try
        {
            using var left = new FileStream(leftPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var right = new FileStream(rightPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (left.Length != right.Length)
                return false;

            return StreamsEqual(left, right);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool StreamsEqual(Stream left, Stream right)
    {
        var leftBuffer = new byte[81920];
        var rightBuffer = new byte[81920];

        while (true)
        {
            var leftRead = left.Read(leftBuffer, 0, leftBuffer.Length);
            var rightRead = right.Read(rightBuffer, 0, rightBuffer.Length);
            if (leftRead != rightRead)
                return false;

            if (leftRead == 0)
                return true;

            if (!leftBuffer.AsSpan(0, leftRead).SequenceEqual(rightBuffer.AsSpan(0, rightRead)))
                return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort temp cleanup.
        }
    }
}
