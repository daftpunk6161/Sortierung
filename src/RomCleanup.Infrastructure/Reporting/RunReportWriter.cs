using System.Text;
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
                Category = group.Winner.Category,
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
                    Action = loser.Category == "JUNK" ? "JUNK" : "MOVE",
                    Category = loser.Category,
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

        foreach (var candidate in result.AllCandidates.Where(c => c.Category is "JUNK" or "BIOS"))
        {
            if (!seenPaths.Add(candidate.MainPath))
                continue;

            entries.Add(new ReportEntry
            {
                GameKey = candidate.GameKey,
                Action = candidate.Category,
                Category = candidate.Category,
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

    public static ReportSummary BuildSummary(RunResult result, string mode)
    {
        var entries = BuildEntries(result);

        return new ReportSummary
        {
            Mode = mode,
            Timestamp = DateTime.UtcNow,
            TotalFiles = result.TotalFilesScanned,
            KeepCount = entries.Count(e => e.Action == "KEEP"),
            MoveCount = entries.Count(e => e.Action == "MOVE"),
            JunkCount = entries.Count(e => e.Action == "JUNK"),
            BiosCount = entries.Count(e => e.Category == "BIOS"),
            DatMatches = entries.Count(e => e.DatMatch),
            ConvertedCount = result.ConvertedCount,
            ErrorCount = (result.MoveResult?.FailCount ?? 0) + result.ConvertErrorCount,
            SkippedCount = result.ConvertSkippedCount,
            SavedBytes = result.MoveResult?.SavedBytes ?? 0,
            GroupCount = result.GroupCount,
            Duration = TimeSpan.FromMilliseconds(result.DurationMs)
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