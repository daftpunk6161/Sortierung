namespace Romulus.Core.SetParsing;

/// <summary>
/// Shared helpers for multi-file set descriptor extensions and parsing.
/// Keeps descriptor-extension truth in one place across scoring and orchestration.
/// </summary>
public static class SetDescriptorSupport
{
    private static readonly HashSet<string> DescriptorExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cue",
        ".gdi",
        ".ccd",
        ".m3u",
        ".mds",
    };

    public static bool IsDescriptorExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return false;

        return DescriptorExtensions.Contains(extension);
    }

    public static IReadOnlyList<string> GetRelatedFiles(string filePath, string extension, bool includeM3uMembers = true)
    {
        return extension.ToLowerInvariant() switch
        {
            ".cue" => CueSetParser.GetRelatedFiles(filePath),
            ".gdi" => GdiSetParser.GetRelatedFiles(filePath),
            ".ccd" => CcdSetParser.GetRelatedFiles(filePath),
            ".m3u" => includeM3uMembers ? M3uPlaylistParser.GetRelatedFiles(filePath) : Array.Empty<string>(),
            ".mds" => MdsSetParser.GetRelatedFiles(filePath),
            _ => Array.Empty<string>(),
        };
    }

    public static int GetMissingFilesCount(string filePath, string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".cue" => CueSetParser.GetMissingFiles(filePath).Count,
            ".gdi" => GdiSetParser.GetMissingFiles(filePath).Count,
            ".ccd" => CcdSetParser.GetMissingFiles(filePath).Count,
            ".m3u" => M3uPlaylistParser.GetMissingFiles(filePath).Count,
            ".mds" => MdsSetParser.GetMissingFiles(filePath).Count,
            _ => 0,
        };
    }
}
