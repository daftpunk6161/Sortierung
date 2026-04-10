namespace Romulus.Tests.Benchmark.Infrastructure;

/// <summary>
/// Resolves absolute paths to benchmark data files relative to the repository root.
/// Uses the same repo-root detection pattern as existing test infrastructure.
/// </summary>
internal static class BenchmarkPaths
{
    private static string? _repoRoot;

    public static string RepoRoot => _repoRoot ??= ResolveRepoRoot();

    public static string BenchmarkDir => Path.Combine(RepoRoot, "benchmark");
    public static string GroundTruthDir => Path.Combine(BenchmarkDir, "ground-truth");
    public static string DatsDir => Path.Combine(BenchmarkDir, "dats");
    public static string BaselinesDir => Path.Combine(BenchmarkDir, "baselines");
    public static string ReportsDir => Path.Combine(BenchmarkDir, "reports");
    public static string GatesJsonPath => Path.Combine(BenchmarkDir, "gates.json");
    public static string ManifestJsonPath => Path.Combine(BenchmarkDir, "manifest.json");
    public static string LatestBaselinePath => Path.Combine(BaselinesDir, "latest-baseline.json");
    public static string BaselineMetricsPath => Path.Combine(BaselinesDir, "baseline-metrics.json");
    public static string VersionedBaselinePath => Path.Combine(BaselinesDir, "v0.1.0-baseline.json");
    public static string CurrentBenchmarkReportPath => Path.Combine(ReportsDir, "benchmark-results.json");
    public static string CurrentMetricsSummaryPath => Path.Combine(ReportsDir, "metrics-summary.json");
    public static string CurrentTrendComparisonPath => Path.Combine(ReportsDir, "trend-comparison.json");
    public static string CurrentConfusionConsoleCsvPath => Path.Combine(ReportsDir, "confusion-console.csv");
    public static string CurrentConfusionCategoryCsvPath => Path.Combine(ReportsDir, "confusion-category.csv");
    public static string CurrentErrorDetailsPath => Path.Combine(ReportsDir, "error-details.jsonl");
    public static string CurrentHtmlDashboardPath => Path.Combine(ReportsDir, "benchmark-dashboard.html");
    public static string SchemaPath => Path.Combine(GroundTruthDir, "ground-truth.schema.json");
    public static string HoldoutDir => Path.Combine(BenchmarkDir, "holdout");
    public static string HoldoutJsonlPath => Path.Combine(HoldoutDir, "holdout.jsonl");
    public static string DataDir => Path.Combine(RepoRoot, "data");
    public static string ConsolesJsonPath => Path.Combine(DataDir, "consoles.json");

    public static string[] AllJsonlFiles =>
        Directory.Exists(GroundTruthDir)
            ? Directory.GetFiles(GroundTruthDir, "*.jsonl")
            : [];

    private static string ResolveRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string? callerPath = null)
    {
        // Walk up from AppContext.BaseDirectory (works when running from bin/Debug)
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "Romulus.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        // Fallback: walk up from compile-time source path (works when running from temp dir)
        if (callerPath is not null)
        {
            dir = new DirectoryInfo(Path.GetDirectoryName(callerPath)!);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "src", "Romulus.sln")))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }

        throw new InvalidOperationException(
            "Repository root not found. Ensure tests run from a directory below the repo root.");
    }
}
