using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.Metrics;
using RomCleanup.Infrastructure.Orchestration;

namespace RomCleanup.Api;

internal static class ApiRunResultMapper
{
    public static ApiRunResult Map(RunResult result, RunProjection projection)
    {
        return new ApiRunResult
        {
            OrchestratorStatus = projection.Status,
            ExitCode = projection.ExitCode,
            TotalFiles = projection.TotalFiles,
            Candidates = projection.Candidates,
            Groups = projection.Groups,
            Winners = projection.Keep,
            Losers = projection.Dupes,
            Games = projection.Games,
            Unknown = projection.Unknown,
            Junk = projection.Junk,
            Bios = projection.Bios,
            DatMatches = projection.DatMatches,
            HealthScore = projection.HealthScore,
            ConvertedCount = projection.ConvertedCount,
            ConvertErrorCount = projection.ConvertErrorCount,
            ConvertSkippedCount = projection.ConvertSkippedCount,
            ConvertBlockedCount = projection.ConvertBlockedCount,
            ConvertReviewCount = projection.ConvertReviewCount,
            ConvertLossyWarningCount = projection.ConvertLossyWarningCount,
            ConvertVerifyPassedCount = projection.ConvertVerifyPassedCount,
            ConvertVerifyFailedCount = projection.ConvertVerifyFailedCount,
            ConvertSavedBytes = projection.ConvertSavedBytes,
            DatHaveCount = projection.DatHaveCount,
            DatHaveWrongNameCount = projection.DatHaveWrongNameCount,
            DatMissCount = projection.DatMissCount,
            DatUnknownCount = projection.DatUnknownCount,
            DatAmbiguousCount = projection.DatAmbiguousCount,
            DatRenameProposedCount = projection.DatRenameProposedCount,
            DatRenameExecutedCount = projection.DatRenameExecutedCount,
            DatRenameSkippedCount = projection.DatRenameSkippedCount,
            DatRenameFailedCount = projection.DatRenameFailedCount,
            JunkRemovedCount = projection.JunkRemovedCount,
            FilteredNonGameCount = projection.FilteredNonGameCount,
            JunkFailCount = projection.JunkFailCount,
            MoveCount = projection.MoveCount,
            SkipCount = projection.SkipCount,
            ConsoleSortMoved = projection.ConsoleSortMoved,
            ConsoleSortFailed = projection.ConsoleSortFailed,
            ConsoleSortReviewed = projection.ConsoleSortReviewed,
            ConsoleSortBlocked = projection.ConsoleSortBlocked,
            FailCount = projection.FailCount,
            SavedBytes = projection.SavedBytes,
            DurationMs = projection.DurationMs,
            PreflightWarnings = result.Preflight?.Warnings?.ToArray() ?? Array.Empty<string>(),
            PhaseMetrics = BuildPhaseMetricsPayload(result.PhaseMetrics),
            DedupeGroups = BuildDedupeGroupsPayload(result.DedupeGroups),
            ConversionPlans = BuildConversionPlansPayload(result.ConversionReport),
            ConversionBlocked = BuildConversionBlockedPayload(result.ConversionReport)
        };
    }

    private static ApiPhaseMetrics BuildPhaseMetricsPayload(PhaseMetricsResult? metrics)
    {
        if (metrics is null)
        {
            return new ApiPhaseMetrics
            {
                Phases = Array.Empty<ApiPhaseMetric>()
            };
        }

        return new ApiPhaseMetrics
        {
            RunId = metrics.RunId,
            StartedAt = metrics.StartedAt,
            TotalDurationMs = (long)metrics.TotalDuration.TotalMilliseconds,
            Phases = metrics.Phases.Select(phase => new ApiPhaseMetric
            {
                Phase = phase.Phase,
                StartedAt = phase.StartedAt,
                DurationMs = (long)phase.Duration.TotalMilliseconds,
                ItemCount = phase.ItemCount,
                ItemsPerSec = phase.ItemsPerSec,
                PercentOfTotal = phase.PercentOfTotal,
                Status = phase.Status
            }).ToArray()
        };
    }

    private static ApiDedupeGroup[] BuildDedupeGroupsPayload(IReadOnlyList<DedupeGroup> dedupeGroups)
    {
        if (dedupeGroups.Count == 0)
            return Array.Empty<ApiDedupeGroup>();

        return dedupeGroups.Select(group => new ApiDedupeGroup
        {
            GameKey = group.GameKey,
            Winner = group.Winner,
            Losers = group.Losers.ToArray()
        }).ToArray();
    }

    private static ApiConversionPlan[] BuildConversionPlansPayload(ConversionReport? report)
    {
        if (report?.Results is null || report.Results.Count == 0)
            return Array.Empty<ApiConversionPlan>();

        return report.Results
            .Where(r => r.Outcome == ConversionOutcome.Success || r.Outcome == ConversionOutcome.Skipped)
            .Select(r => new ApiConversionPlan
            {
                SourcePath = r.SourcePath,
                TargetExtension = r.TargetPath is not null ? Path.GetExtension(r.TargetPath) : null,
                Safety = r.Safety.ToString(),
                Outcome = r.Outcome.ToString(),
                Verification = r.VerificationResult.ToString()
            })
            .ToArray();
    }

    private static ApiConversionBlocked[] BuildConversionBlockedPayload(ConversionReport? report)
    {
        if (report?.Results is null || report.Results.Count == 0)
            return Array.Empty<ApiConversionBlocked>();

        return report.Results
            .Where(r => r.Outcome == ConversionOutcome.Blocked || r.Outcome == ConversionOutcome.Error)
            .Select(r => new ApiConversionBlocked
            {
                SourcePath = r.SourcePath,
                Reason = r.Reason ?? r.Outcome.ToString(),
                Safety = r.Safety.ToString()
            })
            .ToArray();
    }
}
