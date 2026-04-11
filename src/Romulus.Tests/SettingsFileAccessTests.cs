using System.Diagnostics;
using Romulus.Infrastructure.Configuration;
using Xunit;

namespace Romulus.Tests;

public sealed class SettingsFileAccessTests : IDisposable
{
    private readonly string _tempDir;

    public SettingsFileAccessTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_SettingsFileAccess_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task TryReadAllTextAsync_FileLocked_RespectsTotalTimeoutAndReturnsNull()
    {
        var path = Path.Combine(_tempDir, "settings.json");
        File.WriteAllText(path, "{\"mode\":\"DryRun\"}");

        using var lockStream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var sw = Stopwatch.StartNew();
        var content = await SettingsFileAccess.TryReadAllTextAsync(path, maxAttempts: 100, totalTimeoutMs: 120);
        sw.Stop();

        Assert.Null(content);
        Assert.True(sw.ElapsedMilliseconds < 1500, $"Expected timeout-bounded read, but took {sw.ElapsedMilliseconds}ms.");
    }

    [Fact]
    public async Task TryReadAllTextAsync_FileAvailable_ReturnsContent()
    {
        var path = Path.Combine(_tempDir, "settings.json");
        const string expected = "{\"mode\":\"Move\"}";
        File.WriteAllText(path, expected);

        var content = await SettingsFileAccess.TryReadAllTextAsync(path, maxAttempts: 3, totalTimeoutMs: 500);

        Assert.Equal(expected, content);
    }
}
