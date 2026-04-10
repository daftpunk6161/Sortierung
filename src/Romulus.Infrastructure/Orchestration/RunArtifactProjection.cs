using Romulus.Contracts.Models;
using Romulus.Core.Scoring;

namespace Romulus.Infrastructure.Orchestration;

public sealed record ProjectedRunArtifacts(
    IReadOnlyList<RomCandidate> AllCandidates,
    IReadOnlyList<DedupeGroup> DedupeGroups);

/// <summary>
/// Projects run artifacts to the post-conversion truth used by reports and entry points.
/// This keeps GUI, API, and report surfaces aligned after successful conversions.
/// </summary>
public static class RunArtifactProjection
{
    public static ProjectedRunArtifacts Project(RunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var datRenameMutations = BuildPathMutationMap(result.DatRenamePathMutations);
        var consoleSortMutations = BuildPathMutationMap(result.ConsoleSortResult?.PathMutations);
        var successfulConversions = BuildSuccessfulConversionMap(result.ConversionReport);
        if (datRenameMutations.Count == 0
            && consoleSortMutations.Count == 0
            && successfulConversions.Count == 0)
        {
            return new ProjectedRunArtifacts(result.AllCandidates, result.DedupeGroups);
        }

        return new ProjectedRunArtifacts(
            ProjectCandidates(result.AllCandidates, datRenameMutations, consoleSortMutations, successfulConversions),
            ProjectGroups(result.DedupeGroups, datRenameMutations, consoleSortMutations, successfulConversions));
    }

    private static IReadOnlyList<RomCandidate> ProjectCandidates(
        IReadOnlyList<RomCandidate> candidates,
        IReadOnlyDictionary<string, string> datRenameMutations,
        IReadOnlyDictionary<string, string> consoleSortMutations,
        IReadOnlyDictionary<string, ConversionResult> successfulConversions)
    {
        if (candidates.Count == 0)
            return candidates;

        return candidates
            .Select(candidate => ProjectCandidate(candidate, datRenameMutations, consoleSortMutations, successfulConversions))
            .ToArray();
    }

    private static IReadOnlyList<DedupeGroup> ProjectGroups(
        IReadOnlyList<DedupeGroup> groups,
        IReadOnlyDictionary<string, string> datRenameMutations,
        IReadOnlyDictionary<string, string> consoleSortMutations,
        IReadOnlyDictionary<string, ConversionResult> successfulConversions)
    {
        if (groups.Count == 0)
            return groups;

        return groups
            .Select(group => group with
            {
                Winner = ProjectCandidate(group.Winner, datRenameMutations, consoleSortMutations, successfulConversions),
                Losers = group.Losers
                    .Select(loser => ProjectCandidate(loser, datRenameMutations, consoleSortMutations, successfulConversions))
                    .ToArray()
            })
            .ToArray();
    }

    private static RomCandidate ProjectCandidate(
        RomCandidate candidate,
        IReadOnlyDictionary<string, string> datRenameMutations,
        IReadOnlyDictionary<string, string> consoleSortMutations,
        IReadOnlyDictionary<string, ConversionResult> successfulConversions)
    {
        var projectedPath = ApplyPathMutations(candidate.MainPath, datRenameMutations, consoleSortMutations);
        var projectedCandidate = projectedPath is null || projectedPath.Equals(candidate.MainPath, StringComparison.OrdinalIgnoreCase)
            ? candidate
            : candidate with { MainPath = projectedPath };

        if (!successfulConversions.TryGetValue(projectedCandidate.MainPath, out var conversionResult)
            || string.IsNullOrWhiteSpace(conversionResult.TargetPath))
        {
            return projectedCandidate;
        }

        var convertedPath = conversionResult.TargetPath;
        var projectedExtension = Path.GetExtension(convertedPath);
        if (string.IsNullOrWhiteSpace(projectedExtension))
            projectedExtension = projectedCandidate.Extension;

        var projectedSizeBytes = ResolveProjectedSizeBytes(conversionResult, projectedCandidate.SizeBytes);

        return projectedCandidate with
        {
            MainPath = convertedPath,
            Extension = projectedExtension,
            SizeBytes = projectedSizeBytes,
            FormatScore = FormatScorer.GetFormatScore(projectedExtension),
            SizeTieBreakScore = FormatScorer.GetSizeTieBreakScore(null, projectedExtension, projectedSizeBytes)
        };
    }

    private static Dictionary<string, ConversionResult> BuildSuccessfulConversionMap(ConversionReport? report)
    {
        var successfulConversions = new Dictionary<string, ConversionResult>(StringComparer.OrdinalIgnoreCase);
        if (report?.Results is null)
            return successfulConversions;

        foreach (var conversionResult in report.Results)
        {
            if (conversionResult.Outcome != ConversionOutcome.Success
                || string.IsNullOrWhiteSpace(conversionResult.SourcePath)
                || string.IsNullOrWhiteSpace(conversionResult.TargetPath))
            {
                continue;
            }

            successfulConversions[conversionResult.SourcePath] = conversionResult;
        }

        return successfulConversions;
    }

    private static Dictionary<string, string> BuildPathMutationMap(IReadOnlyList<PathMutation>? pathMutations)
    {
        var mutationMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (pathMutations is null)
            return mutationMap;

        foreach (var pathMutation in pathMutations)
        {
            if (string.IsNullOrWhiteSpace(pathMutation.SourcePath)
                || string.IsNullOrWhiteSpace(pathMutation.TargetPath))
            {
                continue;
            }

            mutationMap[pathMutation.SourcePath] = pathMutation.TargetPath;
        }

        return mutationMap;
    }

    private static string? ApplyPathMutations(
        string? originalPath,
        IReadOnlyDictionary<string, string> datRenameMutations,
        IReadOnlyDictionary<string, string> consoleSortMutations)
    {
        if (string.IsNullOrWhiteSpace(originalPath))
            return originalPath;

        var currentPath = originalPath;
        if (datRenameMutations.TryGetValue(currentPath, out var renamedPath))
            currentPath = renamedPath;

        if (consoleSortMutations.TryGetValue(currentPath, out var sortedPath))
            currentPath = sortedPath;

        return currentPath;
    }

    private static long ResolveProjectedSizeBytes(ConversionResult conversionResult, long fallbackSizeBytes)
    {
        long totalSizeBytes = 0;
        var hasMeasuredSize = false;

        if (!string.IsNullOrWhiteSpace(conversionResult.TargetPath))
        {
            if (TryGetFileSize(conversionResult.TargetPath, out var primarySizeBytes))
            {
                totalSizeBytes += primarySizeBytes;
                hasMeasuredSize = true;
            }
            else if (conversionResult.TargetBytes.HasValue)
            {
                totalSizeBytes += conversionResult.TargetBytes.Value;
                hasMeasuredSize = true;
            }
        }

        foreach (var additionalTargetPath in conversionResult.AdditionalTargetPaths)
        {
            if (TryGetFileSize(additionalTargetPath, out var additionalSizeBytes))
            {
                totalSizeBytes += additionalSizeBytes;
                hasMeasuredSize = true;
            }
        }

        return hasMeasuredSize ? totalSizeBytes : fallbackSizeBytes;
    }

    private static bool TryGetFileSize(string? path, out long sizeBytes)
    {
        sizeBytes = 0;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            var fileInfo = new FileInfo(path);
            if (!fileInfo.Exists)
                return false;

            sizeBytes = fileInfo.Length;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
