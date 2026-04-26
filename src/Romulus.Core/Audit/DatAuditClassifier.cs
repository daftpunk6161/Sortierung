using Romulus.Contracts;
using Romulus.Contracts.Models;

namespace Romulus.Core.Audit;

/// <summary>
/// Result of DAT audit classification including matched entry details.
/// </summary>
public readonly record struct DatAuditClassifyResult(
    DatAuditStatus Status,
    string? DatGameName,
    string? DatRomFileName,
    string? ResolvedConsoleKey);

/// <summary>
/// Pure DAT audit status classifier.
/// </summary>
public static partial class DatAuditClassifier
{
    /// <summary>
    /// Classifies a ROM against the DAT index and returns matched entry details.
    /// Tries headerlessHash first (for NES/SNES/7800/Lynx), then falls back to regular hash.
    /// </summary>
    public static DatAuditClassifyResult ClassifyFull(
        string? hash,
        string? headerlessHash,
        string actualFileName,
        string? consoleKey,
        DatIndex datIndex)
        => ClassifyFull(hash, headerlessHash, actualFileName, consoleKey, datIndex, "SHA1");

    public static DatAuditClassifyResult ClassifyFull(
        string? hash,
        string? headerlessHash,
        string actualFileName,
        string? consoleKey,
        DatIndex datIndex,
        string hashType)
    {
        ArgumentNullException.ThrowIfNull(actualFileName);
        ArgumentNullException.ThrowIfNull(datIndex);

        // Try headerless hash first (some DATs use headerless hashes)
        if (!string.IsNullOrWhiteSpace(headerlessHash))
        {
            var result = ClassifyWithHashFull(headerlessHash, actualFileName, consoleKey, datIndex, hashType);
            if (result.Status is DatAuditStatus.Have or DatAuditStatus.HaveWrongName or DatAuditStatus.HaveByName or DatAuditStatus.Ambiguous)
                return result;
            // On Miss or Unknown: fall through to try regular (headered) hash,
            // because the DAT may store headered SHA1 rather than headerless SHA1.
        }

        // Fall back to regular hash (headered or container hash)
        return ClassifyWithHashFull(hash, actualFileName, consoleKey, datIndex, hashType);
    }

    /// <summary>
    /// Classifies a ROM against the DAT index.
    /// Tries headerlessHash first (for NES/SNES/7800/Lynx), then falls back to regular hash.
    /// </summary>
    public static DatAuditStatus Classify(
        string? hash,
        string? headerlessHash,
        string actualFileName,
        string? consoleKey,
        DatIndex datIndex)
        => ClassifyFull(hash, headerlessHash, actualFileName, consoleKey, datIndex).Status;

    /// <summary>Backward-compatible overload without headerlessHash.</summary>
    public static DatAuditStatus Classify(
        string? hash,
        string actualFileName,
        string? consoleKey,
        DatIndex datIndex)
        => Classify(hash, headerlessHash: null, actualFileName, consoleKey, datIndex);

    private static DatAuditClassifyResult ClassifyWithHashFull(
        string? hash,
        string actualFileName,
        string? consoleKey,
        DatIndex datIndex,
        string hashType)
    {
        if (string.IsNullOrWhiteSpace(hash))
            return new(DatAuditStatus.Unknown, null, null, consoleKey);

        if (IsRealConsoleKey(consoleKey))
        {
            var inConsole = datIndex.LookupWithFilename(consoleKey!, hashType, hash);
            if (inConsole is null)
            {
                if (TryOpticalNameFallbackForConsole(consoleKey!, actualFileName, datIndex, out var fallbackMatch))
                {
                    // F-DAT-03: optical name fallback never verified the hash; surface as HaveByName.
                    return new(DatAuditStatus.HaveByName, fallbackMatch.GameName, fallbackMatch.RomFileName, consoleKey);
                }

                // Miss = DAT loaded for this console but hash not found
                // Unknown = no DAT loaded for this console at all
                var status = datIndex.HasConsole(consoleKey!)
                    ? DatAuditStatus.Miss
                    : DatAuditStatus.Unknown;
                return new(status, null, null, consoleKey);
            }

            var nameStatus = IsSameFileName(actualFileName, inConsole.Value)
                ? DatAuditStatus.Have
                : DatAuditStatus.HaveWrongName;
            return new(nameStatus, inConsole.Value.GameName, inConsole.Value.RomFileName, consoleKey);
        }

        var matches = datIndex.LookupAllByHash(hashType, hash)
            .Where(static match => IsRealConsoleKey(match.ConsoleKey))
            .ToArray();
        if (matches.Length == 0)
        {
            if (TryOpticalNameFallbackCrossConsole(actualFileName, datIndex, out var fallbackConsole, out var fallbackEntry))
            {
                // F-DAT-03: cross-console optical name fallback also lacks hash verification.
                return new(DatAuditStatus.HaveByName, fallbackEntry.GameName, fallbackEntry.RomFileName, fallbackConsole);
            }

            return new(DatAuditStatus.Unknown, null, null, consoleKey);
        }

        if (matches.Length > 1)
            return new(DatAuditStatus.Ambiguous, null, null, consoleKey);

        var single = matches[0];
        var singleStatus = IsSameFileName(actualFileName, single.Entry)
            ? DatAuditStatus.Have
            : DatAuditStatus.HaveWrongName;
        return new(singleStatus, single.Entry.GameName, single.Entry.RomFileName, single.ConsoleKey);
    }

