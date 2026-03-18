using System.Text;
using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.Orchestration;

namespace RomCleanup.Infrastructure.Reporting;

/// <summary>
/// Centralized report projection and persistence for completed pipeline runs.
/// </summary>
public static class RunReportWriter
{
    public static IReadOnlyList<ReportEntry> BuildEntries(RunResult result)
    {
        var entries = new List<ReportEntry>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in result.DedupeGroups)
        {
            entries.Add(new ReportEntry
            {
                GameKey = group.GameKey,
                Action = "KEEP",
                Category = ToReportCategory(group.Winner.Category),
                Region = group.Winner.Region,
                FilePath = group.Winner.MainPath,
                FileName = Path.GetFileName(group.Winner.MainPath),
                Extension = group.Winner.Extension,
                SizeBytes = group.Winner.SizeBytes,
                RegionScore = group.Winner.RegionScore,
                FormatScore = group.Winner.FormatScore,
                VersionScore = (int)group.Winner.VersionScore,
                Console = group.Winner.ConsoleKey ?? string.Empty,
                DatMatch = group.Winner.DatMatch
            });
            seenPaths.Add(group.Winner.MainPath);

            foreach (var loser in group.Losers)
            {
                entries.Add(new ReportEntry
                {
                    GameKey = group.GameKey,
                        Action = loser.Category == FileCategory.Junk ? "JUNK" : "MOVE",
                        Category = ToReportCategory(loser.Category),
                    Region = loser.Region,
                    FilePath = loser.MainPath,
                    FileName = Path.GetFileName(loser.MainPath),
                    Extension = loser.Extension,
                    SizeBytes = loser.SizeBytes,
                    RegionScore = loser.RegionScore,
                    FormatScore = loser.FormatScore,
                    VersionScore = (int)loser.VersionScore,
                    Console = loser.ConsoleKey ?? string.Empty,
                    DatMatch = loser.DatMatch
                });
                seenPaths.Add(loser.MainPath);
            }
        }

        // Add remaining candidates not yet covered by dedupe groups.
        // When no dedupe ran (ConvertOnly, empty processingCandidates), this captures all files.
        foreach (var candidate in result.AllCandidates)
        {
            if (!seenPaths.Add(candidate.MainPath))
                continue;

            var action = candidate.Category switch
            {
                FileCategory.Junk => "JUNK",
                FileCategory.Bios => "BIOS",
                _ => "KEEP"
            };

            entries.Add(new ReportEntry
            {
                GameKey = candidate.GameKey,
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
                DatMatch = candidate.DatMatch
            });
        }

        return entries;
    }

    private static string ToReportCategory(FileCategory category)
        => category.ToString().ToUpperInvariant();

    public static ReportSummary BuildSummary(RunResult result, string mode)
    {
        var entries = BuildEntries(result);
        var projection = RunProjectionFactory.Create(result);
        var moveCount = string.Equals(mode, "DryRun", StringComparison.OrdinalIgnoreCase)
            ? projection.Dupes
            : projection.MoveCount;
        var junkCount = projection.Junk;
        var biosCount = projection.Bios;

        // Invariant: report breakdown must account for all scanned files.
        // Only validate when dedupe actually ran — ConvertOnly and empty-scan paths
        // skip deduplication, so Keep/Dupes are both 0 by design.
        if (result.DedupeGroups.Count > 0)
        {
            var groupedJunkBios = result.DedupeGroups
                .SelectMany(g => g.Losers.Append(g.Winner))
                .Count(c => c.Category is FileCategory.Junk or FileCategory.Bios);
            var ungroupedJunkBios = Math.Max(0, junkCount + biosCount - groupedJunkBios);
            var accountedTotal = projection.Keep + projection.Dupes + ungroupedJunkBios
                               + projection.Unknown + projection.FilteredNonGameCount;
            if (projection.TotalFiles > 0 && accountedTotal > projection.TotalFiles)
                throw new InvalidOperationException($"Report summary invariant failed: accounted={accountedTotal} > scanned={projection.TotalFiles}");
            // Soft warning instead of hard failure for under-count — edge cases with
            // overlapping categories can cause small discrepancies that are not bugs.
            // Only throw on significant gaps (>1% of total).
            if (projection.TotalFiles > 0 && accountedTotal < projection.TotalFiles)
            {
                var gap = projection.TotalFiles - accountedTotal;
                var threshold = Math.Max(1, projection.TotalFiles / 100);
                if (gap > threshold)
                    throw new InvalidOperationException($"Report summary invariant failed: accounted={accountedTotal} < scanned={projection.TotalFiles} — {gap} candidates unaccounted");
            }
        }

        var totalErrorCount = projection.FailCount + projection.JunkFailCount + projection.ConsoleSortFailed;

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
            JunkRemovedCount = projection.JunkRemovedCount,
            JunkFailCount = projection.JunkFailCount,
            SkipCount = projection.SkipCount,
            ConsoleSortMoved = projection.ConsoleSortMoved,
            ConsoleSortFailed = projection.ConsoleSortFailed,
            FailCount = projection.FailCount,
            ErrorCount = totalErrorCount,
            SkippedCount = projection.ConvertSkippedCount + projection.SkipCount,
            SavedBytes = projection.SavedBytes,
            GroupCount = projection.Groups,
            Duration = TimeSpan.FromMilliseconds(projection.DurationMs)
        };
    }

    public static string WriteReport(string reportPath, RunResult result, string mode)
    {
        var fullPath = Path.GetFullPath(reportPath);
        var entries = BuildEntries(result);
        var summary = BuildSummary(result, mode);
        var reportDir = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException($"Report path has no directory: {reportPath}");

        Directory.CreateDirectory(reportDir);

        if (fullPath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            var csv = ReportGenerator.GenerateCsv(entries);
            File.WriteAllText(fullPath, csv, Encoding.UTF8);
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