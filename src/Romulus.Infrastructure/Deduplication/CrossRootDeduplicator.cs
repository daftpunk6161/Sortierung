using Romulus.Contracts.Models;
using Romulus.Core.Deduplication;
using Romulus.Core.GameKeys;
using Romulus.Core.Regions;
using Romulus.Core.Scoring;

namespace Romulus.Infrastructure.Deduplication;

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
    /// Recommends which file to keep using the same deterministic winner truth as the
    /// main deduplication engine whenever the relevant projection data is available.
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
        var mappedCandidates = group.Files
            .Select(file =>
            {
                var fileName = Path.GetFileNameWithoutExtension(file.Path);
                var regionTag = string.IsNullOrWhiteSpace(file.Region) ||
                    string.Equals(file.Region, "UNKNOWN", StringComparison.OrdinalIgnoreCase)
                    ? RegionDetector.GetRegionTag(fileName)
                    : file.Region;
                var regionScore = file.RegionScore != 0
                    ? file.RegionScore
                    : FormatScorer.GetRegionScore(regionTag, regions);
                var formatScore = file.FormatScore != 0
                    ? file.FormatScore
                    : FormatScorer.GetFormatScore(file.Extension);
                var versionScore = file.VersionScore != 0
                    ? file.VersionScore
                    : versionScorer.GetVersionScore(fileName);
                var sizeTieBreakScore = file.SizeTieBreakScore != 0
                    ? file.SizeTieBreakScore
                    : FormatScorer.GetSizeTieBreakScore(null, file.Extension, file.SizeBytes);

                var candidate = new RomCandidate
                {
                    MainPath = file.Path,
                    GameKey = GameKeyNormalizer.Normalize(Path.GetFileName(file.Path)),
                    Region = regionTag,
                    RegionScore = regionScore,
                    FormatScore = formatScore,
                    VersionScore = versionScore,
                    HeaderScore = file.HeaderScore,
                    CompletenessScore = file.CompletenessScore,
                    SizeTieBreakScore = sizeTieBreakScore,
                    SizeBytes = file.SizeBytes,
                    Extension = file.Extension,
                    DatMatch = file.DatMatch,
                    Category = file.Category
                };

                return (File: file, Candidate: candidate);
            })
            .ToList();

        var winner = DeduplicationEngine.SelectWinner(mappedCandidates.Select(x => x.Candidate).ToList());
        var keep = mappedCandidates
            .First(x => string.Equals(x.Candidate.MainPath, winner?.MainPath, StringComparison.OrdinalIgnoreCase))
            .File;
        var remove = mappedCandidates
            .Where(x => !string.Equals(x.File.Path, keep.Path, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.File)
            .OrderBy(x => x.Path, StringComparer.Ordinal)
            .ToList();

        return new CrossRootMergeAdvice
        {
            Hash = group.Hash,
            Keep = keep,
            Remove = remove
        };
    }
}
