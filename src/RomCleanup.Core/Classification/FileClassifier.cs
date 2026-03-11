using System.Text.RegularExpressions;
using RomCleanup.Contracts.Models;

namespace RomCleanup.Core.Classification;

/// <summary>
/// Classifies ROM filenames into GAME, BIOS or JUNK categories.
/// Pure function — no I/O, no state. Mirrors Get-FileCategory from Classification.ps1.
/// </summary>
public static class FileClassifier
{
    // Patterns from data/rules.json — compiled once for performance.
    // Order matters: BIOS checked first, then standard junk, then aggressive.

    private static readonly Regex RxBios = new(
        @"\((bios|firmware)\)|\[bios\]|^\s*bios\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RxJunkTags = new(
        @"\((alpha\s*\d*|beta\s*\d*|proto(?:type)?\s*\d*|sample|sampler|demo|preview|pre[\s-]*release|promo|kiosk(?:\s*demo)?|debug|trial(?:\s*version)?|taikenban|rehearsal-?\s*ban|location\s*test|test\s*program)\)"
        + @"|\((program|application|utility|enhancement\s*chip|test\s*program|test\s*cartridge)\)"
        + @"|\((competition\s*cart|service\s*disc|diagnostic|check\s*program)\)"
        + @"|\((hack|pirate|bootleg|homebrew|aftermarket|translated|translation)\)"
        + @"|\((unl|unlicensed)\)"
        + @"|\((not\s*for\s*resale|nfr)\)"
        + @"|\[(b\d*|h\d*|p\d*|t\d*|f\d*|o\d*)\]"
        + @"|\[(cr|tr|m)\s",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RxJunkWords = new(
        @"\b(demo|sample\s*version|trial\s*version|trial|pre[\s-]*release|not\s*for\s*resale|sampler|bootleg\s*sampler)\b"
        + @"|^gamelist(?:\.xml)?(?:\.old|\.bak)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RxJunkTagsAggressive = new(
        @"\((wip|work\s*in\s*progress|playtest|test\s*build|dev\s*build|qa\s*build|review\s*build|internal\s*build|preview\s*build|prototype\s*build|not\s*for\s*distribution)\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RxJunkWordsAggressive = new(
        @"\b(work\s*in\s*progress|wip|playtest|test\s*build|dev\s*build|qa\s*build|review\s*build|internal\s*build|preview\s*build|not\s*for\s*distribution)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Classifies a ROM filename (without extension) into GAME, BIOS or JUNK.
    /// </summary>
    /// <param name="baseName">Filename without extension (e.g. "Super Mario (Europe) (Demo)").</param>
    /// <param name="aggressiveJunk">When true, additional patterns flag WIP/dev builds as JUNK.</param>
    /// <returns>The file category.</returns>
    public static FileCategory Classify(string baseName, bool aggressiveJunk = false)
    {
        if (string.IsNullOrWhiteSpace(baseName))
            return FileCategory.Game;

        // 1. BIOS — highest priority
        if (RxBios.IsMatch(baseName))
            return FileCategory.Bios;

        // 2. Standard junk tags (parenthesized/bracketed)
        if (RxJunkTags.IsMatch(baseName))
            return FileCategory.Junk;

        // 3. Standard junk words (unparenthesized keywords)
        if (RxJunkWords.IsMatch(baseName))
            return FileCategory.Junk;

        // 4. Aggressive junk (only when enabled)
        if (aggressiveJunk)
        {
            if (RxJunkTagsAggressive.IsMatch(baseName))
                return FileCategory.Junk;

            if (RxJunkWordsAggressive.IsMatch(baseName))
                return FileCategory.Junk;
        }

        return FileCategory.Game;
    }
}
