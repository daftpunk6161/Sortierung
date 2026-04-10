using System.Text.Json;
using Romulus.Infrastructure.Logging;
using Xunit;

namespace Romulus.Tests;

public class JsonlLogWriterTests : IDisposable
{
    private readonly string _tempDir;

    public JsonlLogWriterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"logtest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Write_CreatesFileWithJsonlEntry()
    {
        var logPath = Path.Combine(_tempDir, "test.jsonl");
        using (var writer = new JsonlLogWriter(logPath))
        {
            writer.Info("CLI", "scan", "Started scan", "scan");
        }

        Assert.True(File.Exists(logPath));
        var lines = File.ReadAllLines(logPath);
        Assert.Single(lines);

        var doc = JsonDocument.Parse(lines[0]);
        Assert.Equal("Info", doc.RootElement.GetProperty("level").GetString());
        Assert.Equal("CLI", doc.RootElement.GetProperty("module").GetString());
        Assert.Equal("scan", doc.RootElement.GetProperty("action").GetString());
    }

    [Fact]
    public void Write_RespectsMinLevel()
    {
        var logPath = Path.Combine(_tempDir, "level.jsonl");
        using (var writer = new JsonlLogWriter(logPath, LogLevel.Warning))
        {
            writer.Debug("CLI", "should be skipped");
            writer.Info("CLI", "scan", "also skipped");
            writer.Warning("CLI", "this appears");
        }

        var lines = File.ReadAllLines(logPath);
        Assert.Single(lines);
        Assert.Contains("this appears", lines[0]);
    }

    [Fact]
    public void Write_MultipleEntries_MultipleLines()
    {
        var logPath = Path.Combine(_tempDir, "multi.jsonl");
        using (var writer = new JsonlLogWriter(logPath))
        {
            writer.Info("A", "one", "msg1");
            writer.Info("B", "two", "msg2");
            writer.Error("C", "error msg");
        }

        var lines = File.ReadAllLines(logPath);
        Assert.Equal(3, lines.Length);
    }

    [Fact]
    public void Write_IncludesCorrelationId()
    {
        var logPath = Path.Combine(_tempDir, "corr.jsonl");
        using (var writer = new JsonlLogWriter(logPath, correlationId: "test-corr-123"))
        {
            writer.Info("CLI", "x", "msg");
        }

        var line = File.ReadAllText(logPath).Trim();
        var doc = JsonDocument.Parse(line);
        Assert.Equal("test-corr-123", doc.RootElement.GetProperty("correlationId").GetString());
    }

    [Fact]
    public void Write_ErrorClass_Included()
    {
        var logPath = Path.Combine(_tempDir, "err.jsonl");
        using (var writer = new JsonlLogWriter(logPath))
        {
            writer.Error("CLI", "something bad", "Critical");
        }

        var line = File.ReadAllText(logPath).Trim();
        var doc = JsonDocument.Parse(line);
        Assert.Equal("Critical", doc.RootElement.GetProperty("errorClass").GetString());
    }

    [Fact]
    public void Write_Metrics_Included()
    {
        var logPath = Path.Combine(_tempDir, "metrics.jsonl");
        using (var writer = new JsonlLogWriter(logPath))
        {
            writer.Write(LogLevel.Info, "CLI", "perf", "test",
                metrics: new Dictionary<string, object> { ["elapsed"] = 42.5 });
        }

        var line = File.ReadAllText(logPath).Trim();
        var doc = JsonDocument.Parse(line);
        Assert.True(doc.RootElement.TryGetProperty("metrics", out var metrics));
        Assert.True(metrics.TryGetProperty("elapsed", out _));
    }

    [Fact]
    public void Rotation_NoRotationWhenSmall()
    {
        var logPath = Path.Combine(_tempDir, "small.jsonl");
        File.WriteAllText(logPath, "small content");

        JsonlLogRotation.Rotate(logPath, maxBytes: 1024 * 1024);

        Assert.True(File.Exists(logPath)); // file not rotated
        var archives = Directory.GetFiles(_tempDir, "small-*.jsonl");
        Assert.Empty(archives);
    }

    [Fact]
    public void Rotation_RotatesWhenExceedsMaxBytes()
    {
        var logPath = Path.Combine(_tempDir, "big.jsonl");
        File.WriteAllText(logPath, new string('x', 200));

        JsonlLogRotation.Rotate(logPath, maxBytes: 100);

        Assert.False(File.Exists(logPath)); // original moved
        var archives = Directory.GetFiles(_tempDir, "big-*.jsonl");
        Assert.Single(archives);
    }
}
