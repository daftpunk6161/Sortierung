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
public static class DatAuditClassifier
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
    {
        ArgumentNullException.ThrowIfNull(actualFileName);
        ArgumentNullException.ThrowIfNull(datIndex);

        // Try headerless hash first (some DATs use headerless hashes)
        if (!string.IsNullOrWhiteSpace(headerlessHash))
        {
            var result = ClassifyWithHashFull(headerlessHash, actualFileName, consoleKey, datIndex);
            if (result.Status is DatAuditStatus.Have or DatAuditStatus.HaveWrongName or DatAuditStatus.Ambiguous)
                return result;
            // On Miss or Unknown: fall through to try regular (headered) hash,
            // because the DAT may store headered SHA1 rather than headerless SHA1.
        }

        // Fall back to regular hash (headered or container hash)
        return ClassifyWithHashFull(hash, actualFileName, consoleKey, datIndex);
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
        DatIndex datIndex)
    {
        if (string.IsNullOrWhiteSpace(hash))
            return new(DatAuditStatus.Unknown, null, null, consoleKey);

        if (IsRealConsoleKey(consoleKey))
        {
            var inConsole = datIndex.LookupWithFilename(consoleKey!, hash);
            if (inConsole is null)
            {
                // Miss = DAT loaded for this console but hash not found
                // Unknown = no DAT loaded for this console at all
                var status = datIndex.HasConsole(consoleKey!)
                    ? DatAuditStatus.Miss
                    : DatAuditStatus.Unknown;
                return new(status, null, null, consoleKey);
            }

            var nameStatus = IsSameFileName(actualFileName, inConsole.Value.RomFileName)
                ? DatAuditStatus.Have
                : DatAuditStatus.HaveWrongName;
            return new(nameStatus, inConsole.Value.GameName, inConsole.Value.RomFileName, consoleKey);
        }

        var matches = datIndex.LookupAllByHash(hash);
        if (matches.Count == 0)
            return new(DatAuditStatus.Unknown, null, null, consoleKey);

        if (matches.Count > 1)
            return new(DatAuditStatus.Ambiguous, null, null, consoleKey);

        var single = matches[0];
        var singleStatus = IsSameFileName(actualFileName, single.Entry.RomFileName)
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

    private static bool IsSameFileName(string actualFileName, string? datRomFileName)
    {
        if (string.IsNullOrWhiteSpace(datRomFileName))
            return true;

        var actual = Path.GetFileName(actualFileName);
        var expected = Path.GetFileName(datRomFileName);

        // Exact match (same name + extension)
        if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            return true;

        // Stem match: handles archive containers ("Game.zip" vs "Game.nes")
        // and format variants ("Game.chd" vs "Game (Track 01).bin")
        return string.Equals(
            Path.GetFileNameWithoutExtension(actual),
            Path.GetFileNameWithoutExtension(expected),
            StringComparison.OrdinalIgnoreCase);
    }
}
