namespace Romulus.Contracts.Models;

/// <summary>
/// Wave 7 — T-W7-PROVENANCE-TRAIL. Kanonische Liste der Ereignis-Kategorien
/// fuer den per-ROM Provenance-Trail. Diese Liste ist Teil des
/// Schreib-Vertrags (Hash-Kette baut auf <see cref="object.ToString()"/>
/// auf), Renaming oder Reorder von Werten ist eine Breaking-Change.
/// </summary>
public enum ProvenanceEventKind
{
    /// <summary>ROM zum ersten Mal in einem Run gesehen.</summary>
    Imported = 0,

    /// <summary>ROM gegen DAT verifiziert (Hit oder Miss).</summary>
    Verified = 1,

    /// <summary>ROM in Ziel-Verzeichnis verschoben.</summary>
    Moved = 2,

    /// <summary>ROM in anderes Format konvertiert.</summary>
    Converted = 3,

    /// <summary>Eine vorherige Move-/Convert-Aktion wurde rueckgaengig gemacht.</summary>
    RolledBack = 4
}
