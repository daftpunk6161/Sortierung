using System.Text.RegularExpressions;

namespace Romulus.Core.Classification;

/// <summary>
/// Detects console type from filename patterns: serial numbers and system keywords.
/// Serial numbers like SLUS-00123 definitively identify PlayStation, BCUS identifies PS3, etc.
/// Thread-safe (stateless, compiled regex).
/// </summary>
public sealed class FilenameConsoleAnalyzer
{
    private static readonly TimeSpan RxTimeout = SafeRegex.ShortTimeout;

    /// <summary>
    /// Serial number prefix → console key mappings.
    /// Sources: redump.org serial conventions, No-Intro naming.
    /// </summary>
    private static readonly (Regex Pattern, string ConsoleKey)[] SerialPatterns =
    [
        // PlayStation 1 — exclusive prefixes (never PS2)
        (Rx(@"\b(SIPS|PAPX)-\d{3,5}\b"), "PS1"),

        // PlayStation 1 — shared prefixes: 3-4 digit serials, or 5-digit starting with 0-1 (00xxx-19xxx)
        (Rx(@"\b(SLUS|SCUS|SLPS|SCPS|SLPM|SCES|SLES|SLKA)-(\d{3,4}|[01]\d{4})\b"), "PS1"),

        // PlayStation 2 — exclusive prefix (never PS1)
        (Rx(@"\bPBPX-\d{5}\b"), "PS2"),

        // PlayStation 2 — shared prefixes: 5-digit starting with 2-9 (20xxx+)
        (Rx(@"\b(SLUS|SCUS|SLPS|SCPS|SLPM|SCES|SLES|SLKA)-[2-9]\d{4}\b"), "PS2"),

        // PlayStation 3
        (Rx(@"\b(BCUS|BLUS|BCES|BLES|BCJS|BLJS|BCAS|BLAS|BLJM|NPUB|NPEB|NPJB)-\d{5}\b"), "PS3"),

        // PSP
        (Rx(@"\b(UCUS|ULUS|UCES|ULES|UCJS|ULJS|UCAS|ULAS|NPUH|NPEH|NPJH)-\d{5}\b"), "PSP"),

        // PS Vita
        (Rx(@"\b(PCSE|PCSB|PCSA|PCSG|PCSH|PCSD)-\d{5}\b"), "VITA"),

        // GameCube
        (Rx(@"\b[A-Z0-9]{3}[EPJDKW]\d{2}\b"), "GC"),

        // Sega Saturn — has T-prefix serials like T-1001G
        (Rx(@"\bT-\d{3,4}[A-Z]\b"), "SAT"),

        // Nintendo DS
        (Rx(@"\bNTR-[A-Z0-9]{4}-[A-Z]{3}\b"), "NDS"),

        // Nintendo 3DS
        (Rx(@"\bCTR-[A-Z0-9]{4}-[A-Z]{3}\b"), "3DS"),

        // Xbox 360
        (Rx(@"\b\d{2}-\d{5}\b"), "X360"),
    ];

