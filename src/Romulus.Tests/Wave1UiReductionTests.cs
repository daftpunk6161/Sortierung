using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Wave 1 — T-W1-UI-REDUCTION pin tests.
/// Acceptance gates from docs/plan/strategic-reduction-2026/plan.yaml (planning_pass 3):
///   * Top-level Views/ <= 21 XAML files (Workflow-Surfaces incl. W4/W5 surfaces).
///   * Recursive Views/ <= 30 XAML files (Top-Level + Controls/ + Dialogs/).
///   * Subfolders unter Views/ duerfen nur Controls/ und Dialogs/ heissen.
///   * src/Romulus.UI.Wpf/Services/FeatureCommandService*.cs has <= 4 partials.
///   * SmartActionBar wurde entfernt (CommandPalette bleibt als Power-Tool).
///   * "6 sichtbare Tool-Karten" = QuickAccess-Cap + DefaultPinnedKeys <= 6
///     (gemessen ueber ToolsViewModel-Quellcode: Take(6) + DefaultPinnedKeys-Block).
///   * Keine Frontend-Export/ScreenScraper/RetroAchievement/Plugin-Marketplace/MameSet-
///     Builder/RomPatcher-Reste in src/ ausserhalb der Romulus.Tests-Assembly.
/// </summary>
public sealed class Wave1UiReductionTests
{
    private static readonly string[] AllowedViewSubfolders = ["Controls", "Dialogs"];

    private static readonly string[] CullForbiddenSymbols =
    [
        "FrontendExport",
        "ScreenScraper",
        "RetroAchievement",
        "PluginRegistry",
        "PluginManager",
        "PluginMarketplace",
        "MameSetBuilder",
        "RomPatcher",
        "BpsPatcher",
        "IpsPatcher",
        "UpsPatcher"
    ];

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
    public void ViewsFolder_TopLevel_HasAtMost21XamlFiles()
    {
        var viewsDir = Path.Combine(RepoRoot().FullName, "src", "Romulus.UI.Wpf", "Views");
        Assert.True(Directory.Exists(viewsDir), $"Views directory missing: {viewsDir}");

        var xamlFiles = Directory.GetFiles(viewsDir, "*.xaml", SearchOption.TopDirectoryOnly);

        // Pass 3 (Wave 4/5): drei produktive Workflow-Surfaces sind nach W4-AUDIT-VIEWER-UI,
        // W4-REVIEW-INBOX und W5-BEFORE-AFTER-SIMULATOR legitim hinzugekommen. Acceptance bleibt
        // "Reduktion gehalten" — Limit auf 21 angepasst, weitere Erweiterung nur mit Begruendung.
        Assert.True(
            xamlFiles.Length <= 21,
            $"Expected <= 21 top-level view XAML files (T-W1-UI-REDUCTION acceptance, planning_pass 3 nach W4/W5-Surfaces), "
            + $"found {xamlFiles.Length}: " + string.Join(", ", xamlFiles.Select(Path.GetFileName)));
    }

    [Fact]
    public void ViewsFolder_Recursive_HasAtMost30XamlFiles()
    {
        // Acceptance-Klarstellung (planning_pass 2): ehrliche Obergrenze inkl. Subfolders.
        // Verhindert dass "Reduktion" durch beliebige Sub-Verschachtelung umgangen wird.
        var viewsDir = Path.Combine(RepoRoot().FullName, "src", "Romulus.UI.Wpf", "Views");
        var xamlFiles = Directory.GetFiles(viewsDir, "*.xaml", SearchOption.AllDirectories);

        Assert.True(
            xamlFiles.Length <= 30,
            $"Expected <= 30 recursive view XAML files, found {xamlFiles.Length}: "
            + string.Join(", ", xamlFiles.Select(p => Path.GetRelativePath(viewsDir, p))));
    }

