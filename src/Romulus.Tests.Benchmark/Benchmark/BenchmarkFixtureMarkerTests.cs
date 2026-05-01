using Romulus.Tests.Benchmark;
using Xunit;

namespace Romulus.Tests;

public sealed class BenchmarkFixtureMarkerTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"benchmark-marker-{Guid.NewGuid():N}");

    public BenchmarkFixtureMarkerTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void BuildGenerationMarker_ChangesWhenJsonlContentChanges()
    {
        var a = Write("a.jsonl", "{\"id\":\"a\"}");
        var b = Write("b.jsonl", "{\"id\":\"b\"}");

        var first = BenchmarkFixture.BuildGenerationMarker(2, [a, b]);
        File.AppendAllText(a, "\n{\"id\":\"a2\"}");
        var second = BenchmarkFixture.BuildGenerationMarker(2, [a, b]);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void BuildGenerationMarker_IsStableAcrossPathOrder()
    {
        var a = Write("a.jsonl", "{\"id\":\"a\"}");
        var b = Write("b.jsonl", "{\"id\":\"b\"}");

        var first = BenchmarkFixture.BuildGenerationMarker(2, [a, b]);
        var second = BenchmarkFixture.BuildGenerationMarker(2, [b, a]);

        Assert.Equal(first, second);
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }
}
