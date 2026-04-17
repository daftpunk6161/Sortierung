namespace Romulus.Infrastructure.Metadata;

/// <summary>
/// Maps Romulus console keys to ScreenScraper system IDs.
/// See: https://www.screenscraper.fr/webapi2/systemesListe
/// Only includes systems that Romulus actively supports.
/// </summary>
internal static class ScreenScraperSystemMap
{
    private static readonly Dictionary<string, int> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NES"]   = 3,
        ["SNES"]  = 4,
        ["N64"]   = 14,
        ["GC"]    = 13,
        ["WII"]   = 16,
        ["GB"]    = 9,
        ["GBC"]   = 10,
        ["GBA"]   = 12,
        ["NDS"]   = 15,
        ["VB"]    = 11,
        ["FC"]    = 3,     // Famicom = NES
        ["SFC"]   = 4,     // Super Famicom = SNES
        ["FDS"]   = 106,
        ["MD"]    = 1,     // Mega Drive / Genesis
        ["GEN"]   = 1,
        ["SMS"]   = 2,
        ["GG"]    = 21,
        ["32X"]   = 19,
        ["SCD"]   = 20,
        ["SAT"]   = 22,
        ["DC"]    = 23,
        ["PS1"]   = 57,
        ["PS2"]   = 58,
        ["PSP"]   = 61,
        ["PS3"]   = 59,
        ["XBOX"]  = 32,
        ["X360"]  = 33,
        ["PCE"]   = 31,
        ["PCECD"] = 114,
        ["NGEO"]  = 142,   // Neo Geo AES
        ["NEOCD"] = 70,
        ["NGP"]   = 25,
        ["NGPC"]  = 82,
        ["WS"]    = 45,
        ["WSC"]   = 46,
        ["LYNX"]  = 28,
        ["JAG"]   = 27,
        ["JAGCD"] = 171,
        ["A26"]   = 26,
        ["A52"]   = 40,
        ["A78"]   = 41,
        ["CV"]    = 48,
        ["INTV"]  = 115,
        ["3DO"]   = 29,
        ["MAME"]  = 75,
        ["FBA"]   = 75,
        ["CPC"]   = 65,
        ["ZX"]    = 76,
        ["C64"]   = 66,
        ["AMIGA"] = 64,
        ["MSX"]   = 113,
        ["MSX2"]  = 116,
        ["PCFX"]  = 72,
        ["SG1K"]  = 109,
        ["SC3K"]  = 110,
    };

    /// <summary>
    /// Tries to resolve a ScreenScraper system ID for the given console key.
    /// Returns null when no mapping exists.
    /// </summary>
    internal static int? TryGetSystemId(string consoleKey)
        => Map.TryGetValue(consoleKey, out var id) ? id : null;
}
