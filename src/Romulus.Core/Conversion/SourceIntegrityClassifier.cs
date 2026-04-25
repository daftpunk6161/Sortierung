namespace Romulus.Core.Conversion;

using Romulus.Contracts.Models;

/// <summary>
/// Classifies source integrity from extension and filename hints.
/// </summary>
public static class SourceIntegrityClassifier
{
    private static readonly HashSet<string> LosslessExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cue", ".bin", ".iso", ".img", ".gdi", ".gcm", ".wbfs", ".gcz", ".wia", ".wud",
        ".wux", ".tgc", ".chd", ".rvz", ".nsp", ".xci", ".ecm"
    };

    private static readonly HashSet<string> LossyExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cso", ".pbp", ".cdi", ".dax", ".zso", ".jso"
    };

    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".xz"
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

    public static bool IsArchiveExtension(string extension)
        => !string.IsNullOrWhiteSpace(extension) && ArchiveExtensions.Contains(extension.Trim());
}
