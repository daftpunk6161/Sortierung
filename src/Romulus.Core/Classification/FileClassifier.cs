using System.Text.RegularExpressions;
using Romulus.Contracts.Models;

namespace Romulus.Core.Classification;

/// <summary>
/// Classifies ROM filenames into GAME, BIOS or JUNK categories.
/// Pure function — no I/O, no state. Mirrors Get-FileCategory from Classification.ps1.
/// </summary>
public static class FileClassifier
{
    // Patterns from data/rules.json — compiled once for performance.
    // Order matters: BIOS checked first, then standard junk, then aggressive.
    // BUG-FIX: Added RegexTimeout to all patterns to prevent ReDoS on malicious filenames.

    private static readonly TimeSpan RxTimeout = SafeRegex.DefaultTimeout;

    private static readonly Regex RxBios = new(
        @"\((bios|firmware)\)|\[bios\]|^\s*bios(?:\s|_|-|\.|\d|$)"
        + @"|\b(?:gba|dc|psx|ps1|ps2|nds?|saturn|sega_?cd|segacd|genesis|megadrive|n64|lynx|jaguar|turbografx|atari7800)_bios\b"
        + @"|\bscph[-_ ]?\d{3,6}\b"
        + @"|\bsyscard[123]\b"
        + @"|\b(?:cps2|neo\s*geo|sega\s*saturn|atari\s*jaguar|3do)\s+bios\b"
        + @"|\bboot[._ -]?rom\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, RxTimeout);

    private static readonly Regex RxJunkTags = new(
        @"\((alpha\s*\d*|beta\s*\d*|proto(?:type)?\s*\d*|sample|sampler|demo|preview|pre[\s-]*release|promo|kiosk(?:\s*demo)?|debug|trial(?:\s*version)?|taikenban|rehearsal-?\s*ban|location\s*test|test\s*program)\)"
        + @"|\((program|application|utility|enhancement\s*chip|test\s*program|test\s*cartridge)\)"
        + @"|\((competition\s*cart|service\s*disc|diagnostic|check\s*program)\)"
        + @"|\((hack|pirate|bootleg|homebrew|aftermarket|translated|translation)\)"
        + @"|\((unl|unlicensed)\)"
        + @"|\((not\s*for\s*resale|nfr)\)"
        + @"|\[(b\d*|h\d*|p\d*|t\d*|f\d*|o\d*)\]"
        + @"|\[(cr|tr|m)\s",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, RxTimeout);

    private static readonly Regex RxJunkWords = new(
        @"\b(demo|sample\s*version|trial\s*version|trial|pre[\s-]*release|not\s*for\s*resale|sampler|bootleg\s*sampler)\b"
        + @"|^gamelist(?:\.xml)?(?:\.old|\.bak)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, RxTimeout);

    private static readonly Regex RxJunkTagsAggressive = new(
        @"\((wip|work\s*in\s*progress|playtest|test\s*build|dev\s*build|qa\s*build|review\s*build|internal\s*build|preview\s*build|prototype\s*build|not\s*for\s*distribution)\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, RxTimeout);

    private static readonly Regex RxJunkWordsAggressive = new(
        @"\b(work\s*in\s*progress|wip|playtest|test\s*build|dev\s*build|qa\s*build|review\s*build|internal\s*build|preview\s*build|not\s*for\s*distribution)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, RxTimeout);

    private static readonly Regex RxNonGameTags = new(
        @"\((driver|tool|tools|utility|utilities|editor|assembler|compiler|monitor|debugger|devkit|sdk|workbench|workbench\s*disk|system\s*disk|operating\s*system|os|desktop|database|encyclopedia|reference|manual|documentation|docs|guide|tutorial|music\s*disk|sound\s*tool|tracker|composer|paint|drawing|office|word\s*processor|spreadsheet|terminal|shell|diagnostic)\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, RxTimeout);

    private static readonly Regex RxNonGameWords = new(
        @"\b(driver|utility|utilities|tool|editor|workbench|operating\s*system|encyclopedia|reference|manual|documentation|tutorial|music\s*disk|tracker|composer|word\s*processor|spreadsheet|database|desktop\s*publishing|paint\s*program)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, RxTimeout);

    private static readonly Regex RxGenericRawBinaryName = new(
        @"^(?:data|track|trk|disc|disk|cd|dvd|session|file|rom|image|audio|video|part|chunk|unknown|backup|dump)[ _-]*\d*[a-z]?$|^\d+$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, RxTimeout);

    // Conservative allow-deny prefilter: extensions that are clearly non-ROM/user-content.
    private static readonly HashSet<string> NonRomExtensions =
    [
        ".txt", ".md", ".rtf", ".pdf", ".doc", ".docx",
        ".json", ".yaml", ".yml", ".xml", ".ini", ".cfg", ".conf", ".log",
        ".ps1", ".psm1", ".bat", ".cmd", ".sh", ".py", ".js", ".ts",
        ".html", ".htm", ".exe", ".dll", ".msi",
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg",
        ".nfo", ".diz", ".url", ".lnk"
    ];

    private static readonly HashSet<string> LowSignalBinaryExtensions =
    [
        ".bin", ".img", ".iso", ".mdf", ".mds", ".ccd", ".cue", ".gdi", ".sub", ".nrg", ".cdi"
    ];

    /// <summary>
    /// Classification output contract.
    /// Confidence is a 0..100 heuristic certainty score where higher means stronger evidence.
    /// ReasonCode is machine-readable and stable for reporting/telemetry branching.
    /// </summary>
    public sealed record ClassificationDecision(FileCategory Category, int Confidence, string ReasonCode);

    /// <summary>
    /// Checks whether the given extension is a known non-ROM file type.
    /// Used for scan-phase pre-filtering.
    /// </summary>
    public static bool IsNonRomExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return false;
        var normalized = extension.StartsWith(".", StringComparison.Ordinal)
            ? extension.ToLowerInvariant()
            : "." + extension.ToLowerInvariant();
        return NonRomExtensions.Contains(normalized);
    }

    /// <summary>
    /// Classifies a ROM filename (without extension) into GAME, BIOS or JUNK.
    /// </summary>
    /// <param name="baseName">Filename without extension (e.g. "Super Mario (Europe) (Demo)").</param>
    /// <param name="aggressiveJunk">When true, additional patterns flag WIP/dev builds as JUNK.</param>
    /// <returns>The file category.</returns>
    public static FileCategory Classify(string baseName, bool aggressiveJunk = false)
        => Analyze(baseName, aggressiveJunk).Category;

    /// <summary>
    /// Returns classification category plus confidence and a machine-readable reason code.
    /// </summary>
    public static ClassificationDecision Analyze(string baseName, bool aggressiveJunk = false)
        => Analyze(baseName, extension: null, sizeBytes: null, aggressiveJunk: aggressiveJunk);

    /// <summary>
    /// Returns classification category plus confidence and a machine-readable reason code.
    /// Optional extension and size allow scan-level prefilter hardening.
    /// </summary>
    public static ClassificationDecision Analyze(string baseName, string? extension, long? sizeBytes, bool aggressiveJunk = false)
    {
        // Scan-level hard gate: known non-ROM file types should not pass as GAME.
        if (!string.IsNullOrWhiteSpace(extension))
        {
            var normalizedExt = extension.StartsWith(".", StringComparison.Ordinal)
                ? extension.ToLowerInvariant()
                : "." + extension.ToLowerInvariant();

            if (NonRomExtensions.Contains(normalizedExt))
                return new ClassificationDecision(FileCategory.NonGame, 98, "non-rom-extension");

            // Conservative raw-binary gate: generic track/data image names should
            // not become synthetic GAME entries without any positive evidence.
            if (LowSignalBinaryExtensions.Contains(normalizedExt) &&
                SafeRegex.IsMatch(RxGenericRawBinaryName, baseName))
            {
                return new ClassificationDecision(FileCategory.Unknown, 35, "generic-raw-binary");
            }
        }

        // Empty files are never valid game candidates.
        if (sizeBytes.HasValue && sizeBytes.Value == 0)
            return new ClassificationDecision(FileCategory.NonGame, 99, "empty-file");

        // V2-BUG-M05: Return Unknown for empty inputs instead of misclassifying as Game
        if (string.IsNullOrWhiteSpace(baseName))
            return new ClassificationDecision(FileCategory.Unknown, 5, "empty-basename");

        // 1. BIOS — highest priority
        if (SafeRegex.IsMatch(RxBios, baseName))
            return new ClassificationDecision(FileCategory.Bios, 98, "bios-tag");

        // 2. Standard junk tags (parenthesized/bracketed)
        if (SafeRegex.IsMatch(RxJunkTags, baseName))
            return new ClassificationDecision(FileCategory.Junk, 95, "junk-tag");

        // 3. Standard junk words (unparenthesized keywords)
        if (SafeRegex.IsMatch(RxJunkWords, baseName))
            return new ClassificationDecision(FileCategory.Junk, 90, "junk-word");

        // 3b. Explicit non-game software/content
        if (SafeRegex.IsMatch(RxNonGameTags, baseName))
            return new ClassificationDecision(FileCategory.NonGame, 85, "non-game-tag");

        if (SafeRegex.IsMatch(RxNonGameWords, baseName))
            return new ClassificationDecision(FileCategory.NonGame, 75, "non-game-word");

        // 4. Aggressive junk (only when enabled)
        if (aggressiveJunk)
        {
            if (SafeRegex.IsMatch(RxJunkTagsAggressive, baseName))
                return new ClassificationDecision(FileCategory.Junk, 88, "junk-aggressive-tag");

            if (SafeRegex.IsMatch(RxJunkWordsAggressive, baseName))
                return new ClassificationDecision(FileCategory.Junk, 82, "junk-aggressive-word");
        }

        return new ClassificationDecision(FileCategory.Game, 75, "game-default");
    }
}
