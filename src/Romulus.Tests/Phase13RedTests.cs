using Xunit;

namespace Romulus.Tests;

public sealed class Phase13RedTests
{
    [Fact]
    public void TD031_DatSourceService_MustNotUseReadAsByteArrayAsync()
    {
        var sourcePath = ResolveRepoFile("Romulus.Infrastructure", "Dat", "DatSourceService.cs");
        Assert.True(File.Exists(sourcePath), $"Missing source file: {sourcePath}");

        var source = File.ReadAllText(sourcePath);
        Assert.DoesNotContain("ReadAsByteArrayAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TD044_SettingsFileAccess_MustExposeTotalTimeoutParameter()
    {
        var sourcePath = ResolveRepoFile("Romulus.Infrastructure", "Configuration", "SettingsFileAccess.cs");
        Assert.True(File.Exists(sourcePath), $"Missing source file: {sourcePath}");

        var source = File.ReadAllText(sourcePath);
        Assert.Contains("totalTimeoutMs", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TD046_StreamingScanPipelinePhase_MustEmitIncompleteScanWarningSummary()
    {
        var sourcePath = ResolveRepoFile("Romulus.Infrastructure", "Orchestration", "StreamingScanPipelinePhase.cs");
        Assert.True(File.Exists(sourcePath), $"Missing source file: {sourcePath}");

        var source = File.ReadAllText(sourcePath);
        Assert.Contains("Scan incomplete:", source, StringComparison.Ordinal);
    }

    private static string ResolveRepoFile(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Romulus.sln")))
            {
                if (Directory.Exists(Path.Combine(current.FullName, "Romulus.Infrastructure"))
                    && Directory.Exists(Path.Combine(current.FullName, "Romulus.Tests")))
                {
                    return Path.Combine([current.FullName, .. segments]);
                }

                return Path.Combine([current.FullName, "src", .. segments]);
            }

            if (Directory.Exists(Path.Combine(current.FullName, "Romulus.Tests"))
                && Directory.Exists(Path.Combine(current.FullName, "Romulus.Infrastructure")))
            {
                return Path.Combine([current.FullName, .. segments]);
            }

            current = current.Parent;
        }

        return Path.Combine(segments);
    }
}
