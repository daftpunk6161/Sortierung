namespace Romulus.Contracts;

/// <summary>
/// Shared formatting utilities used across all layers (UI, Infrastructure, Reports).
/// Single source of truth for byte-size formatting.
/// </summary>
public static class Formatting
{
    /// <summary>
    /// Formats a byte count as a human-readable size string (B, KB, MB, GB, TB).
    /// </summary>
    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 40 => $"{bytes / (double)(1L << 40):F2} TB",
        >= 1L << 30 => $"{bytes / (double)(1L << 30):F2} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):F2} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):F2} KB",
        _ => $"{bytes} B"
    };
}
