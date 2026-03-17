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
        var junk = candidates.Count(c => c.Category == FileCategory.Junk);
        var bios = candidates.Count(c => c.Category == FileCategory.Bios);
        var games = result.DedupeGroups.Count;
        var unknown = candidates.Count(c => c.Category == FileCategory.Unknown);
        var datMatches = candidates.Count(c => c.DatMatch);
        var failCount = (result.MoveResult?.FailCount ?? 0) + result.ConvertErrorCount;
        var savedBytes = result.MoveResult?.SavedBytes ?? 0;
        var moveCount = result.MoveResult?.MoveCount ?? 0;
        var skipCount = result.MoveResult?.SkipCount ?? 0;
        var junkFailCount = result.JunkMoveResult?.FailCount ?? 0;
        var consoleSortMoved = result.ConsoleSortResult?.Moved ?? 0;
        var consoleSortFailed = result.ConsoleSortResult?.Failed ?? 0;

        var health = HealthScorer.GetHealthScore(total, result.LoserCount, junk, datMatches);

        return new RunProjection(
            Status: result.Status,
            ExitCode: result.ExitCode,
            TotalFiles: total,
            Candidates: candidates.Count,
            Groups: result.GroupCount,
            Keep: result.WinnerCount,
            Dupes: result.LoserCount,
            Games: games,
            Unknown: unknown,
            Junk: junk,
            Bios: bios,
            DatMatches: datMatches,
            ConvertedCount: result.ConvertedCount,
            ConvertErrorCount: result.ConvertErrorCount,
            ConvertSkippedCount: result.ConvertSkippedCount,
            JunkRemovedCount: result.JunkRemovedCount,
            FilteredNonGameCount: result.FilteredNonGameCount,
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