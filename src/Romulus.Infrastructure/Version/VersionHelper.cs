namespace Romulus.Infrastructure.Version;

/// <summary>
/// Version and compatibility helpers.
/// Port of Compatibility.ps1.
/// </summary>
public static class VersionHelper
{
    public const string CurrentVersion = "1.0.0";

    /// <summary>
    /// Parse a CSV list string into an array of trimmed, non-empty strings.
    /// Port of ConvertFrom-CSVList.
    /// </summary>
    public static string[] ParseCsvList(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return [];

        return input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();
    }

    /// <summary>
    /// Normalize an extension list from user input text.
    /// Port of ConvertTo-NormalizedExtensionList from RunHelpers.Insights.ps1.
    /// </summary>
    public static string[] NormalizeExtensionList(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        return text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e =>
            {
                e = e.Trim().ToLowerInvariant();
                if (e.Length > 0 && e[0] != '.') e = "." + e;
                return e;
            })
            .Where(e => e.Length > 1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
