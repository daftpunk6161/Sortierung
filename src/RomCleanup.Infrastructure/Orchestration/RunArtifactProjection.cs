using RomCleanup.Contracts.Models;
using RomCleanup.Core.Scoring;

namespace RomCleanup.Infrastructure.Orchestration;

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

        var successfulConversions = BuildSuccessfulConversionMap(result.ConversionReport);
        if (successfulConversions.Count == 0)
            return new ProjectedRunArtifacts(result.AllCandidates, result.DedupeGroups);

        return new ProjectedRunArtifacts(
            ProjectCandidates(result.AllCandidates, successfulConversions),
            ProjectGroups(result.DedupeGroups, successfulConversions));
    }

    private static IReadOnlyList<RomCandidate> ProjectCandidates(
        IReadOnlyList<RomCandidate> candidates,
        IReadOnlyDictionary<string, ConversionResult> successfulConversions)
    {
        if (candidates.Count == 0)
            return candidates;

        return candidates
            .Select(candidate => ProjectCandidate(candidate, successfulConversions))
            .ToArray();
    }

    private static IReadOnlyList<DedupeGroup> ProjectGroups(
        IReadOnlyList<DedupeGroup> groups,
        IReadOnlyDictionary<string, ConversionResult> successfulConversions)
    {
        if (groups.Count == 0)
            return groups;

        return groups
            .Select(group => group with
            {
                Winner = ProjectCandidate(group.Winner, successfulConversions),
                Losers = group.Losers
                    .Select(loser => ProjectCandidate(loser, successfulConversions))
                    .ToArray()
            })
            .ToArray();
    }

    private static RomCandidate ProjectCandidate(
        RomCandidate candidate,
        IReadOnlyDictionary<string, ConversionResult> successfulConversions)
    {
        if (!successfulConversions.TryGetValue(candidate.MainPath, out var conversionResult)
            || string.IsNullOrWhiteSpace(conversionResult.TargetPath))
        {
            return candidate;
        }

        var projectedPath = conversionResult.TargetPath;
        var projectedExtension = Path.GetExtension(projectedPath);
        if (string.IsNullOrWhiteSpace(projectedExtension))
            projectedExtension = candidate.Extension;

        var projectedSizeBytes = ResolveProjectedSizeBytes(conversionResult, candidate.SizeBytes);

        return candidate with
        {
            MainPath = projectedPath,
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
