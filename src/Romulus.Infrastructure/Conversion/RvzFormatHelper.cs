using Romulus.Contracts.Models;

namespace Romulus.Infrastructure.Conversion;

internal static class RvzFormatHelper
{
    internal const string DefaultCompressionAlgorithm = "zstd";
    internal const int DefaultCompressionLevel = 5;
    internal const int DefaultBlockSize = 131072;

    public static bool VerifyMagicBytes(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var info = new FileInfo(path);
        if (!info.Exists || info.Length < 4)
            return false;

        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> magic = stackalloc byte[4];
            if (fs.ReadAtLeast(magic, 4, throwOnEndOfStream: false) < 4)
                return false;

            return magic[0] == 'R' && magic[1] == 'V' && magic[2] == 'Z' && magic[3] == 0x01;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static string[] BuildDolphinRvzArguments(
        string commandToken,
        string sourcePath,
        string targetPath,
        ConversionCapability capability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(capability);

        var algorithm = NormalizeCompressionAlgorithm(capability.CompressionAlgorithm);
        var level = NormalizeCompressionLevel(capability.CompressionLevel);
        var blockSize = NormalizeBlockSize(capability.BlockSize);

        return
        [
            commandToken,
            "-i", sourcePath,
            "-o", targetPath,
            "-f", "rvz",
            "-c", algorithm,
            "-l", level.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-b", blockSize.ToString(System.Globalization.CultureInfo.InvariantCulture)
        ];
    }

    private static string NormalizeCompressionAlgorithm(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DefaultCompressionAlgorithm;

        var normalized = value.Trim().ToLowerInvariant();
        foreach (var ch in normalized)
        {
            if (!(char.IsLetterOrDigit(ch) || ch is '-' or '_'))
                return DefaultCompressionAlgorithm;
        }

        return normalized;
    }

    private static int NormalizeCompressionLevel(int? value)
        => value is >= 0 and <= 22
            ? value.Value
            : DefaultCompressionLevel;

    private static int NormalizeBlockSize(int? value)
        => value is >= 32768 and <= 1048576
            ? value.Value
            : DefaultBlockSize;
}