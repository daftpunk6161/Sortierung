using RomCleanup.Contracts.Models;
using RomCleanup.Core.Scoring;

namespace RomCleanup.Infrastructure.Deduplication;

/// <summary>
/// Cross-root deduplication — finds identical ROMs across multiple root directories.
/// Mirrors CrossRootDedupe.ps1.
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
            .Where(g => g.Count() >= 2)
            .Where(g => g.Select(f => f.Root).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2)
            .Select(g => new CrossRootDuplicateGroup
            {
                Hash = g.Key,
                Files = g.ToList()
            })
            .ToList();

        return groups;
    }

    /// <summary>
    /// Recommends which file to keep based on format score (highest wins).
    /// </summary>
    public static CrossRootMergeAdvice GetMergeAdvice(CrossRootDuplicateGroup group)
    {
        // Score each file by format
        var scored = group.Files
            .Select(f => (File: f, Score: FormatScorer.GetFormatScore(f.Extension)))
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.File.SizeBytes)
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
