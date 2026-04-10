using System.Text;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Safety;

namespace Romulus.Infrastructure.Reporting;

/// <summary>
/// Centralized report projection and persistence for completed pipeline runs.
/// </summary>
public static class RunReportWriter
{
    public static IReadOnlyList<ReportEntry> BuildEntries(RunResult result, string mode = RunConstants.ModeMove)
    {
        var isDryRun = string.Equals(mode, RunConstants.ModeDryRun, StringComparison.OrdinalIgnoreCase);
        var projectedArtifacts = RunArtifactProjection.Project(result);
        var entries = new List<ReportEntry>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in projectedArtifacts.DedupeGroups)
        {
            entries.Add(BuildReportEntry(group.Winner, group.GameKey, "KEEP"));
            seenPaths.Add(group.Winner.MainPath);

            foreach (var loser in group.Losers)
            {
                var loserAction = loser.Category == FileCategory.Junk ? "JUNK" : (isDryRun ? "DUPE" : "MOVE");
                entries.Add(BuildReportEntry(loser, group.GameKey, loserAction));
                seenPaths.Add(loser.MainPath);
            }
        }

        // Add remaining candidates not yet covered by dedupe groups.
        // When no dedupe ran (ConvertOnly, empty processingCandidates), this captures all files.
        foreach (var candidate in projectedArtifacts.AllCandidates)
        {
            if (!seenPaths.Add(candidate.MainPath))
                continue;

            var action = candidate.Category switch
            {
                FileCategory.Junk => "JUNK",
                FileCategory.Bios => "BIOS",
                _ => "KEEP"
            };

            entries.Add(BuildReportEntry(candidate, candidate.GameKey, action));
        }

        return entries;
    }

    private static ReportEntry BuildReportEntry(RomCandidate candidate, string gameKey, string action)
        => new()
        {
            GameKey = gameKey,
            Action = action,
            Category = ToReportCategory(candidate.Category),
            Region = candidate.Region,
            FilePath = candidate.MainPath,
            FileName = Path.GetFileName(candidate.MainPath),
            Extension = candidate.Extension,
            SizeBytes = candidate.SizeBytes,
            RegionScore = candidate.RegionScore,
            FormatScore = candidate.FormatScore,
            VersionScore = (int)candidate.VersionScore,
            Console = candidate.ConsoleKey ?? string.Empty,
            DatMatch = candidate.DatMatch,
            DecisionClass = candidate.DecisionClass.ToString(),
            EvidenceTier = candidate.EvidenceTier.ToString(),
            PrimaryMatchKind = candidate.PrimaryMatchKind.ToString(),
            PlatformFamily = candidate.PlatformFamily.ToString(),
            MatchLevel = candidate.MatchEvidence.Level.ToString(),
            MatchReasoning = candidate.MatchEvidence.Reasoning
        };

    private static string ToReportCategory(FileCategory category)
        => category.ToString().ToUpperInvariant();

    public static ReportSummary BuildSummary(RunResult result, string mode)
    {
        var entries = BuildEntries(result, mode);
        var projection = RunProjectionFactory.Create(result);
        var moveCount = string.Equals(mode, RunConstants.ModeDryRun, StringComparison.OrdinalIgnoreCase)
            ? projection.Dupes
            : projection.MoveCount;
        var junkCount = projection.Junk;
        var biosCount = projection.Bios;

        // Invariant: report accounting must match scanned files.
        // Use canonical accounting from scanned candidates + prefilter count,
        // independent from dedupe grouping internals.
        if (projection.TotalFiles > 0)
        {
            // Newer pipelines keep AllCandidates as the full scanned set.
            // In that case FilteredNonGameCount is already represented in Candidates
            // and must not be added again.
            var accountedTotal = projection.Candidates;
            if (accountedTotal < projection.TotalFiles)
                accountedTotal += projection.FilteredNonGameCount;

            if (accountedTotal != projection.TotalFiles)
                throw new InvalidOperationException($"Report summary invariant failed: accounted={accountedTotal}, scanned={projection.TotalFiles}");
        }

        var totalErrorCount = projection.FailCount;

        return new ReportSummary
        {
            Mode = mode,
            RunStatus = projection.Status,
            Timestamp = DateTime.UtcNow,
            TotalFiles = projection.TotalFiles,
            Candidates = projection.Candidates,
            KeepCount = projection.Keep,
            DupesCount = projection.Dupes,
            GamesCount = projection.Games,
            MoveCount = moveCount,
            JunkCount = junkCount,
            BiosCount = biosCount,
            DatMatches = projection.DatMatches,
            HealthScore = projection.HealthScore,
            ConvertedCount = projection.ConvertedCount,
            ConvertErrorCount = projection.ConvertErrorCount,
            ConvertSkippedCount = projection.ConvertSkippedCount,
            ConvertBlockedCount = projection.ConvertBlockedCount,
            ConvertReviewCount = projection.ConvertReviewCount,
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
            JunkFailCount = projection.JunkFailCount,
            SkipCount = projection.SkipCount,
            ConsoleSortMoved = projection.ConsoleSortMoved,
            ConsoleSortFailed = projection.ConsoleSortFailed,
            ConsoleSortReviewed = projection.ConsoleSortReviewed,
            ConsoleSortBlocked = projection.ConsoleSortBlocked,
            ConsoleSortUnknown = projection.ConsoleSortUnknown,
            FailCount = projection.FailCount,
            ErrorCount = totalErrorCount,
            SkippedCount = projection.ConvertSkippedCount + projection.ConvertBlockedCount + projection.SkipCount,
            SavedBytes = projection.SavedBytes,
            GroupCount = projection.Groups,
            Duration = TimeSpan.FromMilliseconds(projection.DurationMs)
        };
    }

    public static string WriteReport(string reportPath, RunResult result, string mode)
    {
        var fullPath = SafetyValidator.EnsureSafeOutputPath(reportPath);
        var entries = BuildEntries(result, mode);
        var summary = BuildSummary(result, mode);
        var reportDir = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException($"Report path has no directory: {reportPath}");

        Directory.CreateDirectory(reportDir);

        if (fullPath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            var csv = ReportGenerator.GenerateCsv(entries);
            File.WriteAllText(fullPath, csv, Encoding.UTF8);
        }
        else if (fullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            ReportGenerator.WriteJsonToFile(fullPath, reportDir, summary, entries);
        }
        else
        {
            ReportGenerator.WriteHtmlToFile(fullPath, reportDir, summary, entries);
        }

        if (!File.Exists(fullPath))
            throw new IOException($"Report file was not created: {fullPath}");

        return fullPath;
    }
}
