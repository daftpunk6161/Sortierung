namespace Romulus.Infrastructure.Conversion;

internal static class ConversionOutputValidator
{
    private const long DefaultMinimumBytes = 2;

    private static readonly IReadOnlyDictionary<string, long> MinimumBytesByExtension =
        new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            [".iso"] = 16,
            [".bin"] = 16,
            [".img"] = 16,
            [".cso"] = 16,
            [".chd"] = 16,
            [".wbfs"] = 16,
            [".gcz"] = 16,
            [".rvz"] = 4,
            [".zip"] = 22,
            [".7z"] = 6
        };

    /// <summary>
    /// R5-020: Known magic bytes for output format header validation.
    /// Key = extension (lowercase), Value = expected header bytes.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, byte[]> MagicHeaderByExtension =
        new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            [".chd"] = [0x4D, 0x43, 0x6F, 0x6D, 0x70, 0x72, 0x48, 0x44], // MComprHD
            [".zip"] = [0x50, 0x4B],           // PK
            [".7z"] = [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C], // 7z signature
            [".rvz"] = [0x52, 0x56, 0x5A, 0x01], // RVZ\x01
            [".gcz"] = [0x47, 0x43, 0x5A],     // GCZ
            [".wbfs"] = [0x57, 0x42, 0x46, 0x53], // WBFS
            [".cso"] = [0x43, 0x49, 0x53, 0x4F],  // CISO
        };

    public static bool TryValidateCreatedOutput(string targetPath, out string failureReason)
    {
        return TryValidateCreatedOutput(targetPath, isIntermediate: false, out failureReason);
    }

    public static bool TryValidateCreatedOutput(string targetPath, bool isIntermediate, out string failureReason)
    {
        if (!File.Exists(targetPath))
        {
            failureReason = "output-not-created";
            return false;
        }

        try
        {
            var length = new FileInfo(targetPath).Length;
            if (length <= 0)
            {
                failureReason = "output-empty";
                return false;
            }

            // Intermediate outputs only need existence + non-empty check;
            // strict minimum-size validation applies only to final outputs.
            if (!isIntermediate)
            {
                var minimumExpectedBytes = ResolveMinimumExpectedBytes(targetPath);
                if (length < minimumExpectedBytes)
                {
                    failureReason = "output-too-small";
                    return false;
                }

                if (!ValidateMagicHeader(targetPath))
                {
                    failureReason = "output-magic-header-mismatch";
                    return false;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failureReason = "output-unreadable";
            return false;
        }

        failureReason = string.Empty;
        return true;
    }

    private static long ResolveMinimumExpectedBytes(string targetPath)
    {
        var extension = Path.GetExtension(targetPath);
        if (string.IsNullOrWhiteSpace(extension))
            return DefaultMinimumBytes;

        return MinimumBytesByExtension.TryGetValue(extension, out var minimumBytes)
            ? minimumBytes
            : DefaultMinimumBytes;
    }

    /// <summary>
    /// R5-020: Validates the file header matches known magic bytes for the extension.
    /// Returns true if no magic bytes are known for the extension (pass-through).
    /// Exposed as a separate check so callers can use it for post-conversion verification.
    /// </summary>
    public static bool ValidateMagicHeader(string targetPath)
    {
        var extension = Path.GetExtension(targetPath);
        if (string.IsNullOrWhiteSpace(extension))
            return true;

        if (!MagicHeaderByExtension.TryGetValue(extension, out var expectedHeader))
            return true; // No known magic bytes — skip check

        try
        {
            var headerBuffer = new byte[expectedHeader.Length];
            using var fs = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var bytesRead = fs.Read(headerBuffer, 0, headerBuffer.Length);
            if (bytesRead < expectedHeader.Length)
                return false;

            return headerBuffer.AsSpan().SequenceEqual(expectedHeader);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
