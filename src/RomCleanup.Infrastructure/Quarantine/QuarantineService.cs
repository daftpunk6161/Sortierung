using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;

namespace RomCleanup.Infrastructure.Quarantine;

/// <summary>
/// Quarantine service for suspicious ROM files.
/// Mirrors Quarantine.ps1 logic. Integrated as non-destructive candidate analysis in RunOrchestrator.
/// </summary>
public sealed class QuarantineService
{
    private readonly IFileSystem _fs;

    public QuarantineService(IFileSystem fs) => _fs = fs;

    /// <summary>
    /// Checks if a file is a quarantine candidate based on standard criteria + custom rules.
    /// </summary>
    public QuarantineCandidateResult TestCandidate(QuarantineItem item, IReadOnlyList<QuarantineRule>? rules = null)
    {
        var reasons = new List<string>();

        // Unknown console + unknown format
        if ((string.IsNullOrEmpty(item.Console) || item.Console == "Unknown") &&
            (string.IsNullOrEmpty(item.Format) || item.Format == "Unknown"))
        {
            reasons.Add("UnknownConsoleAndFormat");
        }

        // No DAT match + non-game category
        if (item.DatStatus == "NoMatch" && item.Category != "GAME")
        {
            reasons.Add("NoDatMatchAndNotGame");
        }

        // Header anomalies
        if (item.HeaderStatus is "Anomaly" or "Corrupted")
        {
            reasons.Add("HeaderAnomaly");
        }

        // Custom rules
        if (rules != null)
        {
            foreach (var rule in rules)
            {
                if (string.IsNullOrWhiteSpace(rule.Field))
                    continue;

                var itemValue = rule.Field switch
                {
                    "Console" => item.Console,
                    "Format" => item.Format,
                    "DatStatus" => item.DatStatus,
                    "Category" => item.Category,
                    "HeaderStatus" => item.HeaderStatus,
                    _ => ""
                };

                if (string.Equals(itemValue, rule.Value, StringComparison.OrdinalIgnoreCase))
                {
                    reasons.Add($"CustomRule:{rule.Field}={rule.Value}");
                }
            }
        }

        return new QuarantineCandidateResult
        {
            IsCandidate = reasons.Count > 0,
            Reasons = reasons,
            Item = item
        };
    }

    /// <summary>
    /// Creates a quarantine action for a file.
    /// </summary>
    public QuarantineAction CreateAction(string sourcePath, string quarantineRoot,
        IReadOnlyList<string>? reasons = null, string mode = "DryRun")
    {
        var fileName = Path.GetFileName(sourcePath);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd");
        var targetDir = Path.Combine(quarantineRoot, timestamp);
        var targetPath = Path.Combine(targetDir, fileName);

        return new QuarantineAction
        {
            SourcePath = sourcePath,
            TargetPath = targetPath,
            QuarantineDir = targetDir,
            Reasons = reasons != null ? new List<string>(reasons) : new List<string>(),
            Mode = mode,
            Status = "Pending",
            Timestamp = DateTime.UtcNow.ToString("o")
        };
    }

    /// <summary>
    /// Executes quarantine actions.
    /// </summary>
    public QuarantineResult Execute(IReadOnlyList<QuarantineAction> actions, string mode = "DryRun")
    {
        if (actions.Count == 0)
            return new QuarantineResult();

        var results = new List<QuarantineAction>();
        int moved = 0, errors = 0;

        foreach (var action in actions)
        {
            if (mode == "DryRun")
            {
                action.Status = "DryRun";
                results.Add(action);
                continue;
            }

            try
            {
                _fs.EnsureDirectory(action.QuarantineDir);

                if (_fs.TestPath(action.SourcePath, "Leaf"))
                {
                    if (_fs.MoveItemSafely(action.SourcePath, action.TargetPath) is not null)
                    {
                        action.Status = "Moved";
                        moved++;
                    }
                    else
                    {
                        action.Status = "Error";
                        action.Error = "Move failed";
                        errors++;
                    }
                }
                else
                {
                    action.Status = "SourceMissing";
                    errors++;
                }
            }
            catch (Exception ex)
            {
                action.Status = "Error";
                action.Error = ex.Message;
                errors++;
            }

            results.Add(action);
        }

        return new QuarantineResult
        {
            Processed = actions.Count,
            Moved = moved,
            Errors = errors,
            Results = results
        };
    }

