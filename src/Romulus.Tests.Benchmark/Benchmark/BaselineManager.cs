using Romulus.Tests.Benchmark.Infrastructure;

namespace Romulus.Tests.Benchmark;

internal static class BaselineManager
{
    public static void UpdateBaseline(BenchmarkReport report, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException("Baseline update requires a non-empty reason (ROMULUS_BASELINE_REASON).");
        }

        var baselinesDir = BenchmarkPaths.BaselinesDir;
        var archiveDir = Path.Combine(baselinesDir, "archive");
        Directory.CreateDirectory(baselinesDir);
        Directory.CreateDirectory(archiveDir);

        if (File.Exists(BenchmarkPaths.LatestBaselinePath))
        {
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var archivePath = Path.Combine(archiveDir, $"baseline-{stamp}.json");
            File.Copy(BenchmarkPaths.LatestBaselinePath, archivePath, overwrite: false);

            var reasonPath = Path.ChangeExtension(archivePath, ".reason.txt");
            File.WriteAllText(reasonPath, reason);
        }

        BenchmarkReportWriter.Write(report, BenchmarkPaths.BaselineMetricsPath);
        BenchmarkReportWriter.Write(report, BenchmarkPaths.LatestBaselinePath);
    }
}
