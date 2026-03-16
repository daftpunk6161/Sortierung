using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.Orchestration;
using RomCleanup.Infrastructure.Reporting;

namespace RomCleanup.UI.Wpf.Services;

/// <summary>GUI-040: Delegates to static FeatureService.Workflow methods.</summary>
public sealed class WorkflowService : IWorkflowService
{
    public DryRunCompareResult CompareDryRuns(IReadOnlyList<ReportEntry> a, IReadOnlyList<ReportEntry> b)
        => FeatureService.CompareDryRuns(a, b);

    public Dictionary<string, string> GetSortTemplates()
        => FeatureService.GetSortTemplates();

    public bool TestCronMatch(string cronExpression, DateTime dt)
        => FeatureService.TestCronMatch(cronExpression, dt);

    public string? BuildCsvDiff(string fileA, string fileB, string title)
        => FeatureService.BuildCsvDiff(fileA, fileB, title);

    public string BuildPipelineReport(RunResult? result, IReadOnlyList<RomCandidate> candidates)
        => FeatureService.BuildPipelineReport(result, candidates);

    public string BuildMultiInstanceReport(IReadOnlyList<string> roots, bool isBusy)
        => FeatureService.BuildMultiInstanceReport(roots, isBusy);

    public int RemoveLockFiles(IReadOnlyList<string> roots)
        => FeatureService.RemoveLockFiles(roots);

    public bool HasLockFiles(IReadOnlyList<string> roots)
        => FeatureService.HasLockFiles(roots);
}
