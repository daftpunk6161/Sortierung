namespace Romulus.Core.SetParsing;

/// <summary>
/// Resolves CloneCD (.ccd) related files: .img and .sub with same base name.
/// Mirrors Get-CcdRelatedFiles from SetParsing.ps1.
/// </summary>
public static class CcdSetParser
{
    private static readonly string[] CompanionExts = { ".img", ".sub" };

    public static IReadOnlyList<string> GetRelatedFiles(string ccdPath)
    {
        if (string.IsNullOrWhiteSpace(ccdPath) || !SetParserIo.Exists(ccdPath))
            return Array.Empty<string>();

        var dir = Path.GetDirectoryName(ccdPath) ?? "";
        var baseName = Path.GetFileNameWithoutExtension(ccdPath);
        var result = new List<string>();

        foreach (var ext in CompanionExts)
        {
            var companion = Path.Combine(dir, baseName + ext);
            if (SetParserIo.Exists(companion))
                result.Add(Path.GetFullPath(companion));
        }

        return result;
    }

    public static IReadOnlyList<string> GetMissingFiles(string ccdPath)
    {
        if (string.IsNullOrWhiteSpace(ccdPath))
            return Array.Empty<string>();

        var dir = Path.GetDirectoryName(ccdPath) ?? "";
        var baseName = Path.GetFileNameWithoutExtension(ccdPath);
        var result = new List<string>();

        foreach (var ext in CompanionExts)
        {
            var companion = Path.Combine(dir, baseName + ext);
            if (!SetParserIo.Exists(companion))
                result.Add(Path.GetFullPath(companion));
        }

        return result;
    }
}
