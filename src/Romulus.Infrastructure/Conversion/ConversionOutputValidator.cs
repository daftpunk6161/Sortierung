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
            [".wbfs"] = 16,
            [".gcz"] = 16,
            [".rvz"] = 4,
            [".zip"] = 4,
            [".7z"] = 6
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
}
