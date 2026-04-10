namespace Romulus.Infrastructure.Conversion;

internal static class SourcePathFormatDetector
{
    public static string ResolveSourceExtension(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var fileName = Path.GetFileName(sourcePath);
        if (fileName.EndsWith(".nkit.iso", StringComparison.OrdinalIgnoreCase))
            return ".nkit.iso";

        if (fileName.EndsWith(".nkit.gcz", StringComparison.OrdinalIgnoreCase))
            return ".nkit.gcz";

        return Path.GetExtension(sourcePath).ToLowerInvariant();
    }
}
