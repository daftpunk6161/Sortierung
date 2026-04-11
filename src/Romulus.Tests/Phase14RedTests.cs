using Xunit;

namespace Romulus.Tests;

public sealed class Phase14RedTests
{
    [Fact]
    public void TD035_DeduplicationEngine_MustNotUsePipePipeGroupSeparator()
    {
        var sourcePath = ResolveRepoFile("Romulus.Core", "Deduplication", "DeduplicationEngine.cs");
        Assert.True(File.Exists(sourcePath), $"Missing source file: {sourcePath}");

        var source = File.ReadAllText(sourcePath);
        Assert.DoesNotContain("}||{", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TD036_MainViewModel_MustUnsubscribePropertyChangedBeforeCollectionClear()
    {
        var sourcePath = ResolveRepoFile("Romulus.UI.Wpf", "ViewModels", "MainViewModel.cs");
        var runPipelinePath = ResolveRepoFile("Romulus.UI.Wpf", "ViewModels", "MainViewModel.RunPipeline.cs");

        Assert.True(File.Exists(sourcePath), $"Missing source file: {sourcePath}");
        Assert.True(File.Exists(runPipelinePath), $"Missing source file: {runPipelinePath}");

        var source = File.ReadAllText(sourcePath) + "\n" + File.ReadAllText(runPipelinePath);

        Assert.Contains("-= OnExtensionCheckedChanged", source, StringComparison.Ordinal);
        Assert.Contains("-= OnConsoleCheckedChanged", source, StringComparison.Ordinal);
        Assert.Contains("-= OnExtensionFilterChanged", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TD041_MainViewModel_MustNotInstantiateChildViewModelsDirectly()
    {
        var sourcePath = ResolveRepoFile("Romulus.UI.Wpf", "ViewModels", "MainViewModel.cs");
        var appPath = ResolveRepoFile("Romulus.UI.Wpf", "App.xaml.cs");

        Assert.True(File.Exists(sourcePath), $"Missing source file: {sourcePath}");
        Assert.True(File.Exists(appPath), $"Missing source file: {appPath}");

        var source = File.ReadAllText(sourcePath);
        var appSource = File.ReadAllText(appPath);

        Assert.DoesNotContain("new ShellViewModel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new SetupViewModel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new ToolsViewModel", source, StringComparison.Ordinal);

        Assert.Contains("AddSingleton<ShellViewModel>", appSource, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<SetupViewModel>", appSource, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<ToolsViewModel>", appSource, StringComparison.Ordinal);
    }

    [Fact]
    public void TD042_MovePipelinePhase_MustNotDowngradeRollbackFailureToWarning()
    {
        var sourcePath = ResolveRepoFile("Romulus.Infrastructure", "Orchestration", "MovePipelinePhase.cs");
        Assert.True(File.Exists(sourcePath), $"Missing source file: {sourcePath}");

        var source = File.ReadAllText(sourcePath);
        Assert.DoesNotContain("WARNING: Set-member rollback incomplete", source, StringComparison.Ordinal);
    }

    private static string ResolveRepoFile(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Romulus.sln")))
            {
                if (Directory.Exists(Path.Combine(current.FullName, "Romulus.Core"))
                    && Directory.Exists(Path.Combine(current.FullName, "Romulus.Tests")))
                {
                    return Path.Combine([current.FullName, .. segments]);
                }

                return Path.Combine([current.FullName, "src", .. segments]);
            }

            if (Directory.Exists(Path.Combine(current.FullName, "Romulus.Tests"))
                && Directory.Exists(Path.Combine(current.FullName, "Romulus.Core")))
            {
                return Path.Combine([current.FullName, .. segments]);
            }

            current = current.Parent;
        }

        return Path.Combine(segments);
    }
}