    /// <summary>
    /// Checks whether UNKNOWN/AMBIGUOUS sentinel values are real console keys.
    /// These are detection sentinels, not actual console identifiers in the DAT index.
    /// </summary>
    private static bool IsRealConsoleKey(string? consoleKey)
        => !string.IsNullOrWhiteSpace(consoleKey)
           && !string.Equals(consoleKey, "UNKNOWN", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(consoleKey, "AMBIGUOUS", StringComparison.OrdinalIgnoreCase);

    private static bool IsSameFileName(string actualFileName, DatIndex.DatIndexEntry entry)
    {
        // If the DAT entry has no ROM filename, treat any file as a name match.
        // This occurs for DATs that only record game names without individual ROM filenames.
        if (string.IsNullOrWhiteSpace(entry.RomFileName))
            return true;

        var actual = Path.GetFileName(actualFileName);
        var actualStem = NormalizeComparableStem(Path.GetFileNameWithoutExtension(actual));

        foreach (var candidate in GetComparableNames(entry))
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            var expected = Path.GetFileName(candidate);
            if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                return true;

            var expectedStem = NormalizeComparableStem(Path.GetFileNameWithoutExtension(expected));
            if (string.Equals(actualStem, expectedStem, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool TryOpticalNameFallbackForConsole(
        string consoleKey,
        string actualFileName,
        DatIndex datIndex,
        out DatIndex.DatIndexEntry match)
    {
        match = default;

        if (!CanUseOpticalNameFallback(actualFileName) || !datIndex.HasConsole(consoleKey))
            return false;

        var candidates = datIndex.GetConsoleEntriesDetailed(consoleKey)
            .Select(static item => item.Entry)
            .Where(entry => IsSameFileName(actualFileName, entry))
            .Take(2)
            .ToArray();

        if (candidates.Length != 1)
            return false;

        match = candidates[0];
        return true;
    }

    private static bool TryOpticalNameFallbackCrossConsole(
        string actualFileName,
        DatIndex datIndex,
        out string resolvedConsoleKey,
        out DatIndex.DatIndexEntry resolvedEntry)
    {
        resolvedConsoleKey = string.Empty;
        resolvedEntry = default;

        if (!CanUseOpticalNameFallback(actualFileName))
            return false;

        var candidates = new List<(string ConsoleKey, DatIndex.DatIndexEntry Entry)>();
        foreach (var console in datIndex.ConsoleKeys.Where(static key => IsRealConsoleKey(key)))
        {
            var perConsoleMatches = datIndex.GetConsoleEntriesDetailed(console)
                .Select(static item => item.Entry)
                .Where(entry => IsSameFileName(actualFileName, entry))
                .Take(2)
                .ToArray();

            if (perConsoleMatches.Length == 1)
                candidates.Add((console, perConsoleMatches[0]));

            if (candidates.Count > 1)
                return false;
        }

        if (candidates.Count != 1)
            return false;

        resolvedConsoleKey = candidates[0].ConsoleKey;
        resolvedEntry = candidates[0].Entry;
        return true;
    }

    private static bool CanUseOpticalNameFallback(string actualFileName)
    {
        if (string.IsNullOrWhiteSpace(actualFileName))
            return false;

        var extension = Path.GetExtension(actualFileName);
        if (!DiscFormats.IsDatNameOnlyExtensionWithoutBin(extension))
            return false;

        var stem = NormalizeComparableStem(Path.GetFileNameWithoutExtension(actualFileName));
        if (stem.Length < 6)
            return false;

        return !GenericNameStemRegex().IsMatch(stem);
    }

    private static IEnumerable<string?> GetComparableNames(DatIndex.DatIndexEntry entry)
    {
        yield return entry.RomFileName;
        yield return entry.GameName;
        yield return entry.ParentGameName;
    }

    private static string NormalizeComparableStem(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim();
        normalized = DiscSuffixRegex().Replace(normalized, string.Empty).Trim();
        normalized = TrackSuffixRegex().Replace(normalized, string.Empty).Trim();
        return normalized;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\s*\((disc|disk|cd)\s*\d+\)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex DiscSuffixRegex();

    [System.Text.RegularExpressions.GeneratedRegex(@"\s*\(track\s*\d+\)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex TrackSuffixRegex();

    [System.Text.RegularExpressions.GeneratedRegex(@"^(track|disc|disk|cd|dvd|rom)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex GenericNameStemRegex();
}
