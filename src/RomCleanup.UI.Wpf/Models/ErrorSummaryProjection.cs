using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.Orchestration;

namespace RomCleanup.UI.Wpf.Models;

/// <summary>
/// Builds the protocol/error summary from run result, candidates, and run logs.
/// Centralized projection to avoid divergent ViewModel implementations.
/// </summary>
public static class ErrorSummaryProjection
{
    public static IReadOnlyList<UiError> Build(
        RunResult? result,
        IReadOnlyList<RomCandidate> candidates,
        IEnumerable<LogEntry> runLogs)
    {
        var issues = new List<UiError>();

        foreach (var e in runLogs)
        {
            if (e.Level is "WARN")
                issues.Add(new UiError("RUN-WARN", e.Text, UiErrorSeverity.Warning));
            else if (e.Level is "ERROR")
                issues.Add(new UiError("RUN-ERR", e.Text, UiErrorSeverity.Error));
        }

        if (result is not null)
        {
            if (result.Status == "blocked")
                issues.Insert(0, new UiError("RUN-BLOCKED", $"Preflight: {result.Preflight?.Reason}", UiErrorSeverity.Blocked));

            if (result.MoveResult is { FailCount: > 0 } mv)
                issues.Insert(0, new UiError("IO-MOVE", $"{mv.FailCount} Dateien konnten nicht verschoben werden", UiErrorSeverity.Error));

            if (result.ConvertErrorCount > 0)
                issues.Insert(0, new UiError("CONVERT-ERR", $"{result.ConvertErrorCount} Dateien konnten nicht konvertiert werden", UiErrorSeverity.Error));

            var junk = candidates.Count(c => c.Category == FileCategory.Junk);
            if (junk > 0)
                issues.Insert(0, new UiError("RUN-JUNK", $"{junk} Junk-Dateien erkannt", UiErrorSeverity.Warning));

            var unverified = candidates.Count(c => !c.DatMatch);
            if (unverified > 0 && candidates.Count > 0)
                issues.Insert(0, new UiError("DAT-UNVERIFIED", $"{unverified}/{candidates.Count} Dateien ohne DAT-Verifizierung", UiErrorSeverity.Info));
        }

        if (issues.Count == 0)
        {
            var empty = new List<UiError>
            {
                new("RUN-OK", "Keine Fehler oder Warnungen.", UiErrorSeverity.Info)
            };
            if (result is not null)
                empty.Add(new UiError("RUN-STATS", $"Report geladen: {result.WinnerCount} Winner, {result.LoserCount} Dupes", UiErrorSeverity.Info));
            return empty;
        }

        if (issues.Count <= 50)
            return issues;

        var limited = issues.Take(50).ToList();
        limited.Add(new UiError("RUN-TRUNC", $"… und {issues.Count - 50} weitere", UiErrorSeverity.Warning));
        return limited;
    }
}
