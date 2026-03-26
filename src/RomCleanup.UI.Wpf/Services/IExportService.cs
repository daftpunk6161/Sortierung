using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.Orchestration;
using RomCleanup.Infrastructure.Reporting;

namespace RomCleanup.UI.Wpf.Services;

/// <summary>GUI-037: CSV, Excel, PDF export and junk/rule reporting.</summary>
public interface IExportService
{
    string BuildJunkReport(IReadOnlyList<RomCandidate> candidates, bool aggressive);
    JunkReportEntry? GetJunkReason(string baseName, bool aggressive);
    string ExportCollectionCsv(IReadOnlyList<RomCandidate> candidates, char delimiter = ';');
    string ExportExcelXml(IReadOnlyList<RomCandidate> candidates);
    string FormatRulesFromJson(string rulesPath, IReadOnlyList<RomCandidate>? candidates = null);
    string BuildRuleEngineReport();
    (ReportSummary Summary, List<ReportEntry> Entries) BuildHtmlReportData(
        IReadOnlyList<RomCandidate> candidates,
        IReadOnlyList<DedupeGroup> groups,
        RunResult? runResult, bool dryRun);
}
