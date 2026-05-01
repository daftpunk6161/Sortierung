namespace Romulus.Tests.Benchmark.Infrastructure;

/// <summary>
/// Maps a consoleKey from consoles.json to one of five canonical platform families.
/// Used by CoverageValidator to enforce per-family gate thresholds.
/// </summary>
internal static class PlatformFamilyClassifier
{
    public enum PlatformFamily
    {
        Cartridge,
        Disc,
        Arcade,
        Computer,
        Hybrid
    }

    // Explicit arcade systems (folder-only detection, MAME/FBNeo-based)
    private static readonly HashSet<string> ArcadeSystems = new(StringComparer.OrdinalIgnoreCase)
    {
        "ARCADE", "NEOGEO"
    };

    // Computer systems (home computers, not game-first consoles)
    private static readonly HashSet<string> ComputerSystems = new(StringComparer.OrdinalIgnoreCase)
    {
        "A800", "AMIGA", "ATARIST", "C64", "CPC", "DOS", "MSX", "PC98", "X68K", "ZX"
    };

    // Hybrid systems (physical media + digital distribution, mixed form factors)
    private static readonly HashSet<string> HybridSystems = new(StringComparer.OrdinalIgnoreCase)
    {
        "3DS", "PSP", "SWITCH", "VITA", "WIIU"
    };

    // Disc-based systems determined dynamically from consoles.json discBased flag
    // minus the arcade/computer/hybrid exclusions
    private static readonly HashSet<string> DiscOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        "3DO", "CD32", "CDI", "DC", "FMTOWNS", "GC", "JAGCD", "NEOCD",
        "PCECD", "PCFX", "PS1", "PS2", "PS3", "SAT", "SCD", "WII", "X360", "XBOX"
    };

    public static PlatformFamily Classify(string consoleKey)
    {
        if (string.IsNullOrEmpty(consoleKey))
            return PlatformFamily.Cartridge;

        if (ArcadeSystems.Contains(consoleKey))
            return PlatformFamily.Arcade;

        if (ComputerSystems.Contains(consoleKey))
            return PlatformFamily.Computer;

        if (HybridSystems.Contains(consoleKey))
            return PlatformFamily.Hybrid;

        if (DiscOverrides.Contains(consoleKey))
            return PlatformFamily.Disc;

        // Default: cartridge (all remaining non-disc, non-special systems)
        return PlatformFamily.Cartridge;
    }

    public static string FamilyName(PlatformFamily family) => family switch
    {
        PlatformFamily.Cartridge => "cartridge",
        PlatformFamily.Disc => "disc",
        PlatformFamily.Arcade => "arcade",
        PlatformFamily.Computer => "computer",
        PlatformFamily.Hybrid => "hybrid",
        _ => "unknown"
    };
}
