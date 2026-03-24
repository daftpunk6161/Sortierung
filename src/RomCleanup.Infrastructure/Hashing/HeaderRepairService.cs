using RomCleanup.Contracts.Ports;

namespace RomCleanup.Infrastructure.Hashing;

/// <summary>
/// Header repair implementation for iNES and SNES copier headers.
/// </summary>
public sealed class HeaderRepairService : IHeaderRepairService
{
    private readonly IFileSystem _fileSystem;

    public HeaderRepairService(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public bool RepairNesHeader(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !_fileSystem.TestPath(path, "Leaf"))
            return false;

        try
        {
            using var fs = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            var header = new byte[16];
            var read = fs.Read(header, 0, header.Length);
            if (read < 16)
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

            _fileSystem.CopyFile(path, path + ".bak", overwrite: true);

            fs.Seek(12, SeekOrigin.Begin);
            var zeroBytes = new byte[] { 0x00, 0x00, 0x00, 0x00 };
            fs.Write(zeroBytes, 0, zeroBytes.Length);
            fs.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool RemoveCopierHeader(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !_fileSystem.TestPath(path, "Leaf"))
            return false;

        try
        {
            var fi = new FileInfo(path);
            if (fi.Length < 512 || fi.Length % 1024 != 512)
                return false;

            _fileSystem.CopyFile(path, path + ".bak", overwrite: true);

            var data = File.ReadAllBytes(path);
            var stripped = new byte[data.Length - 512];
            Array.Copy(data, 512, stripped, 0, stripped.Length);
            File.WriteAllBytes(path, stripped);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
