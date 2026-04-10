using System.Text.RegularExpressions;

namespace Romulus.Core.Classification;

/// <summary>
/// Handles double-extension normalization for ROM files.
/// Mirrors Get-NormalizedExtension from Classification.ps1.
/// </summary>
public static class ExtensionNormalizer
{
    private static readonly Regex RxDoubleExt = new(
        @"\.(nkit\.iso|nkit\.gcz|nkit\.chd|ecm\.bin|ecm\.img|wia\.gcz)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Returns the normalized extension including double-extensions like .nkit.iso.
    /// Always lowercase with leading dot.
    /// </summary>
    public static string GetNormalizedExtension(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "";

        var match = RxDoubleExt.Match(fileName);
        if (match.Success)
            return "." + match.Groups[1].Value.ToLowerInvariant();

        var ext = Path.GetExtension(fileName);
        return string.IsNullOrEmpty(ext) ? "" : ext.ToLowerInvariant();
    }
}