    /// <summary>
    /// Broader heuristic: detect console by known system keywords in the filename.
    /// Lower confidence than serial numbers.
    /// </summary>
    private static readonly (Regex Pattern, string ConsoleKey)[] KeywordPatterns =
    [
        (Rx(@"\[PS1\]|\(PS1\)"), "PS1"),
        (Rx(@"\[PS2\]|\(PS2\)"), "PS2"),
        (Rx(@"\[PS3\]|\(PS3\)"), "PS3"),
        (Rx(@"\[PSP\]|\(PSP\)"), "PSP"),
        (Rx(@"\[Vita\]|\(Vita\)"), "VITA"),
        (Rx(@"\[GC\]|\(GC\)|\[GameCube\]|\(GameCube\)"), "GC"),
        (Rx(@"\[Wii\]|\(Wii\)"), "WII"),
        (Rx(@"\[NDS\]|\(NDS\)|\[Nintendo DS\]|\(Nintendo DS\)"), "NDS"),
        (Rx(@"\[3DS\]|\(3DS\)|\[Nintendo 3DS\]|\(Nintendo 3DS\)"), "3DS"),
        (Rx(@"\[N64\]|\(N64\)"), "N64"),
        (Rx(@"\[SNES\]|\(SNES\)|\[Super Nintendo\]|\(Super Nintendo\)"), "SNES"),
        (Rx(@"\[NES\]|\(NES\)|\[Famicom\]|\(Famicom\)"), "NES"),
        (Rx(@"\[GBA\]|\(GBA\)|\[Game Boy Advance\]|\(Game Boy Advance\)"), "GBA"),
        (Rx(@"\[GBC\]|\(GBC\)|\[Game Boy Color\]|\(Game Boy Color\)"), "GBC"),
        (Rx(@"\[GB\]|\(GB\)|\[Game Boy\]|\(Game Boy\)"), "GB"),
        (Rx(@"\[MD\]|\(MD\)|\[Genesis\]|\(Genesis\)|\[Mega Drive\]|\(Mega Drive\)"), "MD"),
        (Rx(@"\[SMS\]|\(SMS\)|\[Master System\]|\(Master System\)"), "SMS"),
        (Rx(@"\[GG\]|\(GG\)|\[Game Gear\]|\(Game Gear\)"), "GG"),
        (Rx(@"\[DC\]|\(DC\)|\[Dreamcast\]|\(Dreamcast\)"), "DC"),
        (Rx(@"\[SAT\]|\(SAT\)|\[Saturn\]|\(Saturn\)"), "SAT"),
        (Rx(@"\[SCD\]|\(SCD\)|\[Sega CD\]|\(Sega CD\)|\[Mega CD\]|\(Mega CD\)"), "SCD"),
        (Rx(@"\[PCE\]|\(PCE\)|\[PC Engine\]|\(PC Engine\)|\[TurboGrafx\]|\(TurboGrafx\)"), "PCE"),
        (Rx(@"\[Neo Geo\]|\(Neo Geo\)|\[NeoGeo\]|\(NeoGeo\)"), "NEOGEO"),
        (Rx(@"\[MAME\]|\(MAME\)|\[Arcade\]|\(Arcade\)|\[FBNeo\]|\(FBNeo\)"), "ARCADE"),
        (Rx(@"\[Switch\]|\(Switch\)|\[NSW\]|\(NSW\)"), "SWITCH"),
        (Rx(@"\[Xbox\]|\(Xbox\)"), "XBOX"),
        (Rx(@"\[Xbox 360\]|\(Xbox 360\)"), "X360"),
        (Rx(@"\[32X\]|\(32X\)"), "32X"),
        (Rx(@"\[Lynx\]|\(Lynx\)"), "LYNX"),
        (Rx(@"\[Jaguar\]|\(Jaguar\)"), "JAG"),
    ];

    /// <summary>
    /// Try to detect console from serial number patterns in the filename.
    /// Returns (consoleKey, confidence) or null if no match.
    /// Serial numbers are high-confidence (95).
    /// </summary>
    public static (string ConsoleKey, int Confidence)? DetectBySerial(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        // Keep deterministic first-match behavior for overlapping serial families.
        foreach (var (pattern, key) in SerialPatterns)
        {
            try
            {
                if (pattern.IsMatch(fileName))
                    return (key, 95);
            }
            catch (RegexMatchTimeoutException) { }
        }

        return null;
    }

    /// <summary>
    /// Try to detect console from system keyword tags in the filename (e.g. "[PS1]", "(GBA)").
    /// Returns (consoleKey, confidence) or null if no match.
    /// Keywords are medium-confidence (75).
    /// </summary>
    public static (string ConsoleKey, int Confidence)? DetectByKeyword(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        foreach (var (pattern, key) in KeywordPatterns)
        {
            try
            {
                if (pattern.IsMatch(fileName))
                    return (key, 75);
            }
            catch (RegexMatchTimeoutException) { }
        }

        return null;
    }

    /// <summary>
    /// Combined detection: try serial first (high confidence), then keywords (medium).
    /// </summary>
    public static (string ConsoleKey, int Confidence)? Detect(string fileName)
    {
        return DetectBySerial(fileName) ?? DetectByKeyword(fileName);
    }

    private static Regex Rx(string pattern) =>
        new(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase, RxTimeout);
}
