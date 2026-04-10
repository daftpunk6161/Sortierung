using Romulus.Tests.Benchmark.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace Romulus.Tests.Benchmark;

[Collection("BenchmarkEvaluation")]
public sealed class BaselineRegressionGateTests : IClassFixture<BenchmarkFixture>
{
    private readonly BenchmarkFixture _fixture;
    private readonly ITestOutputHelper _output;

    public BaselineRegressionGateTests(BenchmarkFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    [Trait("Category", "BenchmarkRegression")]
    public void BenchmarkResults_NoRegressionVsBaseline()
    {
        var setFiles = new[]
        {
            "golden-core.jsonl",
            "edge-cases.jsonl",
            "negative-controls.jsonl",
            "golden-realworld.jsonl",
            "chaos-mixed.jsonl",
            "dat-coverage.jsonl",
            "repair-safety.jsonl",
        };

        var results = new List<BenchmarkSampleResult>();
        foreach (var set in setFiles)
        {
            results.AddRange(BenchmarkEvaluationRunner.EvaluateSet(_fixture, set));
        }

        var aggregate = MetricsAggregator.CalculateExtendedAggregate(results);
        var confusion = MetricsAggregator.BuildConfusionMatrix(results);
        var perSystem = MetricsAggregator.CalculatePerSystem(results);
        var confusionPairs = MetricsAggregator.CalculateConsoleConfusionPairs(results);
        var categoryConfusion = MetricsAggregator.BuildCategoryConfusionMatrix(results);
        var calibration = MetricsAggregator.CalculateConfidenceCalibration(results);
        var report = BenchmarkReportWriter.CreateReport(results, BenchmarkReportWriter.GroundTruthVersion, aggregate, confusion);

        BenchmarkReportWriter.Write(report, BenchmarkPaths.CurrentBenchmarkReportPath);

        var regression = BaselineComparator.Compare(report, BenchmarkPaths.LatestBaselinePath);

        // Write all artifacts including the full HTML dashboard (D1)
        BenchmarkArtifactWriter.WriteMetricsSummary(report, BenchmarkPaths.CurrentMetricsSummaryPath);
        BenchmarkArtifactWriter.WriteConfusionCsv(confusion, BenchmarkPaths.CurrentConfusionConsoleCsvPath);
        BenchmarkArtifactWriter.WriteCategoryConfusionCsv(results, BenchmarkPaths.CurrentConfusionCategoryCsvPath);
        BenchmarkArtifactWriter.WriteErrorDetailsJsonl(results, BenchmarkPaths.CurrentErrorDetailsPath);
        BenchmarkArtifactWriter.WriteTrendComparison(regression, BenchmarkPaths.CurrentTrendComparisonPath);
        BenchmarkHtmlReportWriter.Write(report, BenchmarkPaths.CurrentHtmlDashboardPath,
            perSystem, confusionPairs, categoryConfusion, calibration, regression);

        // TASK-103: TrendAnalyzer integration — write D4 trend report
        var trend = TrendAnalyzer.Analyze(report);
        var trendReportPath = Path.Combine(BenchmarkPaths.ReportsDir, "trend-report.json");
        TrendAnalyzer.WriteTrendReport(trend, trendReportPath);
        _output.WriteLine($"Trend direction: {trend.TrendDirection} (WrongSlope={trend.WrongMatchRateTrend:F6}, UnsafeSlope={trend.UnsafeSortRateTrend:F6})");

        var updateBaseline = string.Equals(
            Environment.GetEnvironmentVariable("ROMULUS_UPDATE_BASELINE"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        if (updateBaseline)
        {
            var reason = Environment.GetEnvironmentVariable("ROMULUS_BASELINE_REASON") ?? string.Empty;
            BaselineManager.UpdateBaseline(report, reason);
            _output.WriteLine($"Baseline metrics updated at {BenchmarkPaths.BaselineMetricsPath}");
        }

        if (!regression.HasBaseline)
        {
            _output.WriteLine($"No baseline found at {BenchmarkPaths.LatestBaselinePath}. Regression gate skipped.");
            return;
        }

        _output.WriteLine($"WrongMatchRateDelta={regression.WrongMatchRateDelta:F6}");
        _output.WriteLine($"UnsafeSortRateDelta={regression.UnsafeSortRateDelta:F6}");
        _output.WriteLine($"UnknownToWrongMigrationRate={regression.UnknownToWrongMigrationRate:F6}");

        Assert.True(regression.WrongMatchRateDelta <= 0.001,
            $"Wrong match rate regression: {regression.WrongMatchRateDelta:P3} > 0.100% threshold");
        Assert.True(regression.UnsafeSortRateDelta <= 0.001,
            $"Unsafe sort rate regression: {regression.UnsafeSortRateDelta:P3} > 0.100% threshold");
        Assert.True(regression.UnknownToWrongMigrationRate <= 0.02,
            $"UNKNOWN->WRONG migration too high: {regression.UnknownToWrongMigrationRate:P3} > 2.000% threshold");
        Assert.True(regression.PerSystemRegressions.Count == 0,
            "Per-system regressions: " + string.Join(", ", regression.PerSystemRegressions));
    }
}
