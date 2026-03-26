using RomCleanup.Contracts.Models;
using RomCleanup.Core.GameKeys;
using RomCleanup.Core.Regions;
using RomCleanup.Core.Scoring;

namespace RomCleanup.Infrastructure.Deduplication;

/// <summary>
/// Cross-root deduplication — finds identical ROMs across multiple root directories.
/// Mirrors CrossRootDedupe.ps1. Integrated as non-destructive analysis in RunOrchestrator.
/// </summary>
public sealed class CrossRootDeduplicator
{
    /// <summary>
    /// Finds identical files across multiple roots via hash.
    /// Returns only groups with 2+ files from 2+ different roots.
    /// </summary>
    public static IReadOnlyList<CrossRootDuplicateGroup> FindDuplicates(
        IReadOnlyList<CrossRootFile> files)
    {
        var groups = files
            .Where(f => !string.IsNullOrEmpty(f.Hash))
            .GroupBy(f => f.Hash, StringComparer.OrdinalIgnoreCase)
            .Select(g => new CrossRootDuplicateGroup
            {
                Hash = g.Key,
                Files = g.ToList()
            })
            .Where(g => g.Files.Count >= 2
                && g.Files.Select(f => f.Root).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2)
            .ToList();

        return groups;
    }

    /// <summary>
    /// Recommends which file to keep based on region score, format score, version score, and size.
    /// </summary>
    public static CrossRootMergeAdvice GetMergeAdvice(CrossRootDuplicateGroup group, string[]? preferRegions = null)
    {
        if (group.Files.Count < 2)
            return new CrossRootMergeAdvice { Hash = group.Hash, Keep = group.Files.FirstOrDefault() ?? new(), Remove = [] };

        // Only advise merging when files span multiple roots
        var distinctRoots = group.Files
            .Select(f => f.Root)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        if (distinctRoots < 2)
            return new CrossRootMergeAdvice { Hash = group.Hash, Keep = group.Files[0], Remove = [] };

        var regions = preferRegions ?? ["EU", "US", "WORLD", "JP"];
        var versionScorer = new VersionScorer();

        // Score each file by region, format, version, then size
        var scored = group.Files
            .Select(f =>
            {
                var fileName = Path.GetFileNameWithoutExtension(f.Path);
                var regionTag = RegionDetector.GetRegionTag(fileName);
                var regionScore = FormatScorer.GetRegionScore(regionTag, regions);
                var formatScore = FormatScorer.GetFormatScore(f.Extension);
                var versionScore = versionScorer.GetVersionScore(fileName);
                return (File: f, RegionScore: regionScore, FormatScore: formatScore,
                        VersionScore: versionScore, Size: f.SizeBytes);
            })
            .OrderByDescending(x => x.RegionScore)
            .ThenByDescending(x => x.FormatScore)
            .ThenByDescending(x => x.VersionScore)
            .ThenByDescending(x => x.Size)
            .ThenBy(x => x.File.Path, StringComparer.Ordinal)
            .ToList();

        var keep = scored[0].File;
        var remove = scored.Skip(1).Select(x => x.File).ToList();

        return new CrossRootMergeAdvice
        {
            Hash = group.Hash,
            Keep = keep,
            Remove = remove
        };
    }
}
