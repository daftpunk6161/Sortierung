using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.Orchestration;
using RomCleanup.Infrastructure.Reporting;

namespace RomCleanup.UI.Wpf.Services;

/// <summary>GUI-040: DryRun comparison, scheduling, pipeline, multi-instance sync.</summary>
public interface IWorkflowService
{
    DryRunCompareResult CompareDryRuns(IReadOnlyList<ReportEntry> a, IReadOnlyList<ReportEntry> b);
    Dictionary<string, string> GetSortTemplates();
    bool TestCronMatch(string cronExpression, DateTime dt);
    string? BuildCsvDiff(string fileA, string fileB, string title);
    string BuildPipelineReport(RunResult? result, IReadOnlyList<RomCandidate> candidates);
    string BuildMultiInstanceReport(IReadOnlyList<string> roots, bool isBusy);
    int RemoveLockFiles(IReadOnlyList<string> roots);
    bool HasLockFiles(IReadOnlyList<string> roots);
}