    /// <summary>
    /// Lists quarantine directory contents.
    /// </summary>
    public QuarantineContents GetContents(string quarantineRoot)
    {
        if (!_fs.TestPath(quarantineRoot, "Container"))
            return new QuarantineContents();

        var files = new List<QuarantineFileEntry>();
        var dateGroups = new Dictionary<string, List<QuarantineFileEntry>>();
        long totalSize = 0;

        foreach (var dir in Directory.GetDirectories(quarantineRoot))
        {
            var dateDir = Path.GetFileName(dir);
            var groupFiles = new List<QuarantineFileEntry>();

            foreach (var file in Directory.GetFiles(dir))
            {
                var info = new FileInfo(file);
                var entry = new QuarantineFileEntry
                {
                    Name = info.Name,
                    Path = info.FullName,
                    Size = info.Length
                };
                files.Add(entry);
                groupFiles.Add(entry);
                totalSize += info.Length;
            }

            if (groupFiles.Count > 0)
                dateGroups[dateDir] = groupFiles;
        }

        return new QuarantineContents
        {
            Files = files,
            TotalSize = totalSize,
            TotalSizeMB = Math.Round(totalSize / (1024.0 * 1024.0), 2),
            DateGroups = dateGroups
        };
    }

    /// <summary>
    /// Restores a file from quarantine to its original location.
    /// </summary>
    public QuarantineRestoreResult Restore(string quarantinePath, string originalPath, string mode = "DryRun",
        IReadOnlyList<string>? allowedRestoreRoots = null)
    {
        if (!_fs.TestPath(quarantinePath, "Leaf"))
            return new QuarantineRestoreResult { Status = "Error", Reason = "QuarantineFileNotFound" };

        // Root allowlist is mandatory — restore without explicit allowed roots is rejected.
        var validRoots = allowedRestoreRoots?
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .ToList();

        if (validRoots is null or { Count: 0 })
            return new QuarantineRestoreResult { Status = "Error", Reason = "NoAllowedRestoreRoots" };

        string fullOriginal;
        try
        {
            fullOriginal = Path.GetFullPath(originalPath);
        }
        catch
        {
            return new QuarantineRestoreResult { Status = "Error", Reason = "PathTraversalBlocked" };
        }

        // Restore target must stay inside at least one allowed root.
        {
            var allowed = validRoots.Any(root => IsPathWithinRoot(fullOriginal, root));

            if (!allowed)
                return new QuarantineRestoreResult { Status = "Error", Reason = "PathTraversalBlocked" };
        }

        if (mode == "DryRun")
            return new QuarantineRestoreResult { Status = "DryRun", From = quarantinePath, To = fullOriginal };

        try
        {
            var dir = Path.GetDirectoryName(fullOriginal);
            if (!string.IsNullOrEmpty(dir))
                _fs.EnsureDirectory(dir);

            if (_fs.MoveItemSafely(quarantinePath, fullOriginal) is null)
                return new QuarantineRestoreResult { Status = "Error", Reason = "MoveFailedAtDestination" };
            return new QuarantineRestoreResult { Status = "Restored", From = quarantinePath, To = fullOriginal };
        }
        catch (Exception ex)
        {
            return new QuarantineRestoreResult { Status = "Error", Reason = ex.Message };
        }
    }

    private static bool IsPathWithinRoot(string fullPath, string rootPath)
    {
        try
        {
            var normalizedFull = Path.GetFullPath(fullPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedRoot = Path.GetFullPath(rootPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (normalizedFull.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                return true;

            return normalizedFull.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
