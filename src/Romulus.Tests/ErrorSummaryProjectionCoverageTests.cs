using System.Collections.ObjectModel;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Orchestration;
using Romulus.UI.Wpf.Models;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Tests for ErrorSummaryProjection.Build — pure static projection.
/// </summary>
public sealed class ErrorSummaryProjectionCoverageTests
{
    #region No-result, no-logs → OK

    [Fact]
    public void Build_NoResultNoLogs_ReturnsOk()
    {
        var issues = ErrorSummaryProjection.Build(null, [], []);
        Assert.Single(issues);
        Assert.Equal("RUN-OK", issues[0].Code);
    }

    #endregion

    #region Log-level routing

    [Fact]
    public void Build_WarnLogs_RouteToWarning()
    {
        var logs = new[] { new LogEntry("some warning", "WARN") };
        var items = ErrorSummaryProjection.Build(null, [], logs);
        Assert.Contains(items, e => e.Code == "RUN-WARN" && e.Severity == UiErrorSeverity.Warning);
    }

    [Fact]
    public void Build_ErrorLogs_RouteToError()
    {
        var logs = new[] { new LogEntry("fatal", "ERROR") };
        var items = ErrorSummaryProjection.Build(null, [], logs);
        Assert.Contains(items, e => e.Code == "RUN-ERR" && e.Severity == UiErrorSeverity.Error);
    }

    [Fact]
    public void Build_InfoLogs_Ignored()
    {
        var logs = new[] { new LogEntry("info line", "INFO") };
        var items = ErrorSummaryProjection.Build(null, [], logs);
        // No WARN/ERROR → empty issues → OK
        Assert.Equal("RUN-OK", items[0].Code);
    }

    #endregion

    #region Result-based issues

    [Fact]
    public void Build_BlockedResult_InsertsBlockedAtTop()
    {
        var result = new RunResult { Status = "blocked", Preflight = new OperationResult { Status = OperationResult.StatusBlocked, Reason = "no roots" } };
        var items = ErrorSummaryProjection.Build(result, [], []);
        Assert.Equal("RUN-BLOCKED", items[0].Code);
        Assert.Equal(UiErrorSeverity.Blocked, items[0].Severity);
        Assert.Contains("no roots", items[0].Message);
    }

    [Fact]
    public void Build_MoveFailCount_InsertsIoMoveError()
    {
        var result = new RunResult
        {
            Status = "completed",
            MoveResult = new MovePhaseResult(MoveCount: 10, FailCount: 3, SavedBytes: 0)
        };
        var items = ErrorSummaryProjection.Build(result, [], []);
        Assert.Contains(items, e => e.Code == "IO-MOVE" && e.Severity == UiErrorSeverity.Error);
    }

    [Fact]
    public void Build_ConvertError_InsertsConvertError()
    {
        var result = new RunResult { Status = "completed", ConvertErrorCount = 2 };
        var items = ErrorSummaryProjection.Build(result, [], []);
        Assert.Contains(items, e => e.Code == "CONVERT-ERR");
    }

    [Fact]
    public void Build_JunkCandidates_InsertsJunkWarning()
    {
        var candidates = new RomCandidate[]
        {
            new() { MainPath = "a.zip", GameKey = "a", Category = FileCategory.Junk }
        };
        var result = new RunResult { Status = "completed" };
        var items = ErrorSummaryProjection.Build(result, candidates, []);
        Assert.Contains(items, e => e.Code == "RUN-JUNK" && e.Severity == UiErrorSeverity.Warning);
    }

    [Fact]
    public void Build_UnverifiedCandidates_InsertsInfo()
    {
        var candidates = new RomCandidate[]
        {
            new() { MainPath = "x.zip", GameKey = "x", DatMatch = false },
            new() { MainPath = "y.zip", GameKey = "y", DatMatch = true }
        };
        var result = new RunResult { Status = "completed" };
        var items = ErrorSummaryProjection.Build(result, candidates, []);
        Assert.Contains(items, e => e.Code == "DAT-UNVERIFIED" && e.Severity == UiErrorSeverity.Info);
    }

    #endregion

    #region OK with stats

    [Fact]
    public void Build_NoIssuesWithResult_ReturnsOkAndStats()
    {
        var result = new RunResult { Status = "completed", WinnerCount = 42, LoserCount = 5 };
        var items = ErrorSummaryProjection.Build(result, [], []);
        Assert.Equal(2, items.Count);
        Assert.Equal("RUN-OK", items[0].Code);
        Assert.Equal("RUN-STATS", items[1].Code);
        Assert.Contains("42", items[1].Message);
    }

    #endregion

    #region Truncation at 50

    [Fact]
    public void Build_MoreThan50Issues_Truncates()
    {
        var logs = Enumerable.Range(0, 60)
            .Select(i => new LogEntry($"error {i}", "ERROR"))
            .ToArray();
        var items = ErrorSummaryProjection.Build(null, [], logs);
        Assert.Equal(51, items.Count); // 50 + truncation notice
        Assert.Equal("RUN-TRUNC", items[^1].Code);
        Assert.Contains("10 weitere", items[^1].Message);
    }

    #endregion

    #region Priority ordering

    [Fact]
    public void Build_ResultIssues_InsertedBeforeLogs()
    {
        var result = new RunResult { Status = "blocked", Preflight = new OperationResult { Status = OperationResult.StatusBlocked, Reason = "x" } };
        var logs = new[] { new LogEntry("warn1", "WARN") };
        var items = ErrorSummaryProjection.Build(result, [], logs);
        Assert.Equal("RUN-BLOCKED", items[0].Code);
        Assert.Equal("RUN-WARN", items[1].Code);
    }

    #endregion
}
