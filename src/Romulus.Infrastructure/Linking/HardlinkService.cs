using Romulus.Contracts.Models;

namespace Romulus.Infrastructure.Linking;

/// <summary>
/// Hardlink/symlink mode support for storage-efficient ROM organization.
/// Mirrors HardlinkMode.ps1. Integrated as support preview in RunOrchestrator.
/// </summary>
public sealed class HardlinkService
{
    /// <summary>
    /// Checks if NTFS hardlinks are supported on the drive containing the path.
    /// </summary>
    public static bool IsHardlinkSupported(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root)) return false;
            var driveInfo = new DriveInfo(root);
            // Hardlinks require NTFS
            return string.Equals(driveInfo.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Creates a link structure configuration.
    /// </summary>
    public static LinkStructureConfig CreateConfig(string sourceRoot, string targetRoot,
        LinkType linkType = LinkType.Hardlink, LinkGroupBy groupBy = LinkGroupBy.Console)
    {
        return new LinkStructureConfig
        {
            SourceRoot = sourceRoot,
            TargetRoot = targetRoot,
            LinkType = linkType,
            GroupBy = groupBy
        };
    }

    /// <summary>
    /// Creates a single link operation.
    /// </summary>
    public static LinkOperation CreateOperation(string sourcePath, string targetPath,
        LinkType linkType = LinkType.Hardlink)
    {
        return new LinkOperation
        {
            SourcePath = sourcePath,
            TargetPath = targetPath,
            LinkType = linkType,
            Status = "Pending"
        };
    }

    /// <summary>
    /// Builds a link plan from files with grouping.
    /// </summary>
    public static LinkPlan BuildPlan(LinkStructureConfig config,
        IReadOnlyList<(string FilePath, string? ConsoleKey, string? Genre, string? Region)> files)
    {
        var operations = new List<LinkOperation>();
        long totalSourceBytes = 0;

        foreach (var (filePath, consoleKey, genre, region) in files)
        {
            var subDir = config.GroupBy switch
            {
                LinkGroupBy.Console => consoleKey ?? "Unknown",
                LinkGroupBy.Genre => genre ?? "Unknown",
                LinkGroupBy.Region => region ?? "Unknown",
                LinkGroupBy.ConsoleAndGenre =>
                    Path.Combine(consoleKey ?? "Unknown", genre ?? "Unknown"),
                _ => ""
            };

            var targetDir = Path.Combine(config.TargetRoot, subDir);
            var targetPath = Path.Combine(targetDir, Path.GetFileName(filePath));

            // TASK-196: Validate target path stays within TargetRoot
            var fullTarget = Path.GetFullPath(targetPath);
            var fullRoot = Path.GetFullPath(config.TargetRoot);
            if (!fullTarget.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !fullTarget.Equals(fullRoot, StringComparison.OrdinalIgnoreCase))
                continue; // skip path-traversal attempt

            operations.Add(new LinkOperation
            {
                SourcePath = filePath,
                TargetPath = targetPath,
                LinkType = config.LinkType,
                Status = "Pending"
            });

            try { if (File.Exists(filePath)) totalSourceBytes += new FileInfo(filePath).Length; }
            catch (IOException) { /* File locked or inaccessible — savings estimate will undercount */ }
        }

        // Hardlinks save 100%, symlinks save ~0%
        long savedBytes = config.LinkType == LinkType.Hardlink ? totalSourceBytes : 0;
        double savedPercent = totalSourceBytes > 0 && config.LinkType == LinkType.Hardlink ? 100.0 : 0.0;

        return new LinkPlan
        {
            Config = config,
            Operations = operations,
            Savings = new LinkSavingsEstimate
            {
                TotalSourceBytes = totalSourceBytes,
                SavedBytes = savedBytes,
                SavedPercent = savedPercent,
                FileCount = operations.Count
            }
        };
    }

    /// <summary>
    /// Gets statistics for a set of link operations.
    /// </summary>
    public static LinkStatistics GetStatistics(IReadOnlyList<LinkOperation> operations)
    {
        return new LinkStatistics
        {
            Completed = operations.Count(o => o.Status == "Completed"),
            Pending = operations.Count(o => o.Status == "Pending"),
            Failed = operations.Count(o => o.Status == "Failed"),
            Total = operations.Count
        };
    }
}
