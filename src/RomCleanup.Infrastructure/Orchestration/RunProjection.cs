namespace RomCleanup.Infrastructure.Orchestration;

using RomCleanup.Contracts.Models;
using RomCleanup.Core.Scoring;

/// <summary>
/// Channel-neutral projection of RunResult metrics used by CLI/API/UI adapters.
/// </summary>
public sealed record RunProjection(
    string Status,
    int ExitCode,
    int TotalFiles,
    int Candidates,
    int Groups,
    int Keep,
    int Dupes,
    int Games,
    int Unknown,
    int Junk,
    int Bios,
    int DatMatches,
    int ConvertedCount,
    int ConvertErrorCount,
    int ConvertSkippedCount,
    int ConvertBlockedCount,
    int ConvertReviewCount,
    long ConvertSavedBytes,
    int DatHaveCount,
    int DatHaveWrongNameCount,
    int DatMissCount,
    int DatUnknownCount,
    int DatAmbiguousCount,
    int DatRenameProposedCount,
    int DatRenameExecutedCount,
    int DatRenameSkippedCount,
    int DatRenameFailedCount,
    int JunkRemovedCount,
    int FilteredNonGameCount,
    int MoveCount,
    int SkipCount,
    int JunkFailCount,
    int ConsoleSortMoved,
    int ConsoleSortFailed,
    int FailCount,
    long SavedBytes,
    long DurationMs,
    int HealthScore);

public static class RunProjectionFactory
{
    public static RunProjection Create(RunResult result)
    {
        var candidates = result.AllCandidates ?? Array.Empty<Contracts.Models.RomCandidate>();
        var total = result.TotalFilesScanned;
        var dedupeGroups = result.DedupeGroups ?? Array.Empty<DedupeResult>();
        var winnerCount = result.WinnerCount > 0 ? result.WinnerCount : dedupeGroups.Count;
        var loserCount = result.LoserCount > 0
            ? result.LoserCount
            : dedupeGroups.Sum(static g => g.Losers?.Count ?? 0);
        var groupCount = result.GroupCount > 0 ? result.GroupCount : dedupeGroups.Count;
        var junk = candidates.Count(c => c.Category == FileCategory.Junk);
        var bios = candidates.Count(c => c.Category == FileCategory.Bios);
        var games = dedupeGroups.Count;
        var unknown = candidates.Count(c => c.Category == FileCategory.Unknown);
        var datMatches = candidates.Count(c => c.DatMatch);
        var filteredNonGameCount = result.FilteredNonGameCount > 0
            ? result.FilteredNonGameCount
            : candidates.Count(c => c.Category == FileCategory.Bios);
        var failCount = (result.MoveResult?.FailCount ?? 0) + result.ConvertErrorCount;
        var savedBytes = result.MoveResult?.SavedBytes ?? 0;
        var moveCount = result.MoveResult?.MoveCount ?? 0;
        var skipCount = result.MoveResult?.SkipCount ?? 0;
        var junkFailCount = result.JunkMoveResult?.FailCount ?? 0;
        var consoleSortMoved = result.ConsoleSortResult?.Moved ?? 0;
        var consoleSortFailed = result.ConsoleSortResult?.Failed ?? 0;

        var health = HealthScorer.GetHealthScore(total, loserCount, junk, datMatches);

        return new RunProjection(
            Status: result.Status,
            ExitCode: result.ExitCode,
            TotalFiles: total,
            Candidates: candidates.Count,
            Groups: groupCount,
            Keep: winnerCount,
            Dupes: loserCount,
            Games: games,
            Unknown: unknown,
            Junk: junk,
            Bios: bios,
            DatMatches: datMatches,
            ConvertedCount: result.ConvertedCount,
            ConvertErrorCount: result.ConvertErrorCount,
            ConvertSkippedCount: result.ConvertSkippedCount,
            ConvertBlockedCount: result.ConvertBlockedCount,
            ConvertReviewCount: result.ConvertReviewCount,
            ConvertSavedBytes: result.ConvertSavedBytes,
            DatHaveCount: result.DatHaveCount,
            DatHaveWrongNameCount: result.DatHaveWrongNameCount,
            DatMissCount: result.DatMissCount,
            DatUnknownCount: result.DatUnknownCount,
            DatAmbiguousCount: result.DatAmbiguousCount,
            DatRenameProposedCount: result.DatRenameProposedCount,
            DatRenameExecutedCount: result.DatRenameExecutedCount,
            DatRenameSkippedCount: result.DatRenameSkippedCount,
            DatRenameFailedCount: result.DatRenameFailedCount,
            JunkRemovedCount: result.JunkRemovedCount,
            FilteredNonGameCount: filteredNonGameCount,
            MoveCount: moveCount,
            SkipCount: skipCount,
            JunkFailCount: junkFailCount,
            ConsoleSortMoved: consoleSortMoved,
            ConsoleSortFailed: consoleSortFailed,
            FailCount: failCount,
            SavedBytes: savedBytes,
            DurationMs: result.DurationMs,
            HealthScore: health);
    }
}