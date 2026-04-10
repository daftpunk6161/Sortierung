using System.IO;

namespace Romulus.Infrastructure.Paths;

/// <summary>
/// Centralized tool-path validation with security hardening.
/// Replaces duplicate ValidateToolPath logic in SettingsLoader and ViewModels.
/// Rejects non-existent files, disallowed extensions, and system-directory tools.
/// </summary>
public static class ToolPathValidator
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".bat", ".cmd" };

    /// <summary>
    /// Validates a tool path. Returns the normalized full path if valid, or null with a reason if invalid.
    /// </summary>
    public static (string? NormalizedPath, string? RejectReason) Validate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return (null, null); // Empty is valid (tool not configured)

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            return (null, "Ungültiger Dateipfad");
        }

        if (!File.Exists(fullPath))
            return (null, $"Datei nicht gefunden: {Path.GetFileName(fullPath)}");

        var ext = Path.GetExtension(fullPath);
        if (!AllowedExtensions.Contains(ext))
            return (null, $"Dateityp '{ext}' nicht erlaubt (nur .exe, .bat, .cmd)");

        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var sysDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
        if (!string.IsNullOrEmpty(winDir) && fullPath.StartsWith(winDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return (null, "Tools aus dem Windows-Verzeichnis sind nicht erlaubt");
        if (!string.IsNullOrEmpty(sysDir) && fullPath.StartsWith(sysDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return (null, "Tools aus dem System32-Verzeichnis sind nicht erlaubt");

        return (fullPath, null);
    }

    /// <summary>
    /// Returns the validated full path, or empty string if invalid.
    /// Compatible with SettingsLoader usage pattern.
    /// </summary>
    public static string ValidateOrEmpty(string path)
    {
        var (normalized, _) = Validate(path);
        return normalized ?? "";
    }
}
