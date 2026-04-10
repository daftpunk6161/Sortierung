using Romulus.Contracts.Models;

namespace Romulus.Core.Audit;

/// <summary>
/// Pure policy for DAT-driven rename decisions.
/// </summary>
public static class DatRenamePolicy
{
    public static DatRenameProposal EvaluateRename(DatAuditEntry entry, string currentFileName)
    {
        if (entry.Status != DatAuditStatus.HaveWrongName)
        {
            return new DatRenameProposal(
                entry.FilePath,
                currentFileName,
                entry.Status,
                $"Skipped: status {entry.Status} is not rename-eligible.");
        }

        if (!string.IsNullOrWhiteSpace(entry.DatRomFileName) && !IsSafeFileName(entry.DatRomFileName))
        {
            return new DatRenameProposal(
                entry.FilePath,
                currentFileName,
                entry.Status,
                "Skipped: unsafe target filename from DAT.");
        }

        var currentExtension = Path.GetExtension(currentFileName);
        var targetCandidate = SelectTargetFileName(entry, currentFileName, currentExtension);

        if (!IsSafeFileName(targetCandidate))
        {
            return new DatRenameProposal(
                entry.FilePath,
                currentFileName,
                entry.Status,
                "Skipped: unsafe target filename from DAT.");
        }

        return new DatRenameProposal(
            entry.FilePath,
            targetCandidate,
            entry.Status,
            null);
    }

    private static string SelectTargetFileName(DatAuditEntry entry, string currentFileName, string currentExtension)
    {
        var preferred = entry.DatRomFileName;

        if (string.IsNullOrWhiteSpace(preferred))
        {
            var fallbackBase = string.IsNullOrWhiteSpace(entry.DatGameName)
                ? Path.GetFileNameWithoutExtension(currentFileName)
                : entry.DatGameName;
            preferred = fallbackBase + currentExtension;
        }

        var preferredBase = Path.GetFileNameWithoutExtension(preferred);
        var preferredExt = Path.GetExtension(preferred);

        // Preserve current extension unless DAT extension matches exactly.
        var finalExt = string.Equals(preferredExt, currentExtension, StringComparison.OrdinalIgnoreCase)
            ? preferredExt
            : currentExtension;

        return preferredBase + finalExt;
    }

    private static bool IsSafeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        if (!string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
            return false;

        if (fileName.Contains("..", StringComparison.Ordinal))
            return false;

        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return false;

        if (IsWindowsReservedDeviceName(fileName))
            return false;

        return true;
    }

    private static bool IsWindowsReservedDeviceName(string segment)
    {
        var dotIndex = segment.IndexOf('.');
        var nameOnly = dotIndex >= 0 ? segment[..dotIndex] : segment;

        return nameOnly.Length switch
        {
            3 => nameOnly.Equals("CON", StringComparison.OrdinalIgnoreCase)
              || nameOnly.Equals("PRN", StringComparison.OrdinalIgnoreCase)
              || nameOnly.Equals("AUX", StringComparison.OrdinalIgnoreCase)
              || nameOnly.Equals("NUL", StringComparison.OrdinalIgnoreCase),
            4 => (nameOnly.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                  || nameOnly.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                 && char.IsAsciiDigit(nameOnly[3]),
            _ => false
        };
    }
}
