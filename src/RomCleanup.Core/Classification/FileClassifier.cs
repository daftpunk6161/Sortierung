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
    // BUG-FIX: Added RegexTimeout to all patterns to prevent ReDoS on malicious filenames.

    private static readonly TimeSpan RxTimeout = TimeSpan.FromMilliseconds(500);

    private static readonly Regex RxBios = new(
        @"\((bios|firmware)\)|\[bios\]|^\s*bios\b",
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

    public sealed record ClassificationDecision(FileCategory Category, int Confidence, string ReasonCode);

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
    {
        // V2-BUG-M05: Return Unknown for empty inputs instead of misclassifying as Game
        if (string.IsNullOrWhiteSpace(baseName))
            return new ClassificationDecision(FileCategory.Unknown, 5, "empty-basename");

        // 1. BIOS — highest priority
        if (RxBios.IsMatch(baseName))
            return new ClassificationDecision(FileCategory.Bios, 98, "bios-tag");

        // 2. Standard junk tags (parenthesized/bracketed)
        if (RxJunkTags.IsMatch(baseName))
            return new ClassificationDecision(FileCategory.Junk, 95, "junk-tag");

        // 3. Standard junk words (unparenthesized keywords)
        if (RxJunkWords.IsMatch(baseName))
            return new ClassificationDecision(FileCategory.Junk, 90, "junk-word");

        // 3b. Explicit non-game software/content
        if (RxNonGameTags.IsMatch(baseName))
            return new ClassificationDecision(FileCategory.NonGame, 85, "non-game-tag");

        if (RxNonGameWords.IsMatch(baseName))
            return new ClassificationDecision(FileCategory.NonGame, 75, "non-game-word");

        // 4. Aggressive junk (only when enabled)
        if (aggressiveJunk)
        {
            if (RxJunkTagsAggressive.IsMatch(baseName))
                return new ClassificationDecision(FileCategory.Junk, 88, "junk-aggressive-tag");

            if (RxJunkWordsAggressive.IsMatch(baseName))
                return new ClassificationDecision(FileCategory.Junk, 82, "junk-aggressive-word");
        }

        return new ClassificationDecision(FileCategory.Game, 90, "game-default");
    }
}
