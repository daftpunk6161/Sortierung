using Romulus.Infrastructure.Metrics;
using Xunit;

namespace Romulus.Tests;

public class PhaseMetricsCollectorTests
{
    [Fact]
    public void Initialize_SetsRunId()
    {
        var collector = new PhaseMetricsCollector();
        collector.Initialize();
        var metrics = collector.GetMetrics();
        Assert.NotEmpty(metrics.RunId);
        Assert.Equal(16, metrics.RunId.Length);
    }

    [Fact]
    public void StartAndComplete_RecordsPhase()
    {
        var collector = new PhaseMetricsCollector();
        collector.Initialize();
        collector.StartPhase("scan");
        collector.CompletePhase(100);
        var metrics = collector.GetMetrics();
        Assert.Single(metrics.Phases);
        Assert.Equal("scan", metrics.Phases[0].Phase);
        Assert.Equal(100, metrics.Phases[0].ItemCount);
        Assert.Equal("Completed", metrics.Phases[0].Status);
    }

    [Fact]
    public void MultiplePhases_AllRecorded()
    {
        var collector = new PhaseMetricsCollector();
        collector.Initialize();
        collector.StartPhase("scan");
        collector.CompletePhase(50);
        collector.StartPhase("dedupe");
        collector.CompletePhase(10);
        collector.StartPhase("move");
        collector.CompletePhase(5);
        var metrics = collector.GetMetrics();
        Assert.Equal(3, metrics.Phases.Count);
    }

    [Fact]
    public void StartPhase_AutoCompletesPrevious()
    {
        var collector = new PhaseMetricsCollector();
        collector.Initialize();
        collector.StartPhase("phase1");
        collector.StartPhase("phase2"); // auto-completes phase1
        collector.CompletePhase();
        var metrics = collector.GetMetrics();
        Assert.Equal(2, metrics.Phases.Count);
        Assert.Equal("Completed", metrics.Phases[0].Status);
    }

    [Fact]
    public void GetMetrics_CalculatesPercentOfTotal()
    {
        var collector = new PhaseMetricsCollector();
        collector.Initialize();
        collector.StartPhase("only");
        System.Threading.Thread.Sleep(10); // ensure non-zero duration
        collector.CompletePhase(1);
        var metrics = collector.GetMetrics();
        Assert.Equal(100.0, metrics.Phases[0].PercentOfTotal);
    }

    [Fact]
    public void CompletePhase_NoActive_DoesNothing()
    {
        var collector = new PhaseMetricsCollector();
        collector.Initialize();
        collector.CompletePhase(); // no active phase
        var metrics = collector.GetMetrics();
        Assert.Empty(metrics.Phases);
    }

    [Fact]
    public void Export_WritesFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"metrics_{Guid.NewGuid():N}");
        try
        {
            var collector = new PhaseMetricsCollector();
            collector.Initialize();
            collector.StartPhase("test");
            collector.CompletePhase(42);
            collector.Export(tempDir);

            var files = Directory.GetFiles(tempDir, "phase-metrics-*.json");
            Assert.True(files.Length >= 2); // timestamped + latest
            Assert.True(File.Exists(Path.Combine(tempDir, "phase-metrics-latest.json")));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GetMetrics_TotalDuration()
    {
        var collector = new PhaseMetricsCollector();
        collector.Initialize();
        collector.StartPhase("a");
        collector.CompletePhase();
        collector.StartPhase("b");
        collector.CompletePhase();
        var metrics = collector.GetMetrics();
        Assert.True(metrics.TotalDuration.TotalMilliseconds >= 0);
    }
}
