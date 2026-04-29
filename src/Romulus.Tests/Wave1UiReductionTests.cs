using System.IO;
using System.Linq;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Wave 1 — T-W1-UI-REDUCTION pin tests.
/// Acceptance gates from docs/plan/strategic-reduction-2026/plan.yaml:
///   * src/Romulus.UI.Wpf/Views contains <= 18 XAML files
///   * src/Romulus.UI.Wpf/Services/FeatureCommandService*.cs has <= 4 partials
///   * SmartActionBar wurde entfernt (CommandPalette bleibt als Power-Tool)
/// </summary>
public sealed class Wave1UiReductionTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null
               && !File.Exists(Path.Combine(dir.FullName, "src", "Romulus.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!;
    }

    [Fact]
    public void ViewsFolder_HasAtMost18XamlFiles()
    {
        var viewsDir = Path.Combine(RepoRoot().FullName, "src", "Romulus.UI.Wpf", "Views");
        Assert.True(Directory.Exists(viewsDir), $"Views directory missing: {viewsDir}");

        var xamlFiles = Directory.GetFiles(viewsDir, "*.xaml", SearchOption.TopDirectoryOnly);

        Assert.True(
            xamlFiles.Length <= 18,
            $"Expected <= 18 view XAML files in Views/ (T-W1-UI-REDUCTION acceptance), found {xamlFiles.Length}: "
            + string.Join(", ", xamlFiles.Select(Path.GetFileName)));
    }

    [Fact]
    public void FeatureCommandService_HasAtMost4Partials()
    {
        var servicesDir = Path.Combine(RepoRoot().FullName, "src", "Romulus.UI.Wpf", "Services");
        Assert.True(Directory.Exists(servicesDir));

        var partials = Directory.GetFiles(servicesDir, "FeatureCommandService*.cs", SearchOption.TopDirectoryOnly);

        Assert.True(
            partials.Length <= 4,
            $"Expected <= 4 FeatureCommandService partials (T-W1-UI-REDUCTION acceptance), found {partials.Length}: "
            + string.Join(", ", partials.Select(Path.GetFileName)));
    }

    [Fact]
    public void SmartActionBar_Removed()
    {
        var viewsDir = Path.Combine(RepoRoot().FullName, "src", "Romulus.UI.Wpf", "Views");
        var smart = Path.Combine(viewsDir, "SmartActionBar.xaml");
        Assert.False(File.Exists(smart),
            "SmartActionBar.xaml should be removed in Wave 1 — CommandPalette is the sanctioned power-user surface.");
    }
}
