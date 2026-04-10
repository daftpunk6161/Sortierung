using Romulus.Contracts.Models;
using Romulus.Infrastructure.Reporting;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// TDD RED (Issue9/A-12): DAT audit counters must reach report summary/output.
/// </summary>
public sealed class DatAuditReportSummaryIssue9RedTests
{
    [Fact]
    public void BuildSummary_ShouldCarryDatAuditCounters_Issue9()
    {
        var result = new RunResult
        {
            DatHaveCount = 5,
            DatHaveWrongNameCount = 4,
            DatMissCount = 3,
            DatUnknownCount = 2,
            DatAmbiguousCount = 1
        };

        var summary = RunReportWriter.BuildSummary(result, "DryRun");

        Assert.Equal(5, summary.DatHaveCount);
        Assert.Equal(4, summary.DatHaveWrongNameCount);
        Assert.Equal(3, summary.DatMissCount);
        Assert.Equal(2, summary.DatUnknownCount);
        Assert.Equal(1, summary.DatAmbiguousCount);
    }

    [Fact]
    public void GenerateHtml_ShouldRenderDatAuditCards_WhenCountersPresent_Issue9()
    {
        var summary = new ReportSummary
        {
            DatHaveCount = 2,
            DatHaveWrongNameCount = 1,
            DatMissCount = 1,
            DatUnknownCount = 1,
            DatAmbiguousCount = 1
        };

        var html = ReportGenerator.GenerateHtml(summary, Array.Empty<ReportEntry>());

        Assert.Contains("DAT Have", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DAT WrongName", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DAT Miss", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DAT Unknown", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DAT Ambiguous", html, StringComparison.OrdinalIgnoreCase);
    }
}
