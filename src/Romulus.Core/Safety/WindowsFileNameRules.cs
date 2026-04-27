namespace Romulus.Core.Safety;

/// <summary>
/// Pure Windows-Datei- und Pfadnamen-Regeln. Keine I/O.
///
/// Single Source of Truth fuer "Reserved Device Name"-Erkennung.
/// Frueher dupliziert in <c>Romulus.Infrastructure.FileSystem.FileSystemAdapter</c>
/// und <c>Romulus.Core.Audit.DatRenamePolicy</c> – konsolidiert per Deep Dive Audit
/// (Safety / FileSystem / Security).
/// </summary>
public static class WindowsFileNameRules
{
    /// <summary>
    /// Prueft, ob ein Pfadsegment einem Windows-Reserved-Device-Namen entspricht
    /// (CON, PRN, AUX, NUL, COM0-COM9, LPT0-LPT9). Erweiterungen werden ignoriert,
    /// d.h. <c>NUL.txt</c> gilt ebenfalls als reserviert (Windows-Verhalten).
    /// </summary>
    /// <remarks>
    /// SEC-PATH-03: reserviert unabhaengig von Erweiterung.
    /// </remarks>
    public static bool IsReservedDeviceName(string? segment)
    {
        if (string.IsNullOrEmpty(segment))
            return false;

        var dotIndex = segment.IndexOf('.');
        var nameOnly = dotIndex >= 0 ? segment[..dotIndex] : segment;

        return nameOnly.Length switch
        {
            3 => nameOnly.Equals("CON", StringComparison.OrdinalIgnoreCase)
              || nameOnly.Equals("PRN", StringComparison.OrdinalIgnoreCase)
              || nameOnly.Equals("AUX", StringComparison.OrdinalIgnoreCase)
              || nameOnly.Equals("NUL", StringComparison.OrdinalIgnoreCase),
            4 => (nameOnly.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                  || nameOnly.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                 && char.IsAsciiDigit(nameOnly[3]),
            _ => false
        };
    }
}
