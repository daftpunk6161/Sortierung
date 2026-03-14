using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.Orchestration;
using RomCleanup.Infrastructure.Reporting;

namespace RomCleanup.UI.Wpf.Services;

/// <summary>GUI-037: Delegates to static FeatureService.Export methods.</summary>
public sealed class ExportService : IExportService
{
    public string BuildJunkReport(IReadOnlyList<RomCandidate> candidates, bool aggressive)
        => FeatureService.BuildJunkReport(candidates, aggressive);

    public JunkReportEntry? GetJunkReason(string baseName, bool aggressive)
        => FeatureService.GetJunkReason(baseName, aggressive);

    public string ExportCollectionCsv(IReadOnlyList<RomCandidate> candidates, char delimiter = ';')
        => FeatureService.ExportCollectionCsv(candidates, delimiter);

    public string ExportExcelXml(IReadOnlyList<RomCandidate> candidates)
        => FeatureService.ExportExcelXml(candidates);

    public string FormatRulesFromJson(string rulesPath, IReadOnlyList<RomCandidate>? candidates = null)
        => FeatureService.FormatRulesFromJson(rulesPath, candidates);

    public string BuildRuleEngineReport()
        => FeatureService.BuildRuleEngineReport();

    public (ReportSummary Summary, List<ReportEntry> Entries) BuildPdfReportData(
        IReadOnlyList<RomCandidate> candidates,
        IReadOnlyList<DedupeResult> groups,
        RunResult? runResult, bool dryRun)
        => FeatureService.BuildPdfReportData(candidates, groups, runResult, dryRun);
}
