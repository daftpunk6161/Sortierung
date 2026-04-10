namespace Romulus.Contracts.Models;

/// <summary>
/// Platform family classification. Determines which recognition strategy
/// and hash strategy to apply for a given console.
/// Loaded from the "family" field in consoles.json.
/// </summary>
public enum PlatformFamily
{
    /// <summary>No-Intro cartridge-based systems (NES, SNES, N64, GB, GBA, MD, etc.).
    /// Uses headerless hashing for DAT matching.</summary>
    NoIntroCartridge,

    /// <summary>Redump disc-based systems (PS1, PS2, Saturn, Dreamcast, GC, Wii, etc.).
    /// Uses track-level SHA1 or CHD raw SHA1 for DAT matching.</summary>
    RedumpDisc,

    /// <summary>Arcade systems (MAME, FBNeo, CPS, Naomi, Atomiswave).
    /// Uses set-structure validation with parent/clone matching.</summary>
    Arcade,

    /// <summary>Computer systems using TOSEC naming conventions (Amiga, DOS, Atari ST, MSX, etc.).
    /// Uses TOSEC naming pattern matching.</summary>
    ComputerTOSEC,

    /// <summary>Folder-based game formats (PS3 extracted, Xbox 360 GOD, WiiU Loadiine).
    /// Uses folder-level hash (key files like PS3_DISC.SFB).</summary>
    FolderBased,

    /// <summary>Hybrid or converted formats (.pbp, .nkit, .rvz, .wbfs).
    /// Container-specific handling required.</summary>
    Hybrid,

    /// <summary>Platform family not determined.</summary>
    Unknown,
}
