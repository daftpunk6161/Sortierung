using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Romulus.Infrastructure;
using Xunit;

namespace Romulus.Tests;

public sealed class Phase6CoreIoAndI18nRedTests
{
    [Fact]
    public void Contracts_MustExpose_CoreIoInterfaces()
    {
        var contractsAssembly = typeof(Romulus.Contracts.RunConstants).Assembly;

        Assert.NotNull(contractsAssembly.GetType("Romulus.Contracts.Ports.ISetParserIo", throwOnError: false));
        Assert.NotNull(contractsAssembly.GetType("Romulus.Contracts.Ports.IClassificationIo", throwOnError: false));
    }

    [Fact]
    public void SharedServiceRegistration_MustRegister_CoreIoPorts()
    {
        var services = new ServiceCollection();
        services.AddRomulusCore();
        using var provider = services.BuildServiceProvider();

        var setParserIoType = Type.GetType("Romulus.Contracts.Ports.ISetParserIo, Romulus.Contracts", throwOnError: true)!;
        var classificationIoType = Type.GetType("Romulus.Contracts.Ports.IClassificationIo, Romulus.Contracts", throwOnError: true)!;

        Assert.NotNull(provider.GetService(setParserIoType));
        Assert.NotNull(provider.GetService(classificationIoType));
    }

    [Theory]
    [InlineData("src/Romulus.UI.Wpf/Services/FeatureCommandService.Security.cs")]
    [InlineData("src/Romulus.UI.Wpf/Services/FeatureService.Export.cs")]
    [InlineData("src/Romulus.UI.Wpf/Services/FeatureCommandService.Data.cs")]
    [InlineData("src/Romulus.UI.Wpf/Services/FeatureCommandService.Workflow.cs")]
    public void WpfFeatureFiles_MustNotContainTrackedGermanLiterals(string relativePath)
    {
        var repoRoot = ResolveRepoRoot();
        var filePath = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var content = File.ReadAllText(filePath);

        Assert.DoesNotContain("Hardlink-Modus", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Verfügbar", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Speicherplatz", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Integritäts", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Benutzerdefinierte", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Erstelle Regeln", content, StringComparison.Ordinal);
    }

    private static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "src", "Romulus.sln")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root could not be resolved from test base directory.");
    }
}
