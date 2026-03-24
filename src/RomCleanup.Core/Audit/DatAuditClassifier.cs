using RomCleanup.Contracts.Models;

namespace RomCleanup.Core.Audit;

/// <summary>
/// Pure DAT audit status classifier.
/// </summary>
public static class DatAuditClassifier
{
    public static DatAuditStatus Classify(
        string? hash,
        string actualFileName,
        string? consoleKey,
        DatIndex datIndex)
    {
        ArgumentNullException.ThrowIfNull(actualFileName);
        ArgumentNullException.ThrowIfNull(datIndex);

        if (string.IsNullOrWhiteSpace(hash))
            return DatAuditStatus.Unknown;

        if (!string.IsNullOrWhiteSpace(consoleKey))
        {
            var inConsole = datIndex.LookupWithFilename(consoleKey, hash);
            if (inConsole is null)
                return DatAuditStatus.Miss;

            return IsSameFileName(actualFileName, inConsole.Value.RomFileName)
                ? DatAuditStatus.Have
                : DatAuditStatus.HaveWrongName;
        }

        var matches = datIndex.LookupAllByHash(hash);
        if (matches.Count == 0)
            return DatAuditStatus.Unknown;

        if (matches.Count > 1)
            return DatAuditStatus.Ambiguous;

        return IsSameFileName(actualFileName, matches[0].Entry.RomFileName)
            ? DatAuditStatus.Have
            : DatAuditStatus.HaveWrongName;
    }

    private static bool IsSameFileName(string actualFileName, string? datRomFileName)
    {
        if (string.IsNullOrWhiteSpace(datRomFileName))
            return true;

        return string.Equals(
            Path.GetFileName(actualFileName),
            Path.GetFileName(datRomFileName),
            StringComparison.OrdinalIgnoreCase);
    }
}
