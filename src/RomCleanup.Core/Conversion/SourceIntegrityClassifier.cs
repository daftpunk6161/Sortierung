namespace RomCleanup.Core.Conversion;

using RomCleanup.Contracts.Models;

/// <summary>
/// Classifies source integrity from extension and filename hints.
/// </summary>
public static class SourceIntegrityClassifier
{
    private static readonly HashSet<string> LosslessExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cue", ".bin", ".iso", ".img", ".gdi", ".gcm", ".wbfs", ".gcz", ".wia", ".wud"
    };

    private static readonly HashSet<string> LossyExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cso", ".pbp", ".cdi"
    };

    /// <summary>
    /// Classifies source integrity for conversion planning.
    /// </summary>
    public static SourceIntegrity Classify(string extension, string? fileName = null)
    {
        var ext = (extension ?? string.Empty).Trim();

        if (!string.IsNullOrWhiteSpace(fileName)
            && fileName.Contains(".nkit.", StringComparison.OrdinalIgnoreCase))
        {
            return SourceIntegrity.Lossy;
        }

        if (LosslessExtensions.Contains(ext))
            return SourceIntegrity.Lossless;

        if (LossyExtensions.Contains(ext))
            return SourceIntegrity.Lossy;

        return SourceIntegrity.Unknown;
    }
}
