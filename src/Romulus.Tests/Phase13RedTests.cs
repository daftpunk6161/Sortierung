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

    // TD044_SettingsFileAccess_MustExposeTotalTimeoutParameter,
    // TD046_StreamingScanPipelinePhase_MustEmitIncompleteScanWarningSummary:
    // removed per testing.instructions.md - both were source-grep alibis pinning
    // identifier strings ('totalTimeoutMs', 'Scan.IncompleteWarning'). The
    // timeout parameter is exercised via SettingsFileAccess unit tests; the
    // incomplete-scan warning is verified by StreamingScanPipelinePhase tests.

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