    [Fact]
    public void ViewsFolder_Subfolders_OnlyContainsControlsAndDialogs()
    {
        var viewsDir = Path.Combine(RepoRoot().FullName, "src", "Romulus.UI.Wpf", "Views");
        var subfolders = Directory.GetDirectories(viewsDir).Select(Path.GetFileName).ToArray();

        var unexpected = subfolders
            .Where(name => !AllowedViewSubfolders.Contains(name, System.StringComparer.Ordinal))
            .ToArray();

        Assert.True(
            unexpected.Length == 0,
            $"Views/ darf nur Controls/ und Dialogs/ als Subfolder haben (T-W1-UI-REDUCTION planning_pass 2). "
            + $"Unerlaubte Ordner: {string.Join(", ", unexpected!)}");
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

    [Fact]
    public void ToolsViewModel_DefaultPinnedKeys_AtMost6()
    {
        // Acceptance "Tool-Karten sichtbar = 6" (planning_pass 2): die Default-Pin-Liste,
        // die beim Start prominent gezeigt wird, darf maximal 6 Eintraege haben.
        var path = Path.Combine(RepoRoot().FullName,
            "src", "Romulus.UI.Wpf", "ViewModels", "ToolsViewModel.cs");
        var src = File.ReadAllText(path);

        var match = Regex.Match(
            src,
            @"DefaultPinnedKeys\s*=\s*\[(?<body>[^\]]*)\]",
            RegexOptions.Singleline);
        Assert.True(match.Success, "DefaultPinnedKeys-Block in ToolsViewModel.cs nicht gefunden.");

        var entries = match.Groups["body"].Value
            .Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries)
            .Where(line => line.Contains("FeatureCommandKeys.", System.StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            entries.Length <= 6,
            $"DefaultPinnedKeys darf max 6 Eintraege haben (Tool-Karten=6 Acceptance), found {entries.Length}: "
            + string.Join(", ", entries));
    }

    [Fact]
    public void ToolsViewModel_QuickAccess_RecommendedAndRecent_CappedAt6()
    {
        // Acceptance "Tool-Karten sichtbar = 6" (planning_pass 2): die drei prominent
        // sichtbaren Tool-Surfaces (QuickAccess, Recommended, Recent) sind explizit
        // auf Take(6) bzw. >= 6 Pin-Capacity gedeckelt. Pin-Test sichert, dass diese
        // Caps nicht stillschweigend hochgesetzt werden.
        var path = Path.Combine(RepoRoot().FullName,
            "src", "Romulus.UI.Wpf", "ViewModels", "ToolsViewModel.cs");
        var src = File.ReadAllText(path);

        // RebuildQuickAccess + Take(6)
        Assert.Contains(".Where(static t => t.IsPinned).Take(6)", src, System.StringComparison.Ordinal);
        // ToggleToolPin: QuickAccessItems.Count >= 6
        Assert.Contains("QuickAccessItems.Count >= 6", src, System.StringComparison.Ordinal);
        // RecordToolUsage: RecentToolItems Take(6)
        Assert.Contains(".OrderByDescending(static t => t.LastUsedAt)", src, System.StringComparison.Ordinal);
        // RebuildRecommendedItems: Take(6) finale Auswahl
        var recommendedTake = Regex.Matches(src, @"recommendations\.Take\(6\)|\.Take\(6\)").Count;
        Assert.True(recommendedTake >= 2,
            $"Erwartet >= 2 Take(6)-Caps in ToolsViewModel.cs (Recent + Recommended), gefunden: {recommendedTake}.");
    }

    [Fact]
    public void WpfSourceTree_NoResidualCullFeatureNames()
    {
        // Acceptance "Keine grep-Treffer fuer entfernte Feature-Namen in src/" (T-W1-UI-REDUCTION).
        // Scan src/ ohne Romulus.Tests (Test-Files duerfen die Cull-Symbole als
        // Negativ-Pin enthalten, z.B. Wave1Removed*Tests.cs).
        var srcRoot = Path.Combine(RepoRoot().FullName, "src");
        var offenders = new List<string>();

        foreach (var ext in new[] { "*.cs", "*.xaml" })
        {
            foreach (var file in Directory.EnumerateFiles(srcRoot, ext, SearchOption.AllDirectories))
            {
                // Test-Assembly ist explizit ausgenommen (siehe Pin-Tests Wave1Removed*).
                if (file.Contains($"{Path.DirectorySeparatorChar}Romulus.Tests{Path.DirectorySeparatorChar}",
                        System.StringComparison.Ordinal))
                    continue;

                var content = File.ReadAllText(file);
                foreach (var symbol in CullForbiddenSymbols)
                {
                    if (content.Contains(symbol, System.StringComparison.Ordinal))
                        offenders.Add($"{Path.GetRelativePath(srcRoot, file)} :: {symbol}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Cull-Liste-Reste im Produktivcode (T-W1-UI-REDUCTION acceptance):\n  "
            + string.Join("\n  ", offenders));
    }
}
