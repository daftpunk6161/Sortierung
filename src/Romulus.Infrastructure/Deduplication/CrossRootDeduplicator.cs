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
    /// <param name="group">Duplicate group keyed by content hash.</param>
    /// <param name="preferRegions">Region preference order; defaults to EU/US/WORLD/JP.</param>
    /// <param name="categoryRanks">
    /// F4: Category rank table to share with the main pipeline. When null, falls back to
    /// <see cref="DeduplicationEngine.SelectWinner(IReadOnlyList{RomCandidate})"/>'s default ranks.
    /// </param>
    /// <param name="recomputePresetScores">
    /// F5: When false, all preset scores on <see cref="CrossRootFile"/> are treated as authoritative
    /// (a preset zero stays zero). When true (default for backward-compatible call sites with
    /// non-projection scores), missing/zero scores are filled in from the local FormatScorer cascade.
    /// </param>
    public static CrossRootMergeAdvice GetMergeAdvice(
        CrossRootDuplicateGroup group,
        string[]? preferRegions = null,
        IReadOnlyDictionary<string, int>? categoryRanks = null,
        bool recomputePresetScores = true)
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

                // F5: when caller marks scores as authoritative we never overwrite, even if 0.
                var regionScore = recomputePresetScores && file.RegionScore == 0
                    ? FormatScorer.GetRegionScore(regionTag, regions)
                    : file.RegionScore;
                var formatScore = recomputePresetScores && file.FormatScore == 0
                    ? FormatScorer.GetFormatScore(file.Extension)
                    : file.FormatScore;
                var versionScore = recomputePresetScores && file.VersionScore == 0
                    ? versionScorer.GetVersionScore(fileName)
                    : file.VersionScore;
                var sizeTieBreakScore = recomputePresetScores && file.SizeTieBreakScore == 0
                    ? FormatScorer.GetSizeTieBreakScore(null, file.Extension, file.SizeBytes)
                    : file.SizeTieBreakScore;

                var candidate = new RomCandidate
                {
                    MainPath = file.Path,
                    // F3: Normalize without extension so the GameKey is independent of container.
                    GameKey = GameKeyNormalizer.Normalize(Path.GetFileNameWithoutExtension(file.Path)),
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

        // F4: route through the categoryRanks-aware overload so caller policy wins.
        var candidateList = mappedCandidates.Select(x => x.Candidate).ToList();
        var winner = categoryRanks is null
            ? DeduplicationEngine.SelectWinner(candidateList)
            : DeduplicationEngine.SelectWinner(candidateList, categoryRanks);
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
